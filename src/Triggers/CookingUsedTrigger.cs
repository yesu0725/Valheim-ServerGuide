using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player uses a cooking station (cooking_station, oven, etc.).
    /// Subject is the station prefab name; `trigger.station:` filters, omitted = any.
    ///
    /// CookingStation.Interact returns FALSE on common paths (early-out when m_addFoodSwitch is
    /// set; OnInteract returns false on the "add food" path), so we do NOT gate on __result.
    /// Interact runs locally with the interacting player as `user`; fire on a non-hold press.
    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Interact))]
    internal static class CookingStationUsedTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(CookingStation __instance, Humanoid user, bool hold)
        {
            if (hold) return;
            if (user == null || user != Player.m_localPlayer) return;

            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "cooking_used",
                Subject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name),
                DisplayName = __instance.m_name,
            });
        }
    }

    /// Sibling patch: a fireplace / hearth also counts as cooking_used (you add fuel / cook over it).
    /// Fireplace.Interact returns mixed results (true on toggle, false on add-fuel limits), so we
    /// fire on a non-hold local-player press rather than on __result.
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
    internal static class FireplaceUsedTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(Fireplace __instance, Humanoid user, bool hold)
        {
            if (hold) return;
            if (user == null || user != Player.m_localPlayer) return;

            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "cooking_used",
                Subject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name),
                DisplayName = __instance.m_name,
            });
        }
    }
}
