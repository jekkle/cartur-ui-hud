using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// Part 6 of Cartur's UI: the build menu.
    ///
    /// Awake is the hook. Read off assembly_valheim: BuildUi.Awake instantiates one tag button
    /// during Awake itself (the favourites tag - Instantiate, GetComponent&lt;BuildUiTagButton&gt;,
    /// SetupFavorite), and it is parented as it is made, so a postfix walk of the whole object
    /// catches it along with everything authored into the prefab.
    ///
    /// Every other button is pooled: TryCreatePieceButtonFromPool and CreateTagButtonFromPool
    /// build them from m_pieceButtonPrefab and m_tagButtonPrefab on demand, long after this runs.
    /// Those two prefabs are skinned here so each clone is born with the look, which is the same
    /// trick the inventory uses for its grid element and recipe row prefabs.
    ///
    /// Both prefab fields are private on BuildUi - checked, not assumed - so they come through
    /// cached FieldInfo, null-checked, in case a game update renames them.
    /// </summary>
    internal static class BuildMenu
    {
        private static readonly FieldInfo s_pieceButtonPrefab = AccessTools.Field(typeof(BuildUi), "m_pieceButtonPrefab");
        private static readonly FieldInfo s_tagButtonPrefab = AccessTools.Field(typeof(BuildUi), "m_tagButtonPrefab");

        [HarmonyPatch(typeof(BuildUi), "Awake")]
        [HarmonyPostfix]
        private static void Dress(BuildUi __instance)
        {
            Canvas canvas = __instance.GetComponentInParent<Canvas>();
            float ppu = canvas != null ? canvas.referencePixelsPerUnit : 100f;

            Skin.Apply(__instance.transform, "build menu", ppu);
            Skin.Apply(Prefab(s_pieceButtonPrefab, __instance), "build piece button prefab", ppu);
            Skin.Apply(Prefab(s_tagButtonPrefab, __instance), "build tag button prefab", ppu);
        }

        private static Transform Prefab(FieldInfo field, BuildUi ui) =>
            (field?.GetValue(ui) as GameObject)?.transform;
    }
}
