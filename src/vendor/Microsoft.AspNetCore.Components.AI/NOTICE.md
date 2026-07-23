# Vendored: Microsoft.AspNetCore.Components.AI

This folder contains a **source snapshot** of the in-progress Blazor AI components
(`Microsoft.AspNetCore.Components.AI`) authored by @javiercn.

- Upstream: https://github.com/dotnet/aspnetcore (`src/Components/AI`)
- Tracking PR: https://github.com/dotnet/aspnetcore/pull/67673
- Snapshot branch: `javiercn/components-ai-full`
- Snapshot commit: `83d9b95daeffc954e2491ea77d25b145b005475b`
- Snapshot date: 2026-07-22
- License: MIT (see https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt)

## Why this is vendored

These components are **not yet published as a NuGet package**. Vendoring the source keeps this
sample fully standalone: clone the repo and `dotnet build` with only the .NET 10 SDK, with no
dependency on an aspnetcore checkout. Everything else in the sample uses released NuGet packages.

## Refreshing the snapshot

`pwsh eng/sync-components-ai.ps1 -AspNetCoreRepo <path to dotnet/aspnetcore clone>`

## When the official package ships

Delete `src/vendor` and replace the two `ProjectReference` items in
`AgenticUI.Web` with a single `PackageReference Include="Microsoft.AspNetCore.Components.AI"`.
The assembly name and namespace are identical, so no code changes are required.
