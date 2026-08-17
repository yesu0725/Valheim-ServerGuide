using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ValheimServerGuide.Display
{
    /// Fixed-step mouse-wheel scrolling for a `ScrollRect`.
    ///
    /// `ScrollRect.OnScroll` moves the content by `scrollDelta * scrollSensitivity`, and that
    /// delta is not a stable quantity. Valheim drives the UI through Unity's
    /// `InputSystemUIInputModule`, which reports a notch as
    /// `raw / InputSystem.scrollWheelDeltaPerTick * scrollDeltaPerTick` — a small fraction of a
    /// unit once the raw wheel value has been normalised. A sensitivity that looks generous on
    /// paper therefore crawled a pixel or two per notch in game.
    ///
    /// This handler throws the magnitude away and moves the content a fixed number of pixels per
    /// notch, so every VSG scroll view moves at the same, tunable speed. `Attach` also zeroes the
    /// ScrollRect's own sensitivity: both handlers end up on the same GameObject and
    /// `ExecuteEvents` runs every `IScrollHandler` it finds there, which would otherwise
    /// double-apply the scroll.
    ///
    /// Dragging the content and the scrollbar are untouched — those go through `IDragHandler`.
    internal class WheelScroller : MonoBehaviour, IScrollHandler
    {
        /// Content pixels moved per wheel notch. Roughly a third of a full-height VSG panel.
        public const float DefaultStep = 180f;

        private ScrollRect _target;
        private float _step = DefaultStep;

        /// Take over wheel handling for `target`. Call once per ScrollRect at build time, **after**
        /// `viewport` and `content` have been assigned.
        public static void Attach(ScrollRect target, float step = DefaultStep)
        {
            if (target == null) return;
            var scroller = target.gameObject.AddComponent<WheelScroller>();
            scroller._target = target;
            scroller._step   = step;
            target.scrollSensitivity = 0f;
            EnsureWheelCatcher(target);
        }

        /// A wheel event reaches a scroll view only when the pointer hits a `Graphic` inside it —
        /// `ExecuteEvents` walks *up* from whatever the raycast hit to find the handler. VSG body
        /// text and row labels are mostly `raycastTarget = false` (so they never steal clicks), so
        /// the viewport needs an invisible full-size catcher or the wheel falls straight through to
        /// the panel background and nothing scrolls.
        private static void EnsureWheelCatcher(ScrollRect target)
        {
            var vp = target.viewport != null ? target.viewport : target.GetComponent<RectTransform>();
            if (vp == null || vp.GetComponent<Graphic>() != null) return;

            var img = vp.gameObject.AddComponent<Image>();
            img.color         = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;
        }

        public void OnScroll(PointerEventData data)
        {
            if (_target == null || _target.content == null || _target.viewport == null) return;

            var dir = data.scrollDelta.y;
            if (Mathf.Approximately(dir, 0f)) return;

            var span = _target.content.rect.height - _target.viewport.rect.height;
            if (span <= 0f) return; // content fits; nothing to scroll

            // Every VSG scroll content is top-anchored at pivot (0.5, 1), so a larger
            // anchoredPosition.y means the view has travelled further down the content. Wheel up
            // (positive delta) therefore has to decrease it.
            var pos = _target.content.anchoredPosition;
            var y   = Mathf.Clamp(pos.y - Mathf.Sign(dir) * _step, 0f, span);
            if (Mathf.Approximately(y, pos.y)) return;

            // ScrollRect notices the moved content in its own LateUpdate and repaints the
            // scrollbar from it, so there is nothing else to keep in sync here.
            _target.content.anchoredPosition = new Vector2(pos.x, y);
        }
    }
}
