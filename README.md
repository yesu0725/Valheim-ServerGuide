# ValheimServerGuide

A server-authoritative, YAML-driven in-game guidance mod for Valheim. Write a `guidance.yaml` on your server and players receive triggered popups, quest chains, NPC conversations, and rewards — all using only vanilla Valheim UI. No custom assets. No custom prefabs.

[![BepInEx](https://img.shields.io/badge/BepInEx-5.x-green)](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
[![Jötunn](https://img.shields.io/badge/Jötunn-required-blue)](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/)
[![Framework](https://img.shields.io/badge/Framework-net48-lightgrey)]()

---

## Overview

```
[ server admin edits guidance.yaml ]
         │
         ▼
GuidanceConfigLoader  ──►  GuidanceSync (Jötunn RPC)
                                   │
                                   ▼
                        [ all connected clients ]
                                   │
                                   ▼
              Harmony patches on game methods raise events
                                   │
                                   ▼
                         GuidanceDispatcher
                          ├── Raven popup
                          ├── MessageHud toast
                          ├── Chat line
                          ├── Rune viewer
                          ├── Intro cinematic
                          └── NPC conversation panel
```

The **server** loads `BepInEx/config/ValheimServerGuide/guidance.yaml`, watches it for changes, and pushes the serialized config to every client on connect and on hot-reload. **Clients** never read the YAML themselves — they only run Harmony patches and a dispatcher that matches game events against the synced config.

---

## Features

### Trigger Types

| `trigger.type` | Fires when… |
|---|---|
| `craft` | Player crafts a specific item |
| `item_acquired` | Player picks up a specific item |
| `kill` | Player kills a specific creature |
| `build` | Player places a specific piece |
| `distance` | Player steps within `radius` of a location |
| `biome` | Player enters a biome |
| `skill_level` | Player's skill crosses a threshold |
| `discover_location` | Player uncovers a named map location |
| `damage_type` | Player takes a specific damage type |
| `npc_interacted` | Player presses E on a trader NPC |
| `npc_conversation` | Player holds E on a trader NPC (≥ 0.5 s) |
| `npc_item_submit` | Player uses a specific item on a trader NPC |
| `boss_defeated` | A specific boss is killed (world event) |
| `first_login` | Player's first ever login to this world |
| `player_death` | Player dies |
| `chest_opened` | Player opens a specific chest type |
| `timed` | Fires on a repeating interval (e.g. every 30 min) |
| `entry_finished` | Another guidance entry completes |

### Display Modes

| `display.mode` | Vanilla UI | Best for |
|---|---|---|
| `raven` | Hugin tutorial popup | First-time hints, lore |
| `message` | `MessageHud.ShowMessage` | Quick tips, toasts |
| `chat` | `Chat.AddString` | Verbose info, persistent |
| `rune` | `TextViewer` rune style | Lore tablets, milestones |
| `intro` | `TextViewer` intro style | Story beats, cinematics |
| `conversation` | Custom panel (vanilla UI) | NPC dialogue trees |

`rune` and `intro` mode put the player in ghost mode (untargetable) for the duration. `intro` additionally pins the vanilla Valkyrie intro music track.

### Firing Semantics

- `once: true` — fires once per character (player scope) or once per world (global scope)
- `cooldown: <seconds>` — throttle recurring tips
- `requires: [id, …]` — prerequisite entries must have fired first
- `stop_when: [id, …]` — stops firing once any listed entry has fired
- `scope: global` — entire world sees the display simultaneously; state persists in the world save

### Guide Chains

Multi-step quests where each step has its own trigger. Progress is tracked in the HUD tracker and Codex. Steps can have progress counters (e.g. "Collect 5 trophies"). The chain only advances once the current step's trigger fires.

### HUD Tracker & Codex

A progress panel (default `F10`) shows the quests the player has **pinned** from the Codex, with live progress. The panel is hidden by default — players open the Codex (default `F3`), select an in-progress quest, and click **Show on Tracker** to pin it. Pinning unhides the panel; `F10` hides/shows it; pinned quests persist across the session. The panel no longer locks movement or shows the cursor, and it can be dragged anywhere while the inventory or ESC menu is open. The Codex itself shows all guidance entries organised by category with full descriptions and completion status.

### NPC Conversations

Holding E (≥ 0.5 s) near Haldor, Hildir, or BogWitch opens a dialogue panel instead of the store. Choice buttons can fire other entries or grant rewards. Input is locked while the panel is open — no movement, no attacks, no pause menu.

### Reward System

Entries and conversation choices can grant rewards on completion:

- `item` — adds items directly to inventory (drops on ground if full)
- `skill_exp` — adds raw experience to a skill
- `skill_level` — jumps a skill to a target level (never lowers)
- `buff` — applies a status effect (e.g. `SE_Rested`) with optional duration override

A reward notification (center `MessageHud`) summarises what was granted.

### Discord Integration

Server-side webhook POSTs when entries fire or chains complete. The webhook URL lives only on the server and is never sent to clients. Templates support `{playerName}`, `{id}`, `{topic}`, `{text}` tokens.

### Admin Commands

| Command | Effect |
|---|---|
| `vsg_reset all` | Clear all player-scope fired IDs for the current character |
| `vsg_reset <id>` | Clear a single entry's fired state (auto-detects scope) |
| `vsg_list` | Print fired IDs and all configured IDs with tags |

---

## Build & Install

### Prerequisites

- Valheim + BepInEx 5 + Jötunn installed
- .NET SDK (for `dotnet build`)

### Build

```bash
dotnet build src/ValheimServerGuide.csproj -c Release
```

The build auto-deploys to `BepInEx/plugins/ValheimServerGuide/` if `$(VALHEIM_INSTALL)` exists. Override:

```bash
dotnet build -c Release -p:VALHEIM_INSTALL="D:\Games\Valheim"
```

Also deploys to r2modman test profile (`$(R2MODMAN_PROFILE_DIR)`) and dedicated server (`$(VALHEIM_DEDICATED_SERVER_DIR)`) if those paths exist.

### Install from Thunderstore

Install via r2modman or Thunderstore mod manager. The mod belongs on both the **server** and every **client**. Only the server reads the YAML; clients without the mod simply won't see the guidance popups.

---

## Configuration

### guidance.yaml

Place at `BepInEx/config/ValheimServerGuide/guidance.yaml` on the server (or single-player host). The mod writes a starter file on first launch. It watches for changes and hot-reloads automatically — no restart needed.

See [`examples/guidance.yaml`](examples/guidance.yaml) for a fully-commented example covering all trigger types and display modes.

### BepInEx Config (`com.valheimserverguide.cfg`)

| Key | Default | Description |
|---|---|---|
| `RavenEnabled` | `true` | Enable raven display mode (independent of vanilla Tutorials toggle) |
| `IntroMusicName` | `intro` | Music track for intro display mode |
| `IntroMusicDuration` | `60` | Seconds intro music stays pinned |
| `IntroFadeInDuration` | `3.0` | Screen fade duration (seconds) |
| `ChatColor` | `#E0C078` | Hex color for chat-mode messages |
| `TrackerEnabled` | `true` | Show HUD objective tracker |
| `TrackerPosition` | `TopRight` | Tracker anchor: `TopRight`, `TopLeft`, `BottomRight`, `BottomLeft` |
| `TrackerMaxVisible` | `3` | Max chains shown before "+N more" |
| `TrackerHotkey` | `F10` | Toggle hotkey for tracker panel |
| `CodexEnabled` | `true` | Enable Codex panel |
| `CodexKey` | `F3` | Toggle hotkey for Codex |
| `WebhookUrl` | *(empty)* | Discord webhook URL (server only) |
| `DefaultTemplate` | see cfg | Default Discord message template |

All tracker and display settings can also be overridden per-server via a `tracker:` section in `guidance.yaml`.

---

## YAML Schema Reference

```yaml
tracker:                        # optional — overrides BepInEx tracker config
  enabled: true
  anchor: TopRight              # TopRight | TopLeft | BottomRight | BottomLeft
  hotkey: F10
  offset_x: 46
  offset_y: 320
  width: 210
  font_size: 15
  auto_hide_delay: 5            # deprecated/ignored — the panel no longer auto-hides
  fade_duration: 1             # deprecated/ignored
  highlight_duration: 3
  completion_vfx_enabled: true
  badge_enabled: true

guidances:
  - id: my_entry                # unique key; used by requires/stop_when/vsg_reset
    title: "Entry Title"        # shown in Codex and HUD tracker
    category: "Crafting"        # Codex left-panel group

    trigger:
      type: craft               # see trigger types table
      item: SwordBronze         # prefab name (for craft/item_acquired/kill/build etc.)
      creature: Troll
      piece: Stone_floor
      biome: AshLands
      location: Vendor_BlackForest
      radius: 50                # for distance trigger
      skill: Swords             # for skill_level trigger
      level: 50                 # for skill_level trigger
      npc: Haldor               # for npc_interacted / npc_conversation / npc_item_submit
      count: 5                  # for npc_item_submit: items needed
      consume: true             # for npc_item_submit: remove items from inventory
      interval: "30m"           # for timed trigger
      entry: other_entry_id     # for entry_finished trigger

    display:
      mode: raven               # raven | message | chat | rune | intro | conversation
      topic: "My Topic"         # header / Hugin popup title
      text: "My text…"          # body; supports {playerName} {itemName} {creatureName} {biome}
      position: TopLeft         # for message mode: TopLeft | Center

    message: "Short text"       # alternative to display.text for single-entry entries
    once: true                  # fire once per character (player scope) or world (global)
    cooldown: 600               # seconds; alternative to once
    scope: player               # player (default) | global
    sound: "sfx_trophie_deer"   # optional vanilla SFX prefab name
    version: 1                  # bump to re-show updated entries to players who already saw it
    discord_on_complete: false  # POST webhook when chain completes

    requires: [other_id]        # must have fired before this entry is eligible
    stop_when: [another_id]     # stop firing once any of these have fired

    announce:
      discord: "**{playerName}** did the thing!"  # "" = use DefaultTemplate

    rewards:
      - type: item
        item: SwordBronze
        amount: 1
        quality: 2
      - type: skill_exp
        skill: Swords
        amount: 500
      - type: skill_level
        skill: Run
        level: 30
      - type: buff
        effect: SE_Rested
        duration_override: 600

    # Multi-step chain (overrides trigger/display/once on parent)
    steps:
      - trigger: { type: craft, item: Wood }
        message: "Step 1: Gather wood."
        description: "Chop any tree to collect wood."   # HUD tracker tooltip
      - trigger: { type: craft, item: SwordBronze }
        message: "Step 2: Forge a bronze sword."
        progress_goal: 3           # counter step: fires only after 3 triggers
        progress_trigger:
          type: kill
          creature: Troll
        progress_label: "Trolls"

    # NPC conversation (display.mode: conversation)
    conversation:
      choices:
        - text: "I'll take the sword."
          goto: another_entry_id  # optional — fires this entry on selection
          rewards:
            - type: item
              item: SwordBronze
              amount: 1
        - text: "Not now."
```

---

## Source Layout

```
src/
├── Plugin.cs                        BepInEx entry, config, loader lifecycle
├── Config/
│   ├── GuidanceConfig.cs            YAML data model (GuidanceEntry, TriggerSpec, DisplaySpec, …)
│   └── GuidanceConfigLoader.cs      FileSystemWatcher + debounce + starter YAML
├── State/
│   ├── SeenTracker.cs               Fire-state storage (player m_customData / world ZoneSystem keys)
│   ├── ChainState.cs                Multi-step chain progress tracking
│   ├── PrerequisiteChecker.cs       requires / stop_when evaluation
│   ├── SubmitState.cs               In-progress item collection counters for npc_item_submit
│   └── TrackedQuestState.cs         HUD tracker pins + saved panel position (per-player)
├── Triggers/
│   ├── GuidanceDispatcher.cs        Match-and-fire logic; player vs global routing
│   ├── CraftTrigger.cs              InventoryGui.DoCrafting / Player.OnCrafted
│   ├── KillTrigger.cs               Character.OnDeath
│   ├── ItemAcquiredTrigger.cs       Humanoid.Pickup
│   ├── BossDefeatedTrigger.cs       Boss death detection
│   ├── FirstLoginTrigger.cs         First-login world event
│   ├── PlayerDeathTrigger.cs        Player.OnDeath
│   ├── ChestOpenedTrigger.cs        Container.Interact
│   ├── SkillLevelTrigger.cs         Skills.RaiseSkill
│   ├── LocationEnteredTrigger.cs    Minimap.DiscoverLocation + distance/biome polling
│   ├── NpcInteractedTrigger.cs      Trader.Interact (tap E)
│   ├── NpcConversationTrigger.cs    Trader.Interact (hold E) + hold detector
│   ├── NpcItemSubmitTrigger.cs      Trader.UseItem + GetHoverText
│   ├── TimedTrigger.cs              Coroutine-based interval trigger
│   └── TriggerUtils.cs              Shared helpers
├── Display/
│   ├── GuidanceDisplay.cs           All display modes + Harmony patches for raven/intro/music
│   ├── GuidanceHudTracker.cs        Objective tracker widget (HUD)
│   ├── GuidanceCodex.cs             Codex browser panel (F3)
│   └── NpcConversationPanel.cs      Hold-E conversation panel with choice navigation
├── Rewards/
│   ├── RewardDispatcher.cs          Grant items / skill exp / buffs on entry completion
│   └── RewardNotification.cs        MessageHud summary after rewards are granted
├── Net/
│   └── GuidanceSync.cs              ZRoutedRpc RPCs; server↔client config & global-event sync
├── Commands/
│   └── AdminCommands.cs             vsg_reset / vsg_list Terminal.ConsoleCommand
└── Discord/
    └── DiscordAnnouncer.cs          Server-side webhook POST via UnityWebRequest
```

---

## Key Invariants

1. **Vanilla assets only** — no custom textures, prefabs, or AssetBundles.
2. **Server is the authority** — clients never override server config.
3. **Discord URL stays server-side** — never sent over RPC to clients.
4. **Raven mode has its own toggle** — `RavenEnabled` BepInEx config, independent of the vanilla "Tutorials" game setting.
5. **YamlDotNet.dll is NOT deployed** — Jötunn's transitive dep provides it at runtime.
6. **RPC names are registered exactly once** — `_rpcsBound` guard in `GuidanceSync`; reset in `ZNet.OnDestroy`.

---

## License

Free to use. No other mods were copied during development.

> Created for the **TaegukGaming** community server running the **Hearthbound** modpack.
