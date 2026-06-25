# HUD Tracker & Codex

## Progress Panel (HUD Tracker)

The progress panel is an on-screen widget that shows the quests a player has chosen to track, with their current progress. Unlike earlier versions, **it no longer shows every active quest automatically** — the player picks which quests to follow by pinning them from the Codex.

### Pin model — how quests get on the panel

- The panel is **hidden by default**, even when the player has quests in progress.
- To show a quest, open the **Codex (F3)**, select an in-progress quest, and click the **`Show on Tracker`** pill. This pins the quest and **unhides the panel**.
- Only **in-progress, trackable** quests have the pill: guide chains, multi-count `kill` quests, multi-count `npc_item_submit` quests, and `item_acquired` count/goal quests. Finished quests and one-off tip entries have **no pill**.
- Pins are **persistent** per character — a pinned quest stays pinned across the play session and survives a relog (though the panel itself starts hidden each login until shown).
- When a pinned quest **completes**, it loses its pill in the Codex and automatically drops off the panel.

### Showing / hiding — F10

- Press **F10** (default) to show or hide the panel.
- Hiding with F10 does **not** unpin anything — the pinned quests are still "in" the panel. Press F10 again (or pin another quest in the Codex) and they reappear.
- The hotkey is configurable in the BepInEx config (`TrackerHotkey`) or in `guidance.yaml` under the `tracker:` section.

### No input lock — moving and dragging

- While the panel is showing during normal play, it **does not freeze the player or show the cursor**. You can move, look, and attack as usual.
- The panel can be **dragged anywhere on screen**. Because dragging needs a free cursor, you can only move it while the **inventory** or the **ESC menu** is open: open a menu, then click-drag the panel to a new spot.
- The dragged position is **saved per character** and reused on the next login.

### Rows, badge, and tooltips

- Shows up to **3 pinned quests** by default (`TrackerMaxVisible`). Additional pinned quests collapse into a `+N more — press [F3] for Codex` label.
- Counter / multi-count quests show a `current/goal` progress indicator. Hovering a row (while the cursor is free) reveals the step's `description` tooltip; for multi-goal `item_acquired` entries the tooltip lists each item's progress (e.g. `FineWood: 18/30`).
- When a quest completes, the row flashes gold and a level-up VFX plays on the player.
- A small corner hint badge (`[F10] Quests (2)`) stays visible — even when the panel is hidden — showing how many pinned quests are active so the player knows there's something to re-open. Disable it with `badge_enabled: false`.

---

### Configuring the Panel

The panel can be configured in two places:

**1. BepInEx config** (`com.valheimserverguide.cfg`) — applies at startup:

| Config Key | Default | Description |
|---|---|---|
| `TrackerEnabled` | `true` | Show or hide the panel entirely |
| `TrackerPosition` | `TopRight` | Corner anchor (used until the player drags the panel) |
| `TrackerMaxVisible` | `3` | Max pinned quests shown before "+N more" |
| `TrackerHotkey` | `F10` | Show/hide hotkey |
| `TrackerBadgeEnabled` | `true` | Show corner badge |

**2. YAML `tracker:` section** — applies live on YAML reload (wins over BepInEx config):

```yaml
tracker:
  enabled: true
  anchor: TopRight              # TopRight | TopLeft | BottomRight | BottomLeft
  hotkey: F10
  badge_enabled: true
  offset_x: 46                  # pixels from corner, horizontal (until the player drags the panel)
  offset_y: 320                 # pixels from corner, vertical
  width: 210                    # panel width in pixels
  font_size: 15
  highlight_duration: 3         # seconds a newly updated row stays gold
  completion_vfx_enabled: true
```

> **Note:** `auto_hide_delay` and `fade_duration` are deprecated and ignored as of v0.6.0 — the panel no longer auto-hides or fades. It stays visible until the player hides it (F10) or unpins every quest. Once a player drags the panel, their saved position overrides `anchor`/`offset_x`/`offset_y`.

Changes to the YAML `tracker:` section take effect immediately on save — no server restart or reconnect needed.

---

### Step Tooltips

When hovering a tracker row, the step's `description` field is shown as a tooltip:

```yaml
steps:
  - trigger: { type: kill, creature: Troll }
    message: "Hunt Trolls in the Black Forest."
    description: >
      Trolls roam the Black Forest at night.
      They drop Troll Hide and Troll Trophy on death.
      Bring a fire arrow bow for ranged advantage.
```

---

## Codex

The Codex is an in-game guide browser. It shows all guidance entries organised by category, with their titles, descriptions, and completion status.

### Opening the Codex

Press **F3** (default) to open the Codex. Press **F3** again or **Escape** to close it. The hotkey is configurable in the BepInEx config (`CodexKey`).

### Layout

The Codex panel has two sections:

- **Left panel** — Category list. Click a category to filter entries.
- **Right panel** — Entry list for the selected category. Each entry shows its title, description, and a completion indicator.

Entries marked `category: "Crafting"` in YAML are grouped under "Crafting" in the Codex. Entries without a category appear in a default group.

### Pinning quests to the Progress Panel

When you select an **in-progress, trackable** quest, a **`Show on Tracker`** pill appears in the right pane:

- Click it to **pin** the quest to the [Progress Panel](#progress-panel-hud-tracker) (F10). Pinning also unhides the panel.
- Click again to **unpin** it.
- The pill only appears for quest types that can show progress on the panel: guide chains, multi-count `kill` quests, multi-count `npc_item_submit` quests, and `item_acquired` count/goal quests — and only while they are in progress. **Finished quests and one-off tips show no pill.**

This lets each player curate their own objective list instead of seeing every active quest at once.

### Disabling the Codex

Set `CodexEnabled: false` in the BepInEx config to disable the Codex and its hotkey entirely.

---

### Making Entries Appear in the Codex

Any entry with a `title` appears in the Codex. Entries without a `title` are functional but invisible in the Codex browser.

```yaml
- id: forge_bronze
  title: "The Bronze Age"       # shown in Codex
  category: "Progression"       # Codex group
  steps:
    ...
```

---

### Codex Body Text — `description`, `message`, and `summary`

The body panel in the Codex right panel shows different text depending on the entry state.
Priority order (highest first):

| Chain state | What is shown |
|---|---|
| **Complete** — `summary:` is set | `summary` — a "Quest Complete" header followed by the recap text |
| **Complete** — no `summary:` | Last step's `message` (the text that fired when the final step completed) |
| **In progress** — current step | `description` (falls back to `message` if `description` is absent) |

Use `description` to tell the player **what to do** to advance the current step.
Use `message` for the text that fires **when the trigger fires** (reward, lore, follow-up tip).
Use `summary` on the **entry** (not a step) for a short recap shown after the quest is done.

```yaml
- id: ward_chain
  title: "Protective Ward"
  category: Building
  summary: >
    You placed a ward and learned about passive repair, raid blocking, and
    the Valkyrie taxi system. Your base is now protected.
  steps:
    - trigger:
        type: build
        piece: guard_stone
      display:
        mode: rune
        topic: "Protective Ward"
      description: >
        Build a Protective Ward to claim your base.
        Open your hammer's building menu, find the ward under Misc,
        and place it inside your base perimeter.
        Required: 5 Fine Wood, 5 Greydwarf Eye, 1 Surtling Core.
      message: "Your ward is active. ProtectiveWards adds passive repair, raid blocking, and rain protection inside the radius."
```

While the player is on this step the Codex body shows the `description` (build instructions).
Once the chain is complete, the Codex body shows the entry-level `summary` if one is set,
otherwise it falls back to the final step's `message`.
