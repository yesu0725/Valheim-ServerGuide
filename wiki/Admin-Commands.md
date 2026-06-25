# Admin Commands

ValheimServerGuide adds five console commands for testing and moderation: `vsg_list`, `vsg_list_player`, `vsg_reset`, `vsg_reset_player`, and `vsg_debug`. Open the F5 console to use them. All are admin-only.

> **Requirement:** You must have `devcommands` enabled and be an admin. In single-player or as the host, you always qualify. On a dedicated server, you must be listed in `adminlist.txt`.

---

## `vsg_list`

Lists all guidance IDs — which are configured, which have fired, and their scope and features.

```
vsg_list
```

**Output example:**

```
[VSG] Configured guidance IDs (3 total):
  first_bronze_sword   [fired] [player]
  world_eikthyr_fell   [global]
  swamp_tip            [discord] [cooldown]
```

Each ID is tagged with:

| Tag | Meaning |
|---|---|
| `[fired]` | This entry has already fired for the current character (`once: true` entries) |
| `[fired N/max]` | This entry uses `max_fires`; shows current count vs cap (these never appear in the Fired list above) |
| `[global]` | This is a global-scope entry |
| `[discord]` | This entry has an `announce.discord` configured |
| `[Complete ✓ (vN)]` | Chain entry — all steps done, at version N |

---

## `vsg_list_player`

Shows the full VSG guidance state of any **currently-online** player — the same breakdown as `vsg_list` (fired IDs, `max_fires` counters, chain progress, item-submit progress, goal-started flags) but for another character.

```
vsg_list_player <playerName>
```

Works from both listen-server hosts and remote admin clients; the target's client answers over an RPC round-trip, so the result appears asynchronously a moment after you run it.

---

## `vsg_reset`

Clears the fired state for one or all entries.

### Reset all player-scope entries

```
vsg_reset all
```

Clears all player-scope state for the **current character**:

- Every `once`-fired ID (`VSG.fired`)
- Every `max_fires` counter (`VSG.fc.*`) — **required** so capped entries like `player_death` tips can fire again
- All chain progress, NPC item-submit counts, kill counts, and item-acquired goal state
- HUD progress-panel pins (`VSG.trk`) — reset quests are no longer pinned
- All in-memory cooldowns
- The raven display queue and dungeon-deferred raven queue
- Stale vanilla raven temp-texts for all VSG entries (so raven re-show isn't blocked by a leftover `Raven.m_tempTexts` entry)
- Vanilla `Player.m_shownTutorials` flags for all VSG raven entries

Global-scope state is untouched.

### Reset a specific entry

```
vsg_reset first_bronze_sword
```

Clears the fired state and cooldown for a single entry. The command auto-detects scope:

- **Player-scope ID** — cleared from the local character's `m_customData`. Only affects the admin running the command.
- **Global-scope ID** — must be run on the server/host. Removes the `VSG.<id>` global key from `ZoneSystem`. The entry can fire again for the entire world.

For a single-id reset, the following are also cleared for that entry:
- The `max_fires` counter (`VSG.fc.<id>`)
- Its HUD progress-panel pin (`VSG.trk`)
- Any pending raven queue entry or dungeon-deferred entry
- Any stale `RavenText` in the vanilla `Raven.m_tempTexts` list (so the raven can re-show without being blocked by a leftover from before the reset)
- The vanilla `Player.m_shownTutorials` seen-flag (for raven entries)

### Tab completion

Tab-complete on `vsg_reset` suggests `all` and all known guidance IDs from the current config.

---

## `vsg_reset_player`

Resets a specific **online** player's guidance state. Mirrors `vsg_reset` exactly (fired IDs, fire counters, chain state, submit/goal/kill progress, conversation nodes, HUD pins, raven flags) but targets another player's character instead of your own.

```
vsg_reset_player <playerName> [all | <id>]
```

The admin console receives a confirmation message once the target client executes the reset. Like `vsg_list_player`, it works from both hosts and remote admin clients and is re-verified server-side.

---

## `vsg_debug`

Dumps a diagnostic snapshot for your character to the console:

```
vsg_debug
```

- All entries currently **eligible to fire** (gates passing) right now.
- Every `VSG.*` key in your `m_customData` (fired set, counters, chain/submit/goal/node/pin state).
- The **last 10 fired** entry IDs this session (a session-only ring buffer — not persisted across relog).

Use it to confirm why an entry is or isn't firing and to verify that state cleared after a `vsg_reset`.

---

## Use Cases

**Testing a new entry:** After adding an entry to `guidance.yaml`, save the file (it hot-reloads), then run `vsg_reset <id>` to clear any old fired state so you can trigger it again.

**Resetting a player's quest:** If a player needs to restart a chain, run `vsg_reset <chain_id>` to clear the chain state. The player can then re-trigger step 1.

**World event reset (testing):** On the server/host, run `vsg_reset world_eikthyr_fell` to allow the global boss event to fire again.

**Fresh start for testing:** Run `vsg_reset all` to clear all your own fired states and replay guidance from the beginning.
