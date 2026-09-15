using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// Loads the kit PNGs from assets/ next to the plugin DLL.
    /// ImageConversion.LoadImage goes through reflection because a direct call does not compile
    /// against Valheim's netstandard facade - same as the other Cartur mods.
    ///
    /// Sprites are built later, from Hud.Awake, because the pixels-per-unit needed depends on
    /// the canvas. Unity renders a sprite at  pixels * canvas.referencePixelsPerUnit / sprite.pixelsPerUnit,
    /// so a HIGHER ppu draws smaller. Two of vanilla's own sprites pin that direction down:
    /// bar_gradient is 32px at ppu 50 against a reference of 50 and tiles exactly once on a
    /// 32 unit bar, and Background's 10px border at ppu 200 draws the thin 2.5 unit panel edge.
    /// Having it backwards drew the bars as a dark slab with a squashed lozenge for a knot -
    /// the borders came out enormous and Unity clamped them down to fit the rect.
    /// </summary>
    internal static class AssetLoader
    {
        // The height the sprites are built for, in canvas units. A bar drawn at any other
        // height uses Image.pixelsPerUnitMultiplier rather than a second sprite: border widths
        // and tile sizes both divide by it, so one pair of sprites covers every size.
        internal const float BaseBarHeight = 32f;

        // The frame's 9-slice, in source pixels of bar_frame.png (Bar.png trimmed, 1181x82).
        // Left has to reach past the chevron that stands off the knot, not stop at the knot:
        // cutting between them puts the stretch in that gap and opens a notch mid-bar.
        // Cut tight to the ornaments, not out past them. Bar.png's ends carry 148px of plain
        // dark bar inside them - 95px between the knot and the chevron, 38px after it, 15px
        // before the point - and anything inside a cap is frame art the fill can never reach.
        // Cutting at the knot instead hands that back to the fill, at the cost of the chevron
        // (207-232) now sitting in the stretched middle, so it widens with the bar.
        private const float BorderLeftPx = 118f;   // the knot, art ends at x=112
        private const float BorderRightPx = 95f;   // the point, art starts at x=1086
        private const float BorderRailPx = 16f;    // the rails along the top and bottom

        /// <summary>
        /// The stretched middle at its natural size. A bar sitting at its own FullStat is drawn
        /// this long, so every bar reaches the same length at its own maximum however different
        /// those maximums are.
        /// </summary>
        internal static float NaturalWindowUnits { get; private set; }

        /// <summary>Frame ornament widths at BaseBarHeight, in canvas units. Scale by the bar's height.</summary>
        internal static float FrameLeftUnits { get; private set; }
        internal static float FrameRightUnits { get; private set; }
        internal static float FrameRailUnits { get; private set; }

        /// <summary>
        /// Floor under a bar's length at BaseBarHeight, scaled by the bar's height. The
        /// ornaments sit outside the window so they can never crush together; this only keeps
        /// the window itself from closing to nothing at very low max stats.
        /// </summary>
        internal static float MinWindowUnits => 24f;

        private static Texture2D s_barFrame, s_barFill, s_foodFrame;

        public static Sprite BarFrame { get; private set; }
        public static Sprite BarFill { get; private set; }
        /// <summary>
        /// The diamond. Used for the three food boxes and for the guardian power box - by
        /// design, not as a stand-in: the power box is meant to read as one of the same family.
        /// </summary>
        public static Sprite FoodFrame { get; private set; }

        public static string AssetsDir { get; private set; }

        public static bool LoadTextures(BepInEx.Logging.ManualLogSource log)
        {
            AssetsDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "assets");

            s_barFrame = Load("bar_frame.png", log);
            s_barFill = Load("bar_fill.png", log);
            s_foodFrame = Load("food_frame.png", log);

            return s_barFrame != null && s_barFill != null && s_foodFrame != null;
        }

        public static void BuildSprites(float referencePixelsPerUnit, BepInEx.Logging.ManualLogSource log)
        {
            // ppu that renders the art at BaseBarHeight. A HIGHER ppu draws smaller, so the
            // texture height goes on top - see the class comment for the two vanilla sprites
            // that pin the direction down.
            float framePpu = s_barFrame.height * referencePixelsPerUnit / BaseBarHeight;
            float fillPpu = s_barFill.height * referencePixelsPerUnit / BaseBarHeight;
            float unitsPerPixel = referencePixelsPerUnit / framePpu;
            FrameLeftUnits = BorderLeftPx * unitsPerPixel;
            FrameRightUnits = BorderRightPx * unitsPerPixel;
            FrameRailUnits = BorderRailPx * unitsPerPixel;
            NaturalWindowUnits = (s_barFrame.width - BorderLeftPx - BorderRightPx) * unitsPerPixel;


            // Border keeps the 118px left knot and the 83px right point unstretched; only the
            // 79px middle grows.
            BarFrame = Make(s_barFrame, new Vector4(BorderLeftPx, BorderRailPx, BorderRightPx, BorderRailPx), framePpu);

            // Tiled. One tile is the whole strip, wider than any bar reaches, so draining
            // uncovers less knotwork rather than squashing it.
            BarFill = Make(s_barFill, Vector4.zero, fillPpu);

            // Drawn Simple with preserveAspect, so ppu only has to not be zero.
            FoodFrame = Make(s_foodFrame, Vector4.zero, referencePixelsPerUnit);

            log.LogInfo($"canvas referencePixelsPerUnit {referencePixelsPerUnit}; frame ppu {framePpu:0.##}, fill ppu {fillPpu:0.##}, ornaments {FrameLeftUnits:0.#} + {FrameRightUnits:0.#}, natural window {NaturalWindowUnits:0.#}, at {BaseBarHeight} tall");
        }

        private static Sprite Make(Texture2D tex, Vector4 border, float pixelsPerUnit)
        {
            if (tex == null)
                return null;
            return Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                border);
        }

        private static Texture2D Load(string fileName, BepInEx.Logging.ManualLogSource log)
        {
            string path = Path.Combine(AssetsDir, fileName);
            if (!File.Exists(path))
            {
                log.LogWarning("asset missing: " + path);
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            if (LoadImageViaReflection(tex, File.ReadAllBytes(path)))
                return tex;

            log.LogWarning("ImageConversion.LoadImage failed for " + path);
            return null;
        }

        private static bool LoadImageViaReflection(Texture2D tex, byte[] data)
        {
            Type imageConversion = AccessTools.TypeByName("UnityEngine.ImageConversion");
            if (imageConversion == null)
                return false;

            MethodInfo loadImage = null;
            foreach (MethodInfo m in imageConversion.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "LoadImage")
                    continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 3 && ps[1].ParameterType == typeof(byte[]))
                {
                    loadImage = m;
                    break;
                }
            }

            if (loadImage == null)
                return false;

            return loadImage.Invoke(null, new object[] { tex, data, false }) is bool ok && ok;
        }
    }
}
