# CRIT-01 — YAML Config Schema

**File:** `src/Config/GuidanceConfig.cs`
**Loader:** `src/Config/GuidanceConfigLoader.cs`
**Path on disk:** `BepInEx/config/ValheimServerGuide/` — any `*.yaml`/`*.yml` in this folder or its subfolders (starter file: `guidance.yaml`)

---

## Top-Level Shape

```yaml
# Optional. Server-wide highlight rules — see CRIT-25. Unlike `tracker:`, the lists from
# every loaded YAML file are CONCATENATED rather than first-file-wins.
highlight:
  - text: string                # phrase to highlight (use this or `any`)
    any: [string]               # several phrases sharing one style
    color: string               # "#RRGGBB" / "#RRGGBBAA", leading '#' optional
    style: string               # Bold | Italic | Underline | Strikethrough, combinable
    size_percent: float         # span font size as % of surrounding text; 0 = unchanged
    first: bool                 # only the first occurrence; default false (all)
    match_case: bool            # default false
    whole_word: bool            # default: auto — word-bounded when the phrase starts and
                                # ends with a letter/digit, literal substring otherwise

guidances:
  - id: string                  # required; unique key for this entry
    trigger: TriggerSpec
    display: DisplaySpec
    once: bool                  # default: true
    cooldown: float             # seconds; default: 0 (disabled)
    requires: [string]          # list of ids that must have fired first
    stop_when: [string]         # list of ids; if any fired, this entry won't fire
    scope: string               # "player" (default) | "global"
    description: string         # optional — the objective, in a line or two ("Cull 15 Greylings.")
                                # Shown under the row in the F10 tracker's Expand Full view and as
                                # the row's hover tooltip. Chain entries normally carry this per
                                # step; the entry-level value is the fallback, and the ONLY source
                                # for non-chain quests (kill counts, item submits, collections).
    summary: string             # optional — short recap shown in Codex body when entry is complete;
                                # takes priority over the final step's message
    highlight: [HighlightSpec]  # optional — same shape as the top-level block, scoped to this
                                # entry. Runs before the server-wide rules. Steps may carry
                                # their own list, which outranks the entry's. See CRIT-25.
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

- `GuidanceConfigLoader` wraps a `FileSystemWatcher` on the config folder.
- **Recursive multi-file merge (v0.8.0):** every `*.yaml` / `*.yml` file under
  `BepInEx/config/ValheimServerGuide/` — including in nested subfolders at any depth
  (`SearchOption.AllDirectories`, watcher `IncludeSubdirectories = true`) — is deserialized and its
  `guidances` concatenated into one config. Files are processed in ascending order of their path
  **relative to the config root** (case-insensitive), so duplicate-id resolution and `tracker:`
  precedence are deterministic. A single malformed file is logged and skipped without blanking the rest.
- Duplicate `id`s across files are dropped by `Validate` (first occurrence, by relative-path order, wins).
- The `tracker:` section is taken from the first file (by relative-path order) that defines one.
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
- [ ] Every `*.yaml`/`*.yml` under the config folder, at any subfolder depth, is merged; edits in subfolders trigger a live reload.
- [ ] File merge order is by path relative to the config root (case-insensitive); duplicate ids and `tracker:` resolve first-wins by that order.
