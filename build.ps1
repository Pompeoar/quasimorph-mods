<#
.SYNOPSIS
    Builds PerkCooldownHud and deploys it into Quasimorph's local mod folder.

.DESCRIPTION
    In the current game build, Bootstrap.InitMods only loads mods from Steam Workshop and
    from Application.persistentDataPath\LocalUserPresets. LocalUserPresets does load mod
    assemblies, so it is the folder to use for local testing.

    The game must be fully restarted to pick up a rebuilt assembly.
#>
[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Quasimorph',
    [string]$Configuration = 'Release',
    [switch]$NoDeploy
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\PerkCooldownHud\PerkCooldownHud.csproj'
$managed = Join-Path $GameDir 'Quasimorph_Data\Managed'

if (-not (Test-Path $managed)) {
    throw "Could not find the game's Managed folder at '$managed'. Pass -GameDir."
}

$outDir = Join-Path $root 'build'
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

Write-Host "Building $Configuration ..." -ForegroundColor Cyan
dotnet build $project -c $Configuration -o $outDir "-p:GameManagedDir=$managed"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# Only our own dll and the manifest ship. Every reference is resolved from the
# player's own install at runtime, so nothing of the game's is redistributed.
$staging = Join-Path $root 'dist\PerkCooldownHud'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

Copy-Item (Join-Path $outDir 'PerkCooldownHud.dll') $staging -Force
Copy-Item (Join-Path $root 'src\PerkCooldownHud\modmanifest.json') $staging -Force

$thumb = Join-Path $root 'thumbnail.png'
if (Test-Path $thumb) { Copy-Item $thumb $staging -Force }

Write-Host "Staged mod at $staging" -ForegroundColor Green

if ($NoDeploy) { return }

$localMods = Join-Path $env:USERPROFILE 'AppData\LocalLow\Magnum Scriptum Ltd\Quasimorph\LocalUserPresets'
$target = Join-Path $localMods 'PerkCooldownHud'
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item (Join-Path $staging '*') $target -Recurse -Force

Write-Host "Deployed to $target" -ForegroundColor Green
Write-Host "Fully restart Quasimorph to load the new assembly." -ForegroundColor Yellow
