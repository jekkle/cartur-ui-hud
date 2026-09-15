using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// One bar, driven entirely by this mod. Vanilla's UpdateHealth / UpdateStamina /
    /// UpdateEitr / UpdateAdrenaline are skipped (see VanillaBars), which is the same call
    /// AugaLite makes and for the same reason: every value otherwise has to be smuggled through
    /// a prefix, and vanilla writes the rect back the next frame regardless.
    ///
    /// What is kept from vanilla is GuiBar. It owns the fast/slow pair that makes a hit drain
    /// sharply and then catch up, which is worth having, so the fill is still driven by giving
    /// GuiBar a width and a value rather than by setting its rect directly.
    ///
    /// The bar's rect is the WINDOW - the fill and nothing else. The frame is grown outside it
    /// by its own ornament widths so the knot sits off the left end and the point off the right,
    /// its stretched middle covering the window, its centre transparent.
    /// </summary>
    internal sealed class BarSkin
    {


        public RectTransform Panel;   // owns the rect: the window
        public RectTransform Inner;   // carries the frame. Same object as Panel on health.
        public GuiBar Fast, Slow;
        public Image Frame;
        public TMP_Text Text;

        /// <summary>Height as a multiple of AssetLoader.BaseBarHeight. Wheel in edit mode.</summary>
        public float Height = 1f;

        /// <summary>Multiplies the length per point of max stat. Shift+wheel in edit mode.</summary>
        public float LengthScale = 1f;

        /// <summary>
        /// The max value this bar is drawn full-length at. Health tops out around 325 and
        /// adrenaline at 100, so without a per-bar figure the adrenaline bar would be a stub
        /// beside the health bar. With it, each bar reaches the same length at its own ceiling.
        /// </summary>
        public float FullStat = 325f;

        /// <summary>How the frame sits around the fill. Ctrl+wheel and ctrl+drag in edit mode.</summary>
        public float FrameScale = 1f;

        /// <summary>
        /// A nudge ON TOP of the resting offset, not an absolute position. The resting offset is
        /// pure geometry - it falls out of the two ornament widths - so storing the final number
        /// meant a saved layout went wrong the moment the cuts changed. As a delta it survives
        /// re-cutting the art, and zero means "wherever the geometry says".
        /// </summary>
        public Vector2 FrameOffset = Vector2.zero;

        private float m_window = -1f;

        public bool Valid => Panel != null && Fast != null && Slow != null;

        // --- edit mode ---

        public void ApplyHeight(float multiplier)
        {
            Height = Mathf.Max(0.1f, multiplier);
            m_window = -1f;   // force the next Drive to re-lay the rect
            RefreshFrame();
        }

        public void ApplyLength(float multiplier)
        {
            LengthScale = Mathf.Max(0.1f, multiplier);
            m_window = -1f;
        }

        public void ApplyFrame(float scale, Vector2 offset)
        {
            FrameScale = Mathf.Max(0.1f, scale);
            FrameOffset = offset;
            RefreshFrame();
        }

        /// <summary>
        /// Where the frame rests with no nudge. The ornaments are not symmetric - a 118px knot
        /// against a 95px point - so a frame left centred hangs wrong; this is the shift that
        /// puts each end where the art expects it.
        /// </summary>
        private Vector2 RestingOffset()
        {
            float k = Height * FrameScale;
            return new Vector2((AssetLoader.FrameRightUnits - AssetLoader.FrameLeftUnits) * k * 0.5f, 0f);
        }

        // --- per frame ---

        public void Drive(float current, float max)
        {
            if (!Valid)
                return;

            // At FullStat the window is the full natural middle of Bar.png. Height is
            // deliberately NOT a factor: it would tie length to thickness, and then bars of
            // different thicknesses could never be made the same length.
            float window = Mathf.Max(
                max / FullStat * HudSkin.MaxWindowUnits * LengthScale,
                AssetLoader.MinWindowUnits);

            // The rect only changes when max health does, so this is untouched most frames.
            if (!Mathf.Approximately(window, m_window))
            {
                m_window = window;
                Panel.sizeDelta = new Vector2(window, AssetLoader.BaseBarHeight * Height);
                if (Inner != null && Inner != Panel)
                    Inner.sizeDelta = Vector2.zero;   // stretch to the panel
                Fast.SetWidth(window);
                Slow.SetWidth(window);
                SizeFill(Fast);
                SizeFill(Slow);
                RefreshFrame();
            }

            Fast.SetMaxValue(max);
            Fast.SetValue(current);
            Slow.SetMaxValue(max);
            Slow.SetValue(current);

            if (Text != null)
            {
                string label = Mathf.CeilToInt(current).ToString();
                if (Text.text != label)
                    Text.text = label;
            }
        }

        public void Show(bool visible)
        {
            if (Panel != null && Panel.gameObject.activeSelf != visible)
                Panel.gameObject.SetActive(visible);
        }

        // --- geometry ---

        private void RefreshFrame()
        {
            if (Frame == null)
                return;

            // Ornament size and the rect that holds them come off the same number, so the drawn
            // 9-slice border always lands exactly on the rect's edge and cannot drift apart.
            float k = Height * FrameScale;
            Frame.pixelsPerUnitMultiplier = 1f / k;

            var rect = (RectTransform)Frame.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = new Vector2(
                (AssetLoader.FrameLeftUnits + AssetLoader.FrameRightUnits) * k,
                AssetLoader.FrameRailUnits * 2f * k);
            rect.anchoredPosition = RestingOffset() + FrameOffset;
        }

        private void SizeFill(GuiBar guiBar)
        {
            RectTransform rt = guiBar?.m_bar;
            if (rt == null)
                return;

            // GuiBar only ever writes the fill's width, so the height has to be set here or the
            // fill sits short inside a taller frame.
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, AssetLoader.BaseBarHeight * Height);

            Image fill = rt.GetComponent<Image>();
            if (fill != null)
                fill.pixelsPerUnitMultiplier = 1f / Height;
        }
    }
}
