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
        public const string PluginVersion = "1.0.2";

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

                ConfigEntry<bool> editMode = Config.Bind(
                    "Layout", "editMode", false,
                    "Turn on to arrange the HUD: drag a piece to move it, wheel to size it, "
                    + "shift+wheel for a bar's length, ctrl+wheel and ctrl+drag for its frame. "
                    + "Turn off when done - the layout is saved either way.");
                HudLayout.Init(Config, editMode);

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
