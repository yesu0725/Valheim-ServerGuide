using System.Collections.Generic;

namespace ValheimServerGuide.State
{
    /// Per-character "hidden from the Codex list" set, stored as a comma-joined id list in
    /// Player.m_customData under VSG.hid (rides with the character save, same as VSG.fired).
    ///
    /// This is purely a display preference. Hiding a quest never affects whether it fires,
    /// tracks, announces or rewards — it only removes its row from the Codex guide list so a
    /// player with a hundred completed guides can keep the list readable.
    public static class HiddenQuestState
    {
        private const string Key = "VSG.hid";

        public static bool IsHidden(Player player, string id)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(id)) return false;
            return GetSet(player).Contains(id);
        }

        public static void SetHidden(Player player, string id, bool hidden)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(id)) return;
            var set = GetSet(player);
            var changed = hidden ? set.Add(id) : set.Remove(id);
            if (!changed) return;
            if (set.Count == 0) player.m_customData.Remove(Key);
            else player.m_customData[Key] = string.Join(",", new List<string>(set).ToArray());
        }

        public static int Count(Player player)
        {
            if (player?.m_customData == null) return 0;
            return GetSet(player).Count;
        }

        public static IReadOnlyCollection<string> GetHiddenIds(Player player)
        {
            if (player?.m_customData == null) return System.Array.Empty<string>();
            return GetSet(player);
        }

        /// Unhide everything. Returns how many ids were cleared.
        public static int ClearAll(Player player)
        {
            if (player?.m_customData == null) return 0;
            var count = GetSet(player).Count;
            player.m_customData.Remove(Key);
            return count;
        }

        /// Drop a single id from the hidden set (used by vsg_reset &lt;id&gt;).
        public static bool Clear(Player player, string id)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(id)) return false;
            if (!IsHidden(player, id)) return false;
            SetHidden(player, id, false);
            return true;
        }

        private static HashSet<string> GetSet(Player player)
        {
            if (!player.m_customData.TryGetValue(Key, out var csv) || string.IsNullOrEmpty(csv))
                return new HashSet<string>();
            return new HashSet<string>(csv.Split(','));
        }
    }
}
