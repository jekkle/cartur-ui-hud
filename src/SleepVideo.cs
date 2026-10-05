using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace CarturUIHud
{
    /// <summary>
    /// What you look at while you sleep: a picture or a video from assets/sleep, the game's own
    /// screen, or nothing at all.
    ///
    /// Vanilla shows three things on one black screen and picks between them by switching a
    /// GameObject on - m_loadingProgress, m_sleepingProgress, m_teleportingProgress, see
    /// Hud.UpdateBlackScreen. So this rides on m_sleepingProgress: it shows exactly while that
    /// object is up and stops when it goes, and nothing here has to know what sleeping is.
    ///
    /// A night is one of four things, rolled before anything is picked: our own dream, the
    /// game's own dream, a video, or black. Rolling the kind first is what makes the odds the
    /// odds - one shared bag of files made a video as likely as anything else, because there
    /// happened to be six of one and twenty of the other.
    ///
    /// Both kinds of dream are read against the backdrop - background.* in assets/sleep - and
    /// the backdrop can be a video, which is the point: the words move over moving cloud
    /// rather than sitting on a still. Text is never baked into the picture behind it.
    ///
    /// The vanilla dream nights are the game's, not ours. DreamTexts.GetRandomDreamText filters
    /// its list by global keys, so boss dreams only come after that boss, then rolls the entry's
    /// own m_chanceToDream and can come back with nothing at all. A quiet night under the
    /// backdrop is that roll failing, and it is left alone.
    ///
    /// Our dreams are the lines in assets/sleep/dreams.txt, handed to the game through its own
    /// SleepText - see OurDream for why that beats drawing a label here.
    ///
    /// Dreams keep their own bag and videos theirs - everything shows once before anything
    /// repeats, and the refill leaves out the one just shown so two nights running cannot be
    /// the same.
    ///
    /// Nothing is shown until the world has finished going black, then it fades in over a
    /// second. The fade out is the game's own: the screen hangs inside LoadingBlack, whose
    /// CanvasGroup takes everything under it to nothing over a second as the player wakes, so
    /// the picture dissolves with the black rather than over the world. See Build for why it
    /// has to live in that branch, and Fade for how the going-black is waited on rather than
    /// timed.
    ///
    /// Videos go through Unity's VideoPlayer - the class the game plays its own cinematics with
    /// - reading a file path rather than a clip, so a file is dropped in and works with no
    /// bundle to build. Pictures are loaded one at a time and thrown away after; a folder of
    /// them is not held in memory for a screen that lasts seconds.
    ///
    /// Everything is stretched to the full screen rather than letterboxed - see Build for why
    /// that beats the two alternatives.
    /// </summary>
    internal static class SleepVideo
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        /// <summary>What the sleep screen shows.</summary>
        internal enum Show
        {
            /// <summary>The pictures and videos in assets/sleep.</summary>
            Media,
            /// <summary>The game's own sleeping screen, untouched.</summary>
            Vanilla,
            /// <summary>Black. Vanilla's own text is hidden too.</summary>
            Nothing,
        }

        private const string FolderName = "sleep";
        private const string ScreenName = "CarturUI_SleepScreen";
        private const string DreamsFile = "dreams.txt";

        private const float FadeIn = 1f;

        private static ConfigEntry<Show> s_show;
        private static ConfigEntry<float> s_volume;
        private static ConfigEntry<float> s_videoChance;
        private static ConfigEntry<float> s_textChance;
        private static ConfigEntry<float> s_blackChance;

        private static VideoPlayer s_player;
        private static RawImage s_screen;
        private static RenderTexture s_target;
        private static Texture2D s_picture;
        private static readonly List<string> s_shown = new List<string>();

        internal static void Init(ConfigFile config)
        {
            s_show = config.Bind("Sleep screen", "show", Show.Media,
                "Media: the pictures and videos in assets/sleep. Vanilla: the game's own screen. "
                + "Nothing: black.");
            s_volume = config.Bind("Sleep screen", "volume", 0.5f,
                "How loud a sleep video is, 0 to 1.");
            s_videoChance = config.Bind("Sleep screen", "videoChance", 0.08f,
                "The chance a night shows a video. 0 to 1.");
            s_textChance = config.Bind("Sleep screen", "vanillaDreamChance", 0.33f,
                "The chance a night shows the game's own dream text over the backdrop "
                + "instead of one from dreams.txt. 0 to 1.");
            s_blackChance = config.Bind("Sleep screen", "blackChance", 0.15f,
                "The chance a night shows nothing at all. 0 to 1.");
        }

        [HarmonyPatch(typeof(Hud), "UpdateBlackScreen")]
        [HarmonyPostfix]
        private static void Watch(Hud __instance)
        {
            if (s_show == null || __instance.m_sleepingProgress == null)
                return;

            bool sleeping = __instance.m_sleepingProgress.activeInHierarchy;

            if (s_show.Value != Show.Media)
            {
                if (s_up)
                    Off();
                if (sleeping)
                    Vanilla(__instance, s_show.Value == Show.Vanilla);
                return;
            }

            if (s_player == null)
            {
                if (!sleeping)
                    return;              // nothing is built until the first night
                Build(__instance);
                if (s_screen == null)
                    return;              // no files - said once in Build
            }

            // The night is settled on the frame the screen comes up, before anything is drawn,
            // because what it turns out to be decides whether vanilla's text is wanted.
            if (sleeping && !s_up)
                On();
            else if (!sleeping && s_up)
                Off();          // by now LoadingBlack's own fade has already taken us to nothing

            if (s_up)
                Fade(__instance);

            // Vanilla's own text and spinner. A card carries its own words, so they are hidden
            // for it, for a video and for a black night, and left alone on a dream-text night.
            // Only the children are touched: the object itself is what the game switches to
            // choose this screen, and hiding it would fight that.
            if (sleeping)
                Vanilla(__instance, s_vanillaText);
        }

        /// <summary>
        /// Shows or hides whatever vanilla drew on this screen, without touching the object the
        /// game uses to choose the screen in the first place.
        /// </summary>
        private static void Vanilla(Hud hud, bool on)
        {
            foreach (Transform child in hud.m_sleepingProgress.transform)
            {
                if (child.name == ScreenName)
                    continue;
                if (child.gameObject.activeSelf != on)
                    child.gameObject.SetActive(on);
            }
        }

        private static void On()
        {
            s_began = Time.unscaledTime;
            s_up = true;
            s_vanillaText = false;
            s_ourDream = null;   // last night's, if the player woke before the game asked for it

            string pick = Next();

            // One line a night, saying what was rolled and what is queued behind it. A sleep is
            // a rare event and this is the only way to tell a quiet dream-text night from a
            // broken one without a second launch.
            Log.LogInfo("night: " + (pick == null ? "black" : Path.GetFileName(pick))
                + (s_ourDream != null
                    ? " + our dream \"" + s_ourDream.Split('\n')[0] + "\""
                    : s_vanillaText ? " + the game's own dream" : " + no text"));

            if (pick == null)
                return;              // a black night

            s_screen.enabled = true;
            s_alpha = 0f;
            s_opaque = 0f;
            Tint(0f, 0f);        // nothing shows until the world has gone black

            if (IsVideo(pick))
            {
                Release();
                s_screen.texture = s_target;
                s_player.url = pick;
                s_player.SetDirectAudioVolume(0, 0f);   // the sound comes up with the picture
                s_player.Play();
                return;
            }

            s_player.Stop();
            Release();
            s_picture = AssetLoader.LoadFile(pick, Log);
            if (s_picture == null)
            {
                s_screen.enabled = false;
                return;
            }
            s_screen.texture = s_picture;
        }

        /// <summary>
        /// Nothing at all until the world has finished going black, then a second fading in. The
        /// fade out is not ours: see Build - the screen sits under LoadingBlack, and that group
        /// fades to nothing over a second when the player wakes, taking the picture with it.
        ///
        /// The wait is not a timer. Hud.UpdateBlackScreen moves m_loadingScreen's alpha towards
        /// 1 over Game.m_fadeTimeSleep, which is set in the scene rather than in code - so the
        /// only honest way to know the world is covered is to read that alpha. That also means
        /// the dream never shows through a half-faded world, whatever that time is set to.
        ///
        /// A video's sound is multiplied by that same group alpha, because a CanvasGroup fades
        /// pictures and not sound - without it the audio would stay at full volume through the
        /// wake and then cut.
        /// </summary>
        private static void Fade(Hud hud)
        {
            if (s_screen == null || !s_screen.enabled)
                return;              // a black night has nothing to fade

            float group = hud.m_loadingScreen != null ? hud.m_loadingScreen.alpha : 1f;

            if (s_opaque <= 0f)
            {
                if (group < 1f)
                {
                    Tint(0f, group);   // the world is still going black - show nothing yet
                    return;
                }
                s_opaque = Time.unscaledTime;
            }

            s_alpha = Mathf.Clamp01((Time.unscaledTime - s_opaque) / FadeIn);
            Tint(s_alpha, group);
        }

        private static void Tint(float alpha, float group)
        {
            if (s_screen != null)
                s_screen.color = new Color(1f, 1f, 1f, alpha);
            if (s_player != null && s_volume != null)
                s_player.SetDirectAudioVolume(0, Mathf.Clamp01(s_volume.Value) * alpha * group);
        }

        private static void Off()
        {
            s_up = false;
            s_vanillaText = false;
            s_alpha = 0f;
            s_opaque = 0f;
            if (s_player != null)
                s_player.Stop();
            if (s_screen != null)
                s_screen.enabled = false;
            Release();

            // How long the spot actually is, said once. The fade is Game.m_fadeTimeSleep and
            // the wait is EnvMan's morning skip, both set in the scene rather than in code - so
            // the only honest figure is one measured on the night.
            if (!s_timed && s_began > 0f)
            {
                s_timed = true;
                Log.LogInfo("sleep screen was up for "
                    + (Time.unscaledTime - s_began).ToString("0.0") + "s");
            }
        }

        /// <summary>A still is loaded for one night and let go after it. 1920x1080 is 8MB.</summary>
        private static void Release()
        {
            if (s_picture == null)
                return;
            if (s_screen != null && s_screen.texture == s_picture)
                s_screen.texture = null;
            Object.Destroy(s_picture);
            s_picture = null;
        }

        private static void Build(Hud hud)
        {
            string dir = Folder();
            if (Files().Count == 0)
            {
                if (!s_warned)
                {
                    s_warned = true;
                    Log.LogInfo("nothing in " + dir + " - the sleep screen stays as it was");
                }
                return;
            }

            // Inside LoadingBlack - m_loadingScreen - and immediately before Sleeping.
            //
            // The whole black screen is one canvas: HUD, sortingOrder 400 with overrideSorting,
            // measured on a night. So draw order is hierarchy order and nothing but the sibling
            // index decides who covers whom. The game's own words are
            // HUD/LoadingBlack/Sleeping/Text and .../Sleeping/DreamText, also measured. Hanging
            // this screen off HUD put it at index 4 against LoadingBlack's 2, so an opaque video
            // painted over the dream text every single night.
            //
            // Sitting between the black and Sleeping is the only place that works: over the
            // black plate LoadingBlack itself carries - which has to draw under Sleeping, or
            // vanilla's own text would never have shown either - and under every word Sleeping
            // holds.
            //
            // The cost is that LoadingBlack's CanvasGroup now owns the fade out, because it
            // takes this screen with it: Hud.UpdateBlackScreen fades that group to 0 over
            // GetFadeDuration - which returns 1, not m_fadeTimeSleep, because by then the player
            // is no longer sleeping - and then SetActive(false)s it. Our own fade out would be
            // invisible, so there isn't one. See Fade.
            Transform host = hud.m_loadingScreen != null ? hud.m_loadingScreen.transform : null;
            if (host == null)
                host = hud.m_sleepingProgress.transform;

            var go = new GameObject(ScreenName, typeof(RectTransform), typeof(RawImage),
                typeof(VideoPlayer));
            var rt = (RectTransform)go.transform;
            rt.SetParent(host, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Transform sleeping = hud.m_sleepingProgress.transform;
            if (sleeping.parent == host)
                rt.SetSiblingIndex(sleeping.GetSiblingIndex());   // Sleeping shifts down one
            else
                rt.SetAsLastSibling();

            s_target = new RenderTexture(1920, 1080, 0);
            s_screen = go.GetComponent<RawImage>();
            s_screen.texture = s_target;
            s_screen.raycastTarget = false;
            s_screen.enabled = false;

            // Stretched to the full rect, not letterboxed. The cards are 1168x784 - 1.49
            // against a 16:9 screen - so this widens them by about a fifth. That was a
            // deliberate call: the alternative that keeps the shape and still fills the screen
            // is to crop, and the cards carry their text in the middle with the ornament near
            // the bottom edge, so cropping takes the words. Widening is the cost of no bars.
            //
            // No AspectRatioFitter: the component is what letterboxed it, and with the rect
            // stretched to the parent there is nothing left for it to do.

            s_player = go.GetComponent<VideoPlayer>();
            s_player.source = VideoSource.Url;
            s_player.renderMode = VideoRenderMode.RenderTexture;
            s_player.targetTexture = s_target;
            s_player.isLooping = true;
            s_player.playOnAwake = false;
            s_player.waitForFirstFrame = false;
            s_player.audioOutputMode = VideoAudioOutputMode.Direct;

            int videos = 0;
            foreach (string f in Files())
                if (IsVideo(f))
                    videos++;
            var hostRect = host as RectTransform;
            Log.LogInfo("sleep screen hosted on " + host.name
                + (hostRect != null
                    ? " (" + Mathf.Round(hostRect.rect.width) + "x" + Mathf.Round(hostRect.rect.height) + ")"
                    : " (no rect)"));
            Log.LogInfo("sleep screen ready: " + (Files().Count - videos) + " cards, "
                + videos + " videos and "
                + (Backdrop() != null ? "backdrop " + Path.GetFileName(Backdrop()) : "no backdrop")
                + " in " + dir);
        }

        private static bool s_up;            // this night has been rolled and dealt with
        private static bool s_vanillaText;   // ... and it turned out to be the game's own dream
        private static float s_opaque;       // when the game's own black screen finished covering
        private static float s_alpha;        // what was drawn last frame
        private static bool s_warned;
        private static bool s_timed;
        private static float s_began;

        private static string Folder() => Path.Combine(AssetLoader.AssetsDir ?? "", FolderName);

        private static bool IsVideo(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mp4" || ext == ".webm" || ext == ".mov" || ext == ".m4v";
        }

        /// <summary>
        /// The one file with no words on it - background.* - which is what a vanilla dream text
        /// is read against. Held out of the card and video bags so it never comes up bare.
        /// </summary>
        private static bool IsBackdrop(string path) =>
            Path.GetFileNameWithoutExtension(path).ToLowerInvariant() == "background";

        /// <summary>
        /// The backdrop, video for preference. It used to be a still only, which is why a dream
        /// text was read against a picture that did not move. Nothing else had to change: On
        /// already branches on IsVideo for whatever Next hands it, so a background.mp4 plays
        /// under the text the same way a card video plays on its own.
        ///
        /// A still is still allowed, and is used when there is no video beside it.
        /// </summary>
        private static string Backdrop()
        {
            string dir = Folder();
            if (!Directory.Exists(dir))
                return null;
            string still = null;
            foreach (string path in Directory.GetFiles(dir))
            {
                if (!IsBackdrop(path))
                    continue;
                if (IsVideo(path))
                    return path;
                still = still ?? path;
            }
            return still;
        }

        private static List<string> Files()
        {
            var found = new List<string>();
            string dir = Folder();
            if (!Directory.Exists(dir))
                return found;
            foreach (string path in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (IsBackdrop(path))
                    continue;
                if (IsVideo(path) || ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    found.Add(path);
            }
            found.Sort();
            return found;
        }

        /// <summary>
        /// The dreams in assets/sleep/dreams.txt: one per block, blank line between blocks, #
        /// for a comment. Read off disk each night rather than held, because the file is small
        /// and a reload beats a restart when a line is being changed.
        /// </summary>
        private static List<string> Dreams()
        {
            var found = new List<string>();
            string path = Path.Combine(Folder(), DreamsFile);
            if (!File.Exists(path))
                return found;

            var block = new List<string>();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("#"))
                    continue;
                if (line.Trim().Length == 0)
                {
                    if (block.Count > 0)
                        found.Add(string.Join("\n", block.ToArray()));
                    block.Clear();
                    continue;
                }
                block.Add(line);
            }
            if (block.Count > 0)
                found.Add(string.Join("\n", block.ToArray()));
            return found;
        }

        /// <summary>
        /// The dream this night is showing, or null. Set on the frame the screen comes up and
        /// read four seconds later - SleepText.OnEnable does Invoke("ShowDreamText", 4f), so
        /// the roll is always long finished by the time the game asks. Read off the DLL.
        /// </summary>
        private static string s_ourDream;
        private static readonly List<string> s_said = new List<string>();

        /// <summary>
        /// One out of a bag: everything is shown once before anything repeats, and the refill
        /// leaves out the one just shown so two nights running cannot be the same.
        /// </summary>
        private static string OneOf(List<string> all, List<string> said)
        {
            if (all.Count == 0)
                return null;

            var left = new List<string>();
            foreach (string s in all)
                if (!said.Contains(s))
                    left.Add(s);

            if (left.Count == 0)
            {
                string last = said.Count > 0 ? said[said.Count - 1] : null;
                said.Clear();
                foreach (string s in all)
                    if (s != last || all.Count == 1)
                        left.Add(s);
            }

            string pick = left[Random.Range(0, left.Count)];
            said.Add(pick);
            return pick;
        }

        /// <summary>
        /// Our dreams, handed to the game as one of its own.
        ///
        /// SleepText.ShowDreamText asks DreamTexts for a dream, gives up if it gets nothing,
        /// and otherwise localises it into m_dreamField, enables the field, cross-fades it in
        /// and hides it again after 6.5 seconds. All of that is worth having, and none of it is
        /// worth writing twice - so the only thing replaced is the answer to the question. The
        /// game then draws our words exactly as it draws its own, over whatever the sleep
        /// screen is showing behind them.
        ///
        /// DreamText is public, and so is its constructor and m_text: read off the DLL, not
        /// reflected at. m_chanceToDream is 1 because the night has already been rolled here.
        /// </summary>
        [HarmonyPatch(typeof(DreamTexts), "GetRandomDreamText")]
        [HarmonyPrefix]
        private static bool OurDream(ref DreamTexts.DreamText __result)
        {
            if (s_ourDream == null)
                return true;

            __result = new DreamTexts.DreamText
            {
                m_text = s_ourDream,
                m_chanceToDream = 1f,
            };
            Log.LogInfo("the game asked for a dream and got ours");
            s_ourDream = null;
            return false;
        }

        /// <summary>
        /// Leaves the words up for the rest of the night.
        ///
        /// ShowDreamText ends with Invoke("HideDreamText", 6.5f) - read off the DLL - and
        /// HideDreamText cross-fades the field out over another 1.5s. Against a night measured
        /// at 14.5s on the screen that puts the words away with half of it still to run, which
        /// is fine over vanilla's plain black and wrong over a backdrop that keeps playing.
        ///
        /// Cancelling that one Invoke is the whole change. Nothing has to put the words away
        /// afterwards: the field lives in LoadingBlack, so the game's own group fade takes them
        /// with the black as the player wakes, and the next night vanilla re-runs
        /// DelayedCrossFadeStart, which starts from alpha 0 again.
        ///
        /// Only while our own screen is up - with show set to Vanilla or Nothing the game's
        /// timing is the game's business.
        /// </summary>
        [HarmonyPatch(typeof(SleepText), "ShowDreamText")]
        [HarmonyPostfix]
        private static void KeepDream(SleepText __instance)
        {
            if (s_show == null || s_show.Value != Show.Media || !s_up)
                return;
            __instance.CancelInvoke("HideDreamText");
        }

        /// <summary>
        /// What this night is: the file to show, or null for black. The kind is rolled first, on
        /// one number, so the odds are the odds however many files of each are in the folder.
        /// A kind with nothing behind it - no videos, no backdrop - falls through to a card.
        /// </summary>
        private static string Next()
        {
            var cards = new List<string>();
            var videos = new List<string>();
            foreach (string f in Files())
                (IsVideo(f) ? videos : cards).Add(f);

            float roll = Random.value;
            float black = Mathf.Clamp01(s_blackChance.Value);
            float video = black + Mathf.Clamp01(s_videoChance.Value);
            float text = video + Mathf.Clamp01(s_textChance.Value);
            string backdrop = Backdrop();

            if (roll < black)
                return null;

            List<string> pool;
            if (roll < video && videos.Count > 0)
            {
                pool = videos;
            }
            else if (roll < text && backdrop != null)
            {
                s_vanillaText = true;
                return backdrop;
            }
            else if (backdrop != null)
            {
                // Our own dreams, on the same moving backdrop the game's own dream gets. The
                // cards this bucket used to draw were text baked into a still picture, which is
                // the one thing a moving background cannot be put behind.
                string dream = OneOf(Dreams(), s_said);
                if (dream != null)
                {
                    s_ourDream = dream;
                    s_vanillaText = true;
                    return backdrop;
                }
                pool = cards;
            }
            else
            {
                pool = cards;
            }

            if (pool.Count == 0)
                return backdrop;   // no cards left to fall back on - the backdrop alone

            var left = new List<string>();
            foreach (string f in pool)
                if (!s_shown.Contains(f))
                    left.Add(f);

            if (left.Count == 0)
            {
                // This kind has been all the way round. Only its own entries are forgotten, so
                // a rare video does not reset which pictures have been seen.
                string last = s_shown.Count > 0 ? s_shown[s_shown.Count - 1] : null;
                s_shown.RemoveAll(pool.Contains);
                foreach (string f in pool)
                    if (f != last || pool.Count == 1)
                        left.Add(f);
            }

            string pick = left[Random.Range(0, left.Count)];
            s_shown.Add(pick);
            return pick;
        }
    }
}
