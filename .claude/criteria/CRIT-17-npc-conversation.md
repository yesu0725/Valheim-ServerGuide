# CRIT-17 — NPC Conversation System

**Status:** `in_progress` (Phase 2 complete; Phase 3 keyboard navigation pending)

A conversation panel triggered by **Shift + E** on a trader NPC (Haldor, Hildir,
BogWitch). Displays a message and a row of choice buttons. Choosing a `goto` entry fires that
entry automatically via `GuidanceDispatcher.FireById()`.

When 2+ `npc_conversation` entries are eligible for the same NPC, a "what would you like to
discuss?" picker is shown first -- see [CRIT-21](/.claude/criteria/CRIT-21-phase3-multi-quest-picker.md).

Conversation entries can also define a multi-node dialogue tree instead of a flat
text+choices block -- see [CRIT-22](/.claude/criteria/CRIT-22-phase4-conversation-sequencing.md).

---

## Overview

**Shift + E** on a trader opens the mod's conversation panel and the store does **not** open.
Plain E is untouched: the vanilla store opens immediately and `npc_interacted` fires unmodified.

When a conversation entry is available and its gates pass, the trader's hover tooltip
gains an extra line: `[Shift + E] Quest`.

> **Superseded: hold-E.** The original design was a 0.5 s hold on E, resolved by an Update-loop
> detector that had to swallow the first key-down and re-open the store itself if the player let
> go early. Every ordinary trade therefore stalled for half a second. A modifier key is decided on
> frame one, so `NpcConvHoldState` and `NpcConversationHoldDetector` no longer exist.

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

Two classes in this file:

#### `NpcConversationTrigger` (Harmony `[HarmonyPatch(typeof(Trader), nameof(Trader.Interact))]`)

Prefix on `Trader.Interact(Humanoid character, bool hold, bool alt)`. It bails out to vanilla
(`return true`) unless **all** of these hold on a key-down frame:

1. `character` is the local player,
2. `hold == false` — vanilla ignores held interacts, so we do too,
3. `ConversationModifierHeld()` — either Shift key, or the bound `Run` / `JoyRun` button. Shift is
   checked literally so the `[Shift + E]` prompt is always honest; `Run` (Shift by default) keeps
   a rebinding player working and is a gamepad's only route in now that hold-E is gone,
4. `FindAllEntries` returns at least one gate-passing entry.

Then `OpenConversation` runs — one entry opens directly via `GuidanceDisplay.Show`, several open
the picker via `NpcConversationPanel.OpenSelection` — and the prefix returns `false` with
`__result = true` so the store stays shut for that press.

#### `TraderHoverTextPatch` (Harmony `[HarmonyPatch(typeof(Trader), nameof(Trader.GetHoverText))]`)

Postfix that appends `TraderHoverTextPatch.ConversationHint` (`"\n[Shift + E] Quest"`) to the
vanilla tooltip whenever a gated `npc_conversation` entry exists for that trader. Keep the
constant in step with `ConversationModifierHeld` — the prompt is a promise.

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
**Body:** `TextMeshProUGUI`, `enableWordWrapping = true`, `overflowMode = Overflow` — the panel is content-sized and scrolls past 82% screen height; nothing is ellipsised (CRIT-25).
**Choices:** `HorizontalLayoutGroup` — all buttons on a single row, equal-width flexible.
  If `conversation.choices` is absent, a default **Dismiss** button is inserted.
**Canvas:** `ScreenSpaceOverlay`, `UiLayers.Conversation`.

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
cooldown / max_fires all pass. Used by `NpcConversationTrigger` (both the Interact prefix and the
hover-text postfix) to avoid duplicating gate logic.

### `FireById(entryId)` — `internal static void`

Finds an entry by ID, checks gates, calls `GuidanceDisplay.Show()`, marks fire state, and
raises `entry_finished` for the fired entry. Used after a choice with a `goto` is selected.

---

## Files Changed

| File | Change |
|---|---|
| `src/Config/GuidanceConfig.cs` | Added `ConversationSpec`, `ChoiceSpec`; added `Conversation` to `GuidanceEntry` |
| `src/Triggers/GuidanceDispatcher.cs` | Added `CheckGates()` helper; added `FireById()` method; added `"npc_conversation"` to `MatchesTrigger` |
| `src/Triggers/NpcConversationTrigger.cs` | New — `NpcConversationTrigger` patch, `TraderHoverTextPatch` (the original `NpcConversationHoldDetector` was removed with hold-E) |
| `src/Display/NpcConversationPanel.cs` | New — panel build/open/close, cursor management, 4 input-lock patches |
| `src/Display/GuidanceDisplay.cs` | Added `"conversation"` mode dispatch to `NpcConversationPanel.Get().Open()` |
| `.claude/criteria/CRIT-02-triggers.md` | Documented `npc_conversation` trigger |
| `.claude/criteria/CRIT-03-display-modes.md` | Documented `conversation` display mode |

---

## Criteria

### Phase 2 — Modifier+E + Basic Panel + Input Lock

- [x] Holding E (≥ 0.5 s) near a trader opens the conversation panel instead of the store.
- [x] Short-press E still opens the trader store normally; `npc_interacted` trigger still fires.
- [x] `trigger.npc` matching is case-insensitive.
- [x] Panel shows `display.topic` as the header and `display.text` (or `entry.message`) as the body.
- [x] Choice buttons are rendered in a single horizontal row; equal-width flexible buttons.
- [x] Mouse click on a choice confirms the selection.
- [x] No custom textures or sprites are used; all visuals use `Image` color fills and TMP text.
- [x] TMP fonts are assigned before `SetActive(true)` to suppress the LiberationSans warning.
- [x] The store (`StoreGui`) does NOT open when the conversation panel is shown.
- [x] If no matching `npc_conversation` entry exists for the trader, Shift+E falls through to vanilla behavior.
- [ ] Hover tooltip gains `[Shift + E] Quest` line when a gated conversation entry is available.
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
