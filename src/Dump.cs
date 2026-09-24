using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// One-shot diagnostic. Walks the vanilla UI hierarchy and logs object names, sprite names
    /// and canvas scalers so a patch is written against what the game actually has rather than
    /// a guess. Runs once per launch while Dump.Enabled is true, then flips it off so a second
    /// launch stays quiet - the plugin's own config save (HudLayout.Persist) is what makes that
    /// stick to disk.
    /// </summary>
    internal static class Dump
    {
        internal static ConfigEntry<bool> Enabled;
        internal static BepInEx.Logging.ManualLogSource Log;

        private static bool s_hudDone;
        private static bool s_inventoryDone;
        private static bool s_loadingDone;
        private static bool s_buildMenuDone;
        private static int s_updateCount;
        private static bool s_barsArmed;
        private static bool s_barsIdle, s_barsEitr, s_barsAdrenaline;

        private static readonly string[] SpriteNeedles =
        {
            "panel", "button", "bar", "frame", "bkg", "tab", "field", "check", "scroll",
            "braid", "tooltip", "woodpanel", "item_background", "selection", "sunken", "darken"
        };

        private static readonly string[] EaqsNames =
        {
            "EaqsSlotRoot", "EaqsEquipmentBkg", "EaqsQuickSlotBkg", "EaqsCustomSlotBkg", "QuickSlotsHotkeyBar"
        };

        [HarmonyPatch(typeof(Hud), "Awake")]
        [HarmonyPostfix]
        private static void HudAwake(Hud __instance)
        {
            // Latched here because the dump switches itself off partway through a
            // boot, and the bar states we want can arrive minutes later.
            s_barsArmed = Enabled.Value;

            if (!Enabled.Value || s_hudDone)
                return;
            s_hudDone = true;

            try
            {
                Tree(__instance.transform, "Hud");

                HotkeyBar bar = __instance.transform.Find("hudroot/HotKeyBar")?.GetComponent<HotkeyBar>();
                if (bar != null && bar.m_elementPrefab != null)
                    Tree(bar.m_elementPrefab.transform, "HotkeyBar element prefab");
                else
                    Log.LogInfo("[dump] HotkeyBar element prefab not found");

                if (__instance.m_statusEffectTemplate != null)
                    Tree(__instance.m_statusEffectTemplate.transform, "StatusEffect template");

                Canvases();
            }
            catch (System.Exception e)
            {
                Log.LogInfo("[dump] failed: " + e);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "Show")]
        [HarmonyPostfix]
        private static void InventoryShow(InventoryGui __instance)
        {
            if (!Enabled.Value || s_inventoryDone)
                return;
            s_inventoryDone = true;

            try
            {
                Tree(__instance.transform, "InventoryGui");

                LogPrefab("m_playerGrid.m_elementPrefab", __instance.m_playerGrid?.m_elementPrefab);
                LogPrefab("m_containerGrid.m_elementPrefab", __instance.ContainerGrid?.m_elementPrefab);
                LogPrefab("m_recipeElementPrefab", __instance.m_recipeElementPrefab);
                LogPrefab("m_dragItemPrefab", __instance.m_dragItemPrefab);
                LogPrefab("m_trophieElementPrefab", __instance.m_trophieElementPrefab);

                RectTransform gridRoot = __instance.m_playerGrid?.m_gridRoot;
                if (gridRoot == null)
                {
                    Log.LogInfo("[dump] m_playerGrid.m_gridRoot is null");
                }
                else
                {
                    int n = 0;
                    foreach (Transform child in gridRoot)
                    {
                        if (n++ >= 60)
                        {
                            Log.LogInfo("[dump] ... gridRoot children truncated");
                            break;
                        }
                        var rt = child as RectTransform;
                        Log.LogInfo("[dump] gridRoot child " + child.name + " pos=" + (rt != null ? rt.anchoredPosition.ToString() : "n/a"));
                    }
                }

                Transform[] all = __instance.transform.GetComponentsInChildren<Transform>(true);
                foreach (string name in EaqsNames)
                {
                    bool found = false;
                    foreach (Transform t in all)
                    {
                        if (t.name != name)
                            continue;
                        found = true;
                        var rt = t as RectTransform;
                        Log.LogInfo("[dump] found " + name + " path=" + FullPath(t) + " parent=" + t.parent?.name
                            + " sizeDelta=" + (rt != null ? rt.sizeDelta.ToString() : "n/a")
                            + " anchoredPosition=" + (rt != null ? rt.anchoredPosition.ToString() : "n/a"));
                    }
                    if (!found)
                        Log.LogInfo("[dump] " + name + " not found");
                }

                SpriteCensus();
                TooltipPrefabs();
            }
            catch (System.Exception e)
            {
                Log.LogInfo("[dump] failed: " + e);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPostfix]
        private static void InventoryUpdate(InventoryGui __instance)
        {
            if (!Enabled.Value || !s_inventoryDone || s_updateCount >= 3)
                return;
            s_updateCount++;
            if (s_updateCount != 2 && s_updateCount != 3)
                return;

            try
            {
                Transform slot = System.Array.Find(__instance.transform.GetComponentsInChildren<Transform>(true),
                    t => t.name == "EaqsSlotRoot");
                var rt = slot as RectTransform;
                Log.LogInfo("[dump] EaqsSlotRoot anchoredPosition (update #" + s_updateCount + ") = "
                    + (rt != null ? rt.anchoredPosition.ToString() : "not found"));
                if (s_updateCount == 3)
                {
                    Enabled.Value = false;
                    Log.LogInfo("[dump] one-shot dump complete, disabling Dump.Enabled");
                }
            }
            catch (System.Exception e)
            {
                Log.LogInfo("[dump] failed: " + e);
            }
        }

        [HarmonyPatch(typeof(LoadingIndicator), "Awake")]
        [HarmonyPostfix]
        private static void LoadingAwake(LoadingIndicator __instance)
        {
            if (!Enabled.Value || s_loadingDone)
                return;
            s_loadingDone = true;

            try
            {
                Tree(__instance.transform, "LoadingIndicator");
                Canvases();

                // Everything has fired by this point in a normal boot - the diagnostic is done.
                Log.LogInfo("[dump] one-shot dump complete, disabling Dump.Enabled");
            }
            catch (System.Exception e)
            {
                Log.LogInfo("[dump] failed: " + e);
            }
        }

        /// <summary>
        /// The build menu, for the corner-ornament question: the Q, E and F key hints sit on top
        /// of the frame's corner knots, and a screenshot cannot say whether those hints are
        /// anchored to the frame or to the content inside it. That decides whether the fix is to
        /// grow the panel, inset the content, or move the hints - so the anchors get read rather
        /// than guessed at.
        ///
        /// BuildUi.Awake is the same hook BuildMenu.Dress uses, so what this prints is the tree
        /// as the skin finds it.
        /// </summary>
        [HarmonyPatch(typeof(BuildUi), "Awake")]
        [HarmonyPostfix]
        private static void BuildMenuAwake(BuildUi __instance)
        {
            if (!Enabled.Value || s_buildMenuDone)
                return;
            s_buildMenuDone = true;

            try
            {
                Tree(__instance.transform, "BuildUi");
            }
            catch (System.Exception e)
            {
                Log.LogInfo("[dump] build menu failed: " + e);
            }
        }

        // --- bars ---

        /// <summary>
        /// Why the eitr and adrenaline bars do not draw. Everything HudSkin.Frame looks at when
        /// it decides to show them, plus the state of the panels themselves, logged once per
        /// pool state. Stamina goes in the same dump as the control: it is built by the same
        /// code down the same path and it works, so any field that differs is the answer.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "Update")]
        [HarmonyPostfix]
        private static void BarState()
        {
            if (!s_barsArmed)
                return;

            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            float maxEitr = player.GetMaxEitr();
            float maxAdrenaline = player.GetMaxAdrenaline();

            if (maxEitr > 0f && !s_barsEitr)
                s_barsEitr = true;
            else if (maxAdrenaline > 0f && !s_barsAdrenaline)
                s_barsAdrenaline = true;
            else if (maxEitr <= 0f && maxAdrenaline <= 0f && !s_barsIdle)
                s_barsIdle = true;
            else
                return;

            try
            {
                Log.LogInfo("[dump] pools: maxEitr=" + maxEitr + " maxAdrenaline=" + maxAdrenaline
                    + " adrenaline=" + player.GetAdrenaline() + " editing=" + HudLayout.Editing);
                LogBar("stamina", HudSkin.Bars[1]);
                LogBar("eitr", HudSkin.Bars[2]);
                LogBar("adrenaline", HudSkin.Bars[3]);
            }
            catch (System.Exception e)
            {
                Log.LogInfo("[dump] failed: " + e);
            }
        }

        private static void LogBar(string label, BarSkin bar)
        {
            if (bar == null || bar.Panel == null)
            {
                Log.LogInfo("[dump] " + label + ": no panel");
                return;
            }

            var sb = new StringBuilder();
            sb.Append("[dump] ").Append(label)
              .Append(": valid=").Append(bar.Valid)
              .Append(" activeSelf=").Append(bar.Panel.gameObject.activeSelf)
              .Append(" inHierarchy=").Append(bar.Panel.gameObject.activeInHierarchy)
              .Append(" pos=").Append(bar.Panel.anchoredPosition)
              .Append(" size=").Append(bar.Panel.sizeDelta)
              .Append(" scale=").Append(bar.Panel.lossyScale);

            sb.Append(" | parents:");
            for (Transform t = bar.Panel.parent; t != null; t = t.parent)
                sb.Append(' ').Append(t.name).Append('(').Append(t.gameObject.activeSelf).Append(')');

            if (bar.Frame != null)
                sb.Append(" | frame enabled=").Append(bar.Frame.enabled)
                  .Append(" a=").Append(bar.Frame.color.a)
                  .Append(" sprite=").Append(bar.Frame.sprite != null ? bar.Frame.sprite.name : "null");
            else
                sb.Append(" | frame null");

            Image fill = bar.Fast != null && bar.Fast.m_bar != null
                ? bar.Fast.m_bar.GetComponent<Image>() : null;
            if (fill != null)
                sb.Append(" | fill enabled=").Append(fill.enabled)
                  .Append(" a=").Append(fill.color.a)
                  .Append(" w=").Append(((RectTransform)fill.transform).rect.width);
            else
                sb.Append(" | fill null");

            if (bar.Text != null)
                sb.Append(" | text=\"").Append(bar.Text.text).Append("\" a=").Append(bar.Text.color.a);

            Log.LogInfo(sb.ToString());
        }

        private static void LogPrefab(string label, GameObject prefab)
        {
            if (prefab == null)
                Log.LogInfo("[dump] " + label + " is null");
            else
                Tree(prefab.transform, label);
        }

        private static void TooltipPrefabs()
        {
            int shown = 0;
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (shown >= 5)
                    break;
                if (go.scene.IsValid())
                    continue;
                if (go.name != "Tooltip" && go.name != "InventoryTooltip")
                    continue;
                shown++;
                Tree(go.transform, "prefab asset " + go.name);
            }
        }

        private static void SpriteCensus()
        {
            int lines = 0;
            foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (lines >= 400)
                {
                    Log.LogInfo("[dump] ... sprite census truncated");
                    break;
                }
                string lower = sprite.name.ToLowerInvariant();
                bool match = false;
                foreach (string needle in SpriteNeedles)
                {
                    if (!lower.Contains(needle))
                        continue;
                    match = true;
                    break;
                }
                if (!match)
                    continue;

                lines++;
                Log.LogInfo("[dump] sprite " + sprite.name + " rect=" + sprite.rect.width + "x" + sprite.rect.height
                    + " border=" + sprite.border + " ppu=" + sprite.pixelsPerUnit
                    + " tex=" + (sprite.texture != null ? sprite.texture.name : "none"));
            }
        }

        private static void Canvases()
        {
            foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (!canvas.gameObject.scene.IsValid())
                    continue;
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                Log.LogInfo("[dump] canvas " + FullPath(canvas.transform) + " mode=" + canvas.renderMode
                    + " sort=" + canvas.sortingOrder + " scaleFactor=" + canvas.scaleFactor
                    + " ppu=" + (scaler != null ? scaler.referencePixelsPerUnit.ToString() : "n/a")
                    + " refRes=" + (scaler != null ? scaler.referenceResolution.ToString() : "n/a"));
            }
        }

        private static string FullPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            Transform p = t.parent;
            while (p != null)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }

        private static int s_treeLines;

        private static void Tree(Transform root, string label)
        {
            if (root == null)
            {
                Log.LogInfo("[dump] " + label + " is null");
                return;
            }
            Log.LogInfo("[dump] --- tree: " + label + " ---");
            s_treeLines = 0;
            Walk(root, 0);
        }

        private static void Walk(Transform t, int depth)
        {
            if (s_treeLines >= 2500)
            {
                if (s_treeLines == 2500)
                    Log.LogInfo("[dump] ... truncated");
                s_treeLines++;
                return;
            }
            s_treeLines++;

            string indent = new string(' ', depth * 2);
            var rt = t as RectTransform;
            string line = "[dump] " + indent + t.name + (t.gameObject.activeSelf ? "" : " (inactive)");
            if (rt != null)
                line += " rect=" + rt.sizeDelta.x + "x" + rt.sizeDelta.y + " pos=" + rt.anchoredPosition
                    + " anchors=" + rt.anchorMin + "-" + rt.anchorMax + " pivot=" + rt.pivot
                    + " scale=" + rt.localScale.x;
            Log.LogInfo(line);

            string compIndent = indent + "  ";
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null)
                    continue;

                if (c is Image img)
                {
                    Log.LogInfo("[dump] " + compIndent + "img sprite=" + (img.sprite != null ? img.sprite.name : "none")
                        + " type=" + img.type + " color=#" + ColorUtility.ToHtmlStringRGBA(img.color)
                        + " ppuMul=" + img.pixelsPerUnitMultiplier + " fillCenter=" + img.fillCenter
                        + " raycast=" + img.raycastTarget);
                }
                else if (c is RawImage raw)
                {
                    Log.LogInfo("[dump] " + compIndent + "raw tex=" + (raw.texture != null ? raw.texture.name : "none"));
                }
                else if (c is TMP_Text tmp)
                {
                    string text = tmp.text ?? "";
                    text = text.Replace("\n", " ").Replace("\r", " ");
                    if (text.Length > 24)
                        text = text.Substring(0, 24);
                    Log.LogInfo("[dump] " + compIndent + "tmp font=" + (tmp.font != null ? tmp.font.name : "none")
                        + " size=" + tmp.fontSize + " color=#" + ColorUtility.ToHtmlStringRGBA(tmp.color)
                        + " text=\"" + text + "\"");
                }
                else if (c is Selectable sel)
                {
                    SpriteState state = sel.spriteState;
                    ColorBlock colors = sel.colors;
                    Log.LogInfo("[dump] " + compIndent + "sel " + sel.GetType().Name + " transition=" + sel.transition
                        + " sprites hl=" + (state.highlightedSprite != null ? state.highlightedSprite.name : "none")
                        + " pr=" + (state.pressedSprite != null ? state.pressedSprite.name : "none")
                        + " dis=" + (state.disabledSprite != null ? state.disabledSprite.name : "none")
                        + " colors n=#" + ColorUtility.ToHtmlStringRGBA(colors.normalColor)
                        + " hl=#" + ColorUtility.ToHtmlStringRGBA(colors.highlightedColor));
                }
                else if (c is Canvas canvas)
                {
                    Log.LogInfo("[dump] " + compIndent + "canvas mode=" + canvas.renderMode + " sort=" + canvas.sortingOrder
                        + " override=" + canvas.overrideSorting);
                }
                else if (c is CanvasScaler scaler)
                {
                    Canvas ownCanvas = scaler.GetComponent<Canvas>();
                    Log.LogInfo("[dump] " + compIndent + "scaler mode=" + scaler.uiScaleMode + " ref=" + scaler.referenceResolution
                        + " match=" + scaler.matchWidthOrHeight + " ppu=" + scaler.referencePixelsPerUnit
                        + " scale=" + (ownCanvas != null ? ownCanvas.scaleFactor.ToString() : "n/a"));
                }
                else if (c is LayoutGroup || c is ContentSizeFitter || c is GridLayoutGroup)
                {
                    Log.LogInfo("[dump] " + compIndent + c.GetType().Name);
                }
                else if (c is MonoBehaviour && !(c is Transform) && !(c is RectTransform) && !(c is CanvasRenderer))
                {
                    Log.LogInfo("[dump] " + compIndent + c.GetType().Name);
                }
            }

            foreach (Transform child in t)
                Walk(child, depth + 1);
        }
    }
}
