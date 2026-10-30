using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Sessions;
using SafetyProto.Runtime;
using SafetyProto.Runtime.Session;
using UnityEngine;

namespace SafetyProto.Networking.Dashboard
{
    /// <summary>
    /// Evaluator dashboard command handler for "start_session" with args
    /// <c>{ participantCode, mode }</c>: the evaluator, not the participant, decides who trains
    /// and in which mode. On the waiting screen it starts the session at once. Over a live or
    /// finished session it files the new identity in <see cref="SessionHandoff"/> and runs the
    /// same reset as "reset_session", so the reloaded scene starts straight away with it.
    /// </summary>
    public class StartSessionCommandHandler : MonoBehaviour, IDashboardCommandWithArgs
    {
        private const string CommandName = "start_session";

        [Tooltip("Scene reset entry point. Resolved from the scene when left empty.")]
        [SerializeField] private SceneLoader sceneLoader;
        [Tooltip("Resolved from the scene when left empty.")]
        [SerializeField] private TrainingSessionManager sessionManager;

        // Two commands can arrive in the same frame before the reload starts; the second must
        // not queue a second reload. Cleared on enable so a handler that outlives the reload
        // does not refuse forever.
        private bool _restartRequested;

        public string Command => CommandName;

        private void OnEnable() => _restartRequested = false;

        public bool TryExecute(out string reason)
        {
            reason = "Informe o código do participante e o modo.";
            return false;
        }

        public bool TryExecute(DashboardCommandArgs args, out string reason)
        {
            if (_restartRequested)
            {
                reason = "Reinício já em andamento.";
                return false;
            }
            if (!ParticipantCode.TryNormalize(args?.participantCode, out var participantId, out reason))
                return false;
            if (!SessionModeState.TryParse(args?.mode, out var mode))
            {
                reason = "Modo inválido: escolha Guiado ou Avaliação.";
                return false;
            }

            if (sessionManager == null)
                sessionManager = FindAnyObjectByType<TrainingSessionManager>(FindObjectsInactive.Include);
            if (sessionManager == null)
            {
                SafetyLog.Warning("[StartSessionCommandHandler] TrainingSessionManager não encontrado na cena.", this);
                reason = "Gerenciador de sessão não configurado.";
                return false;
            }

            string label = $"{participantId} · {(mode == SessionMode.Evaluation ? "Avaliação" : "Guiado")}";

            if (!sessionManager.IsSessionStarted)
            {
                var starter = FindStarter();
                if (starter == null)
                {
                    SafetyLog.Warning("[StartSessionCommandHandler] Nenhum IEvaluatorSessionStarter ativo na cena.", this);
                    reason = "Fluxo de início não configurado.";
                    return false;
                }
                if (!starter.TryStartFromEvaluator(participantId, mode, out reason))
                    return false;
                reason = $"Sessão iniciada: {label}.";
                return true;
            }

            if (sceneLoader == null)
                sceneLoader = FindAnyObjectByType<SceneLoader>(FindObjectsInactive.Include);
            if (sceneLoader == null)
            {
                SafetyLog.Warning("[StartSessionCommandHandler] SceneLoader não encontrado na cena.", this);
                reason = "Reinício de sessão não configurado.";
                return false;
            }

            SessionHandoff.Request(new SessionStartRequest(participantId, ParticipantSources.Dashboard, mode));
            _restartRequested = true;
            sceneLoader.ResetSession();
            reason = $"Sessão anterior encerrada; iniciando {label}.";
            return true;
        }

        private static IEvaluatorSessionStarter FindStarter()
        {
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>())
            {
                if (behaviour is IEvaluatorSessionStarter starter && behaviour.isActiveAndEnabled)
                    return starter;
            }
            return null;
        }
    }
}
