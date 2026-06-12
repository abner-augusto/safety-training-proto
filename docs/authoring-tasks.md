# Authoring tasks with ScriptableObjects

This guide explains how to create training content — actions, tasks, and task
groups — using the project's ScriptableObject assets. No code changes are needed
for most new content.

Assets live under `Assets/_SafetyProto/ScriptableObjects/`:

```
Actions/     ActionTypeSO  — one per discrete action a player can perform
Tasks/       SafetyTask    — one objective the player must complete
TaskGroups/  TaskGroup     — an ordered/unordered set of tasks
```

The runtime action lookup table is `Assets/_SafetyProto/Resources/ActionRegistry.asset`.

---

## The two kinds of task

A `SafetyTask` completes in one of two ways. Which one you get is decided by
**whether the task has an `expectedAction`**:

| | **Action task** | **Equip-set task** |
|---|---|---|
| `expectedAction` | set (an `ActionTypeSO`) | **empty / None** |
| `requiredPPE` | optional — *compliance* prerequisites | **the items to equip** |
| Completes when | a matching `ActionAttemptedEvent` is raised **and** `requiredPPE` are worn | **all** `requiredPPE` are worn, in **any order** |
| Driven by | `ActionEmitter` / `ActionTrigger` / `DrillUse` → action event | `PPEManager` → `PPEStateChanged` |
| Use for | doing something (connect lanyard, install guardrail, press a button) | putting on PPE |

The discriminator is implemented in `SafetyRuleEngineCore` (`IsEquipTask` =
no resolved action id + non-empty `requiredPPE`).

### When to use which

- **Putting on PPE?** Use an **equip-set task**. The PPE slot already reports
  state, so no action is needed, and members can be equipped in any order
  (e.g. left/right gloves as one "wear gloves" task).
- **Performing a discrete action?** Use an **action task** and list any PPE that
  must already be worn in `requiredPPE` (compliance check).

---

## SafetyTask fields

Create one via **Assets → Create → VRSafetyTraining → SafetyTask**.

| Field | Meaning |
|---|---|
| `taskName` | Display name (shown in HUD / reports). Keep player-facing strings in Portuguese. |
| `taskDescription` | Longer description. |
| `expectedAction` | The `ActionTypeSO` this task waits for. **Leave empty for an equip-set task.** |
| `expectedActionId` | Auto-filled from `expectedAction` on validate; used as a fallback id. Leave blank for equip-set tasks. |
| `successPoints` | Score awarded on success. |
| `failurePenalty` | Score lost on a wrong action / timeout. |
| `ppePenalty` | Extra penalty if `requiredPPE` is missing when an **action task** completes (→ `CompletedSuccessButUnsafe`). |
| `requiredPPE` | List of `PPEType`. For an **action task** these are prerequisites; for an **equip-set task** these are the items that complete it. |
| `hintText` | Shown on timeout / wrong-order nudge. |
| `failureAdvice` | Shown in the post-session report on failure. |
| `ppeAdvice` | Shown when a task completes unsafe (missing PPE). |

### PPEType values

`None=0`, `Helmet=1`, `Goggles=3`, `Harness=4`, `Vest=5`, `Boots=6`,
`GloveLeft=7`, `GloveRight=8`. **Ordinal 2 is intentionally skipped** (legacy
`Gloves`, now split into `GloveLeft`/`GloveRight`).

---

## TaskGroup fields

Create one via **Assets → Create → VRSafetyTraining → TaskGroup**.

| Field | Meaning |
|---|---|
| `groupName` | Display name. |
| `executionMode` | `Sequential` (tasks in order; acting out of order = violation) or `FreeOrder` (any order). |
| `timeLimit` | Optional seconds for the whole group. |
| `tasks` | Ordered list of `SafetyTask` references. |
| `requiredGroups` | Other `TaskGroup`s that must finish before this one starts. |

Add the group to the `TaskManager` component's `taskGroups` list in the
`SafetyTraining` scene to make it run.

---

## Equip-set tasks in depth

An equip-set task completes when every item in its `requiredPPE` is worn. To get
order-independence *and* keep a sensible donning progression in a **Sequential**
group, make `requiredPPE` **cumulative, including the item itself**:

```
boots    → [Boots]
gloves   → [Boots, GloveLeft, GloveRight]
goggles  → [Boots, GloveLeft, GloveRight, Goggles]
helmet   → [Boots, GloveLeft, GloveRight, Goggles, Helmet]
harness  → [Boots, GloveLeft, GloveRight, Goggles, Helmet, Harness]
```

Why cumulative:

- **Completion gating** — the helmet task can't complete until everything before
  it is also on, so progression is preserved even though pickup order is free.
- **Order detection** — the engine derives each item's "owning step" as the first
  task whose `requiredPPE` *introduces* that type. `TaskManagerCore
  .IsPpeAheadOfCurrentStep` uses this to detect equipping ahead of the current
  step.

### Order guard

In a Sequential group, snapping a PPE item that belongs to a **later** step is
**rejected** by `PPESnapSlot` (it won't snap, hide, or count) and fires
`onWrongOrderSnapAttempted` → an "Ordem Incorreta" popup. Items in the **current**
step (e.g. both gloves) snap in any order; re-equipping a current/prior item is
allowed. FreeOrder groups don't gate order.

### Wiring the PPE slot

For each PPE item type the task expects, the body-rig `PPESnapSlot` needs a
`ppeActionMappings` entry with the matching `ppeType`. For equip-set tasks set
that entry's **`action` to None** — the `ppeType` still filters what the slot
accepts, but no `ActionAttemptedEvent` is emitted (emitting one would raise a
`WRONG_ACTION` violation against the no-action task). The slot reports wear state
through `PPEManager` automatically.

The PPE prefab's `PPEItem` component carries:

- `ppeType` — which slot/type it satisfies.
- `hideWhenEquipped` — hide the item mesh once worn (gloves, goggles).
- `isDistractor` — a decoy; snapping it is rejected and fires
  `onDistractorSnapAttempted` (wrong-equipment popup).

---

## Worked example — the PPE selection group

`TaskGroup_PPESelection` (Sequential) → `Task_equip_boots`, `Task_equip_gloves`,
`Task_equip_goggles`, `Task_equip_helmet`, `Task_equip_harness`. All are
equip-set tasks (no action) with cumulative `requiredPPE` as above. The body-rig
slots (`[PPESlot(foot)]`, `[PPESlot(LeftHand)]`, `[PPESlot(RightHand)]`,
`[PPESlot(Head)]` (helmet+goggles), `[PPESlot(Chest)]`) map the PPE types with
`action = None`.

`TaskGroup_HeightInspection` (FreeOrder, requires the PPE group) uses **action
tasks**: `connect_harness`, `install_guardrail`, `install_toeboard`,
`flag_safety_net`, each listing the PPE that must already be worn in
`requiredPPE`.

---

## Adding an action task (checklist)

1. **Action** — create an `ActionTypeSO` via
   **Assets → Create → SafetyProto → Actions → ActionType**, give it a unique
   `ActionId`, and add it to `ActionRegistry.asset` (`actions` list).
2. **Task** — create a `SafetyTask`, set `expectedAction` to that asset, set
   scoring, and list any `requiredPPE` prerequisites.
3. **Group** — add the task to a `TaskGroup`.
4. **Emit** — make sure something raises the action: an `ActionEmitter` /
   `ActionTrigger` on the interactable, `DrillUse`, etc.

## Adding an equip-set (PPE) task (checklist)

1. **Task** — create a `SafetyTask`, **leave `expectedAction` empty**, set
   `requiredPPE` to the cumulative set (including the new item). No
   `ActionRegistry` entry is needed.
2. **Group** — add it to the PPE `TaskGroup` in the intended order.
3. **Slot** — ensure the body-rig `PPESnapSlot` has a `ppeActionMappings` entry
   for each expected `ppeType` with `action = None`.
4. **Item** — the PPE prefab's `PPEItem.ppeType` must match; set
   `hideWhenEquipped` as desired.

---

## Testing without the headset

The `.NET` CLI harness drives the same engine from JSON scenarios. See
`Tools/CliHarness/scenarios/` — `ppe_equip.json` shows an equip-set group (PPE
state only) and `ppe_inspection.json` shows equip-set + action tasks combined.

```bash
dotnet run --project Tools/CliHarness -- Tools/CliHarness/scenarios/ppe_equip.json
```

Scenario tasks mirror the SO fields: omit `actionId` for an equip-set task and
list its cumulative `requiredPPE`; the `script` drives `ppe`/`action` events.
