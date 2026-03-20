using System.Collections;
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Data.Enums;
using SafetyProto.Gameplay.Task;
using SafetyProto.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.UI
{
    /// <summary>
    /// Full session report panel. Replaces SessionCompleteUI with a detailed breakdown of
    /// per-task performance, medal award, and contextual improvement tips.
    /// </summary>
    public class SessionReportUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TaskManager taskManager;

        [Header("Score Section")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private Image medalIcon;
        [SerializeField] private Sprite medalGold;
        [SerializeField] private Sprite medalSilver;
        [SerializeField] private Sprite medalBronze;

        [Header("Task Breakdown")]
        [SerializeField] private Transform taskListParent;
        [SerializeField] private GameObject taskRowPrefab;

        [Header("Improvements")]
        [SerializeField] private Transform improvementListParent;
        [SerializeField] private GameObject improvementRowPrefab;
        [SerializeField] private GameObject improvementSection;

        [Header("Footer")]
        [SerializeField] private GameObject certificateIcon;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip completionSound;
        [SerializeField] private AudioClip confettiSound;

        private static SessionCompletedEventArgs? _cachedArgs;

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

            SetupHeader(args, maxPossibleScore);
            BuildTaskBreakdown(tasks);
            BuildImprovements(tasks);
            PlayAudio(args.totalScore, maxPossibleScore);
        }

        // ── Header ────────────────────────────────────────────────

        private void SetupHeader(SessionCompletedEventArgs args, int maxPossibleScore)
        {
            if (titleText != null)
                titleText.text = "TREINAMENTO CONCLUÍDO";

            if (scoreText != null)
                scoreText.text = $"{args.totalScore} / {maxPossibleScore} pts";

            if (timeText != null)
            {
                int minutes = Mathf.FloorToInt(args.totalElapsedTime / 60f);
                int seconds = Mathf.FloorToInt(args.totalElapsedTime % 60f);
                timeText.text = $"Tempo: {minutes:00}:{seconds:00}";
            }

            SetMedal(args.totalScore, maxPossibleScore);
        }

        private void SetMedal(int score, int max)
        {
            if (medalIcon == null) return;

            float pct = max > 0 ? (float)score / max : 0f;

            if (pct >= 0.90f && medalGold != null)
            {
                medalIcon.sprite = medalGold;
                medalIcon.enabled = true;
            }
            else if (pct >= 0.70f && medalSilver != null)
            {
                medalIcon.sprite = medalSilver;
                medalIcon.enabled = true;
            }
            else if (pct >= 0.50f && medalBronze != null)
            {
                medalIcon.sprite = medalBronze;
                medalIcon.enabled = true;
            }
            else
            {
                medalIcon.enabled = false;
            }
        }

        // ── Task Breakdown ────────────────────────────────────────

        private void BuildTaskBreakdown(IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            if (taskListParent == null || taskRowPrefab == null) return;

            // Clear previous rows
            foreach (Transform child in taskListParent)
                Destroy(child.gameObject);

            for (int i = 0; i < tasks.Count; i++)
            {
                var row = Instantiate(taskRowPrefab, taskListParent);
                var rowUI = row.GetComponent<TaskReportRowUI>();
                if (rowUI != null)
                    rowUI.Setup(i + 1, tasks[i], tasks[i].TaskData != null ? tasks[i].TaskData.successPoints : 100);
            }
        }

        // ── Improvements ──────────────────────────────────────────

        private void BuildImprovements(IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            if (improvementListParent == null || improvementRowPrefab == null) return;

            foreach (Transform child in improvementListParent)
                Destroy(child.gameObject);

            var messages = GenerateImprovementMessages(tasks);

            if (messages.Count == 0)
            {
                AddImprovementRow("✅ Excelente! Todas as tarefas foram concluídas corretamente com EPIs adequados.");
                return;
            }

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

                if (t.State == TaskState.CompletedFailure)
                {
                    string advice = !string.IsNullOrEmpty(t.TaskData.failureAdvice)
                        ? t.TaskData.failureAdvice
                        : $"Pratique identificar esta irregularidade mais rapidamente.";
                    messages.Add($"⏱ {name}: Tempo esgotado. {advice}");
                }
                else if (t.State == TaskState.CompletedSuccessButUnsafe)
                {
                    string advice = !string.IsNullOrEmpty(t.TaskData.ppeAdvice)
                        ? t.TaskData.ppeAdvice
                        : "Sempre verifique seus equipamentos antes de agir.";
                    messages.Add($"⚠️ {name}: Concluída sem EPIs completos. {advice}");
                }
                else if (t.State == TaskState.NotStarted || t.State == TaskState.InProgress)
                {
                    messages.Add($"⬜ {name}: Tarefa não concluída.");
                }

                if (t.HasFailedOnce && t.State == TaskState.CompletedSuccess)
                {
                    string hint = !string.IsNullOrEmpty(t.TaskData.hintText) ? t.TaskData.hintText : "";
                    string hintSuffix = !string.IsNullOrEmpty(hint) ? $" Revise: {hint}" : "";
                    messages.Add($"🔁 {name}: Necessitou mais de uma tentativa.{hintSuffix}");
                }

                if (t.HasMissedPPEOnce && t.State == TaskState.CompletedSuccess)
                {
                    string ppeMsg = !string.IsNullOrEmpty(t.TaskData.ppeAdvice)
                        ? t.TaskData.ppeAdvice
                        : "EPI ausente detectado durante a execução.";
                    messages.Add($"🧤 {name}: {ppeMsg}");
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

        private static int ComputeMaxScore(IReadOnlyList<RuntimeSafetyTask> tasks)
        {
            int total = 0;
            foreach (var t in tasks)
                if (t.TaskData != null) total += t.TaskData.successPoints;
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
