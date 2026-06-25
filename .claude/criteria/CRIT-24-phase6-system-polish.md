# CRIT-24 — System Polish (Roadmap Phase 6)

**Status:** `done`

Five cross-cutting quality-of-life items from the roadmap's final phase.

See [`.claude/FEATURE_ROADMAP.md`](/.claude/FEATURE_ROADMAP.md) Phase 6.

---

## 1. Quest Journal HUD

**Already satisfied by the existing `GuidanceHudTracker`** (`src/Display/GuidanceHudTracker.cs`)
rather than a new `QuestJournalPanel.cs` — it already is a persistent, collapsible sidebar on the
`Hud` canvas listing every active multi-step entry, with title, progress, a configurable toggle
hotkey (`F10` default), auto-hide, hover tooltips, and a completion flash. Building a second,
parallel panel would just duplicate it.

The one literal gap was the roadmap's "mini progress bar" — rows previously showed a plain
`cur/goal` text fraction. `GuidanceHudTracker.ProgressBar(cur, goal)` (new) renders a fixed-width
TMP rich-text ghost bar — `[<color=#FFE6A8>==</color><color=#555555>===</color>] 2/5` — bright
filled segments, dark-gray empty segments, capped at 12 characters wide so the row never resizes
as the counter advances. Applied to all five progress-row sites: chain counter steps, chain step
index, `npc_item_submit`, multi-kill, and `item_acquired` (both single- and multi-goal).

## 2. Proximity Chat Bubble (`bubble` display mode)

`display.mode: bubble` + `display.npc_name` (+ optional `display.duration`, default 6s) floats
the rendered text in world space above the nearest matching NPC, within 50m of the local player.
No matching NPC nearby → warning logged, no crash; the entry's other side effects (state,
rewards) still apply normally since this only governs the visual.

NPC lookup searches **both** `Character.GetAllCharacters()` (monsters/Humanoids) and
`Object.FindObjectsOfType<Trader>()`, matching prefab name the same way `trigger.npc` does
(`TriggerUtils.NormalizePrefabName`). This two-list search was needed because `Trader` (Haldor,
BogWitch, Hildir) is a plain `MonoBehaviour` with **no** `Character`/`Humanoid` component at
all — the first implementation only checked `Character.GetAllCharacters()` and silently found
nothing for every vendor NPC, even standing right next to them.

**Vanilla NPC speech bubbles are suppressed while a VSG bubble is active.** Trader's own random
talk/greet/buy/sell lines render via `Chat.instance.SetNpcText(GameObject talker, ...)`, the same
world-space text system. Without coordination the two would stack/overlap above the NPC's head.
`NpcChatBubble.Init()` immediately clears any current vanilla text for that NPC
(`Chat.instance.ClearNpcText(go)`) and adds the NPC's `GameObject` to a static
`NpcBubbleSuppression` set; a Harmony prefix on `Chat.SetNpcText` no-ops for any `talker` in that
set, so new vanilla lines can't appear either. `OnDestroy()` (covers ttl expiry, anchor going
null, and scene unload) always removes the NPC from the set, so vanilla text resumes
automatically once the quest bubble's lifetime ends — never gets stuck suppressed.

- `src/Display/NpcChatBubble.cs` (new) — a `MonoBehaviour` that creates a `TextMeshPro` (3D,
  not UGUI) object 2.2m above the NPC's transform, billboards to face `Camera.main` every frame,
  fades out over the last second of its lifetime, then self-destroys. Built entirely from
  TextMeshPro (already shipped) — no custom assets (CRIT-14).
- `GuidanceDisplay.ShowBubble()` does the NPC lookup and hands off to `NpcChatBubble.Show()`.

```yaml
display:
  mode: bubble
  npc_name: Haldor
  duration: 6
  text: "Wares from across the world..."
```

## 3. `vsg_debug` Admin Command

`vsg_debug` (onlyAdmin, no args) dumps three sections for the local character:
1. **Eligible now (gates passing)** — non-chain entries via the existing `CheckGates`; chain
   entries via `!ChainState.IsComplete && PrerequisiteChecker.AllSatisfied(Requires)` (CheckGates'
   once/cooldown semantics don't apply to chains the same way, so they get their own check).
2. **`VSG.*` custom-data keys** — every key in `player.m_customData` starting with `VSG.`, with
   its raw value.
3. **Last fired (this session)** — up to the last 10 `(id, wall-clock time)` pairs from the new
   `DebugFireLog` (`src/State/DebugFireLog.cs`), an in-memory, per-player, session-only ring
   buffer. Deliberately not persisted to `m_customData` — it's pure diagnostics, not state that
   needs to survive a restart. Hooked into every fire path: `Raise()`'s single-entry fire, chain
   completion (`AdvanceChain`), `FireById`, `FireEntry`, and `PlayGlobalReceived`.

## 4. NPC Hover Text Override

New `hover_text: { default, after_fire }` on a `GuidanceEntry` (`HoverTextSpec`). `TraderHoverTextPatch`
(`Trader.GetHoverText` postfix) now checks, in order:
1. An eligible (gate-passing, not-yet-fired) `npc_conversation` entry for this NPC with
   `hover_text.default` set → **appends** it below the vanilla hover text (e.g. `[E] Talk`),
   same as the generic hint it replaces.
2. A fired, `once: true` entry for this NPC with `hover_text.after_fire` set → appends that instead.
3. Otherwise, the existing behavior: append `"\n[Hold E] Quest"` when any conversation is available,
   or leave the vanilla text untouched.

The vanilla hover line is always kept — `hover_text` only adds a quest-specific line under it,
it never replaces the player's normal interact hint.

Scoped to Trader-bound NPCs only, matching `npc_conversation`'s existing scope — there's no
generic "NPC" hook for hover text outside the trader interaction path.

```yaml
hover_text:
  default: "[Quest] The Missing Shipment"
  after_fire: "[Completed] The Missing Shipment"
```

## 5. Group/Party Kill-Progress Sync

New `trigger.share_progress: true` on a `kill` trigger with `count > 1`. There's no real "party"
system in vanilla Valheim, so — matching the roadmap's own phrasing, "nearby group members" —
proximity is the practical stand-in: when the trigger increments locally, the killer's client also
broadcasts a `VSG_ShareKillProgress` RPC (entryId, killer name, killer position). Every other
connected client checks whether its own local player is within `KillCountTracker.ShareProgressRadius`
(50m) of that position; if so, it applies the same single-kill credit to its own `KillCountState`
counter for that entry (`KillCountTracker.ApplySharedIncrement`) — without re-broadcasting, so the
party doesn't loop the RPC. This is a convenience nudge, not security-sensitive state, so it skips
the server round-trip other RPCs use and goes client → broadcast → clients directly.

`item_acquired` was **not** given the same treatment: its count is re-derived from each player's
own inventory contents on every check (`CountInInventory`), not an independent accumulator like
`KillCountState` — a "shared credit" would just be overwritten the next time it recomputes. Only
true accumulator-style triggers (currently just multi-kill) make sense to share this way.

```yaml
trigger:
  type: kill
  creature: Boar
  count: 3
  share_progress: true
```

---

## Files Changed

| File | Change |
|---|---|
| `src/Config/GuidanceConfig.cs` | Add `HoverTextSpec` + `GuidanceEntry.HoverText`; add `DisplaySpec.NpcName`/`Duration`; add `TriggerSpec.ShareProgress` |
| `src/Display/NpcChatBubble.cs` | New — world-space floating NPC text; `NpcBubbleSuppression` + Harmony prefix on `Chat.SetNpcText` to suppress vanilla NPC speech bubbles while a VSG bubble is active |
| `src/Display/GuidanceDisplay.cs` | Add `bubble` mode dispatch + `ShowBubble()` NPC lookup |
| `src/Display/GuidanceHudTracker.cs` | Add `ProgressBar()` ghost-bar helper; apply to all 5 progress-row sites |
| `src/State/DebugFireLog.cs` | New — session-only last-10-fired ring buffer per player |
| `src/Triggers/GuidanceDispatcher.cs` | `DebugFireLog.Record()` calls at every fire path |
| `src/Triggers/NpcConversationTrigger.cs` | `TraderHoverTextPatch` hover_text override logic |
| `src/Triggers/KillTrigger.cs` | `share_progress` broadcast + `ApplySharedIncrement` |
| `src/Net/GuidanceSync.cs` | New RPC `VSG_ShareKillProgress` (client → broadcast → clients) |
| `src/Commands/AdminCommands.cs` | New `vsg_debug` command |

---

## Criteria

- [x] A chain/counter/submit/kill/item_acquired progress row renders a fixed-width ghost bar instead of a plain `cur/goal` fraction; bar width never changes as the counter advances.
- [x] `display.mode: bubble` floats the rendered text above the nearest matching NPC within 50m, billboarded to the camera, fading out and self-destroying after `duration` seconds.
- [x] While a VSG bubble is showing for an NPC, that NPC's vanilla speech bubble (random talk/greet/buy/sell) does not appear — any current one is cleared immediately, new ones are blocked.
- [x] Vanilla speech bubbles resume normally for that NPC once the VSG bubble's duration ends.
- [x] `bubble` mode with no nearby matching NPC logs a warning and does not crash; other entry side effects still apply.
- [x] `vsg_debug` lists every gate-passing entry (chains via prerequisites+completion, others via `CheckGates`).
- [x] `vsg_debug` lists every `VSG.*` key currently in the player's `m_customData` with its value.
- [x] `vsg_debug` lists the last 10 fired entry ids with timestamps for the current session.
- [x] `hover_text.default` is appended below the vanilla hover text (e.g. `[E] Talk`) for an eligible, unfired entry — the vanilla line is never removed.
- [x] `hover_text.after_fire` is appended the same way for a fired `once: true` entry.
- [x] An entry without `hover_text` is unaffected — exact prior behavior (vanilla text + optional "[Hold E] Quest" hint).
- [x] `trigger.share_progress: true` on a multi-kill entry credits nearby players' (within 50m) own counters for the same entry when one player lands a kill, without requiring them to land it themselves.
- [x] A player outside the 50m radius does not receive shared credit.
- [x] Build is clean: `Build succeeded. 0 Warning(s) 0 Error(s)`.
