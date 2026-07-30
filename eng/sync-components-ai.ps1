#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Refreshes the bundled copy of the in-progress Microsoft.AspNetCore.Components.AI library
    (javiercn's Blazor AI components, aspnetcore PR #67673 / branch javiercn/components-ai-full)
    from a local aspnetcore clone.

.DESCRIPTION
    The Blazor AI components are not yet published as a NuGet package. This sample keeps a local
    copy of their source so the repo is fully standalone (clone + `dotnet build` with only the
    .NET 10 SDK). Run this script to update the copy from a newer aspnetcore checkout. When the
    official Microsoft.AspNetCore.Components.AI package ships, delete src/BlazorAIComponents and
    replace the ProjectReferences with a PackageReference of the same name.

.PARAMETER AspNetCoreRepo
    Path to a local dotnet/aspnetcore clone checked out on a branch that contains
    src/Components/AI. Defaults to a sibling clone at ..\..\..\dotnet\aspnetcore.
#>
[CmdletBinding()]
param(
    [string]$AspNetCoreRepo = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\dotnet\aspnetcore") -ErrorAction SilentlyContinue)
)

$ErrorActionPreference = "Stop"

if (-not $AspNetCoreRepo -or -not (Test-Path $AspNetCoreRepo)) {
    throw "aspnetcore repo not found. Pass -AspNetCoreRepo <path to dotnet/aspnetcore clone>."
}

$componentsAi = Join-Path $AspNetCoreRepo "src\Components\AI"
$srcRoot = Join-Path $componentsAi "src"
$genRoot = Join-Path $componentsAi "gen"
if (-not (Test-Path $srcRoot)) { throw "Could not find $srcRoot. Is the clone on the components-ai branch?" }

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$libDir = Join-Path $repoRoot "src\BlazorAIComponents\Microsoft.AspNetCore.Components.AI"
$genDir = Join-Path $repoRoot "src\BlazorAIComponents\Microsoft.AspNetCore.Components.AI.SourceGenerators"

function Sync-Tree([string]$from, [string]$to, [string[]]$excludeDirs) {
    Get-ChildItem $to -Recurse -Filter *.cs -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem $to -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -in @('Attributes','Blocks','Components','Engine','Pipeline') } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    $files = Get-ChildItem $from -Recurse -Filter *.cs | Where-Object {
        $rel = $_.FullName.Substring($from.Length).TrimStart('\','/')
        $top = ($rel -split '[\\/]')[0]
        ($top -notin $excludeDirs) -and ($rel -notmatch '[\\/](bin|obj)[\\/]')
    }
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($from.Length).TrimStart('\','/')
        $dest = Join-Path $to $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
        Copy-Item $f.FullName $dest -Force
    }
    return $files.Count
}

$libCount = Sync-Tree $srcRoot $libDir @('bin','obj')
$genCount = Sync-Tree $genRoot $genDir @('bin','obj','test')

# Copy the component stylesheet (static web asset) so the local-copy RCL serves it under
# _content/Microsoft.AspNetCore.Components.AI/.
$wwwrootFrom = Join-Path $srcRoot "wwwroot"
$wwwrootTo = Join-Path $libDir "wwwroot"
if (Test-Path $wwwrootFrom) {
    if (Test-Path $wwwrootTo) { Remove-Item $wwwrootTo -Recurse -Force }
    Copy-Item $wwwrootFrom $wwwrootTo -Recurse -Force
}

$commit = (git -C $AspNetCoreRepo rev-parse HEAD).Trim()
$branch = (git -C $AspNetCoreRepo rev-parse --abbrev-ref HEAD).Trim()
$stamp = (Get-Date).ToString("yyyy-MM-dd")

# Files this sample patches on top of the snapshot. Sync-Tree overwrites them, so the patches must
# be re-applied afterwards (or dropped once the equivalent fix lands upstream). Keep this list and
# the "Local modifications" section of NOTICE.md in step.
$patchedFiles = @(
    "Engine\AgentContext.cs",
    "Blocks\UIActionBlock.cs",
    "Components\MessageInput.cs"
)

$localMods = @'
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
'@

$notice = @"
# Bundled copy: Microsoft.AspNetCore.Components.AI

This folder is a **local copy of the source** for the in-progress Blazor AI components
(``Microsoft.AspNetCore.Components.AI``) authored by @javiercn. It is not a fork or a product —
just a snapshot checked in so this sample builds on its own.

- Upstream: https://github.com/dotnet/aspnetcore (``src/Components/AI``)
- Tracking PR: https://github.com/dotnet/aspnetcore/pull/67673
- Snapshot branch: ``$branch``
- Snapshot commit: ``$commit``
- Snapshot date: $stamp
- License: MIT (see https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt)

> The PR branch is regularly rebased, so recorded commits are force-pushed away. Compare the trees
> rather than the SHAs when checking whether this copy is current.

## Why the source is copied in

These components are **not yet published as a NuGet package**. Keeping a local copy of the source
makes this sample fully standalone: clone the repo and ``dotnet build`` with only the .NET 10 SDK,
with no dependency on an aspnetcore checkout. Everything else in the sample uses released NuGet
packages.

## Refreshing the copy

``pwsh eng/sync-components-ai.ps1 -AspNetCoreRepo <path to dotnet/aspnetcore clone>``

> **Note:** this sample carries local patches on top of the snapshot (see *Local modifications*
> below). The sync script overwrites the copy, so re-apply them (or drop them once the equivalent
> fix lands upstream).

$localMods

## When the official package ships

Delete ``src/BlazorAIComponents`` and replace the two ``ProjectReference`` items in
``AgenticUI.Web`` with a single ``PackageReference Include="Microsoft.AspNetCore.Components.AI"``.
The assembly name and namespace are identical, so no code changes are required.
"@
Set-Content -Path (Join-Path $libDir "NOTICE.md") -Value $notice -Encoding utf8

Write-Host "Copied $libCount library files and $genCount source-generator files from $branch@$($commit.Substring(0,10))."

Write-Warning "This sample patches the following files. The sync just overwrote them - re-apply the patches (see NOTICE.md, 'Local modifications'), or drop them if the fix has landed upstream:"
foreach ($p in $patchedFiles) { Write-Warning "  $p" }
