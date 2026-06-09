# ValheimServerGuide — Guide Authoring Reference

This document defines the conventions for writing guidance entries in any `*.yaml` file
placed in `BepInEx/config/ValheimServerGuide/`. All files in that folder are merged
automatically by the loader — any filename works as long as the content follows this schema.

---

## 1. Display Mode by Trigger Type

The table below is the **server rule** for this modpack. Every entry must use the mode
assigned to its trigger type. Triggers not in either list (NPC interactions, equip, etc.)
use `message` unless the entry warrants a more dramatic presentation.

### Rune Mode Triggers

`rune` presents centered lore-stone text on a darkened screen. Ghost mode is engaged
automatically so the player cannot be killed while reading.

| Trigger Type | Display Mode | When it fires |
|---|---|---|
| `craft` | `rune` | Player crafts any item (or a specific item) |
| `item_acquired` | `rune` | Player picks up an item (supports `Trophy*` wildcard) |
| `kill` | `rune` | Player kills a specific creature |
| `build` | `rune` | Player places a specific building piece |
| `chest_opened` | `rune` | Player opens any container (fires once per character) |
| `skill_level` | `rune` | Player crosses a specific skill threshold |
| `timed` | `rune` | Server timer fires after an interval or daily/hourly |
| `boss_defeated` | `rune` | Player lands the killing blow on a boss |

> **Why rune for timed?** Follow-up tips that fire automatically after a chain step
> use rune so the player is protected (ghost mode) while reading and must actively
> dismiss the text. This prevents the tip from going unread.

### Raven Mode Triggers

`raven` calls Hugin (or Munin) to the player and shows the text as a speech bubble.
The raven will fly in when conditions are safe and the player is near enough.

| Trigger Type | Display Mode | When it fires |
|---|---|---|
| `first_login` | `raven` | First time the character logs in (once per character ever) |
| `player_death` | `raven` | Player dies (use `max_fires` to cap repeats) |
| `biome` | `raven` | Player enters a biome for the first time |
| `distance` | `raven` | Player comes within `radius` meters of a named world location |
| `discover_location` | `raven` | *(Planned)* Location revealed on player map |

### Other Trigger Types

These triggers are not covered by the rune/raven rule. Use `message`, `conversation`,
or whichever mode best fits the context.

| Trigger Type | Recommended Mode | Notes |
|---|---|---|
| `equip` | `message` | Brief tip; rune would be too disruptive on equip |
| `npc_interacted` | `message` | Short-press E on a trader; brief tooltip-style tip |
| `npc_conversation` | `conversation` | Hold E on a trader; opens choice panel |
| `npc_item_submit` | `rune` | Player submits an item to an NPC altar |
| `location_entered` | `message` | Player steps into a named location |
| `entry_finished` | *(inherits)* | Use the mode of the entry being chained to |

---

## 2. YAML Entry Structure

Every entry follows this structure. Fields marked **required** must be present.
All other fields are optional.

```yaml
- id: unique_snake_case_id          # required — must be unique across all yaml files
  title: "Human-Readable Title"     # shown in the HUD tracker and Codex
  category: Exploration             # Companions | Trading | Building | Skills | Exploration | Inventory | Groups | General
  version: 1                        # bump when step messages change meaningfully
  scope: player                     # player (default) | global
  once: true                        # true = fire once per character (default); false = repeatable
  cooldown: 0                       # seconds before the entry can fire again (float)

  trigger:                          # required for single-entry (non-chain)
    type: craft                     # trigger type string
    item: ArrowWood                 # filter field — depends on trigger type (see §3)

  display:
    mode: rune                      # rune | raven | message | chat | intro | conversation
    topic: "Entry Title"            # header text (rune/raven/intro/conversation)
    position: Center                # TopLeft (default) | Center  — message mode only

  message: "Guide text here."       # top-level message (use this instead of display.text)

  summary: >                        # short recap shown in Codex when the entry is complete;
    You completed this quest.       # takes priority over the final step's message
    Here is what you learned.

  requires: []                      # list of entry ids that must have fired first
  stop_when: []                     # list of entry ids — stop firing once any of these has fired

  rewards:                          # optional — granted on fire/completion
    - type: item                    # item | skill_exp | skill_level | buff
      item: Coins
      amount: 10
    - type: buff
      effect: SE_Rested
      duration_override: 600

  announce:
    discord: ""                     # "" = use default template; custom string supported

  discord_on_complete: false        # POST discord webhook when a chain completes
```

### Chain Entry (steps list)

When `steps:` is present, the top-level `trigger`/`display`/`message` are ignored.
Each step fires only after the previous step has fired.

```yaml
- id: my_chain
  title: "My Chain Guide"
  category: Exploration
  version: 1
  discord_on_complete: true
  steps:
    - trigger:
        type: first_login           # step 1 — raven (first_login rule)
      display:
        mode: raven
        topic: "Welcome"
      message: "This is the first step."

    - trigger:
        type: timed                 # step 2 — rune (timed rule)
        interval: "120"
        id: my_chain_tip
      display:
        mode: rune
        topic: "Follow-up Tip"
      message: "This fires 120 seconds after step 1."

    - trigger:
        type: item_acquired         # step 3 — rune (item_acquired rule)
        item: Coins
      display:
        mode: rune
        topic: "You Found Coins"
      description: "Pick up Coins from any source — loot, trading, or bounties."
      message: "You picked up some coins. This is step 3."
      rewards:
        - type: buff
          effect: SE_Rested
          duration_override: 300
```

#### `description` — Codex hint for the current step

Each step may include a `description` field. This text is shown in the **Codex body panel**
while the step is the player's **current incomplete step** — it tells the player what to do
next. It also appears as a tooltip when the player hovers the step row in the HUD tracker.

When the chain is complete the Codex shows the entry-level `summary` if one is set,
otherwise it falls back to the final step's `message`. The three fields serve distinct purposes:

| Field | Level | When shown | Purpose |
|---|---|---|---|
| `description` | step | While this step is incomplete (Codex body + tracker tooltip) | What to do to advance |
| `message` | step | When the trigger fires (in-game popup); also Codex recap if no `summary` | What the player learns / reward context |
| `summary` | entry | In the Codex body whenever the entire entry is complete | Short recap of the whole quest |

```yaml
steps:
  - trigger:
      type: build
      piece: guard_stone
    display:
      mode: rune
      topic: "Protective Ward"
    description: >
      Build a Protective Ward at your base.
      Open the hammer menu, find the ward under Misc, and place it inside your perimeter.
      Required: 5 Fine Wood, 5 Greydwarf Eye, 1 Surtling Core.
    message: "Your ward is active. Structures repair passively, raids are blocked, and rain damage is prevented."
```

If `description` is absent the Codex falls back to `message` for the body text.

### Progress Counter Step

A step with `progress_goal > 0` counts matching items before it fires.

```yaml
- trigger:
    type: item_acquired             # activates the counter on first matching pickup
    item: "Trophy*"
  progress_trigger:
    type: item_acquired
    item: "Trophy*"
  progress_goal: 5
  progress_label: "Trophies"
  display:
    mode: rune
    topic: "Trophy Collector"
  message: "You have collected 5 trophies. Your first Slayer buff is now active."
```

---

## 3. Trigger Type Reference

### `craft`
```yaml
trigger:
  type: craft
  item: ArrowWood           # prefab name of the crafted item; omit to match any craft
```
Fires when the player successfully crafts the named item at any crafting station.
Omitting `item` matches the first craft of any item.

### `item_acquired`
```yaml
trigger:
  type: item_acquired
  item: "Trophy*"           # supports trailing * wildcard
  count: 1                  # >1 = collection goal (current/goal count shown)
```
Fires when the player picks up a matching item. `Trophy*` matches `TrophyGreydwarf`,
`TrophyBoar`, etc. Use exact prefab names for specific items (`Coins`, `Wood`, etc.).
With `count > 1` the entry tracks the inventory total (pickups + crafting) and fires
once the goal is reached.

For several different items at once, use a `goals:` list instead of `item`/`count`.
The entry fires only when every goal is met simultaneously:
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
The tracker row shows `N / M goals`; its tooltip and the Codex body list each item's
`current/goal`. Once started, the entry stays visible even if the items are later
removed, and is marked complete only when all goals are currently satisfied.

### `kill`
```yaml
trigger:
  type: kill
  creature: Greydwarf       # prefab name of the killed creature
```
Fires when the local player lands the killing blow on the named creature.
Prefab names: `Greydwarf`, `Troll`, `Skeleton`, `Draugr`, etc.

### `build`
```yaml
trigger:
  type: build
  piece: piece_workbench    # prefab name of the placed piece
```
Fires when the player successfully places the named building piece.
Piece prefab names have no separators: `piece_workbench`, `woodwall`, `guard_stone`.

### `chest_opened`
```yaml
trigger:
  type: chest_opened        # no subject filter — fires on any container
```
Fires once per character on the first container interaction. No additional fields needed.

### `skill_level`
```yaml
trigger:
  type: skill_level
  skill: Swords             # skill name (Swords, Axes, Bows, Woodcutting, etc.)
  level: 25                 # exact whole-number threshold to cross
```
Fires the first time the player's skill crosses the specified level.
Use one step per threshold (25, 50, 75, 100).

### `timed`
```yaml
trigger:
  type: timed
  interval: "120"           # seconds as string; or "daily" | "hourly"
  id: my_chain_tip          # stable unique identifier for this timer
```
Server fires this on a coroutine. On a dedicated server the RPC broadcasts to all clients.
The `id` must be unique across all timed entries. Use `"daily"` for once-per-server-day tips.

### `boss_defeated`
```yaml
trigger:
  type: boss_defeated
  creature: Eikthyr         # Eikthyr | gd_king | Bonemass | Dragon | GoblinKing | SeekerQueen
```
Fires when the local player lands the killing blow on the boss.
Note: fires only for the player who deals the final blow. Use `scope: global` to broadcast
to everyone when the first player defeats the boss world-wide.

### `first_login`
```yaml
trigger:
  type: first_login         # no fields — fires exactly once per character, ever
```
Fires on `Player.OnSpawned` the first time the character is created. Ideal for
modpack introduction messages. Use raven mode per the server rule.

### `player_death`
```yaml
trigger:
  type: player_death
  max_fires: 3              # optional — cap total fires (default: unlimited)
```
Fires every time the player dies, up to `max_fires` times.
Use raven mode per the server rule.

### `npc_interacted`
```yaml
trigger:
  type: npc_interacted
  npc: Haldor               # prefab name of the trader NPC
```
Fires when the player short-presses E on the named trader to open the store.
Supported vanilla NPCs: `Haldor`, `Hildir`, `BogWitch`.

### `npc_conversation`
```yaml
trigger:
  type: npc_conversation
  npc: Haldor
```
Fires when the player holds E (≥ 0.5 s) on the named trader. The store does NOT
open — instead the conversation panel opens. Pair with `display.mode: conversation`
and a `conversation.choices` list.

### `npc_item_submit`
```yaml
trigger:
  type: npc_item_submit
  npc: Haldor               # trader to submit to
  item: TrophyEikthyr       # specific item prefab; omit for catch-all
  count: 1                  # items required (>1 = current/goal count shown)
  consume: true             # remove items on submission
```
Fires when the player presses a hotbar key (1-8) near the named trader.
Use this to implement "sacrifice trophy at altar" mechanics.

### `location_entered`
```yaml
trigger:
  type: location_entered
  location: "WL_*"          # location prefab name; trailing * wildcard supported
```
Fires once per location per character when the player enters within 40 m of the location.

### `entry_finished`
```yaml
trigger:
  type: entry_finished
  entry: some_other_entry_id
```
Fires after the named entry completes (its single step fires, or its final chain step
fires). Use to chain separate entries together without coupling them directly.

---

## 4. Display Mode Reference

### `rune` — Lore Stone  *(action triggers)*
Darkens the screen and shows centered text in runestone style. Ghost mode (invulnerability)
is engaged until the player dismisses. **Requires a `topic` field.**
```yaml
display:
  mode: rune
  topic: "Ancient Knowledge"
```

### `raven` — Hugin Popup  *(environmental/existential triggers)*
Hugin flies to the player and delivers the message as a speech bubble. The raven
requires safe conditions to land. **Requires a `topic` field.**
```yaml
display:
  mode: raven
  topic: "A Word of Warning"
```

### `message` — HUD Toast  *(NPC interactions, location entries, minor tips)*
Quick overlay toast — no ghost mode, no input lock.
```yaml
display:
  mode: message
  position: Center          # Center | TopLeft (default)
```

### `chat` — Chat Log
Appended to the chat log in gold text (`ChatColor` config). Chat panel is forced visible.
Good for ambient server lore/flavor text.
```yaml
display:
  mode: chat
```

### `intro` — Valkyrie Cinematic
Full cinematic: screen fades black, intro music plays, Valkyrie-style scrolling text.
Player input and ESC are locked. Use sparingly — major lore moments only.
```yaml
display:
  mode: intro
  topic: "The World Awakens"
```

### `conversation` — Choice Panel
Opens a lower-screen panel with topic, body text, and clickable choice buttons.
Must pair with `trigger.type: npc_conversation` and a `conversation.choices` list.
```yaml
display:
  mode: conversation
  topic: "Haldor"
conversation:
  choices:
    - text: "Tell me more."
      goto: followup_entry_id
    - text: "Not now."
```

---

## 5. Template Variables

These expand at display time in any `message:` or `display.text:` field.

| Variable | Expands to |
|---|---|
| `{playerName}` or `{player_name}` | Character name of the triggering player |
| `{skill}` | Skill name (skill_level events only) |
| `{level}` | Skill level (skill_level events only) |
| `{biome}` | Current biome name of the local player |
| `{itemName}` | Display name of the triggering item |
| `{creatureName}` | Display name of the killed creature |
| `{step}` | Current chain step number (1-based) |
| `{total}` | Total steps in the chain |

> **Raven limitation:** `display.text` for raven entries is baked in at config load.
> Template variables DO NOT expand in raven `display.text`. Use `message:` (top-level)
> instead — it is overwritten with the rendered text at show time.

---

## 6. Scope: Player vs Global

```yaml
scope: player    # (default) Each player has independent fire state.
scope: global    # First player to trigger broadcasts the display to all connected players.
                 # The fired state is stored in world save (ZoneSystem global key).
```

Use `scope: global` for world-first events (first Eikthyr kill on the whole server, etc.).
`vsg_reset all` does NOT touch global entries — clear them individually or use
`removekey VSG.<id>` in the server console.

---

## 7. Full Example — Mod Introduction Chain

The pattern below is the standard template for introducing a mod that has no NPCs
or buildable pieces. It uses `first_login` (raven) for discovery and `timed` or
`item_acquired` (rune) for follow-up tips.

```yaml
- id: my_mod_chain
  title: "My Mod — Introduction"
  category: Inventory
  version: 1
  steps:
    # Step 1 — introduce on first login (raven rule)
    - trigger:
        type: first_login
      display:
        mode: raven
        topic: "My Mod"
      message: "This server has My Mod installed. Open the panel from the HUD to get started."

    # Step 2 — follow-up tip 2 minutes later (timed rule → rune)
    - trigger:
        type: timed
        interval: "120"
        id: my_mod_tip_1
      display:
        mode: rune
        topic: "My Mod"
      message: "Quick tip: press F5 to open the My Mod configuration panel in-game."

    # Step 3 — practical tip when coins arrive (item_acquired rule → rune)
    - trigger:
        type: item_acquired
        item: Coins
      display:
        mode: rune
        topic: "My Mod Economy"
      message: "You have coins. Use My Mod's shop to spend them on special items."
```

---

## 8. Common Mistakes

| Mistake | Fix |
|---|---|
| `raven` without `topic:` | Always include `display.topic` for raven entries |
| `rune` without `topic:` | Always include `display.topic` for rune entries |
| Template vars in raven `display.text:` | Move text to top-level `message:` field instead |
| Non-existent `requires:` id | Entry will be permanently blocked; verify all required ids |
| Duplicate `id:` across files | First occurrence wins; second is dropped with an error log |
| `timed` trigger without unique `id:` | Each timed step needs a globally unique `id:` value |
| NPC prefab name for a mod that adds no NPC | Use `first_login`, `item_acquired`, or `boss_defeated` instead |
| Build piece for a mod that adds no pieces | Use `first_login` or `item_acquired` to introduce the mod |
