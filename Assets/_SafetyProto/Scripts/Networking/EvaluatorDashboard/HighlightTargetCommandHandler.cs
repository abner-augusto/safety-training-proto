using System.Globalization;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Feedback;
using SafetyProto.Networking.Dashboard;
using SafetyProto.Runtime.Feedback;
using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Evaluator dashboard command handler for "highlight_target": the chosen object or slot
    /// pulses, with a positional ping, for the requested time (see <see cref="AttentionService"/>).
    /// Lets the evaluator point at the next object from across the room instead of walking over.
    /// The bootstrap logs every accepted use as an EvaluatorCommand with the target id.
    /// </summary>
    public class HighlightTargetCommandHandler : MonoBehaviour, IDashboardCommandWithArgs
    {
        private const string CommandName = "highlight_target";

        public string Command => CommandName;

        public bool TryExecute(out string reason) => TryExecute(new DashboardCommandArgs(), out reason);

        public bool TryExecute(DashboardCommandArgs args, out string reason)
        {
            var service = AttentionService.Instance;
            if (service == null)
            {
                reason = "Destaque indisponível nesta cena.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(args.targetId))
            {
                reason = "Escolha o objeto a destacar.";
                return false;
            }

            float seconds = AttentionTargets.ClampSeconds(args.durationSeconds);
            if (!service.TryFlash(args.targetId, seconds, withSound: true, out var info))
            {
                reason = $"Objeto não encontrado na cena: {args.targetId}.";
                return false;
            }

            reason = $"{info.Label} destacado por {seconds.ToString("0", CultureInfo.InvariantCulture)} s.";
            SafetyLog.Info($"[HighlightTargetCommandHandler] {reason}", this);
            return true;
        }
    }
}
