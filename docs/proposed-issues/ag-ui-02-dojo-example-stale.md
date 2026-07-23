# [Bug]: `AGUIDojoServer` MAF example is pinned to an old preview and uses the removed AG-UI API + state contract

**Target repo:** `ag-ui-protocol/ag-ui`
**Area:** .NET SDK — `integrations/microsoft-agent-framework/dotnet/examples/AGUIDojoServer`
**Severity:** Medium — the canonical MAF integration example doesn't compile against the shipped packages, and its state scenarios don't work.

## Describe the bug

The `AGUIDojoServer` example targets an older preview and its pre-public-API contracts, so it does not
build or behave against the shipped .NET AG-UI + Microsoft Agent Framework packages.

1. **Old package pin.** `AGUIDojoServer.csproj` references
   `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` **1.0.0-preview.251110.1** and
   `Microsoft.Agents.AI.OpenAI` **1.0.0-preview.251110.1**. The current release train is **1.15.0**
   (hosting: `1.15.0-preview.260722.1`).

2. **Removed hosting API.** `Program.cs` calls `builder.Services.AddAGUI()` and
   `app.MapAGUI("/route", agent)` (lines 18, 28–41). In the shipped hosting package these were renamed
   to **`AddAGUIServer()`** and **`MapAGUIServer("/route", agent)`**, so the example doesn't compile
   against the current package.

3. **Removed state contract.** The state scenarios use the old pre-public-API state mechanism, which is
   no longer supported (confirmed with the .NET AG-UI maintainers):
   - **Output:** state is emitted as `new DataContent(bytes, "application/json")` /
     `"application/json-patch+json"` (`SharedState/SharedStateAgent.cs:86`,
     `PredictiveStateUpdates/PredictiveStateUpdatesAgent.cs:81`, `AgenticUI/AgenticUIAgent.cs:58,62`).
     The shipped contract is to emit a chat update whose `RawRepresentation` is a `StateSnapshotEvent`
     / `StateDeltaEvent`; the `DataContent` form was a hack from before the public API existed and is
     intentionally no longer converted.
   - **Input:** state is read from `ChatOptions.AdditionalProperties["ag_ui_state"]`
     (`SharedState/SharedStateAgent.cs`). The shipped contract reads run input via
     `chatOptions.TryGetRunAgentInput(out var input)` → `input.State`; the `ag_ui_state` key no longer
     exists.

## Expected

Update the example to the shipped packages and supported contracts:

- Bump the `Microsoft.Agents.AI.*` package references to the current release.
- `AddAGUI()` → `AddAGUIServer()`; `MapAGUI(...)` → `MapAGUIServer(...)`.
- Emit state via `ChatResponseUpdate.RawRepresentation = new StateSnapshotEvent { Snapshot = ... }` /
  `new StateDeltaEvent { Delta = ... }` (instead of `DataContent`).
- Read input state via `chatOptions.TryGetRunAgentInput(out var input)` → `input.State` (instead of
  `AdditionalProperties["ag_ui_state"]`).

## Notes

Happy to open a PR. A working, standalone reference that maps each dojo scenario to `AddAGUIServer` /
`MapAGUIServer` and emits state via `RawRepresentation = StateSnapshotEvent` lives in
[`danroth27/AgenticUI`](https://github.com/danroth27/AgenticUI) (`src/AgenticUI.AgentServer`).
