# TASK-TRACKER — EQS v1.3

Progress checklist. Cross-reference: [TASK-DETAIL.md](./TASK-DETAIL.md),
[EQS_Design_v1.3_final.md](./EQS_Design_v1.3_final.md), [IMPLEM_DETAILS.md](./IMPLEM_DETAILS.md).

---

## Phase 1 — Foundations

- [ ] [TASK-EQS-001](./TASK-DETAIL.md#task-eqs-001--core-component-layouts) — Core component layouts (EqsResult, EqsCognitiveBuffer, EqsSensor)
- [ ] [TASK-EQS-002](./TASK-DETAIL.md#task-eqs-002--eqsresultpool-singleton-and-eqsresultevent) — EqsResultPool singleton and EqsResultEvent
- [ ] [TASK-EQS-003](./TASK-DETAIL.md#task-eqs-003--dds-wire-topics-and-translator-contracts) — DDS wire topics and translator contracts (stubs)
- [ ] [TASK-EQS-004](./TASK-DETAIL.md#task-eqs-004--eqsresultupdatesystem-brain-side) — EqsResultUpdateSystem (Brain side)
- [ ] [TASK-EQS-005](./TASK-DETAIL.md#task-eqs-005--stubbed-eqssolversystem-phase-1-stub) — Stubbed EqsSolverSystem + EqsModule wiring
- [ ] [TASK-EQS-006](./TASK-DETAIL.md#task-eqs-006--btree-lifecycle-nodes-waitforsensor--maintaineqssensor) — BTree lifecycle nodes (WaitForSensor + MaintainEqsSensor)

---

## Phase 2 — Entity-Shaped Queries with Cheap Tests

- [ ] [TASK-EQS-007](./TASK-DETAIL.md#task-eqs-007--full-dds-translator-implementations) — Full DDS translator implementations
- [ ] [TASK-EQS-008](./TASK-DETAIL.md#task-eqs-008--core-interfaces-ieqsgenerator-ieqstest-eqsquerytemplate) — Core interfaces: IEqsGenerator, IEqsTest, EqsQueryTemplate
- [ ] [TASK-EQS-009](./TASK-DETAIL.md#task-eqs-009--entitiesinradius-generator) — EntitiesInRadius generator
- [ ] [TASK-EQS-010](./TASK-DETAIL.md#task-eqs-010--factionfiltertest-and-distancescoretest) — FactionFilterTest and DistanceScoreTest
- [ ] [TASK-EQS-011](./TASK-DETAIL.md#task-eqs-011--time-sliced-eqssolversystem-phase-2-full) — Time-sliced EqsSolverSystem (Phase 2 full)

---

## Phase 3 — Positional Queries with Cheap LOS

- [ ] [TASK-EQS-012](./TASK-DETAIL.md#task-eqs-012--icover-provider-interface-and-coverpoint-struct) — ICoverProvider interface and CoverPoint struct
- [ ] [TASK-EQS-013](./TASK-DETAIL.md#task-eqs-013--coverpointsgenerator-ilosservice-cheaplineofsiightest) — CoverPointsGenerator, ILosService, CheapLineOfSightTest
- [ ] [TASK-EQS-015](./TASK-DETAIL.md#task-eqs-015--findcoverfromtarget-starter-template) — FindCoverFromTarget starter template

---

## Phase 4 — Navmesh Integration via DotRecast

- [ ] [TASK-EQS-016](./TASK-DETAIL.md#task-eqs-016--inavmeshprovider-interface) — INavmeshProvider interface
- [ ] [TASK-EQS-017](./TASK-DETAIL.md#task-eqs-017--navmeshsamplesgenerator-navmeshreachabletest-pathcostscoretest) — NavmeshSamplesGenerator, NavmeshReachableTest, PathCostScoreTest

---

## Phase 5 — Accurate LOS and State Machine

- [ ] [TASK-EQS-018](./TASK-DETAIL.md#task-eqs-018--sensorevalstate-component-and-eqssolvergelobalstate-singleton) — SensorEvalState component and EqsSolverGlobalState singleton
- [ ] [TASK-EQS-019](./TASK-DETAIL.md#task-eqs-019--accuratelineofsiightest-and-cross-tick-polling-in-eqssolversystem) — AccurateLineOfSightTest and cross-tick polling

---

## Phase 6 — Hot-Reload + Authoring

- [ ] [TASK-EQS-020](./TASK-DETAIL.md#task-eqs-020--eqstemplate-roslyn-source-generator) — [EqsTemplate] Roslyn source generator
- [ ] [TASK-EQS-021](./TASK-DETAIL.md#task-eqs-021--hot-reload-structurehash-sensorevalstate-hardsoft-reset) — Hot-reload: StructureHash + hard/soft reset

---

## Phase 7 — Diagnostics

- [ ] [TASK-EQS-022](./TASK-DETAIL.md#task-eqs-022--imgui-inspector-and-gizmo-projector) — ImGui inspector and gizmo projector

---

## Phase 8 — Integration Tests

- [ ] [TASK-EQS-023](./TASK-DETAIL.md#task-eqs-023--basic-round-trip-tests-editor--distributed) — Basic round-trip tests (Editor + Distributed)
- [ ] [TASK-EQS-024](./TASK-DETAIL.md#task-eqs-024--test-top-k-reduction-and-positional-sentinel-preservation) — Test: Top-K reduction and positional sentinel preservation
- [ ] [TASK-EQS-025](./TASK-DETAIL.md#task-eqs-025--test-raycast-budget-exhaustion-and-cross-tick-polling) — Test: Raycast budget exhaustion and cross-tick polling
- [ ] [TASK-EQS-026](./TASK-DETAIL.md#task-eqs-026--test-path-cost-vs-euclidean-distance-inversion) — Test: Path cost vs. Euclidean distance inversion
- [ ] [TASK-EQS-027](./TASK-DETAIL.md#task-eqs-027--test-stale-epoch-rejection-across-dds) — Test: Stale epoch rejection across DDS
- [ ] [TASK-EQS-028](./TASK-DETAIL.md#task-eqs-028--test-mid-evaluation-btree-subtree-abort) — Test: Mid-evaluation BTree subtree abort
- [ ] [TASK-EQS-029](./TASK-DETAIL.md#task-eqs-029--test-targetmemory-threat-threshold-bypassing) — Test: TargetMemory threat threshold bypassing

---

## Phase 9 — HideInCover BTree Behavior

- [ ] [TASK-EQS-030](./TASK-DETAIL.md#task-eqs-030--hideincover-blackboard-and-action_movetooptimalcover) — HideInCoverBlackboard and Action_MoveToOptimalCover
- [ ] [TASK-EQS-031](./TASK-DETAIL.md#task-eqs-031--hideincover_bt-full-behavior-definition) — HideInCover_BT full behavior definition
