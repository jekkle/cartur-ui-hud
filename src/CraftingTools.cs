using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Search, category and craft-N on the crafting board ([Panels] craftingTools).
    ///
    /// Search and category work on the list the game is about to build, not on the rows after: the
    /// Prefix on InventoryGui.UpdateRecipeList removes recipes from the list it is handed (READ:
    /// UpdateCraftingPanel fills m_tempRecipes and passes it in), so the game lays out, sorts and
    /// selects as it always does. A change of text or category runs UpdateCraftingPanel again.
    /// Categories come from the item's own SharedData.m_itemType, no per-item table.
    ///
    /// Craft N rides the game's own multicraft. READ (InventoryGui.UpdateRecipe, OnCraftPressed,
    /// DoCrafting): holding "AltPlace" crafts m_multiCraftAmount at once - requirements, button label,
    /// description and DoCrafting's HaveRequirements / CanAddItem / ConsumeResources / skill raise all
    /// take that amount, and nothing else reads AltPlace in the inventory. So the quantity control sets
    /// m_multiCraftAmount and answers "held" for AltPlace while the inventory is open, and the game
    /// does the rest, other mods' hooks included. The count is capped at what the requirements and the
    /// free space allow. The game does not gate crafting on carry weight, so neither does this.
    ///
    /// While the search box has focus the game would still read the keys typed into it: E is "Use",
    /// which InventoryGui.Update takes as "close the inventory" (READ). Player hotkeys are already
    /// off with the inventory open (Player.TakeInput). Use is reset before Update while typing.
    /// </summary>
    internal static class CraftingTools
    {
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static bool On;

        private const int MaxQty = 50;
        private static readonly string[] Names = { "All", "Weapons", "Armor", "Tools", "Consumables", "Materials", "Misc" };

        private static string s_query = "";
        private static int s_cat, s_qty = 1, s_max = 1, s_vanilla;
        private static bool s_upgrade;
        private static Recipe s_recipe;
        private static TMP_InputField s_field;
        private static Button s_catButton, s_minus, s_plus;
        private static TMP_Text s_catText;

        private static readonly FieldInfo s_list = AccessTools.Field(typeof(InventoryGui), "m_availableRecipes");
        private static readonly FieldInfo s_timer = AccessTools.Field(typeof(InventoryGui), "m_craftTimer");
        private static readonly MethodInfo s_update = AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel");
        private static readonly PropertyInfo s_pairRecipe = AccessTools.Property(AccessTools.Inner(typeof(InventoryGui), "RecipeDataPair"), "Recipe");
        private static readonly PropertyInfo s_pairItem = AccessTools.Property(AccessTools.Inner(typeof(InventoryGui), "RecipeDataPair"), "ItemData");

        private static int CategoryOf(ItemDrop.ItemData.ItemType t)
        {
            switch (t)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                    return 1;
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Hands:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket:
                    return 2;
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Torch:
                    return 3;
                case ItemDrop.ItemData.ItemType.Consumable:
                    return 4;
                case ItemDrop.ItemData.ItemType.Material:
                case ItemDrop.ItemData.ItemType.Trophy:
                case ItemDrop.ItemData.ItemType.Fish:
                    return 5;
                default:
                    return 6;
            }
        }

        // ---- the filter ------------------------------------------------------------------

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
        [HarmonyPrefix]
        private static void Filter(List<Recipe> recipes)
        {
            if (s_query.Length == 0 && s_cat == 0)
                return;
            Localization loc = Localization.instance;
            recipes.RemoveAll(r =>
            {
                ItemDrop.ItemData.SharedData d = r?.m_item?.m_itemData?.m_shared;
                if (d == null)
                    return false;
                if (s_cat != 0 && CategoryOf(d.m_itemType) != s_cat)
                    return true;
                return s_query.Length > 0 && loc.Localize(d.m_name).IndexOf(s_query, System.StringComparison.OrdinalIgnoreCase) < 0;
            });
        }

        private static void Refilter() =>
            s_update?.Invoke(InventoryGui.instance, new object[] { false });

        // ---- craft N ---------------------------------------------------------------------

        // UpdateRecipe (label, requirements) and OnCraftPressed (m_multiCrafting for DoCrafting) both
        // read "AltPlace || JoyLStick || m_touchMultiCrafting" (READ). Answering through a postfix on
        // ZInput.GetButton did nothing: it is a one-line static the JIT inlines into the callers, so
        // the craft ran x1 (pilot 2026-10-17: +10 smoke bombs, 1x consumed). The game's own
        // touch long-press flag carries the same meaning and is a plain field, so it is set instead.
        // OnCraftPressed clears it, and this prefix sets it again on the next frame.
        private static readonly FieldInfo s_touchMulti = AccessTools.Field(typeof(InventoryGui), "m_touchMultiCrafting");

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
        [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
        [HarmonyPrefix]
        private static void Multi(InventoryGui __instance)
        {
            if (s_qty > 1)
                s_touchMulti?.SetValue(__instance, true);
        }

        [HarmonyPatch(typeof(InventoryGui), "SetRecipe")]
        [HarmonyPostfix]
        private static void Selected(InventoryGui __instance, int index)
        {
            Recipe recipe = null;
            bool upgrade = false;
            if (index >= 0 && s_list?.GetValue(__instance) is IList list && index < list.Count)
            {
                object pair = list[index];
                recipe = s_pairRecipe?.GetValue(pair, null) as Recipe;
                upgrade = s_pairItem?.GetValue(pair, null) != null;
            }
            if (recipe != s_recipe)
                s_qty = 1;
            s_recipe = recipe;
            s_upgrade = upgrade;

            Player p = Player.m_localPlayer;
            s_max = 1;
            if (recipe != null && !upgrade && p != null && !recipe.m_requireOnlyOneIngredient)
                while (s_max < MaxQty && Fits(p, recipe, s_max + 1))
                    s_max++;
            Set(s_qty);
        }

        private static bool Fits(Player p, Recipe r, int n)
        {
            int amount = r.GetAmount(1, out int need, out ItemDrop.ItemData single, n);
            return p.HaveRequirements(r, false, 1, n) && p.GetInventory().CanAddItem(r.m_item.gameObject, amount);
        }

        private static void Set(int n)
        {
            s_qty = Mathf.Clamp(n, 1, s_max);
            InventoryGui gui = InventoryGui.instance;
            if (gui != null && s_vanilla > 0)
                gui.m_multiCraftAmount = s_qty > 1 ? s_qty : s_vanilla;
            if (gui != null && s_qty <= 1)
                s_touchMulti?.SetValue(gui, false);
            if (s_minus == null)
                return;
            bool show = s_max > 1 && !s_upgrade;
            s_minus.gameObject.SetActive(show);
            s_plus.gameObject.SetActive(show);
            s_minus.interactable = s_qty > 1;
            s_plus.interactable = s_qty < s_max;
        }

        private static void Step(int by)
        {
            // The count is read when the craft starts and again when it finishes; leave it alone between.
            if (s_timer?.GetValue(InventoryGui.instance) is float t && t >= 0f)
                return;
            Set(s_qty + by);
        }

        // ---- typing, and putting everything back when the inventory shuts ------------------

        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPrefix]
        private static void Typing()
        {
            if (s_field != null && s_field.isFocused)
                ZInput.ResetButtonStatus("Use");
        }

        [HarmonyPatch(typeof(InventoryGui), "Hide")]
        [HarmonyPostfix]
        private static void Hidden()
        {
            s_query = "";
            s_cat = 0;
            if (s_field != null)
            {
                s_field.DeactivateInputField();
                s_field.SetTextWithoutNotify("");
            }
            if (s_catText != null)
                s_catText.text = Names[0];
            Set(1);
        }

        // ---- the controls ------------------------------------------------------------------

        /// <summary>
        /// Builds the controls once and seats them on the board's rects (world space, as every other
        /// piece of the board is). Search and category go in the title bar beside the station name; the
        /// two quantity buttons sit on the knotwork at the ends of the Craft button's plate, parented to
        /// its panel so they draw above the button and take the click first.
        /// </summary>
        internal static void Lay(InventoryGui gui, RectTransform craft, Rect search, Rect cat, Rect minus, Rect plus)
        {
            Transform panel = craft.Find("Decription/craft_button_panel");
            if (panel == null || gui.m_tabCraft == null || gui.m_craftingStationName == null)
                return;
            if (s_field == null)
            {
                s_vanilla = gui.m_multiCraftAmount;
                s_catButton = Make(gui, craft, "CarturUI_Category", Names[s_cat], out s_catText, () =>
                {
                    s_cat = (s_cat + 1) % Names.Length;
                    s_catText.text = Names[s_cat];
                    Refilter();
                });
                s_minus = Make(gui, panel, "CarturUI_QtyMinus", "-", out _, () => Step(-1));
                s_plus = Make(gui, panel, "CarturUI_QtyPlus", "+", out _, () => Step(1));
                s_field = Search(gui, craft);
                Log.LogInfo("crafting tools: search, category and craft-N added to the crafting board");
            }
            Seat(s_field.transform, search);
            Seat(s_catButton.transform, cat);
            Seat(s_minus.transform, minus);
            Seat(s_plus.transform, plus);
            Set(s_qty);
        }

        private static void Seat(Transform t, Rect world)
        {
            var rt = (RectTransform)t;
            Vector3 s = rt.parent.lossyScale;
            if (s.x <= 0f || s.y <= 0f)
                return;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(world.width / s.x, world.height / s.y);
            rt.position = new Vector3(world.center.x, world.center.y, rt.position.z);
        }

        private static readonly Color Gold = new Color32(255, 216, 0, 255);

        private static TMP_Text Label(InventoryGui gui, Transform parent, string text, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6f, 2f);
            rt.offsetMax = new Vector2(-6f, -2f);
            var t = go.AddComponent<TextMeshProUGUI>();
            TMP_Text model = gui.m_craftingStationName;
            t.font = model.font;
            t.fontSharedMaterial = model.fontSharedMaterial;
            t.text = text;
            t.alignment = align;
            t.color = Gold;
            t.enableAutoSizing = true;
            t.fontSizeMin = 8f;
            t.fontSizeMax = 30f;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>A button in the Craft tab's art, states and font - the tabs' own look.</summary>
        private static Button Make(InventoryGui gui, Transform parent, string name, string text, out TMP_Text label, UnityAction click)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            var from = gui.m_tabCraft.GetComponent<Image>();
            if (from != null)
            {
                img.sprite = from.sprite;
                img.type = from.type;
                img.color = from.color;
                img.pixelsPerUnitMultiplier = from.pixelsPerUnitMultiplier;
            }
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            b.transition = gui.m_tabCraft.transition;
            b.spriteState = gui.m_tabCraft.spriteState;
            b.colors = gui.m_tabCraft.colors;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(click);
            label = Label(gui, go.transform, text, TextAlignmentOptions.Center);
            return b;
        }

        private static TMP_InputField Search(InventoryGui gui, Transform parent)
        {
            var go = new GameObject("CarturUI_Search", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.SetActive(false);   // configured before it wakes: TMP_InputField reads its text component in OnEnable
            go.GetComponent<Image>().color = new Color(0.06f, 0.05f, 0.04f, 0.9f);
            var vp = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var vrt = (RectTransform)vp.transform;
            vrt.SetParent(go.transform, false);
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
            TMP_Text text = Label(gui, vrt, "", TextAlignmentOptions.Left);
            TMP_Text hint = Label(gui, vrt, "Search...", TextAlignmentOptions.Left);
            hint.color = new Color(Gold.r, Gold.g, Gold.b, 0.45f);
            var field = go.AddComponent<TMP_InputField>();
            field.textViewport = vrt;
            field.textComponent = text;
            field.placeholder = hint;
            field.targetGraphic = go.GetComponent<Image>();
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = 24;
            field.onValueChanged.AddListener(v => { s_query = v.Trim(); Refilter(); });
            go.SetActive(true);
            return field;
        }
    }
}
