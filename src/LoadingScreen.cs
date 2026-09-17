using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Part 3 of Cartur's UI: the loading screen. The spinning hammer becomes a bar in the same
    /// bronze frame as the health bar.
    ///
    /// Nothing is rebuilt. LoadingIndicator already owns a progress Image, a smoothed value and a
    /// switch for showing it; the switch just ships off and the Image is drawn as a radial rudder.
    /// So the same Image is re-pointed at our bar art and told to fill left to right.
    ///
    /// Every member of LoadingIndicator is private in this build - checked, not assumed - so each
    /// one is a cached FieldInfo. Two things have to be respected or the change lasts one frame:
    ///   - UpdateGUIVisibility rewrites each graphic's colour every LateUpdate from a colour
    ///     cached during Awake, so the tint goes into that cached field, not onto the Image.
    ///   - our frame is not one of the graphics it manages, so the frame's alpha is mirrored from
    ///     the fill each frame, or an empty frame hangs on screen after the bar has faded.
    ///
    /// Two of these exist in a session: the splash before the main menu, which SceneLoader feeds
    /// with real scene-load progress, and the one under the in-game black screen.
    /// </summary>
    internal static class LoadingScreen
    {
        private const float BarWidth = 320f;
        private const float BarHeight = 22f;
        private const float BarMargin = 40f;
        private const string FrameName = "CarturUIHud_LoadBarFrame";

        /// <summary>
        /// The logo's own fire. Measured off Logo2_menu_highres (1000x396, bundle 8d5dbad8): the
        /// lava - pixels above 0.55 saturation and 0.6 value, so the glow rather than the stone -
        /// averages #CC501F at hue 15 degrees.
        ///
        /// bar_fill.png is greyscale (mean 78, peak 193 of 255), built to be tinted the way the
        /// game tints the health bar, and a tint can only multiply. #CC501F multiplied into a
        /// mid-grey comes out almost black, so the measured colour is scaled up to full value in
        /// the same hue and saturation: #FF6427. Same fire, as bright as a multiply allows.
        /// </summary>
        private static readonly Color LogoFire = new Color(1f, 0.392f, 0.153f);

        private static readonly FieldInfo s_spinner = AccessTools.Field(typeof(LoadingIndicator), "m_spinner");
        private static readonly FieldInfo s_background = AccessTools.Field(typeof(LoadingIndicator), "m_background");
        private static readonly FieldInfo s_show = AccessTools.Field(typeof(LoadingIndicator), "m_show");
        private static readonly FieldInfo s_showProgress = AccessTools.Field(typeof(LoadingIndicator), "m_showProgressIndicator");
        private static readonly FieldInfo s_progress = AccessTools.Field(typeof(LoadingIndicator), "m_progressIndicator");
        private static readonly FieldInfo s_text = AccessTools.Field(typeof(LoadingIndicator), "m_text");
        // The colour UpdateGUIVisibility re-applies to the fill every frame.
        private static readonly FieldInfo s_progressOriginal =
            AccessTools.Field(typeof(LoadingIndicator), "m_progressIndicatorOriginalColor");

        private static readonly Dictionary<LoadingIndicator, Image> s_frames = new Dictionary<LoadingIndicator, Image>();

        internal static BepInEx.Logging.ManualLogSource Log;

        [HarmonyPatch(typeof(LoadingIndicator), "Awake")]
        [HarmonyPostfix]
        private static void Dress(LoadingIndicator __instance)
        {
            Image fill = s_progress?.GetValue(__instance) as Image;
            if (fill == null)
                return;

            Canvas canvas = __instance.GetComponentInParent<Canvas>();
            float ppu = canvas != null ? canvas.referencePixelsPerUnit : 100f;

            // The splash indicator wakes long before Hud.Awake, so the bar sprites may not exist.
            if (AssetLoader.BarFill == null || AssetLoader.BarFrame == null)
                AssetLoader.BuildSprites(ppu, Log);
            if (AssetLoader.BarFill == null)
                return;

            // The frame is not a plain rectangle: it is a 118px knot on the left, a 95px point on
            // the right and a 16px rail top and bottom, all of which sit OUTSIDE the window the
            // fill is supposed to show through. Giving the fill the same rect as the frame is what
            // made it overhang the ends. So the frame owns the full BarWidth x BarHeight and the
            // fill is inset to the window, exactly the way BarSkin sizes the HUD bars.
            float k = BarHeight / AssetLoader.BaseBarHeight;
            float left = AssetLoader.FrameLeftUnits * k;
            float right = AssetLoader.FrameRightUnits * k;
            float rail = AssetLoader.FrameRailUnits * k;

            var rt = (RectTransform)fill.transform;
            rt.SetParent(__instance.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(BarWidth - left - right, BarHeight - rail * 2f);
            rt.anchoredPosition = new Vector2(-BarMargin - right, BarMargin + rail);

            fill.sprite = AssetLoader.BarFill;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.preserveAspect = false;

            // Into the cached colour, not the Image - the Image is overwritten every frame. The
            // alpha it cached is kept, because that is what the fade is measured against.
            if (s_progressOriginal != null && s_progressOriginal.GetValue(__instance) is Color cached)
                s_progressOriginal.SetValue(__instance, new Color(LogoFire.r, LogoFire.g, LogoFire.b, cached.a));

            Frame(__instance, rt);

            // Both of these are only ever recoloured by UpdateGUIVisibility, never enabled by it,
            // so switching the components off is enough and nothing fights it. The background is
            // the rudder ring behind the old spinner and it fades on the SAME fader as the bar, so
            // leaving it on puts a circle beside the bar every time the bar appears.
            Image spinner = s_spinner?.GetValue(__instance) as Image;
            if (spinner != null)
                spinner.enabled = false;
            Image background = s_background?.GetValue(__instance) as Image;
            if (background != null)
                background.enabled = false;

            // Ships off. On, the value SceneLoader already feeds in becomes visible.
            __instance.SetShowProgress(true);

            TMP_Text label = s_text?.GetValue(__instance) as TMP_Text;
            if (label != null)
            {
                var trt = (RectTransform)label.transform;
                trt.anchorMin = trt.anchorMax = new Vector2(1f, 0f);
                trt.pivot = new Vector2(1f, 0f);
                trt.sizeDelta = new Vector2(BarWidth, 28f);
                trt.anchoredPosition = new Vector2(-BarMargin, BarMargin + BarHeight + 6f);
                label.alignment = TextAlignmentOptions.Right;
            }

            // m_show is the master switch for the whole indicator and NOTHING in the game ever
            // calls SetShow - it is frozen at the prefab's m_showInitially. When it is false,
            // LateUpdate fades every part out and SceneLoader refuses to feed progress at all, so
            // it decides on its own whether this screen can have a bar. Logged, not assumed.
            Log.LogInfo("loading bar dressed on " + Path(__instance.transform)
                + (SceneLoaderIndicator() == __instance ? "  [SceneLoader's]" : string.Empty)
                + "  m_show=" + (s_show?.GetValue(__instance))
                + "  showProgress=" + (s_showProgress?.GetValue(__instance))
                + "  fillAlpha=" + (s_progressOriginal?.GetValue(__instance) is Color c0 ? c0.a.ToString("0.##") : "?")
                + "  text=\"" + (label != null ? label.text : "<none>") + "\"");
        }

        // Which LoadingIndicator is which is worth saying out loud in the log: one belongs to
        // SceneLoader and covers the startup scene loads, the other hangs under the Hud.
        private static readonly FieldInfo s_sceneLoaderIndicator =
            AccessTools.Field(typeof(SceneLoader), "loadingIndicator");

        private static LoadingIndicator SceneLoaderIndicator()
        {
            SceneLoader loader = Object.FindObjectOfType<SceneLoader>();
            if (loader == null || s_sceneLoaderIndicator == null)
                return null;
            return s_sceneLoaderIndicator.GetValue(loader) as LoadingIndicator;
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }

        private static void Frame(LoadingIndicator indicator, RectTransform fill)
        {
            Transform existing = fill.parent.Find(FrameName);
            Image frame = existing != null ? existing.GetComponent<Image>() : null;

            if (frame == null)
            {
                var go = new GameObject(FrameName, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(fill.parent, false);
                frame = go.GetComponent<Image>();
                frame.raycastTarget = false;
            }

            var frt = (RectTransform)frame.transform;
            frt.anchorMin = fill.anchorMin;
            frt.anchorMax = fill.anchorMax;
            frt.pivot = fill.pivot;
            frt.sizeDelta = new Vector2(BarWidth, BarHeight);
            frt.anchoredPosition = new Vector2(-BarMargin, BarMargin);
            frt.SetAsLastSibling();          // the frame draws over the fill

            frame.sprite = AssetLoader.BarFrame;
            frame.type = Image.Type.Sliced;
            frame.fillCenter = false;        // window stays open so the fill shows through
            // Ornament size and the rect come off the same k, so the drawn 9-slice border lands
            // exactly where the fill was inset to - the two cannot drift apart.
            frame.pixelsPerUnitMultiplier = 1f / (BarHeight / AssetLoader.BaseBarHeight);
            s_frames[indicator] = frame;
        }

        /// <summary>
        /// The frame is ours, so the game's fader knows nothing about it. Its alpha follows the
        /// fill's, which the fader does drive - otherwise an empty frame sits on screen for the
        /// rest of the session after the first load.
        /// </summary>
        [HarmonyPatch(typeof(LoadingIndicator), "LateUpdate")]
        [HarmonyPostfix]
        private static void FadeFrame(LoadingIndicator __instance)
        {
            if (!s_frames.TryGetValue(__instance, out Image frame) || frame == null)
                return;

            Image fill = s_progress?.GetValue(__instance) as Image;
            if (fill == null)
                return;

            Color c = frame.color;
            if (!Mathf.Approximately(c.a, fill.color.a))
                frame.color = new Color(c.r, c.g, c.b, fill.color.a);
        }

        private static bool s_loaderReported;

        /// <summary>
        /// Switch the startup bar on. This indicator ships with m_show = false and nothing in the
        /// game ever calls SetShow, so vanilla never shows it at all: LateUpdate fades every part
        /// of it to zero, and SceneLoader.Update then refuses to feed it progress because it bails
        /// out on IsVisible. That is why the startup screen has no bar of its own, and why the text
        /// on it belongs to something else - this one's label is empty.
        ///
        /// Shown for as long as this Update runs, which is exactly as long as the startup screen is
        /// the screen. The first attempt gated on _sceneLoadOperation instead and showed nothing:
        /// no method on SceneLoader ever assigns that field - it is written from the compiler's
        /// state machine for the load coroutine - so it reads null for most of the screen.
        ///
        /// IsVisible is not m_show: it is the two fade values, so it stays false for the frames
        /// the fade takes. Comparing against it here just means SetShow is called until the fade
        /// has actually started.
        /// </summary>
        [HarmonyPatch(typeof(SceneLoader), "Update")]
        [HarmonyPostfix]
        private static void ShowLoaderBar(SceneLoader __instance)
        {
            LoadingIndicator indicator = s_sceneLoaderIndicator?.GetValue(__instance) as LoadingIndicator;
            if (indicator == null)
                return;

            if (!indicator.IsVisible)
                indicator.SetShow(true);

            // One shot, the first frame the bar has actually drawn something. Never reset.
            Image fill = s_progress?.GetValue(indicator) as Image;
            if (s_loaderReported || fill == null || fill.color.a <= 0f)
                return;

            s_loaderReported = true;
            Log.LogInfo("startup bar visible: fill=" + fill.fillAmount.ToString("0.###")
                + "  alpha=" + fill.color.a.ToString("0.##")
                + "  active=" + indicator.gameObject.activeInHierarchy);
        }

        /// <summary>
        /// The world-join bar. Vanilla only fills this while a server generates locations, and it
        /// leaves the client side empty, so this supplies the number for the screen the player
        /// actually sits through.
        ///
        /// It is a real one. Hud.UpdateBlackScreen only reaches UpdateProgressIndicator while
        /// Game.WaitingForRespawn, and that wait is spelled out in Game.FindSpawnPoint:
        ///
        ///     m_respawnWait += dt;
        ///     if (m_respawnWait &lt;= m_respawnLoadDuration) return false;
        ///     if (!ZNetScene.instance.IsAreaReady(point)) return false;
        ///
        /// So the fraction of the wait that has elapsed is m_respawnWait / m_respawnLoadDuration,
        /// and after that the only thing left is the area streaming in, which publishes no number.
        /// The bar therefore runs on the timer up to 0.95 and finishes when the area reports ready.
        ///
        /// The first version of this parked at 0.5 for the whole screen, because it staged on
        /// ZNetScene.InLoadingScreen, which turns out to be nothing but "the player is null or
        /// teleporting" - the same condition as the stage before it, so it never advanced.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "UpdateProgressIndicator")]
        [HarmonyPostfix]
        private static void Stages(Hud __instance)
        {
            if (__instance.m_loadingIndicator == null || __instance.m_loadingScreen == null)
                return;
            if (__instance.m_loadingScreen.alpha <= 0f)
                return;

            // Vanilla owns the bar while a server generates locations - it has a real figure there.
            if (ZNet.instance != null && ZNet.instance.IsServer()
                && ZoneSystem.instance != null && !ZoneSystem.instance.LocationsGenerated)
                return;

            __instance.m_loadingIndicator.SetShowProgress(true);
            __instance.m_loadingIndicator.SetProgress(Stage());
        }

        // m_respawnWait is private; m_respawnLoadDuration is public but read through the same
        // instance for symmetry with it.
        private static readonly FieldInfo s_respawnWait = AccessTools.Field(typeof(Game), "m_respawnWait");

        private static float Stage()
        {
            Game game = Game.instance;
            if (game == null)
                return 0.05f;
            if (ZNet.instance == null || ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connecting)
                return 0.05f;
            if (!game.WaitingForRespawn())
                return 1f;

            float duration = game.m_respawnLoadDuration;
            if (s_respawnWait?.GetValue(game) is not float wait || duration <= 0f)
                return 0.5f;

            // 0.05 to 0.95 across the timer. The last twentieth is held back for IsAreaReady,
            // which is the one part of the wait with nothing to measure - so the bar sits just
            // short of full while the world streams in, rather than lying that it is done.
            float elapsed = Mathf.Clamp01(wait / duration);
            return 0.05f + 0.90f * elapsed;
        }
    }
}
