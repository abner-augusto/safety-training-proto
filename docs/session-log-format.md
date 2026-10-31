# Session log format

`SessionLoggerCore` writes one `session_log_<start>-<id>.json` per session. The
Unity build and the CLI harness produce the same format. The file holds a list
of `entries` plus a `summary`.

If a session ends without `SessionCompleted` (reset or abandoned), the summary
is synthesized from the events logged so far and has `completed: false`.

## Participant and restarts

`summary.participantId` is the code the session ran under. From the build after
1.2.14 the evaluator sets it on the dashboard; `summary.participantSource` says
who did:

| Token | Meaning |
| --- | --- |
| `dashboard` | the evaluator, through `start_session` |
| `keypad` | typed in VR — the fallback when no dashboard connected, or the development popup |
| `system_keyboard` | a first name typed in VR (development popup); the id is random |
| `skipped` | "Pular" in the development popup; the id is random |
| `simulator` | the session simulator (`SIM-` ids) |

`DEMO-NN` ids are demonstrations, never study data. A reset of an interrupted
session restarts it with the same code and mode: the new log has
`previousSessionId` = the interrupted session and `attempt` = its attempt + 1.
When the evaluator starts another code over an interrupted session (a mistyped
code corrected, or a different participant), `previousSessionId` still links
the replaced session but `attempt` starts at 1, since attempts count the tries
under one code. A session started after a completed one has `attempt` 1 and no
`previousSessionId`. Older logs have neither field (`attempt` 0).

## `LogEntry.data` fields

Each entry carries a `data` object (`LogData`). Fields are reused across events
to keep it small, and fields the event does not use are omitted from the JSON.
`LogEntry.details` holds the human-readable sentence.

| Event | Fields filled |
| --- | --- |
| `ActionAttempt` | `actionId` (stable id; the friendly name is in `details`), `context` |
| `PpeStateChanged` | `ppeType`, `wearing` |
| `ScoreChanged` | `delta`, `totalScore` |
| `Task*` events | `riskLevel`, `riskSeverity`, `riskProbability` |
| `SafetyViolation` | `violationCode`, `message`, `taskId`, `groupId` |
| `SafetyError` | `source`, `errorDetails`, `message` |
| `CriticalSafetyFailure` | `reason`, `violationCount`, `windowSeconds` |
| `SessionCompleted` | `totalScore`, `totalElapsedTime`, `tasksCompleted`, `totalTasks` |
| `SessionPaused` | `pauseSource` (the hold that started the pause), `pauseSources` (every hold active then), `context = pre_session` when the hold began before `SessionStarted` |
| `SessionResumed` | `pauseSource` (the release that ended the pause), `durationSeconds` |
| `SessionStarted` | `source` (who set the participant id), `previousSessionId` (the interrupted session this one restarts), `attempt`, `handCollision` (`push` or `pass_through`: whether the hands pushed interactive objects in this session) |
| `EvaluatorCommand` | `command` (`recenter_player`, `calibrate_room_center`, `return_all_ppe`, `highlight_target`, `set_hand_collision`), `message` (the handler's result summary), `target` (`highlight_target`: the target id, e.g. `ppe:Safety_Helmet`, `socket:GuardRailPiece`; `set_hand_collision`: the mode chosen, `push` or `pass_through`) |
| `PauseHoldChanged` | `pauseSource`, `held` (omitted when false), `pauseSources` (active after the change; omitted when none) |
| `ObjectGrabbed` | `target` (scene name of the object a hand took hold of) |
| `HintShown` | `level` (1 = the next object pulses, 2 = the step is said aloud, 3 = the evaluator is alerted), `taskId`, `durationSeconds` (seconds without progress when it fired). Guided mode only |
| `ObjectReleased` | `target`, `durationSeconds` (how long it was held), `secured` (`true` when the release left it worn, installed or anchored; omitted for a drop) |

Notes for analysis:

- Grab attempts per object: count `ObjectGrabbed` by `target`. An
  `ObjectReleased` without `secured` shortly after its grab is a drop; a long
  hold that ends without `secured` usually means the object was put down to
  free the hands. Both pairs include the evaluator-free automatic returns, which
  never raise a grab.

- `context` is free-form: `gaze_dwell`, or `gaze_dwell:option=loose_fixing` for
  a hazard-classification report.
- `Task*` entries embed the risk the task carried, so a log can be analysed on
  its own without joining it back to the scenario document. `riskLevel` is the
  NR-01 class token (for example `substantial`). `riskSeverity` and
  `riskProbability` are the 1-5 grades behind it, and are 0 when the scenario
  declared a level directly.
- `message` is shared by `SafetyViolation` and `SafetyError`; `totalScore` is
  shared by `ScoreChanged` and `SessionCompleted`.

## Pauses

The session is paused while any source holds it, and one `SessionPaused` /
`SessionResumed` pair brackets each pause however many sources overlap.
`PauseHoldChanged` records every individual hold and release, so the overlap
can be rebuilt. `pauseSource` tokens:

| Token | Meaning |
| --- | --- |
| `Popup` | a popup was open |
| `PauseMenu` | the in-VR pause menu was open |
| `XrSessionUnfocused` | the OpenXR session lost focus while frames kept running: Meta system menu, guardian, or the headset taken off and not yet asleep |
| `ApplicationPause` | the Android activity paused: the headset went to sleep (usually taken off for some seconds) |
| `ApplicationFocus` | the Android window lost focus |
| `Unspecified` | not produced by the app; only an event built by hand |

Reading them: `XrSessionUnfocused` alone, for a few seconds, is most likely the
system menu; `XrSessionUnfocused` followed by `ApplicationPause` is a headset
taken off long enough to sleep. The app cannot tell a short removal from the
menu (proximity sensing is not used). Logs up to 1.2.14 (the 26–28/10
collection) carry no pause data at all.
