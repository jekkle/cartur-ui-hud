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
            Board(__instance.transform);
        }

        /// <summary>
        /// The build window on its own board (board_build.png, 2026-10-05: Grok render drawn from the
        /// pilot's dump of BuildUIV2, option 2 picked by Cartur). Frame = the knotwork rectangle in
        /// board px, measured off the cut PNG. EpicBoards.Back switches the window's own plate off,
        /// which is what makes AutoBoard's generic window board stand down. The filter field and the
        /// category list are painted on the board, so their own frames go clear; the piece grid
        /// keeps its well (the board leaves that area bare wood) and the tabs keep their plates.
        /// </summary>
        private static void Board(Transform ui)
        {
            var window = ui.Find("bar/SelectionWindow/Background") as RectTransform;
            if (window == null || !EpicBoards.Back(window, "build", Frame))
                return;
            foreach (string path in new[] { "bar/SelectionWindow/Main View/Root/TagList/LayoutGroup/SearchBar",
                                            "bar/SelectionWindow/Main View/Root/TagList/Scroll View" })
                if (ui.Find(path)?.GetComponent<UnityEngine.UI.Image>() is UnityEngine.UI.Image plate)
                    plate.color = Color.clear;
            Fit(ui, window);
        }

        // The window's rect in the pilot's dump (screen px at 2560x1440, y up), so every spot below
        // is the dump's own number mapped through the live window - it holds at any resolution.
        private const float DumpX0 = 556.67f, DumpY0 = 460f, DumpW = 2003.33f - 556.67f;

        /// <summary>
        /// Fitment, from the overlay of the dump on the pilot shot (Cartur, 2026-10-05):
        /// - Q and E overlapped the ends of the outer tabs (tabs 594..1966, keys 565..607 and
        ///   1953..1995): both at 0.8 size, just outside the tabs, on the frame rail.
        /// - F overlapped the filter plate's left end (plate 593..798, key 566..609): 0.8 size, left
        ///   of the plate, centred on it.
        /// - The filter text began on the plate's left knot (text from 606; knot to ~622 measured on
        ///   the shot): text area starts at 626.
        /// - The category list cut its last row in half at the bottom: its viewport fades out over the
        ///   bottom 24 px (RectMask2D softness, padded past the top so the top edge stays hard).
        /// </summary>
        private static void Fit(Transform ui, RectTransform window)
        {
            var c = new Vector3[4];
            window.GetWorldCorners(c);
            float k = (c[2].x - c[0].x) / DumpW;
            Vector3 At(float x, float y) => new Vector3(c[0].x + (x - DumpX0) * k, c[0].y + (y - DumpY0) * k, 0f);

            const string tabs = "bar/SelectionWindow/TabContainer/InputHelp/";
            Key(ui.Find(tabs + "MK hints/Left"), At(574f, 1061f));
            Key(ui.Find(tabs + "Gamepad hints/gamepad_hint_L"), At(574f, 1061f));
            Key(ui.Find(tabs + "MK hints/Right"), At(1986f, 1061f));
            Key(ui.Find(tabs + "Gamepad hints/gamepad_hint_R"), At(1986f, 1061f));
            const string tags = "bar/SelectionWindow/Main View/Root/TagList/";
            // Measured by its visible key (Mouse): the container's centre is not the key's - first try
            // centred the container and put the key 17 px right and 30 px high (pilot, 2026-10-05).
            Transform hint = ui.Find(tags + "LayoutGroup/SearchBar/SearchBarHint");
            Key(hint, At(574f, 997.5f), hint?.Find("Mouse"));

            if (ui.Find(tags + "LayoutGroup/SearchBar/Text Area") is RectTransform text && text.lossyScale.x > 0f)
            {
                text.GetWorldCorners(s_c);
                float move = At(626f, 0f).x - s_c[0].x;
                if (move > 0f)
                    text.offsetMin += new Vector2(move / text.lossyScale.x, 0f);
            }

            if (ui.Find(tags + "Scroll View/Viewport") is RectTransform view && view.lossyScale.y > 0f)
            {
                var mask = view.GetComponent<UnityEngine.UI.RectMask2D>() ?? view.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
                int soft = Mathf.RoundToInt(24f * k / view.lossyScale.y);
                mask.softness = new Vector2Int(0, soft);
                mask.padding = new Vector4(0f, 0f, 0f, -soft);
            }
        }

        private static readonly Vector3[] s_c = new Vector3[4];

        /// <summary>A key hint at 0.8 size, its centre (or that of its visible child) on the given world point.</summary>
        private static void Key(Transform t, Vector3 centre, Transform visible = null)
        {
            if (!(t is RectTransform rt))
                return;
            rt.localScale = Vector3.one * 0.8f;
            ((visible as RectTransform) ?? rt).GetWorldCorners(s_c);
            Vector3 now = (s_c[0] + s_c[2]) * 0.5f;
            rt.position += new Vector3(centre.x - now.x, centre.y - now.y, 0f);
        }

        private static readonly RectInt Frame = new RectInt(2, 24, 2039, 929);

        private static Transform Prefab(FieldInfo field, BuildUi ui) =>
            (field?.GetValue(ui) as GameObject)?.transform;
    }
}
