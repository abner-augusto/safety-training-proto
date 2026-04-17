#nullable enable
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Gameplay.PPE;
using SafetyProto.Gameplay.Task;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Gameplay.Safety
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

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }

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
                bus: EventBus.Instance,
                ppeChecker: ppeChecker,
                timer: timerSource,
                logger: new SafetyLogAdapter(),
                verboseLogging: verboseLogging);

            _core.Subscribe();
        }

        private void OnDestroy()
        {
            _core?.Dispose();
            _core = null;
        }
    }
}
