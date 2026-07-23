# [DX/Bug]: .NET `AGUI.Server` silently drops unmapped content (e.g. `DataContent` state) with no diagnostic

**Target repo:** `ag-ui-protocol/ag-ui`
**Area:** .NET SDK — `AGUI.Server`
**Severity:** Medium — state-emitting agents can produce *nothing* with no error, which is hard to diagnose.

## Summary

`AGUI.Server`'s `ChatResponseUpdate` → AG-UI event conversion silently discards any content type it
doesn't recognize. In particular, shared state emitted as a `Microsoft.Extensions.AI.DataContent`
(`application/json` / `application/json-patch+json`) produces **no** `STATE_SNAPSHOT` / `STATE_DELTA`
and **no** warning — the update just vanishes.

To be clear about scope: **I'm not claiming `DataContent` *should* map to a state event.** The shipped
contract for state appears to be `ChatResponseUpdate.RawRepresentation = StateSnapshotEvent /
StateDeltaEvent` (or `AGUIStreamOptions.MapResultAsStateSnapshot(...)`), and that works. The ask is
about the **silent** part: dropping unmapped content with no diagnostic is a DX trap, especially
because the repo's own MAF example still uses the older `DataContent` pattern (below), so a developer
following it sees an agent that "does nothing" with no clue why.

## Root cause

In `sdks/dotnet/src/AGUI.Server/ChatResponseUpdateAGUIExtensions.cs`, the per-content `switch`
(~line 205) handles `TextReasoningContent`, `TextContent`, `FunctionCallContent`,
`FunctionResultContent`, `ToolApprovalRequestContent`, and `InterruptRequestContent`. The `default`
arm only consults `options.InvokeInterruptMappers(content)`; anything else (including `DataContent`)
is dropped with no log.

## Why this bites people

The `AGUIDojoServer` MAF example in this repo emits state the old way and therefore produces no STATE
events against the shipped packages:

- `SharedState/SharedStateAgent.cs` — reads input state from `AdditionalProperties["ag_ui_state"]`
  and emits `new DataContent(stateBytes, "application/json")`.
- `PredictiveStateUpdates/PredictiveStateUpdatesAgent.cs:81`, `AgenticUI/AgenticUIAgent.cs:58,62` —
  same `DataContent` emission.

That example is pinned to `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` **1.0.0-preview.251110.1** and
uses the removed `AddAGUI`/`MapAGUI` API and the old `ag_ui_state` input key — i.e. it reflects an
**older, superseded state contract**, not the shipped one. (Tracked separately as the "stale example"
issue.) So this isn't proof of a regression; it's why the silent drop is easy to hit.

## Suggested resolution (pick one)

- **Preferred:** when an update carries content the converter can't map (e.g. a `DataContent`), emit a
  debug/warning diagnostic (`ILogger`) — e.g. *"AG-UI: dropping unmapped content of type X; emit state
  via RawRepresentation = StateSnapshotEvent or MapResultAsStateSnapshot"*. That turns a silent no-op
  into a discoverable one.
- **Optional:** additionally accept `DataContent` with `application/json` → `STATE_SNAPSHOT` and
  `application/json-patch+json` → `STATE_DELTA` as a convenience, matching the pattern the older
  examples used. (Design call for the maintainers — not asserting this is required.)
- Either way, update the MAF example to the shipped contract (companion "stale example" issue).

## Environment

- `AGUI.Server` / `AGUI.Abstractions` 0.0.4
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview.260722.1
- .NET 10.0.302

## Notes

Verified against the shipped packages: `DataContent(application/json)` → no STATE event, no log;
`RawRepresentation = new StateSnapshotEvent { Snapshot = ... }` → `STATE_SNAPSHOT` emitted and
rendered client-side.
