using System.Collections.Generic;
using UnityEngine;
using ValheimServerGuide.Config;
using ValheimServerGuide.Net;

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
                    case "item":             GrantItem(reward, player);            break;
                    case "skill_exp":        GrantSkillExp(reward, player);        break;
                    case "skill_level":      GrantSkillLevel(reward, player);      break;
                    case "buff":             GrantBuff(reward, player);            break;
                    case "map_pin":          GrantMapPin(reward);                  break;
                    case "location_pin":     GrantLocationPin(reward, player);     break;
                    case "unlock_recipe":    GrantUnlockRecipe(reward, player);    break;
                    case "spawn_creature":   GrantSpawnCreature(reward, player);   break;
                    case "set_global_key":   GrantSetGlobalKey(reward);            break;
                    case "remove_global_key":GrantRemoveGlobalKey(reward);         break;
                    case "set_player_key":   GrantSetPlayerKey(reward, player);    break;
                    case "remove_player_key":GrantRemovePlayerKey(reward, player); break;
                    case "weather":          GrantWeather(reward);                break;
                    case "chat_message":     GrantChatMessage(reward, player);     break;
                    case "teleport":         GrantTeleport(reward, player);        break;
                    case "rename_player":    GrantRenamePlayer(reward, player);    break;
                    case "discord":          GrantDiscord(reward, player);         break;
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
                    case "unlock_recipe":
                        if (!string.IsNullOrEmpty(reward.Recipe) && ObjectDB.instance != null)
                        {
                            var hasRecipe = ObjectDB.instance.m_recipes.Exists(r => r != null && r.name == reward.Recipe);
                            if (!hasRecipe)
                                Plugin.Log.LogWarning($"[rewards] {context}: recipe '{reward.Recipe}' not found.");
                        }
                        break;
                    case "spawn_creature":
                        if (!string.IsNullOrEmpty(reward.Prefab) && ZNetScene.instance != null)
                        {
                            var prefab = ZNetScene.instance.GetPrefab(reward.Prefab);
                            if (prefab == null)
                                Plugin.Log.LogWarning($"[rewards] {context}: creature prefab '{reward.Prefab}' not found.");
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

        private static void GrantMapPin(RewardSpec reward)
        {
            if (Minimap.instance == null)
            {
                Plugin.Log.LogWarning("[rewards] map_pin: Minimap.instance null — skipping.");
                return;
            }
            var pos = new Vector3(reward.X, 0f, reward.Z);
            var name = string.IsNullOrEmpty(reward.Name) ? "Marker" : reward.Name;
            Minimap.instance.AddPin(pos, ParseIcon(reward.Icon), name, true, false);
            Plugin.Log.LogInfo($"[rewards] Added map pin '{name}' at ({reward.X}, {reward.Z}).");
        }

        private static void GrantLocationPin(RewardSpec reward, Player player)
        {
            if (string.IsNullOrEmpty(reward.Location))
            {
                Plugin.Log.LogWarning("[rewards] location_pin reward missing 'location' field — skipping.");
                return;
            }
            if (ZoneSystem.instance == null || Minimap.instance == null)
            {
                Plugin.Log.LogWarning("[rewards] location_pin: ZoneSystem/Minimap not ready — skipping.");
                return;
            }
            if (!ZoneSystem.instance.FindClosestLocation(reward.Location, player.transform.position, out var closest))
            {
                Plugin.Log.LogWarning($"[rewards] location_pin: no discovered instance of '{reward.Location}' found — skipping.");
                return;
            }
            var name = string.IsNullOrEmpty(reward.PinName) ? reward.Location : reward.PinName;
            Minimap.instance.AddPin(closest.m_position, ParseIcon(reward.Icon), name, true, false);
            Plugin.Log.LogInfo($"[rewards] Added location pin '{name}' for '{reward.Location}'.");
        }

        private static Minimap.PinType ParseIcon(string icon)
        {
            if (!string.IsNullOrEmpty(icon) && System.Enum.TryParse<Minimap.PinType>(icon, true, out var parsed))
                return parsed;
            return Minimap.PinType.Icon3;
        }

        private static void GrantUnlockRecipe(RewardSpec reward, Player player)
        {
            if (string.IsNullOrEmpty(reward.Recipe))
            {
                Plugin.Log.LogWarning("[rewards] unlock_recipe reward missing 'recipe' field — skipping.");
                return;
            }
            var recipe = ObjectDB.instance?.m_recipes.Find(r => r != null && r.name == reward.Recipe);
            if (recipe == null)
            {
                Plugin.Log.LogWarning($"[rewards] unlock_recipe: recipe '{reward.Recipe}' not found — skipping.");
                return;
            }
            // AddKnownRecipe is private but the assembly is publicized at build time (see
            // CLAUDE.md), so we can call it directly instead of duplicating its unlock-message logic.
            player.AddKnownRecipe(recipe);
            Plugin.Log.LogInfo($"[rewards] Unlocked recipe '{reward.Recipe}'.");
        }

        private static void GrantSpawnCreature(RewardSpec reward, Player player)
        {
            if (string.IsNullOrEmpty(reward.Prefab))
            {
                Plugin.Log.LogWarning("[rewards] spawn_creature reward missing 'prefab' field — skipping.");
                return;
            }
            var prefab = ZNetScene.instance?.GetPrefab(reward.Prefab);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[rewards] spawn_creature: prefab '{reward.Prefab}' not found — skipping.");
                return;
            }
            int count = Mathf.Max(1, reward.Count);
            for (int i = 0; i < count; i++)
            {
                var offset = Quaternion.Euler(0f, i * (360f / count), 0f) * Vector3.forward * 2.5f;
                var pos = player.transform.position + offset;
                var go = Object.Instantiate(prefab, pos, Quaternion.identity);
                var character = go.GetComponent<Character>();
                if (character != null && reward.Tamed) character.SetTamed(true);
            }
            Plugin.Log.LogInfo($"[rewards] Spawned '{reward.Prefab}' x{count}" + (reward.Tamed ? " (tamed)" : "") + ".");
        }

        private static void GrantSetGlobalKey(RewardSpec reward)
        {
            if (string.IsNullOrEmpty(reward.Key))
            {
                Plugin.Log.LogWarning("[rewards] set_global_key reward missing 'key' field — skipping.");
                return;
            }
            ZoneSystem.instance?.SetGlobalKey(reward.Key);
            Plugin.Log.LogInfo($"[rewards] Set global key '{reward.Key}'.");
        }

        private static void GrantRemoveGlobalKey(RewardSpec reward)
        {
            if (string.IsNullOrEmpty(reward.Key))
            {
                Plugin.Log.LogWarning("[rewards] remove_global_key reward missing 'key' field — skipping.");
                return;
            }
            ZoneSystem.instance?.RemoveGlobalKey(reward.Key);
            Plugin.Log.LogInfo($"[rewards] Removed global key '{reward.Key}'.");
        }

        private static void GrantSetPlayerKey(RewardSpec reward, Player player)
        {
            if (string.IsNullOrEmpty(reward.Key))
            {
                Plugin.Log.LogWarning("[rewards] set_player_key reward missing 'key' field — skipping.");
                return;
            }
            player.AddUniqueKey(reward.Key);
            Plugin.Log.LogInfo($"[rewards] Set player key '{reward.Key}'.");
        }

        private static void GrantRemovePlayerKey(RewardSpec reward, Player player)
        {
            if (string.IsNullOrEmpty(reward.Key))
            {
                Plugin.Log.LogWarning("[rewards] remove_player_key reward missing 'key' field — skipping.");
                return;
            }
            player.RemoveUniqueKey(reward.Key);
            Plugin.Log.LogInfo($"[rewards] Removed player key '{reward.Key}'.");
        }

        private static void GrantWeather(RewardSpec reward)
        {
            if (string.IsNullOrEmpty(reward.Preset))
            {
                Plugin.Log.LogWarning("[rewards] weather reward missing 'preset' field — skipping.");
                return;
            }
            if (EnvMan.instance == null)
            {
                Plugin.Log.LogWarning("[rewards] weather: EnvMan.instance null — skipping.");
                return;
            }
            bool known = EnvMan.instance.m_environments.Exists(e => e != null && e.m_name == reward.Preset);
            if (!known)
                Plugin.Log.LogWarning($"[rewards] weather: preset '{reward.Preset}' not recognised by EnvMan — forcing anyway.");

            EnvMan.instance.SetForceEnvironment(reward.Preset);
            float duration = reward.Duration > 0f ? reward.Duration : 60f;
            Plugin.Instance.StartCoroutine(ClearForcedWeatherAfter(reward.Preset, duration));
            Plugin.Log.LogInfo($"[rewards] Forced weather '{reward.Preset}' for {duration}s.");
        }

        private static System.Collections.IEnumerator ClearForcedWeatherAfter(string preset, float duration)
        {
            yield return new WaitForSeconds(duration);
            // Only clear if nothing else has since forced a different environment.
            if (EnvMan.instance != null && EnvMan.instance.m_forceEnv == preset)
                EnvMan.instance.SetForceEnvironment("");
        }

        private static void GrantChatMessage(RewardSpec reward, Player player)
        {
            if (string.IsNullOrEmpty(reward.Message))
            {
                Plugin.Log.LogWarning("[rewards] chat_message reward missing 'message' field — skipping.");
                return;
            }
            if (Chat.instance == null)
            {
                Plugin.Log.LogWarning("[rewards] chat_message: Chat.instance null — skipping.");
                return;
            }
            var text = ExpandPlayerName(reward.Message, player);
            Chat.instance.AddString(text);
        }

        private static void GrantTeleport(RewardSpec reward, Player player)
        {
            var pos = new Vector3(reward.X, player.transform.position.y, reward.Z);
            // allowlist_only is documentation-only: teleport coordinates come from the
            // server-authoritative synced YAML (CRIT-06), which is already the trust
            // boundary — there is no separate client-supplied destination to allow-list.
            player.TeleportTo(pos, player.transform.rotation, true);
            Plugin.Log.LogInfo($"[rewards] Teleported player to ({reward.X}, {reward.Z}).");
        }

        private static void GrantRenamePlayer(RewardSpec reward, Player player)
        {
            if (string.IsNullOrEmpty(reward.Suffix))
            {
                Plugin.Log.LogWarning("[rewards] rename_player reward missing 'suffix' field — skipping.");
                return;
            }
            var nview = player.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null)
            {
                Plugin.Log.LogWarning("[rewards] rename_player: player ZDO not available — skipping.");
                return;
            }
            var baseName = player.GetPlayerName();
            var newName = baseName + " " + reward.Suffix;
            zdo.Set(ZDOVars.s_playerName, newName);
            Plugin.Log.LogInfo($"[rewards] Renamed player to '{newName}'.");
        }

        private static void GrantDiscord(RewardSpec reward, Player player)
        {
            if (string.IsNullOrEmpty(reward.Message))
            {
                Plugin.Log.LogWarning("[rewards] discord reward missing 'message' field — skipping.");
                return;
            }
            var text = ExpandPlayerName(reward.Message, player);
            GuidanceSync.SendRewardDiscord(text);
        }

        private static string ExpandPlayerName(string template, Player player)
        {
            return template.Replace("{player_name}", player?.GetPlayerName() ?? "");
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
