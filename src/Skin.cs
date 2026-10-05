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

            /// <summary>
            /// A panel fill, so vanilla's litpanel material has to come off it - see Swap. This
            /// was keyed on Piece == "panel" until the panels moved onto the new art, at which
            /// point every one of them silently went back to drawing at 0.37 brightness.
            /// </summary>
            public bool Lit;
        }

        // vanilla sprite name -> our piece. Names come from the game's own texture files
        // (Assets/UI/textures/small); the one-shot Dump confirms which ones each screen uses.
        private static readonly Dictionary<string, Entry> s_table = new Dictionary<string, Entry>();

        private static readonly HashSet<string> s_unmatched = new HashSet<string>();
        private static float s_ppu = 100f;

        internal static BepInEx.Logging.ManualLogSource Log;

        static Skin()
        {
            // Every panel in the game takes the new art, and which of the two it gets is
            // decided by how big it is on screen rather than by its name - see Resolve. The
            // knots are 40 units across, so a panel with no room for them takes the same rule
            // without them: "the small panels don't get the art work knots on the corners just
            // the thin border."
            // The info strip - the one carrying the achievements and trophies buttons - takes
            // the knots outright. It is 590x150, so the size rule below would call it small and
            // give it the plain rule; Cartur wants the corners on it, and a named sprite beats
            // a threshold for a panel somebody has actually looked at.
            Map("panel_ornate", "woodpanel_info_180").Lit = true;

            Map("panel_auto", "woodpanel_playerinventory", "woodpanel_container",
                "woodpanel_crafting", "woodpanel_crafting_240",
                "woodpanel_settings", "woodpanel_texts", "woodpanel_trophys", "woodpanel_serverlist",
                // The long bottom bar goes back to the plain rule: at 1024x120 the 40 unit knots
                // filled a third of its height and Cartur turned them down after seeing them.
                "woodpanel_characterselect",
                "woodpanel_password", "woodpanel_feedback", "woodpanel_flik", "woodpanel_flik_repair",
                "woodpanel_large", "woodpanel_320x320", "woodpanel_512x512", "woodpanel_400_tileable", "woodpanel_highres",
                "panel_bkg", "panel_bkg_128", "panel_bkg_128_transparent").Lit = true;

            // The loading screen's backdrop is not a panel - it is the whole screen behind the
            // tip and the bar - so it keeps the plain piece and gets no frame around the view.
            Map("panel", "load_bkg").Lit = true;
            Map("well", "panel_interior_bkg_128", "chest_bkg", "crafting_panel_bkg", "skill_bkg", "sunken").Lit = true;
            // Vanilla uses its item-cell sprite for two different jobs: an actual 64 unit cell,
            // and the background of a whole pane - the recipe list is 187x545 of it, the
            // compendium's text area 840x641. Sized like a cell it keeps the cell art; anything
            // panel-sized takes the panel fill instead, or a 128px texture gets pulled down
            // half a screen. Measured on Cartur's own log, not guessed at.
            Map("slot_auto", "item_background");
            Map("slot_selected", "selection_frame").FillCenter = false;
            // "texts_button" is NOT mapped: it is the compendium's own icon, not a button
            // background, and our button art drawn over it left an empty box where the icon was.
            // Buttons and tabs take the new rule and never the knots.
            Map("button_thin", "button", "button_small");
            Map("button_thin_hover", "button_highlight", "button_small_highlight");
            Map("button_thin_pressed", "button_pressed", "button_small_pressed");
            Map("button_thin_disabled", "button_disabled", "button_small_disabled");
            Map("tab_thin", "button_tab", "button_tab_disabled");
            Map("tab_thin_hover", "button_tab_hover");
            Map("tab_thin_selected", "button_tab_selected");
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
            // Same story for the textbox sprite: it backs the skills list, the trophy list and
            // the achievements list as well as actual input boxes.
            Map("field_auto", "InputFieldBackground").Lit = true;
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
            //
            // bar_monster_hp_5 is deliberately NOT mapped, and bar_monster_hp_20 is. Measured off
            // EnemyHud.prefab and bar_fill.png rather than eyeballed: the fill is 1950x97, which
            // tiles at 975 x 48.5 units. The ordinary enemy bar is 100x5 units, so it would show
            // about a tenth of the tile - a sliver of knotwork, no pattern - and because the fill
            // is greyscale (mean 77 of 255) the tint drops with it: vanilla's 255,85,85 red comes
            // out averaging 56,18,18. A health bar read at a glance mid-fight cannot afford that.
            // The boss bar is 600x15 and shows about a third of the tile, which is Cartur's call
            // to keep. It darkens the same way - 255,0,100 becomes about 68,0,26 - so if the boss
            // bar should stay bright, the fix is a brighter fill for it, not a change here.
            Entry fill = Map("bar_fill", "bar_gradient", "bar_gradient_16", "bar_gradient_40", "bar_food_8", "bar_stagger");
            fill.Type = Image.Type.Tiled;
            fill.White = false;

            // Enemy bars get the lifted fill instead, drawn Simple. Both sprite names are here -
            // bar_monster_hp_5 is the hostile, tamed, player and mount bars, bar_monster_hp_20 is
            // the boss one - read out of EnemyHud.prefab, not guessed at from the names.
            Entry enemy = Map("bar_fill_enemy", "bar_monster_hp_5", "bar_monster_hp_20");
            enemy.Type = Image.Type.Simple;
            enemy.White = false;
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

            // The inventory screen gets named, one line per Image. Cartur reported the panes
            // inside the crafting window as low resolution and measuring his screenshot proved
            // they are NOT drawing the fill the panels draw - grain 1.1 against the 4.7 that
            // fill measures at that size - so the question is which sprite is on them, and
            // that is not answerable from the assembly. It is one screen, once, at Awake.
            // Off unless the one-shot UI dump is armed. When something on screen cannot be
            // explained - which Image is that, what rect is it, what did it used to wear -
            // ticking dumpOnce lists every image this mod touches, with its rect and its
            // parent's, and then switches itself off again. That listing is what found the
            // tooltip's clipped rail and the panes wearing a cell's art.
            //
            // Guarded because the csproj Compile-Removes Dump.cs from every non-Debug build, so
            // naming the type at all is a compile error in Release. s_naming defaults false,
            // which is the Release answer anyway - there is no dumpOnce switch to read.
#if DIAGNOSTICS
            s_naming = Dump.Enabled != null && Dump.Enabled.Value;
#endif

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

            foreach (Slider slider in root.GetComponentsInChildren<Slider>(true))
                SliderSkin.Dress(slider);

            Log.LogInfo(label + ": " + images + " images, " + states + " button states skinned");
        }

        private static string BaseName(string name) =>
            name.EndsWith("(Clone)") ? name.Substring(0, name.Length - 7).TrimEnd() : name;

        private static bool Swap(Image img)
        {
            // A copied sprite keeps the vanilla name plus "(Clone)" - Jotunn's GUIManager hands out
            // copies ("woodpanel_trophys(Clone)", pilot dump 2026-10-05), so its windows matched nothing.
            if (img.sprite == null || !s_table.TryGetValue(BaseName(img.sprite.name), out Entry e))
            {
                if (img.sprite != null && !img.sprite.name.StartsWith("cartur_") && s_unmatched.Add(img.sprite.name))
                    Log.LogInfo("no skin for sprite '" + img.sprite.name + "' (" + img.name + ")");

                // Named too, and named every time rather than once per sprite: a pane left in
                // vanilla art is exactly what this listing is being taken to find, and the
                // once-per-name filter above would hide the second one.
            if (s_naming)
                    Log.LogInfo("  bare " + Path(img.transform) + "  '"
                        + (img.sprite != null ? img.sprite.name : "<none>") + "'  rect "
                        + img.rectTransform.rect.size);
                return false;
            }

            // A slot's equipped / queued state is the same item_background sprite drawn over
            // the bkg with a tint, which our slot art would swallow. Those two get the hollow
            // selected rail in a colour of their own instead, so the state stays readable.
            if ((e.Piece == "slot" || e.Piece == "slot_auto")
                && (img.name == "equiped" || img.name == "queued"))
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

            Sprite sprite = Piece(e, img);
            if (sprite == null)
                return false;

            // A cell's Button paints its own normal colour onto this Image every time it
            // refreshes, and vanilla's is #22222280 - 13% at half alpha. That multiplies our
            // slot art into the panel behind it: measured on Cartur's screenshot, an empty
            // cell read 13.7 and the gap between cells read 13.7, the same number, which is
            // what "the slots have no background" looks like from the outside.
            //
            // The tint stays, because it is what lights a cell under the pointer; it is just
            // re-pitched so the art is what you see. Normal sits a little under white so the
            // highlight has somewhere to go - a ColorTint multiplies, and nothing is brighter
            // than white.
            if ((e.Piece == "slot" || e.Piece == "slot_auto") && sprite.name == "cartur_slot")
                Lit(img.GetComponent<Selectable>());

            // A cell's Button paints its own normal colour onto this Image every time it
            // refreshes, and vanilla's is #22222280 - 13% at half alpha. That multiplies our
            // slot art into the panel behind it: measured on Cartur's screenshot, an empty
            // cell read 13.7 and the gap between cells read 13.7, the same number, which is
            // what "the slots have no background" looks like from the outside.
            //
            // The tint stays, because it is what lights a cell under the pointer; it is just
            // re-pitched so the art is what you see. Normal sits a little under white so the
            // highlight has somewhere to go - a ColorTint multiplies, and nothing is brighter
            // than white.
            if (sprite.name == "cartur_slot")
                Lit(img.GetComponent<Selectable>());

            if (s_naming)
            {
                // The parent's rect as well as its own: a missing rail on one side is either an
                // Image bigger than what shows it, or one inset from it, and the two numbers
                // side by side say which without another launch.
                var parent = img.rectTransform.parent as RectTransform;
                Log.LogInfo("  skin " + Path(img.transform) + "  '" + img.sprite.name + "' -> "
                    + sprite.name + "  rect " + img.rectTransform.rect.size
                    + " at " + img.rectTransform.anchoredPosition
                    + "  in " + (parent != null ? parent.name + " " + parent.rect.size.ToString() : "no parent"));
            }

            img.sprite = sprite;
            img.type = e.Type;
            // Outline only for a cell - Cartur's call: a cell is a border round what is behind
            // it, the same as the boxes painted on the equipment panel. Decided on the resolved
            // piece, not on the entry: item_background also backs whole panes, and those
            // resolve to the well and still want their fill.
            img.fillCenter = e.FillCenter && sprite.name != "cartur_slot";
            // The sprite is authored at 2 px per unit and created at 2 x ppu, so the multiplier
            // stays 1: a 128 px slot draws as the 64 units vanilla's element is.
            img.pixelsPerUnitMultiplier = 1f;
            // Vanilla's grey tints on panels and slots go to white so the bronze shows. A
            // coloured tint is a state - the equipped and queued overlays on a slot are the
            // same item_background sprite in a colour - and that stays.
            // THE reason our panels looked black. Read off the prefab and the bundle: every
            // woodpanel Image carries vanilla's `litpanel` material, shader Custom/LitGui, with
            // _Brightness 0.37 and _Saturation -0.3 - and Swap replaces the sprite but never the
            // material, so our art drew at 37% through their shader. The sibling material
            // `lithud`, used by buttons and bars, is _Brightness 1.0, which is why those looked
            // right and panels did not. It also explains the knots: those were new GameObjects
            // with the DEFAULT material, and measured 49,37,27 against a panel interior at a
            // third of its file value, same object, same art.
            //
            // Light mode drops the material, so the piece draws through the canvas default at
            // its real value. Dark mode leaves vanilla's, which is the look Cartur has now.
            // Equipment and Quick Slots clones Player/Bkg to build its own panels and its
            // per-frame SyncBackground copies sprite and colour but NOT material, so whatever is
            // set here is what its clones are born with.
            if (e.Lit)
            {
                if (img.material != null && img.material.shader != null
                    && img.material.shader.name == "Custom/LitGui")
                {
                    s_litPanel = img.material;
                    if (!s_loggedPanelMaterial)
                    {
                        s_loggedPanelMaterial = true;
                        Log.LogInfo($"panel '{img.name}' material {img.material.name}/{img.material.shader.name}"
                            + $" brightness {img.material.GetFloat("_Brightness"):0.##}"
                            + $" saturation {img.material.GetFloat("_Saturation"):0.##}"
                            + $" colour {img.color}");
                    }
                }
                img.material = DarkMode ? s_litPanel : null;
            }

            Color c = img.color;
            if (e.White && Mathf.Approximately(c.r, c.g) && Mathf.Approximately(c.g, c.b))
                img.color = new Color(1f, 1f, 1f, c.a);

            // The interior field is the one place a coloured tint is not a state - there is no
            // equipped or queued well - so it is whitened whatever colour vanilla gives it.
            //
            // Kept, but not for the reason first written. The dark inventory field was never
            // well.png - it is Player/Bkg, our panel piece drawn through vanilla's litpanel
            // material, and the `sunken` objects this piece lands on are inactive in the prefab.
            // Whitening a coloured tint here is still right on its own terms: the well is the one
            // piece with no coloured state to preserve.
            if (e.Piece == "well")
                img.color = new Color(1f, 1f, 1f, c.a);

            if (e.Piece == "panel" && AssetLoader.WoodPanel)
            {
                // Measured on screen: the wood came out 5.6,4.5,7.2 against the art's
                // 40.5,29.6,21.6 - seven times down, and blue-biased where the art is warm. The
                // rule above only whitens a tint when it is grey, so a tint that is even slightly
                // off-grey survives and multiplies the panel into the dark. This piece is a
                // full-colour painting; nothing vanilla tints it with is wanted.
                if (s_panelTint == null)
                {
                    s_panelTint = c;
                    Log.LogInfo($"panel tint was {c.r:0.###},{c.g:0.###},{c.b:0.###} alpha {c.a:0.###} on '{img.name}'");
                }
                img.color = Color.white;

                Knots(img);
            }
            // Window frames go on Cartur's boards where nothing else has fitted one. Last, so the
            // colour it keeps for a frame that is not boarded is the finished one.
            if (e.Piece == "panel_auto" || e.Piece == "panel_ornate")
                AutoBoard.Attach(img);
            return true;
        }

        private static Color? s_panelTint;

        private const string KnotTop = "CarturUIHud_KnotTop";
        private const string KnotBottom = "CarturUIHud_KnotBottom";

        /// <summary>
        /// The knot in the middle of the wood panel's top and bottom rails.
        ///
        /// Cartur's cut, and it is the right one: the knot is taken out whole, cut at the middle
        /// of the plain bar on each side of it, so it arrives with 285px of its own rail either
        /// side and blends into the panel's rail instead of sitting on it as a block. The panel
        /// behind is 9-sliced with its edge strips taken from a plain run, so the bars are what
        /// stretch and the knot never does.
        ///
        /// It cannot live inside the panel sprite: a 9-slice stretches one slice of an edge along
        /// the whole run, so a knot in there smears. Pinned to the centre of the edge instead, it
        /// stays one knot at one size on a panel of any width - and the inventory's width is
        /// fixed while its height grows with rows, so both cases are covered.
        /// </summary>
        private static void Knots(Image panel)
        {
            Sprite top = AssetLoader.Knot(s_ppu);
            if (top == null)
                return;
            // A missing bottom cut falls back to the top one rather than leaving that rail bare.
            Sprite bottom = AssetLoader.Knot(s_ppu, true) ?? top;

            // The knot is one fixed cut and cannot stretch - stretching is exactly what a 9-slice
            // does to anything in the middle of an edge, which is why it is a separate piece at
            // all. So it only fits a panel at least as wide as itself. Measured: the cut draws
            // 330.5 units, and the small equipment and hotkey boxes are 80x64, so it overhung them
            // four times over and read as a bar floating off the side of the panel. A panel that
            // cannot hold the knot keeps its plain rail, which is what the art's edge already is.
            var prt = (RectTransform)panel.transform;
            float needed = top.rect.width / AssetLoader.PieceAuthoredPxPerUnit;
            if (prt.rect.width < needed)
            {
                Strip(panel, KnotTop);
                Strip(panel, KnotBottom);
                return;
            }

            Place(panel, KnotTop, top, new Vector2(0.5f, 1f));
            Place(panel, KnotBottom, bottom, new Vector2(0.5f, 0f));

            // Cartur also reported the knot scaled wrong against the rail. The knot is drawn
            // Type.Simple, which ignores the sprite's ppu and takes the rect straight from
            // sizeDelta; the panel is Type.Sliced, whose border is sprite.border scaled by
            // canvas ppu / sprite ppu. Those two only agree while sprite ppu is exactly twice the
            // canvas ppu. Every number in that sentence is below - one line, once, so one launch
            // says which side is wrong instead of another guess.
            if (!s_loggedScale)
            {
                s_loggedScale = true;
                // The wood field also draws dark: measured 13,10,5 on screen against 38,28,21 in
                // panel_wood.png. The panel itself is forced to opaque white, so an alpha above it
                // is the only thing left that can do that. Walk the chain rather than launch twice.
                string groups = "";
                for (Transform t = panel.transform; t != null; t = t.parent)
                {
                    var cg = t.GetComponent<CanvasGroup>();
                    if (cg != null)
                        groups += $" {t.name}={cg.alpha:0.###}";
                }
                Sprite ps = panel.sprite;
                Canvas cv = panel.canvas;
                Log.LogInfo(
                    $"knot scale: s_ppu {s_ppu}, canvas ppu {(cv != null ? cv.referencePixelsPerUnit : -1f)}, " +
                    $"scaleFactor {(cv != null ? cv.scaleFactor : -1f)}; " +
                    $"panel sprite {ps?.rect.size} border {ps?.border} ppu {ps?.pixelsPerUnit}, " +
                    $"type {panel.type} multiplier {panel.pixelsPerUnitMultiplier}, " +
                    $"panel rect {prt.rect.size} lossyScale {prt.lossyScale}; " +
                    $"knot sprite {top.rect.size} ppu {top.pixelsPerUnit} drawn {top.rect.size / AssetLoader.PieceAuthoredPxPerUnit}; " +
                    $"canvasGroup alpha{(groups.Length == 0 ? " none" : groups)}");
            }
        }

        private static bool s_loggedScale;
        private static bool s_loggedPanelMaterial;

        /// <summary>Vanilla's litpanel, kept so dark mode can put it back after light mode drops it.</summary>
        private static Material s_litPanel;

        /// <summary>On: panels keep vanilla's dim material. Off: they draw at their real value.</summary>
        public static bool DarkMode { get; set; }

        /// <summary>Takes a knot off a panel that has become too narrow to carry it.</summary>
        private static void Strip(Image panel, string name)
        {
            Transform existing = panel.transform.Find(name);
            if (existing != null)
                Object.Destroy(existing.gameObject);
        }

        private static void Place(Image panel, string name, Sprite knot, Vector2 anchor)
        {
            Transform existing = panel.transform.Find(name);
            Image img = existing != null ? existing.GetComponent<Image>() : null;
            if (img == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(panel.transform, false);
                img = go.GetComponent<Image>();
                img.raycastTarget = false;
            }

            img.sprite = knot;
            img.type = Image.Type.Simple;
            img.color = Color.white;

            // Each rail has its own cut now, so neither copy is mirrored: the sprite is laid on
            // the edge the right way up and pivots on that edge. localScale is written back to one
            // because a panel skinned by an earlier build still carries the -1 that used to flip
            // the bottom copy.
            var rt = (RectTransform)img.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, anchor.y);
            rt.sizeDelta = new Vector2(knot.rect.width, knot.rect.height) / AssetLoader.PieceAuthoredPxPerUnit;
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.SetAsFirstSibling();
        }

        /// <summary>
        /// The sprite for an entry, and for a panel the choice between the two panel pieces.
        ///
        /// The knots are 40 units across, so a panel has to be big enough to carry four of them
        /// and still be mostly panel. Below that it takes the same double rule with the knots
        /// left off. The number is read off the Image, not off its sprite's name: vanilla's
        /// names lie about size - woodpanel_512x512 is drawn at whatever rect it is given.
        ///
        /// A rect that has not been laid out yet measures zero. That is not "small", it is "not
        /// known", so it takes the ornate piece - the big panels are the ones that stretch, and
        /// those are the ones that measure zero at Awake.
        /// </summary>
        private static Sprite Piece(Entry e, Image img)
        {
            string piece = e.Piece;
            if (piece == "panel_auto")
            {
                Vector2 size = img.rectTransform.rect.size;
                if (size.x <= 0f || size.y <= 0f)
                    size = img.rectTransform.sizeDelta;
                float least = Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.y));
                piece = least > 0f && least < KnotRoom ? "panel_thin" : "panel_ornate";

                if (s_decided.Add(img.sprite.name))
                    Log.LogInfo("panel '" + img.sprite.name + "' on " + img.name + " is "
                        + size.x.ToString("0") + "x" + size.y.ToString("0") + " -> " + piece);
            }
            else if (piece == "slot_auto" || piece == "field_auto")
            {
                // A cell is square and small; a pane is long in at least one direction. So the
                // longer side decides, not the shorter one - the recipe list is only 187 wide
                // but 545 deep, and it is a pane.
                Vector2 size = img.rectTransform.rect.size;
                if (size.x <= 0f || size.y <= 0f)
                    size = img.rectTransform.sizeDelta;
                float most = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y));
                bool pane = most > PaneAbove;
                piece = pane ? "well" : (piece == "slot_auto" ? "slot" : "field");
            }
            return AssetLoader.Piece(piece, s_ppu);
        }

        /// <summary>Four knots across is the least that reads as a framed panel.</summary>
        private const float KnotRoom = 200f;

        /// <summary>
        /// Longer than this and a cell or a textbox is really a pane. A cell is 64 units and
        /// the biggest thing that is still one - an achievement tile - is 180; the smallest
        /// thing that is really a pane is the crafting requirements strip at 334.
        /// </summary>
        private const float PaneAbove = 200f;

        private static readonly HashSet<string> s_decided = new HashSet<string>();

        // Written only under #if DIAGNOSTICS (see Apply), so the explicit false is what keeps a
        // Release build from warning that nothing ever assigns it. False is the Release answer.
        private static bool s_naming = false;

        private static string Path(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p.name != "Inventory_screen"; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }

        /// <summary>
        /// The same lookup without an Image to measure. This path is a Selectable's hover and
        /// pressed art, which is only ever a button or a tab, so a panel never reaches it - the
        /// fallback is there so the method cannot silently return nothing if one ever does.
        /// </summary>
        private static Sprite Piece(Entry e) =>
            AssetLoader.Piece(e.Piece == "panel_auto" ? "panel_ornate" : e.Piece, s_ppu);

        private static void Lit(Selectable sel)
        {
            if (sel == null || sel.transition != Selectable.Transition.ColorTint)
                return;
            ColorBlock c = sel.colors;
            if (c.normalColor == s_cellNormal)
                return;
            c.normalColor = s_cellNormal;
            c.highlightedColor = Color.white;
            c.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            c.selectedColor = s_cellNormal;
            c.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            sel.colors = c;
        }

        private static readonly Color s_cellNormal = new Color(0.86f, 0.86f, 0.86f, 1f);

        private static Sprite Lookup(Sprite vanilla, ref bool any)
        {
            if (vanilla == null || !s_table.TryGetValue(BaseName(vanilla.name), out Entry e))
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
