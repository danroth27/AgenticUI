# AgenticUI — AG-UI for .NET

A hands-on tour of **AG-UI** (the [Agent User Interaction Protocol](https://docs.ag-ui.com))
in .NET. The backend hosts agents built with the **Microsoft Agent Framework (MAF)** and the
**AG-UI C# SDK**; the frontend is a **Blazor** app that consumes them with the new
in-progress Blazor AI components. [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/)
wires the two together, and everything runs on **[Microsoft Foundry](https://learn.microsoft.com/azure/ai-foundry/)**.

## What it demonstrates

| Scenario | AG-UI feature | Endpoint |
| --- | --- | --- |
| **Agentic chat** | Streaming, multi-turn chat (`TEXT_MESSAGE_*`) | `/agentic_chat` |
| **Backend tools** | Server-side tool calls (`TOOL_CALL_*`) rendered as a custom card | `/backend_tool_rendering` |
| **Frontend tools** | Client-side tool executed in the browser | `/tool_based_generative_ui` |
| **Human in the loop** | Tool approval interrupt → Approve / Reject → resume | `/human_in_the_loop` |
| **Shared state** | Structured state via `STATE_SNAPSHOT` | `/shared_state` |
| **Agentic generative UI** | Live plan via `STATE_SNAPSHOT` + `STATE_DELTA` (JSON Patch) | `/agentic_generative_ui` |
| **Predictive state updates** | Stream proposed state, then accept or reject it | `/predictive_state_updates` |
| **Reasoning** | A reasoning model's reasoning summary via `REASONING_*` events | `/reasoning` |

## Architecture

```mermaid
flowchart LR
    subgraph AppHost["Aspire AppHost"]
        Web["AgenticUI.Web (Blazor)"]
        Server["AgenticUI.AgentServer (ASP.NET Core)"]
    end
    Web -- "AGUIChatClient (IChatClient) over HTTP + SSE" --> Server
    Server -- "MapAGUIServer per scenario" --> Agents["MAF AIAgents"]
    Agents -- "IChatClient" --> GH["Microsoft Foundry"]
    Web -. "UIAgent + Blazor AI components" .-> Web
```

- **`AgenticUI.AgentServer`** — ASP.NET Core app. Uses
  `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` (`AddAGUIServer()` + `MapAGUIServer("/route", agent)`)
  to expose one AG-UI endpoint per scenario. Agents are MAF `AIAgent`s backed by Microsoft Foundry via
  `Microsoft.Agents.AI.OpenAI`.
- **`AgenticUI.Web`** — Blazor Web App (Interactive Server). Each scenario builds a `UIAgent` over an
  `AGUIChatClient` (from the AG-UI C# SDK's `AGUI.Client`), which turns an AG-UI endpoint into a
  standard `IChatClient`. UI is rendered with the Blazor AI components (`ChatPage`, `MessageList`,
  `BlockRenderer`, `UIAgent<TState>`, …).
- **`AgenticUI.AppHost` / `AgenticUI.ServiceDefaults`** — Aspire orchestration and service discovery.

### Released packages used

Everything except the Blazor AI components uses released NuGet packages:

- `Microsoft.Agents.AI`, `Microsoft.Agents.AI.OpenAI` (1.15.0)
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` (1.15.0-preview — the AG-UI hosting glue is still preview)
- `AGUI.Client`, `AGUI.Abstractions`, `AGUI.Formatting`, `AGUI.Server` (0.0.5 — the AG-UI C# SDK)
- `.NET Aspire` (13.4)

### The one exception: Blazor AI components

The Blazor AI components (`Microsoft.AspNetCore.Components.AI`) are **in progress** in
the cumulative `javiercn-components-ai-09-predictive-state` branch in dotnet/aspnetcore and not yet
published to NuGet. To keep this sample **standalone**, a local copy of their source is checked in under
[`src/BlazorAIComponents/`](src/BlazorAIComponents/Microsoft.AspNetCore.Components.AI/NOTICE.md)
(MIT-licensed, with
provenance). The assembly and namespace match the upstream package, so swapping to the official
NuGet package later is a one-line change. Refresh the snapshot with
[`eng/sync-components-ai.ps1`](eng/sync-components-ai.ps1).

## Running it

### Prerequisites

- A compatible .NET SDK for the repository's `net10.0` projects
- [.NET Aspire CLI](https://learn.microsoft.com/dotnet/aspire/) (or just `dotnet run` the AppHost)
- A **[Microsoft Foundry](https://learn.microsoft.com/azure/ai-foundry/) resource** with a
  `gpt-5-mini` deployment (used for both the general chat and reasoning scenarios).

### Configure Foundry

Set the endpoint and key as AppHost user-secrets (recommended):

```bash
dotnet user-secrets set "Parameters:foundry-endpoint" "https://<resource>.cognitiveservices.azure.com/openai/v1" --project src/AgenticUI.AppHost
dotnet user-secrets set "Parameters:foundry-api-key" "<key>" --project src/AgenticUI.AppHost
```

Foundry exposes an OpenAI-compatible endpoint at `{resource}/openai/v1`, so the stock `OpenAIClient`
works against it unchanged. Both deployment names default to `gpt-5-mini`; override with
`Parameters:foundry-model` / `Parameters:foundry-reasoning-model` (or the `FOUNDRY_MODEL` /
`FOUNDRY_REASONING_MODEL` env vars).

> **Why a separate reasoning path?** Reasoning models only return their reasoning summaries through
> the OpenAI **Responses** API — chat completions spend the same reasoning tokens but return no
> reasoning text. So the reasoning scenario builds its client with `GetResponsesClient()` and opts in
> via the provider-neutral `ChatOptions.Reasoning`
> (`new ReasoningOptions { Output = ReasoningOutput.Full }`). `Microsoft.Extensions.AI` maps that to
> the Responses API's reasoning summary setting and surfaces the summaries as `TextReasoningContent`,
> which the MAF AG-UI adapter emits as `REASONING_*` events.

### Run

```bash
dotnet run --project src/AgenticUI.AppHost
```

Open the Aspire dashboard, then open the **web** resource and pick a scenario from the nav.

## Repository layout

```
src/
  AgenticUI.AppHost/          Aspire orchestration
  AgenticUI.ServiceDefaults/  Shared service defaults
  AgenticUI.AgentServer/      AG-UI backend (MAF + AG-UI C# SDK)
  AgenticUI.Web/              Blazor front end (Blazor AI components)
  BlazorAIComponents/         Bundled local copy of the in-progress Blazor AI components
docs/
  blog-post.md               Draft blog post summarizing the AG-UI scenarios
  findings.md                Bugs / issues found while building this sample
eng/
  sync-components-ai.ps1     Refresh the local copy of the Blazor AI components
```

## Notes & findings

See [`docs/findings.md`](docs/findings.md) for issues discovered while building this sample against
the freshly shipped packages (including a couple of real bugs in the samples/SDK).
