# Architecture

> Verified against commit `999b057` (2026-10-16): the facts in §2, §4, §6 and §7 are pinned by `AssemblyLayeringTests` and `ArchitectureFactsTests`.

This document describes the internal design of `safety-training-proto`: how the
system is layered, how its parts communicate, and why it is structured the way
it is. It complements the [README](README.md), which covers what the project is,
how to build it, and how to run the tests. Read the README first for the
high-level picture; read this for the design rationale and the invariants a
contributor must preserve.

The prototype exists to demonstrate one claim: that the domain logic of a VR
safety-training simulator — task orchestration, rule evaluation, scoring, and
session logging — can be expressed as engine-independent code that communicates
exclusively through a typed event protocol, and that the *same* compiled logic
can run inside the Unity runtime on a headset and inside a headless .NET process,
with external participants able to join the protocol without coupling to the
engine.

---

## 1. Design principles

Four rules govern the codebase. Every structural decision below follows from
them, and a change that violates one is a regression regardless of whether it
compiles.

1. **The domain layer has zero `UnityEngine` dependencies.** All orchestration,
   rule, scoring, and logging logic is pure C# under `Scripts/Domain/`. It is
   compiled into both the Unity build and a standalone `.NET` assembly from the
   same source files. Anything that needs the engine lives in a thin adapter in
   `Scripts/Runtime/`, never in the domain. The domain assembly definition sets
   `noEngineReferences`, so the compiler enforces the rule.

2. **Systems never call each other directly.** They publish and subscribe to a
   shared event bus. A producer does not know its consumers; a consumer does not
   know its producers. This is what lets a scripted actor, a headless stub, or a
   remote observer take the place of a Unity component transparently.

3. **Events are typed.** The protocol vocabulary is a fixed set of payload
   structs in `Core/EventPayloads.cs` and `Core/Events/`. There is no
   stringly-typed message bus and no reflection-based dispatch on the hot path; a
   subscriber binds to a payload type, not to a topic name.

4. **Engine specifics are injected, not imported.** Where domain code needs a
   capability the engine provides — persistent storage path, JSON serialization,
   a wall clock, a scheduler, the current session mode — that capability is
   passed in as an interface or a delegate. The domain declares the contract
   (`Core/Interfaces/`); the runtime supplies the implementation. The one
   standing deviation is `ScoreService.Instance`, a static accessor kept because
   ten runtime components read the score and only one score scope exists;
   revisit it only if a second scope is ever needed (see
   [ADR 0002](docs/adr/0002-score-service-static-instance.md)).

Deliberate exceptions to these rules are recorded in [docs/adr/](docs/adr/README.md).

---

## 2. Layered structure

The code is organized into seven assembly definitions so that the compiler
enforces the dependency direction. The chain, lowest first:

```
SafetyProto.EventBus.Core   Core/ — EventBus, EventBusRunner, EventPayloads, Events/ (facades,
                            EventContext, EventMetadata, IEventPayload), Interfaces/, Logging/,
                            PPEType, RiskLevel, RiskAssessment, SessionMode, TaskState,
                            ViolationCodes, RuntimeSafetyTask. Mixed: engine types allowed.
SafetyProto.Domain.Core     Domain/ — Actions, Capabilities, Dashboard, Feedback, Metrics,
                            Safety, Scenarios, Scoring, Sessions, Tasks. noEngineReferences.
SafetyProto.Utils.Unity     Utils/ — SessionLogger, SafetyLogAdapter, EventBusPerfRecorder,
                            PauseMenuController, extension methods, misc helpers.
SafetyProto.Runtime.Unity   Runtime/ — MonoBehaviour adapters and scene systems: PPE, Session,
                            Task, Safety, Feedback, Analytics, Actions, Interaction, Recenter,
                            Scaffolding, Simulation, Tools, PhaseController, SceneLoader,
                            XrPerformanceBootstrap.
SafetyProto.Networking.Unity Networking/EvaluatorDashboard — HTTP/WebSocket servers, pose
                            sender, dashboard command handlers.
SafetyProto.UI.Unity        UI/ — HUDs, task list, session report, popup system, menus.
SafetyProto.Editor          Editor/ — editor windows, importers, drawers, build hooks.
```

References point downward only: `Domain.Core → EventBus.Core`;
`Utils.Unity → Domain.Core, EventBus.Core`; `Runtime.Unity → Utils.Unity` and
below; `Networking.Unity` and `UI.Unity` each → `Runtime.Unity` and below;
`Editor` → everything but Networking. `Networking` and `UI` do not reference each other, and
`Runtime` references neither. Two consequences follow. A dashboard command
handler that needs scene services lives in the Networking assembly, because
Runtime cannot reference Networking back. And gameplay waits for popups through bus events, never by referencing
the UI assembly (§4).

`Core` and `Domain` are the load-bearing layers, and the dependency graph shows
it: the most-connected nodes in the whole codebase are `SafetyProto.Core`, its
`Interfaces` and `Logging` sub-namespaces, the `Core.Events` facades, `EventBus`,
and `TaskManagerCore`. Feature modules cluster *around* these shared contracts
rather than around one another. That is the intended shape: a hub-and-spoke
topology centered on the event core, not a mesh of feature-to-feature calls.

### The shared / harness boundary

The `Domain/` layer and the event-contract half of `Core/` compile into
`SafetyProto.Shared.dll` via `Tools/SafetyProto.Shared/SafetyProto.Shared.csproj`.
The csproj globs `Domain/**/*.cs` — the domain assembly is engine-free by
construction, so everything under it compiles wholesale — and lists the `Core/`
files one by one, because `Core/` is mixed and only its engine-free part may
cross. There is no duplication and no port: one set of source files participates
in two compilations. The Unity runtime and the CLI harness therefore execute
*identical* orchestration, rule, scoring, and logging code, differing only in the
adapters wired around it. The same linked-source model feeds the authoring
applications and the headless test runner (§21). See
[ADR 0005](docs/adr/0005-one-source-two-compilations.md).

---

## 3. The event bus

`EventBus` is a `ScriptableObject` singleton loaded from
`Resources/EventBus.asset`. If that asset is missing every system auto-disables,
which is deliberate: no bus means no protocol, so nothing should half-run.

Events are **deferred**. Publishing enqueues a payload; it is not delivered
inline. `EventBusRunner`, a `MonoBehaviour`, drains the queue from `Update()`
each frame with a per-frame time budget (2 ms) so that a burst of events cannot
stall a frame on the headset; the budget is checked after each dispatch, so at
least one item runs per frame and the rest waits for the next. Delivery order is
causal — events are processed in the order they were enqueued. A queue deeper
than 1000 items raises a `SafetyError` once per crossing and the warning re-arms
when the queue drains.

Two subscription styles coexist, one per side of the boundary:

- **Unity subscribers** use `EventBus.Instance.onXxx.AddListener(...)` with a
  matching `RemoveListener` in `OnDisable`/`OnDestroy`. Before subscribing a
  `MonoBehaviour` calls the `this.IsEventBusReady()` extension; if the bus is
  absent the component disables itself instead of throwing.
- **Domain subscribers** use `IEventBus.Subscribe<T>` / `Unsubscribe`. This is
  the interface the headless harness implements, so the same domain class
  subscribes the same way in both runtimes.

Both styles are served from one queue: each enqueued dispatch invokes the
`UnityEvent` listeners first, then the typed subscribers. The static
`OnXxxCSharp` events that an earlier revision exposed have been removed; do not
reintroduce them. There is one bus, and everything flows through it.

### Event stamping and identity

Every payload implements `IEventPayload`: four correlation fields —
`SessionId`, `PlayerId`, `ScenarioId`, `TimestampMs` — and a `SetMetadata`
setter. The bus stamps them on every publish through
`EventMetadata.Stamp<T>`, drawing the values from the static `EventContext`
that `TrainingSessionManager` fills at `BeginSession()` and clears at teardown.
Stamping happens at publish time, so a timestamp records when the producer
raised the event, not when the queue delivered it. Producers never hand-stamp
metadata, and because the contract is an interface rather than a per-type list,
a payload that forgets to implement it fails `EventMetadataTests` instead of
producing empty session ids in a log.

The event facades in `Core/Events/` (`SessionEvents`, `ActionEvents`,
`PPEEvents`, `ScoreEvents`, `SafetyEvents`, `PopupEvents`) are thin static
`Raise*`/`Publish*` methods over `EventBus.Instance`. They are the Unity-side
path only; pure domain code publishes through the `IEventBus` it was
constructed with, and `HarnessEventBus` applies the same `EventMetadata.Stamp`,
so both hosts give the same stamping guarantee. See
[docs/authoring-events.md](docs/authoring-events.md) for the producer-side
split.

### The synchronous consequence channel — a deliberate exception

`DirectConsequenceChannel` (`Core/Events/ConsequenceEvents.cs`) does **not** go
through the deferred queue. When the `InspectionGateValidator` decides a
consequence must play (for example a scripted hazard reaction), it raises
`ConsequenceStarted` / `ConsequenceEnded` synchronously so that animation and
audio timing are not deferred by a frame. This is the one place the "everything
is queued" rule is broken, and it is intentional and contained: the subscribers
(`EventGameObjectListener`, the session simulator) are audio/visual only and
have no ordering dependency on the queued protocol. Keep it that way — do not
route game-state changes through the synchronous channel (see
[ADR 0001](docs/adr/0001-synchronous-consequence-channel.md)).

---

## 4. The event protocol

The protocol vocabulary is the set of payload structs in `Core/EventPayloads.cs`
and `Core/Events/`:

| Event                           | Producer(s)                     | Consumer(s)                           |
|---------------------------------|---------------------------------|---------------------------------------|
| `SessionStartedEventArgs`       | `TrainingSessionManager`, harness | `SessionLoggerCore`, dashboard relay, UI, `PlayerSpawnCenter` |
| `SessionPausedEventArgs` / `SessionResumedEventArgs` | `SessionPause.Hold/Release` only | `SessionLoggerCore`, dashboard relay, `TimerUI`, `LogHUD`, `AmbienceAudioController` |
| `SessionCompletedEventArgs`     | `TaskManagerCore`               | `SessionLoggerCore`, dashboard relay, UI |
| `SessionEndedEventArgs`         | `TaskManagerCore` (after the summary), `TrainingSessionManager` (abort) | `TrainingSessionManager`, dashboard relay, `EventBusPerfRecorder`, `LogHUD`, `EventGameObjectListener` |
| `ActionAttemptedEvent`          | Unity interactors, `SafetyIssueReporter`, `ScriptedActor`, `SessionSimulator` | `SafetyRuleEngineCore`, logger, dashboard relay |
| `PPEStateChangedEventArgs`      | `PPEManager`, `HarnessPPEManager`, `ScriptedActor`, `SessionSimulator` | `SafetyRuleEngineCore`, logger, dashboard relay |
| `TaskEventArgs` (Phase: Started/Completed) | `TaskManagerCore`, `SafetyRuleEngineCore` | `TaskManagerCore`, logger, UI, score |
| `TaskGroupEventArgs` (Phase: Started/Completed) | `TaskManagerCore` | `SafetyRuleEngineCore`, logger, UI, `PhaseController` |
| `ScoreChangedEventArgs`         | `ScoreService`                  | UI, logger, dashboard relay           |
| `SafetyViolationEventArgs`      | `SafetyRuleEngineCore`, `HazardClassificationPolicy`, `InspectionGateValidator` | `SafetyAnalyzer`, logger, dashboard relay |
| `CriticalSafetyFailureEventArgs`| `SafetyAnalyzer`, `InspectionGateValidator` | UI, logger, dashboard relay |
| `SafetyErrorEventArgs`          | `EventBus` itself (queue overflow, listener threw), `TimerSystem` | `LogHUD`, logger, dashboard relay |
| `ActionRefusedEventArgs`        | `SafetyRuleEngineCore`          | the emitter of the refused attempt (`ScaffoldPieceInstaller`, `SafetyIssueReporter`) |
| `PopupClosedEventArgs`          | `PopupService`                  | gameplay objects waiting on a warning |
| `PopupShownEventArgs`           | `PopupService`                  | popup camera (`FadeHandOverlay`)      |
| `ConsequenceStarted` / `ConsequenceEnded` (synchronous) | `InspectionGateValidator` | `EventGameObjectListener`, `SessionSimulator` |

The design points worth stating here are these:

- **Phase discriminators.** `TaskEventArgs` and `TaskGroupEventArgs` carry a
  `Phase` field (`Started` / `Completed`) rather than being split
  into separate event types. A single typed subscriber key then carries both
  lifecycle phases of the same conceptual event, which avoids delegate-combination
  collisions between what would otherwise be near-identical payload types. A
  group that runs out of time is closed by `TaskManagerCore.HandleGroupTimeout()`,
  which marks its pending tasks `NotPerformed` — there is no timeout phase on the
  event.
- **Pause is a hold count, not a flag.** `SessionPause` is the single entry
  point for pausing. Holders — application focus, an open popup, the pause menu
  — are keyed by `PauseSource`, and `SessionPaused` / `SessionResumed` are
  published only on the 0→1 and 1→0 transitions of the hold set. A popup
  opening over an already-paused session therefore produces no second event,
  and the countdown timer freezes for exactly as long as any hold exists.
  `IsPaused` reflects the holds immediately; the events arrive on the next pump
  (see [ADR 0004](docs/adr/0004-session-pause-hold-count.md)).
- **The bus reports its own faults through the protocol.** `SafetyError` is how
  a listener exception or a queue overflow reaches the log and the dashboard. The
  bus dispatches it with error-raising disabled so a faulty `SafetyError`
  listener cannot recurse.
- **Violations are identified, not just described.** Safety violations and log
  entries carry stable task and group identifiers (with a human-readable name as
  a fallback), so a session log can be analyzed by id even if display names
  change between scenario revisions. Every violation and score-reason string is
  a constant in `ViolationCodes`; the strings are logged, so they are frozen —
  add a constant, never rename one.
- **A refused attempt answers its emitter.** A violation explains a refusal to
  the participant; it does not tell the object that acted. When the rule engine
  declines an attempt it also publishes `ActionRefusedEventArgs`, keyed by action
  id and source id, so an emitter that changed the world optimistically can undo
  it — a scaffold piece snaps into its socket, then puts itself back when the
  attempt turns out to be refused for a pending precondition. This invariant
  holds on every decline path (`NO_ACTIVE_GROUP`, `WRONG_ACTION`,
  `PREREQUISITE_PENDING`), funnelled through one `RefuseAttempt` call in
  `SafetyRuleEngineCore` so the violation and the refusal cannot drift apart.
  `PPE_MISSING` is deliberately not one of them: it is not a decline — the task
  still completes as `CompletedSuccessButUnsafe` — so routing it through the
  funnel would tell an emitter to undo a task that succeeded. On the emitter
  side, `RefusedAttemptTracker` (`Domain/Safety`) decides whether a refusal
  belongs to this emitter and *when* to revert: immediately when no popup is
  up, otherwise on the matching popup close or after a fallback deadline, and
  only once.
- **The UI announces the panel's visibility, not gameplay state.**
  `PopupShownEventArgs` / `PopupClosedEventArgs` are published as the shared
  popup panel appears and goes away (button, dismiss, or auto-close). Gameplay
  waits for a warning to have been read before changing the world under the
  participant, and the popup camera keeps the hands in front of the panel while
  it is up — neither consumer needs a dependency from the runtime assembly onto
  the UI assembly.

---

## 5. Session and task lifecycle

A session runs as a chain of protocol exchanges, not a chain of method calls:

```
TrainingSessionManager.BeginSession()   → resets ScoreService, restamps EventContext,
                                           raises SessionStarted
  └─ TaskManager (Mono)                 → loads ScenarioDef JSON, delegates to TaskManagerCore
       └─ [participant acts]            → scene emitter raises ActionAttempted / PPEStateChanged
            └─ SafetyRuleEngine (Mono)  → delegates to SafetyRuleEngineCore
                 └─ TaskManagerCore     → CheckGroupCompletion() → GroupCompleted / SessionCompleted
                                                                  → SessionEnded
```

`TaskManagerCore` is the pure-C# orchestrator: it sequences groups, advances
tasks, gates on group dependencies, and handles timeouts, completion, and the
final `SessionCompleted` emission, followed by `SessionEnded`. `TaskManager`
(the `MonoBehaviour`) only resolves Inspector references, loads the scenario
through `ScenarioSource`, and adapts the scene to the core's interfaces.
`RuntimeSafetyTask` (in `Core/`) wraps the static `ISafetyTask` data with live
per-run state. Groups start in authored order, skipping any whose
`requiredGroups` are not yet complete; a configurable delay (2 s by default)
separates one task's completion from the next task's start, scheduled through
the injected `IAsyncScheduler`.

### Execution modes

Each `TaskGroupDef` declares an `executionMode`:

- **Sequential** — tasks must complete in order; the wrong action is a safety
  violation.
- **FreeOrder** — any task in the group may complete in any order.
  `SafetyRuleEngineCore` maintains an `_activeFreeOrderTasks` set to track which
  of the group's tasks are still open.

Groups may declare `requiredGroups`, so a group can be gated behind the
completion of others regardless of execution mode. A group may also carry a
`timeLimit`; `TimerSystem` counts it down (from group start for Sequential, from
the first task for FreeOrder), freezes it while the session is paused, and a
limit of zero counts up forever. The shipped scenarios set no limit.

### Time and scheduling are injected

The cores never read `Time.time` or start a coroutine. `ITimerSource` supplies
the session clock and `IAsyncScheduler` supplies delays; the runtime binds them
to `TimerSystem` and to Unity's `Awaitable`, the harness to a stopwatch. This is
what lets the same `TaskManagerCore` run a scripted session in milliseconds
under `dotnet test`. The clock is defined as pause-excluded session time:
`SessionClock` (`Domain/Metrics`) advances only while the session runs unpaused,
and both the group countdown and every logged completion time read it, so a
popup or a lost headset focus never inflates a recorded duration. A scheduler
may complete or throw when its token is cancelled; the orchestrator checks the
token after every await, so a reset during the inter-task delay can never
resume the session it tore down.

---

## 6. Scenario, action, and capability catalogs

Everything a session enforces is data, loaded through a layered lookup so a
build can be re-authored without rebuilding:

- **Scenario.** `ScenarioSource.Load(name)` prefers
  `persistentDataPath/scenarios/<name>.json` when it exists and parses, and
  falls back to the embedded `Resources/Scenarios/<name>`. `ScenarioLoader`
  parses leniently (unknown members ignored, never throws), normalizes legacy
  scoring keys, and resolves `requiredGroups` by group name. The CLI harness
  plays the same `default.json`.
- **Actions.** `ActionCatalogSource` applies the same override-then-resource
  rule to `actions.json`. Scene components do not hold `ActionDef` references;
  they hold an action-id string (marked `[ActionId]` for the Inspector drawer)
  and resolve it lazily through the static `ActionResolver`. `TaskManager`
  checks at load that every task's action id resolves, and logs rather than
  aborts when one does not.
- **Capabilities.** `CapabilityCatalogExporter` writes, per build, the action
  ids, PPE types, and phases the build actually understands.
  `CapabilityCatalog` is a DTO shared with the authoring tools; the GUI
  restricts an author's choices to it, and `ScenarioValidator` rejects a
  scenario that names an action or PPE the build lacks. Validation runs in the
  authoring tools, not in the engine.
- **Risk.** A task is authored with a `RiskAssessment` — severity and
  probability on 1–5 scales — whose product is banded into a `RiskLevel`
  (`Trivial` … `Intolerable`). `RiskLevel` is the single driver of the score
  economy (§8); `Substantial` is the eliminatory threshold and `Tolerable` the
  tier whose base penalty prices incidental charges.

`ScenarioCompatibility` compares a candidate scenario with the one loaded in the
scene — groups, order, modes, limits, actions, PPE, scoring, script — so the
session simulator (§15) can refuse an external script that no longer matches the
scene it is about to drive.

---

## 7. PPE compliance

Personal protective equipment is modeled physically and validated logically, and
the two are kept apart. `PPESnapSlot` components report equip state to
`PPEManager` (via `ReportPPEStateChange`) when an item snaps into its body slot.
`PPEComplianceAdapter` wraps `PPEManager` as an `IPPEComplianceChecker`, which is
the contract `SafetyRuleEngineCore` consumes. The rule engine never touches a
collider; it asks the adapter whether the required PPE is worn.

When a task's action succeeds but the required PPE is missing, the task does not
simply fail — it resolves to `CompletedSuccessButUnsafe` and a `ppePenalty` is
applied. The distinction matters pedagogically: the participant performed the
action, but unsafely, and the log and score must reflect both facts.

Two task shapes exist:

- **Action task** — has an `actionId`; completes on a matching
  `ActionAttemptedEvent`.
- **Equip-set task** — empty `actionId`, populated `requiredPPE`; completes when
  all listed PPE are worn, in any order, driven by `PPEStateChanged`
  (`TaskExecutionRules.IsEquipTask`, shared by the task and safety engines).
  Used for donning sequences. The PPE
  slot's action mapping must be empty for these, so that putting the item on does
  not raise a `WRONG_ACTION` violation.

`PPEType` (`Core/PPEType.cs`) is `None=0, Helmet=1, Goggles=3, Harness=4,
Vest=5, Boots=6, GloveLeft=7, GloveRight=8`. Ordinal `2` is intentionally
skipped — it was a legacy combined `Gloves` value, now split into left/right; the
gap preserves serialized-asset compatibility for the values above it.

The physical layer under `Runtime/PPE/` is deliberately dumb about training:
`HelmetChinstrap` and `VerletLanyard` are Verlet simulations, `HandSurfaceLimiter`
keeps the rendered hand off colliders without touching tracked poses,
`GloveHandSkin` swaps hand materials, `PPESlotBodyCalibrator` places slots at
body height. The one physical component with safety meaning is
`RetractableLanyardController`, whose `onLanyardLocked(isCorrectAnchor)` tells
`FallFromHeightController` (§11) whether gravity may be suspended.

---

## 8. Scoring

`ScoreService` is a pure C# class in `Domain/Scoring/` with no engine
dependencies; it tracks the running score and raises `ScoreChanged`.
`ScoreManagerAdapter` (in `Runtime/Task/`) bridges gameplay events
(`TaskCompleted`) to `ScoreService` calls. Rule evaluation for scoring lives in
`ScoreRuleEngineCore`, so the economy of the game is engine-independent and
testable without Unity.

No task outcome subtracts points. A task is worth what its risk level grades it
at; completing it without the required PPE earns a reduced share, and not doing
it at all earns nothing. The risk weighting therefore does the work a penalty
tier would: skipping an Intolerable task forfeits more than skipping a Tolerable
one, without the report having to show a participant a negative number.

The subtractions that remain are *incidental charges*: things the participant
did that no task models. Equipping out of the authored order (caught by the
phase-advance gate), pressing the inspection gate with tasks still pending,
snapping a distractor item, and misclassifying a hazard each cost points. They
are computed in the domain — `IncidentalChargePolicy` in `Domain/Scoring` and
`HazardClassificationPolicy` in `Domain/Safety` — from the scenario's
`ScoringConfig` (the incidental tier's base penalty, or the gate charge), so a
headless run charges exactly what the headset does. The MonoBehaviours only
decide *when* one of these happened.

---

## 9. Session modes: guided and evaluation

A session runs in one of two modes, which change how strictly the orchestrator
enforces order and how it reports what the participant did *not* do.

- **Guided mode** teaches. The active task is highlighted; out-of-order actions
  are corrected rather than punished, so the participant is walked through the
  procedure. Group prerequisites are enforced, and a popup explains a task whose
  group precondition is still pending.
- **Evaluation mode** (*modo avaliação*) assesses. Every group runs as FreeOrder
  (`TaskExecutionRules.EffectiveMode` — the one place a mode branch over
  execution order is allowed), prerequisites are bypassed, and the orchestrator
  advances through phases behind a **phase-advance gate**: the participant must
  explicitly complete and confirm a phase, and the gate evaluates whether the
  tasks were done, done out of order (an order deviation is logged as a
  violation but costs no points), or left undone.
  Both gates run the same closer: pending tasks at phase end are marked
  `NotPerformed` and each raises a `TASK_NOT_PERFORMED` violation, so a skipped
  PPE leaves the same trace as a skipped inspection step. The session summary
  carries a completion flag and a per-task outcome block — id, group, risk
  grading and final state for every task — which is what an evaluator reads
  afterward and what makes adherence per task analysable across sessions.

Both modes run the same `TaskManagerCore` and the same rule engine; the mode
changes gating and reporting, not the underlying orchestration. The mode
reaches the cores by injection: `TaskManagerCore`, `SafetyRuleEngineCore`,
`SessionLoggerCore`, and `DashboardEventRelay` each take a `Func<SessionMode>`,
which the runtime binds to the static `SessionModeState.Current` (written by
the pre-session mode selector or the simulator) and the harness to its
`--mode` argument. Some runtime and UI components — `PhaseController`,
`InspectionGateValidator`, `TaskUIController` — still read the static directly;
they are scene-bound and never run headless, but new domain code must take the
delegate. The evaluator dashboard surfaces the active mode so an observer knows
whether they are watching a lesson or an assessment.

---

## 10. Participant identity and anonymization

The product is used for human-subject sessions, so participant identity is
handled with care. A pre-session popup captures the participant's name; the
system then derives an anonymized `P-XXXX` identifier and uses *that* everywhere
downstream — in the session log, on the in-headset menu, and on the evaluator
dashboard. The name-to-id map is kept private in
`persistentDataPath/participants_private.json`, separate from the session
artifacts, so a log or a dashboard screenshot never carries a real name.
Simulated sessions (§15) use a `SIM-` id and write their artifacts under a
separate `simulations/` directory so they can never be mistaken for human data.

---

## 11. Consequences, hazards, and the safety analyzer

Three mechanisms turn unsafe behaviour into something the participant sees, and
each is pinned to a different layer on purpose.

**The inspection gate plays scripted consequences.** `InspectionGateValidator`
is the scene object a participant presses to declare the scaffold ready. When
tasks are still pending it raises the gate violation and runs its authored
consequence mappings in sequence — object falls, visual alerts — each wrapped in
the synchronous `ConsequenceStarted` / `ConsequenceEnded` pair (§3). A
`PlayerFallSimulation` is always sorted last and treated as terminal: it hands
off to `FallFromHeightController`, which restores the gravity it had suspended
while the lanyard was correctly locked, plays `ScaffoldCollapseSequence` (a
beat sheet that knows nothing about sessions or score), and raises
`CriticalSafetyFailure`. The fall deliberately does not recenter the player.

**Hazard classification is an action with an answer.** `SafetyIssueReporter`
sits on a defect. A gaze dwell (§13) reveals its report button; the report
publishes an ordinary `ActionAttempted` whose context carries the chosen
option, so the task engine completes the task like any other. Whether the
option was *correct* is judged separately by `HazardClassificationPolicy`,
which publishes a `HAZARD_MISCLASSIFIED` violation and charges the base penalty
without touching the task state — reporting the wrong hazard class is a wrong
answer, not an unperformed inspection. A refused report reverts through the
same `RefusedAttemptTracker` as a scaffold piece.

**The analyzer watches for patterns the rules cannot see.** `SafetyAnalyzer`
feeds every `SafetyViolation` into `SafetyPatternDetector`, a pure sliding
window (three violations in 30 s by default), and raises
`CriticalSafetyFailure` once per crossing, re-arming when the count falls. It
is the only consumer that reasons across violations; the rule engine judges one
attempt at a time.

---

## 12. Session logging and persistence

`SessionLoggerCore` (pure C#) subscribes to the full protocol, maintains an
in-memory log, and persists it as JSON when the session completes. It does not
know how to serialize for a given engine or where to write — those are injected:
the Unity wrapper supplies `Application.persistentDataPath` (or its
`simulations/` subfolder for simulated runs), the harness an output directory;
both pass `SessionLoggerCore.SerializeIndentedOmittingDefaults`
(Newtonsoft.Json, defaults omitted) as the serializer, so the two artifacts are
byte-comparable. The log schema is shared, so a Unity session and a harness
session produce directly-comparable artifacts. This equivalence is what makes
the harness usable as an evidence generator: a scripted scenario and a
hand-played scenario yield logs in the same format.

The file is `session_log_<stem>.json`, where `SessionFileStem` combines the UTC
start time with the first eight characters of the session id; the perf CSV
(§16) uses the same stem, so a session's artifacts sort together. The summary
block carries `completed`, the participant id, the mode, the app version and
build commit, score totals, the per-task outcome list, and — when a perf source
is injected — a `perf` summary of frame and flush timings. The log is written on
`SessionCompleted`; a reset or abandoned session leaves a fallback summary with
`completed=false`, and because the file name is keyed on session start,
repeated writes overwrite the same file rather than multiplying.

---

## 13. Interaction primitives

Three small mechanisms recur across the scene, and each splits its logic from
its engine binding the same way the domain does.

- **Gaze dwell.** `HeadGazeSource` casts one ray per frame from the head and
  calls `IGazeTarget.OnGazed`. `GazeDwellTarget` flags the hit and ticks
  `GazeDwellTimer` — pure C#, with dwell, grace, and decay durations — in
  `LateUpdate`. `DwellRingView` draws the progress; `SafetyIssueReporter`
  acts on completion. Dwell is a "look at it for a while" primitive, never a
  click.
- **One button, two interactors.** `DualModeButton` unifies Meta's poke and
  ray interactables through a shared `PointableElement`, so menu buttons work
  for hands and controllers without duplicating prefabs.
- **Menus that follow the head.** `MenuFollowSolver` is a plain class holding
  the follow/snap math; `MenuFollowHmd` and `MenuSnapInFront` are the
  MonoBehaviours that apply it. The solver is tested in EditMode without a rig.

---

## 14. Recenter and phase transitions

`RecenterService` owns the single fade-out → recenter → reground → fade-in
coroutine and a busy guard; nothing else moves the rig during a session.
`PlayerRecenter` is the static solver underneath (`Solve` is pure; `Recenter`
applies it so the head lands over the target). Who asks for a recenter, and
where to:

- `PhaseController` implements `IRecenterAnchorProvider` — the start anchor
  before the phase transition, the scaffold spawn anchor after — and asks for a
  recenter when it teleports the participant between phases. It also owns the
  Evaluation advance button and the Guided group-completion popup.
- `RecenterCommandHandler` serves the dashboard's `recenter_player` command
  (§17) and refuses while busy or without an anchor.
- `PlayerSpawnCenter` recenters once, without a fade, on `SessionStarted`.

While a recenter is in flight the service sets `DashboardGate.PoseBroadcastSuspended`
so the pose stream cannot hitch the frame mid-teleport.

---

## 15. The session simulator

`SessionSimulator` (`Runtime/Simulation/`, compiled only in the Editor and in
Development builds) replays a scenario's `script` inside the real scene. Its
architectural point is that it drives the **same facades** a headset does —
`PPEEvents.RaisePpeStateChanged`, `ActionEvents.PublishActionAttempt`,
`PhaseController.OnAdvanceClicked`, `InspectionGateValidator.Validate()` — so
everything downstream of the bus is exercised exactly as in a human session.
It does not move task objects; a `move` step teleports the rig through
`SimulatedPlayerMover`, which reads stand points from the dashboard's scene
layout (§17) and switches locomotion off for the duration.

The script format, `ScriptStepDef` (`Domain/Scenarios`), is shared with the CLI
harness's `ScriptedActor`: the harness publishes the steps to its own bus and
ignores `move`; the simulator feeds them to the scene. Before running an
external script the simulator checks it against the loaded scenario with
`ScenarioCompatibility` (§6). It takes a `SIM-` participant id, suppresses the
pre-session popups, arms auto-confirm on the gates, waits for the bus to
dispatch each input and for the task state to settle, and reports through a
`SessionSimulationResult` rather than by throwing. `SessionSimulatorWindow`
drives it in the Editor; on a device, `DeviceSimulationLauncher` starts it from
an Android intent extra, which is what `scripts/quest-sim-run.ps1` uses for
unattended headset runs. See [docs/session-simulator.md](docs/session-simulator.md).

---

## 16. Performance instrumentation

The bus exposes one probe, `IEventBusFlushProbe`: a single static hook called
once per non-empty flush, on the main thread, with the queue length before the
flush, the number dispatched, the elapsed ticks, and whether the budget cut the
flush short. The contract is that the implementation must not allocate.
`EventBusPerfRecorder` (`Utils/`) is the only implementer; each `LateUpdate` it
commits one row — flush time, dispatch count, and the engine's frame timings —
into `FramePerfBuffer`, a fixed-capacity, allocation-free buffer in
`Domain/Metrics`. `FramePerfCsv` writes the rows to
`persistentDataPath/perf/perf_<stem>.csv` on session end, reset, pause, focus
loss, or quit, never on a timer; `PerfSummary` reduces them to nearest-rank
percentiles that `SessionLoggerCore` embeds in the session log (§12).

`XrPerformanceBootstrap` applies the headset-side render settings at startup —
eye-texture resolution scale, optional fixed viewport scale, foveated rendering
level with gaze allowed — and retries for a few frames until the OpenXR display
subsystem is up. Its values are the knobs the GPU-performance work turns; keep
them here, not scattered across scene components.

---

## 17. Observability: the evaluator dashboard

`EvaluatorDashboardBootstrap` runs an HTTP + WebSocket server from the headset so
an evaluator can watch a live session in a browser on the local network — task
progress, score, safety events, and participant pose.

Architecturally the dashboard is an **observer with a narrow command
channel**, and the seam between gameplay and transport is drawn inside the
domain:

- `DashboardEventRelay` (`Domain/Dashboard`, pure C#) subscribes through
  `IEventBus` to the thirteen protocol events an evaluator needs, maps each to
  a DTO via `DashboardDtoMapper`, and hands it to an `IDashboardHost`. The host
  interface is the whole transport contract: broadcast, build the session
  manifest, resolve a timestamp, remember known groups. `FakeDashboardHost`
  implements it in EditMode tests, so the relay's behaviour is pinned without a
  socket.
- `EvaluatorDashboardBootstrap` (`Networking`) implements `IDashboardHost`,
  starts `EvaluatorWebSocketServer` and `MiniHttpServer`, serves the generated
  dashboard files from `Resources/Dashboard/`, and runs `PoseSender`.
- Telemetry is one-way. Two inbound envelopes exist: `RequestSync`, answered
  with the scene layout, session, manifest, and score for that client only, and
  `Command`, routed to scene components that implement
  `IDashboardCommandHandler` (today `recenter_player` and `reset_session`) and
  acknowledged with `CommandAckDto`. Handlers are discovered by type when the
  scene loads; no command reaches the domain, and the event protocol stays
  publish-only. The channel carries no authentication yet; that is deferred on
  purpose until the QA phase.

In the dependency structure this reads as follows: `Networking.Unity`
references `Runtime.Unity` one way, so command handlers can reach
`RecenterService` and `SceneLoader`; `Runtime.Unity` never references
Networking, and the domain knows the dashboard only as `IDashboardHost`. The
whole observability subsystem can be removed by deleting the Networking
assembly and the `Domain/Dashboard` folder without touching a line of
orchestration — exactly the payoff the event protocol is meant to deliver.

Constraints worth recording for anyone extending it:

- **Source generation.** The dashboard's web files are authored in
  `Tools/DashboardSrc/` and mirrored by `DashboardSourceSync` into
  `Assets/_SafetyProto/Resources/Dashboard/*.txt` (Unity imports `.txt` as
  `TextAsset`). Edit the source; never hand-edit the generated `.txt`.
- **Threads.** Inbound frames arrive on per-client reader threads and are
  queued to the main thread, which drains them in `Update()`. Outbound events
  are serialized on a dedicated encoder thread; each client has an
  `OutgoingMessageBuffer` with a bounded reliable FIFO and a single replaceable
  pose slot. A client that cannot keep up with reliable frames is disconnected
  rather than allowed to grow the queue.
- **Pose stream.** `PoseReporter` writes head, hands, and PPE attachment into a
  `PoseChannelSO` each `LateUpdate`; `PoseSender` reads it at 10 Hz and
  broadcasts it as droppable. `DashboardGate` is a process-wide flag that only
  the pose stream honours; `RecenterService` sets it during a teleport so a
  blocking send cannot drop the participant through a collider. Discrete events
  are never gated. Do not optimise the pose stream further: its cost was
  measured and closed as negligible (see
  [ADR 0003](docs/adr/0003-dashboard-gate-process-wide-flag.md)).
- **Scene layout.** `DashboardSceneLayout` is a per-scene authoring component
  listing floors and markers; its DTO is sent on `RequestSync` so the browser's
  Three.js viewport can draw the scaffold, and its `TryGetStandPoint` doubles as
  the simulator's waypoint source (`ISimulationWaypoints`).

---

## 18. Audio: one voice for judgement, many for the world

Sound in this project comes from two places on purpose, and the split is worth
knowing before adding a clip.

**Evaluative feedback has one voice.** Whether the participant just succeeded,
succeeded unsafely, violated a rule, or triggered a critical failure is
announced by `AudioFeedbackManager` through a single `AudioSource`, and which of
those a moment gets is decided by `AudioFeedbackArbiter` in `Domain/Feedback/` —
pure, time-injected, and therefore testable without an engine. It exists because
one action routinely raises several feedback events at once: completing a task
without the required PPE publishes both the violation and the unsafe completion,
and layering the two clips reads as noise rather than as a verdict. The arbiter
collapses a frame's requests to the most important one (critical > failure >
unsafe success > success) and lets a clip already sounding be interrupted only by
something strictly more important. A dropped request is not replayed later:
feedback is about what just happened.

**Diegetic sound is local and spatial.** A carabiner latching, a piece seating
into its socket, a helmet buckling, a button clicking — these are emitted by the
object that caused them, through its own `AudioSource`, so the participant can
locate them by ear. They do not pass through the arbiter and are not arbitrated
against each other: several physical events *should* be audible together, and
head-relative placement is the point. `AmbienceAudioController` is the same idea
at scene scale, crossfading a ground bed against a height bed as the participant
climbs.

The rule of thumb: if a sound is the system's *opinion* about what the
participant did, it belongs to the arbiter; if it is something that happened in
the world, it belongs to the object it happened to. Components that play locally
accept a serialized `AudioSource` and fall back to creating one with code
defaults — they warn at `Awake` when they take that path, because the spatial
settings then come from the fallback rather than from the scene.

---

## 19. Cross-cutting: the session-reset lifecycle

Restarting a session must return every stateful system to a clean baseline
without a scene reload leaking old state. Systems that hold session state
implement `ISessionResettable`. `SceneLoader.ResetSession()` — wired to the
Restart buttons and to the dashboard's `reset_session` command — scans scene
`MonoBehaviour`s for `ISessionResettable`, calls `ResetSession()` on each, then
reloads the scene. Non-`MonoBehaviour` state is reset through the
`TrainingSessionManager` lifecycle instead: `BeginSession()` resets
`ScoreService` and restamps `EventContext`; teardown raises an aborted
`SessionEnded`, clears both, and drops every pause hold. New stateful systems
must opt into this contract rather than relying on Unity's domain reload.

---

## 20. The harness boundary and external participants

The CLI harness (`Tools/CliHarness/`) is not a mock of the game — it is the same
domain assembly driven by different participants. It demonstrates three
extensibility roles, each an implementation of a protocol contract:

- **Producer** — `ScriptedActor` reads a JSON step sequence (PPE changes, action
  attempts) and publishes them to the bus with configurable delays. It stands in
  for the Unity interactors.
- **State stub** — `HarnessPPEManager` is a small dictionary-backed
  `IPPEComplianceChecker`, replacing the physics-driven `PPEManager` for scenarios
  that do not need colliders.
- **Observer** — `TranscriptRecorder` subscribes to every event type and streams
  a structured transcript to stdout.

`HarnessEventBus` is a queued `IEventBus` with causal-order delivery and the
same `EventMetadata.Stamp` on publish. That the harness classes implement the
*same* `IEventBus`, `IPPEComplianceChecker`, and `PPEType` as the Unity build is
the concrete proof of the extensibility claim: new participants join by
implementing a contract, not by depending on the engine.

---

## 21. Tests and tooling

The pure classes above are tested in EditMode, and the engine-free subset of
those tests also runs headless:

- `Assets/_SafetyProto/Tests/Editor/` holds 44 fixture files plus
  `Support/` fakes — `FakeEventBus`, `FakeDashboardHost`, `FakeTaskBuilder`,
  `SessionTestHarness`, `EventTimeline`. The folder has no assembly definition,
  so Unity compiles it into the default Editor assembly with access to every
  layer.
- `Tools/SafetyProto.Tests/` is an NUnit project that links 22 of those same
  fixtures by source and runs them under `dotnet test` against
  `SafetyProto.Shared`. Fixtures that depend on engine types are excluded. This
  is why policies live in pure classes rather than in the components that call
  them: a `MonoBehaviour` that subscribes in `OnEnable` cannot be reached from
  either runner, so the decision it makes is extracted (`IncidentalChargePolicy`,
  `HazardClassificationPolicy`, `GazeDwellTimer`, `MenuFollowSolver`) and the
  component is left with only the *when*.
- `Tools/AuthoringApp/` (console) and `Tools/AuthoringApp.Gui/` (Avalonia)
  edit and validate scenarios and action catalogs against the exported
  `CapabilityCatalog`; both reference `SafetyProto.Shared`, so they parse with
  the same `ScenarioLoader` the headset uses.
- `Tools/EventBusBench/` measures `HarnessEventBus` dispatch cost in isolation;
  `Tools/DashboardSrc/tests/` covers the dashboard's pure layout math under
  Node.

A full Unity EditMode run is not optional after a change that touches a
component: `dotnet test` compiles only the engine-independent subset.

---

## 22. What the dependency structure confirms

Three properties of the architecture are visible directly in how the code
connects, and they are the empirical form of the claims above:

1. **It is hub-and-spoke, not a mesh.** The most-connected symbols are the event
   core and the shared contracts (`SafetyProto.Core`, its interfaces and logging,
   the event facades, `EventBus`, `TaskManagerCore`) — not any feature module.
   Features attach to the shared spine; they do not wire to each other.
2. **The domain/transport seam holds.** The domain reaches the dashboard only
   through `IDashboardHost`, and the Networking assembly reaches gameplay only
   downward, through `Runtime`. Deleting the Networking assembly and
   `Domain/Dashboard` leaves every other assembly compiling — the decoupling the
   protocol promises is enforced by the assembly graph, not by convention.
3. **The layering is real.** Module clusters correspond to the seven declared
   assemblies rather than cutting across them, which is what the per-layer
   assembly definitions and the domain's `noEngineReferences` flag are there to
   guarantee.

---

## 23. Extension points, at a glance

| To add… | Do this |
|---|---|
| A domain-produced event | Add a payload struct implementing `IEventPayload` to `EventPayloads.cs` (or a standalone `Core/Events/` file), publish it through the injected `IEventBus` — no facade, no `UnityEvent` field. |
| A Unity-produced event | Add the payload, then a `Raise*`/`Publish*` method on a `Core/Events/` facade (new or existing) that calls `EventBus.Instance`; register a `UnityEvent` + subscription on `EventBus` if the event needs one. |
| Domain logic | Place pure C# in `Scripts/Domain/`; the shared csproj picks it up by glob. A `Core/` file the domain needs must be added to `SafetyProto.Shared.csproj` by hand, and only if it references no `UnityEngine` type. |
| A violation or score reason | Add a constant to `ViolationCodes`; never rename an existing one — the strings are in logs. |
| An action type | Add an `ActionDef` to `Resources/Actions/actions.json`; wire a scene emitter with the same action-id string. |
| A task | Author an action task (`actionId`) or an equip-set task (`requiredPPE`, empty `actionId`) via the authoring GUI or scenario JSON. |
| A dashboard command | Implement `IDashboardCommandHandler` on a scene `MonoBehaviour` in the Networking assembly; the bootstrap discovers it on scene load and acks through `CommandAckDto`. |
| A dashboard event | Subscribe in `DashboardEventRelay`, map in `DashboardDtoMapper`, pin it in `DashboardEventRelayTests` with `FakeDashboardHost`. |
| A scripted step kind | Extend `ScriptStepDef`, then teach both `ScriptedActor` (harness) and `SessionSimulator` (scene) to play it. |
| An external participant | Implement `IEventBus` / `IPPEComplianceChecker` and drive the protocol — as the CLI harness does. |

For task-authoring specifics see [docs/authoring-tasks.md](docs/authoring-tasks.md); for event-authoring specifics see [docs/authoring-events.md](docs/authoring-events.md).
