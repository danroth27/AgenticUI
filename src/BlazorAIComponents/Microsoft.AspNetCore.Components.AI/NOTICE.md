# Bundled copy: Microsoft.AspNetCore.Components.AI

This folder is a **local copy of the source** for the in-progress Blazor AI components
(`Microsoft.AspNetCore.Components.AI`) authored by @javiercn. It is not a fork or a product —
just a snapshot checked in so this sample builds on its own.

- Upstream: https://github.com/dotnet/aspnetcore (`src/Components/AI`)
- Tracking PR: https://github.com/dotnet/aspnetcore/pull/67673
- Snapshot branch: `javiercn/components-ai-full`
- Snapshot commit: `e0618328e0b5571ac8b2c5d189dabc6c576beb53`
- Snapshot date: 2026-07-29
- License: MIT (see https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt)

> The PR branch is regularly rebased, so recorded commits are force-pushed away. Verified on
> 2026-07-29 that every file under `src/Components/AI/src` is byte-identical to this copy apart
> from the local patches listed below.

## Why the source is copied in

These components are **not yet published as a NuGet package**. Keeping a local copy of the source
makes this sample fully standalone: clone the repo and `dotnet build` with only the .NET 10 SDK,
with no dependency on an aspnetcore checkout. Everything else in the sample uses released NuGet
packages.

## Refreshing the copy

`pwsh eng/sync-components-ai.ps1 -AspNetCoreRepo <path to dotnet/aspnetcore clone>`

> **Note:** this sample carries local patches on top of the snapshot (see *Local modifications*
> below). The sync script overwrites the copy, so re-apply them (or drop them once the equivalent
> fix lands upstream).

## Local modifications

This copy carries two small patches on top of the snapshot.

### 1. Frontend tools should not wait for a human (`UIActionBlock`)

While building this sample we found that a **frontend tool** (`UIActionBlock`) is treated by the
engine exactly like a human-approval block (`FunctionApprovalBlock`): both implement
`IInteractiveBlock`, so `AgentContext` waits for an *external* result and the run stalls until app
code invokes the action. A frontend tool has no human step — the model declared it precisely so the
browser runs it automatically — so this forced every consuming page to add glue (a `UIActionRunner`
component) just to un-stall the run.

This copy patches the engine to auto-invoke `UIActionBlock`s and to only enter `AwaitingInput` for
blocks that genuinely need a person:

- `Engine/AgentContext.cs` — auto-invoke `UIActionBlock`s; gate `AwaitingInput` on a non-`UIActionBlock`
  interactive block.
- `Blocks/UIActionBlock.cs` — make `InvokeAsync` idempotent and surface failures through the run.

The behavior is verified end-to-end (frontend tool auto-runs and the run resumes; human approval
still stalls at `AwaitingInput` until approved). The same fix is suggested upstream on PR #67673.

### 2. The message box is not cleared when you submit with Enter

- `Components/MessageInput.cs`

Upstream commit `51a18baa` ("Clear the message textarea after sending") fixes the **send button**
path, but submitting with **Enter** still leaves the text in the box. The browser inserts a newline
for that keypress and raises `input` *after* the submit handler has run, so the stale text is
re-rendered over the cleared value. The send button is unaffected because no `input` event competes
with it.

The patch ignores the single `input` event that follows an Enter submit. Verified: Enter sends and
clears, Shift+Enter inserts a newline without sending, and the send button still sends and clears.

## When the official package ships

Delete `src/BlazorAIComponents` and replace the two `ProjectReference` items in
`AgenticUI.Web` with a single `PackageReference Include="Microsoft.AspNetCore.Components.AI"`.
The assembly name and namespace are identical, so no code changes are required.
