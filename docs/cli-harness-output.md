# CliHarness Sample Output

Reference transcripts from the .NET CLI harness, captured at `main` (`db2dc2a`).

**Runtime:** .NET 10.0.400, Release configuration
**Command:** `dotnet run --project Tools/CliHarness --configuration Release -- <scenario.json>`

Regenerate with:

```bash
dotnet run --project Tools/CliHarness -- Tools/CliHarness/scenarios/ppe_equip.json
dotnet run --project Tools/CliHarness -- Assets/_SafetyProto/Resources/Scenarios/default.json
```

> Scores here follow the current NR-01 GRO risk-matrix economy (task value derived
> from `risk` → risk level → scenario `scoring` block). The frozen manuscript
> figures (`v1.0.0`: 750 / 1400) differ — see `metrics/revision-evidence-1.0.0.md`.
> Timestamps and per-run timings vary; task/score/count lines are stable.

## Scenario 1: canonical `default.json`

Two task groups: PPE selection (sequential) then scaffold inspection. The scripted
playthrough drives all 9 tasks; every one completes.

```
=== SafetyProto CLI Harness ===
Scenario: default
Participant: P000
Groups: 2

  SessionStarted
--- Transcript ---
  GroupStarted       | Seleção de EPIs
  TaskStarted        | Equipar Botina de Segurança
  PpeStateChanged    | Boots=WORN
  TaskCompleted      | Equipar Botina de Segurança
  ScoreChanged       | Delta=100, Total=100
  TaskStarted        | Equipar Luvas
  PpeStateChanged    | GloveRight=WORN
  PpeStateChanged    | GloveLeft=WORN
  TaskCompleted      | Equipar Luvas
  ScoreChanged       | Delta=100, Total=200
  TaskStarted        | Equipar Óculos de Proteção
  PpeStateChanged    | Goggles=WORN
  TaskCompleted      | Equipar Óculos de Proteção
  ScoreChanged       | Delta=150, Total=350
  TaskStarted        | Equipar Capacete com Jugular
  PpeStateChanged    | Helmet=WORN
  TaskCompleted      | Equipar Capacete com Jugular
  ScoreChanged       | Delta=150, Total=500
  TaskStarted        | Equipar Cinto Paraquedista
  PpeStateChanged    | Harness=WORN
  TaskCompleted      | Equipar Cinto Paraquedista
  ScoreChanged       | Delta=200, Total=700
  GroupCompleted     | Seleção de EPIs
  GroupStarted       | Inspeção em Andaime Fachadeiro
  TaskStarted        | Conectar Talabarte ao Ponto de Ancoragem
  ActionAttempt      | connect_harness
  TaskCompleted      | Conectar Talabarte ao Ponto de Ancoragem
  ScoreChanged       | Delta=250, Total=950
  TaskStarted        | Instalar Guarda-corpo
  ActionAttempt      | install_guardrail
  TaskCompleted      | Instalar Guarda-corpo
  ScoreChanged       | Delta=200, Total=1150
  TaskStarted        | Instalar Rodapé
  ActionAttempt      | install_toeboard
  TaskCompleted      | Instalar Rodapé
  ScoreChanged       | Delta=200, Total=1350
  TaskStarted        | Reportar Irregularidade na Tela Fachadeira
  ActionAttempt      | flag_safety_net
[INFO]  TaskManagerCore: All task groups completed or no groups available.
  TaskCompleted      | Reportar Irregularidade na Tela Fachadeira
  ScoreChanged       | Delta=150, Total=1500
  GroupCompleted     | Inspeção em Andaime Fachadeiro
  SessionCompleted   | Score=1500, Tasks=9/9
------------------

Session summary: 9/9 tasks, score 1500
```

## Scenario 2: `ppe_equip.json`

Single task group: PPE selection (sequential). All 5 equip-set tasks complete.

```
=== SafetyProto CLI Harness ===
Scenario: PPEEquip
Participant: P001
Groups: 1

  SessionStarted
--- Transcript ---
  GroupStarted       | Selecao de EPIs
  TaskStarted        | Equipar Botina de Seguranca
  PpeStateChanged    | Boots=WORN
  TaskCompleted      | Equipar Botina de Seguranca
  ScoreChanged       | Delta=100, Total=100
  TaskStarted        | Equipar Luvas de Protecao
  PpeStateChanged    | GloveRight=WORN
  PpeStateChanged    | GloveLeft=WORN
  TaskCompleted      | Equipar Luvas de Protecao
  ScoreChanged       | Delta=100, Total=200
  TaskStarted        | Equipar Oculos de Protecao
  PpeStateChanged    | Goggles=WORN
  TaskCompleted      | Equipar Oculos de Protecao
  ScoreChanged       | Delta=100, Total=300
  TaskStarted        | Equipar Capacete com Jugular
  PpeStateChanged    | Helmet=WORN
  TaskCompleted      | Equipar Capacete com Jugular
  ScoreChanged       | Delta=150, Total=450
  TaskStarted        | Equipar Cinto Paraquedista
  PpeStateChanged    | Harness=WORN
[INFO]  TaskManagerCore: All task groups completed or no groups available.
  TaskCompleted      | Equipar Cinto Paraquedista
  ScoreChanged       | Delta=200, Total=650
  GroupCompleted     | Selecao de EPIs
  SessionCompleted   | Score=650, Tasks=5/5
------------------

Session summary: 5/5 tasks, score 650
```
