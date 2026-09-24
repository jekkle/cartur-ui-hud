using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// Part 12 of Cartur's UI: hosting Better Archery's quiver.
    ///
    /// The problem: Better Archery also grows the player's inventory to make room for its
    /// quiver. Two mods adding hidden rows to the same inventory fight, and its
    /// Player_SetInventorySize_Patch is the dangerous one - it replaces the original, moves
    /// every item at or below its own quiver row, and calls ItemDrop.DropItem on anything it
    /// cannot fit back. On a second login with a full pack, that is worn armour on the floor.
    ///
    /// The fix, and why it is one line rather than sixty
    /// ------------------------------------------------
    /// Better Archery already knows how to share an inventory. Every one of its inventory and
    /// UI patches begins:
    ///
    ///     if (!ConfigQuiverEnabled.Value || isInventoryExpansionModPresent) return true;
    ///
    /// and `isInventoryExpansionModPresent` is a cached lookup for Equipment and Quick Slots or
    /// Extended Player Inventory. That is the whole reason it coexists with EAQS: it stands
    /// down and lets the host provide the cells. The cache is a private static int, `eaqsPresent`,
    /// -1 until first read. Setting it to 1 makes Better Archery treat this mod as the
    /// inventory expansion it already knows how to yield to.
    ///
    /// Then the only other thing it needs is the row number: its quiver is "three ammo cells at
    /// x 0, 1, 2 of BetterArchery.QuiverRowIndex", a public static int. Point that at our
    /// quiver slots and its own logic - which arrows a bow may draw, the quiver HUD, the
    /// "wrong item" and "cannot unequip a full quiver" rules - keeps working against cells we
    /// own. Those last two are not behind the gate, and they are correct once the row is right.
    ///
    /// This file used to unpatch five of its patches by class name, copying what Equipment and
    /// Quick Slots does. That was wrong twice over: EAQS only needs it as belt and braces,
    /// because merely existing already trips the gate for it - and the list was written against
    /// Better Archery 1.9.8x, while the installed 2.0.1 has more inventory patches than that
    /// list names. Unpatching by name has to be kept in step with someone else's refactoring
    /// forever. The gate does not.
    /// </summary>
    internal static class QuiverCompat
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string Guid = "ishid4.mods.betterarchery";

        /// <summary>
        /// Index 16 - the start of the third hidden row, so x is 0, 1, 2 across the three
        /// quiver slots. Better Archery assumes its cells are the first three of a row.
        /// </summary>
        internal const int FirstSlot = 16;

        private static FieldInfo s_quiverRowIndex;
        private static Func<bool> s_isQuiverEquipped;

        internal static bool Hosted { get; private set; }

        internal static void Init()
        {
            PluginInfo s_plugin;
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out s_plugin) || s_plugin?.Instance == null)
                return;

            Type type = s_plugin.Instance.GetType().Assembly.GetType("BetterArchery.BetterArchery");
            if (type == null)
            {
                Log.LogWarning("Better Archery is loaded but BetterArchery.BetterArchery could not be resolved"
                    + " - its quiver may fight the equipment and quick slots.");
                return;
            }

            FieldInfo gate = AccessTools.Field(type, "eaqsPresent");
            s_quiverRowIndex = AccessTools.Field(type, "QuiverRowIndex");
            MethodInfo equipped = AccessTools.Method(type, "IsQuiverEquipped");
            if (equipped != null && equipped.IsStatic)
                s_isQuiverEquipped = Delegate.CreateDelegate(typeof(Func<bool>), equipped, false) as Func<bool>;

            if (gate == null || gate.FieldType != typeof(int)
                || s_quiverRowIndex == null || s_quiverRowIndex.FieldType != typeof(int)
                || s_isQuiverEquipped == null)
            {
                Log.LogWarning("Better Archery " + s_plugin.Metadata.Version
                    + " does not have the members this was written against"
                    + " (eaqsPresent, QuiverRowIndex, IsQuiverEquipped)."
                    + " Its quiver is left alone, and it may fight the equipment and quick slots"
                    + " - turn one of the two off.");
                return;
            }

            // The line that does the work. Everything of Better Archery's that would resize the
            // inventory, lay out cells, count free space or tidy a gravestone now stands down,
            // exactly as it does when Equipment and Quick Slots is installed.
            gate.SetValue(null, 1);

            Hosted = true;
            SyncRow();
            Log.LogInfo("Better Archery " + s_plugin.Metadata.Version
                + ": stood down via its own inventory-expansion gate; its quiver is hosted in"
                + " three ammo slots of the equipment panel, row " + Slots.All[FirstSlot].GridPos.y);
        }

        /// <summary>
        /// The quiver row moves when the player's visible rows do - a bigger pack pushes the
        /// hidden rows down - so the row number is written again whenever that happens, not
        /// only once at load.
        /// </summary>
        internal static void SyncRow()
        {
            if (!Hosted)
                return;
            int y = Slots.All[FirstSlot].GridPos.y;
            if (!(s_quiverRowIndex.GetValue(null) is int current) || current != y)
                s_quiverRowIndex.SetValue(null, y);
        }

        /// <summary>Only ammo, and only while a quiver is actually worn.</summary>
        internal static bool IsQuiverSlotActive()
        {
            if (!Hosted)
                return false;

            // Answered once a frame. Better Archery's IsQuiverEquipped allocates a list and a
            // LINQ closure every call, and this is asked once per slot as the grid lays out -
            // so without the cache it is three of those a frame with the inventory open.
            if (Time.frameCount == s_frame)
                return s_active;
            s_frame = Time.frameCount;
            s_active = Ask();
            return s_active;
        }

        private static int s_frame = -1;
        private static bool s_active;

        private static bool Ask()
        {
            try { return s_isQuiverEquipped(); }
            catch (Exception e)
            {
                if (!s_warned)
                {
                    s_warned = true;
                    Log.LogWarning("Better Archery's IsQuiverEquipped threw; the quiver slots stay hidden: " + e.Message);
                }
                return false;
            }
        }

        private static bool s_warned;

        /// <summary>
        /// Stops Better Archery's `ba` console command throwing the worn kit on the floor.
        ///
        /// Its Terminal patch tidies up by dropping anything at or below
        /// GetBaseInventoryRows(), which is QuiverRowIndex - 1 - one row above the quiver,
        /// because Better Archery's own layout puts the quiver immediately under the visible
        /// grid. Ours has three hidden rows, so the row it calls "the first one that should not
        /// exist" is in fact the equipment row, and `ba drop` would drop every piece of armour
        /// being worn.
        ///
        /// No arrangement of our slots makes that arithmetic true, because it assumes exactly
        /// one hidden row. So the answer is corrected instead: reporting the full height means
        /// nothing is ever below it, which is the truth here - none of our hidden cells are
        /// stray items needing rescue.
        ///
        /// Its other three callers all sit inside patches behind the stand-down gate, so this
        /// only ever reaches the Terminal one. Patched rather than unpatched because `ba` is
        /// one command with several actions, and the rest of them are none of our business.
        /// </summary>
        [HarmonyPatch]
        internal static class BaseRowsPatch
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(AccessTools.TypeByName("BetterArchery.BetterArchery"), "GetBaseInventoryRows");

            private static bool Prepare() => TargetMethod() != null;

            private static void Postfix(ref int __result)
            {
                if (Hosted)
                    __result = Slots.FullHeight;
            }
        }
    }
}
