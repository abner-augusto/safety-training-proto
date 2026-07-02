# Refatoração do Sistema de Dados

> Status: planejado (2026-06-30). Reduzir a dependência de ScriptableObjects e
> unificar o carregamento de dados entre Unity, CLI harness e um futuro app de
> autoria desktop, em torno de um modelo de cenário em JSON compartilhado.

## Motivação

A lógica de aplicação **já é unificada**: tanto o Unity quanto o CLI harness rodam
o mesmo `TaskManagerCore` / `SafetyRuleEngineCore` / `ScoreService` através das
interfaces `ISafetyTask` / `ITaskGroup` (compiladas em `SafetyProto.Shared.dll`).

O que **diverge** é o carregamento de dados:

- **Unity** carrega ScriptableObjects (`SafetyTask` / `TaskGroup`) via referências no Inspector.
- **CLI** já carrega JSON, mas com uma implementação **duplicada** (`Scenario` /
  `ScenarioTask` / `InMemorySafetyTask` / `InMemoryTaskGroup` / `BuildTaskGroups`
  dentro de `Tools/CliHarness/`).

Logo, a refatoração real é: **um único modelo de dados + um único loader JSON**,
no Domain compartilhado, consumido por todos os hosts. Os ScriptableObjects deixam
de ser a fonte de runtime; viram (no longo prazo) saída de uma ferramenta de autoria.

Isso reforça a arquitetura **EDA** do projeto: o dado vira um contrato (JSON +
catálogo) que vive **fora da engine**. Como consequência, o core desacoplado pode
alimentar uma futura iteração do app em **outra engine** com quase nenhum retrabalho —
basta um novo "host" que linke `SafetyProto.Shared` e implemente os adapters de
apresentação.

## Estado-alvo

```
                 ┌─────────────────────────────┐
                 │  scenario_*.json (1 fonte)  │  ← formato unificado
                 └─────────────┬───────────────┘
                       carregado por
   ┌──────────────────────────┼──────────────────────────┐
 Unity (runtime)         CliHarness (.NET)        Authoring App (desktop)
 ScenarioLoader.Load     ScenarioLoader.Load      ScenarioLoader (valida + exporta)
   │                          │                          │
   └──────────► SafetyProto.Shared.dll ◄─────────────────┘
              (model + loader + catalog + validação + Domain)
```

`SafetyProto.Shared.dll` passa a conter, além do Domain atual:

- O **modelo de cenário** (records puros C#).
- O **`ScenarioLoader`** (parsing + validação, Newtonsoft).
- O **`CapabilityCatalog`** (modelo + serialização).

Os três hosts (Unity, CLI, app desktop) linkam a mesma DLL e compartilham parsing
e validação — o app desktop é só "mais um consumidor", como o CLI já é hoje.

## Decisões tomadas

| Tema | Decisão |
|---|---|
| Papel dos SOs de task/group | Aposentados como fonte de runtime; autoria futura via **app desktop separado**, não EditorWindow. |
| Serializador | **Newtonsoft.Json** (oficial no Unity via `com.unity.nuget.newtonsoft-json`; NuGet no CLI/desktop). Uma fonte de parsing. |
| Local do JSON em runtime | **Camadas:** default embarcado em `Resources/Scenarios/` (piso à prova de falha) + override opcional em `Application.persistentDataPath/scenarios/` (atualizável sem rebuild). |
| `ActionTypeSO` / `ActionRegistry` | **Adiado para a Fase 2.** A Fase 1 (tasks/groups → JSON) não toca em ações, pois a task só usa `actionId` (string) via `ResolveExpectedActionId()`. |
| Ferramenta de autoria | **App desktop** que roda fora da Unity, linkando `SafetyProto.Shared`. |

## Modelo de dados compartilhado

Novos records puros C# em `Scripts/Domain/Tasks/Model/`, adicionados ao
`SafetyProto.Shared.csproj`, implementando as interfaces existentes:

- `ScenarioDef` — `name`, `participantId?`, `groups[]`, `script[]` (o `script`
  já existe no CLI; no Unity é opcional/ignorado).
- `TaskGroupDef : ITaskGroup` — **substitui** `InMemoryTaskGroup`.
- `SafetyTaskDef : ISafetyTask` — **substitui** `InMemorySafetyTask` e o SO
  `SafetyTask` como fonte de runtime.

Serialização:

- `requiredPPE` e `executionMode` como **strings por nome** (`"Boots"`, `"Sequential"`).
  Preserva a tolerância ao gap do enum `PPEType` (o ordinal `2` legado) e mantém o
  JSON editável por humanos.

Esses records **deletam a duplicação** existente: `InMemorySafetyTask`,
`InMemoryTaskGroup`, `Scenario`, `ScenarioGroup`, `ScenarioTask` e `BuildTaskGroups`
saem do CLI.

## Loader compartilhado (`ScenarioLoader`)

Em `Scripts/Domain/Tasks/`, puro C#, Newtonsoft:

- `Parse(string json) -> Result<ScenarioDef>` — **nunca lança para o runtime**;
  retorna sucesso+cenário ou falha+motivo.
- **Validação** centralizada aqui (PPE/executionMode inválidos, ids desconhecidos
  contra o catálogo) — hoje espalhada no `Program.cs`, passa a valer para todos os hosts.

**Risco a validar cedo (Fase 1):** comportamento do Newtonsoft sob **IL2CPP no
Quest** (reflection/AOT). Fazer um build Android de fumaça antes de migrar tudo.

## Carregamento em camadas (resiliência)

1. **Default embarcado** — `Resources/Scenarios/*.json` (TextAsset), embarcado no
   APK e **validado no build** (teste de CI roda `ScenarioLoader` em todos os JSONs
   e falha o build se algum não parsear). Piso garantido.
2. **Override externo opcional** — `Application.persistentDataPath/scenarios/*.json`
   (gravável, acessível via `adb push`). Se faltar, não parsear ou não validar,
   o loader loga `[SafetyProto]` (em PT) e **cai para o default embarcado**.

> No Android, **StreamingAssets não serve** para "soltar arquivo sem rebuild"
> (fica dentro do APK, comprimido). O canal de override é `persistentDataPath`.

### Resolução por nome fixo (não varre a pasta)

O `ScenarioSource` resolve **um único arquivo por nome**, derivado de
`TaskManager.scenarioResourceName` (padrão `"default"`). Ele **não faz scan** do
diretório: procura exatamente `{name}.json` no override e, se não achar/não parsear,
cai para `Resources/Scenarios/{name}`. Consequências:

- Vários `.json` na pasta de override → todos ignorados, exceto o que casa com o nome
  (com o padrão, `default.json`). Não há regra de "mais recente" nem "primeiro da lista".
- Para alternar cenários via override sem rebuild, **copie/renomeie** o desejado para
  `default.json` (ex.: `adb shell cp .../scenarios/cenario_x.json .../scenarios/default.json`).
- Trocar o alvo permanentemente exige mudar `scenarioResourceName` na cena (implica rebuild).

Caminho de override no device (package `com.abnersouza.SafetyProto`):
`/sdcard/Android/data/com.abnersouza.SafetyProto/files/scenarios/default.json`.

> **Extensão futura (opcional):** um seletor de cenário em runtime pediria um
> `ScenarioSource.List()` que enumera os `.json` do diretório de override e devolve os
> nomes disponíveis — aditivo, sem quebrar o fail-safe por nome fixo atual.

## Catálogo de capacidades (`CapabilityCatalog`)

Modelo + serialização entram no `SafetyProto.Shared` já na **Fase 1** (mesmo que o
*exportador* só venha na Fase 4), porque tanto o app desktop quanto a validação de
runtime dependem do tipo. Conteúdo:

- `actionId`s registrados (de `ActionRegistry`).
- `PPEType`s disponíveis.
- Cenas / fases (de `PhaseController`).

O Unity exporta `capability_catalog.json` por build. O app desktop o consome para
oferecer **apenas opções válidas** ao especialista SST (dropdowns, não texto livre)
e valida o cenário com o mesmo `ScenarioLoader` **antes** de enviar ao Quest.

## Faseamento

Cada fase fecha em estado compilável/verde.

1. **Modelo + loader compartilhados.** ✅ **Concluída** (commit `fbcab4c`). Records
   `ScenarioDef`/`TaskGroupDef`/`SafetyTaskDef`/`ScriptStepDef` + `ScenarioLoader`
   (Newtonsoft, `ScenarioLoadResult` sem throw) + `CapabilityCatalog` (model) no
   `SafetyProto.Shared`. CLI migrado (deletados `InMemory*` / `Scenario*` /
   `BuildTaskGroups`). Paridade confirmada (`dotnet run`: equip 5/5 score 750,
   inspection 9/9 score 1400). Build IL2CPP/Quest limpo.
2. **Descolar o Unity dos tipos SO concretos.** ✅ **Concluída** (commit `0f47171`).
   Removidos os downcasts `as SafetyTask`/`as TaskGroup` em `TaskUIController`,
   `EvaluatorDashboardBootstrap`, `InspectionGateValidator`, `TimerSystem` e
   `TaskManager.GetCurrentGroup`; comparação por `TaskExecutionModeShared`; `ConvertAll`
   → LINQ. Validado com smoke test ao vivo no editor (loader + interfaces + Newtonsoft).
   **Campos `[SerializeField]` continuam concretos** (`TaskManager.taskGroups`,
   `TaskPopupFeedback.knownTasks`, `PhaseController.triggerGroup`) — são a fonte de
   dados, trocada na Fase 3.
3. **`TaskManager` carrega JSON.** ✅ **Concluída** (commit `2d8f522`). `ScenarioSource`
   (override em `persistentDataPath` → default em `Resources/Scenarios/` → fallback SO)
   alimenta o `TaskManager`. `ScenarioExporter` (menu `SafetyProto/Bake Scene Scenario
   to JSON`) gera `default.json` a partir da cena (2 grupos, 8 tasks, paridade incl. os
   `failureAdvice` curados). Dashboard usa `TaskManager.RuntimeGroups`; `PhaseController`
   casa por `groupName`. Play mode confirma `RuntimeGroups` = `TaskGroupDef` /
   `SafetyTaskDef` (carrega do JSON, sem fallback). A lista `taskGroups` (SO) permanece
   só como fonte de bake e rede de segurança.
   - **Pendente (Fase 6):** `TaskPopupFeedback.knownTasks` continua sendo um lookup por
     `taskName` baseado em SO; revisar/migrar quando os SOs forem removidos.
4. **App de autoria desktop + exportador de catálogo.** 🟡 **Fundação concluída**
   (commit `fa68e01`). `Tools/AuthoringApp` (.NET, consumidor de `SafetyProto.Shared`)
   valida cenários fora do Unity em duas camadas: tier 1 estrutural (`ScenarioLoader`) +
   tier 2 semântico (`ScenarioValidator` contra o `CapabilityCatalog`). Lado Unity:
    `CapabilityCatalogExporter` (menu `SafetyProto/Export Capability Catalog`) gera
    `Tools/AuthoringApp/capability_catalog.json` (ações de `Resources/Actions/actions.json`, enum
   `PPEType`, cenas do build). Validado: `default.json` = VÁLIDO; ações/EPIs fantasmas e
   tasks mortas são rejeitados com mensagens em PT.
   **Validação no device (2026-07-02):** override via `adb push` para
   `persistentDataPath/scenarios/default.json` carregou corretamente (log
   `[ScenarioSource] Cenário carregado do override externo`); a ordem das tarefas do
   cenário demo apareceu certa no task panel — o ciclo *editar/validar no desktop →
   `adb push` → rodar sem rebuild* está fechado. Amostra: `Tools/AuthoringApp/samples/override-demo.json`.
   **Concluído posteriormente:** GUI Avalonia para o especialista SST cria/edita cenários,
   grupos e tarefas, escolhe `actionId` por dropdown a partir do `capability_catalog.json`
   (mais ids já presentes no cenário) e faz deploy via `adb push` para `persistentDataPath`.
   Ela **não cria novas ações lógicas** no catálogo; novas ações ainda entram pelo JSON de ações
   compartilhado/catálogo do build.
5. **Ações para JSON.** ✅ **Concluída.** Modelo lógico
   `ActionDef` (id + metadados) + `ActionCatalogDef` + `ActionCatalogLoader` (sem throw)
   no `SafetyProto.Shared` (`Domain/Actions/`). Runtime: `ActionCatalogSource` (camadas
    override→`Resources/Actions/actions.json`, espelha `ScenarioSource`) e `ActionResolver`
    repontado — resolve `ActionDef` do JSON. O bake inicial preservou os metadados curados do
    antigo registry em `actions.json`; depois da deleção final da Fase 6, esse JSON passou a ser a
    fonte mantida diretamente. Validado: catálogo carrega do
   JSON (4 ações), `connect_harness` resolve com metadados, id fantasma rejeitado; paridade CLI
   mantida (equip 5/5=750, inspection 9/9=1400).
   - **Descoberta que desarriscou a fase:** cada ponto de emissão (`ActionEmitter`,
     `ActionTrigger`, `ScaffoldPieceInstaller`, `PPESnapSlot`, `RetractableLanyardController`)
     já espelha `action.ActionId` num campo `string actionIdOverride` via `OnValidate` — o id
     já está serializado nas cenas como string, então a lógica não depende do objeto SO.
   - **Fechado na Fase 6:** `ActionTypeSO`/`ActionRegistry` deletados; refs de cena/prefab
     trocadas por strings `actionId`.
6. **Runtime 100% JSON — remover dependência de SO dos caminhos de runtime.** ✅
   **Concluída**. A primeira etapa (commit `82352a7`) removeu a dependência de runtime e manteve
   os SOs apenas como fonte de autoria/bake offline. Com a GUI desktop operacional, a deleção final
   foi feita: os SOs e bakers foram removidos, e a cena/prefabs passaram a serializar só strings.
   Mudanças da etapa soft:
   - `TaskManager.LoadRuntimeGroups` — sem fallback SO (o default embarcado em `Resources` é o
     piso garantido); `taskGroups` fica só como fonte de bake.
   - `ActionResolver` — resolve `ActionDef` só do catálogo JSON, sem fallback `ActionRegistry`.
   - `TaskPopupFeedback` — lookup de hint (`taskName→hintText`) construído das
     `TaskManager.RuntimeGroups` (JSON); removido o `knownTasks: List<SafetyTask>`.
   - `PhaseController` — `triggerGroup` (SO) espelhado para `triggerGroupName` string no
     `OnValidate` (padrão dos emitters); runtime compara a string. Valor da cena = "Seleção de
     EPIs"; ref SO preservada para autoria.
   - Validado em Play mode: sessão inicia e roda a 1ª task do JSON; nenhum erro dos sistemas
     alterados; diff da cena = 2 linhas (`triggerGroupName` + `scenarioResourceName` explícito).
   - **Deleção final:** deletadas as classes SO (`SafetyTask`/`TaskGroup`/`ActionTypeSO`/
     `ActionRegistry`) e seus assets (`ScriptableObjects/Actions`, `Tasks`, `TaskGroups` e
     `Resources/ActionRegistry.asset`); removidos os bakers `ScenarioExporter` e
     `ActionCatalogExporter`; `TaskManager`, `PhaseController`, `ActionEmitter`, `ActionTrigger`,
     `ScaffoldPieceInstaller`, `PPESnapSlot` e `RetractableLanyardController` agora dependem só de
     strings (`scenarioResourceName`, `triggerGroupName`, `actionId`, `connectActionId`).

## Validação no device (build 0.7)

As Fases 5–6 + correção NR foram **validadas no Quest** (build 0.7, versionCode 4) sem erros:
ações resolvem do catálogo JSON, sessão carrega 2 grupos/8 tarefas sem fallback SO, transição
de fase dispara via `triggerGroupName`, hints vêm das `RuntimeGroups`, sessão roda ponta a ponta.
O runtime está comprovadamente 100% JSON.

## Resto da Fase 4 (cereja do bolo final)

Com o runtime desacoplado e validado, a **GUI desktop** (Avalonia, .NET multiplataforma, linkando
`SafetyProto.Shared`) fecha o ciclo avaliador↔desenvolvedor para criação/edição de cenário e deploy
via `adb push` para `persistentDataPath`. Como a GUI já edita tarefas com `actionId` a partir do
catálogo, a deleção final dos SOs foi liberada. Extensão futura opcional: tela específica para criar
novas ações lógicas e escrever o `actions.json`/catálogo, caso o especialista SST precise expandir o
vocabulário de ações além do build atual.

## Riscos / a validar

- **Newtonsoft no IL2CPP/Quest** — testar cedo (Fase 1).
- **Paridade de dados** — os SOs atuais têm conteúdo curado (ex.: `failureAdvice`
  alinhados às NRs). O exportador inicial deve preservá-lo exatamente; escrever um
  exportador one-shot SO→JSON garante paridade.
- **Referências de cena** — trocar o tipo de `TaskManager.taskGroups` quebra o wiring
  no `.unity`; fazer com a cena aberta no editor para não perder dados.
