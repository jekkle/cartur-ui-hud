using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Cartur's menu boards (Grok renders on his crafting board's style, picked 2026-10-03):
    /// main menu plaque, character-select bar, world-select panel. Each goes behind the game's own
    /// controls and the controls are seated on the boxes painted for them; the vanilla plates under
    /// them go clear so the art's plates show.
    ///
    /// Box positions are measured off the PNGs (tools/art/out/mainmenu_bars.png,
    /// charselect_plates.png); the controls' positions are read live, so nothing about the
    /// game's layout is assumed. Each screen is fitted once, the first frame it is up and laid out.
    /// </summary>
    internal static class MenuBoards
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        // board_mainmenu.png, 577x832 (Grok, strict six-bar guide): bar centres from y 197.5,
        // 97.6 px apart, read off the bronze rims; x centre 288.5.
        private const float MenuBar0Y = 197.5f, MenuPitch = 97.6f, MenuMidX = 288.5f;
        private static readonly Vector2 MenuBarInner = new Vector2(499 - 75, 233 - 163);

        // board_charselect.png, 1995x294 (Cartur's ravenless bar, 2026-10-04; the old one is in
        // tools/art/out/old): five plates, left to right, rims read off a brightness profile.
        private static readonly Rect[] CharPlates =
        {
            Rect.MinMaxRect(92, 95, 378, 211), Rect.MinMaxRect(427, 95, 715, 211), Rect.MinMaxRect(822, 95, 1166, 211),
            Rect.MinMaxRect(1264, 95, 1549, 211), Rect.MinMaxRect(1600, 95, 1884, 211),
        };

        private static bool s_menu, s_char, s_world, s_server, s_eula;

        [HarmonyPatch(typeof(FejdStartup), "Awake")]
        [HarmonyPostfix]
        private static void Awake()
        {
            s_menu = s_char = s_world = s_server = s_eula = false;
            s_worldBoard = null;
            s_worldSprite = s_serverSprite = null;
            s_covered.Clear();
            s_mods = s_name = false;
        }

        [HarmonyPatch(typeof(FejdStartup), "Update")]
        [HarmonyPostfix]
        private static void Tick(FejdStartup __instance)
        {
            if (!s_menu && Up(__instance.m_menuList))
                s_menu = Try("main menu", () => MainMenu(__instance));
            if (!s_char && Up(__instance.m_selectCharacterPanel))
                s_char = Try("character select", () => CharSelect(__instance));
            if (!s_world && Up(__instance.m_startGamePanel))
                s_world = Try("world select", () => World(__instance));
            if (!s_eula && __instance.m_eulaWindow != null && Up(__instance.m_eulaWindow.m_window))
                s_eula = Try("licence", () => Eula(__instance));
            Rows(__instance);
            // Boards drawn over each panel's own dumped layout (2026-10-03): the board covers the
            // old frame and the controls already stand on their boxes, so only plates go clear.
            Cover(__instance.m_startGamePanel, "newWorld/panel", "newworld",
                  "Cancel", "Done", "NameField", "SeedField", "GenerateSeed");
            if (!s_mods && __instance.m_startGamePanel != null)
            {
                Transform mods = __instance.m_startGamePanel.transform.Find("StartGui_ServerOptions/panel");
                if (mods != null && mods.gameObject.activeInHierarchy)
                    s_mods = Try("world modifiers", () => Modifiers(mods));
            }
            Cover(__instance.m_newCharacterPanel, "NamePanel", "namepanel", "Cancel", "Done", "NameField");
            if (!s_name && s_covered.Contains("namepanel"))
                s_name = Try("name bar", () => NameBar(__instance));
            if (!s_server && s_world && Up(__instance.m_serverListPanel))
                s_server = Try("server list", () => Server(__instance));
            // One panel, two tabs: the shared board shows the server picture while Join is up.
            if (s_worldBoard != null && s_serverSprite != null)
            {
                Sprite want = Up(__instance.m_serverListPanel) ? s_serverSprite : s_worldSprite;
                if (s_worldBoard.sprite != want)
                    s_worldBoard.sprite = want;
            }
        }

        private static bool Up(GameObject go) => go != null && go.activeInHierarchy;

        // Done is true once the screen is fitted or cannot be; a throw is logged once, never retried.
        private static bool Try(string what, System.Func<bool> fit)
        {
            try { return fit(); }
            catch (System.Exception e) { Log.LogWarning(what + " board failed: " + e.Message); return true; }
        }

        private static bool MainMenu(FejdStartup fs)
        {
            Texture2D tex = AssetLoader.Board("mainmenu");
            if (tex == null) return true;
            var list = fs.m_menuList.transform as RectTransform;
            var entries = list.GetComponentsInChildren<Button>(false)
                .Select(b => (RectTransform)b.transform).Where(r => r.rect.height > 0f)
                .OrderByDescending(r => Mid(r).y).ToList();
            if (entries.Count < 2) return false;   // not laid out yet

            float pitch = Mid(entries[0]).y - Mid(entries[1]).y;
            float s = pitch / MenuPitch;                       // world units per art px
            Image board = Board(list, tex, "mainmenu");
            var rt = board.rectTransform;
            SetWorldSize(rt, tex.width * s, tex.height * s);
            Vector2 top = Mid(entries[0]);
            float midX = entries.Average(e => Mid(e).x);
            rt.position = new Vector3(midX + (tex.width * 0.5f - MenuMidX) * s,
                                      top.y + MenuBar0Y * s - tex.height * 0.5f * s, rt.position.z);
            foreach (string n in new[] { "darken", "ornament" })
                Off(list.Find(n));
            // Labels inside the painted bar, not over its rims (Cartur, 2026-10-04: "the font needs
            // to fit properly ... slightly smaller"). The bar's inner box on the art is x 75..499,
            // y 163..233 (rim profile through bar one); each label gets 85% of its width and 70%
            // of its height, centred on its bar, and shrinks only as far as a long word needs.
            foreach (RectTransform e in entries)
                foreach (TMP_Text text in e.GetComponentsInChildren<TMP_Text>(false))
                {
                    if (text.transform.parent.name.StartsWith("gamepad_hint")) continue;
                    var tr = text.rectTransform;
                    float size = text.fontSize;
                    SetWorldSize(tr, MenuBarInner.x * 0.85f * s, MenuBarInner.y * 0.7f * s);
                    tr.position += (Vector3)(Mid(e) - Mid(tr));
                    text.enableAutoSizing = true;
                    text.fontSizeMax = size;
                    text.fontSizeMin = size * 0.5f;
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.alignment = TextAlignmentOptions.Center;
                }
            Log.LogInfo("main menu board: " + entries.Count + " entries on six bars, pitch " + pitch.ToString("0.#"));
            return true;
        }

        private static bool CharSelect(FejdStartup fs)
        {
            Texture2D tex = AssetLoader.Board("charselect");
            var panel = fs.m_selectCharacterPanel.transform as RectTransform;
            var window = panel.Find("BottomWindow") as RectTransform;
            if (tex == null || window == null) return true;
            var buttons = window.GetComponentsInChildren<Button>(false)
                .Select(b => (RectTransform)b.transform).OrderBy(r => Mid(r).x).ToList();
            if (buttons.Count == 0 || window.rect.width <= 0f) return false;

            Off(window);
            // Drawn first: the bar art is taller than this strip and covered the "Cloud save" line
            // under the name (Cartur: "the character panel is hiding text under the panel").
            window.SetAsFirstSibling();
            Image board = Board(window, tex, "charselect");
            Rect w = WorldRect(window);
            float s = w.width / tex.width;
            SetWorldSize(board.rectTransform, w.width, tex.height * s);
            board.rectTransform.position = new Vector3(w.center.x, w.center.y, board.rectTransform.position.z);
            // "Cloud save" under the name sat on the bar's top border; it goes just above it.
            var source = panel.Find("FileSource") as RectTransform;
            if (source != null)
            {
                Rect br = WorldRect(board.rectTransform), sr = WorldRect(source);
                float lift = br.yMax + sr.height * 0.15f - sr.yMin;
                if (lift > 0f)
                {
                    source.position += new Vector3(0f, lift, 0f);
                    foreach (string n in new[] { "CharacterName", "Left", "Right" })
                        if (panel.Find(n) is Transform t) t.position += new Vector3(0f, lift, 0f);
                }
            }
            if (buttons.Count == CharPlates.Length)
                for (int i = 0; i < buttons.Count; i++)
                    Seat(buttons[i], board.rectTransform, tex, CharPlates[i]);
            Log.LogInfo("character select board: " + buttons.Count + " buttons"
                + (buttons.Count == CharPlates.Length ? " seated on the plates" : ", plates skipped (expected 5)"));
            return true;
        }

        private static bool World(FejdStartup fs)
        {
            Texture2D tex = AssetLoader.Board("world");
            var bkg = fs.m_startGamePanel.transform.Find("Panel/bkg") as RectTransform;
            if (tex == null || bkg == null) return true;
            if (bkg.rect.width <= 0f) return false;

            // The board was drawn over this panel's own layout, so it covers the old frame's rect
            // and the controls already stand on their boxes; their own plates go clear.
            Off(bkg);
            Image board = Board(bkg, tex, "world");
            s_worldBoard = board;
            s_worldSprite = board.sprite;
            board.rectTransform.anchorMin = Vector2.zero;
            board.rectTransform.anchorMax = Vector2.one;
            board.rectTransform.offsetMin = board.rectTransform.offsetMax = Vector2.zero;
            Transform panel = fs.m_startGamePanel.transform.Find("Panel");
            Transform world = panel.Find("WorldPanel");
            Off(panel.Find("bkg (1)"));
            Off(world.Find("WorldList"));
            Off(world.Find("worldScroll"));
            Off(world.Find("PasswordField"));

            // Each control onto its own box, measured off board_world.png (638x774,
            // tools/art/out/world_boxes.png). Laying the board over the old frame and trusting the
            // controls to line up left every label off its plate (Cartur, 2026-10-03).
            var rt = board.rectTransform;
            Seat(panel.Find("Host") as RectTransform, rt, tex, Rect.MinMaxRect(70, 55, 248, 93));
            Seat(panel.Find("Join") as RectTransform, rt, tex, Rect.MinMaxRect(258, 55, 438, 93));
            Place(world.Find("topic") as RectTransform, rt, tex, Rect.MinMaxRect(67, 108, 570, 142));
            Place(world.Find("WorldList") as RectTransform, rt, tex, Rect.MinMaxRect(70, 146, 554, 427));
            Place(world.Find("worldScroll") as RectTransform, rt, tex, Rect.MinMaxRect(554, 146, 568, 427));
            string[] left = { "New world", "Remove", "ManageSaves", "Server Options", "Back" };
            float[] top = { 445, 497, 549, 602, 656 };
            for (int i = 0; i < left.Length; i++)
                Seat(world.Find(left[i]) as RectTransform, rt, tex, Rect.MinMaxRect(78, top[i], 270, top[i] + 45));
            // Each toggle: the art's square socket is the selector, the label sits in the box beside
            // it (Cartur, 2026-10-03). Sockets measured (329,443)..(355,467), (329,478)..(355,501),
            // (329,514)..(355,536); label boxes (361,448)..(490,467) and the two below.
            string[] toggles = { "OpenServer", "PublicGameToggle", "CrossplayToggle" };
            Rect[] sockets = { Rect.MinMaxRect(329, 443, 355, 467), Rect.MinMaxRect(329, 478, 355, 501), Rect.MinMaxRect(329, 514, 355, 536) };
            // The knot panel inside each socket's bronze rim, read off board_world.png at 8x (2026-10-04);
            // the sockets above stop 2-4 px short of the painted rim at the bottom.
            Rect[] knots = { Rect.MinMaxRect(334, 448, 351, 467), Rect.MinMaxRect(334, 482, 351, 501), Rect.MinMaxRect(334, 518, 351, 537) };
            for (int i = 0; i < toggles.Length; i++)
            {
                var tog = world.Find(toggles[i]) as RectTransform;
                if (tog == null) continue;
                Rect sock = sockets[i];
                Place(tog, rt, tex, Rect.MinMaxRect(sock.xMin, sock.yMin, 492, sock.yMax));
                var bg = tog.Find("Background") as RectTransform;
                if (bg != null)
                {
                    Place(bg, rt, tex, sock);
                    var bgImg = bg.GetComponent<Image>();
                    if (bgImg != null) bgImg.color = Color.clear;   // the art's socket is the box
                    var check = bg.Find("Checkmark") as RectTransform;
                    if (check != null)
                    {
                        Place(check, rt, tex, knots[i]);
                        // The game's checkmark sprite is a small round dot, lost in the socket's knot
                        // (Cartur, 2026-10-04: "fill the whole square when selected"). No sprite draws
                        // a solid square over the socket's knot panel, in the checkmark's own colour.
                        if (check.GetComponent<Image>() is Image tick)
                        {
                            tick.sprite = null;
                            tick.preserveAspect = false;
                        }
                    }
                }
                var label = tog.Find("Label")?.GetComponent<TMP_Text>();
                if (label != null)
                {
                    Place(label.rectTransform, rt, tex, Rect.MinMaxRect(sock.xMax + 9, sock.yMin + 3, 488, sock.yMax - 1));
                    label.enableAutoSizing = true;
                    label.fontSizeMin = 6f;
                    label.fontSizeMax = Mathf.Max(8f, label.rectTransform.rect.height * 0.8f);
                    label.alignment = TextAlignmentOptions.Left;
                }
            }
            Place(world.Find("Text") as RectTransform, rt, tex, Rect.MinMaxRect(332, 543, 536, 563));
            Place(world.Find("PasswordField") as RectTransform, rt, tex, Rect.MinMaxRect(334, 567, 534, 592));
            Locked(world.Find("PasswordField"));
            Place(world.Find("PasswordError") as RectTransform, rt, tex, Rect.MinMaxRect(332, 596, 536, 618));
            Seat(world.Find("Start") as RectTransform, rt, tex, Rect.MinMaxRect(331, 624, 565, 699));
            Log.LogInfo("world select board placed over the panel's frame");
            return true;
        }

        private static Image s_worldBoard;
        private static Sprite s_worldSprite, s_serverSprite;

        /// <summary>
        /// The server tab on board_serverlist.png - the world board with its lower half rebuilt for
        /// the server controls (tools/art/server_board.py, 2026-10-04), so the frame, the Host/Join
        /// plates and the list well are the same pixels on both tabs and nothing jumps when the tab
        /// changes. The shared board swaps picture (see Tick); here every control goes on its box.
        /// The first server board was drawn for the world's layout and nothing sat on it (Cartur:
        /// "the select server screen is completely messed up").
        /// </summary>
        private static bool Server(FejdStartup fs)
        {
            Texture2D tex = AssetLoader.Board("serverlist");
            Transform join = fs.m_serverListPanel.transform;
            if (tex == null || s_worldBoard == null) return true;
            var rt = s_worldBoard.rectTransform;
            if (rt.rect.width <= 0f) return false;
            s_serverSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f,
                                           0, SpriteMeshType.FullRect);
            s_worldBoard.sprite = s_serverSprite;

            foreach (Transform t in join.Cast<Transform>().Where(t => t.name == "bkg"))
                Off(t);
            foreach (string n in new[] { "ServerList", "ServerListScroll" })
                Off(join.Find(n));

            // The list tabs are one row of their own; whatever stands on the row moves as a group,
            // evenly scaled, so it holds however many tabs the platform shows. Gathered before the
            // title moves - placed first, the title and count landed on the row and were shrunk
            // with the tabs (pilot, 2026-10-04).
            var tab = join.Find("ServerListTab") as RectTransform;
            if (tab != null)
            {
                Rect row = WorldRect(tab);
                var group = join.Cast<Transform>().OfType<RectTransform>()
                    .Where(t => t.name != "bkg" && t.name != "topic" && t.name != "serverCount"
                                && Mathf.Abs(Mid(t).y - row.center.y) < row.height * 0.5f).ToList();
                Rect have = group.Select(WorldRect).Aggregate((a, b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin),
                    Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));
                Rect b0 = WorldRect(rt);
                float sx = b0.width / tex.width, sy = b0.height / tex.height;
                var band = new Rect(b0.xMin + 74 * sx, b0.yMax - 190 * sy, 490 * sx, 42 * sy);
                float f = Mathf.Min(band.width / have.width, band.height / have.height);
                foreach (RectTransform t in group)
                {
                    Vector2 off = (Vector2)Mid(t) - have.center;
                    t.localScale *= f;
                    t.position += (Vector3)(band.center + off * f - (Vector2)Mid(t));
                }
            }
            // Boxes from tools/art/server_board.py, px from the board's top-left.
            // Title centred across the whole plate (Cartur: "select server text should be centered");
            // the count sits on its right end.
            Place(join.Find("topic") as RectTransform, rt, tex, Rect.MinMaxRect(67, 108, 570, 142));
            if (join.Find("topic")?.GetComponent<TMP_Text>() is TMP_Text topic)
                topic.alignment = TextAlignmentOptions.Center;
            Place(join.Find("serverCount") as RectTransform, rt, tex, Rect.MinMaxRect(470, 110, 565, 140));
            Place(join.Find("ServerList") as RectTransform, rt, tex, Rect.MinMaxRect(70, 192, 554, 427));
            Place(join.Find("ServerListScroll") as RectTransform, rt, tex, Rect.MinMaxRect(554, 192, 568, 427));
            Seat(join.Find("Refresh") as RectTransform, rt, tex, Rect.MinMaxRect(80, 443, 126, 488));
            Place(join.Find("FilterField") as RectTransform, rt, tex, Rect.MinMaxRect(138, 450, 314, 482));
            // Remove (favourites) and Favorite (the other tabs) share a box; the game shows one.
            Seat(join.Find("RemoveButton") as RectTransform, rt, tex, Rect.MinMaxRect(326, 443, 372, 488));
            Seat(join.Find("FavoriteButton") as RectTransform, rt, tex, Rect.MinMaxRect(326, 443, 372, 488));
            Seat(join.Find("Add server") as RectTransform, rt, tex, Rect.MinMaxRect(380, 443, 568, 488));
            // Not painted on the board: they keep a plate of their own and show only when usable.
            foreach (var (n, box) in new[] { ("MoveUpButton", Rect.MinMaxRect(80, 497, 126, 542)), ("MoveDownButton", Rect.MinMaxRect(134, 497, 180, 542)) })
            {
                var b = join.Find(n) as RectTransform;
                Place(b, rt, tex, box);
                if (b?.GetComponent<Image>() is Image plate && GridBoard.Plate() is Sprite sp)
                {
                    plate.sprite = sp;
                    plate.type = Image.Type.Sliced;
                    plate.pixelsPerUnitMultiplier = GridBoard.CellBorderPx / 6f;
                    plate.color = Color.white;
                    plate.material = null;
                }
            }
            Seat(join.Find("Back") as RectTransform, rt, tex, Rect.MinMaxRect(80, 645, 300, 690));
            Seat(join.Find("Join") as RectTransform, rt, tex, Rect.MinMaxRect(330, 622, 566, 700));
            Log.LogInfo("server list: controls seated on the server board");
            return true;
        }

        private static bool Eula(FejdStartup fs)
        {
            Texture2D tex = AssetLoader.Board("eula");
            // m_window is the "Popup" itself (dumped), not its parent.
            var popup = fs.m_eulaWindow.m_window.transform;
            var bkg = popup?.Find("bkg") as RectTransform;
            if (tex == null || bkg == null) return true;
            if (bkg.rect.width <= 0f) return false;
            Off(bkg);
            Image board = Board(bkg, tex, "eula");
            board.rectTransform.anchorMin = Vector2.zero;
            board.rectTransform.anchorMax = Vector2.one;
            board.rectTransform.offsetMin = board.rectTransform.offsetMax = Vector2.zero;
            Off(popup.Find("Scroll View/Scrollbar Vertical"));
            // Onto the art's own boxes, measured off board_eula.png (859x736,
            // tools/art/out/eula_boxes.png): text well (86,80)..(763,555), plates (177,588)..(400,658)
            // and (448,588)..(671,658). The header takes the band above the well.
            var rt = board.rectTransform;
            Place(popup.Find("HeaderText") as RectTransform, rt, tex, Rect.MinMaxRect(86, 36, 763, 80));
            Place(popup.Find("Scroll View") as RectTransform, rt, tex, Rect.MinMaxRect(100, 92, 750, 545));
            Seat(popup.Find("ButtonDecline") as RectTransform, rt, tex, Rect.MinMaxRect(177, 588, 400, 658));
            Seat(popup.Find("ButtonAccept") as RectTransform, rt, tex, Rect.MinMaxRect(448, 588, 671, 658));
            Log.LogInfo("licence board placed over the popup's frame");
            return true;
        }

        /// <summary>
        /// World and server list rows: the game draws them as flat white bars with a flat orange
        /// selection (dumped 2026-10-03: WorldElement/bkg white, /selected RGBA(1,0.64,0,1)). They
        /// take a recipe row cut from Cartur's crafting board instead, 9-sliced so its rails keep
        /// their thickness across a much wider row, and a soft gold selection. Rows are rebuilt by
        /// the game whenever the list changes, so this checks each frame, cheaply, for new ones.
        /// </summary>
        private static void Rows(FejdStartup fs)
        {
            foreach (GameObject panel in new[] { fs.m_startGamePanel, fs.m_serverListPanel })
            {
                if (!Up(panel)) continue;
                foreach (string list in new[] { "Panel/WorldPanel/WorldList/ListRoot", "ServerList/ListRoot" })
                {
                    Transform root = panel.transform.Find(list);
                    if (root == null) continue;
                    foreach (Transform row in root)
                    {
                        var bkg = row.Find("bkg")?.GetComponent<Image>();
                        if (bkg == null || bkg.sprite == RowSprite()) continue;
                        // The row prefab keeps vanilla's width, wider than the list the board fits
                        // it into (dumped: rows 339..1031, WorldList 339..933), so the id column ran
                        // under the mask. Each row takes its list's width, left edge kept.
                        var rowRt = row as RectTransform;
                        var rootRt = root as RectTransform;
                        if (rowRt != null && rootRt != null && rowRt.rect.width * rowRt.lossyScale.x > rootRt.rect.width * rootRt.lossyScale.x + 0.5f)
                        {
                            float left = rowRt.TransformPoint(rowRt.rect.min).x;
                            rowRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                                rootRt.rect.width * rootRt.lossyScale.x / rowRt.lossyScale.x);
                            rowRt.position += new Vector3(left - rowRt.TransformPoint(rowRt.rect.min).x, 0f, 0f);
                            // The seed column is placed from the row's left (it stayed at x 851 after
                            // the row narrowed, 2026-10-03), so it still ran past the new right edge.
                            // Any text past the edge comes back in by exactly what overhangs.
                            float right = rowRt.TransformPoint(rowRt.rect.max).x;
                            foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
                            {
                                var t = (RectTransform)text.transform;
                                float over = t.TransformPoint(t.rect.max).x - right;
                                if (over > 0f)
                                    t.position -= new Vector3(over, 0f, 0f);
                            }
                        }
                        // Names and ids shrink to fit their row rather than being cut at its edge.
                        foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
                        {
                            float size = text.fontSize;
                            text.enableAutoSizing = true;
                            text.fontSizeMax = size;
                            text.fontSizeMin = size * 0.6f;
                        }
                        bkg.sprite = RowSprite();
                        bkg.type = Image.Type.Sliced;
                        bkg.color = Color.white;
                        bkg.material = null;
                        var sel = row.Find("selected")?.GetComponent<Image>();
                        if (sel != null)
                        {
                            sel.sprite = RowSprite();
                            sel.type = Image.Type.Sliced;
                            sel.color = new Color(1f, 0.8f, 0.35f, 0.55f);
                            sel.material = null;
                        }
                    }
                }
            }
        }

        // Crafting board row two, px (106, 435.5)..(489, 517.5): see CraftingBoard's measurements.
        private static Sprite s_row;
        private static Sprite RowSprite()
        {
            if (s_row != null) return s_row;
            Texture2D tex = AssetLoader.Board("crafting");
            if (tex == null) return null;
            var r = new Rect(106f, tex.height - 517.5f, 383f, 82f);
            s_row = Sprite.Create(tex, r, new Vector2(0.5f, 0.5f), 200f, 0, SpriteMeshType.FullRect,
                                  new Vector4(14f, 14f, 14f, 14f));
            s_row.name = "cartur_board_row";
            return s_row;
        }

        private static readonly HashSet<string> s_covered = new HashSet<string>();
        private static bool s_mods, s_name;

        // board_namepanel.png (1971x723, Cartur's new name panel, 2026-10-04; the old one is in
        // tools/art/out/old). Boxes read off it and drawn back on the art to check: the long well
        // (137,145)..(1830,330), two knot-cornered plates (240,399)..(845,592) and (1140,399)..(1745,592).
        private static readonly Rect NameLabelPx = Rect.MinMaxRect(170, 165, 440, 310);
        private static readonly Rect NameFieldPx = Rect.MinMaxRect(450, 165, 1800, 310);
        private static readonly Rect NameLeftPx = Rect.MinMaxRect(240, 399, 845, 592);
        private static readonly Rect NameRightPx = Rect.MinMaxRect(1140, 399, 1745, 592);

        /// <summary>The new-character name bar onto board_namepanel.png's boxes.</summary>
        private static bool NameBar(FejdStartup fs)
        {
            Transform p = fs.m_newCharacterPanel.transform.Find("NamePanel");
            var board = p?.Find("bkg/CarturUI_Board_namepanel") as RectTransform;
            Texture2D tex = AssetLoader.Board("namepanel");
            if (board == null || tex == null) return true;
            Place(p.Find("name") as RectTransform, board, tex, NameLabelPx);
            Place(p.Find("NameField") as RectTransform, board, tex, NameFieldPx);
            Seat(p.Find("Cancel") as RectTransform, board, tex, NameLeftPx);
            Seat(p.Find("Done") as RectTransform, board, tex, NameRightPx);
            // The panel stood on the screen's bottom edge with its lower rail cut off (review,
            // 2026-10-04); lifted so the whole board shows, with a small gap.
            var root = board.GetComponentInParent<Canvas>()?.rootCanvas.transform as RectTransform;
            if (root != null)
            {
                Rect b = WorldRect(board), r = WorldRect(root);
                float lift = r.yMin + r.height * 0.02f - b.yMin;
                if (lift > 0f)
                    p.position += new Vector3(0f, lift, 0f);
            }
            Log.LogInfo("name bar controls seated on its boxes");
            return true;
        }

        // board_modifiers.png, 547x732: plank interior and title plate, measured off the PNG.
        private static readonly Rect ModInside = Rect.MinMaxRect(44, 115, 504, 682);
        // Plate interior read off board_modifiers.png (rims at y 50 and 116): centre y 85.
        private static readonly Rect ModTitle = Rect.MinMaxRect(60, 58, 487, 112);

        /// <summary>
        /// World Modifiers on a plain frame, the game's own controls drawing on the wood. The frame
        /// is sized from what the panel draws - every control's live rect - so it all sits inside
        /// the plank interior instead of under the border (Cartur: "make the world modifiers panel
        /// bigger so everything fits inside the border"). The title goes in the title plate.
        /// </summary>
        private static bool Modifiers(Transform panel)
        {
            Texture2D tex = AssetLoader.Board("modifiers");
            var bkg = panel.Find("bkg") as RectTransform;
            if (tex == null || bkg == null) return true;
            if (bkg.rect.width <= 0f) return false;

            // One text size for the whole customise block: slider names, their values and the
            // checkbox labels came in three sizes (Cartur: "world modifiers text still not right").
            // The slider name's size is the size; a label only shrinks below it to fit its box.
            Transform mods = panel.Find("Modifiers");
            TMP_Text model = mods?.Find("Combat/label")?.GetComponent<TMP_Text>();
            if (mods != null && model != null)
            {
                float size = model.fontSize;
                foreach (TMP_Text t in mods.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t.transform.parent.name.StartsWith("gamepad_hint") || t.name.EndsWith("Text")) continue;
                    t.enableAutoSizing = true;
                    t.fontSizeMax = size;
                    t.fontSizeMin = size * 0.6f;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                }
            }

            bool any = false;
            Rect have = default;
            foreach (Transform c in panel)
            {
                if (c == bkg || c.name == "topic" || c.name == "Tooltips" || c.name == "OLD" || !c.gameObject.activeSelf)
                    continue;
                foreach (Graphic g in c.GetComponentsInChildren<Graphic>(false))
                {
                    if (!g.enabled) continue;
                    Rect r = WorldRect(g.rectTransform);
                    have = any ? Rect.MinMaxRect(Mathf.Min(have.xMin, r.xMin), Mathf.Min(have.yMin, r.yMin),
                                                 Mathf.Max(have.xMax, r.xMax), Mathf.Max(have.yMax, r.yMax)) : r;
                    any = true;
                }
            }
            if (!any) return false;

            Off(bkg);
            Image board = Board(bkg, tex, "modifiers");
            var rt = board.rectTransform;
            float pad = have.width * 0.03f;
            float s = Mathf.Max((have.width + 2f * pad) / ModInside.width, (have.height + 2f * pad) / ModInside.height);
            SetWorldSize(rt, tex.width * s, tex.height * s);
            // Interior centre, in board px from the top-left, onto the contents' centre.
            Vector2 insideMid = new Vector2(ModInside.center.x, ModInside.center.y);
            Vector2 boardMid = new Vector2(tex.width * 0.5f, tex.height * 0.5f);
            Vector2 offset = new Vector2(boardMid.x - insideMid.x, insideMid.y - boardMid.y) * s;
            rt.position = new Vector3(have.center.x + offset.x, have.center.y + offset.y, rt.position.z);

            Place(panel.Find("topic") as RectTransform, rt, tex, ModTitle);
            // Centred both ways on the plate; top-aligned it sat high (Cartur, 2026-10-04).
            if (panel.Find("topic")?.GetComponent<TMP_Text>() is TMP_Text title)
                title.alignment = TextAlignmentOptions.Center;
            Log.LogInfo("world modifiers frame sized to its contents, " + (tex.width * s).ToString("0") + " wide");
            return true;
        }

        private static void Cover(GameObject root, string path, string board, params string[] plates)
        {
            if (root == null || s_covered.Contains(board)) return;
            Transform panel = root.transform.Find(path);
            if (panel == null || !panel.gameObject.activeInHierarchy) return;
            var bkg = panel.Find("bkg") as RectTransform;
            Texture2D tex = AssetLoader.Board(board);
            s_covered.Add(board);
            if (bkg == null || tex == null) return;
            try
            {
                Off(bkg);
                Image img = Board(bkg, tex, board);
                img.rectTransform.anchorMin = Vector2.zero;
                img.rectTransform.anchorMax = Vector2.one;
                img.rectTransform.offsetMin = img.rectTransform.offsetMax = Vector2.zero;
                foreach (string n in plates)
                    Off(panel.Find(n));
                Log.LogInfo(board + " board placed over its panel's frame");
            }
            catch (System.Exception e) { Log.LogWarning(board + " board failed: " + e.Message); }
        }

        // ---- helpers -------------------------------------------------------------------------

        private static Image Board(RectTransform parent, Texture2D tex, string name)
        {
            var go = new GameObject("CarturUI_Board_" + name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f,
                                       0, SpriteMeshType.FullRect);
            img.raycastTarget = false;
            img.material = null;   // full brightness - see valheim-litpanel-material
            return img;
        }

        /// <summary>
        /// A field whose own plate went clear (Off) also lost its disabled look: Selectable tints its
        /// target graphic, and that was the plate. The world-select password box is only
        /// interactable while Start Server is ticked (FejdStartup.Update sets it every frame from
        /// m_openServerToggle), so it looked ready to type into and ignored the keys (Grog, Discord
        /// 2026-10-05: "it won't let me set my password").
        ///
        /// The box you see is painted into board_world.png, so nothing on the field can recolour
        /// it - the 1.1.1 attempt tinted the placeholder, which is empty, and measured identical
        /// locked and open (pilot shots, 2026-10-10). Instead the plate comes back as a black
        /// shade over the painted box: tint alpha 0 while usable, 0.55 while locked.
        /// </summary>
        private static void Locked(Transform field)
        {
            var sel = field != null ? field.GetComponent<Selectable>() : null;
            var plate = field != null ? field.GetComponent<Graphic>() : null;
            if (sel == null || plate == null)
                return;
            plate.color = Color.black;
            sel.targetGraphic = plate;
            sel.transition = Selectable.Transition.ColorTint;
            ColorBlock cb = sel.colors;
            cb.normalColor = cb.highlightedColor = cb.pressedColor = cb.selectedColor = new Color(1f, 1f, 1f, 0f);
            cb.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            cb.colorMultiplier = 1f;
            sel.colors = cb;
            // The box is near black inside, so the shade alone is measurable (14 -> 7) but hard to
            // see. Saying what unlocks it is what Grog was missing.
            if (field.GetComponent<LockedHint>() == null)
                field.gameObject.AddComponent<LockedHint>();
        }

        /// <summary>Placeholder reads "Tick Start Server..." while the field is locked, its own text otherwise.</summary>
        private sealed class LockedHint : MonoBehaviour
        {
            private TMP_InputField _field;
            private TMP_Text _hint;
            private string _own;

            private void Awake()
            {
                _field = GetComponent<TMP_InputField>();
                // By path, not _field.placeholder: the pilot's dump puts it at Text Area/Placeholder.
                _hint = transform.Find("Text Area/Placeholder")?.GetComponent<TMP_Text>()
                        ?? (_field != null ? _field.placeholder as TMP_Text : null);
                _own = _hint != null ? _hint.text : null;
                if (_hint == null)
                    return;
                // The box is one line tall: at its own 18pt the placeholder spilled past the
                // Text Area mask and drew nothing (pilot audit "overflow"). Shrink to fit.
                _hint.enableAutoSizing = true;
                _hint.fontSizeMin = 8f;
                _hint.fontSizeMax = _hint.fontSize;
            }

            // Every frame, not on change: the game writes its own placeholder ("[Empty]") back
            // after this first sets it (pilot shot, 2026-10-10). A string compare is the whole cost.
            private void Update()
            {
                if (_hint == null || _field == null)
                    return;
                const string Hint = "Tick Start Server to set a password";
                if (!_field.interactable)
                {
                    if (_hint.text == Hint)
                        return;
                    _own = _hint.text;      // whatever the game wrote last, localized, put back on unlock
                    _hint.text = Hint;
                }
                else if (_hint.text == Hint)
                    _hint.text = _own;
            }
        }

        /// <summary>Stops a vanilla plate drawing. Clickables keep their graphic, cleared, to stay clickable.</summary>
        private static void Off(Transform t)
        {
            var g = t != null ? t.GetComponent<Graphic>() : null;
            if (g == null) return;
            var mask = t.GetComponent<Mask>();
            if (mask != null) mask.showMaskGraphic = false;
            else if (t.GetComponent<Selectable>() != null || t.GetComponent<Scrollbar>() != null)
            {
                g.color = Color.clear;
                var sel = t.GetComponent<Selectable>();
                if (sel != null && sel.transition == Selectable.Transition.SpriteSwap)
                    sel.transition = Selectable.Transition.None;
            }
            else g.enabled = false;
        }

        /// <summary>Sizes and centres a control on a box given in board px from the top-left.</summary>
        private static void Seat(RectTransform control, RectTransform board, Texture2D tex, Rect px)
        {
            if (control == null) return;
            Rect b = WorldRect(board);
            float sx = b.width / tex.width, sy = b.height / tex.height;
            var target = new Rect(b.xMin + px.xMin * sx, b.yMax - px.yMax * sy, px.width * sx, px.height * sy);
            SetWorldSize(control, target.width * 0.86f, target.height * 0.8f);
            control.position += (Vector3)(target.center - Mid(control));
            Off(control);
            // The label fills the plate and sizes itself to it: kept at vanilla's point size on a
            // smaller plate it came out tiny, on a bigger one it spilled (audit, 2026-10-03).
            foreach (TMP_Text text in control.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.transform.parent.name.StartsWith("gamepad_hint")) continue;
                var tr = text.rectTransform;
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                tr.offsetMin = new Vector2(6f, 2f); tr.offsetMax = new Vector2(-6f, -2f);
                text.enableAutoSizing = true;
                text.fontSizeMin = 8f;
                text.fontSizeMax = Mathf.Max(10f, control.rect.height * 0.5f);
                text.alignment = TextAlignmentOptions.Center;
                // One line, like every other plate - "Manage saves" wrapped to two small ones.
                text.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }

        /// <summary>Fits a rect exactly to a box on the board, in board px from its top-left.</summary>
        private static void Place(RectTransform control, RectTransform board, Texture2D tex, Rect px)
        {
            if (control == null) return;
            Rect b = WorldRect(board);
            float sx = b.width / tex.width, sy = b.height / tex.height;
            var target = new Rect(b.xMin + px.xMin * sx, b.yMax - px.yMax * sy, px.width * sx, px.height * sy);
            SetWorldSize(control, target.width, target.height);
            control.position += (Vector3)(target.center - Mid(control));
        }

        private static void SetWorldSize(RectTransform rt, float w, float h)
        {
            Vector3 k = rt.lossyScale;
            if (k.x <= 0f || k.y <= 0f) return;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w / k.x);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h / k.y);
        }

        private static Vector2 Mid(RectTransform rt) => rt.TransformPoint(rt.rect.center);

        private static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }
    }
}
