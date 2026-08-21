# Publishing to the Steam Workshop

Everything here was read out of the game's own console commands
(`CreateSteamWorkshopItemCommand`, `UpdateSteamWorkshopItemCommand`,
`ListSteamWorkshopItemsCommand`), not from a wiki. The important part is what those
commands **don't** do — several steps that look optional are mandatory.

## What each command actually does

| | `mod_createworkshopitem` | `mod_updateworkshopitem` |
|---|---|---|
| Uploads file content | yes | yes |
| Sets the title | yes — to `UniqueModName` | no |
| Sets `SteamTags` from the manifest | **no** | yes |
| Sets the preview image | **no** | yes, if the flag is `TRUE` **and** `thumbnail.png` exists |
| Sets the description | no | no |
| Sets visibility | no | no |

Two consequences worth internalising:

- **Create alone ships a broken-looking item** — no preview image and no tags. You must
  follow it with an update. This is not polish, it is part of publishing.
- **`SetItemVisibility` is never called by either command.** New Steam UGC items default to
  private, so the item stays invisible until you change it on the website. Nothing you type
  in the console will make it public.

## Prerequisites

1. **Steam is running and the game was launched through Steam.** These commands go through
   `SteamUGC`; without the Steam client attached they fail.
2. **The dev console is reachable.** `GameModeStateMachine.LateUpdate` gates the backquote
   toggle on `Data.Global.Console`, and `config_globals.txt` ships with `Console false`, so
   in a stock install the console cannot be opened at all.

   `dev/EnableConsole` exists for exactly this. It sets the flag on the
   `AfterConfigsLoaded` hook — immediately after `Data.Load()`, so config loading can't
   overwrite it:

   ```powershell
   .\build.ps1 -Mod EnableConsole
   ```

   Restart the game, then press `` ` `` (backquote). It is a `dev/` helper and is never
   staged into `dist/`, so there is no way to publish it by accident.
3. **A staged folder.** `.\build.ps1` writes `dist\<ModName>\`, containing the dll,
   `modmanifest.json` and `thumbnail.png`. That folder — not `src\`, not `build\` — is what
   you pass to both commands.

## Steps

```
mod_createworkshopitem C:\Dev\quasimorph-mods\dist\PerkCooldownHud
```

Before touching Steam this validates that the manifest exists and parses, that
`UniqueModName` is legal (letters, digits and underscore only), that every assembly the
manifest declares is actually present in the folder, and that every declared dependency
resolves. It then prints the new item id and its `steamcommunity.com` URL, and drops the id
into the command input box.

**Record that id.** If you lose it, `mod_listworkshopitems` prints your published items
with their ids and content paths.

On a first-ever publish Steam may require you to accept the Workshop legal agreement; the
command detects this and prints a `steam://url/CommunityFilePage/<id>` link. Accept it and
re-run.

```
mod_updateworkshopitem <item_id> C:\Dev\quasimorph-mods\dist\PerkCooldownHud TRUE
```

The trailing `TRUE` is the "update the thumbnail" flag. This call is what applies
`SteamTags` from the manifest and uploads `dist\<ModName>\thumbnail.png` as the preview
image.

## Then finish on the website

Open the item page and set:

- **Visibility → Public.** Until you do, only you can see it.
- **Description.** Neither command writes one. Write it in
  `src/<ModName>/workshop-description.txt`, run
  `.\tools\check-description.ps1 -Mod <ModName>`, then paste it in. The rules it must
  follow — and the research behind them — are in
  [WORKSHOP-DESCRIPTION-SOP.md](WORKSHOP-DESCRIPTION-SOP.md).
- **Title,** if you want something other than the raw `UniqueModName`
  (create set it to `PerkCooldownHud`).

### The title is website-only, and renaming is safe

Worth understanding properly, because it looks riskier than it is.

`SetItemTitle` is called in exactly one place in the whole game — `mod_createworkshopitem`,
which sets it to `UniqueModName`. That is why a freshly created item is named after the raw
camel-case identifier. **`mod_updateworkshopitem` never touches the title**, so a name set on
the website survives every future content upload, permanently. There is nothing to keep in
sync and nothing to re-apply.

The title is not purely cosmetic either. For Workshop mods it flows into the game:

```
SteamWrapper.cs:125       Title = pDetails.m_rgchTitle      (the Steam item title)
UserModSystem.cs:337      userMod.Title = string.IsNullOrEmpty(title) ? UniqueModName : title
ModEntryPanel.cs:62       _title.text = IsNullOrEmpty(entry.Title) ? entry.UniqueModName : entry.Title
```

So renaming on the website also renames the entry in every subscriber's in-game mod list.
A manifest `Title` field would not help: local `LocalUserPresets` mods are hardcoded to
`userMod.Title = userMod.UniqueModName` (line 170), and the Workshop path overwrites it at
line 337 regardless.

**`UniqueModName` is the thing that must never change once published.** It is the dedupe key,
the `LocalUserPresets` folder name and the `modprefs.json` ordering/disabled key. Changing it
orphans every subscriber's preferences. The display title carries no such weight — rename it
as often as you like.

## Steam tags

`SteamTags` in `modmanifest.json` is applied by `mod_updateworkshopitem` only (never by
create). The valid values are configured on Steamworks by the developer, not in the game's
code, so they can only be read off the Workshop browse page's filter sidebar:

| Category | Values |
|---|---|
| Compatible Version | `1.0` `0.9.1` `0.9` `0.8.5` `0.8` `0.7` |
| Type | `New Content` `Quality of Life` `UI` `Graphical` `Gameplay Tweaks` `Overhaul` `Utility` `Other` |

**The version tag is not optional in practice.** Browsers routinely filter by Compatible
Version, and an item with no version tag is excluded from every one of those filters — it
simply does not appear, even for the version it actually supports. PerkCooldownHud shipped
its first day tagged only `UI` and was invisible to anyone filtering on `1.0`.

Re-run the update command after changing tags; the manifest in `dist\` is what gets read.

## Published item ids

Keep these here so an update never depends on remembering one. `mod_listworkshopitems`
recovers them from Steam if this list is ever lost.

| Mod | Item id |
|---|---|
| PerkCooldownHud | `3785994116` |

## Updating later

Rebuild, then re-run the update command with the same id:

```powershell
.\build.ps1 -Mod PerkCooldownHud -NoDeploy
```

```
mod_updateworkshopitem 3785994116 C:\Dev\quasimorph-mods\dist\PerkCooldownHud FALSE
```

Pass `FALSE` when the preview hasn't changed — it skips re-uploading the image. Neither
command writes a changelog, so note changes in the description or a change note on the
site.

## The preview image and gallery

`tools/make-images-perkcooldownhud.ps1` builds `src/PerkCooldownHud/thumbnail.png` and the
`src/PerkCooldownHud/workshop-images/` gallery from in-game captures. `build.ps1` stages
`thumbnail.png` automatically; the gallery images are **attached by hand on the item page**,
since no console command uploads them.

The gallery is a before/after pair twice over — cropped to the effect bar so the feature is
legible, then the untouched full-screen captures. Subscribers reasonably distrust a listing
that only shows zoomed-in fragments, so the full-screen pair is copied byte-for-byte rather
than re-encoded and stays a genuine unedited screenshot.

It is a script rather than checked-in binaries so the crops are reproducible against fresh
screenshots.

Raw captures here are 5120x1440 ultrawide, where the effect bar is a ~900px sliver in a
mostly black frame — unreadable once Steam scales it into a square grid tile. The script
crops the bar and scales it nearest-neighbour (it is pixel art; bilinear smears it). Steam
rejects previews over 1 MB; the script warns if one gets close.

**A trap worth knowing:** this mod's signal is a yellow *border* on a dimmed panel, but the
game independently draws the damage shield with a yellow *icon fill*. Both read as "yellow
panel" at a glance, and the first thumbnail published here showcased two damage shields by
mistake. The zoom rectangle is pinned by coordinate to panels that are genuinely cooling
down.
