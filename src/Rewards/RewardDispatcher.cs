using System.Collections.Generic;
using UnityEngine;
using ValheimServerGuide.Config;

namespace ValheimServerGuide.Rewards
{
    public static class RewardDispatcher
    {
        public static void Grant(List<RewardSpec> rewards, Player player)
        {
            if (rewards == null || rewards.Count == 0 || player == null) return;

            foreach (var reward in rewards)
            {
                switch (reward.Type?.ToLowerInvariant())
                {
                    case "item":       GrantItem(reward, player);       break;
                    case "skill_exp":  GrantSkillExp(reward, player);   break;
                    case "skill_level":GrantSkillLevel(reward, player); break;
                    case "buff":       GrantBuff(reward, player);       break;
                    default:
                        Plugin.Log.LogWarning($"[rewards] Unknown reward type '{reward.Type}' — skipping.");
                        break;
                }
            }

            // Summary message after granting; suppressed automatically for an empty list
            // since this method early-returns above when there is nothing to grant.
            RewardNotification.Show(rewards);
        }

        /// Config-load validation: warn about rewards that name an unknown item / skill /
        /// effect, without granting anything. Game-state lookups (prefabs, status effects)
        /// are skipped when ZNetScene / ObjectDB are not yet loaded; load always succeeds.
        public static void ValidateRewards(List<RewardSpec> rewards, string context)
        {
            if (rewards == null) return;
            foreach (var reward in rewards)
            {
                switch (reward.Type?.ToLowerInvariant())
                {
                    case "item":
                        if (!string.IsNullOrEmpty(reward.Item) && ZNetScene.instance != null)
                        {
                            var prefab = ZNetScene.instance.GetPrefab(reward.Item);
                            if (prefab == null)
                                Plugin.Log.LogWarning($"[rewards] {context}: item prefab '{reward.Item}' not found.");
                            else if (prefab.GetComponent<ItemDrop>() == null)
                                Plugin.Log.LogWarning($"[rewards] {context}: prefab '{reward.Item}' has no ItemDrop.");
                        }
                        break;
                    case "skill_exp":
                    case "skill_level":
                        if (!string.IsNullOrEmpty(reward.Skill) &&
                            !System.Enum.TryParse<Skills.SkillType>(reward.Skill, true, out _))
                            Plugin.Log.LogWarning($"[rewards] {context}: unknown skill '{reward.Skill}'.");
                        break;
                    case "buff":
                        if (!string.IsNullOrEmpty(reward.Effect) && ObjectDB.instance?.m_StatusEffects != null)
                        {
                            var want = NormalizeEffectName(reward.Effect);
                            bool found = false;
                            foreach (var s in ObjectDB.instance.m_StatusEffects)
                            {
                                if (s == null) continue;
                                if (NormalizeEffectName(s.name) == want || NormalizeEffectName(s.m_name) == want)
                                {
                                    found = true;
                                    break;
                                }
                            }
                            if (!found)
                                Plugin.Log.LogWarning($"[rewards] {context}: status effect '{reward.Effect}' not found in ObjectDB.");
                        }
                        break;
                }
            }
        }

        private static void GrantItem(RewardSpec reward, Player player)
        {
            if (string.IsNullOrEmpty(reward.Item))
            {
                Plugin.Log.LogWarning("[rewards] item reward missing 'item' field — skipping.");
                return;
            }

            var prefab = ZNetScene.instance?.GetPrefab(reward.Item);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[rewards] item prefab '{reward.Item}' not found — skipping.");
                return;
            }

            var itemDrop = prefab.GetComponent<ItemDrop>();
            if (itemDrop == null)
            {
                Plugin.Log.LogWarning($"[rewards] prefab '{reward.Item}' has no ItemDrop — skipping.");
                return;
            }

            int quality = Mathf.Clamp(reward.Quality, 1, itemDrop.m_itemData.m_shared.m_maxQuality);
            var added = player.GetInventory().AddItem(reward.Item, reward.Amount, quality, 0, 0L, player.GetPlayerName());
            if (added == null)
            {
                var pos = player.transform.position + player.transform.forward * 1.5f;
                var go = Object.Instantiate(prefab, pos, Quaternion.identity);
                var dropped = go.GetComponent<ItemDrop>();
                if (dropped != null)
                {
                    dropped.m_itemData.m_stack = reward.Amount;
                    dropped.m_itemData.m_quality = quality;
                }
                Plugin.Log.LogInfo($"[rewards] Inventory full — dropped '{reward.Item}' x{reward.Amount} Q{quality} in front of player.");
            }
            else
            {
                Plugin.Log.LogInfo($"[rewards] Granted '{reward.Item}' x{reward.Amount} Q{quality}.");
            }
        }

        private static void GrantSkillExp(RewardSpec reward, Player player)
        {
            if (!TryParseSkill(reward.Skill, out var skillType)) return;
            float xp = reward.SkillExp > 0f ? reward.SkillExp : (float)reward.Amount;
            player.GetSkills().RaiseSkill(skillType, xp);
            Plugin.Log.LogInfo($"[rewards] Raised {reward.Skill} by {xp} XP.");
        }

        private static void GrantSkillLevel(RewardSpec reward, Player player)
        {
            if (!TryParseSkill(reward.Skill, out var skillType)) return;
            // RaiseSkill raises at most one level per call, so reach the target by setting
            // the Skill's level directly. GetSkill creates the entry if it doesn't exist yet.
            var skill = player.GetSkills().GetSkill(skillType);
            if (skill == null)
            {
                Plugin.Log.LogWarning($"[rewards] could not resolve skill '{reward.Skill}' — skipping.");
                return;
            }
            int target = Mathf.Clamp(reward.Level, 1, 100);
            if (target <= skill.m_level)
            {
                Plugin.Log.LogInfo($"[rewards] {reward.Skill} already at {skill.m_level} >= target {target}; skipping.");
                return;
            }
            skill.m_level = target;
            skill.m_accumulator = 0f;
            Plugin.Log.LogInfo($"[rewards] Set {reward.Skill} to level {target}.");
        }

        private static void GrantBuff(RewardSpec reward, Player player)
        {
            if (string.IsNullOrEmpty(reward.Effect))
            {
                Plugin.Log.LogWarning("[rewards] buff reward missing 'effect' field — skipping.");
                return;
            }
            // Status effects are ScriptableObjects in ObjectDB.m_StatusEffects — NOT ZNetScene
            // prefabs. The asset name varies ("Rested" vs the "SE_Rested" convention), so match
            // tolerantly: normalize away a leading "SE_"/"$se_"/"$" on both the query and the
            // candidate's asset name and localization token before comparing.
            var db = ObjectDB.instance;
            StatusEffect proto = null;
            var want = NormalizeEffectName(reward.Effect);
            if (db?.m_StatusEffects != null)
            {
                foreach (var s in db.m_StatusEffects)
                {
                    if (s == null) continue;
                    if (NormalizeEffectName(s.name) == want || NormalizeEffectName(s.m_name) == want)
                    {
                        proto = s;
                        break;
                    }
                }
            }
            if (proto == null)
            {
                Plugin.Log.LogWarning($"[rewards] status effect '{reward.Effect}' not found in ObjectDB — skipping.");
                return;
            }
            // AddStatusEffect(proto, resetTime) clones proto, adds it to the player, and returns
            // the live instance, so duration_override is applied to the clone (not the shared asset).
            var active = player.GetSEMan().AddStatusEffect(proto, true);
            if (reward.DurationOverride.HasValue && active != null)
                active.m_ttl = reward.DurationOverride.Value;
            Plugin.Log.LogInfo($"[rewards] Applied buff '{reward.Effect}'" +
                (reward.DurationOverride.HasValue ? $" ({reward.DurationOverride.Value}s)" : "") + ".");
        }

        /// Lowercases and strips a leading "$", then a leading "se_", so the YAML "SE_Rested",
        /// the asset name "Rested", and the token "$se_rested" all collapse to "rested".
        internal static string NormalizeEffectName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToLowerInvariant();
            if (s.StartsWith("$")) s = s.Substring(1);
            if (s.StartsWith("se_")) s = s.Substring(3);
            return s;
        }

        private static bool TryParseSkill(string skill, out Skills.SkillType skillType)
        {
            if (string.IsNullOrEmpty(skill))
            {
                Plugin.Log.LogWarning("[rewards] skill reward missing 'skill' field — skipping.");
                skillType = default;
                return false;
            }
            if (!System.Enum.TryParse(skill, true, out skillType))
            {
                Plugin.Log.LogWarning($"[rewards] Unknown skill '{skill}' — skipping.");
                return false;
            }
            return true;
        }
    }
}
