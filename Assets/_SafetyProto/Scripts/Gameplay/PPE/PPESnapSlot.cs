using System.Collections.Generic;
using SafetyProto.Core.Logging;
using SafetyProto.Data.Enums;
using UnityEngine;

namespace SafetyProto.Gameplay.PPE
{
    /// <summary>
    /// Placed on each body slot (head, hand, chest, hips).
    /// Detects hovering PPE items via trigger, confirms snap on item release,
    /// and reports worn state to PPEManager.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PPESnapSlot : MonoBehaviour
    {
        [Header("Slot Identity")]
        [Tooltip("PPE types this slot accepts.")]
        [SerializeField] private PPEType[] acceptedTypes;

        [Header("Visual Feedback")]
        [Tooltip("Optional renderer to highlight when a compatible item is hovering.")]
        [SerializeField] private Renderer highlightRenderer;
        [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.1f);
        [SerializeField] private Color hoverColor = new Color(0.2f, 0.8f, 0.2f, 0.5f);
        [SerializeField] private Color occupiedColor = new Color(0.2f, 0.4f, 1f, 0.5f);

        // Currently snapped item — null if slot is empty
        public PPESnapItem SnappedItem { get; private set; }
        public bool IsOccupied => SnappedItem != null && SnappedItem.gameObject != null;

        // Track overlap counts per item to avoid flicker with compound colliders / multiple trigger pairs.
        private readonly Dictionary<PPESnapItem, int> _hoverCounts = new Dictionary<PPESnapItem, int>();
        private Material _highlightMaterial;
        private PPEManager _ppeManager;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;

            if (highlightRenderer != null)
            {
                _highlightMaterial = highlightRenderer.material;
                SetHighlight(idleColor);
            }
        }

        private void Start()
        {
            _ppeManager = FindFirstObjectByType<PPEManager>();

            if (_ppeManager == null)
            {
                SafetyLog.Error($"PPESnapSlot on {name}: PPEManager not found.", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_highlightMaterial != null)
                Destroy(_highlightMaterial);
        }

        public bool Accepts(PPEType type)
        {
            foreach (var t in acceptedTypes)
                if (t == type) return true;
            return false;
        }

        // Called by PPESnapItem when it enters this trigger
        public void OnItemEntered(PPESnapItem item)
        {
            if (item == null) return;
            if (!Accepts(item.PpeType)) return;
            if (IsOccupied) return;

            _hoverCounts.TryGetValue(item, out int count);
            _hoverCounts[item] = count + 1;
            UpdateHighlight();
        }

        // Called by PPESnapItem when it exits this trigger
        public void OnItemExited(PPESnapItem item)
        {
            if (item == null) return;

            if (_hoverCounts.TryGetValue(item, out int count))
            {
                count -= 1;
                if (count <= 0)
                    _hoverCounts.Remove(item);
                else
                    _hoverCounts[item] = count;
            }
            UpdateHighlight();
        }

        // Called by PPESnapItem on release — returns true if snap was accepted.
        // Proximity validation is the item's responsibility; the slot only gates on type and occupancy.
        public bool TryAcceptSnap(PPESnapItem item)
        {
            if (item == null) return false;
            if (!Accepts(item.PpeType)) return false;
            if (IsOccupied) return false;

            SnappedItem = item;
            _hoverCounts.Remove(item);
            UpdateHighlight();
            _ppeManager.ReportPPEStateChange(item.PpeType, true, item.gameObject);
            SafetyLog.Info($"PPESnapSlot [{name}]: accepted {item.PpeType}", this);
            return true;
        }

        // Called by PPESnapItem when it unsnaps
        public void OnItemUnsnapped(PPESnapItem item)
        {
            if (SnappedItem == item)
            {
                SnappedItem = null;
                UpdateHighlight();
                _ppeManager.ReportPPEStateChange(item.PpeType, false, item.gameObject);
                SafetyLog.Info($"PPESnapSlot [{name}]: released {item.PpeType}", this);
            }
        }

        private void UpdateHighlight()
        {
            if (_highlightMaterial == null) return;

            if (IsOccupied)
                SetHighlight(occupiedColor);
            else if (_hoverCounts.Count > 0)
                SetHighlight(hoverColor);
            else
                SetHighlight(idleColor);
        }

        private void SetHighlight(Color color)
        {
            if (_highlightMaterial != null)
                _highlightMaterial.color = color;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsOccupied ? Color.blue : Color.green;
            var col = GetComponent<Collider>();
            if (col is SphereCollider sc)
                Gizmos.DrawWireSphere(transform.position, sc.radius * transform.lossyScale.x);
            else
                Gizmos.DrawWireCube(transform.position, transform.lossyScale);
        }
#endif
    }
}
