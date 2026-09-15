using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    // Part 1 of Cartur's UI: the HUD. Health, stamina, eitr and adrenaline bars, three food
    // boxes and the guardian power box, re-skinned onto the objects vanilla Hud already builds.
    // See HudSkin for why almost none of this needs code.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jekkle.valheim.carturuihud";
        public const string PluginName = "Carturs UI - HUD";
        public const string PluginVersion = "0.2.0";

        private void Awake()
        {
            try
            {
                HudSkin.Log = Logger;
                HudLayout.Log = Logger;

                if (!AssetLoader.LoadTextures(Logger))
                {
                    Logger.LogWarning("assets missing from " + AssetLoader.AssetsDir + " - HUD left vanilla");
                    return;
                }

                ConfigEntry<KeyboardShortcut> editKey = Config.Bind(
                    "Layout", "editKey", new KeyboardShortcut(KeyCode.F6),
                    "Toggles layout edit mode: drag a piece to move it, wheel over it to scale it. Saved on exit.");
                HudLayout.Init(Config, editKey);

                HudDump.Log = Logger;
                HudSkin.s_dumpKey = Config.Bind(
                    "Layout", "dumpKey", new KeyboardShortcut(KeyCode.F7),
                    "Diagnostic: writes BepInEx/CarturUIHud_bars.txt describing the bars as they currently are.");

                // A mod that throws during load takes the whole chainloader with it.
                var harmony = new Harmony(PluginGuid);
                harmony.PatchAll(typeof(HudSkin));
                harmony.PatchAll(typeof(VanillaBars));
                harmony.PatchAll(typeof(EditInputBlock));
            }
            catch (Exception e)
            {
                Logger.LogWarning("patch failed, HUD left vanilla: " + e);
            }
        }
    }
}
