# CRIT-18 — Reward System

**Status:** Phase 4 `done` · Phase 5 `done`

Grant players rewards when a guidance entry fires or when a chain completes.
Rewards can be items, skill experience/level bumps, or temporary status-effect buffs.
A single entry can offer one reward or a combination.

---

## Overview

Rewards are declared in the `rewards` block of a `GuidanceEntry` (or a `ChoiceSpec` for
NPC conversation outcomes — see CRIT-17). When the entry fires (or the conversation choice
is selected), `RewardDispatcher` processes the list in order and grants each reward to the
local player. A reward notification is shown after granting.

---

## YAML Schema

### On `GuidanceEntry`

```yaml
- id: kill_troll_quest
  trigger:
    type: entry_finished
    entry: kill_troll_chain
  display:
    mode: message
    position: Center
    text: "Quest complete! Rewards granted."
  rewards:
    - type: item
      item: SwordBronze
      amount: 1
      quality: 2
    - type: skill_exp
      skill: Swords
      amount: 500
    - type: buff
      effect: SE_Rested
      duration_override: 600    # seconds; omit to use the effect's default duration
```

### On `ChoiceSpec` (NPC conversation — CRIT-17)

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
    - text: "Train me instead."
      goto: null
      rewards:
        - type: skill_exp
          skill: Swords
          amount: 1000
```

---

## Reward Types

### `item`

| Field | Type | Description |
|---|---|---|
| `item` | string | Prefab name (e.g. `SwordBronze`, `Wood`) |
| `amount` | int | Stack count to add (default 1) |
| `quality` | int | Item quality/upgrade level (default 1, clamped to item's max) |

**Implementation:** `Player.GetInventory().AddItem(prefabName, amount, quality, 0, 0, playerName)`
- Resolve prefab via `ZNetScene.instance.GetPrefab(item)`.
- Log a warning and skip if the prefab is not found or has no `ItemDrop` component.
- If the inventory is full, drop the item in front of the player using
  `ItemDrop.SpawnItemDrop(prefab, player.transform.position + player.transform.forward, ...)`.

### `skill_exp`

| Field | Type | Description |
|---|---|---|
| `skill` | string | Skill name (e.g. `Swords`, `Woodcutting`, `Jump`) |
| `amount` | float | Raw experience points to add |

**Implementation:** `player.GetSkills().RaiseSkill(skillType, amount)`
- Parse `skill` string to `Skills.SkillType` via `System.Enum.TryParse`.
- Log a warning and skip for unrecognised skill names.

### `skill_level`

| Field | Type | Description |
|---|---|---|
| `skill` | string | Skill name |
| `level` | int | Target level (1–100). Only raises, never lowers. |

**Implementation:** `Skills.RaiseSkill` raises **at most one level per call**, so it cannot span
to a target level. Instead set the level directly: `var skill = player.GetSkills().GetSkill(type);`
(creates the entry if missing) then, if `target > skill.m_level`, `skill.m_level = Mathf.Clamp(target, 1, 100); skill.m_accumulator = 0f;`.
- If player already meets or exceeds the target level, log info and skip (only raises, never lowers).

### `buff`

| Field | Type | Description |
|---|---|---|
| `effect` | string | Status effect prefab name (e.g. `SE_Rested`, `Potion_stamina_medium`) |
| `duration_override` | float? | Override `m_ttl` in seconds. Omit to use the effect's default. |

**Implementation:** Status effects are ScriptableObjects in `ObjectDB.instance.m_StatusEffects`,
**not** ZNetScene prefabs — `GetPrefab("SE_Rested")` returns null.
- Resolve by scanning `ObjectDB.instance.m_StatusEffects` and matching `s.name` (asset name) or
  `s.m_name` (token) against `effect`. The Rested asset is actually named `Rested`, so normalize
  both sides (lowercase, strip a leading `$`, then a leading `se_`) so `SE_Rested` / `Rested` /
  `$se_rested` all match.
- `var active = player.GetSEMan().AddStatusEffect(proto, resetTime: true);` — this clones `proto`,
  adds the live instance, and returns the clone.
- If `duration_override` is set, assign `active.m_ttl` on that returned clone (not the shared asset).
- Log a warning and skip for unknown effect names.
- Use `StatusEffect` (not `SE_Stats`) — the base class is enough for a fire-and-forget approach.

---

## Suggested / Additional Reward Types (future)

The following types are **not implemented in this phase** but are noted for future extension:

| Type | Description |
|---|---|
| `global_key` | Sets a Valheim world global key (e.g. for unlocking boss altars or lore entries) |
| `unlock_recipe` | Adds a recipe to the player's known list via `Player.AddKnownRecipe` |
| `spawn_npc` | Spawns a friendly NPC/companion prefab near the player |
| `currency` | Grants in-game coins (Coins prefab, integer amount) |

Extend `RewardSpec.Type` with additional strings when these are needed.

---

## RewardSpec (GuidanceConfig.cs additions)

```csharp
public class RewardSpec
{
    /// item | skill_exp | skill_level | buff
    public string Type { get; set; }

    // item fields
    public string Item { get; set; }
    public int Amount { get; set; } = 1;
    public int Quality { get; set; } = 1;

    // skill fields
    public string Skill { get; set; }
    public float SkillExp { get; set; }   // used by skill_exp
    public int Level { get; set; }        // used by skill_level

    // buff fields
    public string Effect { get; set; }
    public float? DurationOverride { get; set; }
}
```

`GuidanceEntry` gains:
```csharp
public List<RewardSpec> Rewards { get; set; } = new List<RewardSpec>();
```

`ChoiceSpec` (CRIT-17) gains:
```csharp
public List<RewardSpec> Rewards { get; set; } = new List<RewardSpec>();
```

---

## RewardDispatcher (new file)

**`src/Rewards/RewardDispatcher.cs`**

```csharp
public static class RewardDispatcher
{
    public static void Grant(List<RewardSpec> rewards, Player player) { ... }
}
```

Called from:
- `GuidanceDispatcher.Raise()` after a single entry fires (if `entry.Rewards.Count > 0`).
- `GuidanceDispatcher.AdvanceChain()` when the final chain step completes (if `entry.Rewards.Count > 0`).
- `NpcConversationPanel` when a choice with `Rewards` is confirmed.

---

## Reward Notification (Phase 5)

**`src/Rewards/RewardNotification.cs`**

After `Grant()` returns, build a summary string listing what was given:
- "Received: Iron Sword (Q2), +500 Swords XP, Rested buff (10 min)"
- Show via `MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, summary)`.

The summary is built from the `RewardSpec` list — no additional state needed.

---

## Validation at Config Load

In `GuidanceConfigLoader` (or the dispatcher pre-warm), iterate all entries' `Rewards` and log a
`LogWarning` for:
- `type: item` with an unrecognised prefab name.
- `type: skill_exp / skill_level` with an unrecognised skill string.
- `type: buff` with an unrecognised prefab name or a prefab that has no `StatusEffect` component.

**Do not throw.** Unknown rewards are skipped at grant-time; the rest of the list still executes.

---

## Files Changed

| File | Change |
|---|---|
| `src/Config/GuidanceConfig.cs` | Add `RewardSpec`; add `Rewards` to `GuidanceEntry` and `ChoiceSpec` |
| `src/Rewards/RewardDispatcher.cs` | New — `Grant()` implementation for all reward types |
| `src/Rewards/RewardNotification.cs` | New — build and show reward summary via `MessageHud` |
| `src/Triggers/GuidanceDispatcher.cs` | Call `RewardDispatcher.Grant()` after single-entry fire and chain completion |
| `src/Display/NpcConversationPanel.cs` | Call `RewardDispatcher.Grant()` on choice confirmation if `choice.Rewards` is non-empty |

---

## Criteria

### Phase 4 — Core Reward Granting

- [x] `type: item` adds the correct prefab to the player's inventory with the specified `amount` and `quality`.
- [x] `type: item` drops the item in front of the player (not silently lost) when the inventory is full.
- [x] `type: item` logs a warning and skips for unknown prefab names.
- [x] `type: skill_exp` adds the exact `amount` of experience to the named skill.
- [x] `type: skill_exp` logs a warning and skips for unknown skill names.
- [x] `type: skill_level` raises the skill to the target `level` without reducing it if already higher.
- [x] `type: buff` applies the named status effect to the player.
- [x] `type: buff` applies `duration_override` (in seconds) when present; uses the effect's default duration when absent.
- [x] `type: buff` logs a warning and skips for unknown effect names.
- [x] Rewards on a `GuidanceEntry` fire when the entry fires (single-entry, player scope).
- [x] Rewards on a chain `GuidanceEntry` fire when the final step completes.
- [x] An unknown `type` string logs a warning and is skipped; the rest of the rewards list still executes.
- [x] Multiple rewards on one entry are all granted in declaration order.

### Phase 5 — Notification + Choice Rewards

- [x] A `MessageHud` center message lists all granted rewards after `Grant()` completes.
- [x] Rewards on a `ChoiceSpec` are granted when that choice is confirmed in the conversation panel.
- [x] The notification message is suppressed when the rewards list is empty.
- [x] Config load logs warnings for unrecognised item/skill/effect names; load still succeeds.
