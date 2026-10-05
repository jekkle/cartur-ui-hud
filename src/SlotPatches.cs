using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// Part 12 of Cartur's UI: the patches that make the three hidden slot rows behave.
    ///
    /// Root cause of everything in this file: the slots are real inventory cells, so the game
    /// treats them as inventory. Left alone it would drop picked-up stone into the helmet slot,
    /// report the pack as having 24 more free cells than it has, and - on any call to
    /// Player.SetInventorySize - drop every worn item on the floor, because SetInventorySize
    /// shortens the grid to the visible rows and then calls DropInvalidItems, which drops
    /// anything below the grid. That last one is the reason the height is restored in a Prefix
    /// rather than tidied up afterwards.
    ///
    /// Everything here is a Prefix or a Postfix. No transpilers.
    /// </summary>
    internal static class SlotPatches
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        // Private in the stock DLLs this builds against. Looked up once; null-checked at every
        // use, because a field found today can vanish in a game update.
        private static readonly MethodInfo s_changed =
            AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });
        private static readonly FieldInfo s_isLoading = AccessTools.Field(typeof(Player), "m_isLoading");

        /// <summary>
        /// Says out loud which of the private members could not be found. A null lookup is a
        /// patch that does nothing rather than a crash, so without this a game update that
        /// renames one of these would show up as "the slots stopped working" and nothing in the
        /// log. Every use of them is already null-guarded; this is only so the cause is legible.
        /// </summary>
        internal static void CheckLookups()
        {
            Report(s_changed != null, "Inventory.Changed", "items moved between slots will not refresh the panel");
            Report(s_isLoading != null, "Player.m_isLoading", "slots may be tidied mid-load");
            Report(s_dragItem != null, "InventoryGui.m_dragItem", "dropping into a slot will not equip");
            Report(s_dragInventory != null, "InventoryGui.m_dragInventory", "dropping into a slot will not equip");
            Report(EquipmentPanel.ElementsFound, "InventoryGrid.m_elements",
                "the slot cells cannot be found, so there will be no equipment panel");
            Report(GamepadSlots.SelectionFound, "InventoryGrid.m_selected",
                "a controller cannot move onto the slots");
            Report(QuickSlots.TakeInputFound, "Player.TakeInput",
                "the quick slot hotkeys stay off, rather than firing when they should not");
        }

        private static void Report(bool found, string member, string consequence)
        {
            if (!found)
                Log.LogWarning(member + " not found in this build of the game - " + consequence);
        }

        private static void Changed(Inventory inventory) =>
            s_changed?.Invoke(inventory, new object[] { false, false });

        private static bool IsLoading(Player player) =>
            s_isLoading?.GetValue(player) as bool? ?? false;

        private static bool IsPlayerInventory(Inventory inventory) =>
            inventory != null && inventory == Slots.PlayerInventory;

        // ---- keeping the inventory three rows taller than it looks ----------------------

        [HarmonyPatch(typeof(Player), "Awake")]
        [HarmonyPostfix]
        private static void Awake(Player __instance)
        {
            __instance.GetInventory()?.SetHeight(Slots.FullHeight);
        }

        /// <summary>
        /// The game sets the grid to the visible row count. We record that as the new base and
        /// put the hidden rows back afterwards, so the slot region follows a bigger pack rather
        /// than being overwritten by it. The Prefix runs high so the new row count is recorded
        /// before anything else reacts to it; the Postfix runs low so the hidden rows go back
        /// on after any other mod has finished resizing.
        /// </summary>
        [HarmonyPatch(typeof(Player), "SetInventorySize")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static void SetInventorySizePre(Player __instance, int rows)
        {
            if (__instance != Player.m_localPlayer)
                return;

            RememberSlots();
            int before = Slots.VisibleRows;
            Slots.SetBaseRows(Mathf.Clamp(rows, 1, 9));
            MoveSlotRows(__instance.GetInventory(), before, Slots.VisibleRows - before);
        }

        /// <summary>
        /// Carries the worn kit down with the slot region when the visible rows change.
        ///
        /// The slots are cells of the hidden rows, and where those rows ARE is VisibleRows. Grow
        /// the pack by a row and the region moves down one, but the items sitting in it do not -
        /// so the shield, the quiver and the three quick slot foods stayed at the old row, which
        /// the player could now see, and the panel came up empty. Cartur got a bag row and found
        /// his shield and his food in it. Nothing had moved: the window had.
        ///
        /// Run from the SetInventorySize prefix, so it happens before the game shortens the grid
        /// and calls DropInvalidItems - on a shrink the kit has to be out of the doomed rows
        /// before that runs, or it lands on the floor.
        ///
        /// Anything that is not ours already sitting in the destination is pushed into the first
        /// free visible cell; Validate would do that on the next change anyway, but doing it here
        /// keeps a swap from stacking two items in one cell for a frame.
        /// </summary>
        private static void MoveSlotRows(Inventory inventory, int fromRow, int delta)
        {
            if (inventory == null || delta == 0)
                return;

            var slotItems = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                int y = item.m_gridPos.y;
                if (y >= fromRow && y < fromRow + Slots.HiddenRows)
                    slotItems.Add(item);
            }
            if (slotItems.Count == 0)
                return;

            foreach (ItemDrop.ItemData item in slotItems)
            {
                Vector2i to = new Vector2i(item.m_gridPos.x, item.m_gridPos.y + delta);
                ItemDrop.ItemData sitting = inventory.GetItemAt(to.x, to.y);
                if (sitting != null && !slotItems.Contains(sitting))
                {
                    Vector2i free = Slots.FirstFreeVisibleCell(inventory);
                    if (free.x >= 0)
                        sitting.m_gridPos = free;
                }
                item.m_gridPos = to;
            }

            Log.LogInfo("visible rows " + fromRow + " -> " + (fromRow + delta) + ", moved "
                + slotItems.Count + " worn items with the slot region");
            Changed(inventory);
        }

        [HarmonyPatch(typeof(Player), "SetInventorySize")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void SetInventorySizePost(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
                return;
            __instance.GetInventory()?.SetHeight(Slots.FullHeight);
            CheckSlotsKept();
        }

        /// <summary>
        /// What each slot held when the resize started. Taken in the prefix and checked in the
        /// postfix, which is after the original has run - so it also catches anything another
        /// mod moves while reacting to the resize. A backpack's dead-cell sweep did exactly
        /// that: it read the shield at its new cell as a cell the bag did not own and moved it
        /// into the grid.
        ///
        /// Silent when nothing changed, which is the normal case. It only ever speaks to name a
        /// slot that lost its item, because that is the failure nobody can see coming - the
        /// panel just looks empty and the item is somewhere in the bag.
        /// </summary>
        private static readonly Dictionary<string, string> s_heldBefore = new Dictionary<string, string>();

        private static void RememberSlots()
        {
            s_heldBefore.Clear();
            foreach (Slots.Slot slot in Slots.All)
            {
                if (slot == null)
                    continue;
                ItemDrop.ItemData item = slot.Item;
                s_heldBefore[slot.Id] = item?.m_shared?.m_name ?? "";
            }
        }

        private static void CheckSlotsKept()
        {
            if (s_heldBefore.Count == 0)
                return;

            string lost = null;
            foreach (Slots.Slot slot in Slots.All)
            {
                if (slot == null || !s_heldBefore.TryGetValue(slot.Id, out string was) || was == "")
                    continue;
                string now = slot.Item?.m_shared?.m_name ?? "";
                if (now == was)
                    continue;
                lost = (lost == null ? "" : lost + ", ") + slot.Label + " lost " + was
                       + (now == "" ? "" : " and holds " + now);
            }
            s_heldBefore.Clear();

            if (lost != null)
                Log.LogWarning("the resize moved worn kit out of its slots: " + lost);
        }

        /// <summary>
        /// The visible row count belongs to the character, not to the session. It is stored on
        /// the player as the "invrows" unique key, and the game only calls SetInventorySize
        /// when that key exists - so a character that has never had a bigger pack would
        /// otherwise silently inherit the rows of whoever was played before it, and the slot
        /// region would sit in the wrong place.
        /// </summary>
        [HarmonyPatch(typeof(Player), "Load")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        private static void Load(Player __instance)
        {
            int rows = 4;
            if (__instance.TryGetUniqueKeyValue("invrows", out string value) && int.TryParse(value, out int stored))
                rows = Mathf.Clamp(stored, 1, 9);
            Slots.SetBaseRows(rows);
        }

        /// <summary>
        /// DropInvalidItems drops every item whose grid position is outside the grid, and
        /// Player.SetInventorySize calls it immediately after shortening the grid. Without this
        /// the whole worn kit hits the floor. This is the one patch in the mod whose absence
        /// loses items, which is why it restores the height before the original runs instead of
        /// trying to catch the fallout.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), "DropInvalidItems")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static void DropInvalidItems(Humanoid __instance)
        {
            if (__instance == Player.m_localPlayer)
                __instance.GetInventory()?.SetHeight(Slots.FullHeight);
        }

        /// <summary>
        /// MoveInventoryToGrave copies the source grid's height onto the grave, so as long as
        /// the height is right when a player dies the worn kit lands in the grave at the same
        /// positions. Belt and braces for the same reason as above: this one is about losing
        /// items, so it does not get to rely on Update having run this frame.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), "MoveInventoryToGrave")]
        [HarmonyPrefix]
        private static void MoveInventoryToGrave(Inventory original)
        {
            if (IsPlayerInventory(original))
                original.SetHeight(Slots.FullHeight);
        }

        // ---- the hidden rows are not free space -----------------------------------------

        /// <summary>
        /// Where a new item goes. Two corrections to vanilla's answer, both for the player's
        /// own inventory and nothing else:
        ///
        ///   1. FindEmptySlot scans the whole grid, and scanning bottom-first it hands out a
        ///      hidden slot cell before any visible one - so the first thing picked up would
        ///      land in the helmet slot.
        ///   2. Row 0 is the hotbar, and vanilla fills it first. Cartur's call: the bag fills
        ///      before the bar, so a carefully built hotbar does not fill up with stone.
        ///
        /// Both are answered by one finder. When there is genuinely nowhere it returns (-1,-1),
        /// which is what the callers already handle - the item stays where it was.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), "FindEmptySlot")]
        [HarmonyPostfix]
        private static void FindEmptySlot(Inventory __instance, bool topFirst, ref Vector2i __result)
        {
            if (!IsPlayerInventory(__instance))
                return;

            Vector2i vanilla = __result;
            __result = Slots.FirstFreeBagCell(__instance, topFirst);
#if DIAGNOSTICS
            Log.LogInfo(string.Format(
                "slotfind: vanilla=({0},{1}) -> ours=({2},{3})  topFirst={4}  baseRows={5} visible={6} full={7} invH={8} invW={9}",
                vanilla.x, vanilla.y, __result.x, __result.y, topFirst,
                Slots.BaseRows, Slots.VisibleRows, Slots.FullHeight,
                __instance.GetHeight(), __instance.GetWidth()));
#endif
        }

        /// <summary>
        /// Both of these are grid area minus item count, so the slot rows inflate them by 24
        /// cells and the game thinks the pack has room it has not got. Recounted over the
        /// visible rows.
        ///
        /// </summary>
        [HarmonyPatch(typeof(Inventory), "GetEmptySlots")]
        [HarmonyPostfix]
        private static void GetEmptySlots(Inventory __instance, ref int __result)
        {
            if (IsPlayerInventory(__instance))
                __result = Mathf.Max(0, __result - Slots.HiddenCells + Slots.CountItemsInSlotRows(__instance));
        }

        [HarmonyPatch(typeof(Inventory), "HaveEmptySlot")]
        [HarmonyPostfix]
        private static void HaveEmptySlot(Inventory __instance, ref bool __result)
        {
            if (IsPlayerInventory(__instance) && __result)
                __result = __instance.GetEmptySlots() > 0;
        }

        /// <summary>
        /// The same inflated capacity, and this one is not cosmetic. Player.AutoPickup asks
        /// CanAddItem before deciding to pull an item towards you, so with a full visible grid
        /// it drags the item to your feet every FixedUpdate, fails the pickup, and prints
        /// "$msg_noroom" each time. Vanilla's behaviour with a genuinely full pack is to leave
        /// the item alone and say nothing.
        ///
        /// An earlier comment here argued this could be left alone because the AddItem that
        /// follows would fail safely. That was true about item loss and wrong about everything
        /// else. Vanilla's own arithmetic is reused, with the empty-cell term corrected -
        /// FindFreeStackSpace is public, so nothing has to be re-derived.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), "CanAddItem", new[] { typeof(ItemDrop.ItemData), typeof(int) })]
        [HarmonyPostfix]
        private static void CanAddItem(Inventory __instance, ItemDrop.ItemData item, int stack, ref bool __result)
        {
            if (!__result || !IsPlayerInventory(__instance) || item?.m_shared == null)
                return;
            // Vanilla's own default: a stack of -1 or less means "all of it". Kept in a local
            // rather than written back onto the parameter - the original has already run, so
            // assigning to it would do nothing but earn a Harmony003 warning.
            int wanted = stack > 0 ? stack : item.m_stack;
            __result = __instance.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel)
                       + __instance.GetEmptySlots() * item.m_shared.m_maxStackSize >= wanted;
        }

        // ---- Stack all ---------------------------------------------------------------------

        /// <summary>
        /// "Stack all" moves anything from your pack that already has a stack in the container.
        /// Armour does not stack, but quick slot food and arrows do - so without this, one
        /// click at a chest empties the quick slots into it.
        ///
        /// The slot items are lifted out of the inventory list for the duration of the call and
        /// put back afterwards, which is what Equipment and Quick Slots did. It looks blunt,
        /// and the alternative is worse: StackAll's own loop is a transpiler target, and a
        /// Prefix that tried to filter it would have to re-implement the stacking rules. This
        /// touches the list, not the logic.
        ///
        /// The Finalizer, not a Postfix, is what puts them back: if anything in StackAll throws
        /// the items must still come home, and a Postfix does not run on a thrown exception.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), "StackAll")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static void StackAllPre(Inventory fromInventory)
        {
            if (!IsPlayerInventory(fromInventory))
                return;
            List<ItemDrop.ItemData> items = fromInventory.GetAllItems();
            for (int i = items.Count - 1; i >= 0; i--)
            {
                ItemDrop.ItemData item = items[i];
                if (item != null && Slots.IsSlotRow(item.m_gridPos))
                {
                    s_liftedFromSlots.Add(item);
                    items.RemoveAt(i);
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), "StackAll")]
        [HarmonyFinalizer]
        [HarmonyPriority(Priority.High)]
        private static void StackAllDone(Inventory fromInventory)
        {
            if (s_liftedFromSlots.Count == 0 || fromInventory == null)
                return;
            fromInventory.GetAllItems().AddRange(s_liftedFromSlots);
            s_liftedFromSlots.Clear();
            // StackAll raised Changed while the slot items were out of the list, so the cached
            // total weight was recomputed without the worn kit or the quick slot stacks in it.
            // Raising it again with everything back puts the carry weight right.
            Changed(fromInventory);
        }

        private static readonly List<ItemDrop.ItemData> s_liftedFromSlots = new List<ItemDrop.ItemData>();

        // ---- dropping an item into a slot -------------------------------------------------

        /// <summary>
        /// The drag itself is the game's; this only decides whether a drop is allowed and
        /// whether it should equip. Dropping a helmet on the head slot wears it. Dragging worn
        /// armour out of its slot into the grid takes it off. Anything that does not fit the
        /// slot is refused outright, so the item stays on the cursor rather than landing in a
        /// slot that Validate would immediately spit back out.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
        [HarmonyPrefix]
        private static bool OnSelectedItem(InventoryGui __instance, InventoryGrid grid, Vector2i pos)
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting())
                return true;

            ItemDrop.ItemData drag = DragItem(__instance);
            if (drag == null || DragInventory(__instance) == null)
                return true;

            bool intoPlayer = grid.GetInventory() == Slots.PlayerInventory;
            bool fromPlayer = DragInventory(__instance) == Slots.PlayerInventory;
            Slots.Slot target = intoPlayer ? Slots.At(pos) : null;
            Slots.Slot source = fromPlayer ? Slots.Of(drag) : null;

            if (target != null)
            {
                // An unused cell of the hidden rows - indices we do not fill - is never a
                // legal destination, and neither is a slot the item is the wrong type for.
                if (!target.Accepts(drag))
                    return false;   // silently: this fires on every nudge of a bad drag
                if (target.Kind == Slots.Kind.Equipment && !player.IsItemEquiped(drag))
                {
                    // Let the drop land first, then wear it: equipping moves the item itself,
                    // so doing it before the drop would equip an item that is still on the
                    // cursor. Validate pulls it into the slot once it is worn.
                    Remember(ref s_equip, pos, drag);
                }
            }

            // Pulling a worn item out of its slot and into the grid takes it off - armour out of
            // an equipment slot, and a raised shield out of the shield slot.
            //
            // The shield needs this for the same reason the armour does, read off
            // InventoryGui.OnSelectedItem: it unequips the dragged item, drops it, and then
            // re-equips it if it was equipped before and is still in the pack (the `flag`
            // branch). So a shield the slot raised comes out of the slot still raised, and
            // nothing lowers it again - ShieldSlot.UnequipItem only lowers the item the slot
            // still holds, which is now something else or nothing. The other ways out already
            // work: shift-click to a container goes through Modifier.Move, which calls
            // UnequipItem and does not re-equip, and a drop into a chest fails the
            // ContainsItem check in that branch.
            //
            // The position is copied to a local first: reading a field of a by-value struct
            // parameter emits ldflda, which Harmony's own analyser reports as modifying a
            // non-ref patch parameter (Harmony003). It is a read - pos is never written in this
            // method - but a warning on every build is how a real one gets missed.
            Vector2i cell = pos;
            if (source != null && (source.Kind == Slots.Kind.Equipment || source.Kind == Slots.Kind.Shield)
                && player.IsItemEquiped(drag)
                && intoPlayer && target == null && grid.GetInventory().GetItemAt(cell.x, cell.y) == null)
            {
                Remember(ref s_unequip, pos, drag);
            }

            return true;
        }

        /// <summary>
        /// Where a dragged item was headed, and what it was - not the item object itself.
        ///
        /// The object cannot be kept. Every drop goes through Inventory.MoveItemToThis, and for
        /// an empty target cell that calls AddItem(item, amount, x, y), which does
        /// `item.Clone()`, adds the clone, and leaves the original with a zero stack for
        /// MoveItemToThis to remove. The ItemData that was on the cursor is therefore in no
        /// inventory at all by the time the Postfix runs, and Humanoid.UseItem starts with
        /// `if (!inventory.ContainsItem(item)) return;` - so equipping it silently did nothing.
        ///
        /// Re-finding by position and m_shared is what Equipment and Quick Slots does, for the
        /// same reason. m_shared is the per-item-type object every copy of an item shares, so
        /// it identifies what landed without holding a reference to what left.
        /// </summary>
        private struct Landing
        {
            internal Vector2i Pos;
            internal ItemDrop.ItemData.SharedData Shared;   // null means nothing is pending
        }

        private static Landing s_equip;
        private static Landing s_unequip;

        private static void Remember(ref Landing landing, Vector2i pos, ItemDrop.ItemData item)
        {
            landing.Pos = pos;
            landing.Shared = item.m_shared;
        }

        private static ItemDrop.ItemData Landed(Landing landing)
        {
            if (landing.Shared == null)
                return null;
            Inventory inv = Slots.PlayerInventory;
            ItemDrop.ItemData item = inv?.GetItemAt(landing.Pos.x, landing.Pos.y);
            return item != null && item.m_shared == landing.Shared ? item : null;
        }

        [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
        [HarmonyPostfix]
        private static void OnSelectedItemPost()
        {
            ItemDrop.ItemData equip = Landed(s_equip);
            ItemDrop.ItemData unequip = Landed(s_unequip);
            s_equip = default(Landing);
            s_unequip = default(Landing);

            Player player = Player.m_localPlayer;
            if (player == null)
                return;
            if (equip != null)
                Slots.Equip(player, equip);
            if (unequip != null)
            {
                // Only if it really did leave a slot - a drop the game refused leaves it where
                // it was, and taking it off then would be wrong.
                Slots.Slot now = Slots.Of(unequip);
                if (now == null || (now.Kind != Slots.Kind.Equipment && now.Kind != Slots.Kind.Shield))
                    Slots.Unequip(player, unequip);
            }
        }

        // m_dragItem and m_dragInventory are private in the stock DLLs.
        private static readonly FieldInfo s_dragItem = AccessTools.Field(typeof(InventoryGui), "m_dragItem");
        private static readonly FieldInfo s_dragInventory = AccessTools.Field(typeof(InventoryGui), "m_dragInventory");

        private static ItemDrop.ItemData DragItem(InventoryGui gui) =>
            s_dragItem?.GetValue(gui) as ItemDrop.ItemData;

        private static Inventory DragInventory(InventoryGui gui) =>
            s_dragInventory?.GetValue(gui) as Inventory;

        private static string PosName(Vector2i pos)
        {
            Slots.Slot slot = Slots.At(pos);
            return slot != null ? "the " + slot.Label + " slot" : "an unused slot cell";
        }

        // ---- keeping the slots and the worn kit in step -----------------------------------

        private static bool s_dirty = true;

        [HarmonyPatch(typeof(Player), "OnInventoryChanged")]
        [HarmonyPostfix]
        private static void OnInventoryChanged(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
                return;
            s_dirty = true;
            // The three food diamonds on the HUD show the quick slots, and this is the only
            // thing that tells them a slot's contents changed. Equipment and Quick Slots
            // raised its own event for this; when that went, so did the only refresh, and the
            // diamonds held whatever was in the slots at login for the rest of the session.
            HudSkin.RefreshQuickSlots();
        }

        [HarmonyPatch(typeof(Humanoid), "SetupEquipment")]
        [HarmonyPostfix]
        private static void SetupEquipment(Humanoid __instance)
        {
            if (__instance == Player.m_localPlayer)
                s_dirty = true;
        }

        [HarmonyPatch(typeof(Player), "Update")]
        [HarmonyPostfix]
        private static void Update(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
                return;
            __instance.GetInventory()?.SetHeight(Slots.FullHeight);
            Validate(__instance);
        }

        /// <summary>
        /// Two rules, run only when something changed:
        ///
        ///   1. Anything sitting in a slot cell that the slot will not keep is moved back into
        ///      the visible grid. That covers armour the player took off from the hotbar, an
        ///      item another mod dropped into the region, and the unused cells of the hidden
        ///      rows.
        ///   2. Anything the player is wearing that has an equipment slot is pulled into it.
        ///      This is what makes equipping from the grid fill the panel, rather than needing
        ///      the panel to be the only way to equip.
        ///
        /// Skipped while the character is loading: positions are still being read off the save
        /// at that point and shuffling them mid-load is how items end up somewhere else.
        /// </summary>
        private static void Validate(Player player)
        {
            if (!s_dirty || IsLoading(player))
                return;
            s_dirty = false;

            Inventory inv = player.GetInventory();
            if (inv == null)
                return;

            bool moved = false;

            var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            foreach (ItemDrop.ItemData item in items)
            {
                if (!Slots.IsSlotRow(item.m_gridPos))
                    continue;
                Slots.Slot slot = Slots.At(item.m_gridPos);
                if (slot != null && slot.Keeps(item))
                    continue;
                Vector2i free = Slots.FirstFreeVisibleCell(inv);
                if (free.x < 0)
                    continue;      // no room; it stays put and is tried again next change
                item.m_gridPos = free;
                moved = true;
            }

            foreach (ItemDrop.ItemData item in items)
            {
                if (!Slots.IsWorn(item))
                    continue;
                Slots.Slot home = Slots.HomeSlot(item);
                if (home == null || item.m_gridPos == home.GridPos)
                    continue;
                ItemDrop.ItemData sitting = home.Item;
                if (sitting == null)
                {
                    item.m_gridPos = home.GridPos;
                    moved = true;
                }
                else if (!Slots.IsWorn(sitting))
                {
                    // Swap: the worn one takes the slot, the loose one takes its cell.
                    Vector2i was = item.m_gridPos;
                    item.m_gridPos = home.GridPos;
                    sitting.m_gridPos = was;
                    moved = true;
                }
            }

            if (moved)
                Changed(inv);
        }
    }
}
