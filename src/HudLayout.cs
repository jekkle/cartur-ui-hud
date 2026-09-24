using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Edit mode: drag a HUD piece to move it, wheel over it to scale it, and the result is
    /// written to the config file so it survives a restart.
    ///
    /// Scaling is localScale, not sizeDelta. The bars' width is rewritten by the game every
    /// frame from the max stat, so anything that sets sizeDelta on them is overwritten
    /// immediately - which is also why the existing UiDrag from the build-menu mod could not
    /// be reused here.
    ///
    /// Move and Hit are separate: a bar's panel is only the window between the ornaments, so
    /// the outline and the mouse test go against the frame instead. Elements carry an owner
    /// (the screen that registered them) so each screen's Awake clears only its own.
    /// </summary>
    internal static class HudLayout
    {
        private const float MinScale = 0.3f;
        private const float MaxScale = 3f;
        private const float ScaleStep = 0.05f;

        private class Element
        {
            public string Owner;   // which screen registered it, so that screen's Awake can clear only its own
            public string Key;
            public string Label;
            public RectTransform Move;   // what gets positioned and scaled
            public RectTransform Hit;    // what the mouse is tested against
            public ConfigEntry<string> Entry;

            /// <summary>
            /// Where this piece goes on a reset. Empty until saveAsDefault is ticked, and then
            /// it holds whatever the layout was at that moment - so "default" means the layout
            /// Cartur built, not the one the mod shipped with.
            /// </summary>
            public ConfigEntry<string> HomeEntry;
            public GameObject Overlay;
            public float Scale = 1f;
            // Bars drive their frame height from the wheel instead of localScale: their length
            // belongs to the game (max stat), so scaling the transform would stretch it too.
            public Action<float> OnScale;
            public Action<float> OnLength;
            public float Length = 1f;
            public Action<float, Vector2> OnFrame;
            public float FrameScale = 1f;
            public Vector2 FrameOffset;
            // What the mod would place this at with no config: what a reset goes back to.
            public Vector2 HomePosition;
            public float HomeScale = 1f;
            public float HomeLength = 1f;
            public float HomeFrameScale = 1f;
        }

        private static readonly List<Element> s_elements = new List<Element>();
        private static readonly FieldInfo s_mouseCapture = AccessTools.Field(typeof(GameCamera), "m_mouseCapture");

        private static ConfigFile s_config;
        private static ConfigEntry<bool> s_editMode;
        private static TMP_FontAsset s_font;
        private static Sprite s_white;

        private static FileSystemWatcher s_watcher;
        private static volatile bool s_reloadPending;
        private static float s_ignoreWritesUntil;

        private static bool s_editing;

        // Raycasters this mod added so pointer events reach the overlays. Taken off again when
        // edit mode goes off, so a canvas the game owns is left as it was found.
        private static readonly List<GraphicRaycaster> s_addedRaycasters = new List<GraphicRaycaster>();

        internal static BepInEx.Logging.ManualLogSource Log;

        public static bool Editing => s_editing;

        private static ConfigEntry<bool> s_reset;
        private static ConfigEntry<bool> s_saveDefault;

        public static void Init(ConfigFile config, ConfigEntry<bool> editMode)
        {
            s_config = config;
            s_editMode = editMode;

            // A tick box rather than a keybind: it sits next to edit mode in the config editor,
            // it cannot be hit by accident, and it unticks itself once the layout is back.
            s_reset = config.Bind("Layout", "resetLayout", false,
                "Tick to put every HUD and inventory piece back where the mod would place it. "
                + "Unticks itself when done.");

            // The other half of resetLayout: this one writes the CURRENT layout down as the
            // one a reset goes back to. Also a tick box, also unticks itself.
            s_saveDefault = config.Bind("Layout", "saveAsDefault", false,
                "Tick to make the layout on screen right now the one resetLayout goes back to. "
                + "Open the inventory once first, so every piece has been built and is counted. "
                + "Unticks itself when done.");

            // Off, or every frame of a drag writes the whole file to disk.
            s_config.SaveOnConfigSet = false;
            Watch();
        }

        /// <summary>
        /// Reloads the config when the file changes on disk, so ticking editMode in any editor -
        /// Configuration Manager, r2modman's, a text editor - takes effect without a restart.
        /// That is what keeps Configuration Manager optional rather than a hard dependency.
        /// </summary>
        private static void Watch()
        {
            try
            {
                string path = s_config.ConfigFilePath;
                s_watcher = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };
                // Fires on a background thread, so it only raises a flag - Tick does the work.
                s_watcher.Changed += (sender, args) => s_reloadPending = true;
                s_watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                Log.LogWarning("could not watch the config file, changes to it will need a restart: " + e.Message);
            }
        }

        private static void Persist()
        {
            // Our own write would otherwise bounce straight back through the watcher.
            s_ignoreWritesUntil = Time.realtimeSinceStartup + 1f;
            s_config.Save();
        }

        private static void ReloadFromConfig()
        {
            foreach (Element e in s_elements)
            {
                if (!TryParse(e.Entry.Value, out Vector2 position, out float scale, out float length,
                        out float frameScale, out Vector2 frameOffset))
                    continue;
                e.Move.anchoredPosition = position;
                e.Scale = Mathf.Clamp(scale, MinScale, MaxScale);
                e.Length = Mathf.Clamp(length, MinScale, MaxScale);
                e.FrameScale = Mathf.Clamp(frameScale, MinScale, MaxScale);
                e.FrameOffset = frameOffset;
                Apply(e);
            }
            Log.LogInfo("config reloaded from disk");
        }

        /// <summary>
        /// Called from a screen's Awake before it registers. Only that screen's elements go:
        /// Hud and InventoryGui wake in no fixed order relative to each other, and the one
        /// waking second must not wipe what the first just registered.
        /// </summary>
        public static void Reset(string owner, TMP_FontAsset font)
        {
            for (int i = s_elements.Count - 1; i >= 0; i--)
            {
                Element e = s_elements[i];
                if (e.Owner != owner)
                    continue;
                if (e.Overlay != null)
                    UnityEngine.Object.Destroy(e.Overlay);
                s_elements.RemoveAt(i);
            }
            s_editing = false;
            if (font != null)
                s_font = font;

            if (s_white != null)
                return;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            s_white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        }

        public static void Register(string owner, string key, string label, RectTransform move, RectTransform hit, Vector2 fallbackPosition, float fallbackScale = 1f, Action<float> onScale = null, Action<float> onLength = null, Action<float, Vector2> onFrame = null, float fallbackFrameScale = 1f, float fallbackLength = 1f, Vector2? supersededPosition = null)
        {
            if (move == null || hit == null)
                return;

            var element = new Element
            {
                Owner = owner,
                Key = key,
                Label = label,
                Move = move,
                Hit = hit,
                OnScale = onScale,
                OnLength = onLength,
                OnFrame = onFrame,
                HomePosition = fallbackPosition,
                HomeScale = fallbackScale,
                HomeLength = fallbackLength,
                HomeFrameScale = fallbackFrameScale,
                Entry = s_config.Bind("Layout", key, Format(fallbackPosition, fallbackScale, fallbackLength, fallbackFrameScale, Vector2.zero),
                    "Layout of the " + label + ". Written by edit mode; x,y,size,length,frameSize,frameX,frameY."),
                HomeEntry = s_config.Bind("Layout defaults", key, "",
                    "Where the " + label + " goes on a reset. Written by saveAsDefault; empty means the mod's own.")
            };

            // A default that was saved from the screen wins over the one written in the code.
            // Same format as the layout itself, so the two can be compared by eye in the file.
            if (TryParse(element.HomeEntry.Value, out Vector2 homePos, out float homeScale,
                    out float homeLength, out float homeFrame, out Vector2 _))
            {
                element.HomePosition = homePos;
                element.HomeScale = homeScale;
                element.HomeLength = homeLength;
                element.HomeFrameScale = homeFrame;
            }

            if (!TryParse(element.Entry.Value, out Vector2 position, out float scale, out float length, out float frameScale, out Vector2 frameOff))
            {
                position = fallbackPosition;
                scale = fallbackScale;
                length = fallbackLength;
                frameScale = fallbackFrameScale;
                frameOff = Vector2.zero;
                Log.LogWarning("could not read layout for " + key + " (\"" + element.Entry.Value + "\") - using the default");
            }

            // A position a previous version wrote as its default, which this version no longer
            // places anything at. Compared on position alone, so it survives the entry gaining
            // fields, and it leaves the size and length the player chose alone.
            if (supersededPosition.HasValue
                && Mathf.Approximately(position.x, supersededPosition.Value.x)
                && Mathf.Approximately(position.y, supersededPosition.Value.y))
            {
                position = fallbackPosition;
                element.Entry.Value = Format(position, scale, length, frameScale, frameOff);
                Log.LogInfo("moved " + key + " off the position an older version defaulted it to");
            }

            element.Scale = Mathf.Clamp(scale, MinScale, MaxScale);
            element.Length = Mathf.Clamp(length, MinScale, MaxScale);
            element.FrameScale = Mathf.Clamp(frameScale, MinScale, MaxScale);
            element.FrameOffset = frameOff;
            move.anchoredPosition = position;
            Apply(element);

            s_elements.Add(element);

            // Overlays were only ever built in Toggle, which runs when edit mode changes. Half
            // the registrations happen after that: the equipment panel, the quiver strip and the
            // side column are all built lazily on the first InventoryGui.Show, so anything
            // registered while edit mode was already on had no handle and could not be dragged
            // until the player turned edit mode off and on again. A piece registering into a
            // live edit session gets its overlay here instead.
            if (s_editing)
            {
                EnsureOverlay(element);
                if (element.Overlay != null)
                    element.Overlay.SetActive(true);
            }
        }

        /// <summary>Driven from the Hud.Update postfix.</summary>
        public static void Tick()
        {
            if (s_reloadPending)
            {
                s_reloadPending = false;
                if (Time.realtimeSinceStartup > s_ignoreWritesUntil)
                {
                    s_config.Reload();
                    ReloadFromConfig();
                }
            }

            if (s_saveDefault != null && s_saveDefault.Value)
                SaveAsDefaults();

            if (s_reset != null && s_reset.Value)
                ResetToDefaults();

            // Driven from the config rather than a key, so it lives with the rest of the
            // settings and cannot be hit by accident mid-fight.
            if (s_editMode.Value != s_editing)
                Toggle();
        }

        /// <summary>
        /// Writes the layout that is on screen right now down as the one a reset returns to.
        ///
        /// Kept in its own config section rather than overwriting the numbers in the code: the
        /// file can be read side by side - what a piece is at, and what it goes back to - and
        /// clearing a line there hands that piece back to the mod's own default without
        /// touching anything else.
        /// </summary>
        private static void SaveAsDefaults()
        {
            foreach (Element e in s_elements)
            {
                e.HomePosition = e.Move.anchoredPosition;
                e.HomeScale = e.Scale;
                e.HomeLength = e.Length;
                e.HomeFrameScale = e.FrameScale;
                e.HomeEntry.Value = Format(e.HomePosition, e.HomeScale, e.HomeLength, e.HomeFrameScale, Vector2.zero);
            }

            s_saveDefault.Value = false;
            Persist();
            Log.LogInfo("layout saved as the default - " + s_elements.Count
                + " pieces will reset to where they are now. Any piece not built yet was not"
                + " counted; tick it again with the inventory open to catch those.");
        }

        /// <summary>Every piece back to the position the mod would give it with no config at all.</summary>
        private static void ResetToDefaults()
        {
            foreach (Element e in s_elements)
            {
                e.Move.anchoredPosition = e.HomePosition;
                e.Scale = e.HomeScale;
                e.Length = e.HomeLength;
                e.FrameScale = e.HomeFrameScale;
                e.FrameOffset = Vector2.zero;
                Apply(e);
                Save(e);
            }

            s_reset.Value = false;
            Persist();
            Log.LogInfo("layout reset - " + s_elements.Count + " pieces back to their defaults");
        }

        private static void Toggle()
        {
            s_editing = s_editMode.Value;

            foreach (Element e in s_elements)
            {
                EnsureOverlay(e);
                if (e.Overlay != null)
                    e.Overlay.SetActive(s_editing);
            }

            if (s_editing)
                AddRaycasters();
            else
                RemoveRaycasters();

            // GameCamera.m_mouseCapture is the game's own cursor lock. Its only other writer is
            // the Ctrl+F1 debug toggle, so it never resets itself - leaving it false on exit
            // would lock the player out of camera look until they found that shortcut. Flipping
            // it is also enough on its own: UpdateMouseCapture shows and unlocks the cursor
            // through ZCursor when it is false, so nothing here touches Cursor directly.
            if (GameCamera.instance != null)
                s_mouseCapture?.SetValue(GameCamera.instance, !s_editing);
            else
                Log.LogWarning("no GameCamera - the cursor will not be released for editing");

            if (!s_editing)
                Persist();

            Log.LogInfo(s_editing
                ? "layout edit on - drag: move | wheel: size | shift+wheel: bar length | ctrl+wheel: frame size | ctrl+drag: frame offset"
                : "layout edit off - saved to " + s_config.ConfigFilePath);
        }

        /// <summary>
        /// A canvas only delivers pointer events if it has a GraphicRaycaster. Vanilla's HUD
        /// canvas is not ours, so whether it carries one is read here rather than assumed, and
        /// one is added only for as long as edit mode is on.
        /// </summary>
        private static void AddRaycasters()
        {
            foreach (Element e in s_elements)
            {
                Canvas canvas = e.Hit != null ? e.Hit.GetComponentInParent<Canvas>() : null;
                if (canvas == null)
                {
                    Log.LogWarning("no canvas above " + e.Key + " - it cannot be dragged");
                    continue;
                }

                if (canvas.GetComponent<GraphicRaycaster>() != null)
                    continue;

                var caster = canvas.gameObject.AddComponent<GraphicRaycaster>();
                s_addedRaycasters.Add(caster);
                Log.LogInfo("added a GraphicRaycaster to " + canvas.name + " for edit mode");
            }

            if (EventSystem.current == null)
                Log.LogWarning("no EventSystem - nothing can be dragged");
        }

        private static void RemoveRaycasters()
        {
            foreach (GraphicRaycaster caster in s_addedRaycasters)
                if (caster != null)
                    UnityEngine.Object.Destroy(caster);
            s_addedRaycasters.Clear();
        }

        private static bool Ctrl() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        private static bool Shift() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        /// <summary>
        /// Drag and wheel for one piece, driven by Unity's own pointer events.
        ///
        /// This used to read Input.GetMouseButton and hit-test every registered piece against
        /// Input.mousePosition by hand. That worked here and not on other people's machines -
        /// reported on 1.0.2 as the overlay appearing with nothing draggable - and there is no
        /// reason to be doing it: the EventSystem already works out what the cursor is over,
        /// which is how the compass mod's edit mode does the same job. The overlay was already a
        /// raycast target, so the handler goes straight on it and the hand-rolled hit test goes.
        ///
        /// The camera comes off the event rather than from the canvas, so a screen-space and a
        /// camera-space canvas both land in the right place with nothing here to get wrong.
        /// </summary>
        private class EditHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
        {
            private Element m_element;
            private Vector2 m_grabOffset;
            private bool m_frameDrag;
            private bool m_resizing;
            private float m_resizeFrom;
            private float m_resizeScale;

            public void Init(Element element) => m_element = element;

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (m_element == null || !s_editing)
                    return;

                m_frameDrag = Ctrl() && m_element.OnFrame != null;
                float reach = 0f;
                m_resizing = !m_frameDrag
                    && OnBorder(m_element, eventData.pressPosition, eventData.pressEventCamera, out reach);
                if (m_resizing)
                {
                    // Scale is driven by how much further the cursor gets from the middle than it
                    // started, so the piece follows the hand at whatever size it already was. The
                    // wheel writes the same field, so a drag and a wheel cannot disagree.
                    m_resizeFrom = reach;
                    m_resizeScale = m_element.Scale;
                    return;
                }

                Vector2 anchor = m_frameDrag ? m_element.FrameOffset : m_element.Move.anchoredPosition;
                m_grabOffset = ToLocal(m_element.Move, eventData.pressPosition, eventData.pressEventCamera) - anchor;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (m_element == null || !s_editing)
                    return;

                if (m_resizing)
                {
                    if (OnReach(m_element, eventData.position, eventData.pressEventCamera, out float reach) && reach > 1f)
                    {
                        m_element.Scale = Mathf.Clamp(m_resizeScale * (reach / m_resizeFrom), MinScale, MaxScale);
                        Apply(m_element);
                    }
                    return;
                }

                Vector2 local = ToLocal(m_element.Move, eventData.position, eventData.pressEventCamera) - m_grabOffset;
                if (m_frameDrag)
                {
                    m_element.FrameOffset = local;
                    Apply(m_element);
                }
                else
                {
                    m_element.Move.anchoredPosition = local;
                }
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (m_element == null)
                    return;

                m_resizing = false;
                Save(m_element);
                Persist();
            }

            public void OnScroll(PointerEventData eventData)
            {
                if (m_element == null || !s_editing)
                    return;

                float wheel = eventData.scrollDelta.y;
                if (Mathf.Abs(wheel) < 0.01f)
                    return;

                float step = Mathf.Sign(wheel) * ScaleStep;
                // Plain wheel is size, shift is the bar's length, ctrl is the frame around it.
                // Length has to be a control of its own because it is otherwise the game's to
                // set, from max health; the frame has to be its own because fitting Cartur's art
                // to the fill is not something a size can express.
                if (Ctrl() && m_element.OnFrame != null)
                    m_element.FrameScale = Mathf.Clamp(m_element.FrameScale + step, MinScale, MaxScale);
                else if (Shift() && m_element.OnLength != null)
                    m_element.Length = Mathf.Clamp(m_element.Length + step, MinScale, MaxScale);
                else
                    m_element.Scale = Mathf.Clamp(m_element.Scale + step, MinScale, MaxScale);

                Apply(m_element);
                Save(m_element);
                Persist();
            }
        }

        /// <summary>
        /// Is the cursor in the grab band just inside a piece's outline, and if so how far is it
        /// from the middle?
        ///
        /// The distance is measured on the axes the grabbed border actually has: a left or right
        /// edge measures across, a top or bottom edge measures up, a corner measures both. That
        /// is what makes an edge drag feel like an edge drag even though the result is the same
        /// uniform scale either way - Scale is a localScale, one number, because the game rewrites
        /// the bars' width every frame and anything written to sizeDelta is gone next frame. So
        /// this cannot stretch one axis on its own, and no edge pretends to.
        /// </summary>
        private static bool OnBorder(Element e, Vector2 screen, Camera cam, out float reach)
        {
            reach = 0f;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(e.Hit, screen, cam, out Vector2 local))
                return false;

            Rect r = e.Hit.rect;
            float band = Mathf.Clamp(Mathf.Min(r.width, r.height) * 0.25f, 3f, 14f);
            bool across = local.x <= r.xMin + band || local.x >= r.xMax - band;
            bool up = local.y <= r.yMin + band || local.y >= r.yMax - band;
            if (!across && !up)
                return false;

            // How far out the grab was, measured exactly the way the drag measures it. It used
            // to be measured here per axis - dx alone for a side edge, dy alone for a top or
            // bottom one - while the drag used the full diagonal, so the number jumped the
            // instant the button went down and the piece resized before the mouse had moved.
            reach = Reach(e, screen, cam);

            // A grab right on the middle has nothing to measure against and would divide by it.
            return reach > 1f;
        }

        /// <summary>
        /// Distance from the piece's middle to the cursor, in the PARENT's units.
        ///
        /// The parent, not the piece itself, and that is the whole of it. A piece is resized by
        /// localScale, and its own local space lives inside that scale - so the same screen
        /// point maps to a smaller local number as the piece grows. Measuring there and then
        /// setting the scale from what came back closed a loop:
        ///
        ///     reach ~ D / scale,  scale = startScale * reach / startReach
        ///
        /// which inverts the scale every frame. On screen that was a piece jumping between tiny
        /// and huge on a pixel of mouse movement, which is what Cartur reported. The parent's
        /// space does not move when the child scales, so reach is proportional to the distance
        /// the hand actually travelled and the size follows it straight.
        /// </summary>
        private static float Reach(Element e, Vector2 screen, Camera cam)
        {
            var parent = e.Hit.parent as RectTransform;
            if (parent == null)
                return 0f;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out Vector2 local))
                return 0f;
            Vector3 centre = parent.InverseTransformPoint(e.Hit.TransformPoint(e.Hit.rect.center));
            return (local - (Vector2)centre).magnitude;
        }

        private static bool OnReach(Element e, Vector2 screen, Camera cam, out float reach)
        {
            reach = Reach(e, screen, cam);
            return reach > 0f;
        }

        private static Vector2 ToLocal(RectTransform rt, Vector2 screen, Camera cam)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt.parent as RectTransform, screen, cam, out Vector2 local);
            return local;
        }

        private const float BorderWidth = 3f;
        private static readonly Color EditBorder = new Color(0.85f, 0.12f, 0.12f, 0.95f);

        /// <summary>One side of the edit outline, stretched along the edge it belongs to.</summary>
        private static void Edge(RectTransform parent, Vector2 min, Vector2 max, Vector2 thickness)
        {
            var go = new GameObject("Edge", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            // One axis is pinned by the anchors; the other is given the strip's thickness.
            rt.sizeDelta = new Vector2(Mathf.Abs(thickness.x), Mathf.Abs(thickness.y));
            rt.pivot = new Vector2(thickness.x < 0f ? 1f : 0f, thickness.y < 0f ? 1f : 0f);

            Image img = go.AddComponent<Image>();
            img.sprite = s_white;
            img.color = EditBorder;
            img.raycastTarget = false;
        }

        private static void EnsureOverlay(Element e)
        {
            if (e.Overlay != null)
                return;

            e.Overlay = new GameObject("CarturUIHud_Edit_" + e.Key, typeof(RectTransform));
            var rt = (RectTransform)e.Overlay.transform;
            rt.SetParent(e.Hit, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Invisible, but still a raycast target: the overlay has to eat the click, because
            // under an inventory panel are item slots and a drag that reached them would pick
            // something up. Alpha zero rather than a tint - a wash over the piece hides the very
            // thing being positioned.
            Image blocker = e.Overlay.AddComponent<Image>();
            blocker.sprite = s_white;
            blocker.color = new Color(1f, 1f, 1f, 0f);
            blocker.raycastTarget = true;

            // The blocker is what the EventSystem hits, so the drag and wheel handler goes on it.
            e.Overlay.AddComponent<EditHandle>().Init(e);

            // A border of four thin strips rather than a UI Outline: Outline works by drawing
            // offset copies of the graphic, so on a filled rect it gives a bigger filled rect,
            // not an edge. Strips give a true hollow box that leaves the piece visible.
            Edge(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -BorderWidth));   // top
            Edge(rt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, BorderWidth));    // bottom
            Edge(rt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(BorderWidth, 0f));    // left
            Edge(rt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-BorderWidth, 0f));   // right

            if (s_font == null)
                return;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            var lrt = (RectTransform)labelGo.transform;
            lrt.SetParent(rt, false);
            lrt.anchorMin = new Vector2(0.5f, 0.5f);
            lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(180f, 24f);
            lrt.anchoredPosition = Vector2.zero;

            var text = labelGo.AddComponent<TextMeshProUGUI>();
            text.font = s_font;
            text.text = e.Label;
            text.fontSize = 13f;
            text.color = new Color(1f, 0.95f, 0.75f, 0.85f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
        }

        private static void Apply(Element e)
        {
            if (e.OnScale != null)
                e.OnScale(e.Scale);
            else
                e.Move.localScale = Vector3.one * e.Scale;

            e.OnLength?.Invoke(e.Length);
            e.OnFrame?.Invoke(e.FrameScale, e.FrameOffset);
        }

        private static void Save(Element e)
        {
            e.Entry.Value = Format(e.Move.anchoredPosition, e.Scale, e.Length, e.FrameScale, e.FrameOffset);
        }

        private static string Format(Vector2 position, float scale, float length, float frameScale, Vector2 frameOffset) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.###},{3:0.###},{4:0.###},{5:0.##},{6:0.##}",
                position.x, position.y, scale, length, frameScale, frameOffset.x, frameOffset.y);

        private static bool TryParse(string value, out Vector2 position, out float scale, out float length,
            out float frameScale, out Vector2 frameOffset)
        {
            position = Vector2.zero;
            scale = 1f;
            length = 1f;
            frameScale = 1f;
            frameOffset = Vector2.zero;
            if (string.IsNullOrEmpty(value))
                return false;

            string[] parts = value.Split(',');
            // Shorter entries are configs written before lengths and frame fitting existed.
            // They still read, with the newer fields left at their defaults.
            if (parts.Length < 3)
                return false;

            var ci = CultureInfo.InvariantCulture;
            if (!float.TryParse(parts[0], NumberStyles.Float, ci, out float x)
                || !float.TryParse(parts[1], NumberStyles.Float, ci, out float y)
                || !float.TryParse(parts[2], NumberStyles.Float, ci, out scale))
                return false;

            if (parts.Length >= 4 && !float.TryParse(parts[3], NumberStyles.Float, ci, out length))
                return false;

            if (parts.Length >= 7)
            {
                if (!float.TryParse(parts[4], NumberStyles.Float, ci, out frameScale)
                    || !float.TryParse(parts[5], NumberStyles.Float, ci, out float fx)
                    || !float.TryParse(parts[6], NumberStyles.Float, ci, out float fy))
                    return false;
                frameOffset = new Vector2(fx, fy);
            }

            position = new Vector2(x, y);
            return true;
        }
    }

    /// <summary>
    /// Swallows player input while the layout is being edited. Without this a left-click drag
    /// also swings whatever is in your hands, and WASD walks you away from the HUD you are
    /// arranging. GameCamera.m_mouseCapture alone only frees the cursor, it does not stop the
    /// player acting on it.
    /// </summary>
    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class EditInputBlock
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (HudLayout.Editing)
                __result = false;
        }
    }
}
