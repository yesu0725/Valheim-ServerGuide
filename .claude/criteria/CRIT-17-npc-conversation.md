# CRIT-17 — NPC Conversation System

**Status:** `in_progress` (Phase 2 complete; Phase 3 keyboard navigation pending)

A conversation panel triggered by holding E (≥ 0.5 s) near a trader NPC (Haldor, Hildir,
BogWitch). Displays a message and a row of choice buttons. Choosing a `goto` entry fires that
entry automatically via `GuidanceDispatcher.FireById()`.

---

## Overview

Vanilla short-press E opens the trader store. **Holding E** (≥ 0.5 s) triggers the mod's
conversation panel instead — the store does **not** open. Normal E (tap) still opens the
store and fires `npc_interacted` unmodified.

When a conversation entry is available and its gates pass, the trader's hover tooltip
gains an extra line: `[Hold E] Quest`.

Supported NPCs: any prefab with a `Trader` component. `trigger.npc` matches the prefab
name case-insensitively, identical to `npc_interacted`.

---

## Trigger

### YAML

```yaml
trigger:
  type: npc_conversation
  npc: Haldor           # prefab name, case-insensitive
```

### Source: `src/Triggers/NpcConversationTrigger.cs`

Three classes in this file:

#### `NpcConversationTrigger` (Harmony `[HarmonyPatch(typeof(Trader), nameof(Trader.Interact))]`)

Prefix on `Trader.Interact(Humanoid character, bool hold, bool alt)`.

- **`hold == false` (key-down frame):** if a gated conversation entry exists, start the hold
  timer (`NpcConvHoldState.HoldStart = Time.time`), record `PendingTrader`, suppress the
  original (return `false`, `__result = true`). The `NpcConversationHoldDetector` Update loop
  now owns the outcome.
- **`hold == true` (held frame):** suppress unconditionally while a `PendingTrader` is set
  (return `false`, `__result = false`).
- If no entry exists or gates are not met: return `true` (full vanilla path).

#### `TraderHoverTextPatch` (Harmony `[HarmonyPatch(typeof(Trader), nameof(Trader.GetHoverText))]`)

Postfix that appends `"\n[Hold E] Quest"` to the vanilla tooltip whenever a gated
`npc_conversation` entry exists for that trader.

#### `NpcConversationHoldDetector` (MonoBehaviour, always-active GO `"VSG_NpcConvHold"`)

Created lazily on the first Trader interaction via `EnsureCreated()`.

Each frame while `PendingTrader != null`:
- `ZInput.GetButton("Use")` is false → **short press** → call `StoreGui.instance.Show(trader)`;
  clear state.
- `Time.time − HoldStart >= HoldThreshold (0.5 s)` → **held** → call
  `GuidanceDisplay.Show(entry, rendered)`; clear state.

### `TriggerSpec` — no new fields needed

`trigger.npc` already exists. The type string `"npc_conversation"` is the only addition in
`MatchesTrigger` (maps to `Eq(t.Npc, evt.Subject)`).

---

## Display Mode: `conversation`

### Panel Layout (vanilla Unity UI only — no custom assets)

```
╔══════════════════════════════════════════════════════════╗
║  Haldor                                       (bold gold) ║
╟──────────────────────────────────────────────────────────╢
║  Well met, traveler! I have rare goods from distant       ║
║  lands. What brings you here?              (white, wrapped)║
╟──────────────────────────────────────────────────────────╢
║  [  Tell me about wares.  ]   [    Nothing, thanks.    ]  ║
║           (horizontal choice buttons, equal width)        ║
╚══════════════════════════════════════════════════════════╝
```

**Dimensions:** 750 × 185 px.
**Position:** anchor Y = 0.25 (between screen centre and bottom edge), pivot (0.5, 0.5) —
the box is vertically centred in the lower-middle band of the screen.
**Background:** `Image` fill `(0.02, 0.02, 0.02, 0.97)` — nearly black, high opacity so
white text reads against the game world.
**Header:** `TextMeshProUGUI`, bold, gold `(0.88, 0.75, 0.47)`, from `display.topic` or
`entry.title`.
**Divider:** 1 px gold-tinted Image strip.
**Body:** `TextMeshProUGUI`, `enableWordWrapping = true`, `overflowMode = Ellipsis`.
**Choices:** `HorizontalLayoutGroup` — all buttons on a single row, equal-width flexible.
  If `conversation.choices` is absent, a default **Dismiss** button is inserted.
**Canvas:** `ScreenSpaceOverlay`, `sortingOrder = 200`.

### Source: `src/Display/NpcConversationPanel.cs`

Singleton `MonoBehaviour` attached to the root Canvas GO.

`Open(entry, renderedText)`:
1. Lazily resolve vanilla font via `GuidanceHudTracker.FindVanillaFontStatic()`.
2. Assign font to header and body TMP before `SetActive(true)` (TMP Awake rule).
3. Set container inactive → destroy old rows → build new rows with fonts assigned →
   set container active (TMP Awake memory rule for dynamic rows).
4. `gameObject.SetActive(true)`.
5. Free OS cursor: `GameCamera.m_mouseCapture = false`, `Cursor.lockState = None`,
   `Cursor.visible = true`.

`Update()`: re-asserts cursor free every frame (Valheim recaptures on its own update).

`Close()`:
1. `gameObject.SetActive(false)`.
2. Restore `GameCamera.m_mouseCapture = true`.

`OnChoiceSelected(choice)`:
1. `Close()`.
2. Mark conversation entry (once / cooldown / max_fires) via `SeenTracker`.
3. If `choice.Goto != null`: `GuidanceDispatcher.FireById(choice.Goto)`.

---

## Input Lock (Phase 2 — implemented in `NpcConversationPanel.cs`)

Four Harmony patches gated by `NpcConversationPanel.IsOpen`:

| Patch | Effect |
|---|---|
| `Player.TakeInput` Postfix → `false` | Disables movement, attack, interact-E, inventory toggle, item use |
| `PlayerController.TakeInput` Postfix → `false` | Disables mouse-look and WASD camera |
| `Menu.Show` Prefix → `false` | Blocks ESC pause/options menu |
| `InventoryGui.Show` Prefix → `false` | Blocks Tab/I inventory (lives in `InventoryGui.Update`, not `Player.TakeInput`) |

The only interaction available while the panel is open is moving the mouse and clicking a button.

---

## YAML Schema

```yaml
conversation:
  choices:
    - text: "Tell me about your wares."
      goto: haldor_wares       # entry ID to fire on selection (optional)
    - text: "Nothing, thanks."
                               # no goto = dismiss panel only
```

### New classes in `GuidanceConfig.cs`

```csharp
public class ConversationSpec
{
    public List<ChoiceSpec> Choices { get; set; } = new List<ChoiceSpec>();
}

public class ChoiceSpec
{
    public string Text { get; set; }   // Button label
    public string Goto { get; set; }   // Entry ID to fire; null = dismiss only
}
```

`GuidanceEntry` gains:
```csharp
public ConversationSpec Conversation { get; set; }
```

---

## New Dispatcher Helpers

### `CheckGates(entry, player)` — `internal static bool`

Extracted from the `Raise()` loop. Returns `true` when requires / stop_when / once /
cooldown / max_fires all pass. Used by `NpcConversationTrigger` and `NpcConversationHoldDetector`
to avoid duplicating gate logic.

### `FireById(entryId)` — `internal static void`

Finds an entry by ID, checks gates, calls `GuidanceDisplay.Show()`, marks fire state, and
raises `entry_finished` for the fired entry. Used after a choice with a `goto` is selected.

---

## Files Changed

| File | Change |
|---|---|
| `src/Config/GuidanceConfig.cs` | Added `ConversationSpec`, `ChoiceSpec`; added `Conversation` to `GuidanceEntry` |
| `src/Triggers/GuidanceDispatcher.cs` | Added `CheckGates()` helper; added `FireById()` method; added `"npc_conversation"` to `MatchesTrigger` |
| `src/Triggers/NpcConversationTrigger.cs` | New — `NpcConversationTrigger` patch, `TraderHoverTextPatch`, `NpcConversationHoldDetector` |
| `src/Display/NpcConversationPanel.cs` | New — panel build/open/close, cursor management, 4 input-lock patches |
| `src/Display/GuidanceDisplay.cs` | Added `"conversation"` mode dispatch to `NpcConversationPanel.Get().Open()` |
| `.claude/criteria/CRIT-02-triggers.md` | Documented `npc_conversation` trigger |
| `.claude/criteria/CRIT-03-display-modes.md` | Documented `conversation` display mode |

---

## Criteria

### Phase 2 — Hold-E + Basic Panel + Input Lock

- [x] Holding E (≥ 0.5 s) near a trader opens the conversation panel instead of the store.
- [x] Short-press E still opens the trader store normally; `npc_interacted` trigger still fires.
- [x] `trigger.npc` matching is case-insensitive.
- [x] Panel shows `display.topic` as the header and `display.text` (or `entry.message`) as the body.
- [x] Choice buttons are rendered in a single horizontal row; equal-width flexible buttons.
- [x] Mouse click on a choice confirms the selection.
- [x] No custom textures or sprites are used; all visuals use `Image` color fills and TMP text.
- [x] TMP fonts are assigned before `SetActive(true)` to suppress the LiberationSans warning.
- [x] The store (`StoreGui`) does NOT open when the conversation panel is shown.
- [x] If no matching `npc_conversation` entry exists for the trader, Hold-E falls through to vanilla behavior.
- [x] Hover tooltip gains `[Hold E] Quest` line when a gated conversation entry is available.
- [x] OS cursor is freed on `Open()` and re-asserted every frame; restored on `Close()`.
- [x] Player movement, attack, interact-E, and inventory toggle are disabled while panel is open.
- [x] Camera mouse-look is disabled while panel is open.
- [x] ESC pause menu is blocked while panel is open.
- [x] Inventory screen (`Tab`/`I`) is blocked while panel is open.
- [x] `choice.goto` fires the target entry by ID (`FireById`) after the panel closes.
- [x] `choice.goto` referencing a non-existent entry logs a warning and closes without error.
- [x] Selecting a choice with no `goto` dismisses the panel cleanly.
- [x] Conversation entry is marked as fired (once / cooldown) after any choice selection.
- [x] If no choices are defined, a default "Dismiss" button is shown.

### Phase 3 — Keyboard Navigation (pending)

- [ ] Right arrow advances the selection; left arrow retreats (both wrap around).
- [ ] Enter / numpad Enter confirms the selected choice.
- [ ] Mouse hover updates the selected index to the hovered button.
- [ ] Selected choice is visually highlighted (gold color / `▶` prefix).
- [ ] ESC dismisses the panel without selecting any choice and marks the entry as fired.
