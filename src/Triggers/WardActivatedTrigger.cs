using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player toggles a ward (PrivateArea). Type-only — no subject filter.
    /// PrivateArea.Interact returns true only when the ward state was actually toggled (the player
    /// is permitted), so we gate on __result; the hold-continuation frame is skipped.
    [HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.Interact))]
    internal static class WardActivatedTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(bool hold, bool __result)
        {
            if (Player.m_localPlayer == null) return;
            if (hold || !__result) return;

            GuidanceDispatcher.Raise(new TriggerEvent { Type = "ward_activated" });
        }
    }
}
