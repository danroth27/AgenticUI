using AgenticUI.AgentServer;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Make the tool argument/result types available to System.Text.Json.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Add(AgentServerSerializerContext.Default));

// Register AG-UI server support (augments the ASP.NET Core JSON options with the AG-UI event types).
builder.Services.AddAGUIServer();

var app = builder.Build();

app.MapDefaultEndpoints();

// Build the per-scenario agents backed by the free GitHub Models endpoint.
var githubModels = GitHubModels.ReadOptions(app.Configuration);
var chatClient = GitHubModels.CreateChatClient(githubModels);
var reasoningChatClient = GitHubModels.CreateChatClient(githubModels, githubModels.ReasoningModel);
var jsonOptions = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
var agents = new AgentCatalog(chatClient, reasoningChatClient, jsonOptions);

// Map one AG-UI endpoint per scenario. Each is an HTTP POST that streams AG-UI events (SSE).
app.MapAGUIServer("/agentic_chat", agents.CreateAgenticChat());
app.MapAGUIServer("/backend_tool_rendering", agents.CreateBackendToolRendering());
app.MapAGUIServer("/human_in_the_loop", agents.CreateHumanInTheLoop());
app.MapAGUIServer("/tool_based_generative_ui", agents.CreateToolBasedGenerativeUI());
app.MapAGUIServer("/agentic_generative_ui", agents.CreateAgenticGenerativeUI());
app.MapAGUIServer("/shared_state", agents.CreateSharedState());
app.MapAGUIServer("/predictive_state_updates", agents.CreatePredictiveStateUpdates());
app.MapAGUIServer("/reasoning", agents.CreateReasoning());
app.MapAGUIServer("/workflow", agents.CreateWorkflow());
app.MapAGUIServer("/selective_approval", agents.CreateSelectiveApproval());

app.MapGet("/", () => Results.Ok(new
{
    service = "AgenticUI AG-UI agent server",
    model = githubModels.Model,
    reasoningModel = githubModels.ReasoningModel,
    endpoints = new[]
    {
        "/agentic_chat",
        "/backend_tool_rendering",
        "/human_in_the_loop",
        "/tool_based_generative_ui",
        "/agentic_generative_ui",
        "/shared_state",
        "/predictive_state_updates",
        "/reasoning",
        "/workflow",
        "/selective_approval"
    }
}));

app.Run();
