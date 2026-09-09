// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AgenticUI.Web.Components.Pages.Scenarios;

public sealed class PackageFeatureChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => GetStreamingResponseAsync(messages, options, cancellationToken)
            .ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lastMessage = messages.LastOrDefault();
        if (lastMessage?.Role == ChatRole.Tool)
        {
            yield return TextUpdate("prediction-result", "Prediction resolved.");
            yield break;
        }

        var prompt = string.Concat(
            lastMessage?.Contents.OfType<TextContent>().Select(content => content.Text) ?? []);

        if (prompt.Contains("rich", StringComparison.OrdinalIgnoreCase))
        {
            yield return RichUpdate("rich-1", "Structured");
            await Task.Delay(100, cancellationToken);
            yield return RichUpdate("rich-1", "Structured output with inline code");
            yield break;
        }

        if (prompt.Contains("server", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("weather", StringComparison.OrdinalIgnoreCase))
        {
            const string callId = "weather-call";
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = "weather",
                Contents =
                [
                    new FunctionCallContent(
                        callId,
                        "get_weather",
                        new Dictionary<string, object?> { ["location"] = "Seattle" }),
                ],
            };

            await Task.Delay(100, cancellationToken);
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = "weather",
                Contents =
                [
                    new FunctionResultContent(
                        callId,
                        JsonSerializer.Serialize(new WeatherInfo
                        {
                            Temperature = 20,
                            Conditions = "sunny",
                            Humidity = 50,
                            WindSpeed = 10,
                            FeelsLike = 25,
                        })),
                ],
                FinishReason = ChatFinishReason.Stop,
            };
            yield break;
        }

        if (prompt.Contains("activity", StringComparison.OrdinalIgnoreCase))
        {
            yield return ActivityUpdate("research", "Searching sources", isComplete: false);
            await Task.Delay(100, cancellationToken);
            yield return ActivityUpdate("research", "Comparing results", isComplete: false);
            await Task.Delay(100, cancellationToken);
            yield return ActivityUpdate("research", "Research complete", isComplete: true);
            yield return TextUpdate("activity-result", "Activity finished.");
            yield break;
        }

        if (prompt.Contains("predict", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("arrive", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("delivery", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = "prediction",
                Contents =
                [
                    new VerificationStateContent("Express shipping", isPredictive: true),
                    new TextContent(
                        "Express shipping should arrive before Friday. " +
                        "Would you like to update the delivery preference?"),
                    new FunctionCallContent(
                        "prediction-call",
                        "complete_prediction",
                        new Dictionary<string, object?>()),
                ],
                FinishReason = ChatFinishReason.ToolCalls,
            };
            yield break;
        }

        if (prompt.Contains("state", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = "state",
                Contents = [new VerificationStateContent("Committed value", isPredictive: false)],
                FinishReason = ChatFinishReason.Stop,
            };
            yield break;
        }

        yield return TextUpdate(
            "help",
            "Enter rich, server, activity, state, or predict to exercise a package feature.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(IChatClient) ? this : null;

    public void Dispose()
    {
    }

    private static ChatResponseUpdate RichUpdate(string messageId, string text)
    {
        var paragraph = new ParagraphNode();
        var strong = new StrongNode();
        strong.AddChild(new TextNode(text.StartsWith("Structured output", StringComparison.Ordinal)
            ? "Structured output"
            : text));
        paragraph.AddChild(strong);

        if (text.Contains("inline code", StringComparison.Ordinal))
        {
            paragraph.AddChild(new TextNode(" with "));
            paragraph.AddChild(new InlineCodeNode("inline code"));
        }

        return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = messageId,
            Contents = [new RichTextContent(text, [paragraph])],
            FinishReason = ChatFinishReason.Stop,
        };
    }

    private static ChatResponseUpdate ActivityUpdate(string id, string text, bool isComplete)
        => new()
        {
            Role = ChatRole.Assistant,
            MessageId = "activity",
            Contents = [new VerificationActivityContent(id, text, isComplete)],
        };

    private static ChatResponseUpdate TextUpdate(string messageId, string text)
        => new()
        {
            Role = ChatRole.Assistant,
            MessageId = messageId,
            Contents = [new TextContent(text)],
            FinishReason = ChatFinishReason.Stop,
        };
}

public sealed class VerificationStateContent(string value, bool isPredictive) : AIContent
{
    public string Value { get; } = value;

    public bool IsPredictive { get; } = isPredictive;
}

public sealed class VerificationActivityContent(
    string activityId,
    string text,
    bool isComplete) : AIContent
{
    public string ActivityId { get; } = activityId;

    public string Text { get; } = text;

    public bool IsComplete { get; } = isComplete;
}

public sealed class VerificationActivityBlock : ActivityContentBlock
{
    public string ActivityId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class VerificationActivityHandler : ActivityHandler<VerificationActivityBlock>
{
    protected override bool TryCreateBlock(
        BlockMappingContext context,
        VerificationActivityBlock state)
        => TryApply(context, state, out _);

    protected override bool TryUpdateBlock(
        BlockMappingContext context,
        VerificationActivityBlock state,
        out bool isCompleted)
        => TryApply(context, state, out isCompleted);

    private static bool TryApply(
        BlockMappingContext context,
        VerificationActivityBlock state,
        out bool isCompleted)
    {
        foreach (var content in context.UnhandledContents)
        {
            if (content is VerificationActivityContent activity &&
                (state.ActivityId.Length == 0 || state.ActivityId == activity.ActivityId))
            {
                context.MarkHandled(content);
                state.ActivityId = activity.ActivityId;
                state.ActivityType = "verification";
                state.Content = JsonSerializer.SerializeToElement(activity.Text);
                state.Text = activity.Text;
                isCompleted = activity.IsComplete;
                return true;
            }
        }

        isCompleted = false;
        return false;
    }
}

public sealed class PackageFeatureState
{
    public string Value { get; set; } = "Standard shipping";
}
