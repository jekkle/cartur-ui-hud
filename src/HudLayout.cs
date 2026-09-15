using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
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
    /// Three of the panels are wrapped in containers (see HudSkin.Wrap) because the game also
    /// rewrites their anchoredPosition every frame. For those the container is what moves while
    /// the panel itself is what the mouse is tested against, since the container has no size.
    /// </summary>
    internal static class HudLayout
    {
        private const float MinScale = 0.3f;
        private const float MaxScale = 3f;
        private const float ScaleStep = 0.05f;

        private class Element
        {
            public string Key;
            public string Label;
            public RectTransform Move;   // what gets positioned and scaled
            public RectTransform Hit;    // what the mouse is tested against
            public ConfigEntry<string> Entry;
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
        }

        private static readonly List<Element> s_elements = new List<Element>();
        private static readonly FieldInfo s_mouseCapture = AccessTools.Field(typeof(GameCamera), "m_mouseCapture");

        private static ConfigFile s_config;
        private static ConfigEntry<KeyboardShortcut> s_editKey;
        private static TMP_FontAsset s_font;
        private static Sprite s_white;

        private static bool s_editing;
        private static Element s_grabbed;
        private static Vector2 s_grabOffset;
        private static bool s_frameDrag;

        internal static BepInEx.Logging.ManualLogSource Log;

        public static bool Editing => s_editing;

        public static void Init(ConfigFile config, ConfigEntry<KeyboardShortcut> editKey)
        {
            s_config = config;
            s_editKey = editKey;
        }

        /// <summary>Called once per Hud.Awake, before anything registers.</summary>
        public static void Reset(TMP_FontAsset font)
        {
            foreach (Element e in s_elements)
                if (e.Overlay != null)
                    UnityEngine.Object.Destroy(e.Overlay);
            s_elements.Clear();
            s_editing = false;
            s_grabbed = null;
            s_font = font;

            if (s_white != null)
                return;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            s_white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        }

        public static void Register(string key, string label, RectTransform move, RectTransform hit, Vector2 fallbackPosition, float fallbackScale = 1f, Action<float> onScale = null, Action<float> onLength = null, Action<float, Vector2> onFrame = null, float fallbackFrameScale = 1f)
        {
            if (move == null || hit == null)
                return;

            var element = new Element
            {
                Key = key,
                Label = label,
                Move = move,
                Hit = hit,
                OnScale = onScale,
                OnLength = onLength,
                OnFrame = onFrame,
                Entry = s_config.Bind("Layout", key, Format(fallbackPosition, fallbackScale, 1f, fallbackFrameScale, Vector2.zero),
                    "Layout of the " + label + ". Written by edit mode; x,y,size,length,frameSize,frameX,frameY.")
            };

            if (!TryParse(element.Entry.Value, out Vector2 position, out float scale, out float length, out float frameScale, out Vector2 frameOff))
            {
                position = fallbackPosition;
                scale = fallbackScale;
                length = 1f;
                frameScale = fallbackFrameScale;
                frameOff = Vector2.zero;
                Log.LogWarning("could not read layout for " + key + " (\"" + element.Entry.Value + "\") - using the default");
            }

            element.Scale = Mathf.Clamp(scale, MinScale, MaxScale);
            element.Length = Mathf.Clamp(length, MinScale, MaxScale);
            element.FrameScale = Mathf.Clamp(frameScale, MinScale, MaxScale);
            element.FrameOffset = frameOff;
            move.anchoredPosition = position;
            Apply(element);

            s_elements.Add(element);
        }

        /// <summary>Driven from the Hud.Update postfix.</summary>
        public static void Tick()
        {
            if (s_editKey.Value.IsDown())
                Toggle();

            if (!s_editing)
                return;

            Vector2 mouse = Input.mousePosition;

            if (Input.GetMouseButtonDown(0))
                Grab(mouse);
            else if (Input.GetMouseButton(0) && s_grabbed != null)
            {
                Vector2 local = ToLocal(s_grabbed.Move, mouse) - s_grabOffset;
                if (s_frameDrag)
                {
                    s_grabbed.FrameOffset = local;
                    Apply(s_grabbed);
                }
                else
                {
                    s_grabbed.Move.anchoredPosition = local;
                }
            }
            else if (Input.GetMouseButtonUp(0) && s_grabbed != null)
            {
                Save(s_grabbed);
                s_grabbed = null;
            }

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                Element under = s_grabbed ?? Find(mouse);
                if (under != null)
                {
                    float step = Mathf.Sign(wheel) * ScaleStep;
                    // Plain wheel is size, shift is the bar's length, ctrl is the frame around
                    // it. Length has to be a control of its own because it is otherwise the
                    // game's to set, from max health; the frame has to be its own because
                    // fitting Cartur's art to the fill is not something a size can express.
                    if (Ctrl() && under.OnFrame != null)
                        under.FrameScale = Mathf.Clamp(under.FrameScale + step, MinScale, MaxScale);
                    else if (Shift() && under.OnLength != null)
                        under.Length = Mathf.Clamp(under.Length + step, MinScale, MaxScale);
                    else
                        under.Scale = Mathf.Clamp(under.Scale + step, MinScale, MaxScale);
                    Apply(under);
                    Save(under);
                }
            }
        }

        private static void Toggle()
        {
            s_editing = !s_editing;

            foreach (Element e in s_elements)
            {
                EnsureOverlay(e);
                if (e.Overlay != null)
                    e.Overlay.SetActive(s_editing);
            }

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
            {
                s_grabbed = null;
                s_config.Save();
            }

            Log.LogInfo(s_editing
                ? "layout edit on - drag: move | wheel: size | shift+wheel: bar length | ctrl+wheel: frame size | ctrl+drag: frame offset | " + s_editKey.Value + ": finish"
                : "layout edit off - saved to " + s_config.ConfigFilePath);
        }

        private static void Grab(Vector2 mouse)
        {
            s_grabbed = Find(mouse);
            if (s_grabbed == null)
                return;

            s_frameDrag = Ctrl() && s_grabbed.OnFrame != null;
            Vector2 anchor = s_frameDrag ? s_grabbed.FrameOffset : s_grabbed.Move.anchoredPosition;
            s_grabOffset = ToLocal(s_grabbed.Move, mouse) - anchor;
        }

        private static bool Ctrl() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        private static bool Shift() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        /// <summary>Topmost element under the cursor, so overlapping pieces pick the front one.</summary>
        private static Element Find(Vector2 mouse)
        {
            Element best = null;
            int bestDepth = -1;
            foreach (Element e in s_elements)
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(e.Hit, mouse, Cam(e.Hit)))
                    continue;
                int depth = e.Hit.GetSiblingIndex();
                if (depth >= bestDepth)
                {
                    best = e;
                    bestDepth = depth;
                }
            }
            return best;
        }

        private static Vector2 ToLocal(RectTransform rt, Vector2 mouse)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt.parent as RectTransform, mouse, Cam(rt), out Vector2 local);
            return local;
        }

        // Overlay canvases hit-test with a null camera; a camera-space canvas needs its own.
        private static Camera Cam(RectTransform rt)
        {
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null)
                return null;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
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

            Image tint = e.Overlay.AddComponent<Image>();
            tint.sprite = s_white;
            tint.color = new Color(0.90f, 0.70f, 0.12f, 0.25f);
            tint.raycastTarget = false;

            Outline outline = e.Overlay.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.92f, 0.25f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = false;

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
            text.color = Color.white;
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
