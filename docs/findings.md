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
- **Microsoft Foundry drops straight in.** Foundry exposes an OpenAI-compatible endpoint at
  `{resource}/openai/v1`, so pointing a stock `OpenAIClient` at it (the API key is accepted as a bearer
  token) and calling `.AsAIAgent(...)` was frictionless — no Azure-specific client required.
- **Streaming chat, backend tools, human-in-the-loop approvals, shared/predictive/plan state, and
  reasoning all worked end-to-end** (state and reasoning after the fixes below). The
  `ApprovalRequiredAIFunction` → AG-UI interrupt → `ToolApprovalRequestContent` → Blazor
  `FunctionApprovalBlock` (Approve/Reject) → resume round-trip is smooth. Reasoning surfaces as AG-UI
  `REASONING_*` events → `TextReasoningContent` → the Blazor collapsible "thought process" block.

## Bugs / issues found

> **Upstream tracking status (checked 2026-07-23).** Each finding was mapped to the repo that owns
> the code and checked for existing issues. Detailed drafts are kept as separate working notes
> (outside this sample repo).
>
> - **Bug #1 (DataContent state dropped)** → **RESOLVED, not a bug.** Javier confirmed (2026-07-23)
>   that emitting state as `DataContent("application/json")` was a pre-public-API hack and is
>   intentionally unsupported; the contract is `RawRepresentation = StateSnapshotEvent` (which our
>   sample and docs already use). The remaining actionable item is the stale `AGUIDojoServer` sample
>   that still uses the removed hack.
> - **Bug #4 (stale `ag-ui` MAF example)** → `ag-ui-protocol/ag-ui`. **Not tracked** → draft issue
>   prepared (pinned to an old preview; uses the removed `AddAGUI`/`MapAGUI` and old state contract).
> - **Bug #3 (`UIActionBlock` no auto-invoke)** → `dotnet/aspnetcore` (Blazor AI components, PR #67673).
>   **Fixed in our components copy** and verified end-to-end; drafted as a PR comment, not an issue.
> - **Bug #2 (client state not auto-sent)** → **reframed as an API-shape gap, not an SDK bug.**
>   `AGUIChatClient` *does* forward `RunAgentInput.State`/`ParentRunId` when set via
>   `RawRepresentationFactory` (confirmed by ag-ui#2151); the gap is that the components have an
>   inbound `StateMapper` but no symmetric outbound hook. Covered in the PR comment.
> - **Conditional approval** → **resolved**: MAF supports argument-based conditional approval via
>   `AIAgentBuilder.UseToolApproval` + `ToolApprovalAgentOptions.AutoApprovalRules`
>   ([agent-framework#6335](https://github.com/microsoft/agent-framework/pull/6335), shipped in 1.15.0,
>   verified end-to-end over AG-UI). The MEAI `ApprovalRequiredAIFunction` primitive itself stays binary
>   ([dotnet/extensions#7449](https://github.com/dotnet/extensions/issues/7449)). See finding #1.
> - **Workflow-over-AG-UI events not surfaced** → already open at
>   [microsoft/agent-framework#2494](https://github.com/microsoft/agent-framework/issues/2494) —
>   draft comment prepared, do not duplicate.
> - **HITL doc/sample hackery** → fixed: MAF docs PR #430 rewrote the HITL page to the idiomatic
>   pattern, and MAF sample PR #7295 simplifies Step04 (removed ~470 lines of approval middleware).
> - **`WithInMemorySessionStore()` throws HTTP 500 by default** → `WithInMemorySessionStore()` defaults
>   to `withIsolation: true`, which requires a `SessionIsolationKeyProvider`; without one the AG-UI
>   endpoint returns 500 (`InvalidOperationException: Session isolation key is required...`). Found while
>   verifying Javier's idiomatic getting-started wiring. Docs now use `WithInMemorySessionStore(withIsolation: false)`
>   for single-user servers. **Worth raising with Javier** (the draft snippet and possibly the default
>   are a footgun for the getting-started path).
>
> **Idiomatic-pattern reconciliation (Javier's draft merged, 2026-07-23):** the .NET AG-UI state docs
> and the AgenticUI sample now use the declarative `AGUIStreamOptions.MapResultAsStateSnapshot /
> MapResultAsStateDelta / MapCall` (+ `MapAGUIServer(...).WithMetadata`) pattern instead of custom
> `DelegatingAIAgent` + `RawRepresentation`. All state scenarios verified end-to-end (STATE_SNAPSHOT /
> STATE_DELTA emitted). The lower-level `RawRepresentation = StateSnapshotEvent` still works but is no
> longer the documented idiom.

### 1. (SDK/sample) State emitted as `DataContent` is silently dropped by `AGUI.Server`

> **RESOLVED — not an SDK bug (Javier, 2026-07-23).** Emitting state as `DataContent("application/json")`
> was a hack from before the public API existed; it is intentionally unsupported now. The supported
> contract is `RawRepresentation = StateSnapshotEvent` (below), which our sample and the docs already
> use. So there's nothing to fix in the SDK — the only actionable item is the **stale `AGUIDojoServer`
> sample** in `ag-ui-protocol/ag-ui`, which still emits the removed `DataContent` pattern. An optional,
> low-priority DX idea is for `AGUI.Server` to *log a warning* when it drops unmapped content instead
> of failing silently.

**Original severity as reported: high** — it makes the state scenarios appear to do nothing.

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

> **Update (2026-07-23): fixed in our components copy.** The engine (`AgentContext`) now auto-invokes
> `UIActionBlock`s and only parks at `AwaitingInput` for blocks that need a human, so the
> `UIActionRunner` glue is gone. Verified end-to-end (frontend tool auto-runs and the run resumes;
> human approval still stalls until approved). Proposed upstream as a PR #67673 comment; see the
> components copy's `NOTICE.md` → *Local modifications*.

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

- **Reasoning summaries are opt-in, and the opt-in is easy to lose.** Reasoning models only return
  their reasoning text through the OpenAI **Responses** API (`GetResponsesClient()`), and only when
  `ResponseReasoningOptions.ReasoningSummaryVerbosity` is set — chat completions spend the same
  reasoning tokens (visible in `usage.completion_tokens_details.reasoning_tokens`) but return no
  reasoning text at all. Setting that option via the agent's `ChatOptions.RawRepresentationFactory`
  is enough: MAF *merges* agent-level options into each run rather than replacing them
  (`ChatClientAgent.CreateConfiguredChatOptions` chains the two factories), so the opt-in survives an
  AG-UI run, which supplies only tools and context. Measured 22/25 runs over the AG-UI wire. A
  first-class `ChatOptions.ReasoningEffort` / `ReasoningSummary` on `Microsoft.Extensions.AI` would
  still be welcome — it would remove the provider-specific `RawRepresentationFactory` glue entirely.
  `ChatClientBuilder.ConfigureOptions(...)` applies the same option correctly (5/5).
- **Prompting for brevity suppresses the reasoning summary.** Measured against `gpt-5-mini`, 5 trials
  per variant: instructions asking for "one or two sentences, no step-by-step recap" produced a
  summary in only **1–2 of 5** runs, while instructions constraining *formatting* only ("plain prose,
  no markdown or bullets") produced one in **5/5** (avg 1192 chars). Raising `ReasoningEffortLevel`
  did **not** compensate — `Medium` was worse than the default. Constrain format, never length.
- **HITL model behavior:** `gpt-4o-mini` often replies "shall I proceed?" in text before actually calling
  an approval-required tool. Tightening the system prompt (or using a stronger model) makes it call the
  tool on the first turn. Not a framework issue.
- **AG-UI hosting is still preview** (`Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview) even
  though the core `Microsoft.Agents.AI` packages are stable 1.15.0. Worth calling out in docs/blog.

## Limitations relative to Python AG-UI support (doc-parity follow-ups)

Found while bringing the C# Learn docs to parity with the Python docs. Each was verified against actual
.NET behavior (see method):

1. **Conditional approval — supported via `UseToolApproval` + auto-approval rules (updated).**
   The low-level MEAI primitive `ApprovalRequiredAIFunction` is still binary — a single `ctor(AIFunction)`
   (always require); "never" = don't wrap; no per-invocation mode on the function itself
   (tracked for that layer by [dotnet/extensions#7449](https://github.com/dotnet/extensions/issues/7449)).
   **However, MAF closes the practical gap** at the agent layer: wrap the tool in
   `ApprovalRequiredAIFunction`, then layer `AIAgentBuilder.UseToolApproval(new ToolApprovalAgentOptions {
   AutoApprovalRules = [...] })`. Each rule gets a `ToolAutoApprovalRuleContext` exposing the pending
   `FunctionCallContent` (name + **arguments**) and returns `true` to auto-approve — evaluated after
   standing rules, before prompting. This gives Python's `conditional` behavior, just composed as
   heuristic auto-approval middleware rather than a per-tool mode enum. Added in
   [agent-framework#6335](https://github.com/microsoft/agent-framework/pull/6335)
   (closes [#6083](https://github.com/microsoft/agent-framework/issues/6083)); shipped in
   `Microsoft.Agents.AI` **1.15.0**. **Verified end-to-end over AG-UI**: a `$500`
   `transfer_funds` call was auto-approved and streamed its `TOOL_CALL_RESULT` with no interrupt, while a
   `$5,000` call raised `RUN_FINISHED` `outcome.type="interrupt"` for the user to confirm — from a single
   tool registration. Tool approval (always/never, selective, conditional) is a general Agent
   Framework capability, so the C# docs reference the generic `agents/tools/tool-approval` page rather
   than duplicating it in the AG-UI HITL page; conditional approval is missing from that generic page
   today, tracked by [semantic-kernel-docs#434](https://github.com/MicrosoftDocs/semantic-kernel-docs/issues/434).
   *Security note:* auto-approval rules can match by tool name alone, so scope each rule to the name **and**
   the specific arguments.

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
7. **No declarative predictive state updates** (streaming tool *arguments* into state). C# has declarative
   helpers for tool *results* (`AGUIStreamOptions.MapResultAsStateSnapshot` / `MapResultAsStateDelta`), but
   the predictive case has no declarative equivalent of Python's `predict_state_config` — you must
   hand-roll the low-level `MapCall(...)` pipeline (~140 lines: read the streamed arg, emit snapshots/deltas,
   complete the call, inject `confirm_changes`, plus manual endpoint wiring without function invocation).
   So the C# predictive docs use a manual document-editor example that can't mirror Python's concise recipe
   example. Tracked by [ag-ui#2245](https://github.com/ag-ui-protocol/ag-ui/issues/2245) (SDK declarative
   mapping) and the broader [agent-framework#4177](https://github.com/microsoft/agent-framework/issues/4177)
   (`StateBag` auto-emission + arg→state mapping; agent-framework core).

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

3. **Client resume — RESOLVED (this was our over-engineering, not an API gap).** Approve→resume works
   transparently: reuse the same `AgentSession` and send the `ToolApprovalResponseContent` back —
   `AGUIChatClient` auto-converts it into the AG-UI `Resume` payload and recovers the thread id itself,
   so **no `RawRepresentationFactory` / `ThreadId` / `ParentRunId` plumbing is needed**. **Verified
   end-to-end** against a live HITL endpoint (approve → the tool runs). The earlier HITL sample/doc
   carried that plumbing defensively; it has been removed from the docs.

4. **Shared-state input requires manual `RunAgentInput.State` plumbing** (also via
   `RawRepresentationFactory`), and the Blazor `UIAgent<TState>` doesn't wire it automatically (bug #2).
   Python surfaces shared state more directly.

5. **No approval "modes"** (see limitations list) — only always/never; no `conditional`.
