using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// The inventory's first row is the hotbar. It holds the same eight bound items the bar at
    /// the bottom of the screen shows, so it is framed the same way: the bar panel goes behind
    /// it, and the rows below move down far enough to clear the rail.
    ///
    /// Vanilla places every cell itself, in InventoryGrid.UpdateGui:
    ///
    ///     anchoredPosition = base + (x * m_elementSpace, -y * m_elementSpace)
    ///
    /// inside m_gridRoot, and only when the grid is rebuilt - so a nudge applied in the postfix
    /// stays put and costs nothing per frame. The nudge is re-applied on every UpdateGui anyway,
    /// because a rebuild puts the cells back on vanilla's own line.
    ///
    /// The row's rect is measured off the cells themselves rather than worked out from the cell
    /// size and the pitch: those are two numbers that can disagree with what is on screen, and
    /// the corners of the first and last cell cannot.
    ///
    /// Only rows the player can see are moved. The hidden slot rows below them belong to
    /// EquipmentPanel and QuickSlots, which have re-parented those cells onto the panel and the
    /// food diamonds; moving them here would drag the equipment off its own panel.
    /// </summary>
    internal static class HotbarRow
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string PanelName = "CarturUI_HotbarRow";

        /// <summary>
        /// Clear air between the row and its rail, the same as the HUD bar's - which is the
        /// knot's own drawn size, because the ornament fills its whole corner block and a
        /// smaller pad leaves a cell sitting on it.
        /// </summary>
        private const float Pad = Hotbar.KnotUnits;

        /// <summary>A little daylight under the frame before the next row starts.</summary>
        private const float Air = 6f;

        /// <summary>
        /// How far the rows under the hotbar move down: enough that the frame's bottom edge
        /// clears the next row, and no more. The frame runs from Pad above row 0 to Pad below
        /// it, so its bottom sits at Pad + cell; the next row would naturally start at one
        /// pitch. Worked out from the grid's own cell and pitch rather than from numbers
        /// written here, and zero when the frame already fits in the gap.
        /// </summary>
        internal static float RowShift { get; private set; } = Pad;

        private static readonly FieldInfo s_elements = AccessTools.Field(typeof(InventoryGrid), "m_elements");

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        [HarmonyPostfix]
        private static void Frame(InventoryGrid __instance) => Lay(__instance);

        private static void Lay(InventoryGrid grid)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || grid != gui.m_playerGrid || Player.m_localPlayer == null)
                return;
            if (!(s_elements?.GetValue(grid) is List<InventoryElement> elements) || elements.Count == 0)
                return;

            int width = Slots.Width;
            if (elements.Count < width)
                return;

            // Everything below the hotbar drops by the rail, up to the last visible row. Past
            // that are the slot rows, which live on their own panel and are not ours to move.
            //
            // The target is worked out, never subtracted. UpdateGui does not always rebuild the
            // cells, so "move it down a bit" run twice walks the rows down the screen; vanilla's
            // own line is base + (x * space, -y * space), and element 0 is never moved, so it IS
            // the base. Writing the answer makes running this again a no-op.
            var head = elements[0]?.transform as RectTransform;
            if (head == null)
                return;
            float space = grid.m_elementSpace;

            if (Boards)
            {
                LayOnBoards(grid, gui, elements, width, head, space);
                return;
            }

            Vector2 origin = head.anchoredPosition;
            RowShift = Mathf.Max(0f, Pad + head.rect.height + Air - space);

            int last = Mathf.Min(elements.Count, Slots.VisibleCells);
            for (int i = width; i < last; i++)
            {
                var rt = elements[i]?.transform as RectTransform;
                if (rt == null)
                    continue;
                rt.anchoredPosition = new Vector2(
                    origin.x + (i % width) * space,
                    origin.y - (i / width) * space - RowShift);
            }

            Place(grid, elements, width);
        }

        // ---- Cartur's boards (2026-10-03) ---------------------------------------------------
        //
        // The hotbar has its own board on the HUD and the bag has its own board under it. The
        // inventory's first row IS the hotbar's eight items, so it is not drawn in the bag at all:
        // while the bag is open its cells are laid invisibly over the hotbar board's boxes
        // (QuickSlots.HostOn) - the hotbar keeps drawing the icons, the cells take the clicks, the
        // drags and the drops. The bag board holds rows two onward. Cartur's call: "Hotbar board
        // is row 1", and the bag "opens under the hotbar lined up".

        internal static bool Boards => InventoryBoard.Active && Hotbar.BoardActive;

        /// <summary>Height of the visible grid, for whoever sizes the grid root.</summary>
        internal static float GridHeight(InventoryGrid grid) =>
            Boards ? (Slots.VisibleRows - 1) * InventoryBoard.RowPitch(grid.m_elementSpace)
                   : Slots.VisibleRows * grid.m_elementSpace + RowShift;

        // Vanilla's own origin for the grid - where it put element 0 - taken while element 0 is
        // still in the grid. Once that cell has been lifted onto the hotbar its position means
        // nothing here, and "move it down a bit" from a moved cell would walk the rows.
        private static Vector2 s_origin;

        private static void LayOnBoards(InventoryGrid grid, InventoryGui gui, List<InventoryElement> elements,
                                        int width, RectTransform head, float space)
        {
            if (head.parent == grid.m_gridRoot)
                s_origin = head.anchoredPosition;

            // A grid one row shorter than vanilla thinks, and a panel to match.
            RowShift = -space;

            for (int c = 0; c < width && c < elements.Count; c++)
            {
                RectTransform box = Hotbar.SlotBox(c);
                if (box != null)
                    QuickSlots.HostOn(elements[c], box);
            }

            float rowPitch = InventoryBoard.RowPitch(space);
            int last = Mathf.Min(elements.Count, Slots.VisibleCells);
            for (int i = width; i < last; i++)
            {
                var rt = elements[i]?.transform as RectTransform;
                if (rt == null)
                    continue;
                rt.anchoredPosition = new Vector2(
                    s_origin.x + (i % width) * space,
                    s_origin.y - (i / width - 1) * rowPitch);
                // The board paints every cell, so the cell's own frame drew a second box inside it
                // (Cartur, 2026-10-04: "the main inventory looks doubled").
                if (rt.GetComponent<Image>()?.sprite != null)
                    EquipmentPanel.Bare(rt.gameObject);
            }

            var first = elements[width]?.transform as RectTransform;
            var second = elements.Count > width + 1 ? elements[width + 1]?.transform as RectTransform : null;
            InventoryBoard.Place(grid, first, Slots.VisibleRows - 1);
            // Placed under the hotbar once, if it is still where the old layout left it, then left to
            // edit mode - see InventoryBoard.Follow.
            InventoryBoard.Follow(gui, first, second);
            Retire(grid, gui);
        }

        /// <summary>
        /// What the board replaces: this row's own bar, and the panel's old frame. The frame's
        /// object stays - edit mode hit-tests against its rect - only its picture goes.
        /// </summary>
        private static void Retire(InventoryGrid grid, InventoryGui gui)
        {
            Transform bar = grid.m_gridRoot != null ? grid.m_gridRoot.Find(PanelName) : null;
            if (bar != null && bar.gameObject.activeSelf)
                bar.gameObject.SetActive(false);
            Image frame = gui.m_player != null ? gui.m_player.Find("Bkg")?.GetComponent<Image>() : null;
            if (frame == null)
                return;
            if (frame.enabled)
                frame.enabled = false;

            // Edit mode grabs the inventory by this rect (InventoryScreen.Register, read live by
            // HudLayout), and it was still the old frame's - one row shorter and above the board -
            // so the board could not be dragged (Cartur: "I can't move the inventory panel").
            // It is laid over the board instead.
            var board = grid.m_gridRoot != null ? grid.m_gridRoot.Find(InventoryBoard.Name) as RectTransform : null;
            if (board == null)
                return;
            var hit = (RectTransform)frame.transform;
            var parent = (RectTransform)hit.parent;
            var c = new Vector3[4];
            board.GetWorldCorners(c);
            Vector2 lo = parent.InverseTransformPoint(c[0]), hi = parent.InverseTransformPoint(c[2]);
            hit.anchorMin = hit.anchorMax = new Vector2(0.5f, 0.5f);
            hit.pivot = new Vector2(0.5f, 0.5f);
            Vector2 mid = (lo + hi) * 0.5f - parent.rect.center;
            hit.anchoredPosition = mid;
            hit.sizeDelta = hi - lo;
        }

        private static void Place(InventoryGrid grid, List<InventoryElement> elements, int width)
        {
            var first = elements[0]?.transform as RectTransform;
            var last = elements[width - 1]?.transform as RectTransform;
            if (first == null || last == null || grid.m_gridRoot == null)
                return;

            Canvas canvas = grid.GetComponentInParent<Canvas>();
            Sprite bar = AssetLoader.Piece("panel_ornate_bar", canvas != null ? canvas.referencePixelsPerUnit : 100f);
            if (bar == null)
                return;

            Transform found = grid.m_gridRoot.Find(PanelName);
            Image img = found != null ? found.GetComponent<Image>() : null;
            if (img == null)
            {
                var go = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(grid.m_gridRoot, false);
                img = go.GetComponent<Image>();
                img.raycastTarget = false;
            }

            img.sprite = bar;
            img.type = Image.Type.Sliced;
            img.color = Color.white;

            // Same lever the HUD bar uses: the knot draws 40 units on a panel, which is most of
            // a 64 unit row, so it is brought down to the bar's own figure.
            float knot = AssetLoader.PieceBorderUnits("panel_ornate_bar");
            if (knot > 0f)
                img.pixelsPerUnitMultiplier = knot / Hotbar.KnotUnits;

            // The cells carry the element prefab's pivot, which is its top-left corner.
            var rt = (RectTransform)img.transform;
            rt.anchorMin = first.anchorMin;
            rt.anchorMax = first.anchorMax;
            rt.pivot = first.pivot;
            float left = first.anchoredPosition.x;
            float right = last.anchoredPosition.x + last.rect.width;
            float top = first.anchoredPosition.y;
            rt.sizeDelta = new Vector2(right - left + Pad * 2f, first.rect.height + Pad * 2f);
            rt.anchoredPosition = new Vector2(left - Pad, top + Pad);
            rt.localScale = Vector3.one;
            rt.SetAsFirstSibling();   // behind the cells
        }
    }
}
