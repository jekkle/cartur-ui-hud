using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// The hotbar. HotkeyBar rebuilds its slots from m_elementPrefab whenever the bound item
    /// count changes and places them by localPosition, so the prefab is skinned once and the
    /// bar as a whole is registered movable and scalable - localScale is safe here because
    /// nothing inside is positioned by the game in canvas units.
    /// </summary>
    internal static class Hotbar
    {
        internal const string Owner = "hotbar";

        internal static BepInEx.Logging.ManualLogSource Log;

        [HarmonyPatch(typeof(Hud), "Awake")]
        [HarmonyPostfix]
        private static void Awake(Hud __instance)
        {
            Transform bar = __instance.transform.Find("hudroot/HotKeyBar");
            if (bar == null)
            {
                Log.LogWarning("hudroot/HotKeyBar not found - hotbar left vanilla");
                return;
            }

            Canvas canvas = __instance.GetComponentInParent<Canvas>();
            float ppu = canvas != null ? canvas.referencePixelsPerUnit : 100f;

            Skin.Apply(bar, "hotbar", ppu);
            if (!BoardActive)
                Back((RectTransform)bar, ppu);
            HotkeyBar hotkeys = bar.GetComponent<HotkeyBar>();
            Skin.Apply(hotkeys?.m_elementPrefab?.transform, "hotbar element prefab", ppu);

            // Reset with the HUD: a second world load builds new objects, and a latched flag
            // from the last one would leave the new bar unmatched.
            s_bar = hotkeys;
            s_matched = false;
            s_board = null;
            s_slots = null;
            Match();
            PlaceBoard();

            HudLayout.Reset(Owner, null);

            var rt = (RectTransform)bar;
            HudLayout.Register(Owner, "hotbar", "Hotbar", rt, rt, rt.anchoredPosition);
        }

        // ---- the hotbar and the bag's top row are the same eight slots, so they are the same
        // size ----------------------------------------------------------------------------

        private static HotkeyBar s_bar;
        private static bool s_matched;

        /// <summary>
        /// The bar and the inventory's first row show the same eight items, and looked like two
        /// different sizes: the hotbar carries its own cell prefab and its own spacing, neither
        /// of which has to agree with the grid's.
        ///
        /// Both numbers are copied off the grid rather than written down here, so they follow
        /// it - including under another mod that changes the grid's pitch. The bar's own rect
        /// is scaled by the same ratios, because HotkeyBar never touches it (UpdateIcons only
        /// sets each element's localPosition from m_elementSpace) and the panel behind the bar
        /// is stretched to it.
        ///
        /// Called from both Awakes because the order between Hud and InventoryGui is not ours
        /// to decide; it no-ops until both are up, then runs once.
        /// </summary>
        internal static void Match()
        {
            if (s_matched || s_bar == null)
                return;
            InventoryGrid grid = InventoryGui.instance?.m_playerGrid;
            if (grid == null || grid.m_elementPrefab == null || s_bar.m_elementPrefab == null)
                return;

            var gridCell = grid.m_elementPrefab.transform as RectTransform;
            var barCell = s_bar.m_elementPrefab.transform as RectTransform;
            if (gridCell == null || barCell == null)
                return;

            s_matched = true;

            float oldSpace = s_bar.m_elementSpace;
            Vector2 oldCell = barCell.sizeDelta;

            s_bar.m_elementSpace = grid.m_elementSpace;
            barCell.sizeDelta = gridCell.sizeDelta;

            var rt = (RectTransform)s_bar.transform;
            Vector2 was = rt.sizeDelta;
            if (oldSpace > 0f && oldCell.y > 0f)
                rt.sizeDelta = new Vector2(was.x * (grid.m_elementSpace / oldSpace),
                                           was.y * (gridCell.sizeDelta.y / oldCell.y));

            Log.LogInfo("hotbar matched to the grid: spacing " + oldSpace + " -> " + grid.m_elementSpace
                + ", cell " + oldCell + " -> " + gridCell.sizeDelta
                + ", bar " + was + " -> " + rt.sizeDelta);

            // The first run of this said 70 -> 70 and 64 -> 64: the pitch and the cell were
            // already the same, so whatever reads as smaller on the bar is inside the cell, not
            // the cell itself. These two lines are what will say which part it is.
            Log.LogInfo("hotbar cell parts: " + Parts(barCell));
            Log.LogInfo("grid cell parts:   " + Parts(gridCell));

            // The spacing just changed, and the board's boxes follow it.
            PlaceBoard();
        }

        // ---- Cartur's hotbar board (Assets/board_hotbar.png, 2026-10-03) ---------------------
        //
        // His picture behind the bar, its eight painted boxes on the eight slots. Measured off the
        // PNG (1684x425) by the gaps between its boxes and checked by drawing them back on the art
        // (tools/art/out/hotbar_boxes.png): 154 px apart, box 0 centred at (308, 196).
        //
        // Where a slot is, is the game's own rule, read from HotkeyBar.UpdateIcons with Cecil:
        // element i is put at localPosition (i * m_elementSpace, 0) in the bar. So the board and the
        // slot boxes below are placed from that rule and the prefab's pivot, not from live cells -
        // the bar only builds as many cells as there are bound items, and may have none.

        private const string BoardName = "CarturUI_HotbarBoard";
        private const string SlotsName = "CarturUI_HotbarSlots";
        private const float BoxPitchPx = 154f;
        private static readonly Vector2 Box0Px = new Vector2(308f, 196f);
        private const float Gap = 6f;   // between the hotbar board and the bag board under it

        internal static bool BoardActive => AssetLoader.Board("hotbar") != null;

        private static RectTransform s_board;
        private static RectTransform s_slots;

        /// <summary>The hotbar board's rect, or null when it is not in use.</summary>
        internal static RectTransform BoardRect => s_board;

        /// <summary>
        /// An invisible box over hotbar slot <paramref name="index"/>, the size of a slot. The
        /// inventory's first row is laid on these while the bag is open (HotbarRow), so dragging to
        /// and from the hotbar board is the game's own drag and drop on its own cells.
        /// </summary>
        internal static RectTransform SlotBox(int index)
        {
            if (s_slots == null || index < 0 || index >= s_slots.childCount)
                return null;
            return s_slots.GetChild(index) as RectTransform;
        }

        /// <summary>World-space room the bag board must leave under the hotbar board.</summary>
        internal static float BoardGap => Gap;

        private static Vector3 SlotCentre(int index)
        {
            var prefab = s_bar.m_elementPrefab.transform as RectTransform;
            Vector2 size = prefab != null ? prefab.sizeDelta : new Vector2(64f, 64f);
            Vector2 pivot = prefab != null ? prefab.pivot : new Vector2(0.5f, 0.5f);
            return new Vector3(index * s_bar.m_elementSpace + (0.5f - pivot.x) * size.x,
                               (0.5f - pivot.y) * size.y, 0f);
        }

        private static void PlaceBoard()
        {
            if (s_bar == null || !BoardActive || s_bar.m_elementPrefab == null)
                return;
            var bar = (RectTransform)s_bar.transform;
            Texture2D tex = AssetLoader.Board("hotbar");
            float s = s_bar.m_elementSpace / BoxPitchPx;   // canvas units per source px

            if (s_board == null)
            {
                var go = new GameObject(BoardName, typeof(RectTransform), typeof(Image));
                s_board = (RectTransform)go.transform;
                s_board.SetParent(bar, false);
                Image img = go.GetComponent<Image>();
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                           new Vector2(0f, 1f), 100f, 0, SpriteMeshType.FullRect);
                img.type = Image.Type.Simple;
                img.raycastTarget = false;
                img.material = null;   // full brightness - see valheim-litpanel-material
            }
            s_board.anchorMin = s_board.anchorMax = bar.pivot;
            s_board.pivot = new Vector2(0f, 1f);
            s_board.sizeDelta = new Vector2(tex.width * s, tex.height * s);
            s_board.localPosition = SlotCentre(0) + new Vector3(-Box0Px.x * s, Box0Px.y * s, 0f);
            s_board.localScale = Vector3.one;
            s_board.SetAsFirstSibling();   // behind the cells

            if (s_slots == null)
            {
                var go = new GameObject(SlotsName, typeof(RectTransform));
                s_slots = (RectTransform)go.transform;
                s_slots.SetParent(bar, false);
                for (int i = 0; i < 8; i++)
                    new GameObject("Slot" + i, typeof(RectTransform)).transform.SetParent(s_slots, false);
            }
            s_slots.anchorMin = s_slots.anchorMax = bar.pivot;
            s_slots.pivot = new Vector2(0.5f, 0.5f);
            s_slots.sizeDelta = Vector2.zero;
            s_slots.localPosition = Vector3.zero;
            var cell = (RectTransform)s_bar.m_elementPrefab.transform;
            for (int i = 0; i < s_slots.childCount; i++)
            {
                var rt = (RectTransform)s_slots.GetChild(i);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = cell.sizeDelta;
                rt.localPosition = SlotCentre(i);
            }
            s_slots.SetAsLastSibling();   // over the bar's own cells

            // The bar's old 9-slice panel, if a previous run built one, is what this replaces.
            Transform back = bar.Find(BackName);
            if (back != null)
                back.gameObject.SetActive(false);
            Log.LogInfo("hotbar board placed: " + tex.width + "x" + tex.height + " px at "
                + s.ToString("0.####") + " units/px, box pitch " + s_bar.m_elementSpace);
        }


        /// <summary>Each direct child of a cell prefab with its rect, for comparing the two.</summary>
        private static string Parts(RectTransform cell)
        {
            var sb = new System.Text.StringBuilder(cell.rect.size.ToString());
            foreach (Transform child in cell)
            {
                var rt = child as RectTransform;
                if (rt == null)
                    continue;
                sb.Append("  ").Append(child.name).Append(rt.rect.size)
                  .Append(rt.localScale.x != 1f ? "x" + rt.localScale.x.ToString("0.##") : "");
            }
            return sb.ToString();
        }

        /// <summary>The other half of the pair - see Match.</summary>
        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        [HarmonyPostfix]
        private static void GuiAwake() => Match();

        private const string BackName = "CarturUI_HotbarBack";

        // Clear air between the panel's rule and the outer cells. It is the knot's own drawn
        // size, and that is not a taste call: measured on refs_frame.png, the corner ornament
        // fills its whole 57px block - it reaches 56 x 54 of it - so anything less than the
        // knot leaves a cell sitting on the ornament. At 10 against a 26 unit knot the first
        // cell overlapped it by 16, and an item's durability bar was drawn across the corner.
        private const float Pad = KnotUnits;

        // What the corner ornament should measure on the bar. The bar is 64 units tall, and the
        // piece draws its 88px corner at 40 units, which is nearly all of that - so the knots
        // are scaled down here rather than the art being cut again. 26 leaves the rule reading
        // as a frame with the cells still the thing you look at.
        internal const float KnotUnits = 26f;

        /// <summary>
        /// The hotbar has no panel of its own in vanilla - the cells sit on the world. This
        /// adds one, from the same 9-slice as the inventory panels, stretched to the bar and
        /// sitting behind every cell.
        /// </summary>
        private static void Back(RectTransform bar, float ppu)
        {
            if (bar == null || bar.Find(BackName) != null)
                return;

            Sprite panel = AssetLoader.Piece("panel_ornate_bar", ppu);
            if (panel == null)
            {
                Log.LogWarning("panel_ornate_bar.png missing - hotbar left without a panel");
                return;
            }

            var go = new GameObject(BackName, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(bar, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-Pad, -Pad);
            rt.offsetMax = new Vector2(Pad, Pad);
            rt.SetAsFirstSibling();   // behind the cells

            Image image = go.GetComponent<Image>();
            image.sprite = panel;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;

            // Filled, because the bar piece carries a fill cut to this shape - 36px of it, not
            // 967 - so the grain lands at about life size instead of streaking. Frame-only was
            // the first answer to that squash and Cartur read it as an empty bar.
            image.fillCenter = true;

            // The multiplier divides the drawn border, so this is "draw the 40-unit knot at 26".
            float knot = AssetLoader.PieceBorderUnits("panel_ornate_bar");
            if (knot > 0f)
                image.pixelsPerUnitMultiplier = knot / KnotUnits;

            Log.LogInfo("hotbar panel added: corner " + knot.ToString("0.#") + " units drawn at "
                + KnotUnits + ", pad " + Pad);
        }
    }
}
