var builder = DistributedApplication.CreateBuilder(args);

// Microsoft Foundry configuration. The endpoint and API key are secret parameters; provide them via
// AppHost user-secrets:
//   dotnet user-secrets set "Parameters:foundry-endpoint" "https://<resource>.cognitiveservices.azure.com/openai/v1"
//   dotnet user-secrets set "Parameters:foundry-api-key" "<key>"
// The deployment names have sensible defaults and can be overridden with Parameters:foundry-model
// and Parameters:foundry-reasoning-model.
var foundryEndpoint = builder.AddParameter("foundry-endpoint", secret: true);
var foundryApiKey = builder.AddParameter("foundry-api-key", secret: true);
var foundryModel = builder.AddParameter("foundry-model", value: "gpt-4o-mini");
var foundryReasoningModel = builder.AddParameter("foundry-reasoning-model", value: "gpt-5-mini");

// The AG-UI agent server: hosts one AG-UI endpoint per demo scenario (MAF + AG-UI C# SDK).
var agentServer = builder.AddProject<Projects.AgenticUI_AgentServer>("agentserver")
    .WithEnvironment("FOUNDRY_ENDPOINT", foundryEndpoint)
    .WithEnvironment("FOUNDRY_API_KEY", foundryApiKey)
    .WithEnvironment("FOUNDRY_MODEL", foundryModel)
    .WithEnvironment("FOUNDRY_REASONING_MODEL", foundryReasoningModel);

// The Blazor front end: consumes the AG-UI endpoints via the Blazor AI components.
builder.AddProject<Projects.AgenticUI_Web>("web")
    .WithReference(agentServer)
    .WaitFor(agentServer);

builder.Build().Run();
