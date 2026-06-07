using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player successfully places a build piece.
    /// Player.TryPlacePiece(Piece piece) returns true only when the piece passes the
    /// placement checks and is actually built (it then calls the void PlacePiece). Postfixing
    /// it with __result == true gives one event per successful placement.
    ///
    /// Subject = piece prefab name with "(Clone)" stripped (e.g. "wood_wall", "piece_workbench"),
    /// matching the prefab-name convention used by the other triggers.
    /// YAML field matched: trigger.piece.
    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class BuildTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, Piece piece, bool __result)
        {
            if (__instance != Player.m_localPlayer) return;
            if (!__result) return;                 // placement rejected (cost/collision/invalid)
            if (piece == null) return;

            var subject = TriggerUtils.NormalizePrefabName(piece.gameObject?.name);
            if (string.IsNullOrEmpty(subject)) return;

            Plugin.Log.LogInfo($"[build] subject='{subject}' (display='{piece.m_name}').");

            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "build",
                Subject = subject,
                DisplayName = piece.m_name,
            });
        }
    }
}
