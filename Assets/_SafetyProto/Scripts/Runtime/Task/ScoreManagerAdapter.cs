using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Safety;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Runtime.Task
{
    public class ScoreManagerAdapter : MonoBehaviour
    {
        private IScoreService _scoreService;
        private ScoreRuleEngineCore _core;

        private void Awake()
        {
            _scoreService = ScoreService.Instance;
        }

        private void OnEnable()
        {
            if (EventBus.Instance == null || !this.IsEventBusReady())
                return;

            _core = new ScoreRuleEngineCore(
                bus: EventBus.Instance,
                scoreService: _scoreService,
                logger: new SafetyLogAdapter());

            _core.Subscribe();
            _scoreService.ScoreChanged += HandleScoreChanged;
        }

        private void OnDisable()
        {
            _core?.Dispose();
            _core = null;

            if (_scoreService != null)
                _scoreService.ScoreChanged -= HandleScoreChanged;
        }

        private void HandleScoreChanged(int newScore, int delta, string reason)
        {
            SafetyLog.Info($"[ScoreManagerAdapter] [Score] {reason}: {(delta >= 0 ? "+" : "")}{delta} (Total: {newScore})", this);
            ScoreEvents.RaiseScoreChanged(new ScoreChangedEventArgs(newScore, delta));
        }
    }
}
