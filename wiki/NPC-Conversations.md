# NPC Conversations

The NPC conversation system lets you attach dialogue panels to trader NPCs (Haldor, Hildir, BogWitch). Players hold E near the NPC to open a custom dialogue instead of the store.

---

## How It Works

- **Tap E** on a trader → opens the store normally (vanilla behaviour). Also fires `npc_interacted` entries.
- **Hold E (≥ 0.5 s)** on a trader → opens the conversation panel if a matching `npc_conversation` entry exists and its gates pass.

When a conversation entry is eligible, the trader's hover tooltip gains an extra line: `[Hold E] Quest`.

---

## Basic Setup

A conversation entry needs:

1. `trigger.type: npc_conversation` with a matching `npc` name.
2. `display.mode: conversation` with `topic` (panel header) and `text` (body text).
3. A `conversation.choices` list with the button options.

```yaml
- id: haldor_greeting
  trigger:
    type: npc_conversation
    npc: Haldor           # case-insensitive; matches any Trader-component prefab
  display:
    mode: conversation
    topic: "Haldor"
    text: "Well met, traveler! I have rare goods from distant lands. What brings you here?"
  conversation:
    choices:
      - text: "Tell me about your wares."
        goto: haldor_wares_info     # fires this entry ID after the panel closes
      - text: "Nothing, thanks."
                                    # no goto = dismiss panel only
  once: false
  cooldown: 300
```

---

## Choice Buttons

Each item in `conversation.choices` becomes a button in the panel.

| Choice Field | Type | Description |
|---|---|---|
| `text` | string | Button label shown to the player. |
| `goto` | string | Entry ID to fire when this choice is selected. Omit for a dismiss-only button. |
| `rewards` | list | Rewards granted to the player when this choice is confirmed. See [Reward System](Reward-System). |

If no choices are defined, a default **Dismiss** button is inserted automatically.

```yaml
conversation:
  choices:
    - text: "I'll take the sword."
      goto: null
      rewards:
        - type: item
          item: SwordBronze
          amount: 1
          quality: 2
    - text: "Train me in swordsmanship."
      goto: null
      rewards:
        - type: skill_exp
          skill: Swords
          amount: 1000
    - text: "Not now."
```

---

## Branching Conversations

Use `goto` to chain entries together into a dialogue tree. Each follow-up entry can itself be a `npc_conversation` entry that fires immediately (via `FireById`) after the choice is made.

```yaml
- id: haldor_greeting
  trigger: { type: npc_conversation, npc: Haldor }
  display:
    mode: conversation
    topic: "Haldor"
    text: "Ah, a traveler! Are you here to trade, or do you seek information?"
  conversation:
    choices:
      - text: "I want to trade."
        goto: haldor_open_store
      - text: "I seek information."
        goto: haldor_info_menu
      - text: "Just passing through."

- id: haldor_info_menu
  trigger: { type: entry_finished, entry: haldor_greeting }
  display:
    mode: conversation
    topic: "Haldor"
    text: "Ask away. I've traveled far and know much."
  conversation:
    choices:
      - text: "Tell me about Moder."
        goto: haldor_moder_lore
      - text: "What's the best weapon?"
        goto: haldor_weapon_tip
      - text: "Never mind."
```

---

## Multi-Quest Picker

When a player holds E on an NPC that has **two or more** eligible `npc_conversation` entries at once, the panel first opens a picker listing each entry's `title` (falling back to its `id`). The player chooses which conversation to start; selecting one fires that entry and opens its conversation normally.

- **1 eligible entry** → opens that conversation directly (no picker).
- **2+ eligible entries** → shows the picker (`What would you like to discuss?`) with one button per entry.

Give pickable entries a `title` so the buttons read well:

```yaml
- id: haldor_wares
  title: "Ask about wares"
  trigger: { type: npc_conversation, npc: Haldor }
  display: { mode: conversation, topic: Haldor, text: "Wares from across the world..." }

- id: haldor_lore
  title: "Ask about the Mistlands"
  trigger: { type: npc_conversation, npc: Haldor }
  display: { mode: conversation, topic: Haldor, text: "The Mistlands hide ancient secrets..." }
```

---

## Multi-Node Dialogue Trees

Instead of a flat `choices:` list, a conversation can define a tree of **nodes**. Each node has its own `text` and `choices`, and choices can jump between nodes, gate on prerequisites, or fire other entries. State is persisted so the NPC can resume where the player left off.

```yaml
- id: hildir_bounty
  once: false
  cooldown: 5
  trigger: { type: npc_conversation, npc: Hildir }
  display:
    mode: conversation
    topic: Hildir
  conversation:
    resume_on_return: true      # reopen at the last-visited node instead of the first
    nodes:
      - id: intro
        text: "You look like someone who's been in the mires before."
        choices:
          - label: "I've fought worse."
            goto_node: fought_worse        # jump to another node; panel stays open
          - label: "What's it to you?"
            goto_node: suspicious
          - label: "[Ask about the bounty board]"
            requires: ["bounty_board_read"] # per-choice gate (entry IDs)
            goto_node: bounty_talk
            hidden_when_locked: true         # omit the button entirely while locked
          - label: "[Trade rare goods]"
            requires: ["rare_trade_unlocked"]
            goto_node: rare_trade
            hidden_when_locked: false        # show greyed-out instead (default)
            locked_hint: "Complete the trade quest first"
      - id: fought_worse
        text: "Good. I need someone fearless for a task..."
        choices:
          - label: "Tell me more."
            goto: hildir_task_followup       # cross-entry goto: closes panel, fires that entry
          - label: "Not interested."         # no goto_node / goto = ends the conversation
      - id: suspicious
        text: "Fair enough. Move along then."
      - id: bounty_talk
        text: "Ah, you've read the board. Here's the job."
      - id: rare_trade
        text: "Rare goods, you say? Let's deal."
```

### Node choice fields

| Field | Type | Description |
|---|---|---|
| `label` | string | Button text. |
| `goto_node` | string | Jump to another node **in the same conversation**; the panel stays open. |
| `goto` | string | Cross-entry goto — closes this conversation and fires another entry by ID. |
| `requires` | list | Entry IDs that must be satisfied for the choice to be usable (same as entry `requires`). |
| `hidden_when_locked` | bool | `true` = hide the button while locked; `false` (default) = show it greyed out. |
| `locked_hint` | string | Hint appended to a locked, visible button. |

A node choice with **neither** `goto_node` **nor** `goto` ends the conversation when selected.

`resume_on_return: true` reopens the conversation at the last-visited node. Node progress is always saved (per character, in `VSG.cn.<entry_id>`) regardless of this flag — it only controls whether a fresh open reads that saved position back.

---

## NPC Hover Text

By default an eligible conversation NPC shows `[Hold E] Quest` on hover. Override it per entry with a `hover_text:` block, keyed by state:

```yaml
- id: bogwitch_brew
  title: "Test Brew"
  trigger: { type: npc_conversation, npc: BogWitch }
  display: { mode: conversation, topic: BogWitch, text: "A test brew, fresh off the cauldron." }
  hover_text:
    default: "[Quest] Test Brew"        # shown while the entry is eligible
    after_fire: "[Completed] Test Brew" # shown once it has fired
```

---

## Input Lock

While the conversation panel is open, the following are disabled:

- Player movement and attacks
- Camera mouse-look
- Pressing E to interact
- Inventory (Tab / I)
- Pause menu (Escape)

The OS cursor is freed so the player can click choice buttons. It is restored when the panel closes.

---

## Supported NPCs

Any NPC with a `Trader` component works. Known names:

| NPC | Prefab Name |
|---|---|
| Haldor | `Haldor` |
| Hildir | `Hildir` |
| Bog Witch | `BogWitch` |

The `npc` field is case-insensitive. A single entry targets one specific NPC.

---

## Firing and Fire State

After the player selects any choice (including dismiss), the conversation entry is marked as fired. `once`, `cooldown`, `requires`, and `stop_when` all apply normally.

To make the conversation reusable, set `once: false` with a `cooldown`:

```yaml
once: false
cooldown: 300    # re-opens every 5 minutes
```

---

## Item Turn-Ins via `npc_item_submit`

For quest turn-ins (rather than dialogue), use `npc_item_submit` instead:

```yaml
- id: haldor_trophy_turnin
  trigger:
    type: npc_item_submit
    npc: Haldor
    item: TrollTrophy
    count: 3
    consume: true
  message: "Haldor thanks you and pockets the trophies."
  rewards:
    - type: item
      item: Coins
      amount: 100
```

The player uses the trophy item while interacting with Haldor. The mod intercepts the action, tracks progress, and fires the entry when the required count is met.
