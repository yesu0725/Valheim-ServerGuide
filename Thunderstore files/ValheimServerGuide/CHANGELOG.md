# Changelog
## 0.3.1

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
