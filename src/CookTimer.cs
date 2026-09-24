using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// How long each thing on the fire has left, on the hover text. One line per full slot:
    /// the item, and the time until it is cooked - or, once it is, the time until it burns.
    ///
    /// Everything comes from the station itself. CookingStation keeps a slot's item name and
    /// its cooked time on the ZDO under "slot0", "slot1" ... - a string and a float on the same
    /// key, which is what GetSlot reads - and the target time is m_cookTime on the matching
    /// entry of m_conversion, which is public. Burning is m_cookTime * 2, read off
    /// UpdateCooking, and only when m_canOvercookItems.
    ///
    /// The slot holds the RAW item's name the whole time it cooks, and keeps holding it after
    /// it is done: UpdateCooking only writes a different name into the slot when the thing
    /// actually burns. That is what makes one lookup cover both halves of the countdown.
    ///
    /// Vanilla's own text is left exactly as it is and the lines are appended after it. The
    /// item name is appended as its localisation token, because the game localises this string
    /// after we hand it back.
    /// </summary>
    internal static class CookTimer
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        // m_nview is private on CookingStation; the slot data lives on its ZDO.
        private static readonly FieldInfo s_nview = AccessTools.Field(typeof(CookingStation), "m_nview");

        internal static bool Found => s_nview != null;

        [HarmonyPatch(typeof(CookingStation), "GetHoverText")]
        [HarmonyPostfix]
        private static void Timers(CookingStation __instance, ref string __result)
        {
            if (s_nview == null || __instance.m_slots == null)
                return;
            var nview = s_nview.GetValue(__instance) as ZNetView;
            if (nview == null || !nview.IsValid())
                return;
            ZDO zdo = nview.GetZDO();
            if (zdo == null)
                return;

            var text = new StringBuilder();
            for (int i = 0; i < __instance.m_slots.Length; i++)
            {
                string item = zdo.GetString("slot" + i, "");
                if (string.IsNullOrEmpty(item))
                    continue;
                text.Append("\n").Append(Line(__instance, item, zdo.GetFloat("slot" + i, 0f),
                    zdo.GetInt("slotstatus" + i, 0)));
            }

            if (text.Length > 0)
                __result += text.ToString();
        }

        private static string Line(CookingStation station, string item, float cooked, int status)
        {
            string name = Short(item);

            // Burnt is the station's own verdict, not something to work out: status 2, written
            // by UpdateCooking when it swaps the slot for m_overCookedItem.
            if (status == Burnt)
                return name + "  " + BurntLabel;

            // While it cooks, the slot holds the RAW item and the conversion is found by its
            // m_from. The moment it is done, UpdateCooking rewrites the slot with m_to - the
            // cooked item - so from then on the same conversion is found by its RESULT. That
            // second lookup is what was missing: without it a cooked steak matched nothing and
            // was called burnt.
            CookingStation.ItemConversion cooking = From(station, item);
            if (cooking != null && cooked < cooking.m_cookTime)
                return name + "  " + Clock(cooking.m_cookTime - cooked);

            CookingStation.ItemConversion done = cooking ?? To(station, item);
            if (done == null)
                return name + "  " + CookedLabel;

            if (!station.m_canOvercookItems)
                return name + "  " + CookedLabel;

            // Burning is m_cookTime * 2, read off UpdateCooking.
            return name + "  " + CookedLabel + ", " + BurnsIn + " "
                + Clock(Mathf.Max(0f, done.m_cookTime * 2f - cooked));
        }

        private const int Burnt = 2;

        /// <summary>
        /// A short name for the thing on the fire: "boar meat", "deer meat", "fish".
        ///
        /// The game's own name is localised here rather than left as a token, because the whole
        /// point is to shorten it, and a token cannot be edited after the fact. "Raw " and
        /// "Cooked " come off the front and the first letter goes down, which turns "Raw fish"
        /// into "fish" and "Deer meat" into "deer meat".
        ///
        /// One name needs help: vanilla calls boar meat "Raw meat" - the animal is not in the
        /// name at all - so that one is given outright. Everything else comes from the game and
        /// follows it, including anything a mod adds.
        ///
        /// The two prefixes are English. In another language nothing matches and the full name
        /// is used, which is the right way for this to fail.
        /// </summary>
        private static string Short(string prefab)
        {
            if (prefab == "RawMeat")
                return "Boar Meat";

            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            string token = drop?.m_itemData?.m_shared?.m_name;
            if (string.IsNullOrEmpty(token))
                return prefab;

            string name = Localization.instance != null ? Localization.instance.Localize(token) : token;
            foreach (string prefix in s_prefixes)
            {
                if (name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(prefix.Length);
                    break;
                }
            }
            return Titled(name.Length > 0 ? name : token);
        }

        /// <summary>
        /// Every word starts with a capital: "Boar Meat", "Deer Meat", "Neck Tail". Only the
        /// first letter of each word is touched, so anything the game already spells its own
        /// way in the middle of a word keeps it.
        /// </summary>
        private static string Titled(string text)
        {
            var built = new StringBuilder(text.Length);
            bool start = true;
            foreach (char c in text)
            {
                built.Append(start ? char.ToUpperInvariant(c) : c);
                start = c == ' ';
            }
            return built.ToString();
        }

        private static readonly string[] s_prefixes = { "Raw ", "Cooked " };

        /// <summary>The conversion that takes this item - it is still raw.</summary>
        private static CookingStation.ItemConversion From(CookingStation station, string item)
        {
            if (station.m_conversion == null)
                return null;
            foreach (CookingStation.ItemConversion c in station.m_conversion)
            {
                if (c != null && c.m_from != null && c.m_from.gameObject.name == item)
                    return c;
            }
            return null;
        }

        /// <summary>The conversion that produced this item - it is cooked and now burning.</summary>
        private static CookingStation.ItemConversion To(CookingStation station, string item)
        {
            if (station.m_conversion == null)
                return null;
            foreach (CookingStation.ItemConversion c in station.m_conversion)
            {
                if (c != null && c.m_to != null && c.m_to.gameObject.name == item)
                    return c;
            }
            return null;
        }

        private static string Clock(float seconds)
        {
            int whole = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return (whole / 60) + ":" + (whole % 60).ToString("00");
        }

        // No colour. Cartur's call - the line is plain hover text like everything around it,
        // and a countdown that changes colour as it runs was one more thing moving on screen.
        private const string BurnsIn = "burns in";
        private const string CookedLabel = "cooked";
        private const string BurntLabel = "burnt";
    }
}
