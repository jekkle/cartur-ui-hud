using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Cartur's grid board (board_grid.png, 8x4, 2026-10-04) laid out for any number of columns and
    /// rows, for the bag and for every chest. Built exactly like tools/art/grid_board.py, which made
    /// the previews he approved: the board is cut mid-gap into columns and rows and laid back
    /// together, so no cell, rail or corner is ever resized. board_grid_base.png is the same art
    /// with its medallion and rune strips painted out by its own rail; those go back on top once,
    /// at their own size. Chests get a band of planks (board_grid_band.png) above the first row for
    /// their name and buttons.
    /// </summary>
    internal static class GridBoard
    {
        // board_grid.png px, measured (tools/art/grid_board.py): cuts mid-gap between cells.
        private static readonly float[] ColCuts = { 277, 460, 645, 831, 1020, 1206, 1393 };
        private static readonly float[] RowCuts = { 314, 491, 669 };
        // Cell centres per source column / row (dark-interior runs, 2026-10-04).
        private static readonly float[] CellX = { 182.5f, 368.5f, 552.5f, 738f, 925.5f, 1113.5f, 1300f, 1486f };
        private static readonly float[] CellY = { 224f, 402.5f, 580.5f, 758f };
        private static readonly Rect Medal = Rect.MinMaxRect(755, 0, 905, 140);
        private static readonly Rect RunesLeft = Rect.MinMaxRect(15, 345, 52, 630);
        private static readonly Rect RunesRight = Rect.MinMaxRect(1617, 345, 1657, 630);
        private const float BandAt = 140f;   // the band goes in here, above the first row of cells
        internal const float BandPx = 84f;
        private const float RailPx = 62f;
        private static readonly Rect RailSrc = Rect.MinMaxRect(0, 110, 62, 340);
        /// <summary>One cell with its bronze rim, for button plates; 9-slice border 22 px.</summary>
        internal static readonly Rect CellPx = Rect.MinMaxRect(93, 138, 272, 311);
        internal const float CellBorderPx = 22f;

        internal static float ColPitchPx => (CellX[7] - CellX[0]) / 7f;   // 186.2
        internal static float RowPitchPx => (CellY[3] - CellY[0]) / 3f;   // 178

        internal static Texture2D Art => AssetLoader.Board("grid");
        private static Texture2D Plain => AssetLoader.Board("grid_base");
        private static Texture2D Planks => AssetLoader.Board("grid_band");
        private static Texture2D Wood => AssetLoader.Board("grid_wood");
        internal static bool Active => Art != null && Plain != null;

        internal sealed class Layout
        {
            public Vector2 Size;               // px
            public List<float> X = new List<float>(), Y = new List<float>();   // cell centres, px from top-left
            public Rect Band;                  // px, empty when no band
        }

        private static readonly Dictionary<string, Sprite> s_sprites = new Dictionary<string, Sprite>();

        private static Sprite Cut(Texture2D tex, Rect px, Vector4 border = default)
        {
            string key = tex.GetInstanceID() + ":" + px + border;
            if (s_sprites.TryGetValue(key, out Sprite sp) && sp != null)
                return sp;
            // px from the top; Unity's rect counts from the bottom.
            sp = Sprite.Create(tex, new Rect(px.xMin, tex.height - px.yMax, px.width, px.height),
                               new Vector2(0f, 1f), 100f, 0, SpriteMeshType.FullRect, border);
            sp.name = "cartur_grid";
            s_sprites[key] = sp;
            return sp;
        }

        internal static Sprite Plate() => Art == null ? null
            : Cut(Art, CellPx, new Vector4(CellBorderPx, CellBorderPx, CellBorderPx, CellBorderPx));

        private static List<(float from, float to, int cell)> Strips(float[] cuts, float total, int n, int cells)
        {
            var edges = new List<float> { 0 };
            edges.AddRange(cuts);
            edges.Add(total);
            var list = new List<(float, float, int)> { (edges[0], edges[1], 0) };
            for (int i = 0; i < n - 2; i++)
            {
                int m = 1 + i % (cells - 2);
                list.Add((edges[m], edges[m + 1], m));
            }
            list.Add((edges[cells - 1], edges[cells], cells - 1));
            return list;
        }

        /// <summary>
        /// Lays the board out under <paramref name="root"/> (pivot top-left), <paramref name="s"/>
        /// canvas units per board px. Reuses root's children, so calling it again is cheap.
        /// </summary>
        internal static Layout Build(RectTransform root, int cols, int rows, bool band, float s,
                                     System.Func<int, int, bool> dead = null)
        {
            // Called from InventoryGrid.UpdateGui, which runs every frame the bag or a chest is open.
            // The same shape, scale and dead cells as last time is the same board, so it is kept.
            string deadMask = null;
            if (dead != null)
            {
                var mask = new char[cols * rows];
                for (int i = 0; i < mask.Length; i++)
                    mask[i] = dead(i % cols, i / cols) ? '1' : '0';
                deadMask = new string(mask);
            }
            if (s_built.TryGetValue(root, out Built was) && was.Cols == cols && was.Rows == rows && was.Band == band
                && was.S == s && was.Dead == deadMask && root.childCount > 0)
                return was.Lay;

            Texture2D art = Art, plain = Plain;
            var lay = new Layout();
            var xs = Strips(ColCuts, plain.width, cols, 8);
            var ys = Strips(RowCuts, plain.height, rows, 4);
            float w = 0f;
            foreach (var c in xs) w += c.to - c.from;

            // Rows, the first split at BandAt when a band goes in.
            var bands = new List<(float from, float to, int cell)>();
            if (band)
            {
                bands.Add((ys[0].from, BandAt, -1));
                bands.Add((0, 0, -2));                       // the band
                bands.Add((BandAt, ys[0].to, 0));
                for (int i = 1; i < ys.Count; i++) bands.Add(ys[i]);
            }
            else
                bands.AddRange(ys);

            int n = 0;
            float y = 0f;
            foreach (var r in bands)
            {
                if (r.cell == -2)
                {
                    lay.Band = new Rect(0, y, w, BandPx);
                    Texture2D planks = Planks;
                    if (planks != null)
                        Lay(root, n++, Cut(planks, new Rect(0, 0, Mathf.Min(planks.width, w - 2 * RailPx), BandPx)),
                            RailPx, y, w - 2 * RailPx, BandPx, s);
                    Lay(root, n++, Cut(plain, Rect.MinMaxRect(RailSrc.xMin, RailSrc.yMin, RailSrc.xMax, RailSrc.yMin + BandPx)),
                        0, y, RailPx, BandPx, s);
                    Lay(root, n++, Cut(plain, Rect.MinMaxRect(plain.width - RailPx, RailSrc.yMin, plain.width, RailSrc.yMin + BandPx)),
                        w - RailPx, y, RailPx, BandPx, s);
                    y += BandPx;
                    continue;
                }
                float x = 0f;
                foreach (var c in xs)
                {
                    Lay(root, n++, Cut(plain, Rect.MinMaxRect(c.from, r.from, c.to, r.to)), x, y, c.to - c.from, r.to - r.from, s);
                    if (r.cell >= 0 && lay.X.Count < cols)
                        lay.X.Add(x + CellX[c.cell] - c.from);
                    x += c.to - c.from;
                }
                if (r.cell >= 0)
                    lay.Y.Add(y + CellY[r.cell] - r.from);
                y += r.to - r.from;
            }
            lay.Size = new Vector2(w, y);

            // A cell that cannot be used (a backpack row's dead cells) is covered with plain grained
            // wood, rim and all, so the board only paints the boxes that take an item.
            Texture2D wood = Wood;
            if (dead != null && wood != null)
                for (int r = 0; r < lay.Y.Count; r++)
                    for (int c = 0; c < lay.X.Count; c++)
                        if (dead(c, r))
                        {
                            float cw = CellPx.width + 4f, ch = CellPx.height + 4f;
                            Lay(root, n++, Cut(wood, new Rect(((c * 197) % (int)(wood.width - cw)), 0, cw, ch)),
                                lay.X[c] - cw * 0.5f, lay.Y[r] - ch * 0.5f, cw, ch, s);
                        }

            // Ornaments, once, at their own size: medallion centred on the top rail, runes centred
            // on the side rails when the board is tall enough to hold them clear of the corners.
            Lay(root, n++, Cut(art, Medal), (w - Medal.width) * 0.5f, 0, Medal.width, Medal.height, s);
            if (y - 400f > RunesLeft.height)
            {
                float ry = (y - RunesLeft.height) * 0.5f;
                Lay(root, n++, Cut(art, RunesLeft), RunesLeft.xMin, ry, RunesLeft.width, RunesLeft.height, s);
                Lay(root, n++, Cut(art, RunesRight), w - (plain.width - RunesRight.xMin), ry, RunesRight.width, RunesRight.height, s);
            }
            for (int i = root.childCount - 1; i >= n; i--)
                Object.Destroy(root.GetChild(i).gameObject);
            root.sizeDelta = lay.Size * s;
            s_built[root] = new Built { Cols = cols, Rows = rows, Band = band, S = s, Dead = deadMask, Lay = lay };
            return lay;
        }

        private sealed class Built
        {
            public int Cols, Rows;
            public bool Band;
            public float S;
            public string Dead;
            public Layout Lay;
        }

        private static readonly Dictionary<RectTransform, Built> s_built = new Dictionary<RectTransform, Built>();

        private static void Lay(RectTransform root, int index, Sprite sprite, float x, float y, float w, float h, float s)
        {
            Image img;
            if (index < root.childCount)
                img = root.GetChild(index).GetComponent<Image>();
            else
            {
                var go = new GameObject("Piece", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(root, false);
                img = go.GetComponent<Image>();
                img.raycastTarget = false;
                img.type = Image.Type.Simple;
                img.material = null;   // full brightness - see valheim-litpanel-material
            }
            img.sprite = sprite;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x * s, -y * s);
            rt.sizeDelta = new Vector2(w * s, h * s);
        }
    }
}
