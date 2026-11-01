<!--STATUS
state: LIVE
build-state: DESIGN — rev 4 (2026-10-31: objectives / rules = entities running GENERIC behaviours with params, conditions by reference — no per-scenario compile, R-4112) · rev 3b (2026-10-31: S0 ①② measured; SR-16 the generated block; SR-19 no re-evaluation) · rev 3a (2026-10-31: SR-15 lowers to a generated .hsm.json + the existing compile / hot-reload path) · rev 3 (2026-10-31): scenario control is a BEHAVIOUR on the World Environment entity's brain, built from data at load by the
  existing HSM / BTree runtime — no rules interpreter (R-4109…R-4111); evaluation (P-4) and the utility director ride the same brain
updated: 2026-10-31 (rev 3 — coordinator)
current-answer: §10 objective kinds + measures (rev 7 draft) · §9 scenario MACROS (rev 6 draft — the general mechanism) · §8 templates + blocks (rev 5 draft, leans await the user) · §0c the rev-4 ruling · §0b the user's rev-3 ruling · §2 claim table · §3 diagrams · §4 decisions SR-1…SR-20 · §5 what is missing · §6 slices · §7 examples
stale-below: §⛔ HISTORY (rev 3's one-generated-HSM-per-scenario; rev 2's ScenarioRulesSystem / RuleSet interpreter — superseded by rev 3)
known-rot: ⛔ §3, §4 (SR-21 / SR-26 / SR-33), §7b and §8 write the evaluator into the SOP slot (`Sop` / `SopState`) — WRONG, SR-51: it is the TASK slot (`Behavior`)
known-conflict: none
related-designs:
  - docs/EXPLAINER_Scenario_Evaluation_And_Direction.md — the EXPLAINER of this design (objective, trigger … with examples); this file keeps the decisions
  - docs/PLAN_Training_And_Gameplay_Engine.md — P-1 (the user's top item), P-4 evaluation, P-5 amplifier; R-4105 (data over NED), R-4108 (every element a feature)
  - docs/blueprints/DESIGN_Behavior_Action_Binding.md — §5.7 OWNS HsmSharedBinder (CE-3163): a shared C# method bound to a params / working-state OFFSET of a brain's block — the seam a data-built brain binds through
  - docs/DESIGN_Decision_Layer.md — §3 OWNS utility hosting in BTree / HSM / Blueprint (ChooseOption, IsOption) the director decision uses
  - docs/OVERVIEW_Behaviour_Building_Blocks.md — the map: §3 utility decisions + inputs, §9 orders, the command chain the director orders through
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — §3 / §8 OWN orders down + reports up (SetReporting, PIR / FFIR, the picture) the director and the measures use
  - docs/designs/fluent-btree/DESIGN.md — records that trees were once JSON compiled at startup by TreeCompiler.CompileFromJson (the runtime path this design revives for data-built brains)
  - docs/DESIGN_Orders_Params_Entity_Classes.md — params DTOs (R-3002: defaults are C# initializers) a reusable director behaviour declares
  - docs/Predicate-Infrastructure-Capabilities.md — OWNS the data predicate (SearchPredicateDto, R-4077) — one of the condition strategies
  - docs/DESIGN_Scenario_Regression.md — §3 OWNS the checks grammar the rules JSON shares
  - docs/DESIGN_Director_Timeline_Editor.md — §9c–§9e OWN the registered timeline lanes; measures and outcomes become lanes
  - docs/designs/weather/DESIGN_Weather_And_Time.md — W-0 / C-3 OWN the World Environment entity (8810) and ScenarioClock
  - docs/DESIGN_Node_Features.md — every element is a feature (R-4108)
  - docs/DESIGN_Player_Join.md — spawn areas (D-11) and score (D-15) the actions reuse
  - docs/blueprints/Architect_Question_79_One_Ownership_Truth.md — §0.10 OWNS PartMetadata (the multi-instance part); §9.3 explains why macro entities are NOT parts
  - docs/DESIGN_Node_Roles_And_Policies.md — §7.3 OWNS IsTransient → ScenarioIgnoreTag (R-140) that macro entities reuse
  - docs/DESIGN_Deterministic_Network_Ids.md — §11b OWNS network id allocation and the per-load re-allocation SR-45 / SR-50 rely on
  - docs/DESIGN_Scenario_Entity_Keys.md — CE-4153 rev 2: STABLE entity GUIDs, derived for macro-made entities — the cross-scope reference (SR-62)
  - docs/DESIGN_Artifact_Staging.md — OWNS the prefetch (NAS → every node's staging) that SR-80 extends with the macros package
  - docs/DESIGN_Content_Layout.md — §2 OWNS the content folders (scenarios/, recipes/scenarios/) a macros/ folder joins (SR-38)
  - docs/designs/mgmt-1/DESIGN.md — §10 Stories (per-trainee micro-exercises, evaluation filtered by story; unbuilt) — one more evaluation scope
  - docs/DESIGN_Sensors_And_Doctrine.md — §4 / §5.1 OWN the per-TKB-type deterministic child entities (SensorChildFactory) that §8 reuses for blocks and per-member evaluators
-->

# DESIGN — **scenario rules** *(objectives, triggers, end of exercise, the director, evaluation — for training and for games)*

> 🔒 **User, `2026-10-31`:** *"The point number one seems to have the most value for me."* — P-1: objectives, end conditions, triggers at
> SCENARIO scope (training: *"secure the bridge by 10:00"*, phase lines, evaluation; game: capture points, time limit, tickets, win / lose).

## 0. ✅ The user's answers, rev 2 (`2026-10-31`)

> 🔒 *"blueprint is just one way of creating some decision logic … authorable also using C# hard-coded methods … best if the rules are data … a
> reusable condition that we can use also in the B3s and HSM guards"* · *"we should have one [evaluator]"* · pause at the end = a switchable
> feature · *"broadcast the event that the mission has finished … not our task to build a nice UI"* · *"define what station is the IOS"* ·
> *"Conditions as names registered. The user never writes expressions"* · *"visibleTo attribute can for sure be added."*

## 0b. ✅ The user's ruling, rev 3 (`2026-10-31`) — **build the brain from data; invent nothing that exists**

> 🔒 *"HSMs and B3s, they are just data as well. They do not need to be compiled. What needs to be compiled is just conditions … they should be
> generic, reusable … we can freely generate the HSM or B3 behaviors for the scenario brain on the fly, basically without recompiling."* ·
> *"What I want to avoid is to create some interpreter of scenario rules if there are already so many different means of how to express these in
> the current engine … avoid developing another type of brain, specifically for scenario control … only if really necessary."* ·
> *"logic like counter attack director can be authored, but it needs to be reusable … usable in multiple exercises in different conditions and
> have enough parameters."* · *"we already created a lot of debugging and monitoring mechanisms. So we should not add one more."* ·
> *"the data basically means that we are building the brain of the entity from the data, procedurally, on the fly, not by hard-coding new
> behaviors. Pretty much the same applies to the performance evaluator. I would rather extend the authoring possibilities of the existing
> features than start inventing something really new."*

**The idea in one line:** the scenario's rules, objectives and measures are DATA that is lowered, at load, into an ordinary **HSM** (or BTree)
— built by the existing runtime builder, with guards and actions that are the existing compiled **shared primitives** — and registered as the
**World Environment entity's brain**; so it runs, hot-swaps, journals and debugs exactly like every unit's brain.

## 0c. ✅ The user's ruling, rev 4 (`2026-10-31`) — **generic rules, composed by parameters; nothing compiles per scenario**

> 🔒 *"What I would like to achieve is that the rules are generic enough and we just parameterize them using values and compose the mission
> objective from those existing rules … So the scenario doesn't really need to recompile any HSM just because it is using already existing
> rules composed in and parameterized in different ways."* · *"I would like to minimize the need of rebuilding anything per scenario unless
> there is an unavoidable need for that. And if there is, it should be deterministic so that it can be done by any brain node."* ·
> *"maybe it's not necessary at all because the scenarios are distributed to all nodes that need them. So all the nodes can recompile
> whatever is in the scenario."*

⭐ ⇒ SR-21…SR-24: objectives and rules are entities running GENERIC compiled behaviours, parameterised; conditions and actions are referenced
by name in the params; only a scenario that embeds its own HSM compiles, in memory and deterministically, on each Brain node.

## 1. INVENTORY *(`2026-10-31`; queries named)*

`search_graph` Class `.*(Hsm|BTree|Bt|Fbt|Tree)(Json|Runtime|Loader|Builder|Compiler|Interpreter|Blob|Asset).*` (29 hits) ·
`.*Utility.*` (80, paginated past tests) · `.*(BehaviorRegistry|BehaviorCatalog|HsmRegistry|…).*` · `.*(Measure|Evaluation|Debrief|Score).*`
(no evaluation code — EQS scoring and the measuring gizmo only) · grep for every `[SharedAiCondition]` / `[SharedAiAction]` / `[UtilityInput]`
name. ⚠ `check_index_coverage` is not available through the CLI — absence claims below are graph + grep, coverage unchecked.

| what exists | where | for scenario control |
|---|---|---|
| ⭐ **an HSM built at RUNTIME** — `HsmBuilder` → `StateMachineGraph.Build` → normalise / flatten → `HsmEmitter.Emit` → `HsmDefinitionBlob` | `Fhsm.Compiler/HsmBuilder.cs:14`, `Graph/StateMachineGraph.cs:93`; used at runtime by `Fdp.Examples.UrbanCombat/Brains/ApcHsmSetup.cs` | ⭐ the lowering target — no Roslyn, no dll |
| ⭐ **a BTree compiled from JSON at RUNTIME** — `TreeCompiler.CompileFromJson` → `BehaviorTreeBlob` | `Fbt.Kernel/Serialization/TreeCompiler.cs:12`; fluent-btree DESIGN :26 (*"compiled at startup"*) | ⭐ the BTree alternative |
| ⭐ **a behaviour registered at RUNTIME by name** — `BehaviorRegistry.Register(name, BehaviorDefinition)`; the definition carries `HsmDefinition` / `BTreeInterpreter`, `BrainStateBytes`, `ParseParams`, `BakeDefaults`, `JsonParamsDtoType`; hot-reload `MergeFrom` | `Fdp.Toolkits/Behavior/BehaviorRegistry.cs:147-343, :414` | ⭐ the data-built brain is just another registration |
| ⭐⭐ **a compiled shared method bound to a FIELD OFFSET at runtime** — `HsmSharedBinder.RegisterCondition / RegisterAction / RegisterStateful*` (key `Fqn@paramOffset[@stateOffset]`, a managed delegate whose closure carries the offsets); `RegisterDeactivator(…, nint paramOffset, nint stateOffset)` already takes raw offsets; BTree twin `SharedNodeBinder` | `Fdp.Toolkits/Behavior/HsmSharedBinder.cs:30-132`; `HsmActionDispatcher` managed tables (CE-3163) | ⭐⭐ **the seam**: a data-built brain lays out its block from the primitives' compiled `P` / `WS` types and binds each site at its offset — only a by-offset overload is missing (today's public overloads take a selector) |
| a brain ticks on ANY entity with `SopState` + owned `BehaviorState`; movement / weapon / interaction channels optional | `BrainTickSystem.cs:224, :296-298` | ⭐ the World Environment entity can carry a brain |
| params from the scenario: entity JSON `"Sop": {"Name","Params","Origin"}`; `SopState.JsonParams` | scenario files; `SopState.cs:18` | ⭐ the director's params live in the scenario, like any unit's |
| shared conditions: 104 `[SharedAiCondition]` (e.g. `ElementReported`, `Secured`, `HeldLongEnough`, `AreaQuiet`, `MoreObjectives`) — ALL self-relative (a unit's / commander's view) | `Fbt.Kernel/SharedAiAttributes.cs:14`; `Squad/Command/PictureConditions.cs:181` | reused inside quantifiers; ⛔ none is scenario-scoped |
| shared actions incl. `Action_IssueTacticalIntent`, `OrderSubordinate`, `SetSubordinateReporting`, `SendReport`, `Action_IncrementCounter`, `ChooseOption` / `ChooseOptionOnce` | grep `[SharedAiAction]` | ⭐ the director orders through these |
| utility: `UtilityDecisionBuilder` (runtime), `UtilityScorer.Evaluate(repo, self, def, context…)`, `UtilityInputCtx {Repo, Self, Context, Params}`, 59 `[UtilityInput]` (e.g. `EnemyStrengthRatio`, `SquadStrengthRatio`, `UnderFire`), `UtilityDecisionLog` | `Utility/Core/UtilityDecisionBuilderInfra.cs:219`; `UtilityScorer.cs:15` | ⭐ the adaptive director's choice; ⛔ no scenario-scope input |
| `CommanderUtilityTickSystem` (manoeuvre selection) — built, **no production caller** | OVERVIEW §6 DEAD box | precedent that utility works above unit level; ⛔ not a host |
| command chain: orders down (`AssignTacticalIntentEvent`), reporting demands (`SetReportingEvent` PIR / FFIR / SITREP, `ReportingRequirementsSystem.cs:38`), reports up (`SquadReportNotification`, `ReportInbox`) | Command Chain Example §3, §8 | ⭐ directing goes DOWN this chain; reports are measured |
| debugging that exists: HSM / BTree runtime inspector + overlay, `BrainJournal` (always on, recorded), `UtilityDecisionLog`, the Brain view | `HsmRuntimeInspectorPane`, `BTreeRuntimeOverlayRenderer`, OVERVIEW :480-484 | ⭐ inherited, nothing new |
| timeline lanes as data (`IndexedLaneDefinition`): shot, hit, blast, state lanes | Timeline §9c-§9e | ⭐ measures read and write lanes |
| the World Environment entity (TKB 8810: scenario-level, Brain-owned) with `ScenarioClock`, `WeatherConditions` | `TkbEntityTypes.cs:120`; Weather W-0 / C-3 | ⭐ the host entity |
| ⭐ **a params field that names a compiled thing, resolved at runtime** — `UtilityDecisionRef` (CE-2068: JSON = the asset id, the editor offers a picker, the scorer keys on the id); utility inputs dispatched by id through `UtilityInputReaderStore` | `Utility/Core/UtilityDecisionRef.cs:11`; `UtilityScorer.cs:32` | ⭐ the pattern `ConditionRef` / `ActionRef` copy (SR-22) |
| ⭐ **in-memory compile on the node that needs it** — `InMemoryRoslynCompiler` (Roslyn → PE in a collectible ALC), used by Blueprints' `Stage8_RoslynFinalize` and `QuickReloadService` | `Hrot.Blueprints.Compiler/Compiler/Roslyn/InMemoryRoslynCompiler.cs` | the escape hatch for a scenario-embedded HSM (SR-24) |

## 2. Claim table

| the design rests on | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| an HSM can be built and run without compiling | ✅ `StateMachineGraph.cs:93`, `ApcHsmSetup.cs` (registered, ticked by `BrainTickSystem`) | ✅ fluent-btree DESIGN :26 (runtime JSON was the first path) |
| a compiled primitive binds at a runtime-chosen offset | ✅ `HsmSharedBinder.cs:98-132` (offsets are `nint`) | ✅ Behavior Action Binding §5.7 |
| a brain may run on a non-unit entity | ✅ `BrainTickSystem.cs:224, :296` | ⛔ searched, no design names a world-entity brain — this one does |
| the director can act only through orders | ✅ `Action_IssueTacticalIntent`, `SetSubordinateReporting` exist | ✅ Command Chain Example §3 |
| the existing inspectors show the scenario brain | ✅ `HsmAssetProjector.Project(blob, metadata, …)` turns any compiled blob + `MachineMetadata` into an editor asset (how every C#-authored HSM is shown); generated machines carry `HsmMetadata = blob.Metadata` (CE-370) | ✅ user, `2026-10-31` |
| an evaluation can be re-run on a recording | — not needed (user: a finished run is final, SR-19) | — |

## 3. Diagrams *(rev 4 — objectives and rules are ENTITIES running GENERIC compiled behaviours; nothing is compiled per scenario)*

```mermaid
classDiagram
    direction LR
    class ScenarioFile { <<existing>> entities, each with Sop name + Params }
    class ObjectiveEntity { <<new TKB type, the area-entity pattern>> SopState = Objective · ObjectiveStatus (replicated) }
    class TriggerEntity { <<new TKB type>> SopState = Trigger }
    class WorldEnvironment { <<existing entity 8810>> ScenarioClock · SopState = ExerciseControl }
    class Objective { <<new GENERIC behaviour, shipped once>> Pending · Active · Achieved · Failed }
    class Trigger { <<new GENERIC behaviour>> Armed · Fired · repeat }
    class ExerciseControl { <<new GENERIC behaviour>> end condition · ExerciseEnded · hosts a director }
    class ObjectiveParams { <<params DTO, R-3002>> side · title · required · visibleTo · points · timeLimit · success ConditionRef · fail ConditionRef }
    class ConditionRef { <<new, the UtilityDecisionRef pattern>> name + args JSON · resolved at load }
    class ActionRef { <<new, same pattern>> name + args JSON }
    class SharedPrimitiveTable { <<new, GENERATED with the primitives, in the shipped dll>> name to compiled condition / action + its P type }
    class SharedPrimitives { <<existing + new scenario-scope>> SharedAiCondition · SharedAiAction }
    class DirectorAsset { <<authored REUSABLE HSM / BTree, shipped>> CounterAttackDirector + params }
    ScenarioFile *-- ObjectiveEntity
    ScenarioFile *-- TriggerEntity
    ObjectiveEntity ..> Objective : runs
    TriggerEntity ..> Trigger : runs
    WorldEnvironment ..> ExerciseControl : runs
    Objective ..> ObjectiveParams
    ObjectiveParams *-- ConditionRef
    Trigger ..> ConditionRef : when
    Trigger ..> ActionRef : do
    ConditionRef ..> SharedPrimitiveTable : resolved by name
    ActionRef ..> SharedPrimitiveTable
    SharedPrimitiveTable ..> SharedPrimitives
    ExerciseControl ..> DirectorAsset : hosts by name
```

*What the picture shows that prose hid:* a scenario adds only ENTITIES and PARAMS — the behaviours (`Objective`, `Trigger`,
`ExerciseControl`, directors) and the primitives are compiled once into the shipped dll. The one new seam is the reference-by-name
(`ConditionRef` / `ActionRef`), copied from `UtilityDecisionRef` + `UtilityInputReaderStore`, which already resolve by name at runtime.

```mermaid
sequenceDiagram
    participant S as scenario load (every node that loads it)
    participant E as objective entity "bridge"
    participant B as BrainTickSystem (the owner node only)
    participant T as SharedPrimitiveTable
    participant N as every node
    S->>E: create entity · SopState = Objective · Params JSON
    B->>E: start Objective · ParseParams fills ObjectiveParams
    B->>T: resolve success = AreaControlled + args (once)
    loop each tick
        B->>T: call AreaControlled(ref P, self, repo)
        T-->>B: true after 60 s held
        B->>E: Achieved · ObjectiveStatus written
        E-->>N: replicated (frontends, IOS, NED)
    end
```

```mermaid
graph TD
    subgraph shipped["compiled ONCE, shipped in Hrot.AI.Behaviors (deterministic build)"]
        GB["generic behaviours: Objective · Trigger · ExerciseControl · measures"]
        PR["shared primitives + the generated by-name table"]
        DIR["reusable directors (authored assets)"]
    end
    subgraph scenario["per scenario: DATA only, on every node that loads it"]
        ENT["objective / trigger entities + Sop params"]
    end
    ENT --> BT["BrainTickSystem on the OWNER node (existing, every frame)"]
    GB --> BT
    PR --> BT
    EMB["a scenario-EMBEDDED .hsm.json (escape hatch, SR-24)"]:::rare -. "compiled in memory on load, deterministically, on each Brain node" .-> BT
    classDef rare fill:#fff3cd,stroke:#c90
```

*Caption:* every frame the existing brain tick runs the generic behaviours on the owner node; nothing scenario-specific is compiled. The
amber edge is the only per-scenario compile, and only for a scenario that embeds its own HSM.

## 4. Decisions (⭐ leans for the user)

| # | decision | ⭐ lean |
|---|---|---|
| **SR-1** | what a rule is | **When** a condition becomes true (optionally within a window) **Do** actions; once or repeat — authored as data (§7a), LOWERED to an HSM region (rev 3) |
| **SR-2** | the condition | a NAMED primitive the author parameterises, never an expression — implemented once as a `[SharedAiCondition]` C# method, a data predicate (R-4077) or a Blueprint hosted as a guard; the same primitive is a BTree condition, an HSM guard, a film cue, a check |
| **SR-3** | scenario-scope primitives | §5 lists them; per-unit ones are reused through a generic quantifier `ForUnits(filter, any / all / atLeast n, condition)` |
| **SR-4 / SR-14** | objectives | first-class records (id · side · title · brief · required / optional · `visibleTo` · success · fail · time limit · points · state · progress · when · why); lowered to a region; mirrored into `ObjectiveStates` |
| **SR-5** | actions | existing shared actions first (`Action_IssueTacticalIntent`, `OrderSubordinate`, `SetSubordinateReporting`, mission commands); new: `Message`, `SpawnGroup`, `AwardPoints`, `EndExercise`, `SetSkillAmplifier` |
| **SR-6** | where it runs | ⭐ rev 3: the **brain of the World Environment entity** on the Brain node that owns it, ticked by the existing `BrainTickSystem`; the feature `scenario.rules` composes the loader; a scenario with rules and no node advertising it ⇒ the master refuses to start |
| **SR-8** | the output | replicated `ObjectiveStates` (NED fact) + lanes `rules.objective`, `rules.outcome`, `measure.*` |
| **SR-9** | the end | `EndExercise` broadcasts **`ExerciseEnded`** with the full result (§7c); pause = feature `scenario.rules.pause-on-end` (lean ON); never an automatic unload; UI only a debug panel `ui.rules-debug-panel` that never pops up |
| **SR-10** | authoring | ⭐ rev 4: objectives and triggers are ENTITIES the editor already places and edits (their Sop params, with a picker for `ConditionRef` / `ActionRef` like `UtilityDecisionRef`'s); the rules JSON of §7a is a compact VIEW of those entities, not a second format |
| **SR-11** | ONE evaluator | ⭐ rev 3: the evaluator IS the brain runtime — rules, objectives, mission objectives and measures are all guards of a brain; film cues and checks call the same primitives |
| **SR-12** | custom logic | a new condition / action = a shared C# method in `Hrot.AI.Behaviors` or a Blueprint; ⛔ rev 2's world-singleton Blueprint host is dropped (the brain is the host) |
| **SR-13** | the IOS | a composition choice; the server's full UI by default (R-4107) |
| ⭐ **SR-21** | **rev 4: objectives and rules are ENTITIES running GENERIC behaviours** (R-4112) | an objective = an entity of a new TKB type (the area-entity pattern) whose `SopState` runs the generic compiled `Objective` behaviour with its params (side · title · required · `visibleTo` · points · time limit · success / fail `ConditionRef`); a rule = a `Trigger` entity (`when` ConditionRef · `do` ActionRef[] · once / repeat); the end of the exercise = `ExerciseControl` on the World Environment entity. ⭐ The scenario is the EXISTING format (entities with `Sop: {Name, Params}`), the editor authors them as entities with params, each has the brain view, the journal and the inspector. ⇒ **a scenario adds data only — nothing compiles per scenario** |
| ⭐ **SR-22** | **a condition / action BY REFERENCE** — the one new seam | `ConditionRef` / `ActionRef` = a primitive's NAME + its args JSON, copied from `UtilityDecisionRef` (CE-2068: a params field naming a decision, a picker in the editor, resolved by id at runtime) and `UtilityInputReaderStore` (inputs dispatched by id). A by-name table of the shared primitives (name → compiled method + its `P` type) is GENERATED with the primitives and ships in the dll; at behaviour start the args are parsed into that `P` once and each tick calls the method. Composition: generic `AnyOf` / `AllOf` / `Not` / `ForUnits` conditions take `ConditionRef[]`. ⛔ not an interpreter: one indirection, the same shape as the HSM action dispatcher's id table |
| ⭐ **SR-23** | **what still compiles, and when** | only NEW PRIMITIVES and new reusable BEHAVIOURS (directors, generic behaviours) — ordinary development, built once into the shipped dll. ⛔ Never a scenario |
| ⭐ **SR-24** | **a scenario that embeds its own HSM** (the escape hatch, authored in the existing HSM editor, saved in the scenario) | ⭐ compiled IN MEMORY on load by every Brain node that needs it — the precedent is Blueprints' `InMemoryRoslynCompiler` (collectible ALC, `QuickReloadService`) — DETERMINISTIC: the same JSON + the same generator + the same referenced dlls ⇒ the same machine, keyed by the JSON's content hash, cached; ⛔ no dll is distributed (the scenario is). ⚠ discouraged by R-4110 (a director belongs in a reusable asset); not in S1 |
| ⚠ **SR-17** — HOSTING SUPERSEDED by SR-99 (a director is a macro of triggers; no `ExerciseControl` host) | **reusable directors** (R-4110) | a director (counter-attack, reinforcement, pressure) is an authored HSM / BTree **asset** with a params DTO (R-3002 initializers), usable in any exercise; the scenario names it + params; the loader hosts it as a sub-machine of the scenario brain (the existing hosted-HSM path). ⛔ never logic for one exercise |
| ⚠ **SR-18** — `self` = World Environment SUPERSEDED by SR-99 (the escape hatch runs on a trigger-kind control entity; the utility choice itself stands) | **the utility director** | a director asset may hold a utility decision (`ChooseOption` / `IsOption`, DESIGN_Decision_Layer §3) over plays (*Counter-attack · Reinforce · Hold · Ease off*), with `self` = the World Environment entity and `Context` = a candidate (objective, spawn area, side HQ); inputs are scenario-scope `[UtilityInput]`s (§5); the winner is executed as an ORDER down the chain; `UtilityDecisionLog` explains it |
| ⭐ **SR-19** | **performance evaluation (P-4) on the same brain** (R-4111) | a measure = a named stateful primitive (`Count`, `Duration`, `Latency`, `Ratio` over events / lanes / conditions) with a **subject** (scenario · side · team = a command-chain node · trainee = a station / unit); lowered as regions of the same brain; results into `ObjectiveStates`' sibling `Measures` and `measure.*` lanes; live feedback to trainee / instructor as NED facts. ✅ **user, `2026-10-31`: no re-evaluation of a finished run** — *"once the life is over, we do not change anything, we do not re-evaluate based on different criteria"*; slice 1 is the LIVE scenario and its evaluation (re-running new measures on an old recording: a future idea) |
| ⭐ **SR-20** | **facts** | ⛔ no fourth registry. ⭐ The existing registries are adapted: a generic condition `InputAbove(input, threshold)` makes every `[UtilityInput]` a condition; a generic measure `Sample(input)` makes it a measure; a condition is a 0 / 1 input. ⇒ a quantity written once as a `[UtilityInput]` is usable as a condition, a utility input and a measure |

**Rejected — one line each:**
- *A rules interpreter (rev 2's `ScenarioRulesSystem`)* — a second brain runtime beside HSM / BTree with its own debugging (R-4109).
- ⛔ ~~*One generated `.hsm.json` + Roslyn build per scenario*~~ — **withdrawn rev 3a**: the SDK is not a blocker (user) and it is the most-reused path; now SR-15's lean.
- *A world-singleton Blueprint as the scenario host* — the brain already hosts Blueprints as guards / actions; a second host duplicates it.
- *A per-scenario params / working-state struct* — the primitives' compiled `P` / `WS` concatenated cover it (SR-16).
- *Directing by moving units* — duplicates the order chain; the director orders.
- *A separate evaluation engine* — measures are primitives in the same brain.
- *A new "fact registry"* — `[UtilityInput]` + two generic adapters cover it (SR-20).

## 5. What is missing — **as registered primitives** *(everything else exists)*

| kind | missing (scenario scope) | ⭐ note |
|---|---|---|
| conditions | `AreaControlled(area, side, minUnits, maxEnemy, forSeconds)` · `UnitsDestroyed(filter, count)` · `CiviliansHarmed(bySide, atLeast)` · `ScenarioTime(after / before)` · `ObjectiveIs(id, state)` · `AllObjectives(side, required, state)` · `ReportReceived(commander, kind, within)` · `InputAbove(input, threshold)` · `ForUnits(filter, quantifier, condition)` | `EnemyInArea` exists but is PRIVATE to `SquadDefendBehavior` and self-relative; EQS `EntitiesOfForceInArea` is the query `AreaControlled` reuses; `ElementReported` is the commander-relative base of `ReportReceived` |
| actions | `Message(to, text)` · `SpawnGroup(group, area)` · `AwardPoints(side, n)` · `EndExercise(outcome)` · `SetSkillAmplifier(side, tiers)` · `PublishObjective(id, state)` (entry action) | orders, reporting demands, mission commands EXIST |
| utility inputs | `SideStrengthRatio(side, vs)` · `ObjectiveProgress(id)` · `ScenarioTimeRemaining` · `MeasureValue(measure)` | squad-level strength ratios exist (`SquadStrengthRatio`, `EnemyStrengthRatio`) |
| measures | `Count(event / lane, subject)` · `Duration(condition, subject)` · `Latency(trigger → response, subject)` · `Ratio(a, b)` · `Sample(input)` | shot / hit / blast lanes exist; report notifications exist |
| infrastructure | `ScenarioBrainLowering` · a by-offset overload on `HsmSharedBinder` / `SharedNodeBinder` · `ObjectiveStates` (+ `Measures`) component · `ExerciseEnded` · the inspector fed with the lowered brain's metadata | ⛔ nothing else |

## 6. Slices

| slice | builds | done when |
|---|---|---|
| **S0 (measure)** | ✅ **① measured `2026-10-31`** (`dotnet build Hrot.AI.Behaviors --no-restore`, warm, this VM): nothing changed **3.8–4.5 s** (up-to-date check) · one NEW `.hsm.json` added **7.5 s** · the same asset rebuilt again **3.8 s** · a whitespace-only edit **7.4 s** (a file change rebuilds even when the content means the same ⇒ the lowering writes the asset only when its content hash changes) · first build of the session 92 s (cold, not representative). ⚠ the probe was a copy of a small test asset; a scenario with many regions emits more code. Hot-reload SWAP time not measured. ✅ **② measured:** the rebuild + reload route is the editor's (`POST /ai/reload`, `AiBehaviorsRebuild`); no push to other nodes. ③ a brain on 8810: code says yes (`BrainTickSystem.cs:224`), no probe run yet | ③ the first S1 rail |
| **S1** | the generic `Objective`, `Trigger`, `ExerciseControl` behaviours; `ConditionRef` / `ActionRef` + the generated by-name table; conditions `AreaControlled`, `UnitsDestroyed`, `ScenarioTime`, `ObjectiveIs`, `AllObjectives`, `AnyOf` / `AllOf` / `ForUnits`; actions `Message`, `AwardPoints`, `EndExercise`; the objective / trigger entity types; `ObjectiveStatus`; `ExerciseEnded`; lanes; a brain on 8810 (first rail) | §7a plays to Blue won live on a cluster; two scenarios use the SAME behaviours with different params and NOTHING rebuilds |
| **S2** | `SpawnGroup`, orders down the chain, a reusable `CounterAttackDirector` asset with params, the utility director, the editor's rules section | the same director runs in two scenarios with different params |
| **S3** | measures + subjects (P-4), `ReportReceived`, `InputAbove` / `Sample`; SR-19's replay question answered | per-trainee and per-team results in `ExerciseEnded` |

## 7. Examples *(illustrative names)*

### 7a. Authored (`scenario.json`, the World Environment entity's section)

```json
"ScenarioRules": {
  "objectives": [
    { "id": "bridge", "side": "Blue", "title": "Secure the bridge by 10:00", "required": true, "visibleTo": ["Blue"], "points": 100,
      "success": { "cond": "AreaControlled", "args": { "area": "obj-bridge", "side": "Blue", "minUnits": 4, "maxEnemy": 0, "forSeconds": 60 } },
      "fail":    { "cond": "ScenarioTime",   "args": { "after": "10:00" } } },
    { "id": "no-civilians", "side": "Blue", "kind": "constraint", "points": -50,
      "fail": { "cond": "CiviliansHarmed", "args": { "by": "Blue", "atLeast": 1 } } }
  ],
  "rules": [
    { "id": "end", "once": true,
      "when": { "any": [ { "cond": "AllObjectives", "args": { "side": "Blue", "required": true, "is": "Achieved" } },
                         { "cond": "ScenarioTime",  "args": { "after": "10:30" } } ] },
      "do": [ { "act": "EndExercise", "args": { "outcome": "byObjectives" } } ] }
  ],
  "measures": [
    { "id": "blue-hit-rate", "subject": { "side": "Blue", "per": "trainee" }, "kind": "Ratio", "args": { "a": "lane:hit", "b": "lane:shot" } },
    { "id": "contact-report", "subject": { "team": "Blue HQ" }, "kind": "Latency", "args": { "from": "FirstSeenBy(Blue, Red)", "to": "ReportReceived(Blue HQ, Contact)" } }
  ],
  "director": { "name": "CounterAttackDirector",
                "params": { "attacker": "Red HQ", "trigger": "bridge", "waveArea": "red-spawn-east", "maxWaves": 2, "pressure": "adaptive" } }
}
```

### 7b. What it becomes *(rev 4)*

Each objective of §7a is one entity in the scenario — e.g. `{"Type": "Objective", "Name": "bridge", "Sop": {"Name": "Objective", "Params":
{"side":"Blue","title":"Secure the bridge by 10:00","required":true,"points":100,"success":{"cond":"AreaControlled","args":{…}},"fail":{"cond":"ScenarioTime","args":{"after":"10:00"}}}}}`
— the rule `end` is a `Trigger` entity, the director runs under `ExerciseControl` on 8810. ⇒ no file is generated, nothing is compiled.

### 7c. `ExerciseEnded` (the NED fact, R-4105)

```json
{ "outcome": { "Blue": "Won", "Red": "Lost" }, "endedAt": "10:12:31", "reason": "rule end: all required Blue objectives achieved",
  "objectives": [ { "id": "bridge", "state": "Achieved", "at": "09:42:10", "points": 100, "details": "held 60 s, 5 Blue / 0 Red" },
                  { "id": "no-civilians", "state": "Failed", "at": "09:55:02", "points": -50, "details": "civilian 1043 wounded by Blue Rifleman 2" } ],
  "measures": [ { "id": "blue-hit-rate", "by": { "Rifleman 1": 0.31, "Rifleman 2": 0.18 }, "side": 0.24 },
                { "id": "contact-report", "team": "Blue HQ", "seconds": 47 } ],
  "points": { "Blue": 50 } }
```

## 8. Templates, blocks and evaluation scopes *(rev 5 draft, `2026-10-31` — measured, NOT yet buildable: decisions SR-25…SR-31 await the user)*

> 🔒 **User, `2026-10-31`:** *"What I would like to avoid is to define the entities per every scenario, because the evaluation criteria might be
> common across multiple scenarios, common across multiple team members … It needs to be simple and reusable."* · *"Such a block, probably at
> every level … should need to come with its own parameters, and those entities and their objective would need to read and use those
> parameters … What is baked in is good as a default, but it always needs to be replaceable when inserted into the final place … This very
> principle is used for using behaviors on entities that the scenario can override the default parameters."* · *"Ticking cost should be no
> issue."* · *"If there are some runtime catalogs missing, we will simply make them a shared code."*

### 8.0 Why package-level params (rev 5a)

⭐ measured: the existing override layering is FLAT — `BehaviorParams.OverlayFlat` merges top-level keys only (`DESIGN_Orders_Params_Entity_Classes.md` §13.10a). Package params keep every override at the top level, so the existing mechanism serves unchanged; a deep merge into a child's nested args would be new code and ambiguous (arrays by index).

### 8.1 INVENTORY *(four measurements, `2026-10-31`: graph `.*(Template|Prefab|Recipe|EntityClass|Archetype|Composition).*` Class (35) + `.*(Parent|Hierarchy|ChildOf).*` Class (13) + grep; a throwaway probe; ⚠ coverage unchecked)*

| what exists | where | for objectives |
|---|---|---|
| ⭐⭐ **the TKB type is the one template applied at BOTH scenario load and runtime spawn** — `BehaviorProfileDto.DefaultSop` + `DefaultSopParamsJson`, applied by `BehaviorTkbTranslator.Inject` | `Tkb/Domain/BehaviorProfileDto.cs:44-49`; `BehaviorTkbTranslator.cs:113-125` | ⭐ the carrier of a block's DEFAULTS |
| ⭐⭐ **layered params = the user's principle, built** — DTO initializers ▸ asset `ParamsDefaultJson` ▸ the scenario / order JSON (only the keys it sends); scenario wins by origin rank | `DESIGN_Orders_Params_Entity_Classes.md` §13.10a (R-3001); `InitialBrainMaterializationSystem.cs:16-50` | ⭐ a block inserted with `{area, deadline}` overrides only those |
| ⭐⭐ **per-TYPE child entities, deterministic, rebuilt on every node, not saved** — the sensor pattern (`SensorChildFactory.EnsureTkbChild`, part ids 1000+i) | `PerceptionTkbTranslator.cs:78-80`; `SensorChildFactory.cs:13-44`; Sensors & Doctrine §4, §5.1 | ⭐ the shape for "every member of type X gets its evaluator" AND for expanding a block's template children |
| params carry strings, enums, nested structs, ARRAYS (authored DTO) → a `[BehaviorResolver]` packs a fixed unmanaged block (≤ 16096 B) | `BehaviorParams.cs:211-230`; `PlatoonDefendParamsJsonDto.Squads` | ⭐ lists of children / actions fit as fixed-capacity arrays |
| ⛔ managed per-instance working state — not supported (removed) | `DESIGN_Occurrence_Scoped_Storage.md` §30.29 | per-subject results = a fixed unmanaged table (`ThreatMatrixAssignmentState.cs:31` precedent) |
| ⛔ **no runtime call of a condition BY NAME** — every shared method binds per site at compile time | `SharedNodeBinder.cs:243-330`, `HsmActionDispatcher` keyed by binding | ⭐ but the CATALOGUE exists: `ActionSchemaExporter` reflects every `[SharedAiCondition]` with its `MethodInfo` + `ref` params type (`Hrot.Editor.AiShared/Blackboard/ActionSchemaExporter.cs:135-150`), built on CGF and the editor — the user: make it shared code |
| ⭐ behaviours DO run by name + JSON: SOP slot, `AssignTacticalIntent`, missions; brain intent (task / SOP / ROE as JSON) replicates on `dtBrainIntent` | `TacticalIntentResolutionSystem.cs:139-176`; `BrainIntentTranslators.cs:27` | ⭐ an objective's params reach every node already |
| a brain ticks on a bare entity (SopState + BehaviorState only, TKB 8810 too); with `gateOnAuthority` (CGF) it must be OWNED (networked) | probe `2026-10-31` (4/6, the 2 reds = the gate); `BrainTickSystem.cs:150, :224` | ✅ objective entities = networked, Brain-owned (as World Environment) |
| hierarchy: `HierarchyNode` DEAD; `VisHierarchyNode` saved, NOT replicated, unused at runtime; the command chain (`UnitSubordinate`) replicated + used (ORBAT, ROE walk, reports) | `HierarchyNode.cs:11`; `VisHierarchyNodeTranslator.cs:23`; `UnitHierarchySystem.cs:35` | ⛔ none fits: objectives in the command chain would pollute the ORBAT and the ROE walk ⇒ the parent link lives in the objective's own replicated status |
| a new replicated component ≈ 9 places (id · DataPolicy · registry · ownership group · descriptor · DDS topic · translators · mapping · packs) — latest: `Descent` bdc7827ec | `DESIGN_Airborne_Insertion.md` D-1 | `ObjectiveStatus` (state · progress · points · parent · results) |
| ⛔ no include / fragment mechanism; recipes are one-time copies | `ContentLayout.cs:91-109`; `DESIGN_Content_Layout.md` §2 | the block TEMPLATE is a new asset kind |
| Stories (per-trainee micro-exercises, evaluation filtered by story) — designed, NOT built | `docs/designs/mgmt-1/DESIGN.md` §10.0-10.1 | a story = one more evaluation scope (later) |
| the default World Environment is created when a scenario has none | `ScenarioLoadStep.cs:185-243` | ⭐ precedent: a default exercise evaluation with zero authoring |

### 8.2 The structure

```mermaid
graph TD
    EX["Exercise result · ExerciseControl on World Environment<br/>DEFAULT, nothing authored"]
    B["Blue mission · block SeizeArea<br/>scenario: area village, deadline 10:00"]
    R["Red mission · block DefendArea<br/>scenario: area village"]
    B1["Reach · reads ^area ^side ^deadline"]
    B2["Hold · reads ^area ^side ^holdSeconds"]
    B3["No civilians · reads ^side"]
    R1["Deny · reads ^area ^side"]
    M["per-member evaluators · from each unit's TKB type<br/>marksmanship · fire discipline · contact reports"]
    EX --> B
    EX --> R
    B --> B1
    B --> B2
    B --> B3
    R --> R1
    EX -.-> M
```

*What the picture shows that prose hid:* only the two mission blocks (one line each, with 2 overrides) are authored in the scenario; their
children come from the block TEMPLATE, the exercise node is a default like the World Environment, and the per-member evaluators come from the
units' TKB types — so a scenario with 40 trainees authors 2 entities.

### 8.3 Decisions *(⭐ leans — for the user)*

| # | decision | ⭐ lean |
|---|---|---|
| **SR-25** | the block TEMPLATE | a new asset kind `*.objective.json` (beside behaviour assets): declared params with defaults + a tree of child objectives whose args may name `^param` (the enclosing block's resolved value). ⛔ not a TKB type per block (TKB has no inheritance and one SOP per entity) |
| **SR-26** | inserting a block | ONE scenario entity `{"Type": "Objective", "Sop": {"Name": "ObjectiveBlock", "Params": {"template": "SeizeArea", "args": {"area": "village", "deadline": "10:00"}}}}` — the args override the template defaults key by key (R-3001's layering, unchanged) |
| **SR-27** | expanding the children | the sensor-child pattern: deterministic part entities from the template, rebuilt on every node from the replicated block params, never saved ⇒ a template edit reaches every scenario, and two nodes always agree. ⛔ open: the children's brains run on the owner — confirm the part-entity ownership path (Sensors §5.1) gives them authority there · ⭐ rev 5c (`2026-10-31`): **WHO expands — the entity CREATION path, not the scenario loader.** The sensor-child translator runs from the one translator list (`TkbTranslatorSet.Base()`, called by `NetworkSpawningSystem.cs:144` and ghost promotion) for scenario load AND runtime spawn alike ⇒ a package inserted at load, by the instructor mid-run or by a trigger expands the same way, through one path. ⛔ rejected: expansion in `ScenarioLoadStep` — a package spawned at runtime would not expand (two paths). ⛔ open: the sensor pattern is keyed on the TKB TYPE; a package is named in its params ⇒ a translator keyed on a `ObjectivePackage {template}` component, and whether the children are local parts on every node (status folded into the package's `ObjectiveStatus`) or networked entities owned by the Brain |
| **SR-28** | `^param` resolution — ⭐ rev 5a (user, `2026-10-31`: *"The objectives inside a package can carry the values on their own, but they should be able to reference a parameter of the whole package instead of hard coding the values inside."*) | a child's arg is EITHER a literal (fixed in the preset) OR `^name` (the package's param). ⭐ The OVERRIDE surface is the package's FLAT param list — exactly the existing `BehaviorParams.OverlayFlat` (DTO default ▸ preset default ▸ insertion args, top-level keys only, §13.10a) — so overriding never needs a deep merge into a child. ⇒ to make an inner literal overridable, the preset author PROMOTES it to a package param. Substitution happens once at expansion; a nested package receives its args the same way (`^` refers only to the IMMEDIATE package) |
| **SR-32** | one mechanism for every kind of preset | missions, objective sets, evaluation criteria, triggers / directors are all `*.objective.json` packages; a preset of a preset (e.g. `SeizeVillageFast` = `SeizeArea` with `deadline: 30 min` fixed) is a package containing one package with some args fixed — no inheritance mechanism |
| **SR-29** | conditions by name | a shared runtime `SharedConditionTable` built once at startup from the existing `ActionSchemaExporter` catalogue (moved to shared code): name → `MethodInfo` → a typed delegate (made once per params type) + its `P` type; a `ConditionRef` = name + args JSON (+ optional `subject` = the unit it is evaluated AS — so self-relative conditions like `ElementReported` work) packed into a fixed byte area of the objective's block. ⇒ all 104 conditions usable at once. ⛔ rejected: one behaviour per objective kind — every new condition would need a new behaviour |
| **SR-30** | per-member evaluation | each unit TKB type names its evaluation template(s) (e.g. Rifleman → `RiflemanCriteria`); the sensor-child pattern attaches one evaluator child per member, everywhere, unauthored. Thresholds: template default ▸ the side's mission block (a `memberArgs` override) ▸ the unit's own scenario entry |
| **SR-31** | state and tree on the wire | a new replicated `ObjectiveStatus` (state · progress · points · parent network id · up to N per-subject results) — the ~9-place recipe; ⛔ not the command chain, ⛔ not `VisHierarchyNode` (unreplicated) |

### 8.4 The package format and its editing *(⛔ SR-34 / SR-35 SUPERSEDED by §9 rev 6; SR-33's params-form reuse stands; rev 5b draft, `2026-10-31` — leans for the user)*

> 🔒 **User, `2026-10-31`:** *"I thought I would be able to somehow edit the objective parameters using the existing means in the scenario
> editor … if we are using the behavior infrastructure with existing debug and monitoring tools, we could reuse the editing part as well."*

| measured | where | so |
|---|---|---|
| ONE params form edits every behaviour's params in the scenario editor (AI section task / SOP rows), the mission panel and orders — a StructEdit document over the params TYPE (a CLR type), map-pickable fields by type | `Hrot.Presentation/Behavior/BehaviorParamsForm.cs` (CE-3043 / CE-1023); Sensors & Doctrine §7.6, §7.6b | ⭐ if a package IS a named behaviour with a params DTO, inserting and parameterising it needs NO new editor |
| "pick a condition / action / blueprint, then edit its args as that primitive's own params type" — shared, also on CGF | `Hrot.Editor.AiShared/Inspector/ActionBinding/BehaviorActionBindingEditor.cs`, `ActionBindingSources.cs`, `ActionSchemaExporter` | ⭐ the `ConditionRef` field editor reuses it |
| a params TYPE from data: `.params.json` → a generated DTO (initialisers = defaults) | `ParamsDtoAsset.cs`; §13.10a | ⭐ a package's params, declared once per package (a shared asset, not per scenario — R-4112) |
| ⛔ no schema-driven form (a form over JSON with no CLR type) | graph `.*(Schema\|Dynamic\|JsonNode\|Untyped)(Form\|Binding\|Document\|Editor\|Drawer).*` = 0; grep | ⇒ do NOT invent one: give every package a params type |

| # | decision | ⭐ lean |
|---|---|---|
| **SR-33** | a package IS a named behaviour | a package `SeizeArea` registers under its name with `JsonParamsDtoType` = its params DTO (from its `.params.json`) and the generic package runner as its machine; inserting it = an ordinary scenario entity `Sop: {"Name": "SeizeArea", "Params": {"area": …, "deadline": "10:00"}}` ⇒ the scenario editor's EXISTING AI section and params form edit it, with map picking for the area, unchanged |
| **SR-34** | the package FILE | `assets/objectives/SeizeArea.objective.json` = a scenario FRAGMENT: `{"$meta": {"kind": "objective-package"}, "params": "SeizeAreaParams", "entities": [ …objective entities, each with Sop {Name, Params} exactly as in scenario.json… ]}` ⇒ authored by opening it in the SCENARIO EDITOR (same entity list, inspector, params form) — ⛔ no new editor |
| **SR-35** | binding a child's field to a package param | ⛔ not `"^deadline"` inside a typed field (a float field cannot hold a string). ⭐ A sibling map per entity: `"Sop": {"Name": "Objective", "Params": {…literals…}, "Bind": {"fail.args.after": "deadline"}}` — the form shows a "bound to package param" marker on that field, the precedent being the HSM editor's per-state param bindings (E3b, `EmitStateParamBindings`). Expansion applies `Bind` after the literals |
| **SR-36** | the `ConditionRef` field | one field editor: the condition picker (`ActionSchemaExporter` entries) + a nested StructEdit over the picked entry's params type (a CLR type known at runtime) — ⛔ unmeasured: whether StructEdit nests a runtime-typed sub-document inside a parent document |

**Still to measure before buildable:** ① can the scenario editor open a fragment file (entities + `$meta`) as a document and save it back? · ② can StructEdit
nest a runtime-typed sub-document (SR-36)? · ③ can a field show a "bound" marker (SR-35) without a new drawer? · ④ do expanded child objectives get
authority on the owner (SR-27)?

## 9. Scenario MACROS — the general mechanism *(rev 6 draft, `2026-10-31` — supersedes §8.4's package file, SR-34 / SR-35; leans for the user)*

> 🔒 **User, `2026-10-31`:** *"Isn't that SOP name the name of the behavior that should evaluate the objective?"* · *"Could we generalize this
> package parameters into scenario parameters so we can get scenarios with parameters that looks like exactly the same principle as for
> packages."* · *"Scenario editor is now saving the whole scenario and i would keep it like that. Those packages might need to be a special type
> of assets, like scenario macros, stored likely in special folder. I would like to make this macros a general concept that is not used just
> for objective, because it might be used also for inserting whole parts of the scenarios."* · *"We need to store just the reference to the
> macro, which is itself living as an independent scenario-like assets in different folder."*

### 9.1 Measured

| fact | where | so |
|---|---|---|
| a scenario entity = a GUID key → components by name: `SimTransform`, `NetworkIdentity`, **`TkbIdentity {TkbType: 305}`** (the type — ⛔ there is no `Type` attribute), `EntityInfo {Name, ForceId}`, brain slots **`Behavior`** (task) / **`Sop`** as `{Name, Params (a JSON string), Origin}`; the file = `$meta` · `header` · `entities` | `scenarios/uc-urban-battle/scenario.json` | §8.4's example was wrong on `Type` — corrected below |
| a `.params.json` asset = `{AssetId, Name, Fields: [{Name, TypeId, DefaultJson, Comment}]}` → a generated DTO, defaults as initialisers | `assets/behaviors/Params/Tests/T41Params.params.json`; §13.10a | ⭐ the package / macro params type |
| ⭐⭐ **derived entities are already kept out of saves**: `ScenarioIgnoreTag` (the serializer skips the whole entity; 22 users) + `PartMetadata {ParentEntity, InstanceId}` (the parent + part id, also the wire key) — the sensor children use both (CE-3045: *"rebuilt … on load, so saving it too duplicated it"*) | `Fdp.Toolkits/Scenario/ScenarioIgnoreTag.cs`; `Replication/Components/PartMetadata.cs:14`; `SensorChildFactory.cs` | ⭐⭐ the answer to *"how do we know what came from a macro"* — it is BUILT |

### 9.2 The model

```mermaid
classDiagram
    direction LR
    class ScenarioFile { <<existing, + params>> header + params type · entities (top level) }
    class MacroAsset { <<new asset kind, scenario-like, own folder>> header + params type · entities }
    class MacroInstance { <<new component, SAVED>> macro name · args JSON (overrides) }
    class MacroPart { <<derived entity, NOT saved>> ScenarioIgnoreTag · PartMetadata(instance, index) }
    class ParamsDto { <<existing .params.json>> fields + defaults }
    class Bind { <<new, per entity in a macro>> own field from macro param }
    ScenarioFile *-- MacroInstance : top-level entity carrying it
    MacroInstance ..> MacroAsset : by name
    MacroAsset ..> ParamsDto : declares
    MacroAsset *-- Bind
    MacroInstance ..> MacroPart : expands into
    MacroPart ..> MacroInstance : nested macros recurse
```

*What the picture shows that prose hid:* ONE file shape serves a scenario and a macro — both have a params type and entities — so a scenario
with parameters IS a top-level macro. What is saved is only the top-level entities and the `MacroInstance` references; every expanded entity
carries the tag the serializer already skips.

| # | decision | ⭐ lean |
|---|---|---|
| **SR-37** | the brain slot names the EVALUATING behaviour (⭐ the TASK slot — SR-51) | ⭐ rev 6: one small generic behaviour per objective KIND — `ReachArea`, `HoldArea`, `ClearArea`, `DestroyUnits`, `ProtectUnits`, `NoCiviliansHarmed`, `ReportWithin`, `HitRatio`, `CompositeObjective` (all / any / weighted over its children) — each with a TYPED params DTO. ⇒ the existing params form edits every field; no `ConditionRef`, no runtime-typed sub-form, no by-name table (SR-29 / SR-36 drop to an optional later `ConditionObjective` for the long tail). ⛔ cost: a new kind = a new reusable behaviour (rare, shared, not per scenario) |
| **SR-38** | a macro asset | `macros/<name>/macro.json` (its own folder in `ContentLayout`, like `scenarios/` and `recipes/scenarios/`) — the SAME shape as `scenario.json` (`$meta` · `header` · `entities`) + `header.params` naming its `.params.json`. Authored by opening it in the scenario editor, which saves the whole file as today |
| **SR-39** | inserting a macro | a top-level scenario entity with a `MacroInstance {macro: "SeizeArea", args: {"area": "<area entity id>", "side": "Blue", "deadline": "10:00"}}` component; the args override the params DTO's defaults key by key (`OverlayFlat`). Editable in the inspector as a params form over the macro's params type |
| **SR-39a** | ✅ the INSTANCE is an ordinary entity (user, `2026-10-31`: *"Couldn't a scenario macro be saved as an entity that gets replaced by the macro? … that would avoid the need for special treatment like insert or delete as adding and deleting entities is handled already. picking the macro file would be an edit on the parameters of that entity, again in the details panel"*) | a "Scenario macro" entity type, placed / deleted / moved / copied like any entity; selecting it shows in Details: the MACRO picker (the scenario picker over `macros/`) + the ARGS form built from the picked macro's `header.params` (defaults shown, overridable). Changing the macro or an arg re-expands that instance; deleting it removes its expanded entities ⇒ ⛔ no special insert / delete command |
| **SR-69** ✅ CONFIRMED `2026-10-31` (user: *"Group A confirmed."*) | positions inside a macro | ⭐ lean: RELATIVE to the instance entity's transform — a macro's entities are authored around its origin and the instance's position + heading place them, so one platoon macro can be dropped anywhere; an objective macro with no geometry ignores it |
| **SR-70** | the instance entity is on the MAP (user, `2026-10-31`: *"the scenario macro entity needs to have the position component as well to be displayed on the map and clickable from the map"*) | ✅ it carries a `SimTransform` — the anchor SR-69's relative positions use — and a map symbol of its own (a "macro" marker), so it is selected by a click and dragged like any entity; dragging it moves everything it expanded. ⭐ ORBAT: shown (it is on the map), its expanded entities beneath it. ⛔ unmeasured: how a non-unit positioned entity gets its symbol today (the visual family / `EntityClassShapeLibrary` path) |
| **SR-71** | ✅ the World Environment is SAVED IN THE HEADER (user, `2026-10-31`: *"I don't think we should save the world entity state into the scenario as entity. I still think the translator for this entity components should write this into the header of the scenario file."* → *"Yes, please file it and file the task."*) | at RUNTIME it stays an entity (it replicates the clock and global weather — Weather W-0); only its SAVED form moves: save writes its components into `header.world` (one entry per component, by the SAME auto-serializer — a future component such as the rules lands there with no new translator) and skips the entity; load creates the entity from `header.world` (no section ⇒ today's C-3 defaults); the distributed merge takes `header.world` from the slice of its owner (the Brain — invariant I4, one source for globals); the 3 scenarios holding it as an entity are migrated, no compatibility path. Weather REGIONS stay entities. Precedent for a non-entity file section: `TerrainObjects` (`ScenarioMergeCore.cs:40`). Tracker **CE-4154** |
| **SR-40** | binding a part's field to a macro param (⭐ stored as SR-57's `ParamBindings` component) | per entity in the macro: `"Bind": {"<slot>.<field>": "<macro param>"}` — e.g. `"Behavior.area": "area"` = *this entity's task param `area` takes the macro's `area`*. Flat (both sides are top-level names, SR-37's typed DTOs), applied after the literals at expansion |
| **SR-41** | scenario parameters | the scenario `header.params` names a params type; its args live in the header (or are passed by whoever starts the run); top-level entities `Bind` to them exactly as macro parts do ⇒ ONE principle at every level |
| ~~**SR-42**~~ ⛔ WITHDRAWN rev 6a (§9.3 — parts cannot be full entities) | who expands, and what is saved | the entity CREATION path (SR-27): creating a `MacroInstance` entity creates its parts, each with `PartMetadata {instance, index}` + `ScenarioIgnoreTag`; save writes top-level entities + instances only. Nested macros recurse (a part may carry a `MacroInstance`) |
| **SR-43** | editing an expanded part in the editor | ⭐ v1: shown under its instance, READ-ONLY, with *"from macro SeizeArea — edit the macro or its args"*. ⏭ later: per-instance overrides of a part's field |

**Still to measure before buildable:** ① can a `PartMetadata` part be a FULL entity (a unit with transform, brain, its own replication) or only a light child like a sensor? · ② deterministic network ids for parts across nodes · ③ the scenario editor opening a file from `macros/` · ④ the editor's entity tree showing parts under their instance · ⑤ the expanded objectives' brain authority (SR-27).

### 9.3 Rev 6a (`2026-10-31`) — measured: parts are the WRONG tool; macro entities are full, transient entities

| measured | where | so |
|---|---|---|
| `PartMetadata` IS the ECS side of NED's MULTI-INSTANCE descriptors (turret pose / weapon task, EQS sensor config / results): the key is (parent network id, instance id); instance ids are RANGES agreed in code (mounts 1…N, behaviour sensors 1–999 allocated, TKB sensors 1000+i, turrets 2000+i), not a protocol enum | AQ79 §0.10 (`Architect_Question_79_One_Ownership_Truth.md:206-240`); `BDC_NED_SST_Descriptor_Rules.md:29,49-57`; `TurretParts.cs:15-45` | — |
| a part has NO `NetworkIdentity`; authority resolves to its PARENT (`AuthorityExtensions.cs:23`), ownership recompute skips it (`OwnershipRecomputeSystem.cs:251`), it dies with the parent (`SubEntityCleanupSystem.cs:24`), scenario extraction folds it into the parent (`StagingEntityExtractor.cs:292-310`) | as cited | ⛔⛔ **SR-42's `PartMetadata` is WITHDRAWN** — a macro entity (a unit, an objective) would lose its own authority, life and save identity |
| ⭐⭐ **the save exclusion already works per FULL entity**: `EntityCreationRequest.IsTransient` → `SpawnEntityCommand.IsTransient` → every node stamps `ScenarioIgnoreTag`; TKB children inherit it; save ⇔ primary owner AND NOT `ScenarioIgnoreTag` | `EntityCreationRequest.cs:~95-125`; `NetworkSpawningSystem.cs:199`; `ScenarioSaveCore.cs:16`; Node Roles §7.3 (R-140) | ⭐ macro entities = ordinary entities created TRANSIENT, each with its own network id |
| network ids: ONE allocator per domain (orchestrator), blocks of 100, reset to 1000 per world; ⭐ **scenario load RE-ALLOCATES every id** (old → new map, `PreAllocatedNetworkId`) — the file's ids only look stable because of the reset | `DdsIdAllocatorServer.cs:119-136`; `StagingEntityExtractor.cs:~245-262`; `DESIGN_Deterministic_Network_Ids.md` §11b | a file id is a FILE-LOCAL key either way |
| the GUID entity keys are REGENERATED on every save (`Guid.NewGuid()` per entity) — a file-local key used only to resolve references inside the file (`IGuidResolver`); all 1059 entities of the 127 scenario files carry a `NetworkIdentity` | `ScenarioSerializer.cs:171, :688-730`; measured over `scenarios/` + `recipes/scenarios/` | ⭐ keying by the network id loses nothing — both are file-local; the number is shorter and is what `EntityRef` already uses (R-184) |
| `EntityRefRemap` rewrites every `EntityRef` (nested, lists) by the old → new map at load; an id not in the map is left as is | `EntityRefRemap.cs:25`; `DESIGN_Entity_Reference.md` | ⚠ a reference to a macro-made entity (never saved) would dangle — or, with ids reset to 1000, hit an unrelated entity |
| the scenario picker enumerates one root recursively | `ScenarioEnumeration.EnumerateRelPaths` | ⭐ macros = a second root |

| # | decision | ⭐ lean |
|---|---|---|
| **SR-42′** | who creates a macro's entities, and what is saved | ⭐ rev 6a: creating a `MacroInstance` entity issues ordinary `EntityCreationRequest`s for its entities with **`IsTransient = true`** (own network ids from the allocator, own authority, own replication, own brain); save writes top-level entities + the instances. ⛔ not `PartMetadata` (above) |
| **SR-44** | macro / scenario params | declared INLINE in the macro's or scenario's `header.params` (rows `{Name, TypeId, Default, Comment}`, the `.params.json` row shape) — ⛔ never compiled: only the expander reads them, copying values into the children's JSON params; each `Bind` is type-checked once against the child's compiled DTO field. ⛔ open: a small "N typed fields" form reusing the per-type field editors (whether they work without a containing type is unmeasured) |
| **SR-44a** | ✅ MEASURED `2026-10-31` — the inline params list IS editable with existing parts (user: *"not an issue if the editor doesn't support the editing without the C# DTO. We will teach the editor"*) | the variable editor already edits ONE value of a runtime-chosen type: `VariableEditModal` wraps it in a generic one-field `ScalarEditBox<T>` (because `CreateLeafBinding` needs a member), then draws only the wrapper's child, renamed, through the public `ComponentEditDrawer.DrawEditNode` (`Hrot.Editor.AiShared/Variables/VariableEditModal.cs:340-380`); `EditNode` / `EditDocument` have public constructors and every per-type editor (`EntityRef` / `PickableGeoPoint` map picking, `FixedString*`, `Guid`, `UtilityDecisionRef`) is a registered `ICustomFieldEditor` that takes ANY `IValueBinding` (`ComponentEditServices.cs:17-27`, `StructEdit.Core/Plugins/ICustomFieldEditor.cs`). ⇒ a macro's params form = one `ScalarEditBox<T>` row per declared param, `T` resolved from its `TypeId` — no generated type, no new editor kind |
| **SR-45** | ids inside a macro | a macro file's network ids are MACRO-LOCAL keys; each instance remaps them to fresh ids through the existing `EntityRefRemap` old → new map, so two instances never collide and refs between a macro's own entities stay right |
| ~~**SR-46**~~ ⛔ SUPERSEDED by SR-59 (§9.5) | a reference INTO a macro from outside | v1: ⛔ refused at save (a saved `EntityRef` must point to a saved entity); later: a qualified ref `{instance: <instance net id>, local: <macro-local id>}` resolved after expansion |
| **SR-47** | two links on an objective | `ObjectiveStatus.Subject: EntityRef` (WHAT it is about — the World Environment = the scenario, a commander = his team, a soldier = that soldier) and `ObjectiveStatus.Parent: EntityRef` (which objective it COMPOSES into). ⭐ They differ in the common case: a per-member criterion's subject is the soldier, its parent is "Blue member evaluation" |
| **SR-48** | objectives replicate | yes — they are full entities: the instructor station and the trainee's HUD sit on other nodes, NED frontends (R-4105), the map on every node, the recording, a brain hand-over to another node |
| **SR-49** | objectives have a position | a `SimTransform` used only for drawing / picking on the map (an area objective at its area's centre); nothing else reads it |
| ~~**SR-50**~~ ⛔ SUPERSEDED (`DESIGN_Scenario_Entity_Keys.md` rev 2: the key stays a — now stable — GUID) | scenario entity KEY = the network id | ⭐ lean YES, as part of macros: `"entities": {"9800": {…}}` instead of a fresh GUID per save; file-internal references (`VisHierarchyNode` today via `IGuidResolver`) move to network ids like `EntityRef`. ⛔ cost: the serializer's resolver + a one-time migration of the 127 files + their tests. ⛔ open: any reader keyed on the GUID string (the editor's selection, the director, checks.json) — measure before deciding |
| **SR-51** | WHICH brain slot runs the evaluator — ⭐ rev 6b (user, `2026-10-31`: *"SOP is the standard operational procedure and this is just a default behavior the entity is doing with special handling … there is also one main behavior that can be assigned"*) | ⭐ the **TASK slot** (scenario JSON `"Behavior": {Name, Params, Origin}`; TKB default `BehaviorProfileDto.DefaultBehaviorHash`). Measured why not the SOP slot (`DESIGN_Decision_Layer.md` glossary :1215, rules :1220-1225, §SOP channels :1448-1452): the SOP is the unit's OWN always-ticking logic (what to do when idle + reactions, woken or every 0.2 s), its slot REFUSES channel-writing behaviours and the editor's SOP row offers only SOP-shaped trees (CE-1019); the task is *"what the unit was told to do — one at a time"* — exactly an objective's evaluation, which an instructor may also re-assign. ⇒ every earlier `Sop` in this document reads as `Behavior` (the use of the SOP slot was not intentional). ⭐ Consequence: a soldier's own slots are taken (his orders, his doctrine) ⇒ per-member evaluators are SEPARATE entities with `Subject` = the soldier (SR-47), never a brain on the soldier. ⛔ gap: the TKB default task is a bare HASH (no default params JSON, unlike `DefaultSopParamsJson`) — 🔒 user: *"the anomaly is historical only, not desired"* ⇒ tracker **CE-4151** |

### 9.4 The two links in a hierarchy *(rev 6c draft, `2026-10-31`)*

> 🔒 **User, `2026-10-31`:** *"How will you express the two links, the subject and the parent, for the criterias or objectives in a hierarchy
> structure?"* · *"the ORBAT serves for quick jump to an entity … there shouldn't be any virtual entities that are not seeable on the 3D map. So
> the orbat needs to be parameterized."*

```mermaid
graph TD
    subgraph obj["the OBJECTIVE tree — edges are Parent"]
        EX["Exercise result · subject: World"]
        BM["Blue mission · subject: Blue HQ"]
        RE["Reach · subject: Blue HQ"]
        HO["Hold · subject: Blue HQ"]
        BE["Blue members · subject: Blue HQ"]
        S1["Squad 1 evaluation · subject: Squad 1"]
        R1["Rifleman 1 marksmanship · subject: Rifleman 1"]
        R2["Rifleman 2 marksmanship · subject: Rifleman 2"]
        EX --> BM
        BM --> RE
        BM --> HO
        EX --> BE
        BE --> S1
        S1 --> R1
        S1 --> R2
    end
    subgraph orbat["the COMMAND chain — existing, unchanged"]
        HQ["Blue HQ"] --> SQ["Squad 1"]
        SQ --> M1["Rifleman 1"]
        SQ --> M2["Rifleman 2"]
    end
    R1 -. "Subject" .-> M1
    S1 -. "Subject" .-> SQ
    BM -. "Subject" .-> HQ
```

*What the picture shows that prose hid:* two trees, never merged — the objective tree's edges are `Parent`; `Subject` is a CROSS link into the
real world (the command chain, the World Environment). The per-member branch MIRRORS the command chain, which is why its parents can be
DERIVED instead of authored.

| # | decision | ⭐ lean |
|---|---|---|
| **SR-53** | the component | `ObjectiveLinks {Subject: EntityRef, Parent: EntityRef}` on every objective entity — saved for top-level objectives, replicated with `ObjectiveStatus`; both network ids (R-184) |
| **SR-54** | `Parent` — three sources, first wins | ① EXPLICIT — inside a macro, a macro-local id (remapped per instance, SR-45); at the top level, an `EntityRef` · ② the macro's ROOT objective — a macro marks one entity as its root; every other objective without an explicit parent hangs under it, and the root hangs where the instance is plugged in · ③ DERIVED from `Subject` via the command chain — the evaluation whose subject is the nearest COMMANDER up `UnitSubordinate` (Rifleman 1 → Squad 1's evaluation → Blue members), else the side's, else the exercise result. ⇒ per-member evaluators attached by TKB type need no authored parent at all; the command chain is READ, never written (it keeps only units) |
| **SR-55** | `Subject` | a macro param by default (`^subject`, defaulting to the entity the instance is inserted on); a TKB-attached evaluator's subject = the unit it was attached for; the exercise result's = the World Environment |
| **SR-56** | how a user sees them | an **Objectives view** — the tree by `Parent`, each row naming its subject (click = jump to the subject on the map, the ORBAT's own jump); the subject's inspector lists *"objectives about this unit"* (reverse lookup by `Subject`) |
| **SR-52** ✅ CONFIRMED `2026-10-31` (user: *"Group A confirmed."*) | the ORBAT — ⭐ refined `2026-10-31` (user: *"I have never seen any sensor displayed in the orbat"* · *"Area entities might need their own root in the orbat tree … Orbat item is also used as a landing point for entity picker, which might be a good reason for keeping the areas"*) | measured: the ORBAT lists every entity WITH `EntityInfo` (`ScenarioOrbatAdapter.cs:61`); sensors are absent only because sensor parts carry no `EntityInfo` — an accident of their shape, not a policy. Objective entities WILL carry `EntityInfo` (a label + a side) ⇒ an explicit inclusion policy is needed. ⭐ lean: units and platforms under their sides (as today) · areas under their OWN root (*Areas*) — kept, since the ORBAT is a landing point of the entity picker · objectives, triggers, the World Environment excluded (they live in the Objectives view, SR-56). Tracker **CE-4152** |
| **SR-57** ✅ CONFIRMED `2026-10-31` (user: *"Group A confirmed."*) | where a macro entity's BINDINGS are saved — user, `2026-10-31`: *"we might need a new field on the behavior intention component for those bindings, correct? Otherwise, they would not be saved in the scenario."* | ⭐ yes, a new saved field — but ⭐ NOT on the brain intent alone: a general macro binds OTHER components too (a unit's `EntityInfo.ForceId` ← `side`, its `SimTransform` position ← `position`, a child's `Behavior` params ← `area`). ⇒ ONE generic per-entity component `ParamBindings {"<Component>.<field path>": "<macro param>"}` (e.g. `"Behavior.Params.area": "area"`, `"EntityInfo.ForceId": "side"`), saved and loaded by its own scenario translator, so it round-trips when a macro file is opened and saved in the scenario editor; the expander applies it when instantiating and does NOT copy it onto the created entities. The brain slot record (`SavedBrainSlot {Name, Params, Origin}`) stays as it is |

### 9.5 References across macro scopes — by NAME PATH *(rev 6d draft, `2026-10-31`)*

> 🔒 **User, `2026-10-31`:** *"if in the exercise objective there is a reference to an entity in the scenario, but that entity comes from a
> scenario macro which is using its own internal IDs, and the exercise objective is also a scenario macro using its own scope of IDs. These two
> can never reference each other reliably. Would we use some names for referencing?"*

| measured | where |
|---|---|
| scenario tooling ALREADY names units: the checks verifier (*"no unit named 'X' in <scenario>"*), the film director's anchors and takes (*"no unit named 'X' (alike: …)"*) | `ScenarioVerification.cs:764`; `AnchorBook.cs:226`; `TakeStore.cs:289` |
| `EntityInfo.Name` is unique within 114 of 117 scenarios; duplicates in `hill-attack` / `hill-attack-close` (`M1 Abrams` ×2), `test-fire` (`M2 Bradley IFV` ×2) | measured `2026-10-31` |

```
scenario
 ├─ "Blue Platoon"      MacroInstance(ReinforcedPlatoon)   → creates "Squad 1", "Squad 1/Rifleman 2", …
 └─ "Blue mission"      MacroInstance(SeizeArea, args: { subject: "Blue Platoon", area: "Village" })
                                                             ↑ a PATH, resolved after expansion to a network id
```

| # | decision | ⭐ lean |
|---|---|---|
| **SR-58** | a macro is CLOSED | inside a macro, references use its own local ids (remapped per instance, SR-45); ⛔ a macro never reaches OUT — anything outside it comes IN through its params (the exercise macro gets its `subject` / `area` as params) |
| ~~**SR-59**~~ ⛔ SUPERSEDED by SR-63 (names are labels; reusable refs use `RefKey`) | a reference that crosses scopes = a NAME PATH | `<instance name>/<entity name>`, nested deeper as macros nest (`Blue Platoon/Squad 1/Rifleman 2`); a top-level entity is just its name. An `EntityRef`-typed param or field accepts a network id OR a path string; the path is resolved ONCE to a network id — after all macros of a load are expanded, or at insertion for a macro added at runtime — through a path index built from a `MacroOrigin {instance, local name}` stamp on every created entity |
| **SR-60** | names unique per scope | a macro's entity names are unique within the macro; instance names unique within their parent scope; a duplicate that a path references is refused at load with the alike-names hint the director already gives. (Today 3 of 117 scenarios have duplicates — they only matter once a path names them) |
| **SR-61** | groups by the command chain, not by lists | an objective about "all of Blue Platoon" names the platoon's path as its `Subject` and quantifies over its members through the command chain (`ForUnits(members of …)`), so a macro's internal roster can change without touching the objective |
| ~~**SR-62**~~ ⛔ SUPERSEDED by SR-63 | ⚠ was OPEN — the user ASKED whether GUIDs are the answer (*"It was a question, not a imperative"*); analysis in `DESIGN_Scenario_Entity_Keys.md` §0b, lean: name paths (SR-59) — candidate: a STABLE GUID | every authored entity keeps one `EntityGuid` for life; a macro-made entity's is derived `uuidv5(instance, local)` — so the exercise macro's `subject` param holds the GUID of *Blue Platoon's Squad 1* that the editor's picker wrote, resolved to the live network id after expansion. SR-58 (macros are closed, refs come in as params) and SR-61 (groups via the command chain) stand. 📄 [`DESIGN_Scenario_Entity_Keys.md`](DESIGN_Scenario_Entity_Keys.md) rev 2 G1–G6 |
| ~~SR-46~~ | ⛔ SUPERSEDED by SR-59 | the qualified `{instance, local id}` reference is replaced by the name path — ids are file-local and change per load; names are what an author reads and what the tooling already uses |

⭐ Rejected — **macro EXPORTS** (declared public aliases) for v1: the macro's entity names already are its interface; add exports only if renaming internals breaks references in practice.

### 9.6 Three reference forms; `RefKey` *(✅ user, `2026-10-31`: "ref key is fine. recorded.")*

> 🔒 **User, `2026-10-31`:** *"Entity name is by no means a stable identifier during the authoring."* · *"sometimes the objectives might be written
> in a generic way referencing some subjects that are expected to be in the scenario, but every scenario might have different ones … So the
> entity would need some kind of extra key which is not the entity's label but is used for referencing it."*

| # | decision (✅ approved) |
|---|---|
| **SR-63** | three reference forms: **exact** (the entity's GUID — *this one entity*) · **key** (`RefKey` — *whatever entity this scenario gives that key*) · **query** (side, class, command chain, `TacticalDesignation` — *the squad leader of @Squad1*, *all members of @BluePlatoon*). ⇒ `EntityInfo.Name` is a LABEL only, never a reference; ⛔ SR-59 (name paths) and SR-62 (GUIDs for cross-scope refs) are superseded |
| **SR-64** | `RefKey` — an author-assigned attribute on an entity (`"RefKey": "BlueHQ"`), separate from the label, set deliberately, rarely changed, unique within its scope; most entities have none. Precedent: `TacticalDesignation` (`Fdp.Core/CommandHierarchy/TacticalDesignation.cs`) is a role key scoped to one command group, a fixed enum on the wire |
| **SR-65** | reusable macros reference keys THROUGH PARAMS — `subject` defaults to `"@BlueHQ"`, a scenario with another key overrides it at insertion; lookup is LEXICAL: inside the enclosing macro instance first, then outward to the scenario; from outside, a key inside an instance is qualified by keys only (`@BluePlatoon/SL`) |
| **SR-66** | a missing key fails LOUDLY: the objective stays inactive with *"no entity with key 'BlueHQ' here"* (the director's *"no unit named …"* shape); duplicate keys in one scope are refused at save and load |
| **SR-67** ✅ CONFIRMED `2026-10-31` (user: *"Group A confirmed."*) | per-member thresholds — who may override a criterion's params | ⭐ nearest wins, walking UP from the evaluator's SUBJECT: the unit's own scenario entry ▸ the nearest enclosing team / squad / platoon macro instance (found up the command chain) that sets that param in its `memberArgs` ▸ the side's ▸ the scenario's ▸ the criterion template's default. Same lexical rule as `RefKey` lookup (SR-65); ⛔ never a global table |
| **SR-68** | where a macro's (and a scenario's) PARAMS DECLARATION is shown and stored — ⭐ rev `2026-10-31` b (user: *"I am not aware that a scenario has a world entity saved as such. What world entity carries is usually written in the scenario header, and I think the scenario macro parameters should be the same case."*) | measured: the World Environment IS saved as an ENTITY (`ua-weather-regions`, `ua-weather-sensing`, `ua-night-sensing` carry `{NetworkIdentity, ScenarioClock, TkbIdentity, WeatherConditions}`; the others get a default at load and save it next time — Weather W-0 / C-3); the header holds `subsystemType · schemaVersion · terrainName · description` (`ScenarioHeader.cs:33`, `ScenarioHeaderDto.cs`). ⭐ lean (user's model): the declaration is FILE metadata ⇒ `header.params` (rows `{Name, TypeId, Default, Comment}`), by extending `ScenarioHeader` / `ScenarioHeaderDto` — ⛔ not a component on the World Environment entity (a macro file's World Environment would be a never-instantiated oddity). Shown and edited in a **Parameters** map-background Details view (the `DetailsViews.Add` pattern beside *Scenario* / *Zones*, `EditorSubsystem.cs:3141-3200`) with SR-44a's `ScalarEditBox<T>` rows. ⚠ small new code: the header gains an edit path (no view edits the header today — unmeasured) |

⚠ The film director's takes keep naming units in a RECORDING by name for now — a GUID would need to exist at runtime and in the recording; a separate decision.

### 9.7 Expanding a scenario macro at LOAD — the sequence *(`2026-10-31`; the gaps are marked ⚠ GAP)*

> 🔒 **User, `2026-10-31`:** *"the scenario files are distributed by the orchestrator before the scenario load. And now we will need to distribute
> not just a single scenario file, but also the macros … we distribute all the macro scenarios, because we do not know upfront the whole dependency
> chain. So we might distribute a zipped package of all the macros together with the scenario."*

```mermaid
sequenceDiagram
    participant OR as Orchestrator (prefetch)
    participant ST as every node's staging dir
    participant B as Brain (scenario load)
    participant CP as entity creation path
    participant N as every node
    OR->>ST: copy the scenario dir AND the macros package (ALL of macros/, one zip)
    B->>CP: create the scenario's own entities (as today)
    CP->>CP: the MacroInstance entity is created: read its macro from staging
    CP->>CP: resolve params = header defaults overlaid by the instance args
    loop each entity of the macro
        CP->>CP: apply ParamBindings · position relative to the instance · GUID = uuidv5(instance, local) · remap its internal refs
        CP->>N: an ordinary TRANSIENT creation request (replicated, never saved), stamped with its origin
    end
    Note over CP: a created entity that is itself a MacroInstance repeats the loop (nesting)
    B->>B: after ALL expansion: resolve RefKey references (nearest scope outwards), a missing key fails loudly
    Note over B: save later writes the scenario's own entities + the MacroInstance (args), never the expanded ones
```

*Caption:* expansion is NOT a loader step — it happens where the instance entity is created, so a macro inserted at RUNTIME (a trigger's
`InsertMacro`, the instructor) expands by the same path (SR-27). The orchestrator only has to make the macro files present beforehand.

| # | decision / ⚠ GAP | lean — ✅ ACCEPTED by the user `2026-10-31` (*"Gap-X Leans accepted."*) |
|---|---|---|
| **SR-80** | distributing the macros | the existing prefetch (`StorageGatewayModule.PrefetchScenarioAsync:229`, `DESIGN_Artifact_Staging.md` — *"an artifact a scenario NAMES must arrive before the scenario LOADS"*) also stages the WHOLE `macros/` tree as ONE zip (the TKB already travels as a zip, cached on its timestamp) — ALL macros, because the dependency chain is not known without expanding (user) |
| ⚠ **GAP-1** | WHEN is expansion finished? | RefKey resolution (step "after ALL expansion") needs an end point, but creation is asynchronous across nodes — lean: hook the load gate that already waits for the scenario's entities to be constructed (CE-4087) and count the expanded entities into it |
| ⚠ **GAP-2** | which nodes READ the macro files | the Brain (it owns the instances) and the editor (it expands to show the parts read-only) — both are in the prefetch's node set; a node that only receives replicated entities never reads a macro |
| ⚠ **GAP-3** | a missing macro, a wrong `TypeId` in a binding, a cycle (macro A contains B contains A) | fail the LOAD loudly naming the instance — where the check lives (the creation path or a validation pass before load) is undecided; a cycle needs a depth guard |
| ⚠ **GAP-4** | editing an instance's args in the editor | re-expand: destroy its parts, create them again — their GUIDs stay the same (derived); undecided whether references from OUTSIDE into those parts survive the re-expansion within one session (they should, by GUID) |
| ⚠ **GAP-5** | ids | the expanded entities get fresh network ids from the allocator like any creation; a macro's internal ids are only its file-local keys (SR-45) |

## 10. Objective KINDS, measures, status, the result *(rev 7 draft, `2026-10-31` — leans for the user)*

### 10.1 The data sources — measured `2026-10-31` (graph `.*(Fire|Hit|Kill|Damage|Destroy|Casualt|Death|Wound).*(Event|Notification|State|Status)$` Class (18) + grep)

| fact | where | feeds |
|---|---|---|
| a SHOT: `WeaponFireIntent {Shooter, Target, WeaponIndex, IsRemote, AtPoint, TargetPoint}` — `IsRemote` ⇒ it is raised on other nodes too | `Fdp.Toolkits/Combat/.../WeaponFireEvents.cs:27-46` | shots per shooter |
| a HIT: `DamageAssessedEvent {HitEntity, TotalDamage, IsRemote, BodyPart, Effect, Source}` — reaches other nodes through `EntityHitDamageIngressTranslator` | `Combat/Events/DetonationEvents.cs:26-54` | hits per shooter, fratricide, civilians harmed |
| destroyed: `Health.Current <= 0` | `Combat/Components/Health.cs:19` | destroyed / survivors |
| a REPORT up the chain: `SquadReportNotification {FromUnit, ToUnit, Subject, SentAt, Report (kind, ObservedAt, …), OriginNodeId}` — raised on EVERY node | `Squad/Command/SquadReportNotification.cs:13-29` | report latency (`SentAt − ObservedAt`), reports made |
| area occupancy: EQS `EntitiesOfForceInArea`, `PolygonMath.Contains`; areas = `EditablePolyline` entities | `EntitiesInAreaGenerator.cs:34, :119`; `PolygonMath.cs:29` | reach / hold / clear |
| the scenario clock | `ScenarioClock` on the World Environment | deadlines, time-to |
| "first seen": the perception contact memory (`KnownCombatantList`, sensor contacts) | OVERVIEW §4 | the start of a report latency |
| ✅ hits reach every node: the hit-damage ingress is in the SHARED translator pack (`SharedTranslatorPack.cs`, `EntityHitDamageIngressTranslator.cs`) · ✅ harm is judged by AFFILIATION: `ForceId {Neutral, Friend, Hostile}` (`Fdp.Core/Components/ForceId.cs:10-20`) — 🔒 user: *"the affiliation is a better indicator than being a civilian or not, because we can have terrorists and other civilian combatants, as well as we do not want the neutral side soldiers be killed"* · ✅ `DamageAssessedEvent.Source` is the SHOOTER for gunfire (`DamageCalculationSystem.cs:~120`), blasts (`AreaEffectSystem.cs:215-225`) and hits from other nodes (`EntityHitDamageIngressTranslator.cs:~101`, `SourceEntityId` mapped); ⚠ drone impacts, gas clouds and ejection carry no source | as cited | — |

### 10.2 Objective kinds v1 — each ONE small generic behaviour in the task slot (SR-37, SR-51)

⭐ Every kind's params begin with the SAME nested block `ObjectiveCommon {title, subject, required, visibleTo, points, weight, deadline, activeWhen, scored}` —
the deadline and the activation are FIELDS, not kinds, so every objective can have them.

| kind | params (besides `ObjectiveCommon`) | achieved when · failed when |
|---|---|---|
| `ReachArea` | area, minUnits = 1 | ≥ minUnits of the subject's units inside · the deadline passes |
| `HoldArea` | area, minUnits = 4, maxEnemy = 0, holdSeconds = 600, `capture` (repeatable: the owner flips) | held continuously for holdSeconds · the deadline passes (capture: never ends, the OWNER is the state) |
| `ClearArea` | area, enemy side, maxEnemy = 0, forSeconds = 30 | no enemy inside for forSeconds · the deadline passes |
| `DestroyTargets` | targets (`RefKey` / query), count | count of them destroyed · the deadline passes |
| `Protect` | targets (`RefKey` / query), minSurvivors | the deadline passes with ≥ minSurvivors · fewer survive |
| `NoHarm` (a constraint) | protected AFFILIATION (`ForceId`: Neutral / Friend / Hostile), by side, maxCount = 0 | the run ends within limits · more harmed than maxCount (fratricide and civilian harm are two uses) |
| `Composite` | mode all / any / weighted | its required children achieved · a required child failed |

### 10.3 Measures v1 — evaluation that SCORES rather than ends (a measure is an objective with a value and a target)

| kind | value | default target |
|---|---|---|
| `HitRatio` | the subject's hits ÷ shots | ≥ 0.30 |
| `Count` | shots · hits · kills · fratricide · civilians harmed · reports made — by the subject | per use (fratricide = 0) |
| `ReportLatency` | first contact seen by the subject's team → its report received by the subject's commander (`SentAt − ObservedAt` on the chain) | ≤ 60 s |
| `TimeTo` | scenario start → a named objective achieved | per scenario |

### 10.4 `ObjectiveStatus` (replicated, SR-31 / SR-48) — one shape for objectives and measures

`State` (Pending · Active · Achieved · Failed) · `Owner` (side, capture only) · `Progress` (current / needed) · `Value` + `Target` (measures) ·
`Points` · `ChangedAt` (scenario time) · `Reason` (a short text: *"held 60 s, 5 Blue / 0 Red"*) · `Subject` · `Parent` (SR-53).

### 10.5 ⛔ SUPERSEDED by §10.7 SR-72 (user: *"Evaluation macro should not come from TKB. TKB is static"*) — per-member evaluation from the TKB type (SR-30)

A unit TKB type names an evaluation MACRO (e.g. Rifleman → `RiflemanCriteria` = `HitRatio` + `Count(fratricide) = 0` + `ReportLatency`); every
unit of that type gets one instance with itself as `Subject` and its commander's evaluation as `Parent` (SR-54 ③); thresholds by SR-67.

### 10.6 `ExerciseEnded` (NED, R-4105)

The outcome per side · end time + reason · the objective TREE (each node: key, title, kind, subject, state, value / target, points, when,
reason) · totals per side. ⭐ A GRADE (A–F, %) is NOT computed by the engine — a frontend derives it from points and pass / fail (R-4105).

### 10.7 Rev 7a (`2026-10-31`) — assignment by ROLE, triggers on the same statuses, external measurements, weights

> 🔒 **User, `2026-10-31`:** *"Evaluation macro should not come from TKB. TKB is static, but the evaluation criteria and objectives, they need to
> be specifiable per scenario"* · *"how a higher echelon macro scenario objective can specify the objectives for subordinates (likely by referencing
> another macro or some macro library where the objective is selected by member role.) The goal is to assign the objectives more or less
> automatically whenever possible."* · *"we should talk also about the triggers … how to affect the running scenario based on similar conditions"* ·
> *"inputs that are not coming directly from our simulation, but are coming from external world … how much a trainee exposes to an enemy … there
> are some measurement devices providing this information"* · *"Weighting of the criteria might be necessary in case we want to act on some
> triggering condition which is based on the evaluation criteria … the points or some values calculated from the objectives would need to be passed
> to the upper layer, which might be aggregating them and deciding something upon them."*

| measured | where |
|---|---|
| a member's ROLE already exists: `TacticalDesignation` (Commander, SquadLeader, TeamLeader, Medic, AutomaticRifleman, MachineGunner, Grenadier, Marksman, …) on the command link, on the wire | `Fdp.Core/CommandHierarchy/TacticalDesignation.cs`; `UnitSubordinate.cs:25` |
| ⛔ no generic NED topic for an external measurement (graph `.*(External|Telemetry|Measurement|Reading|Instrument|Sensor|Variable|Signal|Value)(Topic|Descriptor|Sample|Message)$` — no topic; the protocol inventory names an external gateway as *"a node like any other"*) | `NED_DDS_Protocol.md` |

| # | decision | ⭐ lean |
|---|---|---|
| **SR-72** | per-member objectives are assigned BY ROLE, per scenario | a `MemberEvaluation` kind placed on a commander (its `Subject`): params `byRole {SquadLeader: "SLCriteria", MachineGunner: "MGCriteria", *: "RiflemanCriteria"}` (role → a macro from a library folder, e.g. `macros/evaluation/`) — it creates ONE macro instance per subordinate down the command chain, chosen by `TacticalDesignation` (or the entity class when no role), `Subject` = that member; a role's macro may itself contain a `MemberEvaluation` (squad → fire teams). It follows the chain LIVE: a reinforcement gets an instance; a casualty's instance stays and keeps its result. ⛔ never from the TKB (static) — SR-30 superseded |
| **SR-73** ✅ CONFIRMED `2026-10-31` (user: *"Group A confirmed."*) | triggers act on the SAME statuses | a `Trigger` kind: `when` = a status of other objectives / measures (*"`bridge` becomes Achieved"*, *"`Blue members` value < 0.5"*, *"`Blue tickets` ≤ 0"*) + an optional delay · `do` = actions. ⭐ ⇒ the CONDITION vocabulary IS the objective kinds: a condition that is not scored is an objective with `scored: false` — no second condition language, nothing called by name |
| **SR-74** ✅ CONFIRMED `2026-10-31` (user: *"Group A confirmed."*) | actions v1 | `Order` (an `AssignTacticalIntent` to a `RefKey` / query subject: behaviour + params) · `SetReporting` · `InsertMacro` (a scenario macro instance created at runtime at a position — waves, reinforcements, an IED event: the SAME macro machinery) · `Message` (to a side / the IOS / a trainee) · `SetObjectiveActive` · `SetSkillAmplifier` (P-5) · `EndExercise` |
| **SR-75** ✅ CONFIRMED `2026-10-31` (user: *"Group A confirmed."*) | values passed UP | every status carries a normalised `Score` (0..1: achieved = 1, failed = 0, a measure = value vs target); a `Composite` aggregates its children by their `weight` into its own `Score` / `Value` ⇒ a trigger or a director watches any level (team, side, exercise) |
| **SR-76** | EXTERNAL measurements (the physical world) | a small NED topic `ExternalMeasurement {[DdsKey] StationId (int, SR-76a), [DdsKey] Key, Value, Time, SourceId}` (KeepLast per subject + key) published by a device gateway; the ingress keeps the latest per (subject, key) — a measure kind `External {key, aggregate: mean / max / time above threshold, target}` and the same as a trigger condition. Example: `exposure` from a laser / camera rig behind the physical wall → *"time exposed to the enemy > 20 %"* |
| **SR-77** | the director | a director's utility inputs may read any `Score` / `Value` (SR-18) — the adaptive pressure valve rides the same statuses |

### 10.8 Rev 7b (`2026-10-31`) — aggregation is overridable, roles OR keys, the station link

> 🔒 **User, `2026-10-31`:** *"The aggregation and weighting of the criteria, these are usually something that changes over time, that we will
> never create something that will fit everyone's needs. So these parts shouldn't be hard coded without the possibility to override them … maybe
> some aggregation logic as a function, either hard-coded and registered as such (eval result aggregator) or defined as a blueprint."* ·
> *"tactical designation is not always sufficient, and we might need to reference via the ref key."* · *"The external measurement device reports
> and provides data for its physical ID … this link between the device and the trainee might live as a component on the trainee entity … stations
> … are usually numbered or have a string name … fixed string 64 for that (or GUID)."*

| # | decision | ⭐ lean |
|---|---|---|
| **SR-78** ✅ CONFIRMED `2026-10-31` (user: *"Group A confirmed."*) | the AGGREGATOR is the composite's BEHAVIOUR | ⛔ no aggregator registry: a `Composite` objective is an entity whose task-slot behaviour IS its aggregation — `WeightedMean`, `AllMustPass`, `RequiredGateThenWeighted` ship; a scenario picks one by name like any behaviour, with its params; a custom one is a C# behaviour OR a Blueprint behaviour reading its children's `ObjectiveStatus` and writing its own `Score` / `Value`. ⇒ the existing behaviour registry, params form, debugging and hot reload serve aggregation too (R-4111) |
| **SR-72a** | the member SELECTOR | `MemberEvaluation`'s table is an ordered list of rules, first match wins: a `RefKey` (`@PL-Trainee` → `PlatoonLeaderCriteria`), a `TacticalDesignation` (`MachineGunner` → `MGCriteria`), an entity class, `*` |
| **SR-76a** | the device ↔ trainee link | a component on the trainee's entity, `PhysicalStation {Id: int}` (⭐ rev `2026-10-31`, user: *"Station names are unique across the cluster … Please change the fixed string into an integer. The stations use the name just for convenience, but technically they are assigned a unique integer ID as well"* — the name stays a display label, the INTEGER is the key; cluster-unique) — set when the trainee's Player node takes the unit (the node is configured with its station name), cleared when released; `ExternalMeasurement` is keyed by that integer STATION id (the device knows only where it is installed), and the ingress routes it to the entity whose `PhysicalStation` matches. Generic: any device, any key |
| **SR-79** | the GUID descriptor's owner | ✅ accepted as built (`dtEntityGuid` stays with the creator — the Brain creates scenario entities; a Brain crash is non-recoverable today — user) |

### 10.9 Worked example — *"Platoon attack on the village"* *(`2026-10-31`)*

📐 Measured for it: roles in the 117 scenarios — 409 entities have a command link: `Wingman` 284, `SquadLeader` 80, `MachineGunner` 29,
`TeamLeader` 8, `AutomaticRifleman` 4, `Medic` 4 ⇒ most riflemen carry the DEFAULT role, so SR-72a's `*` / class / `RefKey` rules matter.

**What the author places in the scenario** (everything else expands from macros or follows the command chain):

```
entity "Blue Platoon"      Scenario macro → macros/forces/ReinforcedPlatoon   { side: Blue }                       (units, RefKeys @BluePL, @Sq1…)
entity "Red defenders"     Scenario macro → macros/forces/InsurgentCell       { side: Red, count: 12 }
entity "Blue mission"      Scenario macro → macros/missions/SeizeArea          { subject: "@BluePL", area: "@Village", deadline: "10:00" }
entity "Blue evaluation"   Scenario macro → macros/evaluation/PlatoonAttackEval { subject: "@BluePL" }
entity "Red counter"       Scenario macro → macros/directors/CounterAttack     { attacker: "@RedHQ", trigger: "Blue mission/Hold", wave: "macros/forces/RedQRF" }
area   "Village"           RefKey @Village
```

**What `PlatoonAttackEval` contains** (a reusable library macro):

```
Composite "Platoon attack"   behaviour WeightedMean                              (subject ^subject)
 ├─ ObjectiveRef "mission"     weight 40   value: the Blue mission's Score   (via RefKey of the mission macro's root)
 ├─ MemberEvaluation           weight 60   subject ^subject, rules (first match wins):
 │      @PL-Trainee      → macros/evaluation/PlatoonLeader
 │      SquadLeader      → macros/evaluation/SquadLeader       (itself a MemberEvaluation for its fire team)
 │      MachineGunner    → macros/evaluation/MachineGunner
 │      *                → macros/evaluation/Rifleman           (HitRatio ≥ ^minHit · NoHarm(Friend, Neutral) · ReportLatency ≤ 60 s · External "exposure" ≤ 20 %)
 └─ NoHarm "No fratricide"    weight 0, required   (fails the whole tree)
```

**Triggers in play** (all `Trigger` entities, inside macros):

| trigger | when | do |
|---|---|---|
| `CounterAttack` director | `Blue mission/Hold` becomes Active | `InsertMacro(RedQRF, at the edge of @Village)` + `Order(@RedQRF, Attack @Village)` |
| ease off (in the director) | `Platoon attack` Score < 0.35 after 20 min | `SetSkillAmplifier(Red, −1) (SR-100)` + `Message(IOS, "Red eased")` |
| end | `Blue mission` Achieved OR Failed OR `ScenarioTime > 10:30` | `EndExercise` |

| ⭐ what this shows | |
|---|---|
| six authored entities produce the whole objective tree, per-member evaluation for every soldier and three triggers | reuse across scenarios = swap args / swap the library macro |
| the threshold `^minHit` defaults in `Rifleman`, may be overridden by `PlatoonAttackEval`'s args, a squad's macro, or the unit's own entry | SR-67 nearest wins |
| a trigger's condition is another objective's STATUS; the director reads `Score` | SR-73, SR-75, SR-77 |

### 10.10 Rev 7c (`2026-10-31`) — the member library, attribution, the trainee's own view

> 🔒 **User, `2026-10-31`:** *"How do we specify the macro library for the subordinate units to pick from by ref key or role?"* · *"we should carry
> the original shooter information for grenades and drones. Note that the grenades can be rethrown … it is not clear who to made responsible"* ·
> *"Showing player's own evaluation on the HUD is a good idea for debugging, at least, and I agree that it should be under a feature switch."*

| # | decision | ⭐ lean |
|---|---|---|
| **SR-81** ✅ CONFIRMED `2026-10-31` (user: *"Group A confirmed."*) | the member LIBRARY | ⭐ a FOLDER, resolved by convention — `MemberEvaluation {library: "macros/evaluation/infantry", overrides: {...}}`: for each member it looks for `<library>/<RefKey>` → `<library>/<Role>` (`TacticalDesignation`) → `<library>/<entity class>` → `<library>/Default`, first file found wins. ⇒ NO table to author for the common case; swapping `library` swaps the whole set (e.g. `infantry-recruit` vs `infantry-veteran`); adding `SquadLeader.macro.json` to the folder covers every squad leader. `overrides` (`{"@PL-Trainee": "macros/evaluation/special/PlatoonLeaderTrainee", "Medic": "…"}`) handles scenario-specific exceptions — a FLAT map, so `OverlayFlat` overrides entry by entry (§8.0). Supersedes SR-72a's ordered rule list as the authored form |
| **SR-82** ✅ CONFIRMED `2026-10-31` (user: *"Group A confirmed."*) | attribution of indirect harm | carry the shooter through every indirect damage source (tracker **CE-4155**). For a REthrown grenade the damage event carries BOTH: `Source` = the LAST thrower (who acted last) and `OriginalSource` = the first. ⭐ the evaluation decides, not the engine: a measure's `attribute: last \| original \| both` param, default `last` (the one who threw it back took the decision); both stay in the result for the debrief |
| **SR-83** | the trainee sees their own evaluation | feature `hud.own-evaluation` (R-4108), OFF by default — when on, the Player HUD lists the objectives / measures whose `Subject` is the possessed unit (live `Value` vs `Target`); useful for debugging and for training modes that want live feedback; the instructor's view is unaffected |
| **SR-84** | the Deploy score and respawn (user, `2026-10-31`) | ✅ the Deploy design's score (D-15) IS a set of evaluation measures with the player's unit as `Subject` — one counter; the respawn delay (D-14) is a scenario parameter with a default (`DESIGN_Player_Join.md` §7b) |

### 10.11 Trigger semantics *(B1, `2026-10-31` — leans for the user)*

📐 Measured: the brain tick runs only when simulation time advances (`[TickMode(WhenTimeAdvances)]`, `BrainTickSystem.cs:58`) ⇒ nothing
evaluates or fires while paused; it walks entities in index order (deterministic within a run); the brain journal is always on and RECORDED
(`DESIGN_Entity_Brain_View.md` BV-16) ⇒ a trigger's HSM transition and its cause land in the recording for free.

```mermaid
stateDiagram-v2
    [*] --> Disarmed
    Disarmed --> Armed : activeWhen holds (or none)
    Armed --> Holding : condition rises false to true
    Holding --> Armed : condition drops before holdFor
    Holding --> Waiting : held for holdFor
    Waiting --> Fired : delay elapsed, actions run in order
    Fired --> Cooldown : repeat
    Cooldown --> Armed : cooldown elapsed and condition false again
    Fired --> Done : once
    Armed --> Disarmed : activeWhen stops or deadline passes
```

*Caption:* a trigger is a small HSM; every arrow is a journaled transition with its cause, so the AAR can answer *"why did the counter-attack
start at 09:43?"* without new logging. `holdFor` (debounce: the condition must stay true) and `delay` (fire later, even if the condition
drops) are different and both optional.

| # | rule | ⭐ lean |
|---|---|---|
| **T1** | what fires | the RISING EDGE of the condition (false → true) — ⛔ never "every tick while true"; a *while* belongs to an objective's activity |
| **T2** | once or repeat | `once` by default; `repeat` re-arms only after the condition has gone FALSE again (a new edge) and after `cooldown` |
| **T3** | `holdFor` and `delay` | `holdFor`: the condition must hold continuously that long (debounce) · `delay`: fire that long after the edge, even if the condition drops meanwhile (a scheduled consequence) |
| **T4** | arming | `activeWhen` (ObjectiveCommon) arms it; its `deadline` disarms it; a disarmed trigger never fires |
| **T5** | time | scenario simulation time only (R-126 one time source): paused ⇒ frozen; time scale honoured |
| **T6** | several in one tick | each trigger is independent; a trigger reads other statuses as they are when it ticks ⇒ up to ONE TICK of latency, deterministic (index order). ⭐ where order matters, CHAIN: a trigger's own status (`Fired`, `FireCount`) is a condition another trigger can watch |
| **T7** | its actions | run in their listed order within the firing tick; actions with a duration (`Order`, `InsertMacro`) are fire-and-forget; an action that fails (an order refused) is logged in the trigger's `Reason` and does NOT undo the others |
| **T8** | its status | a trigger carries `ObjectiveStatus` too (state Disarmed / Armed / Holding / Waiting / Fired / Cooldown / Done, `FireCount`, `LastFired`, `Reason`) — `scored: false` by default; a timeline lane `rules.fired` |
| **T9** | the instructor | may FIRE, DISARM or RE-ARM a trigger by hand (an instructor action, journaled with its origin) |
| **T10** | replay | triggers do not run in replay (brains do not tick there) — what fired is in the recording (journal + status) |

### 10.12 Mission plan × triggers — information and a unification proposal *(`2026-10-31`; NOT decided — the user wants information first)*

> 🔒 **User, `2026-10-31`:** *"the triggers which are on an entity can steer the entity, not just a scenario … It still looks to me that these are
> the same concepts and that we should somehow unify … I'm not forcing to unify or replace the mission plan concept yet. I need information first."*
> · *"The concept and UI, as perceived by the user who defines the missions, should stay simple. The internal implementation doesn't need to. We
> can also change the wire format if beneficial, no backward compatibility required. No one is using the NED API to mission editing now … Everything
> what uses mission plans is just UIs and scenarios, and we can migrate these if beneficial."*

| measured | where |
|---|---|
| the mission plan is NOT a brain: `MissionDirectorSystem` evaluates the current phase's ONE trigger per entity with a `MissionPlanQueue` (≤ 8 phases) and starts the next task through `AssignTacticalIntent` (origin Superior); commands replace / jump / abort (`MissionControlExecutionSystem`) | `MissionDirectorSystem.cs:153-200`; `REFERENCE_Command_Building_Blocks_Measured.md` |
| the wire trigger is open (`{Type: string, Params: string}`, a LIST per task) but ingress narrows it to 4 kinds, uses only the FIRST, and ⚠ maps an UNKNOWN type to `TimerElapsed(0)` — the phase advances at once, silently | `Hrot.Network.NED/Helpers/MissionTriggerHelper.cs:30-52` |
| a phase change is journaled only as the next behaviour's start — no *"advanced because …"* | grep `Journal` in `MissionDirectorSystem.cs` = none |
| usage: 35 scenarios / 47 entities; trigger kinds used: `BehaviorFinished` 85 · `TimerElapsed` 18 · `UnderAttack` 0 · `HealthCritical` 0; 52 production + 29 test files touch the mission types | scan `2026-10-31` |
| authoring: the editor's Mission panel (five trigger names, `MissionPanel.cs:78`); saved by `MissionPlanTranslator` | as cited |

**The proposal (U5) — ⛔ SUPERSEDED by §10.13 SR-89 (keep the queue, share the step library) — ONE step engine, two SHAPES, the user's concept unchanged:**

```mermaid
classDiagram
    direction LR
    class Step { <<shared>> condition · holdFor · delay · actions[] }
    class StepCondition { <<shared, a FIXED small set + objective refs>> Timer · BehaviourFinished · UnderAttack · HealthCritical · ScenarioTime · ObjectiveState(ref) }
    class StepAction { <<shared>> RunTask(subject, behaviour, params) · Order · InsertMacro · Message · MissionCommand · SetObjectiveActive · EndExercise }
    class Trigger { <<behaviour>> ONE step · armed / repeat / cooldown (T1-T10) }
    class Sequence { <<behaviour = the MISSION PLAN>> ORDERED steps + a pointer · jump / replace / abort }
    class MissionPanel { <<existing UI, unchanged concept>> a selected unit's ordered task list }
    Trigger *-- Step
    Sequence *-- Step
    Step *-- StepCondition
    Step *-- StepAction
    MissionPanel ..> Sequence : edits the one whose Subject is the unit
```

*Caption:* a mission plan IS an ordered list of steps (*"do task 1 until it finishes, then task 2 for 30 s, then …"*); a trigger is ONE unordered
step. Same conditions, same semantics (T1–T5), same actions, same journal and status, same entity shape — the user still sees *"the unit's
mission: a list"*.

| | effect |
|---|---|
| ⭐ the user concept | unchanged: the Mission panel shows the selected unit's list (task · until · next) — it edits the `Sequence` whose `Subject` is that unit |
| ⭐ removed duplication | one condition vocabulary (the mission's 4 built-ins join `ScenarioTime` / `ObjectiveState` in ONE shared set, usable by triggers too); one action vocabulary; one semantics; one journal (a step advance is a journaled transition WITH its cause) |
| ⭐ gained by missions | richer conditions (*"until `Clear B4` achieved"*), a mission step can carry an objective (its outcome scored), several conditions per step, the instructor's jump / re-arm from the same UI |
| ⚠ retired | `MissionPlanQueue`, `MissionDirectorSystem`, `MissionAdapterSystem`, the ingress narrowing (and its silent fallback), `EntityMission` / `MissionControlRequest` as they are — the wire becomes the replicated params + `ObjectiveStatus` of the `Sequence` entity (+ a generic step-control message) |
| ⚠ cost | migrate 35 scenarios (47 plans, all `BehaviorFinished` / `TimerElapsed` — mechanical), the Mission panel's backing model, 52 + 29 files |
| ⚠ open | a unit's mission moves from a component ON the unit to its own entity (subject = the unit) — the Mission panel and the ORBAT must find "the unit's mission" by `Subject`; one sequence per unit, or several (e.g. an instructor's overlay plan)? |

⭐ Rejected — **lowering a mission into one trigger ENTITY per phase** (up to 8 + their objectives per unit): the same result with many more entities; the `Sequence` shape keeps one entity per plan.
⭐ Rejected — **triggers as one-step mission plans**: the existing mission occupies its unit's task slot; triggers run in parallel without one — U5's `Sequence` does not occupy the slot either (it ORDERS the unit, as the mission director does today).

### 10.13 Where a trigger runs, and the mission plan lean *(`2026-10-31`)*

> 🔒 **User, `2026-10-31`:** *"Where the trigger hsm runs? The behavior on the entity is set by the mission plan items so trigger can not run there."*
> → on the answer: *"trigger runs on sub-entity, not on the entity where the mission is'. I am fine with that."*

| # | | |
|---|---|---|
| **SR-88** | ✅ where a trigger runs | on its OWN entity — conceptually a SUB-entity of the entity it concerns (`Subject`, shown nested under it), technically a full entity with its own brain running the `Trigger` behaviour in ITS task slot (a `PartMetadata` part cannot carry a brain — §9.3). The concerned unit's slots stay its own: the TASK slot = its mission plan / orders, the SOP slot = its doctrine. SR-73 / SR-21 stand |
| ~~**SR-89**~~ ⛔ WITHDRAWN by SR-90 (user: *"I am still not sure if the unification is worth it, it sounds way more complicated than what it is now. Are the gains significant? Simplicity matters"*) — the mission plan | KEEP the mission queue on the unit (one mission per unit, the simple concept) and EXTEND it with a SHARED step library — `StepCondition` (Timer · BehaviourFinished · UnderAttack · HealthCritical · ScenarioTime · ObjectiveState(ref)), `StepState` (T1–T5 edge / holdFor / delay), `StepAction` (RunTask · Order · InsertMacro · Message · MissionCommand · SetObjectiveActive · SetSkillAmplifier · EndExercise): `MissionDirectorSystem` evaluates the current phase's up-to-4 conditions with it; the `Trigger` behaviour binds the same library through thin `[SharedAiCondition]` / `[SharedAiAction]` methods — ONE implementation, two hosts. Per phase: optional extra actions and an optional objective. Fixed: strict parsing (an unknown kind = a load error, today it silently fires `TimerElapsed(0)`), every condition honoured (today only the first), a journaled advance. ⛔ supersedes U5 (§10.12). ⚠ unmeasured: whether `MissionPhase` can carry ~4 conditions in its fixed layout |
| **SR-90** | ✅ the mission plan stays AS IT IS (user, `2026-10-31`: *"Yes"*) | its simple UI, one mission per unit, its own four advance rules. Missions and triggers connect through ONE action — `MissionCommand` (jump / replace / abort): *"when `Clear B4` is achieved, jump Squad 1 to task Withdraw"* is one trigger next to a plain mission. Why: usage is `BehaviourFinished` 85 + `Timer` 18 across 35 scenarios, the rich cases are covered by triggers, and the leftover duplication (four per-phase rules) is small and simple where it is. Only the two defects are fixed — tracker **CE-4157**. A shared step library stays possible later if a richer mission becomes a real need |
| **SR-91** | how a trigger's `do` is implemented (user, `2026-11-01`: *"Is it a sub-behavior or a blueprint script? … It looks like it is just a plain sequence of actions. Is it?"*) | ⭐ PLAIN DATA — an ordered list of `{act: <kind>, args: {…}}` in the trigger's params, run by ONE generic compiled `Trigger` behaviour; ⛔ not a sub-behaviour, not a Blueprint, nothing compiled per scenario. The list stays JSON in the assignment's managed start record (precedent `SopStartRecord.JsonParams`) — no packing of a variable list into the fixed block; at fire time the behaviour walks it in order and hands each entry to a compiled handler chosen by its kind — a small CLOSED set (InsertMacro · Order · MissionCommand · SetReporting · Message · SetObjectiveActive · SetSkillAmplifier · EndExercise), each publishing the existing event. Validated at ASSIGNMENT: an unknown kind or bad args = a loud refusal at load. A new kind = one more compiled handler (ordinary development); logic beyond a list = a reusable director / Blueprint ASSET an action starts by name |
| **SR-93** | ⭐ ONE ACTION VOCABULARY — supersedes SR-91's *closed handler set* (user, `2026-11-01`: *"the actions to cover just the non-self stuff you enumerated, like insert macro, send a message to someone, ask an entity to execute a behavior etc. the sequencing the actions for sure requires that we wait for each to complete … insert macro is something that I would like to be called from basically any behavior as an action … So this is a real unification of action vocabulary."*) | ⭐ the scenario-directing verbs (InsertMacro · Order · MissionCommand · SetReporting · Message · SetObjectiveActive · SetSkillAmplifier · EndExercise) are ordinary **stateful `[SharedAiAction]`s** (`SharedNodeBinder.RegisterStatefulAction`, `SharedNodeBinder.cs:285`) that name their TARGET in their params — `self` is only *who asked*. ⇒ any BTree, HSM, director or trigger calls the same method. ⭐ Each returns `Running` until its effect is established, then `Success`/`Failure`: **InsertMacro** keeps its creation request ids in its working state and completes when every one has an `EntityConstructionCompleted` naming it (`LifecycleEvents.cs:114` carries `RequestId`, AQ107/CE-3323) — any `Succeeded=false` ⇒ `Failure` with its reason; **Order** completes when the unit has accepted the intent (waiting for the OUTCOME is a `when`, not the action). ⭐ A trigger's `do` stays a plain data list (SR-91 keeps that half) and runs as a **sequence**: tick the current action until it ends; `Failure` stops the list and is written to `Reason` (T8 revised — later actions usually depend on earlier ones, e.g. `Order @RedQRF` needs the macro inserted). ⛔ **Not yet buildable — owed measurements:** ① bind a shared action at RUNTIME from data — the binders are generic over the param/working-state TYPES (`SharedNodeBinder.cs:285`, `HsmSharedBinder.cs:109` already turns a `MethodInfo` into a delegate), but today only generated code supplies those types (`Hrot.AiEditor.Persistence/Emit/SharedAiBindings.cs`); a data list needs the same call made with types looked up from the catalogue (`MakeGenericMethod`, once at load); ② ~~`TParams : unmanaged` — InsertMacro's variable `args` cannot live in the block~~ ⇒ ✅ settled by SR-97 (`ActionArgs` side record); ③ the catalogue (`ActionSchemaExporter`) is editor-side (`Hrot.Editor.AiShared`) — load-time validation on a cluster node needs it in shared runtime code |
| **SR-94** | ✅ a `do` entry carries its OWN bindings (user, `2026-11-01`: *"The parambinding switch references the index of the action inside the action list. It seems pretty fragile. Couldn't we describe the bindings per action as part of its record? Imagine manual editing of the svenario file and the neet to reindex the binding - non practical."* → *"Yes"*) | each entry = `{act, args, bind}`; `bind` maps a path RELATIVE TO ITS OWN `args` to a scenario / macro param — `{"act":"InsertMacro","args":{"macro":"RedQRF","macroArgs":{"size":12}},"bind":{"macro":"qrfMacro","macroArgs.size":"qrfSize"}}`. ⭐ reordering moves the bindings with the entry; the literal stays as the unbound value. ⭐ The rule: **the binding lives NEXT TO the value it binds** — the shape SR-35 first proposed; SR-57's `ParamBindings` stays for ordinary component fields (a typed component cannot hold a sibling map). Applied ONCE at load, like every other binding (SR-44) — scenario params are fixed for a run. Chains unchanged: scenario param → action arg → macro param → a part's field |
| **SR-95** | ✅ `when` is the EXISTING condition language, not a new one (user, `2026-11-01`: *"This looks like a special language describing the condition. Couldn't we reuse the existing conditions machinery? … We already have a way how to serialize even rich complex conditions in JSON. This is used for various places like data breakpoint conditions, etc."* → *"Yes"*) | ⭐ `when` = a `SearchPredicateDto` (`Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs:35` — Compound And/Or · PropertyMatch · Numeric · String · Lifecycle · TransientEvent · Spatial · BehaviorParam · BlueprintVariable …), compiled ONCE at load by `IPredicateCompiler.CompileComponentPredicate` → `Func<EntityRepository, Entity, bool>` (`IPredicateCompiler.cs:19`; already built on the CGF node, `CgfSubsystem.cs:859`) — the language data breakpoints AND the Blueprint `When` node (`ConditionMetPayload`, `Nodes.cs:800`) already use. ⭐ A trigger is one more **reactive guard** (`ReactiveGuardVocabulary.cs:7` — BTree observer selectors, HSM transition guards, Blueprint `When`): the predicate says WHAT, the trigger adds only the edge and timing (T1–T10). The editor authors it with the existing predicate builder. ⭐ Two ADDITIONS to the language (new `JsonDerivedType`s, not a second language): ① **`OnEntity {Ref, Predicate}`** — re-targets its child at a `RefKey`/GUID entity, so a condition can be about another entity and, nested in a Compound, about several (*"@A Active AND @B Failed"*); ② **`ScenarioTime {Clock: Elapsed\|Utc, Min, Max}`** — 📐 measured: NO component holds the current scenario time; `ScenarioTime.Utc(world)` (`Weather/ScenarioClock.cs:66`) computes it = `ScenarioClock.StartUtcTicks` (World Environment) + `GlobalTime.TotalTime` ⇒ a PropertyMatch cannot express it, a small leaf can. ⛔ `[SharedAiCondition]`s are NOT the `when` language (they need param blocks + runtime binding — SR-93 ①); one may become a predicate leaf later if a real need appears |
| **SR-96** | ✅ a macro insertion reports its OWN completion (user, `2026-11-01`: *"maybe this is a process that runs on the brain node and takes care about all its subparts like creating entities and so on, and it reports the completion on its own. So the action that triggers the macro expansion just monitors this final completion signal from the node that executed the macro expansion and doesn't care about the individual subparts inside."* → *"Yes write those two leans down."*) | ⭐ the macro INSTANCE entity (`MacroInstance{macro,args}`) carries a small replicated `MacroExpansionStatus {State: Expanding\|Done\|Failed, Reason}`. ⭐ ONE writer — the node that expands it; it alone tracks its child creation requests (each closed by an `EntityConstructionCompleted` naming its `RequestId`, `LifecycleEvents.cs:114`). ⭐ The `InsertMacro` action (SR-93) only creates the instance and reads that status: `Expanding` ⇒ `Running`, `Done` ⇒ `Success`, `Failed` ⇒ `Failure` with its reason — ⛔ it never counts the parts. ⭐ STATE, not an event: readable on every node, survives save / restore / replay — the objective pattern (*"nobody calls it, everyone reads its status"*). ⛔ open: WHICH node expands (macros are not built yet — pinned by the macro design, not assumed here) |
| **SR-97** | ✅ an action's VARIABLE arguments live in ONE generic side record (user, `2026-11-01`: *"the managed side record pointed to from the struct sounds reasonable"* → *"Yes write those two leans down."*) | an action's params are a fixed-size `unmanaged` struct at an offset in the behaviour's block (`SharedNodeBinder.cs:288`), so a list cannot live there. ⭐ The struct carries an `int ArgsRef`; the list lives in ONE generic managed component on the CALLING entity — `sealed record ActionArgs { IReadOnlyList<string> Items }` (JSON per item) — and the action reads `world.GetManagedComponentRO<ActionArgs>(self).Items[p.ArgsRef]` (`self` is always the caller, so the record is always found). ⭐ Filled at ASSIGNMENT (after SR-94's bindings are applied), for a trigger's `do` list and a BTree/HSM asset's node args alike; the `do` list itself is held the same way. ⭐ ONE component for every caller and action ⇒ one translator, one saved/restored shape. Precedent: `SopStartRecord.JsonParams` (`Behavior/Components/SopState.cs:86`, a recorded managed record) |
| **SR-98** | ✅ the action LOOP is itself a shared action (user, `2026-11-01`: *"agreed that the action loop itself is a shared action."*) · ⭐ each step's params live in a blackboard slot made just before it starts and removed when it ends (user proposal, same day: *"Couldn't we create one as a blackboard slit Immediately before given action is to be started, and Delete it once the action completes?"* — lean YES, pending confirmation) | ⭐ `RunActions(listRef)` — a stateful `[SharedAiAction]`; its working state = the current step index; the trigger's `do` is one call to it, and any BTree / HSM / director can run a list the same way. ⭐ NO per-type scenario-loader hook is needed: the `do` list is just the JSON params of the trigger's task-slot behaviour, loaded generically like every brain slot (`SavedBrainSlot {Name, Params}` → `BehaviorStartRecord.JsonParams`); the steps are prepared when `RunActions` STARTS. The only load-time piece is SR-94's binding (a generic JSON-path copy, not per type — ⛔ scenario-param binding is not built yet, SR-41/44). ⚠ A per-component loader extension point exists if ever needed (`IEntityScenarioTranslator.Inject`, `Scenario/IEntityScenarioTranslator.cs:17`). ⭐ Delegates: ONE process-wide cache keyed by the action's key (method), built once per action TYPE from the catalogue — an entity holds only its list (names + `ActionArgs` indices), never delegates. ⭐ Per-step slot: the existing occurrence store already does attach-on-first-use and detach — `OccurrenceWorkingState.ResolveOrAttach<TParams, TWorkingState>` (`Behavior/OccurrenceWorkingState.cs:108`, params + working state in one slot, recorded with the entity) and `OccurrenceStoreAccess.TryDetachSlot` (`Blueprints/Partitioning/OccurrenceStoreAccess.cs:436`) ⇒ only ONE step's bytes exist at a time, nothing is composed for the whole list, and a restore mid-sequence resumes the live step. ⚠ The caller must SEED the params when the slot is freshly attached (that seam's own rule) — `RunActions` does it from the step's `args`. ⭐ the store itself is NOT a constraint (corrected `2026-11-01`, user: *"where did you get that from? Is it true … that it can not be done inside tick?"*): the trigger's FIRST block is created by `BehaviorIngressSystem` when its behaviour is assigned (it declares a stateful slot, so it is provisioned — `DESIGN_Occurrence_Scoped_Storage.md` §27); a step slot that does not fit APPENDS a block, which moves nothing and is allowed mid-tick (§34.6, U-0, rail `O7_R9`). ⚠ An earlier version of this row said *"never inside a tick"* — quoted from a stale doc comment (`OccurrenceWorkingState.cs:27`, now fixed) and §27.1, both overtaken by §34.6 |
| **SR-99** | ✅ NO DIRECTOR ENTITY — a director is a MACRO of objectives + triggers (user, `2026-11-01`: *"I'm just interested if this all can be done on the objectives and trigger entity level, without introducing any new type of director entity."* → *"Yes, record the idea of not needing any special director entity"*) | ⭐ WATCHING = objectives / measures (`Score` 0..1 of a team's `Composite`); REACTING = triggers whose `Subject` is the World Environment (scenario-wide, tied to no side), `when` reads any `Score` / state + the scenario time (SR-95), `do` uses the out-of-world actions (`InsertMacro`, `SetSkillAmplifier` SR-100, `Message`, `EndExercise`). ⭐ Red HQ stays an ordinary COMMANDER fighting from its picture; an inserted force just gets an `Order`. ⭐ Example, a reusable `macros/control/AdaptivePressure {blue, red}`: *Counter-attack* (`@Hold` Active, delay 120 s, once → InsertMacro + Order) · *Ease off* (`Score < 0.35` AND elapsed > 20 min, holdFor 2 min, repeat, cooldown 10 min → SetSkillAmplifier(red, −1) + Message) · *Push harder* (`Score > 0.8` AND elapsed < 30 min, once → InsertMacro + Order). ⭐ T-rule added (lean): **`maxFires`** — a repeating trigger stops after N fires (its own `FireCount`, T8). ⚠ Where triggers get awkward: choosing ONE of several plays (independent triggers can fire in the same tick — exclusive score bands work for 2–3 plays) · a budget SHARED across triggers (a `Count` objective they read) · smooth control (bands, not continuous). ⭐ ESCAPE HATCH, not a new type: the same trigger-kind control entity running a different reusable behaviour in its task slot (`ChooseOption` over plays, `UtilityNodes.cs:64`; each play a `RunActions` list, SR-98). ⇒ `ExerciseControl` hosting (SR-17) is dropped; ending the exercise is a trigger with `EndExercise` |
| **SR-100** | ⚠⚠ OWNED BY **R-4106** + `PLAN_Training_And_Gameplay_Engine.md` **P-5** (found `2026-11-01` — this row first missed them): AI skill is set PER SIDE as an AMPLIFIER in TIERS (−2 … +2), *"never a leveller"*, a unit at the top stays at the top, set in the scenario and changeable live from the game-master mode; feature `ai.skill-amplifier`. ⇒ the action keeps P-5's name **`SetSkillAmplifier(side, tiers)`** (an absolute amplifier LEVEL, relative to each unit's own) — `AdjustProficiency` below is withdrawn as a name; a trigger sets the level, the game-master UI is a slider. ⚠ P-5 leans *applied where `CombatProficiency` is READ*; the fill-system storage below keeps the man self-sufficient (BR-6) — an implementation choice that satisfies the ruling either way, to settle when built · the "skill" knob a director turns = the EXISTING `CombatProficiency` (user, `2026-11-01`: *"there is for sure skill level somehow encoded already in the parameters of AI entities. We have something like veteran, militia, ordinary unit, but this is per entity only."*) — ⚠ corrects the lead's *"no skill mechanism exists"* (a `.*Skill.*` search, the wrong word) | 📐 measured: `CombatProficiency` (`Behavior/Components/CombatProficiency.cs:21`, CE-2273, `DESIGN_Defend_And_Attack_Position.md` §6 DA-1/DA-2, §8a) — 0..1 with presets Untrained 0.1 · Militia 0.35 · Regular 0.6 · Veteran 0.8 · Elite 0.95; three sources, precedence in the reader `ProficiencyOf.Value` (:67): **Authored** (on the man, OR on a unit entity for every man under it) > **Unit** (the nearest commander's Authored, written onto the man by `ProficiencyFillSystem`) > **TypeDefault** (TKB `BehaviorProfileDto`) > Regular. It drives spread, exposure time, reaction delay, burst discipline and cover discipline (82 non-test references). ⇒ ⭐ a per-UNIT setting already exists; ⛔ a per-SIDE runtime change does not. ⭐ Lean: the action is **`AdjustProficiency(target: side \| unit ref, delta)`** — a RELATIVE offset (± tiers or a value), so a veteran stays better than a militiaman and later-inserted forces are covered too; stored as an offset on the target, carried onto each man by `ProficiencyFillSystem` (one more field beside `Unit` — the man stays self-sufficient for the hit model, BR-6), added in `ProficiencyOf.Value` before the clamp. ⭐ NO creation hook is needed (user, `2026-11-01`: *"So something would need to watch for entity creation and adjust its proficiency level accordingly, correct?"*): 📐 `ProficiencyFillSystem.Execute` (`Behavior/Systems/ProficiencyFillSystem.cs:26`) already runs EVERY simulation tick over every man in a command chain (`With<UnitSubordinate>`), writes only on a change, and ADDS the component to a man that has none — so a man inserted by a macro is filled on the next tick, exactly as he gets `Unit` today. ⚠ Two things the offset changes: ① a SIDE target must also reach men with no commander ⇒ the loop's query widens from `UnitSubordinate` to side membership (`EntityInfo.ForceId`); ② one tick at the base value before the fill — harmless. ⛔ rejected: writing `Authored` on Red HQ — a man's own `Authored` wins over it, and a QRF under another commander is missed. ⚠ Proficiency only — temperament (willingness, `DESIGN_Temperament_And_Stress.md`) is a separate knob, never merged (the component's own rule). Replaces the name `SetSkillAmplifier` |
| **SR-101** | ⭐ VARIATION BETWEEN RUNS (gap G1, `docs/RESEARCH_Exercise_Control_Market_Comparison.md`; user, `2026-11-01`: *"Points 1 and 2 seems to be the most valuable ones for us at this time."*) — leans | 📐 measured: the one sim random source `SimRng` (`Determinism/SimRng.cs:35`, CE-2188) is derived from sim state (`FromSim(entityIndex, salt, simTime)`, :66) and is DELIBERATELY the same every run (user `2026-09-04/05`: *"something repeatable which just looks random … but is actually the same for every scenario run"*). ⭐ Lean: ONE new input, the run's **variant** — an integer chosen at start (`header.variant`, default 0; the instructor picks a number or *"random"*, which draws and SHOWS one), recorded with the run and mixed into the salt of the scenario-level random choices ONLY: ① `presence: 0.6` on an entity or macro instance · ② `InsertMacro` / a macro choosing ONE of N (`"macro": ["RedQRF", "RedArmour"]`, optional weights) · ③ `delay: {min, max}` on a trigger · ④ one of N areas / positions. ⭐ Keyed by the entity's STABLE GUID (CE-4153), not its index, so a variant reproduces regardless of creation order. ⇒ the same variant = the identical run (replay, restore, *"rerun last Tuesday's variant"*); a new variant = a different exercise from the same file. ⛔ Behaviour-level randomness stays as it is (the user's ruling) — the variant does not reseed brains. ⛔ open: whether variant 0 means *"as authored"* (every `presence` = present, the first of N) — lean yes, so the editor shows the authored layout |
| **SR-102** | ⚠ the MSEL TABLE shape is SUPERSEDED by SR-105 (a dependency view, not a time-stamped list) and its delivery by SR-106; the inject = trigger + expected-response objective STANDS · ⭐ INJECTS and the MSEL VIEW (gap G2; same user line) — leans | ⭐ an **inject** is NOT a new kind: it is a trigger (time or condition) + an OPTIONAL **expected response** = an objective linked to it, activated when the trigger fires (`SetObjectiveActive`) and measuring the trainees' reaction (*"report the contact within 5 min"* = `ReportLatency`; *"secure the crash site within 20 min"* = `ReachArea` + deadline). ⭐ The **MSEL view** = a TABLE over the scenario's triggers (and the time line lane of SR-103): when (planned time or condition) · to whom · what it does · expected response · state (pending · fired · responded · missed · disarmed) · fire / disarm by hand (T9). ⭐ No new runtime — a VIEW over trigger + objective statuses. ⭐ Authoring: an *Inject* item in the editor = a trigger with an optional *expected response* sub-form, so the MSEL is written as a list (the instructors' mental model) and stored as ordinary entities. ⚠ An inject that MESSAGES trainees needs `Message` to reach trainee stations / units over in-sim comms (gap G3) — today it reaches the instructor only |
| **SR-103** | ⭐ AAR HIGHLIGHTS are timeline LANES — cheap (user, `2026-11-01`: *"The automatic highlights in the AR, this is something what the director scout is likdly already doing, isn't it? That could be also a cheap feature."*) | 📐 measured, BUILT: the shared timeline's lanes are REGISTERED by providers (`ITimelineLaneProvider`, `ReplayBrowser/Timeline/ITimelineLaneProvider.cs:48`, R-4078/R-4079, `DESIGN_Director_Timeline_Editor.md` §9c); an INDEXED lane is DATA — a descriptor + a `SearchPredicateDto` `When` + a payload projection (`IndexedLaneDefinition`, :41) — evaluated by the recording's index (the scout, built once per recording and cached, TE-4) and ALSO live (`TimelineLaneContext.Live`, CE-3330). Precedent: `CombatTimelineLanes` (shots, hits, blasts, health / gesture value-change lanes). ⭐ Lean: ONE provider `ScenarioRulesTimelineLanes` with `rules.fired` (a value-change on a trigger's `ObjectiveStatus.State` to Fired), `objective.state` (any objective's state change, coloured by Achieved / Failed), `inject.responded` / `inject.missed` (SR-102), `skill.amplifier` (SR-100). ⇒ the AAR timeline, the film director's moments (its anchors are queries on the same index) and the MSEL view all read the same lanes — nothing new to build but the provider, once `ObjectiveStatus` exists. ⭐ Instructor bookmarks (gap G5) = one more lane whose marks the provider holds (`Marks`) — a later item |
| **SR-104** | ⭐ the GAME-MASTER ("Zeus") mode — what exists and what it adds (gap G4 / B5; user, `2026-11-01`: *"it might need some improvement in the instructor's UI to easily insert something … this is a great demo feature showing the easiness of how to change the exercise at runtime"*) — leans | ⭐ OWNED BY **R-4107** (*"the game-master mode is the server's default UI"* — the CGF / SimHost full UI) + P-2 (*"no new design: a feature set of the server's UI"*). 📐 measured, ALREADY LIVE while running: Add Entity ▸ side ▸ type → click to place (`MapInteractionPack.cs:386`, one path into `EntityCreation.RequestEntityCreation` via `ScenarioSpawnAdapter.Request`, `ScenarioSpawnAdapter.cs:190`) · unit right-click orders + RTS command mode, all through `EntityOrders` (`EntityOrders.cs:48`, origin Operator) · Brain tab settings · attribute edits routed to the owner (`EntityWriteRouter.cs:28`) · cluster time control · the HTTP API (`DebugApiHost.cs:142`). ExCon is retired (R-3067). ⭐ What the mode ADDS, each a Node Feature (R-4108): **Z1** *Add Entity ▸ Macro ▸ <library>* — the same placement tool places a macro instance at the click (`InsertMacro`, SR-96 status; needs macros built) · **Z2** the MSEL / triggers panel — fire · disarm · re-arm (T9), each inject's state (SR-102) · **Z3** run ANY shared action by hand on the selection or a side (the SR-93 vocabulary: `Order` exists, `Message`, `SetSkillAmplifier` …) — one action palette, nothing separate · **Z4** the per-side skill slider (P-5) · **Z5** Brain-tab settings made CLUSTER-SAFE — today a LOCAL write that only works on the node running the brain (`BrainSettings.cs:155`, R-1036) ⇒ route through `EntityWriteRouter` like attributes · **Z6** a rule for entities added LIVE — ⭐ lean: transient (`IsTransient` → `ScenarioIgnoreTag`, like macro expansions): recorded and journaled, never written into the scenario file · **Z7** a headless server + a REMOTE game-master station (R-4107 requires it) — map orders and Add Entity do nothing on a headless node, and the Player (IG) lost its authoring tools with ExCon ⇒ ⛔ open: the station is a windowed cluster node with the shared UI whose requests travel the existing paths (creation requests are NED; orders and attribute writes must be checked) — to MEASURE before design. ⭐ Demo order: Z1 + Z3 + Z4 on the server UI need no Z7 |
| **SR-105** | ✅ the RULES VIEW — the instructor's viewer of objectives and triggers, by DEPENDENCY, not by clock (user, `2026-11-01`: *"what we are missing now is a view on the objectives and triggers in our scenario, which makes it easy for the instructor to see what they are and what is going to happen"* · *"the slipping issue could be solved by presenting some view that shows how the objectives and triggers are chained, instead of being forced to start at some concrete time. Something similar to how gantt diagram is showing the dependencies between tasks in Microsoft project"* · *"instead of taking the MSEL as is from the other simulator, I would adopt it to our objectives and triggers … basically make it a viewer of objectives and triggers. Scenario time fixed injections are still possible via time-based trigger condition."*) | ⭐ a Gantt-like view: one ROW per objective / trigger (grouped by its macro / subject), a horizontal sim-time axis with a NOW line. ⭐ EDGES are DERIVED, never authored twice: a trigger's `when` names the objectives / triggers it waits on (the entity refs inside its `SearchPredicateDto` — `OnEntity`, statuses) ⇒ an edge *predecessor → trigger*; its `do` names what it starts (`SetObjectiveActive`, `InsertMacro`, `Order` targets) ⇒ an edge *trigger → successor*. ⭐ BARS: the PAST at their actual times (activated → achieved / failed; fired; its `delay` / `holdFor` as the bar's length); the FUTURE placed AFTER their predecessors (an *expected* position, not a promise) — a condition on the scenario clock pins a bar at its time, so fixed-time injects are just time-based triggers. ⭐ State colours (pending · armed · waiting · fired · active · achieved · failed · disarmed), the `Score` on an objective's bar, the inject's expected response under its trigger. ⭐ Acts from the row: fire · disarm · re-arm (T9), activate an objective — journaled. ⭐ Rides the SR-103 lanes for the past (the same data the AAR timeline shows). ⛔ No wall-clock MSEL, no role-player / media / phone injects (user) |
| **SR-106** | ✅ MESSAGES TO PLAYERS — an in-exercise briefing that steers them (user, same day: *"It would be enoigh to deliver a message to certain players, for example as a time limited subtitle shown on their screen (with the possibility to show the older messages again when the player wants)"* · *"something that tell them that there is a new challenge in front of them, that something has changed in the scenario and they need to react. It's some way of briefing them during the exercise and steering their actions. This is not just training consideration, this is something that makes a multiplayer game interesting by introducing some situations during the scenario or in the arena."*) | ⭐ `Message(to, text, seconds)` — `to` = a player / a unit (whoever possesses it) / a side / everyone; delivered as a TIME-LIMITED SUBTITLE on the player's screen (the possession HUD overlay, `PossessionStation.DrawOverlay`, `Map3D/PossessionStation.cs:369`, drawn with the one HUD / film text path `HudText`); ⭐ every message kept in the player's MESSAGE LOG, recallable on a key; the instructor's view shows what went to whom. ⭐ It travels as a NED fact (R-4105), so any frontend (a game UI, a web client) can show it too; recorded ⇒ the AAR shows it on a lane. ⭐ A feature (`hud.messages`, R-4108) — on in games and training. ⛔ No channels / senders / role-players (user) |
| **SR-107** | ✅ the PERFORMANCE VIEWS — the instructor's RESULT ANALYSIS and the player's live HUD (user, same day: *"we are missing the result analysis view for the instructor as well as the during action display of player performance on his hud"*) | ⭐ **instructor**: a results view over `ObjectiveStatus` — per side · team · player (the SR-72 member evaluation), each objective's state and `Score` with its measures and thresholds, the composite tree drillable, the moment it changed linked to the timeline (SR-103) — live during the run and after it (from the recording). ⭐ **player**: the `hud.own-evaluation` feature (SR-83) — the objectives and measures whose `Subject` is the possessed unit (or its team), their state and `Score`, on the possession HUD beside SR-106's subtitles. ⭐ Both read the SAME replicated statuses; nothing is computed twice. ⚠ R-4105: the data is the engine's obligation (gathered, aggregated, on NED); these views are OUR frontend over it — another frontend may draw its own |
| **SR-108** | 📐 the TRIGGER PoC — what was PROVEN and what it found (`2026-11-01`; `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/TriggerPoc/`, 38 rails, test project only, no production change) | ✅ **runtime binding** (SR-93 ①): a minimal runtime catalogue reflects `[SharedAiAction]` stateful methods; `MakeGenericType(SharedNodeStatefulAction<,>)` + `Delegate.CreateDelegate` builds ONE runner per action TYPE, cached (`PocCatalogue.cs:70`) ⇒ ActionSchemaExporter's discovery is all a runtime catalogue needs (SR-93 ③ = move it) · ✅ **per-step slots** (SR-98): `ResolveOrAttach<TP,TS>` + seeding with the EXISTING JSON→DTO rule `BehaviorParams.OverlayFlat` (initialisers, case-insensitive, strict enums, FixedString) + `TryDetachSlot`; one step slot at a time, freed bytes reused with a zeroed working state; the OUTER loop's `ref` stays valid across attach / detach / a mid-tick block APPEND (the store never moves a slot); bad args leave no half-seeded slot · ✅ **the loop as a shared action**: Running stays, Success detaches and starts the next IN THE SAME TICK (instant steps take one tick), Failure stops and records step + reason · ✅ **from a real BTree** through `BrainTickSystem`, the store provisioned by INGRESS (sized exactly: a 256-byte first block) — a fat step appends inside the ambient tick view · ✅ **the `when`**: JSON `SearchPredicateDto` compiled once, ZERO allocation per evaluation; rising edge, delay in sim seconds (survives the condition dropping), once · ✅ **restore mid-step** (`EntityRepository.SyncFrom`): the slots' bytes come across and the list resumes at the same step. ⛔ FINDINGS: ① an `Entity`-typed param is SILENTLY not seeded from JSON (`Entity` is a readonly struct; it stays `Entity.Null`) ⇒ entity args travel as RefKey / GUID and are RESOLVED at step start — and the seeding must refuse an entity field loudly · ② an unknown action name fails only when REACHED, after earlier steps ran ⇒ validation at ASSIGNMENT is required (SR-91 confirmed) · ③ a step slot must carry `OccurrenceKind.Hsm` (or Blueprint): ingress's behaviour-change sweep frees only those; kind BTree LEAKS the orphaned slot · ④ `NodeStatus` carries no reason ⇒ a failing action needs a reason channel (the PoC used a component by convention) · ⑤ ⛔⛔ the predicate compiler compiles an UNKNOWN DTO to constant TRUE, silently — and `Structural / SpatialBounding / Lifecycle / TransientEvent` are constant true on the per-entity path BY DESIGN (the search service's frame loop handles them) ⇒ **CE-4159**; ⭐ so a trigger `when` on an area or an event must be evaluated by the SAME per-frame step the INDEXED timeline lanes use live (`TimelineLaneContext.Live`, SR-103), not by `CompileComponentPredicate` alone — the next measurement · ⑥ `OnEntity` needs a `[JsonDerivedType]` + a compiler arm (a closed polymorphic list) · ⑦ a condition already TRUE when the trigger arms is not an edge (a choice the T-rules did not state — now pinned; lean: keep) · ⑧ a NESTED list needs a depth-salted step-slot key |
| **SR-92** | ✅ `when` may be the scenario clock directly (user, `2026-11-01`: *"The scenario time condition in the when, yes, of course. No need for extra objective just for specifying time condition."*) | ⚠ the SHAPE below is SUPERSEDED by SR-95 (the decision stands — the clock is a `when` leaf, no extra objective): ~~`when: {scenarioTime: ">= 09:30"}` beside `when: {status: "@X", is: "Active"}`~~ |

## ⛔ HISTORY — rev 3 / 3a / 3b (superseded `2026-10-31` by rev 4, R-4112)

Rev 3 lowered each scenario's rules into ONE generated HSM (one parallel region per objective / rule / measure): first via the runtime
`HsmBuilder` (3), then as a generated `.hsm.json` compiled by `HsmJsonGenerator` + hot reload (3a / 3b, SR-15 / SR-16). ⛔ Superseded: any
change in WHICH objectives a scenario has would change the generated machine and rebuild. Kept from it: S0's measured rebuild times
(an incremental rebuild ≈ 7.5 s, an unchanged one ≈ 3.8 s; a whitespace edit also rebuilds), the generated `{Asset}_Block` mechanism
(`HsmBridgeEmitCore.cs:853`), and `HsmAssetProjector` showing any compiled machine.

## ⛔ HISTORY — rev 2 (superseded `2026-10-31` by rev 3, R-4109)

Rev 2 proposed a `ScenarioRulesCapability` registering a `ScenarioRulesSystem` that each tick evaluated a `RuleSet` component's predicates,
ran `RuleAction` classes and wrote a `RuleState` component; a world-singleton Blueprint (`RegisterWorldSingleton`, no production caller) as the
host for custom logic; S3 moving the regression checks onto that evaluator. ⛔ Superseded: that is a second brain runtime with its own tick,
state and (missing) debugging. The objectives record (SR-14), the `ExerciseEnded` payload, the features and the IOS ruling carry over.
