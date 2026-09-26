#!/usr/bin/env pwsh
<#
    Drawing - one-click installer for Graveyard Keeper 2.

    Installs BepInEx 5 (if missing) and drops Drawing.dll into BepInEx\plugins.
    Safe to re-run: it overwrites the plugin and leaves your drawings alone.
#>
[CmdletBinding()]
param(
    [string] $GameDir,
    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'

$BepInExVersion = '5.4.23.5'
$BepInExUrl     = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"

# --- locate the game ------------------------------------------------------------
function Find-Game {
    $candidates = @()
    if ($GameDir) { $candidates += $GameDir }
    # Steam default library locations
    $steamRoots = @(
        "${env:ProgramFiles(x86)}\Steam\steamapps\common",
        "$env:ProgramFiles\Steam\steamapps\common",
        'C:\Program Files (x86)\Steam\steamapps\common',
        'D:\Steam\steamapps\common',
        'E:\Steam\steamapps\common'
    ) | Where-Object { $_ -and (Test-Path $_) }
    foreach ($root in $steamRoots) { $candidates += (Join-Path $root 'Graveyard Keeper 2') }

    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c 'GraveyardKeeper2.exe')) { return $c }
    }
    return $null
}

$game = Find-Game
if (-not $game) {
    Write-Host "Could not find Graveyard Keeper 2." -ForegroundColor Red
    Write-Host "Pass the path explicitly:  .\install.ps1 -GameDir 'C:\path\to\Graveyard Keeper 2'"
    exit 1
}
Write-Host "Game: $game" -ForegroundColor Cyan

$bepDir  = Join-Path $game 'BepInEx'
$coreDir = Join-Path $bepDir 'core'
$plugDir = Join-Path $bepDir 'plugins'

# --- uninstall -----------------------------------------------------------------
if ($Uninstall) {
    Write-Host ''
    Write-Host 'Removing Drawing...' -ForegroundColor Yellow
    foreach ($f in @((Join-Path $plugDir 'Drawing.dll'), (Join-Path $plugDir 'Drawing.pdb'))) {
        if (Test-Path $f) { Remove-Item $f -Force; Write-Host "  removed $f" }
    }
    $plan = Join-Path $env:USERPROFILE 'AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\gk2_projections.json'
    if (Test-Path $plan) {
        Write-Host "  your drawings are still in: $plan" -ForegroundColor DarkGray
        Write-Host '  delete that file by hand if you want to drop the plan as well.'
    }
    Write-Host 'BepInEx itself was left in place - other mods may need it.' -ForegroundColor DarkGray
    exit 0
}

# --- BepInEx -------------------------------------------------------------------
$alreadyPatched = Test-Path (Join-Path $game 'winhttp.dll')
if ($alreadyPatched -and (Test-Path $coreDir)) {
    Write-Host 'BepInEx already installed.' -ForegroundColor DarkGray
} else {
    Write-Host ''
    Write-Host "Downloading BepInEx $BepInExVersion ..." -ForegroundColor Cyan
    $tmp = Join-Path $env:TEMP ("bepinex_" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $zip = Join-Path $tmp 'bepinex.zip'
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $BepInExUrl -OutFile $zip -UseBasicParsing
        Expand-Archive -Path $zip -DestinationPath (Join-Path $tmp 'x') -Force

        $src = Join-Path $tmp 'x'
        foreach ($f in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')) {
            Copy-Item (Join-Path $src $f) $game -Force
        }
        New-Item -ItemType Directory -Force -Path $coreDir, $plugDir, (Join-Path $bepDir 'patchers') | Out-Null
        Get-ChildItem (Join-Path $src 'BepInEx\core') -File | ForEach-Object {
            Copy-Item $_.FullName $coreDir -Force
        }
        Write-Host 'BepInEx installed.' -ForegroundColor Green
    } finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# --- plugin --------------------------------------------------------------------
$here = $PSScriptRoot
$dll = Join-Path $here 'Drawing.dll'
if (-not (Test-Path $dll)) {
    $dll = Join-Path $here 'BepInEx\core\Drawing.dll'
}
if (-not (Test-Path $dll)) {
    throw "Drawing.dll not found next to this script (looked in $here and $here\BepInEx\core)."
}

New-Item -ItemType Directory -Force -Path $plugDir | Out-Null
Copy-Item $dll (Join-Path $plugDir 'Drawing.dll') -Force
if (Test-Path ([IO.Path]::ChangeExtension($dll, '.pdb'))) {
    Copy-Item ([IO.Path]::ChangeExtension($dll, '.pdb')) (Join-Path $plugDir 'Drawing.pdb') -Force
}

Write-Host ''
Write-Host 'Drawing installed.' -ForegroundColor Green
Write-Host "  plugin : $plugDir\Drawing.dll"
Write-Host "  config : $plugDir\..\config\drawing.gk2.cfg   (created on first run)"
Write-Host '  plans  : %USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\gk2_projections.json'
Write-Host ''
Write-Host 'In build mode:  MMB place | Delete remove | F9 clear | H show/hide | F8 model variants'
