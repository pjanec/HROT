# Task Tracker — Modular-2: BDC Network Plugin and Assembly Consolidation

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: FDP Layer Consolidation

**Goal:** Collapse 20+ fragmented FDP assemblies into 4 cohesive deployment units.

- [x] **TASK-P1-001** Create Fdp.Core [details](./TASK-DETAIL.md#task-p1-001-create-fdpcore) ✅ BATCH-01
- [x] **TASK-P1-002** Create Fdp.Engine [details](./TASK-DETAIL.md#task-p1-002-create-fdpengine) ✅ BATCH-02
- [x] **TASK-P1-003** Create Fdp.Presentation [details](./TASK-DETAIL.md#task-p1-003-create-fdppresentation) ✅ BATCH-03
- [x] **TASK-P1-004** Create Fdp.Network.Cyclone [details](./TASK-DETAIL.md#task-p1-004-create-fdpnetworkcyclone) ✅ BATCH-03

---

## Phase 2: Hrot Layer Consolidation

**Goal:** Create clean Hrot.Core (pragmatic DDS base), Hrot.Network.Orchestration, and Hrot.Presentation.

- [x] **TASK-P2-001** Create Hrot.Core [details](./TASK-DETAIL.md#task-p2-001-create-hrotcore) ✅ BATCH-04
- [x] **TASK-P2-002** Create Hrot.Presentation [details](./TASK-DETAIL.md#task-p2-002-create-hrotpresentation) ✅ BATCH-05
- [x] **TASK-P2-003** Create Hrot.Network.Orchestration [details](./TASK-DETAIL.md#task-p2-003-create-hrotnetworkorchestration) ✅ BATCH-04

---

## Phase 3: INetworkFactory Plugin Contract

**Goal:** Define the neutral plugin boundary and create NED + BDC simulation-data network adapters.

- [x] **TASK-P3-001** Define INetworkFactory and Neutral Interfaces [details](./TASK-DETAIL.md#task-p3-001-define-inetworkfactory-and-neutral-interfaces-in-hrotcore) ✅ BATCH-05
- [x] **TASK-P3-002** Create Hrot.Network.NED [details](./TASK-DETAIL.md#task-p3-002-create-hrotnetworkned) ✅ BATCH-06
- [x] **TASK-P3-003** Create Hrot.Network.BDC [details](./TASK-DETAIL.md#task-p3-003-create-hrotnetworkbdc) ✅ BATCH-07

---

## Phase 4: Subsystem Decoupling

**Goal:** Remove all direct NED/DDS coupling from subsystem plugin libraries; move subsystem adapters into their plugin assemblies.

- [x] **TASK-P4-001** Decouple ExCon from NED [details](./TASK-DETAIL.md#task-p4-001-decouple-excon-from-ned) ✅ BATCH-08
- [x] **TASK-P4-002** Decouple SimHost from NED [details](./TASK-DETAIL.md#task-p4-002-decouple-simhost-from-ned) ✅ BATCH-09
- [x] **TASK-P4-003** Decouple IG and CGF from NED [details](./TASK-DETAIL.md#task-p4-003-decouple-ig-and-cgf-from-ned) ✅ BATCH-16 (CGF complete; IG partial — Task 19 blocked by OrchestratePersonalRouteAsync, deferred to BATCH-17)
- [x] **TASK-P4-004** Move ISubsystem Adapters into Plugin Assemblies [details](./TASK-DETAIL.md#task-p4-004-move-isubsystem-adapters-into-plugin-assemblies) ✅ BATCH-12
- [x] **TASK-P4-005** Implement OfflineNetworkFactory for Hrot.Editor [details](./TASK-DETAIL.md#task-p4-005-implement-offlinenetworkfactory-for-hroteditor) ✅ BATCH-13

---

## Phase 5: Composition Root Redesign

**Goal:** Make Hrot.ClusterRunner a pure dynamic loader with no hardcoded subsystem references.

- [x] **TASK-P5-001** Delete RunMode Enum and Refactor CLI Parsing [details](./TASK-DETAIL.md#task-p5-001-delete-runmode-enum-and-refactor-cli-parsing) ✅ BATCH-13
- [x] **TASK-P5-002** Implement In-Memory Reflection Scan in Program.cs [details](./TASK-DETAIL.md#task-p5-002-implement-in-memory-reflection-scan-in-programcs) ✅ BATCH-14
- [x] **TASK-P5-003** Add --network CLI Flag [details](./TASK-DETAIL.md#task-p5-003-add---network-cli-flag) ✅ BATCH-14

---

## Phase 6: Test Harness Update

**Goal:** Update integration test harnesses to use the new INetworkFactory injection pattern.

- [~] **TASK-P6-001** Update Integration Test Harnesses [details](./TASK-DETAIL.md#task-p6-001-update-integration-test-harnesses) — MockNetworkFactory created; harness INetworkFactory injection deferred (DEBT-P6-001-A)
