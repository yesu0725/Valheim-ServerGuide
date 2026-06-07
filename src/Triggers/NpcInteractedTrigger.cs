using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player opens a trader's store (Haldor, Hildir, Bog Witch, etc.).
    /// Subject is the trader's prefab name with "(Clone)" stripped.
    [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]
    internal static class NpcInteractedTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(Trader trader)
        {
            if (trader == null) return;
            if (Player.m_localPlayer == null) return;

            var subject = TriggerUtils.NormalizePrefabName(trader.gameObject?.name);
            if (string.IsNullOrEmpty(subject)) return;

            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "npc_interacted",
                Subject = subject,
                DisplayName = trader.m_name,
            });
        }
    }
}
