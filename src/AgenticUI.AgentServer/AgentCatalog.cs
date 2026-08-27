// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using AgenticUI.AgentServer.Scenarios.AgenticGenerativeUi;
using AgenticUI.AgentServer.Scenarios.BackendToolRendering;
using AgenticUI.AgentServer.Scenarios.SharedState;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace AgenticUI.AgentServer;

/// <summary>
/// Builds the <see cref="AIAgent"/> instances for each AG-UI demo scenario. Each agent is mapped to
/// its own AG-UI endpoint in <c>Program.cs</c> via <c>MapAGUIServer</c>.
/// </summary>
public sealed class AgentCatalog(ChatClient chatClient, IChatClient reasoningChatClient)
{
    private readonly ChatClient _chatClient = chatClient;
    private readonly IChatClient _reasoningChatClient = reasoningChatClient;

    /// <summary>Basic streaming chat — text in, streamed text out.</summary>
    public AIAgent CreateAgenticChat() =>
        this._chatClient.AsAIAgent(
            name: "AgenticChat",
            description: "A simple streaming chat agent.",
            instructions: "You are a helpful, friendly assistant. Keep answers concise.");

    /// <summary>Backend tool rendering — the server executes a <c>get_weather</c> tool.</summary>
    public AIAgent CreateBackendToolRendering() =>
        this._chatClient.AsAIAgent(
            name: "BackendToolRenderer",
            description: "An agent that calls a backend weather tool.",
            instructions: "You are a helpful assistant. Use the get_weather tool when asked about the weather.",
            tools: [AIFunctionFactory.Create(
                GetWeather,
                name: "get_weather",
                description: "Get the weather for a given location.",
                AgentServerSerializerContext.Default.Options)]);

    /// <summary>
    /// Human-in-the-loop. The agent exposes a tool wrapped in <see cref="ApprovalRequiredAIFunction"/>,
    /// so calling it produces an AG-UI interrupt. The AG-UI client surfaces that as an approval request
    /// which the Blazor AI components render with Approve/Reject buttons before the tool runs.
    /// </summary>
    public AIAgent CreateHumanInTheLoop()
    {
        AITool bookMeeting = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            BookMeeting,
            name: "book_meeting",
            description: "Book a meeting on the user's calendar.",
            AgentServerSerializerContext.Default.Options));

        return this._chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "HumanInTheLoopAgent",
            Description = "An assistant that books meetings, but asks for approval first.",
            ChatOptions = new ChatOptions
            {
                Instructions = """
                    You are a helpful scheduling assistant.

                    - When the user asks to schedule something, call the book_meeting tool immediately.
                      Never ask for confirmation in text — the app collects the user's approval for you.
                    - If a book_meeting call comes back rejected, the user declined it. Acknowledge that
                      in one short sentence and stop. Do NOT call book_meeting again for the same
                      request, and do not propose an alternative unless the user asks for one.
                    """,
                Tools = [bookMeeting]
            }
        });
    }

    /// <summary>Frontend tools — the model calls a client-side action that runs in the browser.</summary>
    public AIAgent CreateFrontendTools() =>
        this._chatClient.AsAIAgent(
            name: "FrontendToolsAgent",
            description: "An agent that calls client-side tools.",
            instructions: "Use the tools the client provides when the user asks you to change the page.");

    /// <summary>Tool-based generative UI — the model calls client tools that render bespoke UI.</summary>
    public AIAgent CreateToolBasedGenerativeUI() =>
        this._chatClient.AsAIAgent(
            name: "ToolBasedGenerativeUIAgent",
            description: "An agent that calls client tools which render generative UI.",
            instructions: """
                You are a Japanese haiku assistant.
                For every haiku request, call generate_haiku with exactly three Japanese lines, exactly
                three English translation lines and an attractive CSS gradient.
                Do not print the haiku as ordinary chat text before calling the tool.
                After the tool returns, acknowledge completion in one short sentence without repeating
                the haiku or its arguments.
                """);

    /// <summary>Agentic generative UI — plan/progress rendered live from state snapshots and deltas.</summary>
    public AIAgent CreateAgenticGenerativeUI()
    {
        var baseAgent = this._chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "AgenticGenerativeUIAgent",
            Description = "An agent that plans work and streams live plan progress.",
            ChatOptions = new ChatOptions
            {
                Instructions = """
                    When planning use tools only, without any other messages.
                    IMPORTANT:
                    - Use the `create_plan` tool to set the initial state of the steps
                    - Use the `update_plan_step` tool to update the status of each step
                    - Do NOT repeat the plan or summarise it in a message
                    - Do NOT confirm the creation or updates in a message
                    - Do NOT ask the user for additional information or next steps
                    - Do NOT leave a plan hanging, always complete the plan via `update_plan_step` if one is ongoing.
                    - Continue calling update_plan_step until all steps are marked as completed.

                    Only one plan can be active at a time, so do not call the `create_plan` tool
                    again until all the steps in current plan are completed.
                    """,
                Tools = [
                    AIFunctionFactory.Create(
                        AgenticPlanningTools.CreatePlan,
                        name: "create_plan",
                        description: "Create a plan with multiple steps.",
                        AgentServerSerializerContext.Default.Options),
                    AIFunctionFactory.Create(
                        AgenticPlanningTools.UpdatePlanStepAsync,
                        name: "update_plan_step",
                        description: "Update a step in the plan with new description or status.",
                        AgentServerSerializerContext.Default.Options)
                ],
                AllowMultipleToolCalls = false
            }
        });

        // The create_plan / update_plan_step tool results are turned into STATE_SNAPSHOT / STATE_DELTA
        // events declaratively via AGUIStreamOptions in Program.cs — no custom agent required.
        return baseAgent;
    }

    /// <summary>Shared state — structured recipe kept in sync between agent and client.</summary>
    public AIAgent CreateSharedState()
    {
        AITool generateRecipe = AIFunctionFactory.Create(
            RecipeTools.GenerateRecipe,
            name: "generate_recipe",
            description: "Generate or update the shared recipe and display it to the user.",
            AgentServerSerializerContext.Default.Options);

        var agent = this._chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "SharedStateAgent",
            Description = "An agent that keeps a structured recipe in sync with the client.",
            ChatOptions = new ChatOptions
            {
                Instructions = """
                    You are a helpful recipe assistant that maintains a shared recipe state with the user.

                    IMPORTANT:
                    - When the user asks you to create, change, or improve a recipe, call the `generate_recipe`
                      tool with a COMPLETE recipe: a title, skill_level, cooking_time, special_preferences, the
                      full list of ingredients (each with an icon, name and amount) and the step-by-step
                      instructions.
                    - Use Beginner, Intermediate, or Advanced for skill_level.
                    - Use 15 min, 30 min, 45 min, 1 hr, 1.5 hr, or 2 hr for cooking_time.
                    - Always include every ingredient the recipe needs, keeping any the user already added.
                    - Keep the ingredient list simple so it stays readable in a compact card:
                      `name` is just the ingredient (e.g. "Bread flour", "Olive oil") with no parenthetical
                      notes or substitutions, and `amount` is a short quantity of at most about 20 characters
                      (e.g. "3 1/2 cups", "2 tbsp", "1 clove"). Put substitutions, temperatures, prep notes
                      and anything optional in the instructions instead — never in the name or amount.
                    - When the user only asks a question about the recipe, answer in plain text and do NOT call the tool.
                    - After calling the tool, reply with ONE short sentence in plain text. The recipe card
                      already shows the details, so never repeat them and never use markdown.
                    """,
                Tools = [generateRecipe],
            }
        });

        return new RecipeStateAgent(agent);
    }

    /// <summary>Reasoning — surfaces a reasoning model's reasoning summary separately from its answer.</summary>
    public AIAgent CreateReasoning() =>
        this._reasoningChatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "ReasoningAgent",
            Description = "A reasoning model that returns a reasoning summary.",
            ChatOptions = new ChatOptions
            {
                // Keep the answer formatting simple. Avoid asking for brevity — instructions like
                // "answer in one or two sentences, no step-by-step recap" measurably suppress the
                // model's reasoning summary, leaving the reasoning panel empty.
                Instructions = "Write your answer in plain prose. Do not use markdown, LaTeX, math "
                    + "notation, or bullet points.",
                // Reasoning summaries are opt-in. `ChatOptions.Reasoning` is the provider-neutral
                // switch: the OpenAI client maps `Full` to the Responses API's detailed reasoning
                // summary. MAF merges these agent-level options into every run, including runs
                // that arrive over AG-UI.
                Reasoning = new ReasoningOptions { Output = ReasoningOutput.Full }
            }
        });

    private static WeatherInfo GetWeather(string location) => new()
    {
        Temperature = 20,
        Conditions = "sunny",
        Humidity = 50,
        WindSpeed = 10,
        FeelsLike = 25
    };

    private static string BookMeeting(string title, string time) => $"Booked '{title}' for {time}.";

}
