# Phase 04 — Objective Tracker HUD Widget

**Status:** `complete` (baseline) — sub-phases 04a–04e pending
**Depends on:** Phase 02 (ChainState), Phase 03 (progress counters)
**Blocks:** Phase 09 (content relies on tracker being visible)

Adds a persistent on-screen widget that shows the player's active guide chains and their
current step progress. Uses vanilla UI components only (no custom assets). See CRIT-14.

---

## Sub-Phase Index

| Sub-Phase | File | Status | Delivers |
|---|---|---|---|
| 04a | [Foundation Fixes](phase-04a-foundation-fixes.md) | `pending` | Font warning fix; tracker visible on login |
| 04b | [Hotkey Toggle & Badge](phase-04b-hotkey-toggle.md) | `pending` | F9 toggle; hint badge; empty state; cursor; click-outside |
| 04c | [Auto-Show, Fade & Highlight](phase-04c-auto-show-fade-highlight.md) | `pending` | Auto-show on progress; smooth fade-out; gold row highlight |
| 04d | [Hover Tooltips](phase-04d-hover-tooltips.md) | `pending` | Per-step `description` field; tooltip on row hover |
| 04e | [Progress Bars & Polish](phase-04e-progress-bars-polish.md) | `pending` | ASCII fill bars; completion flash; badge count; SFX |

Sub-phases must be completed in order (04a → 04b → 04c → 04d → 04e) due to shared state
machine dependencies (`_manuallyOpened`, `fromProgress`, fade infrastructure).

---

## Visual Design

```
┌─────────────────────────────┐
│ GUIDES                      │
│ ▸ Offline Companions  2/5   │
│ ▸ Slayer Skills       ██░░  │  ← progress bar for counter steps
│ ▸ ZenBossStone        3/6   │
└─────────────────────────────┘
```

- Position: top-right corner of the screen (configurable via BepInEx config).
- Max visible chains: 3 (configurable; overflow hidden with `+N more` label).
- Chain title sourced from `GuidanceEntry.Title`.
- Step progress shown as `current/total` for chain steps.
- Counter steps shown as `current/goal` for the active progress counter.
- Font: vanilla `AveriaSerifLibre-Regular` (same as Valheim HUD text).
- Background: semi-transparent vanilla panel sprite (reuse `woodpanel` or `bkg_small`).

---

## New File: `src/Display/GuidanceHudTracker.cs`

```csharp
public class GuidanceHudTracker : MonoBehaviour
{
    public static GuidanceHudTracker Instance { get; private set; }

    // Called by GuidanceDispatcher after every ChainState change
    public void Refresh(ChainState state, List<GuidanceEntry> config);

    private void BuildPanel();   // instantiates vanilla UI under Hud.instance.transform
    private void UpdateRows();   // updates text/progress for each visible active chain
}
```

### Harmony Patch

```csharp
[HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
static class HudAwakePatch
{
    static void Postfix(Hud __instance)
    {
        var go = new GameObject("VSG_TrackerPanel");
        go.transform.SetParent(__instance.transform, false);
        GuidanceHudTracker.Instance = go.AddComponent<GuidanceHudTracker>();
        GuidanceHudTracker.Instance.BuildPanel();
    }
}
```

---

## BepInEx Config Keys

| Key | Default | Description |
|---|---|---|
| `TrackerEnabled` | `true` | Show/hide the tracker widget |
| `TrackerPosition` | `TopRight` | `TopRight` / `TopLeft` / `BottomRight` / `BottomLeft` |
| `TrackerMaxVisible` | `3` | Max active chains shown simultaneously |

---

## Refresh Trigger Points

`GuidanceHudTracker.Refresh()` is called from:

1. `GuidanceDispatcher` — after every step advance or counter increment.
2. `GuidanceSync` — after server pushes restored chain state on reconnect.
3. `Plugin.Awake` — after initial config load (to show any in-progress chains from last session).

---

## Criteria

- [x] Widget uses only vanilla UI components and sprites. No custom assets. See CRIT-14.
- [x] Widget is invisible when there are no active (in-progress) chains.
- [x] Widget updates immediately after every step advance or counter change.
- [x] Completed chains are removed from the widget immediately on completion.
- [x] `TrackerEnabled = false` hides the widget entirely (no GameObject destroyed, just inactive).
- [x] Max visible count is respected; overflow shows `+N more` using vanilla text.
- [x] Counter step rows display `N / Goal` and update in real time.
- [x] Widget does not appear during the intro cinematic. See CRIT-07.
- [x] Widget does not overlap the vanilla death screen or the map screen.
- [x] Position config applies on next `Hud.Awake` (session restart); no hot-reload required.

---

## Implementation Notes (completed 2026-06-04)

### New file: `src/Display/GuidanceHudTracker.cs`
- `GuidanceHudTracker : MonoBehaviour` with `BuildPanel()`, `Refresh()`, `Update()`
- `HudAwakePatch` (`Hud.Awake` Postfix) spawns the tracker and calls `BuildPanel()` + `Refresh()`
- Panel is a child `VSG_TrackerPanel` under the tracker's own GameObject under `Hud.transform`
- Background: `Image(color=(0,0,0,0.55))` — no sprite needed (CRIT-14 safe)
- Font: grabbed from `Hud.instance.GetComponentInChildren<Text>(includeInactive:true)` at build time
- Layout: `VerticalLayoutGroup` + `ContentSizeFitter` (vertical only); width fixed at 200 px via `sizeDelta`
- `LayoutRebuilder.ForceRebuildLayoutImmediate` called at end of `Refresh()` for correct height

### csproj change
- Added `UnityEngine.TextRenderingModule` and `Unity.TextMeshPro` references.
  Suppressed `CS0618` (obsolete `enableWordWrapping`) via NoWarn.

### Font / rendering (post-test fixes)
- Valheim's HUD is **TextMeshPro**, not legacy `UnityEngine.UI.Text` — a plain `Text` renders
  with a null font and stays invisible. Widget uses `TextMeshProUGUI` throughout.
- `FindVanillaFont()` scans `Resources.FindObjectsOfTypeAll<TMP_FontAsset>()` for an "Averia"
  asset (the game's `Valheim-AveriaSansLibre`), falling back to a live HUD font then TMP default.
- Row marker is ASCII `"> "` — the game font lacks the `▸`/`▌` geometric glyphs (they render as
  `□` and spam *"character ▸ was not found"* warnings).
- Rows use `TextOverflowModes.Ellipsis` so long titles clamp to the panel width instead of
  spilling off-screen.

### Live YAML layout (`tracker:` section)
`GuidanceConfig.Tracker` (`TrackerSpec`) lets the box be repositioned in-game without a restart;
applied on every YAML reload via `Plugin.OnConfigChanged → GuidanceHudTracker.ApplyLayout()`.

```yaml
tracker:
  enabled: true
  anchor: TopRight          # TopRight | TopLeft | BottomRight | BottomLeft
  offset_x: 46              # px in from the corner, horizontally
  offset_y: 300             # px in from the corner, vertically (clears the minimap)
  width: 210
  font_size: 11
```

When the section is absent, settings fall back to the BepInEx config (`TrackerEnabled`,
`TrackerPosition`) + `TrackerSpec` defaults. `TrackerMaxVisible` remains BepInEx-only (row count
is fixed at `Hud.Awake`).

### Refresh call sites
| Site | When |
|---|---|
| `GuidanceDispatcher.AdvanceChain` | After every step advance or chain completion |
| `GuidanceDispatcher.HandleCounterStep` (activation branch) | When primary trigger activates counter |
| `GuidanceDispatcher.HandleCounterStep` (increment branch) | After every progress trigger increment |
| `GuidanceSync.OnChainStatePush` | After server-restored state applied on reconnect |
| `Plugin.OnConfigChanged` | After YAML hot-reload |
| `HudAwakePatch.Postfix` | On HUD scene load |

### Testing
Both `test_chain_two_step` and `test_counter_stone` have `title:` fields, so the tracker displays them automatically when in progress.
