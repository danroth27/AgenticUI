// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using AGUI.Server;
using Microsoft.Extensions.AI;

namespace AgenticUI.AgentServer.Scenarios.SharedState;

/// <summary>
/// Makes the client's copy of the shared state part of the agent's input.
/// <para>
/// AG-UI streams state from the agent to the UI as <c>STATE_SNAPSHOT</c> events, and the client
/// sends its current state back on the next run in <see cref="AGUI.Abstractions.RunAgentInput.State"/>.
/// This wrapper recovers that state from the request and describes it to the model, so a recipe the
/// user edited by hand in the UI is the version the agent builds on.
/// </para>
/// </summary>
internal sealed class SharedStateChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(WithClientState(messages, options), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetStreamingResponseAsync(WithClientState(messages, options), options, cancellationToken);

    private static IEnumerable<ChatMessage> WithClientState(
        IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        if (options is null || !options.TryGetRunAgentInput(out var input))
        {
            return messages;
        }

        if (input.State is not JsonElement state || !TryDescribeRecipe(state, out var description))
        {
            return messages;
        }

        // Appended last so it takes precedence over anything earlier in the conversation.
        return [.. messages, new ChatMessage(ChatRole.System, description)];
    }

    private static bool TryDescribeRecipe(JsonElement state, out string description)
    {
        description = string.Empty;

        if (state.ValueKind != JsonValueKind.Object
            || !state.TryGetProperty("recipe", out var recipe)
            || recipe.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // An empty recipe means the user has not started one; there is nothing to preserve.
        var title = recipe.TryGetProperty("title", out var t) ? t.GetString() : null;
        var hasIngredients = recipe.TryGetProperty("ingredients", out var ing)
            && ing.ValueKind == JsonValueKind.Array
            && ing.GetArrayLength() > 0;

        if (string.IsNullOrWhiteSpace(title) && !hasIngredients)
        {
            return false;
        }

        description = $"""
            This is the recipe currently shown in the user's editor. The user may have edited it by
            hand since your last reply, so treat it as the authoritative current state — it takes
            precedence over any earlier recipe in this conversation.

            {recipe.GetRawText()}

            When you call generate_recipe, start from this recipe and keep the user's edits unless
            they ask you to change them.
            """;

        return true;
    }
}
