using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Part 10 of Cartur's UI: a border round the minimap and the map.
    ///
    /// Cartur's call, and the reason this is not in Screens: the map is not to be skinned. Nothing
    /// here reads or writes the map - no sprite swap, no colour, no tint. One Image is added
    /// beside it and that is all.
    ///
    /// Panel_thin has the fill inside its rule, so the centre patch is not drawn: the map shows
    /// through right up to the rule, and the map belongs to the same set as the panels.
    ///
    /// The rule is panel_thin's: the same frame with the knots taken off, drawn with fillCenter off
    /// so only the rule shows.
    ///
    /// Size and shape come from the map's own RawImage rect at runtime, not from a number written
    /// here. Whatever shape the map is, the border is that shape, grown outward by its own depth
    /// so no part of it lies over the map (see Hang).
    ///
    /// Sibling order matters: the border goes directly after the map image, so it draws over the
    /// map and under every later sibling.
    ///
    /// Whether that leaves the pins on top is NOT known. Sibling order lives in the scene, not in
    /// the assembly, so it cannot be read the way the rest of this was - and guessing it is how
    /// the map got a circle drawn on it. The indices are logged once instead: if a pin root comes
    /// out lower than the border, the pins are behind the rail and this needs the other order.
    /// </summary>
    internal static class MapBorder
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private static bool s_logged;

        private static string Index(Transform t) => t == null ? "none" : t.name + "#" + t.GetSiblingIndex();

        private const string SmallName = "CarturUIHud_MapBorderSmall";
        private const string LargeName = "CarturUIHud_MapBorderLarge";

        [HarmonyPatch(typeof(Minimap), "Start")]
        [HarmonyPostfix]
        private static void Place(Minimap __instance)
        {
            Hang(__instance.m_mapImageSmall, SmallName);
            Hang(__instance.m_mapImageLarge, LargeName);

            if (!s_logged)
            {
                s_logged = true;
                Log?.LogInfo("map border siblings:"
                    + " smallMap=" + Index(__instance.m_mapImageSmall?.transform)
                    + " smallPins=" + Index(__instance.m_pinRootSmall)
                    + " smallNames=" + Index(__instance.m_pinNameRootSmall)
                    + " largeMap=" + Index(__instance.m_mapImageLarge?.transform)
                    + " largePins=" + Index(__instance.m_pinRootLarge)
                    + " largeNames=" + Index(__instance.m_pinNameRootLarge));
            }
        }

        private static void Hang(RawImage map, string name)
        {
            if (map == null)
                return;

            Canvas canvas = map.GetComponentInParent<Canvas>();
            Sprite rail = AssetLoader.Piece("panel_thin", canvas != null ? canvas.referencePixelsPerUnit : 100f);
            if (rail == null)
                return;

            var maprt = (RectTransform)map.transform;
            Transform parent = maprt.parent;
            if (parent == null)
                return;

            Transform existing = parent.Find(name);
            Image img = existing != null ? existing.GetComponent<Image>() : null;
            if (img == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                img = go.GetComponent<Image>();
                img.raycastTarget = false;
            }

            img.sprite = rail;
            img.type = Image.Type.Sliced;
            img.fillCenter = false;
            img.color = Color.white;
            img.pixelsPerUnitMultiplier = 1f;

            // Same anchors as the map, offsets pushed out by the rule's depth: the border follows
            // the map's own rect at any resolution or canvas scale, stretched anchors included.
            // Drawn on the map's edge the corners covered the map's own corners - the wind arrow is
            // map/wind_marker, a 32x32 box in the bottom-left corner of the 188-unit map (read from
            // the scene with UnityPy) - so the frame sits round the map, not on it.
            float depth = AssetLoader.PieceBorderUnits("panel_thin");
            var rt = (RectTransform)img.transform;
            rt.anchorMin = maprt.anchorMin;
            rt.anchorMax = maprt.anchorMax;
            rt.pivot = maprt.pivot;
            rt.offsetMin = maprt.offsetMin - Vector2.one * depth;
            rt.offsetMax = maprt.offsetMax + Vector2.one * depth;
            rt.localScale = Vector3.one;
            rt.SetSiblingIndex(maprt.GetSiblingIndex() + 1);
        }
    }
}
