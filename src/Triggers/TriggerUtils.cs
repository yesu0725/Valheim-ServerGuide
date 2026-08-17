namespace ValheimServerGuide.Triggers
{
    internal static class TriggerUtils
    {
        private const string CloneSuffix = "(Clone)";

        /// Strips the Unity runtime "(Clone)" suffix from an instantiated GameObject name.
        public static string NormalizePrefabName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            return raw.EndsWith(CloneSuffix) ? raw.Substring(0, raw.Length - CloneSuffix.Length) : raw;
        }

        /// Resolve a Valheim localization token to display text.
        ///
        /// Almost every display name the game hands us is a TOKEN, not a name: `Trader.m_name` is
        /// `$npc_haldor`, `Character.m_name` is `$enemy_greyling`, `ItemDrop.m_shared.m_name` is
        /// `$item_wood`. Printed straight to a panel or a chat line they read as raw markup, which
        /// is what "Haldor" showed up as. Anything that puts a game-supplied name in front of a
        /// player runs it through here first.
        ///
        /// Only strings containing '$' are handed to Localization — plain text is returned
        /// untouched, so an author's own wording can never be mangled. Falls back to the raw
        /// string when Localization is not up yet (early load) or absent (dedicated server).
        public static string LocalizeName(string tokenOrName)
        {
            if (string.IsNullOrEmpty(tokenOrName)) return tokenOrName;
            if (tokenOrName.IndexOf('$') < 0) return tokenOrName;
            var loc = Localization.instance;
            return loc != null ? loc.Localize(tokenOrName) : tokenOrName;
        }
    }
}
