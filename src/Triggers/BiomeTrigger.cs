using HarmonyLib;
using UnityEngine;

namespace ValheimServerGuide.Triggers
{
    /// Fires each time the local player enters a new biome.
    /// YAML `trigger.biome` is matched case-insensitively against Heightmap.Biome.ToString()
    /// (e.g. "BlackForest", "Swamp", "Plains").
    /// Polled on Player.Update every 2 seconds to avoid per-frame overhead.
    /// BiomeTriggerSpawnReset clears the last-biome state on spawn so the event fires on login.
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class BiomeTrigger
    {
        private const float CheckInterval = 2f;
        private static float _nextCheck;
        private static Heightmap.Biome _lastBiome = Heightmap.Biome.None;

        internal static void Reset() => _lastBiome = Heightmap.Biome.None;

        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckInterval;

            var biome = __instance.GetCurrentBiome();
            if (biome == _lastBiome) return;
            var prev = _lastBiome;
            _lastBiome = biome;

            if (biome == Heightmap.Biome.None) return;

            Plugin.Log.LogInfo($"[biome] entered '{biome}' (was '{prev}').");
            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "biome",
                Subject = biome.ToString(),
            });
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class BiomeTriggerSpawnReset
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            BiomeTrigger.Reset();
        }
    }
}
