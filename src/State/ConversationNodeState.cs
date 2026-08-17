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
            if (player == null || string.IsNullOrEmpty(entryId)) return null;
            return PlayerProgress.TryGet(player, Prefix + entryId, out var val) ? val : null;
        }

        public static void SetCurrentNode(Player player, string entryId, string nodeId)
        {
            if (player == null || string.IsNullOrEmpty(entryId)) return;
            PlayerProgress.Set(player, Prefix + entryId, nodeId);
        }

        public static void Clear(Player player, string entryId)
        {
            if (player == null || string.IsNullOrEmpty(entryId)) return;
            PlayerProgress.Remove(player, Prefix + entryId);
        }

        /// Removes ALL conversation-node-state keys. Called by vsg_reset all.
        public static void ResetAll(Player player)
        {
            if (player == null) return;
            PlayerProgress.RemoveWithPrefix(player, Prefix);
        }
    }
}
