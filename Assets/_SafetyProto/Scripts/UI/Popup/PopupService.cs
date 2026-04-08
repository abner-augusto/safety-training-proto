using System.Collections;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.UI
{
    public class PopupService : MonoBehaviour
    {
        public static PopupService Instance { get; private set; }

        [SerializeField] private PopupPanel popupPanel;

        [Header("OVR Initialization Guard")]
        [Tooltip("Número de frames a aguardar antes de permitir qualquer Show(), dando tempo ao OVR " +
                 "inicializar as stereo projection matrices. Sem esse guard, OVROverlayCanvas " +
                 "calcula resolução com matriz inválida → coordenada negativa → XR_ERROR_SWAPCHAIN_RECT_INVALID.")]
        [SerializeField, Min(0)] private int ovrReadyFrames = 10;

        private bool _ovrReady = false;
        private bool _sessionPausedByUs = false;

        // Pedido de Show() que chegou antes do OVR estar pronto — executado assim que ficar pronto.
        private PopupData _pendingData = null;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            if (popupPanel == null)
            {
                SafetyLog.Warning("[PopupService] popupPanel não atribuído no Inspector.", this);
                return;
            }

            // O PopUpCanvas deve começar INATIVO para não disparar OVROverlayCanvas.Update()
            // antes das stereo matrices do OVR estarem disponíveis.
            // PopupPanel.Show() ativa o GameObject quando necessário.
            if (popupPanel.gameObject.activeSelf)
            {
                popupPanel.gameObject.SetActive(false);
                SafetyLog.Info("[PopupService] PopUpCanvas desativado no Start() — aguardando OVR ficar pronto.", this);
            }

            StartCoroutine(WaitForOVRReady());
        }

        /// <summary>
        /// Aguarda o OVR inicializar as stereo matrices antes de permitir qualquer Show().
        /// Sem isso, OVROverlayCanvas.CalculateScaledResolution() usa GetStereoProjectionMatrix()
        /// inválido → RenderTexture com y negativo → XR_ERROR_SWAPCHAIN_RECT_INVALID → freeze.
        /// </summary>
        private IEnumerator WaitForOVRReady()
        {
            int waited = 0;
            while (waited < ovrReadyFrames)
            {
                // Verifica se o OVRPlugin já está inicializado E se a camera já tem
                // projection matrices válidas (não-identidade no eye esquerdo).
                if (OVRPlugin.initialized)
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        var mat = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                        // A matrix identidade indica que OVR ainda não entregou dados reais.
                        if (mat != Matrix4x4.identity)
                        {
                            waited++; // conta só frames com matrix válida, mais conservador
                        }
                    }
                }
                yield return null;
            }

            _ovrReady = true;
            SafetyLog.Info($"[PopupService] OVR pronto após {Time.frameCount} frames. " +
                           "PopupService liberado para exibir popups.", this);

            // Executa show pendente se havia um enfileirado antes do OVR estar pronto.
            if (_pendingData != null)
            {
                SafetyLog.Info("[PopupService] Executando Show() pendente.", this);
                Show(_pendingData);
                _pendingData = null;
            }
        }

        // ── API pública (inalterada externamente) ─────────────────────────────

        public void Show(PopupData data)
        {
            if (popupPanel == null) return;

            if (!_ovrReady)
            {
                // Enfileira o pedido — será exibido assim que o OVR estiver pronto.
                SafetyLog.Info($"[PopupService] Show('{data.title}') enfileirado — OVR ainda não está pronto.", this);
                _pendingData = data;
                return;
            }

            if (!_sessionPausedByUs)
            {
                SessionEvents.RaiseSessionPaused();
                _sessionPausedByUs = true;
            }

            popupPanel.Show(data);
        }

        public void Hide()
        {
            if (popupPanel == null) return;

            _pendingData = null; // cancela qualquer show pendente
            popupPanel.Hide();

            if (_sessionPausedByUs)
            {
                SessionEvents.RaiseSessionResumed();
                _sessionPausedByUs = false;
            }
        }

        public void ShowNormal(string title, string body)
            => Show(new PopupData { type = PopupType.Normal, title = title, body = body });

        public void ShowWarning(string title, string body)
            => Show(new PopupData { type = PopupType.Warning, title = title, body = body });

        public void ShowInteractive(string title, string body,
                                    string buttonLabel, UnityAction onAction)
        {
            var data = new PopupData
            {
                type              = PopupType.Interactive,
                title             = title,
                body              = body,
                actionButtonLabel = buttonLabel,
                onActionPressed   = new UnityEvent()
            };
            data.onActionPressed.AddListener(onAction);
            Show(data);
        }
    }
}