using System;
using System.Collections.Generic;
using System.Reflection;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using UnityEngine;
using UnityEngine.UI;
using RuntimeSafetyTask = SafetyProto.Core.RuntimeSafetyTask;

namespace SafetyProto.UI
{
    /// <summary>
    /// DEV-ONLY utility. Fills the finish-screen task list AND improvement list with
    /// synthetic rows so both ScrollRects can be exercised without playing a full
    /// training session. Drop it on any GameObject; references auto-resolve from the
    /// scene's <see cref="SessionReportUI"/>. Trigger on Start, or via the component
    /// context-menu ("Trigger Finish Screen"). Logs content vs viewport height per list
    /// so scrollability can be confirmed.
    /// </summary>
    public class FinishScreenScrollTester : MonoBehaviour
    {
        [Header("References (auto-resolved from SessionReportUI if empty)")]
        [Tooltip("Raiz do painel de fim de sessão a ser ativado.")]
        [SerializeField] private GameObject finishPanel;
        [Tooltip("Content da TaskScrollView (TaskListParent).")]
        [SerializeField] private Transform taskListParent;
        [Tooltip("Prefab TaskReport_Row instanciado em cada linha de tarefa.")]
        [SerializeField] private GameObject taskRowPrefab;
        [Tooltip("Content da ImprovementListScrollView (ImprovementListParent).")]
        [SerializeField] private Transform improvementListParent;
        [Tooltip("Prefab Improvement_Row instanciado em cada linha de melhoria.")]
        [SerializeField] private GameObject improvementRowPrefab;

        [Header("Test Data")]
        [Tooltip("Quantas linhas gerar por lista. Use um valor alto para forçar overflow e testar o scroll.")]
        [SerializeField] private int rowCount = 15;

        [Header("Trigger")]
        [Tooltip("Dispara automaticamente no Start.")]
        [SerializeField] private bool triggerOnStart = true;

        /// <summary>Last measurement string produced by <see cref="TriggerFinishScreen"/> (for tooling/tests).</summary>
        public string LastResult { get; private set; } = string.Empty;

        private static readonly TaskState[] TaskStates =
        {
            TaskState.CompletedSuccess,
            TaskState.CompletedSuccessButUnsafe,
            TaskState.CompletedFailure,
            TaskState.NotStarted,
        };

        private void Start()
        {
            if (triggerOnStart)
                TriggerFinishScreen();
        }

        [ContextMenu("Trigger Finish Screen")]
        public void TriggerFinishScreen()
        {
            ResolveReferencesIfNeeded();

            if (finishPanel != null && !finishPanel.activeSelf)
                finishPanel.SetActive(true);

            string result = "";

            if (taskListParent != null && taskRowPrefab != null)
            {
                Populate(taskListParent, taskRowPrefab, (go, i) =>
                {
                    var fake = new FakeTask($"Tarefa de teste {i + 1}");
                    var runtimeTask = new RuntimeSafetyTask(fake) { State = TaskStates[i % TaskStates.Length] };
                    var rowUI = go.GetComponent<TaskReportRowUI>();
                    if (rowUI != null)
                        rowUI.Setup(i + 1, runtimeTask, fake.successPoints);
                });
                result += $"TASK[{Measure(taskListParent)}] ";
            }
            else
            {
                SafetyLog.Error("[FinishScreenScrollTester] taskListParent/taskRowPrefab não resolvidos.", this);
            }

            if (improvementListParent != null && improvementRowPrefab != null)
            {
                EnsureActiveHierarchy(improvementListParent);
                Populate(improvementListParent, improvementRowPrefab, (go, i) =>
                {
                    var rowUI = go.GetComponent<ImprovementRowUI>();
                    if (rowUI != null)
                        rowUI.Setup($"Sugestão {i + 1}: verifique todos os EPIs obrigatórios antes de iniciar o trabalho em altura.");
                });
                result += $"IMPROVE[{Measure(improvementListParent)}]";
            }
            else
            {
                SafetyLog.Error("[FinishScreenScrollTester] improvementListParent/improvementRowPrefab não resolvidos.", this);
            }

            LastResult = result;
            SafetyLog.Info($"[FinishScreenScrollTester] {rowCount} linhas/lista | {LastResult}", this);
        }

        private void Populate(Transform parent, GameObject prefab, Action<GameObject, int> setup)
        {
            // Limpa linhas de um disparo anterior (ou de um populate real do SessionReportUI).
            for (int i = parent.childCount - 1; i >= 0; i--)
                DestroyImmediate(parent.GetChild(i).gameObject);

            for (int i = 0; i < rowCount; i++)
            {
                var row = Instantiate(prefab, parent);
                setup(row, i);
            }
        }

        private static string Measure(Transform parent)
        {
            // Força o ContentSizeFitter + layout para medir imediatamente.
            Canvas.ForceUpdateCanvases();
            if (parent is not RectTransform content)
                return "sem RectTransform";

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            var scroll = parent.GetComponentInParent<ScrollRect>(true);
            float viewportH = scroll != null && scroll.viewport != null ? scroll.viewport.rect.height : -1f;
            bool scrollable = viewportH > 0f && content.rect.height > viewportH;

            return $"content={content.rect.height:F0} viewport={viewportH:F0} rolável={scrollable}";
        }

        /// <summary>Walks up to the panel re-activating any inactive ancestor so layout can be measured.</summary>
        private void EnsureActiveHierarchy(Transform leaf)
        {
            var node = leaf;
            while (node != null)
            {
                if (!node.gameObject.activeSelf)
                    node.gameObject.SetActive(true);

                if (finishPanel != null && node.gameObject == finishPanel)
                    break;

                node = node.parent;
            }
        }

        private void ResolveReferencesIfNeeded()
        {
            bool allSet = finishPanel != null
                          && taskListParent != null && taskRowPrefab != null
                          && improvementListParent != null && improvementRowPrefab != null;
            if (allSet)
                return;

            var report = FindFirstObjectByType<SessionReportUI>(FindObjectsInactive.Include);
            if (report == null)
                return;

            if (finishPanel == null)
                finishPanel = report.gameObject;

            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(SessionReportUI);

            taskListParent ??= type.GetField("taskListParent", flags)?.GetValue(report) as Transform;
            taskRowPrefab ??= type.GetField("taskRowPrefab", flags)?.GetValue(report) as GameObject;
            improvementListParent ??= type.GetField("improvementListParent", flags)?.GetValue(report) as Transform;
            improvementRowPrefab ??= type.GetField("improvementRowPrefab", flags)?.GetValue(report) as GameObject;
        }

        /// <summary>Minimal in-memory <see cref="ISafetyTask"/> for synthetic report rows.</summary>
        private sealed class FakeTask : ISafetyTask
        {
            private static readonly PPEType[] NoPPE = new PPEType[0];

            public FakeTask(string name) => taskName = name;

            public string taskName { get; }
            public string taskDescription => string.Empty;
            public int successPoints => 100;
            public int failurePenalty => 50;
            public int ppePenalty => 30;
            public IReadOnlyList<PPEType> requiredPPE => NoPPE;
            public string hintText => string.Empty;
            public string failureAdvice => string.Empty;
            public string ppeAdvice => string.Empty;
            public string ResolveExpectedActionId() => string.Empty;
        }
    }
}
