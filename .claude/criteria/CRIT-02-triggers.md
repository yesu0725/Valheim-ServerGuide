# CRIT-02 — Trigger Types

**Dispatcher:** `src/Triggers/GuidanceDispatcher.cs`
**Trigger sources:** `src/Triggers/` (one file per trigger type)

---

## Implemented Trigger Types

### `craft`
- **Source:** `CraftTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]` — Postfix
- **Subject:** `__instance.m_craftRecipe.m_item.gameObject.name` (prefab name, no suffix stripping needed)
- **YAML field matched:** `trigger.item`

### `kill`
- **Source:** `KillTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]` — Postfix
- **Subject:** prefab name with `"(Clone)"` suffix stripped via `NormalizePrefabName()`
- **Guard:** only fires when `__instance.m_lastHit.GetAttacker() == Player.m_localPlayer`
- **YAML field matched:** `trigger.creature`
- **`trigger.count`** (int, default `1`): when `> 1`, the entry does **not** fire on each kill.
  `KillCountTracker.CheckKillCount` (in `KillTrigger.cs`) accumulates a persistent per-character
  counter (`KillCountState`, key `VSG.kc.<id>`) for each matching, gate-passing entry, shows a
  `current/goal` Center message + HUD row while collecting, and fires the entry via
  `FireEntry` + `FlashCompletion` at `count/count`. Unlike `item_acquired` (which re-sums the
  inventory), kills are a true accumulator — they cannot be recounted, so the count persists in
  `m_customData`. The `Raise()` path skips `kill` entries with `count > 1`.
- **Reset:** `vsg_reset <id>` clears `VSG.kc.<id>`; `vsg_reset all` / `vsg_reset_player` call
  `KillCountState.ResetAll` / `.Clear`.
- **`trigger.share_progress`** (bool, default `false`, Phase 6 — see
  [CRIT-24](/.claude/criteria/CRIT-24-phase6-system-polish.md)): when `true` and `count > 1`,
  each kill also broadcasts a `VSG_ShareKillProgress` RPC; any other connected player within 50m
  of the kill credits their own counter for the same entry too.

### `ward_activated`
- **Source:** `WardActivatedTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.Interact))]` — Postfix
- **Guard:** `Player.m_localPlayer != null`; skips the hold-continuation frame; fires only when `__result == true` (ward toggled / permitted)
- **Subject:** `""` (type match only)
- **YAML field matched:** none

### `tamed_creature`
- **Source:** `TamedCreatureTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(Tameable), nameof(Tameable.Tame))]` — Postfix
- **Guard:** `Player.m_localPlayer != null`. `Tame()` is the taming-completion call; it runs on the creature's ZDO owner (local player in single-player/host, the nearby client-owned creature on a client).
- **Subject:** creature prefab name (`"(Clone)"` stripped); display name from `m_character.m_name`
- **YAML field matched:** `trigger.creature` (omit to match any tame)

### `sign_read`
- **Source:** `SignReadTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(Sign), nameof(Sign.Interact))]` — Postfix
- **Guard:** `Player.m_localPlayer != null`; skips hold-continuation; `__result == true`
- **Subject:** `""` (type match only)
- **YAML field matched:** none

### `crafting_table_used`
- **Source:** `CraftingTableUsedTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Interact))]` — Postfix
- **Guard:** interacting `user == Player.m_localPlayer`; skips the `repeat` continuation frame. Does **not** gate on `__result` — `CraftingStation.Interact` ends its success path with `return false` (after `InventoryGui.Show`).
- **Subject:** station prefab name (`"(Clone)"` stripped, e.g. `"piece_workbench"`, `"forge"`)
- **YAML field matched:** `trigger.station` (omit to match any station)

### `cooking_used`
- **Source:** `CookingUsedTrigger.cs` — two patch classes:
  - `[HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Interact))]` — Postfix
  - `[HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]` — Postfix
- **Guard:** interacting `user == Player.m_localPlayer`; skips hold-continuation. Does **not** gate on `__result` — both methods return `false` on common paths (CookingStation add-food / m_addFoodSwitch early-out; Fireplace fuel-limit).
- **Subject:** station prefab name (`"(Clone)"` stripped, e.g. `"piece_cookingstation"`, `"fire_pit"`)
- **YAML field matched:** `trigger.station` (omit to match any cooking station / fire)

### `portal_used`
- **Source:** `PortalUsedTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]` — Postfix
- **Guard:** fires only when the teleported `Player` argument equals `Player.m_localPlayer`. Uses `Teleport` (actual travel), **not** `Interact` (which only opens the tag-rename dialog).
- **Subject:** portal tag from `TeleportWorld.GetText()` (may be empty)
- **YAML field matched:** `trigger.tag` (omit to match any portal)

### `tombstone_picked`
- **Source:** `TombstonePickedTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(TombStone), nameof(TombStone.Interact))]` — Postfix
- **Guard:** `Player.m_localPlayer != null`; skips hold-continuation; `__result == true` (loot permitted)
- **Subject:** `""` (type match only)
- **YAML field matched:** none

### `ship_sailed`
- **Source:** `ShipSailedTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.Interact))]` — Postfix
- **Guard:** interacting `character == Player.m_localPlayer` AND `player.GetStandingOnShip() == m_ship`; skips the `repeat` continuation frame. Does **not** gate on `__result` — `ShipControlls.Interact` always `return false` after firing the RequestControl RPC. `Ship` exposes no per-frame interact hook, so taking the rudder is the "sailing" signal.
- **Subject:** `""` (type match only)
- **YAML field matched:** none

### `first_login`
- **Source:** `FirstLoginTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]` — Postfix
- **Guard:** `m_customData` key `"first_login_fired"` — set on first fire, never fires again per character
- **Subject:** `""` (type match only; no subject filter)
- **YAML field matched:** none

### `chest_opened`
- **Source:** `ChestOpenedTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(Container), nameof(Container.Interact))]` — Postfix
- **Guard:** `SeenTracker` key `"chest_opened_fired"` — fires once per character
- **Subject:** `""` (type match only)
- **YAML field matched:** none

### `boss_defeated`
- **Source:** `BossDefeatedTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]` — Postfix
- **Guard:** `__instance.IsBoss() == true` AND `m_lastHit.GetAttacker() == Player.m_localPlayer`
- **Subject:** prefab name with `"(Clone)"` stripped (e.g., `"Eikthyr"`, `"gd_king"`, `"Bonemass"`, `"Dragon"`, `"GoblinKing"`)
- **YAML field matched:** `trigger.creature`

### `skill_level`
- **Source:** `SkillLevelTrigger.cs`
- **Harmony patch:** `Skills.RaiseSkill` — Prefix (captures previous level) + Postfix (compares, fires at whole-number crossings)
- **Subject:** `"{SkillType}:{level}"` e.g., `"Woodcutting:2"`
- **YAML fields matched:** `trigger.skill` (name), `trigger.level` (exact int threshold)

### `item_acquired`
- **Source:** `ItemAcquiredTrigger.cs`
- **Harmony patches:**
  - `[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]` — Postfix (picks up items from the ground / containers)
  - `[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]` — Postfix via `ItemAcquiredCraftPatch` (crafted items bypass `Humanoid.Pickup`, so this companion patch handles them)
- **Guard:** `__instance == Player.m_localPlayer` (pickup); `player == Player.m_localPlayer` (craft)
- **Subject:** prefab name with `"(Clone)"` stripped from `go.name`
- **YAML field matched:** `trigger.item` (supports trailing `*` wildcard, e.g., `"Trophy*"`)
- **`trigger.count`** (int, default `1`): when `> 1`, the entry does **not** fire on each individual pickup.
  Instead, progress is tracked as the player's **current inventory total** for that item (summed across
  all matching stacks). The entry fires once the inventory total reaches `trigger.count`. A `current/goal`
  progress bar is shown in the HUD tracker while collecting. Both picking up items AND crafting them count
  toward the goal. The progress bar disappears when the goal is met or the entry is fired.
  - Progress display: `0/200` → `20/200` → … → fires at `200/200`.
  - The tracker bar only appears once the player has at least 1 of the item (i.e., `cur > 0`).
  - If the player drops items the inventory count decreases accordingly (inventory-based, not cumulative).

### `location_entered`
- **Source:** `LocationEnteredTrigger.cs`
- **Harmony patch:** `Player.Update` — Postfix, polled every 5 seconds, 40 m radius
- **Guard:** per-location SeenTracker key `"loc_{prefabName}"` — fires at most once per location per character
- **Subject:** location prefab name from `ZoneSystem.instance.m_locationInstances`
- **YAML field matched:** `trigger.location` (supports `"*"` to match any location)

### `npc_interacted`
- **Source:** `NpcInteractedTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]` — Postfix
- **Subject:** trader prefab name (e.g., `"Haldor"`, `"Hildir"`, `"BogWitch"`)
- **YAML field matched:** `trigger.npc`

### `npc_conversation`
- **Source:** `NpcConversationTrigger.cs` (not raised via `GuidanceDispatcher.Raise` — opened directly from the Interact prefix)
- **Input:** **Shift + E**. `ConversationModifierHeld()` accepts either literal Shift key, plus the
  bound `Run` / `JoyRun` button (Shift by default, so rebinding still works and a gamepad has a
  way in).
- **Harmony patches:**
  - `Trader.Interact` Prefix — on the key-down frame with the modifier held and a gated entry
    available, opens the conversation (or the picker) and returns `false` to suppress the store.
    Every other press falls straight through to vanilla.
  - `Trader.GetHoverText` Postfix — appends `"\n[Shift + E] Quest"` when a gated entry exists.
- **Subject:** trader prefab name (same format as `npc_interacted`)
- **YAML field matched:** `trigger.npc`
- **Plain E** is untouched — the vanilla store opens immediately and `npc_interacted` fires as normal.

> **Superseded design — hold-E.** This used to be a 0.5 s hold: the prefix swallowed the first
> key-down and an `NpcConversationHoldDetector.Update()` loop decided, frames later, whether to
> re-open the store (early release) or the conversation (threshold reached). It made every
> ordinary trade wait half a second to find out what the player meant. A modifier is unambiguous
> on frame one, so the detector and its shared hold state are gone.

### `equip`
- **Source:** `EquipTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]` — Postfix
- **Guard:** `__instance == Player.m_localPlayer` AND `__result == true` (only a successful equip)
- **Subject:** `item.m_dropPrefab.name` (prefab, `"(Clone)"` stripped), falling back to the shared-name token
- **YAML field matched:** `trigger.item`
- **Note:** also fires when equipped items are restored on spawn/load; rely on `once`/`cooldown` to dedupe.

### `build`
- **Source:** `BuildTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]` — Postfix
- **Guard:** `__instance == Player.m_localPlayer` AND `__result == true` (placement actually succeeded)
- **Subject:** `piece.gameObject.name` with `"(Clone)"` stripped (e.g. `"piece_workbench"`, `"woodwall"`)
- **YAML field matched:** `trigger.piece`
- **Prefab-name tip:** piece prefab names drop separators — e.g. `"woodwall"` not `"wood_wall"`. Use the in-game piece name from the build menu to look them up in the Valheim wiki or with `vsg_list`.

### `npc_item_submit`
- **Source:** `NpcItemSubmitTrigger.cs`
- **Harmony patches:**
  - `Trader.UseItem(Humanoid user, ItemDrop.ItemData item)` Prefix — this is the `Interactable`
    hook the game calls when a hotbar item (keys 1-8) is used on a Trader. It returns `bool`:
    `true` = "item consumed" (no `$msg_cantuseon` message), `false` = not consumed (caller shows
    "You can't use X on Y"). We control vanilla via the prefix's `ref bool __result` + return value.
    Vanilla `Trader.UseItem` matches items by **`m_shared.m_name` token** (not prefab name) and
    Hildir's version always returns `true` (accept quest item or play `m_randomGiveItemNo` rejection).
  - `Trader.GetHoverText` Postfix (`NpcItemSubmitHoverPatch`) — appends the **same vanilla string**
    `"\n[<color=yellow><b>1-8</b></color>] $npc_giveitem"` for NPCs that have configured entries
    but empty `m_useItems` (Haldor, BogWitch). Vanilla only adds this when `m_useItems.Count > 0`
    (Hildir), so this mirrors the give-item prompt across all NPCs.
- **Subject:** trader prefab name (e.g. `"Haldor"`)
- **Extra:** `{ "item": "<itemPrefabName>" }` — matched against `trigger.item` in `MatchesTrigger`
- **Item identity:** `item.m_dropPrefab.name` (e.g. `"Wood"`), normalized — consistent with the
  craft / item_acquired triggers. Falls back to the shared-name token if `m_dropPrefab` is null.
- **YAML fields matched:** `trigger.npc` + `trigger.item` (optional; absent = catch-all)
- **`trigger.count`** (int, default `1`): total items required before the entry fires. `> 1` makes
  it a progressive collection quest — each submission accumulates toward the goal, a progress bar
  shows in the tracker + codex, and the entry's display/reward fires only at `count/count`.
  Progress persists per character in `m_customData` (`SubmitState`, key `VSG.is.<id>`).
- **`trigger.consume`** (bool, default `true`): whether submitted items are removed from the
  inventory. When a stack is submitted, only the number still required is consumed via
  `Inventory.RemoveItem(item, take)` where `take = min(remaining, stack)` — never the whole stack.
- **Progress handling** (`HandleSubmission`): single-count entries fire immediately (consume 1 if
  `consume`); multi-count entries consume `take`, advance `SubmitState`, show a center counter
  + `GuidanceHudTracker.Refresh(fromProgress)` while collecting, and `SubmitState.Clear` +
  `FireEntry` + `FlashCompletion` on reaching the goal.
- **Reset:** `vsg_reset <id>` clears the in-progress counter; `vsg_reset all` calls
  `SubmitState.ResetAll`.
- **Vanilla priority (in `Trader.UseItem` prefix):**
  1. Item is in `trader.m_useItems` (Hildir quest items, matched by token) → run vanilla; our trigger
     does NOT fire. Hildir's quest stays intact.
  2. Item matches a configured entry (specific `trigger.item`, then catch-all) → fire our trigger,
     `__result = true`, suppress vanilla (no block message).
  3. No configured match:
     - Hildir (`m_useItems.Count > 0`) → run vanilla so her `m_randomGiveItemNo` rejection plays.
     - Haldor/BogWitch with configured entries → consume silently (`__result = true`) so the ugly
       `$msg_cantuseon` does not appear.
     - Not our NPC at all → full vanilla path.
- **Catch-all pattern:** omit `trigger.item` to match any item submitted to that NPC. This is how
  to replicate Hildir's "I don't need that" rejection UX on other NPCs (add `once: false`,
  `mode: message`).
- **Specific vs. catch-all:** `FindEntry` prefers specific item matches over catch-alls regardless
  of YAML order.
- **Diagnostics:** every submission logs `[item_submit]` lines (resolved item name, token, NPC,
  decision) to the BepInEx console.

### `timed`
- **Source:** `TimedTrigger.cs`
- **Implementation:** `TimedTrigger.OnConfigChanged()` starts coroutines based on scope:
  - **player-scope** — every process (server, host, AND each pure client) starts its own coroutine and raises the event locally via `GuidanceDispatcher.Raise`. Per-player gates (`requires`, `once`, `cooldown`) are evaluated independently on each machine. The dedicated server does **not** broadcast player-scope timers.
  - **global-scope** — server/host starts the coroutine; dedicated server calls `GuidanceSync.BroadcastTimedGuidance(entryId)` so every client receives the event. Pure clients skip global timers (they wait for the RPC).
- **Subject:** `trigger.id`, falling back to `entry.id` when omitted — the same fallback on both the scheduling side (`TimedTrigger`) and the matching side (`GuidanceDispatcher.Matches`).
- **YAML fields matched:** `trigger.id` (optional), `trigger.interval` — seconds (`900`), a suffixed duration (`30s`, `15m`, `2h`, `1d`), or `daily` / `hourly`. Parsed with the invariant culture; anything else logs a warning and the entry is skipped.
- **First fire is one full interval after scheduling**, not immediately.
- **Limitation — top-level entries only:** `timed` cannot be used inside chain steps; `OnConfigChanged` only scans `entry.Trigger`. A step-level `timed` trigger now logs a warning at load instead of failing silently. To sequence timed events, make each one a standalone entry gated with `requires: [previous_entry_id]`.

#### Both halves of the wiring have to be present

This trigger is the one that spans processes, and it has failed twice for the same reason — one
side of the pair was missing:

| | Scheduling side | Matching side |
|---|---|---|
| **Where** | `TimedTrigger.OnConfigChanged`, called from `Plugin.OnConfigChanged` **and `GuidanceSync.OnReceive`** | `GuidanceDispatcher.Matches` |
| **Was broken because** | `Plugin.OnConfigChanged` returns early on any non-authoritative process, so a **client never scheduled its own player-scope timers** — and the dedicated server deliberately skips them. The entry was scheduled nowhere. | `MatchesTrigger` compared `evt.Subject` against `trigger.id` only, and `Eq` rejects a null left side — so an entry that omitted `trigger.id` fired on schedule with nothing listening. |

Config pushes no longer restart unchanged timers: a server-side YAML edit broadcasts to every
client, and restarting the coroutine reset the countdown, so a 15-minute timer on a server whose
config was touched more often than that would never complete an interval. `OnConfigChanged` now
diffs `(trigger id, interval, scope)` and only stops what actually changed.

### `player_death`
- **Source:** `PlayerDeathTrigger.cs`
- **Harmony patch:** `[HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]` — Postfix
- **Guard:** optional `trigger.max_fires` cap (stored in `m_customData` via `SeenTracker.IncrementFireCount`)
- **Subject:** `""` (type match only)
- **YAML field matched:** none (optional `trigger.max_fires`)

### `entry_finished`
- **Source:** `GuidanceDispatcher.cs` (no separate trigger file — raised internally)
- **Raised by:** `GuidanceDispatcher.Raise()` after a player-scope single entry fires;
  `GuidanceDispatcher.AdvanceChain()` when the final chain step completes;
  `GuidanceDispatcher.PlayGlobalReceived()` after a global entry is displayed.
- **Timing:** deferred — collected during the primary `Raise()` loop and fired in a second pass
  after the loop exits, so the list is never modified during iteration.
- **Subject:** the `Id` of the entry that just completed
- **YAML field matched:** `trigger.entry`

```yaml
- id: followup_tip
  trigger:
    type: entry_finished
    entry: some_other_entry_id
  display:
    mode: raven
    topic: "Next Step"
    text: "Great work! Here is what to do next..."
```

### `biome`
- **Source:** `BiomeTrigger.cs`
- **Harmony patch:** `Player.Update` Postfix — polled every 2 seconds; `Player.OnSpawned` Postfix resets last-biome on spawn.
- **Guard:** only fires when biome changes from the previous value; `Heightmap.Biome.None` transitions are ignored.
- **Subject:** `Heightmap.Biome.ToString()` — e.g., `"BlackForest"`, `"Swamp"`, `"Plains"`.
- **YAML field matched:** `trigger.biome` (case-insensitive)

### `distance`
- **Source:** `DistanceTrigger.cs`
- **Harmony patch:** `Player.Update` Postfix — polled every 5 seconds.
- **Guard:** per-location SeenTracker key `"dist_{prefabName}"` — fires at most once per location per character.
- **Subject:** location prefab name (e.g., `"Vendor_BlackForest"`).
- **Candidate sources (all three polled each tick, de-duplicated by prefab name):**
  1. `Location.s_allLocations` — location prefabs actually spawned in the scene. The **only**
     source that works on a client connected to a dedicated server, so it is the primary path.
     `(Clone)` is stripped via `TriggerUtils.NormalizePrefabName`. Limited to the zones loaded
     around the player, which is far beyond the 50 m default radius.
  2. `ZoneSystem.instance.m_locationInstances` — the world's full location list. It is filled by
     world generation, which runs **server-side only**, so on a dedicated-server client it is
     permanently empty (vanilla itself branches on `ZNet.IsServer()` before reading it — see
     `ZoneSystem.GetLocationIcons`). Useful on single-player / listen-server hosts, where it also
     catches placed-but-not-yet-spawned locations and prefabs with no `Location` component.
  3. `ZoneSystem.instance.m_locationIcons` — the position + name list the server pushes to every
     client over the vanilla `"LocationIcons"` RPC. Icon-bearing locations only (boss altars,
     vendors, …), but at any range, so a large `trigger.radius` still works for those on a
     dedicated server.
- **YAML fields matched:** `trigger.location` (trailing `*` wildcard supported), `trigger.radius` (metres; default 50 when absent or zero).
- **Note:** `trigger.radius` is checked inside the trigger before the event is raised. The dispatcher matches only on location name.
- **Note:** the poll returns immediately when no entry in the config uses a `distance` trigger, and
  candidates farther away than the largest configured radius are dropped before per-entry matching.

### `time_of_day`
- **Source:** `TimeTrigger.cs`
- **Implementation:** `TimeTrigger.Start()` runs one 30-second poll coroutine (started once from `Plugin.Awake()`, not config-driven). Each tick evaluates every entry's own condition directly and calls `GuidanceDispatcher.CheckGates` + `FireEntry` — it does **not** route through `Raise()`/`MatchesTrigger` because each entry has its own target, not a single "now" subject to match.
- **Condition:** `|EnvMan.instance.GetDayFraction() - trigger.game_time_fraction| <= trigger.window` (difference wraps across midnight, so `0.0`/`1.0` are adjacent).
- **YAML fields matched:** `trigger.game_time_fraction` (0.0 = midnight, 0.5 = noon), `trigger.window` (± tolerance, fraction of a day; default `0.02`)

### `day_number`
- **Source:** `TimeTrigger.cs` (same poll coroutine as `time_of_day`)
- **Condition:** `EnvMan.instance.GetDay() == int.Parse(trigger.day) && EnvMan.instance.GetDayFraction() >= 0.25f`
- **YAML field matched:** `trigger.day` (in-game day counter; e.g. `"7"`)
- **Note:** `GetDay()` alone ticks over at midnight (fraction 0.0), but vanilla's "Day N" message
  (`EnvMan.OnMorning`) doesn't fire until the fraction crosses 0.25 (morning). The `>= 0.25f`
  check aligns this trigger with that announcement instead of firing at night, hours early.

### `real_world_time`
- **Source:** `TimeTrigger.cs` (same poll coroutine)
- **Condition:** `DateTime.UtcNow.Hour == trigger.utc_hour && DateTime.UtcNow.Minute == trigger.utc_minute`
- **YAML fields matched:** `trigger.utc_hour` (0-23), `trigger.utc_minute` (0-59)
- **Note:** the 30s poll means the matching minute is checked at most twice; a long-running entry should use `once: true` (fires once ever) or a `cooldown` of ~23h (fires once per day) to avoid the minute being hit by 1-2 consecutive ticks counting as separate matches once gates are otherwise satisfied.

### `day_of_week`
- **Source:** `TimeTrigger.cs` (same poll coroutine)
- **Condition:** `DateTime.UtcNow.DayOfWeek.ToString()` equals `trigger.day` (case-insensitive)
- **YAML field matched:** `trigger.day` (real-world UTC weekday name, e.g. `"Saturday"`)
- **Note:** `day_number` and `day_of_week` share the YAML key `day`; `TriggerSpec.Day` is a `string` — `day_number` parses it as an int, `day_of_week` matches it as a weekday name.

---

## External-Integration Triggers (Lost Scrolls II)

These trigger types have **no in-repo source file** — they are not raised by any ServerGuide
Harmony patch. Instead the companion mod **Lost Scrolls II** raises them by calling ServerGuide's
public `GuidanceDispatcher.Raise(new TriggerEvent { Type = "...", Subject = "...", Extra = {...} })`.
ServerGuide only supplies the *matching* + *templating* side here. Both mods must be installed for
these to fire. See CRIT-13 for the full `{token}` list each one makes available.

### `dvergr_recruited`
- **Raised by:** Lost Scrolls II when a player frees (communes with) a corrupted Dvergr.
- **Subject:** the freed Dvergr's caste name (`Rogue` | `FireMage` | `IceMage` | `SupportMage`).
- **YAML field matched:** `trigger.caste` (empty = match any caste).

### `dvergr_duel_won`
- **Raised by:** Lost Scrolls II when a player wins a 1v1 Dvergr duel.
- **Subject:** the **winner's** caste name.
- **YAML field matched:** `trigger.caste` (empty = match any caste).
- **`Extra`:** `companionName`, `opponent`, `caste` (also mirrored as `evt.Subject`).

### `dvergr_level_up`
- **Raised by:** Lost Scrolls II when a recruited Dvergr gains a level.
- **Subject:** `"Caste:level"` (e.g. `"Rogue:3"`), mirroring `skill_level`.
- **YAML fields matched:** `trigger.caste` (empty = any caste) and `trigger.level`
  (`0` or omitted = any level — fires on every level-up). Both filters are optional and ANDed.
- **Helper:** `MatchDvergrLevelUp` in `GuidanceDispatcher.cs`.

### `dvergr_rank_changed`
- **Raised by:** Lost Scrolls II when a companion crosses into (or moves within) the top ranks
  of the server's 1v1 Duel Ladder.
- **Subject:** the companion's caste name.
- **YAML field matched:** `trigger.caste` (empty = match any caste).
- **`Extra`:** `rank`, `rating`, `companionName`, `ownerName`.

### `dvergr_rank_first`
- **Raised by:** Lost Scrolls II only when a companion reaches **rank #1** on the Duel Ladder (a
  genuine climb to the top, not just re-entering the top 3) — the "new champion" moment.
- **Subject:** the companion's caste name.
- **YAML field matched:** `trigger.caste` (empty = match any caste).
- **`Extra`:** `rank` (always `"1"`), `rating`, `companionName`, `ownerName`.

### `dvergr_party_duel_won`
- **Raised by:** Lost Scrolls II when a player's party of companions wins a team-vs-team duel.
- **Subject:** the winning party's owner name. **No `caste` filter** — party duels aren't
  caste-scoped, so `trigger.caste` is ignored for this type.
- **`Extra`:** `partyName` (falls back to the owner's name if unnamed), `winSize`,
  `opponentOwner`, `mvpCaste`, `ownerName`.

### `dvergr_party_rank_changed`
- **Raised by:** Lost Scrolls II when a party crosses into (or moves within) the top ranks of the
  Party Ladder. No `caste` filter (party-scoped, not caste-scoped).
- **`Extra`:** `rank`, `rating`, `partyName`, `ownerName`.

### `dvergr_party_rank_first`
- **Raised by:** Lost Scrolls II only when a party reaches **rank #1** on the Party Ladder. No
  `caste` filter.
- **`Extra`:** `rank` (always `"1"`), `rating`, `partyName`, `ownerName`.

### `dvergr_tournament_joined`
- **Raised by:** Lost Scrolls II when a player registers for a bracket tournament.
- **Subject:** the caste name entered (1v1 tournaments) or the literal string `"party"` (party
  tournaments).
- **YAML field matched:** `trigger.caste` (empty = match any caste/mode).

### `dvergr_tournament_match`
- **Raised by:** Lost Scrolls II when a round's pairing is set, announced to both players.
- **Subject:** same as `dvergr_tournament_joined` (caste name or `"party"`).
- **YAML field matched:** `trigger.caste` (empty = match any).
- **`Extra`:** `round`, `opponent`.

### `dvergr_tournament_won`
- **Raised by:** Lost Scrolls II for the tournament winner only.
- **Subject:** caste name or `"party"`.
- **YAML field matched:** `trigger.caste` (empty = match any).
- **`Extra`:** `mode`, `bracketSize`.

---

## Placeholder Types (not yet implemented)

### `pickup`
- **Trigger event type:** `"pickup"`
- **YAML field matched:** `trigger.item`
- **Status:** superseded by `item_acquired`

### `discover_location`
- **Trigger event type:** `"discover_location"`
- **YAML field matched:** `trigger.location` (location prefab name)
- **Status:** not yet implemented (hook into map fog-of-war reveal; `Minimap.RevealSharedMapData` or `ZoneSystem`)
- **Planned display mode:** `raven`
- **Do not use in YAML** — entries with this type will never fire. Use `location_entered` (which fires on proximity, not map reveal) as the closest available substitute.

---

## Dispatcher Matching Logic (`GuidanceDispatcher.Matches` / `MatchesTrigger`)

```
1. trigger.type must match evt.Type (case-insensitive)
2. switch on evt.Type:
     craft/pickup/equip  -> trigger.item must match evt.Subject (case-insensitive, exact)
     kill / boss_defeated-> trigger.creature must match evt.Subject
     tamed_creature      -> trigger.creature matches evt.Subject (empty = any)
     crafting_table_used -> trigger.station matches evt.Subject (empty = any)
     cooking_used        -> trigger.station matches evt.Subject (empty = any)
     portal_used         -> trigger.tag matches evt.Subject (empty = any)
     ward_activated / sign_read / tombstone_picked / ship_sailed -> type match only
     build               -> trigger.piece must match evt.Subject
     biome               -> trigger.biome must match evt.Subject
     item_acquired       -> trigger.item matches evt.Subject (trailing * wildcard supported)
     location_entered    -> trigger.location matches evt.Subject (trailing * wildcard supported)
     npc_interacted /
     npc_conversation    -> trigger.npc matches evt.Subject
     npc_item_submit     -> trigger.npc matches evt.Subject; trigger.item matches Extra["item"]
                            (empty trigger.item = match any item submitted to that NPC)
     skill_level         -> trigger.skill matches skill part of "Skill:level"; trigger.level == level part
     timed               -> trigger.id (or entry.id when omitted) matches evt.Subject
     entry_finished      -> trigger.entry matches evt.Subject (the completed entry's ID)
     dvergr_recruited /
     dvergr_duel_won /
     dvergr_rank_changed /
     dvergr_rank_first   -> trigger.caste matches evt.Subject (empty = any caste)
     dvergr_level_up     -> trigger.caste matches caste part of "Caste:level" (empty = any);
                            trigger.level matches level part (0 = any level)
     dvergr_party_duel_won /
     dvergr_party_rank_changed /
     dvergr_party_rank_first -> type match only (no caste filter — party-scoped, not caste-scoped)
     dvergr_tournament_joined /
     dvergr_tournament_match /
     dvergr_tournament_won   -> trigger.caste matches evt.Subject (empty = any; Subject is
                                 the caste name or the literal "party")
     first_login / chest_opened / player_death -> type match only (no subject filter)
     (anything else)     -> match succeeds

   time_of_day / day_number / real_world_time / day_of_week are NOT scanned by Raise()/
   MatchesTrigger at all — TimeTrigger.cs's 30s poll evaluates each entry's own condition
   directly and calls CheckGates()/FireEntry() itself (see the "time_of_day" section above).
```

---

## TriggerEvent Shape

```csharp
public class TriggerEvent
{
    public string Type;          // any trigger type string above
    public string Subject;       // prefab name / biome / "Skill:level" / caste / owner name / ""
    public string DisplayName;   // localized display name when available
    public Dictionary<string, object> Extra;  // key/value bag for {token} expansion (CRIT-13) —
                                               // used by the Lost Scrolls II ranking/tournament
                                               // triggers above (rank, rating, companionName,
                                               // ownerName, partyName, winSize, opponentOwner,
                                               // mvpCaste, round, opponent, mode, bracketSize)
}
```

---

## TriggerSpec Fields (GuidanceConfig.cs)

```csharp
public string Type;        // trigger type
public string Item;        // craft | pickup | equip | item_acquired
public string Creature;    // kill | boss_defeated
public string Piece;       // build
public string Biome;       // biome
public string Location;    // location_entered
public string Skill;       // skill_level
public int    Level;       // skill_level threshold
public string Npc;         // npc_interacted | npc_conversation | npc_item_submit
public string Interval;    // timed: seconds ("900") | suffixed ("30s"/"15m"/"2h"/"1d") | "daily" | "hourly"
public string Station;     // crafting_table_used | cooking_used (prefab filter; empty = any)
public string Tag;         // portal_used (portal tag filter; empty = any)
public string Id;          // timed: optional stable identifier matching evt.Subject; defaults to entry.Id
public int    MaxFires;    // optional: cap total fires per player (player_death, others)
public string Entry;       // entry_finished: the completed entry's ID
public int    Count = 1;   // npc_item_submit / kill / item_acquired: count required before firing (>1 = progress)
public bool   Consume = true; // npc_item_submit: remove submitted items (partial-stack aware)
public float  Radius;      // reserved
public string DamageType;  // reserved
public float  GameTimeFraction; // time_of_day: target EnvMan.GetDayFraction() (0.0 = midnight, 0.5 = noon)
public float  Window = 0.02f;   // time_of_day: +/- tolerance around GameTimeFraction
public string Day;         // day_number (parsed as int) | day_of_week (matched as weekday name)
public int    UtcHour;     // real_world_time
public int    UtcMinute;   // real_world_time
public string Caste;       // dvergr_recruited | dvergr_duel_won | dvergr_level_up |
                            // dvergr_rank_changed | dvergr_rank_first | dvergr_tournament_*
                            // (empty = any caste). Ignored by the party-scoped dvergr_party_*
                            // types, which have no caste filter.
```

---

## Adding a New Trigger

1. Create `src/Triggers/<Name>Trigger.cs`.
2. Add a `[HarmonyPatch(...)]` class with a Postfix (or Prefix+Postfix if capturing before-state).
3. Construct a `TriggerEvent` with the correct `Type` and `Subject`.
4. Call `GuidanceDispatcher.Raise(evt)`.
5. Add the new `type` string to `MatchesTrigger` in `GuidanceDispatcher.cs`.
6. Add the matching YAML field to `TriggerSpec` in `GuidanceConfig.cs` if needed.
7. Document the new trigger in this file.

---

## Criteria

- [x] `craft` trigger fires after `InventoryGui.DoCrafting` completes successfully.
- [x] `kill` trigger fires only for deaths caused by `Player.m_localPlayer`.
- [x] `kill` / `boss_defeated` / `item_acquired` strip `"(Clone)"` from prefab names.
- [x] Subject matching is case-insensitive throughout.
- [x] A null or empty `trigger.item/creature/npc/etc.` never matches anything.
- [x] All trigger events are raised only on the local client (dispatcher guards `Player.m_localPlayer != null`).
- [x] `boss_defeated` only fires when `IsBoss() == true`.
- [x] `item_acquired` wildcard (`Trophy*`) matches any prefab starting with `"Trophy"`.
- [x] `item_acquired` `trigger.count > 1` tracks inventory-total progress; fires when inventory >= goal.
- [x] `item_acquired` count-goal progress includes both picked-up AND crafted items.
- [x] `item_acquired` count-goal progress sums across all matching stacks in inventory.
- [x] `item_acquired` count-goal shows a `current/goal` progress bar in the HUD tracker while `0 < cur < goal`.
- [x] `item_acquired` count-goal entries are skipped by the normal `GuidanceDispatcher.Raise()` path.
- [x] `location_entered` fires at most once per location per character (SeenTracker key).
- [x] `timed` GLOBAL-scope events originate server/host-side only; pure clients receive via RPC.
- [ ] `timed` PLAYER-scope entries are scheduled on each client from the config it receives over RPC, not only from a local YAML load.
- [ ] A `timed` entry with no `trigger.id` still fires (subject falls back to the entry id on both sides).
- [ ] `interval` accepts seconds, `30s`/`15m`/`2h`/`1d`, and `daily`/`hourly`; anything else warns and skips the entry.
- [ ] A config push that does not change a timer's schedule leaves its countdown running.
- [x] `player_death` respects `trigger.max_fires` if set.
- [x] `time_of_day` fires within `window` of `game_time_fraction`, wrapping correctly across midnight.
- [x] `day_number` fires once `EnvMan.GetDay()` reaches the configured `day` and morning has started (`GetDayFraction() >= 0.25`).
- [x] `real_world_time` fires at the configured UTC hour/minute.
- [x] `day_of_week` fires on the configured real-world weekday (UTC).
- [x] `skill_level` fires at each configured threshold exactly once per character.
- [x] `entry_finished` raises after a player-scope single entry fires.
- [x] `entry_finished` raises after a global-scope entry is displayed on each receiving client.
- [x] `entry_finished` raises when the final chain step completes (not on intermediate steps).
- [x] `entry_finished` events are deferred until the primary `Raise()` loop finishes.
- [x] `equip` trigger fires only on a successful local-player equip (`__result == true`).
- [x] `equip` subject is the item prefab name (`m_dropPrefab.name`, `"(Clone)"` stripped); matches `trigger.item`.
- [x] `build` trigger fires only on a successful local-player placement via `Player.TryPlacePiece` (`__result == true`).
- [x] `build` subject is the piece prefab name (`"(Clone)"` stripped); matches `trigger.piece`.
- [x] `build` piece prefab names have no separators (e.g. `woodwall`, not `wood_wall`); verified in-game.
- [x] `trigger.entry` matching is case-insensitive; null/absent never matches.
- [ ] `npc_conversation` Shift+E suppresses the vanilla store; plain E opens the store with no delay.
- [ ] `npc_conversation` accepts either Shift key and the bound Run/JoyRun button as the modifier.
- [x] `npc_conversation` `trigger.npc` matching is case-insensitive.
- [ ] `npc_conversation` trader hover text gains `[Shift + E] Quest` when a gated entry exists.
- [x] `npc_conversation` falls back to vanilla store when no matching entry exists or gates are not met.
- [x] `npc_item_submit` fires when the player presses hotbar key 1-8 near a Trader with a configured item.
- [x] `npc_item_submit` does NOT fire for items already in `trader.m_useItems` (Hildir vanilla quest items).
- [x] `npc_item_submit` specific `trigger.item` match takes priority over a catch-all (no `trigger.item`) entry.
- [x] `npc_item_submit` catch-all entry (no `trigger.item`) fires for any item not matched by a specific entry.
- [x] `npc_item_submit` suppresses the vanilla "$msg_cantuseon" message when our trigger fires.
- [x] `npc_item_submit` adds the vanilla `[1-8] $npc_giveitem` hover line to NPCs that have
      configured entries but no vanilla `m_useItems` (Haldor, BogWitch).
- [x] `npc_item_submit` Hildir rejection (`m_randomGiveItemNo`) still plays for items with no configured entry.
- [x] `npc_item_submit` `trigger.npc` and `trigger.item` matching is case-insensitive.
- [x] `npc_item_submit` `trigger.consume: true` removes the submitted item(s); `false` leaves them.
- [x] `npc_item_submit` consuming from a stack removes only the required number, never the whole stack.
- [x] `npc_item_submit` `trigger.count > 1` accumulates progress and fires only at count/count.
- [x] `npc_item_submit` multi-count progress shows a bar in the HUD tracker and the codex.
- [x] `npc_item_submit` multi-count progress persists across sessions (`SubmitState` in m_customData).
- [x] `npc_item_submit` `vsg_reset <id>` / `vsg_reset all` clear in-progress submission counters.
- [x] `kill` `trigger.count > 1` accumulates a persistent counter and fires only at count/count.
- [x] `kill` count progress persists across sessions (`KillCountState`, `VSG.kc.<id>`) and shows an `X/Y` HUD row + Center message.
- [x] `kill` count `vsg_reset <id>` / `all` / `vsg_reset_player` clear the accumulator.
- [x] `ward_activated` fires when the local player toggles a ward.
- [x] `tamed_creature` fires on taming completion; `trigger.creature` filters, omitted = any.
- [x] `sign_read` fires when the local player interacts with a sign.
- [x] `crafting_table_used` fires on station use; `trigger.station` filters, omitted = any.
- [x] `cooking_used` fires for both `CookingStation` and `Fireplace`; `trigger.station` filters.
- [x] `portal_used` fires only on actual travel for the local player; `trigger.tag` filters.
- [x] `tombstone_picked` fires when the local player loots a tombstone.
- [x] `ship_sailed` fires when the local player takes a ship's helm (once per press, not per hold frame).
