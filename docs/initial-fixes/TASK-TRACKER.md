# Task Tracker — Initial Fixes

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for full task specifications  
**Design:** See [DESIGN.md](./DESIGN.md) for architectural rationale

---

## Phase 1: SimHost Architecture Fixes

**Goal:** Correct physics component assignment, doctrine preemption signalling, and EntityMaster DDS publication so SimHost is a fully compliant FDP authority node.

- [x] **TASK-IF001** Remove VehicleState Contamination — [details](./TASK-DETAIL.md#task-if001-remove-vehiclestate-contamination)
- [x] **TASK-IF002** Fix Doctrine Preemption — [details](./TASK-DETAIL.md#task-if002-fix-doctrine-preemption)
- [x] **TASK-IF003** Publish EntityMaster DDS Topic — [details](./TASK-DETAIL.md#task-if003-publish-entitymaster-dds-topic)

---

## Phase 2: IG Architecture Fixes

**Goal:** Make IG a fully compliant FDP ghost-reader node with correct remote ownership, dead-reckoning interpolation, and DDS-routed entity creation.

- [x] **TASK-IF004** Fix Ghost Ownership in EntityMasterTranslator — [details](./TASK-DETAIL.md#task-if004-fix-ghost-ownership-in-entitymastertranslator)
- [x] **TASK-IF005** Register TransformSyncSystem — [details](./TASK-DETAIL.md#task-if005-register-transformsyncsystem)
- [x] **TASK-IF006** Fix Rogue Local Spawning in CreationTool — [details](./TASK-DETAIL.md#task-if006-fix-rogue-local-spawning-in-creationtool)

---

## Phase 3: UI Panel Activation

**Goal:** Make all ImGui panels visible and functional in both IOS and IG at application startup.

- [x] **TASK-IF007** Uncomment IOS Draw Methods — [details](./TASK-DETAIL.md#task-if007-uncomment-ios-draw-methods)
- [x] **TASK-IF008** Connect IG UI Panels to App Loop — [details](./TASK-DETAIL.md#task-if008-connect-ig-ui-panels-to-app-loop)
