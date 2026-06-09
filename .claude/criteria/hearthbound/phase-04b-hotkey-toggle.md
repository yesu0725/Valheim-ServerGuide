# Phase 04b — Hotkey Toggle, Hint Badge & Empty State

**Status:** `complete`
**Depends on:** Phase 04a (stable font + login refresh)
**Blocks:** 04c (auto-show shares the same open/close state machine)

Adds a keyboard shortcut to manually open and close the tracker panel, a lightweight persistent
hint badge that is always visible so players know the feature exists, and a graceful empty state
when the tracker is opened manually with no active chains.

---

## Features

### 1. Hotkey Toggle (default F10)

Pressing the configured key toggles the tracker panel between open and closed, regardless of
whether there are any active chains.

- Key is configurable via a new YAML field `tracker.hotkey` (string, e.g. `"F10"`).
- Also exposed as a BepInEx config entry `TrackerHotkey` (default `"F10"`) for players who
  prefer the config file.
- YAML wins over BepInEx when the `tracker:` section is present.
- Holding the key does not repeat — only fires on the initial `KeyDown` frame.
- Opening via hotkey sets `_manuallyOpened = true`; auto-fade (Phase 04c) is suppressed while
  `_manuallyOpened` is true.
- Pressing the key again sets `_manuallyOpened = false` and closes the panel.

### 2. Always-Visible Hint Badge

A tiny persistent HUD element showing `[F10] Quests` (key label updates dynamically). Visible
even when the full tracker panel is hidden, so players can discover the feature.

- Implemented as a second lightweight panel (`VSG_TrackerBadge`) parented to
  `Hud.instance.transform`, positioned at the same corner as the tracker but 40px above it
  (larger gap to prevent overlap with the main panel).
- Uses a single `TextMeshProUGUI` row: `[F10] Quests (N)` where `N` is the count of active
  chains. When `N` is 0 the count is omitted: `[F10] Quests`.
- Badge is always visible (as long as `TrackerEnabled` is true and the HUD is active).
- Badge is hidden during intro cinematic (same `GuidanceDisplay.IntroLockActive` guard).
- Badge can be disabled independently via a new YAML bool `tracker.badge_enabled` (default
  `true`) or BepInEx `TrackerBadgeEnabled`.

### 3. Cursor & Camera Lock While Open

When the full tracker panel is open (either manually or auto-shown by 04c):

- `GameCamera.instance.m_mouseCapture` is set to `false` — this makes `UpdateMouseCapture()`
  take the "no capture" branch, freeing the OS cursor.
- `PlayerController.TakeInput(bool look)` is patched to return `false` while the tracker is
  open. This is the private gate in `PlayerController.LateUpdate` that feeds mouse delta into
  `SetMouseLook` — the actual driver of camera/character rotation. Without this patch, setting
  `m_mouseCapture = false` alone frees the OS cursor but does NOT stop the camera from rotating.
- `Player.TakeInput()` is also patched false to block attacks and interactions while open.
- On close, `GameCamera.m_mouseCapture` is restored to `true` and mouse-look resumes on the
  next `PlayerController.LateUpdate`.

### 4. Empty State

When the player opens the tracker manually with no active chains, the panel shows:
```
GUIDES
  No active quests
```
instead of hiding itself. This only applies when `_manuallyOpened = true`; auto-show (04c)
still hides the panel when there is nothing to show.

### 5. Click-Outside to Close

While the panel is manually open, a transparent full-screen `Button` (`VSG_ClickOverlay`) sits
behind the tracker panel. Clicking it closes the tracker (sets `_manuallyOpened = false`) and
disables the button. The panel is always kept as the last sibling so its clicks are not
intercepted by the overlay.

---

## Bug Found During Testing — Camera Rotation Not Stopped

### Root Cause
Setting `GameCamera.m_mouseCapture = false` frees the OS cursor but does NOT stop the camera
from rotating on mouse move. Valheim uses two independent `TakeInput` gates:

1. `Player.TakeInput()` — gates attacks, use, and interact actions in `Player.Update`.
2. `PlayerController.TakeInput(bool look)` — a **private** method in `PlayerController` that
   gates mouse-look in `PlayerController.LateUpdate`. It is the actual caller of
   `character.SetMouseLook(mouseDelta)` and is checked independently of `Player.TakeInput`.

Patching `Player.TakeInput` (the first approach) blocked actions but not look because the look
path runs entirely through `PlayerController.TakeInput(bool look)`.

### Fix
Added a second Harmony patch on `PlayerController.TakeInput(bool look)` using the string
overload (private method, no `nameof`):

```csharp
[HarmonyPatch(typeof(PlayerController), "TakeInput", new[] { typeof(bool) })]
internal static class PlayerControllerTakeInputTrackerPatch
{
    private static void Postfix(ref bool __result)
    {
        if (GuidanceHudTracker.InputCaptured) __result = false;
    }
}
```

`InputCaptured` is a static property on `GuidanceHudTracker` returning `Instance != null &&
Instance._manuallyOpened`.

---

## Row Height Scaling Fix

### Root Cause
Row heights were hardcoded at build time (15f header, 14f row, 13f overflow). Increasing
`font_size` via YAML increased the glyph size but the `LayoutElement.preferredHeight` stayed at
the build-time values, clipping the text vertically.

### Fix
Added `SetRow(TMP_Text t, float fontSize)` which sets both `t.fontSize` and recomputes the
`LayoutElement.preferredHeight` as `ceil(fontSize * 1.45)`. Called from `ApplyLayout()` so row
heights scale live with every YAML reload.

---

## Default Changes

| Property | Old | New |
|---|---|---|
| `TrackerSpec.Hotkey` / `TrackerHotkey` BepInEx | `F9` | `F10` |
| `TrackerSpec.FontSize` | `11` | `15` |
| `TrackerSpec.OffsetY` | `300` | `320` (panel shifted down to clear badge) |
| Badge gap above main panel | `22px` | `40px` |

---

## YAML Shape

```yaml
tracker:
  enabled: true
  hotkey: F10              # key name matching UnityEngine.KeyCode enum (string)
  badge_enabled: true      # show the corner hint badge
  anchor: TopRight
  offset_x: 46
  offset_y: 320
  width: 250
  font_size: 15
```

---

## New YAML Fields in `TrackerSpec`

```csharp
public string Hotkey { get; set; } = "F10";
public bool BadgeEnabled { get; set; } = true;
```

---

## New BepInEx Config Keys

| Key | Default | Description |
|---|---|---|
| `TrackerHotkey` | `"F10"` | KeyCode name for the toggle key |
| `TrackerBadgeEnabled` | `true` | Show/hide the corner hint badge |

---

## Files Changed

| File | Change |
|---|---|
| `src/Display/GuidanceHudTracker.cs` | `_manuallyOpened` flag; `InputCaptured` static property; `BuildBadge()`; `RefreshBadge()`; `ApplyBadgeLayout()`; `SetRow()` for scaled row heights; `OpenManual()` / `CloseManual()`; `ResolveHotkey()`; `EnsureClickOverlay()` / `DestroyClickOverlay()`; `Update()` hotkey poll; empty state in `Refresh()`; `PlayerTakeInputTrackerPatch`; `PlayerControllerTakeInputTrackerPatch` |
| `src/Config/GuidanceConfig.cs` | `TrackerSpec.Hotkey`, `TrackerSpec.BadgeEnabled`; default `FontSize` → 15; default `OffsetY` → 320 |
| `src/Plugin.cs` | `TrackerHotkey`, `TrackerBadgeEnabled` config entries |

---

## Criteria

- [x] Pressing the configured hotkey (default F10) shows the tracker panel when hidden and hides
      it when visible.
- [x] The key label in the badge updates if `tracker.hotkey` is changed in YAML and saved.
- [x] The badge displays `[F10] Quests (N)` when N active chains exist; `[F10] Quests` when N = 0.
- [x] The badge is visible when the main tracker panel is hidden.
- [x] The badge is hidden during the intro cinematic.
- [x] `badge_enabled: false` in YAML hides the badge without affecting the main panel.
- [x] When the tracker is open, the OS cursor is visible and usable.
- [x] The cursor is restored to the prior state when the tracker closes.
- [x] Camera and character rotation stop while the tracker is open (mouse-look frozen).
- [x] Opening the tracker manually with no active chains shows `No active quests` instead of
      hiding the panel.
- [x] Clicking outside the tracker panel while it is manually open closes it.
- [x] Pressing F10 again while the panel is open closes it.
- [x] Auto-fade (04c) is suppressed while `_manuallyOpened = true`.
- [x] No regression to existing Refresh, layout, or YAML hot-reload behaviour.
