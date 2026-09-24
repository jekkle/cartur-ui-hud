using HarmonyLib;

namespace CarturUIHud
{
    /// <summary>
    /// Part 8 of Cartur's UI: the screens that are one walk each.
    ///
    /// None of these needs anything of its own - they are vanilla panels built from the sprites
    /// the table already names, so each is a single Apply on the root plus, where the screen has a
    /// list, the row prefab it clones. Six files would be six copies of the same four lines.
    ///
    /// Every hook below was read off the assembly rather than guessed:
    ///
    ///   StoreGui.Awake     15 instructions; m_listElement is the row it clones, public.
    ///   Chat.Awake         124 instructions and not one of them instantiates anything - it is
    ///                      Terminal.Awake, RPC registration and a SetActive - so the window is
    ///                      authored in the scene and is there to walk.
    ///   UnifiedPopup.Awake Show does more than fill text in - it calls ResetUI and one of
    ///                      ShowYesNo / ShowWarning / ShowTask / ShowCancelableTask /
    ///                      ShowTextEntry - but it instantiates nothing, it only SetActives a
    ///                      sub-panel that is already there. The walk includes inactive children,
    ///                      so Awake still sees all five.
    ///   FejdStartup.Start  Awake is 241 instructions and builds the menu; Start runs after it,
    ///                      so the walk sees what Awake made. m_worldListElement is the row.
    ///   EnemyHud.Awake     19 instructions, and the whole body is a single SetActive: it builds
    ///                      nothing. Every bar on screen is therefore a clone made later from one
    ///                      of the four prefabs, which is why the prefabs are what get skinned.
    ///
    /// The settings window is not here: SettingsScreen catches it on Settings.Awake, which covers
    /// both the pause menu's copy and the main menu's.
    ///
    /// The map and the minimap are deliberately not here either. Cartur's call: the map itself is
    /// not to be touched. If it gets anything it is a border around the outside, which is its own
    /// job and does not go through the skin walk.
    /// </summary>
    internal static class Screens
    {
        [HarmonyPatch(typeof(StoreGui), "Awake")]
        [HarmonyPostfix]
        private static void Store(StoreGui __instance)
        {
            Skin.Apply(__instance.transform, "store");
            Skin.Apply(__instance.m_listElement?.transform, "store row prefab");
        }

        [HarmonyPatch(typeof(Chat), "Awake")]
        [HarmonyPostfix]
        private static void ChatWindow(Chat __instance)
        {
            Skin.Apply(__instance.transform, "chat");
        }

        [HarmonyPatch(typeof(UnifiedPopup), "Awake")]
        [HarmonyPostfix]
        private static void Popup(UnifiedPopup __instance)
        {
            Skin.Apply(__instance.transform, "popup");
        }

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        [HarmonyPostfix]
        private static void MainMenu(FejdStartup __instance)
        {
            Skin.Apply(__instance.transform, "main menu");
            Skin.Apply(__instance.m_worldListElement?.transform, "world row prefab");
        }

        // RadialBase is the radial menu itself - it has no subclasses in the assembly, so this
        // one component is the whole of it. Awake is 27 instructions and its parts (element
        // container, cursor, highlighter) are serialized, so they are there to walk.
        [HarmonyPatch(typeof(Valheim.UI.RadialBase), "Awake")]
        [HarmonyPostfix]
        private static void Radial(Valheim.UI.RadialBase __instance)
        {
            Skin.Apply(__instance.transform, "radial");
        }

        [HarmonyPatch(typeof(EnemyHud), "Awake")]
        [HarmonyPostfix]
        private static void Enemies(EnemyHud __instance)
        {
            Skin.Apply(__instance.m_baseHud?.transform, "enemy hud prefab");
            Skin.Apply(__instance.m_baseHudBoss?.transform, "boss hud prefab");
            Skin.Apply(__instance.m_baseHudPlayer?.transform, "player hud prefab");
            Skin.Apply(__instance.m_baseHudMount?.transform, "mount hud prefab");
        }
    }
}
