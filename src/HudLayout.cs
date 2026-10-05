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
            public Vector2 Rest;   // where the game itself rests the piece
            public Pin Pin;        // set when an Animator above the piece writes its position
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

        /// <summary>
        /// Cartur's layout, copied from his [Layout] section on 2026-10-05: the default for every
        /// piece listed. Same format as the config. A piece not listed keeps its code default -
        /// the four bars stay where the published 1.0.5 put them (his call, same day).
        /// </summary>
        private static readonly Dictionary<string, string> Shipped = new Dictionary<string, string>
        {
            ["player"] = "61.75,-184,0.748,1,1,0,0",
            ["container"] = "0,-30,1,1,1,0,0",
            ["crafting"] = "-37,-164.75,0.779,1,1,0,0",
            ["info"] = "-41.5,-39.25,0.769,1,1,0,0",
            ["cluster"] = "90.95,143.6,0.9,1,1,0,0",
            ["hotbar"] = "79.25,-47,0.722,1,1,0,0",
            ["equipment"] = "111.99,237.23,0.891,1,1,0,0",
            ["sidecolumn"] = "73.17,15.87,1.036,1,1,0,0",
        };

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
                SetPos(e, position);
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

            // Cartur's own layout is the default (2026-10-05: "the positions the ui is in on my
            // screen should be the default"). The caller's value is still the rest position the
            // Animator pin works from; only where a piece starts out, and where a reset puts it,
            // comes from the table. A player whose saved entry is exactly the old default never
            // moved that piece, so it follows the new one; any other saved entry is theirs and is
            // left alone.
            Vector2 rest = fallbackPosition;
            string oldDefault = Format(fallbackPosition, fallbackScale, fallbackLength, fallbackFrameScale, Vector2.zero);
            if (Shipped.TryGetValue(key, out string shipped)
                && TryParse(shipped, out Vector2 sp, out float ss, out float sl, out float sf, out Vector2 _))
            {
                fallbackPosition = sp;
                fallbackScale = ss;
                fallbackLength = sl;
                fallbackFrameScale = sf;
            }

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

            string newDefault = Format(fallbackPosition, fallbackScale, fallbackLength, fallbackFrameScale, Vector2.zero);
            if (element.Entry.Value == oldDefault && oldDefault != newDefault)
            {
                element.Entry.Value = newDefault;
                Persist();
                Log.LogInfo("moved " + key + " from the old default to the new one");
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
            element.Rest = rest;
            if (move.GetComponentInParent<Animator>() != null)
                element.Pin = move.gameObject.GetComponent<Pin>() ?? move.gameObject.AddComponent<Pin>();
            if (element.Pin != null)
                element.Pin.Rest = rest;
            SetPos(element, position);
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
        /// <summary>
        /// Saves where a registered piece is now, the same as finishing a drag would. For code that
        /// places something once on the player's behalf (the inventory board under the hotbar).
        /// </summary>
        /// <summary>True the first time it is asked for a key, ever; remembered in the config.</summary>
        public static bool FirstTime(string key)
        {
            var entry = s_config.Bind("Layout migrations", key, false,
                "Set once a one-time layout move has been done. False to have it done again.");
            if (entry.Value)
                return false;
            entry.Value = true;
            Persist();
            return true;
        }

        public static void Commit(string key)
        {
            foreach (Element e in s_elements)
            {
                if (e.Key != key)
                    continue;
                SetPos(e, e.Move.anchoredPosition);   // what is on screen now becomes the spot
                Save(e);
                Persist();
                return;
            }
        }

        private static float s_persistAt = -1f;
        private static float s_fitAt;

        private static Vector2 Pos(Element e) => e.Pin != null ? e.Pin.Desired : e.Move.anchoredPosition;

        private static void SetPos(Element e, Vector2 p)
        {
            e.Move.anchoredPosition = p;
            e.Pin?.Accept(p);
        }

        /// <summary>
        /// Keeps a piece where the player put it when an Animator also drives its position. The
        /// inventory screen's open animation slides the Player panel from y 350 down to its prefab
        /// rest (40,-40) on every open - measured by the test pilot, no code involved, so a Harmony
        /// patch cannot stop it - and that silently threw away the saved spot and every edit-mode
        /// drag (Cartur: "I can't move the inventory panel"). After the Animator has written, the
        /// player's offset from rest is added back, so the slide still plays and ends where he put
        /// the panel. Frames the Animator does not write are left alone.
        /// </summary>
        internal sealed class Pin : MonoBehaviour
        {
            public Vector2 Rest;
            public Vector2 Desired;
            private Vector2 m_written;
            private RectTransform m_rt;

            public void Accept(Vector2 p)
            {
                Desired = p;
                m_written = p;
            }

            // Per axis: the screen's Animator does not drive both. It slides the Player panel in
            // y and the Crafting panel in x only, and leaves the other axis alone. Adding the
            // offset back on an axis it had not written added it again on top of itself every
            // frame - the crafting panel ran millions of units down in three seconds (pilot,
            // 2026-10-04: "it keeps moving"). An axis is offset only when the Animator wrote it.
            private void LateUpdate()
            {
                if (m_rt == null)
                    m_rt = (RectTransform)transform;
                Vector2 cur = m_rt.anchoredPosition;
                bool x = Mathf.Abs(cur.x - m_written.x) > 0.01f, y = Mathf.Abs(cur.y - m_written.y) > 0.01f;
                if (!x && !y)
                    return;
                m_written = new Vector2(x ? cur.x + (Desired.x - Rest.x) : cur.x,
                                        y ? cur.y + (Desired.y - Rest.y) : cur.y);
                m_rt.anchoredPosition = m_written;
            }
        }

        public static void Tick()
        {
            if (s_persistAt > 0f && Time.realtimeSinceStartup > s_persistAt)
            {
                s_persistAt = -1f;
                Persist();
            }

            // The outline follows what is drawn, a few times a second while editing.
            if (s_editing && Time.realtimeSinceStartup > s_fitAt)
            {
                s_fitAt = Time.realtimeSinceStartup + 0.25f;
                foreach (Element e in s_elements)
                    FitOverlay(e);
            }

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
                e.HomePosition = Pos(e);
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
                SetPos(e, e.HomePosition);
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

            // The inventory screen's full-screen drop catcher ("dropButton", two Graphics) sits on
            // its canvas above the HUD's, so with the inventory open every click and wheel on the
            // hotbar went to it, not to the hotbar's edit handle (edit-click log, 2026-10-04). It
            // stops catching while editing and is given back after.
            if (InventoryGui.instance != null)
                foreach (Graphic g in InventoryGui.instance.GetComponentsInChildren<Graphic>(true))
                    if (g.name == "dropButton")
                        g.raycastTarget = !s_editing;

            foreach (Element e in s_elements)
            {
                EnsureOverlay(e);
                if (e.Overlay != null)
                {
                    e.Overlay.SetActive(s_editing);
                    // In front of the piece's own parts: the hotbar's labels are raycast targets and
                    // sat above its handle, so a drag started on them reached nothing (pilot, 2026-10-04).
                    if (s_editing)
                        e.Overlay.transform.SetAsLastSibling();
                }
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

                Vector2 anchor = m_frameDrag ? m_element.FrameOffset : Pos(m_element);
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
                    SetPos(m_element, local);
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
                // Size grows by a percentage, not a fixed amount: +0.05 was a 17% jump at 0.3 and a
                // 2% one at 2.5, which is what read as not scaling smoothly (Cartur, 2026-10-03).
                float factor = 1f + step;
                // Plain wheel is size, shift is the bar's length, ctrl is the frame around it.
                // Length has to be a control of its own because it is otherwise the game's to
                // set, from max health; the frame has to be its own because fitting Cartur's art
                // to the fill is not something a size can express.
                if (Ctrl() && m_element.OnFrame != null)
                    m_element.FrameScale = Mathf.Clamp(m_element.FrameScale + step, MinScale, MaxScale);
                else if (Shift() && m_element.OnLength != null)
                    m_element.Length = Mathf.Clamp(m_element.Length + step, MinScale, MaxScale);
                else
                    m_element.Scale = Mathf.Clamp(m_element.Scale * factor, MinScale, MaxScale);

                Apply(m_element);
                Save(m_element);
                // Written to disk once the wheel stops, not on every notch: each write hitched a
                // frame and fired the config watcher.
                s_persistAt = Time.realtimeSinceStartup + 0.5f;
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
            var box = e.Overlay != null ? (RectTransform)e.Overlay.transform : e.Hit;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(box, screen, cam, out Vector2 local))
                return false;

            Rect r = box.rect;
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
            var box = e.Overlay != null ? (RectTransform)e.Overlay.transform : e.Hit;
            Vector3 centre = parent.InverseTransformPoint(box.TransformPoint(box.rect.center));
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

        /// <summary>
        /// Sizes a piece's outline and grab area to what it actually draws. The outline used to be
        /// the registered hit rect - for the inventory screen that is vanilla's old frame - and
        /// Cartur's boards are other shapes, so the red boxes did not match the panels they belong
        /// to (2026-10-03). The box is the bounds of every visible Graphic under the piece, the
        /// outline's own excluded; with nothing visible it stays the hit rect.
        /// </summary>
        private static void FitOverlay(Element e)
        {
            if (e.Overlay == null || e.Move == null || e.Hit == null || !e.Overlay.activeInHierarchy)
                return;
            var rt = (RectTransform)e.Overlay.transform;
            var c = new Vector3[4];
            bool any = false;
            Vector2 lo = Vector2.zero, hi = Vector2.zero;
            foreach (Graphic g in e.Move.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.enabled || g.color.a < 0.02f || g.transform.IsChildOf(rt))
                    continue;
                if (g is Image img && img.sprite == null && !(g is RawImage))
                    continue;
                g.rectTransform.GetWorldCorners(c);
                for (int i = 0; i < 4; i += 2)
                {
                    Vector2 p = e.Hit.InverseTransformPoint(c[i]);
                    lo = any ? Vector2.Min(lo, p) : p;
                    hi = any ? Vector2.Max(hi, p) : p;
                    any = true;
                }
            }
            if (!any)
            {
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                return;
            }
            Rect r = e.Hit.rect;
            rt.offsetMin = lo - r.min;
            rt.offsetMax = hi - r.max;
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
            e.Entry.Value = Format(Pos(e), e.Scale, e.Length, e.FrameScale, e.FrameOffset);
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
