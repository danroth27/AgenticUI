# Findings: building a Blazor AG-UI sample on the freshly shipped .NET packages

Built against:

- `Microsoft.Agents.AI` / `Microsoft.Agents.AI.OpenAI` **1.15.0**
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` **1.15.0-preview.260722.1**
- `AGUI.Client` / `AGUI.Abstractions` / `AGUI.Server` **0.0.4** (AG-UI C# SDK)
- Blazor AI components from `dotnet/aspnetcore` PR #67673 (branch `javiercn/components-ai-full`)
- .NET 10.0.302 SDK, .NET Aspire 13.4

> **Last re-verified 2026-07-31.** Every upstream issue/PR link in this document was checked live on
> that date, and the reasoning and predictive-state findings were re-confirmed against the actual
> source of `AGUI.Server` 0.0.4, `Microsoft.Extensions.AI` v10.6.0, and `Microsoft.Agents.AI`
> 1.15.0 (not just public API surface).

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
- **Streaming chat, backend tools, human-in-the-loop approvals, shared/plan state, and
  reasoning all worked end-to-end** (state and reasoning after the fixes below). The
  `ApprovalRequiredAIFunction` → AG-UI interrupt → `ToolApprovalRequestContent` → Blazor
  `FunctionApprovalBlock` (Approve/Reject) → resume round-trip is smooth. Reasoning surfaces as AG-UI
  `REASONING_*` events → `TextReasoningContent` → the Blazor collapsible "thought process" block.

## Bugs / issues found

> **Upstream tracking status (re-checked 2026-07-31).** Each finding was mapped to the repo that owns
> the code and checked for existing issues. Detailed drafts are kept as separate working notes
> (outside this sample repo). Every link below was verified live on 2026-07-31.
>
> - **Bug #1 (DataContent state dropped)** → **RESOLVED, not a bug.** Javier confirmed (2026-07-23)
>   that emitting state as `DataContent("application/json")` was a pre-public-API hack and is
>   intentionally unsupported; the contract is `RawRepresentation = StateSnapshotEvent` (which our
>   sample and docs already use). The remaining actionable item is the stale `AGUIDojoServer` sample
>   that still uses the removed hack — now tracked, see bug #4.
> - **Bug #4 (stale `ag-ui` MAF example)** → **tracked** at
>   [ag-ui#2237](https://github.com/ag-ui-protocol/ag-ui/issues/2237) (filed 2026-07-23, still open):
>   pinned to an old preview; uses the removed `AddAGUI`/`MapAGUI` and old state contract.
> - **Bug #3 (`UIActionBlock` no auto-invoke)** → `dotnet/aspnetcore` (Blazor AI components, PR #67673).
>   **Fixed in our components copy** and verified end-to-end; **posted** as a PR comment (2026-07-23),
>   not an issue.
> - **Bug #2 (client state not auto-sent)** → **reframed as an API-shape gap, not an SDK bug.**
>   `AGUIChatClient` *does* forward `RunAgentInput.State`/`ParentRunId` when set via
>   `RawRepresentationFactory` (confirmed by ag-ui#2151); the gap is that the components have an
>   inbound `StateMapper` but no symmetric outbound hook. Covered in the same PR comment.
> - **Conditional approval** → **resolved**: MAF supports argument-based conditional approval via
>   `AIAgentBuilder.UseToolApproval` + `ToolApprovalAgentOptions.AutoApprovalRules`
>   ([agent-framework#6335](https://github.com/microsoft/agent-framework/pull/6335), **merged**, shipped
>   in 1.15.0, verified end-to-end over AG-UI). The MEAI `ApprovalRequiredAIFunction` primitive itself
>   stays binary ([dotnet/extensions#7449](https://github.com/dotnet/extensions/issues/7449), open).
>   See finding #1.
> - **Workflow-over-AG-UI events not surfaced** → already open at
>   [microsoft/agent-framework#2494](https://github.com/microsoft/agent-framework/issues/2494) —
>   draft comment prepared, do not duplicate.
> - **HITL doc/sample hackery** → fixes **proposed but not yet merged**: MAF docs
>   [PR #430](https://github.com/MicrosoftDocs/semantic-kernel-docs/pull/430) rewrites the HITL page to
>   the idiomatic pattern, and MAF sample
>   [PR #7295](https://github.com/microsoft/agent-framework/pull/7295) simplifies Step04 (removes ~470
>   lines of approval middleware). **Both were still open as of 2026-07-31.**
> - **`WithInMemorySessionStore()` throws HTTP 500 by default** → **NOT TRACKED UPSTREAM.**
>   `WithInMemorySessionStore()` defaults
>   to `withIsolation: true`, which requires a `SessionIsolationKeyProvider`; without one the AG-UI
>   endpoint returns 500 (`InvalidOperationException: Session isolation key is required...`). Found while
>   verifying Javier's idiomatic getting-started wiring. Docs now use `WithInMemorySessionStore(withIsolation: false)`
>   for single-user servers. **Worth raising with Javier** (the draft snippet and possibly the default
>   are a footgun for the getting-started path). Searched `microsoft/agent-framework` on 2026-07-31 —
>   no existing issue covers this.
> - **No public `IAGUITransport` implementation** → **NOT TRACKED UPSTREAM.** `AGUI.Client` exports the
>   `IAGUITransport` interface and `AGUIChatClientOptions.Transport` is settable, but the default
>   HTTP/SSE transport is internal and no public implementation is exported. Decorating the transport
>   (the natural way to attach outbound client state — see bug #2) therefore requires constructing a
>   throwaway `AGUIChatClientOptions` purely to harvest its `.Transport`. Found while prototyping the
>   shared-state editor; see the `shared-state-editor` branch, which is deliberately unmerged.
> - **Predictive state cannot be built at all** → see finding #7. Partially covered by
>   [ag-ui#2245](https://github.com/ag-ui-protocol/ag-ui/issues/2245), but that issue is framed as an
>   *ergonomics* gap ("~140 lines vs Python's 2"). The stronger 2026-07-30 finding — that the low-level
>   `MapCall` route named in the issue cannot work either — **is not yet communicated upstream.**
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

- **Reasoning summaries are opt-in, but the opt-in is now provider-neutral (updated 2026-07-31).**
  Reasoning models only return their reasoning text through the OpenAI **Responses** API
  (`GetResponsesClient()`) — chat completions spend the same reasoning tokens (visible in
  `usage.completion_tokens_details.reasoning_tokens`) but return no reasoning text at all. The opt-in
  itself no longer needs provider-specific glue: **`ChatOptions.Reasoning` shipped in
  `Microsoft.Extensions.AI` 10.6.0** (`dotnet/extensions#7192`, closed completed), so
  `Reasoning = new ReasoningOptions { Output = ReasoningOutput.Full }` replaces the hand-written
  `RawRepresentationFactory` + `ResponseReasoningOptions` entirely. Verified in source at v10.6.0:
  `OpenAIResponsesChatClient` maps `ReasoningOutput.Summary` → `ReasoningSummaryVerbosity.Concise` and
  `ReasoningOutput.Full` → `Detailed`; `ChatClientAgent.CreateConfiguredChatOptions` copies it with
  `requestChatOptions.Reasoning ??= agentOptions.ChatOptions.Reasoning`, so the opt-in still survives
  an AG-UI run, which supplies only tools and context. Measured 5/5 runs over the AG-UI wire after
  the switch (avg 2187 chars). `ReasoningOptions` / `ReasoningEffort` / `ReasoningOutput` are stable
  APIs — no experimental suppression needed. **Sharp edge:** the OpenAI client applies it with
  `result.ReasoningOptions ??= ...`, so a `RawRepresentationFactory` that sets `ReasoningOptions`
  silently *wins* over `ChatOptions.Reasoning`; never set both.
- **Prompting for brevity suppresses the reasoning summary.** Measured against `gpt-5-mini`, 5 trials
  per variant: instructions asking for "one or two sentences, no step-by-step recap" produced a
  summary in only **1–2 of 5** runs, while instructions constraining *formatting* only ("plain prose,
  no markdown or bullets") produced one in **5/5** (avg 1192 chars). Raising `ReasoningEffortLevel`
  did **not** compensate — `Medium` was worse than the default. Constrain format, never length.
- **HITL model behavior:** `gpt-4o-mini` often replies "shall I proceed?" in text before actually calling
  an approval-required tool. Tightening the system prompt (or using a stronger model) makes it call the
  tool on the first turn. Not a framework issue. Since switching the default deployment to `gpt-5-mini`,
  the scenario emits `TOOL_CALL_START` with no preceding text message.
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
7. **No predictive state updates at all** (streaming tool *arguments* into state). C# has declarative
   helpers for tool *results* (`AGUIStreamOptions.MapResultAsStateSnapshot` / `MapResultAsStateDelta`), but
   the predictive case has no declarative equivalent of Python's `predict_state_config`, *and* the
   hand-rolled low-level path can't reproduce it either. **Root cause, confirmed in source
   (2026-07-31), not inferred:**

   - `AGUI.Server`'s `ChatResponseUpdateAGUIExtensions` invokes `MapCall` from its
     `case FunctionCallContent fcc:` branch, once per `FunctionCallContent` it sees. It performs no
     buffering of its own.
   - `Microsoft.Extensions.AI` never surfaces a *partial* `FunctionCallContent`. In
     `OpenAIChatClient` (chat completions) the streamed argument deltas are appended to a
     `StringBuilder` on a private `FunctionCallInfo`, and the single `FunctionCallContent` is
     constructed **after the `await foreach` over the provider stream has completed** — the code
     comment reads *"Now that we've received all updates, combine any for function calls into a
     single item to yield."* In `OpenAIResponsesChatClient` the equivalent happens at
     `StreamingResponseOutputItemDoneUpdate`; `OutputItemAdded` yields nothing, and there is no case
     for function-call argument deltas at all.

   So `MapCall` can only ever fire once, *after* the model has finished writing the whole argument.
   Any "progressive" snapshots emitted from inside it are synthesized from an already-complete
   string. Measured on the wire: `TOOL_CALL_START` / `TOOL_CALL_ARGS` / `TOOL_CALL_END` all arrive at
   the same millisecond, and the snapshots a `MapCall` loop emits are 137 events in **9 ms**.
   (Contrast `/agentic_generative_ui`, which is genuinely paced: 1 snapshot + 10 deltas over 34.5 s,
   because each delta corresponds to real agent progress.)

   The deltas are not *lost*, only un-abstracted: both clients set `RawRepresentation` on the
   per-update `ChatResponseUpdate` (chat completions on every update; the Responses client via its
   `default:` branch, which yields a contentless update carrying the raw object). So predictive state
   is reachable today only by inserting a **provider-specific `DelegatingChatClient`** that reads
   `StreamingChatCompletionUpdate.ToolCallUpdates[].FunctionArgumentsUpdate`, re-accumulates and
   partially parses the incomplete JSON itself, and emits `StateSnapshotEvent`s through
   `RawRepresentation` — the one hook `AGUI.Server` honors. That is not something a sample should
   teach, so **this sample no longer ships a predictive-state scenario.**

   Tracked by [ag-ui#2245](https://github.com/ag-ui-protocol/ag-ui/issues/2245) (SDK declarative
   mapping) and the broader [agent-framework#4177](https://github.com/microsoft/agent-framework/issues/4177)
   (`StateBag` auto-emission + arg→state mapping; agent-framework core). **Note:** #2245 is framed as
   an ergonomics gap; the "the low-level route cannot work either" evidence above is not yet on it.

Verified-and-documented C# scenarios (tested, not guessed): agentic chat, backend tools, frontend tools,
human-in-the-loop approval (approve→resume), **selective approval** (mixed approved/unapproved tools in
one turn), shared state, agentic generative UI, reasoning, **workflow-as-agent**, and
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

2. **~~Approval flow requires `#pragma warning disable MEAI001`~~ — RESOLVED (updated 2026-07-31).**
   `ApprovalRequiredAIFunction`, `ToolApprovalRequestContent`, and `ToolApprovalResponseContent` no
   longer carry `[Experimental("MEAI001")]` as of `Microsoft.Extensions.AI.Abstractions` 10.6.0
   (verified in source and by building this sample with the suppression removed). The `MEAI001`
   entry has been dropped from `AgenticUI.AgentServer.csproj`; only `OPENAI001` remains, and only
   for `GetResponsesClient()` / `AsIChatClient(ResponsesClient)`.

3. **Client resume — RESOLVED (this was our over-engineering, not an API gap).** Approve→resume works
   transparently: reuse the same `AgentSession` and send the `ToolApprovalResponseContent` back —
   `AGUIChatClient` auto-converts it into the AG-UI `Resume` payload and recovers the thread id itself,
   so **no `RawRepresentationFactory` / `ThreadId` / `ParentRunId` plumbing is needed**. **Verified
   end-to-end** against a live HITL endpoint (approve → the tool runs). The earlier HITL sample/doc
   carried that plumbing defensively; it has been removed from the docs.

4. **Shared-state input requires manual `RunAgentInput.State` plumbing** (also via
   `RawRepresentationFactory`), and the Blazor `UIAgent<TState>` doesn't wire it automatically (bug #2).
   Python surfaces shared state more directly. Attempting to close this from the client side runs into
   a second gap: `AGUI.Client` exports no public `IAGUITransport` implementation, so decorating the
   transport means harvesting the internal default from a throwaway `AGUIChatClientOptions`. Both gaps
   are demonstrated on the deliberately-unmerged `shared-state-editor` branch.

5. **~~No approval "modes"~~ — RESOLVED (updated 2026-07-31).** Superseded by limitation #1: MAF
   supports conditional approval via `AIAgentBuilder.UseToolApproval` +
   `ToolApprovalAgentOptions.AutoApprovalRules` (shipped in 1.15.0, verified end-to-end over AG-UI).
   Only the low-level MEAI `ApprovalRequiredAIFunction` primitive remains binary
   ([dotnet/extensions#7449](https://github.com/dotnet/extensions/issues/7449)).
