#if UNITY_ANDROID && DEVELOPMENT_BUILD && !UNITY_EDITOR
#nullable enable
using System;
using System.Collections;
using SafetyProto.Core;
using SafetyProto.Core.Logging;
using SafetyProto.Runtime.Simulation;
using SafetyProto.Runtime.Task;
using UnityEngine;

namespace SafetyProto.UI
{
    /// <summary>
    /// Replays the scenario script on the headset, with no participant, when a Development build is
    /// launched with <c>adb shell am start -n &lt;package&gt;/&lt;launcher activity&gt;
    /// -e safety_sim guiado|avaliacao</c> (<c>scripts/quest-sim-run.ps1</c> does this). An optional <c>-e safety_sim_script &lt;device path&gt;</c>
    /// plays an external variant script. One simulation per launch: force-stop the app between runs.
    /// Lives in the UI assembly because it must silence the pre-session popups before the simulated
    /// session starts.
    /// </summary>
    public sealed class DeviceSimulationLauncher : MonoBehaviour
    {
        private const string ModeExtra = "safety_sim";
        private const string ScriptExtra = "safety_sim_script";
        private const float ScenarioWaitSeconds = 15f;

        private SessionMode _mode;
        private string _scriptPath = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LaunchIfRequested()
        {
            string? modeText = ReadIntentExtra(ModeExtra);
            if (string.IsNullOrWhiteSpace(modeText)) return;

            if (!TryParseMode(modeText!, out var mode))
            {
                SafetyLog.Error($"[DeviceSimulationLauncher] Modo de simulação '{modeText}' desconhecido. Use guiado ou avaliacao.");
                return;
            }

            var launcher = new GameObject("Device Simulation Launcher").AddComponent<DeviceSimulationLauncher>();
            launcher._mode = mode;
            launcher._scriptPath = ReadIntentExtra(ScriptExtra) ?? string.Empty;
        }

        private IEnumerator Start()
        {
            float waited = 0f;
            while ((TaskManager.Instance == null || TaskManager.Instance.LoadedScenario == null) &&
                   waited < ScenarioWaitSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            var simulator = SessionSimulator.GetOrCreate();
            simulator.Mode = _mode;
            simulator.ExternalScenarioPath = _scriptPath;
            SafetyLog.Info($"[DeviceSimulationLauncher] Simulação iniciada no headset — modo {_mode}" +
                           (string.IsNullOrEmpty(_scriptPath) ? "." : $", roteiro {_scriptPath}."), this);
            simulator.Run();

            while (simulator.IsBusy) yield return null;

            var result = simulator.Result;
            string summary = $"[DeviceSimulationLauncher] {result.FormatStatus()} | participante {result.participantId} | {result.lastDiagnostic}";
            if (result.status == SimulationStatus.Completed) SafetyLog.Info(summary, this);
            else SafetyLog.Error(summary, this);
        }

        private static bool TryParseMode(string text, out SessionMode mode)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "guiado":
                case "guided":
                    mode = SessionMode.Guided;
                    return true;
                case "avaliacao":
                case "avaliação":
                case "evaluation":
                    mode = SessionMode.Evaluation;
                    return true;
                default:
                    mode = SessionMode.Guided;
                    return false;
            }
        }

        private static string? ReadIntentExtra(string key)
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity?.Call<AndroidJavaObject>("getIntent");
                return intent?.Call<string>("getStringExtra", key);
            }
            catch (Exception ex)
            {
                SafetyLog.Error($"[DeviceSimulationLauncher] Could not read intent extra '{key}': {ex.Message}");
                return null;
            }
        }
    }
}
#endif
