using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Part 12 of Cartur's UI: the equipment panel and the shield slot beside the inventory.
    ///
    /// There are no slot widgets in this file. InventoryGrid.UpdateGui builds one
    /// InventoryElement for every cell of the player's inventory, and the inventory is three
    /// rows taller than it looks (see Slots), so the cells for the slots already exist by the
    /// time this runs. They already drag, drop, highlight, show tooltips and answer the
    /// gamepad. All this does is re-parent them onto a panel of Cartur's art and put them where
    /// the layout says. Building slot widgets instead would mean re-implementing all of that
    /// and it would be worse.
    ///
    /// Layout, and where each number came from
    /// ---------------------------------------
    /// The panel is one piece of art - Assets/equipment_panel.png, 784x1168 - with the seven
    /// slot boxes and their names painted into it. So the layout is not chosen here, it is
    /// measured off that file and the cells are put where the boxes already are.
    ///
    /// Each box's gold outline was taken by bounding box. Centres and sizes, in source px:
    ///
    ///     Head      (365.5, 164.0)   140 x 137
    ///     Shoulder  (232.5, 373.5)   140 x 136
    ///     Shield    (495.0, 373.5)   139 x 136
    ///     Chest     (365.0, 574.0)   139 x 137
    ///     Legs      (365.0, 775.5)   139 x 136
    ///     Utility   (228.0, 976.0)   139 x 135
    ///     Trinket   (496.5, 975.5)   140 x 136
    ///
    /// The panel keeps the height it already had - Cartur asked to hold that when he revised
    /// the art - so the scale is fixed by the panel's height rather than by the cell:
    /// 545.35 / 1125. The art's proportions changed slightly with the revision (734x1125
    /// against the old 784x1168), so holding the height means the panel stands 356 units wide
    /// where it stood 366. A box comes out at 67.6 units and the cell inside it stays vanilla's
    /// 64, which leaves the painted outline standing just clear of the item.
    ///
    /// The art also paints the slot names, so the cell's own label is switched off, and it
    /// paints the box, so the cell's own plate is switched off too - two boxes inside each
    /// other is the thing this art was picked to avoid. The hover tint is kept.
    ///
    /// The panel's resting place is the right-hand edge of the inventory panel, and it is
    /// registered with HudLayout, so moving or scaling it is a drag in edit mode rather than
    /// another number argued over here.
    /// </summary>
    internal static class EquipmentPanel
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        internal const string PanelName = "CarturUI_Equipment";
        private const string QuiverName = "CarturUI_QuiverStrip";
        private const string HiddenName = "CarturUI_UnusedSlotCells";

        private const float Pitch = 80f;    // the quiver strip's own spacing
        private const float Cell = 64f;
        private const float Margin = 16f;

        // The art, and the one measurement everything else comes off. See the class comment.
        private const float ArtWidth = 734f;
        private const float ArtHeight = 1125f;

        // The height the panel has had since the first cut of it, kept across the art revision
        // because that is what Cartur asked to hold. Width follows the new art's proportions.
        private const float PanelHeight = 545.35f;
        private const float Scale = PanelHeight / ArtHeight;    // 0.4847
        private const float PanelWidth = ArtWidth * Scale;      // 355.8

        // Where each box sits in the art, in source pixels from its top-left corner. Measured,
        // not placed - the table in the class comment is this table.
        private static readonly Dictionary<string, Vector2> s_cells = new Dictionary<string, Vector2>
        {
            { "Helmet",   new Vector2(365.5f, 164.0f) },
            { "Shoulder", new Vector2(232.5f, 373.5f) },
            { "Shield",   new Vector2(495.0f, 373.5f) },
            { "Chest",    new Vector2(365.0f, 574.0f) },
            { "Legs",     new Vector2(365.0f, 775.5f) },
            { "Utility",  new Vector2(228.0f, 976.0f) },
            { "Trinket",  new Vector2(496.5f, 975.5f) },
        };

        // The same seven as a plain grid, for the gamepad. It is a map of which slot is
        // left/right/above/below which, not a picture of the panel - the art's centre column
        // is column 1, its pairs sit either side. Step() scans past the gaps.
        private static readonly Dictionary<string, Vector2> s_padCells = new Dictionary<string, Vector2>
        {
            { "Helmet",   new Vector2(1f, 0f) },
            { "Shoulder", new Vector2(0f, 1f) },
            { "Shield",   new Vector2(2f, 1f) },
            { "Chest",    new Vector2(1f, 2f) },
            { "Legs",     new Vector2(1f, 3f) },
            { "Utility",  new Vector2(0f, 4f) },
            { "Trinket",  new Vector2(2f, 4f) },
        };

        // m_elements is private in the stock DLLs. One lookup, null-checked at every use.
        private static readonly FieldInfo s_elements = AccessTools.Field(typeof(InventoryGrid), "m_elements");

        internal static bool ElementsFound => s_elements != null;

        /// <summary>
        /// The cell a slot occupies, as (column, row), for gamepad navigation. It is a
        /// logical map and not a picture of the screen - the quick slots are drawn as the food
        /// diamonds on the HUD, and still sit at row 4 here so a controller can reach them.
        ///
        /// A table rather than the screen-space nearest-neighbour search Equipment and Quick
        /// Slots used: that mod needed one because other mods could add slots anywhere, and
        /// ours is fixed, so the layout is known rather than inferred.
        /// </summary>
        // The quick slots keep row 4 even though they are drawn on the HUD rather than in this
        // panel. This table is a logical map for the pad, not a picture of the screen - the
        // cells are still grid positions (0,4), (1,4) and (2,4), and InventoryGrid neither
        // knows nor cares which canvas their transforms ended up on. Keeping them in the table
        // is what lets a controller still reach them.
        internal const int QuickRow = 5;
        internal const int QuiverRow = 6;

        internal static bool PanelCell(Slots.Slot slot, out int column, out int row)
        {
            column = row = 0;
            if (slot == null)
                return false;
            if (slot.Kind == Slots.Kind.Quick)
            {
                column = slot.Index;
                row = QuickRow;
                return true;
            }
            if (slot.Kind == Slots.Kind.Quiver)
            {
                column = slot.Index - 16;
                row = QuiverRow;
                return true;
            }
            Vector2 cell;
            if (!s_padCells.TryGetValue(slot.Id, out cell))
                return false;
            column = (int)cell.x;
            row = (int)cell.y;
            return true;
        }

        /// <summary>
        /// Where a controller lands when it steps right out of the bag. Scanned rather than
        /// named: the art's top-left cell is empty - Head sits in the middle column - so
        /// "column 0, row 0" points at nothing in this layout.
        /// </summary>
        internal static Slots.Slot FirstCell()
        {
            for (int row = 0; row <= QuiverRow; row++)
                for (int column = 0; column < 3; column++)
                {
                    Slots.Slot slot = SlotAtCell(column, row);
                    if (slot != null)
                        return slot;
                }
            return null;
        }

        internal static Slots.Slot SlotAtCell(int column, int row)
        {
            foreach (Slots.Slot slot in Slots.All)
            {
                if (slot == null || !slot.IsActive)
                    continue;
                int c, r;
                if (PanelCell(slot, out c, out r) && c == column && r == row)
                    return slot;
            }
            return null;
        }


        private static RectTransform s_panel;
        private static RectTransform s_hidden;
        private static bool s_logged;

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        [HarmonyPostfix]
        private static void Awake()
        {
            // The panel and the parked-cell holder are children of objects the GUI rebuilds, so
            // they go with it. Nothing is cached across an Awake.
            s_panel = null;
            s_quiverStrip = null;
            s_hidden = null;
            s_logged = false;
        }

        /// <summary>
        /// Runs after the grid has laid its cells out, so the ones belonging to the hidden rows
        /// can be taken off it. The grid is also cropped to the visible rows here - it sizes
        /// itself from the inventory height, which is three rows taller than the player is
        /// meant to see.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        [HarmonyPostfix]
        private static void UpdateGui(InventoryGrid __instance)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || __instance != gui.m_playerGrid || Player.m_localPlayer == null)
                return;

            var elements = s_elements?.GetValue(__instance) as List<InventoryElement>;
            if (elements == null)
                return;

            // Plus the drop HotbarRow puts between the hotbar and the rest, or the bottom row
            // hangs off the end of the grid root.
            __instance.m_gridRoot?.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Slots.VisibleRows * __instance.m_elementSpace + HotbarRow.RowShift);

            RectTransform panel = Panel(gui);
            RectTransform hidden = Hidden(gui);
            if (panel == null || hidden == null)
                return;

            int first = Slots.VisibleCells;
            for (int i = 0; i + first < elements.Count && i < Slots.SlotCount; i++)
            {
                InventoryElement element = elements[first + i];
                if (element == null)
                    continue;
                Slots.Slot slot = Slots.All[i];
                if (slot == null || !slot.IsActive)
                {
                    Park(element.gameObject, hidden);
                    continue;
                }
                if (slot.Kind == Slots.Kind.Quick)
                {
                    // The quick slots are the food diamonds on the HUD now - see QuickSlots.
                    // There is no quick slot row in the inventory any more.
                    QuickSlots.Host(slot.Index, element);
                    continue;
                }
                if (slot.Kind == Slots.Kind.Quiver)
                {
                        SeatInStrip(element, QuiverStrip(gui), slot.Index - QuiverCompat.FirstSlot, slot.Label);
                    continue;
                }
                Vector2 cell;
                if (!s_cells.TryGetValue(slot.Id, out cell))
                {
                    Park(element.gameObject, hidden);
                    continue;
                }
                Seat(element, panel, cell);
            }

            // Anything past our table - a wider inventory than eight - is parked too.
            for (int i = first + Slots.SlotCount; i < elements.Count; i++)
                Park(elements[i]?.gameObject, hidden);

            if (!s_logged)
            {
                s_logged = true;
                Log.LogInfo("equipment panel: " + s_cells.Count + " slots on the art, cell "
                    + Cell + " at scale " + Scale.ToString("0.####")
                    + ", panel " + PanelWidth.ToString("0.#") + "x" + PanelHeight.ToString("0.#")
                    + ", anchored " + panel.anchoredPosition + " in " + panel.parent.name);
            }
        }

        /// <summary>
        /// Puts a cell on the box the art already drew for it. <paramref name="cell"/> is the
        /// box's centre in the art's own pixels; Scale is the only thing that turns it into
        /// canvas units, so the cells cannot drift away from the picture behind them.
        /// </summary>
        private static void Seat(InventoryElement element, RectTransform panel, Vector2 cell)
        {
            if (panel == null)
                return;
            GameObject go = element.gameObject;
            go.SetActive(true);

            bool arriving = go.transform.parent != panel;
            if (arriving)
                go.transform.SetParent(panel, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cell.x * Scale, -cell.y * Scale);

            // One-time. This runs every frame from the grid's postfix, and re-writing a
            // colour block that already says this would be a frame-rate bug for no gain.
            if (arriving)
                Bare(go);

            // The art paints the slot's name over its box, so the cell's own label is off.
            Label(go.transform.Find("binding") as RectTransform, null);
        }

        /// <summary>
        /// Takes the cell's own slot plate off, because the art already drew the box. The
        /// Image stays - it is the raycast target, which is what makes the box something you
        /// can drop onto - it just draws nothing. The button keeps a hover tint, otherwise
        /// there is no sign at all that the cell under the pointer is live.
        /// </summary>
        internal static void Bare(GameObject go)
        {
            var plate = go.GetComponent<Image>();
            if (plate != null)
            {
                plate.sprite = null;
                plate.color = Color.clear;
            }

            var button = go.GetComponent<Button>();
            if (button == null)
                return;

            // ColorTint writes these onto the plate, so "no plate" has to be said here too or
            // the button paints its own dark square back on the first pointer exit.
            ColorBlock colours = button.colors;
            colours.normalColor = Color.clear;
            colours.highlightedColor = new Color(1f, 1f, 1f, 0.16f);
            colours.pressedColor = new Color(1f, 1f, 1f, 0.28f);
            colours.selectedColor = Color.clear;
            colours.disabledColor = Color.clear;
            button.colors = colours;
        }

        /// <summary>The quiver strip is still a plain row of cells at a fixed pitch.</summary>
        private static void SeatInStrip(InventoryElement element, RectTransform strip, int index, string label)
        {
            if (strip == null)
                return;
            GameObject go = element.gameObject;
            go.SetActive(true);
            if (go.transform.parent != strip)
                go.transform.SetParent(strip, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(
                Margin + Cell * 0.5f + index * Pitch, -(Margin + Cell * 0.5f));

            Label(go.transform.Find("binding") as RectTransform, label);
        }

        /// <summary>
        /// The cell's own "binding" label is vanilla's hotbar number. Anchored to the top of the
        /// cell rather than offset by a measured amount, so it sits above the cell whatever the
        /// element prefab's own pivot turns out to be.
        /// </summary>
        private static void Label(RectTransform binding, string text)
        {
            if (binding == null)
                return;
            if (text == null)
            {
                if (binding.gameObject.activeSelf)
                    binding.gameObject.SetActive(false);
                return;
            }
            TMP_Text label = binding.GetComponent<TMP_Text>();
            if (label == null)
                return;

            binding.gameObject.SetActive(true);
            label.enabled = true;
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.overflowMode = TextOverflowModes.Overflow;
            label.textWrappingMode = TextWrappingModes.NoWrap;

            binding.anchorMin = binding.anchorMax = new Vector2(0.5f, 1f);
            binding.pivot = new Vector2(0.5f, 0f);
            binding.anchoredPosition = new Vector2(0f, 2f);
            binding.sizeDelta = new Vector2(Pitch, 20f);
        }

        private static void Park(GameObject go, RectTransform holder)
        {
            if (go == null)
                return;
            go.SetActive(false);
            if (holder != null && go.transform.parent != holder)
                go.transform.SetParent(holder, false);
        }

        /// <summary>
        /// The registered root carries no art of its own - the frame is a child that stretches
        /// to fill it - so scaling the panel in edit mode scales frame and cells together.
        /// </summary>
        private static RectTransform Panel(InventoryGui gui)
        {
            if (s_panel != null)
                return s_panel;
            if (gui.m_player == null)
                return null;

            var go = new GameObject(PanelName, typeof(RectTransform));
            s_panel = (RectTransform)go.transform;
            s_panel.SetParent(gui.m_player, false);
            s_panel.anchorMin = s_panel.anchorMax = new Vector2(1f, 1f);
            s_panel.pivot = new Vector2(0f, 1f);
            s_panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            s_panel.anchoredPosition = new Vector2(12f, 0f);

            Canvas canvas = gui.GetComponentInParent<Canvas>();
            Art(s_panel, canvas != null ? canvas.referencePixelsPerUnit : 100f);

            HudLayout.Register(InventoryScreen.Owner, "equipment", "Equipment",
                s_panel, s_panel, s_panel.anchoredPosition);
            return s_panel;
        }

        /// <summary>
        /// The one strip under the equipment panel: three ammo cells hosting Better Archery's
        /// quiver, when that mod is installed with its quiver on. Built only when a cell asks
        /// for it, so a profile without Better Archery never grows one, and registered on its
        /// own so it can be dragged separately.
        /// </summary>
        private static RectTransform s_quiverStrip;

        private static RectTransform QuiverStrip(InventoryGui gui)
        {
            if (s_quiverStrip != null)
                return s_quiverStrip;
            RectTransform panel = Panel(gui);
            if (panel == null)
                return null;

            const int count = 3;
            var go = new GameObject(QuiverName, typeof(RectTransform));
            var strip = (RectTransform)go.transform;
            strip.SetParent(gui.m_player, false);
            strip.anchorMin = strip.anchorMax = new Vector2(1f, 1f);
            strip.pivot = new Vector2(0f, 1f);
            strip.sizeDelta = new Vector2(Margin * 2f + Cell + Pitch * (count - 1), Margin * 2f + Cell);
            strip.anchoredPosition = panel.anchoredPosition - new Vector2(0f, PanelHeight + 8f);

            Canvas canvas = gui.GetComponentInParent<Canvas>();
            Frame(strip, canvas != null ? canvas.referencePixelsPerUnit : 100f);

            HudLayout.Register(InventoryScreen.Owner, "quiver", "Quiver",
                strip, strip, strip.anchoredPosition);

            s_quiverStrip = strip;
            return strip;
        }

        /// <summary>
        /// The panel's picture. One sprite drawn whole - the boxes and the names are in it -
        /// so it is Simple, not sliced: a 9-slice would stretch the middle and walk the boxes
        /// away from the cells sitting on them.
        ///
        /// If the PNG is missing it falls back to the plain panel piece the layout used before,
        /// which leaves seven unlabelled cells on a plain panel rather than seven cells on
        /// nothing.
        /// </summary>
        private static void Art(RectTransform parent, float ppu)
        {
            Sprite art = AssetLoader.EquipmentPanel;
            if (art == null)
            {
                Log.LogWarning("equipment_panel.png missing - falling back to the plain panel");
                Frame(parent, ppu);
                return;
            }

            var go = new GameObject("Art", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();

            Image image = go.GetComponent<Image>();
            image.sprite = art;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;   // the rect is already the art's aspect, by Scale
            image.raycastTarget = false;
        }

        private static void Frame(RectTransform parent, float ppu)
        {
            var go = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            var frame = (RectTransform)go.transform;
            frame.SetParent(parent, false);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;
            frame.SetAsFirstSibling();

            Image image = go.GetComponent<Image>();
            image.sprite = AssetLoader.Piece("panel", ppu);
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
        }

        private static RectTransform Hidden(InventoryGui gui)
        {
            if (s_hidden != null)
                return s_hidden;
            if (gui.m_player == null)
                return null;
            var go = new GameObject(HiddenName, typeof(RectTransform));
            s_hidden = (RectTransform)go.transform;
            s_hidden.SetParent(gui.m_player, false);
            go.SetActive(false);
            return s_hidden;
        }
    }
}
