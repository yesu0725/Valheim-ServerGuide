namespace ValheimServerGuide.State
{
    /// Per-player current node within a multi-node conversation (Phase 4).
    /// Key: "VSG.cn.<entryId>" = current node id. Distinct from ChainState's
    /// "VSG.cp./cd." chain-step buckets — this tracks position in a dialogue
    /// tree, not chain-entry progression.
    public static class ConversationNodeState
    {
        private const string Prefix = "VSG.cn.";

        public static string GetCurrentNode(Player player, string entryId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(entryId)) return null;
            return player.m_customData.TryGetValue(Prefix + entryId, out var val) ? val : null;
        }

        public static void SetCurrentNode(Player player, string entryId, string nodeId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(entryId)) return;
            player.m_customData[Prefix + entryId] = nodeId;
        }

        public static void Clear(Player player, string entryId)
        {
            player?.m_customData?.Remove(Prefix + entryId);
        }

        /// Removes ALL conversation-node-state keys. Called by vsg_reset all.
        public static void ResetAll(Player player)
        {
            if (player?.m_customData == null) return;
            var toRemove = new System.Collections.Generic.List<string>();
            foreach (var key in player.m_customData.Keys)
                if (key.StartsWith(Prefix)) toRemove.Add(key);
            foreach (var key in toRemove)
                player.m_customData.Remove(key);
        }
    }
}
