using System;
using System.IO;
using System.Text;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Diagnostic only, on a key. Writes the bar subtrees exactly as they are after the skin
    /// has been applied, so a wrong rect or a sprite that did not take can be read off rather
    /// than guessed at from a screenshot.
    /// </summary>
    internal static class HudDump
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        public static void Write(Hud hud)
        {
            string path = Path.Combine(Paths.BepInExRootPath, "CarturUIHud_bars.txt");
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("referencePixelsPerUnit " + (hud.GetComponentInParent<Canvas>()?.referencePixelsPerUnit));
                Sprite frame = AssetLoader.BarFrame;
                sb.AppendLine("BarFrame sprite: " + (frame == null ? "<null>" : $"rect {frame.rect} border {frame.border} ppu {frame.pixelsPerUnit}"));
                Sprite fill = AssetLoader.BarFill;
                sb.AppendLine("BarFill sprite: " + (fill == null ? "<null>" : $"rect {fill.rect} border {fill.border} ppu {fill.pixelsPerUnit}"));

                Section(sb, "HEALTH", hud.m_healthBarRoot);
                Section(sb, "STAMINA", hud.m_staminaBar2Root);
                Section(sb, "EITR", hud.m_eitrBarRoot);
                Section(sb, "ADRENALINE", hud.m_adrenalineBarRoot);
                Section(sb, "POWER", hud.m_gpRoot);

                File.WriteAllText(path, sb.ToString());
                Log.LogInfo("bar dump written: " + path);
            }
            catch (Exception e)
            {
                Log.LogWarning("bar dump failed: " + e);
            }
        }

        private static void Section(StringBuilder sb, string title, RectTransform root)
        {
            sb.AppendLine().AppendLine("=== " + title + " ===");
            if (root == null)
            {
                sb.AppendLine("<null>");
                return;
            }
            // Walk up too - a wrong scale or rect on an ancestor shows here and nowhere else.
            for (Transform p = root.parent; p != null; p = p.parent)
                sb.AppendLine("  parent: " + Describe(p));
            Walk(sb, root, 0);
        }

        private static void Walk(StringBuilder sb, Transform t, int depth)
        {
            sb.Append(new string(' ', depth * 2)).AppendLine(Describe(t));

            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform)
                    continue;
                sb.Append(new string(' ', depth * 2 + 4)).Append(c.GetType().Name);

                if (c is Image img)
                {
                    Sprite sp = img.sprite;
                    sb.Append(" sprite=").Append(sp == null ? "<null>" : sp.name)
                      .Append(sp == null ? "" : $" spriteRect={sp.rect} border={sp.border} ppu={sp.pixelsPerUnit}")
                      .Append(" type=").Append(img.type)
                      .Append(" fillCenter=").Append(img.fillCenter)
                      .Append(" preserveAspect=").Append(img.preserveAspect)
                      .Append(" ppuMult=").Append(img.pixelsPerUnitMultiplier)
                      .Append(" enabled=").Append(img.enabled)
                      .Append(" color=").Append(ColorUtility.ToHtmlStringRGBA(img.color));
                }
                else if (c is GuiBar bar)
                {
                    sb.Append(" m_bar=").Append(bar.m_bar == null ? "<null>" : bar.m_bar.name);
                }
                else if (c is TMP_Text txt)
                {
                    sb.Append(" text=\"").Append(txt.text).Append("\"");
                }
                else if (c is Animator anim)
                {
                    // An Animator writing a rotation or scale curve onto these children would
                    // not show up anywhere else in this dump.
                    sb.Append(" enabled=").Append(anim.enabled)
                      .Append(" controller=").Append(anim.runtimeAnimatorController == null ? "<null>" : anim.runtimeAnimatorController.name)
                      .Append(" cullingMode=").Append(anim.cullingMode)
                      .Append(" applyRootMotion=").Append(anim.applyRootMotion);
                }

                sb.AppendLine();
            }

            for (int i = 0; i < t.childCount; i++)
                Walk(sb, t.GetChild(i), depth + 1);
        }

        private static string Describe(Transform t)
        {
            string s = t.name + (t.gameObject.activeInHierarchy ? " [active]" : " [INACTIVE]");
            if (t is RectTransform rt)
                s += $" pos={rt.anchoredPosition:F1} size={rt.sizeDelta:F1} rect={rt.rect.size:F1}"
                   + $" anchor={rt.anchorMin:F2}-{rt.anchorMax:F2} pivot={rt.pivot:F2} scale={rt.localScale:F2}"
                   + $" euler={rt.localEulerAngles:F1} lossyScale={rt.lossyScale:F2}";
            return s;
        }
    }
}
