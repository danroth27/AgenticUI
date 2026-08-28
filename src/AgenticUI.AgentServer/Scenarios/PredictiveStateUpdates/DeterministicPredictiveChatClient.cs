// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgenticUI.AgentServer.Scenarios.PredictiveStateUpdates;

internal enum DeterministicPredictiveScenario
{
    MultipleTurns,
    EarlyCompletedCall,
    MixedText,
    Unicode,
    Approval,
    ParallelCalls,
}

internal sealed class DeterministicPredictiveChatClient(
    DeterministicPredictiveScenario scenario,
    ILogger<DeterministicPredictiveChatClient> logger)
    : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in GetStreamingResponseAsync(
            messages,
            options,
            cancellationToken).ConfigureAwait(false))
        {
            updates.Add(update);
        }

        return updates.ToChatResponse();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var results = messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Where(result => result.CallId.StartsWith("deterministic-", StringComparison.Ordinal))
            .ToArray();

        if (scenario == DeterministicPredictiveScenario.MultipleTurns)
        {
            if (results.Length == 0)
            {
                await foreach (var update in EmitCallAsync(
                    callId: "deterministic-first",
                    document: "First deterministic document.",
                    includeMixedText: false,
                    emitCompletedCallEarly: false,
                    splitUnicodeEscape: false,
                    cancellationToken))
                {
                    yield return update;
                }
                yield break;
            }

            if (results.Length == 1)
            {
                await foreach (var update in EmitCallAsync(
                    callId: "deterministic-second",
                    document: "Second deterministic document replaces the first.",
                    includeMixedText: false,
                    emitCompletedCallEarly: false,
                    splitUnicodeEscape: false,
                    cancellationToken))
                {
                    yield return update;
                }
                yield break;
            }

            yield return CreateTextUpdate("Both deterministic document updates completed.");
            yield break;
        }

        if (scenario == DeterministicPredictiveScenario.ParallelCalls)
        {
            if (results.Length == 0)
            {
                await foreach (var update in EmitParallelCallsAsync(cancellationToken))
                {
                    yield return update;
                }
                yield break;
            }

            yield return CreateTextUpdate("Both parallel document calls completed.");
            yield break;
        }

        if (results.Length > 0)
        {
            yield return CreateTextUpdate("The deterministic tool invocation completed.");
            yield break;
        }

        var document = scenario switch
        {
            DeterministicPredictiveScenario.Unicode =>
                "Unicode document: 😀 café 東京.",
            DeterministicPredictiveScenario.MixedText =>
                "Document generated after an assistant preamble.",
            DeterministicPredictiveScenario.EarlyCompletedCall =>
                "Document whose completed call was emitted before a trailing provider update.",
            DeterministicPredictiveScenario.Approval =>
                "Document awaiting approval before invocation.",
            _ => throw new InvalidOperationException($"Unsupported scenario '{scenario}'."),
        };

        await foreach (var update in EmitCallAsync(
            callId: $"deterministic-{scenario}",
            document,
            includeMixedText: scenario == DeterministicPredictiveScenario.MixedText,
            emitCompletedCallEarly: scenario == DeterministicPredictiveScenario.EarlyCompletedCall,
            splitUnicodeEscape: scenario == DeterministicPredictiveScenario.Unicode,
            cancellationToken))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private async IAsyncEnumerable<ChatResponseUpdate> EmitParallelCallsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var firstArguments = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string> { ["document"] = "Parallel document A." });
        var secondArguments = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string> { ["document"] = "Parallel document B." });
        var firstFragments = SplitArguments(firstArguments);
        var secondFragments = SplitArguments(secondArguments);

        for (var fragmentIndex = 0; fragmentIndex < firstFragments.Count; fragmentIndex++)
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                RawRepresentation = new PrototypeToolCallArgumentUpdate(
                    Index: 0,
                    CallId: fragmentIndex == 0 ? "deterministic-parallel-a" : null,
                    Name: fragmentIndex == 0 ? "write_document_local" : null,
                    Delta: firstFragments[fragmentIndex]),
            };
            yield return new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                RawRepresentation = new PrototypeToolCallArgumentUpdate(
                    Index: 1,
                    CallId: fragmentIndex == 0 ? "deterministic-parallel-b" : null,
                    Name: fragmentIndex == 0 ? "write_document_local" : null,
                    Delta: secondFragments[fragmentIndex]),
            };
        }

        yield return new ChatResponseUpdate(
            ChatRole.Assistant,
            [
                new FunctionCallContent(
                    "deterministic-parallel-a",
                    "write_document_local",
                    new Dictionary<string, object?> { ["document"] = "Parallel document A." }),
                new FunctionCallContent(
                    "deterministic-parallel-b",
                    "write_document_local",
                    new Dictionary<string, object?> { ["document"] = "Parallel document B." }),
            ])
        {
            MessageId = "message-deterministic-parallel",
        };
    }

    private async IAsyncEnumerable<ChatResponseUpdate> EmitCallAsync(
        string callId,
        string document,
        bool includeMixedText,
        bool emitCompletedCallEarly,
        bool splitUnicodeEscape,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (includeMixedText)
        {
            yield return CreateTextUpdate("I will prepare the document now.");
        }

        var arguments = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string> { ["document"] = document });
        var fragments = splitUnicodeEscape
            ? SplitUnicodeArguments(arguments)
            : SplitArguments(arguments);

        var fragmentIndex = 0;
        foreach (var fragment in fragments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (emitCompletedCallEarly && fragmentIndex == 2)
            {
                yield return CreateFunctionCallUpdate(callId, document);
                logger.LogInformation(
                    "Deterministic {Scenario} emitted completed call before trailing provider updates.",
                    scenario);
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                RawRepresentation = new PrototypeToolCallArgumentUpdate(
                    Index: 0,
                    CallId: fragmentIndex == 0 ? callId : null,
                    Name: fragmentIndex == 0 ? "write_document_local" : null,
                    Delta: fragment),
            };
            logger.LogInformation(
                "Deterministic {Scenario} emitted fragment {FragmentIndex} for {CallId}.",
                scenario,
                fragmentIndex,
                callId);
            fragmentIndex++;
        }

        if (!emitCompletedCallEarly)
        {
            yield return CreateFunctionCallUpdate(callId, document);
        }
        else
        {
            yield return CreateTextUpdate("Trailing provider update after the completed call.");
        }
    }

    private static ChatResponseUpdate CreateFunctionCallUpdate(
        string callId,
        string document) =>
        new(
            ChatRole.Assistant,
            [
                new FunctionCallContent(
                    callId,
                    "write_document_local",
                    new Dictionary<string, object?> { ["document"] = document }),
            ])
        {
            MessageId = $"message-{callId}",
        };

    private static ChatResponseUpdate CreateTextUpdate(string text) =>
        new(ChatRole.Assistant, [new TextContent(text)])
        {
            MessageId = Guid.NewGuid().ToString("N"),
        };

    private static IReadOnlyList<string> SplitArguments(string arguments)
    {
        var first = Math.Min(14, arguments.Length);
        var second = Math.Min(first + 18, arguments.Length);
        return
        [
            arguments[..first],
            arguments[first..second],
            arguments[second..],
        ];
    }

    private static IReadOnlyList<string> SplitUnicodeArguments(string arguments)
    {
        var highSurrogateEnd = arguments.IndexOf("\\ud83d", StringComparison.OrdinalIgnoreCase);
        if (highSurrogateEnd < 0)
        {
            return SplitArguments(arguments);
        }

        highSurrogateEnd += "\\ud83d".Length;
        return
        [
            arguments[..highSurrogateEnd],
            arguments[highSurrogateEnd..],
        ];
    }
}
