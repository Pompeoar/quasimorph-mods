# Trade Shuttle Planner

Stops the trade shuttle guessing loop: load the hold, pick an orbit, pick a category, read
PROFIT, discover it is bad, unload, repeat.

## The flow it replaces

You want a Weapons Case. Vanilla makes you open the stock market, click the item, memorise the
three stations that manufacture it, back out, load the shuttle by hand, send it to one of them,
find out the trade value was poor, come back and reload.

With this mod:

1. Open the stock market (**H**), go to the goods tab, click the item you want.
2. Open the trade shuttle screen and press **F9**.

The mod loads your shuttle with whatever that destination actually pays for, sets the barter
category, and tells you which orbit to send it to and how many of the item to expect back.

It picks the target up from the stock market window itself, so there is no extra UI to learn -
opening Weapons Case *is* the act of saying "this is what I want".

## What it does when you press F9

- Finds every orbit that will trade with you **and is currently stocking** the item. The stock
  market's manufacturer list tells you who makes it; that is not the same as who has one on the
  shelf right now, which is what the shuttle can actually buy.
- Loads **only cargo that destination actually consumes**, cheapest first. Anything a station
  does not consume is dumped at `TradeShuttleUnsupportedSellValuePercent` — a fifth of its worth
  — while still eating a return cell, so sending it is how a six-figure hold comes back at 39%
  profit. Junk goes before good stock, and the `keep` list is absolute.
- Stops as soon as the simulated run actually brings the item back, and never spends more than
  `cargoValueMultiplier` times the item's world price. It will not empty your hold to fetch one
  cheap thing.
- Ranks destinations by whether they deliver, then by the **reputation-scaled price** they quote.
  Two stations stocking the same item can differ by a factor of two purely on standing, so the
  disliked faction is not the answer just because your cargo happens to profit more there.
- Leaves the winning loadout in the hold with the category already selected.

### When it says 0x

The shuttle does not shop for you; it repeatedly buys the best world/buy ratio item it can still
afford and still fit. A specific item only comes home if it sits near the top of that ordering
within its barter category at that station. If the report says *"it is #7 of 22 in the buy
order"*, the six better-value items will fill the hold first and **no amount of extra cargo
changes that** — the binding constraint is return cells, not trade points. Your options are a
seller where it ranks higher, or better reputation so its price drops.

Press **F9** anywhere else on the space map and it falls back to its other mode: ranking every
destination for the hold you have already built by hand.
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

Pick an item in the stock market, then press **F9** on the trade shuttle screen.

A summary pops up in game; the full report is written to

```
%AppData%\LocalLow\Magnum Scriptum Ltd\Quasimorph\TradeShuttlePlanner\last_plan.txt
```

The mod only ever moves items between your ship cargo and the shuttle hold, and it skips quest
items exactly as the vanilla screen does. If you don't like the loadout, UNLOAD ALL puts it all
back.

## Configuration

`planner.cfg` is created next to the report on first run, with comments.

| Key | Default | Meaning |
|---|---|---|
| `hotkey` | `F9` | Any Unity `KeyCode` name. |
| `topOrbits` | `12` | Destinations listed in the full report. |
| `maxGainsShown` | `4` | Returning items named per destination. |
| `showDialog` | `true` | Set false for a notification only. |
| `wanted` | *(empty)* | Comma-separated item ids or name fragments, for the survey mode's where-to-buy section. |
| `keep` | *(empty)* | Comma-separated ids or name fragments the shuttle must **never** carry. Use it for gear you are saving. |
| `junkCeiling` | `400` | Unit world price at or below which an item counts as barter junk. Junk is spent first. |
| `maxCargoValue` | `0` | Hard ceiling on loaded cargo world value. `0` derives it from the target's price. |
| `cargoValueMultiplier` | `6` | With `maxCargoValue = 0`, load at most this many times the wanted item's world price. |
| `allowUnwantedFiller` | `false` | Allow loading cargo the destination does not consume. It recovers a fifth of its value, so usually a loss. |

The shopping target itself is **not** configured here — it comes from whatever you last clicked
in the stock market.

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
