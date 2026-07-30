// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace AgenticUI.AgentServer.Scenarios.Reasoning;

/// <summary>
/// Opts every request in to OpenAI Responses API reasoning summaries.
/// </summary>
/// <remarks>
/// <para>
/// Reasoning summaries are opt-in. Without them the model still spends reasoning tokens, but returns
/// no reasoning text, so there is nothing for the UI to show.
/// </para>
/// <para>
/// The opt-in has to live on the <see cref="IChatClient"/> rather than on the agent's
/// <see cref="ChatOptions"/>: an AG-UI run supplies its own <see cref="ChatOptions"/> (tools,
/// context), which replaces the agent's and silently drops the
/// <see cref="ChatOptions.RawRepresentationFactory"/>. Decorating the client guarantees the option
/// is applied on every call, whatever the caller passes.
/// </para>
/// </remarks>
public sealed class ReasoningOptionsChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    private static ChatOptions Apply(ChatOptions? options)
    {
        var configured = options?.Clone() ?? new ChatOptions();
        configured.RawRepresentationFactory = _ => new CreateResponseOptions
        {
            ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningSummaryVerbosity = ResponseReasoningSummaryVerbosity.Detailed
            }
        };
        return configured;
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetStreamingResponseAsync(messages, Apply(options), cancellationToken);

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(messages, Apply(options), cancellationToken);
}
