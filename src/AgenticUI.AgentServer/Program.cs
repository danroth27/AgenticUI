using AgenticUI.AgentServer;
using AgenticUI.AgentServer.Scenarios.PredictiveStateUpdates;
using AGUI.Server;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
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

// Build the per-scenario agents backed by Microsoft Foundry.
var foundry = Foundry.ReadOptions(app.Configuration);
var chatClient = Foundry.CreateChatClient(foundry);
var reasoningChatClient = Foundry.CreateReasoningChatClient(foundry);
var jsonOptions = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
var agents = new AgentCatalog(chatClient, reasoningChatClient);

// Map one AG-UI endpoint per scenario. Each is an HTTP POST that streams AG-UI events (SSE).
app.MapAGUIServer("/agentic_chat", agents.CreateAgenticChat());
app.MapAGUIServer("/backend_tool_rendering", agents.CreateBackendToolRendering());
app.MapAGUIServer("/human_in_the_loop", agents.CreateHumanInTheLoop());
app.MapAGUIServer("/frontend_tools", agents.CreateFrontendTools());
app.MapAGUIServer("/tool_based_generative_ui", agents.CreateToolBasedGenerativeUI());
app.MapAGUIServer("/agentic_generative_ui", agents.CreateAgenticGenerativeUI())
    .WithMetadata(new AGUIStreamOptions()
        .MapResultAsStateSnapshot("create_plan")   // full plan -> STATE_SNAPSHOT
        .MapResultAsStateDelta("update_plan_step")); // JSON Patch -> STATE_DELTA
app.MapAGUIServer("/shared_state", agents.CreateSharedState())
    .WithMetadata(new AGUIStreamOptions().MapResultAsStateSnapshot("generate_recipe"));
app.MapAGUIServer("/reasoning", agents.CreateReasoning());
app.MapPredictiveStateEndpoint(
    "/predictive_state_updates",
    chatClient.AsIChatClient(),
    jsonOptions);

app.MapGet("/", () => Results.Ok(new
{
    service = "AgenticUI AG-UI agent server",
    model = foundry.Model,
    reasoningModel = foundry.ReasoningModel,
    endpoints = new[]
    {
        "/agentic_chat",
        "/backend_tool_rendering",
        "/human_in_the_loop",
        "/frontend_tools",
        "/tool_based_generative_ui",
        "/agentic_generative_ui",
        "/shared_state",
        "/reasoning",
        "/predictive_state_updates"
    }
}));

app.Run();
