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
    /// A night is one of four things, rolled before anything is picked: a card, the game's own
    /// dream text read against the plain backdrop, a video, or black. Rolling the kind first is
    /// what makes the odds the odds - one shared bag of files made a video as likely as a card,
    /// because there happened to be six of one and twenty of the other.
    ///
    /// The vanilla dream nights are the game's, not ours. DreamTexts.GetRandomDreamText filters
    /// its list by global keys, so boss dreams only come after that boss, then rolls the entry's
    /// own m_chanceToDream and can come back with nothing at all. A quiet night under the
    /// backdrop is that roll failing, and it is left alone.
    ///
    /// Cards keep their own bag and videos theirs - everything shows once before anything
    /// repeats, and the refill leaves out the one just shown so two nights running cannot be
    /// the same.
    ///
    /// Nothing is shown until the world has finished going black, then it fades in over a
    /// second, and fades out over a second when the sleep ends - our own alpha, because neither
    /// of the game's objects here lives long enough to carry a fade out. See Build for which one
    /// it hangs off and why, and Fade for how the going-black is waited on rather than timed.
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

        private const float FadeIn = 1f;
        private const float FadeOut = 1f;

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
                "The chance a night shows the game's own dream text over background.jpg "
                + "instead of a card. 0 to 1.");
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
            // because what it turns out to be decides whether vanilla's text is wanted. Waking
            // does not tear it down: it starts the fade out, and Fade calls Off at the end of it.
            if (sleeping && !s_up)
                On();
            else if (!sleeping && s_up && s_left <= 0f)
            {
                s_left = Time.unscaledTime;
                s_alphaAtLeave = s_alpha;
            }

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

            string pick = Next();
            if (pick == null)
                return;              // a black night

            s_screen.enabled = true;
            s_alpha = 0f;
            s_opaque = 0f;
            Tint(0f);            // nothing shows until the world has gone black

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
        /// Nothing at all until the world has finished going black, then a second fading in, and
        /// a second fading out again once the sleep ends.
        ///
        /// The wait is not a timer. Hud.UpdateBlackScreen moves m_loadingScreen's alpha towards
        /// 1 over Game.m_fadeTimeSleep, which is set in the scene rather than in code - so the
        /// only honest way to know the world is covered is to read that alpha. That also means
        /// the dream never shows through a half-faded world, whatever that time is set to.
        ///
        /// A video's sound is tied to the same number, so it comes up and goes down with the
        /// picture rather than starting at full volume over a black screen.
        /// </summary>
        private static void Fade(Hud hud)
        {
            if (s_screen == null || !s_screen.enabled)
            {
                if (s_left > 0f)
                    Off();           // a black night still has to end
                return;
            }

            float now = Time.unscaledTime;
            if (s_left <= 0f)
            {
                if (s_opaque <= 0f)
                {
                    if (hud.m_loadingScreen == null || hud.m_loadingScreen.alpha < 1f)
                    {
                        Tint(0f);      // the world is still going black - show nothing yet
                        return;
                    }
                    s_opaque = now;
                }

                s_alpha = Mathf.Clamp01((now - s_opaque) / FadeIn);
            }
            else
            {
                s_alpha = s_alphaAtLeave * (1f - Mathf.Clamp01((now - s_left) / FadeOut));
                if (s_alpha <= 0f)
                {
                    Off();
                    return;
                }
            }

            Tint(s_alpha);
        }

        private static void Tint(float alpha)
        {
            if (s_screen != null)
                s_screen.color = new Color(1f, 1f, 1f, alpha);
            if (s_player != null && s_volume != null)
                s_player.SetDirectAudioVolume(0, Mathf.Clamp01(s_volume.Value) * alpha);
        }

        private static void Off()
        {
            s_up = false;
            s_vanillaText = false;
            s_left = 0f;
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

            // Not under m_sleepingProgress, and not under m_loadingScreen either. Read out of
            // Hud.UpdateBlackScreen: the moment the player stops sleeping, m_sleepingProgress is
            // switched off outright, and m_loadingScreen fades to 0 over GetFadeDuration - which
            // returns 1, not m_fadeTimeSleep, because by then the player is no longer sleeping -
            // and is then SetActive(false). Its CanvasGroup would also multiply our alpha. Either
            // parent cuts a 2s fade out short, so the screen hangs off the object above them,
            // which stays up, and this drives its own alpha end to end.
            Transform host = hud.m_loadingScreen != null ? hud.m_loadingScreen.transform.parent : null;
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
            // In front of the black screen it now sits beside, and behind nothing: vanilla's own
            // sleep text is a child of m_sleepingProgress, which is a different branch entirely
            // and draws later than this one anyway.
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
                + (Backdrop() != null ? "a backdrop" : "no backdrop") + " in " + dir);
        }

        private static bool s_up;            // this night has been rolled and dealt with
        private static bool s_vanillaText;   // ... and it turned out to be the game's own dream
        private static float s_opaque;       // when the game's own black screen finished covering
        private static float s_left;         // when the sleep ended, 0 while it is still going
        private static float s_alpha;        // what was drawn last frame
        private static float s_alphaAtLeave; // what it was when the fade out started
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
        /// The one picture with no words on it - background.jpg - which is what a vanilla dream
        /// text is read against. Held out of the card bag so it never comes up bare.
        /// </summary>
        private static bool IsBackdrop(string path) =>
            Path.GetFileNameWithoutExtension(path).ToLowerInvariant() == "background";

        private static string Backdrop()
        {
            string dir = Folder();
            if (!Directory.Exists(dir))
                return null;
            foreach (string path in Directory.GetFiles(dir))
                if (IsBackdrop(path) && !IsVideo(path))
                    return path;
            return null;
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
            else
            {
                pool = cards;
            }

            if (pool.Count == 0)
                return null;

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
