using System.Collections.Generic;
using UnityEngine;

namespace ValheimServerGuide.Display
{
    /// Single owner of the vanilla centre message for every VSG surface that writes there.
    ///
    /// Vanilla's centre slot holds exactly ONE string: `MessageHud.ShowMessage(Center, …)`
    /// overwrites `m_messageCenterText` and restarts its animation, with no queue (only the
    /// TopLeft type is queued). Two calls in the same frame therefore mean the first is never
    /// read — invisible until two quests share a target. Kill one Greyling with both
    /// "Thin the Wilds" and "Thin the Packs" active and only whichever entry the config listed
    /// last would ever be seen advancing.
    ///
    /// So nothing calls `ShowMessage(Center, …)` directly any more. Lines are queued as they are
    /// produced and flushed as ONE multi-line message from `Plugin.Update`, so every quest that
    /// advanced on the same action gets its own line:
    ///
    ///     Thin the Wilds: 3/10
    ///     Thin the Packs: 7/25
    ///
    /// The detailed view stays where it was — the F3 Codex and the F10 tracker.
    public static class CenterToast
    {
        /// Lines produced this frame, in the order they were queued.
        private static readonly List<string> _pending = new List<string>();

        /// Queue one line for this frame's centre message. Duplicate lines collapse: a
        /// `share_progress` kill can credit the same entry twice in a frame (own kill + the
        /// nearby-party RPC), and "Thin the Packs: 7/25" twice reads like a bug.
        public static void Queue(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (_pending.Contains(line)) return;
            _pending.Add(line);
        }

        /// Emit everything queued since the last call as a single centre message. Called once
        /// per frame from Plugin.Update — cheap no-op when nothing is pending.
        public static void Flush()
        {
            if (_pending.Count == 0) return;

            var hud = MessageHud.instance;
            if (hud == null)
            {
                // No HUD to show it on (loading screen, main menu). Drop rather than hold, so a
                // burst of stale progress lines cannot surface minutes later.
                _pending.Clear();
                return;
            }

            var text = _pending.Count == 1 ? _pending[0] : string.Join("\n", _pending.ToArray());
            _pending.Clear();

            // The centre text is a single non-wrapping line by default and does not grow — widen
            // it and turn wrapping on before handing it a multi-line string (CRIT-25).
            GuidanceDisplay.EnsureCenterMessageWraps();
            hud.ShowMessage(MessageHud.MessageType.Center, text);
        }

        /// Drop anything pending. Used on session teardown so lines queued in the last frame of
        /// one session do not appear in the next.
        public static void Clear() => _pending.Clear();
    }
}
