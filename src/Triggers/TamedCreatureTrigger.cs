using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when a creature finishes being tamed. Tameable.Tame() is the taming-completion call;
    /// it runs on the creature's ZDO owner, which in single-player / host is the local player and
    /// on a client is the nearby (client-owned) creature being tamed. Subject is the creature
    /// prefab name so `trigger.creature: Boar` can filter; omit creature to match any tame.
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Tame))]
    internal static class TamedCreatureTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(Tameable __instance)
        {
            if (Player.m_localPlayer == null) return;

            var subject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name);
            if (string.IsNullOrEmpty(subject)) return;

            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "tamed_creature",
                Subject = subject,
                DisplayName = __instance.m_character?.m_name,
            });
        }
    }
}
