# [Bug]: `AGUIDojoServer` MAF example is pinned to an old preview and uses the removed `AddAGUI`/`MapAGUI` API (and the dropped `DataContent` state pattern)

**Target repo:** `ag-ui-protocol/ag-ui`
**Area:** .NET SDK — `integrations/microsoft-agent-framework/dotnet/examples/AGUIDojoServer`
**Severity:** Medium — the canonical MAF integration example does not build/behave against the shipped packages.

## Describe the bug

The `AGUIDojoServer` example does not reflect the shipped .NET AG-UI + Microsoft Agent Framework
packages:

1. **Old package pin.** `AGUIDojoServer.csproj` references
   `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` **1.0.0-preview.251110.1** and
   `Microsoft.Agents.AI.OpenAI` **1.0.0-preview.251110.1**. The current release train is
   **1.15.0** (hosting: `1.15.0-preview.260722.1`).

2. **Removed API.** `Program.cs` calls `builder.Services.AddAGUI()` and
   `app.MapAGUI("/route", agent)` (lines 18, 28–41). In the shipped hosting package these were
   renamed to **`AddAGUIServer()`** and **`MapAGUIServer("/route", agent)`**. The example does not
   compile against the current package.

3. **Dropped state pattern.** The state scenarios emit shared state as
   `new DataContent(bytes, "application/json")` /
   `"application/json-patch+json"` (`SharedState/SharedStateAgent.cs:86`,
   `PredictiveStateUpdates/PredictiveStateUpdatesAgent.cs:81`, `AgenticUI/AgenticUIAgent.cs:58,62`).
   With `AGUI.Server` 0.0.4 that content is silently dropped, so no `STATE_SNAPSHOT` / `STATE_DELTA`
   is ever emitted (see companion issue on `AGUI.Server`).

## Expected

Update the example to the shipped packages and idiomatic API:

- Bump the `Microsoft.Agents.AI.*` package references to the current release.
- `AddAGUI()` → `AddAGUIServer()`; `MapAGUI(...)` → `MapAGUIServer(...)`.
- Emit state through a supported path so the state scenarios work — either
  `ChatResponseUpdate.RawRepresentation = new StateSnapshotEvent { Snapshot = ... }` /
  `StateDeltaEvent { Delta = ... }`, or `AGUIStreamOptions.MapResultAsStateSnapshot(...)` — pending
  the resolution of the `DataContent` companion issue.

## Notes

Happy to open a PR for the API rename + package bump. The state-emit change depends on how the
`DataContent` issue is resolved (map `DataContent`, or switch the examples to `RawRepresentation`).

A working, standalone reference that maps each dojo scenario to `AddAGUIServer` / `MapAGUIServer`
and emits state via `RawRepresentation = StateSnapshotEvent` lives in
[`danroth27/AgenticUI`](https://github.com/danroth27/AgenticUI) (`src/AgenticUI.AgentServer`).
