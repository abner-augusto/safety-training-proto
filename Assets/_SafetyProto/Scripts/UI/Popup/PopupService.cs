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

        private bool _ovrReady;
        private bool _sessionPausedByUs;

        private PopupData _pendingData;

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
                SafetyLog.Warning("[PopupService] popupPanel not assigned in Inspector.", this);
                return;
            }

            if (popupPanel.gameObject.activeSelf)
            {
                popupPanel.gameObject.SetActive(false);
                SafetyLog.Info("[PopupService] PopUpCanvas deactivated on Start() — awaiting OVR.", this);
            }

            StartCoroutine(WaitForOVRReady());
        }

        private IEnumerator WaitForOVRReady()
        {
            int waited = 0;
            while (waited < ovrReadyFrames)
            {
                if (OVRPlugin.initialized)
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        var mat = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                        if (mat != Matrix4x4.identity)
                        {
                            waited++;
                        }
                    }
                }
                yield return null;
            }

            _ovrReady = true;
            SafetyLog.Info($"[PopupService] OVR ready after {Time.frameCount} frames.", this);

            if (_pendingData != null)
            {
                SafetyLog.Info("[PopupService] Executing pending Show().", this);
                Show(_pendingData);
                _pendingData = null;
            }
        }

        public void Show(PopupData data)
        {
            if (popupPanel == null) return;

            if (!_ovrReady)
            {
                SafetyLog.Info($"[PopupService] Show('{data.title}') queued — OVR not ready.", this);
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

            _pendingData = null;
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

        public void ShowInteractive(string title, string body, string buttonLabel, UnityAction onAction)
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