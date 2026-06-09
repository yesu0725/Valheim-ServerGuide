# CRIT-09 — Admin Commands

**File:** `src/Commands/AdminCommands.cs`

---

## Commands

### `vsg_reset [all | <id>]`

Reset fired guidance state.

| Argument | Behavior |
|---|---|
| `all` | Clear all **player-scope** fired IDs + all cooldowns for the current character. Global-scope entries are NOT touched. |
| `<id>` | Clear a specific entry. Scope is auto-detected from the current config. |

**Scope-aware reset for `<id>`:**

| Situation | Action |
|---|---|
| Entry is player-scope | `SeenTracker.ClearFired(player, id, "player")` — local |
| Entry is global-scope AND we are the server/host | `SeenTracker.ClearFired(null, id, "global")` — removes the ZoneSystem global key locally; vanilla replication broadcasts the removal to all clients |
| Entry is global-scope AND we are an admin client | `GuidanceSync.SendAdminResetGlobal(id)` → `VSG_AdminResetGlobal` RPC → server re-verifies admin status and removes the key |

### `vsg_list`

Prints a summary of all configured entries and this character's fired state.

Output format:
```
=== ValheimServerGuide (<PlayerName>) ===
Fired (N):
  - id_one
  - id_two
Configured by server (M):
  - eikthyr_lore  [global, discord, fired]
  - arrow_hint    [fired]
  - mine_ore
```

Tags shown inline with each configured entry:
- `global` — entry has `scope: global`
- `discord` — entry has `announce.discord` set
- `fired` — this player/world has already fired the entry

---

## Admin Verification

Commands are registered with `onlyAdmin: true` in the 12-argument `Terminal.ConsoleCommand` constructor. In vanilla Valheim:
- Single-player and host: always admin.
- Dedicated server client: must be in `adminlist.txt`.

For global reset from an admin client (`VSG_AdminResetGlobal` RPC), the server **re-verifies** admin status independently:

```csharp
var peer = ZNet.instance.GetPeer(sender);
var hostName = peer?.m_socket?.GetHostName();
if (!ZNet.instance.IsAdmin(hostName)) { deny; return; }
```

This protects against modded/malicious clients crafting the RPC directly without going through the console command.

---

## Tab Completion

`vsg_reset` provides tab completion via `optionsFetcher: KnownIdsForResetTab`:
- Always includes `"all"`.
- Includes all IDs the local player has fired (`SeenTracker.GetFiredIds`).
- Includes all configured entry IDs from the current config (useful from host/server).
- Deduplicated and sorted alphabetically.

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| `vsg_reset all` on global entries | Global keys are NOT cleared; message explains. Use `vsg_reset <id>` individually or vanilla `removekey VSG.<id>`. |
| `vsg_reset <id>` for unknown ID | Falls back to player-scope clear; likely returns false (nothing to clear) with a message. |
| `vsg_reset <id>` while not connected | Returns "not connected to a world" message for global entries. |
| `vsg_list` with no config loaded | Shows "(no guidance loaded — server hasn't synced or YAML is empty)". |
| `vsg_reset` without argument | Shows usage hint. |

---

## Criteria

- [x] All commands are gated `onlyAdmin: true`.
- [x] `vsg_reset all` clears ONLY player-scope entries; global-scope entries are untouched.
- [x] `vsg_reset <id>` auto-detects scope from the current config.
- [x] Global reset from admin client goes through `VSG_AdminResetGlobal` RPC; server re-verifies admin.
- [x] Server-side re-verification uses `ZNet.instance.IsAdmin(hostName)` (not client-provided data).
- [x] `vsg_list` shows `global`, `discord`, and `fired` tags where applicable.
- [x] `vsg_list` shows the correct fired state for global-scope entries (checks ZoneSystem global key).
- [x] Tab completion for `vsg_reset` includes all fired + configured IDs.
- [x] Commands never crash when called with no local player (early `null` check + message).
- [x] `_registered` guard prevents double-registration of commands on hot-reload.
