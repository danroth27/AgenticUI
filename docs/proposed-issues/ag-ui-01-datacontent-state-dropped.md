# [Bug]: .NET `AGUI.Server` silently drops shared state emitted as `DataContent` (no `STATE_SNAPSHOT`, no diagnostic)

**Target repo:** `ag-ui-protocol/ag-ui`
**Area:** .NET SDK — `AGUI.Server`
**Severity:** Medium — a state-emitting agent can produce no state event and no error, which is hard to diagnose.

## Summary

When a server agent streams shared state as a `Microsoft.Extensions.AI.DataContent` with media type
`application/json`, `AGUI.Server`'s `ChatResponseUpdate` → AG-UI event conversion neither emits a
`STATE_SNAPSHOT` nor logs anything — the content is silently discarded.

Scope note: I'm **not** asserting `DataContent` must map to a state event. The shipped state contract
appears to be `ChatResponseUpdate.RawRepresentation = StateSnapshotEvent` /
`AGUIStreamOptions.MapResultAsStateSnapshot(...)`, and that works. The bug is the **silent** drop:
unrecognized content vanishes with no diagnostic.

## Reproduction

Minimal, deterministic repro (no LLM, no credentials):
**https://github.com/danroth27/agui-datacontent-state-repro**

It maps two AG-UI endpoints that emit the **same** state object two different ways via stub chat
clients (isolating the conversion):

| Endpoint | State emitted as | Result |
|----------|------------------|--------|
| `POST /state_via_datacontent` | `new DataContent(json, "application/json")` | no `STATE_SNAPSHOT` |
| `POST /state_via_rawrepresentation` | `RawRepresentation = new StateSnapshotEvent { Snapshot = ... }` | `STATE_SNAPSHOT` present |

```bash
dotnet run
BODY='{"threadId":"t1","runId":"r1","messages":[{"role":"user","content":"recipe"}]}'
curl -sN -X POST http://localhost:5320/state_via_datacontent \
  -H 'Content-Type: application/json' -H 'Accept: text/event-stream' -d "$BODY" \
  | grep -o '"type":"[^"]*"'
```

## Expected behavior

Emitting state as `DataContent("application/json")` should **not** disappear without a trace. Either:

- the content is translated to a `STATE_SNAPSHOT` event, **or**
- if `DataContent` is intentionally unsupported for state, `AGUI.Server` logs a warning identifying
  the dropped content type and pointing at the supported path
  (`RawRepresentation = StateSnapshotEvent` / `MapResultAsStateSnapshot`).

## Actual behavior

The `DataContent` update is dropped. The `/state_via_datacontent` stream contains only run/text
events and **no** `STATE_SNAPSHOT`, and nothing is logged:

```
RUN_STARTED
TEXT_MESSAGE_START
TEXT_MESSAGE_CONTENT
TEXT_MESSAGE_END
RUN_FINISHED
```

The `/state_via_rawrepresentation` endpoint, emitting the identical payload, produces:

```
RUN_STARTED
STATE_SNAPSHOT          <-- present
TEXT_MESSAGE_START
TEXT_MESSAGE_CONTENT
TEXT_MESSAGE_END
RUN_FINISHED
```

## Root cause

In `sdks/dotnet/src/AGUI.Server/ChatResponseUpdateAGUIExtensions.cs`, the per-content `switch`
(~line 205) handles `TextReasoningContent`, `TextContent`, `FunctionCallContent`,
`FunctionResultContent`, `ToolApprovalRequestContent`, and `InterruptRequestContent`. There is no
`DataContent` case; the `default` arm only consults `options.InvokeInterruptMappers(content)`, so any
unmapped content (including `DataContent`) is discarded with no log.

## Suggested resolution (pick one)

- **Preferred:** log a debug/warning diagnostic when the converter drops content it can't map, so a
  silent no-op becomes discoverable.
- **Optional:** additionally accept `DataContent` with `application/json` → `STATE_SNAPSHOT` and
  `application/json-patch+json` → `STATE_DELTA` as a convenience (a design call for the maintainers).

## Environment

- `AGUI.Server` / `AGUI.Abstractions` 0.0.4
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.15.0-preview.260722.1
- `Microsoft.Agents.AI` 1.15.0
- .NET 10.0.302
