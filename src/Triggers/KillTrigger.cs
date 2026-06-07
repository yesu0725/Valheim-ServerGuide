using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player is credited with a kill.
    /// Character.OnDeath runs on whoever owns the dying character's ZDO; we postfix
    /// it, inspect the last attacker, and only raise if it was Player.m_localPlayer.
    /// This means each client raises the event on its own machine when *its* player
    /// is the killer — which is exactly what the dispatcher wants for both player-
    /// scope and global-scope entries (global ones get routed to the server from
    /// the killer's client).
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class KillTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(Character __instance)
        {
            if (Player.m_localPlayer == null) return;          // dedicated server has none
            var attacker = __instance?.m_lastHit?.GetAttacker();
            if (attacker == null) return;
            if (attacker != Player.m_localPlayer) return;       // someone else killed it

            var subject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name);
            if (string.IsNullOrEmpty(subject)) return;

            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "kill",
                Subject = subject,
                DisplayName = __instance.m_name,
            });
        }
    }
}
