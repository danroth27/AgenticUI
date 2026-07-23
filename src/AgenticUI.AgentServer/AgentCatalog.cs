// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using AgenticUI.AgentServer.Scenarios.AgenticGenerativeUi;
using AgenticUI.AgentServer.Scenarios.BackendToolRendering;
using AgenticUI.AgentServer.Scenarios.PredictiveStateUpdates;
using AgenticUI.AgentServer.Scenarios.Reasoning;
using AgenticUI.AgentServer.Scenarios.SharedState;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace AgenticUI.AgentServer;

/// <summary>
/// Builds the <see cref="AIAgent"/> instances for each AG-UI demo scenario. Each agent is mapped to
/// its own AG-UI endpoint in <c>Program.cs</c> via <c>MapAGUIServer</c>.
/// </summary>
public sealed class AgentCatalog(ChatClient chatClient, ChatClient reasoningChatClient, JsonSerializerOptions jsonOptions)
{
    private readonly ChatClient _chatClient = chatClient;
    private readonly ChatClient _reasoningChatClient = reasoningChatClient;
    private readonly JsonSerializerOptions _jsonOptions = jsonOptions;

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
                Instructions = "You are a helpful scheduling assistant. When the user asks to schedule " +
                               "something, call the book_meeting tool. The tool requires the user's approval.",
                Tools = [bookMeeting]
            }
        });
    }

    /// <summary>Tool-based generative UI — the model calls client tools that render bespoke UI.</summary>
    public AIAgent CreateToolBasedGenerativeUI() =>
        this._chatClient.AsAIAgent(
            name: "ToolBasedGenerativeUIAgent",
            description: "An agent that calls client tools which render generative UI.",
            instructions: "You are a helpful assistant. Use the tools the client provides to render rich UI " +
                          "instead of describing things in plain text when appropriate.");

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

        return new AgenticUIAgent(baseAgent, this._jsonOptions);
    }

    /// <summary>Shared state — structured recipe kept in sync between agent and client.</summary>
    public AIAgent CreateSharedState()
    {
        var baseAgent = this._chatClient.AsAIAgent(
            name: "SharedStateAgent",
            description: "An agent that keeps a structured recipe in sync with the client.");

        return new SharedStateAgent(baseAgent, this._jsonOptions);
    }

    /// <summary>Predictive state updates — a document streamed progressively as it is written.</summary>
    public AIAgent CreatePredictiveStateUpdates()
    {
        var baseAgent = this._chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "PredictiveStateUpdatesAgent",
            Description = "A document editor that streams the document as it writes.",
            ChatOptions = new ChatOptions
            {
                Instructions = """
                    You are a document editor assistant. When asked to write or edit content:

                    IMPORTANT:
                    - Use the `write_document` tool with the full document text in Markdown format
                    - Format the document extensively so it's easy to read
                    - You can use all kinds of markdown (headings, lists, bold, etc.)
                    - However, do NOT use italic or strike-through formatting
                    - You MUST write the full document, even when changing only a few words
                    - When making edits to the document, try to make them minimal - do not change every word
                    - Keep stories SHORT!

                    After the user confirms the changes, provide a brief summary of what you wrote.
                    """,
                Tools = [
                    AIFunctionFactory.Create(
                        WriteDocument,
                        name: "write_document",
                        description: "Write a document. Use markdown formatting to format the document.",
                        AgentServerSerializerContext.Default.Options)
                ]
            }
        });

        return new PredictiveStateUpdatesAgent(baseAgent, this._jsonOptions);
    }

    /// <summary>Reasoning — surfaces a reasoning model's chain of thought separately from its answer.</summary>
    public AIAgent CreateReasoning()
    {
        var baseAgent = this._reasoningChatClient.AsAIAgent(
            name: "ReasoningAgent",
            description: "A reasoning model that shows its thinking.",
            instructions: "Think step by step, then give a concise final answer.");

        return new ReasoningAgent(baseAgent);
    }

    /// <summary>[Test] A sequential workflow (researcher -> reporter) exposed as an AG-UI agent.</summary>
    public AIAgent CreateWorkflow()
    {
        AIAgent researcher = this._chatClient.AsAIAgent(
            name: "researcher",
            instructions: "Research the user's topic and write a short, factual brief in under 80 words.");
        AIAgent reporter = this._chatClient.AsAIAgent(
            name: "reporter",
            instructions: "Summarize the researcher's brief into a single clear sentence.");

        return AgentWorkflowBuilder
            .BuildSequential(researcher, reporter)
            .AsAIAgent(name: "ResearchWorkflow");
    }

    /// <summary>[Test] Selective approval: one tool requires approval, another does not.</summary>
    public AIAgent CreateSelectiveApproval()
    {
        AITool getBalance = AIFunctionFactory.Create(
            GetAccountBalance, name: "get_account_balance",
            description: "Get the current account balance. Does not require approval.");

        AITool transfer = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            TransferFunds, name: "transfer_funds",
            description: "Transfer money to another account."));

        return this._chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "SelectiveApprovalAgent",
            ChatOptions = new ChatOptions
            {
                Instructions = "You are a banking assistant. Use get_account_balance to check balances " +
                               "and transfer_funds to move money. Call the tools directly.",
                Tools = [getBalance, transfer]
            }
        });
    }

    private static WeatherInfo GetWeather(string location) => new()
    {
        Temperature = 20,
        Conditions = "sunny",
        Humidity = 50,
        WindSpeed = 10,
        FeelsLike = 25
    };

    private static string BookMeeting(string title, string time) => $"Booked '{title}' for {time}.";

    private static string GetAccountBalance() => "Your current balance is $1,250.00.";

    private static string TransferFunds(string toAccount, decimal amount) =>
        $"Transferred {amount:C} to account {toAccount}.";

    private static string WriteDocument(string document) => "Document written successfully";
}
