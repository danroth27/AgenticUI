# Bundled copy: Microsoft.AspNetCore.Components.AI

This folder is a **local copy of the source** for the in-progress Blazor AI components
(`Microsoft.AspNetCore.Components.AI`) authored by @javiercn. It is not a fork or a product —
just a snapshot checked in so this sample builds on its own.

- Upstream: https://github.com/dotnet/aspnetcore (`src/Components/AI`)
- Tracking branch: `javiercn-components-ai-09-predictive-state`
- Snapshot branch: `javiercn-components-ai-09-predictive-state`
- Snapshot commit: `dd8b97ed95a5b53b4e384e86a670fe3c11f64323`
- Snapshot date: 2026-08-26
- License: MIT (see https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt)

> The PR branch is regularly rebased, so recorded commits are force-pushed away. Compare the trees
> rather than the SHAs when checking whether this copy is current.

## Why the source is copied in

These components are **not yet published as a NuGet package**. Keeping a local copy of the source
makes this sample fully standalone: clone the repo and `dotnet build` with only the .NET 10 SDK,
with no dependency on an aspnetcore checkout. Everything else in the sample uses released NuGet
packages.

## Refreshing the copy

`pwsh eng/sync-components-ai.ps1 -AspNetCoreRepo <path to dotnet/aspnetcore clone>`

## When the official package ships

Delete `src/BlazorAIComponents` and replace the two `ProjectReference` items in
`AgenticUI.Web` with a single `PackageReference Include="Microsoft.AspNetCore.Components.AI"`.
The assembly name and namespace are identical, so no code changes are required.
