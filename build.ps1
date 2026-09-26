#!/usr/bin/env pwsh
# Builds the plugin and runs the projection-store unit tests.
[CmdletBinding()]
param([switch] $SkipTests)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'

if (-not (Test-Path $dotnet)) {
    throw "Local .NET SDK not found at $dotnet - run .tools\dotnet-install.ps1 first."
}
$env:DOTNET_ROOT = Join-Path $root '.tools\dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Write-Host '--- building plugin ---' -ForegroundColor Cyan
& $dotnet build (Join-Path $root 'src\Drawing\Drawing.csproj') -c Release -v m --nologo
if ($LASTEXITCODE -ne 0) { throw "plugin build failed" }

if ($SkipTests) { return }

Write-Host ''
Write-Host '--- building tests ---' -ForegroundColor Cyan
& $dotnet build (Join-Path $root 'src\Drawing.Tests\Drawing.Tests.csproj') -c Release -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "test build failed" }

Write-Host ''
Write-Host '--- running tests ---' -ForegroundColor Cyan
# The suite deliberately writes to stderr (the "corrupted plan file" case logs an error).
# Windows PowerShell promotes native stderr to a terminating error, so relax the preference
# for the run and judge the result by the exit code alone.
$ErrorActionPreference = 'Continue'
& $dotnet (Join-Path $root 'src\Drawing.Tests\bin\Release\net8.0\Drawing.Tests.dll') 2>&1 |
    ForEach-Object { Write-Host $_ }
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($code -ne 0) { throw "tests FAILED (exit $code)" }


