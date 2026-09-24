using HarmonyLib;

namespace CarturUIHud
{
    /// <summary>
    /// Part 7 of Cartur's UI: the skills screen and the compendium.
    ///
    /// They share a file because they are the same screen twice: both are a panel with a scrolling
    /// list, both carry a public m_elementPrefab and m_listRoot, and both are built from the same
    /// named sprites the table already covers. Two files here would be one file written twice.
    ///
    /// Awake is the hook for the same reason as the settings screen: neither dialog exists until
    /// it is opened, and each is instantiated by whoever opens it, so Awake catches the instance
    /// that was just built without caring which caller made it.
    ///
    /// The row prefab is skinned separately because the list is empty at Awake - rows are made
    /// from the prefab later, in Setup, and a clone of an unskinned prefab is an unskinned row.
    ///
    /// Trophies are not here. There is no Trophies or TrophiesDialog type in assembly_valheim -
    /// the trophy list is InventoryGui's, and its m_trophieElementPrefab is already skinned in
    /// InventoryScreen.
    /// </summary>
    internal static class ListScreens
    {
        [HarmonyPatch(typeof(SkillsDialog), "Awake")]
        [HarmonyPostfix]
        private static void Skills(SkillsDialog __instance)
        {
            Skin.Apply(__instance.transform, "skills");
            Skin.Apply(__instance.m_elementPrefab?.transform, "skills row prefab");
        }

        [HarmonyPatch(typeof(TextsDialog), "Awake")]
        [HarmonyPostfix]
        private static void Texts(TextsDialog __instance)
        {
            Skin.Apply(__instance.transform, "compendium");
            Skin.Apply(__instance.m_elementPrefab?.transform, "compendium row prefab");
        }
    }
}
