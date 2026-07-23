// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json;
using AGUI.Abstractions;
using AGUI.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;

namespace AgenticUI.AgentServer.Scenarios.PredictiveStateUpdates;

/// <summary>
/// Predictive state updates, wired the idiomatic way with <see cref="AGUIStreamOptions.MapCall"/>.
/// The model's <c>write_document</c> call is intercepted <em>before</em> execution; the mapping reads
/// the streamed <c>document</c> argument and emits progressive <c>STATE_SNAPSHOT</c> events so the UI
/// renders the document as it is written. Because the mapping produces the tool result itself, the
/// chat client is built without function invocation and streamed through the AG-UI pipeline directly
/// (<see cref="RunAgentInputExtensions.ToChatRequestContext"/> / <c>AsAGUIEventStreamAsync</c>).
/// </summary>
internal static class PredictiveStateEndpoint
{
    private const int ChunkSize = 10;

    private const string SystemPrompt =
        """
        You are a document editor assistant. When asked to write or edit content:
        - Use the `write_document` tool with the full document text in Markdown format.
        - You MUST write the full document, even when changing only a few words.
        - When making edits, keep them minimal. Do not change every word.
        - Keep stories SHORT.

        After writing the document, briefly summarize what you wrote in at most two sentences.
        """;

    [Description("Write a document. Use markdown formatting to format the document.")]
    private static string WriteDocument(
        [Description("The full document text in Markdown format.")] string document) => "Document written.";

    /// <summary>Maps a POST endpoint that streams the document as predictive <c>STATE_SNAPSHOT</c> events.</summary>
    public static void MapPredictiveStateUpdates(
        this WebApplication app, string route, IChatClient chatClient, JsonSerializerOptions jsonOptions)
    {
        AITool writeDocument = AIFunctionFactory.Create(
            WriteDocument,
            name: "write_document",
            description: "Write a document. Use markdown formatting to format the document.",
            jsonOptions);

        app.MapPost(route, (
            [FromBody] RunAgentInput input,
            CancellationToken cancellationToken) =>
        {
            AGUIStreamOptions streamOptions = CreateStreamOptions(jsonOptions);

            ChatRequestContext ctx = input.ToChatRequestContext(jsonOptions, streamOptions);
            ctx.Messages.Insert(0, new ChatMessage(ChatRole.System, SystemPrompt));
            (ctx.ChatOptions.Tools ??= []).Add(writeDocument);

            IAsyncEnumerable<ChatResponseUpdate> updates =
                chatClient.GetStreamingResponseAsync(ctx.Messages, ctx.ChatOptions, cancellationToken);
            IAsyncEnumerable<BaseEvent> events = updates.AsAGUIEventStreamAsync(ctx, cancellationToken);

            return TypedResults.ServerSentEvents(events);
        });
    }

    private static AGUIStreamOptions CreateStreamOptions(JsonSerializerOptions jsonOptions)
    {
        string? lastEmittedDocument = null;

        return new AGUIStreamOptions().MapCall("write_document", fcc =>
        {
            string? document = fcc.Arguments?.TryGetValue("document", out var value) == true
                ? value?.ToString()
                : null;

            if (document is null || document == lastEmittedDocument)
            {
                return [];
            }

            var events = new List<BaseEvent>();

            // Only stream the newly added portion if the document grew.
            int startIndex = lastEmittedDocument is not null &&
                document.StartsWith(lastEmittedDocument, StringComparison.Ordinal)
                    ? lastEmittedDocument.Length
                    : 0;

            for (int i = startIndex; i < document.Length; i += ChunkSize)
            {
                int length = Math.Min(ChunkSize, document.Length - i);
                var snapshot = new DocumentState { Document = document[..(i + length)] };
                JsonElement snapshotJson = JsonSerializer.SerializeToElement(snapshot, jsonOptions);
                events.Add(new StateSnapshotEvent { Snapshot = snapshotJson });
            }

            // Complete the write_document call (its content is now reflected in state) so the client
            // sees no pending tool call.
            events.Add(new ToolCallResultEvent
            {
                MessageId = Guid.NewGuid().ToString("N"),
                ToolCallId = fcc.CallId,
                Content = "Document written.",
                Role = "tool",
            });

            lastEmittedDocument = document;
            return events;
        });
    }
}
