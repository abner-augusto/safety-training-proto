# CliHarness Sample Output

**Date:** 2026-05-16
**Runtime:** .NET 10, Release configuration
**Command:** `dotnet run --project Tools/CliHarness --configuration Release -- <scenario.json>`

## Scenario 1: PPE Inspection (`ppe_inspection.json`)

Two task groups: PPE selection (sequential) followed by scaffold inspection (free-order).
All 9 tasks completed successfully.

```
=== SafetyProto CLI Harness ===
Scenario: PPEInspection
Participant: P001
Groups: 2

  19:56:42.682  SessionStarted
--- Transcript ---
  19:56:42.698  GroupStarted       | Seleção de EPIs
  19:56:42.700  TaskStarted        | Equipar Botina de Segurança
  19:56:42.921  PpeStateChanged    | Boots=WORN
  19:56:42.980  ActionAttempt      | equip_boots
  19:56:42.983  TaskCompleted      | Equipar Botina de Segurança
  19:56:42.983  ScoreChanged       | Delta=100, Total=100
  19:56:42.983  TaskStarted        | Equipar Luvas de Proteção
  19:56:43.196  PpeStateChanged    | Gloves=WORN
  19:56:43.261  ActionAttempt      | equip_gloves
  19:56:43.261  TaskCompleted      | Equipar Luvas de Proteção
  19:56:43.261  ScoreChanged       | Delta=100, Total=200
  19:56:43.261  TaskStarted        | Equipar Óculos de Proteção
  19:56:43.461  PpeStateChanged    | Goggles=WORN
  19:56:43.524  ActionAttempt      | equip_goggles
  19:56:43.524  TaskCompleted      | Equipar Óculos de Proteção
  19:56:43.524  ScoreChanged       | Delta=100, Total=300
  19:56:43.524  TaskStarted        | Equipar Capacete com Jugular
  19:56:43.726  PpeStateChanged    | Helmet=WORN
  19:56:43.787  ActionAttempt      | equip_helmet
  19:56:43.788  TaskCompleted      | Equipar Capacete com Jugular
  19:56:43.788  ScoreChanged       | Delta=150, Total=450
  19:56:43.788  TaskStarted        | Equipar Cinto Paraquedista
  19:56:44.003  PpeStateChanged    | Harness=WORN
  19:56:44.065  ActionAttempt      | equip_harness
  19:56:44.065  TaskCompleted      | Equipar Cinto Paraquedista
  19:56:44.065  ScoreChanged       | Delta=200, Total=650
  19:56:44.065  GroupCompleted     | Seleção de EPIs
  19:56:44.065  GroupStarted       | Inspeção em Andaime Fachadeiro
  19:56:44.065  TaskStarted        | Conectar Talabarte ao Ponto de Ancoragem
  19:56:44.267  ActionAttempt      | connect_harness
  19:56:44.267  TaskCompleted      | Conectar Talabarte ao Ponto de Ancoragem
  19:56:44.267  ScoreChanged       | Delta=200, Total=850
  19:56:44.267  TaskStarted        | Instalar Guarda-corpo
  19:56:44.469  ActionAttempt      | install_guardrail
  19:56:44.469  TaskCompleted      | Instalar Guarda-corpo
  19:56:44.469  ScoreChanged       | Delta=200, Total=1050
  19:56:44.469  TaskStarted        | Instalar Rodapé
  19:56:44.670  ActionAttempt      | install_toeboard
  19:56:44.670  TaskCompleted      | Instalar Rodapé
  19:56:44.670  ScoreChanged       | Delta=150, Total=1200
  19:56:44.670  TaskStarted        | Sinalizar Tela de Proteção
  19:56:44.871  ActionAttempt      | flag_safety_net
[INFO]  TaskManagerCore: All task groups completed or no groups available.
  19:56:44.872  TaskCompleted      | Sinalizar Tela de Proteção
  19:56:44.872  ScoreChanged       | Delta=100, Total=1300
  19:56:44.872  GroupCompleted     | Inspeção em Andaime Fachadeiro
  19:56:44.889  SessionCompleted   | Time=2.20s, Score=1300, Tasks=9/9
------------------

Session summary: 9/9 tasks, score 1300, 2.20s
```

## Scenario 2: PPE Equip (`ppe_equip.json`)

Single task group: PPE selection (free-order). All 5 tasks completed successfully.

```
=== SafetyProto CLI Harness ===
Scenario: PPEEquip
Participant: P001
Groups: 1

  19:56:43.673  SessionStarted
--- Transcript ---
  19:56:43.689  GroupStarted       | Selecao de EPIs
  19:56:43.691  TaskStarted        | Equipar Botina de Seguranca
  19:56:43.900  PpeStateChanged    | Boots=WORN
  19:56:43.959  ActionAttempt      | equip_boots
  19:56:43.962  TaskCompleted      | Equipar Botina de Seguranca
  19:56:43.962  ScoreChanged       | Delta=100, Total=100
  19:56:43.962  TaskStarted        | Equipar Luvas de Protecao
  19:56:44.173  PpeStateChanged    | Gloves=WORN
  19:56:44.237  ActionAttempt      | equip_gloves
  19:56:44.237  TaskCompleted      | Equipar Luvas de Protecao
  19:56:44.237  ScoreChanged       | Delta=100, Total=200
  19:56:44.237  TaskStarted        | Equipar Oculos de Protecao
  19:56:44.437  PpeStateChanged    | Goggles=WORN
  19:56:44.499  ActionAttempt      | equip_goggles
  19:56:44.499  TaskCompleted      | Equipar Oculos de Protecao
  19:56:44.499  ScoreChanged       | Delta=100, Total=300
  19:56:44.499  TaskStarted        | Equipar Capacete com Jugular
  19:56:44.701  PpeStateChanged    | Helmet=WORN
  19:56:44.763  ActionAttempt      | equip_helmet
  19:56:44.763  TaskCompleted      | Equipar Capacete com Jugular
  19:56:44.763  ScoreChanged       | Delta=150, Total=450
  19:56:44.763  TaskStarted        | Equipar Cinto Paraquedista
  19:56:44.965  PpeStateChanged    | Harness=WORN
  19:56:45.027  ActionAttempt      | equip_harness
[INFO]  TaskManagerCore: All task groups completed or no groups available.
  19:56:45.028  TaskCompleted      | Equipar Cinto Paraquedista
  19:56:45.028  ScoreChanged       | Delta=200, Total=650
  19:56:45.028  GroupCompleted     | Selecao de EPIs
  19:56:45.042  SessionCompleted   | Time=1.37s, Score=650, Tasks=5/5
------------------

Session summary: 5/5 tasks, score 650, 1.37s
```
