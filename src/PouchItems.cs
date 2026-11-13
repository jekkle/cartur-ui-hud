using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace CarturUIHud
{
    /// <summary>
    /// What the key pouch does beyond holding things: a belt or similar utility item in it is worn, and so shown
    /// on the character, while the one utility slot is free, and the wishbone's finder effect runs from the pouch.
    ///
    /// Humanoid has one utility field (m_utilityItem), so two belts overlap and only one can be worn. A belt
    /// dropped in the Utility slot equips through the game and replaces the pouch one, which stays in the pouch
    /// unworn. The wishbone is a utility item too but has nothing to show, so it never takes the slot: its
    /// status effect is added by hand while it sits in a pouch cell (the game applies equip effects by name hash
    /// and never stacks a second copy, so a worn wishbone and a pouch one do not double up). Ported from Rune
    /// UI's Wishbone.cs, which read the same game code.
    /// </summary>
    internal static class PouchItems
    {
        private static readonly FieldInfo UtilityField = AccessTools.Field(typeof(Humanoid), "m_utilityItem");
        private static int s_applied;

        internal static void Reset() => s_applied = 0;

        internal static bool IsFinder(ItemDrop.ItemData item) =>
            item?.m_shared != null
            && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility
            && item.m_shared.m_equipStatusEffect is SE_Finder;

        internal static void Sync(Player player)
        {
            if (player == null)
                return;
            List<ItemDrop.ItemData> held = Slots.All.Where(s => s != null && s.Kind == Slots.Kind.Pouch)
                .Select(s => s.Item).Where(i => i != null).ToList();

            if (UtilityField != null && UtilityField.GetValue(player) == null)
            {
                ItemDrop.ItemData spare = held.FirstOrDefault(i =>
                    i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility && !IsFinder(i) && !i.m_equipped);
                if (spare != null)
                    player.EquipItem(spare, true);
            }

            SEMan seman = player.GetSEMan();
            if (seman == null)
                return;
            ItemDrop.ItemData finder = held.FirstOrDefault(IsFinder);
            StatusEffect want = finder?.m_shared.m_equipStatusEffect;
            int wantHash = want != null ? want.NameHash() : 0;

            if (s_applied != 0 && s_applied != wantHash)
            {
                if (!WornProvides(player, s_applied))
                    seman.RemoveStatusEffect(s_applied, false);
                s_applied = 0;
            }
            if (want == null)
                return;
            if (!seman.HaveStatusEffect(wantHash))
                seman.AddStatusEffect(want, false, 0, 0f, -1);
            s_applied = wantHash;
        }

        private static bool WornProvides(Player player, int hash)
        {
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                StatusEffect se = item?.m_shared?.m_equipStatusEffect;
                if (se != null && item.m_equipped && se.NameHash() == hash)
                    return true;
            }
            return false;
        }
    }
}
