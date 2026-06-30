using System.Collections;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using UnityEngine;
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

        [Header("Trigger")]
        [Tooltip("TaskGroup ScriptableObject que dispara a transição ao ser concluído.")]
        [SerializeField] private TaskGroup triggerGroup;

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

        private void Start()
        {
            if (EventBus.Instance == null)
            {
                SafetyLog.Error("[PhaseController] EventBus.Instance is null — transição não será registrada.", this);
                enabled = false;
                return;
            }

            ValidateReferences();

            if (playerLocomotor == null && playerRig != null)
                playerLocomotor = ResolveLocomotor(playerRig);

            if (playerHead == null && playerRig != null)
                playerHead = PlayerRecenter.ResolveHead(playerRig);

            EventBus.Instance.onGroupCompleted.AddListener(OnGroupCompleted);
        }

        // The Meta FirstPersonLocomotor ships as a precompiled type; resolve it by name so this
        // controller stays decoupled from the exact SDK type while still being able to toggle gravity.
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
                EventBus.Instance.onGroupCompleted.RemoveListener(OnGroupCompleted);

            // Safety: never leave the pose stream suspended if we're torn down mid-transition.
            DashboardGate.PoseBroadcastSuspended = false;
        }

        private void OnGroupCompleted(TaskGroupEventArgs args)
        {
            if (_transitionExecuted) return;
            // Match by group name (id), not reference: the event carries a JSON-backed
            // TaskGroupDef at runtime, while triggerGroup is the authoring ScriptableObject.
            if (triggerGroup == null || args.Group == null ||
                !string.Equals(args.Group.groupName, triggerGroup.groupName, System.StringComparison.Ordinal))
                return;

            _transitionExecuted = true;
            StartCoroutine(ExecutePhaseTransition());
        }

        private IEnumerator ExecutePhaseTransition()
        {
            var ovr = OVRScreenFade.instance;

            if (ovr != null)
            {
                ovr.fadeTime = fadeOutDuration;
                ovr.FadeOut();
                yield return new WaitForSeconds(fadeOutDuration);
            }

            // Activate the destination geometry BEFORE moving so its colliders already exist by teleport time.
            foreach (var obj in objectsToHide)
                if (obj != null) obj.SetActive(false);
            foreach (var obj in objectsToShow)
                if (obj != null) obj.SetActive(true);

            // Suspend player gravity for the teleport: disabling the locomotor stops it from calling
            // CharacterController.Move, so a frame hitch (e.g. a dashboard send) can't drop the player
            // through scaffold colliders that haven't registered yet.
            bool locomotorWasEnabled = playerLocomotor != null && playerLocomotor.enabled;
            if (playerLocomotor != null) playerLocomotor.enabled = false;

            // Suspend the dashboard pose stream for the transition: its ~10 Hz main-thread sends are
            // a prime source of the frame hitch behind this bug. Discrete events keep flowing.
            DashboardGate.PoseBroadcastSuspended = true;

            if (playerRig != null && spawnPointAndaime != null)
            {
                if (playerHead != null)
                {
                    // Recenter the HEAD over the spawn (cancels the room-scale rig offset) so the
                    // player lands centered on the scaffold deck regardless of where they physically
                    // stand. PlayerRecenter calls Physics.SyncTransforms internally.
                    PlayerRecenter.Recenter(playerRig, playerHead, spawnPointAndaime);
                }
                else
                {
                    // Fallback: no head ref — move the rig origin directly (legacy behavior).
                    playerRig.position = spawnPointAndaime.position;
                    playerRig.rotation = Quaternion.Euler(0f, spawnPointAndaime.rotation.eulerAngles.y, 0f);
                    Physics.SyncTransforms();
                }
            }

            if (transitionPanel != null)
                transitionPanel.SetActive(true);

            // Hold black for comfort AND until solid ground under the spawn is confirmed. The timeout is a
            // safety cap so a missing/misconfigured floor never freezes the transition.
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

            // Re-enable gravity/locomotion only now that the player has solid ground beneath them.
            if (playerLocomotor != null) playerLocomotor.enabled = locomotorWasEnabled;

            // Resume the dashboard pose stream now the latency-sensitive window is over.
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
        }

        // Probes straight down from the spawn for any non-player collider — confirms the scaffold deck
        // has registered before gravity is restored. Returns true when there is no spawn to check.
        private bool IsGroundReadyAtSpawn()
        {
            if (spawnPointAndaime == null) return true;

            Vector3 origin = spawnPointAndaime.position + Vector3.up * 0.5f;
            var hits = Physics.SphereCastAll(origin, groundProbeRadius, Vector3.down,
                groundProbeDistance, groundMask, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                // Ignore the player's own colliders (capsule, hands, held items).
                if (playerRig != null && hit.collider.transform.IsChildOf(playerRig)) continue;
                return true;
            }
            return false;
        }

        private void ValidateReferences()
        {
            if (triggerGroup == null)
                SafetyLog.Warning("[PhaseController] triggerGroup não atribuído no Inspector.", this);
            if (playerRig == null)
                SafetyLog.Warning("[PhaseController] playerRig não atribuído no Inspector.", this);
            if (spawnPointAndaime == null)
                SafetyLog.Warning("[PhaseController] spawnPointAndaime não atribuído no Inspector.", this);
            // OVRScreenFade.instance is assigned in OVRScreenFade.Start (not Awake), so a Start-time
            // check against instance can race even with execOrder set. Look up the component directly.
            if (FindAnyObjectByType<OVRScreenFade>() == null)
                SafetyLog.Warning("[PhaseController] OVRScreenFade não encontrado na cena — fade visual não funcionará no Quest. Adicione OVRScreenFade ao CenterEyeAnchor.", this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Warn in the Editor if no OVRScreenFade exists anywhere in the scene.
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
