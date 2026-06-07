using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires whenever the local player dies.
    /// Use `trigger.max_fires` in YAML to cap how many times a tip is shown.
    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    internal static class PlayerDeathTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;

            GuidanceDispatcher.Raise(new TriggerEvent { Type = "player_death", Subject = "" });
        }
    }
}
