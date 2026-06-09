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
- [x] Codex does not appear during the intro cinematic. See CRIT-07.
- [x] `CodexEnabled = false` disables the keybind and does not instantiate the panel.
- [x] The game is not paused in multiplayer when only the codex is open.
