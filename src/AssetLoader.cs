using System;
using System.Collections.Generic;
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

        private static Texture2D s_barFrame, s_barFill, s_enemyFill, s_foodFrame;

        // The menu pieces, built by tools/art/assemble.py out of the two sprites above. Each is
        // authored at 2 px per canvas unit with the 9-slice border listed here in source px;
        // Skin creates them at 2 x the canvas ppu so they draw at their authored unit size.
        // A sprite is cached per canvas ppu, because the loading screen is a separate root
        // canvas from IngameGui and the two need not agree.
        // name, 9-slice border in source px, and the px per canvas unit the art was drawn at.
        // The third column used to be one constant for the whole kit; the two panel pieces
        // below come from a different pack drawn at a different size, and the border is what
        // has to land at a sensible number of units, so each piece carries its own.
        private static readonly (string name, float border, float perUnit)[] s_pieces =
        {
            ("panel", 24, PieceAuthoredPxPerUnit), ("panel_selected", 24, PieceAuthoredPxPerUnit),
            // The panes, cells and boxes. These carried the old kit's own rail until Cartur
            // pointed at "the grey on the edge": that rail was authored with a grey-lit top,
            // and beside the pack's rule it read as a grey outline round everything inside a
            // window. Measured across his screenshot, the window's rule runs 92,74,49 - 43
            // apart - while those rails ran 99,89,77, only 22 apart. They carry the same rule
            // as the panels now.
            //
            // The border is 28px like every other piece cut from that rule, and the px per unit
            // is set so the rail lands at the same thickness in units it already had - 8 for a
            // pane or a cell, 10 for a tooltip - so nothing moves on screen, only the colour.
            ("well", 20, 2.5f),

            // The cells wear the equipment panel's own slot border, keyed off that art: a 3px
            // gold line with a cut corner and nothing inside it, so a cell shows the panel
            // behind it the way an equipment slot does. 144px drawn at 64 units is 2.25 px per
            // unit, and the 26px border carries the corner without reaching the straight run.
            ("slot", 26, 2.25f),
            ("slot_selected", 16, PieceAuthoredPxPerUnit),
            ("button", 20, PieceAuthoredPxPerUnit), ("button_hover", 20, PieceAuthoredPxPerUnit),
            ("button_pressed", 20, PieceAuthoredPxPerUnit), ("button_disabled", 20, PieceAuthoredPxPerUnit),
            // Same 20px border as the button it is cut from - see tools/art/menu_button.py.
            ("button_light", 20, PieceAuthoredPxPerUnit),
            ("tab", 20, PieceAuthoredPxPerUnit), ("tab_hover", 20, PieceAuthoredPxPerUnit),
            ("tab_selected", 20, PieceAuthoredPxPerUnit),
            // Text fields take the rule thinner than a panel does. Measured on the world select:
            // the password box is 38px tall and the rule at 8 units ate 10px top and bottom,
            // leaving a 20px interior for 21px of text - so the text sat on the rule. At 4 px
            // per unit the rule draws 5 units and the interior is 19, which the text clears.
            ("field", 20, 4f), ("field_hover", 20, 4f), ("field_disabled", 20, 4f),
            ("tooltip", 20, 2.0f),

            // Cartur's own frame, composited over the fill by tools/art/panels.py. The art has
            // a transparent centre, so the fill goes down first and the rule sits directly on
            // it - there is no second fill inside the frame to disagree with ours, which is the
            // mismatch he was looking at.
            //
            // Measured off refs_frame.png: the rule is 19-20px deep and flush to the edge, and
            // the corner knot reaches 57px in, so 57 is the border. 1.425 px per unit draws
            // that corner at 40 units - what the old knot drew - and the rule at 13.3, against
            // the 12.7 the piece it replaces had. Nothing changes size on screen.
            ("panel_ornate", 57, 1.425f),

            // The same panel cut down for a wide, short piece: the two 88px caps with a 36px
            // band of the fill between them instead of 967px. A 9-slice stretches its centre to
            // whatever it is given, and the hotbar's centre is about 32 units tall - so the
            // full panel's fill was being squashed thirty times over into horizontal streaks.
            // Cut from the same file, at the same border, so it is the same frame.
            ("panel_ornate_bar", 57, 1.425f),

            // The same frame with nothing behind it, for drawing over something that has to
            // stay visible - the map and the minimap. Transparent centre AND transparent
            // between the rule and the centre, so only the gold draws.
            ("panel_frame", 57, 1.425f),

            // The same rule with the knots taken off, mitred at the corners - built from that
            // file's own edges, not drawn by hand. 3.5 px per unit puts its band at 8 units,
            // which is an eighth of the 64-unit boxes it goes on. 312x312, so 256px of the
            // pack's fill sits inside the rule: the first cut of this was 64x64, which left an
            // 8x8 centre for a 9-slice to blow up across the whole box.
            ("panel_thin", 20, 2.5f),

            // Buttons and tabs, same rule and no knots. Their centre is cut wide and short -
            // 384x48 - because that is the shape a button is: a square centre stretched into a
            // button squashes its grain into streaks, which is the same fault that made the
            // crafting window look low resolution. 4.5 px per unit puts the band at 6.2 units,
            // about a sixth of a 40-unit button.
            //
            // The four button tones and the three tab tones are the ratios measured off the
            // kit's own button set (hover 1.141/1.110/1.058, pressed 0.833, and so on), so the
            // new family reads the same way under the hand as the old one.
            ("button_thin", 20, 3.2f), ("button_thin_hover", 20, 3.2f),
            ("button_thin_pressed", 20, 3.2f), ("button_thin_disabled", 20, 3.2f),
            ("tab_thin", 20, 3.2f), ("tab_thin_hover", 20, 3.2f), ("tab_thin_selected", 20, 3.2f),
        };
        internal const float PieceAuthoredPxPerUnit = 2f;
        private static readonly Dictionary<string, Texture2D> s_pieceTex = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, float> s_pieceBorder = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> s_pieceUnit = new Dictionary<string, float>();
        private static readonly Dictionary<string, Sprite> s_pieceSprites = new Dictionary<string, Sprite>();

        // The knot that sits in the middle of the wood panel's top and bottom rails. It cannot
        // live in the panel sprite: a 9-slice stretches one slice of an edge along the whole run,
        // so a knot in there smears across the side. It is drawn as its own sprite instead,
        // anchored to the centre of the edge, which keeps it the same size at any panel size.
        //
        // There are two of them, because the art's two rails are not the same. Measured on the
        // source export (1438x874, the file the panel was cut from): the top rail's bright line
        // sits 6px in from the edge and its centre ornament is 374px wide; the bottom rail's line
        // sits 22px in and its ornament is 158px wide. The bottom knot used to be the top one with
        // localScale.y = -1, which put a wider ornament on the wrong rail with its line 19px out of
        // register - on screen that read as a bright band in a different colour lying across the
        // rail, which is what Cartur reported. Each rail now carries its own cut.
        private static Texture2D s_knotTex, s_knotBottomTex, s_shieldExcluded;

        // The equipment panel's artwork: one picture, drawn whole, with the seven slot boxes
        // and their names painted into it. Not a 9-slice - the layout IS the art, so it is
        // drawn Simple at the size the measured boxes give (see EquipmentPanel).
        private static Texture2D s_equipmentPanel;
        private static readonly Dictionary<float, Sprite> s_knotSprites = new Dictionary<float, Sprite>();
        private static readonly Dictionary<float, Sprite> s_knotBottomSprites = new Dictionary<float, Sprite>();

        public static Sprite Knot(float referencePixelsPerUnit, bool bottom = false)
        {
            Texture2D tex = bottom ? s_knotBottomTex : s_knotTex;
            if (tex == null)
                return null;
            Dictionary<float, Sprite> cache = bottom ? s_knotBottomSprites : s_knotSprites;
            if (cache.TryGetValue(referencePixelsPerUnit, out Sprite cached))
                return cached;
            Sprite sprite = Make(tex, Vector4.zero, referencePixelsPerUnit * PieceAuthoredPxPerUnit);
            sprite.name = bottom ? "cartur_panel_knot_bottom" : "cartur_panel_knot";
            cache[referencePixelsPerUnit] = sprite;
            return sprite;
        }

        /// <summary>
        /// How deep a piece's 9-slice border draws, in canvas units, at an Image with the
        /// default pixelsPerUnitMultiplier of 1. Asked for rather than worked out again at the
        /// call site, so a piece that gets re-authored moves everything that depends on it.
        /// </summary>
        public static float PieceBorderUnits(string name)
        {
            if (!s_pieceBorder.TryGetValue(name, out float border))
                return 0f;
            float perUnit = s_pieceUnit.TryGetValue(name, out float u) ? u : PieceAuthoredPxPerUnit;
            return perUnit > 0f ? border / perUnit : 0f;
        }

        /// <summary>A menu piece as a sliced sprite for the given canvas ppu, or null if the PNG is missing.</summary>
        public static Sprite Piece(string name, float referencePixelsPerUnit)
        {
            string key = name + "@" + referencePixelsPerUnit;
            if (s_pieceSprites.TryGetValue(key, out Sprite cached))
                return cached;
            if (!s_pieceTex.TryGetValue(name, out Texture2D tex) || tex == null)
                return null;

            float b = s_pieceBorder[name];
            float perUnit = s_pieceUnit.TryGetValue(name, out float u) ? u : PieceAuthoredPxPerUnit;
            Sprite sprite = Make(tex, new Vector4(b, b, b, b), referencePixelsPerUnit * perUnit);
            sprite.name = "cartur_" + name;
            s_pieceSprites[key] = sprite;
            return sprite;
        }

        public static Sprite BarFrame { get; private set; }
        public static Sprite BarFill { get; private set; }

        /// <summary>
        /// The enemy bars' fill: the same knotwork lifted into 150..255 by tools/art/enemy_fill.py.
        /// A tint can only multiply, and the game paints these bars in bright colours of its own
        /// (255,85,85 hostile, 67,255,32 tamed, 255,0,100 boss), so the low-toned HUD fill dragged
        /// them to near black. Separate file because bar_fill is shared with the HUD's own bars.
        /// </summary>
        public static Sprite EnemyFill { get; private set; }
        /// <summary>
        /// The diamond. Used for the three food boxes and for the guardian power box - by
        /// design, not as a stand-in: the power box is meant to read as one of the same family.
        /// </summary>
        public static Sprite FoodFrame { get; private set; }

        /// <summary>
        /// The mark drawn over a one-handed weapon that has been told not to call the shield
        /// (see ShieldSlot). Optional on purpose: until the art exists the exclusion still
        /// works, it just has nothing to show for itself, which is better than shipping a
        /// stand-in that does not match the rest.
        /// </summary>
        public static Sprite ShieldExcluded { get; private set; }

        /// <summary>
        /// The equipment panel's artwork, or null if the PNG is missing - and then the panel
        /// falls back to the plain 9-sliced piece it used before, rather than nothing.
        /// </summary>
        public static Sprite EquipmentPanel { get; private set; }

        /// <summary>
        /// Cartur's 2026-10-03 panel boards (tools/art/boards.py): each one his picture with only
        /// the white around it made transparent. Raw textures, because the inventory board is
        /// drawn as slices of itself - see InventoryBoard - rather than as one sprite.
        /// Null when the PNG is missing, and every user falls back to what it drew before.
        /// </summary>
        public static Texture2D Board(string name) =>
            s_boards.TryGetValue(name, out Texture2D tex) ? tex : null;

        private static readonly Dictionary<string, Texture2D> s_boards = new Dictionary<string, Texture2D>();
        private static readonly string[] s_boardNames =
            { "inventory", "hotbar", "equipment", "crafting", "wide", "tall", "small", "banner", "button",
              "mainmenu", "charselect", "world", "serverlist", "eula", "newworld", "modifiers", "namepanel", "custom",
              "window", "window_base", "craftingfull", "grid", "grid_base", "grid_band", "grid_wood", "store", "adventure", "enchant",
              "enchant_sacrifice", "enchant_convert", "enchant_enchant", "enchant_augment", "enchant_disenchant",
              "enchant_rune", "enchant_upgrade",
              "slider_track", "slider_fill", "slider_knob",
              "chest_icon_take", "chest_icon_stack", "chest_icon_sort", "chest_icon_sortall" };

        public static string AssetsDir { get; private set; }

        /// <summary>Swap the panel piece for the generated wood one. Set before LoadTextures.</summary>
        public static bool WoodPanel { get; set; }

        /// <summary>The wood panel's own rail, in source px. Measured off the art, not chosen.</summary>
        private const float WoodPanelBorder = 101f;

        public static bool LoadTextures(BepInEx.Logging.ManualLogSource log)
        {
            AssetsDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "assets");

            s_barFrame = Load("bar_frame.png", log);
            s_barFill = Load("bar_fill.png", log);
            s_enemyFill = Load("bar_fill_enemy.png", log);
            s_foodFrame = Load("food_frame.png", log);
            s_knotTex = Load("panel_knot.png", log);
            s_knotBottomTex = Load("panel_knot_bottom.png", log);
            s_shieldExcluded = Load("shield_excluded.png", log);
            s_equipmentPanel = Load("equipment_panel.png", log);
            foreach (string board in s_boardNames)
                s_boards[board] = Load("board_" + board + ".png", log);
            TintToBag(Board("hotbar"));

            // Menu pieces are optional: a missing one leaves that vanilla sprite alone, which
            // Skin logs, rather than taking the HUD down with it.
            foreach ((string name, float border, float perUnit) in s_pieces)
            {
                s_pieceUnit[name] = perUnit;
                // The wood panel is a drop-in for this one piece only, and it carries its own
                // border: its corner knotwork is 101px deep in the art it was cut from, and a 9-slice
                // border has to cover the corner or the ornament gets stretched. Forcing it to the built
                // panel's 48 meant upscaling a 30px bevel, which blurred it and threw the corner
                // mitres out of line with the edges - measured, after it looked wrong on screen.
                bool wood = name == "panel" && WoodPanel;
                s_pieceTex[name] = Load(wood ? "panel_wood.png" : name + ".png", log);
                s_pieceBorder[name] = wood ? WoodPanelBorder : border;
            }

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

            // Drawn Simple, stretched to whatever the game sized the bar, so ppu only has to not
            // be zero. Not Tiled like BarFill: an enemy bar is 100x5 units and one tile of this art
            // is 975x48.5, so tiling showed a tenth of it - a sliver with no pattern in it.
            if (s_enemyFill != null)
                EnemyFill = Make(s_enemyFill, Vector4.zero, referencePixelsPerUnit);

            // Drawn Simple with preserveAspect, so ppu only has to not be zero.
            FoodFrame = Make(s_foodFrame, Vector4.zero, referencePixelsPerUnit);

            if (s_shieldExcluded != null)
                ShieldExcluded = Make(s_shieldExcluded, Vector4.zero, referencePixelsPerUnit);

            // Simple and stretched to the panel's rect, so the ppu only has to not be zero.
            if (s_equipmentPanel != null)
                EquipmentPanel = Make(s_equipmentPanel, Vector4.zero, referencePixelsPerUnit);

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

        /// <summary>
        /// A texture from any path under the assets folder. Same loader as the kit's own
        /// pieces - ImageConversion reads PNG and JPG alike - exposed for the loading screens,
        /// which are photographs rather than kit art and are loaded one at a time.
        /// </summary>
        internal static Texture2D LoadFile(string path, BepInEx.Logging.ManualLogSource log) =>
            Read(path, log);

        private static Texture2D Load(string fileName, BepInEx.Logging.ManualLogSource log) =>
            Read(Path.Combine(AssetsDir, fileName), log);

        private static Texture2D Read(string path, BepInEx.Logging.ManualLogSource log)
        {
            if (!File.Exists(path))
            {
                log.LogWarning("asset missing: " + path);
                return null;
            }

            // With mips (Fable review, 2026-10-05): the boards are 1000-2000 px and draw 2-4x
            // smaller at 1080p or a low GUI scale, and without mips the knotwork shimmers.
            // LoadImage keeps the mip setting the texture was made with.
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            if (LoadImageViaReflection(tex, File.ReadAllBytes(path)))
            {
                ClearFringe(tex);
                return tex;
            }

            log.LogWarning("ImageConversion.LoadImage failed for " + path);
            return null;
        }

        /// <summary>
        /// The boards were cut from white backgrounds, so their fully transparent pixels still hold
        /// white (board_world.png: 253/253/252). Bilinear filtering blends a sample's colour with its
        /// transparent neighbour's, which drew a 1 px light line round the panels (Cartur,
        /// 2026-10-04, world and server select). Transparent pixels are made black: they never
        /// show, and an edge blended towards black is invisible against the dark frames.
        /// </summary>
        private static void ClearFringe(Texture2D tex)
        {
            Color32[] px = tex.GetPixels32();
            bool changed = false;
            for (int i = 0; i < px.Length; i++)
                if (px[i].a == 0 && (px[i].r | px[i].g | px[i].b) != 0)
                {
                    px[i] = new Color32(0, 0, 0, 0);
                    changed = true;
                }
            // The cut also left the outermost ring half-mixed with that white: semi-transparent
            // light grey (board_world.png 92/91/86, board_grid.png 94/92/83), a fainter line once the
            // white was gone (pilot, 2026-10-04). Each takes the colour of an opaque neighbour, keeping
            // its own alpha, so the edge stays soft but is the frame's colour.
            int w = tex.width, h = tex.height;
            var src = (Color32[])px.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (src[i].a == 0 || src[i].a == 255)
                        continue;
                    // Only the outer ring - a pixel touching the clear outside - not a soft glow inside.
                    bool outside = (x > 0 && src[i - 1].a == 0) || (x < w - 1 && src[i + 1].a == 0)
                                   || (y > 0 && src[i - w].a == 0) || (y < h - 1 && src[i + w].a == 0);
                    if (!outside)
                        continue;
                    int n = x > 0 && src[i - 1].a == 255 ? i - 1
                          : x < w - 1 && src[i + 1].a == 255 ? i + 1
                          : y > 0 && src[i - w].a == 255 ? i - w
                          : y < h - 1 && src[i + w].a == 255 ? i + w : -1;
                    if (n < 0)
                        continue;
                    px[i] = new Color32(src[n].r, src[n].g, src[n].b, src[i].a);
                    changed = true;
                }
            if (!changed)
                return;
            tex.SetPixels32(px);
            tex.Apply(true);   // rebuild the mips from the corrected pixels
        }

        /// <summary>
        /// The hotbar board's wood read lighter and warmer than the bag board's. Measured inside the
        /// empty cells, clear of the rune strip (2026-10-04): hotbar 32/26/20, bag 24/18/11, so the
        /// wood is scaled by bag/hotbar. Teal pixels (runes, raven eyes: green or blue 25 over red)
        /// keep their glow, which a plain Image.color tint would dim. The art file is not touched.
        /// </summary>
        private static void TintToBag(Texture2D tex)
        {
            if (tex == null)
                return;
            Color32[] px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                if (c.b > c.r + 25 || c.g > c.r + 25)
                    continue;
                px[i] = new Color32((byte)(c.r * 0.742f), (byte)(c.g * 0.685f), (byte)(c.b * 0.562f), c.a);
            }
            tex.SetPixels32(px);
            tex.Apply(true);   // rebuild the mips from the corrected pixels
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
