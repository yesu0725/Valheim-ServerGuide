using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimServerGuide.Display
{
    /// World-space floating text above an NPC's head (Phase 6 `bubble` display mode).
    /// Built entirely from engine components already shipped with the game (TextMeshPro,
    /// the game's own font asset) — no custom assets, per CRIT-14.
    ///
    /// While a VSG bubble is active for a given NPC, vanilla's own floating head text
    /// (Trader random talk / greet / buy-sell lines via Chat.SetNpcText) is suppressed for
    /// that NPC so the two never overlap. Vanilla text resumes automatically once the VSG
    /// bubble's lifetime ends — see NpcBubbleSuppression below.
    public class NpcChatBubble : MonoBehaviour
    {
        private static readonly Vector3 HeadOffset = new Vector3(0f, 2.2f, 0f);
        private const float FadeDuration = 1f;

        private Transform _anchor;
        private GameObject _anchorGo;
        private TextMeshPro _text;
        private float _ttl;

        /// Spawns a bubble above `anchor` showing `message` for `duration` seconds, then
        /// self-destroys. Safe to call repeatedly — each call is an independent instance.
        public static void Show(Transform anchor, string message, float duration)
        {
            if (anchor == null || string.IsNullOrEmpty(message)) return;

            var go = new GameObject("VSG_NpcBubble");
            var bubble = go.AddComponent<NpcChatBubble>();
            bubble.Init(anchor, message, duration > 0f ? duration : 6f);
        }

        private void Init(Transform anchor, string message, float duration)
        {
            _anchor = anchor;
            _anchorGo = anchor.gameObject;
            _ttl = duration;
            transform.position = anchor.position + HeadOffset;

            // Hide whatever vanilla NPC text is showing right now, and block new ones
            // (random talk / greet / buy-sell lines) from appearing until this bubble ends.
            NpcBubbleSuppression.Suppress(_anchorGo);
            Chat.instance?.ClearNpcText(_anchorGo);

            _text = gameObject.AddComponent<TextMeshPro>();
            _text.text = message;
            _text.fontSize = 4f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.color = Color.white;
            _text.outlineWidth = 0.2f;
            _text.outlineColor = new Color(0f, 0f, 0f, 0.9f);
            _text.enableWordWrapping = true;

            var font = GuidanceHudTracker.FindVanillaFontStatic();
            if (font != null) _text.font = font;
        }

        private void Update()
        {
            if (_anchor == null) { Destroy(gameObject); return; }

            transform.position = _anchor.position + HeadOffset;
            if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;

            _ttl -= Time.deltaTime;
            if (_ttl <= FadeDuration)
            {
                var a = Mathf.Clamp01(_ttl / FadeDuration);
                var c = _text.color;
                _text.color = new Color(c.r, c.g, c.b, a);
            }
            if (_ttl <= 0f) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // Resume vanilla NPC text. Runs on every destroy path (ttl expiry, anchor gone,
            // scene unload) so suppression never gets stuck on.
            NpcBubbleSuppression.Unsuppress(_anchorGo);
        }
    }

    /// Tracks which NPC GameObjects currently have an active VSG bubble, so the Harmony
    /// patch below can block vanilla's Chat.SetNpcText for just those NPCs.
    internal static class NpcBubbleSuppression
    {
        private static readonly HashSet<GameObject> _suppressed = new HashSet<GameObject>();

        internal static void Suppress(GameObject go)
        {
            if (go != null) _suppressed.Add(go);
        }

        internal static void Unsuppress(GameObject go)
        {
            if (go != null) _suppressed.Remove(go);
        }

        internal static bool IsSuppressed(GameObject go) => go != null && _suppressed.Contains(go);
    }

    /// Blocks vanilla NPC speech bubbles (Trader random talk/greet/buy/sell) for any NPC
    /// that currently has an active VSG bubble — see NpcChatBubble.Init/OnDestroy.
    [HarmonyPatch(typeof(Chat), nameof(Chat.SetNpcText))]
    internal static class ChatSetNpcTextSuppressionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(GameObject talker)
        {
            return !NpcBubbleSuppression.IsSuppressed(talker);
        }
    }
}
