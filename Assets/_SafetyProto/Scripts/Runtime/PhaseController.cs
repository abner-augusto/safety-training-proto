using System.Collections;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Data.ScriptableObjects;
using UnityEngine;

namespace SafetyProto.Runtime
{
    public class PhaseController : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private Transform playerRig;
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
            EventBus.Instance.onGroupCompleted.AddListener(OnGroupCompleted);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
                EventBus.Instance.onGroupCompleted.RemoveListener(OnGroupCompleted);
        }

        private void OnGroupCompleted(TaskGroupEventArgs args)
        {
            if (_transitionExecuted) return;
            if (triggerGroup == null || !ReferenceEquals(args.Group, triggerGroup)) return;

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
            else
            {
                SafetyLog.Warning("[PhaseController] OVRScreenFade.instance é null — etapas de fade serão ignoradas.", this);
            }

            foreach (var obj in objectsToHide)
                if (obj != null) obj.SetActive(false);
            foreach (var obj in objectsToShow)
                if (obj != null) obj.SetActive(true);

            if (playerRig != null && spawnPointAndaime != null)
            {
                playerRig.position = spawnPointAndaime.position;
                playerRig.rotation = Quaternion.Euler(0f, spawnPointAndaime.rotation.eulerAngles.y, 0f);
            }

            if (transitionPanel != null)
            {
                transitionPanel.SetActive(true);
                yield return new WaitForSeconds(holdBlackDuration);
                transitionPanel.SetActive(false);
            }
            else if (ovr != null)
            {
                yield return new WaitForSeconds(holdBlackDuration);
            }

            if (ovr != null)
            {
                ovr.fadeTime = fadeInDuration;
                ovr.FadeIn();
                yield return new WaitForSeconds(fadeInDuration);
            }

            SafetyLog.Info("[PhaseController] Transição concluída. ZonaAndaime ativa.", this);
        }

        private void ValidateReferences()
        {
            if (triggerGroup == null)
                SafetyLog.Warning("[PhaseController] triggerGroup não atribuído no Inspector.", this);
            if (playerRig == null)
                SafetyLog.Warning("[PhaseController] playerRig não atribuído no Inspector.", this);
            if (spawnPointAndaime == null)
                SafetyLog.Warning("[PhaseController] spawnPointAndaime não atribuído no Inspector.", this);
        }
    }
}
