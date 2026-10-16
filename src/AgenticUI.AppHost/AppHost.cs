var builder = DistributedApplication.CreateBuilder(args);

// Microsoft Foundry configuration. The app consumes an existing Foundry inference endpoint but
// doesn't manage the Foundry account or its child resources.
// The deployment names default to gpt-5-mini and can be overridden with Parameters:foundry-model
// and Parameters:foundry-reasoning-model. Note that the AddParameter overload taking a value treats
// it as a constant rather than a default, so read configuration explicitly to honor the override.
var foundryEndpoint = builder.AddParameter("foundry-endpoint");
var foundryModel = builder.AddParameter("foundry-model",
    value: builder.Configuration["Parameters:foundry-model"] ?? "gpt-5-mini");
var foundryReasoningModel = builder.AddParameter("foundry-reasoning-model",
    value: builder.Configuration["Parameters:foundry-reasoning-model"] ?? "gpt-5-mini");

var foundry = builder.AddExternalService("foundry", foundryEndpoint);

// Deepgram API key for the voice scenario. Only the web project sees it: the browser never gets the
// key, just short-lived JWTs minted by the web project's /api/deepgram/token endpoint. The key is
// optional (read from configuration like the model names above) so the app still starts without it;
// the voice controls simply stay hidden. A DEEPGRAM_API_KEY environment variable also works.
var deepgramApiKey = builder.AddParameter("deepgram-api-key",
    value: builder.Configuration["Parameters:deepgram-api-key"]
        ?? builder.Configuration["DEEPGRAM_API_KEY"]
        ?? string.Empty,
    secret: true);

// The AG-UI agent server: hosts one AG-UI endpoint per demo scenario (MAF + AG-UI C# SDK).
var agentServer = builder.AddProject<Projects.AgenticUI_AgentServer>("agentserver")
    .WithEnvironment("FOUNDRY_URI", foundry)
    .WithEnvironment("FOUNDRY_MODEL", foundryModel)
    .WithEnvironment("FOUNDRY_REASONING_MODEL", foundryReasoningModel);

// The Blazor front end: consumes the AG-UI endpoints via the Blazor AI components.
builder.AddProject<Projects.AgenticUI_Web>("web")
    .WithEnvironment("DEEPGRAM_API_KEY", deepgramApiKey)
    .WithReference(agentServer)
    .WaitFor(agentServer);

builder.Build().Run();
