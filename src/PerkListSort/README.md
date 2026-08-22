# PerkListSort

Sorts the perk picker alphabetically instead of leaving it in reverse config order.

## The problem

Open a class project, click a perk slot, and you get a list of every perk from every class
you have unlocked. The order looks arbitrary. It is not — but it is not unlock order
either, which is the natural guess.

`MagnumProjectSelectAbilityWindow.Show` builds the list by walking
`Data.MercenaryClasses.Records` in **config file order**, skipping classes that are not in
`UnlockedClasses`, and within each class walking `PerkIds` in config order, skipping any
perk already added. Unlock status only ever *filters* — it never reorders.

Then the whole thing is turned upside down, because `AddPanel` ends with
`SetAsFirstSibling()`. Every row is inserted above the last, so the list renders as the
exact reverse of the order it was built in.

So the real rule is **the config file, backwards**. `scouts_of_hades` is the first class in
`config_mercenaries.txt`, so its six perks are always the bottom six rows, in reverse:

```
config:    cqc_specialist  military_training  gear_maintenance  blind_fury  fire_transfer  assault_reflex
on screen: Assault reflex  Fire transfer  Blind fury  Gear maintenance  Training  Cqc-specialist
```

## What this does

Sorts the rows by the name shown on them, A to Z. It also covers the talent picker, which
is the same window and the same row prefab.

## Notes

- **It only reorders.** No perk is added, removed, filtered or made cheaper. The dedupe,
  the unlocked-class filter and the `IgnoreIds` exclusions are all vanilla and untouched —
  this runs after the list is built and only rewrites sibling order.
- Sorting is on the **localized display name**, not the perk id, because the two disagree.
  `military_training_basic` displays as "Training": sorting by id would file it under M in
  a list showing T.
- The sort key is rebuilt from the perk id rather than read off the row's label. The label
  has already been through `ColorFirstLetter`, which wraps the first character in a
  rich-text colour tag, so every caption begins with identical markup and the first
  character that actually differs sits several characters in. Comparing captions would
  appear to work and would mis-sort the moment that decoration changed.
- Rows are read from the window's `_panels` list, **not** from the children of the list
  root. `FreePanels` returns rows to the pool with `Pool.Put`, whose `setParent` defaults to
  false, so rows from previous openings are still children of the root — just deactivated.
  Anything walking the children would sort those ghosts in among the live rows. Packing the
  live rows into the first slots also pushes the leftovers below all of them.
- If sorting ever throws, it is disabled for the session and the list falls back to vanilla
  order. An ugly list beats an unusable one.

## Config

`Config.cs`, rebuild to apply.

| Setting | Default | Effect |
|---|---|---|
| `Ascending` | `true` | A-Z; `false` gives Z-A |
| `SortByDisplayName` | `true` | Sort by the name on the row; `false` sorts by internal perk id |
