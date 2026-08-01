# Session Simulator

The Session Simulator is a semantic, no-headset replay tool for the Unity Editor.
It consumes the scenario `script` already used by the CLI (`ppe`, `action`, and
`gate` steps) and drives the real event facades and public gate methods. It does
not simulate hands, physics, locomotion, or a second task engine — it exercises
the *decision* systems (tasks, gates, scoring, consequences), not the *interaction*
ones.

Use it to reproduce a full guided or evaluation run in seconds, to check a
scenario end to end after editing it, or to see how a specific omission plays out
without putting on a Quest.

## Prerequisites

- Open the `SafetyTraining` scene.
- `TrainingSessionManager.autoStartOnStart` must be **disabled** so the real
  session does not start before the simulator does. The scene ships this way
  because the pre-session name/mode flow drives the start; if you turned it on,
  turn it back off.
- The scene must contain the systems the simulator binds to: `TrainingSessionManager`,
  `TaskManager`, `PhaseAdvanceGate`, `PhaseController`, and `InspectionGateValidator`.
  These are all present in `SafetyTraining`.

## Quick start

1. Enter Play Mode. In the Editor window the onboarding controllers are disabled
   and their active popup is closed automatically before the first simulator
   command. The popup service stays active so gameplay feedback can still be tested.
2. Open `SafetyProto > Session Simulator`.
3. Leave the loaded scenario selected to replay the scenario `TaskManager` already
   loaded, or pick an external JSON with **Escolher...** (e.g.
   `Tools/CliHarness/scenarios/eval_omission.json`). An external file must describe
   the same groups/tasks/actions as the loaded scenario — see
   [Using an external scenario](#using-an-external-scenario).
4. Choose **Guiado** or **Avaliação**. The embedded script always comes from the
   scenario `TaskManager` loaded; only the optional external JSON path changes the
   script source.
5. Press **Executar tudo** for automatic replay, or **Próxima etapa** repeatedly
   for manual step-by-step replay. **Executar tudo** can continue after manual steps.
6. Read the results in the window: status line, task states, score, transcript,
   consequences, and any timeout diagnostic.
7. Press **Cancelar** to stop the actor. To run another simulation, exit and
   re-enter Play Mode first (see [One run per Play session](#one-run-per-play-session)).

## Modes

- **Guiado** (`Guided`) — the guided flow: the gate button auto-completes and the
  phase advances when its group is done.
- **Avaliação** (`Evaluation`) — the evaluation flow: the phase-1 gate closes its
  group leaving pending tasks untouched (never attempted), the inspection gate
  closes pending tasks as `CompletedFailure` and applies their consequences, and
  the run finishes with a scored report. Use this mode to exercise the
  consequence path.

## Scripts

A scenario's `script` is an ordered array of steps the simulator replays. Each
step has a `kind` and a `delayMs` (clamped to 0–30 s) applied before it runs.

| `kind`   | Fields | Effect |
|----------|--------|--------|
| `ppe`    | `ppeType`, `isWearing` | Raises `PPEStateChanged` for that PPE. `ppeType` matches the `PPEType` enum (`Boots`, `GloveLeft`, `GloveRight`, `Goggles`, `Helmet`, `Harness`, `Vest`). |
| `action` | `actionId` | Publishes an action attempt (e.g. `install_guardrail`). Must be a real action id from the scenario. |
| `gate`   | `gateTarget` (optional) | Presses a real gate. `phase1` advances past the PPE-selection group; `inspection` runs the scaffold inspection gate. |

`gate` target resolution: an explicit `gateTarget` of `phase1` (`phase`, `fase1`)
or `inspection` (`final`, `inspecao`) is used as given. An empty target keeps CLI
compatibility — it infers `phase1` for the `ppe_selection` group and `inspection`
otherwise. Being explicit is clearer; the inference exists so CLI scripts run
unchanged.

A step that is not equipped/performed before its gate leaves its task pending, and
the gate closes it in evaluation mode — as `CompletedFailure` at the inspection
gate, or untouched (never attempted) at the phase-1 gate. That omission is how you
author "the participant forgot X".

### Phase-1 gate and the PPE group

The `phase1` gate works **whether or not** every PPE was equipped:

- If some PPE is still missing, the gate closes the group (leaving the missing
  tasks in their pending state — never attempted, no violation raised) and then
  teleports.
- If every PPE was equipped, the group has already completed on its own; the gate
  is a no-op and the simulator waits for the teleport that is already running.

You do **not** need to leave a PPE omitted just to make the phase gate fire.

## Using an external scenario

The external JSON's `script` is what runs, but its groups/tasks/actions must match
the scenario `TaskManager` loaded (checked by `ScenarioCompatibility.Validate`).
The scene is wired to one scenario's structure; a mismatched script would emit
events for tasks that do not exist. If the structures differ, the run fails fast
with a Portuguese diagnostic naming the mismatch instead of producing a misleading
transcript. In practice: copy the loaded scenario, keep its `groups` intact, and
edit only the `script`.

## Example scenarios

Under `Tools/CliHarness/scenarios/` (all compatible with the default scenario):

- **`eval_omission.json`** — omits the boots (PPE) and the damaged-mesh report,
  completing everything else. The boots stay never-attempted at the phase-1 gate;
  the mesh report shows `TASK_FAILED` at the inspection gate. No visual
  consequence (neither omitted task has a consequence mapping).
- **`eval_consequence.json`** — equips all PPE and completes the guardrail, toeboard
  and mesh report, but omits the lanyard connection (`connect_harness`). At the
  inspection gate this drives the real blackout consequence — see below.
- **`ppe_equip.json`** — the focused PPE-only scenario.

## Triggering consequences

In evaluation mode the inspection gate plays a visual/synchronous consequence for
each **pending task that has a `ConsequenceMapping`** on the scene's
`InspectionGateValidator`. To make a consequence fire, omit a task whose `actionId`
is mapped.

The default scene maps exactly one: `connect_harness` (the lanyard) →
`PlayerFallSimulation` blackout. So omitting the lanyard connection while completing
the rest — what `eval_consequence.json` does — raises `ConsequenceStarted`/`Ended`
plus a `CriticalSafetyFailure` ("Trabalhou desconectado"). Omitting a task with no
mapping (e.g. the mesh report) is still recorded as `TASK_FAILED` but plays no
animation.

## Reading the results

The window and the `SessionSimulationResult` expose:

- **status** — `Running`, `Completed`, `Failed`, or `Cancelled`, plus the current
  step, active group, and score.
- **tasks** — every session task and its final state (`CompletedSuccess`,
  `CompletedSuccessButUnsafe`, `CompletedFailure`, `NotStarted`, …).
- **transcript** — the ordered event stream (SessionStarted, PPE/action attempts,
  task/group lifecycle, score changes, violations, consequences).
- **consequences** — the `ConsequenceStarted` entries that fired.
- **diagnostic** — the last status message; on `Failed` it names what timed out.

## Data separation

Every simulated participant receives a fresh `SIM-...` id, and the simulator never
writes the private real-name mapping. This `SIM-` prefix is the MVP data-separation
contract:

- Session logs for a `SIM-` run are written under
  `Application.persistentDataPath/simulations`, not alongside real runs.
- They still appear as live dashboard events, but are excluded from the dashboard's
  localStorage history.

## Coverage and limitations

Covered: session start, PPE state events, action attempts, real phase and inspection
gates, task/group/session lifecycle, score changes, violations, and synchronous
consequences. Event waits are bounded and report diagnostics in Portuguese. Input
dispatch waits are short; phase transitions, inspection consequences, blackout, and
terminal session settlement use a separate 45-second operation timeout.

Still requires headset validation: hand interaction, snapping and calibration,
physics and locomotion, visual/audio comfort, object animations, and
dashboard/device behavior. PlayMode coverage for repeated manual steps and gate
coroutine timing remains an editor-runtime check; pure scenario compatibility and
script-format tests run headlessly (`dotnet test`).

### One run per Play session

**Executar tudo** and **Próxima etapa** are disabled after completion, failure, or
cancellation. Same-session retry is intentionally outside the MVP because
`TrainingSessionManager.BeginSession()` is idempotent — exit and re-enter Play Mode
to start a fresh simulation.

## Troubleshooting

- **"A sessão real já foi iniciada."** — `autoStartOnStart` is on, or a previous
  simulation already ran this Play session. Disable auto-start and/or re-enter Play
  Mode.
- **"Cenário externo incompatível…"** — the external JSON's groups/tasks/actions do
  not match the loaded scenario. Base it on the loaded scenario and edit only the
  `script`.
- **"Espera da transição de fase excedeu 45 s…"** — the scaffold ground was not
  confirmed. Check the scene's `PhaseController` spawn/ground references. (This is
  no longer produced by equipping all PPE; that case is handled.)
