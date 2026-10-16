using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace AgenticUI.Web.Voice;

/// <summary>
/// Passes every streaming update through unchanged while turning the assistant's Markdown text into
/// speakable sentences for text-to-speech. Place it inside <c>FormattedChatClient</c> so it only
/// ever sees the raw <see cref="TextContent"/> and not the rich text snapshots.
/// </summary>
/// <remarks>
/// It also keeps the agent's history honest after a barge-in. The UI keeps showing the full reply,
/// but once <see cref="RecordInterruption"/> reports what the user actually heard, every later
/// request sends the agent a copy of the history in which that reply is cut down to the heard text.
/// </remarks>
public sealed partial class SpeechTapChatClient(IChatClient innerClient, ILogger logger)
    : DelegatingChatClient(innerClient)
{
    private const string Fence = "```";
    private const string InterruptedMarker = "[interrupted by the user]";
    private const string UnheardReply = "[The user interrupted this reply before hearing any of it.]";

    // Long runs without punctuation (lists, run-on text) are still spoken in reasonably sized pieces.
    private const int MaxSentenceLength = 240;

    private readonly Lock _gate = new();

    // The most recent response: the user message that prompted it, and the assistant messages it
    // produced, in order (id -> raw text).
    private ResponseRecord? _lastResponse;

    // Interrupted responses: their assistant message ids and what the user heard of them.
    private readonly List<Interruption> _interruptions = [];

    /// <summary>When false, no sentences are produced; the response lifecycle is still reported.</summary>
    public bool Enabled { get; set; }

    /// <summary>Raised when a response starts streaming, before its first sentence.</summary>
    public Func<Task>? ResponseStarted { get; set; }

    /// <summary>Raised, in order, for each speakable sentence of the assistant's reply.</summary>
    public Func<string, Task>? SentenceReady { get; set; }

    /// <summary>
    /// Raised when the response stream ends: <see langword="true"/> after the last sentence of a
    /// completed reply, <see langword="false"/> when it was cancelled or failed.
    /// </summary>
    public Func<bool, Task>? ResponseEnded { get; set; }

    /// <summary>
    /// Records that the user barged in on the most recent response after hearing
    /// <paramref name="spokenText"/> (empty or <see langword="null"/> when nothing was heard).
    /// </summary>
    public void RecordInterruption(string? spokenText)
    {
        lock (_gate)
        {
            if (_lastResponse is not { Messages.Count: > 0 } response)
            {
                return;
            }

            _interruptions.Add(new Interruption(
                response.Request,
                [.. response.Messages.Select(message => (message.MessageId, message.Text.ToString()))],
                spokenText?.Trim() ?? string.Empty));
            _lastResponse = null;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var original = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        var history = ReconcileHistory(original);
        var response = new ResponseRecord(original.LastOrDefault(message => message.Role == ChatRole.User));
        lock (_gate)
        {
            _lastResponse = response;
        }

        var splitter = Enabled ? new SentenceSplitter() : null;
        var completed = false;
        await RaiseAsync(ResponseStarted).ConfigureAwait(false);

        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(
                history,
                options,
                cancellationToken).ConfigureAwait(false))
            {
                if (update.Role is null || update.Role == ChatRole.Assistant)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is not TextContent { Text: { Length: > 0 } text })
                        {
                            continue;
                        }

                        Track(response.Messages, update.MessageId, text);
                        if (splitter is not null)
                        {
                            foreach (var sentence in splitter.Append(text))
                            {
                                await RaiseSentenceAsync(sentence).ConfigureAwait(false);
                            }
                        }
                    }
                }

                yield return update;
            }

            if (splitter is not null)
            {
                foreach (var sentence in splitter.Complete())
                {
                    await RaiseSentenceAsync(sentence).ConfigureAwait(false);
                }
            }

            completed = true;
        }
        finally
        {
            await RaiseAsync(() => ResponseEnded?.Invoke(completed) ?? Task.CompletedTask)
                .ConfigureAwait(false);
        }
    }

    private void Track(List<(string MessageId, StringBuilder Text)> response, string? messageId, string text)
    {
        lock (_gate)
        {
            var id = messageId ?? string.Empty;
            if (response.Count == 0 || response[^1].MessageId != id)
            {
                response.Add((id, new StringBuilder()));
            }

            response[^1].Text.Append(text);
        }
    }

    // Builds the history the agent sees: a copy in which each interrupted reply keeps only what the
    // user heard. The originals belong to the UI's conversation and are never modified.
    private IEnumerable<ChatMessage> ReconcileHistory(IReadOnlyList<ChatMessage> messages)
    {
        Interruption[] interruptions;
        lock (_gate)
        {
            if (_interruptions.Count == 0)
            {
                return messages;
            }

            interruptions = [.. _interruptions];
        }

        var reconciled = new List<ChatMessage>();
        var replaced = new HashSet<Interruption>();
        foreach (var message in messages)
        {
            var interruption = message.Role == ChatRole.Assistant
                ? Array.Find(interruptions, candidate => candidate.Matches(message))
                : null;
            if (interruption is null)
            {
                reconciled.Add(message);
                continue;
            }

            // The heard text spans the whole spoken turn, so it replaces the text of the reply's first
            // message; any later messages of the same reply keep only their non-text content (such as
            // tool calls), so the conversation structure stays intact.
            var heard = replaced.Add(interruption)
                ? interruption.SpokenText.Length > 0
                    ? $"{interruption.SpokenText} {InterruptedMarker}"
                    : UnheardReply
                : null;
            var contents = message.Contents.Where(content => content is not TextContent).ToList();
            if (heard is not null)
            {
                contents.Insert(0, new TextContent(heard));
            }

            if (contents.Count > 0)
            {
                reconciled.Add(new ChatMessage(message.Role, contents)
                {
                    MessageId = message.MessageId,
                    AuthorName = message.AuthorName,
                    CreatedAt = message.CreatedAt,
                });
            }
        }

        // A cancelled reply may be missing from the history altogether (the conversation drops
        // responses that never completed). The user still heard part of it, so put that part back
        // right after the request that prompted it.
        foreach (var interruption in interruptions)
        {
            if (replaced.Contains(interruption) || interruption.SpokenText.Length == 0)
            {
                continue;
            }

            var request = reconciled.FindLastIndex(interruption.IsRequest);
            if (request >= 0 &&
                (request + 1 == reconciled.Count || reconciled[request + 1].Role != ChatRole.Assistant))
            {
                reconciled.Insert(request + 1, new ChatMessage(
                    ChatRole.Assistant,
                    $"{interruption.SpokenText} {InterruptedMarker}"));
            }
        }

        return reconciled;
    }

    private sealed record ResponseRecord(ChatMessage? Request)
    {
        public List<(string MessageId, StringBuilder Text)> Messages { get; } = [];
    }

    private sealed record Interruption(
        ChatMessage? Request,
        IReadOnlyList<(string MessageId, string Text)> Messages,
        string SpokenText)
    {
        public bool IsRequest(ChatMessage message) =>
            Request is not null &&
            message.Role == ChatRole.User &&
            (ReferenceEquals(message, Request) ||
             (Request.MessageId is { Length: > 0 } id && id == message.MessageId) ||
             message.Text == Request.Text);

        // Prefer the message id; fall back to the text in case the conversation re-keys messages.
        public bool Matches(ChatMessage message) => Messages.Any(recorded =>
            (recorded.MessageId.Length > 0 && recorded.MessageId == message.MessageId) ||
            (recorded.Text.Length > 0 && recorded.Text == message.Text));
    }

    private Task RaiseSentenceAsync(string sentence) =>
        SentenceReady is { } callback ? RaiseAsync(() => callback(sentence)) : Task.CompletedTask;

    // Speech is a side channel: a playback failure must never break the chat stream.
    private async Task RaiseAsync(Func<Task>? callback)
    {
        if (callback is null)
        {
            return;
        }

        try
        {
            await callback().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Text-to-speech callback failed.");
        }
    }

    /// <summary>
    /// Buffers streamed Markdown, drops fenced code blocks, and yields cleaned sentences as soon as
    /// they are complete.
    /// </summary>
    private sealed partial class SentenceSplitter
    {
        private readonly StringBuilder _raw = new();
        private readonly StringBuilder _speakable = new();
        private bool _inFence;

        public IEnumerable<string> Append(string text)
        {
            _raw.Append(text);
            DrainRaw(isFinal: false);
            return TakeSentences(isFinal: false);
        }

        public IEnumerable<string> Complete()
        {
            DrainRaw(isFinal: true);
            return TakeSentences(isFinal: true);
        }

        // Moves text outside code fences from _raw to _speakable. A trailing run of backticks may be
        // the start of a fence split across chunks, so it stays in _raw until more text arrives.
        private void DrainRaw(bool isFinal)
        {
            while (true)
            {
                var raw = _raw.ToString();
                var fence = raw.IndexOf(Fence, StringComparison.Ordinal);
                if (fence < 0)
                {
                    var keep = isFinal ? 0 : TrailingBackticks(raw);
                    if (!_inFence)
                    {
                        _speakable.Append(raw, 0, raw.Length - keep);
                    }

                    _raw.Clear().Append(raw, raw.Length - keep, keep);
                    return;
                }

                if (!_inFence)
                {
                    _speakable.Append(raw, 0, fence);
                }

                _inFence = !_inFence;
                _raw.Remove(0, fence + Fence.Length);
            }
        }

        private List<string> TakeSentences(bool isFinal)
        {
            var sentences = new List<string>();
            while (_speakable.Length > 0)
            {
                var text = _speakable.ToString();
                var end = FindSentenceEnd(text);
                if (end < 0)
                {
                    if (isFinal)
                    {
                        end = text.Length;
                    }
                    else if (text.Length > MaxSentenceLength)
                    {
                        var space = text.LastIndexOf(' ', MaxSentenceLength);
                        end = space > 0 ? space + 1 : MaxSentenceLength;
                    }
                    else
                    {
                        break;
                    }
                }

                _speakable.Remove(0, end);
                if (Clean(text[..end]) is { Length: > 0 } sentence)
                {
                    sentences.Add(sentence);
                }
            }

            return sentences;
        }

        // A sentence ends at a newline, or at . ! ? followed by whitespace. The whitespace must have
        // arrived already, so "3." waiting for ".14" is not split early.
        private static int FindSentenceEnd(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    return i + 1;
                }

                if (text[i] is '.' or '!' or '?' &&
                    i + 1 < text.Length &&
                    char.IsWhiteSpace(text[i + 1]))
                {
                    return i + 1;
                }
            }

            return -1;
        }

        private static int TrailingBackticks(string text)
        {
            var count = 0;
            while (count < text.Length && text[text.Length - 1 - count] == '`')
            {
                count++;
            }

            return count;
        }

        private static string Clean(string markdown)
        {
            var text = ImageRegex().Replace(markdown, string.Empty);
            text = LinkRegex().Replace(text, "$1");
            text = LinePrefixRegex().Replace(text, string.Empty);
            text = EmphasisRegex().Replace(text, string.Empty);
            text = SeparatorRegex().Replace(text, " ");
            text = WhitespaceRegex().Replace(text, " ").Trim();
            return text.Any(char.IsLetterOrDigit) ? text : string.Empty;
        }

        [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
        private static partial Regex ImageRegex();

        [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
        private static partial Regex LinkRegex();

        // Headings, block quotes, bullets and numbered list markers.
        [GeneratedRegex(@"^\s*(#{1,6}\s+|>\s*|[-*+]\s+|\d+[.)]\s+)", RegexOptions.Multiline)]
        private static partial Regex LinePrefixRegex();

        // Emphasis and inline code markers. Underscores only count at word edges, so snake_case stays.
        [GeneratedRegex(@"[*`~]+|(?<!\w)_+|_+(?!\w)")]
        private static partial Regex EmphasisRegex();

        // Table pipes and horizontal rules separate words, so they become spaces.
        [GeneratedRegex(@"\||-{3,}")]
        private static partial Regex SeparatorRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();
    }
}
