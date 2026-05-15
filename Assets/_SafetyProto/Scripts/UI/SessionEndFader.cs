using System.Collections;
using SafetyProto.Core;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Fades a CanvasGroup alpha to zero when the session ends.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class SessionEndFader : MonoBehaviour
    {
        [SerializeField] private float fadeDuration = 0.8f;

        private CanvasGroup _canvasGroup;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            if (!this.IsEventBusReady()) return;
            EventBus.Instance.onSessionCompleted.AddListener(OnSessionCompleted);
        }

        private void OnDisable()
        {
            if (EventBus.Instance != null)
                EventBus.Instance.onSessionCompleted.RemoveListener(OnSessionCompleted);
        }

        private void OnSessionCompleted(SessionCompletedEventArgs _)
        {
            StopAllCoroutines();
            StartCoroutine(FadeOut());
        }

        private IEnumerator FadeOut()
        {
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
