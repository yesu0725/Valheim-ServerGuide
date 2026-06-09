# Phase 09 — Content Authoring

**Status:** `pending`
**Depends on:** Phases 01–08, 10 (all complete). Phase 07 (Admin Commands) is still `pending`
but is not a blocker for content — content can be authored and tested without it.
**Blocks:** nothing (this is the deliverable)

Write all YAML guide entries for every player-facing mod in the Hearthbound modpack.
All entries go in `BepInEx/config/ValheimServerGuide/hearthbound_guides.yaml` (or any
`*.yaml` file in that directory — the loader merges all files).

---

## Display Mode Rules

Every trigger type maps to a fixed display mode. **Do not deviate from these rules
without updating `wiki/Guide-Authoring-Reference.md`.**

| Trigger type | Required display mode | Notes |
|---|---|---|
| `craft` | `rune` | Must include `display.topic` |
| `item_acquired` | `rune` | Must include `display.topic` |
| `kill` | `rune` | Must include `display.topic` |
| `build` | `rune` | Must include `display.topic` |
| `chest_opened` | `rune` | Must include `display.topic` |
| `skill_level` | `rune` | Must include `display.topic` |
| `timed` | `rune` | Must include `display.topic` |
| `boss_defeated` | `rune` | Must include `display.topic` |
| `first_login` | `raven` | Must include `display.topic` |
| `player_death` | `raven` | Must include `display.topic` |
| `equip` | `message` | `display.topic` not shown in message mode |
| `npc_interacted` | `message` | `display.topic` not shown in message mode |
| `location_entered` | `message` | `display.topic` not shown in message mode |
| `npc_conversation` | `conversation` | Requires `conversation.choices` block |

---

## Infrastructure Available to Content Authors

All of the following are implemented and available for use in YAML:

| System | Trigger / Field | Notes |
|---|---|---|
| Chains (multi-step) | `steps:` list | Phase 02 — complete |
| Progress counters | `progress_goal` + `progress_trigger` | Phase 03 — complete |
| HUD tracker | automatic (title + category required) | Phases 04–04e — complete |
| Codex UI | automatic (F3 key) | Phase 05 — complete |
| Discord on complete | `discord_on_complete: true` | Phase 08 — complete |
| Rewards | `rewards:` block (item / skill_exp / skill_level / buff) | CRIT-18 — complete |
| NPC conversation panel | `trigger.type: npc_conversation` + `display.mode: conversation` | CRIT-17 — complete |
| NPC item submission | `trigger.type: npc_item_submit` | CRIT-02 — complete |
| Chain completion chaining | `trigger.type: entry_finished` | CRIT-16 — complete |
| Step hover tooltips | `description:` on each step | Phase 04d — complete |
| Template variables | `{player_name}` `{skill}` `{level}` `{step}` `{total}` | Phase 10-B — complete |
| Guide versioning | `version:` on entry | Phase 10-A — complete |
| Stop conditions | `stop_when: [entry_id]` | CRIT-01 — complete |
| Multi-file YAML loading | Any `*.yaml` in config dir is merged | Loader update — complete |

### Trigger Types Available

| Type | Fires When | Key YAML Field |
|---|---|---|
| `craft` | Player crafts an item | `trigger.item` |
| `kill` | Player kills a creature | `trigger.creature` |
| `boss_defeated` | Player kills a boss | `trigger.creature` |
| `first_login` | Player's very first login (once per character) | — |
| `chest_opened` | Player opens a container (once per character) | — |
| `skill_level` | Player crosses a skill level threshold | `trigger.skill` + `trigger.level` |
| `item_acquired` | Player picks up an item (wildcard `Trophy_*` supported) | `trigger.item` |
| `location_entered` | Player enters a POI radius (once per location) | `trigger.location` |
| `npc_interacted` | Player taps E on a trader NPC | `trigger.npc` |
| `npc_conversation` | Player holds E (≥ 0.5 s) on a trader NPC | `trigger.npc` |
| `npc_item_submit` | Player presses hotbar key 1-8 near a trader | `trigger.npc` + `trigger.item` |
| `timed` | Server-side interval fires | `trigger.id` + `trigger.interval` |
| `player_death` | Player dies (respects `trigger.max_fires`) | — |
| `entry_finished` | Another entry/chain completes | `trigger.entry` |
| `equip` | Player equips an item (local player only; also fires on load re-equip) | `trigger.item` (prefab name) |
| `build` | Player places a piece (local player only) | `trigger.piece` (prefab name, e.g. `piece_workbench`) |

### NOT YET IMPLEMENTED (do not use)

- `biome` — hook into biome change (placeholder in CRIT-02, no trigger file exists). Use `first_login` + `boss_defeated` as biome-progression proxies.
- `distance` — proximity-based trigger (planned raven mode; no trigger file exists).
- `discover_location` — map fog-of-war reveal (planned raven mode; no trigger file exists).

Any entry using these types will never fire.

---

## Delivery File

```
BepInEx/config/ValheimServerGuide/hearthbound_guides.yaml
```

Organized by category. The loader reads all `*.yaml` files in the config directory,
so this file is additive alongside any existing `guidance.yaml`.

---

## Guide Inventory

### Category: Inventory (4 guides — simple one-shot)

#### `quickstack_intro`
- **Mod:** Quick Stack / Store / Sort / Trash / Restock
- **Type:** Single entry
- **Trigger:** `chest_opened`
- **Display:** `rune` (topic: "Inventory Tips")
- **Message:** Explain the bulk-store keybind, quick-stack to nearby chests, sort button,
  and the trash slot in the corner of the inventory panel.

#### `comfy_quickslots_intro`
- **Mod:** ComfyQuickSlots
- **Type:** Single entry
- **Trigger:** `equip` (item: `HelmetLeather`)
- **Display:** `message` (Center) — `equip` is not in the rune/raven list
- **Message:** Explain the extra equipment slots (head, chest, legs, utility) and the
  utility quick-slots added to the HUD bottom bar.

#### `recycle_intro`
- **Mod:** Recycle N Reclaim
- **Type:** Single entry
- **Trigger:** `craft` (fires on first craft of any item)
- **Display:** `rune` (topic: "Recycle N Reclaim")
- **Message:** "You can dismantle crafted items at a workbench to recover most of the
  materials used."

#### `craftyboxes_intro`
- **Mod:** AzuCraftyBoxes
- **Type:** Single entry
- **Trigger:** `build` (piece: `piece_workbench`)
- **Display:** `rune` (topic: "AzuCraftyBoxes")
- **Message:** "Nearby containers are automatically pulled during crafting — no need to
  carry all your materials."

---

### Category: Groups (1 guide — medium, 3 steps)

#### `groups_chain`
- **Mod:** Groups
- **Type:** Chain (3 steps)

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `first_login` | `raven` topic "Party System" | Introduce the party system; mention the Groups menu button in the HUD |
| 2 | `timed` 300 s (`groups_invite_tip`) | `rune` topic "Groups" | How to invite a friend; shared map pings |
| 3 | `timed` 300 s (`groups_coop_tip`) | `rune` topic "Groups" | Shared loot ping and co-op XP bonuses |

---

### Category: Building (3 guides — medium, 2–3 steps each)

#### `protectivewards_chain`
- **Mod:** ProtectiveWards
- **Type:** Chain (3 steps)

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `build` piece `guard_stone` | `rune` topic "ProtectiveWards" | Expanded ward radius, griefing protection; hover tooltip covers permissions detail |
| 2 | `timed` 60 s (`pwards_permissions`) | `rune` topic "ProtectiveWards" | Ward permissions: who can interact, build, and pick up items |
| 3 | `timed` 120 s (`pwards_upgrade`) | `rune` topic "ProtectiveWards" | Empowering the ward with Surtling Cores |

#### `armoire_chain`
- **Mod:** Armoire
- **Type:** Chain (3 steps)

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `build` piece `ArmoirePiece` | `rune` topic "Armoire Wardrobe" | Introduce cosmetic appearances; stats unchanged |
| 2 | `timed` 30 s (`armoire_equip_tip`) | `rune` topic "Armoire Wardrobe" | How to apply an unlocked appearance |
| 3 | `timed` 90 s (`armoire_presets`) | `rune` topic "Armoire Wardrobe" | Saving and loading outfit presets |

#### `shipyard_chain`
- **Mod:** balrond_shipyard
- **Type:** Chain (4 steps)

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `build` piece `ShipyardStation` | `rune` topic "Balrond Shipyard" | Custom hulls, figureheads, cargo holds |
| 2 | `timed` 60 s (`shipyard_first_ship`) | `rune` topic "Balrond Shipyard" | Hull types and component slots |
| 3 | `timed` 120 s (`shipyard_upgrade_tip`) | `rune` topic "Balrond Shipyard" | Upgrading sails, cargo, figureheads |
| 4 | `boss_defeated` `Bonemass` | `rune` topic "Ocean Voyage" | Ocean-ready ship advice for post-Bonemass exploration |

---

### Category: Trading (3 guides — medium to chain)

#### `simplemarket_chain`
- **Mod:** SimpleMarket
- **Type:** Chain (3 steps)
- **Design note:** SimpleMarket has **no spawned NPC**. The market is a button in the
  upper right corner of the inventory UI. It requires Comfort status to access (resting
  near beds, fires, and furniture at the player's base). Chain starts with `first_login`.

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `first_login` | `raven` topic "Player Market" | Introduce market button location and comfort requirement |
| 2 | `item_acquired` `Coins` | `rune` topic "SimpleMarket" | Deposit coins; async buy/sell between players |
| 3 | `timed` 120 s (`sm_browse_tip`) | `rune` topic "SimpleMarket" | How to list items; how to browse listings |

#### `traderoverhaul_chain`
- **Mod:** TraderOverhaul
- **Type:** Chain (4 steps)

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `npc_conversation` `Haldor` (Hold E) | `conversation` | TraderOverhaul intro; two dismiss choices |
| 2 | `npc_interacted` `Hildir` | `message` Center | Hildir's cosmetics and expanding catalog |
| 3 | `npc_interacted` `BogWitch` | `message` Center | Bog Witch's late-game catalog |
| 4 | `timed` 120 s (`to_economy_tip`) | `rune` topic "Trader Economy" | All traders accept coins; catalogs expand with boss kills |

#### `haldorbounties_chain`
- **Mod:** HaldorBounties
- **Type:** Chain (4 steps)

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `npc_interacted` `Haldor` | `message` Center | Daily bounty board intro |
| 2 | `timed` 30 s (`hb_accept_tip`) | `rune` topic "Bounty Hunt" | Accept a bounty; map marker for target |
| 3 | `item_acquired` `Trophy_*` | `rune` topic "Trophy Acquired" | Return to Haldor to confirm kill |
| 4 | `npc_interacted` `Haldor` | `message` TopLeft | Collect reward; miniboss contracts for best payouts |

Step 4 rewards: `buff SE_Rested 600 s`.

---

### Category: Companions (2 guides — full quest chains)

#### `offline_companions_chain`
- **Mod:** Offline Companions
- **Type:** Chain (5 steps)
- **discord_on_complete:** `true`
- **Design note:** Offline Companions has **no recruiter NPC**. Companions are hired
  directly through **Haldor for 2000 coins**. They can travel with the player through
  portals and assist with base chores. Chain starts with `first_login` (raven) to
  introduce the mod, then `npc_interacted: Haldor` for the hiring step.

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `first_login` | `raven` topic "Companions" | Introduce companions; hire through Haldor for 2000 coins |
| 2 | `npc_interacted` `Haldor` | `message` Center | Companion hire at Haldor; combat ally and base helper |
| 3 | `item_acquired` `CookedMeat` | `rune` topic "Companion Care" | Companions need food; assign up to 3 food items |
| 4 | `timed` 120 s (`oc_portal_tip`) | `rune` topic "Companion Portals" | Companions travel through portals automatically |
| 5 | `timed` 300 s (`oc_ai_tip`) | `rune` topic "Companion Behavior" | Follow / Guard / Roam AI modes |

#### `wandering_companions_chain`
- **Mod:** Wandering Companions
- **Type:** Chain (4 steps)
- **Design note:** Wandering Companions has **no tameable creature prefab**. Wanderers
  are hostile humanoids that roam the Black Forest, Mountains, Swamps, Plains, Mistlands,
  and Ashlands. They are optional challenge encounters — not friendly or hireable.
  Chain uses `boss_defeated` as biome-progression proxies (since `biome` trigger is not
  implemented) and `player_death` for a post-death tip.

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `first_login` | `raven` topic "Wandering Companions" | Warning: dangerous wanderers in 6 biomes; seek them for a challenge |
| 2 | `boss_defeated` `Eikthyr` | `rune` topic "Wandering Companions" | Black Forest wanderers on your path; do not underestimate |
| 3 | `boss_defeated` `gd_king` | `rune` topic "Wandering Companions" | Swamp/Mountain wanderers; harder challenge |
| 4 | `player_death` max_fires 2 | `raven` topic "Wandering Companions" | Death reminder; study their pattern or avoid patrol zone |

---

### Category: Skills (2 guides — progression chains)

#### `slayerskills_chain`
- **Mod:** SlayerSkills
- **Type:** Chain (4 steps)
- **discord_on_complete:** `true`

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `item_acquired` `Trophy_*` | `rune` topic "Trophy Hunter" | First trophy; SlayerSkills intro; hover tooltip on step |
| 2 | `first_login` + 5 × `Trophy_*` counter | `rune` topic "Trophy Collector" | 5 trophies collected — first Slayer buff unlocked |
| 3 | `skill_level` Swords 25 | `rune` topic "Slayer Skills" | Specialization amplifies trophy bonuses; uses `{skill}`/`{level}` |
| 4 | `skill_level` Swords 50 | `rune` topic "Slayer's Blade" | Half-mastery; combined trophy+weapon advantage grows |

Step 4 rewards: `skill_exp Swords 500`.

#### `impactfulskills_chain`
- **Mod:** ImpactfulSkills
- **Type:** Chain (4 steps)

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `skill_level` Swords 25 | `rune` topic "Skill Milestone" | First milestone — increased stagger |
| 2 | `skill_level` Swords 50 | `rune` topic "Skill Milestone" | Halfway — stamina cost reduction |
| 3 | `skill_level` Swords 75 | `rune` topic "Skill Milestone" | Elite — burst damage chance |
| 4 | `skill_level` Swords 100 | `rune` topic "True Mastery" | Full mastery — all bonuses stack |

Step 4 rewards: `skill_exp Swords 1000`.

> **CRITICAL NOTE:** Specific per-skill effect descriptions at levels 25/50/75/100 MUST be
> sourced from the ImpactfulSkills Thunderstore page or its in-game config before the step
> text is finalized. Placeholder descriptions above must be replaced with exact values.

---

### Category: Exploration (2 guides — discovery chains)

#### `more_world_locations_chain`
- **Mod:** More World Locations AIO
- **Type:** Chain (3 steps)

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `location_entered` `WL_*` | `message` Center | First new POI; 178 locations across all biomes; hover tooltip |
| 2 | `location_entered` `WL_*` | `message` Center | Location type variety: ports, shrines, dungeons, traders |
| 3 | `timed` 60 s (`mwl_reward_tip`) | `rune` topic "World Locations" | Special loot, unique traders, and puzzle rewards |

#### `zenbossstone_chain`
- **Mod:** ZenBossStone
- **Type:** Chain (6 steps)
- **discord_on_complete:** `true`
- **scope:** `player`
- **Design note:** ZenBossStone has **no buildable piece**. The Boss Stone is pre-placed
  in the Spawn Temple. Each player has their own personal achievement record — other
  players cannot see your achievements. Sacrificing boss trophies at the stone unlocks
  boss items for that player only. Chain starts with `first_login` (raven) explaining
  the Spawn Temple and sacrifice mechanic, then `boss_defeated` (rune) for each boss.

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `first_login` | `raven` topic "Spawn Temple" | Spawn Temple intro; Boss Stone sacrifice mechanic |
| 2 | `boss_defeated` `Eikthyr` | `rune` topic "Eikthyr Slain" | Take trophy to Boss Stone; claim Eikthyr's power |
| 3 | `boss_defeated` `gd_king` | `rune` topic "The Elder Slain" | Elder trophy → Boss Stone; Swamp awaits |
| 4 | `boss_defeated` `Bonemass` | `rune` topic "Bonemass Slain" | Bonemass trophy → Boss Stone; halfway through main bosses |
| 5 | `boss_defeated` `Dragon` | `rune` topic "Moder Slain" | Moder trophy → Boss Stone; Plains next; craft silver |
| 6 | `boss_defeated` `GoblinKing` | `rune` topic "Five Bosses Felled" | Final trophy → Boss Stone; full conquest complete |

Step 6 rewards: `buff SE_Rested 1200 s` + `skill_exp Swords 2000`.

---

### Category: General (1 guide — utility)

#### `recovery_tips`
- **Mod:** n/a (general Hearthbound server tip)
- **Type:** Single entry
- **Trigger:** `player_death` (max_fires: 2 — fires on first 2 deaths only)
- **Display:** `raven` (topic: "Death Recovery") — `player_death` rule
- **Message:** Grave marks dropped gear on the map. Free portal tip (Fine Wood + Greydwarf Eyes). Skill exp recovers with use.

---

## Total Guide Count

| Category | Guides | Steps |
|---|---|---|
| Inventory | 4 | 4 (single-entry) |
| Groups | 1 | 3 |
| Building | 3 | 10 |
| Trading | 3 | 11 |
| Companions | 2 | 9 |
| Skills | 2 | 8 |
| Exploration | 2 | 9 |
| General | 1 | 1 (single-entry) |
| **Total** | **18** | **55** |

---

## YAML Authoring Reminders

### Things that work right now
- `description:` on any step shows a hover tooltip in the HUD tracker. Use YAML block scalar (`|`) for multi-line.
- `{player_name}`, `{skill}`, `{level}`, `{step}`, `{total}` expand in all message text.
- `rewards:` on an entry grants items/skill exp/buffs when the entry fires.
- `npc_conversation` + `display.mode: conversation` opens the Hold-E panel with choice buttons.
- `npc_item_submit` + `trigger.consume: false` lets the player "show" an item without losing it.
- `version: N` on an entry — bump when you change step messages meaningfully.

### Display mode rules (summary)
- `rune` requires `display.topic`. Screen darkens; ghost mode protects the player.
- `raven` requires `display.topic`. Hugin flies in; fires when conditions are safe.
- `message` does not use `display.topic`. Brief overlay toast.
- All rune/raven/message modes expand template variables live.
- **Raven text limitation:** `display.text` is baked at config load. Write messages in
  the top-level `message:` field so they are re-rendered at display time.

### Equip and Build trigger notes
- `trigger.type: equip` — fires on every equip including load/respawn re-equip. Always
  pair with `once: true` or `cooldown` to avoid repeated fires on login.
- `trigger.type: build` — piece prefab names have no separators: `woodwall` not `wood_wall`.
  Verify in-game before shipping.

### Things that do NOT work (no trigger file)
- `trigger.type: biome` — not implemented. Use `first_login` + `boss_defeated` as proxies.
- `trigger.type: distance` — not implemented.
- `trigger.type: discover_location` — not implemented.

### Message text rules
- All message text must be plain text. No markdown, no HTML.
- `rune` messages: up to 4–5 sentences (full-screen panel, player must dismiss).
- `raven` messages: up to 5–6 sentences (raven panel has a scroll).
- `message` messages: 1–2 sentences max.
- `conversation` body: 2–4 sentences (fixed-height panel, no scroll).

---

## Pre-Authoring Verification Checklist

Before finalizing entries that reference mod-specific prefabs, verify these against
the live game or the mod's source/config:

- [ ] ImpactfulSkills per-skill effect descriptions at levels 25 / 50 / 75 / 100.
- [ ] ProtectiveWards ward piece prefab name (`guard_stone` — vanilla, verify mod does not rename).
- [ ] Armoire wardrobe piece prefab name (`ArmoirePiece` — placeholder).
- [ ] balrond_shipyard main piece prefab name (`ShipyardStation` — placeholder).
- [ ] HaldorBounties flow: does it use trophy pickup or automatic kill tracking? Determines step 3 trigger.
- [ ] Boss prefab names (`Eikthyr`, `gd_king`, `Bonemass`, `Dragon`, `GoblinKing`) — confirm against vanilla game data.

**Removed from checklist (mods have no NPC/piece prefabs):**
- ~~ZenBossStone piece prefab~~ — Boss Stone is pre-placed in Spawn Temple; no `build` trigger.
- ~~Offline Companions recruiter NPC~~ — Hired through Haldor; uses `npc_interacted: Haldor`.
- ~~SimpleMarket market NPC~~ — No NPC; accessed via inventory UI button.
- ~~Wandering Companions creature prefab~~ — No tameable creature; uses `boss_defeated` + `player_death`.

---

## Criteria

- [ ] All 18 guides are present in `hearthbound_guides.yaml`.
- [ ] All chain `id` values are unique and use snake_case.
- [ ] `category` values match the standardized list: Companions, Trading, Building, Skills, Exploration, Inventory, Groups.
- [ ] No entry uses `trigger.type: biome`, `distance`, or `discover_location` (not implemented).
- [ ] All `build` trigger entries have `display.mode: rune` and `display.topic` set.
- [ ] All `first_login` trigger entries have `display.mode: raven` and `display.topic` set.
- [ ] All `player_death` trigger entries have `display.mode: raven` and `display.topic` set.
- [ ] All `timed` trigger entries have `display.mode: rune` and `display.topic` set.
- [ ] All `item_acquired` trigger entries have `display.mode: rune` and `display.topic` set.
- [ ] All `skill_level` trigger entries have `display.mode: rune` and `display.topic` set.
- [ ] All `boss_defeated` trigger entries have `display.mode: rune` and `display.topic` set.
- [ ] All `chest_opened` trigger entries have `display.mode: rune` and `display.topic` set.
- [ ] All `equip` trigger entries have `display.mode: message` (not rune/raven).
- [ ] All `npc_interacted` trigger entries have `display.mode: message` (not rune/raven).
- [ ] `equip` trigger entries all have `once: true` or a `cooldown` to suppress re-equip-on-load.
- [ ] `build` trigger entries use verified prefab names (no separators).
- [ ] `discord_on_complete: true` is set only on: `offline_companions_chain`, `slayerskills_chain`, `zenbossstone_chain`.
- [ ] All messages are plain text (no markdown, no unsupported characters).
- [ ] `message` mode messages are 1–2 sentences. `rune`/`raven` messages may be longer.
- [ ] `conversation` display entries have at least one dismiss choice in `conversation.choices`.
- [ ] Entries using `{player_name}` / `{skill}` / `{level}` template variables use correct spelling.
- [ ] `recovery_tips` entry is present with `trigger.type: player_death`, `max_fires: 2`, `display.mode: raven`.
- [ ] `zenbossstone_chain` has `scope: player` set.
- [ ] `version: 1` is set on all entries.
- [ ] ImpactfulSkills step text does not contain `[skill effect]` placeholder — sourced and filled in before shipping.
- [ ] The file hot-reloads correctly: edit YAML while game is running; config reloads within 0.5 s and broadcasts to connected clients.
