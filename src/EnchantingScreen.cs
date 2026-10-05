using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// EpicLoot's enchanting table, in our art.
    ///
    /// Its window is not the game's - EpicLoot ships it in its own asset bundle and instantiates
    /// it - but it is built on vanilla's own sprites, which is why one Skin.Apply is the whole
    /// job and nothing here needs new art.
    ///
    /// Read out of that bundle rather than guessed at. EnchantingUI holds one child "Panel",
    /// 1120x700, drawing the sprite "woodpanel_large"; under it the seven tab buttons each draw
    /// "button". Both names are already in the skin table, so the walk swaps the background and
    /// the tabs and leaves everything else as EpicLoot drew it. The one panel piece that is
    /// EpicLoot's own, "TabBackground", is not in the table and is logged once as unmatched
    /// rather than touched.
    ///
    /// Patched by hand instead of with an attribute. EpicLoot is a soft dependency:
    /// [HarmonyPatch(typeof(EnchantingTableUI))] would need a compile-time reference to
    /// EpicLoot.dll and would throw on every install without it. AccessTools.TypeByName returns
    /// null instead and this stands down with a line in the log.
    ///
    /// Awake is the hook because the window is a single prefab: EnchantingTableUI.Awake is three
    /// instructions - it only sets the static instance - and it runs during the Instantiate that
    /// makes the window, so every child is already in place when the walk starts.
    /// </summary>
    internal static class EnchantingScreen
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string Guid = "randyknapp.mods.epicloot";
        private const string UiType = "EpicLoot_UnityLib.EnchantingTableUI";

        internal static void Init(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.ContainsKey(Guid))
            {
                Log.LogInfo("EpicLoot not present - enchanting table left alone");
                return;
            }

            Type type = AccessTools.TypeByName(UiType);
            MethodInfo awake = type != null ? AccessTools.Method(type, "Awake") : null;
            if (awake == null)
            {
                Log.LogWarning(UiType + ".Awake not found - enchanting table left vanilla");
                return;
            }

            harmony.Patch(awake, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(EnchantingScreen), nameof(Skinned))));

            // Each tab has its own board, so it follows the tab. EnchantingTableUI.Update is public and
            // returns early while the window is hidden (read off EpicLoot.dll), so this only runs while open.
            MethodInfo update = AccessTools.Method(type, "Update");
            if (update != null)
                harmony.Patch(update, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(EnchantingScreen), nameof(FollowTab))));
            else
                Log.LogWarning(UiType + ".Update not found - enchanting table keeps the Sacrifice board on every tab");
        }

        private static void FollowTab(MonoBehaviour __instance)
        {
            if (__instance == null)
                return;
            EpicBoards.EnchantingTab(__instance.transform);
            if (Time.frameCount % 15 == 0)
                EpicBoards.Rows(__instance.transform.Find("Panel"));   // rows are rebuilt as the lists refresh
        }

        /// <summary>
        /// __instance is typed as MonoBehaviour on purpose - the real type lives in a mod this
        /// one does not reference, and Harmony is happy with any base it can assign to.
        /// </summary>
        private static void Skinned(MonoBehaviour __instance)
        {
            if (__instance == null)
                return;

            Canvas canvas = __instance.GetComponentInParent<Canvas>();
            float ppu = canvas != null ? canvas.referencePixelsPerUnit : 100f;
            Skin.Apply(__instance.transform, "enchanting table", ppu);
            EpicBoards.Enchanting(__instance.transform);
        }
    }
}
