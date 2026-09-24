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
    /// The piece is panel_frame: the frame Cartur drew, with nothing behind it. Not the panel
    /// piece - that one has the fill composited under the frame, so as an overlay it would
    /// cover the map's outer band. This one is transparent between the rule and the centre, so
    /// the map shows through right up to the gold. Same frame as the panels either way, so the
    /// map belongs to the same set.
    ///
    /// The knot is scaled to the map it sits on. It draws 40 units on a panel, which is most of
    /// a minimap, so the corner is asked to be a tenth of the map's shorter side and clamped
    /// between 16 units and the full 40 - the multiplier divides the drawn border, which is the
    /// same lever the hotbar panel uses. Chosen size is logged, so it can be argued with.
    ///
    /// Size and shape come from the map's own RawImage rect at runtime, not from a number written
    /// here. Whatever shape the map is, the border is that shape - it does not decide for itself.
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
            Sprite rail = AssetLoader.Piece("panel_frame", canvas != null ? canvas.referencePixelsPerUnit : 100f);
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

            Vector2 size = maprt.rect.size;
            if (size.x <= 0f || size.y <= 0f)
                size = maprt.sizeDelta;
            float knot = AssetLoader.PieceBorderUnits("panel_frame");
            float want = Mathf.Clamp(Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.y)) * 0.10f, 16f, knot);
            img.pixelsPerUnitMultiplier = want > 0f ? knot / want : 1f;
            Log?.LogInfo(name + ": map " + size.x.ToString("0") + "x" + size.y.ToString("0")
                + ", knot drawn at " + want.ToString("0.#") + " of " + knot.ToString("0.#") + " units");

            // Exactly the map's rect: same anchors, same pivot, same size, so the rail lands on
            // the map's own edge however the map is laid out or resized.
            var rt = (RectTransform)img.transform;
            rt.anchorMin = maprt.anchorMin;
            rt.anchorMax = maprt.anchorMax;
            rt.pivot = maprt.pivot;
            rt.anchoredPosition = maprt.anchoredPosition;
            rt.sizeDelta = maprt.sizeDelta;
            rt.localScale = Vector3.one;
            rt.SetSiblingIndex(maprt.GetSiblingIndex() + 1);
        }
    }
}
