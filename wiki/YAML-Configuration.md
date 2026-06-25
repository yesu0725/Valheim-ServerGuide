# YAML Configuration

The entire guidance system is controlled by a single file on the server:

```
BepInEx/config/ValheimServerGuide/guidance.yaml
```

Edit and save — all connected clients receive the updated config within seconds, no restart needed.

---

## File Structure

```yaml
tracker:          # optional — HUD tracker layout overrides
  ...

guidances:        # required — the list of guidance entries
  - id: ...
    ...
  - id: ...
    ...
```

---

## `tracker` Section (optional)

The `tracker` section overrides the BepInEx config for the HUD objective tracker. Values here win over `com.valheimserverguide.cfg` and apply live on YAML reload.

```yaml
tracker:
  enabled: true
  anchor: TopRight              # TopRight | TopLeft | BottomRight | BottomLeft
  hotkey: F10                   # UnityEngine.KeyCode name
  badge_enabled: true           # show badge when tracker panel is hidden
  offset_x: 46                  # pixels from the anchored corner (horizontal)
  offset_y: 320                 # pixels from the anchored corner (vertical)
  width: 210                    # panel width in pixels
  font_size: 15                 # base row font size
  auto_hide_delay: 5            # DEPRECATED/IGNORED (v0.6.0+) — panel no longer auto-hides
  fade_duration: 1              # DEPRECATED/IGNORED (v0.6.0+)
  highlight_duration: 3         # seconds an updated row stays gold
  completion_vfx_enabled: true  # spawn level-up VFX on the player when a chain completes
```

> The progress panel is **hidden by default** and only shows quests the player pins from the Codex (F3 → *Show on Tracker*). Once a player drags the panel (while the inventory or ESC menu is open), their saved position overrides `anchor`/`offset_x`/`offset_y`. See [HUD Tracker & Codex](HUD-Tracker-and-Codex) for the full behaviour.

---

## Guidance Entry Fields

Every entry in `guidances` shares these common fields:

| Field | Type | Default | Description |
|---|---|---|---|
| `id` | string | **required** | Unique key. Used for fire tracking, `requires`/`stop_when`, and `vsg_reset`. |
| `title` | string | — | Human-readable title shown in the Codex and HUD tracker. |
| `category` | string | — | Group label used to organise entries in the Codex left panel. |
| `trigger` | TriggerSpec | — | What causes this entry to fire. See [Trigger Types](Trigger-Types). |
| `display` | DisplaySpec | — | How the message is shown. See [Display Modes](Display-Modes). |
| `message` | string | — | Short text for single-entry (non-chain) guidance. Overrides `display.text`. |
| `once` | bool | `true` | Fire once per character (player scope) or once per world (global scope). |
| `cooldown` | float | `0` | Throttle in seconds. Alternative to `once`. |
| `scope` | string | `player` | `player` or `global`. See [Player vs Global Scope](Player-vs-Global-Scope). |
| `sound` | string | — | Optional vanilla SFX prefab name played when the entry fires. |
| `version` | int | `1` | Bump to re-show updated entries to players who already saw an older version. |
| `discord_on_complete` | bool | `false` | POST webhook when a chain completes. |
| `requires` | list | `[]` | Entry IDs that must have fired before this entry is eligible. |
| `stop_when` | list | `[]` | Entry IDs; this entry stops firing once any of these have fired. |
| `announce` | AnnounceSpec | — | Discord webhook message for this entry. |
| `rewards` | list | `[]` | Rewards granted when this entry fires. See [Reward System](Reward-System). |
| `steps` | list | — | Multi-step chain. When set, `trigger`/`display`/`once` on the parent are ignored. See [Guide Chains](Guide-Chains). |
| `conversation` | ConversationSpec | — | NPC conversation choices. Requires `display.mode: conversation`. See [NPC Conversations](NPC-Conversations). |

---

## `trigger` Fields

```yaml
trigger:
  type: craft           # trigger type string — see Trigger Types wiki page
  item: SwordBronze     # prefab name for craft/item_acquired/npc_item_submit
  creature: Troll       # prefab name for kill/boss_defeated
  piece: Stone_floor    # prefab name for build
  biome: AshLands       # biome name for biome trigger
  location: Vendor_BlackForest  # ZoneSystem location name for distance trigger
  radius: 50            # metres for distance trigger
  skill: Swords         # skill name for skill_level trigger
  level: 50             # threshold level for skill_level trigger
  npc: Haldor           # NPC prefab name for npc_interacted/npc_conversation/npc_item_submit
  count: 5              # npc_item_submit OR item_acquired: how many required (>1 = collection goal)
  consume: true         # for npc_item_submit: remove items from inventory
  goals:                # item_acquired only: require several items at once (see below)
    - { item: FineWood, count: 30 }
    - { item: Resin, count: 25 }
  interval: "30m"       # for timed: e.g. "5m", "1h", "30s"
  entry: other_id       # for entry_finished: the entry that must complete
  damage_type: Fire     # for damage_type trigger
```

For an `item_acquired` trigger, `count > 1` requires N of a single item, while a
`goals:` list requires several different items simultaneously (it takes precedence over
`item`/`count`). The HUD tracker and Codex show a `current/goal` count — no progress bar.
Once collection begins the entry stays visible even if the items are later removed, and
completes only when every goal is currently satisfied. See [Trigger Types](Trigger-Types#item_acquired).

---

## `display` Fields

```yaml
display:
  mode: raven           # raven | message | chat | rune | intro | conversation
  topic: "My Topic"     # header text (Hugin popup title, rune/intro heading, conversation title)
  text: "Body text…"    # main message body; supports {playerName} {itemName} {creatureName} {biome}
  position: TopLeft     # for message mode: TopLeft | Center
```

---

## `announce` Fields

```yaml
announce:
  discord: "**{playerName}** did the thing!"
  # Use "" (empty string) to use the DefaultTemplate from BepInEx config.
  # Supported tokens: {playerName}, {id}, {topic}, {text}
```

---

## Text Tokens

The following tokens are replaced with live values in `display.text`, `message`, and `announce.discord`:

| Token | Replaced with |
|---|---|
| `{playerName}` | The triggering player's display name |
| `{itemName}` | The item's display name (craft/pickup triggers) |
| `{creatureName}` | The creature's display name (kill triggers) |
| `{biome}` | The biome name (biome trigger) |

> **Note:** Tokens are expanded in `message`, `chat`, `rune`, and `intro` modes. In `raven` mode, `display.text` is baked in at config load and does **not** expand tokens. Use `message` or `chat` mode if you need personalised raven-style tips.

---

## Complete Example

```yaml
tracker:
  anchor: TopRight
  hotkey: F10
  auto_hide_delay: 8

guidances:

  # Simple one-shot raven popup
  - id: first_axe
    title: "Woodcutter's Call"
    category: "Early Game"
    trigger:
      type: craft
      item: AxeStone
    display:
      mode: raven
      topic: "Stone Axe"
      text: "A crude edge, but it bites. Fell trees, gather wood."
    once: true

  # Recurring tip with stop condition
  - id: swamp_tip
    trigger:
      type: biome
      biome: Swamp
    display:
      mode: message
      position: TopLeft
      text: "Bring an antidote potion into the swamp."
    cooldown: 900
    stop_when: [crafted_antidote_potion]

  # Global event — fires for all players when Eikthyr dies
  - id: world_eikthyr_fell
    scope: global
    trigger:
      type: kill
      creature: Eikthyr
    display:
      mode: intro
      topic: "The Stag-King Falls"
      text: "Eikthyr is slain. The realm trembles."
    once: true
    announce:
      discord: "⚔️ **{playerName}** has slain **Eikthyr**!"

  # Multi-step quest chain with rewards
  - id: forge_bronze
    title: "The Bronze Age"
    category: "Progression"
    steps:
      - trigger: { type: kill, creature: Troll }
        message: "Collect Troll Hide from Troll corpses."
        progress_goal: 2
        progress_trigger: { type: item_acquired, item: TrollHide }
        progress_label: "Hides"
      - trigger: { type: craft, item: ArmorTrollLeatherChest }
        message: "Craft Troll Leather Armor at a workbench."
    rewards:
      - type: skill_exp
        skill: Swords
        amount: 500
      - type: buff
        effect: SE_Rested
        duration_override: 300

  # NPC conversation
  - id: haldor_greeting
    trigger:
      type: npc_conversation
      npc: Haldor
    display:
      mode: conversation
      topic: "Haldor"
      text: "Well met, traveler! I have rare goods from distant lands."
    conversation:
      choices:
        - text: "What do you sell?"
          goto: haldor_wares_info
        - text: "Nothing, thanks."
    once: false
    cooldown: 300
```
