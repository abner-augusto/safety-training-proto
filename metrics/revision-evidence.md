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
