# Authoring Tasks With JSON

Training content now lives in JSON, not ScriptableObjects. Runtime loads:

- Scenario data from `Assets/_SafetyProto/Resources/Scenarios/default.json`, with optional override at `Application.persistentDataPath/scenarios/default.json`.
- Action metadata from `Assets/_SafetyProto/Resources/Actions/actions.json`.
- Capability options for the desktop GUI from `Tools/AuthoringApp/capability_catalog.json`.

Use `Tools/AuthoringApp.Gui` to create/edit scenarios. The GUI edits groups and tasks, chooses `actionId` from the capability catalog dropdown, validates via shared `ScenarioLoader`/`ScenarioValidator`, saves JSON, and deploys the override to the headset via `adb push`.

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

Task fields:

- `taskName`: HUD/report display name. Keep player-facing text in Portuguese.
- `taskDescription`: longer description.
- `actionId`: existing action id, or empty for equip-set tasks.
- `successPoints`, `failurePenalty`, `ppePenalty`: scoring.
- `requiredPPE`: PPE names, e.g. `Boots`, `GloveLeft`.
- `hintText`, `failureAdvice`, `ppeAdvice`: guidance/report copy.

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

`Tools/CliHarness/scenarios/ppe_equip.json` shows an equip-set group. `ppe_inspection.json` combines equip-set and action tasks.
