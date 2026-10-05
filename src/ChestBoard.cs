using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// The container window on Cartur's grid board (GridBoard), laid out for the chest's own size -
    /// every container in the game is at least 2x2 apart from pots and gifts, which keep the
    /// window's own frame. Each slot sits on the centre of its painted cell.
    ///
    /// Above the cells is a band of planks for the chest's name and buttons, his layout A
    /// (2026-10-04): the name centred, the buttons on plates cut from the board's own slot rims,
    /// two each side - Take all and Place stacks on the left, Waste Management's Sort and Sort All
    /// on the right, split by where the game and that mod put them.
    /// </summary>
    internal static class ChestBoard
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string Name = "CarturUI_ChestBoard";
        private static readonly Color Gold = new Color32(255, 214, 90, 255);
        private static readonly Color Parchment = new Color32(232, 214, 170, 255);

        private static readonly FieldInfo s_elements = AccessTools.Field(typeof(InventoryGrid), "m_elements");
        private static readonly FieldInfo s_current = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
        private static readonly HashSet<string> s_said = new HashSet<string>();

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        [HarmonyPostfix]
        private static void Lay(InventoryGrid __instance)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || __instance != gui.ContainerGrid || __instance.m_gridRoot == null || gui.m_container == null)
                return;
            Weight(gui);
            Inventory inv = __instance.GetInventory();
            if (inv == null || !(s_elements?.GetValue(__instance) is List<InventoryElement> elements))
                return;
            int w = inv.GetWidth(), h = inv.GetHeight();
            if (!GridBoard.Active || w < 2 || h < 2 || elements.Count < w * h)
            {
                Show(gui, false);
                if (__instance.m_scrollbar != null && !__instance.m_scrollbar.gameObject.activeSelf)
                    __instance.m_scrollbar.gameObject.SetActive(true);
                return;
            }

            // One board cell = one slot pitch, on the tighter axis so a slot fits its cell both ways.
            float s = __instance.m_elementSpace / Mathf.Min(GridBoard.ColPitchPx, GridBoard.RowPitchPx);
            RectTransform panel = gui.m_container;
            RectTransform board = Root(panel);
            Vector3 gs = __instance.m_gridRoot.lossyScale, ps = panel.lossyScale;
            if (ps.x <= 0f || ps.y <= 0f)
                return;
            board.localScale = new Vector3(gs.x / ps.x, gs.y / ps.y, 1f);
            // A chest taller than it is wide (the 5x10 wardrobe) is shown on its side, 10 across by 5
            // down, the same shape as the rest (Cartur, 2026-10-04). Display only: element e - the
            // item at (e % w, e / w) - is drawn at cell (e % shownW, e / shownW); no item moves.
            int shownW = h > w ? h : w, shownH = h > w ? w : h;
            GridBoard.Layout lay = GridBoard.Build(board, shownW, shownH, true, s);

            // Vanilla puts element (col, row) at base + (col, -row) * space and never moves element 0,
            // so element 0 IS the base; the target is written, never added, so a rerun is a no-op.
            var first = elements[0]?.transform as RectTransform;
            if (first == null)
                return;
            Vector2 origin = first.anchoredPosition;
            for (int e = 0; e < w * h; e++)
            {
                int c = e % shownW, r = e / shownW;
                var rt = elements[e]?.transform as RectTransform;
                if (rt == null)
                    continue;
                rt.anchoredPosition = origin + new Vector2(lay.X[c] - lay.X[0], -(lay.Y[r] - lay.Y[0])) * s;
                // The board paints the cell; the cell's own frame would draw a second box.
                if (rt.GetComponent<Image>()?.sprite != null)
                    EquipmentPanel.Bare(rt.gameObject);
            }

            // On the window, not in the grid: the bigger chests' grid sits in a scroll mask, which cut
            // the rails off (pilot, 2026-10-04). Placed in world space from cell (0,0)'s centre.
            Vector3 centre = first.TransformPoint(first.rect.center);
            board.position = centre + new Vector3(-lay.X[0] * s * gs.x, lay.Y[0] * s * gs.y, 0f);

            // A tall chest's window grows upward and opened over the bag (review, 2026-10-04). When the
            // board would overlap the bag's board, it and its slots drop to sit just below it. A chest
            // moved to the side in edit mode does not overlap, so it is left where it was put.
            if (gui.m_playerGrid?.m_gridRoot?.Find(InventoryBoard.Name) is RectTransform bag)
            {
                var bc = new Vector3[4]; var cc = new Vector3[4];
                bag.GetWorldCorners(bc); board.GetWorldCorners(cc);
                bool across = cc[0].x < bc[2].x && cc[2].x > bc[0].x;
                float gap = (bc[1].y - bc[0].y) * 0.02f;
                float drop = cc[1].y - (bc[0].y - gap);
                if (across && drop > 0f && cc[0].y < bc[1].y)
                {
                    board.position -= new Vector3(0f, drop, 0f);
                    float local = drop / gs.y;
                    for (int e = 0; e < w * h; e++)
                        if (elements[e]?.transform is RectTransform rt)
                            rt.anchoredPosition -= new Vector2(0f, local);
                }
            }
            Show(gui, true);
            // Every slot is on the board, so the window never scrolls; a bar only draws over the rails.
            // Every one under the window, not just the grid's own (review: grausten and wardrobe kept one).
            foreach (Scrollbar bar in panel.GetComponentsInChildren<Scrollbar>(false))
                bar.gameObject.SetActive(false);
            // Nor does it scroll: a tall chest's scroll view had moved its grid (the wardrobe opened over
            // the bag) and kept its own bar outside the window (grausten). Back to the top, then off.
            if (__instance.m_gridRoot.GetComponentInParent<ScrollRect>() is ScrollRect sr && sr.enabled)
            {
                sr.verticalNormalizedPosition = 1f;
                sr.horizontalNormalizedPosition = 0f;
                if (sr.verticalScrollbar != null) sr.verticalScrollbar.gameObject.SetActive(false);
                if (sr.horizontalScrollbar != null) sr.horizontalScrollbar.gameObject.SetActive(false);
                sr.enabled = false;
            }
            // Nor does it clip: the grid's scroll mask would cut off a chest laid out wider than vanilla's.
            for (Transform t = __instance.m_gridRoot; t != null && t != panel; t = t.parent)
            {
                if (t.GetComponent<Mask>() is Mask m && m.enabled) m.enabled = false;
                if (t.GetComponent<RectMask2D>() is RectMask2D m2 && m2.enabled) m2.enabled = false;
            }

            // The board replaces the window's frame; the object stays, it holds the window's clicks.
            Image frame = panel.Find("Bkg")?.GetComponent<Image>();
            if (frame != null && frame.enabled)
                frame.enabled = false;

            Header(gui, __instance, board, lay);

            if (s_said.Add(w + "x" + h))
                Log.LogInfo("chest board " + w + "x" + h + " assembled, slots on its painted cells");
        }

        /// <summary>
        /// Layout A on the band: the name centred, the buttons on slot-rim plates, two each side.
        /// The band inside the rails is five equal places: plate, plate, name, plate, plate.
        /// </summary>
        private static void Header(InventoryGui gui, InventoryGrid grid, RectTransform board, GridBoard.Layout lay)
        {
            var c = new Vector3[4];
            board.GetWorldCorners(c);
            float k = (c[2].x - c[0].x) / lay.Size.x;        // world per board px
            Rect band = Rect.MinMaxRect(c[0].x + 70f * k, c[1].y - (lay.Band.yMax - 6f) * k,
                                        c[0].x + (lay.Size.x - 70f) * k, c[1].y - (lay.Band.yMin + 6f) * k);

            // The game's two on the left in its order (Take all, Place stacks - m_takeAllButton and
            // m_stackAllButton); anything another mod adds (Waste Management's Sort, Sort All) on the
            // right. Split by where they stood, Place stacks landed right and Take all stretched.
            var left = new List<RectTransform>();
            var right = new List<RectTransform>();
            foreach (Button b in new[] { gui.m_takeAllButton, gui.m_stackAllButton })
                if (b != null && b.gameObject.activeInHierarchy)
                    left.Add((RectTransform)b.transform);
            foreach (Button b in gui.m_container.GetComponentsInChildren<Button>(false))
            {
                var rt = (RectTransform)b.transform;
                if (left.Contains(rt) || b.transform.IsChildOf(grid.m_gridRoot) || b.transform.IsChildOf(board) || rt.rect.width <= 1f)
                    continue;
                right.Add(rt);
            }
            right.Sort((a, b) => Centre(a).x.CompareTo(Centre(b).x));

            float gap = band.width * 0.012f;
            float slot = (band.width - 4f * gap) / 5f;
            float side = 2f * slot + gap;
            // Every button has an icon (the game's two and Waste Management's two): each is its
            // icon with its word beside it - Withdraw, Deposit, Sort, Sort All (Cartur, 2026-10-05,
            // Grok's option C on his own board). Anything else on the band keeps the text plates.
            bool icons = left.Concat(right).All(rt => Icon(gui, rt) != null);
            var fromRight = new List<RectTransform>(right);
            fromRight.Reverse();
            if (icons)
            {
                // The buttons fill the outer 27.8% of the band each side, the name the middle -
                // the plaque in Grok's mockup (out_chestC_1.png). Icon at the band's height.
                side = band.width * 0.278f - gap;
                float w = (side - gap) / 2f;
                // Centred on the open wood between the top rail and the first row of cells, not on
                // the band: the rail's inner edge is at board y 88 (board_grid_base.png, column 400)
                // and the cells start where the band ends, so the band's own centre sat low
                // (Cartur, 2026-10-05). Name included, so the row stays level.
                float midY = c[1].y - (HeaderTopPx + lay.Band.yMax) * 0.5f * k;
                band = new Rect(band.xMin, midY - band.height * 0.5f, band.width, band.height);
                Icons(gui, left, band, w);
                // fromRight is walked outermost first: Sort All outside, Sort beside the name.
                Icons(gui, fromRight, band, w);
                SameSize(left, right);   // one size for all four words, as on the text plates
                // A narrow chest has no room for the words - on the 5-wide wood chest they shrank to
                // dashes (pilot, 2026-10-05) - so there the buttons are just their icons (Cartur).
                bool words = WordsFit(left.Concat(right));
                foreach (RectTransform rt in left.Concat(right))
                    foreach (TMP_Text t in rt.GetComponentsInChildren<TMP_Text>(true))
                        if (!t.transform.parent.name.StartsWith("gamepad_hint"))
                            t.enabled = words;
            }
            else
            {
                Row(left, Rect.MinMaxRect(band.xMin, band.yMin, band.xMin + side, band.yMax), gap);
                Row(right, Rect.MinMaxRect(band.xMax - side, band.yMin, band.xMax, band.yMax), gap);
                SameSize(left, right);
            }

            TMP_Text title = gui.m_containerName;
            if (title != null)
            {
                var tr = title.rectTransform;
                Rect area = Rect.MinMaxRect(band.xMin + side + gap, band.yMin, band.xMax - side - gap, band.yMax);
                Vector3 ls = tr.lossyScale;
                if (ls.x > 0f && ls.y > 0f)
                {
                    tr.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, area.width / ls.x);
                    tr.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, area.height / ls.y);
                    tr.position += (Vector3)(area.center - (Vector2)Centre(tr));
                    title.enableAutoSizing = true;
                    title.fontSizeMin = 6f;
                    title.fontSizeMax = Mathf.Max(10f, area.height / ls.y * 0.8f);
                    title.alignment = TextAlignmentOptions.Center;
                    title.textWrappingMode = TextWrappingModes.NoWrap;
                    title.color = Gold;
                }
            }

            // Each pair spread over the wood from the rail to the name's own text, so the gaps
            // before, between and after them match and nothing is left as a hole beside the name
            // (Cartur, 2026-10-05: "space them better").
            if (icons)
            {
                float nameW = 0f;
                if (title != null && title.rectTransform.lossyScale.x > 0f)
                    nameW = title.GetPreferredValues(title.text).x * title.rectTransform.lossyScale.x;
                float half = Mathf.Max(0f, (band.width - nameW) * 0.5f);
                Spread(left, band.xMin, half, +1);
                Spread(fromRight, band.xMax, half, -1);
            }
        }

        /// <summary>Buttons side by side across one side of the band, each on a slot-rim plate.</summary>
        private static readonly Dictionary<string, Sprite> s_icons = new Dictionary<string, Sprite>();

        /// <summary>
        /// The icon asset for one of the band's four buttons (Grok's render, 2026-10-05, bronze
        /// matched to the board), or null. The game's two by reference; Waste Management's two by the
        /// names it gives its clones (cartur-waste-management Plugin.cs: CarturSort(All)ContainerButton).
        /// </summary>
        private static string IconName(InventoryGui gui, RectTransform rt)
        {
            Button b = rt.GetComponent<Button>();
            return b == null ? null
                : b == gui.m_takeAllButton ? "chest_icon_take"
                : b == gui.m_stackAllButton ? "chest_icon_stack"
                : rt.name == "CarturSortContainerButton" ? "chest_icon_sort"
                : rt.name == "CarturSortAllContainerButton" ? "chest_icon_sortall" : null;
        }

        private static readonly Dictionary<string, string> s_words = new Dictionary<string, string>
        {
            { "chest_icon_take", "Withdraw" }, { "chest_icon_stack", "Deposit" },
            { "chest_icon_sort", "Sort" }, { "chest_icon_sortall", "Sort All" },
        };

        private static Sprite Icon(InventoryGui gui, RectTransform rt)
        {
            string name = IconName(gui, rt);
            if (name == null)
                return null;
            if (s_icons.TryGetValue(name, out Sprite sp) && sp != null)
                return sp;
            Texture2D tex = AssetLoader.Board(name);
            if (tex == null)
                return null;
            sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sp.name = "cartur_" + name;
            return s_icons[name] = sp;
        }

        private const string IconChild = "CarturUI_Icon";

        /// <summary>
        /// Each button the band's height, centred on it: the icon square at the left, its word
        /// beside it. The button's own Image stays as a clear hit area so the whole width clicks.
        /// Spread places them across.
        /// </summary>
        private static void Icons(InventoryGui gui, List<RectTransform> buttons, Rect band, float w)
        {
            float s = band.height;
            for (int i = 0; i < buttons.Count; i++)
            {
                RectTransform rt = buttons[i];
                Vector3 k = rt.lossyScale;
                if (k.x <= 0f || k.y <= 0f)
                    continue;
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w / k.x);
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, s / k.y);
                rt.position += (Vector3)(new Vector2(Centre(rt).x, band.center.y) - (Vector2)Centre(rt));

                if (rt.GetComponent<Image>() is Image hit)
                {
                    hit.color = Color.clear;
                    var sel = rt.GetComponent<Selectable>();
                    if (sel != null && sel.transition == Selectable.Transition.SpriteSwap)
                        sel.transition = Selectable.Transition.ColorTint;
                }

                var icon = rt.Find(IconChild) as RectTransform;
                if (icon == null)
                {
                    var go = new GameObject(IconChild, typeof(RectTransform), typeof(Image));
                    icon = (RectTransform)go.transform;
                    icon.SetParent(rt, false);
                    var img = go.GetComponent<Image>();
                    img.sprite = Icon(gui, rt);
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    img.material = null;
                }
                icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0f, 0.5f);
                icon.anchoredPosition = Vector2.zero;
                icon.sizeDelta = new Vector2(s / k.x, s / k.y);

                foreach (TMP_Text t in rt.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t.transform.parent.name.StartsWith("gamepad_hint"))
                        continue;
                    var tr = t.rectTransform;
                    tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                    tr.offsetMin = new Vector2((s + s * IconGap) / k.x, 0f); tr.offsetMax = Vector2.zero;
                    t.text = s_words[IconName(gui, rt)];
                    t.enableAutoSizing = true;
                    t.fontSizeMin = 6f;
                    t.fontSizeMax = Mathf.Max(10f, s / k.y * 0.6f);
                    t.alignment = TextAlignmentOptions.Left;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                    t.color = Parchment;
                }
            }
        }

        /// <summary>
        /// Whether the words are readable at the one size SameSize gave them: each has room at all,
        /// and the size is at least 22% of the button's height. The 8-wide Grausten chest's words
        /// measured about 29% and read fine; the 6-wide barrel's passed 15% and still drew as
        /// dashes; on the 3-wide private chest the words had no width, SameSize skipped them and
        /// the old size said they fit (pilot screenshots, 2026-10-05).
        /// </summary>
        private static bool WordsFit(IEnumerable<RectTransform> buttons)
        {
            bool any = false;
            foreach (RectTransform rt in buttons)
                foreach (TMP_Text t in rt.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t.transform.parent.name.StartsWith("gamepad_hint"))
                        continue;
                    if (t.rectTransform.rect.width <= 0f || rt.rect.height <= 0f || t.fontSize < rt.rect.height * 0.22f)
                        return false;
                    any = true;
                }
            return any;
        }

        private const float HeaderTopPx = 88f;   // the top rail's inner edge, board px
        private const float IconGap = 0.25f;     // icon to word, in icon widths

        /// <summary>
        /// Each button cut to its icon plus its word, then spaced so the gaps before, between and
        /// after them across this side are equal. dir +1 lays them in from the left edge, -1 from
        /// the right.
        /// </summary>
        private static void Spread(List<RectTransform> buttons, float edge, float side, int dir)
        {
            var widths = new List<float>();
            float used = 0f;
            foreach (RectTransform rt in buttons)
            {
                float icon = rt.rect.height * rt.lossyScale.y, wd = icon;
                foreach (TMP_Text t in rt.GetComponentsInChildren<TMP_Text>(true))
                    if (t.enabled && !t.transform.parent.name.StartsWith("gamepad_hint"))
                        wd += icon * IconGap + t.GetPreferredValues(t.text).x * t.rectTransform.lossyScale.x;
                widths.Add(wd);
                used += wd;
            }
            float space = Mathf.Max(0f, side - used) / (buttons.Count + 1);
            float x = space;
            for (int i = 0; i < buttons.Count; i++)
            {
                RectTransform rt = buttons[i];
                Vector3 k = rt.lossyScale;
                if (k.x <= 0f)
                    continue;
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, widths[i] / k.x);
                float cx = edge + dir * (x + widths[i] * 0.5f);
                rt.position += (Vector3)(new Vector2(cx, Centre(rt).y) - (Vector2)Centre(rt));
                x += widths[i] + space;
            }
        }

        private static void Row(List<RectTransform> buttons, Rect side, float gap)
        {
            if (buttons.Count == 0)
                return;
            float w = (side.width - gap) / 2f;    // every plate the same width, one fifth of the band
            Sprite plate = GridBoard.Plate();
            for (int i = 0; i < buttons.Count; i++)
            {
                RectTransform rt = buttons[i];
                Vector3 k = rt.lossyScale;
                if (k.x <= 0f || k.y <= 0f)
                    continue;
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w / k.x);
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, side.height / k.y);
                var target = new Vector2(side.xMin + w * (i + 0.5f) + gap * i, side.center.y);
                rt.position += (Vector3)(target - (Vector2)Centre(rt));

                var img = rt.GetComponent<Image>();
                if (img != null && plate != null)
                {
                    img.sprite = plate;
                    img.type = Image.Type.Sliced;
                    // The rim draws about 8 units thick whatever the plate's size.
                    img.pixelsPerUnitMultiplier = GridBoard.CellBorderPx / 8f;
                    img.color = Color.white;
                    img.material = null;
                    var sel = rt.GetComponent<Selectable>();
                    if (sel != null && sel.transition == Selectable.Transition.SpriteSwap)
                        sel.transition = Selectable.Transition.ColorTint;
                }
                foreach (TMP_Text t in rt.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t.transform.parent.name.StartsWith("gamepad_hint")) continue;
                    var tr = t.rectTransform;
                    tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                    tr.offsetMin = new Vector2(6f, 2f); tr.offsetMax = new Vector2(-6f, -2f);
                    t.enableAutoSizing = true;
                    t.fontSizeMin = 6f;
                    t.fontSizeMax = Mathf.Max(10f, side.height / k.y * 0.7f);
                    t.alignment = TextAlignmentOptions.Center;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                    t.color = Parchment;
                }
            }
        }

        /// <summary>
        /// Every button label at one size - the size the longest word fits its plate at - so "Sort"
        /// is not bigger than "Place stacks" (review, 2026-10-04: private chest). Worked out from each
        /// word's preferred width at a reference size, as the crafting tabs are.
        /// </summary>
        private static void SameSize(List<RectTransform> a, List<RectTransform> b)
        {
            var labels = new List<TMP_Text>();
            foreach (RectTransform rt in a.Concat(b))
                foreach (TMP_Text t in rt.GetComponentsInChildren<TMP_Text>(true))
                    if (!t.transform.parent.name.StartsWith("gamepad_hint") && !string.IsNullOrEmpty(t.text))
                        labels.Add(t);
            if (labels.Count == 0)
                return;
            const float probe = 36f;
            float size = float.MaxValue;
            foreach (TMP_Text t in labels)
            {
                var r = t.rectTransform.rect;
                if (r.width <= 0f || r.height <= 0f) continue;
                t.enableAutoSizing = false;
                t.fontSize = probe;
                Vector2 want = t.GetPreferredValues(t.text);
                if (want.x > 0f) size = Mathf.Min(size, probe * r.width * 0.92f / want.x);
                if (want.y > 0f) size = Mathf.Min(size, probe * r.height * 0.8f / want.y);
            }
            if (size == float.MaxValue)
                return;
            foreach (TMP_Text t in labels)
            {
                t.enableAutoSizing = false;
                t.fontSize = size;
            }
        }

        /// <summary>
        /// A chest's weight readout belongs to carts and ships, where weight slows you down; on a
        /// standing chest it is noise (Cartur: "why do we have a weight icon on the chests").
        /// </summary>
        private static void Weight(InventoryGui gui)
        {
            Transform box = gui.m_containerWeight != null ? gui.m_containerWeight.transform.parent : null;
            if (box == null || box == gui.m_container)
                return;
            var container = s_current?.GetValue(gui) as Container;
            bool moves = container != null
                && (container.GetComponentInParent<Vagon>() != null || container.GetComponentInParent<Ship>() != null);
            if (box.gameObject.activeSelf != moves)
                box.gameObject.SetActive(moves);
        }

        private static void Show(InventoryGui gui, bool on)
        {
            Transform board = gui.m_container.Find(Name);
            if (board != null && board.gameObject.activeSelf != on)
                board.gameObject.SetActive(on);
            if (!on)
            {
                Image frame = gui.m_container.Find("Bkg")?.GetComponent<Image>();
                if (frame != null && !frame.enabled)
                    frame.enabled = true;
            }
        }

        private static RectTransform Root(RectTransform panel)
        {
            var rt = panel.Find(Name) as RectTransform;
            if (rt != null && rt.GetComponent<Image>() != null)
            {
                Object.Destroy(rt.gameObject);   // an older build's single-image board
                rt = null;
            }
            if (rt == null)
            {
                rt = (RectTransform)new GameObject(Name, typeof(RectTransform)).transform;
                rt.SetParent(panel, false);
            }
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.SetAsFirstSibling();   // behind the grid and the buttons
            return rt;
        }

        private static Vector3 Centre(RectTransform rt) => rt.TransformPoint(rt.rect.center);
    }
}
