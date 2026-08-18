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
- **Description.** Neither command writes one.
- **Title,** if you want something other than the raw `UniqueModName`
  (create set it to `PerkCooldownHud`).

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

## The preview image

`tools/make-thumbnail.ps1` builds `src/PerkCooldownHud/thumbnail.png` from an in-game
capture, and `build.ps1` stages it automatically. It is a script rather than a checked-in
binary so the crop is reproducible and re-runnable against a fresh screenshot.

Raw captures here are 5120x1440 ultrawide, where the effect bar is a ~1000px sliver in a
mostly black frame — unreadable once Steam scales it into a square grid tile. The script
crops the bar, scales it nearest-neighbour (it is pixel art; bilinear smears it) and
captions it. Steam rejects previews over 1 MB; the script warns if it gets close.
