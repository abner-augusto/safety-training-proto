# Revision Evidence — current (`main`)

> **Scope.** Quantitative state of the codebase at `main`, recomputed from the
> repository. The figures the SVR/JBCS 2026 manuscript reports are frozen at tag
> `v1.0.0` in [`revision-evidence-1.0.0.md`](revision-evidence-1.0.0.md); this
> file tracks how the project has moved since. The historical change-impact
> ranges (T5 — SO→JSON migration, Meta XR bump, SessionLogger extraction) are
> tied to fixed commit ranges and are not restated here — read them in the frozen
> file.
>
> **Integrity rule.** No number here is estimated. Each is accompanied by the
> command that produces it. Methods favor reproducible tooling (the declared
> module reference graph, `git`, coverlet) over black-box analyzers.
>
> **Repo state.** Computed on branch `main` at commit `db2dc2a`. Commands are
> written relative to the repo root and run under Git Bash on Windows unless
> noted. `S=Assets/_SafetyProto/Scripts` in the command column.

---

## Delta since `v1.0.0`

| Indicator | `v1.0.0` | `main` (`db2dc2a`) |
|---|---:|---:|
| Total source files (excl. `Editor/`) | 120 | **145** |
| Total source LoC | 15,170 | **18,915** |
| Domain Core files | 16 | **23** |
| Domain Core LoC | 2,172 | **3,465** |
| Files without textual `UnityEngine` | 43 | **56** |
| LoC without textual `UnityEngine` | 3,305 | **4,899** |
| Files importing Meta XR SDK | 17 | **19** |
| Shared-library linked `.cs` | 31 | **42** |
| Headless tests (`dotnet test`) | 46 | **122** |
| Coverage — line / branch (`SafetyProto.Shared`) | 70.3% / 57.3% | **77.6% / 67.3%** |

The shape is unchanged; the codebase grew (evaluation mode, the risk model, gaze
interaction, dashboard command routing) and the headless test suite grew with it.

---

## T4 — Module coupling metrics

### Method

Identical to the frozen file: the inter-module reference graph is read directly
from the `references` array of each `.asmdef` and the `ProjectReference` entries
of each `.csproj`. Third-party / engine assemblies (`Oculus.*`,
`Unity.TextMeshPro`, `Unity.InputSystem`, `Newtonsoft.Json`, NUnit, the .NET BCL)
are excluded from own-module counts.

- **Ce** = own modules this module references.
- **Ca** = own modules that reference this module.
- **Instability** `I = Ce / (Ca + Ce)` (0 = stable/depended-upon; 1 = unstable).
- **Abstractness** `A = (interfaces + abstract classes) / total types`, lexical
  heuristic per source folder (`grep -rhoE '\binterface\s+[A-Z]\w*'` +
  `grep -rhoE 'abstract\s+(partial\s+)?class\s+[A-Z]\w*'` over
  `grep -rhoE '\b(class|struct|enum|interface|record)\s+[A-Z]\w*'`).
- **Distance** `D = |A + I − 1|`.

Reference-graph inputs (verbatim from the `.asmdef` `references` arrays):

| Module | references own modules |
|---|---|
| `SafetyProto.EventBus.Core` | — |
| `SafetyProto.Domain.Core` | EventBus.Core |
| `SafetyProto.Utils.Unity` | EventBus.Core, Domain.Core |
| `SafetyProto.Runtime.Unity` | EventBus.Core, Domain.Core, Utils.Unity |
| `SafetyProto.UI.Unity` | EventBus.Core, Domain.Core, Utils.Unity, Runtime.Unity |
| `SafetyProto.Networking.Unity` | EventBus.Core, Domain.Core, Utils.Unity, Runtime.Unity |
| `SafetyProto.Editor` | EventBus.Core, Domain.Core, Utils.Unity, Runtime.Unity, **UI.Unity** |

Third-party references (excluded, noted): `Runtime.Unity` → Oculus.Interaction,
Oculus.Interaction.OVR, Oculus.VR, Unity.TextMeshPro (+ a `USING_META_XR`
versionDefine on `com.meta.xr.sdk.all`); `Utils.Unity` → Oculus.Interaction,
Oculus.Interaction.OVR, Unity.InputSystem; `UI.Unity` → Unity.TextMeshPro.
`Domain.Core` sets `noEngineReferences:true` — the machine-checkable form of the
engine-independence claim.

### Unity assembly graph

| Module | Ce | Ca | I | Abstract | Types | A | D |
|---|---:|---:|---:|---:|---:|---:|---:|
| `EventBus.Core`     | 0 | 6 | **0.00** | 11 | 58 | 0.19 | 0.81 |
| `Domain.Core`       | 1 | 5 | **0.17** | 1  | 48 | 0.02 | 0.81 |
| `Utils.Unity`       | 2 | 4 | 0.33 | 0  | 9  | 0.00 | 0.67 |
| `Runtime.Unity`     | 3 | 3 | 0.50 | 2  | 66 | 0.03 | 0.47 |
| `UI.Unity`          | 4 | 1 | 0.80 | 0  | 25 | 0.00 | 0.20 |
| `Networking.Unity`  | 4 | 0 | **1.00** | 1  | 12 | 0.08 | 0.08 |
| `Editor`            | 5 | 0 | **1.00** | 0  | 15 | 0.00 | 0.00 |

Change from `v1.0.0`: `Editor` now also references `UI.Unity` (Ce 4 → 5), which
gives `UI.Unity` one afferent edge and moves it off I = 1.00 to **I = 0.80**. No
cycles; the graph is still a strict layered DAG.

### .NET (headless Tools) graph

Read from `ProjectReference` (own-module edges only):

| Project | references | Ce | Ca | I |
|---|---|---:|---:|---:|
| `SafetyProto.Shared`   | — | 0 | 3 | **0.00** |
| `CliHarness`           | SafetyProto.Shared | 1 | 0* | 1.00 |
| `AuthoringApp`         | SafetyProto.Shared | 1 | 0 | 1.00 |
| `AuthoringApp.Gui`     | SafetyProto.Shared | 1 | 0 | 1.00 |

`SafetyProto.Shared` Ca counts CliHarness + AuthoringApp + AuthoringApp.Gui = 3.
*Also referenced outside that set: `EventBusBench` → CliHarness + SafetyProto.Shared;
`SafetyProto.Tests` → SafetyProto.Shared. `SafetyProto.Shared` is the
linked-source twin of `EventBus.Core` + `Domain.Core` (it `<Compile Include>`s the
same `.cs` files), so its I = 0.00 corresponds to those two Unity modules.

### Reading

Unchanged in direction: the abstraction/engine hub (`EventBus.Core` I = 0.00,
`Domain.Core` I = 0.17, .NET mirror `SafetyProto.Shared` I = 0.00) sits at the
stable, depended-upon end; every Unity host (`Networking`, `Editor` I = 1.00,
`UI` I = 0.80) sits at the unstable end. `Domain.Core.noEngineReferences = true`
keeps engine-independence a build-enforced invariant.

The lexical `A`/`D` columns tell the same honest story as before: abstraction is
concentrated in `EventBus.Core` (A = 0.19, the interface + payload hub) and the
larger `Domain.Core` type set (48 types, one abstract) reads as concrete-but-stable.

### Caveats

- **Abstractness is a lexical heuristic** — the keyword grep counts every
  `class/struct/enum/interface/record` declaration in a module's folder, including
  nested and `[Serializable]` payload structs, which inflates the denominator.
  Treat `A`/`D` as directional. `Ca`/`Ce`/`I` are exact.
- NDepend was not used; the reference-graph method is fully reproducible from the
  repository without it.

---

## T6 — Codebase indicators

`LoC` = physical lines (`wc -l`). All counts exclude the Unity Editor assembly
(`Scripts/Editor/`); the test assemblies (`Tests/`) are likewise excluded from
"source" counts.

| Indicator | Value | How computed (from repo root) |
|---|---:|---|
| Total source files | **145** | `find $S -name '*.cs' -not -path '*/Editor/*' \| wc -l` |
| Total source LoC | **18,915** | `find … -not -path '*/Editor/*' -print0 \| xargs -0 cat \| wc -l` |
| Files without textual `UnityEngine` reference | **56** | `grep -rLE 'UnityEngine' $S --include=*.cs \| grep -v /Editor/ \| wc -l` |
| LoC without textual `UnityEngine` reference | **4,899** | `cat` of that file list `\| wc -l` |
| Domain Core files | **23** | `find $S/Domain -name '*.cs' \| wc -l` |
| Domain Core LoC | **3,465** | `find $S/Domain -name '*.cs' -print0 \| xargs -0 cat \| wc -l` |
| Files importing Meta XR SDK | **19** | `grep -rlE 'using Oculus\|using Meta\.\|OVR' $S --include=*.cs \| wc -l` |
| Shared-library files (CLI-linked) | **42** | `grep -cE '<Compile Include=' Tools/SafetyProto.Shared/SafetyProto.Shared.csproj` |
| Harness-specific LoC | **766** (8 files) | `cat Tools/CliHarness/*.cs \| wc -l` |
| Typed EventBus channels | **15** | `grep -cE 'public UnityEvent<' $S/Core/EventBus.cs` |
| Event payload types | **13** | 12 `*EventArgs` structs in `EventPayloads.cs` + `ActionAttemptedEvent` (separate file) |

Notes:

- **Domain Core grew 16 → 23 files / 2,172 → 3,465 LoC.** The additions are the
  risk model (`RiskLevel`, risk-level scoring), evaluation-mode orchestration
  (phase-advance gate, inspection gate), and session-log summary types — all pure
  C#, all linked into `SafetyProto.Shared`.
- **Lexical no-`UnityEngine` set (56) vs shared-library (42).** The 42 linked
  files are build-verified by `SafetyProto.Shared.csproj`; the 56-file count is a
  textual negative match only and is reported as a lexical indicator.
- **Meta XR SDK reach = 19 files**, all under `Runtime`/`Utils`/scene-facing code
  — none in `Domain`.

---

## T2/T3 — Test suite

Fixtures live in `Assets/_SafetyProto/Tests/Editor/*.cs` (24 files). The
`SafetyProto.Tests.csproj` links the 15 engine-independent fixtures via
`<Compile Include>` and runs them with `dotnet test`, no Unity Editor. The other
9 are Unity-coupled (UI, head-gaze, menu-follow, player-recenter, dashboard
command routing, the analytics pattern detector, the scene smoke tests) and run
only in the Unity Test Runner.

| | Value | Notes |
|---|---:|---|
| Fixture files | **24** | `ls Assets/_SafetyProto/Tests/Editor/*Tests.cs \| wc -l` |
| Headless fixtures (linked) | **15** | `grep -cE 'Editor\\\\\w+Tests\.cs' Tools/SafetyProto.Tests/SafetyProto.Tests.csproj` |
| **Headless tests run** (`dotnet test`) | **122** | 0 failed, ~0.27 s, no Unity |
| Integration (`SessionIntegrationTests`) | **8** | `dotnet test --filter FullyQualifiedName~SessionIntegrationTests` |

```
dotnet test Tools/SafetyProto.Tests/SafetyProto.Tests.csproj
  → Passed: 122, Failed: 0

dotnet test Tools/SafetyProto.Tests/SafetyProto.Tests.csproj \
  --collect:"XPlat Code Coverage" \
  --settings Tools/SafetyProto.Tests/coverlet.runsettings
  → Line 77.6% (1466/1890), Branch 67.3% (607/902)   [assembly: SafetyProto.Shared]
```

Headless coverage of the engine-independent core by concern: rule engine
(`SafetyRuleEngineCoreTests`, `SafetyRuleEngineDiagnosticTests`), scoring
(`ScoreRuleEngineCoreTests`), the risk model (`RiskAssessmentTests`), session
orchestration (`TaskManagerCoreTests`, `TaskExecutionRulesTests`,
`SessionIntegrationTests`, `SessionLogSummaryTests`), PPE protocol
(`PPEManagerEventProtocolTests`), scenario/action data (`ScenarioCompatibilityTests`,
`ScriptStepDefTests`), and dashboard DTO/relay (`DashboardDtoMapperTests`,
`DashboardEventRelayTests`, `OutgoingMessageBufferTests`, `EventMetadataTests`).

The Unity MonoBehaviour adapter layer (Runtime/UI/Networking hosts) is not
measured by `dotnet test`; the 9 Unity-only fixtures are excluded from the
coverage figure.

---

## Scenario scores

Re-verified at `main` by running the CLI harness on each shipped scenario:

```
dotnet run --project Tools/CliHarness -- Tools/CliHarness/scenarios/ppe_equip.json
  → Session summary: 5/5 tasks, score 650

dotnet run --project Tools/CliHarness -- Assets/_SafetyProto/Resources/Scenarios/default.json
  → Session summary: 9/9 tasks, score 1500
```

**These totals changed after `v1.0.0`** (which produced 750 and 1400) because the
scoring economy was retuned onto the NR-01 GRO risk matrix — task value is now
derived from `risk` (`severity × probability` → risk level) and looked up in the
scenario `scoring` block, rather than the flat per-task points used at `v1.0.0`.
The per-task *behavior* is unchanged; the weights are not. Any manuscript figure
must cite `v1.0.0` (750 / 1400) via `revision-evidence-1.0.0.md`, not these.

The separate Unity-on-device total (`1300`, 8/9 interactive tasks — the
`flag_safety_net` collective-protection check is not yet wired as an interaction)
is a host difference, not a scoring difference.

---

## Not computed / limitations

- **Coverage scope is the engine-independent core only** (`[SafetyProto.Shared]`).
  Covering the Unity adapter layer would need the Unity Test Runner in
  batchmode/PlayMode, out of scope here.
- **Abstractness `A` / distance `D` are a lexical heuristic**, not a type-system
  analysis (see T4 caveats). `Ca`/`Ce`/`I` are exact.
- **LoC = physical lines** (`wc -l`), not logical SLOC.
- **Historical change-impact (T5)** is not recomputed here — it is bound to fixed
  commit ranges in [`revision-evidence-1.0.0.md`](revision-evidence-1.0.0.md).
