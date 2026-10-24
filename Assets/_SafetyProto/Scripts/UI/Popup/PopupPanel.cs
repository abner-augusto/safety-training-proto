using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.UI
{
    public class PopupPanel : MonoBehaviour
    {
        // TMP breaks lines after a hyphen, which split "NR-06" and "mantê-lo" across lines.
        private static readonly Regex HyphenatedWord = new(@"[\w.(]+(?:-[\w.)]+)+", RegexOptions.Compiled);

        private static string KeepHyphenatedWordsTogether(string text) =>
            string.IsNullOrEmpty(text) ? text : HyphenatedWord.Replace(text, "<nobr>$0</nobr>");

        [Header("Referências UI")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI bodyText;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image iconImage;
        [SerializeField] private GameObject actionButtonRoot;
        [SerializeField] private TextMeshProUGUI actionButtonLabel;
        [SerializeField] private GameObject closeButtonRoot;
        [Tooltip("Botão secundário opcional 'Pular' (onboarding) / 'Cancelar' (confirmações). Exibido apenas quando PopupData.showSkipButton.")]
        [SerializeField] private GameObject skipButtonRoot;
        [Tooltip("Rótulo do botão secundário. Permite reusá-lo como 'Cancelar' em confirmações. Opcional.")]
        [SerializeField] private TextMeshProUGUI skipButtonLabel;
        [Tooltip("Campo de texto opcional (identificação do participante). Exibido apenas quando PopupData.showInputField.")]
        [SerializeField] private GameObject inputFieldRoot;
        [Tooltip("Root do layout a reconstruir após mudar o conteúdo (geralmente o background ou este próprio RectTransform).")]
        [SerializeField] private RectTransform layoutRoot;
        [Tooltip("Margem transparente entre o card (Background) e a borda do canvas, em cada lado. O OVROverlayCanvas só desenha o rect do canvas; a margem deixa o botão fechar sobrar além do card sem ser cortado. Deve bater com o recuo do Background no prefab.")]
        [SerializeField, Min(0f)] private float cardMargin = 40f;

        [Header("Teclado numérico")]
        [Tooltip("Tamanho de cada tecla do teclado numérico construído sob o campo de texto.")]
        [SerializeField] private Vector2 keypadCellSize = new Vector2(132f, 72f);
        [SerializeField] private Vector2 keypadSpacing = new Vector2(12f, 12f);
        [SerializeField] private string keypadBackspaceLabel = "Apagar";
        [SerializeField] private string keypadClearLabel = "Limpar";

        [Header("Ilustração")]
        [Tooltip("Área da ilustração que substitui a faixa do ícone quando PopupData.illustrationFrames é definido.")]
        [SerializeField] private Vector2 illustrationSize = new Vector2(500f, 280f);
        [Tooltip("Tempo em que cada quadro fica parado antes do crossfade para o próximo.")]
        [SerializeField, Min(0.1f)] private float illustrationHoldSeconds = 1.4f;
        [SerializeField, Min(0.05f)] private float illustrationFadeSeconds = 0.6f;

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

        /// <summary>
        /// Raised when the panel becomes visible. Unlike the session pause/resume events,
        /// this fires only for the popup itself, so listeners (e.g. a menu that hides behind
        /// the popup) won't be triggered by unrelated session pauses.
        /// </summary>
        public event System.Action Shown;

        private PopupData _currentData;
        private Coroutine _fadeCoroutine;
        private Coroutine _autoCloseCoroutine;
        private CanvasGroup _canvasGroup;

        private Button _actionButton;
        private TMP_InputField _inputField;
        private bool _gateActionOnInput;

        private readonly List<GameObject> _choiceButtons = new List<GameObject>();

        private GameObject _keypadRoot;
        private int _keypadMaxDigits;
        private PopupIllustration _illustration;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>()
                        ?? gameObject.AddComponent<CanvasGroup>();

            if (actionButtonRoot != null)
                _actionButton = actionButtonRoot.GetComponentInChildren<Button>(true);
            if (inputFieldRoot != null)
                _inputField = inputFieldRoot.GetComponentInChildren<TMP_InputField>(true);
            if (_inputField != null)
                _inputField.onValueChanged.AddListener(OnInputValueChanged);

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
            if (bodyText != null) bodyText.text = KeepHyphenatedWordsTogether(data.body);

            if (backgroundImage != null)
            {
                backgroundImage.color = data.type switch
                {
                    PopupType.Warning     => warningColor,
                    PopupType.Interactive => interactiveColor,
                    _                     => normalColor
                };
            }

            bool hasIllustration = data.illustrationFrames != null && data.illustrationFrames.Length > 0;

            if (iconImage != null)
            {
                Sprite resolvedIcon = data.customIcon
                    ?? (data.type == PopupType.Warning ? warningIcon : infoIcon);

                bool hideIconRow = data.hideIcon || hasIllustration;
                bool showIcon = !hideIconRow && (data.customIcon != null || data.type != PopupType.Normal);
                iconImage.gameObject.SetActive(showIcon);

                var iconRow = iconImage.transform.parent;
                if (iconRow != null && iconRow != transform && iconRow != layoutRoot)
                    iconRow.gameObject.SetActive(!hideIconRow);
                if (showIcon)
                    iconImage.sprite = resolvedIcon;
            }

            if (hasIllustration)
            {
                EnsureIllustration();
                if (_illustration != null)
                    _illustration.Play(data.illustrationFrames, illustrationHoldSeconds, illustrationFadeSeconds);
            }
            else if (_illustration != null)
            {
                _illustration.Stop();
            }

            bool isChoice = data.choiceOptions != null && data.choiceOptions.Count > 0;
            bool isInteractive = data.type == PopupType.Interactive;

            ClearChoiceButtons();

            if (actionButtonRoot != null)
                actionButtonRoot.SetActive(isInteractive && !isChoice);
            if (isInteractive && !isChoice && actionButtonLabel != null)
                actionButtonLabel.text = data.actionButtonLabel;

            if (closeButtonRoot != null)
                closeButtonRoot.SetActive(!isInteractive);

            if (skipButtonRoot != null)
                skipButtonRoot.SetActive(data.showSkipButton && !isChoice);

            if (skipButtonLabel != null)
                skipButtonLabel.text = string.IsNullOrEmpty(data.skipButtonLabel)
                    ? "Pular"
                    : data.skipButtonLabel;

            if (isChoice)
                BuildChoiceButtons(data.choiceOptions, data.onChoiceSelected);

            if (inputFieldRoot != null)
                inputFieldRoot.SetActive(data.showInputField);

            bool useKeypad = data.showInputField && data.showNumericKeypad;
            if (useKeypad)
            {
                EnsureKeypad();
                _keypadMaxDigits = Mathf.Max(1, data.keypadMaxDigits);
                // Read-only stops Horizon OS from opening its system keyboard over the panel.
                if (_inputField != null) _inputField.readOnly = true;
            }
            if (_keypadRoot != null)
                _keypadRoot.SetActive(useKeypad);

            _gateActionOnInput = isInteractive && data.showInputField && data.requireInputForAction;
            RefreshActionInteractable();

            StopFade();

            var root = layoutRoot ?? (RectTransform)transform;
            LayoutRebuilder.MarkLayoutForRebuild(root);

            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            gameObject.SetActive(true);
            IsVisible = true;
            _fadeCoroutine = StartCoroutine(FadeAlpha(0f, 1f));

            Shown?.Invoke();

            StopAutoClose();
            if (!isInteractive && data.autoCloseSeconds > 0f)
                _autoCloseCoroutine = StartCoroutine(AutoCloseRoutine(data.autoCloseSeconds));
        }

        private void LateUpdate() => FitInteractionAreaToBackground();

        /// <summary>
        /// The poke and ray surfaces stretch to the canvas rect, but the content size fitter grows
        /// only Background, which is stretched inside that rect with a height offset. Grow or shrink
        /// the canvas by that offset so tall popups (the keypad) stay pokeable and ray-clickable
        /// all the way to their bottom buttons. The canvas keeps <see cref="cardMargin"/> above and
        /// below the card so the overhanging close button is not clipped by OVROverlayCanvas.
        /// </summary>
        private void FitInteractionAreaToBackground()
        {
            if (backgroundImage == null) return;
            var background = backgroundImage.rectTransform;
            if (background.parent is not RectTransform area) return;
            if (background.anchorMin.y != 0f || background.anchorMax.y != 1f) return;

            float overflow = background.sizeDelta.y + 2f * cardMargin;
            if (Mathf.Abs(overflow) < 0.5f) return;
            area.sizeDelta = new Vector2(area.sizeDelta.x, area.sizeDelta.y + overflow);
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;

            SafetyLog.Info("[PopupPanel] Hide()", this);

            StopAutoClose();
            StopFade();

            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            IsVisible = false;
            _fadeCoroutine = StartCoroutine(FadeAlphaAndDeactivate(_canvasGroup.alpha, 0f));

            Hidden?.Invoke();
        }

        public void OnActionButtonPressed()
        {
            if (_gateActionOnInput && string.IsNullOrWhiteSpace(_inputField != null ? _inputField.text : null))
                return;

            SafetyLog.Info("[PopupPanel] OnActionButtonPressed()", this);
            _currentData?.onActionPressed?.Invoke();
        }

        /// <summary>
        /// Clones <see cref="actionButtonRoot"/> once per option instead of instantiating
        /// <c>DualModeButton.prefab</c>: the clone is a plain <c>Button</c>
        /// under Background's <c>VerticalLayoutGroup</c>, so it inherits the canvas-wide poke/ray
        /// interaction (<c>ISDK_PokeCanvasInteraction</c> / <c>ISDK_RayCanvasInteraction</c>) for
        /// free and grows the panel through the fitter already on Background — no second
        /// interaction path stacked on the same surface.
        /// </summary>
        private void BuildChoiceButtons(IReadOnlyList<IReportOption> options, UnityEngine.Events.UnityAction<IReportOption> onChosen)
        {
            if (actionButtonRoot == null)
            {
                SafetyLog.Warning("[PopupPanel] actionButtonRoot ausente — não é possível construir os botões de escolha.", this);
                return;
            }

            var parent = actionButtonRoot.transform.parent;
            int insertIndex = actionButtonRoot.transform.GetSiblingIndex() + 1;

            foreach (var option in options)
            {
                var clone = Instantiate(actionButtonRoot, parent);
                clone.name = $"ChoiceButtonRoot_{option.Id}";
                clone.transform.SetSiblingIndex(insertIndex++);
                clone.SetActive(true);

                var label = clone.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) label.text = option.Label;

                var button = clone.GetComponentInChildren<Button>(true);
                if (button != null)
                {
                    var captured = option;
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() =>
                    {
                        Hide();
                        onChosen?.Invoke(captured);
                    });
                }

                _choiceButtons.Add(clone);
            }
        }

        private void ClearChoiceButtons()
        {
            foreach (var go in _choiceButtons)
                if (go != null) Destroy(go);
            _choiceButtons.Clear();
        }

        /// <summary>
        /// Builds a 3×4 keypad (1–9, backspace, 0, clear) right under the input field, from
        /// clones of <see cref="actionButtonRoot"/> for the same reason as the choice buttons:
        /// each clone inherits the canvas-wide poke and ray interaction.
        /// </summary>
        private void EnsureKeypad()
        {
            if (_keypadRoot != null) return;
            if (actionButtonRoot == null || inputFieldRoot == null)
            {
                SafetyLog.Warning("[PopupPanel] actionButtonRoot ou inputFieldRoot ausente — teclado numérico indisponível.", this);
                return;
            }

            var root = new GameObject("NumericKeypad", typeof(RectTransform));
            root.layer = inputFieldRoot.layer;
            var rect = (RectTransform)root.transform;
            rect.SetParent(inputFieldRoot.transform.parent, false);
            rect.SetSiblingIndex(inputFieldRoot.transform.GetSiblingIndex() + 1);

            var grid = root.AddComponent<GridLayoutGroup>();
            grid.cellSize = keypadCellSize;
            grid.spacing = keypadSpacing;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.MiddleCenter;

            var element = root.AddComponent<LayoutElement>();
            element.preferredWidth = 3 * keypadCellSize.x + 2 * keypadSpacing.x;
            element.preferredHeight = 4 * keypadCellSize.y + 3 * keypadSpacing.y;

            for (int digit = 1; digit <= 9; digit++)
                AddKeypadKey(rect, digit.ToString(), AppendDigit);
            AddKeypadKey(rect, keypadBackspaceLabel, _ => Backspace());
            AddKeypadKey(rect, "0", AppendDigit);
            AddKeypadKey(rect, keypadClearLabel, _ => SetInputText(string.Empty));

            _keypadRoot = root;
        }

        /// <summary>Builds the illustration row in the icon row's slot, which it replaces while shown.</summary>
        private void EnsureIllustration()
        {
            if (_illustration != null) return;
            var iconRow = iconImage != null ? iconImage.transform.parent : null;
            if (iconRow == null || iconRow == transform || iconRow == layoutRoot)
            {
                SafetyLog.Warning("[PopupPanel] Faixa do ícone ausente — ilustração indisponível.", this);
                return;
            }

            _illustration = PopupIllustration.Create(iconRow.parent, iconRow.GetSiblingIndex() + 1,
                illustrationSize, iconRow.gameObject.layer);
        }

        private void AddKeypadKey(RectTransform parent, string label, UnityEngine.Events.UnityAction<string> onPressed)
        {
            var key = Instantiate(actionButtonRoot, parent);
            key.name = $"Key_{label}";
            key.SetActive(true);

            var text = key.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null) text.text = label;

            var button = key.GetComponentInChildren<Button>(true);
            if (button == null) return;

            // The grid sizes the clone's root, but the action button's own LayoutElement still
            // asks for its authored width and would overflow into the neighbouring key.
            var buttonLayout = button.GetComponent<LayoutElement>();
            if (buttonLayout != null)
            {
                buttonLayout.minWidth = -1f;
                buttonLayout.preferredWidth = keypadCellSize.x;
                buttonLayout.minHeight = -1f;
                buttonLayout.preferredHeight = keypadCellSize.y;
            }

            // Persistent listeners are copied onto clones; disable OnActionButtonPressed so a digit does not submit.
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                if (button.onClick.GetPersistentMethodName(i) == nameof(OnActionButtonPressed))
                    button.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);

            button.interactable = true;
            button.onClick.AddListener(() => onPressed(label));
        }

        private void AppendDigit(string digit)
        {
            string current = _inputField != null ? _inputField.text : string.Empty;
            if (current.Length >= _keypadMaxDigits) return;
            SetInputText(current + digit);
        }

        private void Backspace()
        {
            string current = _inputField != null ? _inputField.text : string.Empty;
            if (current.Length > 0) SetInputText(current.Substring(0, current.Length - 1));
        }

        private void SetInputText(string value)
        {
            if (_inputField != null) _inputField.text = value;
        }

        private void OnInputValueChanged(string _) => RefreshActionInteractable();

        /// <summary>
        /// Enables the action button only when its required input field is non-empty.
        /// No-op (button stays interactable) when the current popup isn't gated on input.
        /// </summary>
        private void RefreshActionInteractable()
        {
            if (_actionButton == null) return;

            bool blocked = _gateActionOnInput &&
                           string.IsNullOrWhiteSpace(_inputField != null ? _inputField.text : null);
            _actionButton.interactable = !blocked;
        }

        private void OnDestroy()
        {
            if (_inputField != null)
                _inputField.onValueChanged.RemoveListener(OnInputValueChanged);
            ClearChoiceButtons();
        }

        public void OnCloseButtonPressed()
        {
            SafetyLog.Info("[PopupPanel] OnCloseButtonPressed()", this);
            Hide();
        }

        public void OnSkipButtonPressed()
        {
            SafetyLog.Info("[PopupPanel] OnSkipButtonPressed()", this);
            _currentData?.onSkipPressed?.Invoke();
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

        private IEnumerator AutoCloseRoutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            _autoCloseCoroutine = null;
            SafetyLog.Info($"[PopupPanel] Auto-fechando após {delay:0.#}s.", this);
            Hide();
        }

        private void StopAutoClose()
        {
            if (_autoCloseCoroutine != null)
            {
                StopCoroutine(_autoCloseCoroutine);
                _autoCloseCoroutine = null;
            }
        }
    }
}
