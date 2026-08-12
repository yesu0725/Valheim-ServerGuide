using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValheimServerGuide.Config;
using ValheimServerGuide.Triggers;

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

        // --- Raven queue + dungeon deferral ---
        // Key of the VSG raven currently in Raven.m_tempTexts (null = none active).
        private static string _activeRavenKey;
        // Frame on which the active raven was submitted; guards against a false "gone"
        // read on the same frame as the ShowText call (list add is synchronous but the
        // Raven poll runs next frame).
        private static int _activeRavenSetFrame;
        // Entries waiting for the current raven to finish before they can show.
        private static readonly Queue<(GuidanceEntry entry, string text)> _ravenQueue
            = new Queue<(GuidanceEntry, string)>();
        // Entries that arrived while the player was inside a dungeon/interior.
        // Drained the first tick after the player exits.
        private static readonly Queue<(GuidanceEntry entry, string text)> _dungeonDeferred
            = new Queue<(GuidanceEntry, string)>();
        private static bool _wasInInterior;

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
                // Defer until the player exits the dungeon/interior.
                if (IsPlayerInInterior())
                {
                    _dungeonDeferred.Enqueue((entry, renderedText));
                    Plugin.Log.LogInfo($"[show] raven '{entry.Id}' deferred: player is inside a dungeon/interior.");
                    return;
                }
                // Queue if another VSG raven is already pending acknowledgement.
                if (_activeRavenKey != null)
                {
                    _ravenQueue.Enqueue((entry, renderedText));
                    Plugin.Log.LogInfo($"[show] raven '{entry.Id}' queued: raven '{_activeRavenKey}' still active ({_ravenQueue.Count} in queue).");
                    return;
                }
                ShowRavenNow(entry, renderedText);
                return;
            }

            if (Eq(mode, "message"))
            {
                if (MessageHud.instance == null) { Plugin.Log.LogWarning("[show] message: MessageHud.instance null."); return; }
                var position = ParsePosition(entry.Display?.Position);
                if (position == MessageHud.MessageType.Center) EnsureCenterMessageWraps();
                MessageHud.instance.ShowMessage(position, renderedText);
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
                // Custom themed rune-reading panel (header/body/list + configurable
                // fonts/colors). Ghost mode is engaged/released by the panel itself.
                RunePanel.Get().Open(entry, renderedText);
                return;
            }

            if (Eq(mode, "intro"))
            {
                ShowIntroWithFade(GuidanceDispatcher.TemplateLocal(entry.Display?.Topic) ?? "", renderedText);
                return;
            }

            if (Eq(mode, "conversation"))
            {
                NpcConversationPanel.Get().Open(entry, renderedText);
                return;
            }

            if (Eq(mode, "bubble"))
            {
                ShowBubble(entry, renderedText);
                return;
            }

            Plugin.Log.LogWarning($"[show] unknown display mode '{mode}' on '{entry.Id}'.");
        }

        /// Finds the nearest GameObject matching display.npc_name (same prefab-name matching
        /// as trigger.npc) and floats the text above its head. No-op with a warning if no
        /// matching, nearby NPC is found — the entry's other side effects (rewards, state)
        /// still applied normally since this only governs the visual.
        ///
        /// Searches both Character (monsters/Humanoids) and Trader (Haldor/BogWitch/Hildir
        /// etc. — Trader is a plain MonoBehaviour with no Character/Humanoid component at
        /// all, so vendor NPCs never show up in Character.GetAllCharacters()).
        private static void ShowBubble(GuidanceEntry entry, string renderedText)
        {
            var npcName = entry.Display?.NpcName;
            if (string.IsNullOrEmpty(npcName))
            {
                Plugin.Log.LogWarning($"[show] bubble '{entry.Id}' missing display.npc_name.");
                return;
            }
            var player = Player.m_localPlayer;
            if (player == null) return;

            Transform nearest = null;
            var bestDistSq = 50f * 50f; // only consider NPCs within 50m

            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null) continue;
                if (!string.Equals(TriggerUtils.NormalizePrefabName(c.gameObject.name), npcName,
                        System.StringComparison.OrdinalIgnoreCase)) continue;
                var distSq = (c.transform.position - player.transform.position).sqrMagnitude;
                if (distSq < bestDistSq) { bestDistSq = distSq; nearest = c.transform; }
            }

            foreach (var t in Object.FindObjectsOfType<Trader>())
            {
                if (t == null) continue;
                if (!string.Equals(TriggerUtils.NormalizePrefabName(t.gameObject.name), npcName,
                        System.StringComparison.OrdinalIgnoreCase)) continue;
                var distSq = (t.transform.position - player.transform.position).sqrMagnitude;
                if (distSq < bestDistSq) { bestDistSq = distSq; nearest = t.transform; }
            }

            if (nearest == null)
            {
                Plugin.Log.LogWarning($"[show] bubble '{entry.Id}': no nearby '{npcName}' found.");
                return;
            }

            NpcChatBubble.Show(nearest, renderedText, entry.Display?.Duration ?? 6f);
        }

        /// Submit one raven entry directly to Tutorial/Raven, bypassing queue/deferral checks.
        /// Only call this when it's confirmed safe to show: not in a dungeon, no active raven.
        private static void ShowRavenNow(GuidanceEntry entry, string renderedText)
        {
            if (Player.m_localPlayer == null) { Plugin.Log.LogWarning("[show] raven: no local player."); return; }
            if (Tutorial.instance == null) { Plugin.Log.LogWarning("[show] raven: Tutorial.instance null."); return; }
            EnsureTutorialRegistered(entry);
            // Evict any stale temp text for this key first. Raven.AddTempText early-returns
            // on a duplicate key, so a leftover entry (e.g. one the player never clicked, or
            // one orphaned by vsg_reset clearing the seen-flag) would silently block re-show.
            // We only reach here with no active raven, so removing it is safe.
            RemoveVanillaTempText(entry.Id);
            EnsureVanillaTextNotTruncated();
            // Overwrite the baked text with the live-rendered version so that
            // (a) top-level `message:` fields are honoured, and
            // (b) template variables ({player_name} etc.) are expanded before display.
            UpdateTutorialText(entry.Id, renderedText,
                GuidanceDispatcher.TemplateLocal(entry.Display?.Topic));
            // Vanilla gates the raven on Player.m_shownTutorials: clear the seen-flag so
            // VSG (not vanilla) owns repeat semantics via `once`/SeenTracker.
            ClearVanillaTutorialSeen(entry.Id);
            // Two patches make this fire even with vanilla tutorials disabled:
            //   RavenGetBestTextPatch — forces our queued temp text to win selection.
            //   RavenSpawnBypassPatch — flips the static gate true for that one Spawn().
            Tutorial.instance.ShowText(entry.Id, true);
            _activeRavenKey = entry.Id;
            _activeRavenSetFrame = Time.frameCount;
            Plugin.Log.LogInfo($"[show] raven '{entry.Id}' submitted to Tutorial; awaiting player interaction.");
        }

        /// Called every frame from Plugin.Update.
        /// Checks for dungeon exit (drain deferred queue) and raven completion (drain raven queue).
        public static void Tick()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            // Dungeon exit: drain the deferred queue into the raven pipeline.
            var inInterior = player.InInterior();
            if (_wasInInterior && !inInterior && _dungeonDeferred.Count > 0)
            {
                Plugin.Log.LogInfo($"[raven] player exited interior — draining {_dungeonDeferred.Count} deferred raven entry/entries.");
                while (_dungeonDeferred.Count > 0)
                {
                    var (e, t) = _dungeonDeferred.Dequeue();
                    if (_activeRavenKey != null)
                    {
                        _ravenQueue.Enqueue((e, t));
                        Plugin.Log.LogInfo($"[raven] '{e.Id}' re-queued (raven still busy).");
                    }
                    else
                    {
                        ShowRavenNow(e, t);
                    }
                }
            }
            _wasInInterior = inInterior;

            // Raven completion: once the active key leaves Raven.m_tempTexts the player
            // has interacted with (or the raven has dismissed) the current entry.
            // Guard with a 1-frame delay so the list-add from ShowText is visible first.
            if (_activeRavenKey != null
                && Time.frameCount > _activeRavenSetFrame + 1
                && !IsVsgRavenPending(_activeRavenKey))
            {
                Plugin.Log.LogInfo($"[raven] '{_activeRavenKey}' acknowledged — {_ravenQueue.Count} more in queue.");
                _activeRavenKey = null;
                if (_ravenQueue.Count > 0)
                {
                    var (e, t) = _ravenQueue.Dequeue();
                    ShowRavenNow(e, t);
                }
            }
        }

        /// Returns true when our key is still present in Raven.m_tempTexts (raven not yet
        /// acknowledged by the player or dismissed by the vanilla raven system).
        private static bool IsVsgRavenPending(string key)
        {
            if (Raven.m_tempTexts == null || string.IsNullOrEmpty(key)) return false;
            foreach (var t in Raven.m_tempTexts)
                if (t != null && t.m_key == key) return true;
            return false;
        }

        private static bool IsPlayerInInterior()
            => Player.m_localPlayer != null && Player.m_localPlayer.InInterior();

        /// Clear all raven queues. Called on session end (ZNet.OnDestroy) and vsg_reset all.
        internal static void ClearRavenState()
        {
            _activeRavenKey = null;
            _ravenQueue.Clear();
            _dungeonDeferred.Clear();
            _wasInInterior = false;
            // Also evict our leftover temp texts from the vanilla raven queue (see
            // RemoveVanillaTempText). vsg_reset all clears Player.m_shownTutorials, which
            // disables vanilla's own RemoveSeendTempTexts cleanup, so a lingering temp text
            // would otherwise block Raven.AddTempText forever and the raven never re-shows.
            RemoveAllVanillaTempTexts();
            Plugin.Log.LogInfo("[raven] raven queue and deferral state cleared.");
        }

        /// Remove a specific entry id from the raven queue and dungeon-deferred queue.
        /// Also cancels the active raven if it matches, allowing the next queued entry
        /// to show on the next Tick(). Called by vsg_reset <id>.
        internal static void ClearRavenQueueForId(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            if (string.Equals(_activeRavenKey, id, System.StringComparison.OrdinalIgnoreCase))
            {
                _activeRavenKey = null;
                Plugin.Log.LogInfo($"[raven] cancelled active raven '{id}' via vsg_reset.");
            }

            // Evict any leftover temp text for this id from the vanilla raven queue so a
            // reset entry can be re-added by Raven.AddTempText (it early-returns on a
            // duplicate key, and vsg_reset has just cleared the seen-flag that vanilla
            // would otherwise use to clean it up). See RemoveVanillaTempText.
            RemoveVanillaTempText(id);

            var removed = FilterQueue(_ravenQueue, id) + FilterQueue(_dungeonDeferred, id);
            if (removed > 0)
                Plugin.Log.LogInfo($"[raven] removed {removed} queued raven entry/entries for '{id}'.");
        }

        /// Rebuild a queue without entries whose entry.Id matches id. Returns the count removed.
        private static int FilterQueue(Queue<(GuidanceEntry entry, string text)> queue, string id)
        {
            var before = queue.Count;
            var kept = new List<(GuidanceEntry, string)>(queue.Count);
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                if (!string.Equals(item.entry.Id, id, System.StringComparison.OrdinalIgnoreCase))
                    kept.Add(item);
            }
            foreach (var item in kept) queue.Enqueue(item);
            return before - kept.Count;
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

        /// Intro mode with optional fade-to-black + pre-text delay.
        ///
        /// The player is FROZEN (input) and truly INVULNERABLE (ghost mode +
        /// CharacterDamageIntroPatch) from the very first frame through the end of the
        /// cinematic. Release is owned by IntroRoutine's watchdog — it ends when the
        /// intro animation finishes, the player deliberately skips (Use/Escape), or a
        /// hard safety cap elapses. Release is deliberately NOT tied to TextViewer.Hide:
        /// vanilla's TextViewer.LateUpdate calls Hide() on any stray Use/Escape (read
        /// straight from ZInput, bypassing TakeInput) WITHOUT actually closing the intro
        /// animation, so releasing there let the player act + take damage while the intro
        /// was still on screen.
        private static void ShowIntroWithFade(string topic, string text)
        {
            if (TextViewer.instance == null) { Plugin.Log.LogWarning("[show] intro: TextViewer.instance null."); return; }
            if (Plugin.Instance == null) return;

            // Engage the freeze + protection before anything renders so there is no
            // window in which the player can move or be hit.
            IntroLockActive = true;
            EngageGhostMode();

            var fadeIn = Mathf.Max(0f, Plugin.IntroFadeInDuration?.Value ?? 0f);
            var preDelay = Mathf.Max(0f, Plugin.IntroPreDelay?.Value ?? 0f);
            Plugin.Instance.StartCoroutine(IntroRoutine(topic, text, fadeIn, preDelay));
        }

        private static IEnumerator IntroRoutine(string topic, string text, float fadeIn, float preDelay)
        {
            // Optional fade-to-black transition (skipped entirely when both are 0).
            if (fadeIn > 0f || preDelay > 0f)
            {
                EnsureOverlay();
                _overlayObj.SetActive(true);
                _overlayGroup.alpha = 0f;

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
            }

            EngageIntroMusic();
            EnsureVanillaTextNotTruncated();
            TextViewer.instance.ShowText(TextViewer.Style.Intro, topic, text, autoHide: true);

            // Fade the overlay back out as the text begins scrolling, so the world
            // reveals beneath the cinematic.
            if (_overlayObj != null && _overlayObj.activeSelf)
            {
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
            }

            // --- Hold the freeze while the intro is on screen, then release. ---
            // The intro text holds visible until we explicitly close it (vanilla only
            // ends its own intro via Game.SkipIntro -> HideIntro), so we drive the
            // duration ourselves: a fixed display time, or an earlier deliberate skip.
            var hold = Mathf.Clamp(Plugin.IntroDisplaySeconds?.Value ?? 15f, 1f, 300f);
            const float minSkip = 1.0f; // ignore skip input for the first second
            var shown = 0f;
            while (shown < hold)
            {
                shown += Time.unscaledDeltaTime;
                // Deliberate skip (matches vanilla: Use / Escape), after a short beat so
                // a key the player was already mashing doesn't skip instantly.
                if (shown >= minSkip &&
                    (ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse")
                     || Input.GetKeyDown(KeyCode.Escape)))
                    break;
                yield return null;
            }

            // Fade the intro text out (skip or timeout), then lift the freeze — the
            // player is never free while the intro is still shown, nor frozen after.
            yield return FadeOutIntro();
            ReleaseGhostMode();
            ReleaseIntroLock();
        }

        /// Smoothly fade the intro text away, then deactivate it. Fades a CanvasGroup on
        /// TextViewer's intro root (added once, reused) so only the text fades — the world
        /// stays visible. Alpha is reset to 1 afterward so the next intro shows fully.
        private static IEnumerator FadeOutIntro()
        {
            var tv = TextViewer.instance;
            var root = tv != null ? tv.m_introRoot : null;
            if (root == null)
            {
                if (tv != null) tv.HideIntro();
                yield break;
            }

            var cg = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
            var dur = Mathf.Max(0f, Plugin.IntroFadeOutDuration?.Value ?? 1.0f);
            if (dur > 0f)
            {
                var t = 0f;
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    cg.alpha = 1f - Mathf.Clamp01(t / dur);
                    yield return null;
                }
            }
            cg.alpha = 0f;
            tv.HideIntro();   // SetActive(false) under cover of alpha 0
            cg.alpha = 1f;    // reset for the next intro
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

        /// Engage ghost mode for the custom rune panel (invulnerable + undetected while the
        /// reading is up). Release is owned by RunePanel.Close via ReleaseGhostMode().
        internal static void BeginRuneGhost() => EngageGhostMode();

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

        /// Remove the temp RavenText with this key from the vanilla static queue
        /// (Raven.m_tempTexts). Raven.AddTempText refuses to add a duplicate key, and
        /// vanilla only clears a temp text once Player.HaveSeenTutorial(key) is true —
        /// a condition vsg_reset deliberately breaks by clearing the seen-flag. Without
        /// this, a leftover temp text permanently blocks the raven from re-showing after
        /// a reset. Safe to call when the list is empty or the key isn't present.
        internal static void RemoveVanillaTempText(string id)
        {
            if (Raven.m_tempTexts == null || string.IsNullOrEmpty(id)) return;
            Raven.m_tempTexts.RemoveAll(t =>
                t != null && string.Equals(t.m_key, id, System.StringComparison.OrdinalIgnoreCase));
        }

        /// Remove every VSG-registered temp text from the vanilla raven queue. Used by
        /// vsg_reset all / session teardown. See RemoveVanillaTempText for the rationale.
        internal static void RemoveAllVanillaTempTexts()
        {
            if (Raven.m_tempTexts == null) return;
            Raven.m_tempTexts.RemoveAll(t => t != null && RegisteredTutorialNames.Contains(t.m_key));
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
        /// The topic/label are baked at registration (before a local player exists), so they
        /// are re-templated here too — otherwise a raven topic prints raw "{playerName}".
        private static void UpdateTutorialText(string id, string text, string topic)
        {
            if (Tutorial.instance == null) return;
            var list = Tutorial.instance.m_texts;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].m_name != id) continue;
                list[i].m_text = text;
                if (!string.IsNullOrEmpty(topic))
                {
                    list[i].m_topic = topic;
                    list[i].m_label = topic;
                }
                return;
            }
        }

        private static MessageHud.MessageType ParsePosition(string position)
            => Eq(position, "Center") ? MessageHud.MessageType.Center : MessageHud.MessageType.TopLeft;

        /// The vanilla TextViewer boxes (raven parchment, intro card, runestone reading) ship
        /// with whatever overflow mode the prefab was authored with — a Truncate/Ellipsis
        /// setting there would silently swallow the tail of a long guide. Force wrapping on and
        /// overflow to Overflow so the text is always rendered in full.
        ///
        /// Note these boxes are fixed-size vanilla art: they do not grow. Overflow means long
        /// text spills past the parchment rather than being cut — readable, but a sign the
        /// entry wants `rune` or `conversation` mode, both of which size themselves to content.
        private static void EnsureVanillaTextNotTruncated()
        {
            var tv = TextViewer.instance;
            if (tv == null) return;

            AllowOverflow(tv.m_text);
            AllowOverflow(tv.m_introText);
            AllowOverflow(tv.m_ravenText);
        }

        private static void AllowOverflow(TMP_Text tmp)
        {
            if (tmp == null) return;
            tmp.enableWordWrapping = true;
            tmp.overflowMode       = TextOverflowModes.Overflow;
        }

        /// Vanilla's centre message is a single non-wrapping line sized for "Odin's blessing"
        /// style one-liners: a full sentence of guidance simply runs off both edges of the
        /// screen. Widen its rect to a readable measure and turn wrapping on so long text
        /// stacks into lines instead.
        ///
        /// Applied to the shared vanilla component rather than a copy, because MessageHud owns
        /// the fade coroutine that makes the message appear at all. It is idempotent and only
        /// ever grows the rect, so vanilla's own centre messages — all far too short to reach
        /// the wrap width — look exactly as they did.
        private static void EnsureCenterMessageWraps()
        {
            var tmp = MessageHud.instance?.m_messageCenterText;
            if (tmp == null) return;

            tmp.enableWordWrapping = true;
            tmp.overflowMode       = TextOverflowModes.Overflow;

            var rt = tmp.rectTransform;
            if (rt == null) return;

            // 70% of the screen: wide enough for a long sentence, narrow enough that the eye
            // does not have to travel the full monitor width per line.
            var targetWidth  = Screen.width * 0.70f;
            var targetHeight = Screen.height * 0.30f;

            // sizeDelta is an offset from the anchors, not an absolute size, whenever the rect
            // is anchored to a stretch. Adjusting by the delta between current and target keeps
            // this correct for either anchoring style.
            var size = rt.rect.size;
            if (size.x < targetWidth)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x + (targetWidth - size.x), rt.sizeDelta.y);
            if (size.y < targetHeight)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y + (targetHeight - size.y));
        }
    }

    /// Restore ghost mode when the RUNE viewer closes. Rune mode has no input lock and
    /// its Hide() genuinely closes the viewer, so releasing ghost mode here is correct.
    ///
    /// During an INTRO we deliberately do nothing: vanilla's TextViewer.LateUpdate fires
    /// Hide() on any stray Use/Escape without actually closing the intro animation, so
    /// releasing here would unfreeze + expose the player while the intro is still on
    /// screen. The intro's own watchdog (IntroRoutine) owns release for that case.
    [HarmonyPatch(typeof(TextViewer), nameof(TextViewer.Hide))]
    internal static class TextViewerHidePatch
    {
        private static void Postfix()
        {
            if (GuidanceDisplay.IntroLockActive) return; // intro owns its own release
            GuidanceDisplay.ReleaseGhostMode();
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

    /// True invulnerability for the local player during the intro cinematic.
    /// Ghost mode alone is NOT enough: Character.RPC_Damage has no InGhostMode guard, so
    /// a ghost still takes every hit (ghost mode only clamps otherwise-lethal damage to
    /// 1 HP). Suppressing RPC_Damage entirely while the intro lock is active means the
    /// player cannot be harmed — matching vanilla's own cutscene damage-immunity path.
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class CharacterDamageIntroPatch
    {
        private static bool Prefix(Character __instance)
        {
            if (!GuidanceDisplay.IntroLockActive) return true;       // run vanilla
            return __instance != Player.m_localPlayer;               // skip damage to the local player only
        }
    }

    /// Clear raven queue/deferral state when the session ends (player disconnects or
    /// returns to main menu) so stale entries do not carry over to the next session.
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class ZNetDestroyRavenPatch
    {
        private static void Postfix()
        {
            GuidanceDisplay.ClearRavenState();
            // Dismiss any open rune reading so it doesn't linger into the next session.
            if (RunePanel.Instance != null) RunePanel.Instance.CloseImmediate();
            // Safety: if the session ends mid-intro (disconnect / logout), make sure the
            // static freeze flag can't carry into the next session and lock input there.
            GuidanceDisplay.ReleaseIntroLock();
            GuidanceDisplay.ReleaseGhostMode();
        }
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
