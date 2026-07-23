// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgenticUI.AgentServer.Scenarios.AgenticGenerativeUi;

/// <summary>
/// Wraps the planning agent so that the results of <c>create_plan</c> / <c>update_plan_step</c> tool
/// calls are re-emitted as AG-UI state: <c>create_plan</c> results become a <c>STATE_SNAPSHOT</c>
/// and <c>update_plan_step</c> results become a <c>STATE_DELTA</c> (JSON Patch), so the UI can render
/// live plan progress.
/// </summary>
/// <remarks>
/// State events are surfaced by setting <see cref="ChatResponseUpdate.RawRepresentation"/> to a
/// <see cref="StateSnapshotEvent"/> / <see cref="StateDeltaEvent"/>. (This is the supported API;
/// emitting state as a <c>DataContent("application/json")</c>, as some older samples do, was a
/// pre-public-API hack and is intentionally no longer supported.)
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by the agent catalog.")]
internal sealed class AgenticUIAgent : DelegatingAIAgent
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public AgenticUIAgent(AIAgent innerAgent, JsonSerializerOptions jsonSerializerOptions)
        : base(innerAgent)
    {
        this._jsonSerializerOptions = jsonSerializerOptions;
    }

    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        => this.RunCoreStreamingAsync(messages, session, options, cancellationToken).ToAgentResponseAsync(cancellationToken);

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var trackedFunctionCalls = new Dictionary<string, FunctionCallContent>();

        await foreach (var update in this.InnerAgent.RunStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;

            foreach (var content in update.Contents)
            {
                if (content is FunctionCallContent callContent &&
                    (callContent.Name == "create_plan" || callContent.Name == "update_plan_step"))
                {
                    trackedFunctionCalls[callContent.CallId] = callContent;
                }
                else if (content is FunctionResultContent resultContent &&
                         trackedFunctionCalls.TryGetValue(resultContent.CallId, out var matchedCall))
                {
                    JsonElement payload = JsonSerializer.SerializeToElement(resultContent.Result, this._jsonSerializerOptions);
                    BaseEvent stateEvent = matchedCall.Name == "create_plan"
                        ? new StateSnapshotEvent { Snapshot = payload }
                        : new StateDeltaEvent { Delta = payload };

                    yield return StateUpdate.From(stateEvent);
                }
            }
        }
    }
}
