<#
.SYNOPSIS
    Builds one or all Quasimorph mods in this repo and deploys them for local testing.

.DESCRIPTION
    Each mod lives in src\<ModName>\ and is published as its own Steam Workshop item from
    dist\<ModName>\. Only the mod's own dll and its modmanifest.json ship: every reference
    resolves from the player's install, and 0Harmony.dll already comes with the game.

    dev\<ModName>\ holds local-only helpers that must never reach the Workshop. They are
    built and deployed exactly like a real mod, but are never staged into dist\, and they
    are skipped unless -IncludeDev is passed.

    In the current game build, Bootstrap.InitMods loads assemblies from Steam Workshop and
    from Application.persistentDataPath\LocalUserPresets, so the latter is the local test
    folder. The game must be fully restarted to pick up a rebuilt assembly.

.EXAMPLE
    .\build.ps1                          # build + verify + deploy every shippable mod
    .\build.ps1 -Mod PerkCooldownHud     # just the one
    .\build.ps1 -IncludeDev              # also build/deploy the dev\ helpers
    .\build.ps1 -NoDeploy                # build and stage only
    .\build.ps1 -GameDir 'D:\Steam\steamapps\common\Quasimorph'
#>
[CmdletBinding()]
param(
    [string]$Mod,
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Quasimorph',
    [string]$Configuration = 'Release',
    [switch]$IncludeDev,
    [switch]$NoDeploy,
    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcRoot = Join-Path $root 'src'
$devRoot = Join-Path $root 'dev'
$managed = Join-Path $GameDir 'Quasimorph_Data\Managed'

if (-not (Test-Path $managed)) {
    throw "Could not find the game's Managed folder at '$managed'. Pass -GameDir."
}

function Get-ModFolders([string]$Path) {
    if (-not (Test-Path $Path)) { return @() }
    return @(Get-ChildItem $Path -Directory | Where-Object {
        Test-Path (Join-Path $_.FullName "$($_.Name).csproj")
    })
}

$shippable = Get-ModFolders $srcRoot
$devMods = Get-ModFolders $devRoot

# An explicit -Mod may name a dev helper, in which case wanting it is unambiguous and
# -IncludeDev would be redundant ceremony.
$mods = @($shippable) + $(if ($IncludeDev -or $Mod) { $devMods } else { @() })

if ($Mod) {
    $mods = @($mods | Where-Object { $_.Name -eq $Mod })
    if ($mods.Count -eq 0) {
        $available = (@($shippable) + @($devMods) | ForEach-Object Name) -join ', '
        throw "No mod named '$Mod' in src\ or dev\. Available: $available"
    }
}

if ($mods.Count -eq 0) { throw "No mods found under $srcRoot." }

$localMods = Join-Path $env:USERPROFILE 'AppData\LocalLow\Magnum Scriptum Ltd\Quasimorph\LocalUserPresets'

# The running game keeps loaded assemblies memory-mapped, so copying over one fails with an
# unhelpful "user-mapped section open" error. Check once, up front, and say so plainly.
if (-not $NoDeploy) {
    $running = Get-Process -Name 'Quasimorph' -ErrorAction SilentlyContinue
    if ($running) {
        Write-Warning "Quasimorph is running (PID $($running.Id -join ', ')); it holds the mod assemblies open."
        Write-Warning "Close the game and re-run, or pass -NoDeploy to build without deploying."
        exit 1
    }
}

foreach ($m in $mods) {
    $name = $m.Name
    $isDev = $m.FullName.StartsWith($devRoot, [StringComparison]::OrdinalIgnoreCase)
    Write-Host ''
    if ($isDev) {
        Write-Host "=== $name (dev helper - not published) ===" -ForegroundColor DarkCyan
    } else {
        Write-Host "=== $name ===" -ForegroundColor Cyan
    }

    $project = Join-Path $m.FullName "$name.csproj"
    $outDir = Join-Path $root "build\$name"
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

    dotnet build $project -c $Configuration -o $outDir "-p:GameManagedDir=$managed" --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $name." }

    $manifestPath = Join-Path $m.FullName 'modmanifest.json'
    if (-not (Test-Path $manifestPath)) { throw "$name has no modmanifest.json." }

    # The folder name under LocalUserPresets and on the Workshop should match the mod's
    # declared UniqueModName, or the loader and the config folders disagree.
    $uniqueName = (Get-Content $manifestPath -Raw | ConvertFrom-Json).UniqueModName
    if ($uniqueName -ne $name) {
        Write-Warning "$name\modmanifest.json declares UniqueModName '$uniqueName'; folder is '$name'."
    }

    if ($isDev) {
        # Deliberately never staged into dist\: dist\ is the set of folders that get passed
        # to mod_createworkshopitem, and a dev helper in there is one mistyped path away
        # from being published.
        $staging = $outDir
        Copy-Item $manifestPath $staging -Force
        Write-Host "built    -> $staging (not staged to dist\)" -ForegroundColor DarkGreen
    } else {
        $staging = Join-Path $root "dist\$name"
        if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $staging | Out-Null

        Copy-Item (Join-Path $outDir "$name.dll") $staging -Force
        Copy-Item $manifestPath $staging -Force

        $thumb = Join-Path $m.FullName 'thumbnail.png'
        if (Test-Path $thumb) {
            Copy-Item $thumb $staging -Force
        } else {
            Write-Warning "$name has no thumbnail.png; mod_updateworkshopitem will not set a preview image."
        }

        Write-Host "staged   -> $staging" -ForegroundColor Green
    }

    if ($NoDeploy) { continue }

    $target = Join-Path $localMods $name
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $staging "$name.dll") $target -Force
    Copy-Item (Join-Path $staging 'modmanifest.json') $target -Force
    Write-Host "deployed -> $target" -ForegroundColor Green
}

if (-not $SkipVerify) {
    Write-Host ''
    Write-Host '=== verify ===' -ForegroundColor Cyan

    # dotnet run forwards unrecognised switches to the app, so keep the argument list to
    # exactly what Verify expects. An empty -Mod must not become an empty filter argument.
    $verifyArgs = @($managed)
    if ($Mod) { $verifyArgs += $Mod }

    dotnet run --project (Join-Path $root 'tools\Verify') -- @verifyArgs
    if ($LASTEXITCODE -ne 0) { throw "Verification failed." }
}

if (-not $NoDeploy) {
    Write-Host ''
    Write-Host 'Fully restart Quasimorph to load the new assemblies.' -ForegroundColor Yellow
}
