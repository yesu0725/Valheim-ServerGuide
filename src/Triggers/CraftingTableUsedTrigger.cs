using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player uses a crafting station (workbench, forge, etc.).
    /// Subject is the station prefab name so `trigger.station: piece_workbench` can filter;
    /// omit station to match any station.
    ///
    /// CraftingStation.Interact returns FALSE on its success path (it calls SetCraftingStation +
    /// InventoryGui.Show, then `return false`), so we must NOT gate on __result. Interact is only
    /// ever invoked locally with the interacting player as `user`, so we fire when `user` is the
    /// local player and this is not the repeat-continuation frame.
    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Interact))]
    internal static class CraftingTableUsedTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(CraftingStation __instance, Humanoid user, bool repeat)
        {
            if (repeat) return;
            if (user == null || user != Player.m_localPlayer) return;

            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "crafting_table_used",
                Subject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name),
                DisplayName = __instance.m_name,
            });
        }
    }
}
