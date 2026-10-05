using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Gold art laid into an empty part of the inventory: the longship in the player panel. The
    /// serpent that sat in the recipe pane is gone - Cartur's crafting board paints its own dragon
    /// (removed at his request, 2026-10-03).
    ///
    /// The ship hangs off InventoryGui.m_player. Being a child, it is carried along by the panel
    /// being dragged, and nothing here has to follow anything.
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

        // Settled by eye in the game, then written down. These were config entries while the
        // position was being found; they are not any more, because the position is found.
        internal static void Init()
        {
            s_ship = new Inlay("ship.png", Corner.BottomRight,
                scale: 0.6f, x: 14f, y: 0f, opacity: 0.1f);
        }

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        [HarmonyPostfix]
        private static void Build(InventoryGui __instance)
        {
            s_ship?.Build(__instance.m_player, "player panel");
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
        }

        /// <summary>
        /// The ship rides the bag board's bottom-right corner, inside the rails (Cartur, 2026-10-04:
        /// "anchor to ... the bottom row of the growing inventory panel"). Kept in the board's own
        /// units, so rows coming or going move the corner and the ship with it, never its size.
        /// </summary>
        internal static void ShipOnBoard(RectTransform board)
        {
            Image ship = s_ship?.First;
            if (ship == null || board == null || board.lossyScale.x <= 0f)
                return;
            var rt = ship.rectTransform;
            var b = new Vector3[4];
            board.GetWorldCorners(b);
            float k = board.lossyScale.x;     // world per board unit
            if (!s_shipPinned)
            {
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                // Its size from where the panel rule put it; its place is inside the board's corner,
                // on the wood, clear of the rails - kept from the old rule it hung half below the
                // bottom rail (Cartur, 2026-10-04). Rails are 62 px of the board's 1670 across.
                float rail = (b[2].x - b[0].x) / k * 62f / 1670f;
                s_shipRight = rail * 1.4f;
                s_shipBottom = rail * 1.4f;
                s_shipSize = new Vector2(c[2].x - c[0].x, c[2].y - c[0].y) / k;
                s_shipPinned = true;
                s_ship.Pinned = true;
                Log.LogInfo("ship pinned to the bag board's corner: " + Mathf.Round(s_shipSize.x) + "x" + Mathf.Round(s_shipSize.y)
                            + ", " + Mathf.Round(s_shipRight) + " in from the right, " + Mathf.Round(s_shipBottom) + " up");
            }
            Vector3 p = rt.lossyScale;
            if (p.x <= 0f || p.y <= 0f)
                return;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(s_shipSize.x * k / p.x, s_shipSize.y * k / p.y);
            rt.position = new Vector3(b[2].x - s_shipRight * k, b[0].y + s_shipBottom * k, rt.position.z);
        }

        /// <summary>
        /// The ship behind the icons but over the slots (Cartur, 2026-10-05). The panel-level image
        /// sits under PlayerGrid, so it shows in the gaps; the slot backgrounds are opaque and hid
        /// the rest. One image cannot sit above 24 slot backgrounds and below 24 icons, so each slot
        /// the ship crosses gets its own slice of it, inserted just before that slot's m_icon and
        /// sized to the slot's background (m_button.targetGraphic) - the slices line up with the
        /// gap ship, and nothing is drawn twice. No canvases and no raycast targets, so clicks,
        /// drags and tooltips are untouched. Re-done per frame from the grid's own UpdateGui because
        /// the grid rebuilds its elements whenever the bag changes size.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        [HarmonyPostfix]
        private static void SlicesUnderIcons(InventoryGrid __instance, List<InventoryElement> ___m_elements)
        {
            if (InventoryGui.instance == null || __instance != InventoryGui.instance.m_playerGrid || ___m_elements == null)
                return;
            Image ship = s_ship?.First;
            if (ship == null || ship.sprite == null || !ship.isActiveAndEnabled)
                return;

            // The ship as drawn: preserveAspect fits the texture, centred, inside its rect.
            ship.rectTransform.GetWorldCorners(s_corners);
            float w = s_corners[2].x - s_corners[0].x, h = s_corners[2].y - s_corners[0].y;
            float aspect = ship.sprite.rect.width / ship.sprite.rect.height;
            float dw = Mathf.Min(w, h * aspect), dh = dw / aspect;
            if (dw <= 0f || dh <= 0f)
                return;
            var drawn = new Rect(s_corners[0].x + (w - dw) * 0.5f, s_corners[0].y + (h - dh) * 0.5f, dw, dh);

            foreach (InventoryElement e in ___m_elements)
            {
                if (e == null || e.m_icon == null)
                    continue;
                Graphic slot = e.m_button != null ? e.m_button.targetGraphic : null;
                RectTransform under = slot != null ? slot.rectTransform : (RectTransform)e.transform;
                under.GetWorldCorners(s_corners);
                var r = Rect.MinMaxRect(s_corners[0].x, s_corners[0].y, s_corners[2].x, s_corners[2].y);

                Transform t = e.m_icon.transform.parent.Find(SliceName);
                RawImage slice = t != null ? t.GetComponent<RawImage>() : null;
                if (!r.Overlaps(drawn))
                {
                    if (slice != null) slice.enabled = false;
                    continue;
                }
                if (slice == null)
                {
                    var go = new GameObject(SliceName, typeof(RectTransform), typeof(RawImage));
                    go.transform.SetParent(e.m_icon.transform.parent, false);
                    go.transform.SetSiblingIndex(e.m_icon.transform.GetSiblingIndex());
                    slice = go.GetComponent<RawImage>();
                    slice.raycastTarget = false;
                    slice.texture = ship.sprite.texture;
                    ship.sprite.texture.wrapMode = TextureWrapMode.Clamp;
                }
                slice.enabled = true;
                slice.color = ship.color;
                var srt = slice.rectTransform;
                srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
                Vector3 k = srt.parent.lossyScale;
                if (k.x <= 0f || k.y <= 0f)
                    continue;
                srt.sizeDelta = new Vector2(r.width / k.x, r.height / k.y);
                srt.position = new Vector3(r.center.x, r.center.y, srt.position.z);
                slice.uvRect = new Rect((r.x - drawn.x) / drawn.width, (r.y - drawn.y) / drawn.height,
                                        r.width / drawn.width, r.height / drawn.height);
            }
        }

        private const string SliceName = "CarturUIHud_InlaySlice";
        private static readonly Vector3[] s_corners = new Vector3[4];

        private static bool s_shipPinned;
        private static float s_shipRight, s_shipBottom;
        private static Vector2 s_shipSize;

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

            /// <summary>Placed by someone else now (ShipOnBoard); the panel rule leaves it be.</summary>
            internal bool Pinned;

            internal Image First => _images.Count > 0 ? _images[0] : null;

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

                // Just in front of the item grid: over the panel's own background ("Bkg", "sunken"),
                // under the icons (Cartur, 2026-10-05: render behind the icons). Logged order of
                // the player panel: ..., Bkg, sunken, help_Text, PlayerGrid, TooltipAnchor,
                // Container. First sibling showed nothing - the full-size background drew over it;
                // last sibling drew it over the icons. A panel with no PlayerGrid keeps last.
                Transform grid = panel.Find("PlayerGrid");
                if (grid != null)
                    rt.SetSiblingIndex(grid.GetSiblingIndex());
                else
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
                if (Pinned)
                    return;

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
