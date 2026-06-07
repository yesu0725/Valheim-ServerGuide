# Changelog

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
