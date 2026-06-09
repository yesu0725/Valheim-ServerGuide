# CRIT-05 — Player vs Global Scope

**File:** `src/State/SeenTracker.cs`

---

## Player Scope (`scope: player`, default)

- Fire state is stored in `Player.m_customData` — a `Dictionary<string, string>` that rides with the character save (`.fch` file).
- Key: `"VSG.fired"` → value: comma-separated list of fired entry IDs.
- State is per-character: each character has independent guidance history.
- Two characters on the same account can be at different points in guidance.
- State survives across sessions as long as the character file exists.
- `ClearAllFired` removes the `"VSG.fired"` key entirely from `m_customData`.

**When to use:** Tips about personal progression, first-time tutorials, per-player milestones.

---

## Global Scope (`scope: global`)

- Fire state is stored in `ZoneSystem` global keys as `"VSG.<id>"`.
- ZoneSystem global keys are stored in the **world save** (`.fwl` + `.db` files), not the character.
- Vanilla Valheim automatically replicates global key changes to all connected clients via `RPC_GlobalKeys` — no custom replication needed.
- Only the **server** (or host in local multiplayer) is allowed to set or remove global keys.
- State is world-wide: if the key is set, it is set for every player on that world.

**When to use:** One-time world events (boss kills that unlock lore, first-time world milestones), broadcasts that all players should see once per world.

**Example global flow:**
```
Player A kills Eikthyr
  → KillTrigger raises TriggerEvent
  → Dispatcher: entry is global → SendTriggerGlobal(id, "PlayerA")
  → Server: checks once, sets ZoneSystem key "VSG.eikthyr_lore"
  → Server: broadcasts VSG_PlayGlobal to ALL clients (including Player A, B, C)
  → Every client: shows intro/rune display
```

---

## SeenTracker API

```csharp
// Check
bool HasFired(Player player, string id, string scope)
bool HasFired(Player player, string id)          // defaults to "player"

// Mark (server authority required for global)
void MarkFired(Player player, string id, string scope)
void MarkFired(Player player, string id)          // defaults to "player"

// Clear (admin / vsg_reset)
bool ClearFired(Player player, string id, string scope)
int  ClearAllFired(Player player)                 // player-scope only

// Query
IReadOnlyCollection<string> GetFiredIds(Player player)

// Cooldown (in-memory, not persisted)
bool CooldownReady(string id, float cooldownSeconds, float now)
void MarkCooldown(string id, float cooldownSeconds, float now)

// Helpers
string GlobalKeyFor(string id)                    // "VSG." + id
bool   IsGlobalScope(string scope)
```

---

## Global Key Naming

All our ZoneSystem keys are prefixed `"VSG."` to avoid collisions with vanilla keys (`defeatBoss_*`, `activatedHildir*`, etc.) and other mods.

Example: entry `id: eikthyr_lore` → global key `"VSG.eikthyr_lore"`.

You can inspect global keys in-game with the vanilla console command `listkeys`.
You can remove them manually with `removekey VSG.<id>` (requires cheats/admin).

---

## Authority Rules

| Operation | Who can do it |
|---|---|
| Read global key | Any peer (replicated automatically) |
| Set global key | Server / host only (`ZNet.instance.IsServer()`) |
| Remove global key | Server / host only |
| Admin client reset | Via `VSG_AdminResetGlobal` RPC; server re-verifies admin status |
| Player-scope read | Any client for their own character |
| Player-scope write | Any client for their own character |

---

## Criteria

- [ ] Player-scope state must survive a game restart (stored in character save, not memory).
- [ ] Global-scope state must survive a server restart (stored in world save).
- [ ] Global-scope: only the server/host calls `ZoneSystem.SetGlobalKey` or `RemoveGlobalKey`.
- [ ] Clients receive global key changes automatically via Valheim's own replication — no custom RPC needed for state propagation.
- [ ] `ClearAllFired` clears ONLY player-scope entries; global keys are NOT touched (they affect every player on the world).
- [ ] Global key names must always use the `"VSG."` prefix.
- [ ] `requires` and `stop_when` always check player-scope state regardless of the referenced entry's scope (see CRIT-04).
