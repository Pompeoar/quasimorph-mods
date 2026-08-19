# QuickRecycle

Hold **Ctrl+Shift** and click an item in a cargo hold to send it straight to the Recycling
tab. With the game's **"Fast item transfer with CTRL"** setting enabled you don't even
click — hold Ctrl+Shift and sweep the cursor across the grid, and everything under it goes.

## Read this first: vanilla already does most of this

Before installing anything, know what the base game gives you, because it is not documented
in-game and it is genuinely fast:

- **Click an item, then press `Shift+9`.** That is `Arsenal_DropToTab9`, bound to
  `LeftShift Alpha9` in `config_keybinding.txt` and described in `localization.txt` as
  *"Move an item to Recycling"*. A click under 0.12s picks the item up onto the cursor
  (`DragController` begins the drag on mouse-*up*), so there is no dragging and no mouse
  travel at all.
- **Shift + drop onto the recycle tab** moves **every** item in that hold sharing the same
  item id, not just the one you grabbed — `ScreenWithShipCargo.
  DragControllerShiftClickToTabCallback`.

Tab 9 is the recycler because `ItemTabsView.AddTab` numbers tabs sequentially as they are
added: seven cargo holds take 1–7, the cryochamber takes 8, recycling takes 9. Without the
cryochamber department, recycling is tab 8 instead.

**If `Shift+9` is fast enough for you, you do not need this mod.** What it adds is the
sweep: one held chord and a mouse drag across the grid, instead of one keypress per item.

## What it actually does

`DragController` already owns a "modifier + item slot" gesture. On Ctrl it raises
`_controlClickCallback`, and with fast transfer enabled it raises it on *hover*, every
frame, with no click at all (`DragController` line 374). This mod hooks that callback, so
the sweep behaviour comes free and follows a control the player already opted into.

Ctrl alone was not available — every cargo screen binds it to moving items between the
mercenary and the hold, and stealing that would remove a useful action to add one. Ctrl
**+Shift** is genuinely unclaimed: `DragController`'s Shift branches all test
`LeftShift && !LeftControl`, so holding both reaches nothing in the base game.

`ArsenalScreen`, `FastTradeScreen` and `TradeShuttleScreen` each override the callback, so
patching only `ScreenWithShipCargo` would silently miss the main cargo screen. The base and
`ArsenalScreen` are both patched.

## What it refuses to do

Every refusal falls through to normal Ctrl behaviour rather than doing nothing, except the
two that play the game's own refusal sound.

| Case | Behaviour | Why |
|---|---|---|
| Recycling already running | refuses, with a sound | `ItemTab.DropItemInTab` returns `false` outright for `TabType.RecycleInProgress`, and `MagnumCargoSystem.AddCargo` silently reroutes to `ShipCargo[0]`. Neither is right for a sweep: one would scatter items into hold 1 without telling you. |
| Quest items | refuses, with a sound | `ScreenWithShipCargo.CanDropItemInStorage` blocks exactly this for the recycling storage. |
| Locked items | refuses, with a sound | `BasePickupItem.Locked` is the story/quest hold that `ItemInteractionSystem.Repair` also declines to touch. |
| Item is equipped or in the mercenary's inventory | falls through to vanilla Ctrl | A sweep that can eat the armour you are wearing is a footgun. Configurable via `Config.CargoOnly`. |
| No constructor department on the ship | falls through to vanilla Ctrl | Same condition `Configure` uses to decide whether the tab exists at all. |
| Item already in the recycler | falls through | Nothing to do. |

The refusal sound is rate-limited to one per 0.35s, because with fast transfer enabled the
callback fires every frame the cursor sits on a slot.

A full recycler is not a dead end: placement goes through
`MagnumCargoSystem.PutItemWithFallback`, the same path `DropItemInTab` uses, which expands
the grid by a row rather than failing.

## Notes found in the source

- Putting something non-disassemblable in the recycler is **harmless**. `FinishRecycle`
  checks `CanDisassemble` and returns anything that fails straight back to the recycling
  storage intact, rather than destroying it. That is why this mod does not bother filtering
  on disassemblability — vanilla already handles it, and filtering would only differ from
  what dragging the item in by hand would have done.
- `RecyclingStorage` starts at 8x5.
- Quest items already in the recycler are returned to cargo when a batch starts
  (`MagnumCargoSystem.StartRecycle` → `ReturnQuestItemsToCargo`).
