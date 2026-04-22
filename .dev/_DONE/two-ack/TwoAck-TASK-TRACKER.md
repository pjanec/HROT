# Two-ACK Entity Lifecycle Pattern — Task Tracker

**Reference:** See [TWOACK-TASK-DETAIL.md](./TWOACK-TASK-DETAIL.md) for detailed task descriptions  
**Design:** [TWOACK-DESIGN.md](./TWOACK-DESIGN.md)

---

## Phase 1: Data Model Unification

**Goal:** Establish the unified DDS message contract in `Hrot.NED` — prerequisite for all other phases.

- [x] **TWOACK-DM001** Add `DeleteEntityRequest` struct [details](./TWOACK-TASK-DETAIL.md#twoack-dm001--add-deleteentityrequest-to-datamodel)
- [x] **TWOACK-DM002** Rename `SstErrorCode` → `SstStatusCode` [details](./TWOACK-TASK-DETAIL.md#twoack-dm002--rename-ssterrorcode-to-sststatuscode)
- [x] **TWOACK-DM003** Expand `CreateUpdateDeleteEntityAck`, retire `CreateEntityAck` [details](./TWOACK-TASK-DETAIL.md#twoack-dm003--expand-createupdatedeleteentityack-and-retire-createentityack)

---

## Phase 2: SimHost Two-ACK Pipeline

**Goal:** Add the two-phase ACK state machine in `Hrot.SimHost` without touching FDP.

- [x] **TWOACK-SH001** Create `SstRequestFinalizationSystem` [details](./TWOACK-TASK-DETAIL.md#twoack-sh001--create-sstrequestfinalizationsystem)
- [x] **TWOACK-SH002** Update `CreateEntityRequestSystem` for two-ACK [details](./TWOACK-TASK-DETAIL.md#twoack-sh002--update-createentityrequestsystem-for-two-ack)
- [x] **TWOACK-SH003** Create `DeleteEntityRequestSystem` [details](./TWOACK-TASK-DETAIL.md#twoack-sh003--create-deleteentityrequestsystem)

---

## Phase 3: IOS Client Adaptation

**Goal:** Make the IOS correctly handle two-phase ACKs, lock the UI safely, and surface errors explicitly.

- [x] **TWOACK-IOS001** Update IOS ingress pipeline [details](./TWOACK-TASK-DETAIL.md#twoack-ios001--update-ios-ingress-pipeline)
- [x] **TWOACK-IOS002** Rewrite `ProcessEntityCreationAcks` for two-ACK state machine [details](./TWOACK-TASK-DETAIL.md#twoack-ios002--rewrite-processentitycreationacks-for-two-ack-state-machine)
- [x] **TWOACK-IOS003** Lock UI for pending entities [details](./TWOACK-TASK-DETAIL.md#twoack-ios003--lock-ui-for-pending-entities)
- [x] **TWOACK-IOS004** Surface explicit creation errors to operator [details](./TWOACK-TASK-DETAIL.md#twoack-ios004--surface-explicit-creation-errors-to-operator)
