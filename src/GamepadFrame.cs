using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// The yellow frame the game draws round the inventory while a controller is in use.
    ///
    /// Root cause, measured (pilot "pad yellow": ZInput forced to a gamepad, the orange graphics under the inventory
    /// screen listed with the controller off and on): Inventory_screen/root/Player/selected_frame carries three
    /// pieces that only show in controller mode. "selected (1)" is the game's focus ring for the whole player panel,
    /// 297x169 px on a 1280x720 screen, and "selected (2)" and "selected (3)" ring the armour and weight boxes.
    /// All three are sized and placed for the game's own layout. Cartur's UI draws the inventory on its own board
    /// (CarturUI_InventoryBoard, shorter than the panel the game sized the ring for) and moves armour and weight
    /// into its side column, so the rings hung under the inventory and beside the column (Reecem02, Discord).
    ///
    /// The rings are put on what is actually drawn: the board, and the armour and weight boxes. Only while a
    /// controller is in use and the frame is on, since it is only visible then. Sprites, tint and the rest of the
    /// frame stay the game's and the skin's.
    /// </summary>
    internal static class GamepadFrame
    {
        private static readonly Vector3[] Corners = new Vector3[4];
        private static RectTransform s_board, s_armour, s_weight;

        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPostfix]
        private static void Fit(InventoryGui __instance)
        {
            if (!ZInput.IsExclusiveGamepadActive())
                return;
            RectTransform panel = __instance.m_player;
            Transform frame = panel != null ? panel.Find("selected_frame") : null;
            if (frame == null || !frame.gameObject.activeInHierarchy)
                return;

            if (s_board == null) s_board = Find(panel, InventoryBoard.Name);
            if (s_armour == null) s_armour = Find(panel, "CarturUI_Box_Armor");
            if (s_weight == null) s_weight = Find(panel, "CarturUI_Box_Weight");

            Place(frame.Find("selected (1)"), s_board);
            Place(frame.Find("selected (2)"), s_armour);
            Place(frame.Find("selected (3)"), s_weight);
        }

        private static RectTransform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t as RectTransform;
            return null;
        }

        /// <summary>Centres the ring on the target and gives it the target's size, in the ring's parent's units.</summary>
        private static void Place(Transform ring, RectTransform target)
        {
            var rt = ring as RectTransform;
            var parent = rt != null ? rt.parent as RectTransform : null;
            if (rt == null || parent == null || target == null)
                return;
            target.GetWorldCorners(Corners);
            Vector2 low = parent.InverseTransformPoint(Corners[0]);
            Vector2 high = parent.InverseTransformPoint(Corners[2]);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = high - low;
            rt.anchoredPosition = (low + high) * 0.5f - parent.rect.center;
        }
    }
}
