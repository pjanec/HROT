# Gizmos-2 Headless — Task Tracker

**Reference:** See [TASK-DETAILS.md](./TASK-DETAILS.md) for detailed task descriptions.

---

## Phase 1: Core Infrastructure — Zero-CPU Headless

**Goal:** Allow gizmo systems to run at zero CPU cost when no terminal is connected. Ensure safe
cleanup of interactive gizmos when the last terminal disconnects.

- [x] **GZH-001** Terminal lifecycle events [details](./TASK-DETAILS.md#gzh-001--terminalconnectedevent--terminaldisconnectedevent)
- [x] **GZH-002** `GizmoExecutionController` [details](./TASK-DETAILS.md#gzh-002--gizmoexecutioncontroller)
- [x] **GZH-003** Wire gizmo systems into `TogglablePostSimulationGroup` in all composition roots [details](./TASK-DETAILS.md#gzh-003--wire-gizmo-systems-into-togglablepostsimulationgroup)
- [x] **GZH-004** `GlobalGizmoManager` — handle `TerminalDisconnectedEvent` [details](./TASK-DETAILS.md#gzh-004--globalgizmomanager--handle-terminaldisconnectedevent)
- [x] **GZH-005** `DataDrivenGizmoSystem` — handle `TerminalDisconnectedEvent` [details](./TASK-DETAILS.md#gzh-005--datadrivengizmosystem--handle-terminaldisconnectedevent)

---

## Phase 2: UI State Infrastructure

**Goal:** Enable backend gizmos to push live DTO state to any connected terminal transparently,
using a dual-channel architecture that separates high-frequency primitives from low-frequency JSON.

- [x] **GZH-006** `StructInspectorProjector<T>` [details](./TASK-DETAILS.md#gzh-006--structinspectorprojectort)
- [x] **GZH-007** `GizmoUiStateHub` (multiplexer) [details](./TASK-DETAILS.md#gzh-007--gizmouistatehub)
- [x] **GZH-008** `LocalGizmoUiStateTransport` [details](./TASK-DETAILS.md#gzh-008--localgizmouistatetransport)

---

## Phase 3: Dynamic Terminal Modules

**Goal:** Enable hot-plug installation and removal of local (Raylib) and remote (DDS) terminal
modules at runtime without restarting the simulation.

- [x] **GZH-009** `LocalTerminalModule` [details](./TASK-DETAILS.md#gzh-009--localterminalmodule)
- [x] **GZH-010** `GizmoNetworkTransportModule` [details](./TASK-DETAILS.md#gzh-010--gizmonetworktransportmodule)

---

## Phase 4: `LayerControlGizmo` Upgrade

**Goal:** Upgrade the existing `LayerControlGizmo` to follow the clean architecture: dynamic
schema hash, live DTO sync via `StructInspectorProjector<T>`, and injection of the hub.

- [x] **GZH-011** Refactor `LayerControlGizmo` [details](./TASK-DETAILS.md#gzh-011--refactor-layercontrolgizmo)

---

## Phase 5: ClusterRunner Dynamic Window

**Goal:** Make the ClusterRunner's Raylib window optionally openable and closable at runtime
via console commands. Add per-perspective gizmo CPU control.

- [ ] **GZH-012** `OpenLocalWindow()` / `CloseLocalWindow()` [details](./TASK-DETAILS.md#gzh-012--openlocalwindow-and-closelocalwindow)
- [ ] **GZH-013** `ConsoleCommandService` [details](./TASK-DETAILS.md#gzh-013--consolecommandservice)
- [ ] **GZH-014** Perspective-aware `GizmoExecutionController` switching [details](./TASK-DETAILS.md#gzh-014--perspective-aware-gizmoexecutioncontroller-switching)

---

## Phase 6: Remote Terminal Lifecycle

**Goal:** Detect remote terminal crashes and network partitions automatically via DDS lifecycle
events, preventing zombie listener leaks.

- [x] **GZH-015** DDS lifecycle disconnect detection [details](./TASK-DETAILS.md#gzh-015--dds-lifecycle-disconnect-detection)

---

## Phase 7: Input Isolation

**Goal:** Prevent background subsystems from stealing input intended for the active perspective,
and prevent map clicks from interfering with ImGui panels.

- [ ] **GZH-016** Subsystem input collision fix [details](./TASK-DETAILS.md#gzh-016--subsystem-input-collision-fix)
