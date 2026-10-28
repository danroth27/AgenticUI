using Microsoft.Extensions.AI;

namespace AgenticUI.AgentServer.Tests;

public class FoundryTests
{
    [Theory]
    [InlineData("https://sample.services.ai.azure.com")]
    [InlineData("https://sample.cognitiveservices.azure.com/")]
    public void CreateChatClientUsesFoundryV1Endpoint(string endpoint)
    {
        var options = new FoundryOptions { Endpoint = endpoint, Model = "chat-deployment" };

        using var client = Foundry.CreateChatClient(options).AsIChatClient();
        var metadata = client.GetService<ChatClientMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal(new Uri(new Uri(endpoint), "openai/v1/"), metadata.ProviderUri);
        Assert.Equal(options.Model, metadata.DefaultModelId);
    }

    [Theory]
    [InlineData("https://sample.services.ai.azure.com")]
    [InlineData("https://sample.cognitiveservices.azure.com/")]
    public void CreateReasoningChatClientUsesCompatibleResponsesClient(string endpoint)
    {
        var options = new FoundryOptions { Endpoint = endpoint, ReasoningModel = "reasoning-deployment" };

        using var client = Foundry.CreateReasoningChatClient(options);
        var metadata = client.GetService<ChatClientMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal(new Uri(new Uri(endpoint), "openai/v1/"), metadata.ProviderUri);
        Assert.Equal(options.ReasoningModel, metadata.DefaultModelId);
    }
}
