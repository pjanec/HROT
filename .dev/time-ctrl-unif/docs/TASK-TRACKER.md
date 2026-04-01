# Task Tracker — Time Controller Unification

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: CQRS Message Layer — Foundation

**Goal:** Establish clean plain-field wire DTOs and local domain message types before touching any controller logic.

- [x] **TCU-M001** Fix Network Wire DTOs [details](./TASK-DETAIL.md#tcu-m001--fix-network-wire-dtos)
- [x] **TCU-M002** Introduce Local Domain Message Types [details](./TASK-DETAIL.md#tcu-m002--introduce-local-domain-message-types)

---

## Phase 2: Unified Master Controller

**Goal:** Replace MasterTimeController + SteppedMasterController + DistributedTimeCoordinator with a single self-contained MasterSyncController.

- [x] **TCU-MC001** MasterSyncController [details](./TASK-DETAIL.md#tcu-mc001--mastersynccotroller)

---

## Phase 3: Unified Slave Controller

**Goal:** Replace SlaveTimeController + SteppedSlaveController + SlaveTimeModeListener with a single SlaveSyncController; remove continuousControllerFactory workaround.

- [x] **TCU-SC001** SlaveSyncController [details](./TASK-DETAIL.md#tcu-sc001--slavesynccontroller)

---

## Phase 4: Role-Split Lockstep Translators

**Goal:** Replace the symmetric echo-prone FrameLockstepDescriptorTranslator with two stateless role-specific translators.

- [x] **TCU-TR001** MasterLockstepTranslator [details](./TASK-DETAIL.md#tcu-tr001--masterlocksteptranslator)
- [x] **TCU-TR002** SlaveLockstepTranslator [details](./TASK-DETAIL.md#tcu-tr002--slavelocksteptranslator)
- [x] **TCU-TR003** Update TimeNetworkModule Factory Methods [details](./TASK-DETAIL.md#tcu-tr003--update-timenetworkmodule-factory-methods)

---

## Phase 5: Application Wiring

**Goal:** Wire new controllers and translators in all four application hosts; delete obsolete classes.

- [ ] **TCU-W001** Wire MasterSyncController in Orchestrator [details](./TASK-DETAIL.md#tcu-w001--wire-mastersynccotroller-in-orchestrator)
- [ ] **TCU-W002** Wire SlaveSyncController in SimHost [details](./TASK-DETAIL.md#tcu-w002--wire-slavesynccontroller-in-simhost)
- [ ] **TCU-W003** Wire SlaveSyncController in CGF [details](./TASK-DETAIL.md#tcu-w003--wire-slavesynccontroller-in-cgf)
- [ ] **TCU-W004** Wire SlaveSyncController in IG [details](./TASK-DETAIL.md#tcu-w004--wire-slavesynccontroller-in-ig)
- [x] **TCU-W005** Update TimeControllerFactory [details](./TASK-DETAIL.md#tcu-w005--update-timecontrollerfactory)
- [ ] **TCU-W006** Delete Obsolete Classes [details](./TASK-DETAIL.md#tcu-w006--delete-obsolete-classes)

---

## Phase 6: Test Coverage

**Goal:** Full unit and integration test suite for the unified design.

- [x] **TCU-T001** Unit Tests: MasterSyncController [details](./TASK-DETAIL.md#tcu-t001--unit-tests-mastersynccotroller)
- [x] **TCU-T002** Unit Tests: SlaveSyncController [details](./TASK-DETAIL.md#tcu-t002--unit-tests-slavesynccontroller)
- [x] **TCU-T003** Unit Tests: Lockstep Translators [details](./TASK-DETAIL.md#tcu-t003--unit-tests-lockstep-translators)
- [x] **TCU-T004** Unit Tests: TimeControllerFactory (updated) [details](./TASK-DETAIL.md#tcu-t004--unit-tests-timecontrollerfactory-updated)
- [x] **TCU-T005** Unit Tests: DTO Round-Trip and Domain Events [details](./TASK-DETAIL.md#tcu-t005--unit-tests-dto-round-trip-and-domain-events)
- [ ] **TCU-T006** Integration Test: Full Pause/Step/Resume Cycle [details](./TASK-DETAIL.md#tcu-t006--integration-test-full-pausestepresume-cycle-in-process)
