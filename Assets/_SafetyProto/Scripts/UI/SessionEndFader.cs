using System.Collections;
using SafetyProto.Core;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Fades a CanvasGroup alpha to zero when the session ends. If the panel hosts a
    /// success-advice caption, the fade waits for the caption and its narration to finish, so the
    /// last task's DICA is not cut off mid-sentence.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class SessionEndFader : MonoBehaviour
    {
        [SerializeField] private float fadeDuration = 0.8f;

        private CanvasGroup _canvasGroup;
        private SuccessAdvicePresenter _advicePresenter;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _advicePresenter = GetComponentInChildren<SuccessAdvicePresenter>(true);
        }

        private void OnEnable()
        {
            if (!this.IsEventBusReady()) return;
            EventBus.Instance.Subscribe<SessionCompletedEventArgs>(OnSessionCompleted);
        }

        private void OnDisable()
        {
            if (EventBus.Instance != null)
                EventBus.Instance.Unsubscribe<SessionCompletedEventArgs>(OnSessionCompleted);
        }

        private void OnSessionCompleted(SessionCompletedEventArgs _)
        {
            StopAllCoroutines();
            StartCoroutine(FadeOut());
        }

        private IEnumerator FadeOut()
        {
            // The last task's caption starts on its completion, before the session completes.
            while (_advicePresenter != null && _advicePresenter.IsShowing)
                yield return null;

            float start = _canvasGroup.alpha;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(start, 0f, elapsed / fadeDuration);
                yield return null;
            }

            _canvasGroup.alpha = 0f;
        }
    }
}
