using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player takes the helm of a ship. Ship exposes no per-frame interact
    /// hook, so we use ShipControlls.Interact (taking control of the rudder) as the "sailing"
    /// signal. Type-only — no subject filter.
    ///
    /// ShipControlls.Interact ALWAYS returns false on its success path (it fires the "RequestControl"
    /// RPC, then `return false`), so we cannot gate on __result. Instead we replicate its success
    /// precondition: the interactor is the local player and is standing on this ship. The repeat-
    /// continuation frame is skipped.
    [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.Interact))]
    internal static class ShipSailedTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(ShipControlls __instance, Humanoid character, bool repeat)
        {
            if (repeat) return;
            var player = character as Player;
            if (player == null || player != Player.m_localPlayer) return;
            if (player.GetStandingOnShip() != __instance.m_ship) return;

            GuidanceDispatcher.Raise(new TriggerEvent { Type = "ship_sailed" });
        }
    }
}
