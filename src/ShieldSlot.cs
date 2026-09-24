using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Part 12 of Cartur's UI: the shield slot. Replaces Shield Me Bruh.
    ///
    /// The rule: a shield sitting in the shield slot is raised automatically whenever a
    /// one-handed weapon is drawn, and dropped again when that weapon is put away.
    ///
    /// Why this is thirty lines and the mod it replaces is four hundred
    /// ----------------------------------------------------------------
    /// Shield Me Bruh had no slot to put a shield in. It nominated one by middle-clicking a
    /// cell of the ordinary inventory grid, so it had to remember which cell that was, draw a
    /// marker on it, keep a live reference to the InventoryElement, re-find it whenever the
    /// grid rebuilt, and serialise the grid position into the character's custom data so the
    /// choice survived a reload. All of that exists to answer one question: which shield?
    ///
    /// Here the shield is in a slot, so the question answers itself - Slots.Shield.Item - and
    /// the save file already holds it, because it is an inventory item at an inventory
    /// position. The nomination, the marker, the element tracking and the YAML all go.
    ///
    /// Read off ShieldMeBruh.dll, not guessed, and kept:
    ///   - the trigger is a Postfix on Humanoid.EquipItem / UnequipItem, not an Update poll
    ///   - it fires only for ItemType.OneHandedWeapon (3)
    ///   - equip only when the off hand is empty, so it never fights a torch or a second weapon
    ///   - unequip only when the off hand is holding the nominated shield itself
    ///   - the per-weapon exclusion mark lives in the item's own m_customData, so it travels
    ///     with the weapon rather than with the character
    /// </summary>
    internal static class ShieldSlot
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        /// <summary>Marked weapons never call the shield. Stored on the item, as Shield Me Bruh did.</summary>
        private const string ExcludedKey = "cartur_noshield";

        private static bool s_ours;   // guards against our own Equip re-entering the patch

        internal static void Init()
        {
            Log.LogInfo("shield slot active: a shield in it is raised with any one-handed weapon");
        }

        private static bool IsOneHanded(ItemDrop.ItemData item) =>
            item?.m_shared != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon;

        internal static bool IsExcluded(ItemDrop.ItemData item) =>
            IsOneHanded(item) && item.m_customData != null && item.m_customData.ContainsKey(ExcludedKey);

        /// <summary>
        /// Drawing a one-handed weapon raises the nominated shield. The off-hand check is what
        /// stops it stealing a torch or the second half of a dual wield: if something is
        /// already there, the player put it there on purpose.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), "EquipItem")]
        [HarmonyPostfix]
        private static void EquipItem(Humanoid __instance, ItemDrop.ItemData item, bool __result)
        {
            if (s_ours || !__result || !(__instance is Player player) || player != Player.m_localPlayer)
                return;
            if (!IsOneHanded(item) || IsExcluded(item) || player.LeftItem != null)
                return;

            ItemDrop.ItemData shield = Slots.Shield?.Item;
            if (shield == null || player.IsItemEquiped(shield))
                return;

            s_ours = true;
            try { player.EquipItem(shield); }
            finally { s_ours = false; }
        }

        /// <summary>
        /// Putting the weapon away lowers the shield - but only the shield this slot raised.
        /// A shield the player equipped by hand is left alone, which is why this compares the
        /// off-hand item against the slot's item by reference rather than by name.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), "UnequipItem")]
        [HarmonyPostfix]
        private static void UnequipItem(Humanoid __instance, ItemDrop.ItemData item)
        {
            if (s_ours || !(__instance is Player player) || player != Player.m_localPlayer)
                return;
            if (!IsOneHanded(item) || IsExcluded(item))
                return;

            ItemDrop.ItemData shield = Slots.Shield?.Item;
            if (shield == null || player.LeftItem != shield)
                return;

            s_ours = true;
            try { player.UnequipItem(shield); }
            finally { s_ours = false; }
        }

        // ---- the exclusion mark ------------------------------------------------------------

        /// <summary>
        /// Middle-click a one-handed weapon in the player's grid to mark it, and that weapon
        /// stops calling the shield.
        ///
        /// UIInputHandler does raise a middle-button event, but InventoryGrid never subscribes
        /// to it - it wires only the left and right handlers - so there is no vanilla method to
        /// hook. Rather than walk every element after each rebuild adding a delegate and
        /// keeping a list of which ones already have one, the button is read at its source:
        /// one Postfix on UIInputHandler.OnPointerDown, which fires for the whole UI, and the
        /// first line throws away everything that is not a middle click on an inventory cell.
        /// </summary>
        [HarmonyPatch(typeof(UIInputHandler), "OnPointerDown")]
        [HarmonyPostfix]
        private static void OnPointerDown(UIInputHandler __instance, PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Middle)
                return;
            InventoryElement cell = __instance.GetComponentInParent<InventoryElement>();
            Inventory inventory = Slots.PlayerInventory;
            if (cell == null || inventory == null || !InPlayerPanel(cell))
                return;

            // A cell only means a position; which inventory that position is in depends on the
            // panel the cell lives under. Without the panel check a middle click on a chest's
            // first cell would mark whatever the player had at the same position.
            ItemDrop.ItemData item = inventory.GetItemAt(cell.Position.x, cell.Position.y);
            if (!IsOneHanded(item))
                return;

            if (item.m_customData.Remove(ExcludedKey))
                Log.LogInfo(item.m_shared.m_name + " will call the shield again");
            else
            {
                item.m_customData[ExcludedKey] = "true";
                Log.LogInfo(item.m_shared.m_name + " marked: it will not call the shield");
            }
            s_dirty = true;
        }

        /// <summary>
        /// Set whenever something that decides where a mark goes has moved. Read off
        /// assembly_valheim: InventoryGui.Update calls UpdateInventory, which calls
        /// InventoryGrid.UpdateInventory, which calls UpdateGui - so the postfix below ran EVERY
        /// FRAME the bag was open, and each pass allocated a component array, did a
        /// Transform.Find and a GetComponent per cell, and walked the item list per cell to ask
        /// what was at that position. Nothing it draws can change without one of three events,
        /// so it waits for them instead. Starts true so the first open draws.
        /// </summary>
        private static bool s_dirty = true;

        // The grid's own rebuild condition, read off InventoryGrid.UpdateGui: when the
        // inventory's width or height differs from the grid's, it Destroys every InventoryElement
        // and builds them again - which takes our marks with them. That does not go through
        // Inventory.Changed, because Inventory.SetHeight is a bare field write, so it is watched
        // here rather than assumed to raise anything.
        private static int s_gridWidth, s_gridHeight;

        /// <summary>The player's inventory changed - an item may have moved to another cell.</summary>
        [HarmonyPatch(typeof(Player), "OnInventoryChanged")]
        [HarmonyPostfix]
        private static void InventoryChanged(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
                s_dirty = true;
        }

        /// <summary>
        /// The mark itself: an image on the cell, cloned from the element's own icon so it
        /// lands in the same place, at the same size, in the same draw order, with no new
        /// material. Redrawn when s_dirty says something moved, not every frame.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        [HarmonyPostfix]
        private static void UpdateGui(InventoryGrid __instance)
        {
            InventoryGui gui = InventoryGui.instance;
            Inventory inventory = Slots.PlayerInventory;
            if (gui == null || gui.m_player == null || inventory == null || __instance != gui.m_playerGrid)
                return;
            Sprite mark = AssetLoader.ShieldExcluded;
            if (mark == null)
                return;

            int width = inventory.GetWidth(), height = inventory.GetHeight();
            if (width != s_gridWidth || height != s_gridHeight)
            {
                s_gridWidth = width;
                s_gridHeight = height;
                s_dirty = true;
            }

            if (!s_dirty)
                return;
            s_dirty = false;

            // Walked from the panel, not the grid: the slot cells have been re-parented out of
            // the grid by EquipmentPanel, and a quick slot can hold a one-handed weapon too.
            //
            // The container panel is a CHILD of the player panel - root/Player/Container - so
            // this walk reaches a chest's cells as well, and every cell here is looked up
            // against the PLAYER's inventory by grid position. Left alone, a chest's third slot
            // asked what was in the player's third slot and wore its mark: Cartur saw an X on a
            // chest item because the weapon he had excluded sat at the same position.
            foreach (InventoryElement cell in gui.m_player.GetComponentsInChildren<InventoryElement>(true))
            {
                if (gui.m_container != null && cell.transform.IsChildOf(gui.m_container))
                    continue;

                ItemDrop.ItemData item = inventory.GetItemAt(cell.Position.x, cell.Position.y);
                Image overlay = Mark(cell, mark);
                if (overlay != null)
                    overlay.enabled = IsExcluded(item);
            }
        }

        /// <summary>
        /// The cell belongs to the player's half of the inventory screen - either the grid or
        /// one of our relocated slots, both of which live under m_player.
        /// </summary>
        private static bool InPlayerPanel(InventoryElement cell)
        {
            RectTransform panel = InventoryGui.instance?.m_player;
            if (panel == null)
                return false;
            for (Transform t = cell.transform; t != null; t = t.parent)
                if (t == panel)
                    return true;
            return false;
        }

        private const string MarkName = "cartur_noshield";

        private static Image Mark(InventoryElement cell, Sprite sprite)
        {
            if (cell.m_icon == null)
                return null;
            Transform existing = cell.transform.Find(MarkName);
            if (existing != null)
                return existing.GetComponent<Image>();

            Image image = Object.Instantiate(cell.m_icon, cell.m_icon.transform.parent);
            image.name = MarkName;   // Component.name is the GameObject's name
            image.sprite = sprite;
            // White, not the noteleport overlay's tint. Shield Me Bruh copied that tint because
            // its mark was a white glyph that needed colouring; ours is already the colour it
            // is meant to be, and a Unity tint only multiplies, so borrowing that colour would
            // darken the red rather than leave it alone.
            image.color = Color.white;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }
    }
}
