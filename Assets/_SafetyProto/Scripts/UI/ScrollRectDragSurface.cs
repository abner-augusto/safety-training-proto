using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SafetyProto.UI
{
    /// <summary>Forwards drag gestures from a larger UI surface to a nested scroll view.</summary>
    public sealed class ScrollRectDragSurface : MonoBehaviour,
        IInitializePotentialDragHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IScrollHandler
    {
        [SerializeField] private ScrollRect target;

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (target != null) target.OnInitializePotentialDrag(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (target != null) target.OnBeginDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target != null) target.OnDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (target != null) target.OnEndDrag(eventData);
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (target != null) target.OnScroll(eventData);
        }
    }
}
