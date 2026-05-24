# TASK-TRACKER — EQS v1.3

Progress checklist. Cross-reference: [TASK-DETAIL.md](./TASK-DETAIL.md),
[EQS_Design_v1.3_final.md](./EQS_Design_v1.3_final.md), [IMPLEM_DETAILS.md](./IMPLEM_DETAILS.md).

---

## Phase 1 — Foundations

- [x] [TASK-EQS-001](./TASK-DETAIL.md#task-eqs-001--core-component-layouts) — Core component layouts (EqsResult, EqsCognitiveBuffer, EqsSensor)
- [x] [TASK-EQS-002](./TASK-DETAIL.md#task-eqs-002--eqsresultpool-singleton-and-eqsresultevent) — EqsResultPool singleton and EqsResultEvent
- [x] [TASK-EQS-003](./TASK-DETAIL.md#task-eqs-003--dds-wire-topics-and-translator-contracts) — DDS wire topics and translator contracts (stubs)
- [x] [TASK-EQS-004](./TASK-DETAIL.md#task-eqs-004--eqsresultupdatesystem-brain-side) — EqsResultUpdateSystem (Brain side)
- [x] [TASK-EQS-005](./TASK-DETAIL.md#task-eqs-005--stubbed-eqssolversystem-phase-1-stub) — Stubbed EqsSolverSystem + EqsModule wiring
- [x] [TASK-EQS-006](./TASK-DETAIL.md#task-eqs-006--btree-lifecycle-nodes-waitforsensor--maintaineqssensor) — BTree lifecycle nodes (WaitForSensor + MaintainEqsSensor)

---

## Phase 2 — Entity-Shaped Queries with Cheap Tests

- [x] [TASK-EQS-007](./TASK-DETAIL.md#task-eqs-007--full-dds-translator-implementations) — Full DDS translator implementations
- [x] [TASK-EQS-008](./TASK-DETAIL.md#task-eqs-008--core-interfaces-ieqsgenerator-ieqstest-eqsquerytemplate) — Core interfaces: IEqsGenerator, IEqsTest, EqsQueryTemplate
- [x] [TASK-EQS-009](./TASK-DETAIL.md#task-eqs-009--entitiesinradius-generator) — EntitiesInRadius generator
- [x] [TASK-EQS-010](./TASK-DETAIL.md#task-eqs-010--factionfiltertest-and-distancescoretest) — FactionFilterTest and DistanceScoreTest
- [x] [TASK-EQS-011](./TASK-DETAIL.md#task-eqs-011--time-sliced-eqssolversystem-phase-2-full) — Time-sliced EqsSolverSystem (Phase 2 full)

---

## Phase 3 — Positional Queries with Cheap LOS

- [x] [TASK-EQS-012](./TASK-DETAIL.md#task-eqs-012--icover-provider-interface-and-coverpoint-struct) — ICoverProvider interface and CoverPoint struct
- [x] [TASK-EQS-013](./TASK-DETAIL.md#task-eqs-013--coverpointsgenerator-ilosservice-cheaplineofsiightest) — CoverPointsGenerator, ILosService, CheapLineOfSightTest
- [x] [TASK-EQS-015](./TASK-DETAIL.md#task-eqs-015--findcoverfromtarget-starter-template) — FindCoverFromTarget starter template

---

## Phase 4 — Navmesh Integration via DotRecast

- [x] [TASK-EQS-016](./TASK-DETAIL.md#task-eqs-016--inavmeshprovider-interface) — INavmeshProvider interface
- [x] [TASK-EQS-017](./TASK-DETAIL.md#task-eqs-017--navmeshsamplesgenerator-navmeshreachabletest-pathcostscoretest) — NavmeshSamplesGenerator, NavmeshReachableTest, PathCostScoreTest

---

## Phase 5 — Accurate LOS and State Machine

- [x] [TASK-EQS-018](./TASK-DETAIL.md#task-eqs-018--sensorevalsate-component-and-eqssolverglobalstate-singleton) — SensorEvalState component and EqsSolverGlobalState singleton
- [x] [TASK-EQS-019](./TASK-DETAIL.md#task-eqs-019--accuratelineofsiightest-and-cross-tick-polling-in-eqssolversystem) — AccurateLineOfSightTest and cross-tick polling

---

## Phase 6 — Hot-Reload + Authoring

- [x] [TASK-EQS-020](./TASK-DETAIL.md#task-eqs-020--eqstemplate-roslyn-source-generator) — [EqsTemplate] Roslyn source generator
- [x] [TASK-EQS-021](./TASK-DETAIL.md#task-eqs-021--hot-reload-structurehash-sensorevalstate-hardsoft-reset) — Hot-reload: StructureHash + hard/soft reset

---

## Phase 7 — Diagnostics

- [x] [TASK-EQS-022](./TASK-DETAIL.md#task-eqs-022--imgui-inspector-and-gizmo-projector) -- ImGui inspector and gizmo projector

---

## Phase 8 — Integration Tests

- [x] [TASK-EQS-023](./TASK-DETAIL.md#task-eqs-023--basic-round-trip-tests-editor--distributed) -- Basic round-trip tests (Editor offline T-RT1 + distributed T-DIS1 complete)
- [x] [TASK-EQS-024](./TASK-DETAIL.md#task-eqs-024--test-top-k-reduction-and-positional-sentinel-preservation) -- Test: Top-K reduction and positional sentinel preservation
- [x] [TASK-EQS-025](./TASK-DETAIL.md#task-eqs-025--test-raycast-budget-exhaustion-and-cross-tick-polling) -- Test: Raycast budget exhaustion and cross-tick polling (covered by BATCH-07 AccurateLosPhaseTests T-ALI1/2/3)
- [x] [TASK-EQS-026](./TASK-DETAIL.md#task-eqs-026--test-path-cost-vs-euclidean-distance-inversion) -- Test: Path cost vs. Euclidean distance inversion (covered by BATCH-06 PathCostInversionTests T-PCI1)
- [x] [TASK-EQS-027](./TASK-DETAIL.md#task-eqs-027--test-stale-epoch-rejection-across-dds) -- Test: Stale epoch rejection across DDS
- [x] [TASK-EQS-028](./TASK-DETAIL.md#task-eqs-028--test-mid-evaluation-btree-subtree-abort) -- Test: Mid-evaluation BTree subtree abort
- [x] [TASK-EQS-029](./TASK-DETAIL.md#task-eqs-029--test-targetmemory-threat-threshold-bypassing) -- Test: TargetMemory threat threshold bypassing

---

## Phase 9 — HideInCover BTree Behavior

- [x] [TASK-EQS-030](./TASK-DETAIL.md#task-eqs-030--hideincover-blackboard-and-action_movetooptimalcover) — HideInCoverBlackboard and Action_MoveToOptimalCover
- [x] [TASK-EQS-031](./TASK-DETAIL.md#task-eqs-031--hideincover_bt-full-behavior-definition) — HideInCover_BT full behavior definition
