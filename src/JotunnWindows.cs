using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// Jotunn's "Failed Connection" window (version mismatch) on Cartur's art.
    ///
    /// Read off Jotunn 2.30.2: ModCompatibility.LoadCompatWindow instantiates CompatibilityWindow out
    /// of its own "modcompat" bundle under GUIManager.CustomGUIFront when a connection is refused,
    /// then styles it with GUIManager - woodpanel_trophys on the panel, "button" on the three
    /// buttons. That is after every screen Skin walks and on a canvas none of them reach, so it
    /// stayed light wood. Both sprites are already in Skin's table, and Skin hands a kit window frame
    /// to AutoBoard, so one Skin.Apply on the window it returns is the whole change.
    ///
    /// Patched by hand like EnchantingScreen: Jotunn is a soft dependency, and the method is private.
    /// </summary>
    internal static class JotunnWindows
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string Guid = "com.jotunn.jotunn";

        internal static void Init(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.ContainsKey(Guid))
                return;
            MethodInfo load = AccessTools.Method(AccessTools.TypeByName("Jotunn.Utils.ModCompatibility"), "LoadCompatWindow");
            if (load == null)
            {
                Log.LogWarning("Jotunn ModCompatibility.LoadCompatWindow not found - its failed-connection window stays light wood");
                return;
            }
            harmony.Patch(load, postfix: new HarmonyMethod(AccessTools.Method(typeof(JotunnWindows), nameof(Skinned))));
        }

        // __result typed as MonoBehaviour: CompatibilityWindow is internal to Jotunn.
        private static void Skinned(MonoBehaviour __result)
        {
            if (__result == null)
                return;
            Canvas canvas = __result.GetComponentInParent<Canvas>();
            Skin.Apply(__result.transform, "jotunn compatibility window", canvas != null ? canvas.referencePixelsPerUnit : 100f);
        }
    }
}
