# Phase 02 — Sequential Guide Chains

**Status:** `complete`
**Depends on:** Phase 01 (new triggers), Phase 06 (YAML schema `steps:` field)
**Blocks:** Phase 03, Phase 04, Phase 05, Phase 07, Phase 08, Phase 09

Adds the concept of ordered, multi-step guide journeys. Step N only becomes active after
Step N-1 fires and is acknowledged. This is the core architecture that enables quest-like
progression guides for the Hearthbound modpack.

---

## Concepts

### GuidanceChain
A `GuidanceEntry` with a `steps:` list. Each step is a self-contained sub-entry with its
own `trigger`, `display`, and `message`. Steps are advanced in order.

### ChainState
Per-player state recording which step of each chain the player is currently on. Stored in
`m_customData` alongside existing SeenTracker data.

### Prerequisites
A chain (or individual entry) may declare `requires: [entry_id, ...]`. The dispatcher will
not activate the chain until all listed entry IDs are marked complete in the player's state.

---

## YAML Shape

```yaml
- id: companions_offline_chain
  category: Companions
  title: "Offline Companions Guide"
  requires: []                      # optional: list of entry IDs that must be complete first
  steps:
    - trigger:
        type: npc_interacted
        npc: Haldor
      display:
        mode: raven
      message: "You can hire NPC companions! Talk to a Companion Recruiter to bring one along."
    - trigger:
        type: item_acquired
        item: "CookedMeat"
      display:
        mode: hud
      message: "Your companion needs food. Open their inventory and place food in their food slots."
    - trigger:
        type: timed
        interval: 120
        id: companion_gear_reminder
      display:
        mode: notification
      message: "Companions can equip gear just like you. Armor and weapons improve their combat."
    - trigger:
        type: timed
        interval: 300
        id: companion_ai_tip
      display:
        mode: hud
      message: "Configure your companion's AI stance (Follow / Guard / Roam) in their menu."
    - trigger:
        type: first_login       # fires on next login after previous steps complete
      display:
        mode: raven
      message: "You're a seasoned companion keeper. Check /vsg_guide for all companion tips."
```

---

## New Files

### `src/State/ChainState.cs`
Stores and serializes per-player chain progress.

```csharp
public class ChainState
{
    // entry_id -> index of the NEXT step to fire (0 = chain not started)
    public Dictionary<string, int> StepProgress;

    // entry_ids that are fully complete (all steps fired)
    public HashSet<string> CompletedChains;

    public void Save(Player player);        // serialize to m_customData key "vsg_chain"
    public static ChainState Load(Player player);  // deserialize from m_customData
}
```

### `src/State/PrerequisiteChecker.cs`
Evaluates whether all `requires:` entries are complete for a given player.

```csharp
public static class PrerequisiteChecker
{
    public static bool AllSatisfied(GuidanceEntry entry, ChainState state, SeenTracker seen);
}
```

---

## Changes to Existing Files

### `src/Triggers/GuidanceDispatcher.cs`

Replace flat `Matches + Fire` logic with chain-aware dispatch:

1. On `Raise(TriggerEvent evt)`:
   a. Load `ChainState` for local player.
   b. For each `GuidanceEntry` in loaded config:
      - If entry has no `steps:` list → existing single-entry logic (unchanged).
      - If entry has `steps:` list:
        - Check prerequisites via `PrerequisiteChecker.AllSatisfied`.
        - Get current step index from `ChainState.StepProgress[entry.Id]` (default 0).
        - If current step's trigger matches `evt` → fire that step's display/message.
        - Advance `StepProgress[entry.Id]` to next step.
        - If all steps are done → move entry to `ChainState.CompletedChains`.
        - Save updated `ChainState` to `m_customData`.
        - Sync updated chain state to server via `GuidanceSync` RPC.

2. Chain step matching uses the same `Matches(TriggerSpec, TriggerEvent)` logic as single entries.

### `src/Net/GuidanceSync.cs`

Add new RPC: `vsg_SyncChainState`
- **Direction:** Client → Server (update server's record of player chain progress)
- **Payload:** serialized `ChainState` (JSON or custom ZPackage)
- **Server stores:** `Dictionary<long, ChainState>` keyed by player UID
- **Server → Client sync:** when player reconnects, server pushes their stored chain state back

### `src/Config/GuidanceConfig.cs`

Add to `GuidanceEntry`:

```csharp
public string Title;               // human-readable chain title (for Codex UI)
public string Category;            // mod/group label
public List<string> Requires;      // prerequisite entry IDs
public List<GuidanceStep> Steps;   // ordered list; null/empty = single-entry behavior
```

Add new class `GuidanceStep`:

```csharp
public class GuidanceStep
{
    public TriggerSpec Trigger;
    public DisplaySpec Display;
    public string Message;
    public int ProgressGoal;        // 0 = no counter (Phase 03)
    public TriggerSpec ProgressTrigger; // counter trigger (Phase 03)
}
```

---

## State Serialization Format

Stored under `m_customData` key `"vsg_chain_v1"` as JSON:

```json
{
  "progress": {
    "companions_offline_chain": 2,
    "zen_boss_chain": 1
  },
  "completed": ["quickstack_intro", "comfy_slots_intro"]
}
```

---

## Criteria

- [x] Single-entry guides (no `steps:`) behave identically to current behavior.
- [x] Step 0 of a chain only activates after prerequisites are all in `completed`.
- [x] Step N only activates after Step N-1 has fired.
- [x] `ChainState` is persisted in `m_customData` and survives session end.
- [x] Chain state is synced to the server on every step advance.
- [x] On player reconnect, server pushes stored chain state back to the client.
- [x] A chain with all steps fired is moved to `completed` and never re-fires.
- [x] `requires:` referencing a non-existent entry ID logs a warning and is treated as unsatisfied.
- [ ] Two chains with the same `id` in YAML logs an error; second entry is ignored. _(deferred — YamlDotNet loads last-wins; duplicate detection deferred to Phase 10 polish)_
- [x] Chain step display respects the step's own `display.mode`, not the parent entry's.

## Implementation Notes (2026-06-04)

- `src/State/ChainState.cs` — static helper; `VSG.cp.{chainId}` = next step index, `VSG.cd.{chainId}` = "1" done.
- `src/State/PrerequisiteChecker.cs` — checks `ChainState.IsComplete` **and** `SeenTracker.HasFired`; logs warning for unknown IDs.
- `GuidanceDispatcher.HandleChain` — chain branch in `Raise`; extracts `HandleNormalStep` / `FireStepDisplay` / `AdvanceChain` helpers.
- `GuidanceSync` — `VSG_ChainStepUpdate` (client→server), `VSG_ChainStateRequest` (client→server on spawn), `VSG_ChainStatePush` (server→client reconnect push); `PlayerSpawnedPatch` triggers request.
- Raven step key: `entry.Id + "_s" + stepIndex` prevents `Tutorial.m_texts` collision across steps.
- Counter key encoding: `{chainId}:{stepIndex}=value` piggybacked on `VSG_ChainStepUpdate` RPC.
- Test entry `test_chain_two_step` in `guidance.yaml`: craft SwordBronze → craft ArrowFlint.
