using System.Collections.Generic;
using System.Text;
using ValheimServerGuide.Config;

namespace ValheimServerGuide.Display
{
    /// Applies `highlight:` rules to already-templated display text by wrapping the matched
    /// spans in TMP rich-text tags. Every in-game surface renders through TextMeshPro, so one
    /// pass here covers raven, message, chat, rune, intro, conversation, bubble, the Codex,
    /// the HUD tracker and NPC hover text alike.
    ///
    /// Deliberately NOT applied to Discord messages — a webhook post would show the literal
    /// `&lt;color=#FFCC55&gt;` markup. Discord templating stays on the raw text (CRIT-08).
    ///
    /// Two invariants make the output safe to render:
    ///   1. Matching never looks inside an existing rich-text tag, so an author's own
    ///      `&lt;color&gt;` markup (or a `{token}` that expanded into markup) is left alone.
    ///   2. A span that one rule has already highlighted is locked — later rules skip it —
    ///      so tags never nest or interleave into something TMP can't parse.
    internal static class TextHighlighter
    {
        /// Rule priority, most specific first: the step's own list, then the entry's, then the
        /// server-wide list from the merged YAML config. `step`/`entry` may be null.
        internal static string Apply(string text, GuidanceEntry entry, GuidanceStep step = null)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var rules = Collect(entry, step);
            if (rules.Count == 0) return text;

            // Segments alternate between plain text (matchable) and locked spans (markup or
            // an already-highlighted result). Start with the whole string split on tags.
            var segments = SplitOnMarkup(text);

            foreach (var rule in rules)
            {
                var tags = TagsFor(rule);
                if (string.IsNullOrEmpty(tags.Open)) continue;     // nothing to change

                foreach (var phrase in Phrases(rule))
                {
                    if (string.IsNullOrEmpty(phrase)) continue;
                    ApplyPhrase(segments, phrase, rule, tags.Open, tags.Close);
                }
            }

            var sb = new StringBuilder(text.Length + 32);
            foreach (var seg in segments) sb.Append(seg.Text);
            return sb.ToString();
        }

        // ── Rule collection ───────────────────────────────────────────────────────────────

        private static List<HighlightSpec> Collect(GuidanceEntry entry, GuidanceStep step)
        {
            var rules = new List<HighlightSpec>();
            if (step?.Highlight != null)  rules.AddRange(step.Highlight);
            if (entry?.Highlight != null) rules.AddRange(entry.Highlight);
            var global = Plugin.CurrentConfig?.Highlight;
            if (global != null) rules.AddRange(global);
            return rules;
        }

        private static IEnumerable<string> Phrases(HighlightSpec rule)
        {
            if (!string.IsNullOrEmpty(rule.Text)) yield return rule.Text;
            if (rule.Any == null) yield break;
            foreach (var a in rule.Any) yield return a;
        }

        // ── Tag building ──────────────────────────────────────────────────────────────────

        private struct Tags
        {
            internal string Open;
            internal string Close;
        }

        /// Compiled tags are cached per rule instance. Apply() runs on every render — a Codex
        /// selection, a tracker refresh, each conversation node — so without this a malformed
        /// `color:` would re-log its warning forever instead of once per config load. The weak
        /// table drops its entries when a YAML reload replaces the rule objects.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HighlightSpec, object>
            _tagCache = new System.Runtime.CompilerServices.ConditionalWeakTable<HighlightSpec, object>();

        private static Tags TagsFor(HighlightSpec rule)
        {
            if (_tagCache.TryGetValue(rule, out var cached)) return (Tags)cached;
            var tags = BuildTags(rule);
            _tagCache.Add(rule, tags);
            return tags;
        }

        /// Builds the opening/closing tag runs. Open is "" when the rule would change nothing,
        /// which Apply treats as "skip this rule".
        private static Tags BuildTags(HighlightSpec rule)
        {
            var styles = new List<string>(StyleTags(rule.Style));
            var color  = NormalizeColor(rule.Color);

            var open = new StringBuilder();
            if (color != null) open.Append("<color=").Append(color).Append('>');
            if (rule.SizePercent > 0f)
                open.Append("<size=").Append(rule.SizePercent.ToString("0.##")).Append("%>");
            foreach (var tag in styles) open.Append('<').Append(tag).Append('>');

            // Close in reverse order of opening so the tags nest properly.
            var close = new StringBuilder();
            for (var i = styles.Count - 1; i >= 0; i--)
                close.Append("</").Append(styles[i]).Append('>');
            if (rule.SizePercent > 0f) close.Append("</size>");
            if (color != null) close.Append("</color>");

            return new Tags { Open = open.ToString(), Close = close.ToString() };
        }

        private static IEnumerable<string> StyleTags(string style)
        {
            if (string.IsNullOrEmpty(style)) yield break;
            foreach (var raw in style.Split(' ', ',', '|'))
            {
                var s = raw.Trim().ToLowerInvariant();
                switch (s)
                {
                    case "bold":          yield return "b"; break;
                    case "italic":        yield return "i"; break;
                    case "underline":     yield return "u"; break;
                    case "strikethrough": yield return "s"; break;
                    case "":
                    case "normal":        break;
                    default:
                        Plugin.Log.LogWarning($"[highlight] unknown style '{raw}' — ignored " +
                                              "(use Bold | Italic | Underline | Strikethrough).");
                        break;
                }
            }
        }

        /// "#RRGGBB" / "RRGGBB" / "#RRGGBBAA" → "#RRGGBB[AA]". Returns null when unusable, so
        /// a typo drops the color instead of emitting a tag TMP would print verbatim.
        private static string NormalizeColor(string color)
        {
            if (string.IsNullOrEmpty(color)) return null;
            var c = color.Trim();
            if (c.StartsWith("#")) c = c.Substring(1);
            if (c.Length != 6 && c.Length != 8)
            {
                Plugin.Log.LogWarning($"[highlight] color '{color}' is not #RRGGBB or #RRGGBBAA — ignored.");
                return null;
            }
            foreach (var ch in c)
            {
                var hex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
                if (hex) continue;
                Plugin.Log.LogWarning($"[highlight] color '{color}' has a non-hex character — ignored.");
                return null;
            }
            return "#" + c;
        }

        // ── Segment model ─────────────────────────────────────────────────────────────────

        private class Segment
        {
            internal string Text;
            /// Locked segments are skipped by matching: rich-text markup, or a span some
            /// earlier rule already wrapped.
            internal bool Locked;
        }

        /// Splits into alternating plain/markup segments. A '<' only opens a tag when a '>'
        /// follows on the same string with no other '<' between — anything else is literal
        /// text (Valheim guides use "<" as an arrow often enough to matter).
        private static List<Segment> SplitOnMarkup(string text)
        {
            var segments = new List<Segment>();
            var i = 0;
            var plainStart = 0;

            while (i < text.Length)
            {
                if (text[i] != '<') { i++; continue; }

                var close = text.IndexOf('>', i + 1);
                var nextOpen = text.IndexOf('<', i + 1);
                if (close < 0 || (nextOpen >= 0 && nextOpen < close)) { i++; continue; }

                if (i > plainStart)
                    segments.Add(new Segment { Text = text.Substring(plainStart, i - plainStart) });
                segments.Add(new Segment { Text = text.Substring(i, close - i + 1), Locked = true });

                i = close + 1;
                plainStart = i;
            }

            if (plainStart < text.Length)
                segments.Add(new Segment { Text = text.Substring(plainStart) });

            return segments;
        }

        // ── Matching ──────────────────────────────────────────────────────────────────────

        private static void ApplyPhrase(List<Segment> segments, string phrase, HighlightSpec rule,
            string tagOpen, string tagClose)
        {
            var comparison = rule.MatchCase
                ? System.StringComparison.Ordinal
                : System.StringComparison.OrdinalIgnoreCase;
            var wholeWord = rule.WholeWord ?? IsWordLike(phrase);

            for (var s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                if (seg.Locked) continue;

                var idx = IndexOfMatch(seg.Text, phrase, 0, comparison, wholeWord);
                if (idx < 0) continue;

                // Rebuild this segment as before / highlighted / after, then continue scanning
                // the "after" part in place so every occurrence in the segment is caught.
                var replacements = new List<Segment>();
                var cursor = 0;
                while (idx >= 0)
                {
                    if (idx > cursor)
                        replacements.Add(new Segment { Text = seg.Text.Substring(cursor, idx - cursor) });

                    var matched = seg.Text.Substring(idx, phrase.Length);
                    replacements.Add(new Segment { Text = tagOpen + matched + tagClose, Locked = true });

                    cursor = idx + phrase.Length;
                    if (rule.First) break;
                    idx = IndexOfMatch(seg.Text, phrase, cursor, comparison, wholeWord);
                }
                if (cursor < seg.Text.Length)
                    replacements.Add(new Segment { Text = seg.Text.Substring(cursor) });

                segments.RemoveAt(s);
                segments.InsertRange(s, replacements);
                s += replacements.Count - 1;

                if (rule.First) return;
            }
        }

        private static int IndexOfMatch(string haystack, string needle, int start,
            System.StringComparison comparison, bool wholeWord)
        {
            while (start <= haystack.Length - needle.Length)
            {
                var idx = haystack.IndexOf(needle, start, comparison);
                if (idx < 0) return -1;
                if (!wholeWord || IsBoundedMatch(haystack, idx, needle.Length)) return idx;
                start = idx + 1;
            }
            return -1;
        }

        private static bool IsBoundedMatch(string text, int idx, int length)
        {
            if (idx > 0 && IsWordChar(text[idx - 1])) return false;
            var after = idx + length;
            return after >= text.Length || !IsWordChar(text[after]);
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// A phrase bounded by letters/digits on both ends is treated as a word, so it only
        /// matches on word boundaries. "[F7]" / "(Locked" / "—" are not, and match anywhere.
        private static bool IsWordLike(string phrase)
            => phrase.Length > 0
               && IsWordChar(phrase[0])
               && IsWordChar(phrase[phrase.Length - 1]);
    }
}
