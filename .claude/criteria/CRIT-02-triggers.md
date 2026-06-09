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
- **Source:** `NpcConversationTrigger.cs` (not raised via `GuidanceDispatcher.Raise` — opened directly by the hold detector)
- **Harmony patches:**
  - `Trader.Interact` Prefix — intercepts `hold=false` (key-down frame) to start a 0.5 s
    hold timer, suppressing the vanilla store open. `NpcConversationHoldDetector.Update()`
    resolves outcome each frame: release before threshold → store opens; threshold reached →
    `GuidanceDisplay.Show()` with `mode: conversation`.
  - `Trader.GetHoverText` Postfix — appends `"\n[Hold E] Quest"` when a gated entry exists.
- **Subject:** trader prefab name (same format as `npc_interacted`)
- **YAML field matched:** `trigger.npc`
- **Short-press E** is unaffected — vanilla store opens and `npc_interacted` fires as normal.

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
- **Subject:** `trigger.id` value from YAML
- **YAML fields matched:** `trigger.id` (required — stable identifier; becomes evt.Subject; falls back to entry.Id in the coroutine but dispatcher's `Eq` rejects null trigger.id, so omitting it means the entry never fires), `trigger.interval` (`"daily"` | `"hourly"` | raw float **seconds** — shorthand like `"30m"` or `"1h"` is **not** parsed and returns 0, causing the entry to be skipped)
- **Limitation — chain steps only:** `timed` **cannot be used inside chain steps**. `TimedTrigger.OnConfigChanged` only scans top-level `entry.Trigger`; step-level timed triggers never get a coroutine started and silently never fire. Use `timed` exclusively on **single-entry** (non-chain) guidances. To sequence timed events one after another, convert each step to a standalone entry and gate subsequent entries with `requires: [previous_entry_id]`.

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
- **Subject:** location prefab name from `ZoneSystem.instance.m_locationInstances` (e.g., `"Vendor_BlackForest"`).
- **YAML fields matched:** `trigger.location` (trailing `*` wildcard supported), `trigger.radius` (metres; default 50 when absent or zero).
- **Note:** `trigger.radius` is checked inside the trigger before the event is raised. The dispatcher matches only on location name.

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
     build               -> trigger.piece must match evt.Subject
     biome               -> trigger.biome must match evt.Subject
     item_acquired       -> trigger.item matches evt.Subject (trailing * wildcard supported)
     location_entered    -> trigger.location matches evt.Subject (trailing * wildcard supported)
     npc_interacted /
     npc_conversation    -> trigger.npc matches evt.Subject
     npc_item_submit     -> trigger.npc matches evt.Subject; trigger.item matches Extra["item"]
                            (empty trigger.item = match any item submitted to that NPC)
     skill_level         -> trigger.skill matches skill part of "Skill:level"; trigger.level == level part
     timed               -> trigger.id matches evt.Subject
     entry_finished      -> trigger.entry matches evt.Subject (the completed entry's ID)
     first_login / chest_opened / player_death -> type match only (no subject filter)
     (anything else)     -> match succeeds
```

---

## TriggerEvent Shape

```csharp
public class TriggerEvent
{
    public string Type;          // any trigger type string above
    public string Subject;       // prefab name / biome / "Skill:level" / ""
    public string DisplayName;   // localized display name when available
    public Dictionary<string, object> Extra;  // reserved
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
public string Interval;    // timed: "daily" | "hourly" | raw float seconds ONLY — "30m"/"1h" etc. are NOT parsed
public string Id;          // timed: REQUIRED — stable identifier matching evt.Subject; null = entry never fires
public int    MaxFires;    // optional: cap total fires per player (player_death, others)
public string Entry;       // entry_finished: the completed entry's ID
public int    Count = 1;   // npc_item_submit: items required before firing (>1 = progress bar)
public bool   Consume = true; // npc_item_submit: remove submitted items (partial-stack aware)
public float  Radius;      // reserved
public string DamageType;  // reserved
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
- [x] `timed` events originate server/host-side only; pure clients receive via RPC.
- [x] `player_death` respects `trigger.max_fires` if set.
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
- [x] `npc_conversation` hold-E suppresses the vanilla store; short-press E still opens the store normally.
- [x] `npc_conversation` hold threshold is 0.5 s, tracked by `NpcConversationHoldDetector.Update()`.
- [x] `npc_conversation` `trigger.npc` matching is case-insensitive.
- [x] `npc_conversation` trader hover text gains `[Hold E] Quest` when a gated entry exists.
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
