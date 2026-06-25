# CRIT-19 — Kill-Count + Extended Interaction Triggers (Roadmap Phase 1)

**Status:** `done`

Expands the trigger vocabulary with a `count` goal on the existing `kill` trigger and eight
new interaction triggers. No new architecture — each new trigger is a Harmony patch that raises
a `TriggerEvent` through `GuidanceDispatcher.Raise`, exactly like the existing triggers.

See [`.claude/FEATURE_ROADMAP.md`](/.claude/FEATURE_ROADMAP.md) Phase 1.

---

## Overview

| Trigger type | Valheim hook (confirmed signature) | Subject |
|---|---|---|
| `kill` (+ `count`) | `Character.OnDeath` (existing) | creature prefab |
| `ward_activated` | `PrivateArea.Interact(Humanoid, bool hold, bool alt)` | — (type-only) |
| `tamed_creature` | `Tameable.Tame()` | creature prefab |
| `sign_read` | `Sign.Interact(Humanoid, bool hold, bool alt)` | — (type-only) |
| `crafting_table_used` | `CraftingStation.Interact(Humanoid, bool repeat, bool alt)` | station prefab |
| `cooking_used` | `CookingStation.Interact` + `Fireplace.Interact` | station prefab |
| `portal_used` | `TeleportWorld.Teleport(Player player)` | portal tag |
| `tombstone_picked` | `TombStone.Interact(Humanoid, bool hold, bool alt)` | — (type-only) |
| `ship_sailed` | `ShipControlls.Interact(Humanoid, bool repeat, bool alt)` | — (type-only) |

### Hook notes / deviations from the roadmap

- **`portal_used`** uses `TeleportWorld.Teleport(Player player)` (actual travel through the portal),
  not `TeleportWorld.Interact` (which only opens the tag-rename dialog). `Teleport` receives the
  traveling `Player`, so we fire only when it equals `Player.m_localPlayer`, and read the portal
  tag via `TeleportWorld.GetText()`.
- **`ship_sailed`** uses `ShipControlls.Interact` (taking the helm), since `Ship` exposes no
  per-frame interact hook. "Sailed" = the local player took control of a ship's rudder.
- **`tamed_creature`** patches `Tameable.Tame()` (taming completion). It runs on the ZDO owner of
  the creature; in single-player / host that is the local player, and on a client the nearby
  creature is client-owned, so `Player.m_localPlayer` is the tamer in the common case. Fires only
  when `Player.m_localPlayer != null`.

### Interaction-patch conventions

All interaction patches are postfixes that skip the hold/repeat continuation frame (`hold`/`repeat`
second bool arg) so a held key fires once. Two gating styles, chosen per the method's real return
semantics (confirmed by decompiling `assembly_valheim.dll`):

- **`__result == true` gate** — for methods that return `true` on success: `PrivateArea.Interact`
  (ward), `Sign.Interact`, `TombStone.Interact`. Fires only when the interaction succeeded.
- **local-interactor gate** — for methods that return `false` on their success path:
  `CraftingStation.Interact` (success path ends in `return false` after `InventoryGui.Show`),
  `CookingStation.Interact` / `Fireplace.Interact` (false on common add-food/fuel paths),
  `ShipControlls.Interact` (always `return false` after the RequestControl RPC). These fire when
  the interacting `user`/`character` argument is `Player.m_localPlayer` (Interact only ever runs
  locally with the interacting player). `ship_sailed` additionally requires
  `player.GetStandingOnShip() == m_ship` to mirror the helm-take precondition.

Dedupe across repeated presses is otherwise handled by the normal gates (`once`, `cooldown`).

---

## Kill Count

`trigger.count` already exists on `TriggerSpec` (used by `npc_item_submit` / `item_acquired`).
For `kill` it now means: fire only after `count` matching kills accumulate.

```yaml
- id: neck_hunter
  title: "Neck Hunter"
  trigger:
    type: kill
    creature: Neck
    count: 10          # fires after 10 kills; omit / <=1 = fire on each kill
  display:
    mode: message
    text: "You have slain 10 Necks!"
```

### Progress storage

A dedicated bucket `KillCountState` (`VSG.kc.<entry_id>`), mirroring `SubmitState` (`VSG.is.*`).
Kills cannot be recounted from inventory, so they need a persistent accumulator (unlike
`item_acquired` count goals which re-sum the inventory).

### Flow

- `GuidanceDispatcher.Raise` skips `kill` entries with `Count > 1` (delegated to the count path),
  the same way it skips `item_acquired` count-goal entries.
- `KillTrigger.Postfix` calls `KillCountTracker.CheckKillCount(creature, displayName)` after `Raise`.
- `CheckKillCount` increments the counter for each matching, gate-passing `kill` count entry; on
  reaching `count` it clears the counter and fires the entry via `GuidanceDispatcher.FireEntry`;
  otherwise it persists progress, shows a `MessageType.Center` `"<title>: <cur>/<goal>"` message,
  and refreshes the HUD tracker.
- HUD tracker shows an `X/Y` progress row for in-progress `kill` count entries that have a `title`
  (mirrors the `npc_item_submit` progress block).

---

## Config (`TriggerSpec`) new fields

```csharp
/// crafting_table_used / cooking_used: optional station prefab filter. Empty = any station.
public string Station { get; set; }
/// portal_used: optional portal tag filter. Empty = any portal.
public string Tag { get; set; }
```

`Creature` (kill / tamed_creature) and `Count` already exist.

---

## Dispatcher matching (`MatchesTrigger`)

```csharp
case "tamed_creature":      return string.IsNullOrEmpty(t.Creature) ? true : Eq(t.Creature, evt.Subject);
case "crafting_table_used": return string.IsNullOrEmpty(t.Station)  ? true : Eq(t.Station,  evt.Subject);
case "cooking_used":        return string.IsNullOrEmpty(t.Station)  ? true : Eq(t.Station,  evt.Subject);
case "portal_used":         return string.IsNullOrEmpty(t.Tag)      ? true : Eq(t.Tag,      evt.Subject);
case "ward_activated":
case "sign_read":
case "tombstone_picked":
case "ship_sailed":         return true;   // type-only
```

`kill` keeps `case "kill": return Eq(t.Creature, evt.Subject);` and gains a count-skip guard in `Raise`.

---

## Reset wiring

`KillCountState.ResetAll` / `.Clear` must be called everywhere the other progress buckets are:

| Location | Change |
|---|---|
| `AdminCommands.Reset` (`all`) | add `KillCountState.ResetAll(player)` |
| `AdminCommands.Reset` (`<id>`) | clear `VSG.kc.<id>` and include in the result message |
| `GuidanceSync` remote reset (`all`) | add `KillCountState.ResetAll(player)` |
| `GuidanceSync` remote reset (`<id>`) | clear `VSG.kc.<id>` and include in the result message |

---

## Files Changed

| File | Change |
|---|---|
| `src/Config/GuidanceConfig.cs` | Add `Station`, `Tag` to `TriggerSpec` |
| `src/State/KillCountState.cs` | New — `VSG.kc.*` accumulator (Get/Set/Clear/ResetAll) |
| `src/Triggers/KillTrigger.cs` | Call `KillCountTracker.CheckKillCount`; add `KillCountTracker` class |
| `src/Triggers/GuidanceDispatcher.cs` | Count-skip guard for `kill`; new `MatchesTrigger` cases |
| `src/Triggers/WardActivatedTrigger.cs` | New — `PrivateArea.Interact` |
| `src/Triggers/TamedCreatureTrigger.cs` | New — `Tameable.Tame` |
| `src/Triggers/SignReadTrigger.cs` | New — `Sign.Interact` |
| `src/Triggers/CraftingTableUsedTrigger.cs` | New — `CraftingStation.Interact` |
| `src/Triggers/CookingUsedTrigger.cs` | New — `CookingStation.Interact` + `Fireplace.Interact` |
| `src/Triggers/PortalUsedTrigger.cs` | New — `TeleportWorld.Teleport` |
| `src/Triggers/TombstonePickedTrigger.cs` | New — `TombStone.Interact` |
| `src/Triggers/ShipSailedTrigger.cs` | New — `ShipControlls.Interact` |
| `src/Display/GuidanceHudTracker.cs` | Add `kill` count progress rows |
| `src/Commands/AdminCommands.cs` | Reset `VSG.kc.*` (all + single id) |
| `src/Net/GuidanceSync.cs` | Reset `VSG.kc.*` (all + single id) |
| `.claude/criteria/CRIT-02-triggers.md` | Document the 8 new trigger types + `kill.count` |

---

## Criteria

- [x] `kill` with `count: N` fires only after N matching kills; `count` omitted/`<=1` fires on each kill.
- [x] Kill-count progress persists across logout in `VSG.kc.<id>` and shows an `X/Y` Center message.
- [x] Kill-count in-progress entries with a `title` show an `X/Y` row in the HUD tracker.
- [x] `vsg_reset <id>` and `vsg_reset all` clear `VSG.kc.*`; same for `vsg_reset_player`.
- [x] `ward_activated` fires when the local player toggles a ward (PrivateArea).
- [x] `tamed_creature` fires on taming completion; `creature:` filters by prefab, omitted = any.
- [x] `sign_read` fires when the local player interacts with a sign.
- [x] `crafting_table_used` fires on station use; `station:` filters by prefab, omitted = any.
- [x] `cooking_used` fires for both `CookingStation` and `Fireplace`; `station:` filter works.
- [x] `portal_used` fires only on actual travel for the local player; `tag:` filters by portal tag.
- [x] `tombstone_picked` fires when the local player loots a tombstone.
- [x] `ship_sailed` fires when the local player takes a ship's helm (once per press, not per hold frame).
- [x] Held-key interactions fire once (hold/repeat continuation frames are skipped).
- [x] Build is clean: `Build succeeded. 0 Warning(s) 0 Error(s)`.
