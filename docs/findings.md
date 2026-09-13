# Current findings

This sample currently uses:

- `Microsoft.Agents.AI` / `Microsoft.Agents.AI.OpenAI` 1.15.0
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview.260722.1
- `AGUI.Client` / `AGUI.Abstractions` / `AGUI.Formatting` / `AGUI.Server` 0.0.6
- `Azure.AI.OpenAI` 2.9.0-beta.1
- `Microsoft.AspNetCore.Components.AI` 0.1.0-preview.1.26459.102
- .NET 11.0.100 RC1 and Aspire 13.5.3

The Components AI preview declares a dependency on a newer `Microsoft.AspNetCore.Components.Web` package. The web project pins that transitive dependency to the .NET 11 RC1 version on NuGet.org and uses the ASP.NET Core shared framework at build and run time, avoiding custom package feeds.

## What the sample validates

- `AddAGUIServer()` and `MapAGUIServer()` expose Microsoft Agent Framework (MAF) agents as AG-UI
  HTTP/SSE endpoints, while `AGUIChatClient` presents those endpoints to the Blazor app as standard
  `IChatClient` instances.
- Streaming chat, structured rich text, generated typed tool blocks, custom block renderers, client
  UI actions, approve/reject interrupts, AG-UI state snapshots and deltas, predictive state, and
  reasoning summaries work with the packaged components.

## Current design boundaries and limitations

### UI actions are application-controlled

Registering an action creates a `UIActionBlock`, but the components do not invoke it automatically or provide a default renderer. The application must render the block and call `InvokeAsync()` at the appropriate time. This is intentional: an app can run an action immediately or first collect input or confirmation. The [frontend-tools](../src/AgenticUI.Web/Components/Pages/Scenarios/FrontendTools.razor) page invokes its action automatically from a custom renderer, while [predictive state](../src/AgenticUI.Web/Components/Pages/Scenarios/PredictiveState.razor) asks the user to accept or reject proposed document changes.

### Activity semantics and mapping are application-defined

`ActivityHandler<TBlock>` is an extensibility point, not an AG-UI activity implementation. The
application decides which incoming `AIContent` starts, updates, and completes an activity and how its
content is rendered. The .NET packages do not currently include an AG-UI `ACTIVITY_SNAPSHOT` /
`ACTIVITY_DELTA` handler or a built-in JSON Patch activity mapper; this sample's
[reasoning](../src/AgenticUI.Web/Components/Pages/Scenarios/ReasoningActivityBlock.cs) handler is
application code.

MAF/AG-UI can explicitly forward public AG-UI `BaseEvent` values through a response update's raw
representation. It does not, however, automatically translate MAF workflow lifecycle events into
AG-UI activity snapshots or deltas. Workflow agents therefore stream their ordinary agent output
unless the application adds that mapping. Automatic workflow event projection remains tracked in
[microsoft/agent-framework#2494](https://github.com/microsoft/agent-framework/issues/2494).

### State mapping is explicit

The Blazor components expose an inbound `StateMapper`, but the application must interpret protocol
updates and choose `SetState` or `SetPredictiveState`. The shared-state scenario directly
deserializes its `StateSnapshotEvent`, while the plan scenario applies the specific
`StateDeltaEvent` replace operations emitted by its `update_plan_step` tool.

`UIAgent<TState>` does not automatically send its current state as `RunAgentInput.State`. `AGUIChatClient` can forward state supplied through `ChatOptions.RawRepresentationFactory`, but applications that need bidirectional editable state must add that outbound mapping. Local edits remain client-side until the next agent request.

Receiving `RunAgentInput.State` also does not automatically make that state model context. A delegating agent or chat client can recover the originating input with `TryGetRunAgentInput` and explicitly project the state into the messages sent to the model. When sending the full conversation history, current state must be placed immediately before the latest user request; placing it before older tool results can let a stale snapshot override the user's local edits.

The components package supports predictive state through `SetPredictiveState`,
`AcceptPredictiveState`, and `RejectPredictiveState`. The
[predictive-state scenario](../src/AgenticUI.Web/Components/Pages/Scenarios/PredictiveState.razor)
registers `propose_document` as a frontend UI action. When the completed action call arrives, its
document argument is mapped to predictive state and displayed as a diff while the action waits for
the user to accept or reject it. This demonstrates provisional state and rollback without custom
AG-UI endpoint plumbing.

`AGUI.Server` 0.0.6 can expose provider-native argument fragments as incremental
`TOOL_CALL_ARGS` events through `MapStreamingToolCallArguments`, but `AGUI.Client` currently
coalesces those fragments into a completed `FunctionCallContent` before the Blazor state mapper sees
them. Mapping partial tool arguments directly into predictive state therefore still requires
application/provider integration ([ag-ui#2245](https://github.com/ag-ui-protocol/ag-ui/issues/2245)).

### Rich text requires a structured tree

`RichTextContent` renders a supplied `RichTextNode` tree, but the package does not include a Markdown
parser that creates that tree from model text. Applications must construct the nodes themselves or
integrate a parser. Agentic Chat wraps its AG-UI client in
[`FormattedChatClient`](../src/AgenticUI.Web/Formatting/FormattedChatClient.cs), which accumulates
streaming Markdown and projects it through the sample's
[`MarkdownRichTextParser`](../src/AgenticUI.Web/Formatting/MarkdownRichTextParser.cs).

The package's built-in structured-text renderer is part of `MessageList` and is not exposed as a
standalone component for custom blocks. The reasoning scenario therefore displays its
provider-generated reasoning summary as plain text inside its custom activity block. A summary may
contain visible Markdown syntax; formatting it would currently require the application to duplicate
the package's node-rendering logic.

### Persistence and package maturity

The shared-state scenario's `ConversationThreadStore` keeps conversation threads in memory so the UI can reconnect to the current thread. It is not durable or multi-instance storage.

`Microsoft.AspNetCore.Components.AI` 0.1.0-preview.1 and
`Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview are preview packages. Their APIs and
hosting behavior may change before stable releases.
