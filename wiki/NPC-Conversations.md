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
