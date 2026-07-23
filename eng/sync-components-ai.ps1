#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Refreshes the vendored snapshot of the in-progress Microsoft.AspNetCore.Components.AI
    library (javiercn's Blazor AI components, aspnetcore PR #67673 / branch
    javiercn/components-ai-full) from a local aspnetcore clone.

.DESCRIPTION
    The Blazor AI components are not yet published as a NuGet package. This sample vendors a
    snapshot of their source so the repo is fully standalone (clone + `dotnet build` with only
    the .NET 10 SDK). Run this script to update the snapshot from a newer aspnetcore checkout.
    When the official Microsoft.AspNetCore.Components.AI package ships, delete src/vendor and
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
$vendorLib = Join-Path $repoRoot "src\vendor\Microsoft.AspNetCore.Components.AI"
$vendorGen = Join-Path $repoRoot "src\vendor\Microsoft.AspNetCore.Components.AI.SourceGenerators"

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

$libCount = Sync-Tree $srcRoot $vendorLib @('bin','obj')
$genCount = Sync-Tree $genRoot $vendorGen @('bin','obj','test')

# Copy the component stylesheet (static web asset) so the vendored RCL serves it under
# _content/Microsoft.AspNetCore.Components.AI/.
$wwwrootFrom = Join-Path $srcRoot "wwwroot"
$wwwrootTo = Join-Path $vendorLib "wwwroot"
if (Test-Path $wwwrootFrom) {
    if (Test-Path $wwwrootTo) { Remove-Item $wwwrootTo -Recurse -Force }
    Copy-Item $wwwrootFrom $wwwrootTo -Recurse -Force
}

$commit = (git -C $AspNetCoreRepo rev-parse HEAD).Trim()
$branch = (git -C $AspNetCoreRepo rev-parse --abbrev-ref HEAD).Trim()
$stamp = (Get-Date).ToString("yyyy-MM-dd")

$notice = @"
# Vendored: Microsoft.AspNetCore.Components.AI

This folder contains a **source snapshot** of the in-progress Blazor AI components
(``Microsoft.AspNetCore.Components.AI``) authored by @javiercn.

- Upstream: https://github.com/dotnet/aspnetcore (``src/Components/AI``)
- Tracking PR: https://github.com/dotnet/aspnetcore/pull/67673
- Snapshot branch: ``$branch``
- Snapshot commit: ``$commit``
- Snapshot date: $stamp
- License: MIT (see https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt)

## Why this is vendored

These components are **not yet published as a NuGet package**. Vendoring the source keeps this
sample fully standalone: clone the repo and ``dotnet build`` with only the .NET 10 SDK, with no
dependency on an aspnetcore checkout. Everything else in the sample uses released NuGet packages.

## Refreshing the snapshot

``pwsh eng/sync-components-ai.ps1 -AspNetCoreRepo <path to dotnet/aspnetcore clone>``

## When the official package ships

Delete ``src/vendor`` and replace the two ``ProjectReference`` items in
``AgenticUI.Web`` with a single ``PackageReference Include="Microsoft.AspNetCore.Components.AI"``.
The assembly name and namespace are identical, so no code changes are required.
"@
Set-Content -Path (Join-Path $vendorLib "NOTICE.md") -Value $notice -Encoding utf8

Write-Host "Vendored $libCount library files and $genCount source-generator files from $branch@$($commit.Substring(0,10))."
