# Trade Shuttle Planner

Stops the trade shuttle guessing loop: load the hold, pick an orbit, pick a category, read
PROFIT, discover it is bad, unload, repeat.

## The flow it replaces

You want a Weapons Case. Vanilla makes you open the stock market, click the item, memorise the
three stations that manufacture it, back out, load the shuttle by hand, send it to one of them,
find out the trade value was poor, come back and reload.

With this mod, the quickest route is the built-in **Planner** button:

1. Open the trade shuttle screen and click the **Planner** tab. It opens a full-screen window.
2. Pick the good you want from a grid of item icons, pick a destination orbit, then load and review.

No stock market, no memorising manufacturers. The window loads your shuttle with whatever that
destination actually pays for, sets the barter category, and shows how many of the item to expect
back - updating live as you add or remove individual items. See
[The Planner window](#the-planner-window) below for the full walk-through.

There is also a hotkey flow for when you would rather not open the screen first:

1. Open the stock market (**H**), go to the goods tab, click the item you want.
2. Open the trade shuttle screen and press **F9**.

Both routes end the same way: the shuttle is loaded, the category is set, and you are told which
orbit to send it to. The stock-market click is only an optional shortcut that pre-selects the good
for the F9 flow - the Planner window needs none of it.

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

## The Planner window

The mod adds a third tab to the trade shuttle screen, next to **Cargo** and **Last trip report**.
Clicking it no longer draws an inline page - it opens a **full-screen window** styled to look
native (dark panel, teal border, section headers). The window is built by cloning the game's own
widgets - the item-slot cell for the icon grids, the trade-shuttle buttons for every button and
row, its TMP labels for text - so it inherits the game's fonts, sprites and materials rather than
looking like a bolt-on. **The stock market is not required at any point.**

Open the trade shuttle screen and click the new tab (its icon is a clone of the report tab's). The
window walks three steps. **ESC** or the on-screen **CLOSE** button dismisses it.

### Step 1 - choose the good

A **grid of item icons** (roughly eight per row, forty per page), exactly like the ship cargo grid:
each cell shows the item's real icon and its total stock summed across every orbit that will trade
with you. Across the top are **filter tabs by barter category** - Weapons, Equipment, Consumables,
Goods, Chips - taken from `Data.TradeShuttleBarterCategories`; each item is mapped to a category by
its `ItemClass`. A text line under the grid names whichever cell you are hovering. If a category
runs past one page it paginates with **< PREV** / **NEXT >** and a "Page N/M" readout. Click a cell
to choose that good and go to Step 2.

### Step 2 - choose the station

A list of every reachable station stocking the chosen good, one clickable row each, showing the
orbit, the owning faction, **how many are on the shelf**, the reputation-scaled **buy price**
(`TradeSystem.GetItemBuyPrice`), and the item's **rank** in that station's buy order. Rows are
**sorted by stock, most first** - you usually want the orbit with the most - and each is
**colour-coded by your reputation** with that faction using the game's own thresholds. A **BACK**
button returns to Step 1; click a row to pick that destination and go to Step 3.

### Step 3 - load and review

Two load buttons fill the hold for the picked destination:

- **LOAD IN-DEMAND** - loads only cargo that destination actually consumes (per
  `TradeSystem.IsValidItem`), cheapest first, skipping quest items and anything on your `keep`
  list, and stopping as soon as the simulation shows the target coming home.
- **LOAD BEST** - the high-value variant: loads the most valuable consumable cargo that fits.

Below the buttons are two icon grids: **the shuttle hold as it stands now**, and the **eligible
ship-cargo** items this station demands but which are not yet loaded. You edit the loadout by
clicking cells: click a **hold** cell to unload that item back to ship cargo, click an
**available-cargo** cell to load it. Every move is the same storage operation the rest of the mod
uses, is fully reversible, and never touches quest items.

**The expected return updates live** after every change: the count of the target item coming home,
the returning-items list, the total return value and the profit percent. This is the game's own
`SimulateTradeShuttlePreviewExchange`, not an estimate; it is recomputed whenever the hold actually
changes (the hold is hashed, so it does not re-run every frame). A **BACK** button returns to
Step 2.

Picking a good in the window also sets the same target the **F9** flow uses, so the two stay in
sync: clicking an item in the stock market remains an optional shortcut that pre-selects the good,
but it is no longer required - a brand-new game where the stock market has never been opened works
fine.

The window is deliberately defensive: if any expected screen widget is missing (for instance after
a game update moves a field), it logs once and simply does not open, rather than throwing. A
per-frame circuit-breaker disables it after repeated failures instead of spamming exceptions.

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

Open the trade shuttle screen, click the **Planner** tab to open the window, then pick a good, pick
a destination, and load/review - all in the one window. Or, for the hotkey flow, pick an item in
the stock market and press **F9** on the trade shuttle screen.

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

The shopping target itself is **not** configured here - pick it in the Planner tab, or from
whatever you last clicked in the stock market for the F9 flow.

## Verifying

```powershell
dotnet run --project tools\Verify -- "<Managed folder>" TradeShuttlePlanner
```

`patch-targets.json` declares the game members this mod binds to. Many are compile-time
references, so a rename would already be a build error -- but several are reached by reflection and
would fail silently: `TradeSystem.SimulateTradeShuttlePreviewExchange` (private) and the private
serialized fields the Planner window re-points on cloned widgets to fix the caption and hotkey-glyph
bugs (`LocalizableLabel._label`, `CommonButton._captionLabel`/`_captionTag`/`_interactable`,
`HotkeyButton._keyId`, `GameKeyPanel._keyId`, and the `ItemSlot` icon/background/count fields). If a
game update renames any of them the mod degrades rather than crashing, so the manifest exists to
turn those silent breaks into a build-time failure.

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
