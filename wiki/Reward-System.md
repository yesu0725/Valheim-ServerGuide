# Reward System

Guidance entries and NPC conversation choices can grant rewards when they fire. Rewards are declared in a `rewards` list and processed in order.

After all rewards are granted, a summary notification appears in the center of the screen:
> "Received: Bronze Sword (Q2), +500 Swords XP, Rested buff (10 min)"

---

## Adding Rewards to an Entry

```yaml
- id: forge_bronze
  title: "The Bronze Age"
  steps:
    - trigger: { type: craft, item: Bronze }
      message: "Smelt bronze."
    - trigger: { type: craft, item: SwordBronze }
      message: "Forge a bronze sword."
  rewards:
    - type: item
      item: SwordBronze
      amount: 1
      quality: 2
    - type: skill_exp
      skill: Swords
      amount: 500
```

For a chain entry, rewards fire when the **final step** completes.
For a single entry (no `steps`), rewards fire when the entry fires.

---

## Reward Types

### `item`

Adds items directly to the player's inventory.

```yaml
rewards:
  - type: item
    item: SwordBronze       # internal prefab name
    amount: 1               # stack count (default 1)
    quality: 2              # upgrade level (default 1, clamped to item max)
```

If the inventory is full, the item is dropped on the ground in front of the player instead of being silently lost.

**Finding prefab names:** Use the [Valheim Item Database](https://valheim-modding.github.io/Jotunn/data/objects/item-list.html) to look up internal prefab names. Examples: `SwordBronze`, `Wood`, `Coins`, `TrollHide`, `ArrowIron`.

---

### `skill_exp`

Adds raw experience points to a skill.

```yaml
rewards:
  - type: skill_exp
    skill: Swords           # skill name
    amount: 500             # experience points to add
```

Experience is added directly, bypassing the normal XP-per-action gain. The skill levels up naturally as the accumulated experience crosses each level threshold.

**Valid skill names:** `Swords`, `Knives`, `Clubs`, `Polearms`, `Spears`, `Blocking`, `Axes`, `Bows`, `Crossbows`, `Unarmed`, `Pickaxes`, `WoodCutting`, `Jump`, `Sneak`, `Run`, `Swim`, `ElementalMagic`, `BloodMagic`

---

### `skill_level`

Sets a skill to a target level. Only raises — never lowers a skill that is already higher.

```yaml
rewards:
  - type: skill_level
    skill: Run
    level: 30               # target level (1–100)
```

If the player's Run skill is already 35, this reward has no effect.

---

### `buff`

Applies a status effect to the player.

```yaml
rewards:
  - type: buff
    effect: SE_Rested           # status effect name
    duration_override: 600      # seconds (optional; omit for default duration)
```

`duration_override` overrides how long the buff lasts. If omitted, the effect runs for its normal duration.

**Common status effect names:**

| Name | Effect |
|---|---|
| `SE_Rested` | Rested buff (HP/stamina regen bonus) |
| `SE_Wet` | Wet debuff |
| `SE_Burning` | Burning damage over time |
| `SE_Frozen` | Frost debuff |
| `Potion_stamina_medium` | Stamina mead effect |
| `Potion_health_medium` | Health mead effect |

> **Note:** Status effects use their ScriptableObject asset name, not the display name. If an effect name is unrecognised, the reward is skipped and a warning is logged. The rest of the rewards list still executes.

---

## Enhanced Reward Types

Beyond items, skills, and buffs, an entry can grant world- and player-level effects. Several of these (`location_pin`, `set_global_key`, `teleport`, `weather`, …) are resolved **server-side** for authority.

### `map_pin`

Adds a named map pin at explicit world coordinates.

```yaml
rewards:
  - type: map_pin
    name: "Haldor's Camp"
    x: 512
    z: -200
    icon: trader              # optional pin icon
```

### `location_pin`

Finds the nearest instance of a location prefab (vanilla or mod-added) and pins it.

```yaml
rewards:
  - type: location_pin
    location: StartTemple     # location prefab name
    pin_name: "The Start"
    icon: Boss                # optional pin icon
```

### `unlock_recipe`

Teaches the player a recipe by prefab name.

```yaml
rewards:
  - type: unlock_recipe
    recipe: Recipe_FishingRod
```

### `spawn_creature`

Spawns a creature near the player, optionally tamed.

```yaml
rewards:
  - type: spawn_creature
    prefab: Neck
    tamed: true               # default false
    count: 2                  # default 1
```

### `set_global_key` / `remove_global_key`

Set or remove a Valheim **world** global key (visible to all players, persists in the world save).

```yaml
rewards:
  - type: set_global_key
    key: defeated_eikthyr
  - type: remove_global_key
    key: defeated_eikthyr
```

### `set_player_key` / `remove_player_key`

Set or remove a **personal** key on the player (`Player.AddUniqueKey`), so other mods that check `Player.HaveUniqueKey()` see it.

```yaml
rewards:
  - type: set_player_key
    key: mymod_quest_complete
  - type: remove_player_key
    key: mymod_quest_complete
```

### `weather`

Forces an environment preset for a number of seconds.

```yaml
rewards:
  - type: weather
    preset: Rain              # environment name, e.g. Rain, ThunderStorm, Clear
    duration: 30              # seconds (default 60)
```

### `chat_message`

Posts a chat message (attributed to the entry's NPC). Supports `{player_name}`.

```yaml
rewards:
  - type: chat_message
    message: "{player_name}, the sky darkens overhead."
```

### `teleport`

Warps the player to coordinates. Server-authoritative — the destination must be on the server's allowlist when `allowlist_only` is set.

```yaml
rewards:
  - type: teleport
    x: 0
    z: 0
    allowlist_only: true
```

### `rename_player`

Assigns a custom title/suffix shown after the player's name.

```yaml
rewards:
  - type: rename_player
    suffix: "the Undaunted"
```

### `discord`

Posts a per-reward Discord webhook message, separate from any entry-level `announce.discord`. Supports `{player_name}`. The webhook URL stays server-side. See [Discord Integration](Discord-Integration).

```yaml
rewards:
  - type: discord
    message: "{player_name} completed the trial."
```

---

## Rewards on Conversation Choices

Conversation choices can also grant rewards when selected:

```yaml
conversation:
  choices:
    - text: "I'll take the sword."
      rewards:
        - type: item
          item: SwordBronze
          amount: 1
          quality: 2
    - text: "Train me instead."
      rewards:
        - type: skill_exp
          skill: Swords
          amount: 1000
    - text: "Not now."
```

The player picks one choice — only that choice's rewards are granted.

---

## Multiple Rewards

Any number of rewards can be listed. They are all granted in order:

```yaml
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
    level: 20
  - type: buff
    effect: SE_Rested
    duration_override: 300
```

If one reward fails (e.g. unknown item prefab), the error is logged and the remaining rewards still execute.

---

## Reward Notification

After rewards are granted, a centered toast message summarises what the player received:

> "Received: Bronze Sword (Q2), +500 Swords XP, Run → Lv 20, Rested buff (5 min)"

This notification is suppressed if the rewards list is empty.
