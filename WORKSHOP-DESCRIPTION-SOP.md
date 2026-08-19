# Workshop description SOP

The standard every mod in this repo follows when writing its Steam Workshop store
description. `PUBLISHING.md` covers *how* to get an item onto the Workshop; this covers
*what to write on it*.

It exists because the description is the only part of a mod that is judged before it is
installed, and because writing one by instinct produces the same three failure modes every
time: it opens with backstory, it sells instead of informing, and it buries the one fact
the reader actually needs.

Everything here is derived from two things: the platform's documented constraints, and a
survey of what the most-subscribed mods actually do — on the Quasimorph Workshop itself
(Mod Configuration Menu ~21.8k subs, Quasimorph - Main ~10.9k, More Combat Info ~2.1k,
Sort To Tabs, Map Markers, Item Intelligence, Red's Optional Tweaks) and across the
UI/QoL mod scenes of RimWorld, Project Zomboid, Barotrauma, Stellaris, Don't Starve
Together and Slay the Spire (RimHUD, Better Sorting, BetterHealthUI, UI Overhaul Dynamic,
Combined Status, Has Been Read, and others).

Where a number is community-reported rather than documented by Valve, it says so. Do not
harden those into rules that depend on the exact figure.

---

## 1. The one rule

**Every sentence answers a question the reader is actually asking, in the order they ask
it.** Nothing else. They ask, roughly in this order:

1. What does this do?
2. Why would I want it — what is wrong without it?
3. Will it break my game or my save?
4. How do I use it / set it up?
5. What are the edge cases that will confuse me later?
6. Who made it, and where is the source?

Write in that order and you cannot produce a bad description. Deviate and you produce the
three failure modes above.

---

## 2. Hard constraints

| Constraint | Value | Source |
|---|---|---|
| Description length limit | **8,000 characters**, BBCode markup included | `ISteamUGC::SetItemDescription`; consistently reported across SDK wrappers. Not published on a linkable Valve doc page. |
| Visible before "Read More" | **~300 characters**, roughly 5 rendered lines | Community-reported. It is a CSS line clamp, not a character count, so it moves with font, zoom and browser. |
| Visible in the browse-grid / quick-view card | **~180-260 characters** | Community-reported, post-2026 Workshop beta. Less reliable than the figure above. |
| Preview image | **under 1 MB**, JPG/PNG/GIF | Documented: `ISteamUGC::AddItemPreviewFile`. Dimensions are *not* specified by Valve; 512x512 or 1024x1024 PNG is the safe choice. |
| Title | Renaming is free, does not change the URL or break subscriptions | See `PUBLISHING.md` — it also renames the entry in every subscriber's in-game mod list. |

Because the fold is a line clamp and not a character count, **treat 250 characters as the
budget, not 300.** Everything that decides a subscription must land inside it.

---

## 3. Required structure

Sections marked **[M]** are mandatory. Omit an optional section entirely rather than
writing a stub for it — an empty "Compatibility" heading is worse than no heading.

```
[M]  1. Hook          bold, one sentence, what it does
[M]  2. Contrast      1-3 sentences: what vanilla does, what this does instead
[M]  3. The feature   the legend / list / how to read it
[M]  4. Scope         what it does NOT change; save safety
[O]  5. Usage         hotkeys, procedures, where to find it
[O]  6. Configuration a table, if and only if the user can configure something
[O]  7. Notes         edge cases that will otherwise look like bugs
[O]  8. Install       ONLY if it is more than "subscribe"
[O]  9. Compatibility named conflicts, dependencies, MCM support
[M] 10. Source        link to the repo, if the source is published
[O] 11. Credits       mandatory if this is a fork or builds on someone's work
```

### 1. Hook [M]

One sentence, wrapped in `[b]`. It is the first thing in the description and it is the
thing that shows in the fold. It must be understandable with zero context.

The proven form is: **[Verb] [specific object] [specific qualifier].**

> `[b]Shows how many turns until a triggered perk is ready again.[/b]`

Not a question. Not "Hi, I made a mod". Not a credit block. Not the mod's name repeated
back as a noun phrase with no verb.

### 2. Contrast [M]

Immediately after the hook, in plain prose: what the game does now, and what this does
instead. This is the single highest-leverage pattern in the whole survey, because it
communicates *need* rather than *feature*.

> "Vanilla marks the books you're done with. This mod highlights the books you still
> need." — Has Been Read (Project Zomboid)

Two or three sentences. Concrete. Name the actual in-game things by name — players
recognise "Blurred Silhouette" instantly and "a triggered perk" not at all.

### 3. The feature [M]

How to read the thing you added. For a HUD mod this is a legend; for a tool it is the
feature list. Use `[list]` the moment there are four or more items — a prose paragraph of
features does not get read.

A `[table]` beats a list when the reader is comparing states (vanilla vs. modded, or
state-by-state behaviour). Use one or the other, never both for the same content.

### 4. Scope [M]

The anxiety-removal paragraph, and the one most often skipped. For a QoL/UI mod, state
plainly, near the top:

- **It only draws.** No timings, balance or behaviour changed.
- **Save safety.** Whether it can be added and removed mid-campaign, and whether anything
  of the mod's ends up in the save.
- **Achievement safety**, if relevant to the game.

> "This adds [b]no new content[/b] to the game." — Minty Spire

Players actively fear that a UI mod is secretly a balance mod. One bold sentence removes
the fear. Do not bury it in an FAQ at the bottom.

### 6. Configuration [O]

**Only if the user can actually change something at runtime.** A `const` in a source file
is not configuration and must never be presented as one — that is a lie the first comment
will catch.

If there is real configuration, present it as a table with exactly these columns, because
that is the Quasimorph community's near-universal convention:

```
[table]
[tr][th]Setting[/th][th]Default[/th][th]What it does[/th][/tr]
...
[/table]
```

If the mod supports Mod Configuration Menu (item `3469678797`), say so and say whether it
is required or optional. Community expectation is that MCM is *optional*.

### 7. Notes [O]

Behaviours that are correct but look wrong. Every mature mod in the survey has this
section, and it is the clearest single marker separating maintained mods from throwaways:
Sort To Tabs ships a "Bad Config File" recovery procedure; More Combat Info explains why
the crit marker disappears after a reload.

The test is narrow and it is not "is this interesting": **would a player one day file a
bug report about it?** If yes, it goes here. If it is design rationale — why you picked a
colour, why a hook lives where it does — it goes in the repo README, not the store page.
Nobody subscribing to a mod needs your reasoning, and three sharp notes beat five padded
ones.

### 8. Install [O]

**If the install is "subscribe", say nothing.** Everyone browsing a Workshop page already
knows how a Workshop page works, and an Installing section that says "hit Subscribe" is
the clearest possible signal that the description is padding itself out.

Write this section only when there is something genuinely unusual: a required dependency
mod, a BepInEx requirement, a load-order constraint, a manual file step. Silence means the
native mod loader and nothing to do, which is what the community already assumes.

### 10. Source [M, where the source is published]

A `[url]` to the repository — `https://github.com/Pompeoar/quasimorph-mods`. Every serious
mod in the Quasimorph scene links its source; NBKRedSpy's dozen mods all do, and its
absence reads as a throwaway. Say that issues and pull requests are welcome: it costs four
words and it is the only invitation a passing modder will ever get.

A dead or private link is worse than none.

### 11. Credits [O, mandatory for forks]

If the mod is a fork, a port, or leans on someone else's work, credit them **in the
opening paragraph**, not in a footer. This is preemptive: it prevents the "this is stolen"
comment before it is written.

> "70% of code is taken from TellMe mod. 20% of code is taken from Kindling Fire mod."
> — Show Me (Don't Starve Together)

---

## 4. Voice

The Quasimorph Workshop has a house style and it is worth matching, because it is what
readers there are calibrated to.

**Do:**

- **Impersonal and declarative.** "Adds the hit percentages to the combat log." Roughly
  60% of the surveyed openers are exactly this shape.
- **Terse.** The best utility-mod descriptions run 50-400 words. Information density is
  the whole game.
- **Concrete.** Name the perk, the key, the panel, the number. "It increases visibility"
  is worthless; "the number is the game's own countdown, so it cannot drift" is not.
- **Peer to peer.** The reader is a player who knows this game.
- **Cut it after it is written.** Every bullet is one or two lines. If a sentence only
  restates the bold lead-in of its own bullet, delete the sentence. If a bullet is
  rationale rather than a fact the player will act on, delete the bullet. Three sharp
  notes beat five padded ones, and the second draft should be shorter than the first.
- **Never explain the platform to its own users.** No "hit Subscribe", no "mods go in
  your mods folder", no "restart the game after subscribing". They are on a Workshop page
  already.

**Don't:**

- **No first-person narrative.** "I got tired of guessing, so I made this" is backstory
  occupying the fold. A single first-person aside late in the description is fine and
  humanising; an opening built on one is not. This was the specific flaw in the first
  draft of the PerkCooldownHud description.
- **No marketing voice.** No "seamlessly", no "immersive", no "game-changing". The
  surveyed utility mods contain none of it; only the content overhauls do, and they are a
  different genre.
- **No emoji. No ALL CAPS. No colour.** Zero instances of any of these across 17 surveyed
  Quasimorph mods.
- **No rhetorical-question openers.** They work for content overhauls ("Ever wondered why
  your items so bad?") and read as filler on a utility mod.
- **No changelog in the description.** Steam has a Change Notes tab. A changelog in the
  body pushes the features below the fold. At most, one line naming the current game
  build.

---

## 5. BBCode

Steam's Workshop BBCode is not the same set as its forums. Confirmed working in an item
description:

`[b]` `[i]` `[u]` `[strike]` `[h1]` `[h2]` `[h3]` `[hr]` `[list]` `[olist]` `[*]`
`[table]` `[tr]` `[th]` `[td]` `[url]` `[img]` `[code]` `[quote]` `[spoiler]` `[noparse]`
`[previewyoutube]`

Rules:

- **Use `[b]` for section headings, not `[h1]`.** Fifteen of eighteen surveyed mods do.
  `[h1]` renders very heavy and looks overwrought past two or three sections. `[h2]` and
  `[h3]` work but are *undocumented* by Valve and could be withdrawn.
- **`[color=...]` and `[size=...]` do not exist in Steam BBCode.** They render as literal
  text on the live page. Never use them.
- **`[table]` supports `noborder=1` and `equalcells=1`** on the opening tag.
- **`[spoiler]` is the right home for anything long and secondary** — a changelog, a full
  key list.
- **Markup counts against the 8,000 characters.** A table-heavy description spends a
  surprising amount of its budget on `[td]`.
- **Keep the description pure ASCII.** Use `--` rather than an em dash and `'` rather than
  a typographic apostrophe; the Workshop editor and the console upload path have both
  mangled non-ASCII here before.

---

## 6. Images

The images are read before the text, so they are part of the description.

- **The thumbnail must show the feature working in-game**, not a logo and not stylised
  text. At browse-card size (~150-270px) overlaid text is unreadable.
- **Before/after pairs are the correct format for a UI mod** — vanilla and modded, the
  same scene, the difference obvious at a glance.
- **Include at least one uncropped, unedited full-screen capture.** A gallery of only
  zoomed-in crops reads as something to be suspicious of.
- **Check what is actually in the frame.** The PerkCooldownHud thumbnail initially
  showcased two vanilla damage shields, because the game draws those with a yellow icon
  fill and this mod draws a yellow border — indistinguishable at a glance. See
  `PUBLISHING.md`.
- `tools/make-images.ps1` builds the thumbnail and gallery reproducibly from raw captures.
  Extend it per mod rather than hand-cropping.

---

## 7. Tags

Covered in full in `PUBLISHING.md`, repeated here because it is a discoverability
decision, not a publishing detail:

- **The `Compatible Version` tag is not optional.** Version filters are a *hard exclusion*
  — `AddRequiredTag` returns only items carrying the tag. An untagged item does not rank
  lower, it is absent. PerkCooldownHud spent its first day invisible to anyone filtering
  on `1.0`.
- **Apply the most specific Type tag(s)**: `UI` for a HUD mod, `Quality of Life` for a
  convenience tweak, `Gameplay Tweaks` for anything touching balance.
- **Keywords must appear in the description text.** Steam indexes title and description;
  it does not index text inside images. Title matches weigh more than description matches.

---

## 8. Before publishing

```powershell
.\tools\check-description.ps1 -Mod <ModName>
```

It checks the mechanical things — length against the 8,000 limit, the fold budget, unknown
or unsupported BBCode tags, tag balance, non-ASCII characters, and that a source link is
present. It cannot check whether the writing is any good.

Then read it back against this list:

- [ ] The first sentence is a verb phrase naming what it does, and stands alone.
- [ ] The first 250 characters would make someone subscribe.
- [ ] Vanilla behaviour is contrasted explicitly with the mod's.
- [ ] "It only draws" / save safety is stated plainly and near the top.
- [ ] Anything called configuration is genuinely configurable by the user.
- [ ] Every edge case that could look like a bug is written down — and nothing that is
      merely design rationale is.
- [ ] No section explains how to subscribe to a Workshop item.
- [ ] Every bullet survives "would the player act on this?" and is one or two lines.
- [ ] Source link present. Credits present if anything is a fork.
- [ ] No first-person opener, no marketing adjective, no emoji, no changelog.
- [ ] Pure ASCII.
- [ ] `Compatible Version` and Type tags are set in `modmanifest.json`.

---

## 9. Template

`tools/description-template.txt` is a skeleton with the mandatory sections in order. Copy
it to `src/<ModName>/workshop-description.txt` and fill it in. The file is the source of
truth for the description; paste it into the website editor, since no console command
uploads one.
