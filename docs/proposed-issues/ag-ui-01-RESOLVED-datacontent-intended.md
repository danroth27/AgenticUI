# RESOLVED (not a bug — will not file): `DataContent` state is intentionally unsupported

**Status:** Closed without filing. Confirmed with Javier Calvarro (component/AG-UI .NET), 2026-07-23.

## Question we asked

`AGUI.Server` (0.0.4) does not convert shared state emitted as `DataContent("application/json")` into
a `STATE_SNAPSHOT` — the update is dropped with no event and no log. Bug, or intended?

## Answer

**Intended.** Emitting state as `DataContent("application/json")` was a hack from before there was a
public API for it. It is no longer supported. The supported way to emit shared state from .NET is to
emit a chat update whose `RawRepresentation` is a `StateSnapshotEvent` (and `StateDeltaEvent` for
deltas):

```csharp
yield return new ChatResponseUpdate
{
    Role = ChatRole.Assistant,
    RawRepresentation = new StateSnapshotEvent { Snapshot = snapshotJsonElement }
};
```

This is already the pattern used by `AgenticUI.AgentServer` and by the updated MAF Learn docs (PR #430),
so **no doc or sample changes are needed on our side**.

## Follow-ups that remain

- **Stale `AGUIDojoServer` sample** (still uses the removed `DataContent` hack, plus `AddAGUI`/`MapAGUI`
  and the old `ag_ui_state` input key) — this is the real actionable item. See
  [`ag-ui-02-dojo-example-stale.md`](ag-ui-02-dojo-example-stale.md) and todo `update-agui-dojo`. The
  state-emit fix should use `RawRepresentation = StateSnapshotEvent` per Javier's guidance.
- **Optional DX idea (not filing unless Javier wants it):** have `AGUI.Server` log a warning when it
  drops content it can't map, so the removed pattern fails loudly instead of silently.
- **Repro repo** `danroth27/agui-datacontent-state-repro` — no longer a bug repro. Keep as a
  minimal "how to emit AG-UI state from .NET (supported vs. removed pattern)" demo, or delete. TBD.
