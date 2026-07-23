// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgenticUI.AgentServer.Scenarios.Reasoning;

/// <summary>
/// Wraps a reasoning model (e.g. DeepSeek-R1) that emits its chain of thought inline as
/// <c>&lt;think&gt;…&lt;/think&gt;</c> in the message text. This wrapper splits that thinking out and
/// re-emits it as <see cref="TextReasoningContent"/>, which AGUI.Server turns into AG-UI
/// <c>REASONING_*</c> events and the Blazor AI components render as a collapsible "thought process".
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by the agent catalog.")]
internal sealed class ReasoningAgent : DelegatingAIAgent
{
    public ReasoningAgent(AIAgent innerAgent) : base(innerAgent)
    {
    }

    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        => this.RunCoreStreamingAsync(messages, session, options, cancellationToken).ToAgentResponseAsync(cancellationToken);

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var splitter = new ThinkSplitter();

        await foreach (var update in this.InnerAgent.RunStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
        {
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
                yield return CloneWith(update, outContents);
            }
        }

        var tail = splitter.Flush();
        if (tail.Count > 0)
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant,
                [.. tail.Select(t => t.isReasoning ? (AIContent)new TextReasoningContent(t.text) : new TextContent(t.text))]);
        }
    }

    private static AgentResponseUpdate CloneWith(AgentResponseUpdate source, IList<AIContent> contents) =>
        new(source.Role ?? ChatRole.Assistant, contents)
        {
            MessageId = source.MessageId,
            ResponseId = source.ResponseId,
            CreatedAt = source.CreatedAt,
            AuthorName = source.AuthorName,
            AgentId = source.AgentId,
        };

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
            _buffer += chunk;
            var results = new List<(bool, string)>();

            while (true)
            {
                string tag = _inThink ? Close : Open;
                int idx = _buffer.IndexOf(tag, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    if (idx > 0)
                    {
                        results.Add((_inThink, _buffer.Substring(0, idx)));
                    }
                    _buffer = _buffer.Substring(idx + tag.Length);
                    _inThink = !_inThink;
                    continue;
                }

                // No complete tag yet. Emit everything except a short tail that might begin a tag.
                if (_buffer.Length > s_keep)
                {
                    results.Add((_inThink, _buffer.Substring(0, _buffer.Length - s_keep)));
                    _buffer = _buffer.Substring(_buffer.Length - s_keep);
                }
                break;
            }

            return results;
        }

        public List<(bool isReasoning, string text)> Flush()
        {
            var result = new List<(bool, string)>();
            if (_buffer.Length > 0)
            {
                result.Add((_inThink, _buffer));
                _buffer = string.Empty;
            }
            return result;
        }
    }
}
