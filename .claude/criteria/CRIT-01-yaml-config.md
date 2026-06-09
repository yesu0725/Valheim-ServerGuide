# CRIT-01 — YAML Config Schema

**File:** `src/Config/GuidanceConfig.cs`
**Loader:** `src/Config/GuidanceConfigLoader.cs`
**Path on disk:** `BepInEx/config/ValheimServerGuide/guidance.yaml`

---

## Top-Level Shape

```yaml
guidances:
  - id: string                  # required; unique key for this entry
    trigger: TriggerSpec
    display: DisplaySpec
    once: bool                  # default: true
    cooldown: float             # seconds; default: 0 (disabled)
    requires: [string]          # list of ids that must have fired first
    stop_when: [string]         # list of ids; if any fired, this entry won't fire
    scope: string               # "player" (default) | "global"
    summary: string             # optional — short recap shown in Codex body when entry is complete;
                                # takes priority over the final step's message
    announce:
      discord: string           # null/absent=off, ""=use default template, else literal template
```

---

## TriggerSpec Fields

| Field | Type | Used by trigger type |
|---|---|---|
| `type` | string | all (required) |
| `item` | string | `craft`, `item_acquired`, `equip`, `npc_item_submit`, `chest_opened` |
| `creature` | string | `kill`, `boss_defeated` |
| `piece` | string | `build` |
| `biome` | string | `biome` |
| `location` | string | `distance`, `location_entered` — supports trailing `*` wildcard |
| `radius` | float | `distance` — metres; default 50 when omitted |
| `skill` | string | `skill_level` |
| `level` | int | `skill_level` |
| `damage_type` | string | `damage_type` |

All string matching is **case-insensitive** in the dispatcher.

---

## DisplaySpec Fields

| Field | Type | Default | Notes |
|---|---|---|---|
| `mode` | string | `"raven"` | `raven` \| `message` \| `chat` \| `rune` \| `intro` |
| `topic` | string | — | Header/label shown in raven & rune/intro viewers |
| `text` | string | — | Body text; supports template tokens (see CRIT-13) |
| `position` | string | `"TopLeft"` | `TopLeft` \| `Center` — only used by `message` mode |

---

## Valid Categories

`category` must be one of: `Companions`, `Trading`, `Building`, `Skills`, `Exploration`, `Inventory`, `Groups`, `General`.
Unknown values are accepted but log a warning. Empty/absent category is valid.

---

## Naming Convention

YamlDotNet is configured with `UnderscoredNamingConvention` and `IgnoreUnmatchedProperties`.
YAML keys use `snake_case`; C# properties use `PascalCase`.
Example: YAML `stop_when` → C# `StopWhen`.

---

## Starter YAML

On first run the loader writes a starter `guidance.yaml` with a single arrows-tutorial example entry so the admin can see the format without consulting docs.

---

## Loader Behavior

- `GuidanceConfigLoader` wraps a `FileSystemWatcher` on the YAML file.
- Changes are debounced by **500 ms** before triggering a reload.
- On reload, `ConfigChanged` event fires → `Plugin.OnConfigChanged` → updates `Plugin.CurrentConfig`, re-registers tutorials, and (if server) broadcasts to clients.
- Server authority guard: a client's local YAML change is silently ignored if `ZNet.instance.IsServer()` is false.

---

## Criteria

- [ ] Every entry MUST have a non-empty, unique `id`.
- [ ] `trigger.type` is required; all other TriggerSpec fields are optional and ignored when not applicable to the trigger type.
- [ ] `display.mode` defaults to `"raven"` when absent.
- [ ] `once: true` (default) prevents re-firing after the first trigger.
- [ ] `cooldown` and `once` are independent: `once: false, cooldown: 60` fires repeatedly but no faster than every 60 seconds.
- [ ] `requires` and `stop_when` always check **player-scope** state regardless of the entry's own `scope`.
- [ ] The loader must NOT run on a pure client (one that joined a dedicated server). Only the server/host runs the loader.
- [ ] After a hot-reload the new config must be pushed to all connected clients via RPC.
