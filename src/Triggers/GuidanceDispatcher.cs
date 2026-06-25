using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimServerGuide.Config;
using ValheimServerGuide.Display;
using ValheimServerGuide.Net;
using ValheimServerGuide.Rewards;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Triggers
{
    /// Central match-and-fire logic. Triggers raise a TriggerEvent here; we look up
    /// matching entries from the current config, check gates, and dispatch.
    /// Player-scope: fires locally, persists in m_customData, optional discord ping.
    /// Global-scope: sends an RPC to the server which marks a world-wide global key
    /// and broadcasts a "play now" RPC to every connected client (including the
    /// originator), so everybody sees the same display.
    /// Chain entries (steps: list): step N fires only after step N-1; progress persists
    /// in m_customData via ChainState and is synced server-side on each advance.
    public static class GuidanceDispatcher
    {
        public static void Raise(TriggerEvent evt)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                Plugin.Log.LogDebug($"[dispatch] {evt.Type}/{evt.Subject} ignored: no local player.");
                return;
            }

            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null || config.Guidances.Count == 0)
            {
                Plugin.Log.LogDebug($"[dispatch] {evt.Type}/{evt.Subject} ignored: empty config.");
                return;
            }

            var fired = 0;
            var completedIds = new List<string>();

            foreach (var entry in config.Guidances)
            {
                // Chain entries are handled separately from single entries.
                if (entry.Steps != null && entry.Steps.Count > 0)
                {
                    if (HandleChain(entry, evt, player, completedIds)) fired++;
                    continue;
                }

                if (!Matches(entry, evt)) continue;

                // item_acquired entries with a count goal (single or multi) are handled by
                // ItemAcquiredTrigger.CheckCountGoals and must not fire on each individual pickup.
                if (string.Equals(evt.Type, "item_acquired", System.StringComparison.OrdinalIgnoreCase)
                    && entry.Trigger != null
                    && ItemAcquiredTrigger.GetEffectiveGoals(entry.Trigger) != null)
                {
                    Plugin.Log.LogDebug($"[dispatch] '{entry.Id}' item_acquired count-goal — delegated to count path.");
                    continue;
                }

                // kill entries with a count goal (count > 1) accumulate via KillCountTracker and
                // must not fire on each individual kill.
                if (string.Equals(evt.Type, "kill", System.StringComparison.OrdinalIgnoreCase)
                    && entry.Trigger != null && entry.Trigger.Count > 1)
                {
                    Plugin.Log.LogDebug($"[dispatch] '{entry.Id}' kill count-goal — delegated to count path.");
                    continue;
                }

                if (!RequirementsMet(entry, player)) { Plugin.Log.LogInfo($"[dispatch] '{entry.Id}' skipped: requires not met."); continue; }
                if (StopConditionMet(entry, player)) { Plugin.Log.LogInfo($"[dispatch] '{entry.Id}' skipped: stop_when met."); continue; }
                if (entry.Once && SeenTracker.HasFired(player, entry.Id, entry.Scope)) { Plugin.Log.LogInfo($"[dispatch] '{entry.Id}' skipped: already fired (once)."); continue; }
                if (!SeenTracker.CooldownReady(entry.Id, entry.Cooldown, Time.time)) { Plugin.Log.LogInfo($"[dispatch] '{entry.Id}' skipped: cooldown."); continue; }

                var maxFires = entry.Trigger?.MaxFires ?? 0;
                if (maxFires > 0 && SeenTracker.GetFireCount(player, entry.Id) >= maxFires)
                {
                    Plugin.Log.LogInfo($"[dispatch] '{entry.Id}' skipped: max_fires ({maxFires}) reached.");
                    continue;
                }

                if (SeenTracker.IsGlobalScope(entry.Scope))
                {
                    Plugin.Log.LogInfo($"[dispatch] '{entry.Id}' (global) -> server.");
                    GuidanceSync.SendTriggerGlobal(entry.Id, player.GetPlayerName());
                    SeenTracker.MarkCooldown(entry.Id, entry.Cooldown, Time.time);
                    // Global entries complete on the receiving client (PlayGlobalReceived),
                    // so we do not add to completedIds here.
                    fired++;
                    continue;
                }

                // Player scope — local display + state.
                Plugin.Log.LogInfo($"[dispatch] firing '{entry.Id}' via mode '{entry.Display?.Mode}'.");
                var rawText = !string.IsNullOrEmpty(entry.Message) ? entry.Message : entry.Display?.Text;
                var rendered = TemplateText(rawText, evt, player.GetPlayerName());
                GuidanceDisplay.Show(entry, rendered);

                if (entry.Once) SeenTracker.MarkFired(player, entry.Id, entry.Scope);
                if (maxFires > 0) SeenTracker.IncrementFireCount(player, entry.Id);
                SeenTracker.MarkCooldown(entry.Id, entry.Cooldown, Time.time);
                DebugFireLog.Record(player.GetPlayerName(), entry.Id);

                if (entry.Announce?.Discord != null)
                    GuidanceSync.SendAnnounceRequest(entry.Id, player.GetPlayerName());

                if (entry.DiscordOnComplete)
                    GuidanceSync.SendCompleteAnnounce(entry.Id, player.GetPlayerName());

                if (entry.Rewards != null && entry.Rewards.Count > 0)
                    RewardDispatcher.Grant(entry.Rewards, player);

                completedIds.Add(entry.Id);
                fired++;
            }

            if (fired == 0)
                Plugin.Log.LogInfo($"[dispatch] {evt.Type}/{evt.Subject} matched no guidance entries.");

            // Raise entry_finished events after the loop to avoid modifying the iteration
            // and to bound any completion-chain recursion to one extra Raise() call per entry.
            foreach (var id in completedIds)
                Raise(new TriggerEvent { Type = "entry_finished", Subject = id });
        }

        /// Handles one event against a chain entry. Returns true when a step fired or counter changed.
        private static bool HandleChain(GuidanceEntry entry, TriggerEvent evt, Player player, List<string> completedIds)
        {
            if (ChainState.IsComplete(player, entry.Id)) return false;

            if (!PrerequisiteChecker.AllSatisfied(entry.Requires, player, Plugin.CurrentConfig))
            {
                Plugin.Log.LogDebug($"[chain] '{entry.Id}' prerequisites not met.");
                return false;
            }

            var stepIndex = ChainState.GetStep(player, entry.Id);
            if (stepIndex >= entry.Steps.Count)
            {
                // Safety: shouldn't happen if MarkComplete is always called, but guard anyway.
                ChainState.MarkComplete(player, entry.Id);
                return false;
            }

            var step = entry.Steps[stepIndex];
            if (step?.Trigger == null) return false;

            // Route to counter or normal path.
            if (step.ProgressGoal > 0)
                return HandleCounterStep(entry, step, stepIndex, evt, player, completedIds);
            return HandleNormalStep(entry, step, stepIndex, evt, player, completedIds);
        }

        /// Normal step: fires as soon as the step's trigger matches.
        private static bool HandleNormalStep(GuidanceEntry entry, GuidanceStep step, int stepIndex,
            TriggerEvent evt, Player player, List<string> completedIds)
        {
            if (!MatchesTrigger(step.Trigger, evt)) return false;
            FireStepDisplay(entry, step, stepIndex, evt, player);
            AdvanceChain(entry, stepIndex, player, completedIds);
            return true;
        }

        /// Counter step: primary trigger activates the counter; each progress_trigger match
        /// increments it; the step message fires only when the counter reaches progress_goal.
        private static bool HandleCounterStep(GuidanceEntry entry, GuidanceStep step, int stepIndex,
            TriggerEvent evt, Player player, List<string> completedIds)
        {
            // Misconfigured: no progress_trigger supplied — fall back to normal step and warn once.
            if (step.ProgressTrigger == null)
            {
                Plugin.Log.LogWarning($"[chain] '{entry.Id}' step {stepIndex} has progress_goal " +
                    "but no progress_trigger — treating as normal step.");
                return HandleNormalStep(entry, step, stepIndex, evt, player, completedIds);
            }

            var counter = ChainState.GetCounter(player, entry.Id, stepIndex);

            if (counter < 0)
            {
                // Not yet activated. Wait for the primary trigger.
                if (!MatchesTrigger(step.Trigger, evt)) return false;

                // Seed from existing inventory when progress is counted by item_acquired.
                var seed = 0;
                if (step.ProgressTrigger != null &&
                    string.Equals(step.ProgressTrigger.Type, "item_acquired",
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    seed = System.Math.Min(
                        ItemAcquiredTrigger.CountInInventory(player, step.ProgressTrigger.Item),
                        step.ProgressGoal);
                }

                ChainState.SetCounter(player, entry.Id, stepIndex, seed);
                Plugin.Log.LogInfo($"[chain] '{entry.Id}' step {stepIndex} counter activated (seed: {seed}/{step.ProgressGoal}).");
                GuidanceSync.SendChainStepUpdate(player.GetPlayerName(),
                    entry.Id + ":" + stepIndex, seed.ToString());
                GuidanceHudTracker.Instance?.Refresh(fromProgress: true);

                if (seed >= step.ProgressGoal)
                {
                    FireStepDisplay(entry, step, stepIndex, evt, player);
                    ChainState.ClearCounter(player, entry.Id, stepIndex);
                    AdvanceChain(entry, stepIndex, player, completedIds);
                }
                return true;
            }
            else
            {
                // Activated — count progress trigger events.
                if (!MatchesTrigger(step.ProgressTrigger, evt)) return false;
                var newCount = System.Math.Min(counter + 1, step.ProgressGoal);
                ChainState.SetCounter(player, entry.Id, stepIndex, newCount);
                Plugin.Log.LogInfo($"[chain] '{entry.Id}' step {stepIndex} counter: {newCount}/{step.ProgressGoal}.");
                GuidanceSync.SendChainStepUpdate(player.GetPlayerName(),
                    entry.Id + ":" + stepIndex, newCount.ToString());
                GuidanceHudTracker.Instance?.Refresh(fromProgress: true);

                if (newCount >= step.ProgressGoal)
                {
                    FireStepDisplay(entry, step, stepIndex, evt, player);
                    ChainState.ClearCounter(player, entry.Id, stepIndex);
                    AdvanceChain(entry, stepIndex, player, completedIds);
                }
                return true;
            }
        }

        /// Builds the synthetic entry and calls GuidanceDisplay.Show for one chain step.
        private static void FireStepDisplay(GuidanceEntry entry, GuidanceStep step, int stepIndex,
            TriggerEvent evt, Player player)
        {
            var effectiveDisplay = step.Display ?? entry.Display ?? new DisplaySpec();
            var rawText = !string.IsNullOrEmpty(step.Message) ? step.Message : effectiveDisplay.Text;
            var rendered = TemplateText(rawText, evt, player.GetPlayerName(),
                step: stepIndex + 1, total: entry.Steps.Count);

            // Each step uses a unique key so raven Tutorial.m_texts entries don't collide.
            var stepKey = entry.Id + "_s" + stepIndex;
            var stepEntry = new GuidanceEntry
            {
                Id = stepKey,
                Display = new DisplaySpec
                {
                    Mode = effectiveDisplay.Mode,
                    Topic = effectiveDisplay.Topic,
                    Text = rendered,
                    Position = effectiveDisplay.Position,
                },
                Scope = entry.Scope,
                Once = false,
            };

            Plugin.Log.LogInfo($"[chain] '{entry.Id}' step {stepIndex} firing via '{effectiveDisplay.Mode}'.");
            GuidanceDisplay.Show(stepEntry, rendered);

            if (entry.Announce?.Discord != null)
                GuidanceSync.SendAnnounceRequest(entry.Id, player.GetPlayerName());
        }

        /// Moves the chain to the next step, or marks it complete when all steps are done.
        private static void AdvanceChain(GuidanceEntry entry, int stepIndex, Player player, List<string> completedIds)
        {
            var nextStep = stepIndex + 1;
            if (nextStep >= entry.Steps.Count)
            {
                ChainState.MarkComplete(player, entry.Id);
                ChainState.SetCompletedVersion(player, entry.Id, entry.Version);
                Plugin.Log.LogInfo($"[chain] '{entry.Id}' complete (all {entry.Steps.Count} steps done).");
                GuidanceSync.SendChainStepUpdate(player.GetPlayerName(), entry.Id, "done");
                GuidanceHudTracker.Instance?.FlashCompletion(entry.Id);
                DebugFireLog.Record(player.GetPlayerName(), entry.Id);
                if (entry.DiscordOnComplete)
                    GuidanceSync.SendCompleteAnnounce(entry.Id, player.GetPlayerName());
                if (entry.Rewards != null && entry.Rewards.Count > 0)
                    RewardDispatcher.Grant(entry.Rewards, player);
                completedIds.Add(entry.Id);
            }
            else
            {
                ChainState.SetStep(player, entry.Id, nextStep);
                Plugin.Log.LogInfo($"[chain] '{entry.Id}' advanced to step {nextStep}/{entry.Steps.Count}.");
                GuidanceSync.SendChainStepUpdate(player.GetPlayerName(), entry.Id, nextStep.ToString());
                GuidanceHudTracker.Instance?.Refresh(fromProgress: true);
            }
        }

        /// Called by the VSG_PlayGlobal RPC handler on every client.
        public static void PlayGlobalReceived(string entryId, string sourcePlayerName)
        {
            var config = Plugin.CurrentConfig;
            var entry = config?.Guidances?.Find(g => g.Id == entryId);
            if (entry == null) { Plugin.Log.LogWarning($"[global] received play for unknown id '{entryId}'."); return; }

            Plugin.Log.LogInfo($"[global] showing '{entryId}' (triggered by {sourcePlayerName}).");
            var rawText = !string.IsNullOrEmpty(entry.Message) ? entry.Message : entry.Display?.Text;
            var rendered = TemplateText(rawText, evt: null, playerName: sourcePlayerName);
            GuidanceDisplay.Show(entry, rendered);
            if (Player.m_localPlayer != null)
                DebugFireLog.Record(Player.m_localPlayer.GetPlayerName(), entry.Id);

            // Global entries complete on each receiving client — raise entry_finished here.
            Raise(new TriggerEvent { Type = "entry_finished", Subject = entryId });
        }

        /// Returns true when all gates (requires, stop_when, once, cooldown, max_fires) pass.
        /// Used by NpcConversationTrigger to validate an entry before opening the panel.
        internal static bool CheckGates(GuidanceEntry entry, Player player)
        {
            if (!RequirementsMet(entry, player)) return false;
            if (StopConditionMet(entry, player)) return false;
            if (entry.Once && SeenTracker.HasFired(player, entry.Id, entry.Scope)) return false;
            if (!SeenTracker.CooldownReady(entry.Id, entry.Cooldown, Time.time)) return false;
            var maxFires = entry.Trigger?.MaxFires ?? 0;
            if (maxFires > 0 && SeenTracker.GetFireCount(player, entry.Id) >= maxFires) return false;
            return true;
        }

        /// Fire an entry directly by ID, bypassing trigger matching.
        /// Respects once/requires/stop_when/cooldown. Used after a conversation choice selects goto.
        internal static void FireById(string entryId)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            var config = Plugin.CurrentConfig;
            var entry = config?.Guidances?.Find(g =>
                string.Equals(g.Id, entryId, System.StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                Plugin.Log.LogWarning($"[dispatch] FireById: entry '{entryId}' not found.");
                return;
            }
            if (!CheckGates(entry, player))
            {
                Plugin.Log.LogInfo($"[dispatch] FireById: '{entryId}' gates blocked.");
                return;
            }

            Plugin.Log.LogInfo($"[dispatch] FireById firing '{entryId}'.");
            var rawText = !string.IsNullOrEmpty(entry.Message) ? entry.Message : entry.Display?.Text;
            var rendered = TemplateText(rawText, null, player.GetPlayerName());
            GuidanceDisplay.Show(entry, rendered);

            if (entry.Once) SeenTracker.MarkFired(player, entry.Id, entry.Scope);
            var maxFiresB = entry.Trigger?.MaxFires ?? 0;
            if (maxFiresB > 0) SeenTracker.IncrementFireCount(player, entry.Id);
            SeenTracker.MarkCooldown(entry.Id, entry.Cooldown, Time.time);
            DebugFireLog.Record(player.GetPlayerName(), entry.Id);

            Raise(new TriggerEvent { Type = "entry_finished", Subject = entryId });
        }

        /// Fire a single, already-selected entry against a trigger event (for templating).
        /// Unlike Raise(), this does NOT scan/fire other matching entries — the caller has
        /// already chosen exactly one entry (e.g. NpcItemSubmitTrigger.FindEntry, which prefers
        /// a specific item match over a catch-all). Gates are assumed already checked by the caller.
        /// Handles player + global scope and raises entry_finished afterward.
        internal static void FireEntry(GuidanceEntry entry, TriggerEvent evt)
        {
            var player = Player.m_localPlayer;
            if (player == null || entry == null) return;

            if (SeenTracker.IsGlobalScope(entry.Scope))
            {
                Plugin.Log.LogInfo($"[dispatch] FireEntry '{entry.Id}' (global) -> server.");
                GuidanceSync.SendTriggerGlobal(entry.Id, player.GetPlayerName());
                SeenTracker.MarkCooldown(entry.Id, entry.Cooldown, Time.time);
                return;
            }

            Plugin.Log.LogInfo($"[dispatch] FireEntry firing '{entry.Id}' via mode '{entry.Display?.Mode}'.");
            var rawText = !string.IsNullOrEmpty(entry.Message) ? entry.Message : entry.Display?.Text;
            var rendered = TemplateText(rawText, evt, player.GetPlayerName());
            GuidanceDisplay.Show(entry, rendered);

            if (entry.Once) SeenTracker.MarkFired(player, entry.Id, entry.Scope);
            var maxFires = entry.Trigger?.MaxFires ?? 0;
            if (maxFires > 0) SeenTracker.IncrementFireCount(player, entry.Id);
            SeenTracker.MarkCooldown(entry.Id, entry.Cooldown, Time.time);
            DebugFireLog.Record(player.GetPlayerName(), entry.Id);

            if (entry.Announce?.Discord != null)
                GuidanceSync.SendAnnounceRequest(entry.Id, player.GetPlayerName());
            if (entry.DiscordOnComplete)
                GuidanceSync.SendCompleteAnnounce(entry.Id, player.GetPlayerName());

            if (entry.Rewards != null && entry.Rewards.Count > 0)
                RewardDispatcher.Grant(entry.Rewards, player);

            Raise(new TriggerEvent { Type = "entry_finished", Subject = entry.Id });
        }

        private static bool Matches(GuidanceEntry entry, TriggerEvent evt)
        {
            if (entry.Trigger == null) return false;
            return MatchesTrigger(entry.Trigger, evt);
        }

        internal static bool MatchesTrigger(TriggerSpec t, TriggerEvent evt)
        {
            if (t == null) return false;
            if (!string.Equals(t.Type, evt.Type, System.StringComparison.OrdinalIgnoreCase)) return false;

            switch (evt.Type.ToLowerInvariant())
            {
                case "craft":            return Eq(t.Item, evt.Subject);
                case "pickup":           return Eq(t.Item, evt.Subject);
                case "kill":             return Eq(t.Creature, evt.Subject);
                case "tamed_creature":   return string.IsNullOrEmpty(t.Creature) ? true : Eq(t.Creature, evt.Subject);
                case "crafting_table_used": return string.IsNullOrEmpty(t.Station) ? true : Eq(t.Station, evt.Subject);
                case "cooking_used":     return string.IsNullOrEmpty(t.Station) ? true : Eq(t.Station, evt.Subject);
                case "portal_used":      return string.IsNullOrEmpty(t.Tag) ? true : Eq(t.Tag, evt.Subject);
                case "ward_activated":
                case "sign_read":
                case "tombstone_picked":
                case "ship_sailed":      return true;
                case "build":            return Eq(t.Piece, evt.Subject);
                case "biome":            return Eq(t.Biome, evt.Subject);
                case "equip":            return Eq(t.Item, evt.Subject);
                case "boss_defeated":    return Eq(t.Creature, evt.Subject);
                case "item_acquired":    return WildcardMatch(t.Item, evt.Subject);
                case "location_entered": return WildcardMatch(t.Location, evt.Subject);
                case "distance":         return WildcardMatch(t.Location, evt.Subject);
                case "npc_interacted":
                case "npc_conversation": return Eq(t.Npc, evt.Subject);
                case "npc_item_submit":
                {
                    if (!Eq(t.Npc, evt.Subject)) return false;
                    // Empty trigger.item = match any item submitted to this NPC.
                    if (string.IsNullOrEmpty(t.Item)) return true;
                    var evtItem = evt.Extra != null && evt.Extra.ContainsKey("item")
                        ? evt.Extra["item"]?.ToString() : null;
                    return Eq(t.Item, evtItem);
                }
                case "skill_level":      return MatchSkillLevel(t, evt.Subject);
                case "timed":            return Eq(t.Id, evt.Subject);
                case "entry_finished":   return Eq(t.Entry, evt.Subject);
                // Type-only matches — no subject filter.
                case "first_login":
                case "chest_opened":
                case "player_death":
                    return true;
                default: return true;
            }
        }

        private static bool Eq(string a, string b)
            => !string.IsNullOrEmpty(a) && string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        /// Supports a trailing `*` wildcard: `"Trophy*"` matches any subject starting with `"Trophy"`.
        private static bool WildcardMatch(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(value)) return false;
            if (pattern.EndsWith("*"))
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                return value.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(pattern, value, System.StringComparison.OrdinalIgnoreCase);
        }

        /// Subject format: `"SkillName:level"` (e.g. `"Woodcutting:2"`).
        private static bool MatchSkillLevel(Config.TriggerSpec t, string subject)
        {
            if (string.IsNullOrEmpty(t.Skill) || string.IsNullOrEmpty(subject)) return false;
            var sep = subject.IndexOf(':');
            if (sep < 0) return false;
            var skillPart = subject.Substring(0, sep);
            var levelStr = subject.Substring(sep + 1);
            if (!int.TryParse(levelStr, out var level)) return false;
            return string.Equals(t.Skill, skillPart, System.StringComparison.OrdinalIgnoreCase)
                   && t.Level == level;
        }

        private static bool RequirementsMet(GuidanceEntry entry, Player player)
            => PrerequisiteChecker.AllSatisfied(entry.Requires, player, Plugin.CurrentConfig);

        private static bool StopConditionMet(GuidanceEntry entry, Player player)
        {
            if (entry.StopWhen == null || entry.StopWhen.Count == 0) return false;
            foreach (var id in entry.StopWhen)
                if (SeenTracker.HasFired(player, id, "player")) return true;
            return false;
        }

        /// Expand template variables in a message string. Unknown variables are left as-is.
        /// step/total are 1-based chain step numbers; pass -1 when not in a chain context.
        internal static string TemplateText(string template, TriggerEvent evt, string playerName,
            int step = -1, int total = -1)
        {
            if (string.IsNullOrEmpty(template)) return template;

            // Current biome from the local player.
            var biomeName = "";
            var lp = Player.m_localPlayer;
            if (lp != null)
                biomeName = lp.GetCurrentBiome().ToString();

            // Skill/level from skill_level events: Subject = "SkillName:level".
            var skillName = "";
            var levelStr  = "";
            if (evt != null && string.Equals(evt.Type, "skill_level",
                    System.StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(evt.Subject))
            {
                var sep = evt.Subject.IndexOf(':');
                if (sep >= 0)
                {
                    skillName = evt.Subject.Substring(0, sep);
                    levelStr  = evt.Subject.Substring(sep + 1);
                }
            }

            var result = template
                .Replace("{playerName}", playerName ?? "")
                .Replace("{player_name}", playerName ?? "")
                .Replace("{itemName}", evt?.DisplayName ?? evt?.Subject ?? "")
                .Replace("{creatureName}", evt?.DisplayName ?? evt?.Subject ?? "")
                .Replace("{biome}", string.IsNullOrEmpty(biomeName) ? (evt?.Subject ?? "") : biomeName)
                .Replace("{skill}", skillName)
                .Replace("{level}", levelStr);

            if (step >= 0)  result = result.Replace("{step}",  step.ToString());
            if (total >= 0) result = result.Replace("{total}", total.ToString());

            return result;
        }

        /// Check every completed chain for a version bump. When entry.Version > the stored
        /// completion version, re-deliver the last step's message as a notification so the player
        /// sees the updated content. Chain progress is never reset. Called on player login.
        public static void CheckVersionUpdates(Player player, GuidanceConfig config)
        {
            if (player == null || config?.Guidances == null) return;
            foreach (var entry in config.Guidances)
            {
                if (entry.Steps == null || entry.Steps.Count == 0) continue;
                if (!ChainState.IsComplete(player, entry.Id)) continue;

                var seenVersion = ChainState.GetCompletedVersion(player, entry.Id);
                if (entry.Version <= seenVersion) continue;

                // Re-deliver the last step's message (or entry title) as a notification.
                var lastStep = entry.Steps[entry.Steps.Count - 1];
                var rawText = !string.IsNullOrEmpty(lastStep.Message) ? lastStep.Message
                    : lastStep.Display?.Text ?? entry.Title ?? entry.Id;
                var rendered = TemplateText(rawText, null, player.GetPlayerName());

                if (MessageHud.instance != null)
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, rendered);

                Plugin.Log.LogInfo($"[dispatch] Version update for '{entry.Id}': " +
                    $"seen v{seenVersion}, current v{entry.Version}. Re-delivered notification.");
                ChainState.SetCompletedVersion(player, entry.Id, entry.Version);
            }
        }
    }

    /// On player spawn, check whether any completed chains have a config version bump
    /// and re-deliver updated messages as notifications. Runs after the HUD tracker
    /// refresh so chain state is already loaded from m_customData.
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class PlayerOnSpawnedDispatchPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            GuidanceDispatcher.CheckVersionUpdates(__instance, Plugin.CurrentConfig);
            ItemAcquiredTrigger.CheckAllCountGoals();
            SkillLevelTrigger.CheckAllSkillLevels();
        }
    }

    public class TriggerEvent
    {
        public string Type;          // craft | pickup | kill | build | biome | ...
        public string Subject;       // prefab name / biome name / "Skill:level"
        public string DisplayName;   // localized display name when available
        public Dictionary<string, object> Extra;
    }
}
