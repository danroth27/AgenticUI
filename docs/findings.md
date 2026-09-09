# Current findings

This sample currently uses:

- `Microsoft.Agents.AI` / `Microsoft.Agents.AI.OpenAI` 1.15.0
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview.260722.1
- `AGUI.Client` / `AGUI.Abstractions` / `AGUI.Server` 0.0.4
- `Microsoft.AspNetCore.Components.AI` 0.1.0-preview.1.26459.102
- .NET 11.0.100 RC1 and .NET Aspire 13.4

## What the sample validates

- `AddAGUIServer()` and `MapAGUIServer()` expose Microsoft Agent Framework (MAF) agents as AG-UI
  HTTP/SSE endpoints, while `AGUIChatClient` presents those endpoints to the Blazor app as standard
  `IChatClient` instances.
- Streaming chat, generated typed tool blocks, custom block renderers, client UI actions,
  approve/reject interrupts, AG-UI state snapshots and deltas, reasoning summaries, and in-memory
  conversation restoration work with the packaged components.
- The deterministic [package-features scenario](../src/AgenticUI.Web/Components/Pages/Scenarios/PackageFeatures.razor)
  separately validates structured `RichTextContent`, a generated typed tool block, a custom
  `ActivityHandler<TBlock>`, committed typed state, `SetPredictiveState`, and both predictive-state
  acceptance and rejection/rollback.

## Current design boundaries and limitations

### UI actions are application-controlled

Registering an action creates a `UIActionBlock`, but the components do not invoke it automatically or
provide a default renderer. The application must render the block and call `InvokeAsync()` at the
appropriate time. This is intentional: an app can run an action immediately or first collect input or
confirmation. Both the [frontend-tools](../src/AgenticUI.Web/Components/Pages/Scenarios/FrontendTools.razor)
and [package-features](../src/AgenticUI.Web/Components/Pages/Scenarios/PackageFeatures.razor) pages use
explicit renderers.

### Activity semantics and mapping are application-defined

`ActivityHandler<TBlock>` is an extensibility point, not an AG-UI activity implementation. The
application decides which incoming `AIContent` starts, updates, and completes an activity and how its
content is rendered. The .NET packages do not currently include an AG-UI `ACTIVITY_SNAPSHOT` /
`ACTIVITY_DELTA` handler or a built-in JSON Patch activity mapper; this sample's
[verification](../src/AgenticUI.Web/Components/Pages/Scenarios/PackageFeatureChatClient.cs) and
[reasoning](../src/AgenticUI.Web/Components/Pages/Scenarios/ReasoningActivityBlock.cs) handlers are
application code.

MAF/AG-UI can explicitly forward public AG-UI `BaseEvent` values through a response update's raw
representation. It does not, however, automatically translate MAF workflow lifecycle events into
AG-UI activity snapshots or deltas. Workflow agents therefore stream their ordinary agent output
unless the application adds that mapping. Automatic workflow event projection remains tracked in
[microsoft/agent-framework#2494](https://github.com/microsoft/agent-framework/issues/2494).

### State mapping is explicit

The Blazor components expose an inbound `StateMapper`, but the application must interpret protocol
updates and choose `SetState` or `SetPredictiveState`. For AG-UI state, this sample's
[`AguiState`](../src/AgenticUI.Web/AguiState.cs) helper reads `StateSnapshotEvent` and
`StateDeltaEvent` from `ChatResponseUpdate.RawRepresentation`. Its JSON Patch support is deliberately
limited to the `add`, `replace`, and `remove` operations needed by the plan scenario.

`UIAgent<TState>` does not automatically send its current state as `RunAgentInput.State`.
`AGUIChatClient` can forward state supplied through its lower-level request/raw-representation hook,
but applications that need bidirectional editable state must add that outbound mapping.

The components package does support predictive state. The package-features scenario confirms
`SetPredictiveState`, `AcceptPredictiveState`, and `RejectPredictiveState` rollback. That scenario
uses a deterministic local `IChatClient`; it does not claim that AG-UI automatically derives
predictive snapshots from streaming tool arguments. AG-UI's .NET result mappings support committed
state snapshots/deltas, but predictive tool-argument mapping still requires application/provider
integration rather than a built-in declarative mapping
([ag-ui#2245](https://github.com/ag-ui-protocol/ag-ui/issues/2245)).

### Rich text requires a structured tree

`RichTextContent` renders a supplied `RichTextNode` tree, but the package does not include a Markdown
parser that creates that tree from model text. Applications must construct the nodes themselves or
integrate a parser. The deterministic client demonstrates direct construction in
[`RichUpdate`](../src/AgenticUI.Web/Components/Pages/Scenarios/PackageFeatureChatClient.cs).

### Persistence and package maturity

The sample's `ConversationThreadStore` is scoped, in-memory validation of `IConversationThread` and
`RestoreAsync`; it is not durable or multi-instance storage.

`Microsoft.AspNetCore.Components.AI` 0.1.0-preview.1 and
`Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview are preview packages. Their APIs and
hosting behavior may change before stable releases.
