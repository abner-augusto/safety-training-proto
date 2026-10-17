using System.Collections.Generic;
using System.Text;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Logging;
using SafetyProto.Utils;
using TMPro;
using UnityEngine;

namespace SafetyProto.UI
{
    public class LogHUD : MonoBehaviour
    {
        [Tooltip("Maximum number of messages to keep in the log.")]
        public int maxLines = 20;
        [Tooltip("Assign the TextMeshProUGUI that renders the HUD. Falls back to first child if empty.")]
        [SerializeField] private TextMeshProUGUI logText;

        private readonly Queue<string> _entries = new();
        private readonly StringBuilder _allLogs = new();
        private readonly StringBuilder _displayBuilder = new();
        private bool _dirty;

        private void OnEnable()
        {
            if (!this.IsEventBusReady())
                return;

            if (!TryInitializeLogText())
                return;

            if (EventBus.Instance != null)
            {
                EventBus.Instance.Subscribe<SessionStartedEventArgs>(OnSessionStarted);
                EventBus.Instance.Subscribe<SessionPausedEventArgs>(OnSessionPaused);
                EventBus.Instance.Subscribe<SessionResumedEventArgs>(OnSessionResumed);
                EventBus.Instance.Subscribe<SessionEndedEventArgs>(OnSessionEnded);

                EventBus.Instance.Subscribe<ActionAttemptedEvent>(OnActionAttempt);
                EventBus.Instance.Subscribe<PPEStateChangedEventArgs>(OnPpeStateChanged);

                EventBus.Instance.Subscribe<TaskEventArgs>(OnTaskStarted);
                EventBus.Instance.Subscribe<TaskEventArgs>(OnTaskCompleted);

                EventBus.Instance.Subscribe<ScoreChangedEventArgs>(OnScoreChanged);

                EventBus.Instance.Subscribe<TaskGroupEventArgs>(OnGroupStarted);
                EventBus.Instance.Subscribe<TaskGroupEventArgs>(OnGroupCompleted);

                EventBus.Instance.Subscribe<SafetyViolationEventArgs>(OnSafetyViolation);
                EventBus.Instance.Subscribe<CriticalSafetyFailureEventArgs>(OnCriticalSafetyFailure);
                EventBus.Instance.Subscribe<SafetyErrorEventArgs>(OnSafetyError);
                EventBus.Instance.Subscribe<SessionCompletedEventArgs>(OnSessionCompleted);
            }
        }

        private void OnDisable()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.Unsubscribe<SessionStartedEventArgs>(OnSessionStarted);
                EventBus.Instance.Unsubscribe<SessionPausedEventArgs>(OnSessionPaused);
                EventBus.Instance.Unsubscribe<SessionResumedEventArgs>(OnSessionResumed);
                EventBus.Instance.Unsubscribe<SessionEndedEventArgs>(OnSessionEnded);

                EventBus.Instance.Unsubscribe<ActionAttemptedEvent>(OnActionAttempt);
                EventBus.Instance.Unsubscribe<PPEStateChangedEventArgs>(OnPpeStateChanged);

                EventBus.Instance.Unsubscribe<TaskEventArgs>(OnTaskStarted);
                EventBus.Instance.Unsubscribe<TaskEventArgs>(OnTaskCompleted);

                EventBus.Instance.Unsubscribe<ScoreChangedEventArgs>(OnScoreChanged);

                EventBus.Instance.Unsubscribe<TaskGroupEventArgs>(OnGroupStarted);
                EventBus.Instance.Unsubscribe<TaskGroupEventArgs>(OnGroupCompleted);

                EventBus.Instance.Unsubscribe<SafetyViolationEventArgs>(OnSafetyViolation);
                EventBus.Instance.Unsubscribe<CriticalSafetyFailureEventArgs>(OnCriticalSafetyFailure);
                EventBus.Instance.Unsubscribe<SafetyErrorEventArgs>(OnSafetyError);
                EventBus.Instance.Unsubscribe<SessionCompletedEventArgs>(OnSessionCompleted);
            }
        }

        private bool TryInitializeLogText()
        {
            if (logText == null)
            {
                logText = GetComponentInChildren<TextMeshProUGUI>();
            }

            if (logText == null)
            {
                SafetyLog.Error("LogHUD: logText não foi atribuído no Inspector.", this);
                enabled = false;
                return false;
            }

            logText.text = "Registro de atividades ativo";
            return true;
        }

        private void OnSessionStarted(SessionStartedEventArgs args) => AppendLog("[Sessão] Iniciada");

        private void OnSessionPaused(SessionPausedEventArgs args) => AppendLog("[Sessão] Pausada");

        private void OnSessionResumed(SessionResumedEventArgs args) => AppendLog("[Sessão] Retomada");

        private void OnSessionEnded(SessionEndedEventArgs args) => AppendLog("[Sessão] Encerrada");

        private void OnSessionCompleted(SessionCompletedEventArgs args)
        {
            int minutes = Mathf.FloorToInt(args.totalElapsedTime / 60f);
            int seconds = Mathf.FloorToInt(args.totalElapsedTime % 60f);
            string formattedTime = $"{minutes:00}:{seconds:00}";
            AppendLog($"[Sessão] Concluída | Tempo={formattedTime} | Pontuação={args.totalScore} | Tarefas={args.tasksCompleted}/{args.totalTasks} | Violações de ordem={args.orderViolationCount}");
        }

        private void OnGroupStarted(TaskGroupEventArgs args)
        {
            if (args.Phase != TaskGroupPhase.Started) return;
            string groupName = args.Group != null ? args.Group.groupName : "<Grupo sem nome>";
            AppendLog($"[Grupo] Iniciado '{groupName}'");
        }

        private void OnGroupCompleted(TaskGroupEventArgs args)
        {
            if (args.Phase != TaskGroupPhase.Completed) return;
            string groupName = args.Group != null ? args.Group.groupName : "<Grupo sem nome>";
            AppendLog($"[Grupo] Concluído '{groupName}'");
        }

        private void OnTaskStarted(TaskEventArgs args)
        {
            if (args.Phase != TaskPhase.Started) return;
            string taskName = args.Task != null ? args.Task.taskName : "<Tarefa sem nome>";
            AppendLog($"[Tarefa] Iniciada '{taskName}'");
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
            if (args.Phase != TaskPhase.Completed) return;
            string taskName = args.Task != null ? args.Task.taskName : "<Tarefa sem nome>";
            AppendLog($"[Tarefa] Concluída '{taskName}'");
        }

        private void OnScoreChanged(ScoreChangedEventArgs args)
        {
            string sign = args.Delta >= 0 ? "+" : string.Empty;
            AppendLog($"[Pontuação] {sign}{args.Delta} (Total={args.TotalScore})");
        }

        private void OnPpeStateChanged(PPEStateChangedEventArgs args)
        {
            AppendLog($"[EPI] {GetPpeLabel(args.PpeType)}: {(args.IsWearing ? "EQUIPADO" : "REMOVIDO")}");
        }

        private void OnActionAttempt(ActionAttemptedEvent args)
        {
            var positionText = args.Position.HasValue
                ? $"({args.Position.Value.X:F2}, {args.Position.Value.Y:F2}, {args.Position.Value.Z:F2})"
                : "<sem posição>";
            AppendLog($"[Ação] {GetActionLabel(args.ActionId)} em {positionText}");
        }

        private void OnSafetyViolation(SafetyViolationEventArgs args)
        {
            string code = GetViolationLabel(args.ViolationCode);
            string message = string.IsNullOrEmpty(args.Message) ? "Sem detalhes" : args.Message;
            string task = string.IsNullOrEmpty(args.TaskName) ? "-" : args.TaskName;
            string group = string.IsNullOrEmpty(args.GroupName) ? "-" : args.GroupName;
            AppendLog($"[Segurança] VIOLAÇÃO: {code} | {message} (Tarefa={task}, Grupo={group})");
        }

        private void OnCriticalSafetyFailure(CriticalSafetyFailureEventArgs args)
        {
            string reason = string.IsNullOrEmpty(args.Reason) ? "Motivo desconhecido" : args.Reason;
            AppendLog($"[Segurança] FALHA CRÍTICA | {reason} [{args.ViolationCount} em {args.WindowSeconds}s]");
        }

        private void OnSafetyError(SafetyErrorEventArgs args)
        {
            string source = string.IsNullOrEmpty(args.Source) ? "Origem desconhecida" : args.Source;
            AppendLog($"[Segurança] ERRO INTERNO DO SISTEMA | Origem={source}");
        }

        private static string GetPpeLabel(PPEType ppeType)
        {
            return ppeType switch
            {
                PPEType.Helmet => "Capacete",
                PPEType.Goggles => "Óculos de proteção",
                PPEType.Harness => "Cinto paraquedista",
                PPEType.Vest => "Colete de segurança",
                PPEType.Boots => "Botina de segurança",
                PPEType.GloveLeft => "Luva esquerda",
                PPEType.GloveRight => "Luva direita",
                _ => "EPI não identificado"
            };
        }

        private static string GetActionLabel(string actionId)
        {
            return actionId switch
            {
                "connect_harness" => "Conectar talabarte",
                "install_guardrail" => "Instalar guarda-corpo",
                "install_toeboard" => "Instalar rodapé",
                "flag_safety_net" => "Reportar irregularidade na tela fachadeira",
                _ => $"Ação não catalogada ({actionId})"
            };
        }

        private static string GetViolationLabel(string code)
        {
            return code switch
            {
                ViolationCodes.ActionIdMissing => "Ação não identificada",
                ViolationCodes.NoActiveGroup => "Nenhum grupo de tarefas ativo",
                ViolationCodes.WrongAction => "Ação incorreta",
                ViolationCodes.PpeMissing => "EPI obrigatório ausente",
                ViolationCodes.TaskNotPerformed => "Tarefa não realizada",
                ViolationCodes.PpeOutOfOrder => "EPI fora de ordem",
                ViolationCodes.GateFailed => "Inspeção não aprovada",
                _ => $"Violação não identificada ({code})"
            };
        }

        private void AppendLog(string message)
        {
            lock (_entries)
            {
                _entries.Enqueue(message);
                _allLogs.AppendLine(message);

                if (_entries.Count > maxLines)
                {
                    _entries.Dequeue();
                    RebuildDisplayBuilder();
                }
                else
                {
                    if (_displayBuilder.Length > 0)
                    {
                        _displayBuilder.Append('\n');
                    }
                    _displayBuilder.Append(message);
                }
            }

            _dirty = true;
        }

        private void LateUpdate()
        {
            if (!_dirty)
                return;
            _dirty = false;
            RefreshDisplay();
        }

        private void RebuildDisplayBuilder()
        {
            _displayBuilder.Clear();
            bool first = true;
            foreach (var entry in _entries)
            {
                if (!first)
                    _displayBuilder.Append('\n');
                _displayBuilder.Append(entry);
                first = false;
            }
        }

        private void RefreshDisplay()
        {
            if (logText == null)
            {
                return;
            }

            lock (_entries)
            {
                logText.SetText(_displayBuilder);
            }
            // SetText already dirties the mesh; ForceMeshUpdate() rebuilds it synchronously per call.
        }

        public string GetFullLog() => _allLogs.ToString();
        public void ClearLog()
        {
            lock (_entries)
            {
                _entries.Clear();
                _allLogs.Clear();
            }
            RefreshDisplay();
        }
    }
}
