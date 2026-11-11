using System;
using System.Collections.Generic;
using HarmonyLib;
using ValheimServerGuide.Config;
using ValheimServerGuide.State;
using ValheimServerGuide.Triggers;

namespace ValheimServerGuide.Display
{
    /// Keeps the vanilla compendium's Raven tab (Tab key) in step with the YAML.
    ///
    /// WHY THIS EXISTS. When a player reads a raven, vanilla files its text as a page under the
    /// raven's label (Raven.Talk → Player.AddKnownText), in the character save. AddKnownText
    /// only ever ADDS: a label that is already there is left alone, and nothing anywhere removes
    /// one. So a VSG raven's page froze at whatever text the player first read — editing the
    /// entry never reached it, and deleting the entry left the page there forever. The Guide
    /// Codex re-reads the YAML every time, which is why it looked right while the Raven tab did not.
    ///
    /// WHAT IT DOES.
    ///   - On read: records the page as VSG's (RavenCompendiumState), lets a re-read overwrite
    ///     a stale page, and files nothing at all for an entry with `display.compendium: false`.
    ///   - On every config load / login: rewrites pages whose entry was edited, re-files pages
    ///     whose topic changed, and removes pages whose entry was deleted or opted out.
    ///   - `raven_compendium_remove:` (top level) deletes named labels — the way out for pages
    ///     written before VSG kept records, whose entries are already gone from the YAML.
    ///
    /// Only pages VSG recorded (or can prove are its own) are ever touched; vanilla's and other
    /// mods' pages are not.
    internal static class RavenCompendium
    {
        /// One raven-mode source in the config: a single entry, or one step of a chain. Keyed by
        /// the raven key the dispatcher shows it under — entry.Id, or "{id}_s{index}" for a step
        /// (GuidanceDispatcher.FireStepDisplay), which is also the key Raven.Talk reports.
        private sealed class Source
        {
            public GuidanceEntry Entry;
            public GuidanceStep Step;
            public int StepIndex = -1;
            public DisplaySpec Display;
            public string Raw;
        }

        private static GuidanceConfig _indexedFor;
        private static readonly Dictionary<string, Source> _sources
            = new Dictionary<string, Source>(StringComparer.Ordinal);

        /// The ZNet session whose server config this client holds. Until the server's config
        /// arrives, Plugin.CurrentConfig is whatever local YAML was loaded at the main menu —
        /// reconciling against that would delete every page this server wrote.
        private static ZNet _configFrom;

        /// Raven key being read right now, set around Raven.Talk: AddKnownText is handed only a
        /// label and a text, with nothing that says which raven they came from.
        private static string _talkingKey;

        // ── Entry points ──────────────────────────────────────────────────────────────────────

        /// GuidanceSync.ApplyConfig — the only place a client learns the config (invariant 11).
        internal static void OnConfigReceived()
        {
            _configFrom = ZNet.instance;
            Reconcile("config received");
        }

        /// Run the sync now if it is safe to: a local player, a bound progress store (that is
        /// where the records live) and a config that really is this server's.
        internal static void Reconcile(string why)
        {
            var player = Player.m_localPlayer;
            if (player == null || !PlayerProgress.IsReady || !ConfigTrusted) return;
            var config = Plugin.CurrentConfig;
            if (config == null) return;
            try { DoReconcile(player, config, why); }
            catch (Exception e) { Plugin.Log.LogWarning($"[compendium] sync failed ({why}): {e.Message}"); }
        }

        private static bool ConfigTrusted
        {
            get
            {
                var z = ZNet.instance;
                if (z == null) return false;     // main menu: no world, no pages to manage
                if (z.IsServer()) return true;   // host / single-player: the local YAML IS the config
                return ReferenceEquals(_configFrom, z);
            }
        }

        // ── Read time ─────────────────────────────────────────────────────────────────────────

        /// Prefix on Player.AddKnownText. Returns false to skip vanilla's add.
        internal static bool OnAddKnownText(Player player, string label, string text)
        {
            var key = _talkingKey;
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(label)) return true;
            if (player == null || player != Player.m_localPlayer) return true;
            if (!GuidanceDisplay.RegisteredTutorialNames.Contains(key)) return true; // a vanilla raven

            try
            {
                Index(Plugin.CurrentConfig).TryGetValue(key, out var src);
                if (src == null || !src.Display.Compendium)
                {
                    // Opted out — or deleted from the YAML while its raven was already queued.
                    Plugin.Log.LogInfo($"[compendium] '{key}' read — not filed under the Raven tab " +
                                       (src == null ? "(entry no longer in the config)." : "(compendium: false)."));
                    return false;
                }

                RavenCompendiumState.Set(player, key, label, Hash(src));

                // Vanilla keeps whatever text the label already holds. A re-read raven is the
                // newest text the player has seen, so it replaces the old page.
                if (player.m_knownTexts.ContainsKey(label))
                {
                    player.m_knownTexts[label] = text;
                    return false;
                }
                return true; // vanilla files it and shows its "new text" notice
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[compendium] '{key}': {e.Message}");
                return true;
            }
        }

        // ── Config / login time ───────────────────────────────────────────────────────────────

        private static void DoReconcile(Player player, GuidanceConfig config, string why)
        {
            var known   = player.m_knownTexts;
            var sources = Index(config);
            int adopted = 0, rewritten = 0, removed = 0;

            // 1. Pages written before VSG kept records (pre-0.18.0). A page filed under the label
            //    of a raven this player has been shown is that raven's; claim it so step 2 brings
            //    it up to date. The seen-check keeps a vanilla or other-mod page that happens to
            //    share the label from being claimed.
            foreach (var kv in sources)
            {
                if (RavenCompendiumState.TryGet(player, kv.Key, out _)) continue;
                var label = LabelFor(kv.Key, kv.Value);
                if (string.IsNullOrEmpty(label) || !known.ContainsKey(label)) continue;
                if (!player.m_shownTutorials.Contains(kv.Key)) continue;
                RavenCompendiumState.Set(player, kv.Key, label, ""); // unknown source: rewritten below
                adopted++;
            }

            // Labels that a live, opted-in raven still files a page under. Two entries can share a
            // topic; deleting one of them must not take the other's page with it.
            var records    = RavenCompendiumState.Keys(player);
            var liveLabels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in records)
                if (sources.TryGetValue(key, out var live) && live.Display.Compendium)
                {
                    var l = LabelFor(key, live);
                    if (!string.IsNullOrEmpty(l)) liveLabels.Add(l);
                }

            // 2. Every page VSG wrote.
            foreach (var key in records)
            {
                if (!RavenCompendiumState.TryGet(player, key, out var rec)) continue;
                sources.TryGetValue(key, out var src);

                var label = src != null && src.Display.Compendium ? LabelFor(key, src) : null;
                if (string.IsNullOrEmpty(label))
                {
                    // Deleted, opted out, no longer raven mode, or its topic emptied.
                    if (!liveLabels.Contains(rec.Label) && known.Remove(rec.Label)) removed++;
                    RavenCompendiumState.Remove(player, key);
                    continue;
                }

                if (!known.TryGetValue(rec.Label, out var current))
                {
                    // The page is gone (an older save, or removed by hand) — nothing to keep in step.
                    RavenCompendiumState.Remove(player, key);
                    continue;
                }

                var hash = Hash(src);
                if (hash == rec.Hash && label == rec.Label) continue;

                // Unchanged source but a new label (a {playerName} topic, say): keep the text the
                // player was actually shown, event tokens and all. Changed source: re-render it.
                var text = hash == rec.Hash ? current : Render(src);
                if (label != rec.Label && !liveLabels.Contains(rec.Label)) known.Remove(rec.Label);
                known[label] = text;
                RavenCompendiumState.Set(player, key, label, hash);
                rewritten++;
            }

            // 3. The operator's purge list, for unrecorded pages whose entries are already gone.
            if (config.RavenCompendiumRemove != null)
                foreach (var name in config.RavenCompendiumRemove)
                {
                    var label = name?.Trim();
                    if (string.IsNullOrEmpty(label) || liveLabels.Contains(label)) continue;
                    if (known.Remove(label)) removed++;
                }

            if (adopted + rewritten + removed > 0)
                Plugin.Log.LogInfo($"[compendium] {why}: Raven tab synced — {rewritten} page(s) rewritten, " +
                                   $"{removed} removed ({adopted} pre-0.18.0 page(s) adopted).");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────

        private static Dictionary<string, Source> Index(GuidanceConfig config)
        {
            if (ReferenceEquals(config, _indexedFor)) return _sources;
            _indexedFor = config;
            _sources.Clear();
            if (config?.Guidances == null) return _sources;

            foreach (var e in config.Guidances)
            {
                if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                if (e.Steps != null && e.Steps.Count > 0)
                {
                    for (var i = 0; i < e.Steps.Count; i++)
                    {
                        var step = e.Steps[i];
                        if (step == null) continue;
                        // Same resolution as GuidanceDispatcher.FireStepDisplay.
                        var d = step.Display ?? e.Display ?? new DisplaySpec();
                        if (!IsRaven(d.Mode)) continue;
                        _sources[e.Id + "_s" + i] = new Source
                        {
                            Entry = e, Step = step, StepIndex = i, Display = d,
                            Raw = !string.IsNullOrEmpty(step.Message) ? step.Message : d.Text,
                        };
                    }
                }
                else
                {
                    // GuidanceDisplay.Show treats a missing display as raven.
                    var d = e.Display ?? new DisplaySpec();
                    if (!IsRaven(d.Mode)) continue;
                    _sources[e.Id] = new Source
                    {
                        Entry = e, Display = d,
                        Raw = !string.IsNullOrEmpty(e.Message) ? e.Message : d.Text,
                    };
                }
            }
            return _sources;
        }

        /// The label vanilla files the page under: the tutorial slot's m_label, which is the
        /// templated topic (GuidanceDisplay.UpdateTutorialText), or the raven key when there is
        /// no topic (GuidanceDisplay.EnsureTutorialRegistered). An empty topic files nothing.
        private static string LabelFor(string key, Source src)
        {
            var topic = src.Display.Topic;
            if (topic == null) return key;
            return GuidanceDispatcher.TemplateLocal(topic) ?? "";
        }

        /// Same pipeline as the fire path (template, then highlight), minus the event: tokens
        /// only an event can fill are left verbatim rather than blanked (see TemplateLocal).
        private static string Render(Source src)
        {
            var text = GuidanceDispatcher.TemplateLocal(src.Raw ?? "") ?? "";
            if (src.Step != null)
                text = text.Replace("{step}", (src.StepIndex + 1).ToString())
                           .Replace("{total}", src.Entry.Steps.Count.ToString());
            return TextHighlighter.Apply(text, src.Entry, src.Step);
        }

        /// FNV-1a over the YAML topic and text — stable across runs, unlike string.GetHashCode.
        private static string Hash(Source src)
        {
            var s = (src.Display.Topic ?? "\0") + "\u001F" + (src.Raw ?? "");
            unchecked
            {
                uint h = 2166136261;
                foreach (var c in s) { h ^= c; h *= 16777619; }
                return h.ToString("x8");
            }
        }

        private static bool IsRaven(string mode)
            => string.Equals(mode ?? "raven", "raven", StringComparison.OrdinalIgnoreCase);

        // ── Patches ───────────────────────────────────────────────────────────────────────────

        internal static void SetTalking(string key) => _talkingKey = key;
    }

    /// Brackets Raven.Talk so AddKnownText knows which raven it is filing.
    [HarmonyPatch(typeof(Raven), nameof(Raven.Talk))]
    internal static class RavenTalkCompendiumPatch
    {
        private static void Prefix(Raven __instance)
            => RavenCompendium.SetTalking(__instance.m_currentText?.m_key);

        private static void Finalizer() => RavenCompendium.SetTalking(null);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.AddKnownText))]
    internal static class PlayerAddKnownTextCompendiumPatch
    {
        private static bool Prefix(Player __instance, string label, string text)
            => RavenCompendium.OnAddKnownText(__instance, label, text);
    }

    /// Single-player and host bind their progress store synchronously at spawn, so
    /// PlayerProgress.OnBecameReady never runs for them; this covers that path.
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class PlayerOnSpawnedCompendiumPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) RavenCompendium.Reconcile("spawned");
        }
    }
}
