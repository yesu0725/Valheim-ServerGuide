using System.Collections.Generic;

namespace ValheimServerGuide.State
{
    /// Which pages of the vanilla compendium's Raven tab VSG wrote, so it can rewrite or delete
    /// them later (VSG.rc.&lt;ravenKey&gt; via PlayerProgress).
    ///
    /// The page itself lives in the CHARACTER save (Player.m_knownTexts), where vanilla strips
    /// every marker character on save — nothing on the page can say "VSG made this". Without
    /// this record a page whose entry was deleted from the YAML is indistinguishable from a
    /// vanilla or other-mod page, and can never be safely removed.
    ///
    /// Kept in the server-owned progress file rather than the character, deliberately: a
    /// character that visits a second VSG server must not have the first server's pages deleted
    /// just because the second server's config does not contain them. Each server only ever
    /// manages the pages it wrote itself.
    ///
    /// Value: "&lt;label&gt;\u001F&lt;source hash&gt;" — the label the page is filed under (its key in
    /// m_knownTexts) and a hash of the YAML topic + text it was rendered from. The hash is how
    /// an unchanged entry is told apart from an edited one without re-rendering it: a page
    /// whose source has not changed keeps the text it was shown with, event tokens and all.
    public static class RavenCompendiumState
    {
        private const string Prefix = "VSG.rc.";
        private const char Sep = '\u001F';

        public struct Record
        {
            public string Label;
            public string Hash;
        }

        public static bool TryGet(Player player, string ravenKey, out Record record)
        {
            record = default;
            if (player == null || string.IsNullOrEmpty(ravenKey)) return false;
            if (!PlayerProgress.TryGet(player, Prefix + ravenKey, out var raw) || string.IsNullOrEmpty(raw))
                return false;
            var cut = raw.IndexOf(Sep);
            record.Label = cut < 0 ? raw : raw.Substring(0, cut);
            record.Hash  = cut < 0 ? "" : raw.Substring(cut + 1);
            return true;
        }

        public static void Set(Player player, string ravenKey, string label, string hash)
        {
            if (player == null || string.IsNullOrEmpty(ravenKey) || string.IsNullOrEmpty(label)) return;
            PlayerProgress.Set(player, Prefix + ravenKey, label + Sep + (hash ?? ""));
        }

        public static void Remove(Player player, string ravenKey)
        {
            if (player == null || string.IsNullOrEmpty(ravenKey)) return;
            PlayerProgress.Remove(player, Prefix + ravenKey);
        }

        /// Every raven key with a record — a snapshot, so callers can remove while iterating.
        public static List<string> Keys(Player player)
        {
            var keys = new List<string>();
            if (player == null) return keys;
            foreach (var k in PlayerProgress.KeysWithPrefix(player, Prefix))
                keys.Add(k.Substring(Prefix.Length));
            return keys;
        }
    }
}
