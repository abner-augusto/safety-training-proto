# Authoring Tasks With JSON

Training content now lives in JSON, not ScriptableObjects. Runtime loads:

- Scenario data from `Assets/_SafetyProto/Resources/Scenarios/default.json`, with optional override at `Application.persistentDataPath/scenarios/default.json`.
- Action metadata from `Assets/_SafetyProto/Resources/Actions/actions.json`.
- Capability options for the desktop GUI from `Tools/AuthoringApp/capability_catalog.json`.

Use `Tools/AuthoringApp.Gui` to create/edit scenarios. The GUI edits groups and tasks, chooses `actionId` from the capability catalog dropdown, validates via shared `ScenarioLoader`/`ScenarioValidator`, saves JSON, and deploys the override to the headset via `adb push`.

Open it from the repo root:

```bash
dotnet run --project Tools/AuthoringApp.Gui
```

Or build a standalone executable, so the editor can be handed to someone without a .NET SDK:

```bash
dotnet publish Tools/AuthoringApp.Gui -c Release -r win-x64 --self-contained -o dist/AuthoringApp
# → dist/AuthoringApp/SafetyProto.AuthoringApp.Gui.exe
```

The GUI is an Avalonia desktop app targeting `net10.0`. It is not included in `SafetyProto.sln`, so `dotnet build SafetyProto.sln` does not build it — always launch or publish it by project path.

The GUI can assign existing `actionId`s to tasks. It does not create brand-new logical actions in `actions.json`; add new implemented actions to the action catalog/build first, then refresh the capability catalog.

## The Two Kinds Of Task

A task completes in one of two ways. The discriminator is whether `actionId` is empty:

| | Action task | Equip-set task |
|---|---|---|
| `actionId` | Existing action id, e.g. `connect_harness` | Empty string |
| `requiredPPE` | Compliance prerequisites | The items to equip |
| Completes when | Matching `ActionAttemptedEvent` is raised and required PPE are worn | All `requiredPPE` are worn, in any order |
| Driven by | `ActionEmitter`, `ActionTrigger`, `DrillUse`, `ScaffoldPieceInstaller`, `RetractableLanyardController` | `PPEManager` via `PPEStateChanged` |

## Scenario Fields

Scenario JSON shape:

```json
{
  "name": "default",
  "participantId": "P000",
  "groups": []
}
```

Group fields:

- `groupName`: display/id string.
- `executionMode`: `Sequential` or `FreeOrder`.
- `timeLimit`: seconds for the group.
- `tasks`: ordered list of task objects.
- `requiredGroups`: names of groups that must complete first.
- `prerequisiteTaskId` (optional): id of a task in this group that is the safety precondition for all the others. While it is pending, any sibling attempt is refused instead of completed and raises the `PREREQUISITE_PENDING` violation; the refused task stays pending and free order is preserved. Guided mode only — Evaluation lets the omission happen so the inspection gate can score it. Loading fails if the id matches no task in the group.
- `prerequisiteAdvice` (optional): Portuguese text shown to the participant on that refusal. Falls back to a generic message naming the pending task.

Task fields:

- `taskName`: HUD/report display name. Keep player-facing text in Portuguese.
- `taskDescription`: longer description.
- `actionId`: existing action id, or empty for equip-set tasks.
- `risk`: the occupational risk assessment, `{ "severity": 1-5, "probability": 1-5 }`. Severity is the magnitude of the worst possible consequence (NR-01 1.5.4.4.4); probability is the chance of the injury given exposure and the effectiveness of the measures in place (1.5.4.4.5.4) — not the chance a worker skips the item. The risk level is derived from the product, so re-tuning the bands reclassifies every task without re-authoring.
- `requiredPPE`: PPE names, e.g. `Boots`, `GloveLeft`.
- `hintText`, `failureAdvice`, `ppeAdvice`: guidance/report copy.

Points and penalties are not task fields: they come from the scenario's `scoring` block, keyed by the derived risk level.

Risk index bands (`severity × probability`, 1–25):

| Level | Index | Decision it implies (NR-01 1.5.4.4.3) |
|---|---|---|
| `trivial` | 1–4 | Keep the existing measure |
| `tolerable` | 5–9 | Monitor |
| `moderate` | 10–15 | Preventive measure with a set deadline |
| `substantial` | 16–22 | Measure before starting or continuing the activity |
| `intolerable` | 23–25 | Activity cannot happen until the measure is in place |

`substantial` and above is the eliminatory threshold: failing, omitting or completing such a task without PPE caps the medal.

Scenarios authored before the risk matrix carry a flat `"severity": "critical" | "moderate" | "minor"` string instead. Those still load, mapping to `substantial` / `moderate` / `tolerable`, and their `scoring` block may still use the flat `criticalPoints` / `minorPenalty` / … keys. New content should use `risk` and the `levels` block.

Scoring block:

```json
"scoring": {
  "levels": {
    "trivial":     { "points": 50,  "penalty": 20,  "unsafeFactor": 0.8 },
    "tolerable":   { "points": 100, "penalty": 30,  "unsafeFactor": 0.7 },
    "moderate":    { "points": 150, "penalty": 50,  "unsafeFactor": 0.5 },
    "substantial": { "points": 200, "penalty": 100, "unsafeFactor": 0.0 },
    "intolerable": { "points": 250, "penalty": 150, "unsafeFactor": 0.0 }
  },
  "gateReductionFactor": 0.5
}
```

`unsafeFactor` is the fraction of `points` still earned when the task completes without its required PPE. `gateReductionFactor` scales `penalty` into the charge applied per pending task at a failed inspection-gate press. Any level left out of `levels` falls back to the values above.

`PPEType` values: `None=0`, `Helmet=1`, `Goggles=3`, `Harness=4`, `Vest=5`, `Boots=6`, `GloveLeft=7`, `GloveRight=8`. Ordinal `2` is intentionally skipped for legacy serialized compatibility.

## Equip-Set Tasks

An equip-set task completes when every item in its `requiredPPE` is worn. In a sequential PPE group, keep requirements cumulative:

```text
boots    -> [Boots]
gloves   -> [Boots, GloveLeft, GloveRight]
goggles  -> [Boots, GloveLeft, GloveRight, Goggles]
helmet   -> [Boots, GloveLeft, GloveRight, Goggles, Helmet]
harness  -> [Boots, GloveLeft, GloveRight, Goggles, Helmet, Harness]
```

Why cumulative:

- Completion gating preserves the donning progression.
- `TaskManagerCore.IsPpeAheadOfCurrentStep` can reject items from later steps.

For PPE equip-set tasks, `PPESnapSlot.ppeActionMappings` should keep `actionId` empty. The mapping still filters accepted `ppeType`s, but does not emit an `ActionAttemptedEvent`; emitting one for a no-action task would be treated as a wrong action.

## Adding Content

Action task:

1. Make sure the intended action exists in `Resources/Actions/actions.json` and in the capability catalog.
2. In the GUI, create a task and choose that `actionId`.
3. Add required PPE prerequisites if the task must enforce compliance.
4. Wire a scene emitter/component with the same action string, such as `ActionEmitter.actionId`, `ScaffoldPieceInstaller.actionId`, or `RetractableLanyardController.connectActionId`.

Equip-set task:

1. In the GUI, create a task and choose the no-action/equip-set option.
2. Select the cumulative `requiredPPE` set.
3. Ensure the body-rig `PPESnapSlot` accepts those `ppeType`s and leaves mapped `actionId` empty.
4. Ensure each PPE prefab's `PPEItem.ppeType` matches; set `hideWhenEquipped` as needed.

## Testing Without The Headset

The `.NET` CLI harness drives the same engine from JSON scenarios:

```bash
dotnet run --project Tools/CliHarness -- Tools/CliHarness/scenarios/ppe_equip.json
```

`Tools/CliHarness/scenarios/ppe_equip.json` shows a focused equip-set group. The canonical `Assets/_SafetyProto/Resources/Scenarios/default.json` combines equip-set and action tasks and includes the scripted playthrough used by the CLI harness.
