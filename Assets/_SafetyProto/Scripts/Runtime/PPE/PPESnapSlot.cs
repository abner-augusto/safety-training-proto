using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.Runtime.PPE
{
    [RequireComponent(typeof(Collider))]
    public class PPESnapSlot : MonoBehaviour, ISessionResettable
    {
        private const int MaxCapacity = 3;
        private const int DefaultCapacity = 1;

        [Header("Capacity")]
        [Tooltip("How many items this slot can hold simultaneously (1-3).")]
        [Range(1, 3)]
        [SerializeField] private int slotCapacity = DefaultCapacity;

        [Header("Behavior")]
        [Tooltip("Impede que o item seja removido do slot após ser equipado.")]
        [SerializeField] private bool lockAfterEquipped;

        public bool IsLocked => lockAfterEquipped && OccupiedCount > 0 && !_unlocked;

        private bool _unlocked;

        [Header("Events")]
        [Tooltip("Disparado quando um item distrator tenta encaixar neste slot. " +
                 "Passa o PPEType tentado para o TaskFeedbackController.")]
        public UnityEvent<PPEType> onDistractorSnapAttempted;

        [Header("Task Integration")]
        [Tooltip("Opcional. Mapeia PPEType → ActionTypeSO para emissão de ActionAttemptedEvent por tipo de EPI encaixado.\nSubstitui PpeTaskMapping para tarefas simples de equipar.")]
        [SerializeField] private PPEActionMapping[] ppeActionMappings;

        [System.Serializable]
        public struct PPEActionMapping
        {
            public PPEType ppeType;
            public ActionTypeSO action;
        }

        [Header("Visual Feedback")]
        [Tooltip("Optional renderer to highlight when a compatible item is hovering.")]
        [SerializeField] private Renderer highlightRenderer;
        [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.1f);
        [SerializeField] private Color hoverColor = new Color(0.2f, 0.8f, 0.2f, 0.5f);
        [SerializeField] private Color occupiedColor = new Color(0.2f, 0.4f, 1f, 0.5f);

#if UNITY_EDITOR
        [Header("Editor Preview")]
        [Tooltip("PPE item prefabs (or scene instances) to preview at this slot's snap pose. Editor-only — applies the same snapPoseOverride math used at runtime.")]
        [SerializeField] private PPESnapItem[] editorPreviewItems;
        [Tooltip("Tint for the wireframe preview meshes.")]
        [SerializeField] private Color editorPreviewColor = new Color(0.2f, 1f, 0.6f, 0.9f);
        [Tooltip("Draw a solid (semi-transparent) preview in addition to the wireframe.")]
        [SerializeField] private bool editorPreviewSolid = false;
        [Tooltip("Draw the gizmo even when the slot is not selected.")]
        [SerializeField] private bool editorPreviewAlwaysVisible = false;
#endif

        private readonly List<PPESnapItem> _snappedItems = new List<PPESnapItem>();
        private readonly HashSet<PPEType> _emittedActions = new HashSet<PPEType>();

        public PPESnapItem SnappedItem => _snappedItems.Count > 0 ? _snappedItems[0] : null;
        public IReadOnlyList<PPESnapItem> SnappedItems => _snappedItems;
        public int OccupiedCount => _snappedItems.Count;
        public bool IsOccupied => _snappedItems.Count >= (_slotCapacity > 0 ? _slotCapacity : slotCapacity);
        public bool HasAvailableSpace => _snappedItems.Count < (_slotCapacity > 0 ? _slotCapacity : slotCapacity);

        private int _slotCapacity;

        private readonly Dictionary<PPESnapItem, int> _hoverCounts = new Dictionary<PPESnapItem, int>();
        private Material _highlightMaterial;
        private PPEManager _ppeManager;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;

            EnsureCapacityInitialized();

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
            if (ppeActionMappings != null && ppeActionMappings.Length > 0)
            {
                for (int i = 0; i < ppeActionMappings.Length; i++)
                    if (ppeActionMappings[i].ppeType == type) return true;
                return false;
            }
            return true;
        }

        public void OnItemEntered(PPESnapItem item)
        {
            if (item == null) return;
            if (!Accepts(item.PpeType)) return;
            if (!HasAvailableSpace) return;

            _hoverCounts.TryGetValue(item, out int count);
            _hoverCounts[item] = count + 1;
            UpdateHighlight();
        }

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

        public bool TryAcceptSnap(PPESnapItem item)
        {
            if (item == null) return false;
            if (!Accepts(item.PpeType))
            {
                var ppeItem = item.GetComponent<PPEItem>();
                if (ppeItem != null && ppeItem.isDistractor)
                {
                    onDistractorSnapAttempted?.Invoke(item.PpeType);
                    SafetyLog.Info($"PPESnapSlot [{name}]: distrator '{item.name}' ({item.PpeType}) rejeitado.", this);
                }
                return false;
            }
            if (!HasAvailableSpace) return false;

            if (ContainsItem(item)) return false;

            _snappedItems.Add(item);
            _hoverCounts.Remove(item);
            UpdateHighlight();
            _ppeManager?.ReportPPEStateChange(item.PpeType, true, item.gameObject);

            if (IsLocked)
                item.SetGrabEnabled(false);

            var ppeItemComp = item.GetComponent<PPEItem>();
            if (ppeItemComp != null && ppeItemComp.hideWhenEquipped)
                item.gameObject.SetActive(false);

            SafetyLog.Info($"PPESnapSlot [{name}]: accepted {item.PpeType} ({_snappedItems.Count}/{_slotCapacity})", this);

            if (!_emittedActions.Contains(item.PpeType))
            {
                foreach (var mapping in ppeActionMappings)
                {
                    if (mapping.ppeType == item.PpeType && mapping.action != null && !string.IsNullOrWhiteSpace(mapping.action.ActionId))
                    {
                        _emittedActions.Add(item.PpeType);
                        ActionEvents.PublishActionAttempt(
                            mapping.action.ActionId,
                            sourceId: name,
                            context: "ppe_snap",
                            position: transform.position);
                        SafetyLog.Info($"PPESnapSlot [{name}]: emitted ActionAttempt '{mapping.action.ActionId}' for {item.PpeType}", this);
                        break;
                    }
                }
            }
            else
            {
                SafetyLog.Info($"PPESnapSlot [{name}]: ação para {item.PpeType} já emitida — re-snap ignorado.", this);
            }

            return true;
        }

        public void SetSlotCapacity(int capacity)
        {
            _slotCapacity = Mathf.Clamp(capacity, 1, MaxCapacity);
            slotCapacity = _slotCapacity;

            while (_snappedItems.Count > _slotCapacity)
            {
                var excess = _snappedItems[_snappedItems.Count - 1];
                RemoveItem(excess);
            }

            UpdateHighlight();
            SafetyLog.Info($"PPESnapSlot [{name}]: capacity set to {_slotCapacity}.", this);
        }

        public void ResetSession()
        {
            _emittedActions.Clear();
        }

        public void Unlock()
        {
            _unlocked = true;

            foreach (var item in _snappedItems)
            {
                if (item != null)
                    item.SetGrabEnabled(true);
            }

            SafetyLog.Info($"PPESnapSlot [{name}]: desbloqueado manualmente.", this);
        }

        public void OnItemUnsnapped(PPESnapItem item)
        {
            if (!ContainsItem(item)) return;

            RemoveItem(item);

            // Allow re-equipping to fire the action again: the task system decides
            // whether re-completion is valid, so blocking it here is too aggressive.
            _emittedActions.Remove(item.PpeType);

            var ppeItemComp = item.GetComponent<PPEItem>();
            if (ppeItemComp != null && ppeItemComp.hideWhenEquipped)
                item.gameObject.SetActive(true);

            item.SetGrabEnabled(true);

            _unlocked = false;
            UpdateHighlight();
            _ppeManager?.ReportPPEStateChange(item.PpeType, false, item.gameObject);
            SafetyLog.Info($"PPESnapSlot [{name}]: released {item.PpeType} ({_snappedItems.Count}/{_slotCapacity})", this);
        }

        private void RemoveItem(PPESnapItem item)
        {
            _snappedItems.Remove(item);
        }

        private bool ContainsItem(PPESnapItem item)
        {
            return _snappedItems.Contains(item);
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

        private void EnsureCapacityInitialized()
        {
            if (_slotCapacity <= 0)
                _slotCapacity = Mathf.Clamp(slotCapacity, 1, MaxCapacity);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _slotCapacity = Mathf.Clamp(slotCapacity, 1, MaxCapacity);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsOccupied ? Color.blue : Color.green;
            var col = GetComponent<Collider>();
            if (col is SphereCollider sc)
                Gizmos.DrawWireSphere(transform.position, sc.radius * transform.lossyScale.x);
            else
                Gizmos.DrawWireCube(transform.position, transform.lossyScale);

            if (!editorPreviewAlwaysVisible)
                DrawEditorPreview();
        }

        private void OnDrawGizmos()
        {
            if (editorPreviewAlwaysVisible)
                DrawEditorPreview();
        }

        private void DrawEditorPreview()
        {
            if (editorPreviewItems == null || editorPreviewItems.Length == 0) return;

            for (int i = 0; i < editorPreviewItems.Length; i++)
            {
                var item = editorPreviewItems[i];
                if (item == null) continue;
                DrawItemPreviewGizmo(item);
            }
        }

        private void DrawItemPreviewGizmo(PPESnapItem item)
        {
            ComputeSnapPose(item, transform.position, transform.rotation, out var targetPos, out var targetRot);

            // Anchor crosshair at the resolved snap pose.
            var prevColor = Gizmos.color;
            var prevMatrix = Gizmos.matrix;
            Gizmos.color = editorPreviewColor;
            Gizmos.matrix = Matrix4x4.TRS(targetPos, targetRot, Vector3.one);
            Gizmos.DrawLine(Vector3.zero, Vector3.right * 0.05f);
            Gizmos.DrawLine(Vector3.zero, Vector3.up * 0.05f);
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.05f);
            Gizmos.matrix = prevMatrix;

            // Each MeshFilter in the item's hierarchy, transformed from item-local space
            // to the resolved snap pose.
            var itemRoot = item.transform;
            var rootScale = itemRoot.lossyScale;
            // Compensate for prefab-asset transforms (which sit at world origin) the same way:
            // worldToLocalMatrix is local-to-root regardless of where the root lives.
            var rootWorldToLocal = itemRoot.worldToLocalMatrix;
            var targetRootMatrix = Matrix4x4.TRS(targetPos, targetRot, rootScale);

            var meshFilters = item.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                var mf = meshFilters[i];
                if (mf == null || mf.sharedMesh == null) continue;

                var meshLocalToRoot = rootWorldToLocal * mf.transform.localToWorldMatrix;
                Gizmos.matrix = targetRootMatrix * meshLocalToRoot;
                Gizmos.color = editorPreviewColor;
                Gizmos.DrawWireMesh(mf.sharedMesh);

                if (editorPreviewSolid)
                {
                    var solid = editorPreviewColor;
                    solid.a *= 0.25f;
                    Gizmos.color = solid;
                    Gizmos.DrawMesh(mf.sharedMesh);
                }
            }

            Gizmos.color = prevColor;
            Gizmos.matrix = prevMatrix;
        }

        /// <summary>
        /// Replicates <see cref="PPESnapItem.ApplySnapPose"/> so editor tooling can preview where
        /// an item will land when snapped, without entering Play Mode.
        /// </summary>
        public static void ComputeSnapPose(PPESnapItem item, Vector3 slotPosition, Quaternion slotRotation,
            out Vector3 targetPosition, out Quaternion targetRotation)
        {
            targetPosition = slotPosition;
            targetRotation = slotRotation;
            if (item == null) return;

            var root = item.transform;
            var pose = item.SnapPoseOverride;
            if (pose == null) return;

            Vector3 overrideLocalPos = root.InverseTransformPoint(pose.position);
            Quaternion overrideLocalRot = Quaternion.Inverse(root.rotation) * pose.rotation;

            targetRotation = slotRotation * Quaternion.Inverse(overrideLocalRot);
            targetPosition = slotPosition - (targetRotation * overrideLocalPos);
        }

        public PPESnapItem[] EditorPreviewItems => editorPreviewItems;
#endif
    }
}
