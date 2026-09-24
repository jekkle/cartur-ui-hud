using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CarturUIHud
{
    /// <summary>
    /// The item picture lifts when the pointer is over its cell, and settles back when it
    /// leaves. Every cell the game builds from an InventoryElement gets it: the bag, a chest,
    /// the equipment panel, the shield and quiver slots.
    ///
    /// Only the icon moves. The cell's border stays exactly where it is, so the grid does not
    /// ripple as the pointer crosses it - the item is what answers.
    ///
    /// Unity's own pointer interfaces, not the game's: a cell already carries Valheim's
    /// UIInputHandler, and adding a second handler to the same object is supported - every
    /// component implementing the interface is called. So nothing of the game's is patched, and
    /// a cell that stops being clickable stops lifting, which is the right pairing.
    ///
    /// It stays enabled. The first cut disabled itself once it reached the size it was going
    /// to, to save the Update - and that silently killed the whole feature: ExecuteEvents skips
    /// any Behaviour whose isActiveAndEnabled is false, so OnPointerEnter never fired and
    /// nothing could ever switch it back on. Resting costs one float compare per cell instead.
    /// </summary>
    internal sealed class IconHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>How much bigger the icon draws under the pointer.</summary>
        private const float Peak = 1.12f;

        /// <summary>Scale per second. A tenth of a second across, which reads as a lift.</summary>
        private const float Speed = 1.2f;

        private Transform m_icon;
        private float m_at = 1f;
        private float m_want = 1f;

        private void Awake() => m_icon = transform.Find("icon");

        public void OnPointerEnter(PointerEventData eventData) => Aim(Peak);

        public void OnPointerExit(PointerEventData eventData) => Aim(1f);

        private void Aim(float want) => m_want = want;

        // The cell is pooled: the grid switches elements off rather than destroying them, so an
        // icon left mid-lift would come back that size on a different item.
        private void OnDisable()
        {
            if (!Mathf.Approximately(m_at, 1f))
            {
                m_at = m_want = 1f;
                if (m_icon != null)
                    m_icon.localScale = Vector3.one;
            }
        }

        private void Update()
        {
            if (m_icon == null || m_at == m_want)
                return;

            // Unscaled: the inventory is open while the world still runs, and a paused game
            // should not freeze a lift half way.
            m_at = Mathf.MoveTowards(m_at, m_want, Speed * Time.unscaledDeltaTime);
            m_icon.localScale = Vector3.one * m_at;
        }
    }

    /// <summary>
    /// Hangs the lift on every cell, in the postfix that runs whenever a grid is built or
    /// rebuilt. Not on the element prefab: the prefab is skinned once per InventoryGui.Awake,
    /// and cells that already exist would never get it.
    /// </summary>
    internal static class IconHoverPatch
    {
        private static readonly FieldInfo s_elements = AccessTools.Field(typeof(InventoryGrid), "m_elements");

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        [HarmonyPostfix]
        private static void Hang(InventoryGrid __instance)
        {
            if (!(s_elements?.GetValue(__instance) is List<InventoryElement> elements))
                return;

            foreach (InventoryElement element in elements)
            {
                if (element != null && element.GetComponent<IconHover>() == null)
                    element.gameObject.AddComponent<IconHover>();
            }
        }
    }
}
