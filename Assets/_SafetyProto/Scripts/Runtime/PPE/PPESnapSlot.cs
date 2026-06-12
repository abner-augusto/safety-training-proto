using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using SafetyProto.Runtime.Task;
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

        [Tooltip("Raised when a valid item is snapped out of the task's expected order. " +
                 "Passes the attempted PPEType so the TaskFeedbackController can nudge the player.")]
        public UnityEvent<PPEType> onWrongOrderSnapAttempted;

        [Header("Task Integration")]
        [Tooltip("Opcional. Mapeia PPEType → ActionTypeSO para emissão de ActionAttemptedEvent por tipo de EPI encaixado.")]
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

        [Tooltip("De-flicker (B10): seconds to keep the hover highlight after the last hover blip " +
                 "clears, so a flickering boundary trigger (e.g. boot MeshCollider vs slope sphere) " +
                 "doesn't strobe the highlight on/off.")]
        [SerializeField, Range(0f, 0.5f)] private float hoverHoldSeconds = 0.15f;

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
        private float _hoverHoldUntil;
        private bool _hoverVisualActive;
        private Material _highlightMaterial;
        private PPEManager _ppeManager;
        private TaskManager _taskManager;

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

            // Optional: only needed for the wrong-order guard. If absent, the guard is
            // skipped and the slot behaves as before (TryAcceptSnap null-checks it).
            _taskManager = FindFirstObjectByType<TaskManager>();
            if (_taskManager == null)
                SafetyLog.Warning($"PPESnapSlot on {name}: TaskManager not found — verificação de ordem desativada.", this);
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

            var ppeItem = item.GetComponent<PPEItem>();

            // Reject distractors before the type check. A distractor shares the
            // valid PPEType (ex: capacete sem jugular ainda é Helmet), so Accepts()
            // would otherwise pass and the decoy would be reported as worn — exactly
            // the opposite of the educational intent. Firing here, before Accepts(),
            // means the slot the item legitimately hovers always triggers the popup.
            if (ppeItem != null && ppeItem.isDistractor)
            {
                onDistractorSnapAttempted?.Invoke(item.PpeType);
                SafetyLog.Info($"PPESnapSlot [{name}]: distrator '{item.name}' ({item.PpeType}) rejeitado.", this);
                return false;
            }

            if (!Accepts(item.PpeType)) return false;
            if (!HasAvailableSpace) return false;

            if (ContainsItem(item)) return false;

            // Order guard (equip-set model). If this PPE belongs to a later step than the current
            // one, reject the snap and nudge the player: it must NOT equip, hide, or register as
            // worn out of order — otherwise a hideWhenEquipped item (gloves/goggles) would vanish
            // and silently count. Within a step (gloves L/R) any order is fine. No-op for FreeOrder
            // groups, current/prior-step PPE, or when TaskManager is absent.
            if (_taskManager != null && _taskManager.IsPpeAheadOfCurrentStep(item.PpeType))
            {
                onWrongOrderSnapAttempted?.Invoke(item.PpeType);
                SafetyLog.Info($"PPESnapSlot [{name}]: '{item.name}' ({item.PpeType}) fora de ordem — snap rejeitado.", this);
                return false;
            }

            _snappedItems.Add(item);
            _hoverCounts.Remove(item);
            UpdateHighlight();

            if (IsLocked)
                item.SetGrabEnabled(false);

            if (ppeItem != null && ppeItem.hideWhenEquipped)
                item.gameObject.SetActive(false);

            // Register as worn AFTER the optional hide. SetActive(false) triggers
            // PPEItem.OnDisable -> PPEManager.UnregisterIfOwned, which would immediately
            // clear a worn flag set before the hide. That ordering bug made
            // hideWhenEquipped items (gloves, goggles) report worn then instantly
            // un-worn, so they never satisfied cumulative PPE requirements. Proximity
            // eviction skips inactive objects, so a hidden-but-worn item stays compliant.
            _ppeManager?.ReportPPEStateChange(item.PpeType, true, item.gameObject);

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

        private void Update()
        {
            // Drive the end of the hover-hold window even when no enter/exit events arrive this
            // frame, so the highlight eventually settles back to idle after a flicker burst.
            if (_hoverVisualActive && _hoverCounts.Count == 0 && Time.time >= _hoverHoldUntil)
                UpdateHighlight();
        }

        private void UpdateHighlight()
        {
            if (_highlightMaterial == null) return;

            if (IsOccupied)
            {
                _hoverVisualActive = false;
                SetHighlight(occupiedColor);
                return;
            }

            if (_hoverCounts.Count > 0)
            {
                // Active hover — show it and (re)arm the hold window.
                _hoverVisualActive = true;
                _hoverHoldUntil = Time.time + hoverHoldSeconds;
                SetHighlight(hoverColor);
            }
            else if (_hoverVisualActive && Time.time < _hoverHoldUntil)
            {
                // Hover blipped off, but we're inside the hold window — keep it steady to ride
                // out a flickering boundary trigger.
                SetHighlight(hoverColor);
            }
            else
            {
                _hoverVisualActive = false;
                SetHighlight(idleColor);
            }
        }

        private void SetHighlight(Color color)
        {
            if (_highlightMaterial != null)
                _highlightMaterial.color = color;

            // Honor a fully transparent color as "hidden" regardless of shader. The Standard
            // transparent shader still renders specular highlights and glossy reflections at
            // alpha 0, so a faint shiny sphere would otherwise remain visible (e.g. an occupied
            // slot whose item is hideWhenEquipped). Toggling the renderer is shader-independent
            // and applies per-state (idle/hover/occupied) based on each color's alpha.
            if (highlightRenderer != null)
                highlightRenderer.enabled = color.a > 0.001f;
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
