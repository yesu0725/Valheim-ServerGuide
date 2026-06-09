using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimServerGuide.Config;
using ValheimServerGuide.Display;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player picks up an item.
    /// YAML `trigger.item` supports a trailing wildcard: `"Trophy_*"` matches any trophy.
    ///
    /// When trigger.count > 1 (single-item goal) OR trigger.goals is set (multi-item goals),
    /// the entry does NOT fire on each individual pickup. Instead progress is tracked by
    /// summing the player's current inventory for each goal item after every acquisition
    /// (pickup or craft). The entry fires once ALL goals are reached simultaneously.
    /// Crafted items count via the companion ItemAcquiredCraftPatch.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
    internal static class ItemAcquiredTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(Humanoid __instance, GameObject go)
        {
            if (__instance != Player.m_localPlayer) return;
            if (go == null) return;

            var subject = TriggerUtils.NormalizePrefabName(go.name);
            if (string.IsNullOrEmpty(subject)) return;

            var drop = go.GetComponent<ItemDrop>();
            var displayName = drop?.m_itemData?.m_shared?.m_name;

            // count=1 / no-goals entries fire immediately via the normal dispatch path.
            // count-goal and multi-goal entries are skipped there and handled below.
            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "item_acquired",
                Subject = subject,
                DisplayName = displayName,
            });
            CheckCountGoals(subject, displayName);
        }

        /// Returns the effective goal list for a trigger:
        ///   - Goals list if present and non-empty
        ///   - Single-item list if Count > 1 and Item is set
        ///   - null if the trigger is a plain single-pickup (count <= 1, no Goals)
        internal static List<ItemGoalSpec> GetEffectiveGoals(TriggerSpec trigger)
        {
            if (trigger == null) return null;
            if (trigger.Goals != null && trigger.Goals.Count > 0) return trigger.Goals;
            if (!string.IsNullOrEmpty(trigger.Item) && trigger.Count > 1)
                return new List<ItemGoalSpec> { new ItemGoalSpec { Item = trigger.Item, Count = trigger.Count } };
            return null;
        }

        private static bool IsCountGoalEntry(GuidanceEntry entry)
        {
            if (entry.Trigger == null) return false;
            if (!string.Equals(entry.Trigger.Type, "item_acquired", StringComparison.OrdinalIgnoreCase)) return false;
            return GetEffectiveGoals(entry.Trigger) != null;
        }

        /// Scans all item_acquired count-goal entries regardless of which item was just picked up.
        /// Seeds HUD progress from existing inventory on spawn or config reload.
        internal static void CheckAllCountGoals()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null) return;

            foreach (var entry in config.Guidances)
            {
                if (!IsCountGoalEntry(entry)) continue;
                if (!GuidanceDispatcher.CheckGates(entry, player)) continue;

                var goals = GetEffectiveGoals(entry.Trigger);
                var allMet = true;
                var anyProgress = false;

                foreach (var g in goals)
                {
                    var cur = CountInInventory(player, g.Item);
                    Plugin.Log.LogInfo($"[item_acquired] '{entry.Id}' seed {g.Item}: {cur}/{g.Count}.");
                    if (cur < g.Count) allMet = false;
                    if (cur > 0) anyProgress = true;
                }

                if (allMet)
                {
                    Plugin.Log.LogInfo($"[item_acquired] '{entry.Id}' all goals already met — firing.");
                    GoalStartedState.Clear(player, entry.Id);
                    GuidanceDispatcher.FireEntry(entry, new TriggerEvent
                    {
                        Type = "item_acquired",
                        Subject = goals[0].Item,
                    });
                    GuidanceHudTracker.Instance?.FlashCompletion(entry.Id);
                }
                else if (anyProgress || GoalStartedState.IsStarted(player, entry.Id))
                {
                    if (anyProgress) GoalStartedState.MarkStarted(player, entry.Id);
                    GuidanceHudTracker.Instance?.Refresh(fromProgress: true);
                }
            }
        }

        /// Called after each pickup/craft. Checks all count-goal entries whose goals include
        /// prefabName, then fires or refreshes the tracker as appropriate.
        internal static void CheckCountGoals(string prefabName, string displayName)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null) return;

            foreach (var entry in config.Guidances)
            {
                if (!IsCountGoalEntry(entry)) continue;

                var goals = GetEffectiveGoals(entry.Trigger);

                // Only process entries where the acquired item is relevant to at least one goal.
                var relevant = false;
                foreach (var g in goals)
                    if (ItemWildcardMatch(g.Item, prefabName)) { relevant = true; break; }
                if (!relevant) continue;

                if (!GuidanceDispatcher.CheckGates(entry, player)) continue;

                var allMet = true;
                var anyProgress = false;
                foreach (var g in goals)
                {
                    var cur = CountInInventory(player, g.Item);
                    Plugin.Log.LogInfo($"[item_acquired] '{entry.Id}' {g.Item}: {cur}/{g.Count}.");
                    if (cur < g.Count) { allMet = false; }
                    if (cur > 0) anyProgress = true;
                }

                if (allMet)
                {
                    Plugin.Log.LogInfo($"[item_acquired] '{entry.Id}' all goals reached — firing.");
                    GoalStartedState.Clear(player, entry.Id);
                    GuidanceDispatcher.FireEntry(entry, new TriggerEvent
                    {
                        Type = "item_acquired",
                        Subject = prefabName,
                        DisplayName = displayName,
                    });
                    GuidanceHudTracker.Instance?.FlashCompletion(entry.Id);
                }
                else
                {
                    // Latch "started" the moment any progress is seen so the entry stays visible
                    // even if these items are later removed from the inventory.
                    if (anyProgress) GoalStartedState.MarkStarted(player, entry.Id);
                    GuidanceHudTracker.Instance?.Refresh(fromProgress: true);
                }
            }
        }

        /// Sum of all inventory stack sizes for items whose prefab name matches prefabPattern.
        /// Supports a trailing `*` wildcard (e.g. `"Trophy*"`). Sums across all matching stacks.
        internal static int CountInInventory(Player player, string prefabPattern)
        {
            if (string.IsNullOrEmpty(prefabPattern)) return 0;
            var inv = player.GetInventory();
            if (inv == null) return 0;

            var total = 0;
            foreach (var item in inv.GetAllItems())
            {
                if (item?.m_dropPrefab == null) continue;
                var name = TriggerUtils.NormalizePrefabName(item.m_dropPrefab.name);
                if (ItemWildcardMatch(prefabPattern, name))
                    total += item.m_stack;
            }
            return total;
        }

        /// Builds a one-line-per-goal progress summary. Capped at goal so counts never exceed target.
        /// Example: "FineWood: 18/30\nCoal: 12/25"
        internal static string BuildGoalProgressText(Player player, List<ItemGoalSpec> goals)
        {
            if (player == null || goals == null || goals.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var g in goals)
            {
                var cur = System.Math.Min(CountInInventory(player, g.Item), g.Count);
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(g.Item).Append(": ").Append(cur).Append('/').Append(g.Count);
            }
            return sb.ToString();
        }

        internal static bool ItemWildcardMatch(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(value)) return false;
            if (pattern.EndsWith("*"))
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// Hooks into DoCrafting so that item_acquired count-goal entries also make progress
    /// when items are gained via crafting (crafted items bypass Humanoid.Pickup entirely).
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class ItemAcquiredCraftPatch
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance, Player player)
        {
            if (player != Player.m_localPlayer) return;
            var recipe = __instance.m_craftRecipe;
            if (recipe?.m_item == null) return;

            var subject = TriggerUtils.NormalizePrefabName(recipe.m_item.gameObject.name);
            if (string.IsNullOrEmpty(subject)) return;

            var displayName = recipe.m_item.m_itemData?.m_shared?.m_name;
            ItemAcquiredTrigger.CheckCountGoals(subject, displayName);
        }
    }
}
