# Trigger Types

Each guidance entry has a `trigger` block that defines what event causes it to fire.

```yaml
trigger:
  type: craft
  item: SwordBronze
```

---

## `craft`

Fires when the player crafts a specific item.

```yaml
trigger:
  type: craft
  item: SwordBronze       # prefab name of the crafted item
```

**Tip:** The `item` field uses the internal prefab name, not the display name. For example, `SwordBronze` not `Bronze Sword`. Check the [Valheim Item Database](https://valheim-modding.github.io/Jotunn/data/objects/item-list.html) for prefab names.

---

## `item_acquired`

Fires when the player picks up or receives a specific item.

```yaml
trigger:
  type: item_acquired
  item: TrollHide         # prefab name
```

This fires on any item gain — picking up from the ground, looting a chest, or receiving from a drop.

### Collection goal (`count > 1`)

Add `count` to require the player to accumulate a total quantity before the entry fires:

```yaml
trigger:
  type: item_acquired
  item: Stone
  count: 20               # fires once the player's inventory holds ≥ 20 Stone
```

The HUD tracker shows a progress bar (`Stone: 12 / 20`) while the player collects. Progress is read directly from the player's inventory — **items already in the inventory count immediately** when the entry first becomes eligible (on login or config reload), so the player is never penalised for having gathered materials before the guide entry existed.

The entry fires and the bar completes as soon as the total reaches `count`. Crafted items count toward the goal as well as picked-up items.

**Wildcard support:** `item: Trophy*` matches any item whose prefab name starts with `Trophy`, letting you set collection goals across a whole category.

### Multiple item goals (`goals`)

Use a `goals:` list to require several **different** items at once. Each goal has its own `item` and `count`. The entry fires only when **every** goal is satisfied simultaneously; items may be gathered in any order, and crafted items count.

```yaml
trigger:
  type: item_acquired
  goals:
    - item: FineWood
      count: 30
    - item: BronzeNails
      count: 200
    - item: Resin
      count: 25
```

The HUD tracker row shows `N / M goals` (completed goals out of total); its hover tooltip and the Codex body list each item's `current/goal` breakdown. Once collection begins, the entry stays visible (and pinnable) even if the items are later removed — it is only marked complete when all goals are currently met. When `goals` is present it takes precedence over the single-item `item`/`count` fields.

---

## `kill`

Fires when the player gets credit for killing a specific creature.

```yaml
trigger:
  type: kill
  creature: Troll         # prefab name of the creature
```

Credit is assigned to the player who dealt the killing blow or was the attacker on death.

### Kill count (`count > 1`)

Add `count` to require several kills before the entry fires. Progress is stored per character and shown as a `current/goal` count in the HUD tracker (once the quest is pinned) and the Codex.

```yaml
trigger:
  type: kill
  creature: Neck
  count: 10               # fires after 10 Neck kills; omit (or 1) for any single kill
```

### Shared party progress (`share_progress`)

On a multi-count kill, set `share_progress: true` so each kill also credits nearby players' counters for the same entry — the whole party advances together instead of only the player who landed the blow.

```yaml
trigger:
  type: kill
  creature: Boar
  count: 5
  share_progress: true    # nearby group members get credit too
```

---

## `build`

Fires when the player places a specific building piece.

```yaml
trigger:
  type: build
  piece: Stone_floor      # prefab name of the piece
```

---

## `biome`

Fires when the player enters a specific biome. Checked every 2 seconds.

```yaml
trigger:
  type: biome
  biome: AshLands
```

**Valid biome names:** `Meadows`, `BlackForest`, `Swamp`, `Mountain`, `Plains`, `AshLands`, `DeepNorth`, `Ocean`, `Mistlands`

---

## `distance`

Fires when the player steps within `radius` metres of a named **vanilla** world location. Checked every 5 seconds. Fires at most once per location per character.

```yaml
trigger:
  type: distance
  location: Vendor_BlackForest    # ZoneSystem location prefab name
  radius: 50                      # metres (default 50 when omitted)
```

**Common location names:** `Vendor_BlackForest` (Haldor), `TrollCave02`, `Crypt2` (Burial Chambers), `VikingVillage`

> **Note:** For mod-added locations (e.g. More World Locations AIO), use `location_entered` instead — it detects spawned Location components directly and does not depend on ZoneSystem sync state.

---

## `location_entered`

Fires the first time the player comes within 40 metres of a spawned location instance — vanilla or mod-added. Checked every 5 seconds. Fires at most once per location per character.

```yaml
trigger:
  type: location_entered
  location: "MWL_*"    # trailing * wildcard supported
```

Use a trailing `*` wildcard to match all locations from a mod pack with a shared prefix:

```yaml
trigger:
  type: location_entered
  location: "MWL_*"    # fires for any More World Locations AIO point of interest
```

**How to find the right prefix:** Enable `LogLevel = Debug` in `BepInEx/config/BepInEx.cfg`. When the player approaches a location, a `[location_entered] Scene scan in range: 'PrefabName'` line will appear in the BepInEx log, showing the exact name to use.

**Detection:** Scans `Location.s_allLocations` (all currently spawned Location components in the scene) as the primary source. This is reliable even for zones generated after login, where ZoneSystem's `m_placed` flag may not yet have been updated on the client. A secondary ZoneSystem pass catches any placed locations that lack a `Location` component.

---

## `skill_level`

Fires when the player's skill reaches or crosses a threshold level.

```yaml
trigger:
  type: skill_level
  skill: Swords
  level: 50
```

**Valid skill names:** `Swords`, `Knives`, `Clubs`, `Polearms`, `Spears`, `Blocking`, `Axes`, `Bows`, `Crossbows`, `Unarmed`, `Pickaxes`, `WoodCutting`, `Jump`, `Sneak`, `Run`, `Swim`, `ElementalMagic`, `BloodMagic`

**On-login catch-up:** On player login, the mod scans every configured `skill_level` threshold. Any threshold the player already meets that has not yet fired is raised in ascending level order. This means:
- A player who logs in with Swords at 75 will receive all `skill_level` entries for Swords ≤ 75 that have not already fired.
- For **chains**, all qualifying steps cascade automatically: step 1 fires first (advancing the chain), then step 2, and so on — no manual intervention needed.

---

## `discover_location`

Fires when the player uncovers a named location on the map.

```yaml
trigger:
  type: discover_location
  location: Vendor_BlackForest
```

---

## `damage_type`

Fires when the player takes damage of a specific type.

```yaml
trigger:
  type: damage_type
  damage_type: Fire
```

**Valid damage types:** `Fire`, `Frost`, `Lightning`, `Poison`, `Spirit`, `Blunt`, `Slash`, `Pierce`

---

## `npc_interacted`

Fires when the player presses E on a trader NPC (short press — opens the store).

```yaml
trigger:
  type: npc_interacted
  npc: Haldor             # prefab name, case-insensitive
```

**Supported NPCs:** `Haldor`, `Hildir`, `BogWitch` (any prefab with a `Trader` component)

---

## `npc_conversation`

Fires when the player holds E on a trader NPC for ≥ 0.5 seconds. Opens a dialogue panel instead of the store. Use with `display.mode: conversation`.

```yaml
trigger:
  type: npc_conversation
  npc: Haldor
```

The trader's hover tooltip gains a `[Hold E] Quest` hint when a matching entry is available. See [NPC Conversations](NPC-Conversations) for the full panel setup.

---

## `npc_item_submit`

Fires when the player uses a specific item while interacting with a trader NPC — a classic quest turn-in.

```yaml
trigger:
  type: npc_item_submit
  npc: Haldor
  item: TrollTrophy       # prefab name of the item to submit
  count: 3                # how many are required (default 1)
  consume: true           # remove the items from inventory (default true)
```

When `count > 1`, the entry shows a progress bar in the HUD tracker and Codex until all items are submitted. Items are consumed incrementally — the player can hand them in across multiple interactions.

---

## `boss_defeated`

Fires when a specific boss is killed anywhere in the world.

```yaml
trigger:
  type: boss_defeated
  creature: Eikthyr       # boss prefab name
```

**Boss prefab names:** `Eikthyr`, `gd_king` (The Elder), `Bonemass`, `Dragon` (Moder), `GoblinKing` (Yagluth), `SeekerQueen`, `Fader`

---

## `first_login`

Fires the first time a player ever logs into this world.

```yaml
trigger:
  type: first_login
```

No additional fields required. Combine with `once: true` (the default) to fire exactly once per character per world.

---

## `player_death`

Fires when the local player dies.

```yaml
trigger:
  type: player_death
```

Useful for sympathy messages, tips about the death mechanic, or penalty/recovery guidance.

---

## `chest_opened`

Fires when the player opens a specific type of chest.

```yaml
trigger:
  type: chest_opened
  item: piece_chest_wood    # container prefab name
```

**Common container prefabs:** `piece_chest_wood`, `piece_chest`, `piece_chest_blackmetal`, `TreasureChest_forestcrypt`

---

## Interaction Triggers

These fire when the player interacts with a specific world object. Each takes an optional filter field; omit the filter to match **any** object of that kind.

### `crafting_table_used`

Fires when the player uses a crafting station (Workbench, Forge, etc.).

```yaml
trigger:
  type: crafting_table_used
  station: piece_workbench   # optional prefab filter; omit for any station
```

### `cooking_used`

Fires when the player uses a cooking station or fireplace (Cooking Station, Cauldron, Fireplace).

```yaml
trigger:
  type: cooking_used
  station: piece_cookingstation   # optional prefab filter; omit for any
```

### `portal_used`

Fires when the player uses a teleport portal.

```yaml
trigger:
  type: portal_used
  tag: home                  # optional portal tag filter; omit for any portal
```

### `ward_activated`

Fires when the player interacts with a ward / private-area guard stone.

```yaml
trigger:
  type: ward_activated
```

### `tamed_creature`

Fires when a creature the player owns becomes tamed.

```yaml
trigger:
  type: tamed_creature
  creature: Lox              # optional prefab filter; omit for any creature
```

### `sign_read`

Fires when the player interacts with a sign.

```yaml
trigger:
  type: sign_read
```

### `tombstone_picked`

Fires when the player retrieves their tombstone (death loot).

```yaml
trigger:
  type: tombstone_picked
```

### `ship_sailed`

Fires once the player is sailing a moving ship.

```yaml
trigger:
  type: ship_sailed
```

---

## Time & Day Triggers

A background coroutine polls on a short tick and fires these when the in-game or real-world clock matches. Combine with `once: true` for a one-shot, or leave repeatable with a `cooldown` to fire each matching day.

> **Limitation:** Time triggers work only on **individual (non-chain) entries**, the same as `timed`. Step-level time triggers inside a chain never start a coroutine.

### `time_of_day`

Fires when the in-game time of day reaches a target fraction (`EnvMan.GetDayFraction`).

```yaml
trigger:
  type: time_of_day
  game_time_fraction: 0.0    # 0.0 = midnight, 0.25 = morning, 0.5 = noon
  window: 0.02               # ± tolerance as a fraction of a day (~29 in-game min)
```

### `day_number`

Fires on a specific in-game day (`EnvMan.GetDay`).

```yaml
trigger:
  type: day_number
  day: 7                     # the in-game day counter
```

### `real_world_time`

Fires at a specific real-world UTC time, daily.

```yaml
trigger:
  type: real_world_time
  utc_hour: 20
  utc_minute: 0              # fires at 20:00 UTC
```

### `day_of_week`

Fires on a real-world weekday.

```yaml
trigger:
  type: day_of_week
  day: Saturday              # weekday name
```

---

## `timed`

Fires on a repeating interval while the player is in the world.

```yaml
trigger:
  type: timed
  id: my_tip_id        # required — stable identifier used to dispatch this entry
  interval: "1800"     # seconds as a plain number: 1800 = 30 min, 300 = 5 min, 3600 = 1 h
                       # or the keywords: "hourly" (3600 s), "daily" (86400 s)
```

The `id` field is required — it acts as the dispatch subject that links the timer to this entry. Without it the timer runs but the entry never fires. The interval is always in **seconds** (plain number); shorthand like `"30m"` or `"1h"` is not supported. Useful for periodic tips, reminders, or time-based events. The interval resets each time the entry fires. Combine with `stop_when` to stop after a condition is met.

> **Limitation:** `timed` only works on **individual (non-chain) entries**. Placing it inside a chain step (`steps:` list) silently does nothing — no coroutine is started for step-level triggers. To deliver a sequence of timed tips one after another, convert each step to its own standalone entry and use `requires: [previous_id]` to gate each one on the previous entry completing.

---

## `entry_finished`

Fires when another guidance entry completes (for single entries) or when its final chain step completes (for guide chains).

```yaml
trigger:
  type: entry_finished
  entry: forge_bronze_chain    # the entry ID to watch
```

Use this to chain reward entries, post-quest messages, or unlock follow-up quests automatically.
