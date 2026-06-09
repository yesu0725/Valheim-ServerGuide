# Phase 04d — Hover Tooltips & Step Descriptions

**Status:** `done`
**Depends on:** Phase 04b (cursor enabled while open; panel is interactive)
**Blocks:** nothing (self-contained enrichment)

When the tracker is open and the cursor is visible, hovering over a chain row shows a
floating tooltip with the current step's `description` text — giving players richer context
without cluttering the compact tracker widget.

---

## YAML Schema Addition

Add an optional `description` field to `GuidanceStep`. It is purely informational; it never
affects trigger logic or chain advancement.

```yaml
steps:
  - trigger: { type: item_acquired, item: Wood }
    progress_trigger: { type: item_acquired, item: Stone }
    progress_goal: 3
    description: |
      Stone can be found on the ground throughout the Meadows biome,
      or mined from boulders with a pickaxe. Look for grey rocks near water.
    display: { mode: message, position: Center }
    message: "[counter] You've gathered 3 Stone. Counter step complete!"
```

`description` is multi-line friendly (YAML block scalar). If absent, hovering the row does
nothing (no tooltip shown).

---

## Data Model Change

```csharp
// GuidanceStep in GuidanceConfig.cs
public string Description { get; set; }   // optional tooltip body; null = no tooltip
```

---

## Tooltip Panel

The tooltip is a second lightweight panel (`VSG_TrackerTooltip`) built once and toggled
on/off on hover, parented to `Hud.instance.transform`:

- Background: `Image(color=(0.1, 0.08, 0.06, 0.88))` — slightly warmer than the tracker bg.
- A single `TextMeshProUGUI` with word-wrap enabled, max width 280 px, max 6 lines.
- `ContentSizeFitter` (both axes: `PreferredSize`) so it auto-sizes to the description text.
- Positioned adjacent to the hovered row — appears to the left of the tracker when anchor is
  `TopRight`, or to the right when anchor is `TopLeft`.
- Hidden by default; shown only while a row with a description is hovered.

### Hover Detection

Valheim's HUD does not use EventSystem raycasts by default for modded panels. To detect hover:

- In `Update()`, when the tracker is open (`_manuallyOpened` or auto-shown), get the screen
  position of each active row's `RectTransform` and compare against `Input.mousePosition`.
- Use `RectTransformUtility.RectangleContainsScreenPoint(rowRect, mousePos, null)`.
- Track `_hoveredRowIndex` (-1 = none); on index change, update tooltip content and position.
- When `_hoveredRowIndex == -1` or panel is not open, hide the tooltip.

### Tooltip Position Logic

```
TrackerAnchor = TopRight:
  tooltip.anchoredPosition = row world pos, shifted left by (tooltipWidth + 8px)

TrackerAnchor = TopLeft:
  tooltip.anchoredPosition = row world pos, shifted right by (trackerWidth + 8px)

TrackerAnchor = BottomRight/BottomLeft:
  tooltip appears above the hovered row.
```

---

## No Tooltip When Closed

Tooltip is always hidden when:
- `_manuallyOpened == false` and no auto-show is active (fade has completed).
- The intro cinematic is active.
- `_hoveredRowIndex == -1`.

---

## Files Changed

| File | Change |
|---|---|
| `src/Config/GuidanceConfig.cs` | `GuidanceStep.Description` property |
| `src/Display/GuidanceHudTracker.cs` | `BuildTooltip()`; `_hoveredRowIndex`; `_tooltipPanel`/`_tooltipText`; hover detection in `Update()` |

---

## Criteria

- [x] `description` field parses correctly from YAML (UnderscoredNamingConvention maps
      `description` → `GuidanceStep.Description`).
- [x] Steps without a `description` field do not show a tooltip; hovering has no visible effect.
- [x] Hovering a row (with description) while the tracker is open shows the tooltip within one
      frame.
- [x] Tooltip shows the **current step's** description for the hovered chain.
- [x] Tooltip disappears immediately when the mouse leaves the row.
- [x] Tooltip disappears when the tracker closes (manual or auto-fade).
- [x] Tooltip is hidden during the intro cinematic.
- [x] Tooltip position adjusts based on tracker anchor so it never renders off-screen.
- [x] Tooltip uses vanilla TMP font (Averia). No custom assets. See CRIT-14.
- [x] Tooltip word-wraps correctly for multi-line descriptions.
- [x] Hovering the header or overflow row does not show a tooltip.
- [x] No regression to fade, highlight, hotkey, or layout behaviour.
