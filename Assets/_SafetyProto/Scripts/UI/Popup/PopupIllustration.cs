using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.UI
{
    /// <summary>
    /// Popup illustration that cycles through its frames with a crossfade, so a two-pose gesture
    /// (open hand, closed fist) reads as motion. A single frame is shown static.
    /// </summary>
    public class PopupIllustration : MonoBehaviour
    {
        private Image _front;
        private Image _back;
        private Coroutine _cycle;

        /// <summary>Builds the illustration under <paramref name="parent"/> as a layout row of <paramref name="size"/>.</summary>
        public static PopupIllustration Create(Transform parent, int siblingIndex, Vector2 size, int layer)
        {
            var go = new GameObject("Illustration", typeof(RectTransform));
            go.layer = layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.SetSiblingIndex(siblingIndex);

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = size.x;
            element.preferredHeight = size.y;
            element.minHeight = size.y;

            var illustration = go.AddComponent<PopupIllustration>();
            illustration._front = illustration.CreateLayer("FrameA", layer);
            illustration._back = illustration.CreateLayer("FrameB", layer);
            return illustration;
        }

        private Image CreateLayer(string name, int layer)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        public void Play(Sprite[] frames, float holdSeconds, float fadeSeconds)
        {
            Stop();
            gameObject.SetActive(true);
            SetFrame(_front, frames[0], 1f);
            SetFrame(_back, null, 0f);
            if (frames.Length > 1)
                _cycle = StartCoroutine(Cycle(frames, holdSeconds, fadeSeconds));
        }

        public void Stop()
        {
            if (_cycle != null) StopCoroutine(_cycle);
            _cycle = null;
            gameObject.SetActive(false);
        }

        private IEnumerator Cycle(Sprite[] frames, float hold, float fade)
        {
            int index = 0;
            while (true)
            {
                yield return new WaitForSecondsRealtime(hold);
                index = (index + 1) % frames.Length;
                SetFrame(_back, frames[index], 0f);

                for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
                {
                    // Staggered so two unrelated poses never sit fully overlaid mid-transition.
                    float k = t / fade;
                    SetAlpha(_front, 1f - Mathf.SmoothStep(0f, 1f, k / 0.6f));
                    SetAlpha(_back, Mathf.SmoothStep(0f, 1f, (k - 0.4f) / 0.6f));
                    yield return null;
                }

                SetAlpha(_front, 0f);
                SetAlpha(_back, 1f);
                (_front, _back) = (_back, _front);
            }
        }

        private static void SetFrame(Image image, Sprite sprite, float alpha)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;
            SetAlpha(image, alpha);
        }

        private static void SetAlpha(Image image, float alpha)
        {
            var c = image.color;
            c.a = alpha;
            image.color = c;
        }
    }
}
