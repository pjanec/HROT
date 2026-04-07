# Task Tracker — EyesAndMuscle Workstream

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: DRY Initialization Infrastructure

**Goal:** Extract repeated Hrot node bootstrap boilerplate into `HrotNodeBuilder` / `HrotNodeContext` so each new subsystem needs only a few lines to stand up world, kernel, DDS, time sync, and cluster slave.

- [x] **EAM-I001** HrotNodeBuilder and HrotNodeContext — [details](./TASK-DETAIL.md#eam-i001--fdpkernelbuilder-and-hrotnodecontext)
- [x] **EAM-I002** EnsureIdAllocatorRouting helper — [details](./TASK-DETAIL.md#eam-i002--ensureidallocatorroutinghelper)

---

## Phase 2: NedReplicationModule

**Goal:** Bundle NED translators with their tightly coupled DR/smoothing and ghost lifecycle systems into a single `IEcsModule`, making the network boundary explicit and swap-safe.

- [x] **EAM-N001** NedReplicationModule core — [details](./TASK-DETAIL.md#eam-n001--nedreplicationmodule-core)
- [x] **EAM-N002** Shared translator pack accessibility — [details](./TASK-DETAIL.md#eam-n002--shared-translator-pack-accessibility)

---

## Phase 3: EyesAndMuscle Subsystem

**Goal:** Implement the combined Muscle+Eyes `ISubsystem` using Phase 1+2 building blocks; prove the SoD async-module pattern end-to-end.

- [x] **EAM-E001** EyesAndMuscleSubsystem shell — [details](./TASK-DETAIL.md#eam-e001--eyesandmusclesubsystem-shell)
- [x] **EAM-E002** EyesAndMuscleModule (SoD async PoC) — [details](./TASK-DETAIL.md#eam-e002--eyesandmusclemodule-sod-async-poc)
- [x] **EAM-E003** EyesAndMuscle integration test — [details](./TASK-DETAIL.md#eam-e003--eyesandmuscle-integration-test)

---

## Phase 4: Migrate Existing Subsystems

**Goal:** Apply `HrotNodeBuilder` and `NedReplicationModule` universally to eliminate legacy init boilerplate in `SimHostApp`, `IgApplication`, and `CgfSubsystem`. This phase must complete in the same pass as Phases 1–3; leaving the old boot paths alive would mean two competing initialisation strategies in production.

- [x] **EAM-M001** Migrate SimHostApp — [details](./TASK-DETAIL.md#eam-m001--migrate-simhostapp-to-hrotnodebuilder--nedreplicationmodule)
- [x] **EAM-M002** Migrate IgApplication — [details](./TASK-DETAIL.md#eam-m002--migrate-igapplication-to-hrotnodebuilder--nedreplicationmodule)
- [x] **EAM-M003** Migrate CgfSubsystem — [details](./TASK-DETAIL.md#eam-m003--migrate-cgfsubsystem-to-hrotnodebuilder)
