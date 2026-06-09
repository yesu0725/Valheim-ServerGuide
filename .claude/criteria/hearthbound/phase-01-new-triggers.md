# Phase 01 — New Triggers

**Status:** `complete`
**Depends on:** CRIT-02 (existing trigger architecture)
**Blocks:** Phase 02, Phase 03, Phase 09

Expand the trigger system with all event types required to deliver the Hearthbound mod guides.
Each new trigger follows the same pattern as `CraftTrigger.cs` and `KillTrigger.cs`.

---

## New Trigger Types Required

### `first_login`
- **File:** `src/Triggers/FirstLoginTrigger.cs`
- **Harmony patch:** `Player.OnSpawned` — Postfix
- **Guard:** Check `SeenTracker` for a `"first_login_fired"` key in `m_customData`; only raise if absent, then set it.
- **Subject:** `""` (no subject; matched by type alone)
- **Used by:** Groups intro, Wandering Companions encounter warning

### `chest_opened`
- **File:** `src/Triggers/ChestOpenedTrigger.cs`
- **Harmony patch:** `Container.Interact` — Postfix (or `InventoryGui.Show` when `m_currentContainer != null`)
- **Guard:** Only fire once per player (SeenTracker key `"chest_opened_fired"`)
- **Subject:** `""` (no subject filter needed for one-shot)
- **Used by:** Quick Stack / Store / Sort / Trash / Restock intro

### `boss_defeated`
- **File:** `src/Triggers/BossDefeatedTrigger.cs`
- **Harmony patch:** `Character.OnDeath` — Postfix (reuse KillTrigger patch class or add sibling)
- **Guard:** `__instance.IsBoss()` must be true; `m_lastHit.GetAttacker() == Player.m_localPlayer` (participating player)
- **Subject:** prefab name of the boss with `"(Clone)"` stripped (e.g., `"Eikthyr"`, `"gd_king"`, `"Bonemass"`, `"Dragon"`, `"GoblinKing"`)
- **YAML field:** `trigger.creature` (same as kill — boss is also a creature)
- **Used by:** ZenBossStone chain steps 2–6

### `skill_level`
- **File:** `src/Triggers/SkillLevelTrigger.cs`
- **Harmony patch:** `Skills.RaiseSkill` — Postfix
- **Guard:** Compare previous level (captured before raise) to new level; fire if a threshold defined in config is crossed.
- **Subject:** `"{SkillType}:{level}"` e.g., `"Swords:25"` — dispatcher matches `trigger.skill` and `trigger.level`
- **YAML fields:** `trigger.skill` (string, matches `Skills.SkillType` name), `trigger.level` (int threshold)
- **Used by:** ImpactfulSkills chain (25 / 50 / 75 / 100 per skill), SlayerSkills milestone

### `item_acquired`
- **File:** `src/Triggers/ItemAcquiredTrigger.cs`
- **Harmony patch:** `Humanoid.Pickup` — Postfix (or `Inventory.AddItem`)
- **Subject:** item prefab name with `"(Clone)"` stripped
- **YAML field:** `trigger.item` (supports `*` wildcard suffix, e.g., `"Trophy_*"` matches any trophy)
- **Used by:** SlayerSkills (first trophy), HaldorBounties (reward item), Offline Companions (food)

### `location_entered`
- **File:** `src/Triggers/LocationEnteredTrigger.cs`
- **Harmony patch:** `ZoneSystem.OnLocationFound` or `Minimap.ShowPointOnMap` (whichever fires on player proximity discovery)
- **Subject:** location prefab/type name (e.g., `"WL_Port_01"`, `"WL_Shrine_01"`)
- **YAML field:** `trigger.location` (supports prefix wildcard `"WL_*"` to match any More World Locations POI)
- **Used by:** More World Locations AIO chain

### `npc_interacted`
- **File:** `src/Triggers/NpcInteractedTrigger.cs`
- **Harmony patch:** `StoreGui.Show` — Postfix (covers all traders: Haldor, Hildir, Bog Witch)
- **Subject:** the trader NPC prefab name (e.g., `"Haldor"`, `"Hildir"`, `"BogWitch"`)
- **YAML field:** `trigger.npc`
- **Used by:** TraderOverhaul chain, HaldorBounties intro, SimpleMarket, Offline Companions hire

### `timed`
- **File:** `src/Triggers/TimedTrigger.cs`
- **Implementation:** Server-side coroutine (`IEnumerator`) started in `Plugin.Awake`; sends RPC to all clients at configured intervals.
- **Subject:** the `trigger.id` value from YAML (e.g., `"daily_bounty_reminder"`)
- **YAML fields:** `trigger.interval` (`"daily"` | `"hourly"` | seconds int), `trigger.id`
- **Constraint:** Only fires on a dedicated server or host; clients receive via `GuidanceSync` RPC.
- **Used by:** HaldorBounties daily reminder

### `player_death`
- **File:** `src/Triggers/PlayerDeathTrigger.cs`
- **Harmony patch:** `Player.OnDeath` — Postfix
- **Subject:** `""` (no subject; fires on any local player death)
- **Guard:** Optionally limited by config `trigger.max_fires` to avoid spam
- **Used by:** Recovery tips, companion recovery guidance

### `biome`
- **File:** `src/Triggers/BiomeTrigger.cs`
- **Harmony patch:** `Player.Update` — Postfix; poll every **2 seconds** (`_nextCheck` float guard)
- **Reset patch:** `Player.OnSpawned` — Postfix; resets `_lastBiome` to `Heightmap.Biome.None` on spawn
- **Subject:** `Heightmap.Biome.ToString()` (e.g., `"BlackForest"`, `"Swamp"`)
- **YAML field:** `trigger.biome` — case-insensitive match via `Eq()`
- **Guard:** Fires only when `biome != _lastBiome` and `biome != None`; SeenTracker marks per-character if `once: true`
- **Used by:** TraderOverhaul biome discovery hints

### `distance`
- **File:** `src/Triggers/DistanceTrigger.cs`
- **Harmony patch:** `Player.Update` — Postfix; poll every **5 seconds** (`_nextCheck` float guard)
- **Subject:** `loc.m_location.m_prefabName` (ZoneSystem location prefab name)
- **YAML fields:** `trigger.location` (supports trailing `*` wildcard), `trigger.radius` (float, default 50 m)
- **Guard:** Per-location SeenTracker key `"dist_{prefabName}"` — fires at most once per location per character
- **Config pre-scan:** Dispatcher pre-scans all entries to get the configured radius per location before firing
- **Used by:** Companions intro (approach Haldor's camp)

---

## Dispatcher Changes (`GuidanceDispatcher.cs`)

Add new cases to `Matches` switch:

```csharp
"first_login"     -> no subject filter (type match only)
"chest_opened"    -> no subject filter
"boss_defeated"   -> trigger.creature matches evt.Subject (reuse kill path)
"skill_level"     -> trigger.skill matches evt.Subject skill part; trigger.level <= evt.Subject level part
"item_acquired"   -> trigger.item matches evt.Subject (wildcard suffix support via StartsWith)
"location_entered"-> trigger.location matches evt.Subject (wildcard prefix support)
"npc_interacted"  -> trigger.npc matches evt.Subject
"timed"           -> trigger.id matches evt.Subject
"player_death"    -> no subject filter
"biome"           -> trigger.biome matches evt.Subject (Eq, case-insensitive)
"distance"        -> WildcardMatch(trigger.location, evt.Subject); radius check happens in DistanceTrigger before Raise()
```

---

## TriggerSpec Additions (`GuidanceConfig.cs`)

```csharp
public string Npc;        // for npc_interacted
public string Location;   // for location_entered
public string Skill;      // for skill_level
public int    Level;      // for skill_level threshold
public string Interval;   // for timed: "daily" | "hourly" | int seconds
public string Id;         // for timed: stable identifier
public int    MaxFires;   // optional: cap total fires per player
```

---

## Source Layout After Phase 01

```
src/Triggers/
├── GuidanceDispatcher.cs      (updated: new cases in Matches)
├── CraftTrigger.cs            (existing)
├── KillTrigger.cs             (existing)
├── FirstLoginTrigger.cs       (new)
├── ChestOpenedTrigger.cs      (new)
├── BossDefeatedTrigger.cs     (new)
├── SkillLevelTrigger.cs       (new)
├── ItemAcquiredTrigger.cs     (new)
├── LocationEnteredTrigger.cs  (new)
├── NpcInteractedTrigger.cs    (new)
├── TimedTrigger.cs            (new)
├── PlayerDeathTrigger.cs      (new)
├── BiomeTrigger.cs            (new)
└── DistanceTrigger.cs         (new)
```

---

## Criteria

- [x] `first_login` fires exactly once per player across all sessions (persisted in `m_customData`).
- [x] `chest_opened` fires exactly once per player.
- [x] `boss_defeated` only fires for `IsBoss() == true` characters killed by local player.
- [x] `boss_defeated` subject matches the same prefab names ZenBossStone uses.
- [x] `skill_level` fires at each configured threshold exactly once per skill per player.
- [x] `item_acquired` wildcard (`Trophy*`) matches any prefab starting with `"Trophy"`.
- [x] `location_entered` fires at most once per location prefab per player (SeenTracker keyed by location name).
- [x] `npc_interacted` subject correctly identifies traders by prefab name.
- [x] `timed` events originate server-side only; clients receive via existing RPC channel.
- [x] `player_death` respects `max_fires` cap if configured.
- [x] All new trigger types are documented in CRIT-02.
- [x] All subject matching remains case-insensitive.
- [x] `biome` fires on every new biome entry (per-character once if `once: true`); resets `_lastBiome` on spawn so biome fires again after death/respawn.
- [x] `distance` fires at most once per named location per character (SeenTracker `"dist_{prefabName}"`).
- [x] `distance` radius defaults to 50 m when `trigger.radius` is absent or 0.

## Notes (post-implementation)
- `ItemAcquiredTrigger` patches `Humanoid.Pickup(GameObject go, ...)` — not `item` (wrong param name).
- Raven-mode display required two Harmony patches to bypass the vanilla tutorials-disabled gate:
  `RavenGetBestTextPatch` (forces our temp text to win selection) and `RavenSpawnBypassPatch`
  (temporarily flips `m_tutorialsEnabled` for the Spawn call). See CRIT-11.
- `LocationEnteredTrigger` polls `Player.Update` every 5 s with 40 m radius against
  `ZoneSystem.instance.m_locationInstances`.
