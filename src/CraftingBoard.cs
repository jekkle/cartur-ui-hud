using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Fits the crafting panel's own pieces onto Cartur's crafting board (board_crafting.png,
    /// 1024x1495, 2026-10-03) and takes the vanilla plates off: "all the icons and text need to be
    /// brought down properly and put correctly to fit my art and remove vanilla ui for this panel".
    ///
    /// Regions are measured off the board - brightness profiles, then drawn back on the art to
    /// check (tools/art/out/crafting_regions.png) - in board px from its top-left:
    ///
    ///     title bar      (141, 183)..(881, 281)
    ///     recipe list    (106, 328)..(489, 1395), rows 82 px tall every 107.5 px
    ///     description    (530, 330)..(930, 1035)
    ///     requirements   (553, 1095)..(896, 1167)
    ///     Craft button   (550, 1208)..(898, 1321)
    ///
    /// The vanilla pieces and their names come from the live hierarchy, dumped by the test pilot
    /// (results.txt, 2026-10-03). Each piece is scaled uniformly and moved so its own drawn rect
    /// lands in its region, measured in world space - so it holds whatever edit mode has done to
    /// the panel's size.
    /// </summary>
    internal static class CraftingBoard
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string BoardName = "CarturUI_Board_crafting";

        // Cartur's crafting board, second cut (board_craftingfull.png, 1024x1791, 2026-10-04): ten
        // painted rows, narrower list, new requirements box and button. Every region read off it and
        // drawn back on the art to check (tools/art/out). Same row pitch and height as before.
        private static readonly Rect Title = Px(142, 483, 880, 575);
        // Row one of the painted list carries the tabs (Cartur, 2026-10-03: "move the item list one
        // row down and fit the tabs on the first row"); the recipes start on row two.
        private static readonly Rect TabRow = Px(105, 623, 421, 707);
        private static readonly Rect List = Px(105, 730.5f, 421, 1676);
        // The painted word "Craft" on the button, and a blank strip of the same face beside it,
        // read off a 2x crop of the board (tools/art/out/craft_button.png).
        private static readonly Rect CraftWord = Px(660, 1240, 790, 1293);
        private static readonly Rect BlankFace = Px(630, 1240, 660, 1293);
        private static readonly Rect Detail = Px(470, 600, 920, 1330);
        private static readonly Rect Reqs = Px(478, 1388, 912, 1476);
        private static readonly Rect CraftBtn = Px(491, 1505, 894, 1622);
        private const float RowPitchPx = 107.5f;
        private const float RowHeightPx = 84f;

        private static Rect Px(float x0, float y0, float x1, float y1) => Rect.MinMaxRect(x0, y0, x1, y1);

        // The full board carries a header for the info panel's name and icons, as one piece; its
        // open wood, clear of the top rail and of the tree medallion below. The regions above are
        // all in this board's px; the old board (fallback when this file is missing) no longer
        // matches them.
        private static readonly Rect Header = Px(70, 100, 950, 330);

        private static Texture2D FullTex => AssetLoader.Board("craftingfull");
        private static bool Full => FullTex != null;
        private static Texture2D Tex => Full ? FullTex : AssetLoader.Board("crafting");

        // The info panel's banner (board_banner.png, 1485x637): its inner wood, edges read off the
        // board (x 102/1383, y 110/521), inset clear of the corner runes.
        private static readonly Rect BannerInside = Px(150, 106, 1330, 505);

        /// <summary>
        /// The banner's art is about 3.1:1 inside and the panel's contents - the name over five tab
        /// icons - about 4.4:1, so there is always spare wood. Cartur's call: "put the name and
        /// icons just under the top border". The contents keep their size and move up as one group
        /// to the centre of the banner's inside. Panel-wide children (a background or a
        /// separator) are not contents and stay put; what moved is logged once.
        /// </summary>
        internal static void FitInfo(InventoryGui gui)
        {
            RectTransform info = gui.m_info;
            if (info == null)
                return;
            // On the full board the name and icons sit in its header, and the info panel's own
            // frame and banner do not draw.
            RectTransform board = Full ? gui.m_crafting?.Find(BoardName) as RectTransform
                                       : info.Find("CarturUI_Board_banner") as RectTransform;
            Texture2D tex = Full ? FullTex : AssetLoader.Board("banner");
            if (board == null || tex == null)
            {
                InfoOnce("info panel not fitted: board " + (board != null) + ", art " + (tex != null));
                return;
            }
            Hide(info.Find("Darken"));
            if (Full)
            {
                Hide(info.Find("Bkg"));
                // The header is painted on the crafting panel's board, so the info panel has to draw
                // after it or the board covers the name and icons.
                if (gui.m_crafting != null && info.parent == gui.m_crafting.parent
                    && info.GetSiblingIndex() < gui.m_crafting.GetSiblingIndex())
                    info.SetSiblingIndex(gui.m_crafting.GetSiblingIndex());
            }

            float panelWidth = info.rect.width;
            var parts = new System.Collections.Generic.List<Transform>();
            foreach (Transform c in info)
            {
                var rt = c as RectTransform;
                if (rt == null || c == board || c.name == "Bkg" || c.name == "Darken" || !c.gameObject.activeSelf)
                    continue;
                if (rt.rect.width >= panelWidth * 0.9f)
                    continue;
                parts.Add(c);
            }
            // Measured on what is drawn, not on the parts' rects: some rects reach well above their
            // icons, so the group stopped short of the rail.
            var drawn = new System.Collections.Generic.List<Transform>();
            foreach (Transform c in parts)
                foreach (Graphic g in c.GetComponentsInChildren<Graphic>(false))
                    if (g.enabled && g.color.a > 0.01f && (!(g is Image i) || i.sprite != null))
                        drawn.Add(g.transform);
            if (!Measure(null, drawn.ToArray(), out Rect have))
            {
                InfoOnce("info panel not fitted: " + parts.Count + " parts (" + string.Join(", ", parts.ConvertAll(c => c.name))
                         + "), nothing drawn among them; children " + info.childCount + ", width " + panelWidth.ToString("0"));
                return;
            }

            Rect inside = World(board, tex, Full ? Header : BannerInside);
            // Centred in the banner's inside, at vanilla's size.
            Vector3 move = new Vector3(inside.center.x - have.center.x, inside.center.y - have.center.y, 0f);
            foreach (Transform c in parts)
                c.position += move;
            if (!s_infoLogged)
            {
                s_infoLogged = true;
                Log.LogInfo("info panel: moved " + string.Join(", ", parts.ConvertAll(c => c.name)) + " to the centre of the banner");
            }
        }

        private static bool s_infoLogged;
        private static string s_infoSaid;

        private static void InfoOnce(string line)
        {
            if (s_infoSaid == line)
                return;
            s_infoSaid = line;
            Log.LogInfo(line);
        }

        /// <summary>
        /// On the full board the name and icons are part of the crafting panel's picture, but they
        /// live on the info panel. When the crafting panel moves or is resized - edit mode, or the
        /// screen's own open animation - they are laid back on its header.
        /// </summary>
        private sealed class InfoFollow : MonoBehaviour
        {
            private Vector3 m_pos, m_scale;

            private void LateUpdate()
            {
                if (!Full || InventoryGui.instance == null || !InventoryGui.IsVisible())
                    return;
                if (transform.position == m_pos && transform.lossyScale == m_scale)
                    return;
                m_pos = transform.position;
                m_scale = transform.lossyScale;
                FitInfo(InventoryGui.instance);
            }
        }

        /// <summary>The board's description region in world space, for the Styles tab's pane.</summary>
        internal static bool DetailRegion(InventoryGui gui, out Rect world)
        {
            world = default;
            var board = gui != null && gui.m_crafting != null ? gui.m_crafting.Find(BoardName) as RectTransform : null;
            Texture2D tex = Tex;
            if (board == null || tex == null)
                return false;
            world = World(board, tex, Detail);
            return true;
        }

        internal static void Fit(InventoryGui gui)
        {
            RectTransform craft = gui.m_crafting;
            var board = craft != null ? craft.Find(BoardName) as RectTransform : null;
            if (board == null)
                return;
            Texture2D tex = Tex;

            // Vanilla plates the board replaces. Graphics that carry a Mask keep masking and stop
            // drawing; a button's graphic goes clear rather than off, so it can still be clicked.
            Hide(craft.Find("Darken"));
            Hide(craft.Find("RecipeList/Recipes"));
            Hide(craft.Find("RecipeList/RecipeScroll"));
            Hide(craft.Find("RecipeList/RecipeScroll/Sliding Area/Handle"));
            Hide(craft.Find("TabsButtons/TabBorder"));
            Hide(craft.Find("BraidLineHorisontalMedium"));
            Hide(craft.Find("Decription"));
            Hide(craft.Find("Decription/requirements/level"));
            for (int i = 0; i < 4; i++)
                Hide(craft.Find("Decription/requirements/res_bkg" + (i == 0 ? "" : " (" + i + ")")));
            Hide(craft.Find("Decription/craft_button_panel/CraftButton"));
            // The old art paints "Craft" on its button. The component is switched off, not the object:
            // InventoryGui.UpdateRecipe writes the label through GetComponentInChildren every frame,
            // which skips inactive objects - deactivating it threw a NullReferenceException there.
            // The full board's button is blank, so there the game's label stays on.
            var label = craft.Find("Decription/craft_button_panel/CraftButton/Text")?.GetComponent<TMP_Text>();
            if (!Full && label != null && label != s_label)
            {
                label.enabled = false;
                s_label = label;
                s_plate = Plate(board, tex);
            }

            if (Full && craft.GetComponent<InfoFollow>() == null)
                craft.gameObject.AddComponent<InfoFollow>();

            Into(craft.Find("RecipeList"), board, tex, List, top: true);
            Into(craft.Find("Decription"), board, tex, Detail, top: true,
                 craft.Find("Decription/Icon"), craft.Find("Decription/Name"), craft.Find("Decription/Description"));
            Readable(craft.Find("Decription"));
            Into(craft.Find("Decription/requirements"), board, tex, Reqs, top: false);
            Into(craft.Find("Decription/craft_button_panel"), board, tex, CraftBtn, top: false,
                 craft.Find("Decription/craft_button_panel/CraftButton"));
            // The craft progress bar ("Crafting" while an item is being made) stands in for the button
            // while it runs, so it goes on the button's paint; it was left floating above it.
            Into(craft.Find("Decription/craft_button_panel/Progress"), board, tex, CraftBtn, top: false);

            // Title bar, left to right: the repair button in a square at the end, the station name,
            // then the tabs. Repair used to hang off the panel's left edge, which is a raven now.
            // The station's level star takes the matching square at the far end. Both used to sit on
            // the ravens, out where vanilla's own frame had room for them.
            float pad = Title.height * 0.12f;
            Rect repair = Rect.MinMaxRect(Title.xMin + pad, Title.yMin + pad, Title.xMin + Title.height - pad, Title.yMax - pad);
            Rect level = Rect.MinMaxRect(Title.xMax - Title.height + pad, Title.yMin + pad, Title.xMax - pad, Title.yMax - pad);
            Rect left = Rect.MinMaxRect(repair.xMax + pad, Title.yMin, level.xMin - pad, Title.yMax);
            Into(craft.Find("RepairButton"), board, tex, repair, top: false);
            // The repair button keeps the game's own colours, disabled look and glow (Cartur:
            // "light up normal vanilla colors when can be used or not").
            Into(craft.Find("RepairSimple"), board, tex, repair, top: false);
            Into(craft.Find("Level"), board, tex, level, top: false);
            // The name already says which station; its icon has no place left on the board.
            // The Image, not the object: the game turns the object back on every update, and it
            // drew over the left raven (dumped at the bench, 2026-10-03).
            var icon = craft.Find("station_icon")?.GetComponent<Image>();
            if (icon != null)
                icon.enabled = false;
            Station(craft.Find("topic"), board, tex, left);
            Into(craft.Find("TabsButtons"), board, tex, TabRow, top: false,
                 craft.Find("TabsButtons/Craft"), craft.Find("TabsButtons/UPGRADE"),
                 craft.Find("TabsButtons/CarturUI_StylesTab"));
            Tall(board, tex, craft.Find("TabsButtons/Craft"), craft.Find("TabsButtons/UPGRADE"),
                 craft.Find("TabsButtons/CarturUI_StylesTab"));

            // Rows on the board's rows. AddRecipeToList puts element i at -i * m_recipeListSpace in
            // the list root (Cecil, InventoryGui.AddRecipeToList), so the pitch is set in that root's
            // own units.
            var root = gui.m_recipeListRoot;
            if (root != null)
            {
                float worldPerPx = board.rect.width > 0f ? WorldWidth(board) / tex.width : 0f;
                float rootScale = root.lossyScale.y;
                if (worldPerPx > 0f && rootScale > 0f)
                {
                    gui.m_recipeListSpace = RowPitchPx * worldPerPx / rootScale;
                    var prefab = gui.m_recipeElementPrefab != null
                        ? gui.m_recipeElementPrefab.transform as RectTransform : null;
                    // As wide as the painted row too: at vanilla's 187 the selection ran past the
                    // row's right rim (Cartur, 2026-10-04). Every highlight child is laid on the row.
                    if (prefab != null)
                    {
                        prefab.sizeDelta = new Vector2(List.width * worldPerPx / rootScale, RowHeightPx * worldPerPx / rootScale);
                        foreach (Transform c in prefab)
                            if (c.name == "selected" || c.name == "bkg")
                            {
                                var crt = (RectTransform)c;
                                crt.anchorMin = Vector2.zero;
                                crt.anchorMax = Vector2.one;
                                crt.offsetMin = crt.offsetMax = Vector2.zero;
                            }
                        var sel = prefab.Find("selected")?.GetComponent<Image>();
                        if (sel != null)
                            sel.color = new Color(1f, 0.8f, 0.35f, 0.45f);   // the world list's soft gold
                    }
                    var bkg = prefab != null ? prefab.Find("bkg")?.GetComponent<Image>() : null;
                    if (bkg != null)
                        bkg.color = Color.clear;
                }
            }

            Log.LogInfo("crafting fitted to the board: recipe pitch " + gui.m_recipeListSpace.ToString("0.#"));
        }

        /// <summary>
        /// Fitted again on every open: which tabs and buttons exist depends on the station - at
        /// Awake only CRAFT is up, and three tabs sized for one ran off the board. Into is
        /// idempotent, so a second pass over a fitted piece moves nothing.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "Show")]
        [HarmonyPostfix]
        private static void Shown(InventoryGui __instance)
        {
            RectTransform craft = __instance.m_crafting;
            if (craft == null || craft.Find(BoardName) == null)
                return;
            Fit(__instance);
        }

        /// <summary>
        /// The rows onto the painted rows, left edge to left edge. Measured on a real row each time
        /// the list is built: fitted as a whole, the list sat about 12 px right of the paint and the
        /// selection hung over the rim (pilot, 2026-10-04). Idempotent - a fitted list moves nothing.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
        [HarmonyPostfix]
        private static void RowsOnPaint(InventoryGui __instance)
        {
            RectTransform root = __instance.m_recipeListRoot;
            var board = __instance.m_crafting != null ? __instance.m_crafting.Find(BoardName) as RectTransform : null;
            Texture2D tex = Tex;
            if (root == null || board == null || tex == null || root.childCount == 0)
                return;
            var row = root.GetChild(0) as RectTransform;
            if (row == null || row.rect.width <= 0f)
                return;
            var c = new Vector3[4];
            row.GetWorldCorners(c);
            float dx = World(board, tex, List).xMin - c[0].x;
            if (Mathf.Abs(dx) > 0.5f)
                root.position += new Vector3(dx, 0f, 0f);

            // The highlight fills the painted box's face, inside its bronze rim - as a plain rect on
            // the row's outer edge it covered the rim and read as off the box (Cartur, 2026-10-04:
            // "the yellow highlight needs to match the box the item is in"). Rim measured on the
            // art: 5 px on every side of the 316x84 box.
            float worldPerPx = WorldWidth(board) / tex.width;
            float k = row.lossyScale.x > 0f ? worldPerPx / row.lossyScale.x : 0f;
            foreach (Transform r in root)
            {
                var sel = r.Find("selected") as RectTransform;
                if (sel == null)
                    continue;
                sel.anchorMin = Vector2.zero;
                sel.anchorMax = Vector2.one;
                sel.offsetMin = new Vector2(RowRimPx * k, RowRimPx * k);
                sel.offsetMax = new Vector2(-RowRimPx * k, -RowRimPx * k);
                // A see-through gold so the light item name stays readable on it - drawn opaque, the
                // name vanished into it (review, 2026-10-04).
                if (sel.GetComponent<Image>() is Image glow)
                {
                    glow.sprite = null;
                    glow.color = Selection;
                }
            }
        }

        private const float RowRimPx = 5f;
        internal static readonly Color Selection = new Color(1f, 0.78f, 0.3f, 0.28f);

        /// <summary>
        /// The station name fills its part of the title bar rather than being scaled down with
        /// its rect - Into shrank it to the 30-unit line it was drawn on, and it read as a smudge.
        /// The rect is set to the region and TMP sizes the text to it, in the tabs' gold.
        /// </summary>
        private static void Station(Transform topic, RectTransform board, Texture2D tex, Rect px)
        {
            var text = topic != null ? topic.GetComponent<TMP_Text>() : null;
            if (text == null)
                return;
            var rt = (RectTransform)topic;
            Rect target = World(board, tex, px);
            Vector3 scale = rt.lossyScale;
            if (scale.x <= 0f || scale.y <= 0f)
                return;
            rt.sizeDelta = new Vector2(target.width / scale.x, target.height / scale.y)
                           - (rt.anchorMax - rt.anchorMin) * ((RectTransform)rt.parent).rect.size;
            rt.position += (Vector3)(target.center - (Vector2)rt.TransformPoint(rt.rect.center));
            text.enableAutoSizing = true;
            text.fontSizeMin = 10f;
            text.fontSizeMax = 40f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Gold;
        }

        private static readonly Color Gold = new Color32(255, 216, 0, 255);   // vanilla's own gold

        /// <summary>
        /// The painted button says "Craft", so the game's label stays off - except on the upgrade
        /// tab (and the Styles tab's Change), where the paint would lie. There the label comes back
        /// on its own dark plate over the paint. InUpradeTab is the game's spelling (Cecil).
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPostfix]
        private static void Label(InventoryGui __instance)
        {
            if (s_label == null || !InventoryGui.IsVisible())
                return;
            bool upgrade = __instance.InUpradeTab();
            if (s_label.enabled != upgrade)
                s_label.enabled = upgrade;
            // The Styles tab's Change button sits on the painted button too.
            bool cover = upgrade || StyleTab.Active;
            if (s_plate != null && s_plate.enabled != cover)
                s_plate.enabled = cover;
        }

        private static TMP_Text s_label;
        private static Image s_plate;

        /// <summary>
        /// Covers the painted word with the button's own blank face, tiled from the strip beside
        /// the word - his pixels, so the button still reads as his art with "Upgrade" on it. A flat
        /// brown plate was tried first and Cartur called it ugly. A child of the board, so it
        /// draws over the art and under the label.
        /// </summary>
        private static Image Plate(RectTransform board, Texture2D tex)
        {
            var go = new GameObject("CarturUI_ButtonFace", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(board, false);
            float sx = board.rect.width / tex.width, sy = board.rect.height / tex.height;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(CraftWord.xMin * sx, -CraftWord.yMin * sy);
            rt.sizeDelta = new Vector2(CraftWord.width * sx, CraftWord.height * sy);
            var img = go.GetComponent<Image>();
            img.sprite = Sprite.Create(tex, new Rect(BlankFace.xMin, tex.height - BlankFace.yMax,
                                                     BlankFace.width, BlankFace.height),
                                       new Vector2(0.5f, 0.5f), 1f / sy, 0, SpriteMeshType.FullRect);
            img.type = Image.Type.Tiled;
            // A tile is sprite px / (sprite ppu / canvas ppu * multiplier) units; this makes one
            // source px the same size here as on the board under it.
            Canvas canvas = board.GetComponentInParent<Canvas>();
            img.pixelsPerUnitMultiplier = canvas != null ? canvas.referencePixelsPerUnit : 100f;
            img.material = null;
            img.raycastTarget = false;
            img.enabled = false;
            return img;
        }

        /// <summary>
        /// Into keeps a piece's shape, and three tabs side by side are long and thin - fitted to
        /// the row's width they came out a third of its height. Each tab is grown to the row's full
        /// height about its own centre; the labels size themselves to it.
        /// </summary>
        private static void Tall(RectTransform board, Texture2D tex, params Transform[] tabs)
        {
            float rowHeight = World(board, tex, TabRow).height * 0.8f;
            foreach (Transform t in tabs)
            {
                var rt = t as RectTransform;
                if (rt == null || rt.lossyScale.y <= 0f)
                    continue;
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight / rt.lossyScale.y);
            }

            // One size for every label: each sized itself to its own word, so UPGRADE came out
            // smaller than CRAFT (Cartur: "all the tabs need the same font"). The size the longest
            // word fits at is the size they all get.
            var labels = new System.Collections.Generic.List<TMP_Text>();
            foreach (Transform t in tabs)
                if (t != null)
                    foreach (TMP_Text text in t.GetComponentsInChildren<TMP_Text>(true))
                        // Each tab also holds a gamepad hint whose text reads 'MISSING BUTTON DEF
                        // "LTrigger"' - sized to that, every label came out a dash.
                        if (!UnderHint(text.transform, t))
                            labels.Add(text);
            // Worked out from each word's preferred size at a reference size rather than read back
            // from TMP's auto-size, which reported the maximum for labels not drawn yet and cut
            // the words off ("CRAF", "UPG").
            const float probe = 36f;
            float size = probe;
            foreach (TMP_Text text in labels)
            {
                var r = (RectTransform)text.transform;
                if (string.IsNullOrEmpty(text.text) || r.rect.width <= 0f || r.rect.height <= 0f)
                    continue;
                text.enableAutoSizing = false;
                text.fontSize = probe;
                Vector2 want = text.GetPreferredValues(text.text);
                if (want.x > 0f)
                    size = Mathf.Min(size, probe * r.rect.width * 0.85f / want.x);
                if (want.y > 0f)
                    size = Mathf.Min(size, probe * r.rect.height * 0.8f / want.y);
            }
            foreach (TMP_Text text in labels)
            {
                text.enableAutoSizing = false;
                text.fontSize = size;
            }
        }

        private static bool UnderHint(Transform t, Transform stop)
        {
            for (; t != null && t != stop; t = t.parent)
                if (t.name.StartsWith("gamepad_hint"))
                    return true;
            return false;
        }

        /// <summary>
        /// The description pane is scaled down to fit the board's narrower column (about 0.7), and
        /// its text with it, which read as too small (Cartur, 2026-10-03). The item name and the
        /// description are drawn at their vanilla size again by dividing their font size by the
        /// pane's scale; the description wraps to more lines inside the same column. Sizes come
        /// from the originals each time, so a second Fit does not compound.
        /// </summary>
        private static void Readable(Transform pane)
        {
            if (pane == null || pane.localScale.x <= 0.01f)
                return;
            float k = 1f / pane.localScale.x;
            foreach (string name in new[] { "Name", "Description" })
            {
                var text = pane.Find(name)?.GetComponent<TMP_Text>();
                if (text == null)
                    continue;
                int id = text.GetInstanceID();
                if (!s_fontSize.TryGetValue(id, out float size))
                    s_fontSize[id] = size = text.fontSize;
                // Up to vanilla's size, shrinking only as far as the text needs to fit its box: a
                // long stat list at full size ran off the bottom of the pane (audit, 2026-10-03).
                text.fontSize = size * k;
                text.enableAutoSizing = true;
                text.fontSizeMax = size * k;
                // Below vanilla's size only as far as a long list needs: an upgrade's stats ran past
                // the box at vanilla size, and the box cannot grow - the upgrade line sits under it.
                text.fontSizeMin = size * 0.75f;
            }
        }

        private static readonly System.Collections.Generic.Dictionary<int, float> s_fontSize =
            new System.Collections.Generic.Dictionary<int, float>();

        private static void Hide(Transform t)
        {
            if (t == null)
                return;
            var g = t.GetComponent<Graphic>();
            if (g == null)
                return;
            var mask = t.GetComponent<Mask>();
            if (mask != null)
                mask.showMaskGraphic = false;
            else if (t.GetComponent<Selectable>() != null || t.GetComponent<Scrollbar>() != null)
                g.color = Color.clear;
            else
                g.enabled = false;
        }

        /// <summary>
        /// Scales <paramref name="mover"/> uniformly and moves it so the drawn rect of
        /// <paramref name="measure"/> (its own rect if none given) sits in <paramref name="px"/> on
        /// the board: as wide as it can within the region, top-aligned or centred.
        /// </summary>
        private static void Into(Transform mover, RectTransform board, Texture2D tex, Rect px, bool top,
                                 params Transform[] measure)
        {
            if (mover == null)
                return;
            Rect target = World(board, tex, px);
            if (!Measure(mover, measure, out Rect have) || have.width <= 0f || have.height <= 0f)
                return;

            float f = Mathf.Min(target.width / have.width, target.height / have.height);
            mover.localScale *= f;
            if (!Measure(mover, measure, out have))
                return;

            float dx = target.center.x - have.center.x;
            float dy = top ? target.yMax - have.yMax : target.center.y - have.center.y;
            mover.position += new Vector3(dx, dy, 0f);
        }

        private static bool Measure(Transform mover, Transform[] measure, out Rect r)
        {
            r = default;
            bool any = false;
            var corners = new Vector3[4];
            Transform[] list = measure != null && measure.Length > 0 ? measure : new[] { mover };
            foreach (Transform t in list)
            {
                var rt = t as RectTransform;
                // Inactive pieces count too: the tabs a station turns on arrive after Show, and
                // a rect is valid whether or not its object is drawn.
                if (rt == null)
                    continue;
                rt.GetWorldCorners(corners);
                Rect one = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                r = any ? Rect.MinMaxRect(Mathf.Min(r.xMin, one.xMin), Mathf.Min(r.yMin, one.yMin),
                                          Mathf.Max(r.xMax, one.xMax), Mathf.Max(r.yMax, one.yMax)) : one;
                any = true;
            }
            return any;
        }

        /// <summary>A region in board px (from the top-left) as a world-space rect.</summary>
        private static Rect World(RectTransform board, Texture2D tex, Rect px)
        {
            var c = new Vector3[4];
            board.GetWorldCorners(c);
            float sx = (c[2].x - c[0].x) / tex.width, sy = (c[2].y - c[0].y) / tex.height;
            return Rect.MinMaxRect(c[0].x + px.xMin * sx, c[2].y - px.yMax * sy,
                                   c[0].x + px.xMax * sx, c[2].y - px.yMin * sy);
        }

        private static float WorldWidth(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return c[2].x - c[0].x;
        }
    }
}
