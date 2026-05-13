#nullable enable
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Safety;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Runtime.Safety
{
    public class SafetyRuleEngine : MonoBehaviour
    {
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

            if (timerSystem == null)
            {
                timerSystem = FindFirstObjectByType<TimerSystem>();
            }

            ITimerSource? timerSource = timerSystem != null
                ? new TimerSystemAdapter(timerSystem)
                : null;

            _core = new SafetyRuleEngineCore(
                bus: eventBus,
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
