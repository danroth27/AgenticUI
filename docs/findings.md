# Findings: Blazor, AG-UI, and Microsoft Agent Framework

This document captures the current integration findings from building AgenticUI and the Blazor AG-UI Dojo scenarios. It focuses on behavior that affects application developers and on concrete follow-up work. Resolved investigations are retained only when they prevent repeating obsolete work.

## Current baseline

Last verified: **2026-08-26**

| Dependency | Version or source |
| --- | --- |
| Microsoft Agent Framework | `Microsoft.Agents.AI` / `Microsoft.Agents.AI.OpenAI` 1.15.0 |
| MAF AG-UI hosting | `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview.260722.1 |
| AG-UI .NET SDK | `AGUI.Client`, `AGUI.Abstractions`, `AGUI.Formatting`, and `AGUI.Server` 0.0.5 |
| Blazor AI components | `dotnet/aspnetcore`, branch `javiercn-components-ai-09-predictive-state`, snapshot `dd8b97ed95a5b53b4e384e86a670fe3c11f64323` |
| Application target | `net10.0`, built with .NET SDK 11.0.100-preview.7 |
| Aspire | 13.4.5 |

The Components.AI branch is regularly rebased. Compare trees rather than relying on the snapshot commit remaining reachable.

## Executive summary

The core architecture works well:

- `AddAGUIServer()` and `MapAGUIServer()` expose MAF agents as AG-UI HTTP/SSE endpoints with little ceremony.
- `AGUIChatClient` implements `IChatClient`, so a Blazor `UIAgent` can consume an AG-UI endpoint without protocol-specific UI code.
- Microsoft Foundry works through its OpenAI-compatible endpoint without an Azure-specific application layer.
- Streaming chat, backend tools, frontend tools, human approval, shared state, generative UI, reasoning summaries, and predictive-state UX all work end to end after the application-level integrations described below.

The remaining gaps are mostly at the boundary between the AG-UI event stream and the evolving Blazor AI components. The highest-value follow-up is replacing the predictive-state demo's synthetic snapshots with genuinely paced model argument updates.

## Actionable findings

| Priority | Finding | Current application workaround | Recommended action |
| --- | --- | --- | --- |
| High | Predictive state is visually incremental but not model-paced | Emit ten-character state snapshots from the completed `write_document_local` call | Use AG-UI 0.0.5's streaming argument hook and add a supported partial-arguments-to-state path |
| Medium | `UIActionBlock` does not execute automatically | Invoke it from the rendered Blazor component | Document the requirement clearly or provide a standard auto-run renderer |
| Medium | Components.AI no longer includes a reasoning block handler | Register an application `ActivityHandler` and custom renderer | Restore or publish a reusable default reasoning handler |
| Medium | Typed client state is not automatically sent with a run | Populate `RunAgentInput.State` with `RawRepresentationFactory` | Add a symmetric outbound state hook to `UIAgent<TState>` |
| Medium | Components.AI is undergoing breaking API churn | Pin and vendor an exact upstream snapshot | Keep migration notes with each snapshot and avoid treating current APIs as stable |
| Low | The legacy AG-UI MAF example still uses removed APIs | Use the newer SDK sample instead | Update or remove the legacy example tracked by ag-ui-protocol/ag-ui#2237 |
| Low | `WithInMemorySessionStore()` defaults to isolated sessions without requiring a key provider at compile time | Pass `withIsolation: false` for a single-user sample | Improve the getting-started default or fail during startup with a clearer configuration error |
| Low | Workflows exposed as agents omit workflow-level AG-UI events | Consume agent text and tool events only | Add step, activity, and workflow interrupt event mapping in microsoft/agent-framework#2494 |

### 1. Predictive state is currently synthetic

The predictive-state scenario works in the browser, but it does not yet stream state at the pace the model produces tool arguments.

`AGUIStreamOptions.MapCall("write_document_local", ...)` receives a completed `FunctionCallContent`. The current AgenticUI implementation and the official .NET Dojo then split the complete document into ten-character prefixes and emit a burst of `StateSnapshotEvent` values. This creates the intended visual progression, but all snapshots are generated after the provider has finished the tool call.

AG-UI 0.0.5 materially changes the available implementation path. `AGUIStreamOptions.MapStreamingToolCallArguments(...)` can extract provider-native argument fragments from each `ChatResponseUpdate.RawRepresentation` and preserve them as incremental `TOOL_CALL_ARGS` events. Microsoft.Extensions.AI still exposes the typed `FunctionCallContent` only after coalescing, so the extractor is necessarily provider-specific.

The remaining work is to turn those fragments into predictive state:

1. Register a Foundry/OpenAI fragment extractor with `MapStreamingToolCallArguments(...)`.
2. Incrementally repair and parse the incomplete JSON argument.
3. Map the growing `document` argument to `AgentState<T>.SetPredictiveState(...)`.
4. Keep the completed tool call and confirmation action balanced so conversation history remains valid.

The SDK does not currently provide a provider-neutral extractor, a partial-JSON helper, or a declarative streamed-argument-to-state mapper. The default HTTP transport is also internal, which makes observing raw events on the client unnecessarily difficult. These related ergonomics are tracked by [ag-ui-protocol/ag-ui#2245](https://github.com/ag-ui-protocol/ag-ui/issues/2245).

**Action for AgenticUI:** either implement the true fragment path or label the current effect as simulated progression. Do not describe the existing `MapCall` loop as genuine predictive streaming.

### 2. Frontend UI actions require explicit invocation

Registering a browser function with `UIAgentOptions.RegisterUIAction(...)` produces a `UIActionBlock` and pauses the run until `UIActionBlock.InvokeAsync()` completes. The latest Components.AI snapshot does not invoke the block automatically and does not provide a default renderer that does so.

AgenticUI renders `AccentColorAction`, which calls `InvokeAsync()` once from `OnAfterRenderAsync`. This is correct but easy for an application author to miss: rendering the raw block without this behavior leaves the run waiting indefinitely.

**Recommended Components.AI action:** ship a standard renderer for auto-running frontend actions, or make the invocation requirement prominent in the `RegisterUIAction` documentation and samples.

### 3. Reasoning needs an application block handler

AG-UI reasoning events still become `TextReasoningContent`, but the latest Components.AI snapshot removed `ReasoningContentBlock` and `ReasoningHandler`. Without another handler, the reasoning content is not presented as a dedicated activity.

AgenticUI now defines `ReasoningActivityBlock`, registers an `ActivityHandler<ReasoningActivityBlock>`, and renders a collapsible reasoning card.

**Recommended Components.AI action:** provide a reusable default handler and renderer, or document reasoning as an explicit extension-point scenario.

Reasoning summaries also have provider requirements:

- Use the OpenAI Responses API; chat completions can spend reasoning tokens without returning summary text.
- Set `ChatOptions.Reasoning`, for example `new ReasoningOptions { Output = ReasoningOutput.Full }`.
- Do not set both `ChatOptions.Reasoning` and provider-specific raw reasoning options. The provider applies its raw options first, so they can silently override the provider-neutral setting.
- Constrain answer formatting rather than answer length. Prompts demanding a very short answer measurably suppress reasoning summaries.

### 4. Outbound typed state requires manual request plumbing

`UIAgent<TState>` maps incoming snapshots and deltas through `StateMapper`, but it does not automatically attach the current typed state to outgoing requests. `AGUIChatClient` forwards `RunAgentInput.State` when supplied, so this is a Components.AI API-shape gap rather than a transport bug.

AgenticUI supplies the state through `ChatOptions.RawRepresentationFactory`:

```csharp
options.ChatOptions = new ChatOptions
{
    RawRepresentationFactory = _ => new RunAgentInput
    {
        State = JsonSerializer.SerializeToElement(currentState),
    },
};
```

**Recommended Components.AI action:** add an outbound state callback next to `StateMapper`, so applications do not need to construct protocol-specific `RunAgentInput` values.

Transporting state does not automatically make it model context. The predictive endpoint must also include the current document in the system prompt so the model knows what it is editing. This is expected separation between protocol state and chat messages, but samples should make it explicit.

### 5. Components.AI snapshot upgrades require migration work

The move from the earlier `javiercn/components-ai-full` snapshot to `javiercn-components-ai-09-predictive-state` included several breaking changes:

- `StateMapper` changed from `Func<StateMapperContext, bool>` to `Action<StateMapperContext>`.
- Predictive state now uses `StateMapperContext.SetPredictiveState(...)` and `AgentState<T>.AcceptPredictiveState()` / `RejectPredictiveState()`.
- `Suggestion` and `SuggestionList` were removed.
- Custom block renderers now belong inside `ChatPage.MessageListContent`.
- `ReasoningContentBlock`, `ReasoningHandler`, and several higher-level chat components were removed.
- `UIActionBlock` invocation behavior changed.
- Conversation persistence uses the current `IConversationThread` contract.

These components are unpublished and explicitly in progress, so breaking changes are expected. AgenticUI records the exact source in `src/BlazorAIComponents/Microsoft.AspNetCore.Components.AI/NOTICE.md` and centralizes synchronization in `eng/sync-components-ai.ps1`.

**Action for future upgrades:** diff the complete Components.AI tree, not only public signatures; then exercise every scenario because behavioral changes such as UI action invocation are not reliably exposed by compilation errors.

### 6. Confirmation continuations need turn-scoped tool gating

After a predictive document proposal is accepted or rejected, sending the confirmation result back with the write tool still enabled can cause the model to propose another edit instead of acknowledging the decision.

AgenticUI disables tools for the immediate confirmation-result continuation. The detection must inspect only the final incoming tool-result message. Searching the entire conversation for a prior confirmation result disables tools permanently and breaks later edits.

**Action for samples:** treat confirmation as a one-turn continuation state, not a conversation-wide flag.

### 7. Isolated in-memory sessions have a configuration footgun

`WithInMemorySessionStore()` defaults to `withIsolation: true`, which requires a `SessionIsolationKeyProvider`. Without one, the endpoint fails at request time with:

```text
InvalidOperationException: Session isolation key is required
```

For a local single-user sample, use:

```csharp
WithInMemorySessionStore(withIsolation: false)
```

**Recommended MAF action:** require the isolation provider during startup, improve the exception guidance, or reconsider the getting-started default.

### 8. Workflow agents do not emit workflow-level AG-UI events

A workflow converted with `AgentWorkflowBuilder.BuildSequential(...).AsAIAgent()` and mapped with `MapAGUIServer()` streams each constituent agent's text and tool events. It does not emit workflow step, activity, or workflow-level interrupt events.

This is tracked by [microsoft/agent-framework#2494](https://github.com/microsoft/agent-framework/issues/2494).

## Implementation notes that are working as designed

### Human-in-the-loop approval

Approval and resume work without custom AG-UI middleware:

1. Wrap the server tool in `ApprovalRequiredAIFunction`.
2. Render the resulting `FunctionApprovalBlock`.
3. Reuse the same conversation thread and send its `ToolApprovalResponseContent`.
4. Let `AGUIChatClient` translate the response to AG-UI `Resume`.

No manual `ThreadId`, `ParentRunId`, or resume `RawRepresentationFactory` plumbing is needed.

Argument-based conditional approval is available through `AIAgentBuilder.UseToolApproval(...)` and `ToolApprovalAgentOptions.AutoApprovalRules`. Match both the tool name and relevant arguments when defining an auto-approval rule. The lower-level request for conditional behavior inside `AIFunction` remains open at [dotnet/extensions#7449](https://github.com/dotnet/extensions/issues/7449).

### State event mapping

Emitting JSON state as `DataContent` was a pre-public-API convention and is intentionally unsupported. Use one of the public mechanisms:

- `MapResultAsStateSnapshot(...)`
- `MapResultAsStateDelta(...)`
- `MapCall(...)`
- `MapContent(...)`
- `RawRepresentation = new StateSnapshotEvent(...)`
- `RawRepresentation = new StateDeltaEvent(...)`

Silent dropping of arbitrary `DataContent` is not an SDK state-mapping bug.

### Foundry reasoning

Foundry's OpenAI-compatible endpoint works with the standard OpenAI client. Reasoning summaries require a Responses client and explicit reasoning output options. Prompting matters: format-only constraints preserve summaries more reliably than brevity constraints.

### Human-in-the-loop model behavior

Smaller models may answer with “shall I proceed?” instead of immediately calling an approval-required tool. A stronger model or a more direct system instruction usually resolves this. This is model behavior, not an AG-UI approval failure.

## Resolved or superseded investigations

The following items should not be treated as active gaps:

- **Microsoft Learn uses obsolete C# packages and API names:** resolved. The live AG-UI documentation was updated on 2026-08-25 and now uses the current package model and `MapAGUIServer`.
- **The MAF HITL sample requires hundreds of lines of approval middleware:** resolved by [microsoft/agent-framework#7295](https://github.com/microsoft/agent-framework/pull/7295), merged on 2026-08-19.
- **Approval content requires `MEAI001` suppression:** resolved in Microsoft.Extensions.AI 10.6.0.
- **Client approval resume requires manual AG-UI identifiers:** disproven; normal conversation reuse is sufficient.
- **Conditional approval is unavailable in .NET:** resolved at the MAF agent layer by `UseToolApproval` and `AutoApprovalRules`.
- **Server-side streamed tool arguments cannot survive MEAI coalescing:** superseded by AG-UI 0.0.5's `MapStreamingToolCallArguments(...)`. The remaining gap is mapping those fragments conveniently into predictive state.
- **`AgentSession.StateBag` should be emitted automatically as AG-UI state:** rejected as unsafe and closed as not planned in [microsoft/agent-framework#4177](https://github.com/microsoft/agent-framework/issues/4177). `StateBag` can contain internal provider and session data; frontend state must be projected explicitly.
- **dotnet/aspnetcore#67673 is the current Components.AI source:** superseded. That PR closed without merge; AgenticUI now tracks the cumulative predictive-state branch recorded in the baseline above.

## Upstream status

Status last checked on **2026-08-26**:

| Item | Status |
| --- | --- |
| [ag-ui-protocol/ag-ui#2237](https://github.com/ag-ui-protocol/ag-ui/issues/2237): stale legacy MAF example | Open |
| [ag-ui-protocol/ag-ui#2245](https://github.com/ag-ui-protocol/ag-ui/issues/2245): predictive state and transport ergonomics | Open |
| [microsoft/agent-framework#2494](https://github.com/microsoft/agent-framework/issues/2494): workflow AG-UI events | Open |
| [microsoft/agent-framework#4177](https://github.com/microsoft/agent-framework/issues/4177): automatic `StateBag` emission | Closed as not planned |
| [microsoft/agent-framework#7295](https://github.com/microsoft/agent-framework/pull/7295): modernize AG-UI samples | Merged |
| [dotnet/extensions#7449](https://github.com/dotnet/extensions/issues/7449): conditional approval inside `AIFunction` | Open |
| [MicrosoftDocs/semantic-kernel-docs#434](https://github.com/MicrosoftDocs/semantic-kernel-docs/issues/434): document conditional approval | Open |

## Scenario coverage

The current AgenticUI implementation has exercised:

- Agentic chat
- Backend tool rendering
- Frontend tools
- Human-in-the-loop approval and rejection
- Shared state
- Agentic generative UI
- Predictive state acceptance, rejection, rollback, and subsequent edits
- Reasoning summaries

The application behavior is functional. The predictive-state timing caveat in finding #1 remains the primary fidelity gap relative to genuine model-paced updates.
