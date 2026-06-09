# CRIT-15 — Hearthbound Modpack Guide Plan

Master implementation plan for in-game guidance covering all player-facing mods in the
**Hearthbound Valheim Modpack** (TaegukGaming). Each guide fires through ValheimServerGuide's
trigger/display pipeline and is authored as YAML on the server.

---

## Mod Coverage

### Display Mode Rules (server-wide convention)

All triggers in this modpack follow a fixed display mode assignment. See
`wiki/Guide-Authoring-Reference.md` for the full reference.

| Trigger type | Display mode | Rationale |
|---|---|---|
| `craft`, `item_acquired`, `kill`, `build`, `chest_opened`, `skill_level`, `timed`, `boss_defeated` | `rune` | Player actions — dramatic full-screen acknowledgement; ghost mode protects the reader |
| `first_login`, `player_death` | `raven` | Environmental/existential events — ambient Hugin delivery |
| `npc_interacted`, `equip`, `location_entered` | `message` | Brief contextual tips — no disruption needed |
| `npc_conversation` | `conversation` | Hold-E NPC dialogue — choice-panel format |

### Simple One-Shot Introductions
*Single entry, fires once per player, no chain needed.*

| Mod | Trigger | Display | Purpose |
|---|---|---|---|
| Quick Stack / Store / Sort / Trash / Restock | `chest_opened` | `rune` | Explain bulk-store, quick-stack, sort, trash-slot keybinds |
| ComfyQuickSlots | `equip` (HelmetLeather) | `message` | Explain the extra equipment and utility quick slots |
| Recycle N Reclaim | `craft` (first craft) | `rune` | "Dismantle crafted items at the workbench to recover materials" |
| AzuCraftyBoxes | `build` (piece_workbench) | `rune` | "Nearby containers are pulled automatically during crafting" |

### Medium Depth — Intro + Follow-Up Steps
*2–4 triggered tips spread across early gameplay.*

| Mod | Steps | Triggers Used | Display Modes |
|---|---|---|---|
| ProtectiveWards | 3 | `build` (ward) → `timed` × 2 | `rune` → `rune` → `rune` |
| Groups | 3 | `first_login` → `timed` × 2 | `raven` → `rune` → `rune` |
| SimpleMarket | 3 | `first_login` → `item_acquired` (Coins) → `timed` | `raven` → `rune` → `rune` |
| Armoire | 3 | `build` (wardrobe) → `timed` × 2 | `rune` → `rune` → `rune` |

> **SimpleMarket:** No spawned NPC. Market accessed via inventory UI button (upper right
> corner). Available when the player has Comfort status. Chain starts with `first_login`.

### Quest-Chain Progressions
*Multi-step journeys. Step N only unlocks after Step N-1 is acknowledged.*

| Mod | Steps | Key Triggers | Display Modes |
|---|---|---|---|
| Offline Companions | 5 | `first_login` → `npc_interacted` (Haldor) → `item_acquired` (food) → `timed` × 2 | `raven` → `message` → `rune` → `rune` → `rune` |
| Wandering Companions | 4 | `first_login` → `boss_defeated` (Eikthyr) → `boss_defeated` (gd_king) → `player_death` | `raven` → `rune` → `rune` → `raven` |
| TraderOverhaul | 4 | `npc_conversation` (Haldor) → `npc_interacted` (Hildir) → (BogWitch) → `timed` | `conversation` → `message` → `message` → `rune` |
| HaldorBounties | 4 | `npc_interacted` (Haldor) → `timed` → `item_acquired` (Trophy_*) → `npc_interacted` (Haldor) | `message` → `rune` → `rune` → `message` |
| SlayerSkills | 4 | `item_acquired` (Trophy_*) → counter → `skill_level` × 2 | `rune` → `rune` → `rune` → `rune` |
| ImpactfulSkills | 4 | `skill_level` 25 → 50 → 75 → 100 | `rune` → `rune` → `rune` → `rune` |
| balrond_shipyard | 4 | `build` (shipyard) → `timed` × 2 → `boss_defeated` (Bonemass) | `rune` → `rune` → `rune` → `rune` |
| ZenBossStone | 6 | `first_login` → `boss_defeated` × 5 | `raven` → `rune` × 5 |
| More World Locations AIO | 3 | `location_entered` × 2 → `timed` | `message` → `message` → `rune` |

> **Offline Companions:** No recruiter NPC. Hire through Haldor for 2000 coins. Chain
> starts with `first_login` → `npc_interacted: Haldor`.
>
> **Wandering Companions:** No tameable creature prefab. Wanderers are hostile humanoids.
> Chain uses `boss_defeated` for progression warnings and `player_death` for a death tip.
>
> **ZenBossStone:** No buildable piece. Boss Stone is pre-placed in the Spawn Temple.
> Chain starts with `first_login` explaining the sacrifice mechanic.

---

## Phase Index

| Phase | File | Status |
|---|---|---|
| 1 | [Phase 01 — New Triggers](hearthbound/phase-01-new-triggers.md) | `complete` |
| 2 | [Phase 02 — Sequential Guide Chains](hearthbound/phase-02-guide-chains.md) | `complete` |
| 3 | [Phase 03 — Progress Counter Tracking](hearthbound/phase-03-progress-counters.md) | `complete` |
| 4 | [Phase 04 — Objective Tracker HUD](hearthbound/phase-04-hud-tracker.md) | `complete` (baseline) |
| 4a | [Phase 04a — Foundation Fixes](hearthbound/phase-04a-foundation-fixes.md) | `complete` |
| 4b | [Phase 04b — Hotkey Toggle & Badge](hearthbound/phase-04b-hotkey-toggle.md) | `complete` |
| 4c | [Phase 04c — Auto-Show, Fade & Highlight](hearthbound/phase-04c-auto-show-fade-highlight.md) | `done` |
| 4d | [Phase 04d — Hover Tooltips](hearthbound/phase-04d-hover-tooltips.md) | `done` |
| 4e | [Phase 04e — Progress Bars & Polish](hearthbound/phase-04e-progress-bars-polish.md) | `done` |
| 5 | [Phase 05 — In-Game Codex UI](hearthbound/phase-05-codex-ui.md) | `done` |
| 6 | [Phase 06 — YAML Schema Additions](hearthbound/phase-06-yaml-schema.md) | `done` |
| 7 | [Phase 07 — Admin Commands Expansion](hearthbound/phase-07-admin-commands.md) | `pending` |
| 8 | [Phase 08 — Discord Guide Events](hearthbound/phase-08-discord.md) | `done` |
| 9 | [Phase 09 — Content Authoring](hearthbound/phase-09-content.md) | `pending` |
| 10 | [Phase 10 — QoL Polish](hearthbound/phase-10-polish.md) | `done` |

---

## Implementation Order

```
Phase 1 + Phase 6   — Triggers and schema (do together; schema defines what triggers need)
Phase 2             — Chain architecture (depends on Phase 1 triggers and Phase 6 schema)
Phase 3             — Progress counters (extends Phase 2 chain state)
Phase 4             — HUD tracker baseline (depends on Phase 2 chain state)       [complete]
  Phase 04a         — Foundation fixes (font warning; login visibility)
  Phase 04b         — Hotkey toggle, hint badge, empty state
  Phase 04c         — Auto-show on progress, fade-out, row highlight
  Phase 04d         — Hover tooltips + step description field
  Phase 04e         — Progress bars, completion flash, badge count, SFX
Phase 5             — Codex UI (depends on Phase 2 chain state + Phase 6 categories)
Phase 7             — Admin commands (depends on Phase 2 chain state)
Phase 8             — Discord events (depends on Phase 2 chain completion hook)
Phase 9             — Content authoring (depends on all infrastructure phases)
Phase 10            — Polish (last; refines everything)
```

---

## Key Constraints

- All guides must use vanilla assets only. See CRIT-14.
- Server is the authority for all guide state and chain progression. See CRIT-06.
- Discord webhook URL never leaves the server. See CRIT-08.
- Chain state is stored in `m_customData` per player. See CRIT-12.
- All new trigger types must be registered in `GuidanceDispatcher.Matches`. See CRIT-02.
