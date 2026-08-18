# Perk Cooldown HUD

A small Quasimorph mod that shows you when a triggered perk will be ready again.

Vanilla shows a perk's HUD icon only while the perk is *active*. The moment the active
phase ends the icon vanishes and you are left guessing how many turns remain before
Blurred Silhouette (or any other triggered perk) comes back. This mod keeps the icon on
screen through the cooldown, dimmed, with a turn counter.

| Perk state | Vanilla | With this mod |
|---|---|---|
| Ready | no icon | no icon |
| Active | icon + turns of active phase left | unchanged (green border) |
| Cooling down | **no icon** | yellow border + turns until ready, dimmed |
| Ready again | - | icon disappears |

Green means active, yellow means cooling, no icon means ready. The border carries the
signal because green/red already mean buff/debuff elsewhere in the HUD, so a third colour
reads instantly — where a dimmed green has to be compared against an undimmed one before
it means anything.

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

The mod is five Harmony postfixes:

| Member | Vanilla | Patched to |
|---|---|---|
| `PerkTrigger.Show` | `IsInActivePhase` | `true` |
| `PerkTrigger.ViewValue` | `ActivePhaseDuration - abs(Duration - OriginalDuration)` | `Duration` while cooling |
| `PerkTrigger.BlinkOnChange` | `true` | `IsInActivePhase` |
| `PerkTrigger.IsRedView` | `false` | optional red border while cooling (off by default) |
| `CommonEffectPanel.InitializeBackground` | red or green border | yellow border while cooling |

Details that matter:

- **`ViewValue` runs negative during cooldown.** Reporting `Duration` instead matches what
  `MercenaryClassScreen` already displays as "turns of cooldown remaining" on the character
  sheet, so the HUD and the character screen agree.
- **`BlinkOnChange` had to be narrowed.** `CommonEffectPanel` flashes white and plays the
  "effect received" sound on every value change. The cooldown counter changes every turn,
  so leaving it alone would ping the player once per turn for the entire cooldown.
- **The yellow border is the game's own sprite.** `CommonEffectPanel` already carries a
  `_yellowBorder` (vanilla uses it for hover), so the third state matches the palette
  instead of introducing an invented colour. `InitializeBackground` is the hook point
  because it is the single place vanilla picks red vs green, and it runs at the tail of
  both `Initialize` and `RefreshValue` once the effect list is populated.
- **`_originalBgSprite` has to move too.** `OnPointerExit` restores the border from that
  field, so without it, hovering a cooling panel and moving away would snap it back to
  green. A side effect is that cooling panels have no distinct hover colour, since vanilla
  hover is also yellow.

Dimming is done with a `CanvasGroup` added to the panel rather than by tinting each
`Image`, because the panel's own `Update()` drives the white flash overlay's colour and a
`CanvasGroup` composes with that instead of fighting it. Panels are pooled and reused for
unrelated effects, so the non-cooldown path restores opacity explicitly rather than
skipping.

Perks with `ICDRecovery` tick their cooldown down faster than one per turn. Because the
readout is `Duration` itself rather than a count of elapsed turns, it stays truthful
automatically.

## Building and verifying

See the [repo README](../../README.md). In short, from the repo root:

```powershell
.\build.ps1 -Mod PerkCooldownHud
dotnet run --project tools\Verify -- "<Managed folder>" PerkCooldownHud
```

Verification loads the **shipped** `Assembly-CSharp.dll` and asserts every member listed in
`patch-targets.json` still exists, then replays the countdown arithmetic independently of
the mod's own code to catch off-by-one errors:

```
active 3 / cooldown 5  ->  active shows 3,2,1   cooling shows 5,4,3,2,1
```

## Configuration

`Config.cs`:

- `YellowBorderWhileCoolingDown` (default `true`) - the primary cooling-down signal.
- `CooldownAlpha` (default `0.45`) - panel opacity while cooling. Set to `1.0` to turn
  dimming off and rely on the border alone.
- `RedBorderWhileCoolingDown` (default `false`) - use the red "bad effect" border instead.
  Superseded by the yellow border when both are on. Red means "debuff" everywhere else in
  this HUD.

## Compatibility

- Game build `1.0.1.566s.7e4da55`, Unity 2022.3, Harmony 2.3.3.
- Uses the game's native mod loader. BepInEx is not required.
- Touches display code only. No save data is added or changed, and disabling the mod
  reverts to vanilla behaviour with no migration.
