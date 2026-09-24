using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Part 1 of Cartur's UI: the HUD.
    ///
    /// The bars are taken over rather than skinned. Vanilla's UpdateHealth / UpdateStamina /
    /// UpdateEitr / UpdateAdrenaline are skipped and BarSkin drives them instead - the same
    /// call AugaLite makes. Skinning in place meant every value had to be smuggled through a
    /// prefix on SetHealthBarSize, GuiBar owned the fill width, the panel Animator kept writing
    /// a reparented child, and UpdateStamina rewrote anchoredPosition every frame. Owning the
    /// four methods removes that whole class of problem, and GuiBar is still used for the fill
    /// so the fast/slow drain is kept.
    ///
    /// The food boxes and the guardian power box are left to vanilla, because there the vanilla
    /// behaviour is exactly what is wanted: icon plus countdown, and a box that hides itself
    /// when no power is selected. Those really are only a sprite swap.
    /// </summary>
    internal static class HudSkin
    {
        // Defaults follow the concept: diamonds in a stagger to the left, bars stacked to their
        // right, longest on top. Edit mode overrides all of it from the config file.
        // Health and stamina are where Cartur locked them in. Eitr and adrenaline sit below,
        // still being placed - they only appear once the player has a pool at all.
        private static readonly Vector2 HealthHome = new Vector2(307.5f, 309.25f);
        private static readonly Vector2 StaminaHome = new Vector2(330.75f, 272.75f);
        // Eitr and adrenaline get a row each, 36.5 below stamina and 36.5 below that. They
        // shared one slot until 1.0.3, on the reasoning that a melee character has adrenaline
        // and a mage has eitr, so only one would ever be up. That was wrong: max adrenaline
        // comes from the trinket slot (ItemData.SharedData.m_maxAdrenaline, which every one of
        // the fifteen vanilla trinkets sets, bronze upwards), not from a melee build. Any
        // player wearing any trinket had a non-zero adrenaline pool permanently, and the shared
        // slot gave it to adrenaline - so the eitr bar never drew for them, mana or not.
        // Same x as stamina, so all three thin bars share a left edge and the same geometry -
        // which is why they share a length figure too.
        private static readonly Vector2 EitrHome = new Vector2(330.75f, 236.25f);
        private static readonly Vector2 AdrenalineHome = new Vector2(330.75f, 199.75f);

        // Where 1.0.2 parked both of them. A config still holding this for either bar was
        // written by the shared-slot build, not placed by hand, so it is moved to the new row.
        private static readonly Vector2 SharedRowLegacy = new Vector2(330.75f, 219.75f);

        // Health's frame reads better a shade tighter than the others.
        private const float HealthFrame = 0.85f;

        // Each bar starts at a different x and carries a different ornament width, so a single
        // shared length left their tips 33 units apart. These are solved from
        //   window = 896.76 - x - rightOrnament
        // so that at its own max every bar ends on the same vertical line. 896.76 is the health
        // bar's own tip, which is why its own figure is 1 - it is the bar the length was
        // derived from and the one that stays put.
        private const float HealthLength = 1f, StaminaLength = 0.9728f, RowThreeLength = 0.9728f;
        private const float FoodSize = 82f, PowerSize = 104f;

        // Cartur settled these and confirmed them, so the three diamonds and the power box are
        // one element from here: their offsets and sizes within the cluster are fixed and edit
        // mode moves and scales the group as a whole. Offsets are from the cluster's bottom-left
        // corner, which is the bounding box of the four pieces at these sizes. The bars stay
        // separate, because those are still being tuned.
        private static readonly Vector2 ClusterHome = new Vector2(90.95f, 143.6f);
        private const float ClusterScale = 0.9f;
        private static readonly Vector2 ClusterSize = new Vector2(230.2f, 210.35f);
        private static readonly Vector2 Food0 = new Vector2(127.8f, 161.15f);
        private static readonly Vector2 Food1 = new Vector2(183.05f, 104.15f);
        private static readonly Vector2 Food2 = new Vector2(127.8f, 47.15f);
        private static readonly Vector2 Power = new Vector2(59.8f, 103.9f);
        private static readonly float[] FoodScales = { 1.2f, 1.15f, 1.15f };
        private const float PowerScale = 1.15f;

        // Health is drawn half again as tall as the other three, which all match.
        // Health stays as Cartur set it. The other three all match the stamina bar.
        private const float HealthHeight = 0.9f;
        private const float StatHeight = 0.55f;

        // What counts as a full bar for each stat, from Cartur's own character. Every bar is
        // drawn MaxWindowUnits long when it sits at its own figure here, so they all reach the
        // same length at their own ceiling however different those ceilings are.
        private const float HealthFull = 325f, StaminaFull = 285f, EitrFull = 315f, AdrenalineFull = 100f;

        // Taken from the health bar Cartur settled on at full health: 377.76 units of natural
        // window at height 0.9 and length 1.65 comes to 560.9 at 325 health. Bar length is
        // linear in max stat, so this holds wherever his max happened to be when measured.
        internal const float MaxWindowUnits = 560.9f;

        // Item icons are square, the frame is a diamond. 0.55 of the frame keeps the corners
        // off the bevel without making the icon unreadable.
        private const float IconFraction = 0.55f;

        // Food timers: the last minute blinks red, and a quick slot
        // holding food nobody has eaten yet says so instead of showing a blank box.
        //
        // A flat minute, every food. Cartur's call, and the reason is that the blink is a
        // signal rather than a gauge: it always means the same thing, so you learn it once and
        // never work out what a tenth of this particular stew is worth.
        private const float LowFoodSeconds = 60f;
        private const float BlinkSpeed = 2.5f;
        private static readonly Color LowFoodDim = new Color(0.65f, 0.10f, 0.10f, 0.75f);
        private static readonly Color LowFoodBright = new Color(1f, 0.25f, 0.20f, 1f);
        // Outline thickness on the key letter, in SDF units. Wide enough to separate 14pt
        // glyphs from a busy icon, short of the width that fills in the counters of a B.
        private const float KeyOutline = 0.15f;
        /// <summary>The key letter's own line height.</summary>
        private const float KeyHeight = 20f;

        /// <summary>
        /// How far it rides up from where vanilla's countdown sits. A full line height put it
        /// off the wood; this keeps it on the frame with the icon clear underneath.
        /// </summary>
        private const float KeyRise = 14f;
        private const string EatLabel = "Eat";
        private static readonly Color EatColour = new Color(1f, 0.85f, 0.45f, 0.9f);

        // health, stamina, eitr, adrenaline
        internal static readonly BarSkin[] Bars = { new BarSkin(), new BarSkin(), new BarSkin(), new BarSkin() };

        private static Image[] s_foodFrames;
        private static readonly RectTransform[] s_foodBoxes = new RectTransform[3];

        /// <summary>
        /// The diamond a quick slot lives in. Since Part 12 the diamonds are not a picture of
        /// the quick slots, they are the quick slots: QuickSlots.Host lays the real inventory
        /// cell over one of these as an invisible hit area, so the diamond can be dropped onto,
        /// dragged out of, right-clicked and hovered for a tooltip.
        ///
        /// The drawing is untouched. Everything on the diamond - the frame, the item icon, the
        /// key letter, the countdown - is still Part 1's, exactly as it was.
        /// </summary>
        internal static RectTransform QuickBox(int index) =>
            index >= 0 && index < s_foodBoxes.Length ? s_foodBoxes[index] : null;

        /// <summary>
        /// Lights the diamond a controller is sitting on. The cell's own selection marker is a
        /// square, which is wrong over a diamond, so the frame Cartur drew is the highlight
        /// instead - same sprite, same place, same size, just brighter. Nothing moves.
        /// </summary>
        internal static void SetQuickSelection(int index)
        {
            if (s_foodFrames == null)
                return;
            for (int i = 0; i < s_foodFrames.Length; i++)
            {
                Image frame = s_foodFrames[i];
                if (frame == null)
                    continue;
                Color want = i == index ? QuickSelected : Color.white;
                if (frame.color != want)
                    frame.color = want;
            }
        }

        // Vanilla's own armour-number gold, lifted towards white so it reads as "lit" rather
        // than "tinted yellow".
        private static readonly Color QuickSelected = new Color(1f, 0.95f, 0.72f, 1f);
        private static TMP_Text[] s_foodTimes = new TMP_Text[3];
        private static readonly ItemDrop.ItemData[] s_slotItems = new ItemDrop.ItemData[3];
        // The diamonds have shown quick slot contents since Part 1. They used to be fed by
        // Equipment and Quick Slots through reflection; the slots are this mod's own now
        // (Part 12), so the reflection and the "is that mod installed" branch are both gone.
        private static bool s_started;
        private static readonly TMP_Text[] s_foodKeys = new TMP_Text[3];

        internal static BepInEx.Logging.ManualLogSource Log;

        [HarmonyPatch(typeof(Hud), "Awake")]
        [HarmonyPostfix]
        private static void Skin(Hud __instance)
        {
            Transform hudroot = __instance.transform.Find("hudroot");
            if (hudroot == null)
            {
                Log.LogWarning("no hudroot - nothing skinned");
                return;
            }

            // Sprite scale depends on the canvas, so the sprites cannot be built until now.
            Canvas canvas = __instance.GetComponentInParent<Canvas>();
            AssetLoader.BuildSprites(canvas != null ? canvas.referencePixelsPerUnit : 100f, Log);

            HudLayout.Reset("hud", __instance.m_healthText != null ? __instance.m_healthText.font : null);
            s_started = false;

            Build(0, HealthFull, "health", "Health", HealthHeight, HealthFrame, HealthLength, HealthHome, hudroot,
                __instance.m_healthBarRoot, __instance.m_healthBarRoot,
                __instance.m_healthBarFast, __instance.m_healthBarSlow,
                __instance.m_healthText, __instance.m_healthAnimator);

            Build(1, StaminaFull, "stamina", "Stamina", StatHeight, 1f, StaminaLength, StaminaHome, hudroot,
                __instance.m_staminaBar2Root, __instance.m_staminaBar2Root?.Find("Stamina") as RectTransform,
                __instance.m_staminaBar2Fast, __instance.m_staminaBar2Slow,
                __instance.m_staminaText, __instance.m_staminaAnimator);

            Build(2, EitrFull, "eitr", "Eitr", StatHeight, 1f, RowThreeLength, EitrHome, hudroot,
                __instance.m_eitrBarRoot, __instance.m_eitrBarRoot?.Find("Stamina") as RectTransform,
                __instance.m_eitrBarFast, __instance.m_eitrBarSlow,
                __instance.m_eitrText, __instance.m_eitrAnimator, SharedRowLegacy);

            Build(3, AdrenalineFull, "adrenaline", "Adrenaline", StatHeight, 1f, RowThreeLength, AdrenalineHome, hudroot,
                __instance.m_adrenalineBarRoot, __instance.m_adrenalineBarRoot?.Find("Stamina") as RectTransform,
                __instance.m_adrenalineBarFast, __instance.m_adrenalineBarSlow,
                __instance.m_adrenalineText, __instance.m_adrenalineAnimator, SharedRowLegacy);

            Hide(__instance.m_healthPanel?.Find("healthicon"));
            Hide(__instance.m_healthPanel?.Find("foodicon"));
            Hide(__instance.m_healthPanel?.Find("foodicon (1)"));

            RectTransform cluster = MakeCluster(hudroot);
            SkinFood(__instance, cluster);
            SkinPower(__instance, cluster);
            HudLayout.Register("hud", "cluster", "Food + power", cluster, cluster, ClusterHome, ClusterScale);

            Log.LogInfo("driving 4 bars, skinned 3 food boxes and the guardian power box"
                + ", food boxes showing the quick slots");
        }

        // --- bars ---

        private static void Build(int index, float fullStat, string key, string label, float height, float frameScale, float length, Vector2 position,
            Transform hudroot, RectTransform panel, RectTransform inner,
            GuiBar fast, GuiBar slow, TMP_Text text, Animator animator, Vector2? supersededPosition = null)
        {
            if (panel == null)
            {
                Log.LogWarning("no panel for " + label + " - that bar is left alone");
                return;
            }

            BarSkin bar = Bars[index];
            bar.Panel = panel;
            bar.Inner = inner ?? panel;
            bar.Fast = fast;
            bar.Slow = slow;
            bar.Text = text;
            bar.FullStat = fullStat;

            // The panel Animator drove show/hide and the damage flash. Visibility is ours now,
            // and Unity does not rebind an Animator when a child is reparented out from under
            // it - healthpanel's kept driving the detached Health and stood the bar on its end
            // through a rotation curve. Whatever pose it was mid-way through is cleared after.
            if (animator != null)
                animator.enabled = false;
            foreach (CanvasGroup group in panel.GetComponentsInChildren<CanvasGroup>(true))
                group.alpha = 1f;

            // Disabling an Animator leaves whatever pose it was mid-way through, and it had
            // curves on descendants as well as the panel - that is what left the health number
            // lying on its side three levels down inside fast/bar. Clear the whole subtree.
            foreach (Transform child in panel.GetComponentsInChildren<Transform>(true))
            {
                child.localRotation = Quaternion.identity;
                child.localScale = Vector3.one;
            }

            // Vanilla parents the number inside fast/bar, so it slides along with the fill and
            // ends up riding the right edge of the red. It belongs centred on the bar.
            if (text != null)
            {
                var trt = (RectTransform)text.transform;
                trt.SetParent(bar.Inner, false);
                trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
                trt.pivot = new Vector2(0.5f, 0.5f);
                trt.anchoredPosition = Vector2.zero;
                trt.localRotation = Quaternion.identity;
                trt.localScale = Vector3.one;
                trt.SetAsLastSibling();
            }

            bar.Frame = SkinFrame(bar.Inner);
            SkinFill(fast);
            SkinFill(slow);

            panel.SetParent(hudroot, false);
            panel.anchorMin = panel.anchorMax = Vector2.zero;
            panel.pivot = new Vector2(0f, 0.5f);

            // MiniMap is a child of hudroot, so the large map is a sibling of these panels and
            // draw order inside the canvas is sibling order. SetParent appends to the end of
            // the list, which put the bars in FRONT of the open map. Back to the front of the
            // list, where vanilla's panels sat and the map, build menu and damage flash all
            // paint over them.
            panel.SetAsFirstSibling();

            // Hit-test and outline against the frame, not the panel: the panel is only the
            // window between the ornaments, so an outline on it stopped short of the knot and
            // the point and did not match what you see.
            RectTransform outline = bar.Frame != null ? (RectTransform)bar.Frame.transform : panel;
            HudLayout.Register("hud", key, label, panel, outline, position, height,
                bar.ApplyHeight, bar.ApplyLength, bar.ApplyFrame, frameScale, length, supersededPosition);
        }

        /// <summary>
        /// Puts the frame on a bar's "border" child. Two things vanilla does here that had to be
        /// undone: on stamina, eitr and adrenaline that GameObject ships DISABLED, so assigning
        /// a sprite to it changed nothing visible; and the children are ordered border, bkg,
        /// slow, fast, which in Unity UI draws the frame UNDER the fill. At full health the fill
        /// covers the whole rect, which is why the bar read as a plain dark box with a number in
        /// it - that box was the fill sitting over its own frame.
        /// </summary>
        private static Image SkinFrame(RectTransform bar)
        {
            if (bar == null)
                return null;

            Transform border = bar.Find("border");
            if (border == null)
            {
                Log.LogWarning("no 'border' child under " + bar.name + " - that bar keeps the vanilla frame");
                return null;
            }

            if (!border.gameObject.activeSelf)
                border.gameObject.SetActive(true);

            Image img = border.GetComponent<Image>();
            if (img == null)
            {
                Log.LogWarning("'border' under " + bar.name + " has no Image - that bar keeps the vanilla frame");
                return null;
            }

            // Colour goes to white or the dark vanilla tint (0F0F0F7A) swallows the bronze.
            img.sprite = AssetLoader.BarFrame;
            img.type = Image.Type.Sliced;
            img.color = Color.white;

            // Frame in front of the fill, with the 9-slice middle left transparent so the fill
            // still shows through the window between the ornaments.
            img.fillCenter = false;
            border.SetAsLastSibling();
            return img;
        }

        private static void SkinFill(GuiBar guiBar)
        {
            Image fill = guiBar?.m_bar?.GetComponent<Image>();
            if (fill == null)
                return;

            // Left Tiled, which is what vanilla already uses - that is why the knotwork gets
            // uncovered rather than stretched as the bar drains. Colour is vanilla's own
            // red / yellow / eitr-pink / grey and is left alone.
            fill.sprite = AssetLoader.BarFill;
            fill.type = Image.Type.Tiled;
        }

        // --- food ---

        private static void SkinFood(Hud hud, RectTransform cluster)
        {
            s_foodFrames = new Image[3];
            s_foodTimes = new TMP_Text[3];
            Vector2[] defaults = { Food0, Food1, Food2 };

            for (int i = 0; i < 3; i++)
            {
                Transform box = hud.m_healthPanel?.Find("food" + i);
                if (box == null)
                    continue;

                Image frame = box.GetComponent<Image>();
                if (frame != null)
                {
                    // A diamond cannot be 9-sliced, so it draws Simple with the aspect kept.
                    frame.sprite = AssetLoader.FoodFrame;
                    frame.type = Image.Type.Simple;
                    frame.preserveAspect = true;
                    frame.color = Color.white;
                }
                s_foodFrames[i] = frame;

                var rt = (RectTransform)box;
                Seat(rt, cluster, defaults[i], FoodSize, FoodScales[i]);
                s_foodBoxes[i] = rt;

                Image icon = hud.m_foodIcons.Length > i ? hud.m_foodIcons[i] : null;
                if (icon != null)
                {
                    float margin = FoodSize * (1f - IconFraction);
                    ((RectTransform)icon.transform).sizeDelta = new Vector2(-margin, -margin);
                }

                // The countdown is taken first because KeyLabel reads its font: built the other
                // way round, every key letter came out with a null font on the first Awake.
                //
                // Vanilla hangs the countdown off the box's bottom-right corner, which on a
                // diamond is empty space outside the frame. Centre it along the lower edge.
                s_foodTimes[i] = hud.m_foodTime.Length > i ? hud.m_foodTime[i] : null;

                s_foodKeys[i] = KeyLabel(rt, i);
                if (s_foodTimes[i] != null)
                {
                    var trt = (RectTransform)s_foodTimes[i].transform;
                    trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0f);
                    trt.pivot = new Vector2(0.5f, 0f);
                    trt.anchoredPosition = new Vector2(0f, FoodSize * 0.2f);
                }
            }

            // Blanking these makes vanilla UpdateFood a no-op loop so it stops writing the food
            // icons every frame. The boxes are ours from here.
            hud.m_foodIcons = Array.Empty<Image>();
            hud.m_foodBars = Array.Empty<Image>();
            hud.m_foodTime = Array.Empty<TMP_Text>();
        }

        /// <summary>
        /// The quick slot's key on its own diamond, mirroring the countdown: key at the top,
        /// timer at the bottom, icon between them. Built once per box and then only its text
        /// changes, so nothing is allocated per frame.
        /// </summary>
        private static TMP_Text KeyLabel(RectTransform box, int index)
        {
            var go = new GameObject("CarturUIHud_Key", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(box, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(FoodSize, KeyHeight);
            // Up onto the wood, clear of the food. It sat a fifth of the diamond down, which is
            // where the item icon starts - the icon is 0.55 of the frame, so its top edge is
            // 18.5 units down on an 82 unit diamond and the letter was lying across it. Lifting
            // it by its own height puts it on the frame's upper bevel instead.
            rt.anchoredPosition = new Vector2(0f, -FoodSize * 0.2f + KeyRise);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = s_foodTimes[index] != null ? s_foodTimes[index].font : null;
            text.fontSize = 14f;
            text.alignment = TextAlignmentOptions.Center;
            // White with a thin black outline. The letter sits over the item icon, and icons
            // run from near-black berries to bright cooked meat, so no solid colour reads
            // against all of them - the outline is what makes it legible, the colour only
            // decides which half of the range it prefers.
            //
            // The outline goes on text.fontMaterial, which hands back a per-label instance.
            // Writing it to text.font.material instead would outline every piece of text in
            // the game drawn with this font.
            text.color = Color.white;
            // Bold: at 14pt over a busy icon the plain weight read faint, and thickening the
            // glyph does more for it than brightening a colour that is already white.
            text.fontStyle = FontStyles.Bold;
            Material material = text.fontMaterial;
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, KeyOutline);
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        /// <summary>Refreshes the key letters. Called with the icons, not per frame.</summary>
        private static void RefreshQuickSlotKeys()
        {
            for (int i = 0; i < s_foodKeys.Length; i++)
            {
                TMP_Text label = s_foodKeys[i];
                if (label == null)
                    continue;
                string key = QuickSlots.KeyText(i);
                if (label.text != key)
                    label.text = key;
            }
        }

        internal static void RefreshQuickSlots()
        {
            if (s_foodFrames == null)
                return;

            RefreshQuickSlotKeys();

            // Cached here so the per-frame pass does not walk the inventory three times a
            // frame looking them up. What is actually drawn on a diamond is decided there,
            // because it depends on what is being digested, and that changes without the
            // inventory changing at all.
            for (int i = 0; i < s_slotItems.Length; i++)
                s_slotItems[i] = QuickSlots.Item(i);
        }

        /// <summary>
        /// What each diamond shows, decided every frame, because it depends on two things that
        /// move independently: what is in that quick slot, and what the player is digesting.
        ///
        /// One rule, Cartur's: THE ICON IS THE SLOT. A diamond draws the item that is in its
        /// slot and nothing else, so a full diamond means "you have food loaded here" and an
        /// empty one means "you have not" - which is the question the row is on screen to
        /// answer. The food you are digesting is vanilla's own list; it is not what these are.
        ///
        ///   slot holds food                       -> its icon
        ///   slot is empty                         -> no icon, whatever is still burning down
        ///
        /// The countdown is the one thing that outlives the item, because the food you just ate
        /// out of that slot is still doing something for you:
        ///
        ///   slot holds food you are digesting     -> that buff's countdown
        ///   you ate that slot's food, it is gone  -> empty frame, its countdown still running
        ///   slot holds food you are not digesting -> "Eat"
        ///   nothing in the slot, nothing burning  -> blank
        ///
        /// What went, and why: an earlier version dimmed the eaten food's icon back onto the
        /// diamond, and handed any unclaimed buff - food eaten off the hotbar - down into the
        /// blank diamonds, so all three boxes covered all three buffs like vanilla's. Both put
        /// an icon on a diamond for food that was not in that slot, which is the thing this
        /// rule exists to stop: it made a loaded slot and an empty one look the same.
        ///
        /// Nothing is allocated per frame: GetFoods hands back the player's own list, the slot
        /// items are cached by RefreshQuickSlots, and the claim table is three ints.
        /// </summary>

        // The food last eaten out of each quick slot, by shared name. Set in AteFrom below.
        private static readonly string[] s_ate = new string[3];

        // Which food each diamond is showing this frame, as an index into the player's list.
        private static readonly int[] s_show = { -1, -1, -1 };

        /// <summary>
        /// Remembers which quick slot a food was eaten out of. Player.EatFood is the one place
        /// the game records a food, and the item is still sitting at its inventory position
        /// when it runs - so the slot is read off the item rather than guessed from whichever
        /// hotkey was last pressed, and a food eaten by clicking the diamond is caught too.
        /// </summary>
        [HarmonyPatch(typeof(Player), "EatFood")]
        [HarmonyPrefix]
        private static void AteFrom(Player __instance, ItemDrop.ItemData item)
        {
            if (__instance != Player.m_localPlayer || item?.m_shared == null)
                return;
            Slots.Slot slot = Slots.At(item.m_gridPos);
            if (slot != null && slot.Kind == Slots.Kind.Quick && slot.Index < s_ate.Length)
                s_ate[slot.Index] = item.m_shared.m_name;
        }

        private static void ShowMatchingFoodTimes(Player player)
        {
            // Skin bails out without building these if the HUD it expects is not there, and
            // this runs every frame regardless - so without the guard a missing hudroot is a
            // null reference per frame rather than one warning at load.
            if (s_foodFrames == null || s_foodTimes == null)
                return;

            List<Player.Food> foods = player.GetFoods();

            for (int i = 0; i < s_show.Length; i++)
            {
                ItemDrop.ItemData slot = s_slotItems[i];
                int own = IndexOf(foods, slot?.m_shared?.m_name);
                if (own < 0)
                {
                    own = IndexOf(foods, s_ate[i]);
                    if (own < 0)
                        s_ate[i] = null;   // that buff has run out; stop remembering it
                }
                s_show[i] = own;
            }

            for (int i = 0; i < s_show.Length; i++)
            {
                ItemDrop.ItemData slot = s_slotItems[i];
                Player.Food food = s_show[i] >= 0 && s_show[i] < foods.Count ? foods[s_show[i]] : null;

                Draw(i, slot);
                Countdown(i, slot, food);
            }
        }

        private static int IndexOf(List<Player.Food> foods, string name)
        {
            if (string.IsNullOrEmpty(name))
                return -1;
            for (int f = 0; f < foods.Count; f++)
                if (foods[f]?.m_item?.m_shared != null && foods[f].m_item.m_shared.m_name == name)
                    return f;
            return -1;
        }

        /// <summary>The item picture on a diamond. Only written when it actually changes.</summary>
        private static void Draw(int index, ItemDrop.ItemData item)
        {
            Image frame = s_foodFrames[index];
            if (frame == null || frame.transform.childCount == 0)
                return;
            Image icon = frame.transform.GetChild(0).GetComponent<Image>();
            if (icon == null)
                return;

            bool show = item != null;
            if (icon.gameObject.activeSelf != show)
                icon.gameObject.SetActive(show);
            if (!show)
                return;

            Sprite sprite = item.GetIcon();
            if (icon.sprite != sprite)
                icon.sprite = sprite;
            if (icon.color != Color.white)
                icon.color = Color.white;
        }

        private static void Countdown(int index, ItemDrop.ItemData slot, Player.Food food)
        {
            TMP_Text text = s_foodTimes[index];
            if (text == null)
                return;

            if (food == null)
            {
                // Nothing being digested for this diamond. A slot with food in it is an
                // invitation; an empty one has nothing to say. The same test the slot itself
                // uses to decide what it will accept, so the two can never disagree.
                bool edible = Slots.IsFood(slot);
                if (text.gameObject.activeSelf != edible)
                    text.gameObject.SetActive(edible);
                if (!edible)
                    return;
                if (text.text != EatLabel)
                    text.text = EatLabel;
                text.color = EatColour;
                return;
            }

            if (!text.gameObject.activeSelf)
                text.gameObject.SetActive(true);

            // Same numbers vanilla's UpdateFood prints.
            float seconds = food.m_time / Game.m_foodRate;
            string label = seconds >= 60f
                ? Mathf.CeilToInt(seconds / 60f) + "m"
                : Mathf.FloorToInt(seconds) + "s";
            if (text.text != label)
                text.text = label;

            // Red, blinking, for the last minute. Cartur's call, and the reason is that the
            // blink is a signal rather than a gauge: it always means the same thing, so it is
            // learned once and never worked out again.
            bool nearlyGone = seconds <= LowFoodSeconds;
            text.color = nearlyGone
                ? Color.Lerp(LowFoodDim, LowFoodBright, Mathf.PingPong(Time.time * BlinkSpeed, 1f))
                : Color.white;
        }

        // --- guardian power ---

        private static void SkinPower(Hud hud, RectTransform cluster)
        {
            RectTransform root = hud.m_gpRoot;
            if (root == null)
                return;

            Seat(root, cluster, Power, PowerSize, PowerScale);

            Image bkg = root.Find("Bkg")?.GetComponent<Image>();
            if (bkg != null)
            {
                bkg.sprite = AssetLoader.FoodFrame;
                bkg.type = Image.Type.Simple;
                bkg.preserveAspect = true;
                bkg.color = Color.white;
                ((RectTransform)bkg.transform).sizeDelta = new Vector2(PowerSize, PowerSize);
            }

            // The concept puts the name and the Ready/cooldown text over the icon rather than
            // above and below it, so the diamond stays compact.
            float icon = PowerSize * IconFraction;
            if (hud.m_gpIcon != null)
                ((RectTransform)hud.m_gpIcon.transform).sizeDelta = new Vector2(icon, icon);

            MoveText(hud.m_gpName, new Vector2(0f, -icon * 0.18f));
            MoveText(hud.m_gpCooldown, new Vector2(0f, -icon * 0.42f));


        }

        private static void MoveText(TMP_Text text, Vector2 position)
        {
            if (text == null)
                return;
            var rt = (RectTransform)text.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
        }

        // --- per frame ---

        [HarmonyPatch(typeof(Hud), "Update")]
        [HarmonyPostfix]
        private static void Frame(Hud __instance)
        {
            HudLayout.Tick();

            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            bool edit = HudLayout.Editing;

            Bars[0].Drive(player.GetHealth(), player.GetMaxHealth());
            Bars[1].Drive(player.GetStamina(), player.GetMaxStamina());

            // Eitr and adrenaline are hidden outright without a pool, rather than vanilla's
            // fade, which would leave an empty frame on screen now the frame is visible. Both
            // are forced on in edit mode so they can be placed.
            //
            // Each answers to its own pool and neither knows about the other, which is how the
            // game does it (Hud.UpdateEitr and Hud.UpdateAdrenaline never consult each other).
            // They used to share one slot with adrenaline winning; a trinket gives a permanent
            // adrenaline pool, so that hid the eitr bar for anyone wearing one.
            float maxEitr = player.GetMaxEitr();
            float maxAdrenaline = player.GetMaxAdrenaline();

            // Adrenaline is the one bar whose length follows what you have rather than what you
            // could have. Every other bar's ceiling moves - eat and your health bar grows - but
            // the adrenaline pool is set by the trinket and never changes, so a bar sized from it
            // sat at a fixed length showing nothing. Driving it with the current value as its own
            // ceiling grows the bar out of the knot as adrenaline builds, and empty means gone
            // rather than an empty frame, which is also what the game's own HUD does.
            float adrenaline = player.GetAdrenaline();
            Bars[3].Show((maxAdrenaline > 0f && adrenaline > 0f) || edit);
            if (adrenaline > 0f)
                Bars[3].Drive(adrenaline, adrenaline);
            else if (edit)
                Bars[3].Drive(AdrenalineFull, AdrenalineFull);   // full length to place it against

            Bars[2].Show(maxEitr > 0f || edit);
            if (maxEitr > 0f)
                Bars[2].Drive(player.GetEitr(), maxEitr);

            if (edit && __instance.m_gpRoot != null && !__instance.m_gpRoot.gameObject.activeSelf)
                __instance.m_gpRoot.gameObject.SetActive(true);

            ShowMatchingFoodTimes(player);

            if (s_started)
                return;
            s_started = true;

            // The first icon fill needs a player: logging in with items already in the slots
            // changes nothing, so nothing would otherwise ask for a refresh.
            RefreshQuickSlots();
        }

        /// <summary>
        /// The container the diamonds and the power box ride in. Sized to their bounding box so
        /// edit mode has something to take hold of.
        /// </summary>
        private static RectTransform MakeCluster(Transform hudroot)
        {
            var go = new GameObject("CarturUIHud_Cluster", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(hudroot, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = ClusterSize;
            rt.SetAsFirstSibling();   // behind the map and the build menu, as above
            return rt;
        }

        /// <summary>
        /// Places one piece in the cluster. Its own scale is baked onto the transform so the
        /// group's scale multiplies on top, keeping the relative sizes Cartur set.
        /// </summary>
        private static void Seat(RectTransform rt, RectTransform cluster, Vector2 offset, float size, float scale)
        {
            rt.SetParent(cluster, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = offset;
            rt.localScale = Vector3.one * scale;
        }

        private static void Hide(Transform t)
        {
            if (t != null)
                t.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Stops vanilla driving the four bars. Each of these writes the bar's rect, its fill and
    /// its position every frame from its own formula, so anything this mod sets is overwritten
    /// on the next tick unless the method is skipped outright. BarSkin does the work instead.
    /// This is what AugaLite does too, and for the same reason.
    /// </summary>
    [HarmonyPatch(typeof(Hud))]
    internal static class VanillaBars
    {
        [HarmonyPatch("UpdateHealth")]
        [HarmonyPrefix]
        private static bool Health() => false;

        [HarmonyPatch("UpdateStamina")]
        [HarmonyPrefix]
        private static bool Stamina() => false;

        [HarmonyPatch("UpdateEitr")]
        [HarmonyPrefix]
        private static bool Eitr() => false;

        [HarmonyPatch("UpdateAdrenaline")]
        [HarmonyPrefix]
        private static bool Adrenaline() => false;
    }
}
