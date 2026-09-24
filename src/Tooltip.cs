using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Part 9 of Cartur's UI: the item tooltip.
    ///
    /// UITooltip is not a screen, it is a component that sits on every hoverable thing in the
    /// game - it lives in assembly_guiutils, not assembly_valheim - and they all share one
    /// tooltip window held in a private STATIC field, m_tooltip. So there is nothing to skin at
    /// Awake: at that point the window does not exist yet.
    ///
    /// This used to skin the live window and guard with a reference compare against the last one
    /// dressed. That guard almost never matched, and the reason is in the assembly: HideTooltip
    /// calls Object.Destroy on m_tooltip and nulls the static, and LateUpdate calls HideTooltip
    /// the moment the pointer leaves the hovered rect. So the window is thrown away and built
    /// again on every single hover, the compare failed every time, and a full skin walk plus the
    /// LogInfo at the end of Skin.Apply ran on each one.
    ///
    /// The window is a clone of m_tooltipPrefab - OnHoverStart Instantiates it into
    /// GetComponentInParent&lt;Canvas&gt;() when m_tooltip is null - so the prefab is what gets
    /// skinned, once, and every clone after that is born with the look. That is the same trick
    /// the inventory grid, the recipe row and the build menu buttons already use.
    /// </summary>
    internal static class Tooltip
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        /// Keyed by the prefab, not a single bool.
        ///
        /// UITooltip.m_tooltipPrefab is a public INSTANCE field and OnHoverStart instantiates
        /// this.m_tooltipPrefab, so one flag would skin whichever component the player happened
        /// to hover first and leave every other prefab vanilla. Whether the game points every
        /// UITooltip at one shared prefab is serialized data, not something readable in the DLL -
        /// so it is not assumed. A set costs the same and does not have to know.
        private static readonly System.Collections.Generic.HashSet<UnityEngine.GameObject> s_dressed =
            new System.Collections.Generic.HashSet<UnityEngine.GameObject>();

        /// <summary>
        /// A Prefix, not because the original is wrong - nothing is returned, so it always runs -
        /// but because the window this call is about is created inside OnHoverStart. Skinning the
        /// prefab afterwards would leave the first tooltip the player ever sees wearing vanilla.
        /// </summary>
        [HarmonyPatch(typeof(UITooltip), "OnHoverStart")]
        [HarmonyPrefix]
        private static void Dress(UITooltip __instance)
        {
            if (__instance.m_tooltipPrefab == null || !s_dressed.Add(__instance.m_tooltipPrefab))
                return;

            // The canvas the game itself parents the clone to, so the sprite scale is worked out
            // against the canvas it will actually live in rather than a guessed 100.
            Canvas canvas = __instance.GetComponentInParent<Canvas>();
            Skin.Apply(__instance.m_tooltipPrefab.transform, "tooltip prefab",
                       canvas != null ? canvas.referencePixelsPerUnit : 100f);
            Cover(__instance.m_tooltipPrefab);
        }

        /// <summary>
        /// Keeps our plate inside whatever clips it.
        ///
        /// Measured after layout, which is the only way this was ever going to be found: the
        /// tooltip's plate is the ScrollRect's Content at 360 wide, sitting in a Viewport that
        /// masks at 350. Our right-hand rail is drawn in the ten units past the mask, so it was
        /// cut off every time - vanilla's flat grey plate had nothing there to lose.
        ///
        /// A component rather than a one-off, because the ScrollRect lays the content out after
        /// this runs and again whenever the text changes. It writes only when the width is
        /// actually wrong, so a correct tooltip costs one compare a frame.
        /// </summary>
        private static void Cover(GameObject tip)
        {
            if (tip.GetComponent<TooltipFit>() == null)
                tip.AddComponent<TooltipFit>();
        }
    }

    /// <summary>
    /// Holds our tooltip plate to the width of the rect that masks it. See Tooltip.Cover.
    /// </summary>
    internal sealed class TooltipFit : MonoBehaviour
    {
        private static bool s_said;

        private void LateUpdate()
        {
            foreach (Image image in GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null || !image.sprite.name.StartsWith("cartur_"))
                    continue;

                var rt = image.rectTransform;
                var parent = rt.parent as RectTransform;
                if (parent == null)
                    continue;

                float room = parent.rect.width;
                float over = rt.rect.width - room;
                if (room <= 0f || over <= 0.5f)
                    continue;

                rt.sizeDelta = new Vector2(rt.sizeDelta.x - over, rt.sizeDelta.y);

                // Said once, so the fix is a fact in the log rather than something that looks
                // right on one screenshot.
                if (!s_said)
                {
                    s_said = true;
                    Tooltip.Log?.LogInfo("tooltip plate trimmed: " + image.name + " was "
                        + (room + over).ToString("0") + " wide inside " + parent.name + " at "
                        + room.ToString("0") + " - the right rail was outside the mask");
                }
            }
        }
    }
}