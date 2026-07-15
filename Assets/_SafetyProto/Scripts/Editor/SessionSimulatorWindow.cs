#if UNITY_EDITOR
#nullable enable
using SafetyProto.Core;
using SafetyProto.Runtime.Simulation;
using SafetyProto.Runtime.Task;
using SafetyProto.UI;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Editor
{
    /// <summary>Small Play Mode control surface for the semantic session simulator.</summary>
    public sealed class SessionSimulatorWindow : EditorWindow
    {
        private SessionSimulator? _simulator;
        private Vector2 _scroll;
        private string _externalScenarioPath = string.Empty;
        private SessionMode _mode = SessionMode.Guided;

        [MenuItem("SafetyProto/Session Simulator")]
        private static void Open() => GetWindow<SessionSimulatorWindow>("Simulador de Sessão");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Simulador de Sessão", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Apenas no Editor. Identidades SIM- não entram no mapa privado de participantes.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                var loadedScenario = TaskManager.Instance?.LoadedScenario;
                EditorGUILayout.LabelField("Cenário carregado", loadedScenario?.Name ?? "(aguardando TaskManager)");
                using (new EditorGUILayout.HorizontalScope())
                {
                    _externalScenarioPath = EditorGUILayout.TextField("Script externo", _externalScenarioPath);
                    if (GUILayout.Button("Escolher...", GUILayout.Width(80)))
                    {
                        _externalScenarioPath = EditorUtility.OpenFilePanel(
                            "Escolher cenário JSON", "Tools/CliHarness/scenarios", "json");
                    }
                }
                _mode = (SessionMode)EditorGUILayout.EnumPopup("Modo", _mode);

                _simulator ??= SessionSimulator.GetOrCreate();
                _simulator.ExternalScenarioPath = _externalScenarioPath;
                _simulator.Mode = _mode;

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool terminal = _simulator.Result.status == SimulationStatus.Completed ||
                                    _simulator.Result.status == SimulationStatus.Failed ||
                                    _simulator.Result.status == SimulationStatus.Cancelled;
                    using (new EditorGUI.DisabledScope(terminal || _simulator.IsBusy))
                    {
                        if (GUILayout.Button("Executar tudo"))
                        {
                            PrepareForSimulation();
                            _simulator.Run();
                        }
                        if (GUILayout.Button("Próxima etapa"))
                        {
                            PrepareForSimulation();
                            _simulator.Step();
                        }
                    }
                    using (new EditorGUI.DisabledScope(!_simulator.IsBusy))
                    if (GUILayout.Button("Cancelar")) _simulator.Cancel();
                    if (GUILayout.Button("Sair do Play Mode")) EditorApplication.isPlaying = false;
                }
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Entre em Play Mode para iniciar uma simulação.", MessageType.Warning);
                return;
            }

            var result = _simulator?.Result;
            if (result == null) return;

            EditorGUILayout.LabelField(result.FormatStatus(), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Participante", result.participantId);
            EditorGUILayout.LabelField("Diagnóstico", result.lastDiagnostic);
            if (result.status == SimulationStatus.Cancelled || result.status == SimulationStatus.Failed)
                EditorGUILayout.HelpBox("Reinicie o Play Mode antes de iniciar outra sessão simulada.", MessageType.Warning);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Tarefas", EditorStyles.boldLabel);
            foreach (var task in result.tasks)
                EditorGUILayout.LabelField($"{task.groupId} / {task.name}: {task.state}");

            EditorGUILayout.LabelField("Transcript", EditorStyles.boldLabel);
            foreach (var entry in result.transcript)
                EditorGUILayout.LabelField(entry);

            if (result.consequences.Count > 0)
            {
                EditorGUILayout.LabelField("Consequências", EditorStyles.boldLabel);
                foreach (var consequence in result.consequences)
                    EditorGUILayout.LabelField(consequence);
            }
            EditorGUILayout.EndScrollView();
            Repaint();
        }

        private void PrepareForSimulation()
        {
            if (_simulator == null) return;

            var popupService = PopupService.Instance;
            if (popupService != null)
                popupService.Hide();

            foreach (var controller in FindObjectsByType<NameEntryController>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                controller.StopAllCoroutines();
                controller.enabled = false;
            }
            foreach (var controller in FindObjectsByType<SessionModeSelectionController>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                controller.enabled = false;
            foreach (var controller in FindObjectsByType<OnboardingController>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                controller.StopAllCoroutines();
                controller.enabled = false;
            }
        }
    }
}
#endif
