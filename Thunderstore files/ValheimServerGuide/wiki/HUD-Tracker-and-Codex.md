# HUD Tracker & Codex

## HUD Tracker

The HUD tracker is an on-screen widget that shows active guide chains and their current progress. It appears in the corner of the screen while the player has active quests, so they always know what to do next.

### Default Behaviour

- Appears in the **top-right corner** by default (below the minimap).
- Shows up to **3 active chains** by default. Additional chains collapse into a `+N more` label.
- Each row shows the chain's current step message.
- Counter steps show a progress bar: `Trophies: 2 / 5`.
- When a chain completes, the row flashes gold and a level-up VFX plays on the player, then the row fades out.
- The tracker **auto-hides** after 5 seconds of no new progress (configurable). It reappears when progress is made.

### Toggle Hotkey

Press **F10** (default) to show or hide the tracker panel. The hotkey is configurable in the BepInEx config (`TrackerHotkey`) or in `guidance.yaml` under the `tracker:` section.

### Badge

A small corner hint badge (`[F10] Quests (2)`) stays visible even when the tracker panel is hidden, showing how many active chains there are. Disable it with `badge_enabled: false`.

---

### Configuring the Tracker

The tracker can be configured in two places:

**1. BepInEx config** (`com.valheimserverguide.cfg`) — applies at startup:

| Config Key | Default | Description |
|---|---|---|
| `TrackerEnabled` | `true` | Show or hide the tracker entirely |
| `TrackerPosition` | `TopRight` | Corner anchor |
| `TrackerMaxVisible` | `3` | Max chains shown before "+N more" |
| `TrackerHotkey` | `F10` | Toggle hotkey |
| `TrackerBadgeEnabled` | `true` | Show corner badge when panel is hidden |

**2. YAML `tracker:` section** — applies live on YAML reload (wins over BepInEx config):

```yaml
tracker:
  enabled: true
  anchor: TopRight              # TopRight | TopLeft | BottomRight | BottomLeft
  hotkey: F10
  badge_enabled: true
  offset_x: 46                  # pixels from corner, horizontal
  offset_y: 320                 # pixels from corner, vertical
  width: 210                    # panel width in pixels
  font_size: 15
  auto_hide_delay: 5            # 0 = never auto-hide
  fade_duration: 1
  highlight_duration: 3         # seconds a newly updated row stays gold
  completion_vfx_enabled: true
```

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

The `description` field on individual chain steps also populates the Codex detail view when the entry is selected.
