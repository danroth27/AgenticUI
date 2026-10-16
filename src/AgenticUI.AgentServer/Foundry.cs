using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace AgenticUI.AgentServer;

/// <summary>
/// Configuration for the <see href="https://learn.microsoft.com/azure/ai-foundry/">Microsoft Foundry</see>
/// resource that backs every scenario. The Azure OpenAI client authenticates to the resource with
/// Microsoft Entra ID.
/// </summary>
public sealed class FoundryOptions
{
    public const string DefaultModel = "gpt-5-mini";
    public const string DefaultReasoningModel = "gpt-5-mini";

    /// <summary>The Foundry resource endpoint.</summary>
    public string? Endpoint { get; set; }

    /// <summary>The deployment name used by most scenarios, e.g. <c>gpt-5-mini</c>.</summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>A reasoning-capable deployment used by the reasoning scenario, e.g. <c>gpt-5-mini</c>.</summary>
    public string ReasoningModel { get; set; } = DefaultReasoningModel;
}

/// <summary>Helpers for resolving Foundry configuration and building chat clients.</summary>
public static class Foundry
{
    /// <summary>
    /// Reads Foundry settings from configuration. Aspire provides <c>FOUNDRY_URI</c> when the
    /// AppHost references the Foundry resource. The <c>Foundry</c> configuration section remains
    /// available for running the agent server without the AppHost.
    /// </summary>
    public static FoundryOptions ReadOptions(IConfiguration configuration)
    {
        var options = new FoundryOptions();
        configuration.GetSection("Foundry").Bind(options);

        options.Endpoint = configuration["FOUNDRY_URI"] ?? options.Endpoint;
        options.Model = configuration["FOUNDRY_MODEL"] ?? options.Model;
        options.ReasoningModel = configuration["FOUNDRY_REASONING_MODEL"] ?? options.ReasoningModel;

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException(
                "No Microsoft Foundry endpoint configured. Reference the Foundry resource from the AppHost " +
                "or set the Foundry:Endpoint configuration value.");
        }

        return options;
    }

    private static AzureOpenAIClient CreateClient(FoundryOptions options) =>
        new(new Uri(options.Endpoint!, UriKind.Absolute), new DefaultAzureCredential());

    /// <summary>Creates a chat-completions <see cref="ChatClient"/> for a Foundry deployment.</summary>
    /// <param name="options">The Foundry configuration.</param>
    /// <param name="model">The deployment name; defaults to <see cref="FoundryOptions.Model"/>.</param>
    public static ChatClient CreateChatClient(FoundryOptions options, string? model = null) =>
        CreateClient(options).GetChatClient(model ?? options.Model);

    /// <summary>
    /// Whether a deployment accepts the <c>reasoning_effort</c> request option. Reasoning models
    /// (the o-series and GPT-5, apart from the non-reasoning <c>gpt-5-chat</c>) accept it; other
    /// models such as gpt-4o-mini reject the request with HTTP 400. This goes by the deployment name,
    /// so it assumes deployments are named after their model, as the defaults here are.
    /// </summary>
    public static bool SupportsReasoningEffort(string deployment)
    {
        var name = deployment.ToLowerInvariant();
        return (name.StartsWith("gpt-5", StringComparison.Ordinal) && !name.Contains("chat", StringComparison.Ordinal)) ||
            name.StartsWith("o1", StringComparison.Ordinal) ||
            name.StartsWith("o3", StringComparison.Ordinal) ||
            name.StartsWith("o4", StringComparison.Ordinal);
    }

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
