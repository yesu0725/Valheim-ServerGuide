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

**`vsg_reset all` clears ALL of the following:**

| State bucket | Key pattern | Cleared by |
|---|---|---|
| `once` fired set | `VSG.fired` | `SeenTracker.ClearAllFired` |
| `max_fires` counters | `VSG.fc.*` | `SeenTracker.ClearAllFired` (iterates keys) |
| Chain progress | `VSG.cd.*`, `VSG.cp.*`, `VSG.cc.*` | `ChainState.ResetAll` |
| NPC item-submit counts | `VSG.is.*` | `SubmitState.ResetAll` |
| Item-acquired goal started | `VSG.ig.*` | `GoalStartedState.ResetAll` |
| Vanilla raven seen-flags | `Player.m_shownTutorials` | `GuidanceDisplay.ClearAllVsgTutorialSeen` |
| Vanilla raven temp-texts | `Raven.m_tempTexts` | `GuidanceDisplay.ClearRavenState` → `RemoveAllVanillaTempTexts` |
| In-memory raven queues | `_ravenQueue`, `_dungeonDeferred` | `GuidanceDisplay.ClearRavenState` |
| In-memory cooldowns | `SeenTracker.CooldownExpiry` | `SeenTracker.ClearAllFired` |

**Scope-aware reset for `<id>`:**

| Situation | Action |
|---|---|
| Entry is player-scope | `SeenTracker.ClearFired(player, id, "player")` — clears `VSG.fired` entry + `VSG.fc.<id>` counter + cooldown |
| Entry is global-scope AND we are the server/host | `SeenTracker.ClearFired(null, id, "global")` — removes the ZoneSystem global key locally; vanilla replication broadcasts the removal to all clients |
| Entry is global-scope AND we are an admin client | `GuidanceSync.SendAdminResetGlobal(id)` → `VSG_AdminResetGlobal` RPC → server re-verifies admin status and removes the key |

### `vsg_list_player <playerName>`

Show the full VSG guidance state of another currently-online player.

- **Admin only.** Works from both listen-server hosts and remote admin clients.
- Results are asynchronous: the console prints "requesting..." immediately and the state payload arrives after the RPC round-trip from the target client.
- If the target is not currently online, an error is printed immediately.
- If you are the listen-server host and want to inspect your *own* state, use `vsg_list` instead (you are not in the peer list).

**Output format (per player):**
```
=== ValheimServerGuide (TargetPlayer) ===
Fired (N):
  - id_one
  - id_two
Fire counts (M):
  - companions_tip = 2
Chain state:
  - tutorial_chain: complete
  - gather_chain: step 2
Submit state:
  - trader_job: 5 submitted
Goal state:
  - collect_ore: started
```

**RPC flow:**

| Step | Direction | RPC | Purpose |
|---|---|---|---|
| 1 | Admin client → Server | `VSG_APListReq` | Request target player's state |
| 2 | Server → Target client | `VSG_APListFwd` | Ask target to collect and return state |
| 3 | Target client → Server | `VSG_APListResp` | Return collected payload + admin marker |
| 4 | Server → Admin client | `VSG_APListOut` | Relay payload to admin for display |

Listen-server path skips step 1 and sends `VSG_APListFwd` directly to the target peer with `adminMarker = "server"`. The response goes to `Console.instance` instead of a remote peer.

---

### `vsg_reset_player <playerName> [all | <id>]`

Reset a specific online player's guidance state, identical in scope to `vsg_reset` but targeting another character.

| Argument | Behavior |
|---|---|
| `all` | Clears all player-scope state for that character: fired IDs, `max_fires` counters, chain progress, submit progress, goal state, raven seen-flags, raven queue. |
| `<id>` | Clears state for a single entry (same logic as `vsg_reset <id>` but on the target player). |

- Global-scope entries are NOT affected (they belong to the world, not a character).
- The reset executes on the target player's client (where `m_customData` lives); the admin receives a confirmation message once complete.
- If the target is not currently online, an error is returned immediately.

**RPC flow:**

| Step | Direction | RPC | Purpose |
|---|---|---|---|
| 1 | Admin client → Server | `VSG_APResetReq` | Request reset on target player |
| 2 | Server → Target client | `VSG_APResetFwd` | Tell target to perform the reset |
| 3 | Target client → Server | `VSG_APResetAck` | Return result message + admin marker |
| 4 | Server → Admin client | `VSG_APResetOut` | Relay confirmation to admin |

Listen-server path skips step 1 as above.

---

### `vsg_list`

Prints a summary of all configured entries and this character's fired state.

Output format:
```
=== ValheimServerGuide (<PlayerName>) ===
Fired (N):
  - id_one
  - id_two
Configured by server (M):
  - eikthyr_lore        [global, discord, fired]
  - arrow_hint          [fired]
  - companions_tip      [fired 2/2]
  - mine_ore
```

Tags shown inline with each configured entry:
- `global` — entry has `scope: global`
- `discord` — entry has `announce.discord` set
- `fired` — this player/world has already fired the entry (`once: true` entries only)
- `fired N/max` — `max_fires` entry; shows current count vs cap (these never appear in the "Fired" list above since they don't write `VSG.fired`)

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

`vsg_list_player` tab-completes with online peer names (`GuidanceSync.GetOnlinePeerNames`).

`vsg_reset_player` tab-completes with online peer names + `"all"` + all configured entry IDs (combined, deduplicated).

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| `vsg_reset all` on global entries | Global keys are NOT cleared; message explains. Use `vsg_reset <id>` individually or vanilla `removekey VSG.<id>`. |
| `vsg_reset <id>` for unknown ID | Falls back to player-scope clear; likely returns false (nothing to clear) with a message. |
| `vsg_reset <id>` while not connected | Returns "not connected to a world" message for global entries. |
| `vsg_list` with no config loaded | Shows "(no guidance loaded — server hasn't synced or YAML is empty)". |
| `vsg_reset` without argument | Shows usage hint. |
| `vsg_list_player <name>` — target offline | Error returned synchronously: "not currently online." |
| `vsg_list_player` — admin is the listen-server host targeting themselves | `FindPeerByPlayerName` won't find the host (not in peer list); error message suggests using `vsg_list` instead. |
| `vsg_reset_player <name>` — target offline | Error returned synchronously: "not currently online." |
| `vsg_reset_player <name> all` — global entries | Global entries are NOT affected; same behaviour as `vsg_reset all`. |
| Non-admin crafts the raw RPC | Server re-verifies `ZNet.instance.IsAdmin(hostName)` and drops the request silently with a log warning. |

---

## Criteria

- [x] All commands are gated `onlyAdmin: true`.
- [x] `vsg_reset all` clears ONLY player-scope entries; global-scope entries are untouched.
- [x] `vsg_reset <id>` auto-detects scope from the current config.
- [x] Global reset from admin client goes through `VSG_AdminResetGlobal` RPC; server re-verifies admin.
- [x] Server-side re-verification uses `ZNet.instance.IsAdmin(hostName)` (not client-provided data).
- [x] `vsg_list` shows `global`, `discord`, `fired`, and `fired N/max` tags where applicable.
- [x] `vsg_list` shows the correct fired state for global-scope entries (checks ZoneSystem global key).
- [x] `vsg_reset all` clears ALL state buckets: `VSG.fired`, `VSG.fc.*`, chain state, submit state, goal state, vanilla seen-flags, raven temp-texts, raven queues, and cooldowns.
- [x] `vsg_reset <id>` clears the `max_fires` counter (`VSG.fc.<id>`) in addition to `VSG.fired` and cooldown.
- [x] Stale `RavenText` entries are evicted from `Raven.m_tempTexts` on all reset paths (both `all` and single-id).
- [x] Tab completion for `vsg_reset` includes all fired + configured IDs.
- [x] Commands never crash when called with no local player (early `null` check + message).
- [x] `_registered` guard prevents double-registration of commands on hot-reload.
- [x] `vsg_list_player <name>` shows fired IDs, fire counts, chain state, submit state, and goal state for any online player.
- [x] `vsg_reset_player <name> all` clears all player-scope state buckets on the target client (same scope as `vsg_reset all`).
- [x] `vsg_reset_player <name> <id>` clears a single entry on the target client (same logic as `vsg_reset <id>`).
- [x] Both commands work from listen-server host (direct peer RPC) and remote admin client (4-RPC round-trip).
- [x] Server re-verifies admin status for `VSG_APListReq` and `VSG_APResetReq` before forwarding.
- [x] Admin marker `"server"` distinguishes listen-server responses (output to `Console.instance`) from remote admin responses (RPC relay).
- [x] Tab completion for `vsg_list_player` includes online peer names; `vsg_reset_player` includes peer names + `"all"` + configured IDs.
