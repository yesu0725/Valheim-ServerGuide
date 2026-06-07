using HarmonyLib;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Triggers
{
    /// Fires exactly once per character — on the very first spawn, not on respawns.
    /// Player.OnSpawned is called every time the player spawns (including respawn after
    /// death), so we guard with a SeenTracker key rather than relying only on the
    /// dispatcher's `once` check.
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class FirstLoginTrigger
    {
        private const string GuardKey = "first_login_fired";

        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            if (SeenTracker.HasFired(__instance, GuardKey)) return;
            SeenTracker.MarkFired(__instance, GuardKey);

            GuidanceDispatcher.Raise(new TriggerEvent { Type = "first_login", Subject = "" });
        }
    }
}
