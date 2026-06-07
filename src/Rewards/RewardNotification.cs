using System.Collections.Generic;
using System.Text;
using ValheimServerGuide.Config;

namespace ValheimServerGuide.Rewards
{
    /// Builds and shows a "Received: ..." summary after rewards are granted.
    /// The summary is derived purely from the RewardSpec list — no extra state.
    public static class RewardNotification
    {
        public static void Show(List<RewardSpec> rewards)
        {
            if (rewards == null || rewards.Count == 0) return;
            if (MessageHud.instance == null) return;

            var parts = new List<string>();
            foreach (var reward in rewards)
            {
                var part = Describe(reward);
                if (!string.IsNullOrEmpty(part)) parts.Add(part);
            }
            if (parts.Count == 0) return;

            var summary = "Received: " + string.Join(", ", parts);
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, summary);
        }

        private static string Describe(RewardSpec reward)
        {
            switch (reward.Type?.ToLowerInvariant())
            {
                case "item":        return DescribeItem(reward);
                case "skill_exp":   return DescribeSkillExp(reward);
                case "skill_level": return DescribeSkillLevel(reward);
                case "buff":        return DescribeBuff(reward);
                default:            return null;
            }
        }

        private static string DescribeItem(RewardSpec reward)
        {
            if (string.IsNullOrEmpty(reward.Item)) return null;
            var name = LocalizeItemName(reward.Item);
            var sb = new StringBuilder(name);
            if (reward.Amount > 1) sb.Append(" x").Append(reward.Amount);
            if (reward.Quality > 1) sb.Append(" (Q").Append(reward.Quality).Append(")");
            return sb.ToString();
        }

        private static string DescribeSkillExp(RewardSpec reward)
        {
            if (string.IsNullOrEmpty(reward.Skill)) return null;
            float xp = reward.SkillExp > 0f ? reward.SkillExp : reward.Amount;
            return $"+{xp:0.#} {reward.Skill} XP";
        }

        private static string DescribeSkillLevel(RewardSpec reward)
        {
            if (string.IsNullOrEmpty(reward.Skill)) return null;
            return $"{reward.Skill} level {reward.Level}";
        }

        private static string DescribeBuff(RewardSpec reward)
        {
            if (string.IsNullOrEmpty(reward.Effect)) return null;
            var name = LocalizeBuffName(reward.Effect);
            if (reward.DurationOverride.HasValue)
            {
                float secs = reward.DurationOverride.Value;
                string dur = secs >= 60f ? $"{secs / 60f:0.#} min" : $"{secs:0}s";
                return $"{name} buff ({dur})";
            }
            return $"{name} buff";
        }

        /// Resolve a friendly item display name from the prefab's ItemDrop token.
        /// Falls back to the raw prefab name if the prefab or localization is unavailable.
        private static string LocalizeItemName(string prefabName)
        {
            var prefab = ZNetScene.instance?.GetPrefab(prefabName);
            var itemDrop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            var token = itemDrop?.m_itemData?.m_shared?.m_name;
            if (!string.IsNullOrEmpty(token) && Localization.instance != null)
                return Localization.instance.Localize(token);
            return prefabName;
        }

        /// Resolve a friendly buff name from the status effect token, matched the same
        /// tolerant way RewardDispatcher resolves it. Falls back to the raw effect string.
        private static string LocalizeBuffName(string effect)
        {
            var db = ObjectDB.instance;
            if (db?.m_StatusEffects != null)
            {
                var want = RewardDispatcher.NormalizeEffectName(effect);
                foreach (var s in db.m_StatusEffects)
                {
                    if (s == null) continue;
                    if (RewardDispatcher.NormalizeEffectName(s.name) == want ||
                        RewardDispatcher.NormalizeEffectName(s.m_name) == want)
                    {
                        if (!string.IsNullOrEmpty(s.m_name) && Localization.instance != null)
                            return Localization.instance.Localize(s.m_name);
                        break;
                    }
                }
            }
            return effect;
        }
    }
}
