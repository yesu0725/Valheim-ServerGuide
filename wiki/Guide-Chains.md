# Guide Chains

A guide chain is a multi-step quest. Each step has its own trigger. The chain advances one step at a time — step 2 only becomes active after step 1 fires, and so on. The chain is complete when the final step fires.

Active chains appear in the **Codex**, and pin themselves to the **HUD tracker** the moment they start (see [Auto-pin](#auto-pin-to-the-hud-tracker)).

---

## Defining a Chain

Add a `steps` list to a guidance entry. When `steps` is present, the parent entry's `trigger`, `display`, and `once` fields are ignored — each step has its own trigger and message.

```yaml
- id: forge_bronze
  title: "The Bronze Age"
  category: "Progression"
  steps:
    - trigger: { type: item_acquired, item: CopperOre }
      message: "Mine Copper from deposits in the Black Forest."
    - trigger: { type: craft, item: Bronze }
      message: "Smelt Copper and Tin at a Smelter to make Bronze."
    - trigger: { type: craft, item: SwordBronze }
      message: "Craft a Bronze Sword at the Forge."
```

The player sees step 1's message when they pick up their first Copper Ore. Once that fires, step 2 activates, and so on.

---

## Step Fields

Each step in the `steps` list supports:

| Field | Type | Description |
|---|---|---|
| `trigger` | TriggerSpec | What causes this step to advance. |
| `message` | string | Text shown when this step fires. Supports `{playerName}` etc. |
| `display` | DisplaySpec | Override the default display mode for this step (optional). |
| `description` | string | What the step asks the player to do. Shown on the HUD tracker row (tooltip / Full view) and as the step's line in the Codex's **Upcoming Steps** list (which falls back to `message` when absent). |
| `progress_goal` | int | If > 0, this is a counter step — fires only after this many trigger events. |
| `progress_trigger` | TriggerSpec | The trigger counted toward `progress_goal`. Required when `progress_goal > 0`. |
| `progress_label` | string | Label for the counter (e.g. `"Trophies"`, `"Kills"`). |

---

## Counter Steps

A counter step requires the player to perform a trigger action multiple times before advancing.

```yaml
- id: troll_trophy_quest
  title: "Troll Hunt"
  category: "Bounties"
  steps:
    - trigger: { type: npc_interacted, npc: Haldor }
      message: "Haldor wants Troll Trophies. Bring him 3."
    - trigger: { type: npc_item_submit, npc: Haldor, item: TrollTrophy, count: 3 }
      message: "Trophies delivered! Haldor rewards you."
```

Or using a `progress_goal` counter:

```yaml
steps:
  - trigger: { type: biome, biome: BlackForest }
    message: "Hunt 5 Trolls in the Black Forest."
    progress_goal: 5
    progress_trigger:
      type: kill
      creature: Troll
    progress_label: "Trolls"
  - trigger: { type: entry_finished, entry: troll_hunt_chain }
    message: "Quest complete! Return to camp."
```

The HUD tracker shows the step's progress as a `2/5` count next to the chain title as the player kills them. The step fires when the counter reaches `progress_goal`.

---

## Chain Rewards

Add a `rewards` list to the parent entry to grant rewards when the **final step completes**:

```yaml
- id: forge_bronze
  title: "The Bronze Age"
  rewards:
    - type: skill_exp
      skill: Swords
      amount: 500
    - type: buff
      effect: SE_Rested
      duration_override: 600
  steps:
    - trigger: { type: craft, item: Bronze }
      message: "Smelt Bronze."
    - trigger: { type: craft, item: SwordBronze }
      message: "Forge a Bronze Sword."
```

See [Reward System](Reward-System) for all reward types.

---

## Discord Notification on Completion

Set `discord_on_complete: true` to post a Discord webhook when the chain finishes:

```yaml
- id: boss_prep_chain
  title: "Prepare for Eikthyr"
  discord_on_complete: true
  announce:
    discord: "**{playerName}** has completed the Eikthyr preparation quest!"
  steps:
    ...
```

---

## Auto-pin to the HUD Tracker

Since 0.17.0 a chain pins itself to the tracker when its first step fires (or its first counter step starts), and the panel opens to its titles view so the player sees the quest arrive. It happens once per chain: a player who unpins it is never overruled. A chain with no `title` is not pinned (the tracker cannot show it), and neither is one that completes in the same event it started.

```yaml
- id: background_lore
  title: "Old Stories"
  auto_track: false        # stays off the tracker until the player pins it from the Codex
  steps:
    ...
```

Players can turn auto-pinning off for every quest with `HudTracker → AutoPinNewQuests = false` in their BepInEx config. Only chains auto-pin; single counted entries are pinned from the Codex by hand.

---

## Chain State in the HUD Tracker

While a chain is in progress, the HUD tracker (top-right by default) shows:

- The chain's `title`
- The current step's `message` (truncated if long)
- A `current/goal` count for counter steps
- Hovering the row shows the step's `description` tooltip (if set)

Once the final step fires, the chain row shows a completion animation and is removed from the tracker after a short delay.

The tracker badge (corner hint) shows the count of active chains even when the tracker panel is hidden.

---

## Chaining Entries with `entry_finished`

Chains can trigger each other. Use `trigger.type: entry_finished` to start a follow-up entry or chain when another completes:

```yaml
- id: reward_forge_bronze
  trigger:
    type: entry_finished
    entry: forge_bronze       # fires when the forge_bronze chain completes
  display:
    mode: message
    position: Center
    text: "Quest complete! Rewards granted."
  rewards:
    - type: item
      item: SwordBronze
      amount: 1
      quality: 2
```

This separates reward delivery from the chain itself, which is useful when you want the reward entry to have its own display message.
