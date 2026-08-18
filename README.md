# Perk Cooldown HUD

A small Quasimorph mod that shows you when a triggered perk will be ready again.

Vanilla shows a perk's HUD icon only while the perk is *active*. The moment the active
phase ends the icon vanishes and you are left guessing how many turns remain before
Blurred Silhouette (or any other triggered perk) comes back. This mod keeps the icon on
screen through the cooldown, dimmed, with a turn counter.

| Perk state | Vanilla | With this mod |
|---|---|---|
| Ready | no icon | no icon |
| Active | icon + turns of active phase left | unchanged |
| Cooling down | **no icon** | dimmed icon + turns until ready |
| Ready again | - | icon disappears |

## How it works

The game already tracks everything needed; it just declines to draw it.

`PerkTrigger` is a `BaseEffect` created the instant a triggered perk fires. Its single
`Duration` counts down the active phase and then the cooldown, and the effect is removed
when `Duration` hits 0. `PerkSystem.ApplyPerkTrigger` only ever runs when
`GetTrigger(...)` returns `null`, so "a `PerkTrigger` exists" means exactly "this perk is
active or cooling down".

What hid it was one line:

```csharp
public bool Show => IsInActivePhase;   // PerkTrigger
```

`EffectsView` calls `panel.gameObject.SetActive(effect.Show)` each tick, so the panel was
being switched off for the whole cooldown half of the effect's life.

The mod is four Harmony postfixes:

| Member | Vanilla | Patched to |
|---|---|---|
| `PerkTrigger.Show` | `IsInActivePhase` | `true` |
| `PerkTrigger.ViewValue` | `ActivePhaseDuration - abs(Duration - OriginalDuration)` | `Duration` while cooling |
| `PerkTrigger.BlinkOnChange` | `true` | `IsInActivePhase` |
| `PerkTrigger.IsRedView` | `false` | optional red border while cooling (off by default) |

Two details that matter:

- **`ViewValue` runs negative during cooldown.** Reporting `Duration` instead matches what
  `MercenaryClassScreen` already displays as "turns of cooldown remaining" on the character
  sheet, so the HUD and the character screen agree.
- **`BlinkOnChange` had to be narrowed.** `CommonEffectPanel` flashes white and plays the
  "effect received" sound on every value change. The cooldown counter changes every turn,
  so leaving it alone would ping the player once per turn for the entire cooldown.

Dimming is done with a `CanvasGroup` added to the panel rather than by tinting each
`Image`, because the panel's own `Update()` drives the white flash overlay's colour and a
`CanvasGroup` composes with that instead of fighting it.

Perks with `ICDRecovery` tick their cooldown down faster than one per turn. Because the
readout is `Duration` itself rather than a count of elapsed turns, it stays truthful
automatically.

## Building

Requires the .NET SDK. All references resolve from your own Quasimorph install; nothing
from the game is redistributed, and `0Harmony.dll` already ships with the game so it is
not bundled either.

```powershell
.\build.ps1                 # build, stage to dist\, deploy to the local mod folder
.\build.ps1 -NoDeploy       # build and stage only
.\build.ps1 -GameDir 'D:\Steam\steamapps\common\Quasimorph'
```

Deploys to `%USERPROFILE%\AppData\LocalLow\Magnum Scriptum Ltd\Quasimorph\LocalUserPresets\PerkCooldownHud`.
In the current game build `Bootstrap.InitMods` loads assemblies from Steam Workshop and
from `LocalUserPresets`, so that is the folder for local testing. **Restart the game fully**
after a rebuild; assemblies are not hot-reloaded.

## Verifying

```powershell
dotnet run --project tools\VerifyPatchTargets
```

Harmony resolves its targets by name at runtime, so a rename in a game update would
otherwise show up as "the mod silently does nothing". This tool loads the **shipped**
`Assembly-CSharp.dll` through a `MetadataLoadContext` and asserts every patched member
still exists with the expected signature, then replays the countdown arithmetic
independently to catch off-by-one errors:

```
active 3 / cooldown 5  ->  active shows 3,2,1   cooling shows 5,4,3,2,1
```

Run it after every game update.

## Configuration

`src/PerkCooldownHud/Config.cs`:

- `CooldownAlpha` (default `0.45`) - panel opacity while cooling down.
- `RedBorderWhileCoolingDown` (default `false`) - also flip the border to the red
  "bad effect" sprite. Off because red means "debuff" everywhere else in this HUD.

## Compatibility

- Game build `1.0.1.566s.7e4da55`, Unity 2022.3, Harmony 2.3.3.
- Uses the game's native mod loader. BepInEx is not required.
- Touches display code only. No save data is added or changed, and disabling the mod
  reverts to vanilla behaviour with no migration.
