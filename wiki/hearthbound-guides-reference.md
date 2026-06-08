# Hearthbound Modpack Guides — Reference

Complete reference for `hearthbound_guides.yaml`: every guide entry, its trigger conditions,
display mode, and how to test it in-game.

File location (server / test profile):
```
BepInEx/config/ValheimServerGuide/hearthbound_guides.yaml
```
The loader merges all `*.yaml` files in that directory, so this file is additive alongside `guidance.yaml`.

---

## Display Mode Rules

This modpack follows a consistent trigger → display mode convention. See
`wiki/Guide-Authoring-Reference.md` for the full reference.

| Trigger type | Display mode |
|---|---|
| `craft`, `item_acquired`, `kill`, `build`, `chest_opened`, `skill_level`, `timed`, `boss_defeated` | `rune` |
| `first_login`, `player_death`, `biome`, `distance` | `raven` |
| `npc_interacted`, `equip`, `location_entered` | `message` |
| `npc_conversation` | `conversation` |

> **Note:** `companions_farming` (`equip Cultivator`) and `companions_fishing` (`equip FishingRod`)
> intentionally use `rune` instead of `message` — these entries are gated by `requires:` and fire
> as part of a deliberate tutorial sequence, not on arbitrary equip.

---

## Prefab Names That Require In-Game Verification

The following identifiers are placeholders. Verify each against the mod's ZNetScene
registration or spawn config before shipping to a live server.

| Placeholder | Expected for | How to verify |
|---|---|---|
| `guard_stone` | ProtectiveWards ward piece | Vanilla ward is `guard_stone`; confirm the mod does not rename it |
| `ArmoirePiece` | Armoire wardrobe piece | Check mod's `ZNetScene` registration via console or source |
| `ShipyardStation` (not yet in YAML) | balrond_shipyard main piece | Check mod's `ZNetScene` registration |

Boss prefab names (`Eikthyr`, `gd_king`, `Bonemass`, `Dragon`, `GoblinKing`) are confirmed standard Valheim values.

---

## Guide Inventory

### Category: Inventory

#### `quickstack_chain` — Quick Stack Store Sort Trash Restock (7 steps)
- **Mod:** Quick Stack / Store / Sort / Trash / Restock
- **Discord on complete:** no

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `build` piece `piece_chest_wood` | rune (topic: "Inventory Management") | Favoriting items with Alt+click; protected from quick stack/sort/trash |
| 2 | `timed` 30 s (`qs_quickstack_tip`) | rune (topic: "Quick Stacking") | Quick Stack sends matching non-favorited items to nearby chests |
| 3 | `timed` 30 s (`qs_restock_tip`) | rune (topic: "Restocking") | Restock tops off inventory from nearby containers |
| 4 | `timed` 30 s (`qs_area_tip`) | rune (topic: "Area Actions") | Area Quick Stack / Restock covers all chests in range at once |
| 5 | `timed` 30 s (`qs_storeall_tip`) | rune (topic: "Store and Take All") | Store All deposits all non-favorited items; Take All pulls everything |
| 6 | `timed` 30 s (`qs_sort_tip`) | rune (topic: "Sorting") | Sort by category, name, weight, or value; can auto-sort on chest open |
| 7 | `timed` 30 s (`qs_trash_tip`) | rune (topic: "Trashing") | Trash slot destroys items; flag-and-Quick-Trash for bulk cleanup |

#### `comfy_quickslots_intro` — ComfyQuickSlots Equipment Slots
- **Mod:** ComfyQuickSlots
- **Requires:** `quickstack_chain`
- **Trigger:** `equip` item `HelmetLeather`
- **Display:** `rune` (topic: "ComfyQuickSlots")
- **Fires:** once per player
- **Message:** 8×5 grid with dedicated armor slots; three quick slots (Z/V/B); separate grave for equipped items on death.

#### `recycle_intro` — Recycle N Reclaim
- **Mod:** Recycle N Reclaim
- **Requires:** `comfy_quickslots_intro`
- **Trigger:** `craft` (any item)
- **Display:** `rune` (topic: "Recycle N Reclaim")
- **Fires:** once per player
- **Message:** Reclaim tab in crafting menu; 50% material recovery; 20-second undo window.

#### `craftyboxes_intro` — AzuCraftyBoxes
- **Mod:** AzuCraftyBoxes
- **Requires:** `recycle_intro`
- **Trigger:** `build` piece `piece_workbench`
- **Display:** `rune` (topic: "AzuCraftyBoxes")
- **Fires:** once per player
- **Reward:** 1× QueenBee
- **Message:** Auto-pulls from containers within 20 blocks; toggle with Alt+O; one item left per chest.

---

### Category: Groups

#### `groups_chain` — Groups Party System
- **Mod:** Groups
- **Trigger:** `timed` 300 s (`groups_intro`)
- **Display:** `raven` (topic: "Party System")
- **Fires:** once per player
- **Message:** /invite to form group; party health bars; /p for group chat; Left Alt ping; friendly fire off.

> **Note:** This is a single entry, not a chain — the `timed` trigger fires 5 minutes after the chain
> becomes eligible (no `requires`, so fires 5 min after character logs in first time).

---

### Category: Building

#### `protectivewards_chain` — ProtectiveWards (5 steps)
- **Mod:** ProtectiveWards

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `item_acquired` `SurtlingCore` | raven (topic: "Protective Ward") | Surtling Core found; wards protect from raids, enemy damage, griefing |
| 2 | `build` piece `guard_stone` | rune (topic: "ProtectiveWards") | Ward active; auto-repair, raid block, rain/water/ship protection; drop Surtling Core to instantly repair structures |
| 3 | `item_acquired` `Coins` (progress 2000, label "Coins") | rune (topic: "Ward Taxi") | 2000 coins collected; drop them on ward for Valkyrie taxi to Haldor |
| 4 | `npc_interacted` `Haldor` | message (Center) | Arrived via taxi; other taxi destinations: Hildir (Fuling totem), Bog Witch (Pukeberries), Stones (boss trophy) |
| 5 | `timed` 150 s (`pwards_features_tip`) | rune (topic: "ProtectiveWards") | Smelters/fermenters run faster; stat bonuses; LeftShift+E for config panel |

Step 5 grants rewards: 2000× Coins + 500 Run skill exp.

#### `armoire_chain` — Armoire Wardrobe
- **Mod:** Armoire
- **Trigger:** `build` piece `ArmoirePiece` *(prefab unverified)*
- **Display:** `rune` (topic: "Armoire Wardrobe")
- **Fires:** once per player
- **Message:** Cosmetic appearance unlock from discovery; save 3 outfit configurations; works with vanilla and modded gear.

> **Note:** This is a single entry, not a multi-step chain.

#### `shipyard_gather_chain` — Balrond Shipyard — Gather Materials (5 steps)
- **Mod:** balrond_shipyard

| Step | Trigger | Display | Progress goal | Message summary |
|---|---|---|---|---|
| 1 | `item_acquired` `FineWood` (progress 30) | rune (topic: "Balrond Shipyard") | 30 Fine Wood | Materials needed; 7 ship types; collect Bronze Nails next |
| 2 | `craft` `BronzeNails` (progress 200) | rune (topic: "Balrond Shipyard") | 200 Bronze Nails | Nails done; still need Coal, Deer Hide, Resin |
| 3 | `item_acquired` `Coal` (progress 25) | rune (topic: "Balrond Shipyard") | 25 Coal | Coal done; hunt deer for hides |
| 4 | `item_acquired` `DeerHide` (progress 10) | rune (topic: "Balrond Shipyard") | 10 Deer Hide | Hides done; collect Resin from Black Forest |
| 5 | `item_acquired` `Resin` (progress 25) | rune (topic: "Balrond Shipyard") | 25 Resin | All materials gathered; build the Shipyard Station |

> **Note:** Step 2 uses `craft BronzeNails` (not `item_acquired`) because BronzeNails are produced at a forge, not picked up from the world.

#### `shipyard_scribetable` — Balrond Shipyard — Scribe's Table
- **Mod:** balrond_shipyard
- **Requires:** `shipyard_gather_chain`
- **Trigger:** `build` piece `piece_scribetable`
- **Display:** `rune` (topic: "Ship Upgrades")
- **Fires:** once per player
- **Message:** Craft ship schematics here; bring to Shipyard to apply; build a Karve next.

#### `shipyard_karve` — Balrond Shipyard — Karve
- **Mod:** balrond_shipyard
- **Requires:** `shipyard_scribetable`
- **Trigger:** `build` piece `Karve`
- **Display:** `rune` (topic: "Ready to Upgrade")
- **Fires:** once per player
- **Message:** 7 upgrade slots on a Karve; craft Plan Karve: Oars schematic at the Scribe's Table.

#### `shipyard_schematic` — Balrond Shipyard — First Upgrade
- **Mod:** balrond_shipyard
- **Requires:** `shipyard_karve`
- **Trigger:** `item_acquired` `SchematicKarveOars`
- **Display:** `rune` (topic: "Balrond Shipyard")
- **Fires:** once per player
- **Reward:** Swim skill set to level 10
- **Message:** Dock Karve at Shipyard; place schematic in container; pull lever; Fishnet Trap, cargo holds, and cosmetics overview.

---

### Category: Trading

#### `simplemarket_chain` — SimpleMarket Player Market
- **Mod:** SimpleMarket
- **Requires:** *(none)*
- **Trigger:** `item_acquired` `Coins`
- **Display:** `rune` (topic: "SimpleMarket")
- **Fires:** once per player
- **Message:** Market button in tab menu requires Resting effect; async buy/sell; bulk discounts; durability and crafter name preserved.

> **Note:** SimpleMarket has no spawned NPC. The market is accessed via a button in the tab menu
> while the player has the Resting status effect (near beds, fires, or furniture).

#### `traderoverhaul_chain` — TraderOverhaul Unified Traders
- **Mod:** TraderOverhaul
- **Requires:** `simplemarket_chain`
- **Trigger:** `biome` `BlackForest`
- **Display:** `raven` (topic: "TraderOverhaul")
- **Fires:** once per player
- **Message:** Black Forest entered; find Haldor; 590+ items unified across Haldor/Hildir/Bog Witch; catalog expands with boss kills; 20-item transactions; shared coin bank.

#### `haldorbounties_chain` — HaldorBounties Daily Contracts
- **Mod:** HaldorBounties
- **Requires:** `traderoverhaul_chain`
- **Trigger:** `timed` 300 s (`haldorbounties_intro`)
- **Display:** `rune` (topic: "HaldorBounties")
- **Fires:** once per player
- **Message:** Daily bounty board; kill contracts, miniboss hunts (map marker), raid contracts; four reward tiers; boss-gated difficulty; 1.5× payout for miniboss, 1.25× for raids.

---

### Category: Companions

#### `companions_intro` — Offline Companions Hire and Manage
- **Mod:** Offline Companions
- **Requires:** `haldorbounties_chain`
- **Trigger:** `distance` location `Vendor_BlackForest` radius `50`
- **Display:** `raven` (topic: "Companions")
- **Fires:** once per player
- **Message:** Hire companion from Haldor for 2000 coins; starter companion spawns automatically; E for inventory/rename/food; Hold E for radial command wheel.

#### `companions_combat_tip` — Offline Companions Combat
- **Mod:** Offline Companions
- **Requires:** `companions_intro`
- **Trigger:** `player_death` max_fires 2
- **Display:** `raven` (topic: "Companions")
- **Message:** Companions block, parry, counter-attack, and retreat at 30% HP; hire from Haldor for backup.

#### `companions_woodcutting` — Offline Companions Woodcutting
- **Requires:** `companions_intro`
- **Trigger:** `item_acquired` `Axe*`
- **Display:** `rune` (topic: "Companion: Woodcutting")
- **Fires:** once per player
- **Message:** Set companion to gather wood; auto-chops trees and deposits into chests; stops at 298/300 carry weight.

#### `companions_mining` — Offline Companions Mining
- **Requires:** `companions_intro`
- **Trigger:** `item_acquired` `Pickaxe*`
- **Display:** `rune` (topic: "Companion: Mining")
- **Fires:** once per player
- **Message:** Assign to stone/ore gathering; uses tools from inventory; deposits at 298/300 carry weight.

#### `companions_foraging` — Offline Companions Foraging
- **Requires:** `companions_intro`
- **Trigger:** `item_acquired` `Raspberry`
- **Display:** `rune` (topic: "Companion: Foraging")
- **Fires:** once per player
- **Message:** Send on foraging task; collects berries, mushrooms, flowers, branches; no tools required.

#### `companions_smelting` — Offline Companions Smelting
- **Requires:** `companions_intro`
- **Trigger:** `build` piece `charcoal_kiln`
- **Display:** `rune` (topic: "Companion: Smelting")
- **Fires:** once per player
- **Message:** Manages kilns and furnaces within 25 m; collects bars; deposits into chests; smart fuel/ore prioritization.

#### `companions_farming` — Offline Companions Farming
- **Requires:** `companions_intro`
- **Trigger:** `equip` item `Cultivator`
- **Display:** `rune` (topic: "Companion: Farming")
- **Fires:** once per player
- **Message:** Harvests and replants in grid layout; Farm Zones designate areas and crops; persists across sessions.

#### `companions_fishing` — Offline Companions Fishing
- **Requires:** `companions_intro`
- **Trigger:** `equip` item `FishingRod`
- **Display:** `rune` (topic: "Companion: Fishing")
- **Fires:** once per player
- **Message:** 15-20 s cast, 85% hook rate; stock bait by species; vanilla probability tables.

#### `companions_hunting` — Offline Companions Hunting
- **Requires:** `companions_intro`
- **Trigger:** `item_acquired` `Bow*`
- **Display:** `rune` (topic: "Companion: Hunting")
- **Fires:** once per player
- **Message:** Hunts passive wildlife with bow; standoff distance to avoid spooking prey; needs bow and arrows in inventory.

#### `companions_cooking` — Offline Companions Cooking
- **Requires:** `companions_intro`
- **Trigger:** `build` piece `piece_cookingstation`
- **Display:** `rune` (topic: "Companion: Cooking")
- **Fires:** once per player
- **Message:** Crafts meals at cauldrons from nearby chests; brews mead bases; fills fermenters; taps finished meads.

#### `companions_base` — Offline Companions Base Maintenance
- **Requires:** `companions_intro`
- **Trigger:** `build` piece `bed`
- **Display:** `rune` (topic: "Companion: Stay Home")
- **Fires:** once per player
- **Message:** Stay Home mode: repairs structures, refuels fires, consolidates chests every 60 s within 40 m; Wander = 50 m patrol.

#### `companions_portal` — Offline Companions Portal Travel
- **Requires:** `companions_intro`
- **Trigger:** `build` piece `portal_wood`
- **Display:** `rune` (topic: "Companion: Portals")
- **Fires:** once per player
- **Message:** Companions teleport with player through portals and dungeon entrances; exception: Stay Home mode stays put.

#### `wandering_companions_chain` — Wandering Companions Dangerous Encounters (4 steps)
- **Mod:** Wandering Companions
- **Requires:** `companions_intro`
- **Note:** Wanderers are hostile humanoids in 6 biomes — not tameable, not hireable.

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `first_login` | raven (topic: "Wandering Companions") | Warning: dangerous wanderers in Black Forest, Mountains, Swamps, Plains, Mistlands, Ashlands |
| 2 | `boss_defeated` `Eikthyr` | rune (topic: "Wandering Companions") | Black Forest wanderers are formidable; do not underestimate on first encounter |
| 3 | `boss_defeated` `gd_king` | rune (topic: "Wandering Companions") | Wanderers in Swamp and Mountains; harder challenge |
| 4 | `player_death` max_fires 2 | raven (topic: "Wandering Companions") | Study their pattern or avoid patrol zones |

---

### Category: Skills

#### `slayerskills_chain` — SlayerSkills Trophy Hunter (4 steps)
- **Mod:** SlayerSkills
- **Discord on complete:** yes

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `item_acquired` `Trophy*` | rune (topic: "SlayerSkills") | First trophy; creature-specific slayer XP and damage bonus intro; starred creatures give more points |
| 2 | `item_acquired` `Trophy*` | rune (topic: "Trophy Collector") | Slayer bonuses building; multiplayer kill credit modes; tamed animal kills count |
| 3 | `boss_defeated` `Eikthyr` | rune (topic: "Boss Slayer") | Boss kills grant special slayer multipliers; hunting bosses repeatedly accelerates progression |
| 4 | `item_acquired` `Trophy*` | rune (topic: "True Slayer") | Reaching the max cap earns Slayer status: skill points cannot be lost on death; all bonuses unlocked |

Step 4 grants rewards: SE_Rested (1200 s) + 1000 Swords skill exp.

> **Design note:** Steps 2 and 4 use plain `item_acquired Trophy*` (no progress counter). They fire on
> the next trophy pickup after the previous step completes. The message text references "Five trophies"
> and "Twenty trophies" as flavor — no actual counting occurs at those steps.

#### `impactfulskills_run` — ImpactfulSkills Movement Skills
- **Mod:** ImpactfulSkills
- **Trigger:** `skill_level` Run 25
- **Display:** `rune` (topic: "ImpactfulSkills")
- **Fires:** once per player
- **Message:** Run increases movement speed; Jump improves height/distance and reduces fall damage; Sneak reduces noise at level 50.

#### `impactfulskills_woodcutting` — ImpactfulSkills Gathering Skills
- **Mod:** ImpactfulSkills
- **Trigger:** `skill_level` WoodCutting 25
- **Display:** `rune` (topic: "Gathering Skills")
- **Fires:** once per player
- **Message:** Woodcutting boosts yield and chop damage; Pickaxes add AOE mining at level 50; Farming boosts harvest yield and AOE scythe at level 50.

#### `impactfulskills_chain` — ImpactfulSkills Sword Milestones (2 steps)
- **Mod:** ImpactfulSkills

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `skill_level` Swords 25 | rune (topic: "Combat Skills") | Stamina cost reduction up to 50% at level 100; bonus shown on item tooltip; Blood Magic XP from blocked damage |
| 2 | `skill_level` Swords 50 | rune (topic: "New Skills") | Three new skills: Voyager (sailing), Hauling (carry weight), Animal Whisper (taming); Cooking extends food duration; Knowledge Sharing cross-skill learning |

Step 2 grants rewards: SE_Rested (600 s) + 500 Run skill exp.

---

### Category: Exploration

#### `more_world_locations_chain` — More World Locations AIO (3 steps)
- **Mod:** More World Locations AIO

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `location_entered` location `MWL_*` | raven (topic: "World Locations") | First MWL POI found; 178 locations across all biomes |
| 2 | `location_entered` location `MWL_*` | raven (topic: "World Locations") | Location variety: ports, shrines, dungeons, puzzle sites, special traders |
| 3 | `timed` 60 s (`mwl_reward_tip`) | raven (topic: "World Locations") | Special traders, puzzle rewards, and rare loot hint |

#### `zenbossstone_chain` — ZenBossStone Boss Progression (6 steps)
- **Mod:** ZenBossStone
- **Scope:** `player`
- **Discord on complete:** yes
- **Note:** Boss Stone is pre-placed in the Spawn Temple. Achievements are personal and per-character.

| Step | Trigger | Display | Message summary |
|---|---|---|---|
| 1 | `first_login` | raven (topic: "Spawn Temple") | Introduces Boss Stone; personal achievements; sacrifice trophies for boss items |
| 2 | `boss_defeated` `Eikthyr` | rune (topic: "Eikthyr Slain") | Take trophy to Boss Stone; claim Eikthyr's power |
| 3 | `boss_defeated` `gd_king` | rune (topic: "The Elder Slain") | Elder trophy → Boss Stone; Swamp awaits |
| 4 | `boss_defeated` `Bonemass` | rune (topic: "Bonemass Slain") | Bonemass trophy → Boss Stone; halfway through main bosses |
| 5 | `boss_defeated` `Dragon` | rune (topic: "Moder Slain") | Moder trophy → Boss Stone; Plains next; craft silver first |
| 6 | `boss_defeated` `GoblinKing` | rune (topic: "Five Bosses Felled") | Final trophy → Boss Stone; full conquest complete |

Step 6 grants rewards: SE_Rested (1200 s) + 2000 Swords skill exp.

---

### Category: General

#### `recovery_tips` — Death Recovery
- **Mod:** n/a (general server tip)
- **Trigger:** `player_death` max_fires 3, `once: false`
- **Display:** `raven` (topic: "Death Recovery")
- **Message:** Grave marks gear on the map; skill exp recovers with use. Fires up to 3 times.

---

## Total Guide Count

| Category | Entries | Steps |
|---|---|---|
| Inventory | 4 | 10 (chain 7 + 3 singles) |
| Groups | 1 | 1 |
| Building | 7 | 14 (protectivewards 5, shipyard_gather 5, 5 singles) |
| Trading | 3 | 3 |
| Companions | 12 | 15 (wandering 4, 11 singles) |
| Skills | 4 | 6 (slayer 4, impactful 2, 2 singles) |
| Exploration | 2 | 9 (mwl 3, zenbossstone 6) |
| General | 1 | 1 |
| **Total** | **34** | **59** |

---

## Test Instructions

All tests require a character on the r2modman test profile with the Hearthbound modpack active.
The `hearthbound_guides.yaml` file hot-reloads on save — no game restart needed after edits.

| # | Action | Expected result | Entry |
|---|---|---|---|
| 1 | Place a wood chest (`piece_chest_wood`) | Rune: "Inventory Management" — Favoriting intro | `quickstack_chain` step 1 |
| 2–7 | Wait 30 s between steps | Six rune overlays: Quick Stack → Restock → Area → Store/Take → Sort → Trash | `quickstack_chain` steps 2–7 |
| 8 | Equip Leather Hood (`HelmetLeather`) | Rune: "ComfyQuickSlots" — equipment slots and quick slots | `comfy_quickslots_intro` |
| 9 | Craft any item | Rune: "Recycle N Reclaim" — Reclaim tab intro | `recycle_intro` |
| 10 | Place a Workbench | Rune: "AzuCraftyBoxes" — auto-pull from containers; QueenBee reward | `craftyboxes_intro` |
| 11 | Wait ~5 min on first login | Raven: "Party System" — /invite, health bars, group chat | `groups_chain` |
| 12 | Pick up a Surtling Core | Raven: "Protective Ward" — ward protection intro | `protectivewards_chain` step 1 |
| 13 | Place a ward (`guard_stone`) | Rune: "ProtectiveWards" — full feature list; auto-repair, raid block | `protectivewards_chain` step 2 |
| 14 | Accumulate 2000 Coins (progress bar visible) | Rune: "Ward Taxi" — 2000 coins = Valkyrie taxi | `protectivewards_chain` step 3 |
| 15 | Short-press E on Haldor | Center message: arrived via taxi; other destination offerings | `protectivewards_chain` step 4 |
| 16 | Wait 150 s after step 15 | Rune: "ProtectiveWards" — stat bonuses, config panel; 2000 Coins + 500 Run exp | `protectivewards_chain` step 5 |
| 17 | Build an Armoire piece (`ArmoirePiece`) | Rune: "Armoire Wardrobe" — cosmetic overview | `armoire_chain` |
| 18 | Pick up 30 Fine Wood (progress bar) | Rune: "Balrond Shipyard" — materials list | `shipyard_gather_chain` step 1 |
| 19 | Craft 200 Bronze Nails at forge (progress bar) | Rune: "Balrond Shipyard" — nails done | `shipyard_gather_chain` step 2 |
| 20 | Pick up 25 Coal (progress bar) | Rune: "Balrond Shipyard" — coal done; hunt deer | `shipyard_gather_chain` step 3 |
| 21 | Pick up 10 Deer Hide (progress bar) | Rune: "Balrond Shipyard" — hides done; collect Resin | `shipyard_gather_chain` step 4 |
| 22 | Pick up 25 Resin (progress bar) | Rune: "Balrond Shipyard" — all materials gathered | `shipyard_gather_chain` step 5 |
| 23 | Build a Scribe's Table (`piece_scribetable`) | Rune: "Ship Upgrades" — schematics and Karve guidance | `shipyard_scribetable` |
| 24 | Build a Karve | Rune: "Ready to Upgrade" — 7 slots; craft oar schematic next | `shipyard_karve` |
| 25 | Pick up `SchematicKarveOars` | Rune: "Balrond Shipyard" — install schematic guide; Swim skill set to 10 | `shipyard_schematic` |
| 26 | Pick up Coins (any amount) | Rune: "SimpleMarket" — market button, Resting requirement, async trades | `simplemarket_chain` |
| 27 | Enter the Black Forest biome | Raven: "TraderOverhaul" — find Haldor; unified 590+ item catalog | `traderoverhaul_chain` |
| 28 | Wait ~5 min after step 27 | Rune: "HaldorBounties" — daily bounty board intro | `haldorbounties_chain` |
| 29 | Approach Haldor's camp within 50 m | Raven: "Companions" — hire for 2000 coins; radial command wheel | `companions_intro` |
| 30 | Die (first death) | Raven: "Companions" — combat companion backup suggestion (fire 1 of 2) | `companions_combat_tip` |
| 31 | Pick up any axe (`Axe*`) | Rune: "Companion: Woodcutting" | `companions_woodcutting` |
| 32 | Pick up any pickaxe (`Pickaxe*`) | Rune: "Companion: Mining" | `companions_mining` |
| 33 | Pick up a Raspberry | Rune: "Companion: Foraging" | `companions_foraging` |
| 34 | Build a Charcoal Kiln | Rune: "Companion: Smelting" | `companions_smelting` |
| 35 | Equip a Cultivator | Rune: "Companion: Farming" | `companions_farming` |
| 36 | Equip a Fishing Rod | Rune: "Companion: Fishing" | `companions_fishing` |
| 37 | Pick up any bow (`Bow*`) | Rune: "Companion: Hunting" | `companions_hunting` |
| 38 | Build a Cooking Station | Rune: "Companion: Cooking" | `companions_cooking` |
| 39 | Build a Bed | Rune: "Companion: Stay Home" — base maintenance modes | `companions_base` |
| 40 | Build a Portal | Rune: "Companion: Portals" — follow through portals | `companions_portal` |
| 41 | First login (fresh character) | Raven: "Wandering Companions" — biome warning | `wandering_companions_chain` step 1 |
| 42 | Kill Eikthyr | Two rune overlays: "Wandering Companions" (Black Forest warning) + "Eikthyr Slain" (Boss Stone) | `wandering_companions_chain` step 2 + `zenbossstone_chain` step 2 |
| 43 | Pick up first trophy (`Trophy*`) | Rune: "SlayerSkills" — trophy hunter intro | `slayerskills_chain` step 1 |
| 44 | Pick up another trophy | Rune: "Trophy Collector" — slayer bonuses building | `slayerskills_chain` step 2 |
| 45 | Kill The Elder (`gd_king`) | Rune: "Wandering Companions" escalation + Rune: "The Elder Slain" (Boss Stone) | `wandering_companions_chain` step 3 + `zenbossstone_chain` step 3 |
| 46 | Pick up any trophy after Eikthyr kill | Rune: "True Slayer" — max cap info; SE_Rested (1200 s) + 1000 Swords exp | `slayerskills_chain` step 4 |
| 47 | Reach Run skill level 25 | Rune: "ImpactfulSkills" — movement skill perks | `impactfulskills_run` |
| 48 | Reach WoodCutting skill level 25 | Rune: "Gathering Skills" — yield and AOE perks | `impactfulskills_woodcutting` |
| 49 | Reach Swords skill level 25 | Rune: "Combat Skills" — stamina reduction; Blood Magic XP | `impactfulskills_chain` step 1 |
| 50 | Reach Swords skill level 50 | Rune: "New Skills" — Voyager, Hauling, Animal Whisper; SE_Rested (600 s) + 500 Run exp | `impactfulskills_chain` step 2 |
| 51 | Enter any `MWL_*` location | Raven: "World Locations" — 178 POI intro | `more_world_locations_chain` step 1 |
| 52 | Enter a second `MWL_*` location | Raven: "World Locations" — location type variety | `more_world_locations_chain` step 2 |
| 53 | Wait 60 s after step 52 | Raven: "World Locations" — special traders and puzzle rewards | `more_world_locations_chain` step 3 |
| 54 | First login (fresh character) | Raven: "Spawn Temple" — Boss Stone intro | `zenbossstone_chain` step 1 |
| 55 | Kill Bonemass | Rune: "Bonemass Slain" | `zenbossstone_chain` step 4 |
| 56 | Kill Moder (`Dragon`) | Rune: "Moder Slain" | `zenbossstone_chain` step 5 |
| 57 | Kill Yagluth (`GoblinKing`) | Rune: "Five Bosses Felled"; SE_Rested (1200 s) + 2000 Swords exp | `zenbossstone_chain` step 6 |
| 58 | Die (up to 3 times) | Raven: "Death Recovery" — grave marker and skill recovery | `recovery_tips` (max 3 fires) |
| 59 | Die a fourth time | No message — `max_fires: 3` cap reached | `recovery_tips` cap verified |

---

## Known Design Notes

- **`first_login` overlap:** `wandering_companions_chain` and `zenbossstone_chain` both have a
  `first_login` step 1. Both fire on first login. Raven queues them; both appear in sequence.
- **`boss_defeated` overlap:** `zenbossstone_chain` and `wandering_companions_chain` share
  `Eikthyr` and `gd_king` boss steps. Both advance independently — two rune overlays appear
  in sequence.
- **`slayerskills_chain` steps 2 and 4:** These use plain `item_acquired Trophy*` with no
  progress counter. The message text references "Five trophies" and "Twenty trophies" as
  flavor only — these steps fire on the next single trophy pickup after the previous step.
- **Companion trigger deviations:** `companions_farming` (`equip Cultivator`) and
  `companions_fishing` (`equip FishingRod`) use `rune` instead of the standard `message` for
  equip triggers. This is intentional — both are gated behind `companions_intro` and serve as
  tutorial steps, not ambient tips.
- **`wandering_companions_chain` `player_death` step:** `max_fires: 2` caps fires. After 2 deaths
  the step is exhausted regardless of how many times the player dies afterward.
- **`recovery_tips` fires:** `max_fires: 3` with `once: false` — fires on deaths 1, 2, and 3.
  Fourth death and beyond show nothing.
- **`traderoverhaul_chain` biome trigger:** Fires once when the player first enters `BlackForest`.
  The raven message points them toward Haldor's location but does not open the store.
- **`companions_intro` distance trigger:** Fires once when the player comes within 50 m of the
  `Vendor_BlackForest` world location (Haldor's camp). Fires at most once per character
  (SeenTracker guard). The `haldorbounties_chain` must have fired first due to `requires:`.
- **ZenBossStone scope:** `scope: player` ensures each character's Boss Stone progression is
  tracked independently. `discord_on_complete: true` posts when the chain finishes per player.
- **`shipyard_gather_chain` step 2:** Uses `craft BronzeNails` (not `item_acquired`) because
  Bronze Nails are produced at the forge and never exist as world-drop pickups.
