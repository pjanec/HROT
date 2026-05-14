# Stride Mock — Task Tracker

**Reference:** See [TASK-DETAILS.md](./TASK-DETAILS.md) for detailed task descriptions and success conditions.  
**Design:** See [DESIGN.md](./DESIGN.md) for architecture and rationale.

---

## Phase 1 — Foundation: Project Scaffolding

**Goal:** Create the two new C# projects and wire them into the solution.

- [x] **SM-001** Create project scaffolding (`Hrot.StrideMock` + `Hrot.FakeStrideApp` + solution wiring) [details](./TASK-DETAILS.md#sm-001--create-project-scaffolding)

---

## Phase 2 — Shared Application Bootstrapper

**Goal:** Extract the common 7-phase initialisation sequence to `Hrot.Common.Infrastructure` to eliminate duplication across SimHost, IG, and StrideMock.

- [x] **SM-002** Implement `SharedApplicationBootstrapper` (Template Method, 7-phase pipeline, 5 trap-safe order) [details](./TASK-DETAILS.md#sm-002--implement-sharedapplicationbootstrapper)

---

## Phase 3 — Core Integration Library

**Goal:** Build the engine-agnostic `Hrot.StrideMock` library: bootstrapper, ECS sync script, visual effects.

- [x] **SM-003** Implement `StrideNodeBootstrapper` (concrete bootstrapper, full SimHost-role parity, gizmo terminal, slave time sync) [details](./TASK-DETAILS.md#sm-003--implement-stridenodebootstrapper)
- [x] **SM-004** Implement `SyncFdpToStrideScript` (2-pass differential ECS sync, cluster state gating) [details](./TASK-DETAILS.md#sm-004--implement-syncfdptostridesscript)
- [x] **SM-005** Visual effects wiring (`EventToEffectSystem` + `VisualEffectCleanupSystem`, `FakeStrideEffect` rendering) [details](./TASK-DETAILS.md#sm-005--visual-effects-wiring)

---

## Phase 4 — ClusterRunner Integration

**Goal:** Plug `StrideMockSubsystem` into `ClusterRunner` with camera sync, gated rendering, and CLI support.

- [x] **SM-006** Implement `StrideMockSubsystem` (`ISubsystem` + `IMapCameraProvider`, camera pan/zoom, DrawWorld/DrawUI) [details](./TASK-DETAILS.md#sm-006--implement-stridemocksubsystem)
- [x] **SM-007** Wire `StrideMockSubsystem` into `ClusterRunner` (CLI validation, NodeId offset 700, project reference) [details](./TASK-DETAILS.md#sm-007--wire-stridemocksubsystem-into-clusterrunner)

---

## Phase 5 — Standalone App

**Goal:** Build the independent `FakeStrideApp` executable for lightweight testing outside `ClusterRunner`.

- [x] **SM-008** Implement `FakeStrideApp` (Raylib/ImGui shell, 2D entity rendering, map pan/zoom) [details](./TASK-DETAILS.md#sm-008--implement-fakestrideapp)

---

## Phase 6 — DRY Refactoring of Existing Nodes

**Goal:** Migrate `SimHostApp` and `IgApplication` to use `SharedApplicationBootstrapper`, eliminating duplicated initialisation code.

- [ ] **SM-009** Refactor `SimHostApp` to use `SharedApplicationBootstrapper` (all SimHost tests green) [details](./TASK-DETAILS.md#sm-009--refactor-simhostapp-to-use-sharedapplicationbootstrapper)
- [ ] **SM-010** Refactor `IgApplication` to use `SharedApplicationBootstrapper` (all IG tests green) [details](./TASK-DETAILS.md#sm-010--refactor-igapplication-to-use-sharedapplicationbootstrapper)

---

## Phase 7 — Integration Gate

**Goal:** Verify all success conditions are met end-to-end before marking the workstream complete.

- [ ] **SM-011** Full integration validation gate (replay safety, recording, 2PC, diagnostics, time sync, camera sync) [details](./TASK-DETAILS.md#sm-011--full-integration-validation-gate)
