# CRIT-21 — Multi-Quest NPC Selection Menu (Roadmap Phase 3)

**Status:** `done`

When a player holds E on a trader NPC with 2+ gate-passing `npc_conversation` entries, show a
"What would you like to discuss?" picker before entering any specific conversation. With exactly
one eligible entry, behavior is unchanged from [CRIT-17](/.claude/criteria/CRIT-17-npc-conversation.md).

See [`.claude/FEATURE_ROADMAP.md`](/.claude/FEATURE_ROADMAP.md) Phase 3.

---

## How it works

1. `NpcConversationTrigger.FindAllEntries(npcSubject, player)` (new) collects every
   `npc_conversation` entry for that NPC whose gates currently pass (same checks as the existing
   single-entry `FindEntry`, just not stopping at the first match).
2. `NpcConversationHoldDetector.Update()`'s held-threshold branch now calls `FindAllEntries`
   instead of `FindEntry`:
   - **0 entries** — unchanged: falls back to opening the vanilla store.
   - **1 entry** — unchanged: opens that entry's conversation directly via `GuidanceDisplay.Show`.
   - **2+ entries** — new: calls `NpcConversationPanel.Get().OpenSelection(trader.m_name, entries, npcSubject)`.
3. `OpenSelection` reuses the existing conversation panel chrome (same 750×185 box, same header/
   body/choice-row layout) — header = NPC name, body = "What would you like to discuss?", one
   button per eligible entry labeled `entry.Title ?? entry.Id`.
4. Selecting a button calls `GuidanceDispatcher.FireEntry(entry, evt)` — the same entry-point used
   by every other "fire a pre-selected entry" path (`KillCountTracker`, `TimeTrigger`). `FireEntry`
   marks the entry fired/cooldown, grants any rewards, and calls `GuidanceDisplay.Show`, which
   (because the entry's `display.mode` is `conversation`) reopens the panel in **normal mode**
   for that entry — its own header/body/choices render exactly as if it had been the only
   eligible entry. No special-case code is needed for "enter the conversation after picking it."

No changes to `GuidanceDispatcher.Raise`/`MatchesTrigger`, `TriggerSpec`, or the YAML schema for
`npc_conversation` triggers — the picker is purely a panel/trigger-side selection step in front of
the existing single-entry flow.

---

## `GuidanceEntry.Title` fallback

`Title` already exists on `GuidanceEntry` (used by the HUD tracker and Codex). The picker is the
first place an entry without a `title` matters for *display*: it falls back to `entry.Id` as the
button label. No schema change — this was already nullable.

---

## Files Changed

| File | Change |
|---|---|
| `src/Triggers/NpcConversationTrigger.cs` | Add `FindAllEntries`; held-threshold branch in `NpcConversationHoldDetector.Update()` branches on count (0 / 1 / 2+) |
| `src/Display/NpcConversationPanel.cs` | Add `OpenSelection(npcDisplayName, entries, npcSubject)`, `AddSelectionButton`, `OnEntrySelected`; `AddChoiceButton` takes an optional `onClick` override so selection rows can bypass the normal `ChoiceSpec.Goto` path |
| `.claude/criteria/CRIT-17-npc-conversation.md` | Cross-reference this file from the overview |

---

## Criteria

- [x] Holding E on a trader with exactly 1 eligible `npc_conversation` entry behaves identically to before (opens that conversation directly, no picker).
- [x] Holding E on a trader with 0 eligible entries falls back to the vanilla store, unchanged.
- [x] Holding E on a trader with 2+ eligible entries shows the picker: header = NPC name, body = "What would you like to discuss?", one button per eligible entry.
- [x] Picker button labels show `title` when set, falling back to `id` when absent.
- [x] Selecting a picker button opens that entry's own conversation (its own header/body/choices), exactly as if it had fired normally.
- [x] Selecting a picker entry marks it fired (`once`/`cooldown`/`max_fires`) the same as any other `FireEntry` path.
- [x] Gates (`requires`, `stop_when`, `once`, `cooldown`, `max_fires`) are re-checked at panel-open time, so an entry that fired moments earlier via another path does not appear in the picker.
- [x] Input lock (movement/attack/inventory/ESC) applies identically while the picker is open, same as the normal conversation panel (shared `NpcConversationPanel.IsOpen` gate).
- [x] Build is clean: `Build succeeded. 0 Warning(s) 0 Error(s)`.
