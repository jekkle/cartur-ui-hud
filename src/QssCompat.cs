using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// Quick Stack Store (goldenrevolver.quick_stack_store, read off 1.4.15) next to this mod's slot
    /// rows. Two clashes, both reported on Nexus by crysis2142 (2026-10-09):
    ///
    /// 1. QSS asks CompatibilitySupport.IsEquipOrQuickSlot before quick stacking, store-all and sort,
    ///    and IsEquipSlot before restocking, and only knows the slot mods it names (Randy's, Azu's,
    ///    Comfy's, ...). Our hidden rows looked like bag space, so P put the quick slots' contents
    ///    into chests. Both are postfixed to say yes for our rows. IsEquipSlot says yes only for the
    ///    equipment and shield slots, so food in a quick slot and arrows in the quiver still restock.
    ///
    /// 2. TrashModule.TrashItemsPatches.UpdateTrashCanUI builds its trash can once per session as
    ///    Instantiate(m_player.transform.Find("Armor")) and then looks for ac_text and armor_icon
    ///    inside it. InventoryScreen moves Armor into the side column, so the Find is null and
    ///    Instantiate throws on every inventory open. Its button code anchors on Find("Weight") the
    ///    same way, which threw next (pilot). While QSS's Show postfixes run, both are lent back to
    ///    the player panel; the clone, which QSS renames "Trash", then joins the side column.
    /// </summary>
    internal static class QssCompat
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        internal const string Guid = "goldenrevolver.quick_stack_store";

        internal static bool Present => Chainloader.PluginInfos.ContainsKey(Guid);

        internal static void Init(Harmony harmony)
        {
            if (!Present)
            {
                Log.LogInfo("Quick Stack Store not present - nothing to do");
                return;
            }

            Type compat = AccessTools.TypeByName("QuickStackStore.CompatibilitySupport");
            MethodInfo equipOrQuick = compat != null ? AccessTools.Method(compat, "IsEquipOrQuickSlot") : null;
            MethodInfo equip = compat != null ? AccessTools.Method(compat, "IsEquipSlot") : null;
            if (equipOrQuick != null && equip != null)
            {
                harmony.Patch(equipOrQuick, postfix: new HarmonyMethod(typeof(QssCompat), nameof(AnySlot)));
                harmony.Patch(equip, postfix: new HarmonyMethod(typeof(QssCompat), nameof(EquipOnly)));
            }
            else
                Log.LogWarning("Quick Stack Store: slot checks not found - it may move items out of the slot rows");

            // Around QSS's own InventoryGui.Show postfix (HarmonyPriority 300, which builds the can),
            // not on UpdateTrashCanUI: that method is small enough to be inlined into the postfix,
            // and a patch on it never ran (pilot, 2026-10-10 - the Instantiate still threw). 301 lends
            // after InventoryScreen.Column (400) has seated Armor and before QSS reads it; Last
            // returns it. The trash-can toggle in QSS's config calls UpdateTrashCanUI directly and is
            // not covered - logging out and back in rebuilds it through Show.
            MethodInfo show = AccessTools.Method(typeof(InventoryGui), "Show");
            harmony.Patch(show, postfix: new HarmonyMethod(typeof(QssCompat), nameof(LendArmor)) { priority = 301 });
            harmony.Patch(show, postfix: new HarmonyMethod(typeof(QssCompat), nameof(ReturnArmor))
                { priority = Priority.Last, after = new[] { Guid } });

            Log.LogInfo("Quick Stack Store: slot rows marked as slots, trash can built from the side column's Armor");
        }

        private static void AnySlot(Vector2i itemPos, ref bool __result)
        {
            if (!__result && Slots.At(itemPos) != null)
                __result = true;
        }

        private static void EquipOnly(Vector2i itemPos, ref bool __result)
        {
            Slots.Slot slot = Slots.At(itemPos);
            if (!__result && slot != null && (slot.Kind == Slots.Kind.Equipment || slot.Kind == Slots.Kind.Shield))
                __result = true;
        }

        // Armor and Weight, each with the box it was seated in and its place there.
        private static readonly System.Collections.Generic.List<(Transform piece, Transform home, int index)> s_lent =
            new System.Collections.Generic.List<(Transform, Transform, int)>();

        /// <summary>
        /// QSS's two Show postfixes find "Armor" (trash can, favouriting button anchor) and "Weight"
        /// (mini button anchor) as direct children of the player panel. World position is kept both
        /// ways, so the anchors QSS measures are where the boxes really are on screen.
        /// </summary>
        private static void LendArmor(InventoryGui __instance)
        {
            Transform panel = __instance != null && __instance.m_player != null ? __instance.m_player.transform : null;
            if (panel == null)
                return;
            foreach (TMPro.TMP_Text text in new[] { __instance.m_armor, __instance.m_weight })
            {
                Transform piece = text != null ? text.transform.parent : null;
                if (piece == null || piece.parent == panel || piece.parent == null)
                    continue;
                s_lent.Add((piece, piece.parent, piece.GetSiblingIndex()));
                piece.SetParent(panel, true);
            }
        }

        private static void ReturnArmor()
        {
            foreach ((Transform piece, Transform home, int index) in s_lent)
            {
                if (piece == null || home == null)
                    continue;
                piece.SetParent(home, true);
                piece.SetSiblingIndex(index);
            }
            s_lent.Clear();
        }
    }
}
