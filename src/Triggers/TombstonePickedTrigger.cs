using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player loots a tombstone (recovers their dropped items).
    /// Type-only — no subject filter. TombStone.Interact returns true only when the player is
    /// permitted to loot it; the hold-continuation frame is skipped.
    [HarmonyPatch(typeof(TombStone), nameof(TombStone.Interact))]
    internal static class TombstonePickedTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(bool hold, bool __result)
        {
            if (Player.m_localPlayer == null) return;
            if (hold || !__result) return;

            GuidanceDispatcher.Raise(new TriggerEvent { Type = "tombstone_picked" });
        }
    }
}
