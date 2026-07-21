using System;
using System.Collections;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Dashboard;
using SafetyProto.Domain.Scoring;
using SafetyProto.Utils;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace SafetyProto.Networking.Dashboard
{
    /// <summary>
    /// Bootstraps the on-device evaluator dashboard servers (HTTP + WebSocket) and event streaming.
    /// Drop this into a boot scene to expose training telemetry over LAN.
    /// </summary>
    public class EvaluatorDashboardBootstrap : MonoBehaviour, IDashboardHost
    {
        [Header("Networking")]
        public int httpPort = 8080;
        public int wsPort = 7071;

        [Header("Pose Broadcasting")]
        [SerializeField] private PoseChannelSO poseChannel;
        [SerializeField] private float poseSendRateHz = 10f;
        [SerializeField] private int poseDecimalPrecision = 3;

        [Header("Event Filtering")]
        [Tooltip("If false, reduces chatter by skipping high-volume events (ActionAttempts, PPE changes).")]
        public bool verboseEvents = true;

        [Header("Session Log Broadcast")]
        [Tooltip("Delay (seconds) before broadcasting the session log to ensure it has been written to disk.")]
        [SerializeField] private float sessionLogBroadcastDelay = 0.25f;

        private static EvaluatorDashboardBootstrap _instance;

        private MiniHttpServer _httpServer;
        private EvaluatorWebSocketServer _wsServer;
        private DashboardEventRelay _relay;
        private Coroutine _pendingLogBroadcast;
        private Coroutine _poseSendCoroutine;
        private readonly List<ITaskGroup> _knownGroups = new List<ITaskGroup>();
        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable() { } // intentionally empty — subscription moved to Start

        private void Start()
        {
            StartServers();
            if (poseChannel != null)
            {
                var poseSender = new PoseSender(poseChannel, _wsServer, poseSendRateHz, poseDecimalPrecision);
                _poseSendCoroutine = StartCoroutine(poseSender.SendLoop());
            }
            _ = LogStartupInfoAsync();
            var eventBus = EventBus.Instance;
            if (eventBus != null)
            {
                _relay = new DashboardEventRelay(eventBus, this);
                _relay.Subscribe();
            }
        }

        private void Update()
        {
            lock (_mainThreadQueue)
            {
                while (_mainThreadQueue.Count > 0)
                {
                    try
                    {
                        _mainThreadQueue.Dequeue()?.Invoke();
                    }
                    catch (Exception e)
                    {
                        SafetyLog.Error($"[EvaluatorDashboardBootstrap] Error executing main thread action: {e.Message}", this);
                    }
                }
            }
        }

        private void OnDisable() { } // intentionally empty

        private void OnDestroy()
        {
            _relay?.Unsubscribe();
            if (_poseSendCoroutine != null)
            {
                StopCoroutine(_poseSendCoroutine);
                _poseSendCoroutine = null;
            }
            if (_pendingLogBroadcast != null)
            {
                StopCoroutine(_pendingLogBroadcast);
                _pendingLogBroadcast = null;
            }
            _httpServer?.Stop();
            _wsServer?.StopServer();
            if (_instance == this) _instance = null;
        }

        private void StartServers()
        {
            _wsServer = new EvaluatorWebSocketServer();
            _wsServer.MessageReceived += OnClientMessageReceived;
            _wsServer.StartServer(wsPort);

            var indexAsset = Resources.Load<TextAsset>("Dashboard/index");
            var appAsset = Resources.Load<TextAsset>("Dashboard/app");
            var styleAsset = Resources.Load<TextAsset>("Dashboard/style");

            var indexBytes = indexAsset != null ? Encoding.UTF8.GetBytes(indexAsset.text) : null;
            var appBytes = appAsset != null ? Encoding.UTF8.GetBytes(appAsset.text) : null;
            var styleBytes = styleAsset != null ? Encoding.UTF8.GetBytes(styleAsset.text) : null;

            // Three.js is vendored locally as TextAssets (.txt — Unity does not import
            // .js as TextAsset) and served at /vendor/*.js so the dashboard works offline.
            var threeAsset = Resources.Load<TextAsset>("Dashboard/vendor/three.module");
            var orbitAsset = Resources.Load<TextAsset>("Dashboard/vendor/OrbitControls");

            var extraRoutes = new Dictionary<string, (byte[] body, string contentType)>();
            if (threeAsset != null)
                extraRoutes["/vendor/three.module.js"] = (Encoding.UTF8.GetBytes(threeAsset.text), "application/javascript");
            if (orbitAsset != null)
                extraRoutes["/vendor/OrbitControls.js"] = (Encoding.UTF8.GetBytes(orbitAsset.text), "application/javascript");
            if (threeAsset == null || orbitAsset == null)
                SafetyLog.Warning("Assets do viewport 3D não encontrados em Resources/Dashboard/vendor; o painel funcionará sem a visualização 3D.", this);

            // IBM Plex fonts vendored locally as .bytes (raw woff2) and served at
            // /vendor/fonts/*.woff2, so the dashboard needs no font CDN when offline.
            // The HTML still has a system-font fallback if any of these are missing.
            string[] fontFiles =
            {
                "ibm-plex-sans-latin-400-normal", "ibm-plex-sans-latin-500-normal",
                "ibm-plex-sans-latin-600-normal", "ibm-plex-sans-latin-700-normal",
                "ibm-plex-mono-latin-400-normal", "ibm-plex-mono-latin-500-normal",
                "ibm-plex-mono-latin-600-normal",
            };
            foreach (var font in fontFiles)
            {
                var fontAsset = Resources.Load<TextAsset>($"Dashboard/vendor/fonts/{font}");
                if (fontAsset != null)
                    extraRoutes[$"/vendor/fonts/{font}.woff2"] = (fontAsset.bytes, "font/woff2");
            }

            // ES modules generated by DashboardSourceSync into Resources/Dashboard/js
            // (.txt TextAssets; asset name "state" ← file "state.txt" → route "/js/state.js").
            var moduleAssets = Resources.LoadAll<TextAsset>("Dashboard/js");
            foreach (var module in moduleAssets)
            {
                if (module != null)
                    extraRoutes[$"/js/{module.name}.js"] = (Encoding.UTF8.GetBytes(module.text), "application/javascript");
            }

            _httpServer = new MiniHttpServer(indexBytes, appBytes, styleBytes, extraRoutes);
            _httpServer.Start(httpPort);
        }

        private void OnClientMessageReceived(EvaluatorWebSocketServer.ClientConnection client, string json)
        {
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() => ProcessClientMessage(client, json));
            }
        }

        private void ProcessClientMessage(EvaluatorWebSocketServer.ClientConnection client, string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return;
                json = json.Trim('\0', ' ', '\r', '\n', '\t');

                // The dashboard client sends a plain-text "ping" keepalive every 8s; it is not JSON.
                if (json == "ping") return;

                // GenericEventEnvelope requires a JSON object root; ignore anything else
                // so non-JSON frames don't spam the console with parse errors.
                if (json.Length == 0 || json[0] != '{') return;

                var envelope = JsonUtility.FromJson<GenericEventEnvelope>(json);
                if (envelope != null && envelope.eventType == "RequestSync")
                {
                    HandleRequestSync(client);
                }
            }
            catch (Exception e)
            {
                SafetyLog.Error($"[EvaluatorDashboardBootstrap] Error parsing client message: {e.Message}", this);
            }
        }

        private void HandleRequestSync(EvaluatorWebSocketServer.ClientConnection client)
        {
            var sessionId = EventContext.CurrentSessionId;
            if (string.IsNullOrEmpty(sessionId)) return;

            // 0. Enviar SessionStarted para contextualizar o dashboard
            var sessionDto = new SessionDto
            {
                sessionId = sessionId,
                participantId = string.IsNullOrEmpty(EventContext.CurrentPlayerId) ? "—" : EventContext.CurrentPlayerId,
                mode = SessionModeState.CurrentName,
                timestampMs = ResolveTimestamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            };
            _wsServer.SendToClient(client, "SessionStarted", sessionDto);

            // 1. Enviar Manifest
            var manifest = BuildSessionManifest(sessionId);
            _wsServer.SendToClient(client, "SessionManifest", manifest);

            var scoreService = SafetyProto.Domain.Scoring.ScoreService.Instance;
            var score = scoreService != null ? scoreService.CurrentScore : 0;
            var scoreDto = new ScoreDto
            {
                sessionId = sessionId,
                totalScore = score,
                delta = 0,
                timestampMs = ResolveTimestamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            };
            _wsServer.SendToClient(client, "ScoreChanged", scoreDto);


        }

        [Serializable]
        private class GenericEventEnvelope
        {
            public string eventType;
        }

        // --- IDashboardHost ---
        bool IDashboardHost.VerboseEvents => verboseEvents;
        ScoringConfig IDashboardHost.Scoring => ResolveScoring();
        IReadOnlyList<ITaskGroup> IDashboardHost.KnownGroups => _knownGroups;
        void IDashboardHost.RegisterKnownGroup(ITaskGroup group)
        {
            if (group != null && !_knownGroups.Contains(group)) _knownGroups.Add(group);
        }
        long IDashboardHost.ResolveTimestamp(long timestampMs) => ResolveTimestamp(timestampMs);
        SessionManifestDto IDashboardHost.BuildSessionManifest(string sessionId) => BuildSessionManifest(sessionId);
        void IDashboardHost.QueueSessionLogBroadcast(string sessionId, string playerId) => QueueSessionLogBroadcast(sessionId, playerId);
        void IDashboardHost.Broadcast<T>(string eventType, T payload) => Broadcast(eventType, payload);

        private void RegisterKnownGroupsFromTaskManager(SafetyProto.Runtime.Task.TaskManager taskManager)
        {
            if (taskManager == null || taskManager.RuntimeGroups == null)
                return;

            foreach (var group in taskManager.RuntimeGroups)
            {
                if (group != null && !_knownGroups.Contains(group))
                    _knownGroups.Add(group);
            }
        }

        private SessionManifestDto BuildSessionManifest(string sessionId)
        {
            var scoring = ResolveScoring();
            var taskManager = FindFirstObjectByType<SafetyProto.Runtime.Task.TaskManager>();
            if (taskManager != null)
            {
                RegisterKnownGroupsFromTaskManager(taskManager);

                var sessionTasks = taskManager.GetSessionTasks();
                var liveDtos = new List<TaskManifestItemDto>(sessionTasks.Count);
                foreach (var runtimeTask in sessionTasks)
                {
                    var task = runtimeTask.TaskData;
                    if (task == null)
                        continue;

                    liveDtos.Add(DashboardDtoMapper.BuildManifestItem(
                        task, _knownGroups, scoring,
                        DashboardDtoMapper.ResolveTaskStatus(runtimeTask.State)));
                }

                return new SessionManifestDto
                {
                    sessionId = sessionId,
                    tasks = liveDtos.ToArray()
                };
            }

            var dtos = new List<TaskManifestItemDto>();

            foreach (var group in _knownGroups)
            {
                if (group == null) continue;
                foreach (var task in group.tasks)
                {
                    if (task == null) continue;
                    dtos.Add(DashboardDtoMapper.BuildManifestItem(task, _knownGroups, scoring, "pending"));
                }
            }

            return new SessionManifestDto
            {
                sessionId = sessionId,
                tasks = dtos.ToArray()
            };
        }

        private long ResolveTimestamp(long timestampMs)
        {
            return timestampMs != 0 ? timestampMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private static ScoringConfig ResolveScoring()
        {
            return SafetyProto.Runtime.Task.TaskManager.Instance != null
                ? SafetyProto.Runtime.Task.TaskManager.Instance.Scoring
                : ScoringConfig.Default;
        }

        public void ResetSession()
        {
            _knownGroups.Clear();
            var dto = new SessionResetDto
            {
                timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            Broadcast("SessionReset", dto);
        }

        private void Broadcast<T>(string eventType, T payload)
        {
            if (_wsServer == null || !_wsServer.HasConnections)
                return;
            _wsServer.Broadcast(eventType, payload);
        }

        private void QueueSessionLogBroadcast(string sessionId, string playerId)
        {
            if (!isActiveAndEnabled)
                return;

            if (_pendingLogBroadcast != null)
            {
                StopCoroutine(_pendingLogBroadcast);
            }

            _pendingLogBroadcast = StartCoroutine(BroadcastLogDelayed(sessionId, playerId));
        }

        private IEnumerator BroadcastLogDelayed(string sessionId, string playerId)
        {
            if (sessionLogBroadcastDelay > 0f)
            {
                yield return new WaitForSeconds(sessionLogBroadcastDelay);
            }
            else
            {
                yield return null;
            }

            // Capture the Unity-thread-only path here, then run the disk read + JSON
            // encode + frame enqueue on a background thread. The whole session log
            // (which grows with session length) was being read and re-serialized on the
            // main thread, causing a ~300ms freeze at session completion. The WebSocket
            // send itself is already off-thread (per-client SenderThread), and both
            // File IO and JsonUtility.ToJson over a plain struct are thread-safe.
            string persistentDataPath = Application.persistentDataPath;
            _pendingLogBroadcast = null;
            string logDirectory = playerId != null && playerId.StartsWith("SIM-", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(persistentDataPath, "simulations")
                : persistentDataPath;
            _ = System.Threading.Tasks.Task.Run(() =>
                TryBroadcastLatestSessionLog(logDirectory, sessionId, playerId));
        }

        private void TryBroadcastLatestSessionLog(string dir, string sessionId, string playerId)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return;

                var files = Directory.GetFiles(dir, "session_log_*.json");
                if (files == null || files.Length == 0)
                    return;

                string latestFile = null;
                DateTime latestTime = DateTime.MinValue;
                string sessionSuffix = string.IsNullOrEmpty(sessionId)
                    ? null
                    : "_" + sessionId.Substring(0, Math.Min(8, sessionId.Length)) + ".json";
                foreach (var f in files)
                {
                    if (sessionSuffix != null &&
                        !f.EndsWith(sessionSuffix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var t = File.GetLastWriteTimeUtc(f);
                    if (t > latestTime)
                    {
                        latestTime = t;
                        latestFile = f;
                    }
                }

                if (string.IsNullOrEmpty(latestFile))
                    return;

                var content = File.ReadAllText(latestFile);
                var payload = new SessionLogFileDto
                {
                    sessionId = sessionId,
                    participantId = playerId,
                    fileName = Path.GetFileName(latestFile),
                    path = latestFile,
                    content = content
                };
                Broadcast("SessionLogFile", payload);
            }
            catch (Exception ex)
            {
                SafetyLog.Warning($"Falha ao transmitir o arquivo de log da sessão: {ex.Message}", this);
            }
        }

        private async Awaitable LogStartupInfoAsync()
        {
            string ip = await System.Threading.Tasks.Task.Run(TryGetLocalIPv4);
            SafetyLog.Info($"Servidores do Painel do Avaliador iniciados. HTTP=http://{ip}:{httpPort} WS=ws://{ip}:{wsPort}/eval", this);
        }

        private string TryGetLocalIPv4()
        {
            try
            {
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var props = networkInterface.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                        {
                            return addr.Address.ToString();
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
            return "0.0.0.0";
        }

    }
}
