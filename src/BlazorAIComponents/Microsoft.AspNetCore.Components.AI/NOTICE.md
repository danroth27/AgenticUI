# Bundled copy: Microsoft.AspNetCore.Components.AI

This folder is a **local copy of the source** for the in-progress Blazor AI components
(`Microsoft.AspNetCore.Components.AI`) authored by @javiercn. It is not a fork or a product —
just a snapshot checked in so this sample builds on its own.

- Upstream: https://github.com/dotnet/aspnetcore (`src/Components/AI`)
- Tracking PR: https://github.com/dotnet/aspnetcore/pull/67673
- Snapshot branch: `javiercn/components-ai-full`
- Snapshot commit: `83d9b95daeffc954e2491ea77d25b145b005475b`
- Snapshot date: 2026-07-22
- License: MIT (see https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt)

## Why the source is copied in

These components are **not yet published as a NuGet package**. Keeping a local copy of the source
makes this sample fully standalone: clone the repo and `dotnet build` with only the .NET 10 SDK,
with no dependency on an aspnetcore checkout. Everything else in the sample uses released NuGet
packages.

## Refreshing the copy

`pwsh eng/sync-components-ai.ps1 -AspNetCoreRepo <path to dotnet/aspnetcore clone>`

> **Note:** this sample carries a small local patch on top of the snapshot (see *Local modifications*
> below). Re-running the sync script overwrites the copy, so re-apply the patch (or drop it once the
> equivalent fix lands upstream).

## Local modifications

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


## When the official package ships

Delete `src/BlazorAIComponents` and replace the two `ProjectReference` items in
`AgenticUI.Web` with a single `PackageReference Include="Microsoft.AspNetCore.Components.AI"`.
The assembly name and namespace are identical, so no code changes are required.
