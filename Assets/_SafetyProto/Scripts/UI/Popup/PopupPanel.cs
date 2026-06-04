using System.Collections;
using SafetyProto.Core.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.UI
{
    public class PopupPanel : MonoBehaviour
    {
        [Header("Referências UI")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI bodyText;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image iconImage;
        [SerializeField] private GameObject actionButtonRoot;
        [SerializeField] private TextMeshProUGUI actionButtonLabel;
        [SerializeField] private GameObject closeButtonRoot;
        [Tooltip("Root do layout a reconstruir após mudar o conteúdo (geralmente o background ou este próprio RectTransform).")]
        [SerializeField] private RectTransform layoutRoot;

        [Header("Estilos por tipo")]
        [SerializeField] private Color normalColor      = new Color(0.086f, 0.110f, 0.157f, 0.88f);
        [SerializeField] private Color warningColor     = new Color(0.118f, 0.071f, 0.039f, 0.92f);
        [SerializeField] private Color interactiveColor = new Color(0.086f, 0.110f, 0.157f, 0.88f);
        [SerializeField] private Sprite warningIcon;
        [SerializeField] private Sprite infoIcon;

        [Header("Animação")]
        [SerializeField] private float fadeDuration = 0.25f;

        public bool IsVisible { get; private set; }

        /// <summary>
        /// Raised when the panel starts hiding, no matter the trigger (close button or
        /// programmatic <see cref="Hide"/>). PopupService listens to this to resume the
        /// session, so closing via the button can't leave the timer paused.
        /// </summary>
        public event System.Action Hidden;

        private PopupData _currentData;
        private Coroutine _fadeCoroutine;
        private CanvasGroup _canvasGroup;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>()
                        ?? gameObject.AddComponent<CanvasGroup>();

            if (titleText == null)
                SafetyLog.Error("[PopupPanel] titleText not assigned in Inspector!", this);
            if (bodyText == null)
                SafetyLog.Error("[PopupPanel] bodyText not assigned in Inspector!", this);
            if (backgroundImage == null)
                SafetyLog.Error("[PopupPanel] backgroundImage not assigned in Inspector!", this);
            if (layoutRoot == null)
                SafetyLog.Warning("[PopupPanel] layoutRoot null — using root transform.", this);
        }

        public void Show(PopupData data)
        {
            SafetyLog.Info($"[PopupPanel] Show() — type: {data.type}, title: '{data.title}'", this);

            _currentData = data;

            if (titleText != null) titleText.text = data.title;
            if (bodyText != null) bodyText.text = data.body;

            if (backgroundImage != null)
            {
                backgroundImage.color = data.type switch
                {
                    PopupType.Warning     => warningColor,
                    PopupType.Interactive => interactiveColor,
                    _                     => normalColor
                };
            }

            if (iconImage != null)
            {
                Sprite resolvedIcon = data.customIcon
                    ?? (data.type == PopupType.Warning ? warningIcon : infoIcon);

                bool showIcon = data.customIcon != null || data.type != PopupType.Normal;
                iconImage.gameObject.SetActive(showIcon);
                if (showIcon)
                    iconImage.sprite = resolvedIcon;
            }

            bool isInteractive = data.type == PopupType.Interactive;
            if (actionButtonRoot != null)
                actionButtonRoot.SetActive(isInteractive);
            if (isInteractive && actionButtonLabel != null)
                actionButtonLabel.text = data.actionButtonLabel;

            if (closeButtonRoot != null)
                closeButtonRoot.SetActive(!isInteractive);

            StopFade();

            var root = layoutRoot ?? (RectTransform)transform;
            LayoutRebuilder.MarkLayoutForRebuild(root);

            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            gameObject.SetActive(true);
            IsVisible = true;
            _fadeCoroutine = StartCoroutine(FadeAlpha(0f, 1f));
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;

            SafetyLog.Info("[PopupPanel] Hide()", this);

            StopFade();

            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            IsVisible = false;
            _fadeCoroutine = StartCoroutine(FadeAlphaAndDeactivate(_canvasGroup.alpha, 0f));

            Hidden?.Invoke();
        }

        public void OnActionButtonPressed()
        {
            SafetyLog.Info("[PopupPanel] OnActionButtonPressed()", this);
            _currentData?.onActionPressed?.Invoke();
        }

        public void OnCloseButtonPressed()
        {
            SafetyLog.Info("[PopupPanel] OnCloseButtonPressed()", this);
            Hide();
        }

        private IEnumerator FadeAlpha(float from, float to)
        {
            float elapsed = 0f;
            _canvasGroup.alpha = from;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / fadeDuration);
                yield return null;
            }

            _canvasGroup.alpha = to;

            if (to >= 1f)
            {
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }

            _fadeCoroutine = null;
        }

        private IEnumerator FadeAlphaAndDeactivate(float from, float to)
        {
            yield return FadeAlpha(from, to);
            gameObject.SetActive(false);
        }

        private void StopFade()
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }
        }
    }
}