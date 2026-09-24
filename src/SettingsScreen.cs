using HarmonyLib;

namespace CarturUIHud
{
    /// <summary>
    /// Part 4 of Cartur's UI: the settings screen.
    ///
    /// Nothing here is special-cased. The settings window is built from the same sprites as the
    /// rest of the menus - woodpanel_settings for the frame, button and button_tab for the
    /// controls, text_field for the boxes - and the skin table already names all of them, so the
    /// whole screen is one walk.
    ///
    /// Awake is the hook because this screen does not exist until it is opened: Menu.OnSettings
    /// and FejdStartup.OnButtonSettings each Instantiate m_settingsPrefab, so patching either
    /// caller would miss the other, and patching Awake catches both - in game and at the main
    /// menu - on the instance that was just built.
    ///
    /// The gamepad key hints are left alone by the table rather than by a rule here: their icons
    /// are their own sprites and are not named in it, so the walk passes over them.
    /// </summary>
    internal static class SettingsScreen
    {
        [HarmonyPatch(typeof(Settings), "Awake")]
        [HarmonyPostfix]
        private static void Dress(Settings __instance)
        {
            Skin.Apply(__instance.transform, "settings");
        }
    }
}
