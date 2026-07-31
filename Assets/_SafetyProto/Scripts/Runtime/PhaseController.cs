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
    public class PhaseController : MonoBehaviour, IRecenterAnchorProvider
    {
        [Header("Player")]
        [SerializeField] private Transform playerRig;
        [Tooltip("Pre-transition anchor (Canteiro). Point the same Transform PlayerSpawnCenter.startPoint " +
                 "uses. Returned by CurrentAnchor before the phase transition executes.")]
        [SerializeField] private Transform startPointCanteiro;
        [SerializeField] private Transform spawnPointAndaime;

        [Header("Recenter")]
        [Tooltip("Shared fade -> recenter -> reground sequence and busy guard.")]
        [SerializeField] private RecenterService recenterService;

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
        private bool _simulationTransitionCompleted;
        private bool _advanceConsumed;

        private UnityAction<SessionStartedEventArgs>? _onSessionStarted;

        public bool IsSimulationTransitionProcessing => recenterService != null && recenterService.IsBusy;
        public bool SimulationTransitionCompleted => _simulationTransitionCompleted;

        /// <summary>IRecenterAnchorProvider — the current phase's center anchor. Canteiro before
        /// the transition executes, Andaime after.</summary>
        public Transform CurrentAnchor => _transitionExecuted ? spawnPointAndaime : startPointCanteiro;

        public void SetSimulationAutoConfirm(bool enabled) => _simulationAutoConfirm = enabled;

        public void CancelSimulationTransition()
        {
            if (recenterService == null || !recenterService.IsBusy) return;
            StopAllCoroutines();
            recenterService.CancelActive();
            if (transitionPanel != null) transitionPanel.SetActive(false);
            SetButtonsActive(false);
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

            _popupFeedback = popupFeedbackProvider as SafetyProto.Core.Interfaces.IPopupFeedback;

            SetButtonsActive(false);

            _onSessionStarted = OnSessionStarted;
            EventBus.Instance.onSessionStarted.AddListener(_onSessionStarted);
            EventBus.Instance.onGroupCompleted.AddListener(OnGroupCompleted);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onGroupCompleted.RemoveListener(OnGroupCompleted);
                if (_onSessionStarted != null)
                    EventBus.Instance.onSessionStarted.RemoveListener(_onSessionStarted);
            }

            if (recenterService != null)
                recenterService.CancelActive();
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
            int charge = scoring.BasePenaltyFor(RiskLevels.IncidentalChargeTier);
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
            if (recenterService == null)
            {
                SafetyLog.Error("[PhaseController] recenterService não atribuído — transição abortada.", this);
                yield break;
            }

            var options = new RecenterOptions
            {
                FadeOutDuration = fadeOutDuration,
                HoldBlackDuration = holdBlackDuration,
                FadeInDuration = fadeInDuration,
                SuspendPoseBroadcast = true,
                UseGroundProbe = true,
                GroundReady = IsGroundReadyAtSpawn,
                GroundWaitTimeout = groundWaitTimeout,
                LocomotorHandling = LocomotorMode.ToggleEnabled,
                OnBlackout = () =>
                {
                    foreach (var obj in objectsToHide)
                        if (obj != null) obj.SetActive(false);
                    foreach (var obj in objectsToShow)
                        if (obj != null) obj.SetActive(true);
                    if (transitionPanel != null)
                        transitionPanel.SetActive(true);
                },
                OnBeforeFadeIn = () =>
                {
                    if (transitionPanel != null)
                        transitionPanel.SetActive(false);
                },
            };

            yield return recenterService.RecenterTo(spawnPointAndaime, options);

            SafetyLog.Info("[PhaseController] Transição concluída. ZonaAndaime ativa.", this);
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
            if (startPointCanteiro == null)
                SafetyLog.Warning("[PhaseController] startPointCanteiro não atribuído no Inspector — CurrentAnchor retornará null antes da transição.", this);
            if (recenterService == null)
                SafetyLog.Warning("[PhaseController] recenterService não atribuído no Inspector.", this);
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
