using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// A third tab on the crafting panel: Styles. It lists the capes and shields you are
    /// carrying that have more than one look, and changing one costs a tenth of what the item
    /// costs to make.
    ///
    /// Why capes and shields and nothing else
    /// --------------------------------------
    /// The look is vanilla's variant system - ItemData.m_variant, picking an icon out of
    /// m_shared.m_icons - and VisEquipment only carries a variant for two places on the body:
    /// m_shoulderItemVariant, and m_leftItemVariant / m_leftBackItemVariant. Helmet, chest,
    /// legs, utility and the right hand have no variant field at all. So for anything else the
    /// icon would change and the item on your character would not, which is not worth charging
    /// for. Read off VisEquipment, not assumed.
    ///
    /// What is vanilla's and what is ours
    /// ----------------------------------
    /// The item text is vanilla's: ItemDrop.ItemData.GetTooltip is public and static and is the
    /// call the crafting pane itself makes, so the stats on our pane read exactly as they do on
    /// the Craft tab. The styles are the item's own icons, m_shared.m_icons, which is what the
    /// variant indexes into.
    ///
    /// Vanilla's variant dialog is NOT used. It was, briefly - borrowing its public Action and
    /// handing it back - but the styles belong in the pane beside the item and its cost, so
    /// there is no popup and nothing of the game's to put back.
    ///
    /// The list is ours. Vanilla's recipe list is one 325-instruction method building rows from
    /// a prefab this mod has never read the inside of, so while our tab is up that method is
    /// skipped and plain rows are drawn in the same place instead.
    ///
    /// The cost is the item's own recipe: ObjectDB.GetRecipe gives Recipe.m_resources, and each
    /// requirement is charged at a tenth, rounded up, never less than one. So 20 iron is 2, and
    /// 5 iron is 1 - never free, and never a surprise.
    /// </summary>
    internal static class StyleTab
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string TabName = "CarturUI_StylesTab";
        private const string RowName = "CarturUI_StyleRow";
        private const string TabLabel = "STYLES";
        private const float RowHeight = 48f;
        private const float CostShare = 0.1f;

        private static Button s_tab;
        private static bool s_active;
        private static readonly List<GameObject> s_rows = new List<GameObject>();
        private static readonly HashSet<string> s_priced = new HashSet<string>();

        // What is selected in the list, and which style is being looked at. The style starts as
        // the one the item already wears, so the pane opens showing the truth.
        private static ItemDrop.ItemData s_selected;
        private static int s_variant;
        private static GameObject s_pane;

        // Private on Inventory: setting m_variant changes nothing the UI watches, so the
        // inventory has to be told the item changed or the icon stays as it was.
        private static readonly MethodInfo s_changed =
            AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });

        // Humanoid.SetupEquipment is private - it is what rebuilds the cape and shield on the
        // body from the inventory, so a style change is not visible on the player until it runs.
        private static readonly MethodInfo s_setupEquipment =
            AccessTools.Method(typeof(Humanoid), "SetupEquipment");

        internal static bool Found => s_changed != null;

        // ---- the tab itself ---------------------------------------------------------------

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        [HarmonyPostfix]
        private static void Build(InventoryGui __instance)
        {
            s_active = false;
            s_rows.Clear();
            s_tab = null;

            Button model = __instance.m_tabUpgrade;
            if (model == null)
            {
                Log.LogWarning("no upgrade tab to copy - no Styles tab this run");
                return;
            }

            var go = Object.Instantiate(model.gameObject, model.transform.parent);
            go.name = TabName;
            go.SetActive(true);
            s_tab = go.GetComponent<Button>();

            var rt = (RectTransform)go.transform;
            var from = (RectTransform)model.transform;
            rt.anchorMin = from.anchorMin;
            rt.anchorMax = from.anchorMax;
            rt.pivot = from.pivot;
            rt.sizeDelta = from.sizeDelta;

            // Spaced by the gap the game already uses between its own two tabs, rather than a
            // number of ours: whatever Craft-to-Upgrade measures, Upgrade-to-Styles is the same.
            var craft = __instance.m_tabCraft != null ? (RectTransform)__instance.m_tabCraft.transform : null;
            float step = craft != null
                ? from.anchoredPosition.x - craft.anchoredPosition.x
                : from.rect.width + 4f;
            rt.anchoredPosition = from.anchoredPosition + new Vector2(step, 0f);

            // A clone copies the label, but not always what has happened to it since: another
            // mod swaps the font on the objects that exist when it loads, and ours is born
            // after that. So the model's own label is copied across field by field - font,
            // material, size, weight, spacing, colour - and only the words differ.
            // The label is copied field by field - see below - but the font and its material
            // are set together and only when both are there. TMP's fontSharedMaterial setter
            // walks the font asset it is given, and handing it a material for a null font
            // throws inside TMP: that is the NullReferenceException Cartur got out of
            // InventoryGui.Awake, which also stopped every postfix queued after ours.
            TMP_Text source = model.GetComponentInChildren<TMP_Text>(true);
            foreach (TMP_Text label in go.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label == null)
                    continue;
                if (source != null)
                {
                    if (source.font != null)
                    {
                        label.font = source.font;
                        if (source.fontSharedMaterial != null)
                            label.fontSharedMaterial = source.fontSharedMaterial;
                    }
                    label.fontSize = source.fontSize;
                    label.fontStyle = source.fontStyle;
                    label.characterSpacing = source.characterSpacing;
                    label.alignment = source.alignment;
                    label.color = source.color;
                }
                label.text = TabLabel;
            }

            if (s_tab == null)
            {
                Log.LogWarning("the upgrade tab has no Button on its root - no Styles tab this run");
                return;
            }

            s_tab.onClick.RemoveAllListeners();
            s_tab.onClick.AddListener(Press);

            Log.LogInfo("Styles tab added at " + rt.anchoredPosition);
        }

        /// <summary>
        /// A tab is "selected" when it is NOT interactable - that is how vanilla reads its own,
        /// see InCraftTab. So selecting ours means making the other two live and ours dead.
        /// </summary>
        private static void Press()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || s_tab == null)
                return;

            s_active = true;
            if (gui.m_tabCraft != null) gui.m_tabCraft.interactable = true;
            if (gui.m_tabUpgrade != null) gui.m_tabUpgrade.interactable = true;
            s_tab.interactable = false;

            // Skipping UpdateRecipeList stops the list being REBUILT; the rows already in it
            // stay where they are. They are switched off by hand, and back on when the tab is
            // left - which is what Cartur saw drawn underneath ours.
            Vanilla(gui, false);
            Refresh(gui);
        }

        /// <summary>
        /// The tab only exists at a station. Player.GetCurrentCraftingStation is public and is
        /// the same thing the panel uses to decide it is a workbench rather than the plain
        /// inventory, so nothing here has to guess what counts as one.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "UpdateCraftingPanel")]
        [HarmonyPostfix]
        private static void AtStation()
        {
            if (s_tab == null)
                return;
            bool station = Player.m_localPlayer != null
                && Player.m_localPlayer.GetCurrentCraftingStation() != null;
            if (s_tab.gameObject.activeSelf != station)
                s_tab.gameObject.SetActive(station);
            if (!station && s_active)
                Leave();
        }

        // Prefixes, not postfixes, and that is the whole of a bug Cartur hit: going back to
        // Craft or Upgrade sometimes showed nothing, or showed the other tab's list.
        //
        // Both of these rebuild the recipe list inside themselves. Leaving on a POSTFIX meant
        // s_active was still true while that rebuild ran, so our SkipRecipes prefix skipped it
        // - and then the rows we switched back on were the stale ones from before. Standing
        // down first lets the rebuild do its job.
        [HarmonyPatch(typeof(InventoryGui), "OnTabCraftPressed")]
        [HarmonyPrefix]
        private static void LeaveOnCraft() => Leave();

        [HarmonyPatch(typeof(InventoryGui), "OnTabUpgradePressed")]
        [HarmonyPrefix]
        private static void LeaveOnUpgrade() => Leave();

        private static void Leave()
        {
            if (!s_active)
                return;
            s_active = false;
            if (s_tab != null)
                s_tab.interactable = true;
            Clear();

            // Vanilla's own rows and pane go back on BEFORE it rebuilds them, so it is working
            // on live objects rather than switched-off ones.
            Vanilla(InventoryGui.instance, true);
        }

        // ---- the list ---------------------------------------------------------------------

        /// <summary>
        /// While our tab is up, vanilla's recipe list does not run. Returning false skips it,
        /// which leaves the list root empty for our own rows.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
        [HarmonyPrefix]
        private static bool SkipRecipes() => !s_active;

        /// <summary>
        /// Vanilla's own rows and its item pane, on or off. The pane is found by walking up
        /// from m_recipeDecription - it is a TMP_Text inside it - and is only touched when the
        /// name matches, so a game update that moves it leaves the pane alone rather than
        /// hiding whatever happens to be there instead.
        /// </summary>
        private static void Vanilla(InventoryGui gui, bool on)
        {
            if (gui == null)
                return;

            if (gui.m_recipeListRoot != null)
            {
                foreach (Transform child in gui.m_recipeListRoot)
                {
                    if (child.name != RowName && child.gameObject.activeSelf != on)
                        child.gameObject.SetActive(on);
                }
            }

            Transform pane = Pane(gui);
            if (pane != null && pane.gameObject.activeSelf != on)
                pane.gameObject.SetActive(on);
        }

        /// <summary>
        /// The whole right-hand pane, found by what it holds rather than by its name.
        ///
        /// Hiding the object called "Decription" was not enough: the item's name, its icon, the
        /// requirement boxes and the Craft button are siblings of it, so they stayed on screen
        /// with a Flint Axe still selected under our tab. The pane is the first ancestor of the
        /// recipe name that also holds the craft button.
        ///
        /// And it stops if it reaches something that also holds the recipe LIST - that would be
        /// the whole crafting panel, tabs and all, and hiding that would take our own tab with
        /// it. Null then, and the pane is left alone rather than guessed at.
        /// </summary>
        private static Transform Pane(InventoryGui gui)
        {
            Transform name = gui.m_recipeName != null ? gui.m_recipeName.transform : null;
            Transform craft = gui.m_craftButton != null ? gui.m_craftButton.transform : null;
            Transform list = gui.m_recipeListRoot;
            if (name == null || craft == null)
                return null;

            for (Transform t = name.parent; t != null; t = t.parent)
            {
                if (list != null && list.IsChildOf(t))
                    return null;
                if (craft.IsChildOf(t))
                    return t;
            }
            return null;
        }

        private static void Select(ItemDrop.ItemData item)
        {
            s_selected = item;
            s_variant = item != null ? item.m_variant : 0;
            BuildPane(InventoryGui.instance);
        }

        /// <summary>
        /// Everywhere a style can be changed from: the player, and the chest that is open.
        ///
        /// The open container is vanilla's own - InventoryGui.m_currentContainer - so this
        /// needs no other mod and follows whatever the game already considers open. A chest is
        /// only in the pool while its window is up, which is also the only time its items are
        /// there to be clicked.
        /// </summary>
        private static List<Inventory> Pool()
        {
            var pool = new List<Inventory>();
            Player player = Player.m_localPlayer;
            Inventory bag = player != null ? player.GetInventory() : null;
            if (bag != null)
                pool.Add(bag);

            InventoryGui gui = InventoryGui.instance;
            Container chest = gui != null && gui.IsContainerOpen() ? Chest(gui) : null;
            Inventory inside = chest != null ? chest.GetInventory() : null;
            if (inside != null && inside != bag)
                pool.Add(inside);

            return pool;
        }

        // m_currentContainer is private in the stock DLL.
        private static readonly FieldInfo s_container = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

        private static Container Chest(InventoryGui gui) =>
            s_container != null ? s_container.GetValue(gui) as Container : null;

        private static void Refresh(InventoryGui gui)
        {
            Clear();
            if (!s_active || gui.m_recipeListRoot == null)
                return;

            float y = 0f;
            foreach (Inventory where in Pool())
            {
                foreach (ItemDrop.ItemData item in where.GetAllItems())
                {
                    if (!HasStyles(item))
                        continue;
                    s_rows.Add(Row(gui, item, y));
                    y -= RowHeight;
                }
            }

            if (s_rows.Count == 0)
                Log.LogInfo("Styles: nothing carried that has more than one look");
        }

        /// <summary>
        /// A cape or a shield with more than one look. Those two because they are the only
        /// places VisEquipment carries a variant - see the class note.
        /// </summary>
        private static bool HasStyles(ItemDrop.ItemData item)
        {
            ItemDrop.ItemData.SharedData d = item?.m_shared;
            if (d == null || d.m_variants <= 1)
                return false;
            return d.m_itemType == ItemDrop.ItemData.ItemType.Shoulder
                || d.m_itemType == ItemDrop.ItemData.ItemType.Shield;
        }

        private static GameObject Row(InventoryGui gui, ItemDrop.ItemData item, float y)
        {
            var go = new GameObject(RowName, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(gui.m_recipeListRoot, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(0f, RowHeight - 4f);
            rt.anchoredPosition = new Vector2(0f, y);

            Canvas canvas = gui.GetComponentInParent<Canvas>();
            float ppu = canvas != null ? canvas.referencePixelsPerUnit : 100f;
            var plate = go.GetComponent<Image>();
            plate.sprite = AssetLoader.Piece("button_thin", ppu);
            plate.type = Image.Type.Sliced;
            plate.color = Color.white;

            // Two lines, not two columns. The list column is about 250 units wide and the cost
            // of a shield runs to "3 Leather Scraps, 1 Wooden Protection Idol" - side by side
            // the two labels landed on top of each other.
            Icon(rt, item);
            Label(rt, Localize(item.m_shared.m_name), -2f, 15f, 1f);
            Label(rt, Cost(item), -24f, 12f, 0.65f);

            ItemDrop.ItemData captured = item;
            go.GetComponent<Button>().onClick.AddListener(() => Select(captured));
            return go;
        }

        private static void Icon(RectTransform row, ItemDrop.ItemData item)
        {
            var go = new GameObject("icon", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(row, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(34f, 34f);
            rt.anchoredPosition = new Vector2(8f, 0f);

            var image = go.GetComponent<Image>();
            image.sprite = item.GetIcon();
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        /// <summary>
        /// One line of a row: stretched across the row, inset past the icon on the left and off
        /// the rule on the right, and never wrapped - a long cost is cut with an ellipsis
        /// rather than pushed onto a second line and into the line below it.
        /// </summary>
        private static void Label(RectTransform row, string text, float y, float size, float bright)
        {
            var go = new GameObject("text", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(row, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            // Inset 48 on the left for the icon and 8 on the right: the width drops by both and
            // the centre shifts by half their difference.
            rt.sizeDelta = new Vector2(-56f, 20f);
            rt.anchoredPosition = new Vector2(20f, y);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = InventoryGui.instance?.m_recipeName?.font;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.color = new Color(bright, bright, bright, 1f);
            label.text = text;
            label.raycastTarget = false;
        }

        // ---- the pane ---------------------------------------------------------------------

        /// <summary>
        /// The right-hand side, built where vanilla's own pane sits and sized to it.
        ///
        /// The item's text is vanilla's, not a copy of it: ItemDrop.ItemData.GetTooltip is
        /// public and static and is the call the crafting pane itself makes, so the stats read
        /// exactly as they do on the Craft tab.
        ///
        /// The styles are the item's own icons - m_shared.m_icons is what the variant indexes
        /// into - and picking one only previews it. Nothing is charged and nothing changes
        /// until the button at the bottom is pressed.
        /// </summary>
        private static void BuildPane(InventoryGui gui)
        {
            if (s_pane != null)
                Object.Destroy(s_pane);
            s_pane = null;

            Transform pane = gui != null ? Pane(gui) : null;
            ItemDrop.ItemData item = s_selected;
            if (pane == null || item == null || item.m_shared == null || !s_active)
                return;

            Canvas canvas = gui.GetComponentInParent<Canvas>();
            float ppu = canvas != null ? canvas.referencePixelsPerUnit : 100f;

            var go = new GameObject("CarturUI_StylePane", typeof(RectTransform));
            s_pane = go;
            var rt = (RectTransform)go.transform;
            var from = (RectTransform)pane;
            rt.SetParent(from.parent, false);
            rt.anchorMin = from.anchorMin;
            rt.anchorMax = from.anchorMax;
            rt.pivot = from.pivot;
            rt.sizeDelta = from.sizeDelta;
            rt.anchoredPosition = from.anchoredPosition;

            string missing;
            bool afford = CanPay(item, out missing);
            bool changed = s_variant != item.m_variant;

            float width = from.rect.width;
            float height = from.rect.height;

            Sprite[] icons = item.m_shared.m_icons;
            Sprite preview = icons != null && s_variant >= 0 && s_variant < icons.Length
                ? icons[s_variant] : item.GetIcon();

            Picture(rt, preview, new Vector2(16f, -16f), 72f);
            Text(rt, Localize(item.m_shared.m_name), new Vector2(100f, -20f), width - 116f, 30f,
                24f, 1f, TextAlignmentOptions.TopLeft, Gold);

            // Laid out from the BOTTOM up. The stats are as long as the item is complicated -
            // a shield with an enchantment runs past twenty lines - so the pieces under them
            // are placed first and the text is given exactly the room that is left. It is also
            // told to truncate: a long tooltip now stops, where before it ran straight through
            // the style buttons.
            float buttonTop = 12f + ApplyHeight;
            float costY = buttonTop + 14f;
            float stylesY = costY + 24f + 14f;
            float headingY = stylesY + StyleBox + 6f;
            float textBottom = headingY + 24f + 10f;

            Apply(rt, ppu, width, changed && afford);
            Text(rt, Cost(item), new Vector2(16f, -(height - costY - 24f)), width - 32f, 24f, 16f, 1f,
                TextAlignmentOptions.TopLeft, afford ? Color.white : Short);
            Styles(rt, item, icons, ppu, -(height - stylesY - StyleBox), width);
            Text(rt, StylesLabel, new Vector2(16f, -(height - headingY - 24f)), width - 32f, 24f, 18f, 1f,
                TextAlignmentOptions.TopLeft, Gold);

            // Vanilla's own builder, then vanilla's own localiser. GetTooltip hands back tokens
            // - $item_weight, $item_blockarmor - because the caller is expected to localise the
            // finished string, which is what the crafting pane does. Ours has to as well, or
            // the pane reads like a language file.
            string tooltip = Localize(
                ItemDrop.ItemData.GetTooltip(item, item.m_quality, false, item.m_worldLevel, 1, false));
            Text(rt, tooltip, new Vector2(16f, -104f), width - 32f, height - 104f - textBottom, 15f, 0.92f,
                TextAlignmentOptions.TopLeft, null, TextOverflowModes.Truncate);

            // Last, so it lands on top of everything this pane just drew.
            Inlays.Decorate(rt);
        }

        /// <summary>
        /// The styles, in a strip that scrolls sideways when there are more than the pane is
        /// wide. A tower shield carries seven and the pane holds six, so the last one used to
        /// hang off the panel and over the world.
        ///
        /// A viewport with a RectMask2D and a ScrollRect, which is Unity's own machinery - the
        /// strip can be dragged or wheeled and is clamped at both ends. The buttons themselves
        /// are unchanged; they just live on the content rect now instead of on the pane.
        /// </summary>
        private static void Styles(RectTransform pane, ItemDrop.ItemData item, Sprite[] icons,
            float ppu, float y, float width)
        {
            if (icons == null)
                return;
            int count = Mathf.Min(icons.Length, item.m_shared.m_variants);
            if (count <= 0)
                return;

            var viewGo = new GameObject("styles", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
            var view = (RectTransform)viewGo.transform;
            view.SetParent(pane, false);
            view.anchorMin = view.anchorMax = new Vector2(0f, 1f);
            view.pivot = new Vector2(0f, 1f);
            view.sizeDelta = new Vector2(width - 32f, StyleBox);
            view.anchoredPosition = new Vector2(16f, y);

            var contentGo = new GameObject("strip", typeof(RectTransform));
            var content = (RectTransform)contentGo.transform;
            content.SetParent(view, false);
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.sizeDelta = new Vector2(count * (StyleBox + StyleGap), 0f);
            content.anchoredPosition = Vector2.zero;

            var scroll = viewGo.GetComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = view;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = StyleBox;
            scroll.inertia = false;

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("style" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                var rt = (RectTransform)go.transform;
                rt.SetParent(content, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(StyleBox, StyleBox);
                rt.anchoredPosition = new Vector2(i * (StyleBox + StyleGap), 0f);

                var plate = go.GetComponent<Image>();
                plate.sprite = AssetLoader.Piece("slot", ppu);
                plate.type = Image.Type.Sliced;
                plate.fillCenter = false;
                plate.color = i == s_variant ? Color.white : new Color(0.5f, 0.5f, 0.5f, 1f);

                Picture(rt, icons[i], new Vector2(6f, -6f), StyleBox - 12f);

                int pick = i;
                go.GetComponent<Button>().onClick.AddListener(delegate
                {
                    s_variant = pick;
                    BuildPane(InventoryGui.instance);
                });
            }
        }

        private static void Apply(RectTransform pane, float ppu, float width, bool live)
        {
            var go = new GameObject("apply", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(pane, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(-32f, ApplyHeight);
            rt.anchoredPosition = new Vector2(0f, 12f);

            var plate = go.GetComponent<Image>();
            plate.sprite = AssetLoader.Piece(live ? "button_thin" : "button_thin_disabled", ppu);
            plate.type = Image.Type.Sliced;

            Text(rt, ChangeLabel, Vector2.zero, width - 48f, ApplyHeight, 20f, 1f,
                TextAlignmentOptions.Center, live ? Gold : Short);

            var button = go.GetComponent<Button>();
            button.interactable = live;
            button.onClick.AddListener(Change);
        }

        private static void Picture(RectTransform parent, Sprite sprite, Vector2 at, float size)
        {
            var go = new GameObject("icon", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = at;

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private static void Text(RectTransform parent, string text, Vector2 at, float width, float height,
            float size, float bright, TextAlignmentOptions align, Color? colour = null,
            TextOverflowModes overflow = TextOverflowModes.Overflow)
        {
            var go = new GameObject("text", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            bool centred = align == TextAlignmentOptions.Center;
            rt.anchorMin = rt.anchorMax = new Vector2(centred ? 0.5f : 0f, centred ? 0.5f : 1f);
            rt.pivot = new Vector2(centred ? 0.5f : 0f, centred ? 0.5f : 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = at;

            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = InventoryGui.instance != null && InventoryGui.instance.m_recipeDecription != null
                ? InventoryGui.instance.m_recipeDecription.font : null;
            label.fontSize = size;
            label.alignment = align;
            label.text = text;
            label.color = colour.HasValue ? colour.Value : new Color(bright, bright, bright, 1f);
            label.overflowMode = overflow;
            label.raycastTarget = false;
        }

        private const float StyleBox = 52f;
        private const float StyleGap = 6f;
        private const float ApplyHeight = 44f;
        private const string StylesLabel = "Styles";
        private const string ChangeLabel = "Change style";
        private static readonly Color Gold = new Color(1f, 0.79f, 0.29f, 1f);
        private static readonly Color Short = new Color(0.85f, 0.35f, 0.3f, 1f);

        // ---- the cost ---------------------------------------------------------------------

        /// <summary>
        /// A tenth of what the item costs to make, rounded up, never less than one of anything.
        /// Taken at the item's own quality, so a level 3 cape is priced as a level 3 cape.
        /// </summary>
        private static List<KeyValuePair<string, int>> Price(ItemDrop.ItemData item)
        {
            var bill = new List<KeyValuePair<string, int>>();
            Recipe recipe = ObjectDB.instance != null ? ObjectDB.instance.GetRecipe(item) : null;
            if (recipe?.m_resources == null)
                return bill;

            var said = new StringBuilder();
            foreach (Piece.Requirement req in recipe.m_resources)
            {
                if (req == null || req.m_resItem == null)
                    continue;
                int full = req.GetAmount(item.m_quality);

                // Base game materials only. EpicLoot adds an enchanting token to armour and
                // shield recipes - Upgrader0Armor, the Wooden Protection Idol - and that is a
                // fee for enchanting, not part of what the shield is made of, so it has no
                // business in the price of a colour. Cartur's own log:
                //   Recipe_ShieldWoodTower: Wood 10, LeatherScraps 6, Upgrader0Armor 1
                //   vanilla's recipe:       Wood 10, LeatherScraps 6
                //
                // Keyed on the name because that is what identifies them: EpicLoot.dll carries
                // Upgrader7Armor and Upgrader7Weapon, so the whole Upgrader<n><type> family is
                // theirs. Nothing matches with EpicLoot absent, and a vanilla ingredient needed
                // only once still counts - it is a real material.
                if (req.m_resItem.name.StartsWith("Upgrader"))
                    continue;
                if (full <= 0)
                    continue;

                int charged = Mathf.Max(1, Mathf.CeilToInt(full * CostShare));

                // A requirement's ItemDrop is a prefab, and a prefab's m_itemData is only
                // filled in once something has woken it. Reaching straight through it for the
                // name is what threw on the Styles list; the prefab's own name is the fallback,
                // which is at worst untranslated rather than a crash.
                string res = req.m_resItem.m_itemData != null && req.m_resItem.m_itemData.m_shared != null
                    ? req.m_resItem.m_itemData.m_shared.m_name
                    : req.m_resItem.name;

                bill.Add(new KeyValuePair<string, int>(res, charged));
                said.Append("  ").Append(res).Append(" ").Append(full).Append(" -> ").Append(charged);
            }

            // Said once per item. Cartur asked why a style change wants a Wooden Protection
            // Idol: the bill is whatever ObjectDB hands back as that item's recipe, and with a
            // minimum of one, a component the recipe only needs once is still charged once.
            // This prints the recipe as read, so the answer comes from the game, not from me.
            // The recipe's own name is NOT read here. Recipe is a UnityEngine.Object, and
            // reading .name on one the engine has already torn down throws - a plain C# null
            // check does not catch that, because a destroyed object is not null to C#.
            if (item.m_shared != null && s_priced.Add(item.m_shared.m_name))
                Log.LogInfo("style cost for " + item.m_shared.m_name + ":" + said);
            return bill;
        }

        private static string Cost(ItemDrop.ItemData item)
        {
            var text = new StringBuilder();
            foreach (KeyValuePair<string, int> line in Price(item))
            {
                if (text.Length > 0)
                    text.Append(", ");
                text.Append(line.Value).Append(" ").Append(Localize(line.Key));
            }
            return text.Length > 0 ? text.ToString() : "free";
        }

        /// <summary>
        /// The price as the game's own requirement list, so it can be handed to the game's own
        /// machinery instead of being counted and taken by hand.
        ///
        /// This is the whole point: Cartur's Craft From Containers prefixes
        /// Player.ConsumeResources and Player.HaveRequirementItems, and it already reaches
        /// chests AND kg ItemDrawers (its Sources.Drawers calls API.ClientSideV2.AllDrawers).
        /// Everything in that mod is internal, so there is nothing for us to call - but the
        /// vanilla methods it wraps are public, and going through them means a style change
        /// draws on exactly what a craft draws on, with no knowledge of drawers here at all.
        ///
        /// With no such mod installed these are plain vanilla calls against the player's own
        /// inventory, which is the right behaviour then too.
        /// </summary>
        private static Piece.Requirement[] Requirements(ItemDrop.ItemData item)
        {
            List<KeyValuePair<string, int>> bill = Price(item);
            var reqs = new List<Piece.Requirement>();
            Recipe recipe = ObjectDB.instance != null ? ObjectDB.instance.GetRecipe(item) : null;
            if (recipe == null || recipe.m_resources == null)
                return reqs.ToArray();

            foreach (Piece.Requirement req in recipe.m_resources)
            {
                if (req == null || req.m_resItem == null)
                    continue;
                string name = req.m_resItem.m_itemData != null && req.m_resItem.m_itemData.m_shared != null
                    ? req.m_resItem.m_itemData.m_shared.m_name
                    : req.m_resItem.name;

                foreach (KeyValuePair<string, int> line in bill)
                {
                    if (line.Key != name)
                        continue;
                    // A fresh requirement rather than the recipe's own: the amount is ours, and
                    // writing it onto the game's object would change what the item costs to
                    // make for the rest of the session.
                    reqs.Add(new Piece.Requirement
                    {
                        m_resItem = req.m_resItem,
                        m_amount = line.Value,
                        m_amountPerLevel = 0,
                        m_recover = false,
                    });
                    break;
                }
            }
            return reqs.ToArray();
        }

        // One throwaway recipe, reused. It carries nothing but our requirements and is never
        // registered anywhere - it exists only to ask HaveRequirements a question about an
        // amount that is not any real recipe's.
        private static Recipe s_ask;

        /// <summary>
        /// Whether the price can be met, asked the way crafting asks it.
        /// </summary>
        private static bool CanPay(ItemDrop.ItemData item, out string missing)
        {
            missing = null;
            Player player = Player.m_localPlayer;
            Piece.Requirement[] reqs = Requirements(item);
            if (player == null)
                return false;
            if (reqs.Length == 0)
                return true;

            try
            {
                // A COPY of the item's real recipe, with only the amounts swapped. An empty
                // Recipe made from scratch threw inside HaveRequirements - it reaches for
                // fields a bare ScriptableObject has not got - and the fallback below then hid
                // the containers, which is exactly what Cartur was seeing.
                Recipe real = ObjectDB.instance != null ? ObjectDB.instance.GetRecipe(item) : null;
                if (real != null)
                {
                    if (s_ask != null)
                        Object.Destroy(s_ask);
                    s_ask = Object.Instantiate(real);
                    s_ask.m_resources = reqs;

                    // Whatever station we are standing at, so restyling at a workbench is not
                    // refused because the item itself is forged somewhere else.
                    s_ask.m_craftingStation = player.GetCurrentCraftingStation();
                    s_ask.m_minStationLevel = 0;

                    if (player.HaveRequirements(s_ask, false, 1, 1))
                        return true;
                }
            }
            catch (System.Exception e)
            {
                // Asking through a made-up recipe reaches another mod's prefix, so a throw here
                // is its business, not a reason to stop. Counted by hand instead, which finds
                // less - the bag and the open chest - and so only ever under-offers.
                if (s_askFailed == null)
                {
                    s_askFailed = e.GetType().Name;
                    Log.LogWarning("style cost check fell back to counting by hand: " + e.Message);
                }
            }

            // Either the answer was no, or nobody could be asked. Name the first line that is
            // short so the player is told what to fetch.
            foreach (KeyValuePair<string, int> line in Price(item))
            {
                int have = 0;
                foreach (Inventory where in Pool())
                    have += where.CountItems(line.Key, -1, true);
                if (have < line.Value)
                {
                    missing = Localize(line.Key);
                    return s_askFailed != null && have >= line.Value;
                }
            }

            // Nothing is short by our own count, so if the ask failed, allow it.
            return s_askFailed != null;
        }

        private static string s_askFailed;

        // ---- changing the style -------------------------------------------------------------

        /// <summary>
        /// Takes the price and puts the chosen style on the item.
        ///
        /// The picker that used to open here - vanilla's VariantDialog - is gone: the styles
        /// are laid out in the pane now, so there is nothing to borrow and nothing to hand back.
        ///
        /// Checked twice on purpose. The pane only offers the button when it can be paid, but
        /// the inventory can change under an open window - a chest, another player, a mod - and
        /// charging for a change that then fails would be the one bug worth avoiding here.
        /// </summary>
        private static void Change()
        {
            InventoryGui gui = InventoryGui.instance;
            ItemDrop.ItemData item = s_selected;
            Player player = Player.m_localPlayer;
            if (item == null || player == null || s_variant == item.m_variant)
                return;

            string missing;
            if (!CanPay(item, out missing))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement " + missing);
                return;
            }

            // The game's own taking, so Craft From Containers plans it across the bag, the
            // chests in range and the drawers exactly as it would for a craft.
            player.ConsumeResources(Requirements(item), 1, 1, 1);
            item.m_variant = s_variant;

            // Setting the variant changes nothing the UI watches, so every inventory in the
            // pool is told something changed - the one holding the item so its icon redraws,
            // and the one that paid so its own window does.
            foreach (Inventory where in Pool())
            {
                if (s_changed != null)
                    s_changed.Invoke(where, new object[] { false, false });
            }

            // And the cape or shield on the body is rebuilt from the inventory, so it only
            // follows once the game is told to look again.
            if (s_setupEquipment != null)
                s_setupEquipment.Invoke(player, null);

            Log.LogInfo("style changed: " + item.m_shared.m_name + " to " + s_variant);

            if (gui != null)
            {
                Refresh(gui);
                Select(item);
            }
        }

        private static void Clear()
        {
            foreach (GameObject row in s_rows)
            {
                if (row != null)
                    Object.Destroy(row);
            }
            s_rows.Clear();

            if (s_pane != null)
                Object.Destroy(s_pane);
            s_pane = null;
            s_selected = null;
        }

        private static string Localize(string token) =>
            Localization.instance != null ? Localization.instance.Localize(token) : token;
    }
}
