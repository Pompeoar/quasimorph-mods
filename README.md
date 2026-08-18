# Quasimorph Mods

Mods for [Quasimorph](https://store.steampowered.com/app/2059170/Quasimorph/), built
against the game's native mod loader. BepInEx is not required.

| Mod | What it does |
|---|---|
| [PerkCooldownHud](src/PerkCooldownHud) | Shows how many turns until a triggered perk is ready again |

## Layout

```
Directory.Build.props        common properties (target framework, game paths)
Directory.Build.targets      the game references, applied to mod projects only
build.ps1                    build / verify / deploy, one mod or all
src/<ModName>/               one folder per mod = one Workshop item
  <ModName>.csproj             identity only; shared plumbing is inherited
  modmanifest.json             what the game's loader reads
  patch-targets.json           the game members this mod patches (see Verifying)
tools/Verify/                dev-only verifier, never ships
tools/core-targets.json      mod-loader surface every mod depends on
dist/<ModName>/              exactly what gets uploaded to the Workshop
```

Each mod ships **only** its own dll and `modmanifest.json`. Every reference resolves from
the player's own install, and `0Harmony.dll` already comes with the game, so nothing of
the game's is redistributed.

## Building

Needs the .NET SDK. No Unity install required.

```powershell
.\build.ps1                          # build + verify + deploy everything
.\build.ps1 -Mod PerkCooldownHud     # just one
.\build.ps1 -NoDeploy                # build and stage, don't touch the game folder
.\build.ps1 -SkipVerify
.\build.ps1 -GameDir 'D:\Steam\steamapps\common\Quasimorph'
```

Deploys to
`%USERPROFILE%\AppData\LocalLow\Magnum Scriptum Ltd\Quasimorph\LocalUserPresets\<ModName>`.
In the current game build, `Bootstrap.InitMods` loads assemblies from Steam Workshop and
from `LocalUserPresets`, so that is the folder for local testing.

**Restart the game fully after a rebuild** — assemblies are not hot-reloaded. `build.ps1`
refuses to deploy while the game is running, because a running Quasimorph keeps the loaded
dll memory-mapped and the copy would otherwise fail with an opaque "user-mapped section
open" error.

## Verifying

```powershell
dotnet run --project tools\Verify
dotnet run --project tools\Verify -- "<Managed folder>" PerkCooldownHud
```

Harmony binds its targets **by name at runtime**. A rename in a game update therefore shows
up as "the mod silently does nothing" (for a patched method) or as an exception deep inside
patching (for a private field reached via `FieldRefAccess`) — never as a build error. This
is the gate that turns both into a readable failure.

It loads the **shipped** `Assembly-CSharp.dll` through a `MetadataLoadContext` — not a
decompiler dump, which can be stale — and checks every member each mod declares in its
`patch-targets.json`, plus the loader surface in `tools/core-targets.json`. Mods can also
add behavioural checks under `tools/Verify/Checks/` for logic a JSON file can't express,
such as replaying a mod's arithmetic independently of the mod's own code.

Run it after every game update.

## Adding a mod

1. `src/<ModName>/<ModName>.csproj` — identity only; `Directory.Build.*` supplies the rest.
2. `src/<ModName>/modmanifest.json` — `UniqueModName` should match the folder name.
3. A `[Hook(ModHookType.BeforeBootstrap)]` static entry point that applies
   `new Harmony("<ModName>")`. Use a per-mod Harmony id so patches stay attributable in
   logs and individually unpatchable when a user reports a conflict.
4. `src/<ModName>/patch-targets.json` declaring what it patches.
5. Optionally a checks class in `tools/Verify/Checks/`, registered in `Program.cs`.

`build.ps1` picks up the new folder automatically.

### Gotchas found in the loader

- `UserModSystem.GrabMethods` registers a hook method **twice** when the hook-type key
  already exists, so entry points must be safe to call more than once.
- `Dependencies` in `modmanifest.json` is matched **by `UniqueModName` only, with no
  version constraint**. A breaking change to a shared library won't disable dependents;
  they will load and then fail at runtime. Bump the unique name on a breaking change.
- Hook invocation order follows the **user's mod order in prefs**, not dependency order, so
  a library must not require its own entry point to have run first.

## Environment

Game build `1.0.1.566s.7e4da55`, Unity 2022.3, Harmony 2.3.3, `netstandard2.1`.
