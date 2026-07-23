// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AGUI.Abstractions;
using AGUI.Server;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgenticUI.AgentServer.Scenarios.SharedState;

/// <summary>
/// Wraps a base agent to demonstrate AG-UI shared state. It asks the model to produce a recipe as
/// structured JSON and emits that as an AG-UI <c>STATE_SNAPSHOT</c> event (via
/// <see cref="ChatResponseUpdate.RawRepresentation"/>), then streams a short natural-language summary.
/// If the client supplies existing state on <see cref="RunAgentInput.State"/> it is used as context.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by the agent catalog.")]
internal sealed class SharedStateAgent : DelegatingAIAgent
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public SharedStateAgent(AIAgent innerAgent, JsonSerializerOptions jsonSerializerOptions)
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
        var chatRunOptions = options as ChatClientAgentRunOptions;

        // Use any client-supplied state as context (optional — the Blazor UIAgent does not send it today).
        JsonElement? incomingState = null;
        if (chatRunOptions?.ChatOptions is { } chatOptions &&
            chatOptions.TryGetRunAgentInput(out RunAgentInput? agentInput) &&
            agentInput.State is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } state)
        {
            incomingState = state;
        }

        var firstRunOptions = new ChatClientAgentRunOptions
        {
            ChatOptions = (chatRunOptions?.ChatOptions ?? new ChatOptions()).Clone(),
        };
        firstRunOptions.ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<RecipeResponse>(
            schemaName: "RecipeResponse",
            schemaDescription: "A response containing a recipe with title, skill level, cooking time, preferences, ingredients, and instructions");

        var firstRunMessages = messages.ToList();
        if (incomingState is { } current)
        {
            firstRunMessages.Add(new ChatMessage(ChatRole.System,
            [
                new TextContent("Here is the current state in JSON format:"),
                new TextContent(current.GetRawText()),
                new TextContent("Update it based on the user's request. The new state is:")
            ]));
        }

        var allUpdates = new List<AgentResponseUpdate>();
        await foreach (var update in this.InnerAgent.RunStreamingAsync(firstRunMessages, session, firstRunOptions, cancellationToken).ConfigureAwait(false))
        {
            allUpdates.Add(update);
        }

        var response = allUpdates.ToAgentResponse();

        if (!TryParse(response.Text, out JsonElement snapshot))
        {
            // Fall back to plain text if structured output failed.
            yield return new AgentResponseUpdate(ChatRole.Assistant, response.Text);
            yield break;
        }

        yield return StateUpdate.From(new StateSnapshotEvent { Snapshot = snapshot });

        var secondRunMessages = messages.Concat(response.Messages).Append(
            new ChatMessage(ChatRole.System,
            [new TextContent("Please provide a concise summary of the recipe in at most two sentences.")]));

        await foreach (var update in this.InnerAgent.RunStreamingAsync(secondRunMessages, session, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private bool TryParse(string json, out JsonElement result)
    {
        try
        {
            result = JsonSerializer.Deserialize<JsonElement>(json, this._jsonSerializerOptions);
            return result.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            result = default;
            return false;
        }
    }
}
