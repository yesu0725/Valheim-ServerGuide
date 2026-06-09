using System;
using HarmonyLib;
using UnityEngine;
using ValheimServerGuide.Display;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player picks up an item.
    /// YAML `trigger.item` supports a trailing wildcard: `"Trophy_*"` matches any trophy.
    ///
    /// When trigger.count > 1 the entry does NOT fire on each individual pickup. Instead
    /// progress is tracked by summing the player's current inventory for that item after every
    /// acquisition (pickup or craft). The entry fires once the total reaches trigger.count.
    /// A HUD progress bar shows current/goal while collecting. Crafted items count too via the
    /// companion ItemAcquiredCraftPatch (crafted items bypass Humanoid.Pickup).
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

            // count=1 (or absent) entries fire immediately via the normal dispatch path.
            // count>1 entries are skipped there and handled below.
            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "item_acquired",
                Subject = subject,
                DisplayName = displayName,
            });
            CheckCountGoals(subject, displayName);
        }

        /// Scans all item_acquired entries with count > 1 that match prefabName, sums the
        /// player's current inventory for that item, and fires or refreshes the tracker.
        internal static void CheckCountGoals(string prefabName, string displayName)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null) return;

            foreach (var entry in config.Guidances)
            {
                if (entry.Trigger == null) continue;
                if (!string.Equals(entry.Trigger.Type, "item_acquired",
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.Trigger.Count <= 1) continue;
                if (!ItemWildcardMatch(entry.Trigger.Item, prefabName)) continue;
                if (!GuidanceDispatcher.CheckGates(entry, player)) continue;

                var goal = entry.Trigger.Count;
                var current = CountInInventory(player, entry.Trigger.Item);

                Plugin.Log.LogInfo($"[item_acquired] '{entry.Id}' count progress: {current}/{goal}.");

                if (current >= goal)
                {
                    Plugin.Log.LogInfo($"[item_acquired] '{entry.Id}' goal reached — firing.");
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
