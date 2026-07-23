// Copyright (c) Microsoft. All rights reserved.

using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

namespace AgenticUI.AgentServer;

/// <summary>
/// Creates an OpenAI <see cref="ChatClient"/> pointed at the free
/// <see href="https://github.com/marketplace/models">GitHub Models</see> OpenAI-compatible endpoint.
/// GitHub Models is used so the sample needs no paid Azure/OpenAI resources — just a GitHub token
/// with the <c>models</c> permission (the GitHub CLI token works: <c>gh auth token</c>).
/// </summary>
public sealed class GitHubModelsOptions
{
    public const string DefaultEndpoint = "https://models.github.ai/inference";
    public const string DefaultModel = "openai/gpt-4o-mini";
    public const string DefaultReasoningModel = "deepseek/deepseek-r1";

    /// <summary>GitHub token with the <c>models</c> permission.</summary>
    public string? Token { get; set; }

    /// <summary>The GitHub Models inference endpoint.</summary>
    public string Endpoint { get; set; } = DefaultEndpoint;

    /// <summary>The model id, e.g. <c>openai/gpt-4o-mini</c>.</summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>A reasoning-capable model id used by the reasoning scenario.</summary>
    public string ReasoningModel { get; set; } = DefaultReasoningModel;
}

/// <summary>Helpers for resolving GitHub Models configuration and building a chat client.</summary>
public static class GitHubModels
{
    /// <summary>
    /// Reads GitHub Models settings from configuration. Recognizes <c>GITHUB_TOKEN</c>,
    /// <c>GITHUB_MODEL</c>, and <c>GITHUB_MODELS_ENDPOINT</c> (or the <c>GitHubModels</c> section).
    /// </summary>
    public static GitHubModelsOptions ReadOptions(IConfiguration configuration)
    {
        var options = new GitHubModelsOptions();
        configuration.GetSection("GitHubModels").Bind(options);

        options.Token = configuration["GITHUB_TOKEN"] ?? options.Token;
        options.Model = configuration["GITHUB_MODEL"] ?? options.Model;
        options.ReasoningModel = configuration["GITHUB_REASONING_MODEL"] ?? options.ReasoningModel;
        options.Endpoint = configuration["GITHUB_MODELS_ENDPOINT"] ?? options.Endpoint;

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            throw new InvalidOperationException(
                "No GitHub Models token configured. Set the GITHUB_TOKEN environment variable (or the " +
                "GitHubModels:Token configuration value) to a GitHub token with the 'models' permission. " +
                "The GitHub CLI token works: run 'gh auth token'.");
        }

        return options;
    }

    /// <summary>Creates a <see cref="ChatClient"/> for a GitHub Models deployment.</summary>
    /// <param name="options">The GitHub Models configuration.</param>
    /// <param name="model">The model id to use; defaults to <see cref="GitHubModelsOptions.Model"/>.</param>
    public static ChatClient CreateChatClient(GitHubModelsOptions options, string? model = null)
    {
        var client = new OpenAIClient(
            new ApiKeyCredential(options.Token!),
            new OpenAIClientOptions { Endpoint = new Uri(options.Endpoint) });

        return client.GetChatClient(model ?? options.Model);
    }
}
