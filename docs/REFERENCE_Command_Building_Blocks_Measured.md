<!--STATUS
state: LIVE
updated: 2026-10-18
current-answer: the whole file — a MEASURED reference, re-check a row before building on it (code moves)
stale-below: nothing.
known-rot: none — §5's SOP row re-measured 2026-10-27 (CE-1410: the shipped SOPs are C#-built).
known-conflict: none — the 2026-10-18 "everything in the blackboard" rule was corrected the same day (R-2042: choose by scope; AQ87 §4 stands)
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md — the attack / defence / clearing plays built from these blocks
  - docs/OVERVIEW_Behaviour_Building_Blocks.md — the MAP of the machinery; this file is the raw measurement under its command-and-control boxes
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — the worked multi-level example built only from the blocks measured here
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — §5b–§5d: the report concept these blocks implement
  - docs/blueprints/Architect_Question_87_Unit_Memory.md — OWNS unit memory (R-237)
  - docs/DESIGN_Decision_Layer.md — OWNS task · SOP · reaction (§4)
-->

# Command building blocks — what the engine has, measured *(2026-10-18)*

🔒 User, `2026-10-18`: *"It would be fine if you record the raw results from your searches as document, because that would serve as a
base for any other behavior design."* · *"line numbers change very often, so please do not use line numbers as a reference. Just file
name and maybe even just class name or method name"*.

**How this was measured:** codebase-memory graph (`search_graph` by name pattern and label, `trace_path`), then the source opened at
each symbol; grep only to confirm call sites the graph cannot resolve (⚠ the graph does not resolve calls to GENERIC methods, e.g.
`UnitMemory.Ref<T>` showed 0 callers; and some structs are not indexed as `Struct`). `check_index_coverage` not run (CLI only).
References are **symbols**, never line numbers. ⚠ A row is true as of the date above — re-check before building on it.

## 1. What a brain stores, and where

| block | what it is | symbols | measured |
|---|---|---|---|
| the store | one multi-block blackboard store per entity, keyed slots (≈ 47), a slot never moves once attached | `OccurrenceStoreAccess` (`Blueprints/Partitioning/OccurrenceStoreAccess.cs`) · `TryAttachSlot` · `OccurrenceKind` (Blueprint, BTree, Hsm, BlueprintBehavior, **UnitMemory**) | graph `^(RootParamsAccess\|RootStateAccess\|RootHsmAccess\|OccurrenceWorkingState\|OccurrenceStoreAccess\|BrainSlotScope)$` → 6 classes |
| slot keys | computed, never stored | `OccurrenceSlotKey` (`ComputeRootParamsKey`, root state, `ComputeHsmStateKey`, `ComputeNested`) | read |
| a behaviour's block | `{In; St}` = params + working state (a blueprint: `{Params; Variables}`), plus a brain state (cursor / HSM instance / `Exec`) | `RootParamsAccess`, `RootStateAccess`, `RootHsmAccess`, `OccurrenceWorkingState`; design `docs/blueprints/DESIGN_Unified_Behaviour_Run.md` | read |
| params | the order's JSON parsed into the root params slot when the behaviour starts | `BehaviorIngressSystem.ParseIntoShadow`, `def.ParseParams` | read |
| working state | a `ref TState ws` parameter of an action | e.g. `PeekAndFireNodes.PeekAndFire(ref PeekAndFireParams, ref PeekAndFireState ws, Entity, EntityRepository)` | read |
| ⭐ **shared slot = unit memory** | a `[UnitMemory]` struct shared by EVERY behaviour of the unit, keyed by its type, created on first touch with `new T()`, never swept by a behaviour switch, last writer wins, layout-guarded | attribute `UnitMemoryAttribute` (`Fdp.Core/DtoAttribute.cs`) · API `UnitMemory.Get<T>(world, unit)` / `Ref<T>` / `Set<T>` / `Has<T>` / `GetInView<T>(view, unit)` (`Fdp.Toolkits/Behavior/UnitMemory.cs`) · example `FiringPositionMemory` (`Combat/FiringPositionMemory.cs`) | graph `^UnitMemory$` [Class] → 1; `UnitMemory` → 29 hits |
| how a behaviour touches it | directly, or as a trailing `ref T` parameter of a `[SharedAi*]` method (the emitter binds it) | `UnitMemory.Ref<FiringPositionMemory>(world, self)` in `PeekAndFireNodes`, `VehicleFightFromCoverNodes`; emitter `UnitMemoryParams` (`AiEditor.Persistence/Emit/UnitMemoryParams.cs`) | read |
| can a plain SYSTEM write it? | ⭐ **mechanically yes** (the API needs only the world and the unit); ⛔ **no production system does today** (callers: two behaviour nodes, a gizmo read, tests) | — | grep confirmed (generic calls invisible to the graph) |
| ⚠ the design rule today | AQ87 §4: *"engine systems (non-behaviour code needs it) → an ECS component in a toolkit; behaviours only → a unit memory struct"*; "self only" | `docs/blueprints/Architect_Question_87_Unit_Memory.md` §4 | read — ✅ consistent with R-2042 (knowledge lives at its scope) |
| removed | `BlueprintSharedState`, the old entity scope | `OccurrenceSlotKey` header | read |

## 2. Knowledge held OUTSIDE the slots today

| component | holds | written by | read by |
|---|---|---|---|
| `TargetMemory` (`Perception/Components/PerceptionComponents.cs`) | a unit's contacts, decay 0.1 / s, forgotten below 0.5 untracked | `ThreatEvaluationSystem` (also publishes the edges; the decay runs on every holder) | `SopConditions.InContact`, sensor nodes, utility inputs |
| `SquadCognitiveState` (`Squad/State/SquadCognitiveState.cs`, `DataPolicy NoScenario`) | the squad commander's contact pool (16) + fire assignment | `SquadPerceptionMergeSystem.Run` (rebuilt from members' `TargetMemory`), `ThreatMatrixAssignmentSystem`, `SquadAssignment`, `SquadManeuverRunner`, `SquadDangerCrossingBehavior` | the runner, fire distribution, utility inputs (`SquadKnowsContact`, …) |
| `DangerAreaCognitiveBuffer` | a danger-area sensor's processed answer | `DangerAreaSensorSystem` | `UnitSensors.TryGetResults<T>`, SOP condition `DangerAreaAhead`, utility input `ActiveFeatureThreatRating` |
| `EqsCognitiveBuffer` | an EQS sensor's answer | `EqsSolverSystem` / `EqsResultUpdateSystem` (SimHost) | `Action_WaitForSensor` (`EqsLifecycleNodes`) |
| `Roe` | fire / reactions / force level | `RoeSystem.Apply` from `SetRoeEvent` | the fire gate, `RoeOf` |
| design quote | *"True entity-wide data is an ECS component field — not a variable"* | `docs/blueprints/DESIGN_Parameter_Model.md` | — |

## 3. Orders

| block | symbols | facts |
|---|---|---|
| assign a behaviour | `AssignBehaviorEvent` (`Behavior/Events/AssignBehaviorEvent.cs`) | `Entity`, `BehaviorName`, `JsonParams`, `Origin`, `Urgency` |
| a tactical intent | `AssignTacticalIntentEvent` | `Entity`, `IntentId`, `JsonParams`, `Origin`, `Kind` (Assign / Clear), `ClearsPriorIntent` |
| who sent it | `BehaviorOrigin` (`Unmarked, Sop, Superior, Operator, Reaction, Possession, Self`), rank rule `BehaviorOriginRank.Of` / `Admits` · `ReactionUrgency` (`Alert < Contact < UnderFire < Hit`) | `Behavior/Components/BehaviorOrigin.cs` |
| params DTO | `[Behavior(name, Params = typeof(DTO), Categories)]` on the BEHAVIOUR (`Hrot.Core/MapDefinitions/Behavior/BehaviorAttribute.cs`) · `[Order(name)]` a `DtoAttribute`, intent id `"Order.<Name>"` (`Orders/OrderAttribute.cs`) | — |
| intent → behaviour | `TacticalIntentResolutionSystem` (CGF): an intent becomes an `AssignBehaviorEvent`; an `Order.` intent is resolved by the RECEIVER's own class; a Clear becomes `ClearBehaviorEvent` | — |
| ⭐ commander → subordinate commander | `SquadManeuverRunner.Order` sends `AssignTacticalIntentEvent{IntentId = "Order.<name>"}` to a UNIT member (`IsUnit`); `SquadManeuverRunner.Publish` sends a behaviour name to a SOLDIER member; both recorded in `SubordinateOrders` (`Behavior/Components/SubordinateOrders.cs`), released when the commander's run ends (`Release`) | read |
| ⭐ mission plan | `MissionPlanQueue` (≤ 8 `MissionPhase`: behaviour hash + ONE trigger — `TimerElapsed` · `UnderAttack` · `HealthCritical` · `BehaviorFinished`; first trigger only, none ⇒ hold) + `ActiveMissionPlan` (names, JSON params); applied by `MissionControlExecutionSystem` (`CMD_REPLACE_MISSION`, `CMD_JUMP_TO_TASK`, abort), advanced by `MissionDirectorSystem` with `Origin = Superior`, a task that is an intent dispatched by `MissionAdapterSystem` (CGF); an immediate order clears the plan (`MissionControlExecutionSystem.ClearFor`); a reaction ending never advances it; ⛔ **only the operator writes plans** — `MissionControlIntent` publishers are `ScenarioMissionService` (editor/ExCon) and `MissionControlIngressTranslator` (wire); design: *"an ordinary end-user-facing surface"* (Decision Layer §2) | read |
| older precedents | `HillAttackCommanderNodes.Action_DispatchAllToBaseline` (intent `MoveToLocation`, Superior) · `CommanderNodes` (a stub sending `DefendArea`) | read |

## 4. The gate — `BehaviorIngressSystem`

| step | symbol | does |
|---|---|---|
| order of work | `Execute` | Admit → PauseRecord → Start → AfterAdmitted |
| admit | `Admit`, `AdmitsWithReactions` | the four reaction rules; ROE `StayOnTask` refuses reactions (`RoeOf.Reactions`) |
| pause | `PauseRecord` → `PausedTask` | name, JsonParams, Origin, Hash, InstanceId, BrainTier, HeldKeys, Restart — one paused task |
| start | `Start` | writes `BehaviorState` (ActiveBehaviorHash, InstanceId, BrainTier, Origin, Urgency, RunSince), the root slots, `BehaviorStartRecord` |
| after | `AfterAdmitted` | stores the paused task, or drops it when an order replaces the task |
| resume | `ResumePausedTask` | restores in place, or restarts on a storage clash |
| direct | `AssignNow` | the paused / editor path |
| ⛔ no general admit hook | the only per-start code is the behaviour's own params parse / resolver; mission-phase params via `MissionPhaseParams` | read |
| ⭐ ROE is NOT in an order | `SetRoeEvent` (Entity, Fire, Reactions, Origin, ReturnFireWindowSeconds, ForceLevel) → `RoeSystem.Apply`, origin-gated; design: *"set by an order — with the assignment or on its own… persists across tasks"* (Decision Layer §4.4); ⛔ no production code sends ROE with an order (publishers: the editor, `InitialBrainMaterializationSystem`); `MissionTask` has no ROE field | read |

## 5. SOP

| block | symbols | facts |
|---|---|---|
| asset | `BasicSquadSop` — C#-built (`AI.Behaviors/Brains/BasicSquadSop.cs`, CE-1410, R-1042; was `Assets/BTrees/Library/Sop/BasicSquadSop.btree.json`) | a BTree: Selector over Sequences of `Condition → React(BehaviorName, Urgency)` (the SOP order verbs `.React` / `.DoWhenIdle`; a JSON asset's `SopOrder` node binds the same key); blackboard of `SopSenseParams` / `SopAreaParams` |
| wake | `BrainTickSystem.TickSopSlots` | any `SensorChangedEvent` whose `Unit` is this unit wakes its SOP; otherwise periodic; edges recorded by `RecentSensesSystem` |
| react | `SopActions.React` | asks the gate first, then publishes `AssignBehaviorEvent{Origin = Reaction, Urgency}` |
| conditions | `[SharedAiCondition] bool X(ref P p, Entity self, EntityRepository world)` | `SopConditions.SensedFresh` (reads `RecentSenses` + `BehaviorState.RunSince`) · `SquadSopConditions.DangerAreaAhead` (reads the commander's `DangerAreaCognitiveBuffer` via `DangerCrossingPlay.NextArea` → `UnitSensors.TryGetResults`) |

## 6. Squad-level edges — the "system raises senses on a commander" pattern

`SquadSensing` (`Squad/Systems/SquadSensing.cs`), driven by `SquadCoordinationSystem.Execute` (commanders with `SquadCognitiveState` +
`UnitRoster`, only those whose `BehaviorState` this node owns): `PoolEdges` (SquadContact / SquadAllClear) · `UnderFire` (members' Hit /
NearMiss mapped to the commander via `UnitSubordinate`) · `Succession` (SquadLeaderDown) · `MemberDown` (`Target = member`) — each a
`repo.Bus.Publish(new SensorChangedEvent{Unit = commander, What = …})`. `SensorChangedEvent` carries `Unit`, `Sensor`, `Target`, the
change and a modality; `SensorChange` values run to `MissileWarning = 17`.

## 7. HSM authoring

| block | symbols | facts |
|---|---|---|
| parallel regions | `StateFlags.IsParallel` (`HsmKernelCore`); `HsmBuilder.InRegion` | ⛔ **no production HSM uses regions** — only `HsmOrthogonalRegions.hsm.json` (samples) and `HsmTwoChannelRegionsDemo.hsm.json` (tests); the editor's region canvas is a ui-lane gap |
| polled guard | fluent `s.On(0).GoTo("X").Guard<Bb, P[, WS]>(bb => bb.Params[, bb => bb.State], Method).Polled()` | example `CivilianBehavior` (`Wounded`, `InsideNow`) |
| guard method | `[SharedAiCondition] static bool G(ref P p, [ref WS ws,] Entity self, EntityRepository world [, ref T unitMemory])` | reads the ECS through `world`; a trailing `ref [UnitMemory]` is allowed |
| hosting | an HSM state hosts a BTree (E5), any tier hosted (S5a), blueprint `Run Behaviour` (S5d) | built |

## 8. "A system writes, behaviours read" — the template

`DangerAreaSensorSystem` (Simulation phase; its own doc: *"Behaviours only READ the processed buffer"*): ① apply each answer
(`DangerAreaResultEvent`) into a buffer (`DangerAreaCognitiveBuffer`), ② rate every tick from the unit's memory, ③ raise
`SensorChangedEvent` edges. Readers: `UnitSensors.TryGetResults<T>`, an SOP condition, a utility input
(`[UtilityInput("ActiveFeatureThreatRating")] float F(in UtilityInputCtx)`). EQS is the same shape (`EqsCognitiveBuffer`,
`Action_WaitForSensor` polls `IsReady`). `SquadKnowsContact` is a member reading its commander's `SquadCognitiveState`.

## 9. Related measurements from the same day *(already folded into designs)*

| fact | where it lives |
|---|---|
| the brain reads terrain directly in ~20 places (behaviors `CE-2286`, backend `CE-2287`) — the brain's terrain is for the map only (R-2037) | `docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md` §13b |
| EQS sensors are standing: `EqsSolverSystem` re-solves every live sensor at 10 Hz in a counted budget; `RefreshTick`; `EqsChildSensor.Refresh` re-asks | same, §13b |
| squad roles = `TacticalDesignation`, read only by succession and the medic pick; no infantry MG | same, §12 |
| no sector / arc target restriction anywhere; five target picks (`TopThreat`, `TopAim`, `LockTarget`, `ThreatMatrixAssignmentSystem`, `TopContact`) + the gate `AimAndFireExecutor` | same, §13 |
| a near miss carries no shooter; the gunshot SOUND carries the position (`SoundEmissionSystem`, `SoundContactEvent`) | same, §14 |
