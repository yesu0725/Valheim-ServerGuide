using HarmonyLib;

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
    }
}
