using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimServerGuide.Config;
using ValheimServerGuide.Display;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Triggers
{
    /// Harmony patches on Trader:
    ///   Interact     — Shift + E opens the conversation instead of the store. Plain E is left
    ///                  entirely to vanilla.
    ///   GetHoverText — appends "[Shift + E] Quest" when a conversation entry is available.
    ///
    /// This replaced a hold-E detector (press E, keep holding past 0.5 s → conversation, release
    /// early → store). That design had to swallow the first key-down and re-open the store from
    /// an Update loop to find out which the player meant, so every ordinary trade paid a half
    /// second of nothing happening. A modifier key is unambiguous on the very first frame: plain
    /// E never reaches our code at all, and the store opens as instantly as it does in vanilla.
    [HarmonyPatch(typeof(Trader), nameof(Trader.Interact))]
    internal static class NpcConversationTrigger
    {
        [HarmonyPrefix]
        private static bool Prefix(Trader __instance, Humanoid character, bool hold, ref bool __result)
        {
            var player = character as Player;
            if (player == null || player != Player.m_localPlayer) return true;

            if (hold) return true;          // vanilla ignores held interacts; so do we
            if (!ConversationModifierHeld()) return true;  // plain E → vanilla store

            var subject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name);
            var entries = FindAllEntries(subject, player);
            if (entries.Count == 0) return true;  // nothing to talk about → vanilla store

            OpenConversation(__instance, player, subject, entries);
            __result = true;
            return false; // suppress the store for this press
        }

        /// True while the player is holding the conversation modifier.
        ///
        /// Shift is checked literally so the "[Shift + E]" prompt is always honest, and the
        /// bound Run button is accepted as well — it is Shift by default, which keeps a
        /// rebinding player working, and it is the only way a gamepad (no Shift key) can reach
        /// conversations now that the hold path is gone.
        private static bool ConversationModifierHeld()
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return true;
            return ZInput.GetButton("Run") || ZInput.GetButton("JoyRun");
        }

        /// Opens the single eligible conversation, or the picker when several are available.
        internal static void OpenConversation(Trader trader, Player player,
            string subject, List<GuidanceEntry> entries)
        {
            if (entries.Count == 1)
            {
                var entry    = entries[0];
                var rawText  = !string.IsNullOrEmpty(entry.Message) ? entry.Message : entry.Display?.Text;
                var rendered = GuidanceDispatcher.RenderDisplay(entry, null, rawText, null, player.GetPlayerName());
                GuidanceDisplay.Show(entry, rendered);
                return;
            }

            // 2+ eligible conversations — show the "what would you like to discuss?" picker.
            // Selecting an entry there calls GuidanceDispatcher.FireEntry, which opens that
            // entry's own conversation normally.
            NpcConversationPanel.Get().OpenSelection(trader.m_name, entries, subject);
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

        /// Finds every gate-passing npc_conversation entry for the given NPC prefab name.
        /// Used to decide between opening a single conversation directly (count == 1) and
        /// showing the multi-quest picker (count >= 2).
        internal static List<GuidanceEntry> FindAllEntries(string npcSubject, Player player)
        {
            var result = new List<GuidanceEntry>();
            if (string.IsNullOrEmpty(npcSubject) || player == null) return result;
            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null) return result;

            foreach (var entry in config.Guidances)
            {
                if (entry.Trigger == null) continue;
                if (!string.Equals(entry.Trigger.Type, "npc_conversation",
                        System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(entry.Trigger.Npc, npcSubject,
                        System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!GuidanceDispatcher.CheckGates(entry, player)) continue;
                result.Add(entry);
            }
            return result;
        }
    }

    /// Appends the Shift+E hint to the vanilla trader hover tooltip when a conversation
    /// entry is available and its gates are satisfied.
    [HarmonyPatch(typeof(Trader), nameof(Trader.GetHoverText))]
    internal static class TraderHoverTextPatch
    {
        /// The generic prompt, used when an entry has no `hover_text` of its own. Keep it in step
        /// with NpcConversationTrigger.ConversationModifierHeld — the prompt is a promise.
        internal const string ConversationHint = "[Shift + E] Quest";

        [HarmonyPostfix]
        private static void Postfix(Trader __instance, ref string __result)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            var subject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name);

            // hover_text override (Phase 6) takes priority over the generic "[Shift + E] Quest"
            // hint, but is appended below the vanilla hover text (e.g. "[E] Talk") rather than
            // replacing it — the player still sees the normal interact hint plus the quest-
            // specific line. "default" applies to an eligible-but-unfired entry; "after_fire"
            // applies to an already-fired once:true entry that still wants its own hover line
            // (e.g. "[Completed] ...").
            var eligible = NpcConversationTrigger.FindEntry(subject, player);
            if (eligible?.HoverText?.Default != null)
            {
                __result += "\n" + GuidanceDispatcher.RenderLocal(eligible, eligible.HoverText.Default);
                return;
            }

            var firedWithHover = FindFiredEntryWithAfterFireHover(subject, player);
            if (firedWithHover != null)
            {
                __result += "\n" + GuidanceDispatcher.RenderLocal(firedWithHover, firedWithHover.HoverText.AfterFire);
                return;
            }

            if (eligible != null)
                __result += "\n" + ConversationHint;
        }

        /// Finds a fired npc_conversation entry for this NPC whose hover_text.after_fire
        /// is set, so the hover tooltip can change once the quest is done.
        private static GuidanceEntry FindFiredEntryWithAfterFireHover(string npcSubject, Player player)
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
                if (string.IsNullOrEmpty(entry.HoverText?.AfterFire)) continue;
                if (!entry.Once || !SeenTracker.HasFired(player, entry.Id, entry.Scope)) continue;
                return entry;
            }
            return null;
        }
    }

}
