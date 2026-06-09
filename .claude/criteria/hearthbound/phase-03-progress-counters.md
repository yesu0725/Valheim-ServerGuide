# Phase 03 — Progress Counter Tracking

**Status:** `complete`
**Depends on:** Phase 02 (ChainState), Phase 01 (`item_acquired` trigger)
**Blocks:** Phase 09 (SlayerSkills content, HaldorBounties content)

Enables a chain step to require N occurrences of a trigger before it advances.
Used for guides like SlayerSkills ("collect 5 trophies") or HaldorBounties
("complete 3 bounties this week").

---

## YAML Shape

A `GuidanceStep` with `progress_goal` set will not fire its `message` until
the counter reaches the goal. Each matching `progress_trigger` event increments the counter.

```yaml
steps:
  - trigger:
      type: first_login             # step activates on first login (starts the counter)
    progress_trigger:
      type: item_acquired
      item: "Trophy_*"             # wildcard: any trophy item
    progress_goal: 3               # must collect 3 trophies before step message fires
    display:
      mode: hud
    message: "You've collected enough trophies to unlock your first Slayer buff! Check your skills menu."
```

Counter progress is shown in the Objective Tracker HUD (Phase 04) as `"Trophies: 2 / 3"`.

---

## Data Model

### `ChainState` additions (`src/State/ChainState.cs`)

```csharp
// entry_id:step_index -> current count toward progress_goal
public Dictionary<string, int> StepCounters;
```

Serialized alongside existing chain state under `"vsg_chain_v1"`.

---

## Dispatcher Changes (`GuidanceDispatcher.cs`)

On `Raise(TriggerEvent evt)`:

1. For each active chain step that has `ProgressGoal > 0`:
   a. Check if `evt` matches the step's `ProgressTrigger`.
   b. If match → increment `ChainState.StepCounters["{entry_id}:{step_index}"]`.
   c. If counter has reached `ProgressGoal`:
      - Fire the step's `message` via `GuidanceDisplay`.
      - Advance the chain to the next step.
      - Reset the counter entry (it is no longer needed).
   d. Save and sync `ChainState`.

2. The step's primary `Trigger` determines when the counter *starts* (i.e., when the step
   becomes active). The `ProgressTrigger` is what gets counted.

---

## Wildcard Matching for `item_acquired`

`trigger.item = "Trophy_*"` → dispatcher uses `evt.Subject.StartsWith("Trophy_",
StringComparison.OrdinalIgnoreCase)` instead of exact equality.

Wildcard is only supported as a trailing `*`. Patterns like `*_Trophy` are not supported.

---

## Criteria

- [x] A step with `progress_goal: 0` (or field absent) behaves as a normal non-counter step.
- [x] Counter is persisted in `m_customData` and survives session end.
- [x] Counter is synced to server on every increment alongside chain state.
- [x] Wildcard `item: "Trophy_*"` matches any prefab starting with `"Trophy_"` (case-insensitive).
- [x] Counter never exceeds `progress_goal` (capped on increment).
- [x] Once the goal is reached and the message fires, the counter entry is removed from state.
- [x] HUD tracker (Phase 04) displays current counter as `"N / Goal"` — implemented in Phase 04.
- [x] If `progress_trigger` is missing but `progress_goal > 0`, log a config warning and treat the step as a normal step.

---

## Implementation Notes (completed 2026-06-04)

- **Counter key**: `VSG.cc.{chainId}:{stepIndex}` in `Player.m_customData`; absent = -1 (not yet activated)
- **Lifecycle**: -1 (dormant) → 0 (activated by primary trigger) → N (incremented) → cleared when goal reached
- **Sync**: piggybacked on `VSG_ChainStepUpdate` RPC using `{chainId}:{stepIndex}` key encoding; `OnChainStatePush` routes on `:` separator
- **Cap**: `Math.Min(counter + 1, goal)` prevents overshooting on rapid events
- **Fallback**: `ProgressGoal > 0 && ProgressTrigger == null` → warns + falls back to HandleNormalStep
- **Admin reset**: counter keys not yet cleared by `vsg_reset` — deferred to Phase 07
- **Test entry in guidance.yaml**: `test_counter_stone` — pick up Wood to activate; 3 Stone pickups fires message
