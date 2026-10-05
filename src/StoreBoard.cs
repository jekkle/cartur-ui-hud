using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// The trader's store window on Cartur's store board (board_store.png, 638x1287, Grok render he
    /// picked 2026-10-04, colour-matched to the Epic Loot trader board). The board stands behind the
    /// window at its height and the game's own controls go onto the boxes painted for them - read
    /// off the art and drawn back on it to check. Layout from the pilot's dump of StoreGui:
    /// Store/topic, Store/ItemList (Items + ItemScroll), Store/BuyButton, Store/coins.
    /// </summary>
    internal static class StoreBoard
    {
        private const string Name = "CarturUI_Board_store";
        private static readonly Rect TitlePx = Rect.MinMaxRect(85, 84, 550, 165);
        private static readonly Rect WellPx = Rect.MinMaxRect(70, 195, 568, 980);
        private static readonly Rect BuyPx = Rect.MinMaxRect(134, 1012, 504, 1111);
        private static readonly Rect CoinsPx = Rect.MinMaxRect(70, 1150, 568, 1220);
        private static readonly Color Gold = new Color32(255, 214, 90, 255);

        [HarmonyPatch(typeof(StoreGui), "Show")]
        [HarmonyPostfix]
        private static void Shown(StoreGui __instance)
        {
            Transform store = __instance.transform.Find("Store");
            var frame = store?.Find("border (1)") as RectTransform;
            Texture2D tex = AssetLoader.Board("store");
            if (frame == null || tex == null)
                return;
            foreach (string n in new[] { "bkg", "border (1)" })
                if (store.Find(n)?.GetComponent<Image>() is Image old)
                    old.enabled = false;   // AutoBoard stands down on a frame that is switched off

            var board = store.Find(Name) as RectTransform;
            if (board == null)
            {
                var go = new GameObject(Name, typeof(RectTransform), typeof(Image));
                board = (RectTransform)go.transform;
                board.SetParent(store, false);
                board.SetAsFirstSibling();
                var img = go.GetComponent<Image>();
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f,
                                           0, SpriteMeshType.FullRect);
                img.raycastTarget = false;
                img.material = null;   // full brightness - see valheim-litpanel-material
            }

            // As tall as the window's old frame, its own aspect, centred on it.
            Rect f = World(frame);
            Vector3 k = board.lossyScale;
            if (k.x <= 0f || k.y <= 0f)
                return;
            float h = f.height, w = h * tex.width / tex.height;
            board.anchorMin = board.anchorMax = board.pivot = new Vector2(0.5f, 0.5f);
            board.sizeDelta = new Vector2(w / k.x, h / k.y);
            board.position = new Vector3(f.center.x, f.center.y, board.position.z);

            Place(store.Find("topic") as RectTransform, board, tex, TitlePx);
            Place(store.Find("ItemList") as RectTransform, board, tex, Rect.MinMaxRect(WellPx.xMin + 12, WellPx.yMin + 12, WellPx.xMax - 12, WellPx.yMax - 12));
            Place(store.Find("BuyButton") as RectTransform, board, tex, BuyPx);
            Place(store.Find("coins") as RectTransform, board, tex, CoinsPx);

            // The list's own well and the Buy plate are painted on the board now.
            if (store.Find("ItemList/Items")?.GetComponent<Image>() is Image well)
                well.color = Color.clear;
            if (store.Find("BuyButton")?.GetComponent<Image>() is Image buy)
                buy.color = Color.clear;
            if (store.Find("topic")?.GetComponent<TMP_Text>() is TMP_Text title)
            {
                title.alignment = TextAlignmentOptions.Center;
                title.color = Gold;
            }

            ClearHud(store, board);

            // The sell slot (drop an item, click the coin) kept the old frame's spot and floated off
            // the board (Cartur, 2026-10-04). Docked to the board's right edge, level with Buy, on the
            // slot-rim plate the other buttons use.
            var sell = store.Find("SellPanel") as RectTransform;
            if (sell != null)
            {
                Rect bw = World(board), sw = World(sell);
                float buyY = bw.yMax - BuyPx.center.y * bw.height / tex.height;
                sell.position += new Vector3(bw.xMax - bw.width * 0.02f - sw.xMin, buyY - sw.center.y, 0f);
                if (sell.GetComponent<Image>() is Image back && GridBoard.Plate() is Sprite plate)
                {
                    back.sprite = plate;
                    back.type = Image.Type.Sliced;
                    back.pixelsPerUnitMultiplier = GridBoard.CellBorderPx / 8f;
                    back.color = Color.white;
                    back.material = null;
                }
            }
        }

        /// <summary>
        /// The window ran down over the health bar (pilot shot + both panel reviews, 2026-10-05). When
        /// it overlaps the health bar - the top bar - it is scaled and moved, as one, into the space
        /// between the hotbar board and that bar, read from where the player has them now, so it
        /// follows their own layout. Reset to the game's own size and spot first on every open, so it
        /// never compounds.
        /// </summary>
        private static void ClearHud(Transform store, RectTransform board)
        {
            var rt = store as RectTransform;
            if (rt == null)
                return;
            if (!s_rest.HasValue)
                s_rest = (rt.localScale, rt.anchoredPosition);
            rt.localScale = s_rest.Value.scale;
            rt.anchoredPosition = s_rest.Value.pos;

            RectTransform hp = HudLayout.HitOf("health"), bar = Hotbar.BoardRect;
            if (hp == null || bar == null || !hp.gameObject.activeInHierarchy)
                return;
            Rect h = World(hp), t = World(bar), w = World(board);
            bool overlaps = w.xMin < h.xMax && w.xMax > h.xMin && w.yMin < h.yMax;
            if (!overlaps)
                return;
            float gap = w.height * 0.01f;
            float lo = h.yMax + gap;
            float hi = (t.xMin < w.xMax && t.xMax > w.xMin) ? Mathf.Min(t.yMin - gap, w.yMax) : w.yMax;
            if (hi - lo < w.height * 0.5f)
                return;          // no sensible room: leave it as the game placed it
            float k = Mathf.Min(1f, (hi - lo) / w.height);
            rt.localScale = s_rest.Value.scale * k;
            w = World(board);
            rt.position += new Vector3(0f, lo - w.yMin, 0f);
        }

        private static (Vector3 scale, Vector2 pos)? s_rest;

        /// <summary>
        /// Epic Loot builds its trader panel after the store opens, so it is put on its board from the
        /// first frame it is up rather than from Show (pilot, 2026-10-04: the panel stayed light wood).
        /// </summary>
        [HarmonyPatch(typeof(StoreGui), "Update")]
        [HarmonyPostfix]
        private static void Merchant(StoreGui __instance)
        {
            var panel = __instance.transform.Find("MerchantPanel");
            if (panel == null || !panel.gameObject.activeInHierarchy)
                return;
            if (panel.Find("CarturUI_Board_adventure") == null)
                EpicBoards.Merchant(__instance.transform);
            else if (Time.frameCount % 15 == 0)
                EpicBoards.Rows(panel);   // rows are rebuilt as Epic Loot refreshes its lists
        }

        private static void Place(RectTransform rt, RectTransform board, Texture2D tex, Rect px)
        {
            if (rt == null)
                return;
            Rect b = World(board);
            float sx = b.width / tex.width, sy = b.height / tex.height;
            var target = new Rect(b.xMin + px.xMin * sx, b.yMax - px.yMax * sy, px.width * sx, px.height * sy);
            Vector3 k = rt.lossyScale;
            if (k.x <= 0f || k.y <= 0f)
                return;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, target.width / k.x);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, target.height / k.y);
            rt.position += (Vector3)(target.center - (Vector2)rt.TransformPoint(rt.rect.center));
        }

        private static Rect World(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }
    }
}
