using System.Collections.Generic;
using HarmonyLib;
using ValheimServerGuide.Config;

namespace ValheimServerGuide.Triggers
{
    /// Fires a `skill_level` event for each whole-number threshold crossed when a skill
    /// level increases.  Subject format: `"SkillName:level"` e.g. `"Swords:25"`.
    /// YAML entries match on `trigger.skill` + `trigger.level` (exact level).
    [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
    internal static class SkillLevelTrigger
    {
        // Captured in Prefix; compared in Postfix. Safe: Unity main thread is single-threaded.
        private static int _prevLevel;

        [HarmonyPrefix]
        private static void Prefix(Skills __instance, Skills.SkillType skillType)
        {
            if (Player.m_localPlayer == null) return;
            if (Player.m_localPlayer.m_skills != __instance) return;
            _prevLevel = (int)__instance.GetSkillLevel(skillType);
        }

        [HarmonyPostfix]
        private static void Postfix(Skills __instance, Skills.SkillType skillType)
        {
            if (Player.m_localPlayer == null) return;
            if (Player.m_localPlayer.m_skills != __instance) return;

            var newLevel = (int)__instance.GetSkillLevel(skillType);
            // Fire one event per whole-number threshold crossed (usually just one per raise).
            for (var lvl = _prevLevel + 1; lvl <= newLevel; lvl++)
            {
                GuidanceDispatcher.Raise(new TriggerEvent
                {
                    Type = "skill_level",
                    Subject = $"{skillType}:{lvl}",
                });
            }
        }

        /// On player spawn, fire skill_level events for any thresholds the player already meets
        /// that haven't been handled yet. Thresholds are raised in ascending level order so that
        /// chain steps fire in sequence: step 0 (level 5) fires first, advancing the chain, then
        /// step 1 (level 10) fires on the next raise. The list is not deduplicated so that
        /// consecutive chain steps sharing the same level threshold each get one raise.
        internal static void CheckAllSkillLevels()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null) return;

            var thresholds = new List<(string skill, int level)>();
            foreach (var entry in config.Guidances)
            {
                CollectThreshold(entry.Trigger, thresholds);
                if (entry.Steps == null) continue;
                foreach (var step in entry.Steps)
                    CollectThreshold(step?.Trigger, thresholds);
            }

            if (thresholds.Count == 0) return;

            // Sort ascending: lower levels fire before higher ones within each skill.
            thresholds.Sort((a, b) =>
            {
                var c = string.Compare(a.skill, b.skill, System.StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : a.level.CompareTo(b.level);
            });

            foreach (var (skill, level) in thresholds)
            {
                if (!System.Enum.TryParse<Skills.SkillType>(skill, true, out var skillType)) continue;
                var playerLevel = (int)player.m_skills.GetSkillLevel(skillType);
                if (playerLevel < level) continue;

                Plugin.Log.LogInfo($"[skill_level] Login scan: {skill}:{level} (player {playerLevel}) — raising.");
                GuidanceDispatcher.Raise(new TriggerEvent
                {
                    Type = "skill_level",
                    Subject = $"{skill}:{level}",
                });
            }
        }

        private static void CollectThreshold(TriggerSpec t, List<(string, int)> list)
        {
            if (t == null) return;
            if (!string.Equals(t.Type, "skill_level", System.StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrEmpty(t.Skill) || t.Level <= 0) return;
            list.Add((t.Skill, t.Level));
        }
    }
}
