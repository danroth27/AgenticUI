# [Bug]: .NET `AGUI.Server` silently drops shared state emitted as `DataContent` (STATE_SNAPSHOT / STATE_DELTA never sent)

**Target repo:** `ag-ui-protocol/ag-ui`
**Area:** .NET SDK — `AGUI.Server`
**Severity:** High — the shared-state and predictive-state scenarios silently do nothing.

## Describe the bug

When a Microsoft Agent Framework agent emits shared state as an `Microsoft.Extensions.AI.DataContent`
with media type `application/json` (a full snapshot) or `application/json-patch+json` (a delta),
`AGUI.Server`'s `ChatResponseUpdate` → AG-UI event conversion never turns it into a `STATE_SNAPSHOT`
or `STATE_DELTA` event. The content is **silently dropped** — no state event reaches the client and
no diagnostic is produced.

State is only emitted when either:

- `ChatResponseUpdate.RawRepresentation` is an AG-UI `BaseEvent` (e.g. `StateSnapshotEvent` /
  `StateDeltaEvent`), or
- a tool result is mapped via `AGUIStreamOptions.MapResultAsStateSnapshot(...)` /
  `MapResultAsStateDelta(...)`.

## Root cause

In `sdks/dotnet/src/AGUI.Server/ChatResponseUpdateAGUIExtensions.cs`, the per-content `switch`
(around line 205) handles `TextReasoningContent`, `TextContent`, `FunctionCallContent`,
`FunctionResultContent`, `ToolApprovalRequestContent`, and `InterruptRequestContent`. There is **no
`DataContent` case**. The `default` arm only consults `options.InvokeInterruptMappers(content)`; if
no interrupt mapper matches (the normal case for state), the content is discarded.

## Evidence — the repo's own MAF example relies on the dropped pattern

`integrations/microsoft-agent-framework/dotnet/examples/AGUIDojoServer` emits state exactly this way,
so its state scenarios never produce STATE events with `AGUI.Server` 0.0.4:

- `SharedState/SharedStateAgent.cs:86` → `Contents = [new DataContent(stateBytes, "application/json")]`
- `PredictiveStateUpdates/PredictiveStateUpdatesAgent.cs:81` → `new DataContent(stateBytes, "application/json")`
- `AgenticUI/AgenticUIAgent.cs:58,62` → `new DataContent(bytes, "application/json")` / `"application/json-patch+json"`

## Steps to reproduce

1. Map an agent whose streamed `ChatResponseUpdate` has
   `Contents = [ new DataContent(Encoding.UTF8.GetBytes(json), "application/json") ]`.
2. `POST` a `RunAgentInput` and read the SSE stream.
3. **Actual:** the stream contains no `STATE_SNAPSHOT` event; the JSON is dropped.
4. **Works instead:** set `update.RawRepresentation = new StateSnapshotEvent { Snapshot = ... }`
   (or use `MapResultAsStateSnapshot`) and the `STATE_SNAPSHOT` is emitted.

## Expected behavior

Either:

- **(preferred)** map `DataContent` with `application/json` → `STATE_SNAPSHOT` and
  `application/json-patch+json` → `STATE_DELTA` (the pattern the MAF examples already use), **or**
- if `DataContent` is intentionally unsupported, emit a diagnostic/warning instead of silently
  dropping it, and fix the MAF examples (see companion issue) to use a supported pattern.

## Environment

- `AGUI.Server` / `AGUI.Abstractions` 0.0.4
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview.260722.1
- .NET 10.0.302

## Notes

Verified by building an agent both ways against the shipped packages: `DataContent` → no STATE
event; `RawRepresentation = StateSnapshotEvent` → STATE_SNAPSHOT emitted and rendered client-side.
