using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Two pieces of gold art laid into the empty parts of the inventory: the longship in the
    /// player panel and the serpent in the item details panel, the one every crafting station
    /// shares.
    ///
    /// The ship hangs off InventoryGui.m_player. The serpent hangs off the recipe pane, and off
    /// the styles tab's own pane when that one is standing in for it. Being children, they are
    /// carried along by a station opening or by the panel being dragged, and nothing here has to
    /// follow anything.
    ///
    /// Size is a fraction of the panel's own height rather than a pixel count. The player panel
    /// grows with the number of inventory rows, so a fixed size would drift out of its corner;
    /// the fraction is resolved against the measured rect each time the screen opens, and the
    /// pixel size it came to is logged rather than assumed.
    ///
    /// Drawn last in the panel and with raycastTarget off. Behind was tried first and showed
    /// nothing: a UI panel's own background is a child, so a first sibling is under it, not
    /// under the slots. With no raycast, drawing on top takes no click away from anything.
    /// </summary>
    internal static class Inlays
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        /// <summary>Which corner of the panel the art is pinned to.</summary>
        internal enum Corner
        {
            BottomLeft,
            BottomRight,
            TopLeft,
            TopRight,
            Center,
        }

        private const string FolderName = "inlays";

        private static Inlay s_ship;
        private static Inlay s_serpent;

        // Settled by eye in the game, then written down. These were config entries while the
        // position was being found; they are not any more, because the position is found.
        internal static void Init()
        {
            s_ship = new Inlay("ship.png", Corner.BottomRight,
                scale: 0.6f, x: 14f, y: 0f, opacity: 0.1f);
            s_serpent = new Inlay("serpent.png", Corner.Center,
                scale: 0.8f, x: 0f, y: 30f, opacity: 0.2f);
        }

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        [HarmonyPostfix]
        private static void Build(InventoryGui __instance)
        {
            s_ship?.Build(__instance.m_player, "player panel");
            s_serpent?.Build(Details(__instance), "recipe pane");
        }

        /// <summary>
        /// The pane the selected recipe is written into. It is not m_info: that measured
        /// 570x130 at runtime, which is the strip above the crafting list, not the tall pane the
        /// description fills. So it is found from the text the game itself writes there -
        /// m_recipeDecription - by walking up to the last parent before m_crafting, which is the
        /// pane as a whole rather than the text's own layout row.
        /// </summary>
        private static RectTransform Details(InventoryGui gui)
        {
            if (gui.m_recipeDecription == null || gui.m_crafting == null)
                return null;

            RectTransform last = null;
            for (Transform t = gui.m_recipeDecription.transform; t != null; t = t.parent)
            {
                if (t == gui.m_crafting)
                    break;
                if (t is RectTransform rt)
                    last = rt;
            }
            return last;
        }

        // The panel is only the size it is going to be once the screen is up: the player panel
        // is resized for the row count and the recipe pane is laid out by its own content. So
        // the rect is read here, every time it opens, rather than once at Awake off a rect that
        // is still the prefab's.
        [HarmonyPatch(typeof(InventoryGui), "Show")]
        [HarmonyPostfix]
        private static void Resize()
        {
            s_ship?.Apply();
            s_serpent?.Apply();
        }

        /// <summary>
        /// The serpent again, on the styles pane. That pane stands in for the recipe pane, and
        /// is destroyed and rebuilt every time the selection changes, so it asks for the art
        /// each time rather than this having to watch for it.
        /// </summary>
        internal static void Decorate(RectTransform stylePane)
        {
            if (stylePane == null || s_serpent == null || s_serpent.Has(stylePane))
                return;
            s_serpent.Build(stylePane, "styles pane");
        }

        /// <summary>One piece of art, on every panel that asked for it.</summary>
        private sealed class Inlay
        {
            private readonly string _file;
            private readonly Corner _corner;
            private readonly float _scale;
            private readonly float _x;
            private readonly float _y;
            private readonly float _opacity;

            // One inlay, more than one host. The styles tab builds its own pane beside the
            // recipe pane and throws it away again on every rebuild, so the art is put on each
            // pane that wants it and the destroyed ones are dropped on the next pass.
            private readonly List<Image> _images = new List<Image>();
            private readonly HashSet<string> _logged = new HashSet<string>();
            private Texture2D _tex;
            private float _lastLogged;

            /// <param name="scale">Height as a fraction of the panel's height.</param>
            /// <param name="x">Inset from the pinned corner, sideways, in pixels.</param>
            /// <param name="y">Inset from the pinned corner, up or down, in pixels.</param>
            internal Inlay(string file, Corner corner, float scale, float x, float y, float opacity)
            {
                _file = file;
                _corner = corner;
                _scale = scale;
                _x = x;
                _y = y;
                _opacity = opacity;
            }

            internal void Build(RectTransform panel, string where)
            {
                if (panel == null)
                {
                    Log.LogWarning("no panel for " + _file + " - inlay skipped");
                    return;
                }

                if (_tex == null)
                {
                    _tex = AssetLoader.LoadFile(
                        System.IO.Path.Combine(AssetLoader.AssetsDir ?? "", FolderName, _file), Log);
                    if (_tex == null)
                        return;      // Read already said which file was missing
                }

                var go = new GameObject("CarturUIHud_Inlay_" + _file,
                    typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(panel, false);

                // Last sibling, not first. First put it behind the panel's own full-size
                // background child, which drew over it and showed nothing at all. It carries no
                // raycast, so drawing on top costs nothing: a click still reaches the slot
                // underneath.
                rt.SetAsLastSibling();

                var image = go.GetComponent<Image>();
                image.sprite = Sprite.Create(_tex, new Rect(0, 0, _tex.width, _tex.height),
                    new Vector2(0.5f, 0.5f));
                image.raycastTarget = false;
                image.preserveAspect = true;
                _images.Add(image);

                _lastLogged = 0f;
                Apply();

                if (!_logged.Add(where))
                    return;          // the styles pane is rebuilt constantly - said once
                // The sibling list, once. If it still cannot be seen, this says what is drawn
                // after it and therefore what is on top of it - which is the one thing a
                // screenshot of an empty corner cannot tell us.
                var order = new System.Text.StringBuilder();
                foreach (Transform child in panel)
                    order.Append(order.Length > 0 ? ", " : "").Append(child.name);
                Log.LogInfo(_file + " laid into the " + where + " ("
                    + _tex.width + "x" + _tex.height + " source), drawn over: " + order);
            }

            /// <summary>Whether this inlay is already on that panel, so it is not doubled.</summary>
            internal bool Has(Transform panel)
            {
                foreach (Image image in _images)
                    if (image != null && image.transform.parent == panel)
                        return true;
                return false;
            }

            internal void Apply()
            {
                if (_tex == null)
                    return;

                for (int i = _images.Count - 1; i >= 0; i--)
                {
                    if (_images[i] == null)
                        _images.RemoveAt(i);     // its pane was thrown away
                    else
                        Apply(_images[i]);
                }
            }

            private void Apply(Image image)
            {
                var rt = (RectTransform)image.transform;

                image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(_opacity));

                var panel = rt.parent as RectTransform;
                if (panel == null)
                    return;

                float height = panel.rect.height * Mathf.Max(0f, _scale);
                float width = _tex.height > 0 ? height * _tex.width / _tex.height : height;
                rt.sizeDelta = new Vector2(width, height);

                Vector2 a = Pin(_corner);
                rt.anchorMin = a;
                rt.anchorMax = a;
                rt.pivot = a;

                // x and y are an inset from whichever corner was named, so the same pair of
                // numbers means the same gap on the right as on the left.
                rt.anchoredPosition = new Vector2(
                    a.x > 0.5f ? -_x : _x,
                    a.y > 0.5f ? -_y : _y);

                // Said once per size, not once per open: this is the measurement that says
                // whether the scale in the config is the scale that was wanted.
                if (!Mathf.Approximately(height, _lastLogged))
                {
                    _lastLogged = height;
                    Log.LogInfo(_file + " in " + panel.name + ": " + Mathf.Round(width) + "x"
                        + Mathf.Round(height) + "px, panel is " + Mathf.Round(panel.rect.width)
                        + "x" + Mathf.Round(panel.rect.height) + "px");
                }
            }

            private static Vector2 Pin(Corner c)
            {
                switch (c)
                {
                    case Corner.BottomLeft: return new Vector2(0f, 0f);
                    case Corner.BottomRight: return new Vector2(1f, 0f);
                    case Corner.TopLeft: return new Vector2(0f, 1f);
                    case Corner.TopRight: return new Vector2(1f, 1f);
                    default: return new Vector2(0.5f, 0.5f);
                }
            }
        }
    }
}
