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
        [SerializeField] private AnimationCurve growCurve   = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] private AnimationCurve shrinkCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

        public bool IsVisible { get; private set; }

        private PopupData _currentData;
        private Coroutine _animCoroutine;
        private Vector3   _openScale;

        // MenuFollowHmd no mesmo GameObject ou no pai — pausado durante animação de entrada
        // para evitar que mover o transform enquanto OVROverlayCanvas está inicializando
        // gere um segundo CopyTexture com coordenadas inválidas.
        private MenuFollowHmd _followHmd;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            _openScale = transform.localScale;

            // Busca MenuFollowHmd neste GameObject ou no pai imediato (PopUpCanvas está
            // diretamente no [UI_Popup] que não tem o componente, mas o canvas raiz tem).
            _followHmd = GetComponent<MenuFollowHmd>()
                      ?? GetComponentInParent<MenuFollowHmd>();

            // Validação de referências — erros visíveis no Console em vez de NullRef silencioso.
            if (titleText == null)
                SafetyLog.Error("[PopupPanel] titleText não atribuído no Inspector!", this);
            if (bodyText == null)
                SafetyLog.Error("[PopupPanel] bodyText não atribuído no Inspector!", this);
            if (backgroundImage == null)
                SafetyLog.Error("[PopupPanel] backgroundImage não atribuído no Inspector!", this);
            if (layoutRoot == null)
                SafetyLog.Warning("[PopupPanel] layoutRoot null — usará transform raiz.", this);
        }

        // ── API pública ───────────────────────────────────────────────────────

        public void Show(PopupData data)
        {
            SafetyLog.Info($"[PopupPanel] Show() — tipo: {data.type}, título: '{data.title}'", this);

            _currentData = data;

            if (titleText != null) titleText.text = data.title;
            if (bodyText  != null) bodyText.text  = data.body;

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

        /// <summary>Chamado pelo botão de ação via Inspector ou UnityEvent.</summary>
        public void OnActionButtonPressed()
        {
            SafetyLog.Info("[PopupPanel] OnActionButtonPressed()", this);
            _currentData?.onActionPressed?.Invoke();
        }

        /// <summary>Chamado pelo botão X via Inspector.</summary>
        public void OnCloseButtonPressed()
        {
            SafetyLog.Info("[PopupPanel] OnCloseButtonPressed()", this);
            Hide();
        }

        // ── Coroutines ────────────────────────────────────────────────────────

        private IEnumerator ShowWithRebuild()
        {
            // Para o MenuFollowHmd durante a inicialização/animação de entrada.
            // Mover o transform enquanto OVROverlayCanvas está calculando a RenderTexture
            // pode gerar coordenadas de CopyTexture inválidas (y negativo).
            if (_followHmd != null) _followHmd.enabled = false;

            // Frame 1: Canvas processa os textos e ativa/desativa filhos.
            yield return null;

            // Usa MarkLayoutForRebuild + aguarda 1 frame em vez de ForceRebuildLayoutImmediate.
            // ForceRebuildLayoutImmediate no meio de uma coroutine com OVROverlayCanvas ativo
            // força o compositor a resubmeter o swapchain no mesmo frame → stall no render thread.
            SafetyLog.Info("[PopupPanel] MarkLayoutForRebuild — aguardando frame de layout...", this);
            var root = layoutRoot != null ? layoutRoot : (RectTransform)transform;
            LayoutRebuilder.MarkLayoutForRebuild(root);

            // Frame 2: layout reconstruído organicamente pelo Canvas.
            yield return null;
            SafetyLog.Info("[PopupPanel] Layout concluído — iniciando animação de entrada.", this);

            yield return AnimateScale(Vector3.zero, _openScale, growCurve);

            // Reativa MenuFollowHmd após animação concluída.
            if (_followHmd != null) _followHmd.enabled = true;

            SafetyLog.Info("[PopupPanel] Animação de entrada concluída.", this);
        }

        private void StopAnim()
        {
            if (_animCoroutine != null)
            {
                StopCoroutine(_animCoroutine);
                _animCoroutine = null;

                // Garante reativar MenuFollowHmd se animação foi interrompida.
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

            SafetyLog.Info("[PopupPanel] AnimateScaleAndDeactivate — SetActive(false)", this);
            gameObject.SetActive(false);

            if (_followHmd != null) _followHmd.enabled = true;
        }
    }
}