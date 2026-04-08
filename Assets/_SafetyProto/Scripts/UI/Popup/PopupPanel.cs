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
        [Tooltip("Root do layout a reconstruir após mudar o conteúdo (geralmente o background ou este próprio RectTransform).")]
        [SerializeField] private RectTransform layoutRoot;

        [Header("Estilos por tipo")]
        [SerializeField] private Color normalColor      = new Color(0.086f, 0.110f, 0.157f, 0.88f);
        [SerializeField] private Color warningColor     = new Color(0.118f, 0.071f, 0.039f, 0.92f);
        [SerializeField] private Color interactiveColor = new Color(0.086f, 0.110f, 0.157f, 0.88f);
        [SerializeField] private Sprite warningIcon;
        [SerializeField] private Sprite infoIcon;

        [Header("Animação")]
        [SerializeField] private float animDuration = 0.2f;
        [SerializeField] private AnimationCurve growCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] private AnimationCurve shrinkCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

        public bool IsVisible { get; private set; }

        private PopupData _currentData;
        private Coroutine _animCoroutine;
        private Vector3 _openScale;

        // Paused during entrance animation to avoid moving transform while OVROverlayCanvas
        // is initializing, which can cause a second CopyTexture with invalid coordinates.
        private MenuFollowHmd _followHmd;

        private void Awake()
        {
            _openScale = transform.localScale;

            _followHmd = GetComponent<MenuFollowHmd>()
                      ?? GetComponentInParent<MenuFollowHmd>();

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
                    PopupType.Warning => warningColor,
                    PopupType.Interactive => interactiveColor,
                    _ => normalColor
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

            StopAnim();
            transform.localScale = Vector3.zero;
            gameObject.SetActive(true);
            IsVisible = true;
            _animCoroutine = StartCoroutine(ShowWithRebuild());
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;

            SafetyLog.Info("[PopupPanel] Hide()", this);

            StopAnim();
            IsVisible = false;
            _animCoroutine = StartCoroutine(AnimateScaleAndDeactivate(_openScale, Vector3.zero, shrinkCurve));
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

        private IEnumerator ShowWithRebuild()
        {
            // Disable follow during entrance animation to avoid OVROverlayCanvas coordinate issues.
            if (_followHmd != null) _followHmd.enabled = false;

            yield return null;

            var root = layoutRoot ?? (RectTransform)transform;
            LayoutRebuilder.MarkLayoutForRebuild(root);

            yield return null;

            yield return AnimateScale(Vector3.zero, _openScale, growCurve);

            if (_followHmd != null) _followHmd.enabled = true;
        }

        private void StopAnim()
        {
            if (_animCoroutine != null)
            {
                StopCoroutine(_animCoroutine);
                _animCoroutine = null;

                if (_followHmd != null) _followHmd.enabled = true;
            }
        }

        private IEnumerator AnimateScale(Vector3 from, Vector3 to, AnimationCurve curve)
        {
            float elapsed = 0f;
            transform.localScale = from;

            while (elapsed < animDuration)
            {
                elapsed += Time.deltaTime;
                float t = curve.Evaluate(Mathf.Clamp01(elapsed / animDuration));
                transform.localScale = Vector3.LerpUnclamped(from, to, t);
                yield return null;
            }

            transform.localScale = to;
            _animCoroutine = null;
        }

        private IEnumerator AnimateScaleAndDeactivate(Vector3 from, Vector3 to, AnimationCurve curve)
        {
            if (_followHmd != null) _followHmd.enabled = false;

            yield return AnimateScale(from, to, curve);
            gameObject.SetActive(false);

            if (_followHmd != null) _followHmd.enabled = true;
        }
    }
}