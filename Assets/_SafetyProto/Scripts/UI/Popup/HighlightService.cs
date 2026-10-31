using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Feedback;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>The onboarding's highlight: keeps a target pulsing through <see cref="AttentionService"/>.</summary>
    public static class HighlightService
    {
        private static bool _warned;

        public static void Enable(GameObject target)
        {
            var service = AttentionService.Instance;
            if (service == null)
            {
                if (!_warned)
                    SafetyLog.Warning("HighlightService: nenhum AttentionService na cena; o destaque do onboarding não aparece.");
                _warned = true;
                return;
            }
            service.Hold(target);
        }

        public static void Disable(GameObject target) => AttentionService.Instance?.Release(target);
    }
}
