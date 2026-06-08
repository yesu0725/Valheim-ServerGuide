using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using ValheimServerGuide.Config;

namespace ValheimServerGuide.Display
{
    /// Render guidance via vanilla UI only:
    ///   raven   -> Hugin tutorial popup (bypasses vanilla "tutorials enabled" toggle;
    ///              gated instead by our own RavenEnabled BepInEx config entry)
    ///   message -> MessageHud toast
    ///   chat    -> chat log line + chat panel forced visible
    ///   rune    -> TextViewer Rune style (screen darkens, centered text)
    ///   intro   -> TextViewer Intro style (Valkyrie-style scrolling text) +
    ///              vanilla "intro" music for the duration
    /// While `rune` or `intro` is on screen the player is flipped into ghost mode
    /// (creatures don't detect them), then restored when the viewer closes.
    public static class GuidanceDisplay
    {
        // Made internal so the Raven.Spawn patch can recognize our tutorials and bypass
        // the vanilla "tutorials enabled" gate just for them.
        internal static readonly HashSet<string> RegisteredTutorialNames = new HashSet<string>();
        private static bool _ghostEngaged;
        private static bool _priorGhostState;
        // True while the intro music is pinned; gates the MusicMan override patch.
        // Deliberately decoupled from display lifetime so dismissing the on-screen
        // text early doesn't yank the music — the lock expires on a time basis.
        internal static bool IntroMusicLockActive;
        internal static float IntroMusicLockUntil;

        // True for the entire intro span: fade-in start through TextViewer hide.
        // Gates the Player.TakeInput and Menu.Show patches so the player is frozen,
        // can't open the menu, can't move the camera with the mouse, etc.
        internal static bool IntroLockActive;

        // Our own black overlay (programmatic Canvas + Image) — avoids hijacking the
        // vanilla loading screen, which carries spinner / tip / image children we'd
        // have to stash and restore.
        private static GameObject _overlayObj;
        private static CanvasGroup _overlayGroup;

        public static void Initialize() { }

        public static void RegisterTutorials(GuidanceConfig config)
        {
            if (config?.Guidances == null) return;
            if (Tutorial.instance == null) return;

            foreach (var entry in config.Guidances)
            {
                if (entry.Display == null) continue;
                if (!IsRaven(entry.Display.Mode)) continue;
                if (string.IsNullOrEmpty(entry.Id)) continue;
                if (RegisteredTutorialNames.Contains(entry.Id)) continue;

                Tutorial.instance.m_texts.Add(new Tutorial.TutorialText
                {
                    m_name = entry.Id,
                    m_label = entry.Display.Topic ?? entry.Id,
                    m_topic = entry.Display.Topic ?? entry.Id,
                    m_text = entry.Message ?? entry.Display.Text ?? "",
                });
                RegisteredTutorialNames.Add(entry.Id);
            }
        }

        public static void Show(GuidanceEntry entry, string renderedText)
        {
            var mode = entry.Display?.Mode ?? "raven";

            if (IsRaven(mode))
            {
                if (!Plugin.RavenEnabled.Value)
                {
                    Plugin.Log.LogInfo($"[show] raven '{entry.Id}' suppressed: RavenEnabled=false in mod config.");
                    return;
                }
                EnsureTutorialRegistered(entry);
                // Overwrite the baked text with the live-rendered version so that
                // (a) top-level `message:` fields are honoured, and
                // (b) template variables ({player_name} etc.) are expanded before display.
                UpdateTutorialText(entry.Id, renderedText);
                if (Player.m_localPlayer == null) { Plugin.Log.LogWarning("[show] raven: no local player."); return; }
                if (Tutorial.instance == null) { Plugin.Log.LogWarning("[show] raven: Tutorial.instance null."); return; }
                // Vanilla gates the raven on Player.m_shownTutorials: Player.ShowTutorial
                // no-ops when HaveSeenTutorial(id) is true (its `force` arg is ignored by
                // Tutorial.ShowText), Raven.CheckSpawn's RemoveSeendTempTexts strips any
                // already-seen temp text, and Raven.Spawn marks a seen key as already-talked.
                // That seen-set persists in the character save, so a raven entry shows once
                // and never again — and vsg_reset (which only clears VSG state) can't revive
                // it. VSG owns repeat semantics via `once`/SeenTracker, so clear the vanilla
                // seen-flag here and queue the text directly through Tutorial.ShowText.
                ClearVanillaTutorialSeen(entry.Id);
                // Two patches make this fire even with vanilla tutorials disabled:
                //   RavenGetBestTextPatch — forces our queued temp text to win selection
                //                            (otherwise a stuck static text like haldor
                //                            starves it, since Spawn() bails on the gate).
                //   RavenSpawnBypassPatch — flips the static gate true for that one Spawn().
                Tutorial.instance.ShowText(entry.Id, true);
                return;
            }

            if (Eq(mode, "message"))
            {
                if (MessageHud.instance == null) { Plugin.Log.LogWarning("[show] message: MessageHud.instance null."); return; }
                MessageHud.instance.ShowMessage(ParsePosition(entry.Display?.Position), renderedText);
                return;
            }

            if (Eq(mode, "chat"))
            {
                if (Chat.instance == null) { Plugin.Log.LogWarning("[show] chat: Chat.instance null."); return; }
                Chat.instance.AddString(ColorizeChat(renderedText));
                ForceChatPanelVisible();
                return;
            }

            if (Eq(mode, "rune"))
            {
                ShowViewer(TextViewer.Style.Rune, entry.Display?.Topic ?? "", renderedText, autoHide: false, lockIntroMusic: false);
                return;
            }

            if (Eq(mode, "intro"))
            {
                ShowIntroWithFade(entry.Display?.Topic ?? "", renderedText);
                return;
            }

            if (Eq(mode, "conversation"))
            {
                NpcConversationPanel.Get().Open(entry, renderedText);
                return;
            }

            Plugin.Log.LogWarning($"[show] unknown display mode '{mode}' on '{entry.Id}'.");
        }

        /// Make the chat panel appear immediately.
        /// Chat.Update: m_hideTimer += dt; SetActive(m_hideTimer < m_hideDelay);
        /// So the panel is visible while the timer is *under* the delay — resetting
        /// the timer to 0 gives a full m_hideDelay window of visibility.
        private static void ForceChatPanelVisible()
        {
            var chat = Chat.instance;
            if (chat == null) return;
            chat.m_hideTimer = 0f;
        }

        /// Wrap chat text in Unity rich-text color tags so guidance reads distinct
        /// from regular say (white) and shout (yellow). Empty/blank config disables.
        private static string ColorizeChat(string text)
        {
            var hex = Plugin.ChatColor?.Value;
            if (string.IsNullOrWhiteSpace(hex)) return text;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return $"<color={hex}>{text}</color>";
        }

        private static void ShowViewer(TextViewer.Style style, string topic, string text, bool autoHide, bool lockIntroMusic)
        {
            if (TextViewer.instance == null) { Plugin.Log.LogWarning($"[show] {style}: TextViewer.instance null."); return; }
            EngageGhostMode();
            if (lockIntroMusic) EngageIntroMusic();
            // TextViewer.ShowText runs the value through Localization; raw text is fine,
            // it's passed through when not a registered token.
            TextViewer.instance.ShowText(style, topic, text, autoHide);
        }

        /// Intro mode with optional fade-to-black + pre-text delay. Ghost mode is
        /// engaged immediately so the player can't be killed during the dark transition.
        /// Music is started at the same moment the text appears, not during the fade.
        private static void ShowIntroWithFade(string topic, string text)
        {
            if (TextViewer.instance == null) { Plugin.Log.LogWarning("[show] intro: TextViewer.instance null."); return; }
            EngageGhostMode();

            var fadeIn = Mathf.Max(0f, Plugin.IntroFadeInDuration?.Value ?? 0f);
            var preDelay = Mathf.Max(0f, Plugin.IntroPreDelay?.Value ?? 0f);

            if (fadeIn <= 0f && preDelay <= 0f)
            {
                // No transition configured -- preserve old behavior.
                EngageIntroMusic();
                TextViewer.instance.ShowText(TextViewer.Style.Intro, topic, text, autoHide: true);
                return;
            }

            if (Plugin.Instance == null) return;
            Plugin.Instance.StartCoroutine(IntroFadeRoutine(topic, text, fadeIn, preDelay));
        }

        private static IEnumerator IntroFadeRoutine(string topic, string text, float fadeIn, float preDelay)
        {
            // Lock player input + menu for the whole intro span. Released by the
            // TextViewer hide patch (or the safety release at the end of this
            // coroutine if the text never shows).
            IntroLockActive = true;
            EnsureOverlay();
            _overlayObj.SetActive(true);
            _overlayGroup.alpha = 0f;

            // Fade in (transparent -> black).
            if (fadeIn > 0f)
            {
                var t = 0f;
                while (t < fadeIn)
                {
                    t += Time.unscaledDeltaTime;
                    _overlayGroup.alpha = Mathf.Clamp01(t / fadeIn);
                    yield return null;
                }
            }
            _overlayGroup.alpha = 1f;

            if (preDelay > 0f)
                yield return new WaitForSecondsRealtime(preDelay);

            EngageIntroMusic();
            TextViewer.instance.ShowText(TextViewer.Style.Intro, topic, text, autoHide: true);

            // Fade overlay back out as the text begins scrolling, so the world
            // reveals beneath the cinematic. Fade-out is shorter than fade-in --
            // we want the text legible quickly.
            const float fadeOut = 1.5f;
            var tt = 0f;
            while (tt < fadeOut)
            {
                tt += Time.unscaledDeltaTime;
                _overlayGroup.alpha = 1f - Mathf.Clamp01(tt / fadeOut);
                yield return null;
            }
            _overlayGroup.alpha = 0f;
            _overlayObj.SetActive(false);
            // IntroLockActive stays true until TextViewer.Hide/HideIntro postfix
            // releases it -- the player should remain frozen for the whole reading.
        }

        /// Build a screen-space-overlay Canvas with a single full-screen black Image.
        /// Sort order is set very high so it sits above all vanilla UI (including
        /// the loading screen during world transitions, in the rare overlap case).
        private static void EnsureOverlay()
        {
            if (_overlayObj != null && _overlayGroup != null) return;

            _overlayObj = new GameObject("VSG_IntroOverlay");
            Object.DontDestroyOnLoad(_overlayObj);

            var canvas = _overlayObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760; // above vanilla UI

            var panel = new GameObject("Black");
            panel.transform.SetParent(_overlayObj.transform, false);
            var rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = panel.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;

            _overlayGroup = panel.AddComponent<CanvasGroup>();
            _overlayGroup.alpha = 0f;
            _overlayGroup.blocksRaycasts = false;
            _overlayGroup.interactable = false;

            _overlayObj.SetActive(false);
        }

        /// Safety release: also fires from TextViewer.Hide/HideIntro postfixes so
        /// the player is unfrozen when the intro text finishes or is dismissed.
        internal static void ReleaseIntroLock()
        {
            IntroLockActive = false;
            // If the overlay was somehow left up (early exit / exception), make sure
            // it's torn down so the player isn't staring at a black screen.
            if (_overlayObj != null && _overlayObj.activeSelf)
            {
                _overlayGroup.alpha = 0f;
                _overlayObj.SetActive(false);
            }
        }

        private static void EngageGhostMode()
        {
            var player = Player.m_localPlayer;
            if (player == null || _ghostEngaged) return;
            _priorGhostState = player.InGhostMode();
            if (!_priorGhostState) player.SetGhostMode(true);
            _ghostEngaged = true;
        }

        /// Releases ghost mode only. Intro music is intentionally NOT released here —
        /// its lifetime is governed by IntroMusicDuration so the soundtrack plays
        /// through even if the player presses a key and dismisses the text early.
        internal static void ReleaseGhostMode()
        {
            if (!_ghostEngaged) return;
            var player = Player.m_localPlayer;
            if (player != null && !_priorGhostState && player.InGhostMode())
            {
                player.SetGhostMode(false);
            }
            _ghostEngaged = false;
            _priorGhostState = false;
        }

        private static void EngageIntroMusic()
        {
            var name = Plugin.IntroMusicName?.Value;
            if (string.IsNullOrEmpty(name)) return;
            if (MusicMan.instance == null) return;
            // Extend any existing lock; restart never shortens it.
            var until = UnityEngine.Time.time + System.Math.Max(0f, Plugin.IntroMusicDuration.Value);
            if (until > IntroMusicLockUntil) IntroMusicLockUntil = until;
            IntroMusicLockActive = true;
            MusicMan.instance.StartMusic(name);
        }

        private static bool IsRaven(string mode) => Eq(mode, "raven");
        private static bool Eq(string a, string b)
            => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        /// Vanilla gates the raven on Player.m_shownTutorials (HaveSeenTutorial). That set
        /// persists in the character save and ignores ShowTutorial's `force` arg, so clearing
        /// the id is the only way to let the same raven display again. VSG decides when that
        /// should happen (a fresh `once`-cleared trigger, or vsg_reset).
        internal static void ClearVanillaTutorialSeen(string id)
        {
            var p = Player.m_localPlayer;
            if (p == null || string.IsNullOrEmpty(id)) return;
            p.m_shownTutorials.Remove(id);
        }

        /// Clear the vanilla seen-flag for every raven id VSG has registered (entries +
        /// chain step keys). Used by `vsg_reset all` so raven entries become showable again.
        internal static void ClearAllVsgTutorialSeen()
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            foreach (var id in RegisteredTutorialNames)
                p.m_shownTutorials.Remove(id);
        }

        /// Clear the vanilla seen-flag for one entry id plus any of its chain-step keys
        /// (registered as "{id}_s{n}"). Used by `vsg_reset <id>`.
        internal static void ClearVsgTutorialSeenForEntry(string entryId)
        {
            var p = Player.m_localPlayer;
            if (p == null || string.IsNullOrEmpty(entryId)) return;
            p.m_shownTutorials.Remove(entryId);
            var stepPrefix = entryId + "_s";
            foreach (var id in RegisteredTutorialNames)
                if (id.StartsWith(stepPrefix, System.StringComparison.Ordinal))
                    p.m_shownTutorials.Remove(id);
        }

        private static void EnsureTutorialRegistered(GuidanceEntry entry)
        {
            if (Tutorial.instance == null) return;
            if (RegisteredTutorialNames.Contains(entry.Id)) return;

            Tutorial.instance.m_texts.Add(new Tutorial.TutorialText
            {
                m_name = entry.Id,
                m_label = entry.Display?.Topic ?? entry.Id,
                m_topic = entry.Display?.Topic ?? entry.Id,
                m_text = entry.Message ?? entry.Display?.Text ?? "",
            });
            RegisteredTutorialNames.Add(entry.Id);
        }

        /// Overwrite the m_text of an already-registered tutorial slot with the
        /// current rendered value so templates and top-level message: fields take effect.
        private static void UpdateTutorialText(string id, string text)
        {
            if (Tutorial.instance == null) return;
            var list = Tutorial.instance.m_texts;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].m_name != id) continue;
                list[i].m_text = text;
                return;
            }
        }

        private static MessageHud.MessageType ParsePosition(string position)
            => Eq(position, "Center") ? MessageHud.MessageType.Center : MessageHud.MessageType.TopLeft;
    }

    /// Restore ghost mode + release the intro input/menu lock when the rune/intro
    /// viewer closes by any path (user click-through, ESC, animator auto-hide).
    [HarmonyPatch(typeof(TextViewer), nameof(TextViewer.Hide))]
    internal static class TextViewerHidePatch
    {
        private static void Postfix()
        {
            GuidanceDisplay.ReleaseGhostMode();
            GuidanceDisplay.ReleaseIntroLock();
        }
    }

    [HarmonyPatch(typeof(TextViewer), nameof(TextViewer.HideIntro))]
    internal static class TextViewerHideIntroPatch
    {
        private static void Postfix()
        {
            GuidanceDisplay.ReleaseGhostMode();
            GuidanceDisplay.ReleaseIntroLock();
        }
    }

    /// Force one of OUR queued tutorials to win raven text selection.
    /// `Raven.GetBestText` prefers a nearby static text (e.g. haldor's) over a temp
    /// tutorial on a priority tie. When vanilla tutorials are disabled, that static
    /// text can never spawn (Spawn bails on the gate) yet still permanently out-ranks
    /// our temp text, so our raven never reaches Spawn. This postfix overrides the
    /// result with our pending temp text whenever one matches this raven's type.
    [HarmonyPatch(typeof(Raven), nameof(Raven.GetBestText))]
    internal static class RavenGetBestTextPatch
    {
        private static void Postfix(Raven __instance, ref Raven.RavenText __result)
        {
            if (__result != null && GuidanceDisplay.RegisteredTutorialNames.Contains(__result.m_key))
                return; // already one of ours
            if (Raven.m_tempTexts == null) return;

            foreach (var t in Raven.m_tempTexts)
            {
                if (t == null) continue;
                if (t.m_munin != __instance.m_isMunin) continue; // wrong raven (Hugin vs Munin)
                if (!GuidanceDisplay.RegisteredTutorialNames.Contains(t.m_key)) continue;
                __result = t;
                return;
            }
        }
    }

    /// Force the vanilla raven gate (static Raven.m_tutorialsEnabled, normally controlled
    /// by the in-game "Tutorials Enabled" setting) to true for the single call to Spawn()
    /// that's about to render one of OUR guidance entries. Vanilla tutorials are untouched.
    /// We can't bracket the call site because Raven.Spawn is invoked from Raven.CheckSpawn
    /// (a per-frame poll), not synchronously from ShowTutorial.
    [HarmonyPatch(typeof(Raven), nameof(Raven.Spawn))]
    internal static class RavenSpawnBypassPatch
    {
        // Parameter name MUST match the original ("text"), not the field name on RavenText.
        // Harmony binds by name when the signature isn't reflected.
        private static void Prefix(Raven.RavenText text, out bool __state)
        {
            __state = false;
            if (text == null) return;
            if (!GuidanceDisplay.RegisteredTutorialNames.Contains(text.m_key)) return;
            if (Raven.m_tutorialsEnabled) return; // already on, nothing to do
            Raven.m_tutorialsEnabled = true;
            __state = true;
        }

        private static void Postfix(bool __state)
        {
            if (__state) Raven.m_tutorialsEnabled = false;
        }
    }

    /// Freeze the player for the whole intro span. Player.TakeInput gates movement,
    /// camera/mouse-look, attacks, interactions, inventory, etc — returning false
    /// makes the character inert while the cinematic plays.
    [HarmonyPatch(typeof(Player), nameof(Player.TakeInput))]
    internal static class PlayerTakeInputPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!GuidanceDisplay.IntroLockActive) return true; // run vanilla
            __result = false;
            return false; // skip vanilla; player takes no input
        }
    }

    /// Block the ESC menu from opening during the intro. Menu.Show is the entry
    /// point for the pause/options menu; suppressing it keeps the cinematic
    /// uninterrupted. (The intro text itself is dismissed by its own click handler,
    /// which does not route through Menu.Show.)
    [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
    internal static class MenuShowPatch
    {
        private static bool Prefix() => !GuidanceDisplay.IntroLockActive;
    }

    /// Pin the music to the configured "intro" track for IntroMusicDuration
    /// seconds after the most recent intro-mode trigger. The lock outlives the
    /// on-screen text so a player dismissing the display doesn't stop the music.
    /// Vanilla UpdateCurrentMusic re-evaluates every frame based on Game.InIntro()
    /// — false from our viewpoint — so without this patch the track is overridden
    /// the tick after we call StartMusic.
    [HarmonyPatch(typeof(MusicMan), nameof(MusicMan.UpdateCurrentMusic))]
    internal static class MusicManUpdatePatch
    {
        private static bool Prefix(MusicMan __instance)
        {
            if (!GuidanceDisplay.IntroMusicLockActive) return true; // run vanilla

            if (UnityEngine.Time.time >= GuidanceDisplay.IntroMusicLockUntil)
            {
                // Lock expired — release and let vanilla pick environment music.
                GuidanceDisplay.IntroMusicLockActive = false;
                return true;
            }

            var name = Plugin.IntroMusicName?.Value;
            if (string.IsNullOrEmpty(name)) return true;
            if (__instance.GetCurrentMusic() != name) __instance.StartMusic(name);
            return false; // skip vanilla logic this tick
        }
    }
}
