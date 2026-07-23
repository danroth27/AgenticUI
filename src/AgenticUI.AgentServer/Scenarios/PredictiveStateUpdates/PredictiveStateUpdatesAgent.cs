// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgenticUI.AgentServer.Scenarios.PredictiveStateUpdates;

/// <summary>
/// Wraps a document-editing agent to demonstrate predictive state updates. As the model calls the
/// <c>write_document</c> tool, the wrapper progressively emits the document content as AG-UI
/// <c>STATE_SNAPSHOT</c> events (via <see cref="ChatResponseUpdate.RawRepresentation"/>) so the UI shows
/// the document being written in real time, before the tool result is finalized.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by the agent catalog.")]
internal sealed class PredictiveStateUpdatesAgent : DelegatingAIAgent
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private const int ChunkSize = 10;

    public PredictiveStateUpdatesAgent(AIAgent innerAgent, JsonSerializerOptions jsonSerializerOptions)
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
        string? lastEmittedDocument = null;

        await foreach (var update in this.InnerAgent.RunStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
        {
            bool hasToolCall = false;
            string? documentContent = null;

            foreach (var content in update.Contents)
            {
                if (content is FunctionCallContent callContent && callContent.Name == "write_document")
                {
                    hasToolCall = true;
                    if (callContent.Arguments?.TryGetValue("document", out var documentValue) == true)
                    {
                        documentContent = documentValue?.ToString();
                    }
                }
            }

            yield return update;

            if (hasToolCall && documentContent != null && documentContent != lastEmittedDocument)
            {
                int startIndex = 0;
                if (lastEmittedDocument != null && documentContent.StartsWith(lastEmittedDocument, StringComparison.Ordinal))
                {
                    startIndex = lastEmittedDocument.Length;
                }

                for (int i = startIndex; i < documentContent.Length; i += ChunkSize)
                {
                    int length = Math.Min(ChunkSize, documentContent.Length - i);
                    string chunk = documentContent.Substring(0, i + length);

                    JsonElement snapshot = JsonSerializer.SerializeToElement(
                        new DocumentState { Document = chunk }, this._jsonSerializerOptions);
                    yield return StateUpdate.From(new StateSnapshotEvent { Snapshot = snapshot });

                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }

                lastEmittedDocument = documentContent;
            }
        }
    }
}
