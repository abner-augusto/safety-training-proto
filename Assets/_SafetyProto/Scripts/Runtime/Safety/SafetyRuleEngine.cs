#nullable enable
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.PPE;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Runtime.Safety
{
    public class SafetyRuleEngine : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PPEManager? ppeManager;

        [Header("Settings")]
        [SerializeField] private bool verboseLogging;

        [Header("Timing")]
        [SerializeField] private TimerSystem? timerSystem;

        private SafetyRuleEngineCore? _core;

        private void Awake()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

            var eventBus = EventBus.Instance!;

            if (ppeManager == null)
            {
                ppeManager = FindFirstObjectByType<PPEManager>();
                if (ppeManager == null)
                {
                    SafetyLog.Warning("SafetyRuleEngine: PPEManager not found. Falling back to PPE event tracking only.", this);
                }
            }

            if (timerSystem == null)
            {
                timerSystem = FindFirstObjectByType<TimerSystem>();
            }

            IPPEComplianceChecker? ppeChecker = ppeManager != null
                ? new PPEComplianceAdapter(ppeManager)
                : null;

            ITimerSource? timerSource = timerSystem != null
                ? new TimerSystemAdapter(timerSystem)
                : null;

            _core = new SafetyRuleEngineCore(
                bus: eventBus,
                ppeChecker: ppeChecker,
                timer: timerSource,
                logger: new SafetyLogAdapter(),
                verboseLogging: verboseLogging);

            _core.Subscribe();

            eventBus.onGroupStarted.AddListener(_core.NotifyGroupStarted);
            eventBus.onGroupCompleted.AddListener(_core.NotifyGroupCompleted);
            eventBus.onTaskStarted.AddListener(_core.NotifyTaskStarted);
        }

        private void OnDestroy()
        {
            var eventBus = EventBus.Instance;
            if (eventBus != null && _core != null)
            {
                eventBus.onGroupStarted.RemoveListener(_core.NotifyGroupStarted);
                eventBus.onGroupCompleted.RemoveListener(_core.NotifyGroupCompleted);
                eventBus.onTaskStarted.RemoveListener(_core.NotifyTaskStarted);
            }
            _core?.Dispose();
            _core = null;
        }
    }
}
