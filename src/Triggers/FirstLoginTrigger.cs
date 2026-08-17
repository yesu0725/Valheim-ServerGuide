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
            RunIfNeeded(__instance);
        }

        /// Also called from PlayerProgress once a client's progress file arrives: on a pure
        /// client the store is still in flight when OnSpawned runs, and setting the guard key
        /// against an empty store would consume the one-shot without ever firing it.
        internal static void RunIfNeeded(Player player)
        {
            if (player == null || !PlayerProgress.IsReady) return;
            if (SeenTracker.HasFired(player, GuardKey)) return;
            SeenTracker.MarkFired(player, GuardKey);

            GuidanceDispatcher.Raise(new TriggerEvent { Type = "first_login", Subject = "" });
        }
    }
}
