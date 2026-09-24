using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Part 5 of Cartur's UI: the in-game pause menu.
    ///
    /// Start is the hook, not Awake: Menu has no Awake at all (read off assembly_valheim - the
    /// type has Start, Show, Hide and no Awake), and Start is where m_instance is set, so the
    /// object is whole by then.
    ///
    /// Menu.Show only calls SetActive on m_root, m_menuDialog, m_quitDialog and m_logoutDialog -
    /// it builds nothing - so all four already exist at Start, just switched off. The walk uses
    /// GetComponentsInChildren(true), which includes inactive children, so the quit and logout
    /// confirmations are skinned here too rather than on first sight of them.
    ///
    /// The four transforms are checked with IsChildOf rather than assumed to sit under the Menu
    /// component. If m_root is a child, the first walk already covered it and it is skipped; if it
    /// is parked somewhere else in the canvas, it gets its own walk. Nothing here depends on
    /// knowing which, because nobody has read that hierarchy.
    ///
    /// There is no panel behind the menu entries. One used to be added here, because vanilla's
    /// pause menu has nothing to skin - read off IngameGui_Menu.prefab, it is a blur, a darken,
    /// an ornament line and a column of Text. Cartur asked for it gone: it was the last thing
    /// still wearing the old kit's rail, whose outer edge is a pale grey by design (lit top,
    /// warm bottom), and that grey edge is what he was looking at. Measured on his screenshot:
    /// the panel's outer pixels ran 98,93,84 and 146,135,123 before turning warm, while the
    /// crafting window's edge under the new art runs 124,100,72 - gold all the way out.
    ///
    /// The settings window is not dressed from here. It is a prefab instantiated on demand and
    /// SettingsScreen already catches it on Settings.Awake, for both this menu and the main menu.
    /// </summary>
    internal static class PauseMenu
    {
        private const string RowName = "CarturUIHud_MenuRow";

        [HarmonyPatch(typeof(Menu), "Start")]
        [HarmonyPostfix]
        private static void Dress(Menu __instance)
        {
            Skin.Apply(__instance.transform, "pause menu");

            Dressed(__instance.transform, __instance.m_root, "pause menu root");
            Dressed(__instance.transform, __instance.m_menuDialog, "pause menu dialog");
            Dressed(__instance.transform, __instance.m_quitDialog, "quit dialog");
            Dressed(__instance.transform, __instance.m_logoutDialog, "logout dialog");
        }

        /// <summary>
        /// The row slabs, kept in step every frame.
        ///
        /// Every frame rather than once, because the rows are not fixed: Skip Intro, Save,
        /// Invite Friends and the player list come and go with SetButtonsEnabled, and the
        /// layout group moves the rest when they do.
        /// </summary>
        [HarmonyPatch(typeof(Menu), "Update")]
        [HarmonyPostfix]
        private static void Fit(Menu __instance)
        {
            RectTransform entries = __instance.menuEntriesParent;
            if (entries == null || entries.parent == null)
                return;
            Rows(__instance, entries);
        }

        /// <summary>
        /// A wood slab behind each row that is actually a button.
        ///
        /// The test is a Button component, not the row's name or its position: that is what makes
        /// "Last time saved" - a plain Text row in the same list - stay a plain line, and what lets
        /// a row another mod added, like MODS SETTINGS, get the same slab as the rest without being
        /// named here.
        ///
        /// The slab is the row's own first child, so it draws behind the label and the two orange
        /// hover knots, and it stretches to the row rather than to a size written here - the layout
        /// group above owns those rects and rewrites them whenever a row appears or goes.
        ///
        /// Vanilla's text is not touched. Font, size and colour are all still the game's.
        /// </summary>
        private static void Rows(Menu menu, RectTransform entries)
        {
            Canvas canvas = menu.GetComponentInParent<Canvas>();
            Sprite piece = AssetLoader.Piece("button_light", canvas != null ? canvas.referencePixelsPerUnit : 100f);
            if (piece == null)
                return;

            foreach (RectTransform row in entries)
            {
                if (row.GetComponent<Button>() == null)
                    continue;

                Transform found = row.Find(RowName);
                Image img = found != null ? found.GetComponent<Image>() : null;
                if (img == null)
                {
                    var go = new GameObject(RowName, typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(row, false);
                    img = go.GetComponent<Image>();
                    img.raycastTarget = false;
                    img.rectTransform.SetAsFirstSibling();
                }

                img.sprite = piece;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
                img.color = Color.white;

                var rt = (RectTransform)img.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(0f, 2f);
                rt.offsetMax = new Vector2(0f, -2f);
                rt.localScale = Vector3.one;
            }
        }

        private static void Dressed(Transform walked, Transform part, string label)
        {
            if (part != null && !part.IsChildOf(walked))
                Skin.Apply(part, label);
        }
    }
}
