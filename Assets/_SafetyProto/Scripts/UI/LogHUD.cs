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
                EventBus.Instance.onSessionStarted.AddListener(OnSessionStarted);
                EventBus.Instance.onSessionPaused.AddListener(OnSessionPaused);
                EventBus.Instance.onSessionResumed.AddListener(OnSessionResumed);
                EventBus.Instance.onSessionEnded.AddListener(OnSessionEnded);

                EventBus.Instance.onActionAttempt.AddListener(OnActionAttempt);
                EventBus.Instance.onPpeStateChanged.AddListener(OnPpeStateChanged);

                EventBus.Instance.onTaskStarted.AddListener(OnTaskStarted);
                EventBus.Instance.onTaskCompleted.AddListener(OnTaskCompleted);

                EventBus.Instance.onScoreChanged.AddListener(OnScoreChanged);

                EventBus.Instance.onGroupStarted.AddListener(OnGroupStarted);
                EventBus.Instance.onGroupCompleted.AddListener(OnGroupCompleted);

                EventBus.Instance.onSafetyViolation.AddListener(OnSafetyViolation);
                EventBus.Instance.onCriticalSafetyFailure.AddListener(OnCriticalSafetyFailure);
                EventBus.Instance.onSafetyError.AddListener(OnSafetyError);
                EventBus.Instance.onSessionCompleted.AddListener(OnSessionCompleted);
            }
        }

        private void OnDisable()
        {
            if (EventBus.Instance != null)
            {
                EventBus.Instance.onSessionStarted.RemoveListener(OnSessionStarted);
                EventBus.Instance.onSessionPaused.RemoveListener(OnSessionPaused);
                EventBus.Instance.onSessionResumed.RemoveListener(OnSessionResumed);
                EventBus.Instance.onSessionEnded.RemoveListener(OnSessionEnded);

                EventBus.Instance.onActionAttempt.RemoveListener(OnActionAttempt);
                EventBus.Instance.onPpeStateChanged.RemoveListener(OnPpeStateChanged);

                EventBus.Instance.onTaskStarted.RemoveListener(OnTaskStarted);
                EventBus.Instance.onTaskCompleted.RemoveListener(OnTaskCompleted);

                EventBus.Instance.onScoreChanged.RemoveListener(OnScoreChanged);

                EventBus.Instance.onGroupStarted.RemoveListener(OnGroupStarted);
                EventBus.Instance.onGroupCompleted.RemoveListener(OnGroupCompleted);

                EventBus.Instance.onSafetyViolation.RemoveListener(OnSafetyViolation);
                EventBus.Instance.onCriticalSafetyFailure.RemoveListener(OnCriticalSafetyFailure);
                EventBus.Instance.onSafetyError.RemoveListener(OnSafetyError);
                EventBus.Instance.onSessionCompleted.RemoveListener(OnSessionCompleted);
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
            string groupName = args.Group != null ? args.Group.groupName : "<Grupo sem nome>";
            AppendLog($"[Grupo] Iniciado '{groupName}'");
        }

        private void OnGroupCompleted(TaskGroupEventArgs args)
        {
            string groupName = args.Group != null ? args.Group.groupName : "<Grupo sem nome>";
            AppendLog($"[Grupo] Concluído '{groupName}'");
        }

        private void OnTaskStarted(TaskEventArgs args)
        {
            string taskName = args.Task != null ? args.Task.taskName : "<Tarefa sem nome>";
            AppendLog($"[Tarefa] Iniciada '{taskName}'");
        }

        private void OnTaskCompleted(TaskEventArgs args)
        {
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
                "ACTION_ID_MISSING" => "Ação não identificada",
                "NO_ACTIVE_GROUP" => "Nenhum grupo de tarefas ativo",
                "WRONG_ACTION" => "Ação incorreta",
                "PPE_MISSING" => "EPI obrigatório ausente",
                "TASK_NOT_PERFORMED" => "Tarefa não realizada",
                "GATE_FAILED" => "Inspeção não aprovada",
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

            // Coalesce: many events can arrive in a single frame (the EventBus drains
            // its queue on completion), so flag dirty and repaint once in LateUpdate
            // instead of forcing a full TMP mesh rebuild per line.
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
            // SetText already flags the mesh dirty; TMP regenerates it during the
            // end-of-frame canvas update. ForceMeshUpdate() would regenerate the mesh
            // synchronously on every call — the source of the per-line TMP/layout spikes.
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
