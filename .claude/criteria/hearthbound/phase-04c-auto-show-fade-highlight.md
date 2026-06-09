# Phase 04c — Auto-Show on Progress, Fade-Out & Row Highlight

**Status:** `done`
**Depends on:** Phase 04b (`_manuallyOpened` flag; open/close state machine)
**Blocks:** nothing (self-contained visual layer)

Makes the tracker reactive: it pops up automatically when the player makes progress on a chain,
highlights the updated row so the player immediately knows what changed, then fades away after a
configurable idle period.

---

## Features

### 1. Auto-Show on Progress

Whenever a chain step fires, a chain step is advanced, or a counter increments, the tracker
panel becomes visible — even if it was previously hidden by the auto-fade.

- Achieved by adding an `AutoShow()` method to `GuidanceHudTracker` that:
  1. Sets `_panel.SetActive(true)`.
  2. Resets the fade timer.
  3. Sets alpha to fully opaque.
- `AutoShow()` is called from the same `Refresh()` call sites that exist today (Dispatcher,
  GuidanceSync), but **only when called from a progress event** (not from YAML reload or login).
  A parameter `bool fromProgress = false` distinguishes the cases.
- Auto-show does **not** override the `_manuallyOpened` close state — it only overrides the
  auto-fade state.

### 2. Auto-Fade

After `tracker.auto_hide_delay` seconds of no new progress, the panel fades out smoothly.

- Fade is a linear alpha lerp from 1.0 → 0.0 over `tracker.fade_duration` seconds (default 1.0 s).
- The panel's `CanvasGroup` component drives the alpha; the `Image` and all TMP text components
  respond automatically.
- Timer resets whenever `AutoShow()` is called (new progress arrives).
- When `_manuallyOpened == true`, the timer is paused — the panel stays fully opaque until the
  player closes it manually.
- At the end of the fade (`alpha <= 0`), `_panel.SetActive(false)` to stop rendering.

```
Progress event
     │
     ▼
AutoShow() ──► Reset timer, alpha = 1, panel active
                           │
                     [no new progress for auto_hide_delay seconds]
                           │
                           ▼
                    Fade lerp: alpha 1 → 0 over fade_duration seconds
                           │
                           ▼
                    _panel.SetActive(false)
```

### 3. Row Highlight on Update

When `Refresh()` is called with `fromProgress = true`, the row(s) whose content changed since
the last frame are displayed in a distinct accent color (`new Color(1f, 0.95f, 0.5f)` — bright
gold) for a configurable duration (`tracker.highlight_duration`, default 3.0 s), then lerp back
to normal white.

- Highlight state is tracked per-row with a `float[] _rowHighlightTimers` array.
- Each `Update()` tick decrements non-zero timers; when a timer expires, the row's color is set
  back to `Color.white`.
- Rows are compared by content string between Refresh calls — a changed string triggers a
  highlight.
- The header row is never highlighted.
- The `+N more` overflow row is never highlighted.

---

## YAML Shape

```yaml
tracker:
  auto_hide_delay: 5.0       # seconds of idle before fade begins
  fade_duration: 1.0         # seconds the fade-out takes
  highlight_duration: 3.0    # seconds the updated row stays gold before returning to white
```

---

## New YAML Fields in `TrackerSpec`

```csharp
public float AutoHideDelay { get; set; } = 5f;
public float FadeDuration { get; set; } = 1f;
public float HighlightDuration { get; set; } = 3f;
```

---

## Implementation Notes

### `CanvasGroup` for alpha fade
Add `_panelGroup = _panel.AddComponent<CanvasGroup>()` in `BuildPanel()`. Setting
`_panelGroup.alpha` drives opacity for all child elements at once.

### `Refresh(bool fromProgress)` signature change
All existing call sites pass `fromProgress: false` by default. The dispatcher call sites
(`AdvanceChain`, `HandleCounterStep`) pass `fromProgress: true`.

### `Update()` additions
```csharp
private void Update()
{
    // Intro cinematic guard (existing)
    if (_panel != null && _panel.activeSelf && GuidanceDisplay.IntroLockActive)
        _panel.SetActive(false);

    // Auto-fade timer
    if (!_manuallyOpened && _panel != null && _panel.activeSelf)
    {
        _fadeTimer -= Time.deltaTime;
        if (_fadeTimer <= 0f)
        {
            _fadeElapsed += Time.deltaTime;
            var spec = EffectiveSpec();
            var t = Mathf.Clamp01(_fadeElapsed / Mathf.Max(0.1f, spec.FadeDuration));
            _panelGroup.alpha = 1f - t;
            if (t >= 1f) _panel.SetActive(false);
        }
    }

    // Per-row highlight countdown
    for (var i = 0; i < _rowHighlightTimers.Length; i++)
    {
        if (_rowHighlightTimers[i] <= 0f) continue;
        _rowHighlightTimers[i] -= Time.deltaTime;
        if (_rowHighlightTimers[i] <= 0f && i < _rowTexts.Count && _rowTexts[i] != null)
            _rowTexts[i].color = Color.white;
    }
}
```

---

## Files Changed

| File | Change |
|---|---|
| `src/Display/GuidanceHudTracker.cs` | `CanvasGroup` field; `_fadeTimer`/`_fadeElapsed`; `_rowHighlightTimers`; `AutoShow()`; `Refresh(bool fromProgress)`; extended `Update()` |
| `src/Config/GuidanceConfig.cs` | `TrackerSpec.AutoHideDelay`, `TrackerSpec.FadeDuration`, `TrackerSpec.HighlightDuration` |
| `src/Triggers/GuidanceDispatcher.cs` | Pass `fromProgress: true` to `Refresh()` call sites |

---

## Criteria

- [x] Tracker panel appears automatically when a chain step fires or a counter increments.
- [x] Panel does not auto-show on YAML reload or on login (use `fromProgress: false`).
- [x] After `auto_hide_delay` seconds with no new progress, the panel fades out over
      `fade_duration` seconds.
- [x] Fade is smooth (alpha lerp), not an instant disappearance.
- [x] `SetActive(false)` is called at the end of the fade to stop rendering.
- [x] New progress (another trigger fires) resets the fade timer and snaps alpha back to 1.
- [x] When `_manuallyOpened == true` (hotkey-opened), fade timer is paused.
- [x] The updated chain row turns gold immediately when Refresh fires with `fromProgress: true`.
- [x] The gold highlight fades back to white after `highlight_duration` seconds.
- [x] Non-updated rows remain white during another row's highlight.
- [x] The header and overflow rows are never highlighted.
- [x] `auto_hide_delay`, `fade_duration`, and `highlight_duration` are live-tunable from YAML.
- [x] No regression to hotkey, badge, empty state, font, or layout behaviour.

---

## Post-Ship Notes

### Bug 1 — Manual hotkey could not re-open a faded panel
`OpenManual()` called `SetActive(true)` on a panel whose `CanvasGroup.alpha` was still `0` from a
completed fade — invisible but technically active. Fixed by resetting `alpha = 1` and cancelling
the fade timer (`_fadeTimer = float.MaxValue`, `_fadeElapsed = 0`) inside `OpenManual()`.

`CloseManual()` was also calling `Refresh()`, which re-showed active chains instead of hiding.
Fixed to call `SetActive(false)` directly and reset fade state instead.

### Bug 2 — Panel hidden behind Valheim's crafting/inventory UI
Initial approach (nested sub-canvas with `overrideSorting = true, sortingOrder = 50`) backfired:
a sub-canvas with `overrideSorting` sorts **globally** by its own order, ignoring the parent's
order. Sorting order 50 placed the panel below most other UI.

Final fix: all tracker UI (panel, badge, click-overlay) parents to a dedicated **root canvas**
(`VSG_TrackerRoot`, `sortingOrder = 1000`). The badge was always visible because it had no
nested canvas — the panel was hidden because it did. Removing the nested canvas from the panel
lets it draw at its root's order (1000) and appear above the crafting UI.
`OnDestroy` tears down the root canvas since it is a scene-root object (no parent) and is not
auto-destroyed with the tracker's own GameObject.
