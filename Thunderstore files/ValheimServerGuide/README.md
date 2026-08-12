# ValheimServerGuide

Turn your Valheim server into a guided experience. Write a YAML file on your server and players receive quest objectives, NPC dialogues, lore popups, and rewards — triggered automatically as they play, using only Valheim's own UI.

No custom assets. No custom UI skins. Just your words, delivered through Hugin, the chat, and dramatic cinematic screens.

---

## What it does

**Guides players through your content.** When a player crafts their first weapon, enters a dangerous biome, discovers the trader, or kills a boss — show them a message. Give them a quest. Give them a reward.

**Runs entirely from the server.** You write one YAML file. Every player connected to your server receives it automatically. Update it and save — all players get the new version instantly without restarting.

**Uses vanilla Valheim UI.** Messages appear as Hugin (the raven) popups, system toasts, chat lines, rune-tablet screens, or the dramatic intro cinematic style. Nothing looks out of place.

---

## Quick setup

1. Install this mod on your **server** (and on every player's game via r2modman).
2. Start the server. It creates `BepInEx/config/ValheimServerGuide/guidance.yaml` automatically.
3. Edit that file to add your guidance entries. You can split guidance across as many `*.yaml` / `*.yml` files as you like — including in nested subfolders — and they are all merged.
4. Save. All players receive the update without a restart.

The mod works in **single-player and host & play** as well — the host acts as the server.

---

## What you can create

**Triggered messages** — Show a Hugin raven popup, a toast, or a dramatic full-screen text when a player does something specific:

- Crafts an item for the first time
- Enters a biome
- Kills a creature
- Reaches a skill milestone
- Discovers the trader
- Opens a chest
- And many more...

**Multi-step quests** — Chain triggers into a quest with steps. "Gather 10 wood → craft a workbench → build a wall." Each step has its own trigger. Players pin the quests they care about from the in-game Codex (F3 → **Show on Tracker**) and follow their progress on a draggable on-screen panel (F10).

**NPC conversations** — Hold E near Haldor, Hildir, or BogWitch to open a dialogue panel. Give them choices. Fire quests. Grant rewards.

**Item turn-ins** — Have players bring a specific item to a trader NPC to complete a quest step.

**Rewards** — Grant items, skill experience, or status-effect buffs when an entry or quest completes.

**World events** — Mark an entry as `scope: global` and the first player to trigger it fires the display for *every* connected player simultaneously. Perfect for boss kills and world milestones.

**Discord announcements** — Post to a Discord channel when something happens (server-side only; your webhook URL is never exposed to players).

---

## Example

A raven popup when a player crafts their first bronze sword:

```yaml
- id: first_bronze_sword
  trigger:
    type: craft
    item: SwordBronze
  display:
    mode: raven
    topic: "Bronze Edge"
    text: "Forged in fire, this blade cleaves the Black Forest's beasts."
  once: true
```

A world event when the first boss falls:

```yaml
- id: world_eikthyr_fell
  scope: global
  trigger:
    type: kill
    creature: Eikthyr
  display:
    mode: intro
    topic: "The Stag-King Falls"
    text: "Eikthyr is slain. The realm trembles. Your trial begins, traveler."
  once: true
  announce:
    discord: "⚔️ **{playerName}** has slain **Eikthyr** — the first boss has fallen!"
```

Text tokens such as `{playerName}` (or `{player_name}` — the two are aliases), `{creatureName}`, `{itemName}`, and `{biome}` are substituted when the entry fires.

_New in 0.10.0:_ panels now size themselves to your text — the NPC conversation panel grows to fit and scrolls instead of cutting long dialogue off, `message` at `position: Center` word-wraps, and no display mode truncates any more. Plus a new `highlight:` block that colours chosen words or phrases inside any text, so the hotkey, the cost or the warning survives a skim.

_New in 0.9.1:_ `{playerName}` now expands everywhere author-written text is shown — the Guide Codex, the HUD tracker, NPC conversation headers and choice buttons, raven/intro topics, NPC hover text, and every reward message — instead of only in the message shown at the moment an entry fires.

_New in 0.9.0:_ the **rune** display mode is now a fully customizable, game-themed panel — set your own header/body/list fonts, colors, and alignment per entry, add a bullet list, and enjoy a smooth fade in/out. Plus seven new Lost Scrolls II trigger types for party duels, the party ladder, and bracket tournaments.

---

## Documentation

Full configuration guides, all trigger types, display mode options, quest chains, NPC conversation setup, reward configuration, and admin commands are covered in the [Wiki](https://github.com/yesu0725/Valheim-ServerGuide/wiki).

---

## Try it out

This mod was built for the **TaegukGaming community server** running the **Hearthbound modpack**. If you want to see it in action:

**[Hearthbound Valheim Modpack](https://thunderstore.io/c/valheim/p/TaegukGaming/Hearthbound_Valheim_Modpack/)**

---

## Disclaimer

This mod is **created using AI**. No other mods were copied during the process. All feature ideas come from the uploader and are mainly to cater to the needs of the **TaegukGaming community server**. If any features or ideas look similar to other mods, these are not intentional.

This mod is **free to use as is**. Voluntary support is appreciated.

---

**Version:** 0.10.0
**Source / issues / wiki:** https://github.com/yesu0725/Valheim-ServerGuide
