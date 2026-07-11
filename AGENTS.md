# AGENTS.md

This file provides guidance to coding agents working in this repository, including Claude Code and OpenCode.

## Project Overview

VR safety training simulation built in Unity targeting **Meta Quest** (OpenXR). Players perform workplace safety procedures; the game tracks PPE compliance, validates actions, and scores performance. The project uses a **7-layer architecture** with ~110 C# scripts under `Assets/_SafetyProto/Scripts/`.

**Target SDK**: Meta XR SDK 85.0.0, OpenXR, Unity XR Hands 1.7.3, Input System 1.18.0

## Task Tracking (Obsidian Kanban)

Bugs and features for this app are tracked outside the repo, in the author's Obsidian vault (master's dissertation project):

- **Vault root**: `D:\Nextcloud\1-ABNER\Obsidian`
- **Master board**: `Mestrado/Kanban - Mestrado.md` — Obsidian Kanban plugin board with `Backlog / Em Andamento / Bloqueado / Concluído` columns. App work items are tagged `#vr-app` (other tags: `#experimento`, `#dissertacao`, `#doutorado`).
- **QA bug boards**: `Mestrado/Kanban - QA Sessão N.md` — per-session QA boards listing bugs found during playtests.

These are `.md` files; cards are checklist items grouped under `## <column>` headings, with a trailing `kanban-plugin: board` fence. Notes are written in **Portuguese**. The vault is not part of this git repo — read/update it via normal file tools at the path above when asked to consult or sync the backlog.

## Unity MCP Integration

A **Unity MCP server** is running and connected to the open Unity Editor. Use it after every code creation or edit to catch compilation errors without waiting for the user to report them.

### Unity MCP project context

- Unity MCP is configured for this repository via `.mcp.json` and `opencode.json` at the repo root.
- Open the project from the repository root so the local MCP configuration is picked up automatically.
- At the start of a Unity-focused task, call `Unity_GetUserGuidelines` once to load the editor-side project conventions before creating, editing, or analyzing scripts and assets.
- Use Unity MCP tools for Unity-side state and `Assets/` resources; use normal workspace file tools for non-Unity project files such as solution files, docs, and config.

### Workflow after creating or editing a C# file

1. **Trigger recompilation** via `Unity_RunCommand`:
```csharp
using UnityEditor;
using UnityEditor.Compilation;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.None);
        AssetDatabase.Refresh();
        result.Log("Recompilation requested.");
    }
}
```

2. **Check for errors** via `Unity_ReadConsole` (Types: `["Error"]`, Count: 50, IncludeStacktrace: true). Filter for the changed file name or assembly to focus on new errors.

3. **Validate a specific script** quickly via `Unity_ValidateScript` (Uri: Assets-relative path, Level: `"standard"`, IncludeDiagnostics: true) - returns diagnostics without a full recompile round-trip.

> Note: `Unity_RunCommand` triggers an implicit `AssetDatabase.Refresh` internally, so a standalone compile trigger is only needed if you wrote a file externally (via Edit/Write tools) and Unity hasn't auto-detected it yet.

### Available Unity MCP tools summary

| Tool | Use |
|---|---|
| `Unity_ReadConsole` | Read/filter editor console (errors, warnings, logs) |
| `Unity_GetConsoleLogs` | Simpler console read with logType filter |
| `Unity_RunCommand` | Execute arbitrary C# in the editor (triggers recompile) |
| `Unity_ValidateScript` | Syntax + diagnostic check on a single `.cs` file |
| `Unity_ManageEditor` | Query editor state (Play/Pause/Stop, tags, layers) |
| `Unity_ManageScene` | Inspect/modify scene hierarchy |
| `Unity_ManageGameObject` | Inspect/modify GameObjects and components |
| `Unity_FindProjectAssets` | Search project assets by name/type |

### Entering Play mode & runtime scene inspection

Driving the running game (to reproduce gameplay, read live state, or test runtime systems) is part of the normal workflow, not just compiling:

1. **Enter Play mode** via `Unity_ManageEditor` (Action=Play). The MCP server **briefly loses connection** during the domain reload, so the next tool call typically fails with *"Unity not detected (no fresh discovery files found)"*. Retry `Unity_ManageEditor` (Action=GetState) ~3-4 times until it returns, then proceed. Stop with Action=Stop when done.
2. **Read live state** with `Unity_RunCommand` (arbitrary C# against the live scene) and `Unity_ReadConsole` to watch `[SafetyProto]` logs as the session runs.
3. **Read-only diagnostics**: use `Renderer.sharedMaterial`/`sharedMaterials`, never `.material`/`.materialForRendering` - the latter *instantiate* the material and mutate scene state. When hunting error-shader (magenta) objects, scan **both** `Renderer` and `UnityEngine.UI.Graphic` (UI uses `CanvasRenderer`, not `Renderer`).
4. **Externally-written assets** (files created via Write/Edit rather than the editor, e.g. vendored `.txt`) need an `AssetDatabase.Refresh()` via `Unity_RunCommand` before Unity generates their `.meta` and `Resources.Load` can find them.

### Live dashboard / browser testing

The EvaluatorDashboard is served only in Play mode: `EvaluatorDashboardBootstrap` logs `[SafetyProto] Evaluator Dashboard servers started. HTTP=http://<ip>:8080` (default httpPort 8080, wsPort 7071, path `/eval`). To drive its UI end-to-end, a **BrowserOS MCP** runs at `http://127.0.0.1:9003/mcp` (older docs/instances used `:9000` - try that if 9003 refuses) but is **not** registered as a coding-agent MCP server - drive it with `curl` JSON-RPC (stateless: `initialize` -> `tools/list` -> `tools/call`). Live tool names (server 0.0.117): `tabs` ({action:"new"|"list", url}) opens/lists pages, `navigate` ({url}) loads/reloads, `evaluate` ({expression}) runs page JS and returns the result, and `screenshot` ({...}) writes a Windows path like `C:\tmp\x.png` (read back via `/c/tmp/x.png`). There is no dedicated console-logs tool in this version - capture console output by installing a `console.error`/`window.onerror` collector via `evaluate` before your probes. Always confirm the current surface with `tools/list` - the older `new_page`/`evaluate_script`/`get_console_logs`/`save_screenshot` names are gone.

Dashboard source lives in `Tools/DashboardSrc/` as real `index.html`/`style.css`/`app.js` files.
The `Resources/Dashboard/*.txt` copies loaded by `EvaluatorDashboardBootstrap` are generated by
`DashboardSourceSync` (an editor script that syncs on entering Play mode, before every build, and
via `SafetyProto → Sync Dashboard Source`) - never edit the `.txt` files directly.

## Build & Editor Commands

This is a Unity project - there is no CLI build script. All development happens inside the Unity Editor (Unity 6 / 6000.x). To build:
- **Android (Quest)**: File -> Build Settings -> Android -> Build
- **Editor play**: Press Play in the Unity Editor with the `SafetyTraining` scene open

**CLI harness** (pure .NET 10, no Unity required):
```bash
dotnet build SafetyProto.sln
dotnet run --project Tools/CliHarness -- Tools/CliHarness/scenarios/ppe_equip.json
```
Available scenarios live in `Tools/CliHarness/scenarios/` (`ppe_equip.json`, `ppe_inspection.json`).

**Editor Tools** (available via Unity menu after opening project):
- `Assets/_SafetyProto/Scripts/Editor/ComponentFinder.cs` - scene debugger window
- `Assets/_SafetyProto/Scripts/Editor/SceneDumper.cs` - exports scene hierarchy
- `Assets/_SafetyProto/Scripts/Editor/CapabilityCatalogExporter.cs` - exports action/PPE/scene options for the authoring GUI
- `Assets/_SafetyProto/Scripts/Editor/CountTris.cs` - counts triangles in the selection
- `Assets/_SafetyProto/Scripts/Editor/PPESnapSlotEditor.cs` / `CollectionInstanceArrayEditor.cs` - custom Inspectors

**Runtime logging**: All logs go through `SafetyLog.cs` with `[SafetyProto]` prefix. Debug logs only appear in Editor + Development builds; errors always appear.

## Architecture

### 7-Layer Structure

```
Scripts/
├── Core/           # EventBus(+Runner), EventPayloads, RuntimeSafetyTask, TaskState, PPEType,
│   │               #   GameConstants, SceneLoader, PoseChannelSO, PoseReporter
│   ├── Events/     # Event facades: SessionEvents, ActionEvents, PPEEvents,
│   │               #   ScoreEvents, SafetyEvents, ConsequenceEvents; EventContext
│   ├── Interfaces/ # IEventBus, IScoreService, ISafetyTask, ISessionResettable, ...
│   └── Logging/    # IHarnessLogger, SafetyLog
├── Domain/         # Pure C# business logic (zero UnityEngine deps), shared with CLI harness
│   ├── Actions/    # ActionDef, ActionCatalogDef, ActionCatalogLoader
│   ├── Capabilities/# CapabilityCatalog / ScenarioValidator support for authoring
│   ├── Scenarios/  # ScenarioDef, TaskGroupDef, SafetyTaskDef, ScriptStepDef, ScenarioLoader
│   ├── Safety/     # SafetyRuleEngineCore
│   ├── Scoring/    # ScoreService, ScoreRuleEngineCore
│   ├── Sessions/   # SessionLoggerCore
│   └── Tasks/      # TaskManagerCore
├── Runtime/        # Unity MonoBehaviour adapters and scene-side systems
│   ├── Actions/    # ActionResolver, ActionCatalogSource, EventGameObjectListener
│   ├── Analytics/  # SafetyAnalyzer, SafetyPatternDetector
│   ├── Feedback/   # AudioFeedbackManager, HapticManager, ReturnObjectHome
│   ├── PPE/        # PPEManager, PPEItem, PPESnapItem/Slot, AnchorPoint,
│   │               #   PPESlotBodyCalibrator, RetractableLanyardController, VerletLanyard, ...
│   ├── Safety/     # SafetyRuleEngine (Mono wrapper), PPEComplianceAdapter, InspectionGateValidator
│   ├── Scaffolding/# ScaffoldPieceInstaller
│   ├── Session/    # TrainingSessionManager
│   ├── Task/       # TaskManager (Mono wrapper), TimerSystem(+Adapter), ScoreManagerAdapter, AwaitableAsyncSchedulerAdapter
│   └── Tools/Drill/# DrillUse, FastenerSocket
├── Networking/     # EvaluatorDashboard HTTP/WebSocket server
│   └── EvaluatorDashboard/  # MiniHttpServer, EvaluatorWebSocketServer, PoseSender, Bootstrap
├── UI/             # ScoreHUD, LogHUD, TaskUIController, Popup system
│   └── Popup/
├── Utils/          # SessionLogger, MonoBehaviourExtensions, SafetyLogAdapter, ...
└── Editor/         # Editor-only tools
```

**Assembly definitions** (one per layer): `EventBus.Core`, `Domain.Core`, `Runtime.Unity`, `UI.Unity`, `Networking.Unity`, `Utils.Unity`, `SafetyProto.Editor`.

### Shared / CLI Harness Boundary

The `Domain/` layer and `Core/` (event payloads, interfaces) compile into `SafetyProto.Shared.dll` via `Tools/SafetyProto.Shared/SafetyProto.Shared.csproj`. The same `.cs` files participate in both the Unity build and the standalone .NET 10 CLI harness - zero duplication, one source of truth.

### Event-Driven Core

Everything communicates through a **deferred event queue** in `EventBus` (a ScriptableObject singleton loaded from `Resources/EventBus.asset`). Events are processed max 2ms per frame via `EventBusRunner` (a MonoBehaviour that calls `EventBus.ProcessEvents()` each Update).

All systems subscribe/unsubscribe to EventBus, never calling each other directly. Before subscribing, MonoBehaviours call `this.IsEventBusReady()` (extension method) - if EventBus is missing, the component auto-disables.

Unity-side subscribers use `EventBus.Instance.onXxx.AddListener(...)` (with a matching `RemoveListener` in `OnDisable`/`OnDestroy`); the pure-C# Domain layer uses `IEventBus.Subscribe<T>`. The former static `OnXxxCSharp` events were removed — do not reintroduce them.

Event facades in `Core/Events/` (`SessionEvents`, `ActionEvents`, `PPEEvents`, `ScoreEvents`, `SafetyEvents`) provide static `Raise*` methods that stamp every event with `SessionId`, `PlayerId`, `ScenarioId`, and a Unix timestamp from `EventContext`.

`ConsequenceEvents` is the exception: it is **synchronous and not routed through the EventBus queue** - `InspectionGateValidator` raises `ConsequenceStarted`/`ConsequenceEnded` directly so animation timing isn't deferred a frame. Its subscribers are audio/visual only, with no ordering dependency on queued events.

### Session & Task Lifecycle

```
TrainingSessionManager.Start()         -> SessionStarted event
  └─ TaskManager (Mono)                -> delegates to TaskManagerCore
       └─ [Player acts]                -> scene emitter (e.g. PPESnapSlot) -> ActionEvents.PublishActionAttempt()
            └─ SafetyRuleEngine (Mono) -> delegates to SafetyRuleEngineCore
                 └─ TaskManagerCore    -> CheckGroupCompletion() -> GroupCompleted / SessionCompleted
```

`TaskManagerCore` is the pure-C# orchestrator. `TaskManager` (MonoBehaviour) loads `ScenarioDef` JSON through `ScenarioSource` and adapts it to the core's interfaces. `RuntimeSafetyTask` (in `Core/`) wraps `ISafetyTask` data to track live state.

### Task Execution Modes

`TaskGroupDef.executionMode`:
- **Sequential** - tasks must be completed in order; wrong action = safety violation
- **FreeOrder** - any task in the group can be done in any order; `SafetyRuleEngineCore` maintains `_activeFreeOrderTasks` set

### PPE Enforcement

`PPESnapSlot` components report equip state to `PPEManager` via `ReportPPEStateChange` when PPE snaps into its body slot. `PPEComplianceAdapter` wraps it to `IPPEComplianceChecker`. `SafetyRuleEngineCore` checks compliance before completing tasks. If PPE is missing, task state becomes `CompletedSuccessButUnsafe` and a `ppePenalty` is applied.

**PPEType enum** (in `Core/PPEType.cs`): `None=0 | Helmet=1 | Goggles=3 | Harness=4 | Vest=5 | Boots=6 | GloveLeft=7 | GloveRight=8`. Ordinal `2` is intentionally skipped - it was the legacy `Gloves` value (now split into `GloveLeft`/`GloveRight`); the gap preserves serialized-asset compatibility for `Goggles`..`Boots`.

### Scoring

`ScoreService` is a **pure C# class** in `Domain/Scoring/` (no Unity dependencies). `ScoreManagerAdapter` (in `Runtime/Task/`) bridges gameplay events (TaskCompleted, TaskTimeout) to `ScoreService` calls.

### Action Catalog

Actions are defined in `Assets/_SafetyProto/Resources/Actions/actions.json` as `ActionDef` records. `ActionResolver` provides case-insensitive lookup from that JSON catalog. Scene emitters store plain action-id strings (`ScaffoldPieceInstaller.actionId`, `RetractableLanyardController.connectActionId`).

### Service Reset Pattern

Systems that hold session state implement `ISessionResettable`. `SceneLoader.ResetSession()` — wired to the Restart buttons in `FinishScreenPanel.prefab` and `MenuCanvas.prefab` — scans scene MonoBehaviours for `ISessionResettable`, calls `ResetSession()` on each, then reloads the scene. Non-MonoBehaviour state is reset by the `TrainingSessionManager` lifecycle instead: `BeginSession()` resets `ScoreService` and restamps `EventContext`; `OnDestroy` clears both. Implementing classes: `TaskManager`, `TimerSystem`, `PPEManager`, `PPESnapSlot`, `AudioFeedbackManager`, `HapticManager`, `FallFromHeightController`, `ScoreService`, `SessionLogger`, `SceneLoader`.

## Key File Locations

| Concern | File |
|---|---|
| Event hub | `Scripts/Core/EventBus.cs` |
| Event payloads (all structs) | `Scripts/Core/EventPayloads.cs` |
| Event facades | `Scripts/Core/Events/` |
| Core interfaces | `Scripts/Core/Interfaces/` |
| Runtime task state | `Scripts/Core/RuntimeSafetyTask.cs` |
| Session lifecycle | `Scripts/Runtime/Session/TrainingSessionManager.cs` |
| Task orchestration (pure C#) | `Scripts/Domain/Tasks/TaskManagerCore.cs` |
| Task orchestration (Mono) | `Scripts/Runtime/Task/TaskManager.cs` |
| Action validation (pure C#) | `Scripts/Domain/Safety/SafetyRuleEngineCore.cs` |
| Action validation (Mono) | `Scripts/Runtime/Safety/SafetyRuleEngine.cs` |
| PPE state tracking | `Scripts/Runtime/PPE/PPEManager.cs` |
| Score logic (pure C#) | `Scripts/Domain/Scoring/ScoreService.cs` |
| Session logging (pure C#) | `Scripts/Domain/Sessions/SessionLoggerCore.cs` |
| Action lookup | `Scripts/Runtime/Actions/ActionResolver.cs` |
| Logging | `Scripts/Core/Logging/SafetyLog.cs` |
| Session context (IDs) | `Scripts/Core/Events/EventContext.cs` |

## Runtime Data Assets

Gameplay data lives in JSON:
- `Assets/_SafetyProto/Resources/Scenarios/default.json` - default scenario groups/tasks.
- `Assets/_SafetyProto/Resources/Actions/actions.json` - action ids and metadata.
- `Tools/AuthoringApp/capability_catalog.json` - exported valid options for the desktop GUI.

Runtime singletons loaded via `Resources.Load` (under `Assets/_SafetyProto/Resources/`):
- `EventBus.asset` - must exist or all systems auto-disable

`PoseChannel.asset` (shared pose data channel, a `PoseChannelSO`) lives in `Assets/_SafetyProto/ScriptableObjects/` and is wired via Inspector references rather than `Resources.Load`.

## Adding New Content

**New action type**: Add an `ActionDef` to `Resources/Actions/actions.json`, wire a scene emitter/component with the same action-id string, then export/refresh `capability_catalog.json` for the authoring GUI.

**New task**: Use `Tools/AuthoringApp.Gui` or edit scenario JSON. Two kinds - an **action task** (set `actionId`; completes on a matching `ActionAttemptedEvent`) or an **equip-set task** (leave `actionId` empty, populate `requiredPPE`; completes when all those PPE are worn, any order, via `PPEStateChanged` - see `SafetyRuleEngineCore.IsEquipTask`). Used for PPE donning; the PPE slot's `ppeActionMappings.actionId` must be empty so it doesn't raise `WRONG_ACTION`. Full guide: `docs/authoring-tasks.md`.

**New task group**: Add a group in the authoring GUI/scenario JSON, set `executionMode`, optional `timeLimit`, tasks, and `requiredGroups` by group name.

**New event**: Add an args struct to `EventPayloads.cs`, add a static `Raise*` method to the relevant facade in `Core/Events/`, and register the subscription method in `EventBus`.

**New Domain logic**: Place pure-C# classes in `Scripts/Domain/` and add their paths to `Tools/SafetyProto.Shared/SafetyProto.Shared.csproj` so they compile into the CLI harness too.
