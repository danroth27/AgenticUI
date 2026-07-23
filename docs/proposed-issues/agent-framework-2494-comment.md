# Draft comment for microsoft/agent-framework#2494 (".NET: AG-UI support for workflow as agent")

> This confirms the reporter's observation and adds specifics from testing the shipped packages,
> so we don't open a duplicate.

We hit the same thing building a .NET AG-UI sample. Concrete findings against the shipped packages
(`Microsoft.Agents.AI` 1.15.0, `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview,
`AGUI.Server` 0.0.4):

**What does work.** A workflow built with
`AgentWorkflowBuilder.BuildSequential(...).AsAIAgent(name: ...)` and mapped with
`app.MapAGUIServer("/workflow", agent)` streams each **constituent agent's** output over AG-UI:
`TEXT_MESSAGE_*` and `TOOL_CALL_*` events arrive, tagged per agent (the agent name flows through as
`AuthorName`). So a client sees the agents' text and tool calls.

**What doesn't.** The **workflow's own** lifecycle events (executor/step start & complete, activity/
progress, edge transitions — the `WorkflowEvent` family) are **not** surfaced as AG-UI events. The
AG-UI stream carries the agents' chat output but nothing that says "step 2 of the workflow started/
finished." That matches the report: after the orchestration runs, there's no workflow-level signal
to drive UI beyond the raw agent messages.

**Suggested direction.** A mapping from `WorkflowEvent`s to AG-UI events would close this — most
naturally to AG-UI **custom events** (or `STEP_STARTED`/`STEP_FINISHED` where they fit). Python had a
parallel ask in #3872 ("custom `WorkflowEvent` emission for AG-UI/CopilotKit"). Ideally the
`AsAIAgent()`/`MapAGUIServer` path exposes an opt-in so workflow events flow without per-app plumbing.

Related: #4902 (make it clear which agent is executing in multi-agent workflows) — the `AuthorName`
tagging above partially addresses the identification half, but not the workflow-lifecycle half.

Happy to share a minimal repro (a sequential two-agent workflow mapped via `MapAGUIServer`) if useful.
