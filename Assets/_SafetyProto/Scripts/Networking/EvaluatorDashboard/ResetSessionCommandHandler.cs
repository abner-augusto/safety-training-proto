using SafetyProto.Core.Logging;
using SafetyProto.Runtime;
using UnityEngine;

namespace SafetyProto.Networking.Dashboard
{
    /// <summary>
    /// Evaluator dashboard command handler for "reset_session". Runs the same reset the in-VR
    /// menu's restart button uses (<see cref="SceneLoader.ResetSession"/>): every
    /// ISessionResettable flushes its state — the session logger writes the partial log tagged
    /// "SessionReset" — and the scene reloads, which returns the participant to the spawn point
    /// with all PPE unequipped and every interactive object back in its authored state.
    /// </summary>
    public class ResetSessionCommandHandler : MonoBehaviour, IDashboardCommandHandler
    {
        private const string CommandName = "reset_session";

        [Tooltip("Scene reset entry point. Resolved from the scene when left empty.")]
        [SerializeField] private SceneLoader sceneLoader;

        private bool _resetRequested;

        public string Command => CommandName;

        public bool TryExecute(out string reason)
        {
            if (_resetRequested)
            {
                reason = "Reinício já em andamento.";
                return false;
            }

            if (sceneLoader == null)
                sceneLoader = FindFirstObjectByType<SceneLoader>(FindObjectsInactive.Include);
            if (sceneLoader == null)
            {
                SafetyLog.Warning("[ResetSessionCommandHandler] SceneLoader não encontrado na cena.", this);
                reason = "Reinício de sessão não configurado.";
                return false;
            }

            _resetRequested = true;
            sceneLoader.ResetSession();
            reason = string.Empty;
            return true;
        }
    }
}
