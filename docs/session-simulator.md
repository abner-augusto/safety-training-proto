# Session Simulator

The Session Simulator is a semantic, no-headset replay tool for the Unity Editor.
It consumes the scenario `script` already used by the CLI
(`ppe`, `action`, and `gate` steps) and drives the real event facades and public
gate methods. It does not simulate hands, physics, locomotion, or a second task
engine.

## Use

1. Open `SafetyTraining` and enter Play Mode with `TrainingSessionManager.autoStartOnStart`
   disabled. In the Editor window, the onboarding controllers are disabled and their
   active popup is closed automatically before the first simulator command. The popup
   service remains active so gameplay feedback can still be tested.
2. Open `SafetyProto > Session Simulator`.
3. Leave the loaded scenario selected, or choose an external JSON such as
   `Tools/CliHarness/scenarios/eval_omission.json`. The external file must have
   the same groups/tasks/actions as the loaded `TaskManager` scenario.
4. Select `Guiado` or `Avaliação`. The embedded script always comes from the
   scenario already loaded by `TaskManager`; only the optional external JSON path
   changes the script source.
5. Use `Executar tudo` for automatic replay or `Próxima etapa` repeatedly for
   manual replay. `Executar tudo` can continue after manual steps.
6. Use `Cancelar` to stop the actor, then exit and restart Play Mode before a
   new simulation. The window shows task states, score,
   transcript, consequences, and timeout diagnostics.

`Executar tudo` and `Próxima etapa` are disabled after completion, failure, or
cancellation. Same-session retry is intentionally outside the MVP because
`TrainingSessionManager.BeginSession()` is idempotent.

Gate steps may set `gateTarget` to `phase1` or `inspection`. An empty target
keeps CLI compatibility and infers phase 1 for the `ppe_selection` group;
otherwise it targets inspection.

Every simulated participant receives a fresh `SIM-...` id. The simulator never
writes the private real-name mapping. This prefix is the MVP data-separation
contract.

## Coverage

The tool covers session start, PPE state events, action attempts, real phase and
inspection gates, task/group/session lifecycle, score changes, violations, and
synchronous consequences. Event waits are bounded and report diagnostics in
Portuguese.
Input dispatch waits are short; phase transitions, inspection consequences,
blackout, and terminal session settlement use a separate 45-second operation
timeout.

Simulation logs are written under `Application.persistentDataPath/simulations`.
They remain visible as live dashboard events but are excluded from dashboard
localStorage history.

Headset validation is still required for hand interaction, snapping and
calibration, physics and locomotion, visual/audio comfort, object animations,
and dashboard/device behavior. PlayMode coverage for repeated manual steps and
gate coroutine timing remains an editor-runtime check; pure scenario
compatibility and script-format tests run headlessly.
