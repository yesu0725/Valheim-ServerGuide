using HarmonyLib;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Triggers
{
    /// Fires once per player the first time they open any chest/container.
    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class ChestOpenedTrigger
    {
        private const string GuardKey = "chest_opened_fired";

        [HarmonyPostfix]
        private static void Postfix(Humanoid character, bool hold, bool __result)
        {
            if (hold) return;           // ignore held interactions
            if (!__result) return;      // interaction was rejected
            if (character != Player.m_localPlayer) return;

            var player = Player.m_localPlayer;
            if (SeenTracker.HasFired(player, GuardKey)) return;
            SeenTracker.MarkFired(player, GuardKey);

            GuidanceDispatcher.Raise(new TriggerEvent { Type = "chest_opened", Subject = "" });
        }
    }
}
