using System.Collections;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Gameplay
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
        [SerializeField] private string triggerGroupName = "ppe_selection";

        private bool _transitionExecuted;

        private void Start()
        {
            ValidateReferences();

            if (EventBus.Instance != null)
                EventBus.Instance.onGroupCompleted.AddListener(OnGroupCompleted);
            else
                SafetyLog.Error("[PhaseController] EventBus.Instance is null — transição não será registrada.", this);
        }

        private void OnDestroy()
        {
            if (EventBus.Instance != null)
                EventBus.Instance.onGroupCompleted.RemoveListener(OnGroupCompleted);
        }

        private void OnGroupCompleted(TaskGroupEventArgs args)
        {
            if (_transitionExecuted) return;
            if (args.Group == null) return;
            if (!string.Equals(args.Group.groupName, triggerGroupName, System.StringComparison.OrdinalIgnoreCase)) return;

            _transitionExecuted = true;
            StartCoroutine(ExecutePhaseTransition());
        }

        private IEnumerator ExecutePhaseTransition()
        {
            var ovr = OVRScreenFade.instance;

            // 1. Fade out
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

            // 2. Trocar zonas
            foreach (var obj in objectsToHide)
                if (obj != null) obj.SetActive(false);
            foreach (var obj in objectsToShow)
                if (obj != null) obj.SetActive(true);

            // 3. Reposicionar player rig
            if (playerRig != null && spawnPointAndaime != null)
            {
                playerRig.position = spawnPointAndaime.position;
                playerRig.rotation = Quaternion.Euler(0f, spawnPointAndaime.rotation.eulerAngles.y, 0f);
            }

            // 4. Painel de contexto durante hold
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

            // 5. Fade in
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
            if (playerRig == null)
                SafetyLog.Warning("[PhaseController] playerRig não atribuído no Inspector.", this);
            if (spawnPointAndaime == null)
                SafetyLog.Warning("[PhaseController] spawnPointAndaime não atribuído no Inspector.", this);
        }
    }
}
