using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player interacts with a sign. Type-only — no subject filter.
    /// The hold-continuation frame is skipped; we gate on __result so only a successful interact fires.
    [HarmonyPatch(typeof(Sign), nameof(Sign.Interact))]
    internal static class SignReadTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(bool hold, bool __result)
        {
            if (Player.m_localPlayer == null) return;
            if (hold || !__result) return;

            GuidanceDispatcher.Raise(new TriggerEvent { Type = "sign_read" });
        }
    }
}
