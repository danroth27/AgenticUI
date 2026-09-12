var builder = DistributedApplication.CreateBuilder(args);

// Microsoft Foundry configuration. The account already exists in Azure; provide its resource name
// and resource group via AppHost user-secrets.
// The deployment names default to gpt-5-mini and can be overridden with Parameters:foundry-model
// and Parameters:foundry-reasoning-model. Note that the AddParameter overload taking a value treats
// it as a constant rather than a default, so read configuration explicitly to honor the override.
var foundryName = builder.AddParameter("foundry-name");
var foundryResourceGroup = builder.AddParameter("foundry-resource-group");
var foundryModel = builder.AddParameter("foundry-model",
    value: builder.Configuration["Parameters:foundry-model"] ?? "gpt-5-mini");
var foundryReasoningModel = builder.AddParameter("foundry-reasoning-model",
    value: builder.Configuration["Parameters:foundry-reasoning-model"] ?? "gpt-5-mini");

var foundry = builder.AddFoundry("foundry")
    .AsExisting(foundryName, foundryResourceGroup);

// The AG-UI agent server: hosts one AG-UI endpoint per demo scenario (MAF + AG-UI C# SDK).
var agentServer = builder.AddProject<Projects.AgenticUI_AgentServer>("agentserver")
    .WithReference(foundry)
    .WithEnvironment("FOUNDRY_MODEL", foundryModel)
    .WithEnvironment("FOUNDRY_REASONING_MODEL", foundryReasoningModel);

// The Blazor front end: consumes the AG-UI endpoints via the Blazor AI components.
builder.AddProject<Projects.AgenticUI_Web>("web")
    .WithReference(agentServer)
    .WaitFor(agentServer);

builder.Build().Run();
