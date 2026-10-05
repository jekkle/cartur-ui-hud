using HarmonyLib;
using UnityEngine;

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
            Smaller(__instance.m_elementPrefab);
        }

        /// <summary>
        /// The skill names drew about twice the size of every other Cartur panel's text (both panel
        /// reviews, 2026-10-05). Measured on the row prefab: name is TMP 16 with auto-size up to 25,
        /// so it grows to 25; description auto 1-16, level and bonus a fixed 16. Auto-size is capped
        /// at 16, which leaves everything else as it was. Idempotent, so a second Awake changes nothing.
        /// </summary>
        private static void Smaller(GameObject row)
        {
            if (row == null)
                return;
            foreach (TMPro.TMP_Text t in row.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (t.enableAutoSizing && t.fontSizeMax > 16f)
                    t.fontSizeMax = 16f;
        }

        [HarmonyPatch(typeof(TextsDialog), "Awake")]
        [HarmonyPostfix]
        private static void Texts(TextsDialog __instance)
        {
            Skin.Apply(__instance.transform, "compendium");
            Skin.Apply(__instance.m_elementPrefab?.transform, "compendium row prefab");
        }

        /// <summary>
        /// The compendium's selected entry is a solid orange bar; every other list here selects in a
        /// see-through gold (review, 2026-10-04). FillTextList builds the rows, each with a child
        /// "selected" it switches on for the open entry (read off the IL), so that child is recoloured.
        /// </summary>
        [HarmonyPatch(typeof(TextsDialog), "FillTextList")]
        [HarmonyPostfix]
        private static void TextsSelection(TextsDialog __instance)
        {
            if (__instance.m_listRoot == null)
                return;
            foreach (UnityEngine.Transform row in __instance.m_listRoot)
                foreach (UnityEngine.UI.Image img in row.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                    if (img.name == "selected" && img.color != CraftingBoard.Rim)
                        CraftingBoard.Bronze(img);
        }
    }
}
