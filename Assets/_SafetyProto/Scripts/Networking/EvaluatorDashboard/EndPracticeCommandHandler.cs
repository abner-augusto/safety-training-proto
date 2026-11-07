using SafetyProto.Core.Logging;
using SafetyProto.Networking.Dashboard;
using SafetyProto.Runtime.Practice;
using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Evaluator dashboard command handler for "end_practice": closes the practice area before the
    /// countdown runs out, for a participant who has tried enough or is struggling there. The
    /// area ends exactly as it does on "Continuar" and fades the participant back to the
    /// training's start point; the reason is recorded as <c>evaluator</c>. Logged as an
    /// EvaluatorCommand like every in-session command.
    /// </summary>
    public class EndPracticeCommandHandler : MonoBehaviour, IDashboardCommandHandler
    {
        private const string CommandName = "end_practice";

        public string Command => CommandName;

        public bool TryExecute(out string reason)
        {
            var area = PracticeArea.Active;
            if (area == null)
            {
                reason = "Nenhuma área de prática aberta.";
                return false;
            }
            // Close is a silent no-op while the area is already closing; accepting would log an
            // intervention that changed nothing.
            if (area.IsClosing)
            {
                reason = "A área de prática já está sendo encerrada.";
                return false;
            }

            area.Close("evaluator");
            reason = "Área de prática encerrada pelo avaliador.";
            SafetyLog.Info($"[EndPracticeCommandHandler] {reason}", this);
            return true;
        }
    }
}
