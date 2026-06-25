using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player actually travels through a portal. TeleportWorld.Teleport(Player)
    /// is the travel call (not Interact, which only opens the tag-rename dialog), so we fire only
    /// when the teleported player is the local player. Subject is the portal's tag, so
    /// `trigger.tag: home` can filter; omit tag to match any portal.
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    internal static class PortalUsedTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(TeleportWorld __instance, Player player)
        {
            if (Player.m_localPlayer == null) return;
            if (player != Player.m_localPlayer) return;

            var tag = __instance.GetText() ?? "";

            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "portal_used",
                Subject = tag,
                DisplayName = tag,
            });
        }
    }
}
