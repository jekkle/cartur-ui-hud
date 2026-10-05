using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Every window frame Skin turns into the old kit panel that nothing else has put on Cartur's
    /// art - the pause menu, skills, trophies, the split and confirm dialogs, popups, the menu's
    /// smaller panels - takes his small-border window board instead (board_window.png, 2026-10-04:
    /// "use this for the trophies skills achievements etc, less ravens, small border").
    ///
    /// Windows come in every shape, so the board is 9-sliced: board_window_base.png is the same
    /// art with its medallion, diamond and rune strips painted over by plain rail
    /// (tools/art/window_board.py), so the rails stretch and nothing else does. Those four pieces
    /// are laid back on at their own size, cut from board_window.png - medallion on the top rail,
    /// diamond on the bottom, runes mid-way down each side. The game's own controls draw on the
    /// wood. The old plate stays, clear, so it still blocks clicks to whatever is behind it.
    ///
    /// Stands down where a fitted board already covers the window (world select, character
    /// select and the rest): there the old plate is a leftover and only goes clear. Skips what is
    /// not a window - a full-screen blocker, a long thin bar.
    /// </summary>
    internal sealed class AutoBoard : MonoBehaviour
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        private const string BoardName = "CarturUI_Board_auto";

        // board_window.png (982x1635) px: where its rails end, and its four ornaments. Read off
        // zoomed crops of the art.
        private static readonly Vector4 BorderPx = new Vector4(66, 104, 66, 100);   // left, bottom, right, top
        private static readonly Rect MedalPx = Rect.MinMaxRect(415, 0, 567, 150);
        private static readonly Rect DiamondPx = Rect.MinMaxRect(415, 1480, 567, 1635);
        private static readonly Rect RunesLeftPx = Rect.MinMaxRect(0, 545, 66, 1100);
        private static readonly Rect RunesRightPx = Rect.MinMaxRect(916, 545, 982, 1100);
        // Art px per canvas unit: the 66 px rail draws 13 units wide on any window.
        private const float PxPerUnit = 5f;
        // How far the rails stand outside the window's own rect, in units.
        private const float Proud = 8f;

        // A bar's shape is past any board's: evenly scaled, the frame would overhang it by more
        // than the bar itself. Those keep the thin rule.
        private const float MaxAspect = 4f;

        // Below this on its short side a frame is a box, not a window - the container's weight
        // box is 80x60, the trader's sell strip 160x80. They keep the thin rule.
        private const float MinSide = 150f;

        private Image _plate;
        private Image _board;
        private Vector2 _size;
        private int _next;
        private bool _covered, _keptLogged;

        internal static void Attach(Image plate)
        {
            if (plate.GetComponent<AutoBoard>() != null)
                return;
            var ab = plate.gameObject.AddComponent<AutoBoard>();
            ab._plate = plate;
            // Hidden from the start, so the old kit frame never draws while the board is pending
            // (Cartur, 2026-10-04: "older assets loading then new ones popping in after"). Frames
            // that do not get a board are given their colour back in Fit.
            ab._restColor = plate.color;
            plate.color = new Color(plate.color.r, plate.color.g, plate.color.b, 0f);
        }

        private Color _restColor;
        private bool _wasEnabled = true;

        /// <summary>A frame that keeps the old art (a bar, a small box) shows it again.</summary>
        private void Keep()
        {
            Show(false);
            if (_plate.color.a == 0f && _restColor.a > 0f)
                _plate.color = _restColor;
        }

        private void LateUpdate()
        {
            if (_plate == null)
                return;
            Vector2 size = _plate.rectTransform.rect.size;
            // Re-checked every half second as well as on resize: a hand-fitted board can arrive
            // after this one, the first frame its screen is up.
            if (size == _size && Time.frameCount < _next && _plate.enabled == _wasEnabled)
                return;
            _wasEnabled = _plate.enabled;
            _size = size;
            _next = Time.frameCount + 30;
            Fit();
        }

        private void Fit()
        {
            // A hand-fitted board (world select, server list, new world) switches the frame off when
            // it goes on. This board may have been placed a frame earlier; it stands down then, or
            // it covers that board's painted plates and the buttons read as bare text (pilot and
            // Cartur, 2026-10-04).
            if (!_plate.enabled)
            {
                Show(false);
                return;
            }
            if (_size.x <= 1f || _size.y <= 1f)
                return;      // not laid out yet: stays hidden until it is
            float aspect = _size.x / _size.y;
            Canvas canvas = _plate.canvas;
            var root = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            Rect mine = WorldRect(_plate.rectTransform);
            // The root canvas's rect is the screen, except where a mod's canvas reports nonsense:
            // Jotunn's CustomGUIFront measured -50x-50 (pilot, 2026-10-05), so every window on it
            // counted as full screen. A rect with no size falls back to the screen itself.
            Rect screen = root != null ? WorldRect(root) : default;
            if (screen.width <= 1f || screen.height <= 1f)
                screen = new Rect(0f, 0f, Screen.width, Screen.height);
            bool fullScreen = mine.width >= screen.width * 0.9f && mine.height >= screen.height * 0.9f;
            if (fullScreen || aspect > MaxAspect || aspect < 1f / MaxAspect || Mathf.Min(_size.x, _size.y) < MinSide)
            {
                // Once per frame object: the inputs that kept it on the old art. Jotunn's failed-
                // connection window (1000x600) was kept with no word as to why (pilot, 2026-10-05).
                if (!_keptLogged)
                {
                    _keptLogged = true;
                    Log.LogInfo($"window kept on old art: {Path(transform)} {_size.x:0}x{_size.y:0} aspect {aspect:0.##}"
                        + $" fullScreen {fullScreen} (mine {mine.width:0}x{mine.height:0}, root "
                        + $"{screen.width:0}x{screen.height:0}" + (root != null ? $" '{root.name}'" : "") + ")");
                }
                Keep();
                return;
            }

            if (UnderFittedBoard(transform.parent as RectTransform, mine))
            {
                Show(false);
                Clear();
                if (!_covered)
                {
                    _covered = true;
                    Log.LogInfo("old plate under a fitted board, cleared: " + Path(transform));
                }
                return;
            }

            Texture2D art = AssetLoader.Board("window"), plain = AssetLoader.Board("window_base");
            if (art == null || plain == null)
            {
                Keep();
                return;
            }
            if (_board == null)
            {
                _board = Piece(transform, BoardName, Sprite.Create(plain, new Rect(0, 0, plain.width, plain.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, BorderPx));
                _board.type = Image.Type.Sliced;
                // A slice is border px / sprite ppu * canvas ppu / multiplier units wide.
                Canvas cv = _plate.canvas;
                _board.pixelsPerUnitMultiplier = PxPerUnit * (cv != null ? cv.referencePixelsPerUnit : 100f) / 100f;
                var brt = _board.rectTransform;
                brt.anchorMin = Vector2.zero;
                brt.anchorMax = Vector2.one;
                brt.offsetMin = new Vector2(-Proud, -Proud);
                brt.offsetMax = new Vector2(Proud, Proud);
                Ornament(art, MedalPx, new Vector2(0.5f, 1f));
                Ornament(art, DiamondPx, new Vector2(0.5f, 0f));
                if (_size.y * 0.8f > RunesLeftPx.height / PxPerUnit)
                {
                    Ornament(art, RunesLeftPx, new Vector2(0f, 0.5f));
                    Ornament(art, RunesRightPx, new Vector2(1f, 0.5f));
                }
                Log.LogInfo("window board on " + Path(transform) + " (" + _size.x.ToString("0") + "x" + _size.y.ToString("0") + ")");
            }
            Show(true);
            Clear();
            Plates();
        }

        /// <summary>
        /// The window's own buttons on the slot-rim plate the chest buttons use, so every dialog's
        /// buttons match (Cartur, 2026-10-04: the split stack buttons "need work"). Only the
        /// generic kit buttons - a button already on a painted board is left alone.
        /// </summary>
        private void Plates()
        {
            Sprite plate = GridBoard.Plate();
            if (plate == null || transform.parent == null)
                return;
            foreach (Button b in transform.parent.GetComponentsInChildren<Button>(true))
            {
                var img = b.GetComponent<Image>();
                if (img == null || img.sprite == plate || img.sprite == null || !img.sprite.name.StartsWith("cartur_button"))
                    continue;
                img.sprite = plate;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = GridBoard.CellBorderPx / 8f;
                img.color = Color.white;
                img.material = null;
                if (b.transition == Selectable.Transition.SpriteSwap)
                    b.transition = Selectable.Transition.ColorTint;
            }
        }

        private static Image Piece(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.material = null;   // full brightness - see valheim-litpanel-material
            return img;
        }

        /// <summary>
        /// One of the board's ornaments at its own size. Each one's box in the art starts at the
        /// art's own edge, so it is pinned to the same edge of the board, and lands on the rail
        /// exactly where it was drawn.
        /// </summary>
        private void Ornament(Texture2D art, Rect px, Vector2 edge)
        {
            Sprite sp = Sprite.Create(art, new Rect(px.xMin, art.height - px.yMax, px.width, px.height),
                                      new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            Image img = Piece(_board.transform, "ornament", sp);
            img.transform.SetAsLastSibling();
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = edge;
            rt.sizeDelta = new Vector2(px.width, px.height) / PxPerUnit;
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Whether a board placed by hand (CarturUI_Board_*, not one of these) covers most of this
        /// plate within its own window - the plate's parent and below. Searching the whole canvas
        /// took the world-select board for the Remove World dialog's own, and cleared that dialog
        /// to nothing (pilot, 2026-10-04).
        /// </summary>
        private bool UnderFittedBoard(RectTransform window, Rect mine)
        {
            if (window == null)
                return false;
            foreach (Image img in window.GetComponentsInChildren<Image>(false))
            {
                if (!img.enabled || !img.name.StartsWith("CarturUI_Board_") || img.name.StartsWith(BoardName))
                    continue;
                Rect b = WorldRect(img.rectTransform);
                float w = Mathf.Min(b.xMax, mine.xMax) - Mathf.Max(b.xMin, mine.xMin);
                float h = Mathf.Min(b.yMax, mine.yMax) - Mathf.Max(b.yMin, mine.yMin);
                if (w > 0f && h > 0f && w * h >= mine.width * mine.height * 0.5f)
                    return true;
            }
            return false;
        }

        /// <summary>The old plate stops drawing but keeps catching clicks.</summary>
        private void Clear()
        {
            if (_plate.color.a != 0f)
                _plate.color = new Color(1f, 1f, 1f, 0f);
        }

        private void Show(bool on)
        {
            if (_board != null && _board.gameObject.activeSelf != on)
                _board.gameObject.SetActive(on);
        }

        private static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (Transform q = t.parent; q != null && q.parent != null; q = q.parent)
                p = q.name + "/" + p;
            return p;
        }
    }
}
