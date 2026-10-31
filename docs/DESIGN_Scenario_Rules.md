<!--STATUS
state: LIVE
build-state: DESIGN — leans for the user (2026-10-31)
updated: 2026-10-31 (first version — coordinator)
current-answer: §2 the claim table · §3 the diagrams · §4 decisions SR-1…SR-12 · §5 slices
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/PLAN_Training_And_Gameplay_Engine.md — P-1 (the user's top item); R-4105 (data over NED), R-4108 (every element a feature)
  - docs/Predicate-Infrastructure-Capabilities.md — OWNS the ONE condition model (SearchPredicateDto, PredicateCompiler; R-4077) every rule condition is
  - docs/DESIGN_Scenario_Regression.md — §3 OWNS the checks grammar (anchors, do, expect windows, never, atEnd) the rules grammar shares
  - docs/DESIGN_Director_Timeline_Editor.md — §9c–§9e OWN the registered timeline lanes; rule outcomes become a lane
  - docs/designs/weather/DESIGN_Weather_And_Time.md — W-0 / C-3 OWN the World Environment entity (8810) and ScenarioClock the rules live on
  - docs/blueprints/Blueprint_Subsystem_Runtime_Detailed_Design.md — OWNS the world-singleton Blueprint path (S3's escape hatch)
  - docs/DESIGN_Node_Features.md — every rule element is a feature (R-4108)
  - docs/DESIGN_Player_Join.md — spawn areas (D-11) and score (D-15) the rule actions reuse
-->

# DESIGN — **scenario rules** *(objectives, triggers, timers, end of exercise — for training and for games)*

> 🔒 **User, `2026-10-31`:** *"The point number one seems to have the most value for me."* · *"Let's please dive into the point number one, which I
> think is the most important for now."* — P-1: objectives, end conditions, triggers at SCENARIO scope (training: *"secure the bridge by 10:00"*,
> phase lines, evaluation; game: capture points, time limit, tickets, win / lose).

**The idea in one line:** a scenario carries **rules** — *when this condition holds (in this window), do these actions* — whose conditions are the
engine's ONE predicate model (R-4077), evaluated LIVE on the node that owns the scenario's World Environment entity; every outcome is replicated
state, a NED fact (R-4105) and a timeline lane, so a frontend, an evaluation (P-4) and a replay all read the same truth.

## 1. INVENTORY *(`2026-10-31` — an Explore pass over code and docs; `search_graph` Class `.*(Mission|Objective|Trigger|Rule|Check|Cue|Anchor|Area|Zone|Blueprint.*Host|Condition).*` + grep; spot-verified)*

| what exists | where | for rules |
|---|---|---|
| **the ONE condition model** — `SearchPredicateDto` + `PredicateCompiler`; runs LIVE every tick today (`DataBreakpointSystem`); edge detection left to the caller | `Fdp.Toolkits/ReplayBrowser/Search/`; `Predicate-Infrastructure-Capabilities.md` §1, §3.3, §7; **R-4077** | ⭐ every rule CONDITION is a predicate — no second language |
| the checks grammar — anchors, `do` (set a component), `expect` with after / before windows, `never`, `atEnd`, any / all / atLeast; 103 `checks.json` | `Hrot.Editor/Director/ScenarioChecks.cs` 15-235; Regression §3 | ⭐ the rules grammar's shape; ⚠ its evaluator (`ConditionBook`) is EDITOR-only (reads `DebugApiService` JSON) |
| the cue evaluators `AnchorBook.HoldsFor` / `HoldsOnLaneEntry` (near, task, side, health, embark, gesture; shot, hit, blast) | `Hrot.Presentation/Map3D/Director/AnchorBook.cs` 311-383 | pure comparisons — lowered onto predicates, not called from the server (it is the UI library) |
| a scenario-level host: the **World Environment entity** (TKB 8810: no position, one per scenario, owned by the Brain that loaded it) carrying `ScenarioClock`, `WeatherConditions` | `TkbEntityTypes.cs:120`; `ScenarioClock.cs:20`; Weather W-0 / C-3 | ⭐ where rules, objective states and the outcome live (saved, loaded, replicated by the existing machinery) |
| world-scope Blueprints: `TickWorldSingletons` (`self = Entity.Null`), `RegisterWorldSingleton` / `AddWorldSingleton`, `IsWorldSingleton` in the IR | `BlueprintTickSystem.cs:80, :169`; `BlueprintRegistry.cs:84, :244` | ✅ runtime exists — ⛔ **no production caller** (measured): S3 wires it |
| areas: `EditablePolyline` + its own TKB type per kind; live tests `PointInPolygon`, `PolygonMath.Contains`, EQS `EntitiesOfForceInArea` | `EntitiesInAreaGenerator.cs` 40-110; `PolygonMath.cs:29` | ⭐ an objective area = a new area type; "N of side X inside" exists as a query |
| timeline lanes registered as DATA (`IndexedLaneDefinition` + predicate + projection) | Timeline §9c-§9e | ⭐ `rules.objective` / `rules.outcome` lanes for free |
| missions: `MissionTask` PLANNED / ACTIVE / DONE / FAILED; commands JUMP / APPEND / INSERT / REPLACE / ABORT; `/missions` routes | `MissionDescriptors.cs`, `MissionMessages.cs` | ⭐ the "order side / unit to do X" action |
| ⛔ missing | — | live server evaluator · "unit killed" and "enters polygon" predicates · actions: message, spawn, end exercise, award points · any `end exercise` command |

## 2. Claim table

| claim | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| conditions must be the shared predicate model | ✅ `PredicateCompiler`, live in `DataBreakpointSystem` | ✅ R-4077 *"one condition model"* |
| the predicate model can run live every tick | ✅ `DataBreakpointSystem` | ✅ Predicate doc §3.3 |
| polygon entry is not a predicate yet | ✅ only a box | ✅ Predicate doc §6 TE-3 (planned) |
| the World Environment entity is scenario-level and Brain-owned | ✅ `TkbEntityTypes.cs:120` | ✅ Weather W-0 / C-3 |
| world-scope Blueprints are built but unused | ✅ no caller of `RegisterWorldSingleton` | ✅ Runtime DD (designed) |
| the checks evaluator cannot run on a server | ✅ `ConditionBook` reads `DebugApiService` | ✅ Regression §0 (*"the cluster is not judged"*) |

## 3. Diagrams

```mermaid
classDiagram
    direction LR
    class ScenarioRulesCapability { <<new, composed on the Brain>> declares the scenario.rules features · registers the system }
    class ScenarioRulesSystem { <<new, Brain>> each tick: evaluate the active rules' predicates (edge-detected) · run their actions · write the states }
    class RuleSet { <<new component on the World Environment entity, authored + saved>> Rules · Objectives · Areas by id }
    class Rule { <<new>> Id · When: predicate (R-4077) · Window after / before · Once or Repeat · Do: actions · Feature id }
    class Objective { <<new>> Id · Side · Title · State: Pending / Active / Achieved / Failed · Points }
    class RuleState { <<new component, replicated>> objective states · fired rules · the outcome per side · points per side }
    class RuleAction { <<new, one class per kind>> Message · SetObjective · EndExercise · AwardPoints · SpawnGroup · MissionCommand · SetSkillAmplifier }
    class PredicateCompiler { <<existing, grows>> + PolygonOccupancy (TE-3) · + UnitKilled · + ScenarioTime }
    class ObjectiveArea { <<new area type, the WeatherRegion pattern>> EditablePolyline + Side }
    class TimelineLaneCatalog { <<existing>> + rules.objective · rules.outcome lanes }
    class WorldEnvironment { <<existing entity 8810>> ScenarioClock · WeatherConditions · + RuleSet · + RuleState }
    ScenarioRulesCapability *-- ScenarioRulesSystem
    WorldEnvironment *-- RuleSet
    WorldEnvironment *-- RuleState
    RuleSet *-- Rule
    RuleSet *-- Objective
    Rule ..> PredicateCompiler : When
    Rule *-- RuleAction : Do
    ScenarioRulesSystem ..> RuleSet : reads
    ScenarioRulesSystem ..> RuleState : writes
    PredicateCompiler ..> ObjectiveArea
    TimelineLaneCatalog ..> RuleState : projects
```

*What the picture shows that prose hid:* the rules add ONE system and two components on an entity that already exists, saves and replicates —
the conditions are the existing predicate compiler (three new predicate kinds), so a rule cannot disagree with a data breakpoint, a film cue or a
replay search.

```mermaid
sequenceDiagram
    participant B as Brain (ScenarioRulesSystem)
    participant W as World Environment entity
    participant N as every node (replicated RuleState)
    participant T as timeline index (live + recording)
    Note over B: tick — rule "bridge": When = PolygonOccupancy(Bridge, Blue ≥ 4, Red = 0) · Before = 10:00 (ScenarioClock)
    B->>B: predicate false → true (edge) inside the window
    B->>W: RuleState: objective "Secure the bridge" = Achieved, Blue +100
    W-->>N: replicated (frontends, HUD, the operator read it — R-4105)
    W-->>T: lane rules.objective: "Secure the bridge" Achieved @ 09:42
    Note over B: rule "end": When = all Blue objectives Achieved ⇒ Do EndExercise(Blue wins)
    B->>W: RuleState.Outcome = Blue won · the run PAUSES (SR-9)
```

```mermaid
graph TD
    subgraph brain["the Brain node that owns the World Environment entity (one decider)"]
        RSC["ScenarioRulesCapability<br/>(feature scenario.rules)"] --> RSS["ScenarioRulesSystem<br/>(each tick, Simulation phase)"]
        RSS --> PC["PredicateCompiler (shared)"]
    end
    subgraph every["every node"]
        RS["RuleState (replicated)"] --> UI["frontends · HUD · operator · NED"]
        LANE["timeline lanes rules.*"]
    end
    RSS --> RS
    RS --> LANE
    EDITOR["editor: author rules + areas<br/>(saved in scenario.json on the World Environment entity)"] -.-> RSS
```

*Caption:* the system is reached only on a node that composes the capability AND owns the World Environment entity — one decider, as the
deploy server; ⚠ the silent-dead edge is a cluster whose Brain does not compose the capability: the rules are then inert, so the master
refuses to start a scenario that HAS rules when no node advertises `scenario.rules` (the run would otherwise silently have no objectives).

## 4. Decisions (⭐ leans for the user)

| # | decision | ⭐ lean |
|---|---|---|
| **SR-1** | what a rule is | **When** a predicate becomes true (edge-detected) — optionally within a **window** (after / before a scenario time or another rule) — **Do** a list of actions; **Once** or **Repeat** |
| **SR-2** | the condition language | the ONE predicate model (R-4077) — no rules-only conditions |
| **SR-3** | new predicate kinds | `PolygonOccupancy(area, side, count op n)` (the planned TE-3), `UnitKilled(unit / class / side)`, `ScenarioTime(op t)`, `ObjectiveState(id, state)` — each also usable by data breakpoints and replay search |
| **SR-4** | objectives | first-class: id, side, title, state, points — so a frontend can list them and an evaluation can score them without parsing rules |
| **SR-5** | v1 actions | `Message` (to a side / all), `SetObjective`, `AwardPoints`, `EndExercise(outcome per side)`; S2: `SpawnGroup` (the deploy's spawn areas), `MissionCommand` (JUMP / ABORT … the existing routes), `SetSkillAmplifier` (P-5) |
| **SR-6** | where it runs | a `ScenarioRulesSystem` from the capability `scenario.rules`, on the Brain that owns the World Environment entity — one decider; composed by default on a Brain node (rules are military training too) and switchable (R-4108) |
| **SR-7** | where rules live | a `RuleSet` component on the World Environment entity, saved in `scenario.json` by the existing save path; objective areas are their own area type (the `WeatherRegion` pattern) |
| **SR-8** | the output | a replicated `RuleState` (objective states, outcome, points per side) — the NED fact a frontend reads (R-4105) — and timeline lanes `rules.objective` / `rules.outcome` (replay, AAR, P-4) |
| **SR-9** | the end of the exercise | `EndExercise` sets the outcome and **pauses** the run; the instructor (Zeus) resumes, stops or reviews. ⛔ never an automatic unload |
| **SR-10** | authoring v1 | **data, not code**: a rules section in the editor (a list: condition · window · actions), the JSON grammar shaped like `checks.json` (anchors, windows, any / all / atLeast) |
| **SR-11** | one grammar with the regression checks | ⭐ the checks and the rules share the grammar AND the evaluator: a regression `expect` becomes a rule whose action is "record pass / fail" — ⛔ `ConditionBook`'s editor-only path then retires (R-4077, ruling 9: no two implementations) |
| **SR-12** | custom logic | S3: a **world-singleton Blueprint** on the World Environment entity (the runtime exists, `RegisterWorldSingleton` gets its first caller at load) with event nodes for `ObjectiveChanged` / `RuleFired` and action nodes for SR-5's actions |

**Rejected — one line each:**
- *Blueprints only* — every objective would need a visual script; data rules cover the common cases and stay replayable and editable by a non-programmer.
- *A rules-only condition language* — R-4077.
- *Rules evaluated on the master / orchestrator* — no ECS world there (R-4102).
- *Missions as the rules system* — a mission is ONE unit's task sequence, not the scenario's objectives; missions stay the action target.
- *Ending a run by unloading* — the instructor loses the moment to review.

## 5. Slices

| slice | builds | done when |
|---|---|---|
| **S1** | `RuleSet` / `RuleState` / `Objective`, `ScenarioRulesSystem`, the predicates `PolygonOccupancy` + `UnitKilled` + `ScenarioTime` + `ObjectiveState`, actions `Message` · `SetObjective` · `AwardPoints` · `EndExercise`, the objective area type, the `rules.*` lanes, the refusal when no node advertises `scenario.rules` | a scenario "hold the bridge until 10:00 / kill the 3 technicals" plays to Blue won and Red lost on a live cluster; the outcome is on the timeline of its recording; a rail per predicate and action |
| **S2** | `SpawnGroup`, `MissionCommand`, `SetSkillAmplifier`; the editor's rules section | a wave spawns when an objective falls; an author builds S1's scenario in the editor without JSON |
| **S3** | SR-11 (checks run on the rules evaluator) · SR-12 (world-singleton Blueprints) | the 103 `checks.json` pass through the rules evaluator; `ConditionBook` retired |
