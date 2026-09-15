using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
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
        // Eitr and adrenaline share the third row, one 36.5 step below stamina. A melee
        // character has adrenaline and no eitr, a mage the reverse, and the rare build with
        // both is running eitr food it would not normally eat - so adrenaline takes the slot
        // when it is there and eitr falls back to it otherwise. Same x as stamina, so the two
        // thin bars line up exactly.
        // Third row sits on stamina's x, so the two thin bars share a left edge and the same
        // geometry - which is why they end up sharing a length figure too.
        private static readonly Vector2 EitrHome = new Vector2(330.75f, 219.75f);
        private static readonly Vector2 AdrenalineHome = new Vector2(330.75f, 219.75f);

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

        internal const string EaqsGuid = "randyknapp.mods.equipmentandquickslots";

        // health, stamina, eitr, adrenaline
        internal static readonly BarSkin[] Bars = { new BarSkin(), new BarSkin(), new BarSkin(), new BarSkin() };

        private static Image[] s_foodFrames;
        private static TMP_Text[] s_foodTimes = new TMP_Text[3];
        private static readonly ItemDrop.ItemData[] s_slotItems = new ItemDrop.ItemData[3];
        private static bool s_quickSlotMode;
        private static bool s_started;
        private static MethodInfo s_getQuickSlotItems;

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

            HudLayout.Reset(__instance.m_healthText != null ? __instance.m_healthText.font : null);
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
                __instance.m_eitrText, __instance.m_eitrAnimator);

            Build(3, AdrenalineFull, "adrenaline", "Adrenaline", StatHeight, 1f, RowThreeLength, AdrenalineHome, hudroot,
                __instance.m_adrenalineBarRoot, __instance.m_adrenalineBarRoot?.Find("Stamina") as RectTransform,
                __instance.m_adrenalineBarFast, __instance.m_adrenalineBarSlow,
                __instance.m_adrenalineText, __instance.m_adrenalineAnimator);

            Hide(__instance.m_healthPanel?.Find("healthicon"));
            Hide(__instance.m_healthPanel?.Find("foodicon"));
            Hide(__instance.m_healthPanel?.Find("foodicon (1)"));

            RectTransform cluster = MakeCluster(hudroot);
            SkinFood(__instance, cluster);
            SkinPower(__instance, cluster);
            HudLayout.Register("cluster", "Food + power", cluster, cluster, ClusterHome, ClusterScale);

            Log.LogInfo("driving 4 bars, skinned 3 food boxes and the guardian power box"
                + (s_quickSlotMode ? ", food from Equipment and Quick Slots" : ", food from vanilla")
                + (AssetLoader.PowerFrameIsPlaceholder ? "; power box is using the food diamond as a placeholder" : ""));
        }

        // --- bars ---

        private static void Build(int index, float fullStat, string key, string label, float height, float frameScale, float length, Vector2 position,
            Transform hudroot, RectTransform panel, RectTransform inner,
            GuiBar fast, GuiBar slow, TMP_Text text, Animator animator)
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
            HudLayout.Register(key, label, panel, outline, position, height,
                bar.ApplyHeight, bar.ApplyLength, bar.ApplyFrame, frameScale, length);
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
            s_quickSlotMode = Chainloader.PluginInfos.ContainsKey(EaqsGuid) && ResolveEaqs();
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

                Image icon = hud.m_foodIcons.Length > i ? hud.m_foodIcons[i] : null;
                if (icon != null)
                {
                    float margin = FoodSize * (1f - IconFraction);
                    ((RectTransform)icon.transform).sizeDelta = new Vector2(-margin, -margin);
                }

                // Vanilla hangs the countdown off the box's bottom-right corner, which on a
                // diamond is empty space outside the frame. Centre it along the lower edge.
                s_foodTimes[i] = hud.m_foodTime.Length > i ? hud.m_foodTime[i] : null;
                if (s_foodTimes[i] != null)
                {
                    var trt = (RectTransform)s_foodTimes[i].transform;
                    trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0f);
                    trt.pivot = new Vector2(0.5f, 0f);
                    trt.anchoredPosition = new Vector2(0f, FoodSize * 0.2f);
                }
            }

            if (!s_quickSlotMode)
                return;

            // Blanking these makes vanilla UpdateFood a no-op loop so it stops writing the food
            // icons every frame. The boxes are ours from here.
            hud.m_foodIcons = Array.Empty<Image>();
            hud.m_foodBars = Array.Empty<Image>();
            hud.m_foodTime = Array.Empty<TMP_Text>();
        }

        private static bool ResolveEaqs()
        {
            Type api = AccessTools.TypeByName("EquipmentAndQuickSlots.API");
            s_getQuickSlotItems = api?.GetMethod("GetQuickSlotItems", BindingFlags.Public | BindingFlags.Static);
            if (s_getQuickSlotItems == null)
            {
                Log.LogWarning("Equipment and Quick Slots is loaded but API.GetQuickSlotItems is gone - falling back to vanilla food");
                return false;
            }

            // EQAS raises this whenever a slot's item changes, so the boxes never need a
            // per-frame refresh of their own.
            MethodInfo add = api.GetMethod("AddSlotItemChangedListener", BindingFlags.Public | BindingFlags.Static);
            if (add != null)
            {
                Action<string, ItemDrop.ItemData, ItemDrop.ItemData> listener = (slot, before, after) => RefreshQuickSlots();
                add.Invoke(null, new object[] { listener });
            }
            else
            {
                Log.LogWarning("Equipment and Quick Slots API.AddSlotItemChangedListener is gone - quick slot icons will not refresh");
            }

            return true;
        }

        private static void RefreshQuickSlots()
        {
            if (s_getQuickSlotItems == null || s_foodFrames == null)
                return;

            var items = s_getQuickSlotItems.Invoke(null, null) as IList<ItemDrop.ItemData>;

            for (int i = 0; i < s_foodFrames.Length; i++)
            {
                // Cached so the per-frame countdown does not have to go through reflection.
                s_slotItems[i] = items != null && i < items.Count ? items[i] : null;

                Image frame = s_foodFrames[i];
                if (frame == null || frame.transform.childCount == 0)
                    continue;

                Image icon = frame.transform.GetChild(0).GetComponent<Image>();
                if (icon == null)
                    continue;

                ItemDrop.ItemData item = s_slotItems[i];
                icon.gameObject.SetActive(item != null);
                if (item != null)
                {
                    icon.sprite = item.GetIcon();
                    icon.color = Color.white;
                }
            }
        }

        /// <summary>
        /// A quick slot holds an inventory item and has no countdown of its own - the timer
        /// belongs to the separate list of foods the player has eaten. Each slot is matched
        /// against that list by shared name, and when the slot holds something currently being
        /// digested its remaining time is drawn on that diamond.
        /// </summary>
        private static void ShowMatchingFoodTimes(Player player)
        {
            List<Player.Food> foods = player.GetFoods();

            for (int i = 0; i < s_foodTimes.Length; i++)
            {
                TMP_Text text = s_foodTimes[i];
                if (text == null)
                    continue;

                Player.Food match = null;
                ItemDrop.ItemData slot = s_slotItems[i];
                if (slot?.m_shared != null)
                {
                    foreach (Player.Food food in foods)
                    {
                        if (food?.m_item?.m_shared != null && food.m_item.m_shared.m_name == slot.m_shared.m_name)
                        {
                            match = food;
                            break;
                        }
                    }
                }

                if (match == null)
                {
                    if (text.gameObject.activeSelf)
                        text.gameObject.SetActive(false);
                    continue;
                }

                if (!text.gameObject.activeSelf)
                    text.gameObject.SetActive(true);

                // Same numbers vanilla's UpdateFood prints, including the pulse under a minute.
                float seconds = match.m_time / Game.m_foodRate;
                string label = seconds >= 60f
                    ? Mathf.CeilToInt(seconds / 60f) + "m"
                    : Mathf.FloorToInt(seconds) + "s";
                if (text.text != label)
                    text.text = label;
                text.color = seconds >= 60f
                    ? Color.white
                    : new Color(1f, 1f, 1f, 0.4f + Mathf.Sin(Time.time * 10f) * 0.6f);
            }
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
                bkg.sprite = AssetLoader.PowerFrame;
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
            // Both sit in the third row, so only one of them draws. Adrenaline wins it: with
            // both pools up you are a melee character who happens to have eaten eitr food, and
            // adrenaline is the one changing several times a second.
            float maxEitr = player.GetMaxEitr();
            float maxAdrenaline = player.GetMaxAdrenaline();

            Bars[3].Show(maxAdrenaline > 0f || edit);
            if (maxAdrenaline > 0f)
                Bars[3].Drive(player.GetAdrenaline(), maxAdrenaline);

            Bars[2].Show((maxEitr > 0f && maxAdrenaline <= 0f) || edit);
            if (maxEitr > 0f)
                Bars[2].Drive(player.GetEitr(), maxEitr);

            if (edit && __instance.m_gpRoot != null && !__instance.m_gpRoot.gameObject.activeSelf)
                __instance.m_gpRoot.gameObject.SetActive(true);

            if (s_quickSlotMode)
                ShowMatchingFoodTimes(player);

            if (s_started)
                return;
            s_started = true;

            if (!s_quickSlotMode)
                return;

            // EQAS builds QuickSlotsHotkeyBar from its own Awake patch, so at ours it may not
            // exist yet. And the first icon fill needs a player, because logging in with items
            // already in the slots raises no change event.
            Transform bar = __instance.transform.Find("hudroot")?.Find("QuickSlotsHotkeyBar");
            if (bar != null)
                Hide(bar);
            else
                Log.LogWarning("QuickSlotsHotkeyBar not found - quick slot items may show twice");

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
