# Findings: building a Blazor AG-UI sample on the freshly shipped .NET packages

Built against:

- `Microsoft.Agents.AI` / `Microsoft.Agents.AI.OpenAI` **1.15.0**
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` **1.15.0-preview.260722.1**
- `AGUI.Client` / `AGUI.Abstractions` / `AGUI.Server` **0.0.4** (AG-UI C# SDK)
- Blazor AI components from `dotnet/aspnetcore` PR #67673 (branch `javiercn/components-ai-full`)
- .NET 10.0.302 SDK, .NET Aspire 13.4

## What worked well

- **Server hosting is clean.** `builder.Services.AddAGUIServer()` + `app.MapAGUIServer("/route", agent)`
  maps a MAF `AIAgent` to an AG-UI HTTP + SSE endpoint with no ceremony. Mapping several agents (one per
  scenario) on one host "just works".
- **Client-as-`IChatClient` is the right shape.** `new AGUIChatClient(new AGUIChatClientOptions(httpClient, "/route"))`
  turns an AG-UI endpoint into a standard `IChatClient`, so the Blazor AI components' `UIAgent` consumes it
  with zero AG-UI-specific code.
- **GitHub Models is a great free backend.** Pointing an `OpenAIClient` at `https://models.github.ai/inference`
  with a GitHub token (the `gh auth token` works) and calling `.AsAIAgent(...)` was frictionless.
- **Streaming chat, backend tools, human-in-the-loop approvals, shared/predictive/plan state, and
  reasoning all worked end-to-end** (state and reasoning after the fixes below). The
  `ApprovalRequiredAIFunction` → AG-UI interrupt → `ToolApprovalRequestContent` → Blazor
  `FunctionApprovalBlock` (Approve/Reject) → resume round-trip is smooth. Reasoning surfaces as AG-UI
  `REASONING_*` events → `TextReasoningContent` → the Blazor collapsible "thought process" block.

## Bugs / issues found

### 1. (SDK/sample) State emitted as `DataContent` is silently dropped by `AGUI.Server`

**Severity: high** — it makes the state scenarios appear to do nothing.

The MAF AG-UI "dojo" samples
(`dotnet/samples/05-end-to-end/AGUIClientServer/AGUIDojoServer`) emit shared/predictive/plan state by
yielding `new DataContent(bytes, "application/json")` (and `"application/json-patch+json"`). But
`AGUI.Server`'s `ChatResponseUpdateAGUIExtensions.AsAGUIEventStreamAsync` has **no `DataContent` case** —
its content switch handles only text, reasoning, function call/result, and interrupt content. State
events are emitted only when either:

- `ChatResponseUpdate.RawRepresentation is BaseEvent` (e.g. a `StateSnapshotEvent`), or
- a tool result is mapped via `AGUIStreamOptions.MapResultAsStateSnapshot(...)` / `MapResultAsStateDelta(...)`.

So with released `AGUI.Server` 0.0.4 the dojo state scenarios emit **no** `STATE_SNAPSHOT` / `STATE_DELTA`
events. Confirmed by POSTing to the endpoint and inspecting the SSE stream (only `RUN_STARTED`,
`TEXT_MESSAGE_*`, `TOOL_CALL_*`, `RUN_FINISHED` — no state events).

**This sample's fix:** emit state via `RawRepresentation`, mirroring MAF's own integration test
(`SharedStateTests.FakeStateAgent`):

```csharp
yield return new AgentResponseUpdate
{
    Role = ChatRole.Assistant,
    RawRepresentation = new ChatResponseUpdate
    {
        Role = ChatRole.Assistant,
        RawRepresentation = new StateSnapshotEvent { Snapshot = snapshot } // or StateDeltaEvent
    }
};
```

**Recommendation:** either (a) update the dojo samples to use `RawRepresentation` (or the
`MapResultAsState*` options), or (b) have `AGUI.Server` map `DataContent("application/json")` /
`"application/json-patch+json"` to `STATE_SNAPSHOT` / `STATE_DELTA` so the sample code is correct.
Today the samples and the released SDK disagree.

### 2. (SDK/components) Client state is never sent upstream as `RunAgentInput.State`

**Severity: medium** — bidirectional shared state is effectively server→client only.

The AG-UI protocol carries client state on `RunAgentInput.State`, and MAF's `SharedStateAgent` sample
*gates* on it (`agentInput.State is { ValueKind: not Undefined }`), echoing an updated snapshot back.
But neither `AGUIChatClient` nor the Blazor `UIAgent<TState>` populate outgoing `RunAgentInput.State`
from the client's current state. So the sample's `SharedStateAgent` always sees no incoming state and
falls through to plain chat — the recipe card never updates.

**This sample's fix:** made the shared-state agent always produce and emit the snapshot (dropping the
incoming-state gate). True round-trip shared state would need the client to send its state.

**Recommendation:** provide a supported way for `UIAgent<TState>` / `AGUIChatClient` to attach the
current state to the outgoing request (e.g. surface `RunAgentInput.State` through `ChatOptions`, the way
tools are surfaced), so the CopilotKit-style bidirectional shared-state pattern works in .NET.

### 3. (components) `UIActionBlock` (frontend tools) has no default rendering or invocation

**Severity: medium** — a frontend tool call hangs the turn with no app-side glue.

When the model calls a client-registered UI action, the engine emits a `UIActionBlock` (an
`IInteractiveBlock`) and `AgentContext` parks at `AwaitingInput` awaiting `UIActionBlock.InvokeAsync()`.
But nothing invokes it by default, and `MessageListContext.RenderBlock` renders it as the raw type name
(`"UIActionBlock"`). Contrast with backend tool blocks, which the engine auto-invokes.

**This sample's fix:** a small `UIActionRunner` component (cascaded `AgentContext` +
`RegisterOnBlockAdded` → `InvokeAsync`) plus a `BlockRenderer<UIActionBlock>` for presentation.

**Recommendation:** consider auto-invoking `UIActionBlock`s (like backend tools) and/or shipping a
default renderer, so "frontend tools" work without bespoke wiring.

### 4. (ag-ui repo) Stale MAF integration example uses the old API

`ag-ui/integrations/microsoft-agent-framework/dotnet/examples/AGUIDojoServer/Program.cs` still calls the
renamed-away `builder.Services.AddAGUI()` / `app.MapAGUI(...)`. The shipped API is `AddAGUIServer()` /
`MapAGUIServer(...)`. (Also uses `TargetFramework=net9.0` while the packages target net8/9/10.)

### 5. (docs) Learn AG-UI C# docs are stale

The current [Microsoft Learn AG-UI page](https://learn.microsoft.com/agent-framework/integrations/ag-ui/?pivots=programming-language-csharp)
describes the **removed** in-tree `Microsoft.Agents.AI.AGUI` package and the old `AddAGUI()` / `MapAGUI()`
API. It should point at the `AGUI.*` SDK packages + `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` and the
new `AddAGUIServer()` / `MapAGUIServer()` names. (A draft update is being prepared in
`semantic-kernel-docs`.)

## Minor observations (not bugs)

- **Reasoning models on GitHub Models emit `<think>…</think>` inline** in the message content rather
  than a separate `reasoning_content` field, so `Microsoft.Extensions.AI` doesn't auto-map it to
  `TextReasoningContent`. This sample adds a small streaming `ReasoningAgent` that splits the `<think>`
  block out and re-emits it as `TextReasoningContent` (which then flows through AG-UI `REASONING_*`
  events to the Blazor reasoning block). A reusable "`<think>` splitter" chat-client middleware in
  `Microsoft.Extensions.AI` (or MAF) would remove this per-app glue.
- **HITL model behavior:** `gpt-4o-mini` often replies "shall I proceed?" in text before actually calling
  an approval-required tool. Tightening the system prompt (or using a stronger model) makes it call the
  tool on the first turn. Not a framework issue.
- **AG-UI hosting is still preview** (`Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview) even
  though the core `Microsoft.Agents.AI` packages are stable 1.15.0. Worth calling out in docs/blog.

## Limitations relative to Python AG-UI support (doc-parity follow-ups)

Found while bringing the C# Learn docs to parity with the Python docs. Each was verified against actual
.NET behavior (see method):

1. **No tool "approval modes."** Python has `@tool(approval_mode="always_require" | "never_require" |
   "conditional")`. .NET's `ApprovalRequiredAIFunction` has a single constructor `ctor(AIFunction)` —
   always require. "Never" = don't wrap; there is **no `conditional` mode**. (Verified via reflection on
   `Microsoft.Extensions.AI.Abstractions`; the `*ApprovalMode` types are for hosted MCP tools, a
   different feature.) The C# docs now describe the wrap/don't-wrap model instead of porting the Python
   modes.

2. **Workflows over AG-UI stream agent output only, not workflow events.** A workflow converted with
   `AgentWorkflowBuilder.BuildSequential(...).AsAIAgent()` and mapped with `MapAGUIServer` streams each
   constituent agent's `TEXT_MESSAGE_*` / `TOOL_CALL_*` events (with `AuthorName` per agent), but emits
   **no** AG-UI workflow events (`STEP_STARTED/FINISHED`, `ACTIVITY_SNAPSHOT/DELTA`, workflow-level
   interrupts) that the Python integration provides. (Verified: POSTing to a `/workflow` endpoint yielded
   RUN_STARTED, 2× TEXT_MESSAGE_START named "researcher"/"reporter", TEXT_MESSAGE_CONTENT, RUN_FINISHED —
   and nothing else.) The C# `workflows.md` scopes this honestly.

3. **No `wildcard tool arguments` equivalent.** Python's "Advanced State Patterns" uses Pydantic wildcard
   kwargs; there is no direct .NET analog, so that section is intentionally omitted from the C# docs.

4. **State via `DataContent` is dropped** (see bug #1) — must use `RawRepresentation = StateSnapshotEvent`.
5. **Blazor `UIAgent<TState>` / `AGUIChatClient` don't auto-send client state** (`RunAgentInput.State`);
   a client must set it manually via `ChatOptions.RawRepresentationFactory` (see bug #2).
6. **`UIActionBlock` has no default renderer/auto-invoke** (see bug #3).

Verified-and-documented C# scenarios (tested, not guessed): agentic chat, backend tools, frontend tools,
human-in-the-loop approval (approve→resume), **selective approval** (mixed approved/unapproved tools in
one turn), shared state, predictive state, agentic generative UI, reasoning, **workflow-as-agent**, and
the minimal-body `curl` test.

## C# developer-experience issues to track (vs Python)

Found while auditing the docs for idiomatic patterns. These are ergonomics/complexity gaps, not doc bugs:

1. **HITL sample teaches obsolete hackery.** The MAF Step04 `Human-in-the-Loop` sample (and, until this
   PR, the Learn docs) implement ~400 lines of custom `request_approval` middleware
   (`ServerFunctionApprovalAgent` + `ServerFunctionApprovalClientAgent`) to translate approvals over the
   wire. **This is no longer necessary** — `AGUIChatClient` converts an outgoing `ToolApprovalResponseContent`
   into the AG-UI `Resume` mechanism, and `AGUI.Server` converts `RunAgentInput.Resume` back into the
   approval pair. **Verified**: a raw `AGUIChatClient` console client does the full round-trip idiomatically
   in ~30 lines (wrap tool + `MapAGUIServer` on the server; `CreateResponse(approved)` + resume on the
   client). *Recommendation: rewrite the Step04 sample to the idiomatic pattern; it currently teaches a
   workaround as if it were the required approach.* (The docs now show the idiomatic pattern.)

2. **Approval flow requires `#pragma warning disable MEAI001`.** `ApprovalRequiredAIFunction`,
   `ToolApprovalRequestContent`, and `ToolApprovalResponseContent` are all evaluation-only, so idiomatic
   approval code can't avoid the pragma. Rough edge for a core scenario.

3. **Client resume boilerplate.** To resume a run with thread continuity, the stateless `AGUIChatClient`
   requires the caller to hand-set `RunAgentInput.ThreadId`/`ParentRunId` via
   `ChatOptions.RawRepresentationFactory`. Python's client handles continuation more transparently. A
   first-class "resume/continue" helper on the .NET client would remove this boilerplate.

4. **Shared-state input requires manual `RunAgentInput.State` plumbing** (also via
   `RawRepresentationFactory`), and the Blazor `UIAgent<TState>` doesn't wire it automatically (bug #2).
   Python surfaces shared state more directly.

5. **No approval "modes"** (see limitations list) — only always/never; no `conditional`.
