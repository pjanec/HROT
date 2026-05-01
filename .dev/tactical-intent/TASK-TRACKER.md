# Task Tracker - Tactical Intent Distribution System

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

## Phase 1: Core Contracts

**Goal:** Define the shared event and mapper interface types in `Fdp.Toolkits` with no Hrot dependencies.

- [x] **TASK-TI001** AssignTacticalIntentEvent — [details](./TASK-DETAIL.md#task-ti001---add-assigntacticalintentevent)
- [x] **TASK-TI002** ITacticalOrderMapper Interface and TacticalIntentMapperRegistry — [details](./TASK-DETAIL.md#task-ti002---add-itacticalordermapper-interface-and-tacticalintentmapperregistry)

## Phase 2: Receiver-Side Resolution

**Goal:** Add `TacticalIntentResolutionSystem` so that published intents are translated to `AssignDoctrineEvent` on the same node.

- [x] **TASK-TI003** TacticalIntentResolutionSystem — [details](./TASK-DETAIL.md#task-ti003---implement-tacticalintentresolutionsystem)

## Phase 3: MissionAdapterSystem Modification

**Goal:** Replace `AssignDoctrineEvent` emission in `MissionAdapterSystem` with `AssignTacticalIntentEvent` so human-authored mission plans flow through the same resolution pipeline.

- [ ] **TASK-TI004** MissionAdapterSystem Emits AssignTacticalIntentEvent — [details](./TASK-DETAIL.md#task-ti004---change-missionadaptersystem-to-emit-assigntacticalintentevent)

## Phase 4: UI Discovery for Intent DTOs

**Goal:** Make generic intent DTOs discoverable by `DoctrineSchemaDiscovery` and visible in the Mission Editor behavior dropdown.

- [ ] **TASK-TI005** Commander Flag in DoctrineCategory — [details](./TASK-DETAIL.md#task-ti005---add-commander-flag-to-doctrinecategory)
- [ ] **TASK-TI006** Example Intent DTOs in Hrot.Core — [details](./TASK-DETAIL.md#task-ti006---add-example-intent-dtos-to-hrotcore)

## Phase 5: Network Transport

**Goal:** Allow `AssignTacticalIntentEvent` to cross Brain-node boundaries via a dedicated DDS topic and translator pair.

- [ ] **TASK-TI007** TacticalIntentRequest DDS Message and EDescriptorType — [details](./TASK-DETAIL.md#task-ti007---define-tacticalintentrequest-dds-message-and-edescriptortype)
- [ ] **TASK-TI008** TacticalIntentEgressTranslator — [details](./TASK-DETAIL.md#task-ti008---implement-tacticalintenteresstranslator)
- [ ] **TASK-TI009** TacticalIntentIngressTranslator — [details](./TASK-DETAIL.md#task-ti009---implement-tacticalintentingresstranslator)

## Phase 6: Commander BTree Integration and Example Mapper

**Goal:** Provide a working Commander BTree action and the first concrete mapper so the full pipeline can be exercised end-to-end.

- [ ] **TASK-TI010** Reference Commander BTree Action — [details](./TASK-DETAIL.md#task-ti010---reference-commander-btree-action)
- [ ] **TASK-TI011** DefendAreaMapper — [details](./TASK-DETAIL.md#task-ti011---implement-defendareamapper-first-concrete-mapper)
