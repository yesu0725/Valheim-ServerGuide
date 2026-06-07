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
    }
}
