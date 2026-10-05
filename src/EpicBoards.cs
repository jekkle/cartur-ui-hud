using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Epic Loot's two windows on Cartur's boards (2026-10-04, Grok renders he picked, colour-matched
    /// to the store board): the trader's MerchantPanel (board_adventure.png) and the enchanting table
    /// (board_enchant.png). Both boards were drawn from guides built out of the panels' own dumped
    /// layout, so with the board's frame laid exactly over the panel, every painted box is under its
    /// control - the frame is the board minus the medallions that stand out above and below it.
    /// Epic Loot's light-wood panel, its list backgrounds and its button plates stop drawing; the
    /// buttons stay clickable.
    /// </summary>
    internal static class EpicBoards
    {
        // board px: the frame inside each board (medallions overhang it top and bottom).
        private static readonly RectInt AdventureFrame = new RectInt(4, 17, 1221, 766);

        internal static void Merchant(Transform storeScreen)
        {
            var panel = storeScreen?.Find("MerchantPanel") as RectTransform;
            if (panel == null || !Back(panel, "adventure", AdventureFrame))
                return;
            foreach (Image img in panel.GetComponentsInChildren<Image>(true))
                if (Plate(img))
                    img.color = Color.clear;

            // Titles and labels onto their painted plates. The board was drawn from a guide built out of
            // this panel's dumped layout (screen px, panel (798,460)..(2293,1393) at 2560x1600), so the
            // plates are given here in those same numbers and mapped through the panel's own rect.
            Seat(panel, "SecretStash/Title", 833, 1300, 1180, 1378);
            Seat(panel, "Gamble/Title", 1193, 1300, 1540, 1378);
            Seat(panel, "TreasureMap/Title", 1553, 1300, 1900, 1378);
            Seat(panel, "Bounties/Title", 1913, 1300, 2260, 1378);
            Seat(panel, "Bounties/AvailableLabel", 1913, 1236, 2260, 1282);
            Seat(panel, "Bounties/ClaimLabel", 1913, 876, 2260, 918);
            Rows(panel);
        }

        private const float DumpX0 = 798f, DumpY1 = 1393f, DumpW = 2293f - 798f, DumpH = 1393f - 460f;

        private static void Seat(RectTransform panel, string path, float x0, float y0, float x1, float y1)
        {
            var rt = panel.Find(path) as RectTransform;
            if (rt == null)
                return;
            var c = new Vector3[4];
            panel.GetWorldCorners(c);
            float W = c[2].x - c[0].x, H = c[2].y - c[0].y;
            var target = Rect.MinMaxRect(c[0].x + (x0 - DumpX0) / DumpW * W, c[1].y - (DumpY1 - y0) / DumpH * H,
                                         c[0].x + (x1 - DumpX0) / DumpW * W, c[1].y - (DumpY1 - y1) / DumpH * H);
            Vector3 k = rt.lossyScale;
            if (k.x <= 0f || k.y <= 0f)
                return;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, target.width / k.x);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, target.height / k.y);
            rt.position += (Vector3)(target.center - (Vector2)rt.TransformPoint(rt.rect.center));
            foreach (TMPro.TMP_Text t in rt.GetComponents<TMPro.TMP_Text>())
                t.alignment = TMPro.TextAlignmentOptions.Center;
        }

        /// <summary>
        /// Epic Loot's list rows draw a light grey plate each; on the board's wells they read as a
        /// mess. The plate goes, the selection is the soft gold the crafting list uses. Rows are rebuilt
        /// as the lists refresh, so this is called again while the panel is up.
        /// </summary>
        internal static void Rows(Transform panel)
        {
            foreach (Image img in panel.GetComponentsInChildren<Image>(false))
            {
                // Selectable, not Button: the trader's rows hang off a Button, the enchanting table's
                // ItemElement is a bare Selectable (pilot dump, 2026-10-05). Button is a Selectable.
                if (img.sprite == null || img.transform.parent == null || img.transform.parent.GetComponent<Selectable>() == null)
                    continue;
                if (img.name == "Background" && img.color.a > 0f)
                    img.color = Color.clear;
                else if (img.name == "Selected" && img.color != Selection)
                    img.color = Selection;
            }
        }

        private static readonly Color Selection = new Color(1f, 0.8f, 0.35f, 0.45f);

        /// <summary>
        /// One board per tab layout (2026-10-05, Grok renders from enchant_guides.py, which drew each
        /// tab's guide from that tab's dumped rects). Keyed by the TabContent child the tab switches
        /// on; frame = the knotwork rectangle in board px, measured off each cut PNG (rows and
        /// columns over 90% opaque), the medallions overhanging it.
        ///
        /// tabs = the painted tab boxes' left and right rims, in the dump's screen px (panel
        /// 533.33..2026.67 at 2560 wide). Grok painted the column slightly differently on each board,
        /// so it is per board: measured off each PNG through its frame, and on the six boards a pilot
        /// shot exists for, the same numbers came off the screenshot within 2 px.
        /// </summary>
        private static readonly Dictionary<string, (string board, RectInt frame, float tabL, float tabR)> EnchantTabs =
            new Dictionary<string, (string, RectInt, float, float)>
            {
                ["SacrificeContent"] = ("enchant_sacrifice", new RectInt(4, 24, 1665, 1043), 573, 723),
                ["ConvertContent"] = ("enchant_convert", new RectInt(1, 26, 1666, 1040), 577, 734),
                ["EnchantContent"] = ("enchant_enchant", new RectInt(2, 26, 1664, 1045), 577, 757),
                ["AugmentContent"] = ("enchant_augment", new RectInt(1, 26, 1666, 1042), 582, 746),
                ["DisenchantContent"] = ("enchant_disenchant", new RectInt(1, 26, 1666, 1039), 577, 735),
                ["RuneContent"] = ("enchant_rune", new RectInt(4, 27, 1666, 1040), 579, 742),
                ["UpgradeContent"] = ("enchant_upgrade", new RectInt(2, 26, 1665, 1040), 576, 737),
            };

        /// <summary>
        /// Epic Loot centres each tab's icon and label on its button (577..760, centre 668), which is
        /// wider than and right of every painted box - the icons sat up to 20 px off centre and "Convert
        /// Materials" ran past the rim. Both move to the box's centre; a label wider than the box
        /// shrinks to fit. The lock icon is a child of Image (dump), so it moves with it.
        /// </summary>
        private static void CentreTabs(RectTransform panel, float tabL, float tabR)
        {
            Transform tabs = panel.Find("Tabs");
            if (tabs == null)
                return;
            var c = new Vector3[4];
            panel.GetWorldCorners(c);
            float k = (c[2].x - c[0].x) / (2026.67f - 533.33f);
            float left = c[0].x + (tabL - 533.33f) * k, right = c[0].x + (tabR - 533.33f) * k;
            foreach (Button b in tabs.GetComponentsInChildren<Button>(true))
            {
                if (b.transform.Find("Text") is RectTransform label && label.GetComponent<Text>() is Text text)
                {
                    float room = (right - left - 12f * k) / label.lossyScale.x;
                    if (label.rect.width > room)
                    {
                        label.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, room);
                        text.resizeTextForBestFit = true;
                        text.resizeTextMaxSize = text.fontSize;
                        text.resizeTextMinSize = Mathf.Max(6, text.fontSize / 2);
                    }
                }
                foreach (string part in new[] { "Image", "Text" })
                    if (b.transform.Find(part) is RectTransform rt)
                        rt.position += new Vector3((left + right) * 0.5f - rt.TransformPoint(rt.rect.center).x, 0f, 0f);
            }
        }

        /// <summary>The tab's board on, the others off. Cheap enough per frame: seven children.</summary>
        internal static void EnchantingTab(Transform ui)
        {
            var panel = ui?.Find("Panel") as RectTransform;
            Transform content = panel?.Find("TabContent");
            if (content == null)
                return;
            foreach (Transform tab in content)
            {
                if (!tab.gameObject.activeSelf || !EnchantTabs.TryGetValue(tab.name, out var b))
                    continue;
                // Once per tab change. Not "is its board showing": Enchanting puts the Sacrifice board up
                // on Awake, and that check skipped the first tab's centring and knots.
                if (tab.name == s_tab)
                    return;
                s_tab = tab.name;
                string want = "CarturUI_Board_" + b.board;
                Transform shown = panel.Find(want);
                foreach (Transform ch in panel)
                    if (ch.name.StartsWith("CarturUI_Board_"))
                        ch.gameObject.SetActive(ch.name == want);
                if (shown != null)
                    shown.gameObject.SetActive(true);
                Back(panel, b.board, b.frame);
                CentreTabs(panel, b.tabL, b.tabR);
                ClearKnots(tab);
                return;
            }
        }

        /// <summary>
        /// The knot ornament at each end of a painted plate is 40 of the plate's 312 board px - measured
        /// on board_enchant_sacrifice.png, first mode plate. Epic Loot puts a mode toggle's check box
        /// 17-38 px into a 280 px toggle, which is on the knot; the box moves just past it and the label
        /// is kept between the box and the right-hand knot.
        /// </summary>
        private const float Knot = 40f / 312f;

        private static void ClearKnots(Transform tab)
        {
            foreach (Toggle t in tab.GetComponentsInChildren<Toggle>(true))
            {
                if (t.transform.parent == null || t.transform.parent.name != "ModeSelectors")
                    continue;
                var rt = (RectTransform)t.transform;
                var box = rt.Find("Background") as RectTransform;
                if (box == null)
                    continue;
                float knot = rt.rect.width * Knot;
                float boxLeft = rt.InverseTransformPoint(box.TransformPoint(new Vector3(box.rect.xMin, 0f))).x;
                float dx = rt.rect.xMin + knot - boxLeft;
                if (dx > 0.5f)
                    box.position += rt.TransformVector(new Vector3(dx, 0f, 0f));

                var label = rt.Find("Text") as RectTransform;
                if (label == null)
                    continue;
                float boxRight = rt.InverseTransformPoint(box.TransformPoint(new Vector3(box.rect.xMax, 0f))).x;
                float left = boxRight + 4f, right = rt.rect.xMax - knot;
                float labelLeft = rt.InverseTransformPoint(label.TransformPoint(new Vector3(label.rect.xMin, 0f))).x;
                float labelRight = rt.InverseTransformPoint(label.TransformPoint(new Vector3(label.rect.xMax, 0f))).x;
                if (labelLeft >= left - 0.5f && labelRight <= right + 0.5f)
                    continue;
                float scale = label.lossyScale.x / rt.lossyScale.x;
                label.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, (right - left) / scale);
                float centre = rt.InverseTransformPoint(label.TransformPoint(label.rect.center)).x;
                label.position += rt.TransformVector(new Vector3((left + right) * 0.5f - centre, 0f, 0f));
            }
        }

        private static string s_tab;

        internal static void Enchanting(Transform ui)
        {
            s_tab = null;
            var panel = ui?.Find("Panel") as RectTransform;
            if (panel == null || !Back(panel, "enchant_sacrifice", EnchantTabs["SacrificeContent"].frame))
                return;
            Transform tabs = panel.Find("Tabs");
            if (tabs?.GetComponent<Image>() is Image tabBack)
                tabBack.color = Color.clear;
            foreach (Image img in panel.GetComponentsInChildren<Image>(true))
                if (Plate(img))
                    img.color = Color.clear;
        }

        /// <summary>A background Epic Loot draws that the board now paints: a list well, a field, a button plate.</summary>
        private static bool Plate(Image img)
        {
            if (img.name.StartsWith("CarturUI_") || img.sprite == null)
                return false;
            string s = img.sprite.name;
            bool well = img.name == "Panel" || img.name.EndsWith("BountiesPanel") || img.name == "ItemList"
                        || img.name == "FilterInput" || img.name == "SortDropdown";
            bool button = img.GetComponent<Button>() != null || img.GetComponent<Toggle>() != null;
            return (well || button) && (s.StartsWith("cartur_") || s == "button" || s == "item_background");
        }

        /// <summary>The board behind <paramref name="panel"/>, its frame over the panel's rect; the panel's own art off.</summary>
        private static bool Back(RectTransform panel, string board, RectInt frame)
        {
            Texture2D tex = AssetLoader.Board(board);
            if (tex == null || panel.rect.width <= 0f)
                return false;
            // Off, not clear: AutoBoard stands down only on a frame that is switched off.
            if (panel.GetComponent<Image>() is Image own)
                own.enabled = false;

            string name = "CarturUI_Board_" + board;
            var rt = panel.Find(name) as RectTransform;
            if (rt == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                rt = (RectTransform)go.transform;
                rt.SetParent(panel, false);
                var img = go.GetComponent<Image>();
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f,
                                           0, SpriteMeshType.FullRect);
                img.raycastTarget = false;
                img.material = null;   // full brightness - see valheim-litpanel-material
            }
            rt.SetAsFirstSibling();
            // panel units per board px, from the frame onto the panel rect
            float sx = panel.rect.width / frame.width, sy = panel.rect.height / frame.height;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(tex.width * sx, tex.height * sy);
            rt.anchoredPosition = new Vector2(-frame.x * sx, frame.y * sy);
            return true;
        }
    }
}
