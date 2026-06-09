# Phase 10 — QoL Polish

**Status:** `done`
**Depends on:** All previous phases complete
**Blocks:** nothing (final phase)

Refinements applied across the whole system after content is live and in player hands.
Each item here is independent; they can be done in any order.

---

## 10-A: Guide Versioning

Allow guides to be updated without losing player progress, while still re-delivering
changed messages to players who already completed a step.

**YAML field:**
```yaml
- id: companions_offline_chain
  version: 2          # bump when step messages change meaningfully
```

**Behavior:**
- `ChainState` stores `{entry_id: {step: N, version: V}}` alongside progress.
- On config load, if `GuidanceEntry.Version > stored version` for a completed step,
  re-fire that step's message on next login (display mode: `notification`).
- Do not reset the chain — only re-deliver updated message text.

**Files:** `GuidanceConfig.cs` (add `Version` field), `ChainState.cs` (store version per entry), `GuidanceDispatcher.cs` (version check on load).

---

## 10-B: Text Template Variables

Extend `GuidanceDisplay.cs` to expand placeholders in message text before display.

| Variable | Expands To |
|---|---|
| `{player_name}` | `Player.m_localPlayer.GetPlayerName()` |
| `{biome}` | Current biome display name |
| `{skill}` | Skill name from the triggering `skill_level` event |
| `{level}` | Skill level from the triggering `skill_level` event |
| `{step}` | Current step number (1-based) |
| `{total}` | Total steps in the chain |

Example message: `"Well done, {player_name}! Step {step} of {total} complete."`

**File:** `src/Display/GuidanceDisplay.cs` — add `ExpandTemplates(string message, TriggerEvent evt)` called before all display paths.

See also CRIT-13.

---

## 10-C: Sound Cues

Play a vanilla SFX when a guide fires, configurable per entry via the `sound:` YAML field
added in Phase 06.

**Implementation:**
```csharp
if (!string.IsNullOrEmpty(entry.Sound))
{
    Player.m_localPlayer.GetComponent<AudioSource>()
          ?.PlayOneShot(ZNetScene.instance.GetPrefab(entry.Sound)
                                 ?.GetComponent<AudioSource>()?.clip);
}
```

Or simpler: use `ZSFX` / `ZSFXRef` vanilla sound helpers if available.

**Default sound per display mode (if `sound:` is absent):**

| Mode | Default Sound |
|---|---|
| `raven` | existing raven audio (unchanged) |
| `hud` | `sfx_build_cultivator` |
| `notification` | `sfx_new_message` (if exists) |
| `chat` | none |
| `intro` | existing intro audio (unchanged) |

**File:** `src/Display/GuidanceDisplay.cs`.

---

## 10-D: Guide Version Stamp in `vsg_progress`

When displaying progress via `vsg_progress`, include the config version for each chain
so admins can tell whether a player has seen the latest version of a guide.

Output addition:
```
  Skills / Slayer Skills Guide  — Complete ✓  (v2, seen v1 — will refresh on next login)
```

---

## 10-E: `+N more` Overflow Polish in HUD Tracker

When active chains exceed `TrackerMaxVisible`, show a click-hint:

```
  ▸ Offline Companions   2/5
  ▸ ZenBossStone         3/6
  ▸ Slayer Skills        ██░░
  + 2 more — press F2 for Codex
```

The `F2` reference is pulled from the configured `CodexKey` BepInEx value so it stays accurate if the key is rebound.

**File:** `src/Display/GuidanceHudTracker.cs`.

---

## 10-F: Config Reload Notification

When `GuidanceConfigLoader` hot-reloads the YAML (via `FileSystemWatcher`), show a brief
`notification`-mode message to all local admins:

```
[VSG] Guide config reloaded — 17 entries loaded.
```

Visible only to players with admin privileges (`SynchronizationManager.Instance.PlayerIsAdmin`).

**File:** `src/Config/GuidanceConfigLoader.cs`.

---

## Criteria

- [x] **10-A:** Version bump re-delivers changed step messages without resetting chain progress.
- [x] **10-A:** Entries without a `version:` field default to version `1`; no regression for existing YAML.
- [x] **10-B:** All template variables expand correctly; unknown `{variables}` are left as-is (no error).
- [x] **10-B:** Template expansion happens before all five display modes. See CRIT-13.
- ~~[ ] **10-C:** `sound:` prefab name is validated on load; invalid names log a warning and play no sound.~~ — **Dropped:** sound cue system removed; completion audio is handled by the existing level-up VFX (`SpawnCompletionVfx`). The `sound:` YAML field is retained as a dormant/reserved field.
- ~~[ ] **10-C:** Sound cues do not play during the intro cinematic. See CRIT-07.~~ — **Dropped** (same reason).
- [x] **10-D:** Version stamp only appears in `vsg_list` output when stored version differs from current config version. (`vsg_progress` does not exist yet; stamp added to `vsg_list`.)
- [x] **10-E:** `+N more` hint uses the live value of `CodexKey` from BepInEx config.
- [x] **10-F:** Config reload notification is only shown to admin players; non-admins see nothing.
