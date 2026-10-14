# Session log format

`SessionLoggerCore` writes one `session_log_<start>-<id>.json` per session. The
Unity build and the CLI harness produce the same format. The file holds a list
of `entries` plus a `summary`.

If a session ends without `SessionCompleted` (reset or abandoned), the summary
is synthesized from the events logged so far and has `completed: false`.

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

Notes for analysis:

- `context` is free-form: `gaze_dwell`, or `gaze_dwell:option=loose_fixing` for
  a hazard-classification report.
- `Task*` entries embed the risk the task carried, so a log can be analysed on
  its own without joining it back to the scenario document. `riskLevel` is the
  NR-01 class token (for example `substantial`). `riskSeverity` and
  `riskProbability` are the 1-5 grades behind it, and are 0 when the scenario
  declared a level directly.
- `message` is shared by `SafetyViolation` and `SafetyError`; `totalScore` is
  shared by `ScoreChanged` and `SessionCompleted`.
