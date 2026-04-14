# Task Tracker: OrchestratorSubsystem Hexagonal Architecture & Bus Unification

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions
**Design:** See [DESIGN.md](./DESIGN.md) for architecture and rationale

---

## Phase 1: Unify Event Buses (Fix IsPaused Bug)

**Goal:** Collapse all secondary `FdpEventBus` instances into one per subsystem, across every orchestrator-aware node, restoring correct pause UI state.

- [x] **HEXAG2-S001** Collapse all buses in `OrchestratorSubsystem` into single `_bus` [details](./TASK-DETAIL.md#hexag2-s001--collapse-dual-buses-into-single-_bus)
- [x] **HEXAG2-S001b** Collapse all buses in `ExConSubsystem` into single `_bus` [details](./TASK-DETAIL.md#hexag2-s001b--collapse-all-buses-in-exconsubsystem-into-single-_bus)
- [x] **HEXAG2-S002** Strict 4-phase single-swap `Update()` loop [details](./TASK-DETAIL.md#hexag2-s002--strict-4-phase-single-swap-update-loop)

---

## Phase 2: Hexagonal Architecture Compliance

**Goal:** Remove all CycloneDDS dependencies from subsystem domain logic; close all C# event couplings.

- [ ] **HEXAG2-S003** Define `IOrchestrationTranslator` interface [details](./TASK-DETAIL.md#hexag2-s003--define-iOrchestrationtranslator-interface)
- [ ] **HEXAG2-S004** Extend `INetworkFactory` with master + slave ports [details](./TASK-DETAIL.md#hexag2-s004--extend-inetworkfactory-with-createorchestratortranslators)
- [ ] **HEXAG2-S005** Move master translators to `Hrot.Network.Orchestration` [details](./TASK-DETAIL.md#hexag2-s005--move-master-translators-to-hrotnetworkOrchestration)
- [ ] **HEXAG2-S010** Sever `unhandledRequestCallback`; add time-control intents to bus [details](./TASK-DETAIL.md#hexag2-s010--sever-unhandledRequestcallback-from-clusterOpmastertranslator)
- [ ] **HEXAG2-S011** Eliminate `ClusterMaster.TimeControlRequested` C# event; wire `MasterSyncController` to bus [details](./TASK-DETAIL.md#hexag2-s011--eliminate-clustermastertimecontrolrequested-c-event)
- [ ] **HEXAG2-S006** Implement `CreateOrchestratorTranslators` in `NedNetworkFactory` [details](./TASK-DETAIL.md#hexag2-s006--implement-createorchestratortranslators-in-nednetworkfactory)
- [ ] **HEXAG2-S007** Extract `DdsIdAllocatorServer` behind `CreateIdAllocatorServer()` port [details](./TASK-DETAIL.md#hexag2-s007--extract-ddsidAllocatorserver-behind-dedicated-factory-port)
- [ ] **HEXAG2-S008** Refactor `OrchestratorSubsystem` to use `INetworkFactory` [details](./TASK-DETAIL.md#hexag2-s008--refactor-orchestratorsubsystem-to-use-inetworkfactory)
- [ ] **HEXAG2-S012** Slave subsystem factory refactor (`ExCon`, `SimHost`, `CGF`) [details](./TASK-DETAIL.md#hexag2-s012--slave-subsystem-factory-refactor)
- [ ] **HEXAG2-S009** Verify composition root wiring [details](./TASK-DETAIL.md#hexag2-s009--verify-composition-root-wiring)
