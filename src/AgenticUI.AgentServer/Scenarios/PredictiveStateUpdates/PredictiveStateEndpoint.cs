// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json;
using AGUI.Abstractions;
using AGUI.Formatting;
using AGUI.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;

namespace AgenticUI.AgentServer.Scenarios.PredictiveStateUpdates;

internal static class PredictiveStateEndpoint
{
    private const string PredictiveStateMediaType =
        "application/vnd.aspnetcore.ai.predictive-state+json";

    private const string SystemPrompt = """
        You are a document editor assistant. When asked to write or edit content:

        IMPORTANT:
        - Use the `write_document_local` tool with the full document text in Markdown format
        - If the user asks to clear the document, call the tool with an empty document string;
          do not substitute a notice, placeholder, or explanation
        - Format the document extensively so it is easy to read
        - You can use headings, lists, bold text, and other Markdown
        - Do not use italic or strike-through formatting
        - Always write the full document, even when changing only a few words
        - Keep edits focused and stories short

        After writing the document, briefly summarize the changes in at most two sentences.
        """;

    internal static IEndpointConventionBuilder MapPredictiveStateEndpoint(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        IChatClient chatClient,
        JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        var serverTools = new List<AITool>
        {
            AIFunctionFactory.Create(
                WriteDocument,
                name: "write_document_local",
                description: "Write a document using Markdown formatting.",
                jsonOptions),
        };

        return endpoints.MapPost(pattern, (
            [FromBody] RunAgentInput input,
            CancellationToken cancellationToken) =>
        {
            var streamOptions = CreateStreamOptions(jsonOptions);
            var clientTools = input.Tools;
            input.Tools = null;
            var context = input.ToChatRequestContext(jsonOptions, streamOptions);
            input.Tools = clientTools;

            var confirmationCallIds = context.Messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionCallContent>()
                .Where(call => call.Name == "confirm_changes")
                .Select(call => call.CallId)
                .Where(callId => callId is not null)
                .ToHashSet(StringComparer.Ordinal);
            var confirmationCompleted = context.Messages
                .LastOrDefault()?
                .Contents
                .OfType<FunctionResultContent>()
                .Any(result => confirmationCallIds.Contains(result.CallId)) == true;

            if (!confirmationCompleted)
            {
                var serverToolNames = serverTools
                    .Select(tool => tool.Name)
                    .ToHashSet(StringComparer.Ordinal);
                context.ChatOptions.Tools ??= [];
                if (clientTools is { Count: > 0 })
                {
                    foreach (var tool in clientTools.AsAITools())
                    {
                        if (!serverToolNames.Contains(tool.Name))
                        {
                            context.ChatOptions.Tools.Add(tool);
                        }
                    }
                }
                foreach (var tool in serverTools)
                {
                    context.ChatOptions.Tools.Add(tool);
                }
            }

            var systemMessage = confirmationCompleted
                ? "The user has decided whether to keep the proposed document. Do not call any tools. Briefly acknowledge their decision."
                : $"""
                    {SystemPrompt}

                    The current document state is:
                    {input.State}
                    """;
            context.Messages.Insert(0, new ChatMessage(ChatRole.System, systemMessage));
            var updates = chatClient.GetStreamingResponseAsync(
                context.Messages,
                context.ChatOptions,
                cancellationToken);
            var events = updates.AsAGUIEventStreamAsync(context, cancellationToken);

            return new AGUIEventStreamResult(
                events,
                new SseEventStreamFormatter(),
                cancellationToken);
        });
    }

    private static AGUIStreamOptions CreateStreamOptions(JsonSerializerOptions jsonOptions)
    {
        string? lastEmittedDocument = null;
        var hasEmittedConfirmation = false;
        var options = new AGUIStreamOptions();

        options.MapContent(content => content is DataContent data &&
            data.MediaType == PredictiveStateMediaType
                ? [new StateSnapshotEvent
                {
                    Snapshot = JsonSerializer.Deserialize<JsonElement>(data.Data.Span),
                }]
                : null);

        options.MapCall("write_document_local", call =>
        {
            var document = call.Arguments?.TryGetValue("document", out var value) == true
                ? value?.ToString()
                : null;
            if (document is null)
            {
                return [];
            }

            var events = new List<BaseEvent>();
            if (document != lastEmittedDocument)
            {
                var startIndex = lastEmittedDocument is not null &&
                    document.StartsWith(lastEmittedDocument, StringComparison.Ordinal)
                        ? lastEmittedDocument.Length
                        : 0;

                if (document.Length == 0)
                {
                    events.Add(new StateSnapshotEvent
                    {
                        Snapshot = JsonSerializer.SerializeToElement(
                            new DocumentState(),
                            jsonOptions),
                    });
                }

                const int chunkSize = 10;
                for (var index = startIndex; index < document.Length; index += chunkSize)
                {
                    var length = Math.Min(chunkSize, document.Length - index);
                    var state = new DocumentState { Document = document[..(index + length)] };
                    events.Add(new StateSnapshotEvent
                    {
                        Snapshot = JsonSerializer.SerializeToElement(state, jsonOptions),
                    });
                }

                lastEmittedDocument = document;
            }

            events.Add(new ToolCallResultEvent
            {
                MessageId = Guid.NewGuid().ToString("N"),
                ToolCallId = call.CallId,
                Content = "Document written.",
                Role = "tool",
            });

            if (!hasEmittedConfirmation)
            {
                hasEmittedConfirmation = true;
                var confirmationCallId = Guid.NewGuid().ToString("N");
                events.Add(new ToolCallStartEvent
                {
                    ToolCallId = confirmationCallId,
                    ToolCallName = "confirm_changes",
                    ParentMessageId = Guid.NewGuid().ToString("N"),
                });
                events.Add(new ToolCallArgsEvent
                {
                    ToolCallId = confirmationCallId,
                    Delta = "{}",
                });
                events.Add(new ToolCallEndEvent { ToolCallId = confirmationCallId });
            }

            return events;
        });

        return options;
    }

    [Description("Write a document in Markdown format.")]
    private static string WriteDocument(
        [Description("The complete document content.")] string document)
        => "Document written.";
}
