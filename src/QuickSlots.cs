using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Part 12 of Cartur's UI: the quick slot hotkeys. Replaces the quick slot half of
    /// Equipment and Quick Slots.
    ///
    /// The quick slot cells sit in the equipment board's QUICK SLOTS boxes (EquipmentPanel,
    /// Cartur 2026-10-03). The three food diamonds on the HUD only show what is in them - item,
    /// key letter, countdown - and take no clicks. Host below is how the diamonds used to BE the
    /// cells; the same trick now puts the inventory's first row on the HUD hotbar board (see
    /// HostOn and HotbarRow).
    ///
    /// Part 1 still owns everything the diamond draws: the frame, the item icon, the key
    /// letter and the food countdown. None of that changed.
    ///
    /// Default keys are Z, V and B, which are the ones Equipment and Quick Slots shipped and
    /// what Cartur's config on disk is set to, so nothing has to be relearned.
    /// </summary>
    internal static class QuickSlots
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        internal const int Count = 3;
        private static readonly KeyCode[] DefaultKeys = { KeyCode.Z, KeyCode.V, KeyCode.B };
        private static ConfigEntry<KeyboardShortcut>[] s_keys;

        internal static void Init(ConfigFile config)
        {
            s_keys = new ConfigEntry<KeyboardShortcut>[Count];
            for (int i = 0; i < Count; i++)
                s_keys[i] = config.Bind("Quick slots", "key" + (i + 1),
                    new KeyboardShortcut(DefaultKeys[i]),
                    "Hotkey that uses the item in quick slot " + (i + 1) + ".");
            Log.LogInfo("quick slot keys " + KeyText(0) + "/" + KeyText(1) + "/" + KeyText(2));
        }

        /// <summary>The key letter for a slot, for the label drawn on its diamond.</summary>
        internal static string KeyText(int index) =>
            s_keys != null && index >= 0 && index < s_keys.Length
                ? s_keys[index].Value.MainKey.ToString()
                : "";

        /// <summary>The item in quick slot <paramref name="index"/>, or null.</summary>
        internal static ItemDrop.ItemData Item(int index) =>
            index >= 0 && index < Count ? Slots.All[index]?.Item : null;

        // ---- a real inventory cell laid over a HUD box ---------------------------------------

        private static readonly System.Collections.Generic.List<InventoryElement> s_hosted =
            new System.Collections.Generic.List<InventoryElement>();

        /// <summary>
        /// Lays a quick slot's real inventory cell over its diamond as an invisible hit area.
        ///
        /// Nothing is drawn by the cell. Every graphic it carries is switched off, including
        /// its own icon - the diamond already shows the item, the key letter and the countdown,
        /// and that drawing is Part 1's and is not touched. What the cell contributes is
        /// behaviour: drop an item on the diamond, drag one off it, right-click it, hover it
        /// for a tooltip. All of that is the game's own, and it keeps working after the cell is
        /// moved because InventoryGrid finds a cell by identity - GetButtonPos walks m_elements
        /// looking for the clicked GameObject - and its click handlers were wired when it was
        /// built. Neither cares which canvas it ended up on.
        ///
        /// The alternative was writing drag and drop for a HUD widget, which is a few hundred
        /// lines to arrive back where the game already was.
        ///
        /// Called from EquipmentPanel as the grid lays out, because the cells do not exist
        /// until InventoryGrid has built them, which is after the HUD is up.
        /// </summary>
        internal static void HostOn(InventoryElement element, RectTransform box)
        {
            if (element == null || box == null)
                return;

            if (!s_hosted.Contains(element))
                s_hosted.Add(element);
            s_hosted.RemoveAll(e => e == null);   // cells die with a grid rebuild
            GameObject go = element.gameObject;
            go.SetActive(true);

            // Every frame, and cheap: InventoryGrid.UpdateGui switches each cell's selection
            // marker back on for whichever cell the pad is on, and that marker is a square.
            // HudSkin lights the diamond's own frame instead, so this one stays off. This runs
            // after that loop, because EquipmentPanel's postfix is what calls Host.
            if (element.m_selected != null)
                element.m_selected.SetActive(false);

            // The rest is one-time. Host is called from a per-frame postfix, and walking the
            // cell's children every frame to re-hide things that are already hidden would be a
            // frame-rate bug for no gain.
            var rt = (RectTransform)go.transform;
            if (rt.parent == box)
                return;

            // Centred on the diamond and the same size as it, so the area you can drop on is
            // the frame you can see. Nothing here moves or resizes the diamond itself.
            rt.SetParent(box, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = box.rect.size;
            rt.localScale = Vector3.one;
            rt.SetAsLastSibling();

            // The cell gets its own canvas, sorted one above the inventory screen's.
            //
            // Root cause, measured rather than reasoned: the inventory screen's canvas is at
            // sorting order 600 and the HUD's at 400, and Inventory_screen/root/dropButton is
            // a full-screen raycast target on it. So with the bag open every pointer event
            // over a diamond went to that button instead of to the cell - no tooltip, no
            // pickup, and an item dragged onto a diamond was "dropped outside" and landed on
            // the floor. A raycast at the diamond listed dropButton first and the cell fourth.
            //
            // Nothing is drawn differently: the cell is invisible, so raising it changes the
            // order the pointer is tested in and nothing else. The diamond art stays where it
            // is, behind the map and the build menu.
            //
            // An overriding canvas needs its own raycaster - a graphic under a nested canvas
            // is registered to that canvas, so the HUD's raycaster stops seeing it.
            var canvas = go.GetComponent<Canvas>();
            if (canvas == null)
                canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = InventoryOrder() + 1;
            if (go.GetComponent<GraphicRaycaster>() == null)
                go.AddComponent<GraphicRaycaster>();

            // The cell's Button repaints its plate on every state change, and a cell's normal
            // tint is no longer near-black - see Skin - so clearing the colour here is not
            // enough on its own. EquipmentPanel.Bare puts the plate and the whole colour block
            // out together, which is the same thing the equipment cells need.
            EquipmentPanel.Bare(go);

            // The hover grows the hotbar box this cell sits on, the same as an inventory cell
            // grows itself (Cartur, 2026-10-04): the cell draws nothing, so growing it alone showed nothing.
            var fx = go.GetComponent<HoverFx>() ?? go.AddComponent<HoverFx>();
            fx.Target = box;

            // Invisible, but still a raycast target - that is the whole job. A Graphic with no
            // sprite and a clear colour still receives the pointer; disabling it would leave a
            // cell that cannot be clicked, which is the one thing it is here for.
            foreach (Graphic graphic in go.GetComponentsInChildren<Graphic>(true))
            {
                bool hitArea = graphic.gameObject == go;
                graphic.raycastTarget = hitArea;
                if (hitArea)
                {
                    var image = graphic as Image;
                    if (image != null)
                        image.sprite = null;
                    graphic.color = Color.clear;
                }
                else
                {
                    graphic.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// The inventory screen's own sorting order. Read from its canvas rather than written
        /// down, because it is the number this has to beat. 600 is only what it measured at,
        /// and is used if the canvas cannot be found at all.
        /// </summary>
        private static int InventoryOrder()
        {
            Canvas canvas = InventoryGui.instance?.GetComponentInParent<Canvas>();
            return canvas != null ? canvas.sortingOrder : 600;
        }

        /// <summary>
        /// A cell sitting on the HUD can be clicked whether or not the inventory is open, and a
        /// click with the bag shut would start a drag nobody can see or finish. They take the
        /// pointer only while the inventory is up.
        ///
        /// Load-bearing since the cells sort above the inventory screen: with the bag shut a
        /// raised hit area would swallow clicks meant for the world.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "Update")]
        [HarmonyPostfix]
        private static void GateClicks()
        {
            bool open = InventoryGui.IsVisible();
            if (open != s_clickable)
            {
                s_clickable = open;
                foreach (InventoryElement cell in s_hosted)
                {
                    if (cell == null)
                        continue;
                    Graphic hitArea = cell.GetComponent<Graphic>();
                    if (hitArea != null)
                        hitArea.raycastTarget = open;
                    else if (!s_noHitArea)
                    {
                        // Host keeps the raycast on the Graphic sitting on the element root.
                        // If the prefab ever stops having one, the diamond goes dead to the
                        // mouse and nothing else would say why.
                        s_noHitArea = true;
                        Log.LogWarning("a quick slot cell has no Graphic on its root - the"
                            + " diamond cannot be clicked or dropped onto");
                    }
                }
            }

            // A controller can move onto a quick slot, but its cell is drawn on the HUD, far
            // from the panel - and the cell's own selection square is switched off. So the
            // diamond itself is lit instead.
            // The quick slots are ordinary cells on the equipment board now, with their own
            // selection marker, so the diamonds no longer light for a controller.
            HudSkin.SetQuickSelection(-1);
        }

        private static bool s_clickable = true;
        private static bool s_noHitArea;

        /// <summary>
        /// Uses the item in a quick slot: eats the food, equips the weapon, same as clicking it
        /// in the inventory.
        ///
        /// The gate is the game's own Player.TakeInput, called through a delegate. It is
        /// protected, so C# cannot reach it - but reflection can, and an earlier version of
        /// this file copied its thirteen tests out by hand on the reasoning that it "cannot be
        /// called". That was wrong, and a hand copy silently stops matching the moment the game
        /// adds a state to it.
        ///
        /// HotkeyBar's two extra checks are kept on top, because they apply to hotbar keys
        /// specifically rather than to input in general.
        /// </summary>
        [HarmonyPatch(typeof(Player), "Update")]
        [HarmonyPostfix]
        private static void Hotkeys(Player __instance)
        {
            if (s_keys == null || __instance != Player.m_localPlayer || !CanAct(__instance))
                return;

            for (int i = 0; i < s_keys.Length; i++)
            {
                if (!s_keys[i].Value.IsDown())
                    continue;
                ItemDrop.ItemData item = Item(i);
                if (item != null)
                    __instance.UseItem(null, item, false);
            }
        }

        // Player.TakeInput is protected, so it is bound once as a delegate rather than
        // reflected per call - this is asked every frame, and a MethodInfo.Invoke there would
        // be a frame-rate bug.
        private static readonly Func<Player, bool> s_takeInput = BindTakeInput();

        internal static bool TakeInputFound => s_takeInput != null;

        private static Func<Player, bool> BindTakeInput()
        {
            MethodInfo method = AccessTools.Method(typeof(Player), "TakeInput");
            return method == null ? null : AccessTools.MethodDelegate<Func<Player, bool>>(method);
        }

        private static bool CanAct(Player player)
        {
            if (s_takeInput == null)
                return false;   // said once at load by CheckLookups; better mute than misfiring
            return s_takeInput(player)
                // HotkeyBar's two extras, on top of TakeInput's list.
                && !Hud.IsPieceSelectionVisible() && !Hud.InRadial();
        }
    }
}
