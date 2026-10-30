using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Feedback;
using UnityEngine;

namespace SafetyProto.Runtime
{
    /// <summary>
    /// Evaluator dashboard command handler for "return_all_ppe": every loose PPE, distractor,
    /// scaffold piece and tool goes back to its home pose (see <see cref="LooseObjectRecall"/>),
    /// without restarting the session. Replaces the restart the evaluator used when the hands
    /// knocked PPE out of reach. Always accepted; the result summary reaches the evaluator in the
    /// ack and the session log as an EvaluatorCommand entry.
    ///
    /// Lives in the Networking assembly for the same reason as <see cref="RecenterCommandHandler"/>.
    /// </summary>
    public class ReturnLooseObjectsCommandHandler : MonoBehaviour, SafetyProto.Networking.Dashboard.IDashboardCommandHandler
    {
        private const string CommandName = "return_all_ppe";

        public string Command => CommandName;

        public bool TryExecute(out string reason)
        {
            var result = LooseObjectRecall.RecallAll();
            reason = result.Describe();
            SafetyLog.Info($"[ReturnLooseObjectsCommandHandler] {reason}", this);
            return true;
        }
    }
}
