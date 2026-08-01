using System.Collections;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Domain.Scoring;
using SafetyProto.Runtime.Safety;
using SafetyProto.Runtime.Task;
using SafetyProto.Utils;
// RuntimeSafetyTask has been moved to Core for clean dependency layering
using RuntimeSafetyTask = SafetyProto.Core.RuntimeSafetyTask;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.UI
{
    /// <summary>
    /// Full session report panel. Shows a detailed breakdown of
    /// per-task performance, medal award, and contextual improvement tips.
    /// </summary>
    public class SessionReportUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TaskManager taskManager;
        [SerializeField] private InspectionGateValidator gateValidator; // optional

        [Header("Score Section")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private Image medalIcon;
        [SerializeField] private Color goldColor = new Color(1f, 0.84f, 0f);
        [SerializeField] private Color silverColor = new Color(0.75f, 0.75f, 0.75f);
        [SerializeField] private Color bronzeColor = new Color(0.80f, 0.50f, 0.20f);

        [Header("Task Breakdown")]
        [SerializeField] private Transform taskListParent;
        [SerializeField] private GameObject taskRowPrefab;

        [Header("Improvements")]
        [SerializeField] private Transform improvementListParent;
        [SerializeField] private GameObject improvementRowPrefab;
        [SerializeField] private GameObject improvementSection;
        [SerializeField] private GameObject noWarningText;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip completionSound;
        [SerializeField] private AudioClip confettiSound;

        private static SessionCompletedEventArgs? _cachedArgs;

        /// <summary>True when the last populated report's medal was pulled down from gold by a
        /// critical-tier violation. Read by <see cref="BuildImprovements"/> to add the explanatory
        /// message; set once per <see cref="PopulateReport"/> call.</summary>
        private bool _medalCappedByCritical;

        // ──────────────────────────────────────────────────────────

        private void OnEnable()
        {
            if (!this.IsEventBusReady()) return;

            EventBus.Instance.onSessionCompleted.AddListener(OnSessionCompleted);

            // Panel may have been activated after the event already fired
            if (_cachedArgs.HasValue)
            {
                PopulateReport(_cachedArgs.Value);
            }
            else if (taskManager != null && taskManager.LastSessionSummary.HasValue)
            {
                PopulateReport(taskManager.LastSessionSummary.Value);
            }
        }

        private void OnDisable()
        {
            _cachedArgs = null;
            if (EventBus.Instance != null)
                EventBus.Instance.onSessionCompleted.RemoveListener(OnSessionCompleted);
        }

        private void OnSessionCompleted(SessionCompletedEventArgs args)
        {
            _cachedArgs = args;
            PopulateReport(args);
        }

        // ──────────────────────────────────────────────────────────

        private void PopulateReport(SessionCompletedEventArgs args)
        {
            IReadOnlyList<RuntimeSafetyTask> tasks =
                taskManager != null ? taskManager.GetSessionTasks() : new List<RuntimeSafetyTask>();

            int maxPossibleScore = ComputeMaxScore(tasks);
            bool criticalViolation = HasCriticalViolation(tasks);
            float pct = maxPossibleScore > 0 ? Mathf.Max(0, args.totalScore) / (float)maxPossibleScore : 0f;
            _medalCappedByCritical = criticalViolation && pct >= 0.95f;

            SetupHeader(args, maxPossibleScore, criticalViolation);
            BuildTaskBreakdown(tasks);
            BuildImprovements(tasks);
            PlayAudio(args.totalScore, maxPossibleScore);
        }

        // ── Header ────────────────────────────────────────────────

        private void SetupHeader(SessionCompletedEventArgs args, int maxPossibleScore, bool criticalViolation)
        {
            if (titleText != null)
                titleText.text = "TREINAMENTO CONCLUÍDO";

            if (scoreText != null)
                scoreText.text = $"{Mathf.Max(0, args.totalScore)} / {maxPossibleScore} pts";

            if (timeText != null)
            {
                int minutes = Mathf.FloorToInt(args.totalElapsedTime / 60f);
                int seconds = Mathf.FloorToInt(args.totalElapsedTime % 60f);
                timeText.text = $"Tempo: {minutes:00}:{seconds:00}";
            }

            SetMedal(args.totalScore, maxPossibleScore, criticalViolation);
        }

        private void SetMedal(int score, int max, bool criticalViolation)
        {
            if (medalIcon == null) return;

            float pct = max > 0 ? Mathf.Max(0, score) / (float)max : 0f;

            // Bronze anchors to the conventional NR-training minimum (70% de
            // aproveitamento). A critical-tier violation caps the medal at silver:
            // a session where the participant worked unanchored cannot be gold,
            // whatever the points say (eliminatory-fault model).
            Color? medal = pct >= 0.95f ? goldColor
                         : pct >= 0.85f ? silverColor
                         : pct >= 0.70f ? bronzeColor
                         : (Color?)null;

            if (criticalViolation && medal.HasValue && medal.Value == goldColor)
                medal = silverColor;

            medalIcon.enabled = medal.HasValue;
            if (medal.HasValue) medalIcon.color = medal.Value;
        }

        // A task at or above the eliminatory risk threshold that did not end in a clean
        // CompletedSuccess is a critical violation for medal purposes (unsafe completion,
        // timeout, or omission). Gate charges on those tasks are covered by the same test:
        // a charged task was pending, so it is not CompletedSuccess.
        // The comparison is a threshold, not an equality on one tier — that is what keeps a
        // newly added top tier (Intolerable) inside the rule instead of silently outside it.
        private bool HasCriticalViolation(IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            foreach (var t in tasks)
            {
                if (t.TaskData == null || t.TaskData.riskLevel < RiskLevels.EliminatoryThreshold) continue;
                if (t.State != TaskState.CompletedSuccess) return true;
                if (t.HasMissedPPEOnce) return true;
            }
            return false;
        }

        // ── Task Breakdown ────────────────────────────────────────

        private void BuildTaskBreakdown(IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            if (taskListParent == null || taskRowPrefab == null) return;

            // Clear previous rows
            foreach (Transform child in taskListParent)
                Destroy(child.gameObject);

            var scoring = taskManager != null ? taskManager.Scoring : ScoringConfig.Default;
            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                var sev = t.TaskData?.riskLevel ?? RiskAssessment.Default.Level;
                int full = scoring.PointsFor(sev);
                // Not doing a task costs nothing but the points it would have earned. The
                // weighting still shows through: a task graded higher on the risk matrix is
                // worth more, so skipping it forfeits more.
                int earned = t.State switch
                {
                    TaskState.CompletedSuccess => full,
                    TaskState.CompletedSuccessButUnsafe => scoring.UnsafeEarnFor(sev),
                    _ => 0
                };
                var row = Instantiate(taskRowPrefab, taskListParent);
                var rowUI = row.GetComponent<TaskReportRowUI>();
                if (rowUI != null) rowUI.Setup(i + 1, t, full, earned);
            }
        }

        // ── Improvements ──────────────────────────────────────────

        private void BuildImprovements(IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            if (improvementListParent == null || improvementRowPrefab == null) return;

            foreach (Transform child in improvementListParent)
                Destroy(child.gameObject);

            var messages = GenerateImprovementMessages(tasks);

            if (_medalCappedByCritical)
                messages.Add("Medalha limitada: houve violação crítica de segurança durante a sessão.");

            // Gate validator failures
            if (gateValidator != null && gateValidator.FailedAttemptCount > 0)
            {
                int n = gateValidator.FailedAttemptCount;
                messages.Add(
                    $"Você tentou iniciar a atividade {n} vez(es) sem completar a inspeção. " +
                    "Na obra real, isso equivale a começar o trabalho com condições inseguras.");
            }

            bool hasImprovements = messages.Count > 0;

            if (noWarningText != null)
                noWarningText.SetActive(!hasImprovements);

            if (improvementSection != null)
                improvementSection.SetActive(hasImprovements);

            if (!hasImprovements)
                return;

            foreach (var msg in messages)
                AddImprovementRow(msg);
        }

        private List<string> GenerateImprovementMessages(IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            var messages = new List<string>();

            foreach (var t in tasks)
            {
                if (t.TaskData == null) continue;

                string name = t.TaskData.taskName;

                if (t.State == TaskState.CompletedSuccessButUnsafe)
                {
                    string advice = !string.IsNullOrEmpty(t.TaskData.ppeAdvice)
                        ? t.TaskData.ppeAdvice
                        : "Sempre verifique seus equipamentos antes de agir.";
                    messages.Add($"{name}: Concluída sem EPIs completos. {advice}");
                }
                else if (t.State != TaskState.CompletedSuccess)
                {
                    // NotPerformed, or still open because the report was built before a gate
                    // closed the group. Either way the participant did not carry the task
                    // out, which is what omissionAdvice — the NR-quoting text — is written
                    // for; failureAdvice survives as a fallback for scenarios authored
                    // before omissionAdvice existed.
                    string advice = FirstNonEmpty(
                        t.TaskData.omissionAdvice,
                        t.TaskData.failureAdvice,
                        t.TaskData.hintText);
                    string suffix = string.IsNullOrEmpty(advice) ? string.Empty : $" {advice}";
                    messages.Add($"{name}: Tarefa não realizada.{suffix}");
                }

                if (t.HasMissedPPEOnce && t.State == TaskState.CompletedSuccess)
                {
                    string ppeMsg = !string.IsNullOrEmpty(t.TaskData.ppeAdvice)
                        ? t.TaskData.ppeAdvice
                        : "EPI ausente detectado durante a execução.";
                    messages.Add($"{name}: {ppeMsg}");
                }
            }

            return messages;
        }

        private void AddImprovementRow(string message)
        {
            var row = Instantiate(improvementRowPrefab, improvementListParent);
            var rowUI = row.GetComponent<ImprovementRowUI>();
            rowUI?.Setup(message);
        }

        // ── Helpers ───────────────────────────────────────────────

        /// <summary>First non-empty candidate, or empty when every candidate is blank.</summary>
        private static string FirstNonEmpty(params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
                if (!string.IsNullOrEmpty(candidates[i])) return candidates[i];
            return string.Empty;
        }

        private int ComputeMaxScore(IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            var scoring = taskManager != null ? taskManager.Scoring : ScoringConfig.Default;
            int total = 0;
            foreach (var t in tasks)
                if (t.TaskData != null) total += scoring.PointsFor(t.TaskData.riskLevel);
            return total;
        }

        private void PlayAudio(int score, int max)
        {
            if (audioSource == null) return;

            float pct = max > 0 ? (float)score / max : 0f;
            AudioClip clip = pct >= 0.70f ? confettiSound : completionSound;
            if (clip != null)
                audioSource.PlayOneShot(clip);
        }
    }
}
