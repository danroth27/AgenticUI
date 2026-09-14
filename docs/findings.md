# Findings

## Tested stack

The sample currently uses:

- .NET 11 RC1 and Aspire 13.5.3
- `Microsoft.Agents.AI.OpenAI` and `Microsoft.Agents.AI.Workflows` 1.15.0
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview.260722.1
- `AGUI.Client` and `AGUI.Server` 0.0.6
- `Azure.AI.OpenAI` 2.9.0-beta.1
- `Microsoft.AspNetCore.Components.AI` 0.1.0-preview.1.26459.102

The Components AI preview declares a dependency on a newer `Microsoft.AspNetCore.Components.Web` package. The web project pins that transitive dependency to the .NET 11 RC1 version available on NuGet.org and uses the ASP.NET Core shared framework at build and run time, avoiding custom package feeds.

## Validated capabilities

`AddAGUIServer()` and `MapAGUIServer()` expose Microsoft Agent Framework agents as AG-UI HTTP/SSE endpoints, while `AGUIChatClient` presents those endpoints to the Blazor app as standard `IChatClient` instances.

The scenarios validate:

- Streaming, multi-turn chat rendered as structured rich text
- Server tools mapped to generated typed blocks and custom renderers
- Frontend UI actions with either automatic invocation or explicit user confirmation
- Approve/reject interrupts
- Bidirectional editable state through AG-UI state snapshots
- Live plans through state snapshots and JSON Patch state deltas
- Predictive state with diff preview, acceptance, rejection, and rollback
- Reasoning summaries rendered through a custom activity block

## Integration findings

### UI actions remain application-controlled

Registering a UI action creates a `UIActionBlock`, but the components do not invoke it automatically or supply a default renderer. The application renders the block and decides when to call `InvokeAsync()`. This supports both automatic actions and actions that first collect input or confirmation.

The [frontend tools](../src/AgenticUI.Web/Components/Pages/Scenarios/FrontendTools.razor) scenario invokes its action automatically from a custom renderer. The [predictive state](../src/AgenticUI.Web/Components/Pages/Scenarios/PredictiveState.razor) scenario instead presents an accept/reject dialog and adds the user's decision to the pending action arguments before invoking it.

### State transport, model context, and UI projection are separate concerns

`UIAgent<TState>` does not automatically send its current state as `RunAgentInput.State`. `AGUIChatClient` can forward state supplied through `ChatOptions.RawRepresentationFactory`, so applications that need bidirectional editable state must explicitly add that outbound mapping. Local edits remain client-side until the next agent request.

Receiving `RunAgentInput.State` also does not automatically add it to the model context. The shared-state and predictive-state agents use `TryGetRunAgentInput` and insert the current state immediately before the latest user request. Placing it before older tool results can allow stale conversation content to override the user's local edits.

Inbound state projection is also explicit. The shared-state scenario deserializes `StateSnapshotEvent` into its typed state, while the plan scenario applies the specific `StateDeltaEvent` replace operations emitted by `update_plan_step`.

### Predictive state requires explicit mapping and resolution

Registering `propose_document` as a frontend action does not make its argument predictive state. The predictive-state scenario maps the completed action's `document` argument with `SetPredictiveState`, then resolves that pending state with `AcceptPredictiveState` or `RejectPredictiveState` after the user reviews the diff.

With `AGUI.Client` 0.0.6, the completed action arguments are deserialized into `IDictionary<string, object?>`; a JSON string argument arrives at the state mapper as a string-valued `JsonElement`. The scenario validates that representation rather than supporting an unobserved CLR `string` alternative.

`AgentContext` rejects pending predictive state before publishing `Idle` or `Error`, so application code does not need a second completion-time rollback. Application-level validation is still useful before accepting or rejecting because the state APIs otherwise silently do nothing when no prediction is pending.

`AGUI.Server` 0.0.6 can expose provider-native argument fragments as incremental `TOOL_CALL_ARGS` events through `MapStreamingToolCallArguments`, but `AGUI.Client` coalesces those fragments into a completed `FunctionCallContent` before the Blazor state mapper sees them. Mapping partial tool arguments directly into predictive state still requires application or provider integration ([ag-ui#2245](https://github.com/ag-ui-protocol/ag-ui/issues/2245)).

### Component lifetime is handled by `AgentBoundary`

When its `Agent` parameter changes, `AgentBoundary` disposes the previous `AgentContext`, creates a new one, and recreates its descendants through an internally keyed render region. An additional `@key` on `AgentBoundary` is unnecessary; reset behavior was verified both with and without it.

Presentation components do not need direct access to `AgentContext`. In the predictive-state scenario, the workspace owns status handling and message dispatch while the document editor and suggestion list communicate through parameters and callbacks.

### Activity semantics and rendering are application-defined

`ActivityHandler<TBlock>` is an extensibility point, not an AG-UI activity implementation. The application decides which incoming `AIContent` starts, updates, and completes an activity and how that activity is rendered. The packages do not currently include an AG-UI `ACTIVITY_SNAPSHOT` / `ACTIVITY_DELTA` handler or a built-in JSON Patch activity mapper; the sample's [reasoning handler](../src/AgenticUI.Web/Components/Pages/Scenarios/ReasoningActivityBlock.cs) is application code.

MAF/AG-UI can explicitly forward public AG-UI `BaseEvent` values through a response update's raw representation, but it does not automatically translate MAF workflow lifecycle events into AG-UI activity snapshots or deltas. Workflow agents therefore stream their ordinary output unless the application adds that mapping. Automatic workflow event projection remains tracked in [microsoft/agent-framework#2494](https://github.com/microsoft/agent-framework/issues/2494).

### Rich text requires a structured tree

`RichTextContent` renders a supplied `RichTextNode` tree, but the package does not include a Markdown parser that creates that tree from model text. Agentic Chat wraps its AG-UI client in [`FormattedChatClient`](../src/AgenticUI.Web/Formatting/FormattedChatClient.cs), which accumulates streaming Markdown and projects it through the sample's [`MarkdownRichTextParser`](../src/AgenticUI.Web/Formatting/MarkdownRichTextParser.cs).

The built-in structured-text renderer is part of `MessageList` and is not exposed as a standalone component for custom blocks. The reasoning scenario therefore displays its provider-generated reasoning summary as plain text. Formatting that summary would currently require the application to duplicate or replace the package's node-rendering logic.

## Current limitations

The sample creates one stable AG-UI thread ID for each `UIAgent`; reset creates a new agent and thread. It has no application-level durable conversation store, so it does not demonstrate persistence across page instances, server restarts, or multiple server instances.

After a rejected predictive-state proposal, the model can sometimes issue another proposal despite being instructed to acknowledge the rejection and stop. State rollback works correctly, but reliably preventing the repeated tool call would require targeted tool suppression or a client-only completion path rather than prompt instructions alone.

`Microsoft.AspNetCore.Components.AI` 0.1.0-preview.1 and `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview are preview packages. Their APIs and hosting behavior may change before stable releases.
