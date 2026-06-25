# ValheimServerGuide — Feature Roadmap

> Created: 2026-06-11  
> Status: Approved, pending implementation  
> Development order: Phase 1 → 2 → 3 → 4 → 5 → 6

---

## Development Order Summary

| Order | Phase | Summary |
|---|---|---|
| **1** | Phase 1 ✅ `done` | Kill-count trigger + 8 new interaction triggers — see [CRIT-19](/.claude/criteria/CRIT-19-phase1-triggers.md) |
| **2** | Phase 2 ✅ `done` | Time & day triggers — see [CRIT-20](/.claude/criteria/CRIT-20-phase2-time-day-triggers.md) |
| **3** | Phase 3 ✅ `done` | Multi-quest NPC selection menu — see [CRIT-21](/.claude/criteria/CRIT-21-phase3-multi-quest-picker.md) |
| **4** | Phase 4 ✅ `done` | Conversation sequencing — multi-node dialogue trees — see [CRIT-22](/.claude/criteria/CRIT-22-phase4-conversation-sequencing.md) |
| **5** | Phase 5 ✅ `done` | Enhanced rewards (13 new reward types) — see [CRIT-23](/.claude/criteria/CRIT-23-phase5-enhanced-rewards.md) |
| **6** | Phase 6 ✅ `done` | System polish — journal HUD, bubble mode, debug command, hover text, group sync — see [CRIT-24](/.claude/criteria/CRIT-24-phase6-system-polish.md) |

---

## Phase 1 — Kill-Count Trigger + Extended Interaction Triggers

**Goal:** Expand the trigger vocabulary with zero new architecture — new Harmony patches and a `count` field on the existing kill trigger.

### Kill Count
Add a `count` field to the `kill` trigger spec. Progress stored in `VSG.fc.<entry_id>` (same bucket as `item_acquired` multi-goal). Entry fires when the running count reaches the target.

```yaml
trigger:
  type: kill
  creature: Neck
  count: 10          # fires after 10 kills; omit for any single kill
```

### New Interaction Triggers

| Trigger type | Valheim hook |
|---|---|
| `ward_activated` | `PrivateArea.Interact` |
| `tamed_creature` | `Character.SetTamed` or `Tameable.TamingUpdate` |
| `sign_read` | `Sign.Interact` |
| `crafting_table_used` | `CraftingStation.Interact` |
| `cooking_used` | `CookingStation.Interact` / `Fireplace.Interact` |
| `portal_used` | `TeleportWorld.Interact` |
| `tombstone_picked` | `TombStone.Interact` |
| `ship_sailed` | `Ship.OnTriggerStay` / velocity threshold |

YAML shape:
```yaml
trigger:
  type: crafting_table_used
  station: piece_workbench   # prefab name; omit for any station

trigger:
  type: tamed_creature
  creature: Lox              # prefab name; omit for any creature

trigger:
  type: portal_used
  tag: home                  # portal tag; omit for any portal
```

---

## Phase 2 — Time & Day Triggers

**Goal:** Fire entries based on real-world clock or in-game day/time — for seasonal events, daily login rewards, timed lore reveals.

A background coroutine in `TimeTrigger.cs` polls on a 30-second tick and calls `GuidanceDispatcher.Raise()` with a synthetic `TriggerEvent`. Gate logic (cooldown, once) prevents duplicate fires.

```yaml
trigger:
  type: time_of_day
  game_time_fraction: 0.0   # 0.0 = midnight, 0.5 = noon (EnvMan.GetDayFraction)
  window: 0.02              # ± tolerance (~29 in-game minutes)

trigger:
  type: day_number
  day: 7                    # in-game day counter (EnvMan.instance.GetDay)

trigger:
  type: real_world_time
  utc_hour: 20
  utc_minute: 0             # fires at 20:00 UTC daily

trigger:
  type: day_of_week
  day: Saturday             # real-world weekday
```

`once: true` + existing cooldown field covers "fire only once ever" vs "fire every matching day."

---

## Phase 3 — Multi-Quest NPC Selection Menu

**Goal:** When a player holds E on an NPC that has 2+ eligible `npc_conversation` entries, show a "What would you like to discuss?" picker before entering any specific conversation.

**How it works:**
1. `GuidanceDispatcher` collects all gate-passing `npc_conversation` entries for the NPC.
2. Count = 1 → behaves exactly as today (no change).
3. Count ≥ 2 → opens `NpcConversationPanel` in **selection mode**: header = NPC name, body = list of entry titles as choice buttons.
4. Selecting one fires `FireEntry()` for that entry and opens its conversation normally.

New top-level field on `GuidanceEntry`:
```yaml
title: "The Missing Shipment"    # shown in multi-quest picker and quest journal
```

Entries without `title` fall back to `id`. No existing behavior breaks.

---

## Phase 4 — Conversation Sequencing (Multi-Node Dialogue Trees)

**Goal:** Conversations are a graph of **nodes**, each with its own text and choices. Choices can be conditionally locked. State persists so the NPC remembers where you left off.

### YAML Schema Extension

```yaml
display:
  type: conversation
  npc_name: Haldor
  resume_on_return: true      # if player closes mid-convo, reopen at last node
  nodes:
    - id: intro
      text: "You look like someone who's been in the mires before."
      choices:
        - label: "I've fought worse."
          goto_node: fought_worse
        - label: "What's it to you?"
          goto_node: suspicious
        - label: "[Ask about bounty]"
          requires: ["bounty_board_read"]   # per-choice gate
          goto_node: bounty_talk
          hidden_when_locked: true          # hide vs. grey out
    - id: fought_worse
      text: "Good. I need someone fearless for a task..."
      choices:
        - label: "Tell me more."
          goto: bounty_start_entry          # cross-entry goto (existing mechanic)
        - label: "Not interested."
          closes_conversation: true
    - id: suspicious
      text: "Fair enough. Move along then."
      closes_conversation: true
```

### State Persistence
Current node stored in `m_customData["VSG.cs.<entry_id>"]`. On `resume_on_return: true`, `NpcConversationPanel.Open()` reads this key to start at the saved node instead of `nodes[0]`.

### Locked Choice Rendering
- `hidden_when_locked: true` → choice not rendered at all
- `hidden_when_locked: false` (default) → choice rendered greyed/disabled with optional `locked_hint:` tooltip text

---

## Phase 5 — Enhanced Reward Types

**Goal:** Reward players with more than items and skill XP.

### Full Reward Type Table

| Type | Description |
|---|---|
| `map_pin` | Add a named map pin at explicit coordinates |
| `location_pin` | Find nearest instance of a location by prefab name and add a map pin |
| `unlock_recipe` | Teach the player a recipe by prefab name |
| `spawn_creature` | Spawn a creature near the player (tamed or neutral) |
| `set_global_key` | Set a Valheim world global key |
| `remove_global_key` | Remove a world global key |
| `set_player_key` | Set a personal key on the player (compatible with other mods) |
| `remove_player_key` | Remove a personal key from the player |
| `weather` | Force an environment preset for N seconds |
| `chat_message` | NPC speaks a chat message attributed to npc_name |
| `teleport` | Warp player to coordinates (server allowlist only) |
| `rename_player` | Assign a custom title/suffix shown in chat |
| `discord` | Per-reward webhook post, separate from entry-level Discord |

### YAML Examples

```yaml
rewards:
  - type: map_pin
    name: "Haldor's Camp"
    x: 512
    z: -200
    icon: trader

  - type: location_pin
    location: Runestone_Boars    # prefab name of any vanilla or mod location
    pin_name: "Strange Runestone"
    icon: dot

  - type: set_global_key
    key: defeated_eikthyr

  - type: remove_global_key
    key: defeated_eikthyr

  - type: set_player_key
    key: mymod_quest_complete     # stored in Player.m_uniqueKeys

  - type: remove_player_key
    key: mymod_quest_complete

  - type: unlock_recipe
    recipe: Recipe_SwordBlackmetal

  - type: spawn_creature
    prefab: Lox
    tamed: true
    count: 1

  - type: weather
    preset: ThunderStorm
    duration: 120

  - type: chat_message
    message: "Well done, {player_name}."

  - type: teleport
    x: 512
    z: -200
    allowlist_only: true          # server enforces allowed destination list

  - type: rename_player
    suffix: "the Undaunted"

  - type: discord
    message: "{player_name} completed the trial."
```

### Implementation Notes

**`location_pin`:** Uses `ZoneSystem.FindClosestLocation()` server-side (same internals as the `find` console command). Server resolves coords → sends to client via RPC → client writes map pin into `Minimap`. Works with any location prefab registered by any mod.

**`set_player_key` / `remove_player_key`:** Calls `Player.AddUniqueKey()` / removes from `Player.m_uniqueKeys` so other mods that check `Player.HaveUniqueKey()` see them correctly.

**`teleport`:** Server-authoritative. Destination must be on a server-configured allowlist to prevent abuse.

---

## Phase 6 — System Improvements & UX Polish

**Goal:** Cross-cutting quality-of-life features for players and server admins.

### 1. Quest Journal HUD
Persistent collapsible sidebar listing all active multi-step entries for the local player. Shows `title`, current step index, mini progress bar. Toggle with configurable keybind. Implemented as `QuestJournalPanel.cs` on the vanilla `Hud` canvas.

### 2. Proximity Chat Bubble (`bubble` display mode)
Renders text above the NPC's head in world-space `TextMeshPro` without opening any panel. Useful for flavor text when walking past NPCs.

```yaml
display:
  type: bubble
  npc_name: Haldor
  duration: 6      # seconds visible
```

### 3. `vsg_debug` Admin Command
Extends `AdminCommands.cs`. Dumps for a named player:
- All entries currently eligible to fire (gates passing)
- All `VSG.*` keys in their `m_customData`
- Last 10 fired entry IDs with timestamps

### 4. NPC Hover Text Override
Per-entry `hover_text:` field replaces the vanilla "Hold E to interact" string, keyed by state.

```yaml
hover_text:
  default: "[Quest] The Missing Shipment"
  after_fire: "[Completed] The Missing Shipment"
```

### 5. Group/Party Quest Progress Sync
For `player` scope entries with count-based goals (`kill: count`, `item_acquired: count`), an optional `share_progress: true` flag pools increment RPCs across nearby group members so the whole party advances the same counter.

---

## Phase Dependency & Risk Summary

| Phase | Depends on | Risk | Complexity |
|---|---|---|---|
| 1 | Nothing | Low | Small |
| 2 | Phase 1 patterns | Low | Small |
| 3 | Nothing | Low | Small–Medium |
| 4 | Phase 3 | Medium | Large |
| 5 | Phase 1 (kill-count pattern) | Medium | Medium |
| 6 | Phases 3–5 | Low–Medium per sub-feature | Medium |
