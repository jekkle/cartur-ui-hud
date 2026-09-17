using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// The hotbar. HotkeyBar rebuilds its slots from m_elementPrefab whenever the bound item
    /// count changes and places them by localPosition, so the prefab is skinned once and the
    /// bar as a whole is registered movable and scalable - localScale is safe here because
    /// nothing inside is positioned by the game in canvas units.
    /// </summary>
    internal static class Hotbar
    {
        internal const string Owner = "hotbar";

        internal static BepInEx.Logging.ManualLogSource Log;

        [HarmonyPatch(typeof(Hud), "Awake")]
        [HarmonyPostfix]
        private static void Awake(Hud __instance)
        {
            Transform bar = __instance.transform.Find("hudroot/HotKeyBar");
            if (bar == null)
            {
                Log.LogWarning("hudroot/HotKeyBar not found - hotbar left vanilla");
                return;
            }

            Canvas canvas = __instance.GetComponentInParent<Canvas>();
            float ppu = canvas != null ? canvas.referencePixelsPerUnit : 100f;

            Skin.Apply(bar, "hotbar", ppu);
            HotkeyBar hotkeys = bar.GetComponent<HotkeyBar>();
            Skin.Apply(hotkeys?.m_elementPrefab?.transform, "hotbar element prefab", ppu);

            HudLayout.Reset(Owner, null);
            var rt = (RectTransform)bar;
            HudLayout.Register(Owner, "hotbar", "Hotbar", rt, rt, rt.anchoredPosition);
        }
    }
}
