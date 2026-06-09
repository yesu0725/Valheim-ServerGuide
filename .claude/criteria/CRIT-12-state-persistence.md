# CRIT-12 — State Persistence

**File:** `src/State/SeenTracker.cs`

---

## What Persists Where

| State type | Storage | Tied to | Survives |
|---|---|---|---|
| Player-scope fired IDs | `Player.m_customData["VSG.fired"]` | Character `.fch` file | Game restarts, server changes |
| Global-scope fired IDs | ZoneSystem global key `"VSG.<id>"` | World `.fwl`/`.db` save | Game restarts, new players joining |
| Cooldown timers | `SeenTracker.CooldownExpiry` (in-memory) | Process lifetime | Does NOT survive game restart |

---

## Player-Scope (`m_customData`)

`Player.m_customData` is a `Dictionary<string, string>` serialized inside the character save (`.fch` binary file, in `AppData/LocalLow/IronGate/Valheim/characters/`).

**Format:** key `"VSG.fired"` → comma-separated string of fired entry IDs.

Example: `"eikthyr_lore,arrow_hint,first_pick"`

**Edge cases:**
- If the string is empty or the key doesn't exist, `GetSet()` returns an empty `HashSet`.
- When all fired IDs are cleared, the key is **removed** from `m_customData` entirely (not set to empty string) to keep the character data clean.
- The comma-separated format means entry IDs must not contain commas. (Enforced by convention — IDs use `snake_case`.)

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

- All player-scope fired state is lost (character file deleted).
- Global-scope state is unaffected (stored in world, not character).
- A new character on the same world will see global-scope entries as "not fired" — they will trigger the global display again if the world key was cleared, or skip if the world key is still set.

---

## What Happens on World Delete / New World

- All global-scope fired state is lost (world file deleted).
- Player-scope state on characters is retained (stored in character file).
- Characters joining a new world will re-trigger global-scope entries (world keys don't exist yet).

---

## Criteria

- [ ] Player-scope fired IDs survive game restarts (stored in character save).
- [ ] Player-scope state is per-character — two characters on the same account have independent guidance history.
- [ ] Global-scope fired IDs survive server restarts (stored in world save).
- [ ] Global-scope state is per-world — every character on the same world shares it.
- [ ] Cooldown timers do NOT persist across game restarts (in-memory only).
- [ ] `m_customData["VSG.fired"]` is removed (not set to empty) when all player-scope IDs are cleared.
- [ ] Entry IDs must not contain commas (they are used as CSV values in `m_customData`).
- [ ] `ClearAllFired` resets cooldowns in addition to clearing fired IDs.
- [ ] Only the server/host writes to ZoneSystem global keys; clients are read-only.
