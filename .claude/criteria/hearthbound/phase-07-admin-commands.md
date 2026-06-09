# Phase 07 — Admin Commands Expansion

**Status:** `pending`
**Depends on:** Phase 02 (ChainState), Phase 06 (YAML `category:`, `title:`)
**Blocks:** Phase 09 (server operators need these to manage guide delivery)

Extends `src/Commands/AdminCommands.cs` with new `Terminal.ConsoleCommand` entries for
managing guide delivery, resetting progress, and inspecting player state.

---

## New Commands

### `vsg_guide <entry_id>`
Re-fires the current active step of a chain for the requesting player.
- Available to **all players** (not admin-only).
- Pulls the current step from `ChainState` and fires its display/message.
- Does not advance the chain.
- If the entry is a single-entry (no steps), re-fires its message.
- If the entry ID is not found or not yet triggered, prints: `"Guide not found or not yet unlocked."`

### `vsg_progress`
Prints the requesting player's chain completion status to their console.
- Available to **all players**.
- Output format:
  ```
  [VSG] Your Guide Progress:
    Companions / Offline Companions Guide  — Step 2 / 5
    Skills / Slayer Skills Guide           — Complete ✓
    Exploration / ZenBossStone             — Step 1 / 6
  ```

### `vsg_progress <player_name>`
Admin-only. Prints the named player's chain completion status to the admin's console.
- Requires the requesting player to be listed in the BepInEx admin list.
- Fetches `ChainState` from server's stored state (`GuidanceSync` server-side dictionary).

### `vsg_send <player_name> <entry_id>`
Admin-only. Pushes a specific guide entry to a named player via RPC.
- Fires the entry's current active step (or Step 0 if not yet started) for that player.
- Does not advance the chain; only displays.
- Useful for manually introducing a player to a guide they missed.

### `vsg_broadcast <entry_id>`
Admin-only. Pushes a specific guide entry to **all currently connected players** via RPC.
- Fires the entry's Step 0 (or single-entry message) regardless of each player's chain state.
- Does not affect any player's chain progress.
- Use case: server-wide announcements, event guides, or onboarding new players mid-session.

### `vsg_chains`
Admin-only. Lists all guide entries defined in the loaded YAML config.
- Output format:
  ```
  [VSG] Loaded Guide Chains (18 total):
    [chain] companions_offline_chain  — "Offline Companions Guide"  (5 steps)
    [chain] zen_boss_chain            — "ZenBossStone Guide"         (6 steps)
    [entry] quickstack_intro          — "Quick Stack Intro"          (single)
    ...
  ```

### `vsg_reset_chain <entry_id>`
Admin-only for other players; self-reset available to all.
- Usage: `vsg_reset_chain <entry_id>` — resets the requesting player's progress on that chain.
- Usage: `vsg_reset_chain <entry_id> <player_name>` — admin resets another player's chain.
- Removes the entry from both `StepProgress` and `CompletedChains` in `ChainState`.
- The chain can then fire from Step 0 again.

---

## Changes to `AdminCommands.cs`

Add each command inside `Plugin.Start` (same pattern as existing `vsg_reset` and `vsg_list`):

```csharp
new Terminal.ConsoleCommand("vsg_guide", "[entry_id] — Re-display current step of a guide", ...);
new Terminal.ConsoleCommand("vsg_progress", "[player?] — Show guide progress (admin: specify player)", ...);
new Terminal.ConsoleCommand("vsg_send", "<player> <entry_id> — Admin: push guide to player", ...);
new Terminal.ConsoleCommand("vsg_broadcast", "<entry_id> — Admin: push guide to all players", ...);
new Terminal.ConsoleCommand("vsg_chains", "Admin: list all loaded guide chains", ...);
new Terminal.ConsoleCommand("vsg_reset_chain", "<entry_id> [player?] — Reset chain progress", ...);
```

---

## Admin Check Pattern

Reuse the existing admin-check pattern from `vsg_reset`:

```csharp
if (!SynchronizationManager.Instance.PlayerIsAdmin)
{
    Terminal.Log("You must be an admin to use this command.");
    return;
}
```

---

## RPC Requirements

`vsg_send` and `vsg_broadcast` require two new RPCs in `GuidanceSync.cs`:

| RPC Name | Direction | Payload |
|---|---|---|
| `vsg_AdminSendGuide` | Server → specific Client | `string entry_id` |
| `vsg_AdminBroadcastGuide` | Server → all Clients | `string entry_id` |

Both RPCs trigger the client to call `GuidanceDisplay.Show(entry, step)` without altering chain state.

---

## Criteria

- [ ] `vsg_guide` and `vsg_progress` are available to all players (no admin check).
- [ ] `vsg_send`, `vsg_broadcast`, `vsg_chains`, and `vsg_reset_chain <id> <player>` require admin.
- [ ] `vsg_reset_chain <id>` (self-reset, no player arg) is available to all players.
- [ ] `vsg_guide` with an unknown or not-yet-triggered entry prints a clear message rather than erroring.
- [ ] `vsg_broadcast` fires display only — it never alters any player's `ChainState`.
- [ ] `vsg_send` fires display only — it never alters the target player's `ChainState`.
- [ ] `vsg_reset_chain` removes the entry from both `StepProgress` and `CompletedChains`.
- [ ] All commands appear in `vsg_list` output (or the existing help listing).
- [ ] New RPCs are registered exactly once via the `_rpcsBound` guard. See CRIT-06.
