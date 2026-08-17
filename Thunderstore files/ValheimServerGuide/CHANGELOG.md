# Changelog
## 0.12.0

**Before you update:** quest progress moves out of player character files and into files your server
owns. Existing progress migrates by itself the first time each character logs in, and nothing resets
— but back up your world first, and back up the new
`BepInEx/config/ValheimServerGuide/PlayerProgress/` folder once your players have logged in. Update
the mod on your server **and** on your players; the first entry explains what happens if only one
side is updated.

### Quest progress is saved on the server, not in the player's character file

**This changes where every quest's progress is stored. Read the whole entry.**

Progress used to live inside the player's own character save. That let it travel in the wrong
direction:

1. A player joins your server and starts a quest chain.
2. They log off and play **single-player** with the same character.
3. Every quest they finish offline is written into their character file.
4. They rejoin your server — and the server sees those quests as already done.

Progress now lives in a file your **server** owns, one per character:

```
BepInEx/config/ValheimServerGuide/PlayerProgress/
  Ulf_-8264718293746152.yml
  Astrid_44019283746152.yml
```

Single-player is its own server, so offline play writes to that player's own local folder and can
never reach yours. Nothing about writing guides changes — no YAML field, trigger or command behaves
differently.

The files are plain text, and you can read or hand-edit them while the character is offline:

```yaml
player_name: Ulf
character_id: -8264718293746152
migrated_from_character_file: true
migrated_at: 2026-08-17 10:00:00 UTC
last_saved: 2026-08-17 10:42:11 UTC
progress:
  VSG.cp.valcoin_quest: '2'
  VSG.fired: eikthyr_lore,arrow_hint
```

The name in the filename is only there to keep the folder readable — `character_id` is the identity,
so a player who renames their character keeps their progress.

**Existing progress migrates automatically, once.** The first time a character logs in after this
update, whatever progress is still in their character file is copied into their new server-side file.
Every later login reads the server file. Nothing is lost and nothing resets — a player mid-chain
stays mid-chain. Each player just needs to log in once.

Their character file keeps its old progress untouched and unread from then on, as a backup: if you
ever lose a progress file, deleting it makes that character's next login re-seed from the backup.

**Back up the `PlayerProgress` folder.** These files *are* your players' quest progress, and they sit
under `config/`, which some mod managers rewrite when they update a mod. The new
`PlayerProgress / ProgressPath` setting moves the folder anywhere you like — a path outside the
config tree is the safe choice on a managed server.

**Both sides need this version.** An updated player on an old server falls back to character-file
storage after about 20 seconds, so their quest log still works, it just is not server-authoritative.
An old player on an updated server keeps using their character file, and migrates whenever they do
update. Neither case loses progress.

Progress also survives a server restart now. Chain progress used to be kept only in the running
server's memory, so a restart lost it.

**Admins:** `vsg_debug` and `vsg_list_player` now open with the storage that character is using —
`server-side`, `local file`, or `character file (legacy fallback)`. That is the first thing to check
if someone reports a wrong quest state. `vsg_refresh` re-pulls the progress file too, and no longer
discards a change made in the same moment.

### The Guide Codex is drawn in the game's own window

The Codex used to be a stack of flat coloured rectangles. It is now the **player inventory window** —
the same carved wood frame, the same darker boxes behind the guide list and the guide text, the same
buttons (hover and press included), and the same font the inventory uses. It is the game's own
artwork, so the Codex matches your install and the download does not grow by a byte.

Its text is a size or two larger to suit that font, and its colours are Valheim's orange-on-parchment.

- **Buttons are sized for their labels.** `Close`, both toggle pills and the footer toggle were small
  boxes with the text crammed against the edge. They are taller now, padded inside, and their labels
  are larger. The pin button reads `[ ] Pin to Tracker`, or `[x] Pinned to Tracker` once pinned.
- **The window scales with your display.** It takes about 80% of your screen height at any resolution
  and GUI scale, and re-fits itself if you change either. It used to come out oversized on most
  displays.
- **Upcoming Steps only appears when a guide has some.** For everything else — which is most quests —
  the guide text gets that space instead.

The F10 quest tracker looks the same as before.

### The Guide Codex list scrolls instead of paging

`[<] Page 2 / 4 [>]` is gone. The category pane is one continuous list you scroll with the wheel,
which is what you expect a quest log to do. Paging had rough edges: a category that straddled a page
break printed its heading twice, hiding a guide could shuffle everything onto different pages, and
you could not see two categories at once if the split fell between them.

A thin scrollbar appears on the right only when the list is longer than the pane, so its presence
tells you there is more below. The footer keeps `[ ] Show hidden (n)` and gains the space the arrows
used to take.

Two fixes came with it:

- **The wheel no longer zooms the camera while the Codex is open.** Scrolling the list used to pull
  the camera in and out at the same time. Your hotbar no longer cycles underneath it either.
- **Scrolling works everywhere, at a sensible speed.** The guide text, the "Upcoming Steps" section
  and the rune reading could not be wheel-scrolled at all — only dragged — and where the wheel did
  work it crawled a pixel or two per notch. Every scrolling area now moves a readable distance per
  notch.

### Nothing in the game's HUD covers a mod panel any more

The stamina bar drew on top of the NPC dialogue panel, and the crosshair sat in the middle of every
panel you opened. The quest tracker, NPC dialogue, rune reading, Guide Codex and intro cinematic now
all sit above the game's interface.

The crosshair and the hover-name text hide while a panel is open instead of showing through it — you
have a free mouse cursor at that point, so a centre-screen reticle is only in the way. Both return
the instant the panel closes.

One thing to be aware of: these panels also draw over the pause menu. The Codex closes on ESC rather
than opening the menu, so this shows up mainly if you open the menu with the tracker on screen.

### Text no longer gets cut off anywhere

Rune readings were clipped on **both** sides — sentences started and ended mid-word. That is fixed,
along with the last places text was cut short rather than wrapped:

- **F10 tracker** — quest rows ended in `...`; they wrap onto extra lines now and the panel grows. Progress bars stay whole instead of breaking across a line.
- **Tracker tooltip** — was capped at six lines; shows the whole description now.
- **Codex list** — quest titles were cut short; they wrap onto a second line and the row grows to fit.
- **Codex detail title** — a long title ran over the rule beneath it; the title area grows instead.
- **Codex "Upcoming Steps"** — locked steps were cut at 40 characters; they show in full.
- **Reward summary and top-left toasts** — long lines ran off the screen edge; they wrap.

### Rune readings use the width you set

The first rune reading of each session ignored its `width:` and came out as a narrow column of text,
often a third of the width you asked for, while every later reading in the same session was correct.
Every reading uses your width now. If you raised `width:` to work around it, you can set it back to
the value you actually want.

### Several quests advancing at once all get a line

Killing one Greyling with two Greyling quests active advanced both counters but only ever showed one
of them. Everything the mod writes to the centre of your screen is now gathered up and shown as a
single message, one line per quest:

```
Thin the Wilds: 3/15
Thin the Pack: 7/20
```

A quest completing at the same moment another advances no longer wipes out the other's feedback.

### F10 cycles three views instead of toggling two

```
Collapsed  ──F10──▶  Expand Titles  ──F10──▶  Expand Full  ──F10──▶  Collapsed
```

**Expand Full** prints each pinned quest's objective under its row, so you can read objectives
without opening the inventory to free the cursor and hover a row.

The badge says what the **next** press will do — `Show Quests (2) [F10]` → `Show Desc [F10]` →
`Hide Quests [F10]` — so the key explains itself to a player who has never pressed it, and a second
line notes that the panel can be dragged (with the inventory open). The badge now travels with the
panel when you drag it instead of being left at the screen corner, and you can grab either box.

**Guide authors:** the objective text comes from the step's `description:`, falling back to a new
**entry-level `description:`**. Non-chain quests — kill counts, item submits, collection goals — have
no step, so set the entry-level field or they show a title and a bar with no objective. An
entry-level `description:` was ignored in earlier versions; it works now.

### NPC conversations open with Shift + E

`[Hold E]` is gone. Holding E for half a second meant every ordinary trade had to wait out the hold
timer before the store could open. **Shift + E** is decided instantly, so plain E opens the store
with no delay. Either Shift key works, as does the key bound to Run (Shift by default), which is how
a gamepad reaches conversations. Hover prompts now read `[Shift + E] Quest`; update any `hover_text:`
in your own YAML that spells out the old binding.

### NPC and creature names show properly

The multi-quest picker used `$npc_haldor` as its heading instead of "Haldor", and `{creatureName}` /
`{itemName}` had been printing raw names like that into messages and Discord posts ever since they
were added. They all read properly now.

### `timed` entries with `scope: player` never fired on a dedicated server

A player-scope timer runs on each player's own game so its gates apply per player — and on a
dedicated server it was never started anywhere, so the entry simply never fired. It worked in
single-player, which is why it went unnoticed. Also fixed:

- An entry with no `trigger.id` fired on schedule with nothing listening. `id` is optional now and defaults to the entry's own id.
- Every config change reset every countdown, so on a server whose YAML is edited often a long timer could never finish an interval. Unchanged timers keep running now.
- `interval` only accepted plain seconds, and anything else was skipped without a word. It now takes `30s`, `15m`, `2h`, `1d`, `daily`, `hourly` or plain seconds, and says so in the log when it cannot read the value.

**Your players need this version for it to matter** — updating only the server changes nothing for
player-scope timers.

## 0.11.1

Republish of 0.11.0. **No functional changes** — same code, rebuilt and repackaged.

If your game reported `0.10.0` after updating to 0.11.0, that was a stale DLL left behind in your own plugin folder, not a problem with the release. Installing this version guarantees a clean overwrite. Everything in 0.11.0 below — the paged Codex list, hiding guides, and `vsg_refresh` — is unchanged and included.

Note that a running game or dedicated server keeps the old assembly loaded, so restart it after updating rather than reloading in place.

## 0.11.0

### The Guide Codex list no longer breaks on a large server

On a server with a lot of guides, the Codex list turned into a column of category names separated by blank gaps — the quest rows were there, and clicking the empty space still selected them, but nothing was drawn.

The list was a fixed-height layout with no way to overflow. Unity does not spill a layout group past its container; it *compresses* it. Category headers had a pinned minimum height so they held their size, while the quest rows did not, so every row was squeezed toward zero height and vanished. It looked intermittent because it only started once a player had unlocked enough guides to outgrow the panel — a new character saw a perfectly normal Codex, and the same character saw a broken one twenty hours later.

**The list is now paged.** New controls at the bottom of the category pane:

```
[<]   Page 2 / 4   [>]
```

Each page holds as many rows as actually fit — around eighteen guides — and never more, so no row is ever compressed or clipped. A category that runs across a page break repeats its header on the next page, so a guide is never stranded under the wrong heading. The arrows grey out on the first and last page. Row heights are now pinned at both ends as well, so the old compression cannot recur even if the metrics change.

The "Upcoming Steps" section on the detail pane had the same latent problem — it fitted about seven step rows — and now scrolls.

### Hide guides you are finished with

Every guide you have ever triggered stays in the Codex forever, which is what made the list so long in the first place. You can now take one out of it.

Select a guide and use the new pill next to "Show on Tracker":

```
[ ] Hide from list        ->  [x] Hidden — unhide
```

Unlike the tracker pin, this works on completed guides too — a finished quest is usually the thing you most want out of the way.

To get one back, flip **`[ ] Show hidden (3)`** at the bottom of the category pane. Hidden guides reappear dimmed and marked with `[-]`; select one and click the pill again to restore it.

Hiding is display-only and per-character. A hidden guide still fires, still tracks, still announces to Discord and still pays out its rewards — it simply does not take up a row. The preference is stored on the character (`VSG.hid`), so it follows that character between servers, and `vsg_reset` clears it (otherwise a reset quest could sit invisible in the list forever).

### New public command: `vsg_refresh`

```
vsg_refresh
```

Rebuilds the Codex and tracker from scratch and asks the server to re-send its guide config and your chain progress. This is the one VSG command that is **not** admin-gated — it changes no state at all, so a player who hits a stale or half-drawn panel can fix it without hunting down an admin.

It is also a genuine fix for a real timing hole: the HUD is built when the game loads it, which can happen *before* the server's config push arrives. A newly arriving config now repaints the tracker and repopulates an open Codex on its own, so a guide list that used to look empty until you relogged now fills itself in.

## 0.10.0

### Panels size themselves to your text — nothing is cut off

The **NPC conversation panel** was a fixed 750×185 box with an 82-pixel body set to ellipsis: roughly four lines, about 360 characters, and everything past that was silently thrown away. Long dialogue simply lost its ending.

It is now driven entirely by its content. The panel is anchored above the bottom edge and grows **upward** as the text gets longer, so it can never slide off screen. There is no character limit: the body sizes itself to whatever you wrote, and only once the panel would exceed 82% of the screen height does it stop growing and let the body scroll instead. The header wraps too, and choice buttons now flow onto as many rows as they need — three across, or two across when there are more than three options — with each button growing to fit a label that wrapped onto a second line. Five choices used to become five unreadable slivers.

The same audit was applied to every other mode:

- **rune** — auto-sized already, but had no ceiling, so a very long reading would have run off both edges of the screen. The body and bullet list now share a scroll view capped at 86% of screen height.
- **message** with `position: Center` — vanilla's centre text is a single non-wrapping line, so a full sentence ran off both sides. It now word-wraps at 70% of the screen width. Applied to the shared vanilla component and only ever growing its rect, so vanilla's own centre messages are untouched.
- **raven / intro** — forced to overflow rather than clip, so nothing is dropped. Note these are fixed-size vanilla art and cannot grow: very long text will spill past the parchment, which is a sign the entry wants `rune` or `conversation` instead.
- **bubble** — an explicit world rect so wrapping has a sensible measure; overflow, never truncate.
- **Guide Codex** — the detail-pane quest title now wraps instead of ellipsising. (The category list on the left stays single-line on purpose — it is navigation, and the full title shows on the right.)

### Highlight chosen words in any text — new `highlight:` block

Long guides are skimmed. You can now colour the words that must survive the skim — a hotkey, an item name, a cost, a warning — anywhere text is shown.

```yaml
highlight:
  - any: ["[F7]", "[E]"]
    color: "#8FD5FF"
    style: "Bold"
  - text: "Communion Totem"
    color: "#F0C868"
```

Fields: `text` or `any` (a list sharing one style), `color` (`#RRGGBB` / `#RRGGBBAA`), `style` (`Bold` / `Italic` / `Underline` / `Strikethrough`, combinable), `size_percent`, `first`, `match_case`, `whole_word`.

- **Three scopes.** At the root of any YAML file the rules apply server-wide — and unlike `tracker:`, the lists from *every* file are merged, so each guide pack can contribute its own vocabulary. Per entry and per chain step are also supported; step beats entry beats server-wide.
- **Sensible matching by default.** A phrase that starts and ends with a letter or digit matches on word boundaries, so `Guard` never lights up inside `Guardian`, while `[F7]` or `Shift + E` match literally. Case-insensitive unless you ask otherwise.
- **Safe output.** Matching never looks inside an existing rich-text tag, so markup you wrote yourself is never corrupted, and a span that one rule has highlighted is left alone by later rules — tags never nest into something the game cannot render. An invalid colour is dropped with a warning rather than printed as broken markup.
- **Everywhere in game, never on Discord.** Raven, message, chat, rune, intro, conversation, bubble, the Guide Codex, the HUD tracker and NPC hover text all pick it up. Discord webhook posts deliberately do not — they would show the raw `<color=…>` markup. (The `chat_message` reward is the one reward that does get highlighting, since it renders in the in-game chat.)

Highlighting runs *after* token substitution, so a rule can match text a token produced — a player name, a creature name — without ever seeing the tokens themselves.

## 0.9.1

### Template tokens now expand everywhere text is shown (fix)

`{playerName}` was rendering **literally** — as the raw text `{playerName}` — in several places, most visibly the **Guide Codex**. The token itself was always correct (`{playerName}` and `{player_name}` are aliases for the same value); the problem was that only the *firing* path expanded it. The panels that re-read the same YAML fields later printed them verbatim. No config changes are needed — guides already written with `{playerName}` simply start rendering correctly.

Fixed in:

- **Guide Codex** — quest titles (both the category list and the detail header), `summary`, the `message` / `display.text` body, step `message` and `description` text, and the greyed-out "upcoming steps" labels.
- **HUD tracker** — pinned quest titles on every row type (chains, item-submit, kill-count, item-collection) and the step description in the hover tooltip.
- **NPC conversation panel** — the header (`display.topic` / `title`), every choice-button label, and the multi-quest picker rows. Node body text was already correct.
- **Raven** — the topic/label shown above the message. The raven *body* was already correct; the topic is registered before a character exists, so it is now re-expanded at display time. Same fix for the `intro` mode topic.
- **NPC hover text** — `hover_text.default` and `hover_text.after_fire`.
- **Rewards** — a `chat_message` or `discord` reward written with `{playerName}` (rather than `{player_name}`) no longer prints the raw token when granted from a chain completion or a conversation choice. Both spellings now work on every reward path.

Tokens that depend on what triggered the entry (`{creatureName}`, `{itemName}`, `{rank}`, and the rest) are intentionally left **as written** in these panels rather than blanked, since the Codex has no triggering event to resolve them from — they still expand normally in the message shown when the entry fires.

## 0.9.0

### Rune Display Mode — Full Redesign

The `rune` display mode is now rendered by a custom, game-themed panel instead of the plain vanilla runestone reading — and every part of it is now configurable per-entry under a new `display.rune` block:

- **Layout:** header, divider, word-wrapped body, and an optional bullet list, stacked in a centered card that auto-sizes to its content.
- **Fonts & colors:** independent color/size/font-style (`Bold`/`Italic`/`Underline`/`Uppercase`/`Strikethrough`, any combination)/alignment for the header, body, and list rows, plus panel background and accent (divider) color.
- **Lists:** an `items:` array renders as a styled bullet list below the body — pick your own glyph, color, size, and style.
- **Fade in/out:** the panel now fades in on open and fades out on dismiss (`fade_in`/`fade_out`, seconds, default `0.35` each — `0` for instant). Re-triggering a reading mid fade-out crossfades smoothly instead of flashing.
- Uses the game's own font and vanilla `Image` color fills only — no custom assets. Ghost mode (ubiquitous invulnerability + undetected) and ghost-mode/intro interplay are unchanged from the old rune mode.
- Entries with no `rune:` block are unaffected in behavior — they get the new panel with the same themed defaults (gold header, parchment body, dark stone background) the old vanilla reading approximated.

See the wiki's Display Modes page for the full field reference and examples.

**Verified: Lost Scrolls II compatibility.** Lost Scrolls II is a separate mod/assembly and
cannot modify (and did not modify) ServerGuide's rune display code. Its integration is entirely
through the public trigger API — the new `dvergr_rank_first`/`dvergr_party_*`/`dvergr_tournament_*`
triggers it raises (see below) are already fully supported. One scope note for anyone comparing
notes with Lost Scrolls II's own UI: its **Ranking Board** and **Tournament Board** (opened by
their own hotkeys, not fired as ServerGuide guidance entries) render with the plain **vanilla**
`TextViewer.Style.Rune` reading directly — they do not route through ServerGuide's dispatcher, so
they do not pick up the new themed panel, fonts/colors, lists, or fade in/out described above.
Only entries authored with `mode: rune` in ServerGuide's own YAML get the new panel.

### Reward Message Templating (fix)

`chat_message` and `discord` **reward** messages now expand the firing entry's **full** token set — `{companionName}`, `{rank}`, `{rating}`, `{winSize}`, `{opponentOwner}`, `{mode}`, `{bracketSize}`, `{partyName}`, `{biome}`, `{level}`, and the rest — not just `{player_name}`. Previously any other `{...}` placeholder in a reward message rendered literally, so reward text couldn't reference what actually triggered it. Display/message text was already templated; rewards now match it (chain-completion and NPC-conversation-choice rewards, which have no triggering event to template from, still expand only `{player_name}`).

### Lost Scrolls II Integration — Rankings, Party Duels & Tournaments

Seven new trigger types for the companion mod's competitive suite (party duels, party ladder, and bracket tournaments), plus the matching template variables (CRIT-13) so guidance can announce results without extra plumbing:

- `dvergr_rank_first` — a companion reaches **#1** on the duel ladder (optional `caste:` filter). Vars: `{rank}` `{rating}` `{companionName}` `{ownerName}`.
- `dvergr_party_duel_won` — a party of companions wins a team-vs-team duel. Vars: `{partyName}` `{winSize}` `{opponentOwner}` `{mvpCaste}` `{ownerName}`.
- `dvergr_party_rank_changed` — a party crosses into (or moves within) the party ladder's top ranks. Vars: `{rank}` `{rating}` `{partyName}` `{ownerName}`.
- `dvergr_party_rank_first` — a party reaches **#1** on the party ladder. Vars: `{rank}` `{rating}` `{partyName}` `{ownerName}`.
- `dvergr_tournament_joined` — a player registers for a bracket tournament (1v1 or party).
- `dvergr_tournament_match` — a round's pairing is announced. Vars: `{round}` `{opponent}`.
- `dvergr_tournament_won` — the tournament champion is decided. Vars: `{mode}` `{bracketSize}`.

New templating variables: **`{partyName}`**, **`{winSize}`**, **`{opponentOwner}`**, **`{mvpCaste}`**, **`{round}`**, **`{opponent}`**, **`{mode}`**, **`{bracketSize}`**.

## 0.8.0

### Quest-Start Discord Log (new)

A separate, opt-in Discord webhook that posts a debug/monitoring line every time a player **starts** a new quest — the first step of a chain, or the first fire of any single entry (player- or global-scope). This is independent of the normal `announce.discord` and `discord_on_complete` posts, so you can route quest-start tracking to its own channel to verify quests trigger as intended.

Each log is a rich embed containing the quest's base info (title, id, category, trigger type), the player's name, their location (biome + coordinates at trigger time), and a UTC timestamp. It fires **once per character per quest** (latched), so cooldown re-fires and later chain steps do not re-log. `vsg_reset` / `vsg_reset_player` clear the latch so a quest can be re-tested.

New BepInEx config (`Discord` section):

- `QuestStartWebhookUrl` (default empty) — webhook URL for the quest-start log, a **separate** channel from `WebhookUrl`. Server-side only; empty disables the log.
- `QuestStartLogEnabled` (default `true`) — master toggle for the quest-start log (requires `QuestStartWebhookUrl` to be set).

### Config Loading

- **Guidance YAML is now loaded recursively from subfolders.** Every `*.yaml` / `*.yml` file anywhere under `BepInEx/config/ValheimServerGuide/` — at any depth — is merged into the config, and the live file-watcher reloads on edits in subfolders too. Organise guidance by pack/topic in nested folders; ids must still be unique across the whole tree. Flat top-level layouts are unaffected.
## 0.7.1

### Intro Cinematic Fixes

- **Player is now fully frozen and invulnerable for the entire intro.** Previously a stray key press (Use/Escape) released the freeze and ghost mode while the intro was still on screen, letting the player move, attack, and take damage mid-cinematic. Input is now blocked and all damage to the local player is suppressed for the whole display.
- **Fixed input staying dead after an un-skipped intro.** An intro that played to the end without being dismissed (common right after login) could leave the input lock stuck, disabling keys like E/C/X afterward. Release is now driven by the intro's own timer, so it always lifts.
- **Configurable on-screen duration.** The intro now stays up for `IntroDisplaySeconds` (default 15) instead of ending early, then auto-fades out. Skippable with Use/Escape after a short grace.
- **Fade-out on exit.** Skipping (or the timer elapsing) now fades the intro text out (`IntroFadeOutDuration`, default 1.0s) instead of cutting instantly.

### New Config (BepInEx `Display` section)

- `IntroDisplaySeconds` (default `15`) — seconds the intro stays on screen before auto-fading out.
- `IntroFadeOutDuration` (default `1.0`) — intro fade-out duration on skip/timeout.

### Dependencies

- Bumped the Jötunn dependency to ValheimModding-Jotunn-2.29.1.
## 0.7.0

### Lost Scrolls II Integration

Adds three new trigger types so guidance can react to Lost Scrolls II's companion system. The events are raised by Lost Scrolls II through ServerGuide's public `GuidanceDispatcher.Raise` API — install both mods to use them.

- **`dvergr_recruited`** — fires when a player frees (communes with) a corrupted Dvergr. Optional `caste:` filter (`Rogue` / `FireMage` / `IceMage` / `SupportMage`); omit it to match any caste.
- **`dvergr_duel_won`** — fires when a player wins a Dvergr duel. Optional `caste:` filter matches on the **winner's** caste.
- **`dvergr_level_up`** — fires when a recruited Dvergr levels up. Optional `caste:` filter and optional `level:` (`level: 0` or omitted = any level, fires on every level-up).

### Schema

- New `trigger.caste` field on `TriggerSpec` for the three triggers above.
## 0.6.0

This release lands a large batch of features (new triggers, conversation sequencing, enhanced rewards, system polish) plus a reworked progress panel.

### New Triggers

- **Kill count.** The `kill` trigger now takes a `count` field — fire after N kills instead of one. Progress is tracked per character and shown in the HUD/Codex.
- **Shared party kill progress.** Add `share_progress: true` to a multi-count `kill` so nearby group members' counters advance from each other's kills.
- **8 interaction triggers:** `crafting_table_used`, `cooking_used`, `portal_used` (optional `tag`), `ward_activated`, `tamed_creature` (optional `creature`), `sign_read`, `tombstone_picked`, and `ship_sailed`. Each takes an optional prefab filter; omit it to match any.
- **4 time & day triggers:** `time_of_day` (`game_time_fraction` + `window`), `day_number`, `real_world_time` (`utc_hour`/`utc_minute`), and `day_of_week`. Driven by a background poll; combine with `once`/`cooldown` for one-shot vs. per-day firing. (Individual entries only, like `timed`.)

### NPC Conversations

- **Multi-quest picker.** Holding E on an NPC with 2+ eligible `npc_conversation` entries now opens a chooser listing each entry's `title`; selecting one starts that conversation. A single eligible entry still opens directly.
- **Multi-node dialogue trees.** Conversations can define a `nodes:` tree instead of a flat choice list. Choices can jump between nodes (`goto_node`), gate on prerequisites (`requires` + `hidden_when_locked`/`locked_hint`), or fire other entries (`goto`). `resume_on_return: true` reopens at the last-visited node; node progress persists per character.
- **NPC hover-text override.** A per-entry `hover_text:` block replaces the default `[Hold E] Quest` hint, keyed by state (`default` / `after_fire`).

### Enhanced Rewards

- **13 new reward types:** `map_pin`, `location_pin`, `unlock_recipe`, `spawn_creature`, `set_global_key`, `remove_global_key`, `set_player_key`, `remove_player_key`, `weather`, `chat_message`, `teleport` (server allowlist), `rename_player`, and `discord` (per-reward webhook). World/server-affecting rewards are resolved server-side.

### Display

- **`bubble` display mode.** Floats text above an NPC's head in world-space without opening a panel or locking input — ideal for ambient flavour. Vanilla trader bubbles are suppressed while a VSG bubble shows.

### Progress Panel (HUD Tracker) rework

- **Player-curated panel.** The F10 progress panel now shows only the quests a player **pins** from the Guide Codex (F3 -> **Show on Tracker**) instead of every active quest. The panel is hidden by default and unhides when you pin a quest.
- **Codex pin toggle.** In-progress, trackable quests (chains, multi-count `kill`, multi-count `npc_item_submit`, and `item_acquired` goals) get a "Show on Tracker" pill; finished quests and one-off tips do not. Multi-count kill quests now also appear in the Codex while in progress so they can be pinned.
- **No input lock.** The panel no longer freezes movement/look or shows the cursor — it displays over normal gameplay. Pins persist for the session; the panel starts hidden each login.
- **Drag-to-move.** The panel can be dragged anywhere while the inventory or ESC menu is open; the position is saved per character.
- **Deprecated:** `auto_hide_delay` / `fade_duration` are ignored — the panel no longer auto-hides or fades.

### Admin

- **`vsg_debug`.** Dumps your eligible entries, all `VSG.*` state keys, and the last 10 fired IDs to the console.
- **`vsg_reset` / `vsg_reset_player` clear more state.** Resets now also clear kill counts, conversation-node pointers, and HUD progress-panel pins (`VSG.trk`).
## 0.5.2

### New Features

- **`vsg_list_player <playerName>`** — Show the full VSG guidance state of any currently-online player directly from the admin console. Lists fired IDs, `max_fires` counters, chain progress (step / complete), item-submit progress, and goal-started flags. Works from both listen-server hosts and remote admin clients; results appear asynchronously after the RPC round-trip.
- **`vsg_reset_player <playerName> [all | <id>]`** — Reset a specific online player's guidance state. Mirrors `vsg_reset` exactly (clears fired IDs, fire counters, chain state, submit progress, goal state, raven flags) but targets another player's character instead of your own. The admin console receives a confirmation message once the target client executes the reset. Both commands are admin-only (`onlyAdmin: true`) and re-verified server-side.

## 0.5.1

### Bug Fixes

- **Raven re-show after `vsg_reset` fixed.** After `vsg_reset` (all or single-id), raven entries now correctly re-show when re-triggered. The root cause was that Vanilla's `Raven.AddTempText` silently no-ops when a `RavenText` with the same key already exists in its static list, and `vsg_reset` clearing the seen-flag disabled vanilla's own cleanup for that entry. The fix evicts stale `RavenText` entries from `Raven.m_tempTexts` on every reset path and defensively before every re-show.
- **`max_fires` entries re-fire after `vsg_reset`.** Entries using `trigger.max_fires: N` (such as `player_death` tips) were permanently blocked after hitting their cap, even after `vsg_reset all`. Fire counts are stored in separate `VSG.fc.*` keys that the old reset code never cleared. `vsg_reset all` and `vsg_reset <id>` now also clear these counters.
- **`vsg_list` surfaces `max_fires` progress.** Entries using `max_fires` never appeared as "fired" in `vsg_list` (they don't write `VSG.fired`). They are now tagged `[fired N/max]` in the configured-entries list so you can see their counter and confirm it cleared after a reset.
- **`skill_level` trigger fires on login for skills already above threshold.** Previously the `skill_level` trigger only fired when a skill level actively increased during the session. If a player logged in with a skill already above one or more thresholds, those entries were silently skipped. On login the mod now scans every configured `skill_level` threshold; any threshold the player already meets that has not yet fired is raised in ascending level order. For chains, this means all qualifying steps cascade automatically — step 1 fires first (advancing the chain), then step 2 fires, and so on.
- **`location_entered` trigger now detects mod-added locations reliably.** The previous implementation read `ZoneSystem.m_locationInstances` and required `m_placed = true`, which is only set after the server re-syncs location data to the client. Locations generated after login never received that re-sync, so the trigger was permanently skipped for any zone entered for the first time. Detection now uses `Location.s_allLocations` (the scene's live spawned Location components) as the primary source, with the ZoneSystem as a fallback for locations lacking a `Location` component. The fallback also now tries `m_name` when `m_prefabName` is empty. Both paths emit `LogDebug` lines (enable `LogLevel = Debug` in `BepInEx.cfg`) so you can confirm the exact prefab names being detected and verify your wildcard patterns.

## 0.5.0

### New Features

- **Multi-goal `item_acquired` triggers.** An `item_acquired` entry can now require several different items at once via a `goals:` list (each with its own `item` and `count`). The entry fires only when every goal is met simultaneously. Items may be collected in any order, and crafted items count toward their goals. Replaces the need to chain several single-item collection steps.
- **Per-item goal progress.** Multi-goal entries show a per-item breakdown (`FineWood: 18/30`, `Coal: 12/25`, …) — in the HUD Tracker row tooltip and in the Guide Codex body — so the player always knows exactly what is still needed. The Codex badge shows `N / M goals` completed.
- **Persistent "started" state.** Once the player has collected toward any goal, the entry stays visible in both the HUD Tracker and the Codex even if those items are later removed from the inventory (crafted away, dropped, or lost on death). Visibility is no longer tied to the current inventory once collection has begun.

### Improvements

- **Plain numeric progress.** The HUD progress *bar* has been removed in favour of a simple `current/goal` count across all collection displays (chain counter steps, `npc_item_submit`, and `item_acquired` goals) for a cleaner, consistent look.
- **Chain counter steps show their count.** A chain step with a `progress_goal` now displays its `current/goal` count in the HUD Tracker row.
- **Codex completion is goal-accurate.** A multi-goal `item_acquired` entry is only marked complete in the Codex when every goal is currently satisfied, re-checked live against the inventory.
- **`vsg_reset` clears goal state.** `vsg_reset all` and `vsg_reset <id>` now also clear the latched goal-started flag.

## 0.4.0

### Improvements

- **Raven display queue.** Multiple raven entries that fire in quick succession are now shown one at a time instead of all at once. Each raven persists until the player interacts with it (or the raven auto-dismisses). The next queued raven appears only after the current one is acknowledged, so no message is skipped or overwritten.
- **Dungeon deferral for raven.** Raven entries that fire while the player is inside a dungeon or interior location are held in a deferred queue. The moment the player exits, the deferred ravens drain into the normal queue and show in order.
- **`vsg_reset` clears the raven queue.** `vsg_reset all` wipes the entire raven display queue and deferred list. `vsg_reset <id>` removes any pending instance of that specific entry from both queues and cancels it immediately if it is the currently-active raven.

## 0.3.2

### Improvements

- **`item_acquired` inventory seeding.** When an `item_acquired count > 1` entry becomes eligible (on player login or config reload), the mod now immediately reads the player's current inventory and seeds the progress counter from it. Items already carried before the guide entry existed count toward the goal — the player is never penalised for having collected materials early. If the inventory total already meets the goal at that moment, the entry fires right away without requiring another pickup.
- **Chain step inventory seeding.** When a chain counter step uses `progress_trigger: { type: item_acquired }`, activating the step now seeds the counter from the player's existing inventory instead of starting at zero. If the seeded count already meets `progress_goal`, the step advances immediately.
- **Wiki updated.** `Trigger-Types`, `Guide-Chains`, and `YAML-Configuration` pages document the new inventory-seeding behaviour and the `count` field for standalone `item_acquired` entries.

## 0.3.0

### New Features

- **`item_acquired` count goal.** Add `trigger.count: N` to any `item_acquired` entry to require the player to accumulate N of that item in their inventory before the entry fires. Progress is tracked as the current inventory total (all matching stacks summed), so two stacks of 10 count as 20. Both picking up items and crafting them count toward the goal. A `current/goal` progress bar appears in the HUD Tracker while collecting and disappears once the goal is reached.

---

## 0.2.0

### New Features

- **Multi-file YAML loading.** The loader now scans the entire `BepInEx/config/ValheimServerGuide/` folder for `*.yaml` and `*.yml` files and merges them into one config. Split your guidance across as many files as you like. Duplicate ids across files: first file (alphabetically) wins. A malformed file is skipped with a log error; other files still load.
- **Biome trigger.** New `trigger.type: biome` fires when the local player enters a named biome (e.g. `biome: BlackForest`). Fires once per session entry; resets on spawn so it also fires on first login.
- **Distance trigger.** New `trigger.type: distance` fires when the local player comes within `trigger.radius` metres (default 50) of a world location whose prefab name matches `trigger.location` (trailing `*` wildcard supported). Fires at most once per location per character.
- **Codex entry `summary:`.** Add a top-level `summary:` field to any entry; the Codex shows a "Quest Complete" header + recap once the chain finishes. Falls back to the last step's message if not set.
- **Codex step `description:`.** In-progress chain steps now display `description:` in the Codex body (what the player needs to do), not the completion `message:` text. Entries without `description` fall back to `message` as before.
- **`General` category.** Added `General` to the list of valid Codex categories.
- **Display mode rules doc.** `wiki/Display-Modes.md` now includes a full recommended-mode table per trigger type (rune for action events, raven for environmental events, message for NPC/minor tips).
- **Guide Authoring Reference.** New `wiki/Guide-Authoring-Reference.md` — comprehensive reference for guide authors covering display mode assignments, chain patterns, and Codex field semantics.

### Bug Fixes

- **Raven re-fire fix.** Raven entries now correctly re-fire when `once` is not set or after `vsg_reset`. Previously the vanilla `Player.m_shownTutorials` gate caused the raven to show only once per character save, ignoring VSG's own repeat controls.
- **Raven `message:` and template support.** Raven mode now reads the top-level `message:` field (same as all other modes). Template tokens (`{player_name}`, `{biome}`, etc.) are expanded each time the entry fires.
- **Timed trigger player-scope fix.** Player-scope timed entries now run on each client individually. Previously dedicated servers skipped player-scope timers entirely, preventing per-player timed tips from ever firing on dedicated servers.
- **`vsg_reset` raven fix.** `vsg_reset` (both `all` and single-entry) now clears the vanilla raven seen-flag so raven entries can re-show after a reset.

---

## 0.1.0

Initial release.

### Features

- **YAML-driven guidance system.** Server admins write a `guidance.yaml` that is automatically pushed to all connected clients. No client-side file editing required.
- **18 trigger types.** React to crafting, item pickups, kills, builds, biome entries, location discovery, skill milestones, NPC interactions, boss defeats, player deaths, timed intervals, and more.
- **6 display modes.** Raven (Hugin popup), Message toast, Chat line, Rune viewer, Intro cinematic, and NPC conversation panel — all using vanilla Valheim UI.
- **Guide chains.** Multi-step quests with per-step triggers, progress counters, and HUD tracking.
- **HUD Tracker.** On-screen objective tracker widget shows active guide chains with live progress bars. Toggle with F10 (configurable).
- **Codex panel.** In-game guide browser (F3) organised by category with full entry descriptions.
- **NPC Conversation system.** Hold E near a trader to open a dialogue panel with choice buttons. Choices can fire entries or grant rewards.
- **Reward system.** Grant items, skill experience, skill levels, and status-effect buffs on entry completion or conversation choices.
- **Discord integration.** Server-side webhook POSTs when entries fire or chains complete. Webhook URL stays on the server only.
- **Player vs Global scope.** Player-scoped entries track per-character; global entries fire for all connected players simultaneously and persist with the world save.
- **Firing controls.** `once`, `cooldown`, `requires`, and `stop_when` give full control over when entries fire.
- **Admin commands.** `vsg_reset` and `vsg_list` for testing and moderation from the F5 console.
- **Hot-reload.** Edit and save `guidance.yaml` — all connected clients receive the update instantly, no server restart needed.
