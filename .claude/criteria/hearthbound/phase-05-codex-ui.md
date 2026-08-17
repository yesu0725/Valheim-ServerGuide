# Phase 05 — In-Game Codex UI

**Status:** `done`
**Depends on:** Phase 02 (ChainState), Phase 06 (YAML `category:` and `title:` fields)
**Blocks:** Phase 09 (players need a way to re-read guides)

A keyboard-accessible in-game panel where players can browse all guides, re-read any
guide they've unlocked, and check their chain progress. Vanilla UI components only. See CRIT-14.

---

## Layout

```
┌──────────────────────────────────────────────────────────┐
│  GUIDE CODEX                                   [X] Close │
├────────────────┬─────────────────────────────────────────┤
│ CATEGORIES     │  Offline Companions Guide        2 / 5  │
│                │  ─────────────────────────────────────  │
│ > Companions   │  Step 2: Feed Your Companion            │
│   Trading      │                                         │
│   Building     │  Your companion needs food. Open their  │
│   Skills       │  inventory and place food in their food │
│   Exploration  │  slots. Cooked Meat and other prepared  │
│   Inventory    │  foods work best.                       │
│                │                                         │
│                │  ── Upcoming Steps ──────────────────── │
│                │  Step 3: Equip Gear          (locked)   │
│                │  Step 4: Configure AI        (locked)   │
│                │  Step 5: Mastery             (locked)   │
└────────────────┴─────────────────────────────────────────┘
```

- Left panel: category list (one entry per unique `category:` value in YAML).
- Right panel (top): selected guide title + `step N / total` badge.
- Right panel (middle): current step `description` (if set) or `message` — scrollable. Shows `message` when the chain is complete.
- Right panel (bottom): upcoming steps listed as locked if not yet reached.
- Completed guides show a checkmark badge next to the title.
- Locked/unseen guides are hidden entirely (players cannot see guides they haven't triggered yet).

---

## Vanilla Inventory Styling

The codex is drawn as a Valheim window, not as flat boxes: the frame, the interior boxes and the
buttons are the **player inventory's own art**, read off the live `InventoryGui` at runtime by
`VanillaUi` (see CRIT-14 for the asset table and the degradation rules). The F10 quest tracker is
deliberately left on its own lightweight styling — it is a HUD overlay, not a window.

- **Frame:** `woodpanel_playerinventory`. Because Valheim's window art is one bespoke sprite per
  window rather than a 9-sliced tile, the panel is *fitted to that sprite's aspect ratio*
  (`ResolvePanelSize`) at ~80% of screen height, clamped to 92% of screen width — it is never
  stretched. The fit re-runs when the screen resolution **or the game's GUI scale** changes.
  - Screen-to-canvas conversion comes from `UiScaleFactor()`, which reads the **vanilla HUD**
    canvas's `scaleFactor`. Our own canvas cannot be asked: a `CanvasScaler` applies itself from
    `Canvas.preWillRenderCanvases` (render time), so on the frame the codex root is activated our
    canvas still reports the factor it was created with (1), which fitted the panel to raw pixels
    and made it ~20% oversized on any display that is not exactly the reference resolution.
- **Content inset:** `_frameInset` scales with the panel (2.5% of its width, 18–34 px) because the
  frame's carving scales with it too; every content rect is derived from it in `ApplyGeometry`.
- **Interior boxes:** the guide list, the description scroll and the Upcoming Steps section sit in
  `panel_interior_bkg_128` when that sprite is 9-sliceable, else a flat dark fill — which is what
  the vanilla trophy/skills windows show inside their frames anyway.
- **Buttons:** Close, the two toggle pills and the footer toggle are vanilla `button` sprites with
  vanilla's own hover/press/disabled swaps (`Selectable.Transition.SpriteSwap`). They are sized for
  their text rather than the reverse: 16 px horizontal / 4 px vertical padding inside the sprite
  (`ButtonPadX` / `ButtonPadY`), a 38 px pill bar, a 42 px footer and a 48 px title band, with 16 pt
  labels. The pin label is kept short (`[x] Pinned to Tracker`) so it still fits at the narrowest
  panel size — the "(click to pin)" hint is redundant now that the pills visibly behave as buttons.
- **Font:** the face the inventory's own labels use, resolved by majority vote over its `TMP_Text`s
  rather than by name (outline variants skipped — they are for text over the world). The
  tracker-resolved font remains the fallback until the GUI scene exists, and is treated as
  provisional so the next `Open()` upgrades to the inventory's font.
- **Sizes** are matched to that window (title 20, entry title 18, body 16, rows/buttons 14) and
  **colours** to Valheim's pairing of orange headings on parchment body text (`VanillaUi.Orange` /
  `Beige` / `Dim` / `Green`).
- **Upcoming Steps collapses** when the selected guide has no locked steps left, handing its 150 px
  back to the description — the larger font needs the room.

---

## New File: `src/Display/GuidanceCodex.cs`

```csharp
public class GuidanceCodex : MonoBehaviour
{
    public static GuidanceCodex Instance { get; private set; }

    public void Open();
    public void Close();
    public bool IsOpen { get; }

    private void BuildPanel();
    private void PopulateCategories(List<GuidanceEntry> config, ChainState state);
    private void ShowEntry(GuidanceEntry entry, ChainState state);
}
```

### Harmony Patches

```csharp
// Block ESC from closing the codex and closing the game menu simultaneously
[HarmonyPatch(typeof(Menu), nameof(Menu.IsOpen))]
// Return true when codex is open so game logic treats it as a menu

// Block game pause when only the codex is open (singleplayer)
[HarmonyPatch(typeof(Game), nameof(Game.Pause))]
```

---

## Keybind

- Default: `F2` (configurable in BepInEx config as `CodexKey`).
- Toggle open/close.
- `Escape` closes the codex if open.

BepInEx config key:

```
[Codex]
CodexKey = F2
```

---

## Entry Visibility Rules

| Player State | Codex Shows? |
|---|---|
| Entry never triggered (Step 0 not yet fired) | Hidden |
| Entry triggered (Step 0+ fired) | Visible |
| Entry complete | Visible with checkmark |
| Entry locked by unmet `requires:` | Hidden |

---

## Re-Read Behavior

Clicking on a visible guide in the Codex:
- In-progress guides show the current step's **`description`** (if present) or **`message`** as a hint of what to do next.
- Completed guides show the **last step's `message`** (recap of what they finished).
- Does **not** re-fire the trigger or advance the chain — read-only.

---

## BepInEx Config Keys

| Key | Default | Description |
|---|---|---|
| `CodexEnabled` | `true` | Enable/disable the codex feature entirely |
| `CodexKey` | `F2` | Keyboard shortcut to open the codex |

---

## Criteria

- [x] Codex opens and closes with the configured key (default `F3`; was `F2` in spec, changed to `F3` to avoid conflict).
- [x] `Escape` closes the codex if it is the top-most open panel.
- [x] Only guides the player has triggered (Step 0+ fired) are visible.
- [x] Guides locked by unmet prerequisites are completely hidden.
- [x] Completed guides display a visual checkmark and are still browsable.
- [x] Current step `description` is shown in the right panel for in-progress chains; falls back to `message` when `description` is absent.
- [x] Upcoming (locked) steps are listed but clearly marked as not yet unlocked.
- [x] The codex is read-only — it cannot advance chains or re-fire triggers.
- [x] Codex uses only vanilla UI sprites and fonts. No custom assets. See CRIT-14.
- [ ] The codex window is drawn in the vanilla player-inventory frame, with vanilla interior boxes and vanilla buttons (hover/press states included) — no flat boxes while the inventory UI is available.
- [ ] The panel is never stretched off `woodpanel_playerinventory`'s aspect ratio, and re-fits when the screen resolution or GUI scale changes.
- [ ] The panel is fitted in canvas units, not raw pixels — it is the same relative size at every resolution and GUI scale.
- [ ] Every button label clears the sprite's carved border on both axes and is legible at a glance.
- [ ] Every text in the panel uses the font the vanilla inventory uses, at sizes legible in that face.
- [ ] A missing or renamed vanilla sprite leaves the codex plainer (flat fills) but fully functional.
- [ ] The Upcoming Steps section is hidden, and its space given to the description, for guides with no locked steps left.
- [ ] The F10 quest tracker's styling is unchanged.
- [x] Codex does not appear during the intro cinematic. See CRIT-07.
- [x] `CodexEnabled = false` disables the keybind and does not instantiate the panel.
- [x] The game is not paused in multiplayer when only the codex is open.
