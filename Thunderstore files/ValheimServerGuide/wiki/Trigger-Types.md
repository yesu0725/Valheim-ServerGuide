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

---

## `kill`

Fires when the player gets credit for killing a specific creature.

```yaml
trigger:
  type: kill
  creature: Troll         # prefab name of the creature
```

Credit is assigned to the player who dealt the killing blow or was the attacker on death.

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

Fires when the player steps within `radius` metres of a named world location. Checked every 5 seconds. Fires at most once per location per character.

```yaml
trigger:
  type: distance
  location: Vendor_BlackForest    # ZoneSystem location prefab name
  radius: 50                      # metres (default 50 when omitted)
```

**Common location names:** `Vendor_BlackForest` (Haldor), `TrollCave02`, `Crypt2` (Burial Chambers), `VikingVillage`

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
