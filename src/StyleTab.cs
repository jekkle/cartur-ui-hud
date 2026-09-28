using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
        private const float CostShare = 0.1f;

        private static Button s_tab;
        private static bool s_active;
        private static readonly List<GameObject> s_rows = new List<GameObject>();
        private static readonly List<ItemDrop.ItemData> s_rowItems = new List<ItemDrop.ItemData>();
        private static readonly HashSet<string> s_priced = new HashSet<string>();

        // What is selected in the list, and which style is being looked at. The style starts as
        // the one the item already wears, so the pane opens showing the truth.
        private static ItemDrop.ItemData s_selected;
        private static int s_variant;
        private static GameObject s_pane;
        private static bool s_saidButton;   // the Change button is measured into the log once

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
            s_rowItems.Clear();
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
            Mark();
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

            ItemDrop.ItemData first = null;
            foreach (Inventory where in Pool())
            {
                foreach (ItemDrop.ItemData item in where.GetAllItems())
                {
                    if (!HasStyles(item))
                        continue;
                    if (first == null)
                        first = item;
                    s_rows.Add(Row(gui, item, s_rows.Count));
                    s_rowItems.Add(item);
                }
            }

            if (s_rows.Count == 0)
                Log.LogInfo("Styles: nothing carried that has more than one look");

            // The top row is picked for you, so the tab opens on an item rather than on an
            // empty pane. Select builds the pane itself; with nothing carried there is no row
            // to pick and the art-only pane stands in.
            if (first != null)
                Select(first);
            else
                BuildPane(gui);
        }

        /// <summary>
        /// A cape, a shield, or a one-handed weapon with more than one look.
        ///
        /// The first two because they are the only places VisEquipment carries a variant - see
        /// the class note. One-handed weapons are here for a different reason: the right hand
        /// has no variant field at all, so nothing vanilla will change the model, but a mod can
        /// swap the mesh on m_rightItemInstance after VisEquipment.SetRightHandEquipped attaches
        /// it. Cartur's Weapon Styles does exactly that, and stores its choice in m_variant like
        /// everything else here, so the pane and the pricing work unchanged.
        ///
        /// No vanilla one-handed weapon ships with m_variants > 1 - $item_sword_bronze has
        /// m_variants = 0 and one icon, read from the game - so on an unmodded install this
        /// line lists nothing new.
        /// </summary>
        private static bool HasStyles(ItemDrop.ItemData item)
        {
            ItemDrop.ItemData.SharedData d = item?.m_shared;
            if (d == null || d.m_variants <= 1)
                return false;
            return d.m_itemType == ItemDrop.ItemData.ItemType.Shoulder
                || d.m_itemType == ItemDrop.ItemData.ItemType.Shield
                || d.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon
                // Bows. A bow carries a real variant the way a shield does - Humanoid.EquipItem
                // tests m_itemType against 4 (ItemType.Bow) at IL_050b and stores the item in
                // m_leftItem at IL_052d - so the style travels in the ZDO and other players see
                // it. Without this line the two Rotvein bow styles register fine and are simply
                // never listed, which is exactly how they presented: the loader logged
                // "BowFineWood style is variant 1 of 2" and the tab still showed nothing.
                || d.m_itemType == ItemDrop.ItemData.ItemType.Bow;
        }

        /// <summary>
        /// Vanilla's own row, not a hand-built copy of one. The rows here used to be a plate, an
        /// icon and two labels with their sizes written down, and they came out visibly smaller
        /// than the Craft tab's beside them.
        ///
        /// InventoryGui.AddRecipeToList instantiates m_recipeElementPrefab into
        /// m_recipeListRoot, spaces the rows by m_recipeListSpace, and fills them by finding
        /// "icon", "name", "Durability", "QualityLevel" and "selected" inside by those names.
        /// All of that is read off that method, and doing the same here means the row IS the
        /// Craft tab's row - same plate, same icon size, same font, same spacing - with no
        /// number in this file left to drift out of step with it. The prefab is also the one
        /// this mod already skins, see InventoryScreen.
        ///
        /// Durability and QualityLevel are switched off: vanilla switches them on for the cases
        /// that need them, so a fresh instance left alone shows whatever the prefab was saved
        /// with rather than nothing.
        /// </summary>
        private static GameObject Row(InventoryGui gui, ItemDrop.ItemData item, int index)
        {
            var go = Object.Instantiate(gui.m_recipeElementPrefab, gui.m_recipeListRoot);
            go.name = RowName;              // Vanilla() leaves our rows alone by this name
            go.SetActive(true);

            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = new Vector2(0f, -index * gui.m_recipeListSpace);

            var icon = go.transform.Find("icon")?.GetComponent<Image>();
            if (icon != null)
            {
                icon.sprite = item.GetIcon();
                icon.color = Color.white;
            }

            Off(go, "Durability");
            Off(go, "QualityLevel");
            Off(go, "selected");            // Mark switches the picked one back on

            // The cost goes under the name as a second label cloned from the name itself, so it
            // carries vanilla's own font and rect and sits at a fraction of vanilla's size
            // rather than at a size chosen here. The two are pushed half a line apart.
            var name = go.transform.Find("name")?.GetComponent<TMP_Text>();
            if (name != null)
            {
                name.text = Localize(item.m_shared.m_name);
                name.color = Color.white;
                float line = name.fontSize;
                var nameRt = (RectTransform)name.transform;

                var cost = Object.Instantiate(name.gameObject, go.transform).GetComponent<TMP_Text>();
                cost.name = "cost";
                cost.fontSize = line * CostTextShare;
                cost.color = new Color(0.65f, 0.65f, 0.65f, 1f);
                cost.overflowMode = TextOverflowModes.Ellipsis;
                cost.textWrappingMode = TextWrappingModes.NoWrap;
                cost.text = Cost(item);

                var costRt = (RectTransform)cost.transform;
                costRt.anchorMin = nameRt.anchorMin;
                costRt.anchorMax = nameRt.anchorMax;
                costRt.pivot = nameRt.pivot;
                costRt.sizeDelta = nameRt.sizeDelta;
                costRt.anchoredPosition = nameRt.anchoredPosition - new Vector2(0f, line * 0.7f);
                nameRt.anchoredPosition += new Vector2(0f, line * 0.35f);
            }

            ItemDrop.ItemData captured = item;
            var button = go.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();   // the prefab's own recipe click
                button.onClick.AddListener(() => Select(captured));
            }
            return go;
        }

        private static void Off(GameObject row, string child)
        {
            Transform t = row.transform.Find(child);
            if (t != null)
                t.gameObject.SetActive(false);
        }

        /// <summary>
        /// The highlight is vanilla's own "selected" child - the same one SetRecipe switches on
        /// for the Craft tab, so the picked row reads the same on all three.
        /// </summary>
        private static void Mark()
        {
            for (int i = 0; i < s_rows.Count; i++)
            {
                Transform t = s_rows[i] != null ? s_rows[i].transform.Find("selected") : null;
                if (t != null)
                    t.gameObject.SetActive(i < s_rowItems.Count && s_rowItems[i] == s_selected);
            }
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
            if (pane == null || !s_active)
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

            // Nothing picked yet. The pane is still built, and carries only the serpent, so
            // the tab opens with the art the crafting pane has instead of a bare hole. It is
            // the same rect copied from the same vanilla pane, so it is the same size and in
            // the same place as on Craft.
            if (item == null || item.m_shared == null)
            {
                Inlays.Decorate(rt);
                return;
            }

            string missing;
            bool afford = CanPay(item, out missing);
            bool changed = s_variant != item.m_variant;

            float width = from.rect.width;
            float height = from.rect.height;

            // Every size below is the Craft tab's own, read off the very components the
            // crafting pane draws with. They were written down here as 72, 24, 15 and 44 and
            // came out smaller than the pane beside them; taken this way they cannot be wrong,
            // and they follow the game or a skin if either changes them.
            float nameSize = Size(gui.m_recipeName, 24f);
            float bodySize = Size(gui.m_recipeDecription, 15f);
            float iconSize = Side(gui.m_recipeIcon, 72f);
            float buttonHeight = Side(gui.m_craftButton, 44f, tall: true);

            // The style boxes are the inventory's own slot. InventoryGrid.UpdateGui places its
            // elements at (x * m_elementSpace, y * -m_elementSpace) and instantiates
            // m_elementPrefab at each spot, so the prefab's rect IS the box and m_elementSpace
            // is the pitch - read off the live grid, not written down here. If the grid has
            // gone, the pane falls back to the smaller boxes this file used to draw.
            float box = Box(gui);
            float pitch = gui.m_playerGrid != null && gui.m_playerGrid.m_elementSpace > box
                ? gui.m_playerGrid.m_elementSpace : box + StyleGap;

            float textLeft = Pad + iconSize + Pad;

            Sprite[] icons = item.m_shared.m_icons;
            Sprite preview = icons != null && s_variant >= 0 && s_variant < icons.Length
                ? icons[s_variant] : item.GetIcon();

            Picture(rt, preview, new Vector2(Pad, -Pad), iconSize);
            Text(rt, Localize(item.m_shared.m_name), new Vector2(textLeft, -Pad),
                width - textLeft - Pad, nameSize * 1.3f,
                nameSize, 1f, TextAlignmentOptions.TopLeft, Gold);

            // Laid out from the BOTTOM up. The stats are as long as the item is complicated -
            // a shield with an enchantment runs past twenty lines - so the pieces under them
            // are placed first and the text is given exactly the room that is left. It is also
            // told to truncate: a long tooltip now stops, where before it ran straight through
            // the style buttons.
            float costLine = bodySize * 1.6f;
            float barHeight = BarHeight(gui);
            float buttonTop = 12f + buttonHeight;
            float costY = buttonTop + 14f;
            float barY = costY + costLine + 10f;
            float stylesY = barY + barHeight + 6f;
            float headingY = stylesY + box + 6f;
            float textBottom = headingY + costLine + 10f;

            Apply(gui, rt, changed && afford);
            Text(rt, Cost(item), new Vector2(Pad, -(height - costY - costLine)), width - Pad * 2f,
                costLine, bodySize, 1f,
                TextAlignmentOptions.TopLeft, afford ? Color.white : Short);
            Styles(gui, rt, item, icons, ppu, -(height - stylesY - box), width, box, pitch, barHeight);
            Text(rt, StylesLabel, new Vector2(Pad, -(height - headingY - costLine)), width - Pad * 2f,
                costLine, bodySize * 1.15f, 1f,
                TextAlignmentOptions.TopLeft, Gold);

            // Vanilla's own builder, then vanilla's own localiser. GetTooltip hands back tokens
            // - $item_weight, $item_blockarmor - because the caller is expected to localise the
            // finished string, which is what the crafting pane does. Ours has to as well, or
            // the pane reads like a language file.
            string tooltip = Localize(
                ItemDrop.ItemData.GetTooltip(item, item.m_quality, false, item.m_worldLevel, 1, false));
            float textTop = Pad + iconSize + Pad;
            Text(rt, tooltip, new Vector2(Pad, -textTop), width - Pad * 2f, height - textTop - textBottom,
                bodySize, 0.92f,
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
        ///
        /// The style the item is WEARING is not in the strip. It is on the item already, it is
        /// the picture at the top of the pane, and picking it is the one choice that can never
        /// be paid for - Change returns early on it. So the strip is the alternatives only, and
        /// the worn one comes back into it as soon as the item is wearing something else.
        ///
        /// Under the strip is a bar to drag, and it is vanilla's own - m_recipeListScroll, the
        /// bar beside the recipe list on this very panel - cloned and laid on its side. Same
        /// reason the Change button is a clone of the Craft button: the look is the game's, and
        /// it follows a skin or a game update with nothing here to keep in step.
        /// </summary>
        private static void Styles(InventoryGui gui, RectTransform pane, ItemDrop.ItemData item,
            Sprite[] icons, float ppu, float y, float width, float box, float pitch, float barHeight)
        {
            if (icons == null)
                return;
            int count = Mathf.Min(icons.Length, item.m_shared.m_variants);
            int offer = count - (item.m_variant >= 0 && item.m_variant < count ? 1 : 0);
            if (offer <= 0)
                return;

            var viewGo = new GameObject("styles", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
            var view = (RectTransform)viewGo.transform;
            view.SetParent(pane, false);
            view.anchorMin = view.anchorMax = new Vector2(0f, 1f);
            view.pivot = new Vector2(0f, 1f);
            view.sizeDelta = new Vector2(width - 32f, box);
            view.anchoredPosition = new Vector2(16f, y);

            var contentGo = new GameObject("strip", typeof(RectTransform));
            var content = (RectTransform)contentGo.transform;
            content.SetParent(view, false);
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.sizeDelta = new Vector2(offer * pitch, 0f);
            content.anchoredPosition = Vector2.zero;

            var scroll = viewGo.GetComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = view;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            // One notch of the wheel was one box, which is a long grind through a seven-style
            // shield. Three boxes a notch, measured in the same pitch the boxes are placed at,
            // so it stays three whole boxes whatever the grid's spacing is.
            scroll.scrollSensitivity = pitch * WheelBoxes;
            scroll.inertia = false;

            Wheel(pane, scroll);
            Bar(gui, pane, scroll, view, content, width, y - box - 6f, barHeight);

            int slot = 0;
            for (int i = 0; i < count; i++)
            {
                if (i == item.m_variant)
                    continue;

                var go = new GameObject("style" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                var rt = (RectTransform)go.transform;
                rt.SetParent(content, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(box, box);
                rt.anchoredPosition = new Vector2(slot * pitch, 0f);
                slot++;

                var plate = go.GetComponent<Image>();
                plate.sprite = AssetLoader.Piece("slot", ppu);
                plate.type = Image.Type.Sliced;
                plate.fillCenter = false;
                plate.color = i == s_variant ? Color.white : new Color(0.5f, 0.5f, 0.5f, 1f);

                Picture(rt, icons[i], new Vector2(6f, -6f), box - 12f);

                int pick = i;
                go.GetComponent<Button>().onClick.AddListener(delegate
                {
                    s_variant = pick;
                    BuildPane(InventoryGui.instance);
                });
            }
        }

        /// <summary>
        /// The wheel works over the whole pane, not only over the strip.
        ///
        /// Unity sends a scroll to the object under the cursor and then up its parents, and the
        /// strip is one row of boxes - so over the stats, the cost or the button, which is most
        /// of the pane, the wheel reached nothing and did nothing. This puts a handler on the
        /// pane root, which IS a parent of everything drawn here, and a clear plate under it so
        /// the empty parts of the pane are something the raycaster can hit at all.
        ///
        /// The handler hands the event straight to the ScrollRect rather than moving the strip
        /// itself, so the speed and the clamping are the one set the ScrollRect already has and
        /// there is no second number here to drift out of step. The cursor is not hit-tested by
        /// hand: that was tried in HudLayout, worked here and not on other machines, and the
        /// EventSystem does the job. See that file's note.
        /// </summary>
        private static void Wheel(RectTransform pane, ScrollRect scroll)
        {
            var plate = pane.GetComponent<Image>();
            if (plate == null)
            {
                plate = pane.gameObject.AddComponent<Image>();
                plate.color = new Color(0f, 0f, 0f, 0f);
            }
            plate.raycastTarget = true;

            PaneWheel wheel = pane.GetComponent<PaneWheel>() ?? pane.gameObject.AddComponent<PaneWheel>();
            wheel.Strip = scroll;
        }

        /// <summary>A scroll anywhere on the pane, given to the strip.</summary>
        private sealed class PaneWheel : MonoBehaviour, IScrollHandler
        {
            internal ScrollRect Strip;

            public void OnScroll(PointerEventData eventData)
            {
                if (Strip != null)
                    ExecuteEvents.Execute(Strip.gameObject, eventData, ExecuteEvents.scrollHandler);
            }
        }

        /// <summary>
        /// Vanilla's scrollbar, cloned and laid on its side under the strip.
        ///
        /// Only when there is something to scroll. A bar whose handle fills it is a bar that
        /// says the wrong thing - it looks draggable and does nothing - so with every style
        /// already on screen there is no bar and the pane is that much shorter.
        ///
        /// The clone's own listeners go first: it arrives wired to the recipe list, and
        /// ScrollRect.horizontalScrollbar adds the one that drives ours.
        /// </summary>
        private static void Bar(InventoryGui gui, RectTransform pane, ScrollRect scroll,
            RectTransform view, RectTransform content, float width, float y, float barHeight)
        {
            Scrollbar model = gui != null ? gui.m_recipeListScroll : null;
            if (model == null || content.sizeDelta.x <= view.sizeDelta.x)
                return;

            var go = Object.Instantiate(model.gameObject, pane);
            go.name = "styles_scroll";
            go.SetActive(true);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(view.sizeDelta.x, barHeight);
            rt.anchoredPosition = new Vector2(16f, y);

            var bar = go.GetComponent<Scrollbar>();
            if (bar == null)
            {
                Log.LogWarning("the recipe scrollbar has no Scrollbar on its root - no bar under the styles");
                Object.Destroy(go);
                return;
            }
            bar.onValueChanged.RemoveAllListeners();
            bar.direction = Scrollbar.Direction.LeftToRight;
            bar.value = 0f;
            scroll.horizontalScrollbar = bar;
        }

        /// <summary>
        /// The thickness of one of vanilla's own scrollbars, which is a standing bar's width and
        /// a lying one's height. Off m_recipeListScroll, the bar beside the recipe list on this
        /// same panel, so ours is as thick as the one next to it.
        /// </summary>
        private static float BarHeight(InventoryGui gui)
        {
            var rt = gui != null && gui.m_recipeListScroll != null
                ? gui.m_recipeListScroll.transform as RectTransform : null;
            float v = rt != null ? rt.rect.width : 0f;
            return v > 1f ? v : BarFallback;
        }

        /// <summary>
        /// The Craft button itself, cloned.
        ///
        /// This used to be a plate of our own - a sprite out of the bundle for live, a second
        /// one for dead, and our two colours on the label. It never matched, because vanilla
        /// does not draw a second button for "cannot craft": InventoryGui.UpdateRecipe only
        /// ever sets m_craftButton.interactable, and the look of both states lives on the
        /// prefab - its Selectable ColorBlock, its transition, its label. Read off the DLL,
        /// not guessed.
        ///
        /// So the button is vanilla's, copied. Setting interactable on the copy gives the
        /// enabled and disabled look the Craft tab has, for free, and it follows a skin or a
        /// game update the same way vanilla's does.
        /// </summary>
        private static void Apply(InventoryGui gui, RectTransform pane, bool live)
        {
            Button model = gui != null ? gui.m_craftButton : null;
            if (model == null)
            {
                Log.LogWarning("no craft button to copy - no Change style button on this pane");
                return;
            }

            var go = Object.Instantiate(model.gameObject, pane);
            go.name = "apply";
            go.SetActive(true);

            // The height comes off the MODEL, not off the clone. It used to be read from the
            // clone's own rect on the line that set it - after its anchors had just been
            // changed to the bottom edge. rect.height is sizeDelta.y once a rect stops
            // stretching vertically, and vanilla's button does stretch, so the number read back
            // was the stretched rect's inset and not a height at all: a button of no height,
            // which is a pane with no Change button on it.
            var modelRect = (RectTransform)model.transform;
            float height = modelRect.rect.height;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(-Pad * 2f, height);
            rt.anchoredPosition = new Vector2(0f, 12f);

            // Temporary. The button went missing and this says which of the two it was - a
            // height of nothing, or a button placed off the pane - in one look at the log
            // rather than one more guess. Once. Delete when it has been read.
            if (s_saidButton == false)
            {
                s_saidButton = true;
                Log.LogInfo("style button: model rect " + modelRect.rect.width + "x" + modelRect.rect.height
                    + " anchors " + modelRect.anchorMin + "-" + modelRect.anchorMax
                    + " sizeDelta " + modelRect.sizeDelta
                    + " | clone " + rt.rect.width + "x" + rt.rect.height + " at " + rt.anchoredPosition
                    + " on a pane " + pane.rect.width + "x" + pane.rect.height);
            }

            foreach (TMP_Text label in go.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label != null)
                    label.text = ChangeLabel;
            }

            // The clone brings the craft button's hover text with it, which would say the wrong
            // thing over ours.
            var tip = go.GetComponent<UITooltip>();
            if (tip != null)
                tip.m_text = "";

            var button = go.GetComponent<Button>();
            if (button == null)
            {
                Log.LogWarning("the craft button has no Button on its root - no Change style button");
                return;
            }
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(Change);
            button.interactable = live;
        }

        /// <summary>
        /// A font size off one of vanilla's own labels, or the number this file used to carry
        /// if the field has gone. Null-checked because a member found today can vanish in a
        /// game update, and a pane drawn a little wrong beats a pane that throws.
        /// </summary>
        private static float Size(TMP_Text text, float fallback) =>
            text != null && text.fontSize > 0f ? text.fontSize : fallback;

        /// <summary>
        /// The side of one inventory slot, off the grid's own element prefab. The fallback is
        /// the smaller box this pane drew before, so a missing grid costs a look, not a pane.
        /// </summary>
        private static float Box(InventoryGui gui)
        {
            GameObject prefab = gui != null && gui.m_playerGrid != null
                ? gui.m_playerGrid.m_elementPrefab : null;
            var rt = prefab != null ? prefab.transform as RectTransform : null;
            float v = rt != null ? Mathf.Max(rt.rect.width, rt.rect.height) : 0f;
            return v > 1f ? v : StyleBox;
        }

        /// <summary>The height, or for a square thing the side, of one of vanilla's own rects.</summary>
        private static float Side(Component c, float fallback, bool tall = false)
        {
            var rt = c != null ? c.transform as RectTransform : null;
            if (rt == null)
                return fallback;
            float v = tall ? rt.rect.height : Mathf.Max(rt.rect.width, rt.rect.height);
            return v > 1f ? v : fallback;
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
        private const float WheelBoxes = 3f;    // boxes moved per notch of the wheel
        private const float BarFallback = 12f;  // only if m_recipeListScroll has gone
        private const float CostTextShare = 0.72f;
        private const float Pad = 16f;
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
            s_rowItems.Clear();

            if (s_pane != null)
                Object.Destroy(s_pane);
            s_pane = null;
            s_selected = null;
        }

        private static string Localize(string token) =>
            Localization.instance != null ? Localization.instance.Localize(token) : token;
    }
}
