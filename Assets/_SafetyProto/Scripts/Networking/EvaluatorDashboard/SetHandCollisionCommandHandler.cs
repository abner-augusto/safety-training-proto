using SafetyProto.Core.Logging;
using SafetyProto.Networking.Dashboard;
using SafetyProto.Runtime.Interaction;
using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Evaluator dashboard command handler for "set_hand_collision": switches whether the physical
    /// hands push interactive objects (<c>args.mode</c> = "push" | "pass_through"), so both can
    /// be compared on the headset in one build. Logged as an EvaluatorCommand like every
    /// in-session command.
    /// </summary>
    public class SetHandCollisionCommandHandler : MonoBehaviour, IDashboardCommandWithArgs
    {
        private const string CommandName = "set_hand_collision";

        public string Command => CommandName;

        public bool TryExecute(out string reason) => TryExecute(new DashboardCommandArgs(), out reason);

        public bool TryExecute(DashboardCommandArgs args, out string reason)
        {
            var collision = HandObjectCollision.Instance;
            if (collision == null)
            {
                reason = "Alternância das mãos indisponível nesta cena.";
                return false;
            }
            if (!HandObjectCollision.TryParse(args.mode, out var mode))
            {
                reason = $"Modo das mãos desconhecido: '{args.mode}'.";
                return false;
            }

            collision.Choose(mode);
            reason = mode == HandObjectCollision.Mode.PassThrough
                ? "Mãos atravessam os objetos."
                : "Mãos empurram os objetos.";
            SafetyLog.Info($"[SetHandCollisionCommandHandler] {reason}", this);
            return true;
        }
    }
}
