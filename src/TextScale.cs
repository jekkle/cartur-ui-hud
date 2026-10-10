using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace CarturUIHud
{
    /// <summary>
    /// Panels / textScale: the text in the inventory, crafting, chest and trader windows, bigger or
    /// smaller (crysis2142, Nexus 2026-10-09: "too small to read for big monitors").
    ///
    /// Font sizes are set in some fifty places across the boards, and rows such as the recipe list
    /// are built after the window opens, so scaling at each call site would miss some and drift. A
    /// watcher on each window's root instead scales every text under it from its own size, twice a
    /// second while the window is open. A size it did not write itself - the game or a board
    /// setting it again - is taken as the new original. Auto-sized text scales its maximum.
    /// The HUD is left out: its numbers sit in fixed-size bars and diamonds.
    /// </summary>
    internal static class TextScale
    {
        internal static ConfigEntry<float> Scale;

        internal static void Init(ConfigFile config)
        {
            Scale = config.Bind("Panels", "textScale", 1f,
                new ConfigDescription("Size of the text in the inventory, crafting, chest and trader windows. 1 is the normal size; raise it on a big monitor. Applies straight away.",
                    new AcceptableValueRange<float>(0.8f, 1.6f)));
        }

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        [HarmonyPostfix]
        private static void WatchInventory(InventoryGui __instance) => Watch(__instance.gameObject);

        [HarmonyPatch(typeof(StoreGui), "Awake")]
        [HarmonyPostfix]
        private static void WatchStore(StoreGui __instance) => Watch(__instance.gameObject);

        private static void Watch(GameObject root)
        {
            if (root != null && Scale != null && root.GetComponent<Watcher>() == null)
                root.AddComponent<Watcher>();
        }

        private sealed class Watcher : MonoBehaviour
        {
            // Per text: the size it had before scaling, and the size this last wrote.
            private readonly Dictionary<TMP_Text, (float own, float wrote)> _sizes =
                new Dictionary<TMP_Text, (float, float)>();
            private readonly List<TMP_Text> _found = new List<TMP_Text>();
            private float _next;

            private void Update()
            {
                if (Time.unscaledTime < _next)
                    return;
                _next = Time.unscaledTime + 0.5f;

                float scale = Scale.Value;
                _found.Clear();
                GetComponentsInChildren(false, _found);
                foreach (TMP_Text t in _found)
                {
                    float now = t.enableAutoSizing ? t.fontSizeMax : t.fontSize;
                    if (!_sizes.TryGetValue(t, out var s) || !Mathf.Approximately(now, s.wrote))
                        s = (now, now);                       // new text, or resized by someone else
                    float want = s.own * scale;
                    if (!Mathf.Approximately(now, want))
                    {
                        if (t.enableAutoSizing) t.fontSizeMax = want;
                        else t.fontSize = want;
                    }
                    _sizes[t] = (s.own, want);
                }
                // Rows are rebuilt as lists refresh; forget the destroyed ones now and then.
                if (_sizes.Count > _found.Count + 256)
                {
                    var gone = new List<TMP_Text>();
                    foreach (TMP_Text t in _sizes.Keys)
                        if (t == null) gone.Add(t);
                    foreach (TMP_Text t in gone)
                        _sizes.Remove(t);
                }
            }
        }
    }
}
