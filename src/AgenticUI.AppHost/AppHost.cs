var builder = DistributedApplication.CreateBuilder(args);

// GitHub Models configuration. The token is a secret parameter; provide it via AppHost user-secrets:
//   dotnet user-secrets set "Parameters:github-token" "$(gh auth token)"
// The model has a sensible default and can be overridden with Parameters:github-model.
var githubToken = builder.AddParameter("github-token", secret: true);
var githubModel = builder.AddParameter("github-model", value: "openai/gpt-4o-mini");
var githubReasoningModel = builder.AddParameter("github-reasoning-model", value: "deepseek/deepseek-r1");

// The AG-UI agent server: hosts one AG-UI endpoint per demo scenario (MAF + AG-UI C# SDK).
var agentServer = builder.AddProject<Projects.AgenticUI_AgentServer>("agentserver")
    .WithEnvironment("GITHUB_TOKEN", githubToken)
    .WithEnvironment("GITHUB_MODEL", githubModel)
    .WithEnvironment("GITHUB_REASONING_MODEL", githubReasoningModel);

// The Blazor front end: consumes the AG-UI endpoints via the Blazor AI components.
builder.AddProject<Projects.AgenticUI_Web>("web")
    .WithReference(agentServer)
    .WaitFor(agentServer);

builder.Build().Run();
