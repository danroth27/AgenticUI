// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgenticUI.AgentServer.Scenarios.Reasoning;

/// <summary>
/// Wraps a reasoning model (e.g. DeepSeek-R1) that emits its chain of thought inline as
/// <c>&lt;think&gt;…&lt;/think&gt;</c> in the message text. This client splits that thinking out and
/// re-emits it as <see cref="TextReasoningContent"/>, which AGUI.Server turns into AG-UI
/// <c>REASONING_*</c> events and the Blazor AI components render as a collapsible "thought process".
/// </summary>
/// <remarks>
/// This must sit <em>below</em> the agent, as an <see cref="IChatClient"/> decorator. The agent layer
/// (<c>AsAIAgent</c> in Microsoft.Agents.AI 1.15.0) strips <c>&lt;think&gt;…&lt;/think&gt;</c> out of the
/// response text and discards it, so a <c>DelegatingAIAgent</c> wrapper placed above the agent never
/// sees the thinking and produces no reasoning content.
/// </remarks>
internal sealed class ThinkSplittingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var splitter = new ThinkSplitter();
        ChatResponseUpdate? last = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            last = update;
            var outContents = new List<AIContent>();
            foreach (var content in update.Contents)
            {
                if (content is TextContent { Text.Length: > 0 } textContent)
                {
                    foreach ((bool isReasoning, string text) in splitter.Push(textContent.Text))
                    {
                        outContents.Add(isReasoning ? new TextReasoningContent(text) : new TextContent(text));
                    }
                }
                else
                {
                    outContents.Add(content);
                }
            }

            if (outContents.Count > 0)
            {
                update.Contents = outContents;
                yield return update;
            }
        }

        var tail = splitter.Flush();
        if (tail.Count > 0)
        {
            // Reuse the last update's message identity: a tail update with no MessageId is treated as
            // a brand-new assistant message and renders as a stray extra chat bubble.
            yield return new ChatResponseUpdate(last?.Role ?? ChatRole.Assistant,
                [.. tail.Select(t => t.isReasoning ? (AIContent)new TextReasoningContent(t.text) : new TextContent(t.text))])
            {
                MessageId = last?.MessageId,
                ResponseId = last?.ResponseId,
                AuthorName = last?.AuthorName,
                CreatedAt = last?.CreatedAt,
            };
        }
    }

    /// <summary>Streaming splitter that separates <c>&lt;think&gt;…&lt;/think&gt;</c> from answer text.</summary>
    private sealed class ThinkSplitter
    {
        private const string Open = "<think>";
        private const string Close = "</think>";
        private static readonly int s_keep = Math.Max(Open.Length, Close.Length) - 1;

        private string _buffer = string.Empty;
        private bool _inThink;

        public List<(bool isReasoning, string text)> Push(string chunk)
        {
            this._buffer += chunk;
            var results = new List<(bool, string)>();

            while (true)
            {
                string tag = this._inThink ? Close : Open;
                int idx = this._buffer.IndexOf(tag, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    if (idx > 0)
                    {
                        results.Add((this._inThink, this._buffer[..idx]));
                    }
                    this._buffer = this._buffer[(idx + tag.Length)..];
                    this._inThink = !this._inThink;
                    continue;
                }

                // No complete tag yet. Emit everything except a short tail that might begin a tag.
                if (this._buffer.Length > s_keep)
                {
                    results.Add((this._inThink, this._buffer[..^s_keep]));
                    this._buffer = this._buffer[^s_keep..];
                }
                break;
            }

            return results;
        }

        public List<(bool isReasoning, string text)> Flush()
        {
            var result = new List<(bool, string)>();
            if (this._buffer.Length > 0)
            {
                result.Add((this._inThink, this._buffer));
                this._buffer = string.Empty;
            }
            return result;
        }
    }
}
