using System;
using System.Collections.Generic;

namespace ValheimServerGuide.State
{
    /// In-memory (session-only, not persisted) record of the last N entries fired per
    /// player, for `vsg_debug`. Deliberately not written to m_customData — this is pure
    /// diagnostics and doesn't need to survive a restart.
    public static class DebugFireLog
    {
        private const int MaxEntries = 10;
        private static readonly Dictionary<string, List<(string Id, DateTime When)>> _log
            = new Dictionary<string, List<(string Id, DateTime When)>>();

        public static void Record(string playerName, string id)
        {
            if (string.IsNullOrEmpty(playerName) || string.IsNullOrEmpty(id)) return;
            if (!_log.TryGetValue(playerName, out var list))
            {
                list = new List<(string Id, DateTime When)>();
                _log[playerName] = list;
            }
            list.Add((id, DateTime.Now));
            if (list.Count > MaxEntries) list.RemoveAt(0);
        }

        public static IReadOnlyList<(string Id, DateTime When)> Get(string playerName)
        {
            if (string.IsNullOrEmpty(playerName) || !_log.TryGetValue(playerName, out var list))
                return Array.Empty<(string Id, DateTime When)>();
            return list;
        }
    }
}
