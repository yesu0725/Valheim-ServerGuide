using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player delivers the killing blow to a boss.
    /// Sibling patch to KillTrigger — both postfix Character.OnDeath; Harmony applies both.
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class BossDefeatedTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(Character __instance)
        {
            if (Player.m_localPlayer == null) return;
            if (!__instance.IsBoss()) return;

            var attacker = __instance?.m_lastHit?.GetAttacker();
            if (attacker != Player.m_localPlayer) return;

            var subject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name);
            if (string.IsNullOrEmpty(subject)) return;

            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "boss_defeated",
                Subject = subject,
                DisplayName = __instance.m_name,
            });
        }
    }
}
