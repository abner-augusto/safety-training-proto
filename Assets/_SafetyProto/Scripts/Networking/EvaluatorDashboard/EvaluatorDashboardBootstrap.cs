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
using SafetyProto.Runtime.Analytics;
using SafetyProto.Runtime.Session;
using SafetyProto.Utils;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace SafetyProto.Networking.Dashboard
{
    /// <summary>
    /// Bootstraps the on-device evaluator dashboard servers (HTTP + WebSocket) and event streaming.
    /// Drop this into a boot scene to expose training telemetry over LAN.
    /// </summary>
    public class EvaluatorDashboardBootstrap : MonoBehaviour, IDashboardHost, ISessionResettable
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

        private static EvaluatorDashboardBootstrap _instance;

        private MiniHttpServer _httpServer;
        private EvaluatorWebSocketServer _wsServer;
        private DashboardEventRelay _relay;
        private SessionLogger _sessionLogger;
        private Coroutine _poseSendCoroutine;
        private readonly List<ITaskGroup> _knownGroups = new List<ITaskGroup>();
        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private readonly Dictionary<string, IDashboardCommandHandler> _commandHandlers = new Dictionary<string, IDashboardCommandHandler>();

        // Often enough that the start form unlocks soon after a reload and a falling battery is
        // seen; each frame is a few hundred bytes, next to the 10 Hz pose stream.
        private const float StationStatusIntervalSeconds = 5f;
        private float _nextStationStatusAt;
        private TrainingSessionManager _sessionManager;
        private IEvaluatorSessionStarter _sessionStarter;

        // These reload the scene: an EvaluatorCommand queued for them would reach the logger after
        // it flushed the session, so SessionReset and the next SessionStarted record them instead.
        private static readonly HashSet<string> CommandsLoggedBySessionLifecycle = new HashSet<string>
        {
            "reset_session", "start_session"
        };

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RegisterCommandHandlers();
            AttachSessionLogger();
            _sessionManager = null;
            _sessionStarter = null;
            // The waiting screen opens a few frames after load; report it soon after.
            _nextStationStatusAt = Time.unscaledTime + 1f;
        }

        private void AttachSessionLogger()
        {
            if (_sessionLogger != null) return;
            _sessionLogger = FindAnyObjectByType<SessionLogger>();
            if (_sessionLogger != null)
                _sessionLogger.CompletedLogWritten += OnCompletedLogWritten;
        }

        private void OnCompletedLogWritten(string sessionId, string playerId, string path) =>
            BroadcastCompletedSessionLog(sessionId, playerId, path);

        private void OnEnable() { }

        private void Start()
        {
            StartServers();
            AttachSessionLogger();
            if (poseChannel != null)
            {
                var poseSender = new PoseSender(poseChannel, _wsServer, poseSendRateHz, poseDecimalPrecision);
                _poseSendCoroutine = StartCoroutine(poseSender.SendLoop());
            }
            _ = LogStartupInfoAsync();
            var eventBus = EventBus.Instance;
            if (eventBus != null)
            {
                _relay = new DashboardEventRelay(eventBus, this, () => SessionModeState.Current);
                _relay.Subscribe();
                eventBus.Subscribe<SessionStartedEventArgs>(OnSessionLifecycleForStatus);
                eventBus.Subscribe<SessionCompletedEventArgs>(OnSessionLifecycleForStatus);
            }
            RegisterCommandHandlers();
        }

        private void OnSessionLifecycleForStatus(SessionStartedEventArgs _) => BroadcastStationStatus();
        private void OnSessionLifecycleForStatus(SessionCompletedEventArgs _) => BroadcastStationStatus();

        private void RegisterCommandHandlers()
        {
            _commandHandlers.Clear();
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>())
            {
                if (behaviour is not IDashboardCommandHandler handler) continue;
                if (_commandHandlers.ContainsKey(handler.Command))
                {
                    SafetyLog.Warning($"[EvaluatorDashboardBootstrap] Comando duplicado '{handler.Command}' — mantendo o primeiro handler registrado.", this);
                    continue;
                }
                _commandHandlers[handler.Command] = handler;
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

            EvaluatorLink.SetConnected(_wsServer != null && _wsServer.HasConnections);
            if (Time.unscaledTime >= _nextStationStatusAt)
                BroadcastStationStatus();
        }

        private void OnDisable() { }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_sessionLogger != null)
                _sessionLogger.CompletedLogWritten -= OnCompletedLogWritten;
            _relay?.Unsubscribe();
            if (EventBus.Instance != null)
            {
                EventBus.Instance.Unsubscribe<SessionStartedEventArgs>(OnSessionLifecycleForStatus);
                EventBus.Instance.Unsubscribe<SessionCompletedEventArgs>(OnSessionLifecycleForStatus);
            }
            if (_poseSendCoroutine != null)
            {
                StopCoroutine(_poseSendCoroutine);
                _poseSendCoroutine = null;
            }
            _httpServer?.Stop();
            _wsServer?.StopServer();
            if (_instance == this)
            {
                _instance = null;
                EvaluatorLink.SetConnected(false);
            }
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

            // Unity does not import .js as TextAsset, so vendored scripts ship as .txt.
            var threeAsset = Resources.Load<TextAsset>("Dashboard/vendor/three.module");
            var orbitAsset = Resources.Load<TextAsset>("Dashboard/vendor/OrbitControls");

            var extraRoutes = new Dictionary<string, (byte[] body, string contentType)>();

            // Several headsets in one tab: the shell iframes each headset's own dashboard.
            var multiAsset = Resources.Load<TextAsset>("Dashboard/multi");
            if (multiAsset != null)
            {
                var multiBytes = Encoding.UTF8.GetBytes(multiAsset.text);
                extraRoutes["/multi"] = (multiBytes, "text/html; charset=utf-8");
                extraRoutes["/multi.html"] = (multiBytes, "text/html; charset=utf-8");
            }
            if (threeAsset != null)
                extraRoutes["/vendor/three.module.js"] = (Encoding.UTF8.GetBytes(threeAsset.text), "application/javascript");
            if (orbitAsset != null)
                extraRoutes["/vendor/OrbitControls.js"] = (Encoding.UTF8.GetBytes(orbitAsset.text), "application/javascript");
            if (threeAsset == null || orbitAsset == null)
                SafetyLog.Warning("Assets do viewport 3D não encontrados em Resources/Dashboard/vendor; o painel funcionará sem a visualização 3D.", this);

            // Unity has no woff2 importer; fonts ship as .bytes TextAssets.
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

            // Unity cannot import GLB; the model ships as a .bytes TextAsset.
            var ppeModelAsset = Resources.Load<TextAsset>("Dashboard/vendor/models/ppe");
            if (ppeModelAsset != null)
                extraRoutes["/vendor/models/ppe.glb"] = (ppeModelAsset.bytes, "model/gltf-binary");

            // Unity imports the generated modules as .txt TextAssets.
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

                if (json.Length == 0 || json[0] != '{') return;

                var envelope = JsonUtility.FromJson<GenericEventEnvelope>(json);
                if (envelope == null) return;

                if (envelope.eventType == "RequestSync")
                {
                    HandleRequestSync(client);
                }
                else if (envelope.eventType == "Command")
                {
                    HandleCommand(client, json);
                }
            }
            catch (Exception e)
            {
                SafetyLog.Error($"[EvaluatorDashboardBootstrap] Error parsing client message: {e.Message}", this);
            }
        }

        private void HandleCommand(EvaluatorWebSocketServer.ClientConnection client, string json)
        {
            var command = JsonUtility.FromJson<DashboardCommandEnvelope>(json);
            if (command == null || string.IsNullOrEmpty(command.command))
                return;

            // Unity's null override does not apply to interface references; a destroyed handler still tests non-null.
            if (_commandHandlers.TryGetValue(command.command, out var known)
                && known is UnityEngine.Object obj && obj == null)
            {
                RegisterCommandHandlers();
            }

            if (!_commandHandlers.TryGetValue(command.command, out var handler))
            {
                SafetyLog.Info($"[EvaluatorDashboardBootstrap] Comando desconhecido ignorado: '{command.command}'.", this);
                return;
            }

            string reason;
            bool accepted = handler is IDashboardCommandWithArgs withArgs
                ? withArgs.TryExecute(command.args ?? new DashboardCommandArgs(), out reason)
                : handler.TryExecute(out reason);

            if (accepted && !CommandsLoggedBySessionLifecycle.Contains(command.command) && EventBus.Instance != null)
            {
                EventBus.Instance.Publish(new EvaluatorCommandEventArgs
                {
                    Command = command.command,
                    Summary = reason ?? string.Empty,
                    Target = CommandTarget(command)
                });
            }

            var ack = new CommandAckDto
            {
                requestId = command.requestId,
                command = command.command,
                accepted = accepted,
                reason = reason ?? string.Empty,
            };
            _wsServer.SendToClient(client, "CommandAck", ack);
            BroadcastStationStatus();
        }

        /// <summary>What the command acted on, as a stable token for the log: the attention target
        /// of highlight_target, the hand mode of set_hand_collision.</summary>
        private static string CommandTarget(DashboardCommandEnvelope command)
        {
            var args = command.args;
            if (args == null) return string.Empty;
            if (!string.IsNullOrEmpty(args.targetId)) return args.targetId;
            return command.command == "set_hand_collision" ? args.mode ?? string.Empty : string.Empty;
        }

        private void BroadcastStationStatus()
        {
            _nextStationStatusAt = Time.unscaledTime + StationStatusIntervalSeconds;
            Broadcast("StationStatus", BuildStationStatus());
        }

        private StationStatusDto BuildStationStatus()
        {
            if (_sessionManager == null)
                _sessionManager = FindAnyObjectByType<TrainingSessionManager>();
            if (_sessionStarter is UnityEngine.Object starterObject && starterObject == null)
                _sessionStarter = null;
            if (_sessionStarter == null)
            {
                foreach (var behaviour in FindObjectsByType<MonoBehaviour>())
                {
                    if (behaviour is IEvaluatorSessionStarter starter) { _sessionStarter = starter; break; }
                }
            }

            bool started = _sessionManager != null && _sessionManager.IsSessionStarted;
            bool completed = _sessionManager != null && _sessionManager.IsSessionCompleted;
            bool starterEnabled = _sessionStarter is Behaviour b && b.isActiveAndEnabled;
            return new StationStatusDto
            {
                awaitingStart = !started && starterEnabled && _sessionStarter.IsAwaitingEvaluator,
                participantKeypadOpen = !started && starterEnabled && _sessionStarter.IsParticipantKeypadOpen,
                sessionActive = started && !completed,
                sessionCompleted = completed,
                pauseSources = Array.ConvertAll(SessionPause.ActiveSources(), s => s.ToString()),
                participantId = started ? EventContext.CurrentPlayerId ?? string.Empty : string.Empty,
                mode = started ? SessionModeState.CurrentName : string.Empty,
                attempt = _sessionManager != null ? _sessionManager.Attempt : 0,
                deviceModel = SystemInfo.deviceModel,
                appVersion = Application.version,
                batteryLevel = SystemInfo.batteryLevel,
                batteryStatus = SystemInfo.batteryStatus.ToString(),
                handCollision = SafetyProto.Runtime.Interaction.HandObjectCollision.Instance != null
                    ? SafetyProto.Runtime.Interaction.HandObjectCollision.Token(SafetyProto.Runtime.Interaction.HandObjectCollision.Instance.Current)
                    : string.Empty,
                timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private void HandleRequestSync(EvaluatorWebSocketServer.ClientConnection client)
        {
            _wsServer.SendToClient(client, "StationStatus", BuildStationStatus());

            var sceneLayout = FindAnyObjectByType<DashboardSceneLayout>();
            if (sceneLayout != null)
                _wsServer.SendToClient(client, "SceneLayout", sceneLayout.Build());

            if (SafetyProto.Runtime.Feedback.AttentionService.Instance != null)
                _wsServer.SendToClient(client, "HighlightTargets",
                    DashboardDtoMapper.ToHighlightTargets(SafetyProto.Runtime.Feedback.AttentionService.Instance.Targets));

            var sessionId = EventContext.CurrentSessionId;
            if (string.IsNullOrEmpty(sessionId)) return;

            var sessionDto = new SessionDto
            {
                sessionId = sessionId,
                participantId = string.IsNullOrEmpty(EventContext.CurrentPlayerId) ? "—" : EventContext.CurrentPlayerId,
                mode = SessionModeState.CurrentName,
                participantSource = ParticipantIdentity.CurrentSource,
                previousSessionId = _sessionManager != null ? _sessionManager.PreviousSessionId : string.Empty,
                attempt = _sessionManager != null ? _sessionManager.Attempt : 0,
                timestampMs = ResolveTimestamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            };
            _wsServer.SendToClient(client, "SessionStarted", sessionDto);

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

        [Serializable]
        private class DashboardCommandEnvelope
        {
            public string eventType;
            public string command;
            public string requestId;   // echoed back in CommandAck
            public DashboardCommandArgs args;
        }

        bool IDashboardHost.VerboseEvents => verboseEvents;
        ScoringConfig IDashboardHost.Scoring => ResolveScoring();
        IReadOnlyList<ITaskGroup> IDashboardHost.KnownGroups => _knownGroups;
        void IDashboardHost.RegisterKnownGroup(ITaskGroup group)
        {
            if (group != null && !_knownGroups.Contains(group)) _knownGroups.Add(group);
        }
        long IDashboardHost.ResolveTimestamp(long timestampMs) => ResolveTimestamp(timestampMs);
        SessionManifestDto IDashboardHost.BuildSessionManifest(string sessionId) => BuildSessionManifest(sessionId);
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
            var taskManager = SafetyProto.Runtime.Task.TaskManager.Instance;
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

        public static void BroadcastCompletedSessionLog(string sessionId, string playerId, string path)
        {
            var instance = _instance;
            if (instance == null || instance._wsServer == null || string.IsNullOrEmpty(path)) return;
            _ = System.Threading.Tasks.Task.Run(() => instance.TryBroadcastSessionLog(path, sessionId, playerId));
        }

        private void TryBroadcastSessionLog(string path, string sessionId, string playerId)
        {
            try
            {
                if (!File.Exists(path))
                    return;
                var content = File.ReadAllText(path);
                var payload = new SessionLogFileDto
                {
                    sessionId = sessionId,
                    participantId = playerId,
                    fileName = Path.GetFileName(path),
                    path = path,
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
            }
            return "0.0.0.0";
        }

    }
}
