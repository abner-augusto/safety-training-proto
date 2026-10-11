# Authoring Events

The protocol vocabulary is the set of payload structs in
`Assets/_SafetyProto/Scripts/Core/EventPayloads.cs` plus the standalone payload
files under `Assets/_SafetyProto/Scripts/Core/Events/`. This guide covers how to
add one, and where the plumbing already exists so you don't rebuild it.

See `ARCHITECTURE.md` §3–4 for the bus mechanics (deferred delivery, causal
ordering, the synchronous consequence exception) and §16 for the extension-point
table this guide is linked from.

## The producer-side split

There are two kinds of producer, and the rule for which one you write depends on
which side of the domain/engine seam the code lives on (principle 4 in
`ARCHITECTURE.md` §1: pure domain logic must not reference `UnityEngine`).

**Domain producers** (`Scripts/Domain/**`) publish through the `IEventBus` they
were constructed with — `_bus.Publish(new SomethingEventArgs(...))` — and get no
facade and no `UnityEvent` field. `SafetyRuleEngineCore` publishing
`ActionRefusedEventArgs` from `RefuseAttempt` is the example: the domain has no
dependency on `EventBus.Instance` or `UnityEngine`, so it cannot call a facade
even if one existed. This is also what makes the class headlessly testable
against `FakeEventBus` — the CLI harness and the Unity runtime both satisfy
`IEventBus`, so the same producer code runs under both.

**Unity-side producers** (`Scripts/Runtime/**`, `Scripts/UI/**`) go through a
facade in `Core/Events/` — `SessionEvents`, `ActionEvents`, `PPEEvents`,
`ScoreEvents`, `SafetyEvents`, `PopupEvents`, `ConsequenceEvents`. A facade is a
static class with `Raise*`/`Publish*` methods that call `EventBus.Instance`
directly, so it is Unity-side by construction and must never be referenced from
`Scripts/Domain/**`. This is where the stamping guarantee is stated: every
facade method stamps `SessionId`, `PlayerId`, `ScenarioId`, and a Unix
timestamp before the payload reaches a subscriber, so a producer never
hand-stamps metadata and a subscriber can always correlate an event to a
session and participant.

If your new event has no reason to be Unity-only (most don't), give it a
facade even if today's only producer happens to be a `MonoBehaviour` — that
keeps the door open for a future domain producer without a rewrite. Write a
facade-free domain-only event only when the producer is genuinely pure C#,
the way `ActionRefusedEventArgs` is.

## Where stamping actually happens

There is one stamping path. Every payload struct implements `IEventPayload`
explicitly, and `EventMetadata.Stamp` fills the four metadata fields
(`SessionId`, `PlayerId`, `ScenarioId`, `TimestampMs`) through that interface,
handing back the stamped copy. Both ways a payload enters the bus call it:

- **Facade events with a dedicated `UnityEvent`** (the ones with a `public
  UnityEvent<T> onXxx` field and a `RaiseXxx` method on `EventBus`, e.g.
  `onActionAttempt`/`RaiseActionAttempt`) are stamped by
  `EventMetadata.Stamp` at the top of that `RaiseXxx` method, before the
  payload is queued.
- **Typed-only payloads** (no dedicated `UnityEvent` — `ActionRefusedEventArgs`,
  `PopupClosedEventArgs`, and `PopupShownEventArgs` today) fall through `EventBus.Publish<T>`'s
  `default` branch, which calls `EventMetadata.Stamp(payload)` before
  `DispatchTyped` queues it.

A type that does not implement `IEventPayload` passes through `Stamp`
unchanged, so forgetting the interface would leave the metadata empty.
`EventMetadataTests.EveryPayloadStructImplementsIEventPayload` fails for any
struct that declares `SessionId` without implementing the interface.

## Adding a new event, step by step

1. Add the payload struct to `EventPayloads.cs` (or a standalone file under
   `Core/Events/` if the type is large enough to warrant its own file, as
   `ActionRefusedEventArgs.cs` and `PopupClosedEventArgs.cs` are). Give it the
   four metadata fields (`SessionId`, `PlayerId`, `ScenarioId`, `TimestampMs`)
   and a constructor that defaults them to empty/zero — `EventMetadata` fills
   them in, the producer never should.
2. Implement `IEventPayload` explicitly (copy the block from any existing
   payload).
3. Decide whether it needs a dedicated `UnityEvent<T> onXxx` field and
   `RaiseXxx`/`case` entry on `EventBus`, or whether it is fine to travel as a
   typed-only payload through the generic `Publish<T>` default branch (this is
   the right default for anything the domain also publishes, since the domain
   has no reason to reach for a `UnityEvent`-shaped API).
4. Write the facade method (Unity-side producers only) in the matching
   `Core/Events/*Events.cs` file, or add a new one if the event doesn't belong
   to an existing facade's domain.
5. Files under `Scripts/Domain/` are compiled into the harness automatically
   (the csproj globs the folder). A **`Core/`** file that domain code needs must
   still be added to `Tools/SafetyProto.Shared/SafetyProto.Shared.csproj` by
   hand — and only if it references no `UnityEngine` type;
   `Core/Events/PopupEvents.cs` is the deliberate counter-example: it references
   `EventBus.Instance` and `UnityEngine.Events`, so the harness cannot compile it
   and should never try.
6. Add the payload to the list in
   `EventMetadataTests.AllSupportedPayloadsReceiveTheSameMetadataContract`.
   If step 2 was missed, `EventMetadataTests.EveryPayloadStructImplementsIEventPayload`
   catches the omission.

## Subscribing

- **Unity subscribers**: `EventBus.Instance.onXxx.AddListener(Handler)` behind
  `this.IsEventBusReady()`, with the matching `RemoveListener` in
  `OnDisable`/`OnDestroy`. This is the only pattern for a facade event with a
  dedicated `UnityEvent`.
- **Typed-only payloads**, from either side: `EventBus.Instance.Subscribe<T>`
  (Unity-side, e.g. `ScaffoldPieceInstaller` subscribing to
  `ActionRefusedEventArgs`) or the injected `IEventBus.Subscribe<T>` (domain
  side, e.g. `SafetyRuleEngineCore` subscribing to `ActionAttemptedEvent`).
  Always pair a `Subscribe` with an `Unsubscribe` using the *same* delegate
  instance — cache it in a field (`_onActionRefused ??= HandleActionRefused;`)
  rather than passing a new lambda to each call, or `Unsubscribe` silently
  does nothing.

## The synchronous exception

`ConsequenceEvents` (`ConsequenceStarted`/`ConsequenceEnded`) bypasses the
deferred queue entirely and dispatches inline — see `ARCHITECTURE.md` §3. This
exists so a scripted hazard reaction's animation/audio timing isn't delayed by
a frame. It is deliberately the only synchronous channel. Do not add a second
one; route game-state changes through the normal deferred `Publish`/`Subscribe`
path even when the timing feels tight, and use `ConsequenceEvents` only for the
audio/visual reaction it already owns.

## Violation and refusal codes

Every `SafetyViolationEventArgs.ViolationCode` and
`ActionRefusedEventArgs.ReasonCode` value comes from
a constant in `SafetyProto.Core.ViolationCodes` (`NO_ACTIVE_GROUP`,
`WRONG_ACTION`, `PREREQUISITE_PENDING`, `PPE_MISSING`, `TASK_NOT_PERFORMED`,
`HAZARD_MISCLASSIFIED`, `PPE_OUT_OF_ORDER`, `ACTION_ID_MISSING`,
`ORDER_VIOLATION`, `GATE_PENALTY`, `GATE_FAILED`, `INSPECTION_INCOMPLETE`,
`WRONG_PPE_SELECTED`), not a bare string literal at the call site. Score
reasons passed to `IScoreService.SubtractPoints` use the same constants. These
values are recorded in session logs and compared across scenario revisions, so
an existing constant's *value* must never change — add a new constant for a new
code instead of repurposing one.
