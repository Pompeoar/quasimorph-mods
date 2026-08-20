# BaneCounter

Shows your actual Bane number instead of leaving you to guess it.

## What it does

**On the HUD.** The Bane icon already existed and already printed a number — it was just the
wrong number. Vanilla shows `CursesPower.Count`, the count of curse tiers currently active,
which is 1 to 5. This shows `CurseLevel`, the real value, which runs 0 to 1000+.

**On hover.** The icon's tooltip gains three rows under vanilla's list of active curses:
the current Bane level, the level the next curse activates at, and the gap between them.

**On the roster.** Manage Operators gets a Bane diamond per row, with the level on it, and
the same three rows are appended to the operator tooltip. Bane is a property of the clone
you're choosing, so it belongs next to class, equipment and augments.

## How Bane actually works

Bane is called **Curse** everywhere in the assembly — nothing in the code says "Bane", only
localization does. That is why it looks undocumented.

- It rises **per Pact cast**, not per mission. `Mercenary.ResetPact()` adds
  `round(CurseValue x (1 - FPactDebuff))`. Across the 142 Pacts the cost ranges from **7**
  (`flauros_barkskin`) to **175** (`mars_great_awakening`, `venus_hemostatic_shock`).
- It falls **per mission**, by your ship's `MorphanalPactRecovery` (`MissionSystem`), and
  from the item path in `ItemInteractionSystem`.
- Curses activate at **1, 200, 400, 700 and 1000** for every patron in the shipped data.
- Between two thresholds, a curse's power is an `InverseLerp` across the gap
  (`CurseSystem.RefreshCursesPower`). **Bane below the next threshold is not wasted** — it is
  continuously strengthening the curses already on you. That is why the tooltip shows the
  gap and not just the next threshold.

## Notes

- The HUD number flashes and chimes whenever it changes. That is vanilla behaviour, and it
  used to be rare because the old value only moved when a whole tier activated; now it fires
  on every cast. That is the feedback the mod exists to provide, but `Config.BlinkOnAccrual`
  turns it off.
- The roster icon is hidden for operators with 0 Bane, the same way the healing icon only
  appears when it applies. `Config.ShowRosterIconAtZero` shows it always.
- The roster icon is a **clone of the implants diamond**. Those icons are prefab-authored, so
  their images, sizes and anchoring are serialized data this mod cannot see; cloning inherits
  the row's layout for free and survives a restyle in a game patch. Building one by hand
  would mean hardcoding a guess at the game's UI metrics.
- Two tooltip strings — "Next curse at" and "Bane to go" — are English only. "Bane level"
  reuses the game's own localized key, so it follows your language.

## Config

`Config.cs`, rebuild to apply.

| Setting | Default | Effect |
|---|---|---|
| `ShowLevelOnIcon` | `true` | HUD icon shows the Bane level instead of the tier count |
| `ShowTooltipDetail` | `true` | Adds the level / next / gap rows to both tooltips |
| `BlinkOnAccrual` | `true` | Keep vanilla's flash and chime when the number changes |
| `ShowRosterIcon` | `true` | Bane diamond on each Manage Operators row |
| `ShowRosterIconAtZero` | `false` | Also show it for operators with no Bane |
| `RosterLabelScale` | `0.9` | Roster number size, relative to the row's class label |
