using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player successfully equips an item.
    /// Humanoid.EquipItem(ItemData item, bool triggerEquipEffects) returns true when the
    /// item became equipped. We postfix it, guard for the local player, and only raise on
    /// a successful equip (so unequips / failed equips don't fire).
    ///
    /// Note: EquipItem can also run when the game restores a character's equipped items on
    /// spawn/load. Entry-level firing semantics (once / cooldown) dedupe those repeats.
    ///
    /// Item identity = m_dropPrefab.name (prefab, e.g. "BronzeSword"), matching the craft /
    /// item_acquired / npc_item_submit convention. Falls back to the shared-name token.
    /// YAML field matched: trigger.item.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    internal static class EquipTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result)
        {
            if (__instance != Player.m_localPlayer) return;
            if (!__result) return;                 // not actually equipped (toggle-off / failed)
            if (item == null) return;

            var subject = ResolveItemName(item);
            if (string.IsNullOrEmpty(subject)) return;

            Plugin.Log.LogInfo($"[equip] subject='{subject}' (token='{item.m_shared?.m_name}').");

            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "equip",
                Subject = subject,
                DisplayName = item.m_shared?.m_name,
            });
        }

        /// Prefab name for an item ("BronzeSword", …), matching craft / item_acquired.
        /// Falls back to the shared-name token when m_dropPrefab is null.
        private static string ResolveItemName(ItemDrop.ItemData item)
        {
            var n = item.m_dropPrefab?.name;
            if (!string.IsNullOrEmpty(n)) return TriggerUtils.NormalizePrefabName(n);
            return TriggerUtils.NormalizePrefabName(item.m_shared?.m_name ?? "");
        }
    }
}
