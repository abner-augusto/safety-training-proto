using System.Collections;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Scoring;
using SafetyProto.Runtime.Task;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SafetyProto.Runtime
{
    public class PhaseController : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private Transform playerRig;
        [Tooltip("Head transform (CenterEyeAnchor). Auto-resolved from playerRig if empty. Used to " +
                 "cancel the room-scale offset when teleporting so the player — not the rig origin — " +
                 "lands centered on the scaffold spawn.")]
        [SerializeField] private Transform playerHead;
        [SerializeField] private Transform spawnPointAndaime;

        [Header("Zonas (opcional)")]
        [Tooltip("GameObjects a desativar ao sair do Canteiro. Deixe vazio se não usar.")]
        [SerializeField] private GameObject[] objectsToHide;
        [Tooltip("GameObjects a ativar ao entrar no Andaime. Deixe vazio se não usar.")]
        [SerializeField] private GameObject[] objectsToShow;

        [Header("Transição")]
        [SerializeField] private float fadeOutDuration = 0.8f;
        [SerializeField] private float holdBlackDuration = 1.5f;
        [SerializeField] private float fadeInDuration = 0.8f;

        [Header("UI de Contexto")]
        [SerializeField] private GameObject transitionPanel;

        [Header("Confirmação")]
        [Tooltip("Popup provider (PopupService) implementing IPopupFeedback. When set, the teleport waits for a confirmation button instead of firing on group completion.")]
        [SerializeField] private MonoBehaviour popupFeedbackProvider;
        [SerializeField] private string confirmTitle = "Fase concluída";
        [SerializeField, TextArea(2, 3)] private string confirmBody = "Você será levado ao andaime para a próxima etapa.";
        [SerializeField] private string confirmButtonLabel = "Ir para o andaime";

        [Header("Botão de Avanço")]
        [Tooltip("Botão visível apenas no modo Avaliação. No modo Guiado é oculto; o popup de confirmação aparece automaticamente quando o grupo completa.")]
        [SerializeField] private GameObject[] advanceButtonObjects;
        [Tooltip("TaskManager da cena. Auto-resolvido se vazio.")]
        [SerializeField] private TaskManager taskManager;
        [Tooltip("Grupo alvo (id). Comparado contra TaskGroupDef.id do JSON.")]
        [SerializeField] private string targetGroupId = "ppe_selection";

        private SafetyProto.Core.Interfaces.IPopupFeedback _popupFeedback;

        [Header("Trigger")]
        [Tooltip("Nome do grupo-gatilho (id). Comparado contra o TaskGroupDef vindo do JSON.")]
        [SerializeField] private string triggerGroupName = string.Empty;

        [Header("Anti-queda no teleporte")]
        [Tooltip("Locomotor do jogador (FirstPersonLocomotor). Desabilitado durante o teleporte para " +
                 "suspender a gravidade até o chão estar pronto. Auto-resolvido a partir do playerRig se vazio.")]
        [SerializeField] private Behaviour playerLocomotor;
        [Tooltip("Layers consideradas 'chão' na sondagem pós-teleporte.")]
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("Distância máxima da sondagem para baixo a partir do spawn.")]
        [SerializeField] private float groundProbeDistance = 5f;
        [Tooltip("Raio do SphereCast da sondagem de chão.")]
        [SerializeField] private float groundProbeRadius = 0.25f;
        [Tooltip("Tempo máximo aguardando o chão registrar antes de religar o locomotor (rede de segurança).")]
        [SerializeField] private float groundWaitTimeout = 3f;

        private bool _transitionExecuted;
        private bool _simulationAutoConfirm;
        private bool _transitionInProgress;
        private bool _simulationTransitionCompleted;
        private bool _advanceConsumed;
        private Behaviour _transitionLocomotor;
        private bool _transitionLocomotorWasEnabled;

        private UnityAction<SessionStartedEventArgs>? _onSessionStarted;

        public bool IsSimulationTransitionProcessing => _transitionInProgress;
        public bool SimulationTransitionCompleted => _simulationTransitionCompleted;

        public void SetSimulationAutoConfirm(bool enabled) => _simulationAutoConfirm = enabled;

        public void CancelSimulationTransition()
        {
            if (!_transitionInProgress) return;
            StopAllCoroutines();
            if (_transitionLocomotor != null)
                _transitionLocomotor.enabled = _transitionLocomotorWasEnabled;
            if (transitionPanel != null) transitionPanel.SetActive(false);
            SetButtonsActive(false);
            DashboardGate.PoseBroadcastSuspended = false;
            _transitionLocomotor = null;
            _transitionInProgress = false;
            _simulationTransitionCompleted = false;
        }

        private void Start()
        {
            if (EventBus.Instance == null)
            {
                SafetyLog.Error("[PhaseController] EventBus.Instance is null — transição não será registrada.", this);
                enabled = false;
                return;
            }

            ValidateReferences();

            if (taskManager == null)
                taskManager = TaskManager.Instance != null ? TaskManager.Instance : FindFirstObjectByType<TaskManager>();

            if (playerLocomotor == null && playerRig != null)
                playerLocomotor = ResolveLocomotor(playerRig);

            if (playerHead == null && playerRig != null)
                playerHead = PlayerRecenter.ResolveHead(playerRig);

            _popupFeedback = popupFeedbackProvider as SafetyProto.Core.Interfaces.IPopupFeedback;

            SetButtonsActive(false);

            _onSessionStarted = OnSessionStarted;
            EventBus.Instance.onSessionStarted.AddListener(_onSessionStarted);
            EventBus.Instance.onGroupCompleted.AddListener(OnGroupCompleted);
        }

        private static Behaviour ResolveLocomotor(Transform root)
        {
            foreach (var b in root.GetComponentsInChildren<Behaviour>(true))
                if (b != null && b.GetType().Name == "FirstPersonLocomotor")
                    return b;
            return null;
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onGroupCompleted.RemoveListener(OnGroupCompleted);
                if (_onSessionStarted != null)
                    EventBus.Instance.onSessionStarted.RemoveListener(_onSessionStarted);
            }

            DashboardGate.PoseBroadcastSuspended = false;
            if (_transitionLocomotor != null)
                _transitionLocomotor.enabled = _transitionLocomotorWasEnabled;
            _transitionInProgress = false;
            _simulationAutoConfirm = false;
        }

        private void OnSessionStarted(SessionStartedEventArgs _)
        {
            _advanceConsumed = false;
            _transitionExecuted = false;
            SetButtonsActive(SessionModeState.Current == SessionMode.Evaluation);
        }

        /// <summary>Wire to the advance button OnClick. Evaluation mode only: applies order penalties, marks pending tasks omitted, shows popup, then teleports.</summary>
        public void OnAdvanceClicked()
        {
            if (_advanceConsumed) { SafetyLog.Warning("[PhaseController] OnAdvanceClicked ignorado — já consumido.", this); return; }
            if (SessionModeState.Current != SessionMode.Evaluation && !_simulationAutoConfirm)
            {
                SafetyLog.Warning($"[PhaseController] OnAdvanceClicked ignorado — modo atual é {SessionModeState.Current}, esperado Evaluation.", this);
                return;
            }
            if (taskManager == null)
            {
                SafetyLog.Error("[PhaseController] TaskManager não encontrado.", this);
                return;
            }

            var currentGroup = taskManager.GetCurrentGroup();
            if (currentGroup == null || !string.Equals(currentGroup.id, targetGroupId, System.StringComparison.Ordinal))
            {
                var actualId = currentGroup?.id ?? "(null)";
                SafetyLog.Info($"[PhaseController] Avanço ignorado — grupo atual é '{actualId}', esperado '{targetGroupId}'.", this);
                return;
            }

            _advanceConsumed = true;
            SetButtonsActive(false);

            ApplyOrderPenaltyIfDeviated(currentGroup.id, currentGroup.groupName);
            taskManager.ForceCompleteCurrentGroup();
            SafetyLog.Info("[PhaseController] Grupo de EPIs fechado.", this);

            if (_simulationAutoConfirm)
            {
                StartCoroutine(ExecutePhaseTransition());
                return;
            }

            if (_popupFeedback != null)
            {
                _popupFeedback.ShowInteractive(confirmTitle, confirmBody, confirmButtonLabel,
                    () =>
                    {
                        _popupFeedback.Hide();
                        StartCoroutine(ExecutePhaseTransition());
                    });
            }
            else
            {
                StartCoroutine(ExecutePhaseTransition());
            }
        }

        private void ApplyOrderPenaltyIfDeviated(string groupId, string groupName)
        {
            var deviations = taskManager.GetCompletionOrderDeviations();
            if (deviations.Count == 0) return;

            string list = string.Join(", ", deviations);

            taskManager.RegisterOrderViolation($"EPIs fora da ordem recomendada: {list}");
            SafetyEvents.RaiseSafetyViolation(new SafetyViolationEventArgs
            {
                ViolationCode = "ORDER_VIOLATION",
                Message = $"EPIs equipados fora da ordem recomendada: {list}",
                TaskId = string.Empty,
                GroupId = groupId,
                TaskName = string.Empty,
                GroupName = groupName
            });

            var scoring = taskManager.Scoring ?? ScoringConfig.Default;
            int charge = scoring.BasePenaltyFor(TaskSeverity.Minor);
            if (charge > 0)
                ScoreService.Instance.SubtractPoints(charge, "ORDER_VIOLATION", string.Empty);
        }

        private void OnGroupCompleted(TaskGroupEventArgs args)
        {
            if (_transitionExecuted) return;
            if (string.IsNullOrEmpty(triggerGroupName) || args.Group == null ||
                !string.Equals(args.Group.groupName, triggerGroupName, System.StringComparison.Ordinal))
                return;

            _transitionExecuted = true;
            _simulationTransitionCompleted = false;

            if (_simulationAutoConfirm)
            {
                StartCoroutine(ExecutePhaseTransition());
            }
            else if (SessionModeState.Current == SessionMode.Guided)
            {
                if (_popupFeedback != null)
                {
                    _popupFeedback.ShowInteractive(confirmTitle, confirmBody, confirmButtonLabel,
                        () =>
                        {
                            _popupFeedback.Hide();
                            StartCoroutine(ExecutePhaseTransition());
                        });
                }
                else
                {
                    StartCoroutine(ExecutePhaseTransition());
                }
            }
            // Evaluation mode: do nothing — player presses the advance button when ready.
        }

        private void SetButtonsActive(bool active)
        {
            if (advanceButtonObjects == null) return;
            foreach (var go in advanceButtonObjects)
                if (go != null) go.SetActive(active);
        }

        private IEnumerator ExecutePhaseTransition()
        {
            _transitionInProgress = true;
            var ovr = OVRScreenFade.instance;

            if (ovr != null)
            {
                ovr.fadeTime = fadeOutDuration;
                ovr.FadeOut();
                yield return new WaitForSeconds(fadeOutDuration);
            }

            foreach (var obj in objectsToHide)
                if (obj != null) obj.SetActive(false);
            foreach (var obj in objectsToShow)
                if (obj != null) obj.SetActive(true);

            _transitionLocomotor = playerLocomotor;
            _transitionLocomotorWasEnabled = playerLocomotor != null && playerLocomotor.enabled;
            if (playerLocomotor != null) playerLocomotor.enabled = false;

            DashboardGate.PoseBroadcastSuspended = true;

            if (playerRig != null && spawnPointAndaime != null)
            {
                if (playerHead != null)
                {
                    PlayerRecenter.Recenter(playerRig, playerHead, spawnPointAndaime);
                }
                else
                {
                    playerRig.position = spawnPointAndaime.position;
                    playerRig.rotation = Quaternion.Euler(0f, spawnPointAndaime.rotation.eulerAngles.y, 0f);
                    Physics.SyncTransforms();
                }
            }

            if (transitionPanel != null)
                transitionPanel.SetActive(true);

            float elapsed = 0f;
            bool groundReady = false;
            while (elapsed < holdBlackDuration || (!groundReady && elapsed < groundWaitTimeout))
            {
                if (!groundReady) groundReady = IsGroundReadyAtSpawn();
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!groundReady)
                SafetyLog.Warning($"[PhaseController] Chão do andaime não confirmado em {groundWaitTimeout}s — religando locomotor mesmo assim.", this);

            if (playerLocomotor != null) playerLocomotor.enabled = _transitionLocomotorWasEnabled;
            _transitionLocomotor = null;

            DashboardGate.PoseBroadcastSuspended = false;

            if (transitionPanel != null)
                transitionPanel.SetActive(false);

            if (ovr != null)
            {
                ovr.fadeTime = fadeInDuration;
                ovr.FadeIn();
                yield return new WaitForSeconds(fadeInDuration);
            }

            SafetyLog.Info("[PhaseController] Transição concluída. ZonaAndaime ativa.", this);
            _transitionInProgress = false;
            _simulationTransitionCompleted = true;
        }

        private bool IsGroundReadyAtSpawn()
        {
            if (spawnPointAndaime == null) return true;

            Vector3 origin = spawnPointAndaime.position + Vector3.up * 0.5f;
            var hits = Physics.SphereCastAll(origin, groundProbeRadius, Vector3.down,
                groundProbeDistance, groundMask, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (playerRig != null && hit.collider.transform.IsChildOf(playerRig)) continue;
                return true;
            }
            return false;
        }

        private void ValidateReferences()
        {
            if (string.IsNullOrEmpty(triggerGroupName))
                SafetyLog.Warning("[PhaseController] triggerGroupName vazio.", this);
            if (playerRig == null)
                SafetyLog.Warning("[PhaseController] playerRig não atribuído no Inspector.", this);
            if (spawnPointAndaime == null)
                SafetyLog.Warning("[PhaseController] spawnPointAndaime não atribuído no Inspector.", this);
            if (FindAnyObjectByType<OVRScreenFade>() == null)
                SafetyLog.Warning("[PhaseController] OVRScreenFade não encontrado na cena — fade visual não funcionará no Quest. Adicione OVRScreenFade ao CenterEyeAnchor.", this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!string.IsNullOrEmpty(triggerGroupName))
                triggerGroupName = triggerGroupName.Trim();

            if (FindAnyObjectByType<OVRScreenFade>() == null)
            {
                Debug.LogWarning(
                    "[PhaseController] OVRScreenFade não encontrado na cena. " +
                    "Adicione o componente ao CenterEyeAnchor (OVRCameraRig > TrackingSpace > CenterEyeAnchor) " +
                    "com fadeOnStart = false para que o fade funcione no Quest.",
                    this);
            }
        }
#endif
    }
}
