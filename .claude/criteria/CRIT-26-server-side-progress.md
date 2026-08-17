# CRIT-26 — Server-Side Player Progress

**Files:** `src/State/PlayerProgress.cs`, `src/State/PlayerProgressStore.cs`,
`src/Net/GuidanceSync.cs` (progress RPCs)

Supersedes the player-scope half of [CRIT-12](/.claude/criteria/CRIT-12-state-persistence.md)
and replaces the old in-memory chain cache (`_playerChainData` + `VSG_ChainStep*` RPCs).

---

## The problem this solves

Every `VSG.*` state bucket used to live in `Player.m_customData`, which is serialized inside the
**character** save (`.fch`). That made quest progress portable in the wrong direction:

1. Player joins the server with character *Ulf*, starts a quest chain.
2. Player logs off and plays **single-player** with the same *Ulf*.
3. Every quest they complete offline is written into `Ulf.fch`.
4. They rejoin the server — and the server sees those quests as already done.

Progress now lives in a file the **server** owns, one per character. Offline play writes to the
client's *own* local folder (single-player is its own server), so it can never leak onto the
server.

---

## Storage layout

```
<BepInEx>/config/ValheimServerGuide/PlayerProgress/
  Ulf_-8264718293746152.yml
  Astrid_44019283746152.yml
```

One folder, one file per character. Override the folder with the
`PlayerProgress / ProgressPath` BepInEx setting — recommended if a mod manager rewrites
`config/` on update, because **these files are the players' quest progress**.

File format:

```yaml
# ValheimServerGuide — server-side quest progress for one character.
player_name: Ulf
character_id: -8264718293746152
migrated_from_character_file: true
migrated_at: 2026-08-17 10:00:00 UTC
last_saved: 2026-08-17 10:42:11 UTC
progress:
  VSG.cc.bogwitch_rite:0: '3'
  VSG.cp.valcoin_quest: '2'
  VSG.fired: eikthyr_lore,arrow_hint
```

Only `progress:` is read back; everything above it is bookkeeping for the admin. The keys are
exactly the ones CRIT-12 documents — nothing about the key vocabulary changed, only where it
is stored.

### Identity

`character_id` is `Player.GetPlayerID()` (resolved from the loaded `PlayerProfile` first, since
that is populated earliest). It is stable for the life of the character and survives renames.
The name in the filename is for readability only: on load the store looks for the exact filename
first, then falls back to scanning for `*_<characterId>.yml`, and **renames the file** when it
finds a match under an old name. Per-character (not per-account) keeps the previous semantics —
two characters on one account have independent quest logs.

### Durability

- Mutations mark a record dirty; `Tick()` writes at most every **3 s**.
- A peer disconnect (`ZNet.Disconnect` prefix) saves and unloads that character immediately.
- `ZNet.OnDestroy` and `Plugin.OnDestroy` force a full save.
- Writes go to `<file>.tmp` and are then swapped in, so a crash mid-write cannot replace a
  complete file with a truncated one.
- A file that fails to parse is moved aside to `<file>.corrupt-<timestamp>` and a fresh store is
  started, with an error in the log. It is never silently ignored — "the server wiped my quests"
  must always leave evidence.

---

## Modes

`PlayerProgress.Mode` decides where reads and writes land:

| Mode | When | Reads/writes go to |
|---|---|---|
| `Local` | dedicated-server host character, listen-server host, single-player | the store's dictionary (this process owns the file) |
| `Remote` | pure client | a mirror the server pushed; every change is queued as a delta |
| `Legacy` | server never answered the handshake, or the character id could not be resolved | `Player.m_customData` (old behavior) |
| `Unbound` | no session yet, or handshake in flight | a buffer; **nothing may fire** (see below) |

`Legacy` exists so a client joining a server that does not run VSG (or runs an older build) still
has a working quest log — it just is not server-authoritative. It logs a warning naming the
likely cause.

---

## Sync protocol

Three RPCs, all `ZPackage`:

| RPC | Direction | Payload |
|---|---|---|
| `VSG_ProgReq` | client → server | `characterId`, `playerName`, migration seed map |
| `VSG_ProgPush` | server → client | `characterId`, `migrated` flag, full progress map |
| `VSG_ProgDelta` | client → server | `characterId`, `playerName`, `(key, removed, value)*` |

Flow:

1. `Player.OnSpawned` **prefix** calls `GuidanceSync.EnsureProgressSession`.
   A prefix, not a postfix: other patches hook `OnSpawned` and read state, and Harmony does not
   order patches across classes — binding first guarantees the host's store is live before any
   of them run.
2. Host / single-player binds synchronously off disk and is immediately ready.
3. A pure client sends `VSG_ProgReq` carrying its migration seed and stays `Unbound`.
4. The server binds (migrating if needed) and replies with `VSG_ProgPush`.
5. `PlayerProgress.ApplyServerPush` installs the mirror and runs the ready catch-up.
6. From then on `PlayerProgress.Tick` (called from `Plugin.Update`) flushes queued deltas —
   at most one packet per frame, and only when something actually changed.

A no-op write (same value already stored) neither dirties the file nor sends a delta.

### Firing is gated on readiness

`GuidanceDispatcher.Raise`, `FireEntry` and `CheckGates` all return early when
`!PlayerProgress.IsReady`. This is the single most important invariant in this system: firing
against an unloaded store reads as *"this player has done nothing"*, so every `once` quest would
re-run **and then persist those duplicate fires to the server file**.

The one-shot triggers that mark a guard key *before* dispatching (`FirstLoginTrigger`,
`ChestOpenedTrigger`, `LocationEnteredTrigger`, `DistanceTrigger`) also check `IsReady`
themselves — otherwise they would consume their own guard key while the dispatcher was still
refusing to act on it, blocking the trigger permanently.

### Ready catch-up

`Player.OnSpawned` has already come and gone by the time a client's push lands, so anything that
only happens on spawn gets a second chance in `PlayerProgress.OnBecameReady()`:
`FirstLoginTrigger.RunIfNeeded`, `ItemAcquiredTrigger.CheckAllCountGoals`, plus a tracker and
Codex repaint. Add to that method — not to a new patch — if a future feature needs spawn-time work.

### Trust model

A client supplies its own `characterId` and `playerName`, so a modified client could claim
another character's file, exactly as it could already spoof the name-keyed RPCs this replaces.
The server does enforce that every delta key starts with `VSG.` so a client cannot write outside
this mod's namespace. This is co-op-grade, not anti-cheat.

---

## Migration (once per character, automatic)

On a character's **first** contact with the server store:

- The client (or the host, locally) collects every `VSG.*` key still in `m_customData`.
- The server has no file for that `characterId`, so it creates one seeded from those keys, sets
  `migrated_from_character_file: true` + `migrated_at`, and **saves immediately** — so the
  migration cannot run twice even if the process dies seconds later.
- Log line: `[progress] MIGRATION: '<name>' (character <id>) had N progress key(s) in their
  character file — copied into the server store.`

On every later login the file exists, so the seed is ignored and the file is authoritative.

The old `m_customData` keys are **left in place, untouched, and never read again**. They are a
read-only backup: if a progress file is ever lost, deleting it lets the migration re-seed from
the character save. `vsg_debug` reports how many leftovers a character still carries.

Anything completed offline *after* this release writes to the client's own local progress folder,
never to `m_customData`, so it can never be picked up by a migration.

---

## Admin surface

- `vsg_debug` — prints the active storage mode (`local file (...)` / `server-side (mirrored…)` /
  `character file (legacy fallback…)`), every `VSG.*` key in the store, and the count of
  character-file leftovers.
- `vsg_list_player <name>` — the relayed payload now leads with the target's storage mode.
- `vsg_refresh` — re-requests the config **and** the progress file; unflushed local changes are
  re-applied on top of the incoming push rather than discarded.
- `vsg_reset` / `vsg_reset_player` — unchanged in behavior. They go through the same state
  buckets, so the deletions flow to the server file as deltas like any other change.

---

## Criteria

- [x] Player-scope progress is stored in a server-owned file, not the character save.
- [x] One folder, one YAML file per character, named `<PlayerName>_<characterId>.yml`.
- [x] The character id is the identity; a renamed character keeps its progress and its file is
      renamed to match.
- [x] Quests completed in single-player never appear as done on the server.
- [x] A character's pre-existing `m_customData` progress is migrated exactly once, automatically,
      on the first login after this release.
- [x] The migration is keyed on file existence, so it cannot repeat on later logins.
- [x] Migrated `m_customData` keys are left intact as an unused backup.
- [x] Nothing fires while the store is unbound, so `once` quests cannot re-run on a slow join.
- [x] One-shot guard-key triggers do not consume their key before the store is ready.
- [x] Spawn-time triggers are re-run once a client's progress arrives.
- [x] A client on a server without VSG falls back to character-file storage with a warning
      rather than losing its quest log.
- [x] Progress files are written atomically; a parse failure is preserved as `.corrupt-*`.
- [x] Progress is flushed on peer disconnect, logout, world teardown and plugin shutdown.
- [x] Delta keys outside the `VSG.` namespace are rejected server-side.
- [x] The `PlayerProgress` folder is excluded from the guidance YAML watcher, so saving progress
      never triggers a config reload and progress files are never merged as guidance.
