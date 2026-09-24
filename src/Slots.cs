using System.Collections.Generic;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// Part 12 of Cartur's UI: the slot table behind the equipment panel, the quick slots and
    /// the shield slot. This replaces Equipment and Quick Slots and Shield Me Bruh; neither is
    /// referenced and neither may be installed alongside (see Plugin's incompatibility list).
    ///
    /// Why there is almost no UI code here
    /// -----------------------------------
    /// The player's inventory is grown by three rows that the grid never shows, and the slots
    /// live in those rows. InventoryGrid.UpdateGui builds one InventoryElement per cell of the
    /// whole inventory, so the cells for the hidden rows already exist, already drag, already
    /// drop, already show tooltips and already answer the gamepad. EquipmentPanel moves those
    /// cells into position instead of building slot widgets. That is where the bulk of the code
    /// for a feature like this normally goes, and it is not written here because the game
    /// already wrote it.
    ///
    /// Why this grid layout and not a tidier one
    /// -----------------------------------------
    /// The index-to-cell mapping is byte-identical to the one Equipment and Quick Slots used -
    /// read from its DLL, not guessed: quick slots at indices 0-5, its custom slots at 6-7 and
    /// 16-23, equipment at 8-15, all laid out as (index % width, VisibleRows + index / width).
    /// Shield takes index 6 because that is the first index EAQS hands to a custom slot, which
    /// is where Cartur's shield already sits on a live character. Matching it means swapping
    /// this mod in for those two leaves every worn item exactly where it was, with no migration
    /// step and nothing to drop on the floor.
    ///
    /// What each kind of slot means
    /// ----------------------------
    ///   Equipment  Head, Shoulders, Chest, Legs, Utility, Trinket. Dropping an item in queues
    ///              the equip; the slot only keeps items the player is actually wearing.
    ///   Shield     A holding slot. The shield in it is nominated, not worn - that is the whole
    ///              point of it, so unlike the equipment slots it does not demand m_equipped.
    ///   Quick      Holding slots with a hotkey. Same rule: held, not worn.
    /// </summary>
    internal static class Slots
    {
        internal enum Kind { Equipment, Shield, Quick, Quiver }

        internal sealed class Slot
        {
            internal readonly string Id;
            internal readonly int Index;
            internal readonly string Label;
            internal readonly Kind Kind;
            internal readonly ItemDrop.ItemData.ItemType Type;

            /// <summary>
            /// Whether the slot exists right now. Only the quiver slots ever say no - they are
            /// there when a quiver is worn and not otherwise. This was a Func&lt;bool&gt; passed to
            /// the constructor, which is a general mechanism with exactly one user; asking the
            /// one thing directly is shorter and says what it means.
            /// </summary>
            internal bool IsActive => Kind != Kind.Quiver || QuiverCompat.IsQuiverSlotActive();

            internal Slot(string id, int index, string label, Kind kind, ItemDrop.ItemData.ItemType type)
            {
                Id = id;
                Index = index;
                Label = label;
                Kind = kind;
                Type = type;
            }

            internal Vector2i GridPos => new Vector2i(Index % Width, VisibleRows + Index / Width);

            internal ItemDrop.ItemData Item
            {
                get
                {
                    Inventory inv = PlayerInventory;
                    if (inv == null)
                        return null;
                    Vector2i p = GridPos;
                    return inv.GetItemAt(p.x, p.y);
                }
            }

            /// <summary>
            /// The item is of the right kind for this slot. Says nothing about wearing it.
            ///
            /// A quick slot takes food and nothing else. Cartur's call, and it follows from
            /// what the slots became: each one is a food diamond on the HUD, showing an icon
            /// and a countdown, so a pickaxe sitting in one has an icon and nothing to count.
            /// The type field is not consulted for a quick slot - "food" is not an ItemType -
            /// so it is left as None and IsFood answers instead.
            /// </summary>
            internal bool Accepts(ItemDrop.ItemData item)
            {
                if (item?.m_shared == null || !IsActive)
                    return false;
                return Kind == Kind.Quick ? IsFood(item) : item.m_shared.m_itemType == Type;
            }

            /// <summary>
            /// The item is allowed to stay here. An equipment slot is a picture of what the
            /// player is wearing, so an item that is not worn - and not on its way to being
            /// worn this frame - is moved back out by Validate. A shield or quick slot just
            /// holds things, so the type check is the whole rule.
            /// </summary>
            internal bool Keeps(ItemDrop.ItemData item)
            {
                if (!Accepts(item))
                    return false;
                if (Kind != Kind.Equipment)
                    return true;
                return IsWorn(item) || IsEquipQueued(item);
            }
        }

        // Three rows of eight below the visible grid, the same shape EAQS used, so a character
        // that has been through that mod keeps every worn item in place. 24 cells, of which we
        // fill 10; the rest stay empty and are parked off-screen by EquipmentPanel.
        internal const int HiddenRows = 3;
        internal const int SlotCount = 24;

        /// <summary>
        /// Cells in the hidden region. SlotCount is the size of the slot table - 24, being
        /// three rows of eight, the shape EAQS used - but the region itself is as wide as the
        /// inventory. They are the same number today and stop being so the moment anything
        /// widens the pack, so capacity arithmetic uses this and the table uses SlotCount.
        /// </summary>
        internal static int HiddenCells => HiddenRows * Width;

        internal static readonly Slot[] All = BuildTable();

        private static Slot[] BuildTable()
        {
            var table = new Slot[SlotCount];

            // Indices read off EquipmentAndQuickSlots.Slots.InitializeSlots. The quick slots
            // carry no item type: Accepts does not consult it for them.
            for (int i = 0; i < QuickSlots.Count; i++)
                table[i] = new Slot("Quick" + (i + 1), i, "Quick " + (i + 1), Kind.Quick,
                    ItemDrop.ItemData.ItemType.None);
            table[6] = new Slot("Shield", 6, "Shield", Kind.Shield, ItemDrop.ItemData.ItemType.Shield);
            table[8] = new Slot("Helmet", 8, "Head", Kind.Equipment, ItemDrop.ItemData.ItemType.Helmet);
            table[9] = new Slot("Chest", 9, "Chest", Kind.Equipment, ItemDrop.ItemData.ItemType.Chest);
            table[10] = new Slot("Legs", 10, "Legs", Kind.Equipment, ItemDrop.ItemData.ItemType.Legs);
            table[11] = new Slot("Shoulder", 11, "Shoulders", Kind.Equipment, ItemDrop.ItemData.ItemType.Shoulder);
            table[12] = new Slot("Utility", 12, "Utility", Kind.Equipment, ItemDrop.ItemData.ItemType.Utility);
            table[13] = new Slot("Trinket", 13, "Trinket", Kind.Equipment, ItemDrop.ItemData.ItemType.Trinket);

            // Better Archery's quiver, when that mod is installed and its quiver is on. 16-18
            // is the start of the third hidden row, so these sit at x 0, 1, 2 - the shape it
            // expects. See QuiverCompat.
            for (int i = 0; i < 3; i++)
                table[QuiverCompat.FirstSlot + i] = new Slot("Quiver" + (i + 1),
                    QuiverCompat.FirstSlot + i, "Ammo", Kind.Quiver, ItemDrop.ItemData.ItemType.Ammo);
            return table;
        }

        /// <summary>
        /// Food, as the game itself distinguishes it: something you consume that feeds you.
        ///
        /// Both halves are needed. ItemType.Consumable alone also covers the meads, which are
        /// drunk for a status effect - so a health mead would sit in a diamond with no
        /// countdown, which is the thing this rule exists to prevent.
        ///
        /// m_food alone, not the union with stamina and eitr: Player.ConsumeItem only calls
        /// EatFood when m_food is above zero, so a consumable that feeds only stamina or eitr
        /// is swallowed without ever entering the food list, and its diamond would say "Eat"
        /// forever and never count anything down. The tooltip uses the wider union, which is
        /// why it looked like the right test; what decides whether there is a countdown to
        /// show is the narrower one.
        /// </summary>
        internal static bool IsFood(ItemDrop.ItemData item)
        {
            ItemDrop.ItemData.SharedData d = item?.m_shared;
            return d != null
                && d.m_itemType == ItemDrop.ItemData.ItemType.Consumable
                && d.m_food > 0f;
        }

        internal static Slot Shield => All[6];

        // Rows the player can see. Captured from the inventory the game built rather than
        // assumed to be four, because another mod may have widened it before we look.
        // Set from the character's own "invrows" key as it loads, and by the game whenever it
        // grants a bigger pack. There used to be a CaptureBaseRows that read the height off the
        // inventory at Player.Awake; it was pointless, because Humanoid constructs that
        // inventory as a fixed 8 by 4 and the load path overwrites the answer a moment later.
        internal static int BaseRows { get; private set; } = 4;

        internal static int VisibleRows => BaseRows;
        internal static int FullHeight => VisibleRows + HiddenRows;
        internal static int VisibleCells => VisibleRows * Width;

        internal static Player CurrentPlayer => Player.m_localPlayer;

        internal static Inventory PlayerInventory => Player.m_localPlayer?.GetInventory();

        internal static int Width
        {
            get
            {
                Inventory inv = PlayerInventory;
                return inv == null ? 8 : inv.GetWidth();
            }
        }

        /// <summary>Rows set by the game (Player.SetInventorySize) move the slot region down with them.</summary>
        internal static void SetBaseRows(int rows)
        {
            rows = Mathf.Max(1, rows);
            if (rows == BaseRows)
                return;
            BaseRows = rows;
            // The hidden rows move down with the visible ones, so Better Archery has to be
            // told the quiver's row again.
            QuiverCompat.SyncRow();
        }

        internal static bool IsSlotRow(Vector2i pos) => pos.y >= VisibleRows && pos.y < FullHeight;

        internal static Slot At(Vector2i pos)
        {
            if (!IsSlotRow(pos) || pos.x < 0 || pos.x >= Width)
                return null;
            int index = (pos.y - VisibleRows) * Width + pos.x;
            return index >= 0 && index < SlotCount ? All[index] : null;
        }

        internal static Slot Of(ItemDrop.ItemData item)
        {
            if (item == null)
                return null;
            Inventory inv = PlayerInventory;
            return inv != null && inv.ContainsItem(item) ? At(item.m_gridPos) : null;
        }

        internal static bool IsWorn(ItemDrop.ItemData item)
        {
            if (item == null)
                return false;
            if (item.m_equipped)
                return true;
            Player p = CurrentPlayer;
            return p != null && p.IsItemEquiped(item);
        }

        /// <summary>
        /// Armour has an equip duration, so the game does not wear it on the spot - it puts a
        /// MinorActionData on the player's action queue and plays the animation first. An item
        /// dropped into an equipment slot is therefore not worn for a second or so, and without
        /// asking about that queue the slot would bounce it straight back out in the gap.
        ///
        /// Player.IsEquipActionQueued is public and answers exactly this, for equip and unequip
        /// together (assembly_valheim, Player.IsEquipActionQueued). An earlier version of this
        /// file read Player.m_actionQueue through a cached FieldInfo to tell the two apart; the
        /// distinction was never used, because at every call site the other case is already
        /// excluded by the equipped check beside it.
        /// </summary>
        internal static bool IsEquipQueued(ItemDrop.ItemData item)
        {
            Player p = CurrentPlayer;
            return p != null && item != null && p.IsEquipActionQueued(item);
        }

        // Humanoid.UseItem with fromInventoryGui set is the game's own path for "the player
        // clicked this item in the inventory": it skips the hover-object branch and goes
        // straight to ToggleEquipped, which is what decides between equipping on the spot and
        // queueing the animation. Calling EquipItem directly instead would skip the animation
        // for armour. It toggles, so the state is checked before each call.
        internal static void Equip(Player player, ItemDrop.ItemData item)
        {
            if (player == null || item == null || player.IsItemEquiped(item) || IsEquipQueued(item))
                return;
            player.UseItem(null, item, true);
        }

        internal static void Unequip(Player player, ItemDrop.ItemData item)
        {
            if (player == null || item == null || !player.IsItemEquiped(item) || IsEquipQueued(item))
                return;
            player.UseItem(null, item, true);
        }

        /// <summary>
        /// The first free cell, with the hotbar left until everything else is full.
        ///
        /// Row 0 is the hotbar, and vanilla hands out the first free cell it finds - so a
        /// pickup lands there while the bag below is empty, and the bar you built fills up with
        /// stone. This looks at rows 1 and down first and only falls back to row 0 when there
        /// is nowhere else, which is the same answer vanilla would have given anyway.
        /// </summary>
        internal static Vector2i FirstFreeBagCell(Inventory inventory, bool topFirst)
        {
            if (inventory == null)
                return new Vector2i(-1, -1);
            int width = inventory.GetWidth();

            for (int pass = 0; pass < 2; pass++)
            {
                // Pass one is the bag, pass two is the hotbar.
                int from = pass == 0 ? 1 : 0;
                int to = pass == 0 ? VisibleRows : 1;
                if (from >= to)
                    continue;

                if (topFirst)
                {
                    for (int y = from; y < to; y++)
                        for (int x = 0; x < width; x++)
                            if (inventory.GetItemAt(x, y) == null)
                                return new Vector2i(x, y);
                }
                else
                {
                    for (int y = to - 1; y >= from; y--)
                        for (int x = 0; x < width; x++)
                            if (inventory.GetItemAt(x, y) == null)
                                return new Vector2i(x, y);
                }
            }
            return new Vector2i(-1, -1);
        }

        /// <summary>The first free cell in the rows the player can see, or (-1,-1).</summary>
        internal static Vector2i FirstFreeVisibleCell(Inventory inventory, bool topFirst = true)
        {
            if (inventory == null)
                return new Vector2i(-1, -1);
            int width = inventory.GetWidth();
            if (topFirst)
            {
                for (int y = 0; y < VisibleRows; y++)
                    for (int x = 0; x < width; x++)
                        if (inventory.GetItemAt(x, y) == null)
                            return new Vector2i(x, y);
            }
            else
            {
                for (int y = VisibleRows - 1; y >= 0; y--)
                    for (int x = 0; x < width; x++)
                        if (inventory.GetItemAt(x, y) == null)
                            return new Vector2i(x, y);
            }
            return new Vector2i(-1, -1);
        }

        /// <summary>
        /// The equipment slot an item of this type belongs in, or null if it is not the sort of
        /// thing the panel wears. The shield slot is deliberately excluded: a shield sitting
        /// there is nominated, not worn, so nothing should drag a shield into it.
        /// </summary>
        internal static Slot HomeSlot(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
                return null;
            foreach (Slot slot in All)
                if (slot != null && slot.Kind == Kind.Equipment && slot.Type == item.m_shared.m_itemType)
                    return slot;
            return null;
        }

        internal static int CountItemsInSlotRows(Inventory inventory)
        {
            if (inventory == null)
                return 0;
            int n = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (item != null && IsSlotRow(item.m_gridPos))
                    n++;
            return n;
        }

    }
}
