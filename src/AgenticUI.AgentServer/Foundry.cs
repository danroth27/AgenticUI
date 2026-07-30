// Copyright (c) Microsoft. All rights reserved.

using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace AgenticUI.AgentServer;

/// <summary>
/// Configuration for the <see href="https://learn.microsoft.com/azure/ai-foundry/">Microsoft Foundry</see>
/// resource that backs every scenario. Foundry exposes an OpenAI-compatible endpoint at
/// <c>{resource}/openai/v1</c>, so the stock <see cref="OpenAIClient"/> works against it directly —
/// the API key is sent as a bearer token.
/// </summary>
public sealed class FoundryOptions
{
    public const string DefaultModel = "gpt-4o-mini";
    public const string DefaultReasoningModel = "gpt-5-mini";

    /// <summary>The Foundry OpenAI-compatible endpoint, e.g. <c>https://my-resource.cognitiveservices.azure.com/openai/v1</c>.</summary>
    public string? Endpoint { get; set; }

    /// <summary>The Foundry API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The deployment name used by most scenarios, e.g. <c>gpt-4o-mini</c>.</summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>A reasoning-capable deployment used by the reasoning scenario, e.g. <c>gpt-5-mini</c>.</summary>
    public string ReasoningModel { get; set; } = DefaultReasoningModel;
}

/// <summary>Helpers for resolving Foundry configuration and building chat clients.</summary>
public static class Foundry
{
    /// <summary>
    /// Reads Foundry settings from configuration. Recognizes <c>FOUNDRY_ENDPOINT</c>,
    /// <c>FOUNDRY_API_KEY</c>, <c>FOUNDRY_MODEL</c>, and <c>FOUNDRY_REASONING_MODEL</c>
    /// (or the <c>Foundry</c> configuration section).
    /// </summary>
    public static FoundryOptions ReadOptions(IConfiguration configuration)
    {
        var options = new FoundryOptions();
        configuration.GetSection("Foundry").Bind(options);

        options.Endpoint = configuration["FOUNDRY_ENDPOINT"] ?? options.Endpoint;
        options.ApiKey = configuration["FOUNDRY_API_KEY"] ?? options.ApiKey;
        options.Model = configuration["FOUNDRY_MODEL"] ?? options.Model;
        options.ReasoningModel = configuration["FOUNDRY_REASONING_MODEL"] ?? options.ReasoningModel;

        if (string.IsNullOrWhiteSpace(options.Endpoint) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "No Microsoft Foundry endpoint/key configured. Set FOUNDRY_ENDPOINT (for example " +
                "https://my-resource.cognitiveservices.azure.com/openai/v1) and FOUNDRY_API_KEY, or the " +
                "Foundry:Endpoint and Foundry:ApiKey configuration values.");
        }

        return options;
    }

    private static OpenAIClient CreateClient(FoundryOptions options) =>
        new(new ApiKeyCredential(options.ApiKey!),
            new OpenAIClientOptions { Endpoint = new Uri(options.Endpoint!) });

    /// <summary>Creates a chat-completions <see cref="ChatClient"/> for a Foundry deployment.</summary>
    /// <param name="options">The Foundry configuration.</param>
    /// <param name="model">The deployment name; defaults to <see cref="FoundryOptions.Model"/>.</param>
    public static ChatClient CreateChatClient(FoundryOptions options, string? model = null) =>
        CreateClient(options).GetChatClient(model ?? options.Model);

    /// <summary>
    /// Creates the chat client for the reasoning scenario over the OpenAI <em>Responses</em> API.
    /// Reasoning models only surface their reasoning summaries through the Responses API, and
    /// Microsoft.Extensions.AI maps those summaries to <see cref="TextReasoningContent"/> — which the
    /// MAF AG-UI adapter then emits as <c>REASONING_*</c> events. Chat completions would spend the
    /// same reasoning tokens but return no reasoning text at all.
    /// </summary>
    public static IChatClient CreateReasoningChatClient(FoundryOptions options) =>
        CreateClient(options).GetResponsesClient().AsIChatClient(options.ReasoningModel);
}

