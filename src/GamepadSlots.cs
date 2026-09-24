using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Part 12 of Cartur's UI: reaching the slots with a controller.
    ///
    /// This is a fix as much as a feature. Vanilla InventoryGrid.UpdateGamepad walks the
    /// selection down to m_height - 1, and our inventory is three rows taller than it looks, so
    /// with a pad in hand the selection already wanders into the hidden rows - onto cells that
    /// EquipmentPanel has re-parented or switched off. It then calls Select() on a disabled
    /// object. So the choice is not "add gamepad support or not", it is "own this or leave it
    /// walking off the edge".
    ///
    /// Why this is short where Equipment and Quick Slots' was long
    /// ----------------------------------------------------------
    /// That mod scored candidate slots by screen position, because its slot set was open to
    /// other mods and it could not know where anything would be. Ours is a fixed table, so
    /// moving is a lookup in EquipmentPanel's own layout: two columns of equipment, the quick
    /// slots as a row under them, and the quiver under that. Up, down, left and right are steps
    /// in that little grid. No geometry, no scoring - which is also why it still reaches the
    /// quick slots now they are drawn as the food diamonds on the HUD, nowhere near the panel.
    ///
    /// The rules, kept deliberately plain:
    ///   right, from the last column of the inventory   -> into the panel, top left
    ///   left,  from the panel's first column           -> back to the inventory, same row
    ///   up / down / left / right inside the panel      -> next filled cell in that direction
    ///   down, from the bottom row of the inventory     -> vanilla's own container jump, untouched
    ///
    /// Down out of the inventory is left alone on purpose. It is how you get to a chest, and
    /// taking it for the equipment panel would cost more than it gave.
    /// </summary>
    internal static class GamepadSlots
    {
        // m_selected is private; SetGamepadSelection is public, so only the read needs this.
        private static readonly FieldInfo s_selected = AccessTools.Field(typeof(InventoryGrid), "m_selected");

        internal static bool SelectionFound => s_selected != null;

        /// <summary>
        /// The slot the pad is currently on, or null. Used by QuickSlots to light the right
        /// diamond, since a quick slot's cell is drawn nowhere near the panel.
        /// </summary>
        internal static Slots.Slot Selected()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || gui.m_playerGrid == null || !ZInput.IsExclusiveGamepadActive())
                return null;
            if (!gui.m_playerGrid.m_uiGroup.IsActive)
                return null;
            return s_selected?.GetValue(gui.m_playerGrid) is Vector2i pos ? Slots.At(pos) : null;
        }

        private const int Columns = 3;   // the quick slots and the quiver are both three wide
        // Four equipment rows, the quick strip, and the quiver strip: rows 0..5, so the bound
        // is 6. It was 5 when this was written, before the quiver row existed, which put
        // EquipmentPanel.QuiverRow exactly one past the end and made the quiver unreachable
        // with a pad. Derived from the panel's own constant now rather than counted again here.
        private const int Rows = EquipmentPanel.QuiverRow + 1;

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGamepad")]
        [HarmonyPrefix]
        private static bool UpdateGamepad(InventoryGrid __instance)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || __instance != gui.m_playerGrid || Player.m_localPlayer == null)
                return true;
            if (!__instance.m_uiGroup.IsActive || Console.IsVisible()
                || ZInput.IsTouchActive() || !ZInput.IsExclusiveGamepadActive())
                return true;
            if (!(s_selected?.GetValue(__instance) is Vector2i pos))
                return true;

            int dx = (Pressed("JoyDPadLeft", "JoyLStickLeft") ? -1 : 0)
                   + (Pressed("JoyDPadRight", "JoyLStickRight") ? 1 : 0);
            int dy = (Pressed("JoyDPadUp", "JoyLStickUp") ? -1 : 0)
                   + (Pressed("JoyDPadDown", "JoyLStickDown") ? 1 : 0);

            // Nothing pressed: hand straight back, so vanilla still gets to act on A and the
            // triggers. Only movement is taken over.
            if (dx == 0 && dy == 0)
                return true;

            Slots.Slot current = Slots.At(pos);

            if (current == null)
            {
                if (dx > 0 && pos.x >= Slots.Width - 1)
                {
                    // Into the panel at its first filled cell - Head, which the art puts in
                    // the middle column rather than at column 0.
                    Slots.Slot first = EquipmentPanel.FirstCell();
                    if (first != null)
                        Move(__instance, first.GridPos);
                    return false;
                }
                if (dy > 0 && pos.y >= Slots.VisibleRows - 1)
                {
                    // Vanilla decides this against m_height, which is three rows taller than
                    // the player can see - so left to itself it steps onto a hidden cell
                    // instead of jumping to the container. The jump is made here instead.
                    __instance.OnMoveToLowerInventoryGrid?.Invoke(pos);
                    return false;
                }
                return true;    // an ordinary move inside the grid; vanilla's is correct
            }

            // Standing on a slot: every direction is ours, including the ones that find
            // nothing, or vanilla would move the selection underneath us.
            int column, row;
            if (EquipmentPanel.PanelCell(current, out column, out row))
            {
                // Left out of the panel is "left, and there was nothing there" rather than
                // "left from column 0": the art's columns are ragged - Head and Chest sit in
                // the middle with nothing beside them - so a fixed column test would trap the
                // selection on them.
                if (!Step(__instance, column, row, dx, dy) && dx < 0)
                    Move(__instance, new Vector2i(Slots.Width - 1, Mathf.Clamp(row, 0, Slots.VisibleRows - 1)));
            }
            return false;
        }

        // Button names are passed whole rather than built from a direction. This runs every
        // frame, and "JoyDPad" + direction was eight string allocations a frame; literals are
        // interned and cost nothing.
        private static bool Pressed(string dpad, string stick) =>
            ZInput.GetButtonDown(dpad) || ZInput.GetButtonDown(stick);

        /// <summary>
        /// Walks in the given direction until it finds a filled cell. Scanning rather than
        /// stepping once is what makes the gaps in the layout invisible: the left column has
        /// three slots against the right column's four, so a straight step right from the
        /// bottom-left slot lands on nothing.
        /// </summary>
        private static bool Step(InventoryGrid grid, int column, int row, int dx, int dy)
        {
            int c = column, r = row;
            for (int guard = 0; guard < Columns + Rows; guard++)
            {
                c += dx;
                r += dy;
                if (c < 0 || c >= Columns || r < 0 || r >= Rows)
                    return false;
                Slots.Slot slot = EquipmentPanel.SlotAtCell(c, r);
                if (slot != null)
                    return Move(grid, slot.GridPos);
            }
            return false;
        }


        /// <summary>
        /// Moves the selection and tells Unity's own UI about it. Selecting the Selectable is
        /// not decoration: it is what drives the highlight and what the A button acts on, and
        /// vanilla does the same thing at the end of its own UpdateGamepad.
        /// </summary>
        private static bool Move(InventoryGrid grid, Vector2i pos)
        {
            grid.SetGamepadSelection(pos);
            // Vanilla's own accessor for "the cell the pad is on", used straight after setting
            // the selection, so it resolves the same element its own UpdateGamepad would.
            RectTransform element = grid.GetGamepadSelectedElement();
            Selectable selectable = element != null ? element.GetComponent<Selectable>() : null;
            if (selectable != null && element.gameObject.activeInHierarchy)
                selectable.Select();
            return true;
        }
    }
}
