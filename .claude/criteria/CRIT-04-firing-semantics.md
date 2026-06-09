# CRIT-04 — Firing Semantics

**File:** `src/Triggers/GuidanceDispatcher.cs`

Controls whether a matching entry actually fires, and how often.

---

## Evaluation Order (all gates must pass)

For each matching entry, the dispatcher checks in this order:

1. **`requires`** — all listed IDs must have fired for this player (player-scope check).
2. **`stop_when`** — if ANY listed ID has fired for this player, this entry is skipped permanently.
3. **`once`** — if `true` and already fired (per scope), skip.
4. **`cooldown`** — if the cooldown window hasn't expired yet, skip.
5. **`max_fires`** — if `trigger.max_fires > 0` and fire count ≥ cap, skip.

If all five pass, the entry fires.

---

## `once` (bool, default: `true`)

- `true`: entry fires exactly once. After firing, `SeenTracker.MarkFired` is called and the entry never fires again for that player (player-scope) or that world (global-scope).
- `false`: entry can fire multiple times. Only `cooldown` limits re-firing frequency.
- For global-scope entries: "already fired" is checked via `ZoneSystem.GetGlobalKey("VSG.<id>")` on both the client (before sending to server) and server (race-condition guard).

---

## `cooldown` (float, seconds, default: `0`)

- `0` or negative: disabled (no cooldown, effectively unlimited re-fires if `once: false`).
- Positive: entry cannot re-fire until this many seconds have elapsed since the last fire.
- Cooldown state is **in-memory only** — it resets when the game restarts.
- Tracked in `SeenTracker.CooldownExpiry` (static `Dictionary<string, float>`).
- `once` and `cooldown` are independent — both can be active, but `once: true` makes cooldown irrelevant (it fires once and then is permanently gated).

**Typical pattern for a repeating hint:**
```yaml
once: false
cooldown: 300   # re-fires at most every 5 minutes
```

**Pattern for a hint that stops after player learns the mechanic:**
```yaml
once: false
cooldown: 120
stop_when: [learned_mechanic_id]
```

---

## `requires` (list of strings)

- Each string is the `id` of another entry.
- All listed entries must have fired for this player (player-scope state, even if the referenced entry itself is global-scope).
- If any required entry has NOT fired, the current entry is skipped with a log message.
- Empty list (default) means no requirements.

---

## `stop_when` (list of strings)

- Each string is the `id` of another entry.
- If ANY listed entry has fired for this player (player-scope), the current entry is permanently suppressed.
- Used to build "hint until player masters the mechanic" patterns.
- Checked every time the entry matches — does not latch at config load time.

---

## `requires` and `stop_when` Scope Note

Both `requires` and `stop_when` always check **player-scope** (`m_customData`), regardless of the scope of the entry they reference or the entry that contains them. Cross-scope chaining (e.g., requires a global event to have fired) is not supported in v1.

---

## Global-Scope Fire Path

When a global-scope entry passes all local gates:
1. Dispatcher calls `GuidanceSync.SendTriggerGlobal(id, playerName)` → RPC to server.
2. Local cooldown is marked immediately (to prevent the same player from spamming the server).
3. Server re-checks `once` (race guard), marks world global key, broadcasts `VSG_PlayGlobal` to all clients.
4. Each client (including the original triggerer) runs display via `GuidanceDispatcher.PlayGlobalReceived`.
5. `MarkFired` is NOT called locally — the world global key IS the fired state.

---

## `max_fires` (int, default: `0`)

`trigger.max_fires: N` caps how many times an entry fires per character. Unlike `once: true`, the entry is NOT marked in `VSG.fired` — it counts in a separate per-entry key `VSG.fc.<id>`.

- `0` (default): no cap (unlimited fires if `once: false`).
- `N > 0`: entry fires at most N times. After that the `max_fires` gate blocks it permanently.
- `max_fires` entries do NOT appear in the "Fired" section of `vsg_list`; they show as `[fired N/max]` in the configured-entries list.
- `vsg_reset` must clear the `VSG.fc.<id>` key or the cap is permanent even after reset.

**Typical use:** death tips that should only nag new players a couple of times:
```yaml
trigger:
  type: player_death
  max_fires: 2
once: false
```

---

## Criteria

- [x] `requires` failure is a skip (not an error); entry remains eligible to fire in future triggers.
- [x] `stop_when` suppression is permanent for that player's session (not re-evaluated after the stop condition clears — clearing is done via `vsg_reset`).
- [x] `once: true` + `cooldown > 0`: cooldown is irrelevant — the `once` gate permanently blocks after first fire.
- [x] `once: false` + `cooldown: 0`: entry can fire every time the trigger event is raised.
- [x] Cooldown is per-entry (keyed by `id`), not per-trigger-type.
- [x] Cooldown resets on game restart (expected behavior — not a bug).
- [x] Global-scope entries mark a local cooldown to prevent client-side spam before server responds.
- [x] All gate checks log at `LogInfo` level with a reason when skipping (for debuggability).
- [x] `max_fires` counts are stored in `VSG.fc.<id>` (not `VSG.fired`) and cleared by `vsg_reset`.
