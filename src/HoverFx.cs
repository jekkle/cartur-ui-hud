using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CarturUIHud
{
    /// <summary>
    /// One hover, the same on every button in the game (Cartur, 2026-10-04: "make sure all of our
    /// buttons on all screens have a hover animation and make it the same on every button"). While
    /// the pointer is on a button, or a gamepad has it selected, its label brightens toward
    /// gold-white and the button eases up 5%; pressed, the label dips. Eased over 0.1 s of
    /// unscaled time, so it works on the pause menu too.
    ///
    /// Added to every Button, Toggle and Dropdown as it is enabled - none has an OnEnable of its own,
    /// so the hook is Selectable's. Toggles and dropdowns are buttons to the player: Epic Loot's
    /// mode, rarity and augment selectors are all Toggles (pilot dump, 2026-10-05). The label is the
    /// button's own text - TMP or legacy UI.Text, which is what Epic Loot uses - not a gamepad hint
    /// or an input placeholder. A button with no text (an icon button) only grows.
    ///
    /// A button with no picture of its own inside another Selectable is a hit area over a row (Epic
    /// Loot's ItemElement/Button, a null-sprite Image); growing it would show nothing, so the row is
    /// what grows and lights.
    ///
    /// The size it returns to is re-read whenever the button is at rest and something else has
    /// resized it, so layout code that scales buttons (CraftingBoard fits the tabs by scaling) keeps
    /// working and the hover never compounds.
    /// </summary>
    internal sealed class HoverFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        private const float Grow = 1.05f;
        private const float Seconds = 0.1f;
        private static readonly Color Lit = new Color(1f, 0.93f, 0.7f, 1f);

        private Selectable m_sel;
        private Transform m_target;

        /// <summary>
        /// What grows, when it is not the button itself - a hotbar slot's hit area is an invisible
        /// bag cell laid over the hotbar box, so the box is what has to grow (QuickSlots.HostOn).
        /// </summary>
        internal Transform Target
        {
            get => m_target != null ? m_target : transform;
            set
            {
                if (m_t > 0f) { m_t = 0f; Apply(); }
                m_target = value;
                m_rest = m_written = Target.localScale;
            }
        }
        private Graphic m_label;
        private Color m_labelRest;
        private Vector3 m_rest, m_written;
        private bool m_over, m_selected, m_down;
        private float m_t;          // 0 at rest, 1 fully hovered

        [HarmonyPatch(typeof(Selectable), "OnEnable")]
        [HarmonyPostfix]
        private static void Attach(Selectable __instance)
        {
            if ((__instance is Button || __instance is Toggle || __instance is Dropdown || __instance is TMP_Dropdown)
                && __instance.GetComponent<HoverFx>() == null)
                __instance.gameObject.AddComponent<HoverFx>();
        }

        private void Awake()
        {
            m_sel = GetComponent<Selectable>();
            Image own = GetComponent<Image>();
            if ((own == null || own.sprite == null) && transform.parent != null
                && transform.parent.GetComponent<Selectable>() != null)
                m_target = transform.parent;
            foreach (Graphic g in Target.GetComponentsInChildren<Graphic>(true))
                if ((g is TMP_Text || g is Text) && g.name != "Placeholder"
                    && !g.transform.parent.name.StartsWith("gamepad_hint")) { m_label = g; break; }
            m_rest = m_written = Target.localScale;
            if (m_label != null)
                m_labelRest = m_label.color;
        }

        public void OnPointerEnter(PointerEventData e) => m_over = true;
        public void OnPointerExit(PointerEventData e) { m_over = false; m_down = false; }
        public void OnPointerDown(PointerEventData e) => m_down = true;
        public void OnPointerUp(PointerEventData e) => m_down = false;
        public void OnSelect(BaseEventData e) => m_selected = true;
        public void OnDeselect(BaseEventData e) => m_selected = false;

        private void OnDisable()
        {
            m_over = m_selected = m_down = false;
            // Only undo what a hover did. A button switched off before it was ever hovered - the split
            // dialog's, at Awake - had its label set from a colour never read, and went invisible.
            if (m_t > 0f)
            {
                m_t = 0f;
                Apply();
            }
        }

        private void LateUpdate()
        {
            bool hot = (m_over || m_selected) && (m_sel == null || m_sel.IsInteractable());
            // At rest and resized by someone else: that is the new size to return to. The label's
            // colour likewise - the game recolours labels (an unaffordable recipe, a selected tab).
            if (m_t <= 0f && !hot)
            {
                if (Target.localScale != m_written)
                    m_rest = m_written = Target.localScale;
                if (m_label != null)
                    m_labelRest = m_label.color;
                return;
            }
            m_t = Mathf.MoveTowards(m_t, hot ? 1f : 0f, Time.unscaledDeltaTime / Seconds);
            Apply();
        }

        private void Apply()
        {
            float grow = Mathf.Lerp(1f, m_down ? 0.98f : Grow, m_t);
            m_written = m_rest * grow;
            Target.localScale = m_written;
            if (m_label != null)
            {
                Color lit = m_down ? Color.Lerp(m_labelRest, Lit, 0.4f) : Lit;
                lit.a = m_labelRest.a;
                m_label.color = Color.Lerp(m_labelRest, lit, m_t);
            }
        }
    }
}
