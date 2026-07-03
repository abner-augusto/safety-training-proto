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

---

## T6 — Refreshed codebase indicators (Table 2)

Recomputed after the −1438/+2656 LoC churn that made the submitted paper's table stale. `LoC` =
physical lines (`wc -l`). All counts exclude the Unity Editor assembly (`Scripts/Editor/`); the test
assemblies live under `Scripts/../Tests/` and are likewise excluded from "source" counts.

| Indicator | Value | How computed (from repo root) |
|---|---:|---|
| Total source files | **126** | `find Assets/_SafetyProto/Scripts -name '*.cs' -not -path '*/Editor/*' \| wc -l` |
| Total source LoC | **15,320** | `find … -not -path '*/Editor/*' -print0 \| xargs -0 cat \| wc -l` |
| Engine-independent files | **42** | `grep -rLE 'UnityEngine' Scripts --include=*.cs \| grep -v /Editor/ \| wc -l` |
| Engine-independent LoC | **2,871** | `cat` of that file list `\| wc -l` |
| Domain Core files | **16** | `find Scripts/Domain -name '*.cs' \| wc -l` |
| Domain Core LoC | **2,058** | `find Scripts/Domain -name '*.cs' -print0 \| xargs -0 cat \| wc -l` |
| Files importing Meta XR SDK | **17** | `grep -rlE 'using Oculus\|using Meta\.\|OVR' Scripts --include=*.cs \| wc -l` |
| Shared-library files (CLI-linked) | **31** | `grep -cE '<Compile Include=' Tools/SafetyProto.Shared/SafetyProto.Shared.csproj` |
| Harness-specific LoC | **771** (8 files) | `cat Tools/CliHarness/*.cs \| wc -l` |
| Typed EventBus channels | **16** | `grep -cE 'public UnityEvent<' Scripts/Core/EventBus.cs` |
| Event payload types | **13** | 12 structs in `EventPayloads.cs` + `ActionAttemptedEvent` (separate file) |

Notes / relationships (all cross-validated):

- **Engine-independent (42) vs shared-library (31).** The 31 files the CLI harness compiles
  (`SafetyProto.Shared.csproj`) are a build-verified *subset* of the 42 files that contain no
  UnityEngine reference. The extra 11 (2871 − 2657 ≈ 214 LoC) are engine-independent event facades
  (`SessionEvents`, `TaskEvents`, …) not needed by the CLI. Both figures agree with the per-module
  split: Core 20/31, Domain 15/16, Runtime 4/42, UI 1/24, Networking 1/5, Utils 1/8 engine-independent.
- **Domain Core is fully engine-independent.** The one Domain file that textually matches "UnityEngine"
  (`SessionLoggerCore.cs`) does so only in a comment; it compiles in pure .NET via the shared project,
  and `Domain.Core.asmdef` sets `noEngineReferences:true`, so all 16 files are engine-independent by a
  build-enforced invariant.
- **Meta XR SDK reach = 17 files**, all under `Runtime`/`Utils`/scene-facing code — none in `Domain`
  (consistent with T5's finding that the 85→201 upgrade touched zero `Domain`/`Networking` code).
- **Typed channels: 16** strongly-typed `UnityEvent<T>` dispatch channels, paralleled by **15** static
  `Action<T>` C# events (`grep -cE 'public static event Action<' EventBus.cs`) for non-Unity subscribers
  — two typed façades over the same event set. **13 event payload types** flow across them.

---

## T2/T3 — Test suite

Counts from `grep -cE '\[Test\b|\[TestCase' Assets/_SafetyProto/Tests/Editor/*.cs`:

| | Tests | Notes |
|---|---:|---|
| Total test methods | **50** | across 8 fixtures |
| Integration (`SessionIntegrationTests`) | **8** | the T2 battery — covers the plan's 7 cases (case 7 = 2 tests: unknown-PPE + malformed-JSON) |
| Unit | **42** | `TaskManagerCore` (6), `SafetyRuleEngineCore` (8), `SafetyRuleEngineDiagnostic` (4), `ScoreRuleEngineCore` (13), `PPEManagerEventProtocol` (5), `SafetyPattern` (3), `SafetyTraining` (3) |
| **Run headless** (`dotnet test`) | **44** | 0 failed, ~0.12 s, no Unity |
| Unity-only (excluded from headless) | 6 | `SafetyPattern` (→ `Runtime.Analytics`), `SafetyTraining` (→ Unity `EventBus` singleton + facades) |

Headless run + coverage commands (recap of T3):

```
# Run the engine-independent suite without Unity:
dotnet test Tools/SafetyProto.Tests/SafetyProto.Tests.csproj
  → Passed: 44, Failed: 0

# Line + branch coverage of the engine-independent core [SafetyProto.Shared]:
dotnet test Tools/SafetyProto.Tests/SafetyProto.Tests.csproj \
  --collect:"XPlat Code Coverage" \
  --settings Tools/SafetyProto.Tests/coverlet.runsettings
  → Line 70.6% (801/1135), Branch 58.1% (280/482)
```

Per-class coverage of the core orchestration (cobertura, same run): `ScoreRuleEngineCore` 100% L /
93.3% B · `SafetyRuleEngineCore` 86.4% / 75.5% · `ScenarioLoader` 82.6% / 81.2% · `SessionLoggerCore`
89.8% / 60.7% · `TaskManagerCore` 77.2% / 63.0% · `ScoreService` 51.9% / 33.3%. The overall rate is
pulled down by authoring-only code compiled into the shared library but not exercised by these tests
(`ActionCatalog*`, `ActionDef`, `ScenarioValidator` = 0%).

**Drivers vs stubs** (Reviewer E). In every integration case the **driver** is the scripted test body
(via `SessionTestHarness.WearPpe/Attempt/ReplayScript`) — the in-process equivalent of the CLI
harness's `ScriptedActor`, i.e. it publishes the exact events a VR player generates. The **stubs** are
`FakeEventBus` (a real enqueue-then-drain bus standing in for Unity's deferred `EventBus` asset,
deterministic and Unity-free) and the PPE-sensor events themselves (standing in for
`PPEManager`/`PPEZone` trigger callbacks). Everything else — both rule engines, `TaskManagerCore`,
`ScoreService`, `SessionLoggerCore` — is **real, unmodified production code**. This is documented
in-source on `SessionTestHarness`.

Two production defects were found and fixed while building the battery (both now guarded by tests):

1. **Group timeout never ended the session** (`SessionEnded` undispatched) — fixed in T1; regression
   test = integration case 6.
2. **`ppePenalty` never applied end-to-end** — a PPE-missing task fired a violation but its penalty
   was never scored, because `ScoreRuleEngineCore` ignored `WasPpeCompliant` when `RuntimeTask` was
   null. Fixed (`ScoreRuleEngineCore` now honors the documented flag); guarded by integration case 2
   plus a unit test.

---

## Claim → evidence seed table

Each architectural claim the manuscript makes, mapped to the concrete, re-runnable evidence in this
artifact. (The writing track turns this into the paper's centralized evidence table.)

| Claim | Evidence |
|---|---|
| **Engine independence** (domain logic has no Unity dependency) | `Domain.Core.asmdef` `noEngineReferences:true` (build-enforced); 16 Domain files / 2,058 LoC compile in pure .NET via `SafetyProto.Shared`; module instability `Domain.Core` I=0.17, `EventBus.Core` I=0.00, .NET `SafetyProto.Shared` I=0.00 at the stable end (T4); the Meta XR 85→201 upgrade changed **0** Domain files (T5). |
| **SDK isolation** (engine/SDK churn doesn't reach domain/evaluation) | Meta XR 85→201 = commit `f2a68e4`, 8 files, config/scene only, **zero C#**, zero `Domain`/`Networking` (T5); the 17 Meta-XR-importing files are all in `Runtime`/`Utils`, none in `Domain` (T6). |
| **Testability outside Unity** | 44 tests run via `dotnet test` with no Unity Editor (T3); the same test `.cs` compile under both Unity and .NET (linked-source); full-stack `SessionIntegrationTests` drive the real domain through a real in-process bus (T2); coverage 70.6% line / 58.1% branch on the engine-independent core (T3). |
| **Zero-modification scenario authoring** | Runtime is 100% JSON (commit `82352a7`); a scenario is a ~120–160-line JSON file, **0 C# changed, 0 recompilation** (T5); loader validates bad input without throwing (integration case 7). |
| **Event-driven decoupling** | 16 typed EventBus channels + 13 payload types (T6); adding `SessionLogger` touched only `Core`+`Utils`, no gameplay producers, purely by subscribing (T5); strict layered DAG, no cycles (T4). |
| **Correct safety/scoring semantics** | Group-timeout terminality (T1 + case 6); PPE-missing penalty applied end-to-end (scoring fix + case 2); Sequential/FreeOrder/group-gating behavior (integration cases 3–5). |

---

## Not computed / limitations

- **Coverage scope is the engine-independent core only** (`[SafetyProto.Shared]`). The Unity
  MonoBehaviour adapter layer (Runtime/UI/Networking hosts) is **not** measured by `dotnet test`;
  covering it would require the Unity Test Runner in batchmode / PlayMode, out of scope for this pass.
  The 6 Unity-only tests are therefore excluded from the coverage figure.
- **Abstractness `A` / distance `D` are a lexical heuristic**, not a type-system analysis (see T4
  caveats). `Ca`/`Ce`/`I` are exact (declared reference graph).
- **Meta XR range** is reported as the repository's actual tracked upgrade **85.0.0 → 201.0.0**
  (commit `f2a68e4`); the submitted paper's "v76→v201" framing does not match repo history and is
  superseded here.
- **LoC = physical lines** (`wc -l`, includes braces/blank/comment lines), not logical SLOC; git
  ±figures include `.meta`/asset text as git counts them. Methods are stated so any stricter recount
  is reproducible.
- **Unity Editor test execution was not automated** in this pass (the Unity Test Runner via MCP did
  not surface async results reliably); the reproducible runner of record is headless `dotnet test`.
- Coverage `lines-valid=1135` counts only the instrumented `SafetyProto.Shared` production lines, not
  the linked test code (excluded via `coverlet.runsettings`).
