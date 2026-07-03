# Revision Evidence — SVR/JBCS 2026

> **Purpose.** Single source of truth for every quantitative claim the revised manuscript will
> report. Each metric below is accompanied by the exact command or a precisely stated method, so it
> can be re-run and audited.
>
> **Integrity rule.** No number here is estimated. Where a metric could not be computed reliably it
> is recorded as `NOT COMPUTED — <reason>`. Methods favor reproducible, transparent tooling (module
> reference graph, git, coverlet) over black-box analyzers.
>
> **Repo state.** Metrics were computed on branch `main`. Commands are written relative to the repo
> root (`safety-training-proto/`) and run under Git Bash on Windows unless noted. Where a number
> depends on the working tree, the commit is stated.

---

## T4 — Module coupling metrics

### Method (reproducible)

The project's own modules are its assembly-definition (`.asmdef`) assemblies on the Unity side and
its `.csproj` projects on the .NET side. The **inter-module reference graph** is read directly from
the `references` array of each `.asmdef` and the `ProjectReference` entries of each `.csproj`.
Third-party / engine assemblies (`Oculus.*`, `Unity.TextMeshPro`, `Newtonsoft.Json`, NUnit, the
.NET BCL) are **excluded** from the own-module counts; the notable third-party references are listed
separately below.

Per module:

- **Ce (efferent coupling)** = number of *own* modules this module references.
- **Ca (afferent coupling)** = number of *own* modules that reference this module.
- **Instability** `I = Ce / (Ca + Ce)`  (0 = maximally stable/depended-upon; 1 = maximally unstable).
- **Abstractness** `A = (interfaces + abstract classes) / total types`, counted per assembly source
  folder with:
  - abstract types: `grep -rhoE '\binterface\s+[A-Z]\w*' <dir>` + `grep -rhoE 'abstract\s+(partial\s+)?class\s+[A-Z]\w*' <dir>`
  - total types: `grep -rhoE '\b(class|struct|enum|interface|record)\s+[A-Z]\w*' <dir>`
  (A stated lexical heuristic — it counts type-declaration keywords in `.cs` files. See caveats.)
- **Distance from main sequence** `D = |A + I − 1|` (0 = on the main sequence).

Reference-graph inputs (verbatim from the `.asmdef` `references` arrays):

| Module (Unity) | references own modules |
|---|---|
| `SafetyProto.EventBus.Core` | — |
| `SafetyProto.Domain.Core` | EventBus.Core |
| `SafetyProto.Utils.Unity` | EventBus.Core, Domain.Core |
| `SafetyProto.Runtime.Unity` | EventBus.Core, Domain.Core, Utils.Unity |
| `SafetyProto.UI.Unity` | EventBus.Core, Domain.Core, Utils.Unity, Runtime.Unity |
| `SafetyProto.Networking.Unity` | EventBus.Core, Domain.Core, Utils.Unity, Runtime.Unity |
| `SafetyProto.Editor` | EventBus.Core, Domain.Core, Utils.Unity, Runtime.Unity |

Third-party references (excluded from counts, noted): `Runtime.Unity` → Oculus.Interaction,
Oculus.Interaction.OVR, Oculus.VR, Unity.TextMeshPro; `Utils.Unity` → Oculus.Interaction,
Oculus.Interaction.OVR; `UI.Unity` → Unity.TextMeshPro. `Domain.Core` sets `noEngineReferences:true`
(it does not even reference UnityEngine) — the machine-checkable form of the engine-independence claim.

### Unity assembly graph

| Module | Ce | Ca | I | Ifaces | Abstract | Types | A | D |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `EventBus.Core`     | 0 | 6 | **0.00** | 10 | 0 | 51 | 0.20 | 0.80 |
| `Domain.Core`       | 1 | 5 | **0.17** | 0  | 0 | 22 | 0.00 | 0.83 |
| `Utils.Unity`       | 2 | 4 | 0.33 | 0  | 0 | 9  | 0.00 | 0.67 |
| `Runtime.Unity`     | 3 | 3 | 0.50 | 0  | 0 | 51 | 0.00 | 0.50 |
| `UI.Unity`          | 4 | 0 | **1.00** | 0  | 0 | 25 | 0.00 | 0.00 |
| `Networking.Unity`  | 4 | 0 | **1.00** | 0  | 0 | 24 | 0.00 | 0.00 |
| `Editor`            | 4 | 0 | **1.00** | 0  | 0 | 12 | 0.00 | 0.00 |

### .NET (headless Tools) graph

Read from `ProjectReference` (own-module edges only; `Newtonsoft.Json` / NUnit excluded). The plan's
four own .NET modules, with `EventBusBench` and `SafetyProto.Tests` noted:

| Project | references own modules | Ce | Ca | I |
|---|---|---:|---:|---:|
| `SafetyProto.Shared`   | — | 0 | 3 | **0.00** |
| `CliHarness`           | SafetyProto.Shared | 1 | 0* | 1.00 |
| `AuthoringApp`         | SafetyProto.Shared | 1 | 0 | 1.00 |
| `AuthoringApp.Gui`     | SafetyProto.Shared | 1 | 0 | 1.00 |

`Ca` for `SafetyProto.Shared` counts CliHarness + AuthoringApp + AuthoringApp.Gui = 3 (within the
plan's own-module set). *Also referenced but outside that set: `EventBusBench` → CliHarness +
SafetyProto.Shared; `SafetyProto.Tests` → SafetyProto.Shared. Counting those, `Shared` Ca = 5 and
`CliHarness` Ca = 1. `SafetyProto.Shared` is the linked-source twin of `EventBus.Core` + `Domain.Core`
(it `<Compile Include>`s the same `.cs` files), so its I = 0.00 corresponds to those two Unity modules.

### Reading

The direction of dependency is unambiguous and matches the architectural claim: the abstraction/engine
hub (`EventBus.Core`, I = 0.00; `Domain.Core`, I = 0.17; and their .NET mirror `SafetyProto.Shared`,
I = 0.00) sits at the **stable, depended-upon** end, and every Unity host (`UI`, `Networking`,
`Editor`, all I = 1.00) sits at the **unstable, dependency** end. No cycles exist; references form a
strict layered DAG. `Domain.Core.noEngineReferences = true` makes engine-independence a build-enforced
invariant, not a convention.

The `A`/`D` column tells a subtler, honest story: abstraction is **concentrated** in `EventBus.Core`
(the interface hub, A = 0.20) rather than spread across modules, so the stable core modules read as
concrete-but-stable (high `D`), while the leaf Unity hosts are maximally-unstable-and-concrete and so
land *on* the main sequence (`D = 0`). For a codebase this size this is expected and not a defect
signal; see caveats.

### Caveats

- **Abstractness is a lexical heuristic.** The keyword grep counts every `class/struct/enum/interface/
  record` declaration in a module's folder, including nested and `[Serializable]` payload structs. It
  does not distinguish public API surface from internal helpers, and payload-struct-heavy modules
  (e.g. `EventBus.Core`'s `EventPayloads.cs`) inflate the "total types" denominator, pushing `A` down.
  Treat `A`/`D` as directional, not precise. `Ca`/`Ce`/`I` are exact (they come from the declared
  reference graph, not a heuristic).
- NDepend was intentionally not used (out of scope for this pass); the reference-graph method above is
  fully reproducible from the repository without it.

---

## T5 — Change-impact metrics (git)

All figures come from `git diff --shortstat` / `--stat` / `--dirstat` over the stated commit ranges.
Line counts are additions/deletions as git reports them (includes `.meta` and asset text).

### SO→JSON data-layer migration (Phases 1–6)

Range: **`fbcab4c^ .. 284e6a1`** — from just before the first phase (`fbcab4c` "unify scenario model
+ loader across Unity and CLI") through the deletion commit (`284e6a1` "remove legacy ScriptableObject
authoring"). Phase mapping is documented in `docs/refatoracao-sistema-dados.md`.

```
git diff --shortstat fbcab4c^ 284e6a1
  → 121 files changed, 2562 insertions(+), 1463 deletions(-)

git diff --shortstat fbcab4c^ 284e6a1 -- Assets/_SafetyProto/Scripts Tools   (code only)
  → 79 files changed, 2293 insertions(+), 885 deletions(-)
```

Net code delta ≈ **+1408 LoC** across 79 source files. `--dirstat` shows the change concentrated in
`Domain/Scenarios` (15.1%), `Runtime/Actions` (8.8%), `Domain/Actions` (7.5%), the new
`AuthoringApp.Gui` (10.0% across ViewModels+Views), and `Editor` (6.3%) — i.e. the new unified model,
its consumers, and the desktop authoring tool, exactly the surface the migration was scoped to.

### Meta XR SDK update (85.0.0 → 201.0.0)

Isolable to a single commit **`f2a68e4`** ("upgrade Meta XR SDK from 85.0.0 to 201.0.0 with Horizon OS
support"). (The submitted paper's "v76→v201" framing is superseded by the actual repo history: the
tracked upgrade is 85→201.)

```
git show --shortstat f2a68e4
  → 8 files changed, 668 insertions(+), 55 deletions(-)
```

The 8 files are **entirely configuration / scene wiring — zero C# scripts**:
`Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings/ProjectSettings.asset`,
`ProjectSettings/DynamicsManager.asset`, `Assets/XR/Settings/OpenXRPackageSettings.asset`,
`Assets/Plugins/Android/AndroidManifest.xml`, and the two scene files (`SafetyTraining.unity`,
`TestScene.unity`).

```
git show --stat f2a68e4 -- Assets/_SafetyProto/Scripts/Domain Assets/_SafetyProto/Scripts/Networking
  → (empty)   # zero Domain Core / Evaluator-dashboard files changed
```

**Confirmed: the SDK bump changed no gameplay, Domain, or Evaluation code** — the evidence for SDK
isolation. The scene-file deltas are Inspector/prefab rewiring, not logic.

### Add SessionLogger

Commit **`afbe400`** ("Part 4: extract session logging logic and add harness logger adapter") — the
extraction of session logging into the testable, engine-independent `SessionLoggerCore` plus its
adapter.

```
git show --shortstat afbe400
  → 7 files changed, 264 insertions(+), 172 deletions(-)
```

Modules touched: **`Core` (1 file) and `Utils` (SessionLogger, SessionLoggerCore, SafetyLogAdapter)** —
two modules. No gameplay producer (`TaskManager`, `SafetyRuleEngine`, PPE, scoring) was modified:
session logging integrates purely by **subscribing to existing EventBus events**, which is the
event-driven-decoupling claim made concrete. (An earlier noisier commit `9551469` first introduced
logging mixed with unrelated work — 12 files, +2824/−819 — and is not the representative measure.)

### Add / swap a scenario (now a JSON edit)

After the migration, runtime scenario data is 100% JSON (commit `82352a7`, "runtime 100% JSON — drop SO
dependency"), loaded via `Resources.Load` + `ScenarioLoader`. Swapping or adding a scenario is a single
JSON file edit — **0 C# files changed, 0 recompilation**.

```
wc -l on scenario files:
  Assets/_SafetyProto/Resources/Scenarios/default.json   157 lines
  Tools/CliHarness/scenarios/ppe_inspection.json         117 lines
  Tools/CliHarness/scenarios/ppe_equip.json               51 lines
```

A complete authored scenario is ~**120–160 lines of JSON**; authoring or swapping one recompiles
nothing (contrast with the pre-migration ScriptableObject flow, which required Editor asset edits and
domain reloads).
