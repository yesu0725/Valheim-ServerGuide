using System;
using HarmonyLib;
using UnityEngine;
using ValheimServerGuide.Config;
using ValheimServerGuide.Display;
using ValheimServerGuide.Net;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player is credited with a kill.
    /// Character.OnDeath runs on whoever owns the dying character's ZDO; we postfix
    /// it, inspect the last attacker, and only raise if it was Player.m_localPlayer.
    /// This means each client raises the event on its own machine when *its* player
    /// is the killer — which is exactly what the dispatcher wants for both player-
    /// scope and global-scope entries (global ones get routed to the server from
    /// the killer's client).
    ///
    /// kill entries with a count goal (trigger.count > 1) do NOT fire on each kill via the
    /// dispatcher; KillCountTracker.CheckKillCount accumulates them instead.
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class KillTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(Character __instance)
        {
            if (Player.m_localPlayer == null) return;          // dedicated server has none
            var attacker = __instance?.m_lastHit?.GetAttacker();
            if (attacker == null) return;
            if (attacker != Player.m_localPlayer) return;       // someone else killed it

            var subject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name);
            if (string.IsNullOrEmpty(subject)) return;

            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "kill",
                Subject = subject,
                DisplayName = __instance.m_name,
            });
            KillCountTracker.CheckKillCount(subject, __instance.m_name);
        }
    }

    /// Accumulates progress for `kill` entries with a count goal (trigger.count > 1).
    /// Mirrors NpcItemSubmitTrigger's multi-count handling: each matching kill increments a
    /// persistent counter (VSG.kc.<id>); the entry fires only when the counter reaches the goal.
    internal static class KillCountTracker
    {
        /// How close a nearby player must be to the killer to have a `share_progress`
        /// entry's count advance on their own client too. There's no real "party" system
        /// in vanilla Valheim, so proximity is the practical stand-in the roadmap calls
        /// "nearby group members".
        internal const float ShareProgressRadius = 50f;

        internal static void CheckKillCount(string creature, string displayName)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null) return;

            foreach (var entry in config.Guidances)
            {
                var t = entry.Trigger;
                if (t == null) continue;
                if (!string.Equals(t.Type, "kill", StringComparison.OrdinalIgnoreCase)) continue;
                if (t.Count <= 1) continue;   // not a count entry — handled by the normal dispatch path
                if (string.IsNullOrEmpty(t.Creature) ||
                    !string.Equals(t.Creature, creature, StringComparison.OrdinalIgnoreCase)) continue;
                if (!GuidanceDispatcher.CheckGates(entry, player)) continue;

                ApplyIncrement(entry, player, creature, displayName);

                if (t.ShareProgress)
                    GuidanceSync.SendShareKillProgress(entry.Id, player.GetPlayerName(), player.transform.position);
            }
        }

        /// Applies one kill-credit to `entry` for `player` and shows the same progress
        /// message / fires the same completion path CheckKillCount uses for a real kill.
        private static void ApplyIncrement(GuidanceEntry entry, Player player, string creature, string displayName)
        {
            var goal = entry.Trigger.Count;
            var newCount = KillCountState.Get(player, entry.Id) + 1;
            Plugin.Log.LogInfo($"[kill] '{entry.Id}' progress {newCount}/{goal} ({creature}).");

            if (newCount >= goal)
            {
                KillCountState.Clear(player, entry.Id);
                Plugin.Log.LogInfo($"[kill] '{entry.Id}' complete ({goal}/{goal}) — firing.");
                GuidanceDispatcher.FireEntry(entry, new TriggerEvent
                {
                    Type = "kill",
                    Subject = creature,
                    DisplayName = displayName,
                });
                GuidanceHudTracker.Instance?.FlashCompletion(entry.Id);
            }
            else
            {
                KillCountState.Set(player, entry.Id, newCount);
                var title = !string.IsNullOrEmpty(entry.Title) ? entry.Title : (displayName ?? creature);
                player.Message(MessageHud.MessageType.Center, $"{title}: {newCount}/{goal}");
                GuidanceHudTracker.Instance?.Refresh(fromProgress: true);
            }
        }

        /// Called from GuidanceSync.OnShareKillProgress when a nearby player's client
        /// reports a `share_progress` kill. Credits this local player's own counter for
        /// the same entry — without re-broadcasting, so the party doesn't loop the RPC.
        internal static void ApplySharedIncrement(string entryId, Player localPlayer, Vector3 killerPosition)
        {
            if (string.IsNullOrEmpty(entryId) || localPlayer == null) return;

            var distSq = (localPlayer.transform.position - killerPosition).sqrMagnitude;
            if (distSq > ShareProgressRadius * ShareProgressRadius) return;

            var entry = Plugin.CurrentConfig?.Guidances?.Find(g => g.Id == entryId);
            if (entry?.Trigger == null) return;
            if (!string.Equals(entry.Trigger.Type, "kill", StringComparison.OrdinalIgnoreCase)) return;
            if (entry.Trigger.Count <= 1) return;
            if (!GuidanceDispatcher.CheckGates(entry, localPlayer)) return;

            Plugin.Log.LogInfo($"[kill] '{entryId}' shared credit from nearby party kill.");
            ApplyIncrement(entry, localPlayer, entry.Trigger.Creature, entry.Trigger.Creature);
        }
    }
}
