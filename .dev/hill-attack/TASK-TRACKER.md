# Hill Attack Group Behavior — Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: EQS Infrastructure

**Goal:** Implement the generic Area Query System batch pipeline that allows Brain-tier AI
to query entities inside a polygon area, mirroring the PathfindingBatchData/RaycastBatchData
patterns.

- [x] **TASK-HA001** AreaQueryBatchData Types and Component Registration [details](./TASK-DETAIL.md#task-ha001-areaquerybatchdata-types-and-component-registration) *(BATCH-01)*
- [x] **TASK-HA002** AreaQuerySolverSystem (Muscle Tier, SoD Module) [details](./TASK-DETAIL.md#task-ha002-areaquerysolversystem-muscle-tier-sod-module) *(BATCH-01)*
- [x] **TASK-HA003** AreaQueryInitializationSystem (Brain Tier, per-frame reset) [details](./TASK-DETAIL.md#task-ha003-areaqueryinitializationsystem-brain-tier-cgf) *(BATCH-01)*
- [x] **TASK-HA004** EQS Network Translators [details](./TASK-DETAIL.md#task-ha004-eqs-network-translators)

---

## Phase 2: Hill Attack Data Contracts

**Goal:** Define the unmanaged DTOs for both behaviors, verified to fit within memory
budget constraints.

- [x] **TASK-HA005** Commander DTOs [details](./TASK-DETAIL.md#task-ha005-commander-dtos) *(BATCH-01)*
- [x] **TASK-HA006** Tank DTOs [details](./TASK-DETAIL.md#task-ha006-tank-dtos) *(BATCH-01)*

---

## Phase 3: HullDownAttackRun Behavior

**Goal:** Implement the subordinate tank behavior, mapper, and register it so it can be
dispatched via the TacticalIntent pipeline.

- [x] **TASK-HA007** Condition_HasTarget and Action_CreepToAndBeyondSlot [details](./TASK-DETAIL.md#task-ha007-condition_hastarget-and-action_creeptoandbeyondslot) *(BATCH-01 + BATCH-02 corrective tests)*
- [x] **TASK-HA008** Action_AimAndFireSpecific and Action_ReverseToBaseline [details](./TASK-DETAIL.md#task-ha008-action_aimandfirespecific-and-action_reversetobaseline) *(BATCH-01 + BATCH-02 corrective tests)*
- [x] **TASK-HA009** HullDownAttackRun BTree, Mapper, and Registration [details](./TASK-DETAIL.md#task-ha009-hulldownattackrun-btree-mapper-and-registration) *(BATCH-01 + BATCH-02 corrective tests)*

---

## Phase 4: PlatoonHillAttack Behavior

**Goal:** Implement the commander behavior including the EQS integration, wave dispatch,
slot management, and register it.

- [x] **TASK-HA010** Action_CalculateSegments, Action_DispatchAllToBaseline, Condition_AreAllAtBaseline [details](./TASK-DETAIL.md#task-ha010-action_calculatesegments-action_dispatchalltobaseline-condition_areallatbaseline) *(BATCH-02)*
- [x] **TASK-HA011** Action_RequestAreaQuery and Condition_IsAreaQueryResolved [details](./TASK-DETAIL.md#task-ha011-action_requestareaquery-and-condition_isareaqueryresolved) *(BATCH-02)*
- [x] **TASK-HA012** Action_DispatchWaveWithTargets and Condition_IsWaveCompleted [details](./TASK-DETAIL.md#task-ha012-action_dispatchwavewithtargets-and-condition_iswavecompleted) *(BATCH-02)*
- [x] **TASK-HA013** PlatoonHillAttack BTree Definition and Registration [details](./TASK-DETAIL.md#task-ha013-platoonhillattack-btree-definition-and-registration) *(BATCH-02)*

---

## Phase 5: TKB Blueprint and Integration Validation

**Goal:** Wire the behaviors into entity blueprints and validate the full end-to-end
scenario.

- [x] **TASK-HA014** TKB Blueprint Updates [details](./TASK-DETAIL.md#task-ha014-tkb-blueprint-updates) *(BATCH-02)*
- [x] **TASK-HA015** Integration Test (Scenario-based) [details](./TASK-DETAIL.md#task-ha015-integration-test-scenario-based)

---

## Phase 6: JSON Authoring DTO and ParseParams Delegate

**Goal:** Bridge mission-plan authoring (WGS-84 coordinates, network entity IDs) to the
simulation tier (ENU Cartesian floats, local ECS handles) on the cold ingress path.

- [x] **TASK-HA016** PlatoonHillAttack JSON DTO and ParseParams Delegate [details](./TASK-DETAIL.md#task-ha016-platoonhillattack-json-dto-and-parseparams-delegate) *(BATCH-02)*
