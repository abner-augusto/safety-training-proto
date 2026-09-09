using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
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
    /// Two-panel session finish report: a glanceable main card (score, time, task
    /// count, medal, primary improvement, group summaries) and an optional details
    /// card with the full per-task breakdown.
    /// </summary>
    public class SessionReportUI : MonoBehaviour
    {
        private const string MedalCapExplanation =
            "Medalha limitada: houve violação crítica de segurança durante a sessão.";

        [Header("References")]
        [SerializeField] private TaskManager taskManager;
        [SerializeField] private InspectionGateValidator gateValidator; // optional

        [Header("Header")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private TMP_Text tasksText;
        [SerializeField] private Image scoreProgressFill;
        [SerializeField] private Image medalIcon;
        [SerializeField] private Color goldColor = new Color(1f, 0.84f, 0f);
        [SerializeField] private Color silverColor = new Color(0.75f, 0.75f, 0.75f);
        [SerializeField] private Color bronzeColor = new Color(0.80f, 0.50f, 0.20f);

        [Header("Primary Improvement")]
        [SerializeField] private GameObject primaryImprovementCard;
        [SerializeField] private Image primaryImprovementWarningIcon;
        [SerializeField] private TMP_Text primaryImprovementTitle;
        [SerializeField] private TMP_Text primaryImprovementBody;
        [SerializeField] private TMP_Text primaryImprovementPillLabel;
        [SerializeField] private Color safeColor = new Color(0.161f, 0.702f, 0.396f); // #29B365
        [SerializeField] private Color issueColor = new Color(0.941f, 0.682f, 0.247f); // #F0AE3F

        [Header("Group Summaries")]
        [SerializeField] private Transform groupSummaryParent;
        [SerializeField] private GameObject groupSummaryPrefab;

        [Header("Task Breakdown")]
        [SerializeField] private Transform taskListParent;
        [SerializeField] private GameObject taskRowPrefab;
        [SerializeField] private GameObject groupLabelPrefab;

        [Header("Details Panel")]
        [SerializeField] private GameObject detailsPanel;
        [SerializeField] private ScrollRect detailsScrollRect;
        [SerializeField] private Button detailsToggleButton;
        [SerializeField] private TMP_Text detailsToggleLabel;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip completionSound;
        [SerializeField] private AudioClip confettiSound;

        private static SessionCompletedEventArgs? _cachedArgs;

        /// <summary>True when the last populated report's medal was pulled down from gold by a
        /// critical-tier violation. Read by <see cref="BuildPrimaryImprovement"/> to add the
        /// explanatory message; set once per <see cref="PopulateReport"/> call.</summary>
        private bool _medalCappedByCritical;

        private void OnEnable()
        {
            // Registered before the EventBus check below: the toggle must work even if the
            // report is opened without a fresh SessionCompleted publish this activation.
            if (detailsToggleButton != null)
                detailsToggleButton.onClick.AddListener(ToggleDetails);

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
            if (detailsToggleButton != null)
                detailsToggleButton.onClick.RemoveListener(ToggleDetails);

            _cachedArgs = null;
            if (EventBus.Instance != null)
                EventBus.Instance.onSessionCompleted.RemoveListener(OnSessionCompleted);
        }

        private void OnSessionCompleted(SessionCompletedEventArgs args)
        {
            _cachedArgs = args;
            PopulateReport(args);
        }

        private void PopulateReport(SessionCompletedEventArgs args)
        {
            IReadOnlyList<RuntimeSafetyTask> tasks =
                taskManager != null ? taskManager.GetSessionTasks() : new List<RuntimeSafetyTask>();

            int maxPossibleScore = ComputeMaxScore(tasks);
            bool criticalViolation = HasCriticalViolation(tasks);
            float pct = maxPossibleScore > 0 ? Mathf.Max(0, args.totalScore) / (float)maxPossibleScore : 0f;
            _medalCappedByCritical = criticalViolation && pct >= 0.95f;

            SetupHeader(args, maxPossibleScore, criticalViolation);
            BuildPrimaryImprovement(tasks);
            BuildGroupSummaries(tasks);
            BuildTaskBreakdown(tasks);
            SetDetailsVisible(true);
            PlayAudio(args.totalScore, maxPossibleScore);
        }

        private void SetupHeader(SessionCompletedEventArgs args, int maxPossibleScore, bool criticalViolation)
        {
            if (titleText != null)
                titleText.text = "Treinamento concluído";

            int clampedScore = Mathf.Max(0, args.totalScore);

            if (scoreText != null)
                scoreText.text = $"{clampedScore} / {maxPossibleScore} pts";

            if (timeText != null)
            {
                int minutes = Mathf.FloorToInt(args.totalElapsedTime / 60f);
                int seconds = Mathf.FloorToInt(args.totalElapsedTime % 60f);
                timeText.text = $"{minutes:00}:{seconds:00}";
            }

            if (tasksText != null)
                tasksText.text = $"{args.tasksCompleted} / {args.totalTasks}";

            if (scoreProgressFill != null)
                scoreProgressFill.fillAmount =
                    maxPossibleScore > 0 ? Mathf.Clamp01(clampedScore / (float)maxPossibleScore) : 0f;

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

        private void BuildPrimaryImprovement(IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            if (primaryImprovementCard == null) return;

            var issueTask = SelectPrimaryIssueTask(tasks);
            if (issueTask != null)
            {
                ShowIssueCard(issueTask);
                return;
            }

            if (gateValidator != null && gateValidator.FailedAttemptCount > 0)
            {
                ShowGateAttemptCard(gateValidator.FailedAttemptCount);
                return;
            }

            ShowSafeCard();
        }

        // Deterministic ranking: highest risk level, then highest risk index, then the more
        // severe outcome (omission over unsafe completion over a clean run with a remembered
        // PPE miss), then original task order. Every tie-break narrows the field further, so
        // two runs of the same session always surface the same lesson.
        private RuntimeSafetyTask SelectPrimaryIssueTask(IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            RuntimeSafetyTask best = null;
            int bestRiskLevel = -1;
            int bestRiskIndex = -1;
            int bestSeverityRank = int.MaxValue;

            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                if (t.TaskData == null) continue;
                if (t.State == TaskState.CompletedSuccess && !t.HasMissedPPEOnce) continue;

                int riskLevel = (int)t.TaskData.riskLevel;
                int riskIndex = t.TaskData.risk.Index;
                int severityRank = IssueSeverityRank(t);

                bool better = best == null
                    || riskLevel > bestRiskLevel
                    || (riskLevel == bestRiskLevel && riskIndex > bestRiskIndex)
                    || (riskLevel == bestRiskLevel && riskIndex == bestRiskIndex && severityRank < bestSeverityRank);

                if (!better) continue;

                best = t;
                bestRiskLevel = riskLevel;
                bestRiskIndex = riskIndex;
                bestSeverityRank = severityRank;
            }

            return best;
        }

        private static int IssueSeverityRank(RuntimeSafetyTask t)
        {
            if (t.State == TaskState.CompletedSuccessButUnsafe) return 1;
            if (t.State == TaskState.CompletedSuccess) return 2; // clean completion, missed PPE remembered
            return 0; // not performed, or any other non-terminal state
        }

        private void ShowIssueCard(RuntimeSafetyTask t)
        {
            primaryImprovementCard.SetActive(true);

            if (primaryImprovementTitle != null)
                primaryImprovementTitle.text = t.taskName;

            string body = BuildTaskAdvice(t);
            if (_medalCappedByCritical)
                body = string.IsNullOrEmpty(body) ? MedalCapExplanation : $"{body} {MedalCapExplanation}";

            if (primaryImprovementBody != null)
                primaryImprovementBody.text = body;

            if (primaryImprovementPillLabel != null && t.TaskData != null)
                primaryImprovementPillLabel.text = RiskLevels.DisplayName(t.TaskData.riskLevel).ToUpperInvariant();

            SetPrimaryCardTone(issue: true);
        }

        private void ShowGateAttemptCard(int failedAttempts)
        {
            primaryImprovementCard.SetActive(true);

            if (primaryImprovementTitle != null)
                primaryImprovementTitle.text = "Concluir a inspeção antes de iniciar";

            if (primaryImprovementBody != null)
                primaryImprovementBody.text =
                    $"Você tentou iniciar a atividade {failedAttempts} vez(es) sem completar a inspeção. " +
                    "Na obra real, isso equivale a começar o trabalho com condições inseguras.";

            if (primaryImprovementPillLabel != null)
                primaryImprovementPillLabel.text = "ATENÇÃO";

            SetPrimaryCardTone(issue: true);
        }

        private void ShowSafeCard()
        {
            primaryImprovementCard.SetActive(true);

            if (primaryImprovementTitle != null)
                primaryImprovementTitle.text = "Nenhum ponto crítico de melhoria";

            if (primaryImprovementBody != null)
                primaryImprovementBody.text = "Você concluiu todas as tarefas sem condições inseguras registradas.";

            if (primaryImprovementPillLabel != null)
                primaryImprovementPillLabel.text = "SEGURO";

            SetPrimaryCardTone(issue: false);
        }

        private void SetPrimaryCardTone(bool issue)
        {
            Color color = issue ? issueColor : safeColor;
            if (primaryImprovementWarningIcon != null) primaryImprovementWarningIcon.color = color;
            if (primaryImprovementPillLabel != null) primaryImprovementPillLabel.color = color;
        }

        private void BuildGroupSummaries(IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            if (groupSummaryParent == null || groupSummaryPrefab == null) return;

            ClearChildren(groupSummaryParent);

            foreach (var bucket in BuildGroupBuckets(tasks))
            {
                var card = Instantiate(groupSummaryPrefab, groupSummaryParent);
                var ui = card.GetComponent<SessionReportGroupSummaryUI>();
                if (ui != null) ui.Setup(bucket.groupName, bucket.tasks);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(groupSummaryParent as RectTransform);
        }

        private void BuildTaskBreakdown(IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            if (taskListParent == null || taskRowPrefab == null) return;

            ClearChildren(taskListParent);

            var scoring = taskManager != null ? taskManager.Scoring : ScoringConfig.Default;

            foreach (var bucket in BuildGroupBuckets(tasks))
            {
                if (groupLabelPrefab != null)
                {
                    var label = Instantiate(groupLabelPrefab, taskListParent);
                    var labelText = label.GetComponentInChildren<TMP_Text>();
                    if (labelText != null) labelText.text = bucket.groupName;
                }

                foreach (var t in bucket.tasks)
                {
                    var row = Instantiate(taskRowPrefab, taskListParent);
                    var rowUI = row.GetComponent<TaskReportRowUI>();
                    if (rowUI != null) rowUI.Setup(t, EarnedPointsFor(t, scoring), BuildTaskAdvice(t));
                }
            }

            // Two passes on purpose. The first assigns each row its width; only then can a
            // wrapping task name report a real preferred height, so a single pass leaves every
            // row sized for one-word-per-line text.
            var listRect = taskListParent as RectTransform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);

            if (detailsScrollRect != null)
                detailsScrollRect.verticalNormalizedPosition = 1f;
        }

        private static int EarnedPointsFor(RuntimeSafetyTask t, ScoringConfig scoring)
        {
            var sev = t.TaskData?.riskLevel ?? RiskAssessment.Default.Level;
            int full = scoring.PointsFor(sev);
            // Not doing a task costs nothing but the points it would have earned. The
            // weighting still shows through: a task graded higher on the risk matrix is
            // worth more, so skipping it forfeits more.
            return t.State switch
            {
                TaskState.CompletedSuccess => full,
                TaskState.CompletedSuccessButUnsafe => scoring.UnsafeEarnFor(sev),
                _ => 0
            };
        }

        /// <summary>Groups runtime tasks by their authored <see cref="ITaskGroup.groupName"/>,
        /// in <see cref="TaskManager.RuntimeGroups"/> authoring order, matched by reference —
        /// the same invariant <c>TaskManagerCore.ContainsByReference</c> relies on. Any runtime
        /// task that matches no group lands last under a catch-all bucket so it never silently
        /// disappears from either the group grid or the details list.</summary>
        private List<(string groupName, List<RuntimeSafetyTask> tasks)> BuildGroupBuckets(
            IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            var buckets = new List<(string groupName, List<RuntimeSafetyTask> tasks)>();
            var consumed = new HashSet<RuntimeSafetyTask>();
            var groups = taskManager != null ? taskManager.RuntimeGroups : null;

            if (groups != null)
            {
                foreach (var group in groups)
                {
                    var bucketTasks = new List<RuntimeSafetyTask>();
                    foreach (var t in tasks)
                    {
                        if (t.TaskData != null && ContainsByReference(group.tasks, t.TaskData))
                        {
                            bucketTasks.Add(t);
                            consumed.Add(t);
                        }
                    }
                    if (bucketTasks.Count > 0)
                        buckets.Add((group.groupName, bucketTasks));
                }
            }

            List<RuntimeSafetyTask> leftover = null;
            foreach (var t in tasks)
            {
                if (consumed.Contains(t)) continue;
                leftover ??= new List<RuntimeSafetyTask>();
                leftover.Add(t);
            }
            if (leftover != null)
                buckets.Add(("Outras tarefas", leftover));

            return buckets;
        }

        private static bool ContainsByReference(IReadOnlyList<ISafetyTask> list, ISafetyTask target)
        {
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], target)) return true;
            return false;
        }

        private string BuildTaskAdvice(RuntimeSafetyTask t)
        {
            if (t.TaskData == null) return string.Empty;

            if (t.State == TaskState.CompletedSuccessButUnsafe)
            {
                return FirstNonEmpty(t.TaskData.ppeAdvice, "Sempre verifique seus equipamentos antes de agir.");
            }

            if (t.State != TaskState.CompletedSuccess)
            {
                // NotPerformed, or still open because the report was built before a gate
                // closed the group. Either way the participant did not carry the task
                // out, which is what omissionAdvice — the NR-quoting text — is written
                // for; failureAdvice survives as a fallback for scenarios authored
                // before omissionAdvice existed.
                string advice = FirstNonEmpty(t.TaskData.omissionAdvice, t.TaskData.failureAdvice, t.TaskData.hintText);
                return string.IsNullOrEmpty(advice) ? "Tarefa não realizada." : $"Tarefa não realizada. {advice}";
            }

            if (t.HasMissedPPEOnce)
            {
                return FirstNonEmpty(t.TaskData.ppeAdvice, "EPI ausente detectado durante a execução.");
            }

            return string.Empty;
        }

        private void ToggleDetails()
        {
            bool visible = detailsPanel != null && !detailsPanel.activeSelf;
            SetDetailsVisible(visible);
        }

        private void SetDetailsVisible(bool visible)
        {
            if (detailsPanel != null) detailsPanel.SetActive(visible);
            if (detailsToggleLabel != null)
                detailsToggleLabel.text = visible ? "Ocultar detalhes" : "Ver detalhes";
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }

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
