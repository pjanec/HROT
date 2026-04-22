# Task Tracker: ClusterMaster CQRS Decoupling

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for full task descriptions and success conditions.  
**Design:** See [DESIGN.md](./DESIGN.md) for architecture and phased overview.

---

## Phase 1 — FDP Domain Enums and Event DTOs

**Goal:** Establish the pure FDP domain vocabulary — no existing behaviour changes, no breaking changes.

- [x] **CMC-S001** Domain Enums (ClusterState, ClusterOpType, NodeOpType) in FDP.Toolkit.Orchestration [details](./TASK-DETAIL.md#cmc-s001--domain-enums-in-fdptoolkitorchestration) ✅ BATCH-01
- [x] **CMC-S002** Core CQRS event structs (ExecuteClusterOpIntent, ClusterOpCompletedEvent, ExecuteNodeOpIntent, NodeOpCompletedEvent) [details](./TASK-DETAIL.md#cmc-s002--core-cqrs-event-bus-structs) ✅ BATCH-01
- [x] **CMC-S003** Specific operation payload intents (TransitionStateIntent, ManageEpisodeIntent, SeekReplayIntent, CancelOperationIntent, ExecuteStorageOpIntent) [details](./TASK-DETAIL.md#cmc-s003--specific-operation-payload-intent-structs) ✅ BATCH-01

---

## Phase 2 — IClusterStateHandler Enum Migration

**Goal:** Replace raw `int` operation IDs with strongly-typed `NodeOpType` enum throughout the handler interface and all implementations.

- [x] **CMC-S004** IClusterStateHandler.CanHandle → NodeOpType enum; update all handler implementations [details](./TASK-DETAIL.md#cmc-s004--iclusterstatehandlercanhandle--nodeoptype) ✅ BATCH-02
- [x] **CMC-S005** OrchestrationCommand.OperationId changed from int to NodeOpType [details](./TASK-DETAIL.md#cmc-s005--orchestrationcommand-uses-nodeoptype) ✅ BATCH-02

---

## Phase 3 — ClusterSlave Event Bus Integration

**Goal:** ClusterSlave stops using IOrchestrationTransport and operates purely via FdpEventBus.

- [x] **CMC-S006** ClusterSlave consumes ExecuteNodeOpIntent from FdpEventBus; publishes NodeOpCompletedEvent [details](./TASK-DETAIL.md#cmc-s006--clusterslave-reads-from-fdpeventbus) ✅ BATCH-03
- [x] **CMC-S007** Delete IOrchestrationTransport and DdsOrchestrationTransport [details](./TASK-DETAIL.md#cmc-s007--delete-iorchestatransport-and-ddsorchestatransport) ✅ BATCH-03

---

## Phase 4 — ClusterMaster Event Bus Integration

**Goal:** ClusterMaster has zero DDS references and zero JSON parsing; it is a pure domain state machine.

- [x] **CMC-S008** Remove DDS readers from ClusterMaster; consume typed intents from FdpEventBus [details](./TASK-DETAIL.md#cmc-s008--remove-dds-from-clustermaster-ingress) ✅ BATCH-04
- [x] **CMC-S009** Remove DDS writers from ClusterMaster; publish typed events to FdpEventBus [details](./TASK-DETAIL.md#cmc-s009--remove-dds-from-clustermaster-egress) ✅ BATCH-04
- [x] **CMC-S010** Remove all JsonDocument.Parse and PayloadJson parsing from ClusterMaster and handlers [details](./TASK-DETAIL.md#cmc-s010--remove-json-parsing-from-clustermaster-and-handlers) ✅ BATCH-04

---

## Phase 5 — Application Layer Translators

**Goal:** Stateless translator classes in the Hrot layer act as the Anti-Corruption Layer between DDS and the FDP domain.

- [x] **CMC-S011** Hrot-layer JSON payload DTOs with JsonStringEnumConverter support [details](./TASK-DETAIL.md#cmc-s011--hrot-json-payload-dtos) ✅ BATCH-05
- [x] **CMC-S012** NodeOpSlaveTranslator (replaces DdsOrchestrationTransport) [details](./TASK-DETAIL.md#cmc-s012--nodeopslavetranslator) ✅ BATCH-05
- [x] **CMC-S013** NodeOpMasterTranslator [details](./TASK-DETAIL.md#cmc-s013--nodeopmastertranslator) ✅ BATCH-05
- [x] **CMC-S014** ClusterOpMasterTranslator [details](./TASK-DETAIL.md#cmc-s014--clusteropmastertranslator) ✅ BATCH-05
- [x] **CMC-S015** EventDrivenStorageGateway [details](./TASK-DETAIL.md#cmc-s015--eventdrivengateway) ✅ BATCH-05

---

## Phase 6 — Composition Root and Integration

**Goal:** Wire everything together; validate AllInOne and distributed topologies; full regression.

- [x] **CMC-S016** Update composition roots (Orchestrator, SimHost, IG, AllInOne) [details](./TASK-DETAIL.md#cmc-s016--update-composition-roots) ✅ BATCH-06
- [x] **CMC-S017** Integration tests: AllInOne 2PC end-to-end and translator round-trip [details](./TASK-DETAIL.md#cmc-s017--integration-tests-for-cqrs-orchestration) ✅ BATCH-06

---

## Tech Debt Resolved

- [x] **DEBT-007** ClusterSlave multi-intent queue + dedup key fix — resolves 2 pre-existing AllSubsystems test failures ✅ BATCH-07
- [x] **DEBT-008** Document DdsIdAllocatorServer hosting requirement in bus-mode ClusterMaster constructor ✅ BATCH-07 (documentation only)
- [x] **DEBT-004** Document `NodeOpCompletedEvent.ResultPayload` allowed types ✅ BATCH-07 (documentation only)
