using UnityEngine;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// Every slider on Cartur's slider art (Grok render 2 he picked, 2026-10-04, bronze matched to
    /// board_grid.png's rims): a wood groove with knotwork end caps, an amber fill, a bronze knob.
    ///
    /// Vanilla draws them with plain white Images (no sprite) and the round checkbox_marker as the
    /// handle, so the sprite table in Skin cannot reach them by name. Read off the pilot's dump of
    /// the split stack, barber, new-character and World Modifiers sliders - all the same shape:
    /// Background (the track, ~half the slider's height), Fill Area/Fill, Handle Slide Area/Handle
    /// (taller than the track). The slider's own fillRect and handleRect are used; Background by
    /// name, and a slider without one keeps no track.
    /// </summary>
    internal static class SliderSkin
    {
        // board_slider_track.png: 1180x61, knotwork caps 47 px each end.
        private const float CapPx = 47f;
        private const float KnobScale = 0.525f;   // 0.7, then 25% smaller (Cartur, 2026-10-05)

        private static Sprite s_track, s_fill, s_knob;

        internal static void Dress(Slider slider)
        {
            if (!Load())
                return;
            if (slider.transform.Find("Background")?.GetComponent<Image>() is Image track)
            {
                track.sprite = s_track;
                track.type = Image.Type.Sliced;
                track.color = Color.white;
                // Caps scaled to the track's height, so they stay square knots at any size.
                float h = track.rectTransform.rect.height;
                if (h > 0f)
                    track.pixelsPerUnitMultiplier = s_track.rect.height / h;
            }
            if (slider.fillRect?.GetComponent<Image>() is Image fill)
            {
                fill.sprite = s_fill;
                fill.type = Image.Type.Simple;
                fill.color = Color.white;
            }
            if (slider.handleRect?.GetComponent<Image>() is Image knob)
            {
                knob.sprite = s_knob;
                knob.type = Image.Type.Simple;
                knob.preserveAspect = true;   // the handle box is taller than wide
                knob.color = Color.white;
                // Filling the handle box drew it about five times the track's height - "a bit big"
                // (Cartur, 2026-10-05). Scaled on the transform only; the slider's drag maths reads
                // the handle's anchors, not its scale.
                knob.rectTransform.localScale = new Vector3(KnobScale, KnobScale, 1f);
            }
        }

        private static bool Load()
        {
            if (s_track != null)
                return true;
            Texture2D t = AssetLoader.Board("slider_track"), f = AssetLoader.Board("slider_fill"), k = AssetLoader.Board("slider_knob");
            if (t == null || f == null || k == null)
                return false;
            s_track = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0,
                                    SpriteMeshType.FullRect, new Vector4(CapPx, 0f, CapPx, 0f));
            s_fill = Sprite.Create(f, new Rect(0, 0, f.width, f.height), new Vector2(0.5f, 0.5f), 100f);
            s_knob = Sprite.Create(k, new Rect(0, 0, k.width, k.height), new Vector2(0.5f, 0.5f), 100f);
            // Named ours, so Skin's sprite table leaves them alone on a second walk.
            s_track.name = "cartur_slider_track";
            s_fill.name = "cartur_slider_fill";
            s_knob.name = "cartur_slider_knob";
            return true;
        }
    }
}
