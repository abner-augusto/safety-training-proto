# Session Simulator

The Session Simulator is a semantic, no-headset replay tool for the Unity Editor.
It consumes the scenario `script` already used by the CLI (`ppe`, `action`, and
`gate` steps) and drives the real event facades and public gate methods. It does
not simulate hands, physics, locomotion, or a second task engine — it exercises
the *decision* systems (tasks, gates, scoring, consequences), not the *interaction*
ones.

Use it to reproduce a full guided or evaluation run in seconds, to check a
scenario end to end after editing it, or to see how a specific omission plays out
without putting on a Quest. A Development build can also run it on the headset,
with nobody wearing it — see [On the headset](#on-the-headset).

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
   loaded, or pick an external JSON with **Escolher...** (e.g. an omission
   variant, see [Variant scripts](#variant-scripts)). An external file must describe
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
- **Avaliação** (`Evaluation`) — the evaluation flow: both gates close their group
  the same way, marking every still-open task `NotPerformed` and raising one
  `TASK_NOT_PERFORMED` violation each; the inspection gate additionally plays the
  consequences of what was left undone. The run finishes with a scored report. Use
  this mode to exercise the consequence path.

## Scripts

A scenario's `script` is an ordered array of steps the simulator replays. Each
step has a `kind` and a `delayMs` (clamped to 0–30 s) applied before it runs.

| `kind`   | Fields | Effect |
|----------|--------|--------|
| `ppe`    | `ppeType`, `isWearing` | Raises `PPEStateChanged` for that PPE. `ppeType` matches the `PPEType` enum (`Boots`, `GloveLeft`, `GloveRight`, `Goggles`, `Helmet`, `Harness`, `Vest`). |
| `action` | `actionId` | Publishes an action attempt (e.g. `install_guardrail`). Must be a real action id from the scenario. |
| `gate`   | `gateTarget` (optional) | Presses a real gate. `phase1` advances past the PPE-selection group; `inspection` runs the scaffold inspection gate. |
| `move`   | `target`, `posture` (optional) | Parks the player rig at a waypoint with its eyes 1.65 m (`standing`, default) or 1.05 m (`crouched`) above the floor. See [Moving the player](#moving-the-player). The CLI harness ignores it. |

`gate` target resolution: an explicit `gateTarget` of `phase1` (`phase`, `fase1`)
or `inspection` (`final`, `inspecao`) is used as given. An empty target keeps CLI
compatibility — it infers `phase1` for the `ppe_selection` group and `inspection`
otherwise. Being explicit is clearer; the inference exists so CLI scripts run
unchanged.

A step that is not equipped/performed before its gate leaves its task pending, and
the gate closes it as `NotPerformed` in evaluation mode. That omission is how you
author "the participant forgot X".

### Moving the player

Without a headset the Editor camera sits at the rig's feet, and after the phase
teleport the Meta character controller falls through the 3 cm scaffold deck. A
`move` step, or **Mover jogador** in the window (any time in Play Mode, with or
without a running simulation), puts the player where the dashboard and the
posture chip can be checked:

```json
{ "kind": "move", "target": "toeboard", "posture": "crouched", "delayMs": 200 }
```

- **Targets** are the ids of the scene's `DashboardSceneLayout`: the floors
  `canteiro` and `andaime` (the player stands at the floor's centre) and the task
  spots `guardrail`, `toeboard`, `anchor` and `irregularity` (the player stands
  on the nearest point of the floor under the spot, 0.25 m in from the edge,
  facing it). An unknown target fails the run and lists the valid ids.
- **Locomotion is switched off** for the rest of Play Mode on the first move, so
  the player stays put.
- **A move waits for a phase teleport in progress** to land first; otherwise
  the teleport would undo it.

The default scenario script moves to each scaffold spot before its action, and
crouches for the toeboard.

### Phase-1 gate and the PPE group

The `phase1` gate works **whether or not** every PPE was equipped:

- If some PPE is still missing, the gate closes the group — each missing PPE
  becomes `NotPerformed` and raises a `TASK_NOT_PERFORMED` violation — and then
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

## Variant scripts

The repository keeps one scenario, the canonical
`Assets/_SafetyProto/Resources/Scenarios/default.json`; the CLI harness plays only
that file. To exercise an evaluation path the default script does not reach, copy
it outside the repository, keep `groups` and `scoring` untouched, and edit only the
`script`:

- **Omission:** drop the `Boots` PPE step and the `flag_safety_net` action, and add
  a `gate` step after the PPE steps and another at the end. Both omitted tasks show
  `TASK_NOT_PERFORMED`, the boots at the phase-1 gate and the mesh report at the
  inspection gate. Neither has a consequence mapping, so nothing plays.
- **Consequence:** drop only the `connect_harness` action and end with a `gate`
  step. At the inspection gate this drives the real blackout consequence, see below.

Delete the copy afterwards; a committed variant drifts from the canonical scenario.

## Triggering consequences

In evaluation mode the inspection gate plays a visual/synchronous consequence for
each **pending task that has a `ConsequenceMapping`** on the scene's
`InspectionGateValidator`. To make a consequence fire, omit a task whose `actionId`
is mapped.

The default scene maps exactly one: `connect_harness` (the lanyard) →
`PlayerFallSimulation` blackout. So omitting the lanyard connection while completing
the rest — the consequence variant above — raises `ConsequenceStarted`/`Ended`
plus a `CriticalSafetyFailure` ("Trabalhou desconectado"). Omitting a task with no
mapping (e.g. the mesh report) is still recorded as `TASK_NOT_PERFORMED` but plays
no animation.

## Reading the results

The window and the `SessionSimulationResult` expose:

- **status** — `Running`, `Completed`, `Failed`, or `Cancelled`, plus the current
  step, active group, and score.
- **tasks** — every session task and its final state (`CompletedSuccess`,
  `CompletedSuccessButUnsafe`, `NotPerformed`, `NotStarted`).
- **transcript** — the ordered event stream (SessionStarted, PPE/action attempts,
  task/group lifecycle, score changes, violations, consequences).
- **consequences** — the `ConsequenceStarted` entries that fired.
- **diagnostic** — the last status message; on `Failed` it names what timed out.

## Data separation

Every simulated participant receives a fresh `SIM-...` id, and the simulator never
writes the private real-name mapping. This `SIM-` prefix is the MVP data-separation
contract:

- Session logs for a `SIM-` run are written under
  `Application.persistentDataPath/simulations`, not alongside real runs, and their
  EventBus/frame-time CSVs under `simulations/perf/` instead of `perf/`.
- They still appear as live dashboard events, but are excluded from the dashboard's
  localStorage history.

## On the headset

Development builds compile the simulator in; release builds do not. Build one with
`scripts/build-android.ps1 -Development` (`Builds/Android/SafetyTraining_dev.apk`) and
`adb install -r` it. Launching a
Development build with a `safety_sim` intent extra skips the identification,
mode and onboarding popups and replays the script as soon as the scenario loads:

```powershell
scripts/quest-sim-run.ps1 -Mode avaliacao -Runs 3 -MetricsCsv
```

The script checks that the installed package is debuggable, overrides the
proximity sensor and pauses Guardian so a headset resting on a desk keeps the app
in focus, launches one run per app start, and saves to `dist/quest-sim/` each
run's session log, perf CSV, `[SafetyProto]` logcat lines, per-second VrApi lines
and, with `-MetricsCsv`, the OVR Metrics CSV. It restores the sensor and Guardian
when it finishes. `-Script <file.json>` pushes and plays a variant script.

The script wakes the screen, but an OS dialog on the headset (typically Quest Link
"available" while the cable is on a PC) holds every launch until someone dismisses it
with the headset on. The script detects "Launch is blocked" in logcat, stops, and says
so. Disabling Guardian is already part of the script; it does not clear that dialog.

By hand: `adb shell am start -n <package>/<launcher activity> --es safety_sim
guiado|avaliacao [--es safety_sim_script <device path>]`. The launcher logs the
final status as a `[DeviceSimulationLauncher]` line.

The head pose is wherever the headset rests, so desk runs are comparable with each
other but not with a participant's run: a static view renders a different share of
the scene, and the GPU numbers move with it.

### Performance runs

- **Fix the view with a hold script.** A variant whose last steps repeat `move` to
  one target with long `delayMs` holds the camera there for minutes. `move` turns
  the rig to face the target, so turning the headset by hand only changes the view
  after the move, not before.
- **`-Extra key=value,...`** adds string intent extras to the launch, for example
  `XrPerformanceBootstrap`'s Development-only overrides `safety_perf_scale`,
  `safety_perf_viewport` and `safety_perf_foveation`. Separate pairs with commas
  inside one argument: `pwsh -File` hands the script a single string.
- **Measure on an LTO build.** Meta's build processor adds `-flto=thin` to every
  Release-type Android build, Development or not. `build-android.ps1 -Development`
  switches the build type to Development to skip it, which is about three times
  faster but costs about 5 fps of CPU time on a Quest 2. For numbers, build through
  the Editor with `unity command build --target Android --outputPath
  Builds/Android/SafetyTraining_dev.apk --options '["Development","AllowDebugging"]'
  --confirm`.
- **The simulator never wears PPE.** The helmet stays loose on its shelf and the
  lanyard is never clipped, so their strap and rope simulations run for the whole
  run, unlike in a participant's session.

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
