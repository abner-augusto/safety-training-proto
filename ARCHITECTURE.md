# Architecture

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
   `Scripts/Runtime/`, never in the domain.

2. **Systems never call each other directly.** They publish and subscribe to a
   shared event bus. A producer does not know its consumers; a consumer does not
   know its producers. This is what lets a scripted actor, a headless stub, or a
   remote observer take the place of a Unity component transparently.

3. **Events are typed.** The protocol vocabulary is a fixed set of payload
   structs in `Core/EventPayloads.cs`. There is no stringly-typed message bus and
   no reflection-based dispatch on the hot path; a subscriber binds to a payload
   type, not to a topic name.

4. **Engine specifics are injected, not imported.** Where domain code needs a
   capability the engine provides — persistent storage path, JSON serialization,
   a wall clock, a scheduler — that capability is passed in as an interface or a
   delegate. The domain declares the contract (`Core/Interfaces/`); the runtime
   supplies the implementation.

---

## 2. Layered structure

The code is organized into seven layers, each an assembly definition so that the
compiler enforces the dependency direction. Higher layers depend on lower ones;
the domain depends on nothing engine-specific.

```
Core          EventBus, EventPayloads, RuntimeSafetyTask, PPEType, GameConstants,
              Events/ (facades), Interfaces/, Logging/         → assembly: EventBus.Core
Domain        Actions, Capabilities, Scenarios, Safety, Scoring,
              Sessions, Tasks — pure C#, shared with the harness → assembly: Domain.Core
Runtime       MonoBehaviour adapters and scene systems (PPE,
              Session, Task, Safety, Feedback, Analytics, Tools) → assembly: Runtime.Unity
Networking    Evaluator dashboard HTTP/WebSocket server           → assembly: Networking.Unity
UI            HUDs, task UI, popup system                         → assembly: UI.Unity
Utils         Logging adapters, extension methods                → assembly: Utils.Unity
Editor        Editor-only tools and custom inspectors            → assembly: SafetyProto.Editor
```

`Core` and `Domain` are the load-bearing layers, and the dependency graph shows
it: the most-connected nodes in the whole codebase are `SafetyProto.Core`, its
`Interfaces` and `Logging` sub-namespaces, the `Core.Events` facades, `EventBus`,
and `TaskManagerCore`. Feature modules cluster *around* these shared contracts
rather than around one another, and each module cluster maps cleanly onto one of
the seven layers above. That is the intended shape: a hub-and-spoke topology
centered on the event core, not a mesh of feature-to-feature calls.

### The shared / harness boundary

The `Domain/` layer and the event-contract half of `Core/` compile into
`SafetyProto.Shared.dll` via `Tools/SafetyProto.Shared/SafetyProto.Shared.csproj`,
which `<Compile Include>`s the same `.cs` files the Unity build uses. There is no
duplication and no port: one set of source files participates in two compilations.
The Unity runtime and the CLI harness therefore execute *identical* orchestration,
rule, scoring, and logging code, differing only in the adapters wired around it.

---

## 3. The event bus

`EventBus` is a `ScriptableObject` singleton loaded from
`Resources/EventBus.asset`. If that asset is missing every system auto-disables,
which is deliberate: no bus means no protocol, so nothing should half-run.

Events are **deferred**. Publishing enqueues a payload; it is not delivered
inline. `EventBusRunner`, a `MonoBehaviour`, drains the queue from `Update()`
each frame with a per-frame time budget (about 2 ms) so that a burst of events
cannot stall a frame on the headset. Delivery order is causal — events are
processed in the order they were enqueued.

Two subscription styles coexist, one per side of the boundary:

- **Unity subscribers** use `EventBus.Instance.onXxx.AddListener(...)` with a
  matching `RemoveListener` in `OnDisable`/`OnDestroy`. Before subscribing a
  `MonoBehaviour` calls the `this.IsEventBusReady()` extension; if the bus is
  absent the component disables itself instead of throwing.
- **Domain subscribers** use `IEventBus.Subscribe<T>` / `Unsubscribe`. This is
  the interface the headless harness implements, so the same domain class
  subscribes the same way in both runtimes.

The static `OnXxxCSharp` events that an earlier revision exposed have been
removed; do not reintroduce them. There is one bus, and everything flows
through it.

### Event stamping and identity

Producers do not hand-stamp metadata. The event facades in `Core/Events/`
(`SessionEvents`, `ActionEvents`, `PPEEvents`, `ScoreEvents`, `SafetyEvents`,
`ConsequenceEvents`) expose static `Raise*` methods that stamp every event with
`SessionId`, `PlayerId`, `ScenarioId`, and a Unix timestamp drawn from
`EventContext`. A subscriber can therefore correlate any event to a session and
participant without the producer having to thread that state through.

### The synchronous consequence channel — a deliberate exception

`ConsequenceEvents` does **not** go through the deferred queue. When the
`InspectionGateValidator` decides a consequence must play (for example a
scripted hazard reaction), it raises `ConsequenceStarted` / `ConsequenceEnded`
synchronously so that animation and audio timing are not deferred by a frame.
This is the one place the "everything is queued" rule is broken, and it is
intentional and contained: the subscribers are audio/visual only and have no
ordering dependency on the queued protocol. Keep it that way — do not route
game-state changes through the synchronous channel.

---

## 4. The event protocol

The protocol vocabulary is the set of payload structs in `Core/EventPayloads.cs`.
The README carries the full producer/consumer table; the design points worth
stating here are these:

- **Phase discriminators.** `TaskEventArgs` and `TaskGroupEventArgs` carry a
  `Phase` field (`Started` / `Completed` / `Timeout`) rather than being split
  into separate event types. A single typed subscriber key then carries both
  lifecycle phases of the same conceptual event, which avoids delegate-combination
  collisions between what would otherwise be near-identical payload types.
- **Violations are identified, not just described.** Safety violations and log
  entries carry stable task and group identifiers (with a human-readable name as
  a fallback), so a session log can be analyzed by id even if display names
  change between scenario revisions.
- **A refused attempt answers its emitter.** A violation explains a refusal to
  the participant; it does not tell the object that acted. When the rule engine
  declines an attempt it also publishes `ActionRefusedEventArgs`, keyed by action
  id and source id, so an emitter that changed the world optimistically can undo
  it — a scaffold piece snaps into its socket, then puts itself back when the
  attempt turns out to be refused for a pending precondition.
- **The UI announces dismissal, not visibility.** `PopupClosedEventArgs` is
  published whenever the shared popup panel goes away (button, dismiss, or
  auto-close). It lets gameplay wait for a warning to have been read before
  changing the world under the participant, without a dependency from the runtime
  assembly onto the UI assembly.

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
```

`TaskManagerCore` is the pure-C# orchestrator: it sequences groups, advances
tasks, gates on group dependencies, and handles timeouts, completion, and the
final `SessionCompleted` emission. `TaskManager` (the `MonoBehaviour`) only
resolves Inspector references, loads the scenario through `ScenarioSource`, and
adapts the scene to the core's interfaces. `RuntimeSafetyTask` (in `Core/`) wraps
the static `ISafetyTask` data with live per-run state.

### Execution modes

Each `TaskGroupDef` declares an `executionMode`:

- **Sequential** — tasks must complete in order; the wrong action is a safety
  violation.
- **FreeOrder** — any task in the group may complete in any order.
  `SafetyRuleEngineCore` maintains an `_activeFreeOrderTasks` set to track which
  of the group's tasks are still open.

Groups may declare `requiredGroups`, so a group can be gated behind the
completion of others regardless of execution mode.

---

## 6. PPE compliance

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
  (`SafetyRuleEngineCore.IsEquipTask`). Used for donning sequences. The PPE
  slot's action mapping must be empty for these, so that putting the item on does
  not raise a `WRONG_ACTION` violation.

`PPEType` (`Core/PPEType.cs`) is `None=0, Helmet=1, Goggles=3, Harness=4,
Vest=5, Boots=6, GloveLeft=7, GloveRight=8`. Ordinal `2` is intentionally
skipped — it was a legacy combined `Gloves` value, now split into left/right; the
gap preserves serialized-asset compatibility for the values above it.

---

## 7. Scoring

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
one, without the report having to show a participant a negative number. The only
subtraction left in the session is the order-deviation charge applied by the
phase-advance gate.

---

## 8. Session modes: guided and evaluation

A session runs in one of two modes, which change how strictly the orchestrator
enforces order and how it reports what the participant did *not* do.

- **Guided mode** teaches. The active task is highlighted; out-of-order actions
  are corrected rather than punished, so the participant is walked through the
  procedure.
- **Evaluation mode** (*modo avaliação*) assesses. The orchestrator advances
  through phases behind a **phase-advance gate**: the participant must explicitly
  complete and confirm a phase, and the gate evaluates whether the tasks were
  done, done out of order (an order deviation carries a penalty), or left undone.
  Both gates run the same closer: pending tasks at phase end are marked
  `NotPerformed` and each raises a `TASK_NOT_PERFORMED` violation, so a skipped
  PPE leaves the same trace as a skipped inspection step. The session summary
  carries a completion flag and a per-task outcome block — id, group, risk
  grading and final state for every task — which is what an evaluator reads
  afterward and what makes adherence per task analysable across sessions.

Both modes run the same `TaskManagerCore` and the same rule engine; the mode
changes gating and reporting, not the underlying orchestration. The evaluator
dashboard surfaces the active mode so an observer knows whether they are watching
a lesson or an assessment.

---

## 9. Participant identity and anonymization

The product is used for human-subject sessions, so participant identity is
handled with care. A pre-session popup captures the participant's name; the
system then derives an anonymized `P-XXXX` identifier and uses *that* everywhere
downstream — in the session log, on the in-headset menu, and on the evaluator
dashboard. The name-to-id map is kept private and separate from the session
artifacts, so a log or a dashboard screenshot never carries a real name.

---

## 10. Session logging and persistence

`SessionLoggerCore` (pure C#) subscribes to the full protocol, maintains an
in-memory log, and persists it as JSON when the session completes. It does not
know how to serialize for a given engine or where to write — those are injected:
the Unity wrapper supplies `Application.persistentDataPath` and a Unity
serializer; the harness supplies a file path and `System.Text.Json`. The log
schema is shared, so a Unity session and a harness session produce
directly-comparable artifacts. This equivalence is what makes the harness usable
as an evidence generator: a scripted scenario and a hand-played scenario yield
logs in the same format.

---

## 11. The harness boundary and external participants

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

`HarnessEventBus` is a queued `IEventBus` with causal-order delivery. That the
harness classes implement the *same* `IEventBus`, `IPPEComplianceChecker`, and
`PPEType` as the Unity build is the concrete proof of the extensibility claim:
new participants join by implementing a contract, not by depending on the engine.

---

## 12. Observability: the evaluator dashboard

`EvaluatorDashboardBootstrap` runs an HTTP + WebSocket server from the headset so
an evaluator can watch a live session in a browser on the local network — task
progress, score, safety events, and participant pose.

Architecturally the dashboard is a **pure, read-only observer**: it subscribes to
every event facade, translates each event into a serialization DTO, and pushes it
over the wire. It never issues commands back into gameplay. This has a telling
consequence in the dependency structure: `EvaluatorDashboardBootstrap` is the
single articulation point between the gameplay/domain assemblies and the
networking assembly. Gameplay code never references the network layer and the
network layer never references gameplay; the bootstrap is the only class that
touches both. A whole observability subsystem was therefore added without
coupling the domain to any transport — exactly the payoff the event protocol is
meant to deliver. It also means the dashboard can be removed or replaced wholesale
without touching a line of domain logic.

Two constraints are worth recording for anyone extending it:

- **Source generation.** The dashboard's web files are authored in
  `Tools/DashboardSrc/` and mirrored by `DashboardSourceSync` into
  `Assets/_SafetyProto/Resources/Dashboard/*.txt` (Unity imports `.txt` as
  `TextAsset`). Edit the source; never hand-edit the generated `.txt`.
- **Main-thread protection.** WebSocket sends run on the main thread and can
  stall a frame if the pose stream floods during an expensive moment (such as a
  teleport). `DashboardGate` gates the pose stream through those windows so the
  observability layer never degrades the headset experience.

---

## 13. Cross-cutting: the session-reset lifecycle

Restarting a session must return every stateful system to a clean baseline
without a scene reload leaking old state. Systems that hold session state
implement `ISessionResettable`. `SceneLoader.ResetSession()` — wired to the
Restart buttons — scans scene `MonoBehaviour`s for `ISessionResettable`, calls
`ResetSession()` on each, then reloads the scene. Non-`MonoBehaviour` state is
reset through the `TrainingSessionManager` lifecycle instead: `BeginSession()`
resets `ScoreService` and restamps `EventContext`; teardown clears both. New
stateful systems must opt into this contract rather than relying on Unity's
domain reload.

---

## 14. What the dependency structure confirms

Three properties of the architecture are visible directly in how the code
connects, and they are the empirical form of the claims above:

1. **It is hub-and-spoke, not a mesh.** The most-connected symbols are the event
   core and the shared contracts (`SafetyProto.Core`, its interfaces and logging,
   the event facades, `EventBus`, `TaskManagerCore`) — not any feature module.
   Features attach to the shared spine; they do not wire to each other.
2. **The domain/transport seam holds.** `EvaluatorDashboardBootstrap` is the sole
   bridge between the gameplay assemblies and the networking assembly. The
   decoupling the protocol promises is not aspirational — it is enforced by the
   fact that removing that one adapter would fully disconnect the two halves.
3. **The layering is real.** Module clusters correspond to the seven declared
   layers rather than cutting across them, which is what the per-layer assembly
   definitions are there to guarantee.

---

## 15. Extension points, at a glance

| To add… | Do this |
|---|---|
| A new event | Add a payload struct to `EventPayloads.cs`, a `Raise*` method to the relevant facade in `Core/Events/`, and register the subscription in `EventBus`. |
| Domain logic | Place pure C# in `Scripts/Domain/` and add its path to `SafetyProto.Shared.csproj` so it compiles into the harness too. |
| An action type | Add an `ActionDef` to `Resources/Actions/actions.json`; wire a scene emitter with the same action-id string. |
| A task | Author an action task (`actionId`) or an equip-set task (`requiredPPE`, empty `actionId`) via the authoring GUI or scenario JSON. |
| An external participant | Implement `IEventBus` / `IPPEComplianceChecker` and drive the protocol — as the CLI harness does. |

For task-authoring specifics see [docs/authoring-tasks.md](docs/authoring-tasks.md).
