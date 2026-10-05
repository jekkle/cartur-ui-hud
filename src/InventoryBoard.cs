using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// The player inventory drawn on Cartur's grid board (GridBoard, the same board as the chests,
    /// 2026-10-04), with the real slots set into its cells. The board is assembled for the pack's
    /// row count, so it grows with the pack and no cell, rail or corner is ever resized.
    ///
    /// The board's cells are 186.2 px apart across and 178 px down - slightly wider than tall -
    /// while the game's slots are square. So the columns set the scale (one column is
    /// m_elementSpace) and the rows are spaced to the art. Cartur's call: space the slots to the
    /// art rather than stretch the art.
    /// </summary>
    internal static class InventoryBoard
    {
        internal const string Name = "CarturUI_InventoryBoard";

        // 2026-10-04: the bag is on Cartur's grid board, the same board the chests use, assembled
        // for its row count by GridBoard (board_grid.png; the earlier boards are in tools/art/out/old).
        private static float ColPitchPx => GridBoard.ColPitchPx;
        private static float RowPitchPx => GridBoard.RowPitchPx;

        internal static bool Active => GridBoard.Active;

        /// <summary>Distance between rows of real slots, in canvas units, for a grid of this column pitch.</summary>
        internal static float RowPitch(float columnPitch) =>
            Active ? columnPitch * RowPitchPx / ColPitchPx : columnPitch;

        /// <summary>
        /// Lays the board behind the grid so its cell (0,0) is centred on element 0. Runs from the
        /// grid's UpdateGui postfix, so it follows the row count whenever the game rebuilds.
        /// </summary>
        internal static void Place(InventoryGrid grid, RectTransform first, int rows)
        {
            if (!Active || grid.m_gridRoot == null || first == null || rows < 2)
                return;

            float s = grid.m_elementSpace / ColPitchPx;   // canvas units per board px
            RectTransform board = Root(grid.m_gridRoot, first);
            // Bag row r is inventory row r + 1: row 0 is the hotbar, drawn on its own board.
            GridBoard.Layout lay = GridBoard.Build(board, Slots.Width, rows, false, s, (c, r) => Dead(c, r + 1));
            Vector2 centre = first.anchoredPosition + new Vector2(
                (0.5f - first.pivot.x) * first.rect.width,
                (0.5f - first.pivot.y) * first.rect.height);
            board.anchoredPosition = centre + new Vector2(-lay.X[0] * s, lay.Y[0] * s);
            Inlays.ShipOnBoard(board);
        }

        // Cartur's Bag Rows marks the cells of a merged backpack row past the backpack's own slots as
        // dead and hides their game cells; the board covers them too. Soft: absent, nothing is dead.
        // Bound once to a delegate: it is asked for every cell on every grid refresh, and
        // MethodInfo.Invoke boxed two ints and an array each time.
        private static readonly System.Func<int, int, bool> s_isDead = BindIsDead();

        private static System.Func<int, int, bool> BindIsDead()
        {
            var m = HarmonyLib.AccessTools.Method(HarmonyLib.AccessTools.TypeByName("CarturBagRows.DeadCells"), "IsDead",
                                                  new[] { typeof(int), typeof(int) });
            return m != null && m.ReturnType == typeof(bool)
                ? (System.Func<int, int, bool>)System.Delegate.CreateDelegate(typeof(System.Func<int, int, bool>), m)
                : null;
        }

        private static bool Dead(int x, int y) => s_isDead != null && s_isDead(x, y);

        // ---- opening under the hotbar ------------------------------------------------------

        /// <summary>
        /// Opens the bag board directly under the hotbar board with its columns on the hotbar's
        /// (Cartur, 2026-10-03: "the inventory needs to open up to where is under the hotbar lined
        /// up nicely").
        ///
        /// Measured off the cells in world space, every time: the panel is scaled so a bag column
        /// is as wide as a hotbar slot - edit mode can size the hotbar - then moved so bag column 0
        /// sits under hotbar slot 0 and the bag board's top edge sits a gap below the hotbar
        /// board's bottom edge. Moving the hotbar in edit mode moves the bag with it, which is why
        /// the bag panel is not registered with edit mode on the boards.
        /// </summary>
        /// <summary>
        /// Remembers the cells and makes sure the panel carries a Follower. The move itself runs
        /// in LateUpdate: InventoryGui has an Animator (m_animator, driven from Show) that writes
        /// the panel's transform every frame after Update, so a move made from the grid's
        /// UpdateGui was undone before it was drawn - logged as a 790-unit move, seen as no move.
        /// </summary>
        internal static void Follow(InventoryGui gui, RectTransform first, RectTransform second)
        {
            s_gui = gui;
            s_first = first;
            s_second = second;
            if (gui.m_player != null && gui.m_player.GetComponent<Follower>() == null)
                gui.m_player.gameObject.AddComponent<Follower>();
        }

        private static InventoryGui s_gui;
        private static RectTransform s_first, s_second;

        /// <summary>
        /// Once per session: if the bag is still at the old layout's top-left default it sits on
        /// top of the hotbar board (Cartur: "why does the inventory keep covering the hotbar"), so
        /// it is moved under the hotbar and the spot is saved as if he had dragged it there. A bag
        /// he has placed himself is never touched. Runs in LateUpdate because the screen's
        /// Animator rewrites the panel's transform after Update.
        /// </summary>
        private sealed class Follower : MonoBehaviour
        {
            private int m_shown;

            private void LateUpdate()
            {
                if (s_placed || s_gui == null || !HotbarRow.Boards || !InventoryGui.IsVisible())
                {
                    m_shown = 0;
                    return;
                }
                // The open animation slides the panel in over a few frames (pilot: 350 -> rest in
                // 4); placing mid-slide saved a spot measured off a moving panel.
                if (++m_shown < 30)
                    return;
                s_placed = true;
                RectTransform panel = s_gui.m_player;
                HotbarRow.Log?.LogInfo("inventory board: at " + (panel != null ? panel.anchoredPosition.ToString() : "null")
                    + ", old default " + OldDefault);
                // Once ever, whatever the saved spot (Cartur, 2026-10-03: "fix bag under hotbar");
                // after that his drags win.
                // v2: the first run of this saved a spot measured while the open animation was
                // still overriding the panel (fixed with HudLayout.Pin, 2026-10-03).
                if (panel == null || !HudLayout.FirstTime("bagUnderHotbar2"))
                    return;
                // Only off the old dev default. Cartur's own layout now ships as the default
                // (HudLayout.Shipped, 2026-10-05), and a fresh install aligned here would lose it on
                // the first open; the published mod never had a bag panel, so no player needs this.
                if ((panel.anchoredPosition - OldDefault).sqrMagnitude > 1f)
                    return;
                Align(s_gui, s_first, s_second, scale: false);
                HudLayout.Commit("player");
                HotbarRow.Log?.LogInfo("inventory board moved from the old default to under the hotbar: " + panel.anchoredPosition);
            }
        }

        private static readonly Vector2 OldDefault = new Vector2(40f, -40f);
        private static bool s_placed;

        internal static void Align(InventoryGui gui, RectTransform first, RectTransform second, bool scale = true)
        {
            RectTransform panel = gui.m_player;
            RectTransform slot0 = Hotbar.SlotBox(0), slot1 = Hotbar.SlotBox(1);
            RectTransform bar = Hotbar.BoardRect;
            Transform board = first != null ? first.parent?.Find(Name) : null;
            if (panel == null || first == null || second == null || slot0 == null || slot1 == null
                || bar == null || board == null)
            {
                Once("align skipped: panel " + (panel != null) + ", cells " + (first != null) + "/" + (second != null)
                    + ", hotbar slots " + (slot0 != null) + "/" + (slot1 != null) + ", hotbar board " + (bar != null)
                    + ", bag board " + (board != null));
                return;
            }

            // Same width as the hotbar board, centred under it (Cartur, 2026-10-03: "main inventory
            // needs to be the same width as the hotbar"). The two boards are not drawn to the same
            // pitch - 10.9 hotbar slots across its board, 9.8 bag columns across this one - so a bag
            // column comes out about 11% wider than a hotbar slot and the columns do not stack
            // exactly; the boards' edges do.
            var corners = new Vector3[4];
            bar.GetWorldCorners(corners);
            float barBottom = corners[0].y, barLeft = corners[0].x, barRight = corners[2].x;
            ((RectTransform)board).GetWorldCorners(corners);
            float bagWidth = corners[2].x - corners[0].x;
            if (scale && bagWidth > 0.01f && Mathf.Abs((barRight - barLeft) / bagWidth - 1f) > 0.001f)
            {
                panel.localScale *= (barRight - barLeft) / bagWidth;
                ((RectTransform)board).GetWorldCorners(corners);
            }
            float bagTop = corners[1].y;
            float bagMid = (corners[0].x + corners[2].x) * 0.5f;
            float gap = Hotbar.BoardGap * bar.lossyScale.y;

            Vector3 move = new Vector3((barLeft + barRight) * 0.5f - bagMid, barBottom - gap - bagTop, 0f);
            Once("align: hotbar board " + barLeft.ToString("0") + ".." + barRight.ToString("0")
                + ", bag width " + bagWidth.ToString("0") + ", move " + move);
            panel.position += move;
        }

        // One line per world load, for the diagnosis above - this runs on every grid update.
        private static string s_said;
        private static void Once(string line)
        {
            string key = line.Length > 24 ? line.Substring(0, 24) : line;
            if (s_said == key)
                return;
            s_said = key;
            HotbarRow.Log?.LogInfo(line);
        }

        private static Vector3 Centre(RectTransform rt) => rt.TransformPoint(rt.rect.center);

        /// <summary>The board's right edge in the given panel's space, or the panel's own if there is no board.</summary>
        internal static float RightIn(RectTransform panel)
        {
            float right = panel.rect.xMax;
            Transform found = Find(panel);
            if (found == null || !found.gameObject.activeInHierarchy)
                return right;
            var corners = new Vector3[4];
            ((RectTransform)found).GetWorldCorners(corners);
            return Mathf.Max(right, panel.InverseTransformPoint(corners[2]).x);
        }

        private static Transform Find(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == Name)
                    return t;
            return null;
        }

        private static RectTransform Root(Transform gridRoot, RectTransform first)
        {
            var board = gridRoot.Find(Name) as RectTransform;
            if (board == null)
            {
                var go = new GameObject(Name, typeof(RectTransform));
                board = (RectTransform)go.transform;
                board.SetParent(gridRoot, false);
            }
            board.anchorMin = first.anchorMin;
            board.anchorMax = first.anchorMax;
            board.pivot = new Vector2(0f, 1f);
            board.localScale = Vector3.one;
            board.SetAsFirstSibling();   // behind the cells
            return board;
        }
    }
}
