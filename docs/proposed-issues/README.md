# Proposed issues & upstream feedback (drafts for review)

These are **drafts** written while building the AgenticUI sample and updating the MAF AG-UI docs.
Nothing here has been filed. Filing on external repos needs @danroth27's go-ahead.

Each finding was mapped to the repo that actually owns the code, and checked against existing
issues to avoid duplicates (searched 2026-07-23).

## To file (not currently tracked)

| # | File | Target repo | Kind |
|---|------|-------------|------|
| 1 | [`ag-ui-01-datacontent-state-dropped.md`](ag-ui-01-datacontent-state-dropped.md) | `ag-ui-protocol/ag-ui` | Bug (high) |
| 2 | [`ag-ui-02-dojo-example-stale.md`](ag-ui-02-dojo-example-stale.md) | `ag-ui-protocol/ag-ui` | Bug / chore (can be a PR) |

## Already tracked — comment, do NOT open a duplicate

| Finding | Existing issue | Suggested action |
|---------|----------------|------------------|
| No `conditional` approval mode (only always-require) | [dotnet/extensions#7449](https://github.com/dotnet/extensions/issues/7449) (open) | 👍 / add a note that MAF AG-UI HITL hits the same gap. No new issue. |
| Workflow-over-AG-UI events not surfaced | [microsoft/agent-framework#2494](https://github.com/microsoft/agent-framework/issues/2494) (open) | Comment with the specifics below — [`agent-framework-2494-comment.md`](agent-framework-2494-comment.md). |
| Multi-agent workflow: which agent is running | [microsoft/agent-framework#4902](https://github.com/microsoft/agent-framework/issues/4902) (open) | Already covered; nothing to add. |

## Blazor AI components (javiercn's PR) — comment + sample fix, not an issue

Per @danroth27: component feedback goes as a **comment on the PR we copied from**, and we address
the issues in our sample's copy where we can.

- Draft PR comment: [`aspnetcore-67673-comment.md`](aspnetcore-67673-comment.md)
- Sample fix already committed: frontend-tool (`UIActionBlock`) auto-invocation in the components
  copy (see `src/BlazorAIComponents/.../NOTICE.md` → *Local modifications*).
- Epic tracking issue for the library: [dotnet/aspnetcore#66178](https://github.com/dotnet/aspnetcore/issues/66178).

## Not filed (by design / low value)

- **Approval APIs are `[Experimental]` (`MEAI001`)** — intentional until the API graduates; the
  `#pragma` is expected. Noted in the docs, not worth an issue.
- **Client resume boilerplate (`ThreadId`/`ParentRunId` via `RawRepresentationFactory`)** — works;
  ergonomic only. `State`/`ParentRunId` already forward (see ag-ui#2151). Track as DX, not a bug.
