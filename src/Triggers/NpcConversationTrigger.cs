using HarmonyLib;
using UnityEngine;
using ValheimServerGuide.Config;
using ValheimServerGuide.Display;

namespace ValheimServerGuide.Triggers
{
    /// Shared hold state between the Harmony patch and the Update-loop detector.
    internal static class NpcConvHoldState
    {
        internal const float HoldThreshold = 0.5f;
        internal static float HoldStart    = -1f;
        internal static Trader PendingTrader = null;
    }

    /// Harmony patches on Trader:
    ///   Interact  — intercepts the very first key-down (hold=false) to start the
    ///               hold timer, suppressing the store until we know whether it is a
    ///               short press (open store) or a long hold (open conversation).
    ///               The Update loop in NpcConversationHoldDetector makes the call.
    ///   GetHoverText — appends "[Hold E] Quest" when a conversation entry is available.
    [HarmonyPatch(typeof(Trader), nameof(Trader.Interact))]
    internal static class NpcConversationTrigger
    {
        [HarmonyPrefix]
        private static bool Prefix(Trader __instance, Humanoid character, bool hold, ref bool __result)
        {
            // Ensure the Update-loop component is alive before we need it.
            NpcConversationHoldDetector.EnsureCreated();

            var player = character as Player;
            if (player == null || player != Player.m_localPlayer) return true;

            // Does a valid conversation entry exist for this trader?
            var subject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name);
            var entry   = FindEntry(subject, player);
            if (entry == null) return true; // no conversation — full vanilla path

            if (!hold)
            {
                // E just pressed (key-down frame).
                // Start the hold timer and suppress the store for now.
                // The detector's Update will open the store after a quick release,
                // or open the conversation after HoldThreshold seconds.
                NpcConvHoldState.HoldStart    = Time.time;
                NpcConvHoldState.PendingTrader = __instance;
                __result = true;
                return false; // skip Trader.Interact original
            }
            else
            {
                // E still held — the Update loop is driving the logic, suppress.
                __result = false;
                return false;
            }
        }

        /// Finds the first eligible npc_conversation entry for the given NPC prefab name.
        internal static GuidanceEntry FindEntry(string npcSubject, Player player)
        {
            if (string.IsNullOrEmpty(npcSubject) || player == null) return null;
            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null) return null;

            foreach (var entry in config.Guidances)
            {
                if (entry.Trigger == null) continue;
                if (!string.Equals(entry.Trigger.Type, "npc_conversation",
                        System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(entry.Trigger.Npc, npcSubject,
                        System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!GuidanceDispatcher.CheckGates(entry, player)) continue;
                return entry;
            }
            return null;
        }
    }

    /// Appends a hold-E hint to the vanilla trader hover tooltip when a conversation
    /// entry is available and its gates are satisfied.
    [HarmonyPatch(typeof(Trader), nameof(Trader.GetHoverText))]
    internal static class TraderHoverTextPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Trader __instance, ref string __result)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            var subject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name);
            if (NpcConversationTrigger.FindEntry(subject, player) != null)
                __result += "\n[Hold E] Quest";
        }
    }

    /// Always-active MonoBehaviour that resolves hold-vs-press after the key is down.
    /// Created lazily on the first Trader interaction; persists across scene loads.
    internal class NpcConversationHoldDetector : MonoBehaviour
    {
        private static NpcConversationHoldDetector _instance;

        internal static void EnsureCreated()
        {
            if (_instance != null) return;
            var go = new GameObject("VSG_NpcConvHold");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<NpcConversationHoldDetector>();
        }

        private void Update()
        {
            if (NpcConvHoldState.PendingTrader == null) return;

            var player = Player.m_localPlayer;
            if (player == null) { Reset(); return; }

            bool holding = ZInput.GetButton("Use");

            if (!holding)
            {
                // Released before threshold → short press → open store normally.
                var trader = NpcConvHoldState.PendingTrader;
                Reset();
                if (StoreGui.instance != null)
                    StoreGui.instance.Show(trader);
                return;
            }

            if (Time.time - NpcConvHoldState.HoldStart >= NpcConvHoldState.HoldThreshold)
            {
                // Held long enough → open conversation.
                var trader = NpcConvHoldState.PendingTrader;
                Reset();

                var subject = TriggerUtils.NormalizePrefabName(trader.gameObject?.name);
                var entry   = NpcConversationTrigger.FindEntry(subject, player);
                if (entry == null)
                {
                    // Entry became unavailable (e.g., YAML reload, gate changed) — fall back to store.
                    if (StoreGui.instance != null) StoreGui.instance.Show(trader);
                    return;
                }

                var rawText  = !string.IsNullOrEmpty(entry.Message) ? entry.Message : entry.Display?.Text;
                var rendered = GuidanceDispatcher.TemplateText(rawText, null, player.GetPlayerName());
                GuidanceDisplay.Show(entry, rendered);
            }
        }

        private static void Reset()
        {
            NpcConvHoldState.HoldStart    = -1f;
            NpcConvHoldState.PendingTrader = null;
        }
    }
}
