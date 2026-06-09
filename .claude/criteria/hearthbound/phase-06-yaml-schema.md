# Phase 06 — YAML Schema Additions

**Status:** `done`
**Depends on:** CRIT-01 (existing schema), CRIT-02 (existing TriggerSpec)
**Blocks:** Phase 02, Phase 03, Phase 09

All new YAML fields required by the Hearthbound guide plan. Changes land in
`src/Config/GuidanceConfig.cs`. The YAML lib is YamlDotNet 16.3.0 with
`UnderscoredNamingConvention` — all field names are `snake_case` in YAML.

---

## Updated `GuidanceEntry`

```csharp
public class GuidanceEntry
{
    // --- existing fields (unchanged) ---
    public string Id;
    public TriggerSpec Trigger;
    public DisplaySpec Display;
    public string Message;
    public string FireMode;   // once_per_player | once_global | always | once_per_session

    // --- new fields ---
    public string Title;              // Human-readable name shown in Codex and HUD tracker
    public string Category;          // Mod/group label for Codex left-panel grouping
    public List<string> Requires;    // Entry IDs that must be complete before this fires
    public List<GuidanceStep> Steps; // Ordered steps; null/empty = single-entry behavior (unchanged)
    public string Sound;             // Optional vanilla SFX prefab name played on fire
    public bool DiscordOnComplete;   // Phase 08: POST Discord webhook when chain completes
}
```

---

## New Class: `GuidanceStep`

```csharp
public class GuidanceStep
{
    public TriggerSpec Trigger;          // When this step activates
    public DisplaySpec Display;          // How this step is shown
    public string Message;               // Text shown to the player
    public int ProgressGoal;             // 0 = no counter; >0 = must occur N times
    public TriggerSpec ProgressTrigger;  // What event is counted toward ProgressGoal
    public string ProgressLabel;         // HUD label for the counter, e.g. "Trophies"
}
```

---

## Updated `TriggerSpec`

```csharp
public class TriggerSpec
{
    // --- existing fields (unchanged) ---
    public string Type;       // craft | kill | item_acquired | build | biome | equip | distance | ...
    public string Item;       // for craft / item_acquired / equip / npc_item_submit
    public string Creature;   // for kill / boss_defeated
    public string Piece;      // for build
    public string Biome;      // for biome

    // --- new fields ---
    public string Npc;        // for npc_interacted / npc_conversation / npc_item_submit
    public string Location;   // for location_entered / distance (wildcard suffix supported)
    public float  Radius;     // for distance: metres (default 50 when 0)
    public string Skill;      // for skill_level (matches Skills.SkillType name)
    public int    Level;      // for skill_level (threshold, e.g. 25)
    public string Interval;   // for timed: "daily" | "hourly" | int-as-string (seconds)
    public string Id;         // for timed: stable identifier matched as Subject
    public int    MaxFires;   // optional cap on total fires per player (0 = unlimited)
}
```

---

## Category Values (Standardized)

Use these exact strings in `category:` to ensure consistent Codex grouping:

| Category | Mods |
|---|---|
| `Companions` | Offline Companions, Wandering Companions |
| `Trading` | TraderOverhaul, HaldorBounties, SimpleMarket |
| `Building` | ProtectiveWards, Armoire, balrond_shipyard |
| `Skills` | SlayerSkills, ImpactfulSkills |
| `Exploration` | More World Locations AIO, ZenBossStone |
| `Inventory` | Quick Stack / Store, ComfyQuickSlots, Recycle N Reclaim, AzuCraftyBoxes |
| `Groups` | Groups |
| `General` | Cross-mod tips, recovery tips, general gameplay guidance |

---

## Full YAML Example

```yaml
guidance:
  - id: slayer_skills_chain
    title: "Slayer Skills Guide"
    category: Skills
    fire_mode: once_per_player
    requires: []
    sound: "sfx_build_cultivator"
    discord_on_complete: true
    steps:
      - trigger:
          type: item_acquired
          item: "Trophy_*"
        display:
          mode: raven
        message: "You've collected a trophy! SlayerSkills rewards hunters who specialize. Keep hunting to unlock powerful buffs."

      - trigger:
          type: first_login
        progress_trigger:
          type: item_acquired
          item: "Trophy_*"
        progress_goal: 5
        progress_label: "Trophies"
        display:
          mode: hud
        message: "You've collected 5 trophies. Visit your Skills menu to see your first Slayer buff unlocked."

      - trigger:
          type: skill_level
          skill: "Swords"
          level: 25
        display:
          mode: notification
        message: "Your Slayer skill is growing. Specializing in one weapon type amplifies your trophy bonuses."

      - trigger:
          type: skill_level
          skill: "Swords"
          level: 50
        display:
          mode: raven
        message: "Mastery achieved. Your trophy collection and weapon focus now grant significant combat advantages."
```

---

## Validation Rules

Enforced in `GuidanceConfigLoader.cs` on load:

- `id` must be non-empty and unique across all entries.
- If `steps` is non-empty, the top-level `trigger`, `display`, and `message` fields are ignored (log a warning if present).
- Each step must have a non-null `trigger` and `message`.
- `requires` entries that reference unknown IDs log a warning on load.
- `progress_goal > 0` requires a non-null `progress_trigger`; log a warning and treat as 0 if missing.
- `category` should match one of the standardized values above; unrecognized values are accepted but logged.
- `sound` is optional; if set, the prefab name is validated against known vanilla SFX on load (log warning if not found).

---

## Criteria

- [x] All new fields deserialize correctly from YAML using `UnderscoredNamingConvention`.
- [x] `steps: []` or absent `steps` preserves existing single-entry behavior with no regressions.
- [x] `requires: []` or absent `requires` is treated as no prerequisites (always eligible).
- [x] Duplicate `id` values cause the second entry to be skipped with a logged error.
- [x] `progress_goal: 0` (or absent) in a step skips counter logic entirely.
- [x] All validation warnings/errors are written to the BepInEx log; they never throw exceptions.
- [x] `GuidanceConfigLoader` reloads correctly via `FileSystemWatcher` when the YAML is updated (hot-reload).
- [x] Existing YAML files that omit all new fields continue to load and fire without modification.
