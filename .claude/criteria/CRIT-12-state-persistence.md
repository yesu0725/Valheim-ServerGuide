# CRIT-12 — State Persistence

**File:** `src/State/SeenTracker.cs`

> **Player-scope state no longer lives in the character save.** Every `VSG.*` key below is now
> read and written through `PlayerProgress` into a per-character file the **server** owns — see
> [CRIT-26](/.claude/criteria/CRIT-26-server-side-progress.md) for the storage layout, the sync
> protocol, and the one-time migration out of `m_customData`. The key vocabulary and semantics
> documented here are unchanged; only the backing store moved. Read the sections below as
> describing the *keys*, and read `m_customData` as *"the progress store"* except where this file
> explicitly says otherwise.

---

## What Persists Where

| State type | Storage | Tied to | Survives |
|---|---|---|---|
| Player-scope fired IDs (`once`) | progress store `"VSG.fired"` | Server progress file (per character) | Game restarts; **not** carried in from single-player |
| `max_fires` fire counts | progress store `"VSG.fc.<id>"` | Server progress file | Game restarts |
| Global-scope fired IDs | ZoneSystem global key `"VSG.<id>"` | World `.fwl`/`.db` save | Game restarts, new players joining |
| Chain progress | progress store `"VSG.cd/cp/cc/cv.*"` | Server progress file | Game restarts |
| NPC item-submit counts | progress store `"VSG.is.<id>"` | Server progress file | Game restarts |
| Item-acquired goal started | progress store `"VSG.ig.<id>"` | Server progress file | Game restarts |
| Conversation node pointer | progress store `"VSG.cn.<id>"` | Server progress file | Game restarts |
| Tracker pins + panel position | progress store `"VSG.trk"` / `"VSG.tpos"` | Server progress file | Game restarts |
| Quest-start log latch | progress store `"VSG.qs.<id>"` | Server progress file | Game restarts |
| Codex hidden-quest list | progress store `"VSG.hid"` | Server progress file | Game restarts |
| Cooldown timers | `SeenTracker.CooldownExpiry` (in-memory) | Process lifetime | Does NOT survive game restart |

The progress file is per **character** (keyed on `Player.GetPlayerID()`), so the per-character
independence the old `.fch` storage gave is preserved. What changed is *whose* disk it lives on.

### `VSG.hid` — Codex "hide from list"

**Format:** key `"VSG.hid"` → comma-separated string of hidden entry IDs. Same shape as
`VSG.fired`, and removed entirely (not left empty) when the last id is unhidden.

This is a **display preference only** (`HiddenQuestState`). A hidden quest still fires, tracks,
announces, and rewards exactly as before — it is simply omitted from the Codex guide list until
the player flips the "Show hidden" footer toggle. Both reset paths (`vsg_reset all` and
`vsg_reset_player <name> all`) clear it, and a single-id reset unhides that id, so a reset quest
can never sit invisible in the list.

---

## Player-Scope (progress store)

The progress store is a `Dictionary<string, string>` held by `PlayerProgress` and persisted to
`<config>/ValheimServerGuide/PlayerProgress/<PlayerName>_<characterId>.yml` by the authoritative
process. Clients hold a mirror and stream changes to the server (CRIT-26).

Historically this was `Player.m_customData`, serialized inside the character save (`.fch` binary
file, in `AppData/LocalLow/IronGate/Valheim/characters/`). Those keys are still present in old
character files as an untouched backup, but the mod no longer reads them — except once, as the
migration seed.

### `VSG.fired` — `once` entries

**Format:** key `"VSG.fired"` → comma-separated string of fired entry IDs.

Example: `"eikthyr_lore,arrow_hint,first_pick"`

**Edge cases:**
- If the string is empty or the key doesn't exist, `GetSet()` returns an empty `HashSet`.
- When all fired IDs are cleared, the key is **removed** from `m_customData` entirely (not set to empty string) to keep the character data clean.
- The comma-separated format means entry IDs must not contain commas. (Enforced by convention — IDs use `snake_case`.)

### `VSG.fc.<id>` — `max_fires` counters

Each entry that uses `trigger.max_fires: N` stores its fire count under a separate per-entry key. These keys **never** appear in `VSG.fired`, so `vsg_list` cannot surface them from the fired set — they are displayed separately as `[fired N/max]` tags.

`SeenTracker.GetFireCount(player, id)` reads, `IncrementFireCount` writes, and `ClearFireCount` removes. `ClearAllFired` iterates all `VSG.fc.*` keys and removes them.

**Important:** `vsg_reset` must clear these counters or a capped entry (e.g. `player_death` tip with `max_fires: 2`) stays permanently blocked even after a full reset.

---

## Global-Scope (ZoneSystem Global Keys)

ZoneSystem global keys are stored in the world save and automatically replicated to all connected clients by vanilla's `RPC_GlobalKeys` mechanism. No custom networking is required for propagation.

Key format: `"VSG.<entry_id>"` (e.g., `"VSG.eikthyr_lore"`).

**Server authority:** Only the server/host can call `ZoneSystem.SetGlobalKey` or `ZoneSystem.RemoveGlobalKey`. Clients call `ZoneSystem.GetGlobalKey` for read-only checks (the value is already replicated to them).

**Persistence:** Global keys persist as long as the world save exists. Deleting the world wipes all global keys including ours.

**Manual inspection:** Use vanilla console `listkeys` to see all global keys. Use `removekey VSG.<id>` (with cheats/admin) to manually clear one.

---

## Cooldown State (In-Memory)

`CooldownExpiry` is a static `Dictionary<string, float>` where:
- Key: entry ID string
- Value: `Time.time` value at which the cooldown expires

This is **process-local and ephemeral**:
- Resets completely when the game closes or the player returns to the main menu.
- This is expected and intentional — cooldowns are rate-limiting, not permanent gates.
- For permanent "only once" behavior, use `once: true` (stored in `m_customData` or global key).

`ClearAllFired` also calls `CooldownExpiry.Clear()` so a full admin reset removes cooldown state too.
`ClearFired` removes the specific entry's cooldown via `CooldownExpiry.Remove(id)`.

---

## What Happens on Character Delete

- The character's progress file is orphaned, not deleted — the server has no way to know the
  character file is gone. A new character gets a new `characterId` and so a new, empty progress
  file. Orphans are harmless; delete them by hand if the folder gets untidy.
- Global-scope state is unaffected (stored in world, not character).
- A new character on the same world will see global-scope entries as "not fired" — they will trigger the global display again if the world key was cleared, or skip if the world key is still set.

---

## What Happens on World Delete / New World

- All global-scope fired state is lost (world file deleted).
- Player-scope progress files are untouched — they live beside the config, not in the world save,
  and are keyed on character rather than world. A wiped world keeps everyone's quest history.
- Characters joining a new world will re-trigger global-scope entries (world keys don't exist yet).

---

## Criteria

- [x] Player-scope fired IDs survive game restarts (stored in the server's progress file).
- [x] Player-scope state is per-character — two characters on the same account have independent guidance history.
- [x] Player-scope state does NOT travel with the character save, so single-player progress
      cannot pre-complete server quests (CRIT-26).
- [x] Global-scope fired IDs survive server restarts (stored in world save).
- [x] Global-scope state is per-world — every character on the same world shares it.
- [x] Cooldown timers do NOT persist across game restarts (in-memory only).
- [x] `"VSG.fired"` is removed (not set to empty) when all player-scope IDs are cleared.
- [x] Entry IDs must not contain commas (they are used as CSV values in `VSG.fired`).
- [x] `ClearAllFired` resets cooldowns in addition to clearing fired IDs.
- [x] Only the server/host writes to ZoneSystem global keys; clients are read-only.
- [x] `max_fires` fire counts are stored in `VSG.fc.<id>` keys (separate from `VSG.fired`).
- [x] `ClearAllFired` also removes all `VSG.fc.*` keys so `max_fires` entries re-fire after `vsg_reset all`.
- [x] `ClearFired(player, id, "player")` also calls `ClearFireCount` so single-id reset unblocks `max_fires` entries.
