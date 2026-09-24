using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace CarturUIHud
{
    /// <summary>
    /// Part 12 of Cartur's UI: putting your kit back on after a death.
    ///
    /// Emptying a gravestone hands back a pile of armour and leaves you standing in your
    /// underwear next to it. This re-equips what you were wearing when you died.
    ///
    /// How it knows which items were yours
    /// -----------------------------------
    /// Item type is not enough. "Re-equip any helmet you pick up" would grab loot off a corpse
    /// and wear it, which is not the behaviour being replaced. So the items are marked at the
    /// moment of death with the profile's player id, and the mark is spent on the way back in -
    /// read off Equipment and Quick Slots, which does the same thing with its own key.
    ///
    /// The mark lives in ItemData.m_customData, so it is saved with the item and survives the
    /// grave, a server restart, and another player picking the item up (their id will not
    /// match, so it does nothing for them).
    ///
    /// Carry weight items are the exception and need no mark: a belt is identified by what it
    /// does - an equip status effect that adds carry weight - so the Megingjord goes back on
    /// whoever is holding it, which is the point of the option.
    /// </summary>
    internal static class AutoEquip
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string WornKey = "cartur_wasworn";

        private static ConfigEntry<bool> s_armour;
        private static ConfigEntry<bool> s_weaponShield;
        private static ConfigEntry<bool> s_carryWeight;

        internal static void Init(ConfigFile config)
        {
            s_armour = config.Bind("Auto equip", "armourOnPickup", true,
                "Put armour back on when you pick up the kit you died in.");
            s_weaponShield = config.Bind("Auto equip", "weaponAndShieldOnPickup", true,
                "Draw the weapon and shield you died holding when you pick them back up.");
            s_carryWeight = config.Bind("Auto equip", "carryWeightOnPickup", true,
                "Put a carry weight item - the Megingjord, a belt - back on as soon as it is picked up, "
                + "whether or not you died in it.");
        }

        private static string PlayerId()
        {
            PlayerProfile profile = Game.instance?.GetPlayerProfile();
            return profile != null ? profile.GetPlayerID().ToString() : null;
        }

        // ---- marking, at the moment of death ----------------------------------------------

        /// <summary>
        /// A Prefix, because by the time OnDeath has finished the items have been moved into
        /// the grave and the hands have been emptied. Everything worn is marked, hands
        /// included, and the hands are read from LeftItem and RightItem rather than guessed at
        /// from item types - a torch, an atgeir and a shield are all "in a hand" and no type
        /// test would agree on that.
        /// </summary>
        [HarmonyPatch(typeof(Player), "OnDeath")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static void OnDeath(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
                return;
            string id = PlayerId();
            Inventory inv = __instance.GetInventory();
            if (id == null || inv == null)
                return;

            int marked = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item == null || !__instance.IsItemEquiped(item))
                    continue;
                item.m_customData[WornKey] = id;
                marked++;
            }
            Log.LogInfo("death: marked " + marked + " worn items to go back on when they are picked up");
        }

        // ---- spending the mark, on the way back in ----------------------------------------

        [HarmonyPatch(typeof(InventoryGui), "OnTakeAll")]
        [HarmonyPostfix]
        private static void OnTakeAll() => Restore();

        [HarmonyPatch(typeof(TombStone), "OnTakeAllSuccess")]
        [HarmonyPostfix]
        private static void OnTakeAllSuccess() => Restore();

        // There is deliberately no hook on Player.AutoPickup. It was tried, and it is called
        // unconditionally from FixedUpdate, so Restore ran every physics tick - copying the
        // whole inventory list each time, and, worse, re-equipping a carry weight item the
        // moment it was taken off, because the belt rule does not need a mark. Equipment and
        // Quick Slots hooks only these two, which is the right place: the kit comes back when
        // a container is emptied, not continuously.

        /// <summary>
        /// Walks what is now in the pack and wears anything that qualifies. The mark is removed
        /// whether or not the item is worn in the end, so a helmet you decided to keep in your
        /// pack does not jump onto your head every time you pick something up.
        ///
        /// Equipping is left to Slots.Equip, which goes through the game's own UseItem - so the
        /// equip animation plays and SlotPatches' validation pulls each item into its panel
        /// slot as it lands, rather than this having to place anything itself.
        /// </summary>
        private static void Restore()
        {
            Player player = Player.m_localPlayer;
            Inventory inv = player?.GetInventory();
            string id = PlayerId();
            if (player == null || inv == null || id == null || player.IsDead())
                return;

            // Copied, because equipping changes the inventory list underneath the loop.
            var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            foreach (ItemDrop.ItemData item in items)
            {
                if (item == null || player.IsItemEquiped(item))
                    continue;

                bool wasWorn = item.m_customData.TryGetValue(WornKey, out string owner) && owner == id;
                if (wasWorn)
                    item.m_customData.Remove(WornKey);

                if (Wanted(item, wasWorn))
                    Slots.Equip(player, item);
            }
        }

        private static bool Wanted(ItemDrop.ItemData item, bool wasWorn)
        {
            if (s_carryWeight.Value && AddsCarryWeight(item))
                return true;
            if (!wasWorn)
                return false;
            return IsHandHeld(item) ? s_weaponShield.Value : s_armour.Value;
        }

        /// <summary>
        /// A belt: something whose equip status effect raises max carry weight. Identified by
        /// effect rather than by name, so a modded belt works too.
        /// </summary>
        private static bool AddsCarryWeight(ItemDrop.ItemData item) =>
            item?.m_shared?.m_equipStatusEffect is SE_Stats stats && stats.m_addMaxCarryWeight > 0f;

        private static bool IsHandHeld(ItemDrop.ItemData item)
        {
            switch (item?.m_shared?.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Tool:
                    return true;
                default:
                    return false;
            }
        }
    }
}
