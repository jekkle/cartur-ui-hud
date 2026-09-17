using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// The reskin: every vanilla panel, slot, button and field is a plain PNG on an Image, so
    /// matching the sprite's name and swapping it for one of ours is the whole job. Apply walks
    /// a hierarchy once. It is run on each screen's root at Awake and on the element PREFABS
    /// the game instantiates from (InventoryElement, HotKeyElement, RecipeElement ...): those
    /// are ordinary in-memory GameObjects and Instantiate copies component state, so skinning
    /// the prefab once skins every element the game makes afterwards, at no per-frame cost.
    ///
    /// Our sprites carry our names, so a second walk over the same objects is a no-op, and a
    /// vanilla sprite renamed by a game update is left as it was and logged once - never a crash.
    /// </summary>
    internal static class Skin
    {
        private class Entry
        {
            public string Piece;
            public Image.Type Type = Image.Type.Sliced;
            public bool FillCenter = true;
            public bool White = true;   // vanilla tints most panels grey; our art carries its own tone
        }

        // vanilla sprite name -> our piece. Names come from the game's own texture files
        // (Assets/UI/textures/small); the one-shot Dump confirms which ones each screen uses.
        private static readonly Dictionary<string, Entry> s_table = new Dictionary<string, Entry>();

        private static readonly HashSet<string> s_unmatched = new HashSet<string>();
        private static float s_ppu = 100f;

        internal static BepInEx.Logging.ManualLogSource Log;

        static Skin()
        {
            Map("panel", "woodpanel_playerinventory", "woodpanel_container", "woodpanel_crafting", "woodpanel_crafting_240",
                "woodpanel_info_180", "woodpanel_settings", "woodpanel_texts", "woodpanel_trophys", "woodpanel_serverlist",
                "woodpanel_password", "woodpanel_feedback", "woodpanel_characterselect", "woodpanel_flik", "woodpanel_flik_repair",
                "woodpanel_large", "woodpanel_320x320", "woodpanel_512x512", "woodpanel_400_tileable", "woodpanel_highres",
                "panel_bkg", "panel_bkg_128", "panel_bkg_128_transparent", "load_bkg");
            Map("well", "panel_interior_bkg_128", "chest_bkg", "crafting_panel_bkg", "skill_bkg", "sunken");
            Map("slot", "item_background");
            Map("slot_selected", "selection_frame").FillCenter = false;
            Map("button", "button", "button_small", "texts_button");
            Map("button_hover", "button_highlight", "button_small_highlight");
            Map("button_pressed", "button_pressed", "button_small_pressed");
            Map("button_disabled", "button_disabled", "button_small_disabled");
            Map("tab", "button_tab");
            Map("tab_hover", "button_tab_hover");
            Map("tab_selected", "button_tab_selected");
            Map("tab", "button_tab_disabled");
            Map("field", "text_field");
            Map("field_hover", "text_field_highlight");
            Map("field_disabled", "text_field_disabled");
            Map("tooltip", "Tooltip_bkg");
            // Gamepad focus: vanilla tints a copy of each panel's shape (the *_mask sprites). A
            // hollow rail in the same place, keeping vanilla's tint colour, reads the same way.
            Entry focus = Map("panel_selected", "woodpanel_playerinventory_mask", "woodpanel_container_mask",
                "woodpanel_crafting_240_mask", "woodpanel_info_180_mask", "woodpanel_flik_mask", "woodpanel_flik_repair_mask");
            focus.FillCenter = false;
            focus.White = false;
            Map("field", "InputFieldBackground");
            // Unity's built-in sprites, used with a colour: scrollbar tracks and the item
            // tooltip (Background), scrollbar handles and the upgrade strip (UISprite), the
            // round icon buttons on the info panel (point3). The vanilla tint is kept, so a
            // black-tinted one stays a dark box - the shape is ours, the colour is theirs.
            Map("well", "Background").White = false;
            Map("button", "UISprite").White = false;
            // point3 is deliberately NOT mapped. It is the soft dark disc behind the round icon
            // buttons on the info panel - compendium, skills, trophies, achievements, pvp - drawn
            // black at 53%. Our square slot frame in that tint covered the icons up.
            // Every small bar in the menus - craft progress, durability, food - takes the HUD
            // bar's own knotwork fill, tinted by whatever colour the game gives it.
            Entry fill = Map("bar_fill", "bar_gradient", "bar_gradient_16", "bar_gradient_40", "bar_food_8", "bar_stagger",
                "bar_monster_hp_5", "bar_monster_hp_20");
            fill.Type = Image.Type.Tiled;
            fill.White = false;
        }

        private static Entry Map(string piece, params string[] vanilla)
        {
            var e = new Entry { Piece = piece };
            foreach (string name in vanilla)
                s_table[name] = e;
            return e;
        }

        /// <summary>Skins a live hierarchy; the sprite scale comes from its own canvas.</summary>
        public static void Apply(Transform root, string label)
        {
            if (root == null)
                return;
            Canvas canvas = root.GetComponentInParent<Canvas>();
            Apply(root, label, canvas != null ? canvas.referencePixelsPerUnit : 100f);
        }

        /// <summary>Skins a prefab, which has no canvas of its own: pass the ppu of the canvas it will live in.</summary>
        public static void Apply(Transform root, string label, float referencePixelsPerUnit)
        {
            if (root == null)
                return;
            s_ppu = referencePixelsPerUnit;

            int images = 0, states = 0;
            foreach (Image img in root.GetComponentsInChildren<Image>(true))
            {
                if (Swap(img))
                    images++;
            }

            // Button / Toggle / Scrollbar hover and pressed art lives in the Selectable's sprite
            // state, not on the Image, so it goes through the same table separately.
            foreach (Selectable sel in root.GetComponentsInChildren<Selectable>(true))
            {
                if (sel.transition != Selectable.Transition.SpriteSwap)
                    continue;
                SpriteState st = sel.spriteState;
                bool any = false;
                st.highlightedSprite = Lookup(st.highlightedSprite, ref any);
                st.pressedSprite = Lookup(st.pressedSprite, ref any);
                st.selectedSprite = Lookup(st.selectedSprite, ref any);
                st.disabledSprite = Lookup(st.disabledSprite, ref any);
                if (any)
                {
                    sel.spriteState = st;
                    states++;
                }
            }

            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                Recolour(text);

            Log.LogInfo(label + ": " + images + " images, " + states + " button states skinned");
        }

        private static bool Swap(Image img)
        {
            if (img.sprite == null || !s_table.TryGetValue(img.sprite.name, out Entry e))
            {
                if (img.sprite != null && !img.sprite.name.StartsWith("cartur_") && s_unmatched.Add(img.sprite.name))
                    Log.LogInfo("no skin for sprite '" + img.sprite.name + "' (" + img.name + ")");
                return false;
            }

            // A slot's equipped / queued state is the same item_background sprite drawn over
            // the bkg with a tint, which our slot art would swallow. Those two get the hollow
            // selected rail in a colour of their own instead, so the state stays readable.
            if (e.Piece == "slot" && (img.name == "equiped" || img.name == "queued"))
            {
                Sprite state = AssetLoader.Piece("slot_selected", s_ppu);
                if (state == null)
                    return false;
                img.sprite = state;
                img.type = Image.Type.Sliced;
                img.fillCenter = false;
                img.pixelsPerUnitMultiplier = 1f;
                img.color = img.name == "equiped" ? new Color(1f, 0.85f, 0.45f, 1f) : new Color(0.7f, 0.7f, 0.7f, 0.8f);
                return true;
            }

            Sprite sprite = Piece(e);
            if (sprite == null)
                return false;

            img.sprite = sprite;
            img.type = e.Type;
            img.fillCenter = e.FillCenter;
            // The sprite is authored at 2 px per unit and created at 2 x ppu, so the multiplier
            // stays 1: a 128 px slot draws as the 64 units vanilla's element is.
            img.pixelsPerUnitMultiplier = 1f;
            // Vanilla's grey tints on panels and slots go to white so the bronze shows. A
            // coloured tint is a state - the equipped and queued overlays on a slot are the
            // same item_background sprite in a colour - and that stays.
            Color c = img.color;
            if (e.White && Mathf.Approximately(c.r, c.g) && Mathf.Approximately(c.g, c.b))
                img.color = new Color(1f, 1f, 1f, c.a);
            return true;
        }

        private static Sprite Piece(Entry e)
        {
            if (e.Piece != "bar_fill")
                return AssetLoader.Piece(e.Piece, s_ppu);

            // The bar fill is the HUD's own sprite, and it is built from Hud.Awake. Unity does
            // not order Awake between components, so InventoryGui can get here first and find it
            // null - which silently left every craft, durability and food bar vanilla, and only
            // some launches. Build it here rather than depend on who woke first.
            if (AssetLoader.BarFill == null)
                AssetLoader.BuildSprites(s_ppu, Log);
            return AssetLoader.BarFill;
        }

        private static Sprite Lookup(Sprite vanilla, ref bool any)
        {
            if (vanilla == null || !s_table.TryGetValue(vanilla.name, out Entry e))
                return vanilla;
            Sprite ours = Piece(e);
            if (ours == null)
                return vanilla;
            any = true;
            return ours;
        }

        // Vanilla's orange headings (FFA000) against bronze read as a warning light. Only that
        // exact colour is touched: everything the game writes per frame (requirement red, food
        // colours, durability pulse) is its own and stays.
        private static readonly Color32 VanillaOrange = new Color32(255, 160, 0, 255);
        private static readonly Color32 Gold = new Color32(228, 195, 106, 255);

        private static void Recolour(TMP_Text text)
        {
            Color32 c = text.color;
            if (c.r == VanillaOrange.r && c.g == VanillaOrange.g && c.b == VanillaOrange.b)
                text.color = new Color32(Gold.r, Gold.g, Gold.b, c.a);
        }
    }
}
