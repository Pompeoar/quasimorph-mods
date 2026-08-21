# Trade Shuttle Planner

A Quasimorph mod that answers the only two questions that matter when you load the trade
shuttle: **where should I send it, and which category should I pick?**

Press a hotkey on the star map and it simulates every reachable orbit against every barter
category at once, then ranks them. No more loading the hold, guessing a destination, reading
the PROFIT number, and starting over.

## What it tells you

- **Ranked destinations.** Every orbit you can reach, with the best barter category for that
  orbit, the resulting PROFIT percentage, and a preview of what actually comes back.
- **Dead weight.** Cargo that no eligible station at your best destination consumes. Those items
  do not get sold, they get liquidated at the junk rate, so they are usually worth pulling out
  and selling by hand instead.
- **Where to buy.** List items you are hunting in the config file and it sweeps every station's
  stock, reporting the orbit, price at your current reputation, and where the item sits in the
  shuttle's own value-for-money ordering. **Rank 1/12** means the priority budget buys that item
  before the other eleven candidates in its category.

## Why this is exact, not a heuristic

The shuttle exchange contains **no RNG whatsoever** — its final tie-break is
`string.CompareOrdinal(itemId)`. Given the same inputs it produces the same output every time.
So this mod does not model or estimate anything. It calls the game's own
`SimulateTradeShuttlePreviewExchange` once per orbit/category pair and reports the real answer.

Two consequences worth knowing:

- The scan is read-only. The simulation clones faction and station state into
  `TradeShuttlePreviewFactionState` / `TradeShuttlePreviewStationState` and never touches the
  live objects. The one field the mod does set, `SelectedBarterCategoryId`, is restored in a
  `finally` block.
- **The real run usually beats the preview.** The preview passes
  `includeFactionTradePoints: false`, while the actual execution lets it default to `true`. Your
  banked trade points are invisible to the numbers shown here and in the vanilla UI. Treat every
  PROFIT figure as a floor.

## Two things the vanilla UI misleads you about

- **The category is a priority budget, not a filter.** Only 60% of the run's trade points
  (`TradeShuttleBarterPriorityPercent`) chase your chosen category, and only from stock the
  destination already holds. The other 40% buys whatever is best value. Picking "Goods" at a
  station with no goods does nothing at all.
- **100% is break even, not 70%.** The PROFIT readout turns green at 70, which is a 30% loss.

## Install

```powershell
.\build.ps1 -Mod TradeShuttlePlanner
```

From the repo root. See the [repo README](../../README.md) for the rest of the build options.
Restart the game fully afterwards; assemblies are not hot-reloaded.

## Use

Load a save, open the star map with the shuttle loaded, and press **F9**.

A summary pops up in game; the full report is written to

```
%AppData%\LocalLow\Magnum Scriptum Ltd\Quasimorph\TradeShuttlePlanner\last_plan.txt
```

## Configuration

`planner.cfg` is created next to the report on first run, with comments.

| Key | Default | Meaning |
|---|---|---|
| `hotkey` | `F9` | Any Unity `KeyCode` name. |
| `topOrbits` | `12` | Destinations listed in the full report. |
| `maxGainsShown` | `4` | Returning items named per destination. |
| `showDialog` | `true` | Set false for a notification only. |
| `wanted` | *(empty)* | Comma-separated item ids or name fragments to locate. |

`wanted` matches on item id or on any part of the localized display name, so
`wanted = research, ore cargo` works.

## Verifying

```powershell
dotnet run --project tools\Verify -- "<Managed folder>" TradeShuttlePlanner
```

`patch-targets.json` declares the game members this mod binds to. Almost all of them are
compile-time references, so a rename would already be a build error -- but
`TradeSystem.SimulateTradeShuttlePreviewExchange` is private and reached by reflection. If a
game update renames it, the mod silently degrades to value-only output with no error anywhere.
That one member is the main reason the manifest exists.

`tools/Verify/Checks/TradeShuttlePlannerChecks.cs` additionally proves the where-to-buy
ranking is sound. The game selects purchases by repeated argmax over
`IsTradeShuttleBuyCandidateBetter`; this mod sorts once and reads off positions. Those agree
only if that predicate is a strict total order, so the check verifies irreflexivity,
antisymmetry and transitivity, confirms sorting reproduces repeated-argmax, and asserts the
mod's comparator agrees with the game's predicate on every pair. It also fails if the random
inputs never produced a zero buy price or a ratio tie, since passing on data that never ties
would prove nothing.

## Notes on the mod loader

- This uses Quasimorph's **first-party mod API** (`[Hook]` attributes), not BepInEx.
- `UserModSystem.GrabMethods` double-registers a hook when its type key already exists, so every
  hook here is written to be safe if invoked twice in one frame.
- The manifest sets `RoomPresetNamespaceSlot: 0` and ships no `BinaryPresetsMap.json`, which is
  the valid combination for a mod with no room presets.
