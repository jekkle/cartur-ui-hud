using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Part 2 of Cartur's UI: the inventory screen. Player panel, container, crafting and info
    /// are vanilla's own objects with our sprites on them, and each panel root is registered
    /// with edit mode so it can be dragged and scaled like the HUD pieces.
    ///
    /// Nothing is rebuilt. The slot grid, recipe rows and the drag ghost come from prefabs the
    /// game instantiates on demand, so those prefabs are skinned once here and every element
    /// made from them afterwards inherits the look.
    /// </summary>
    internal static class InventoryScreen
    {
        internal const string Owner = "inventory";

        // Both grids are reachable without reflection: m_playerGrid is a public field and the
        // container's is behind the public ContainerGrid property.

        internal static BepInEx.Logging.ManualLogSource Log;

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        [HarmonyPostfix]
        private static void Awake(InventoryGui __instance)
        {
            Canvas canvas = __instance.GetComponentInParent<Canvas>();
            float ppu = canvas != null ? canvas.referencePixelsPerUnit : 100f;

            Skin.Apply(__instance.transform, "inventory", ppu);
            Skin.Apply(__instance.m_playerGrid?.m_elementPrefab?.transform, "inventory element prefab", ppu);
            Skin.Apply(__instance.ContainerGrid?.m_elementPrefab?.transform, "container element prefab", ppu);
            Skin.Apply(__instance.m_recipeElementPrefab?.transform, "recipe row prefab", ppu);
            Skin.Apply(__instance.m_dragItemPrefab?.transform, "drag item prefab", ppu);
            Skin.Apply(__instance.m_trophieElementPrefab?.transform, "trophy element prefab", ppu);

            HudLayout.Reset(Owner, null);
            s_registered = false;

            // The prefab's own positions are the fallbacks, so a fresh config lands exactly
            // where vanilla put things. Container is a child of Player in the prefab, so for
            // now it rides along when Player moves and can be nudged on top of that.
            // Movable and scalable in edit mode on the boards too - Cartur places every panel
            // himself (2026-10-03). InventoryBoard.Align runs once only, from its Follower, to move
            // a bag still at the old default under the hotbar; after that edit mode owns the spot.
            Register("player", "Inventory", __instance.m_player);
            Register("container", "Container", __instance.m_container);
            Register("crafting", "Crafting", __instance.m_crafting);
            Register("info", "Info", __instance.m_info);

            Room(__instance);
            // Cartur's full crafting board (2026-10-04) carries the info panel's header in the same
            // picture; the old pair - crafting board plus banner - stays as the fallback.
            bool full = AssetLoader.Board("craftingfull") != null;
            Dress(__instance.m_crafting, full ? "craftingfull" : "crafting", "crafting", full ? 296f : 0f);
            CraftingBoard.Fit(__instance);
            if (!full)
                Dress(__instance.m_info, "banner");
            CraftingBoard.FitInfo(__instance);
            Log.LogInfo("inventory skinned, 4 panels registered for edit mode");
        }

        /// <summary>
        /// Gives the player panel the room HotbarRow takes.
        ///
        /// The hotbar row carries a frame, and the rows under it drop to clear its rail - so
        /// the grid is that much taller than vanilla thinks. Vanilla owns this height and
        /// recomputes it from scratch whenever the rows change:
        ///
        ///     m_player.sizeDelta = (x, m_playerHeight + rows * m_invGridHeight)
        ///
        /// so adding the shift after it has run cannot accumulate. Applied at Awake as well,
        /// because a character that has never had a bigger pack is never resized at all - the
        /// game only calls SetInventorySize when the "invrows" key exists.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "SetInventorySize")]
        [HarmonyPostfix]
        private static void Taller(InventoryGui __instance) => Room(__instance);

        private static void Room(InventoryGui gui)
        {
            RectTransform panel = gui.m_player;
            if (panel == null)
                return;
            Vector2 size = panel.sizeDelta;
            panel.sizeDelta = new Vector2(size.x, size.y + HotbarRow.RowShift);
        }

        /// <summary>
        /// Puts one of Cartur's boards (2026-10-03) behind a panel in place of its old frame: as
        /// wide as the panel, as tall as the art's own aspect makes it, top edges together. The
        /// frame's Image is switched off but its object stays - edit mode hit-tests its rect.
        /// Positions and sizes are his to set in edit mode; this is only where they start.
        /// </summary>
        private static void Dress(RectTransform panel, string board, string name = null, float headerPx = 0f)
        {
            Texture2D tex = AssetLoader.Board(board);
            if (panel == null || tex == null)
                return;
            Transform bkg = panel.Find("Bkg");
            Image old = bkg != null ? bkg.GetComponent<Image>() : null;
            if (old != null)
                old.enabled = false;

            // Found again rather than made again: a second Awake on the same panel stacked a second board.
            string goName = "CarturUI_Board_" + (name ?? board);
            if (panel.Find(goName) != null)
                return;
            var go = new GameObject(goName, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(panel, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            float w = panel.rect.width + 40f;   // the old frame stood 20 proud of the panel
            rt.sizeDelta = new Vector2(w, w * tex.height / tex.width);
            // A header painted above the part that fits the panel rises above the panel's top.
            rt.anchoredPosition = new Vector2(0f, 20f + headerPx * w / tex.width);
            rt.SetAsFirstSibling();

            Image img = go.GetComponent<Image>();
            img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f,
                                       0, SpriteMeshType.FullRect);
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            img.material = null;   // full brightness - see valheim-litpanel-material

            // The banner is rendered lighter and warmer than the crafting board it sits on top of.
            // Measured on both PNGs: crafting/banner is 0.90/0.91/0.97 on the bronze and
            // 0.81/0.87/0.87 on the wood; this is the average, so neither drifts far.
            if (board == "banner")
                img.color = new Color(0.85f, 0.89f, 0.92f, 1f);
        }

        private static void Register(string key, string label, RectTransform panel)
        {
            if (panel == null)
            {
                Log.LogWarning("no " + label + " panel - not registered");
                return;
            }
            // Hit-test and outline against Bkg, not the panel - the same correction HudSkin
            // already carries for the HUD bars. The panel is the window; Bkg is the ornate frame
            // drawn around it, and it is bigger. Measured in game on all four, every one the same:
            //
            //     player  570x313 -> Bkg 590x333      crafting 570x650 -> Bkg 590x670
            //     container 570x340 -> Bkg 590x360    info     570x130 -> Bkg 590x150
            //
            // Twenty wider and twenty taller, centred, so the frame stands 10 units proud on every
            // side and an edit box on the panel sat that far inside what the player can see.
            //
            // Move stays the panel: Bkg is its child, so moving Bkg would slide the frame off its
            // own contents.
            RectTransform frame = panel.Find("Bkg") as RectTransform;
            HudLayout.Register(Owner, key, label, panel, frame != null ? frame : panel,
                               panel.anchoredPosition);
        }

        // The side column: armour, trash can, sort, weight. Armour and weight are vanilla's and
        // anchor to the panel's right edge; the can and the sort button are Cartur's Waste
        // Management, built on the first Show and hung off the weight box's own position with a
        // different anchor, which is what left them bunched together. One owner for the whole
        // column fixes it: same size, same backing, one even spacing, all four anchored the same
        // way.
        //
        // Both mods postfix InventoryGui.Show and both write the same two GameObjects, so the
        // order between them is now declared rather than assumed: [HarmonyAfter] on Column below
        // puts this postfix after the waste mod's, and a soft BepInDependency on Plugin loads
        // that mod first so its patch exists to be ordered against. Before those two the order
        // was whatever the chainloader happened to give, and this comment claimed it as settled.
        private const float Margin = 6f;        // clear air either side of the column
        private const float BoxWidth = 80f;     // vanilla's armour and weight box width
        private const float MinBoxWidth = 44f;
        private const float BoxHeight = 64f;
        private const float BoxGap = 8f;
        private const float Padding = 12f;      // frame rail plus breathing room inside a box
        private const float IconSize = 28f;
        private const float LabelHeight = 22f;
        private const string BoxPrefix = "CarturUI_Box_";
        private const string ColumnName = "CarturUI_SideColumn";
        private static readonly Color Gold = new Color32(255, 216, 0, 255);  // vanilla's armour number

        // Top to bottom. The two Cartur names are built by the waste management mod; the column
        // is laid out again until all four have turned up rather than once and hoping, which
        // covers that mod being absent altogether as well as its pieces arriving late.
        private static readonly string[] ColumnNames = { "Armor", "CarturTrashCan", "CarturSortButton", "Weight" };
        private static int s_seated;
        private static bool s_registered;

        [HarmonyPatch(typeof(InventoryGui), "Show")]
        [HarmonyPostfix]
        [HarmonyAfter("com.jekkle.valheim.carturwastemanagement")]
        private static void Column(InventoryGui __instance)
        {
            s_seated = 0;
            s_tries = 0;
            LayOutColumn(__instance);
        }

        // Retries only while the column is still filling up. Without the waste management mod
        // the can and the sort button never arrive, so this would otherwise walk the whole
        // inventory subtree every frame forever; a few seconds of grace is enough for another
        // mod's Show postfix to build its pieces.
        private const int SettleFrames = 240;
        private static int s_tries;

        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPostfix]
        private static void ColumnLate(InventoryGui __instance)
        {
            if (s_seated >= ColumnNames.Length || s_tries >= SettleFrames)
                return;
            s_tries++;
            LayOutColumn(__instance);
            if (s_tries == SettleFrames && s_seated < ColumnNames.Length)
                Log.LogInfo("side column settled with " + s_seated + " of " + ColumnNames.Length + " pieces");
        }

        private static void LayOutColumn(InventoryGui gui)
        {
            RectTransform panel = gui.m_player;
            if (panel == null || !panel.gameObject.activeInHierarchy)
                return;

            // The vanilla two come off the GUI's own text fields, because the container panel
            // carries a second object called "Weight" and it is a child of this one - a search
            // by name would find the wrong readout. The Cartur two are searched for by name over
            // the whole subtree, since once seated they live inside their own box.
            var found = new List<RectTransform>();
            AddIfFound(found, Box(gui.m_armor));
            AddIfFound(found, Descendant(panel, "CarturTrashCan"));
            AddIfFound(found, Descendant(panel, "CarturSortButton"));
            AddIfFound(found, Box(gui.m_weight));

            if (found.Count == 0 || found.Count == s_seated)
                return;

            // Nothing is placed until the measurement can be trusted. On the first pass the
            // equipment cluster does not exist yet, so the only thing to measure to is the
            // crafting panel out at 1220 - a number known to be wrong before it is used. Wait a
            // frame instead of acting on it.
            if (!Measured(gui, panel, out float measured))
                return;

            // Always spaced for the full column, so the boxes do not jump when the last one
            // finally appears.
            float gap = measured;
            float width = Mathf.Clamp(gap - Margin * 2f, MinBoxWidth, BoxWidth);
            float x = gap * 0.5f;   // centred in the gap, not hugging the panel edge
            float step = BoxHeight + BoxGap;
            float top = (ColumnNames.Length - 1) * step * 0.5f;

            // One container for the lot, so the whole column moves and scales as a unit - and
            // so the measured position below is only ever a starting point. Where it ends up is
            // a drag in edit mode, which beats another round of guessing at somebody else's panel.
            // Keep re-placing while the column is still settling. The first pass runs before the
            // equipment cluster exists, so its measurement is wrong by definition; placing only
            // on creation froze that first bad number in place. Once all four are seated the
            // position is set one last time and then left alone, so a drag in edit mode sticks.
            RectTransform column = Container(panel, new Vector2(x, 0f),
                new Vector2(width, ColumnNames.Length * step), !s_registered);

            for (int i = 0; i < found.Count; i++)
                Seat(column, found[i], new Vector2(0f, top - Slot(found[i]) * step), width);

            s_seated = found.Count;

            // Registered only once the whole column is up. Doing it on the first pass - when the
            // equipment cells do not exist yet and the measurement is wrong - also stopped the
            // re-placement, which is what pinned the boxes out to the right at the bad number.
            if (found.Count == ColumnNames.Length && !s_registered)
            {
                HudLayout.Register(Owner, "sidecolumn", "Armour / trash / sort / weight",
                    column, column, column.anchoredPosition);
                s_registered = true;
            }

            Log.LogInfo("side column: " + found.Count + " of " + ColumnNames.Length
                + " boxes placed, width " + width.ToString("0"));
        }

        /// <summary>
        /// The clear space between the inventory panel's right edge and whatever stands to its
        /// right - the equipment panel, whose position is a config value in that mod, so it is
        /// measured rather than assumed. Measured in the panel's own space, so it holds at any
        /// resolution. Falls back to the room vanilla left for its two boxes.
        /// </summary>
        /// <summary>
        /// True only when the gap is measured against something worth measuring: the equipment
        /// panel, or - before it has been built - nothing at all, in which case vanilla's own
        /// box width is the answer. False means "not yet", never "here is a guess".
        ///
        /// This measured Equipment and Quick Slots' cluster until Part 12 made the slots ours;
        /// the measurement is the same one, taken against our own panel now. Those Head / Chest
        /// / Legs boxes are inventory grid CELLS, 64 units each, so the edge comes from the
        /// leftmost cell inside the panel rather than from the panel's own rect.
        /// </summary>
        private static bool Measured(InventoryGui gui, RectTransform panel, out float gap)
        {
            gap = BoxWidth + Margin * 2f;

            RectTransform slots = Descendant(panel, EquipmentPanel.PanelName);
            if (slots == null)
                return true;             // no cluster to clear; vanilla spacing stands
            if (!slots.gameObject.activeInHierarchy)
                return false;            // it exists but is not up yet - wait a frame

            // The board stands proud of the panel, so the column clears its right edge rather
            // than the panel's.
            float panelRight = InventoryBoard.RightIn(panel);
            float left = float.MaxValue;
            var corners = new Vector3[4];
            foreach (RectTransform cell in slots.GetComponentsInChildren<RectTransform>(false))
            {
                if (cell == slots)
                    continue;
                cell.GetWorldCorners(corners);
                left = Mathf.Min(left, panel.InverseTransformPoint(corners[0]).x);
            }

            if (left == float.MaxValue)
                return false;            // no cells yet

            float measured = left - panelRight;
            // Cartur's bag board reaches past the equipment panel's left edge, so there may be no
            // gap at all - the column then takes vanilla spacing and he places it in edit mode.
            // Waiting for a gap that cannot come is what left the can and Sort unboxed.
            if (measured <= MinBoxWidth)
                return true;

            Log.LogInfo("side column gap: panel right " + panelRight.ToString("0")
                + " to equipment cells left " + left.ToString("0") + " = " + measured.ToString("0"));
            gap = measured;
            return true;
        }

        private static RectTransform Container(RectTransform panel, Vector2 position, Vector2 size, bool place)
        {
            RectTransform column = panel.Find(ColumnName) as RectTransform;
            if (column == null)
            {
                place = true;
                var go = new GameObject(ColumnName, typeof(RectTransform));
                column = (RectTransform)go.transform;
                column.SetParent(panel, false);
            }

            column.anchorMin = column.anchorMax = new Vector2(1f, 0.5f);
            column.pivot = new Vector2(0.5f, 0.5f);
            column.sizeDelta = size;
            if (place)
                column.anchoredPosition = position;
            return column;
        }

        private static void AddIfFound(List<RectTransform> list, RectTransform rt)
        {
            if (rt != null)
                list.Add(rt);
        }

        /// <summary>The box a readout sits in - the "Armor" or "Weight" object itself.</summary>
        private static RectTransform Box(TMPro.TMP_Text text)
        {
            return text != null ? text.transform.parent as RectTransform : null;
        }

        private static int Slot(RectTransform rt)
        {
            for (int i = 0; i < ColumnNames.Length; i++)
                if (rt.name == ColumnNames[i])
                    return i;
            return 0;
        }

        private static RectTransform Descendant(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t as RectTransform;
            return null;
        }

        /// <summary>
        /// One box of the column: a frame at the shared size, with the original object inside it
        /// at its own size. Resizing the objects themselves stretched their art - the trash can
        /// and the Sort button both carry a sprite on the same rect - so each gets a wrapper and
        /// keeps its own proportions, shrunk only if it would not fit inside the frame.
        /// </summary>
        private static void Seat(RectTransform parent, RectTransform content, Vector2 position, float width)
        {
            string boxName = BoxPrefix + content.name;
            RectTransform box = parent.Find(boxName) as RectTransform;
            if (box == null)
            {
                var go = new GameObject(boxName, typeof(RectTransform), typeof(Image));
                box = (RectTransform)go.transform;
                box.SetParent(parent, false);

                // Cartur's square button board (2026-10-03) when it is there, else the thin rule:
                // a 40-unit corner knot on a 64-unit box is all corner and no box.
                Image frame = go.GetComponent<Image>();
                Sprite button = ButtonFrame(Ppu(parent));
                frame.sprite = button != null ? button : AssetLoader.Piece("panel_thin", Ppu(parent));
                frame.type = Image.Type.Sliced;
                frame.color = Color.white;
                frame.raycastTarget = false;
                if (button != null)
                {
                    frame.material = null;   // full brightness - see valheim-litpanel-material
                    // Down to the equipment board's wood beside it: measured in game, box wood
                    // RGB 53/42/33 against the board's 16-23, so 0.4 (Cartur: "need to be darker").
                    frame.color = new Color(ButtonShade, ButtonShade, ButtonShade, 1f);
                    frame.pixelsPerUnitMultiplier = ButtonBorderPx / ButtonCornerUnits;
                }
            }

            // Anchored to the column's CENTRE, not its right edge.
            //
            // With anchor (1, 0.5), a centre pivot and position.x of 0, every box was centred on
            // the column's right edge - so the four boxes sat half a column-width to the right of
            // the column that owns them, hugging the equipment panel instead of sitting in the
            // gap the column was measured to fill. The edit-mode outline is drawn from the column
            // rect, so it looked like the outline was offset to the left when in fact it was the
            // only part in the right place. A container whose contents fall outside it is the bug;
            // the outline was the symptom that showed it.
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(width, BoxHeight);
            box.anchoredPosition = position;

            if (content.parent != box)
                content.SetParent(box, false);
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
            content.pivot = new Vector2(0.5f, 0.5f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(width, BoxHeight);

            // Any plate the piece carried is turned off - ours is the plate now. Only our own
            // sprites go, so the trash can's picture, which sits on the same rect, is left alone.
            // A button's own graphic is what it raycasts against, so that one is made invisible
            // rather than disabled: disabling it would leave a button that cannot be clicked.
            bool clickable = content.GetComponent<Selectable>() != null;
            foreach (Image img in content.GetComponents<Image>())
            {
                if (img.sprite == null || !img.sprite.name.StartsWith("cartur_"))
                    continue;
                if (clickable)
                    img.color = new Color(1f, 1f, 1f, 0f);
                else
                    img.enabled = false;
            }
            Transform bkg = content.Find("bkg");
            if (bkg != null)
                bkg.gameObject.SetActive(false);

            Centre(content);

            Vector2 size = content.rect.size;
            float room = Mathf.Min((width - Padding) / Mathf.Max(size.x, 1f),
                                   (BoxHeight - Padding) / Mathf.Max(size.y, 1f));
            content.localScale = Vector3.one * Mathf.Min(1f, room);

            // Waste Management turns its trash can or its sort button off when its own config
            // says so, and it does that with SetActive rather than by destroying them. Descendant
            // walks with includeInactive true, so a hidden piece is still found and still gets a
            // box - and the box was left on, drawing as an empty framed gap in the column. The
            // frame follows what it is wrapping.
            box.gameObject.SetActive(content.gameObject.activeSelf);
        }

        /// <summary>
        /// The vanilla armour and weight boxes hang their icon off the top edge and their number
        /// below it, measured for a box that is not this one, so the icon ended up half outside.
        /// Icon in the upper half, number in the lower half, both on the centre line. The Sort
        /// button's label is painted the same gold as those numbers.
        /// </summary>
        private static void Centre(RectTransform content)
        {
            foreach (Image img in content.GetComponentsInChildren<Image>(true))
            {
                if (!img.name.EndsWith("_icon"))
                    continue;
                var rt = (RectTransform)img.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(IconSize, IconSize);
                rt.anchoredPosition = new Vector2(0f, IconSize * 0.45f);
            }

            foreach (TMPro.TMP_Text text in content.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                var rt = (RectTransform)text.transform;
                // sizeDelta has to be set as well as the anchors: on a stretch-anchored label it
                // holds the edge offsets, which are negative, so switching to a centred anchor
                // without this leaves a rect of negative size and the text simply disappears.
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(content.rect.width - Padding, LabelHeight);
                rt.anchoredPosition = new Vector2(0f, -IconSize * 0.45f);
                text.alignment = TMPro.TextAlignmentOptions.Center;
                text.color = Gold;
            }
        }

        // The button board as a 9-slice. Its corner knots reach 150 px in (measured on
        // board_button.png, 627x644), so that is the border: the knots stay whole and only the
        // plain rails and the wood between them stretch to the box. Drawn so a corner is 14 units.
        private const float ButtonBorderPx = 150f;
        private const float ButtonCornerUnits = 14f;
        private const float ButtonShade = 0.4f;
        private static Sprite s_button;

        private static Sprite ButtonFrame(float ppu)
        {
            if (s_button != null)
                return s_button;
            Texture2D tex = AssetLoader.Board("button");
            if (tex == null)
                return null;
            s_button = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu,
                0, SpriteMeshType.FullRect, new Vector4(ButtonBorderPx, ButtonBorderPx, ButtonBorderPx, ButtonBorderPx));
            s_button.name = "cartur_board_button";
            return s_button;
        }

        private static float Ppu(Transform t)
        {
            Canvas canvas = t.GetComponentInParent<Canvas>();
            return canvas != null ? canvas.referencePixelsPerUnit : 100f;
        }

        // Equipment and Quick Slots: read from its DLL (Mono.Cecil), not guessed. It creates
        // EaqsSlotRoot under m_player from an InventoryGrid.UpdateGui postfix, fills it with the
        // grid's own InventoryElements (our prefab, so already skinned), and clones m_player's
        // Bkg for EaqsEquipmentBkg / EaqsQuickSlotBkg (cloned after our Awake, so already ours).
        // It then re-syncs every background's position from its own config each Update, and
        // ships its own drag option for the panel. Nothing to skin and nothing to register here;
        // moving that panel stays EQAS's job until a later part takes it over.
    }
}
