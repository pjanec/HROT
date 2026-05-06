# Task Tracker — FDP Declarative Gizmo & Presentation Framework

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: Core Primitive Protocol

**Goal:** Establish the 64-byte `DebugPrimitive` tagged union and `IDebugDrawBuilder` as the
sole output interface for all gizmos. No gizmo logic; pure data model. Includes the string
interning side-channel for AI diagnostic text exceeding 31 characters.

- [x] **TASK-GZ001** Color type and primitive enums (`Rgba32`, `PipelineTarget`, `CoordinateSpace`, `SizeMode`, `PickToken`) [details](./TASK-DETAIL.md#task-gz001--color-type-and-primitive-enums)
- [x] **TASK-GZ002** `DebugPrimitive` 64-byte tagged union (with `ThicknessU16`, `MinZoomLod`, `MaxZoomLod`, `StringHash` overlay) [details](./TASK-DETAIL.md#task-gz002--debugprimitive-tagged-union)
- [x] **TASK-GZ003** `IDebugDrawBuilder` interface and `DebugPrimitiveBuffer` accumulator (includes `DrawTextLong`) [details](./TASK-DETAIL.md#task-gz003--idebugdrawbuilder-and-debugprimitivebuffer)
- [x] **TASK-GZ019** `StringInternMap`, `DrawTextLong` implementation, and `StringInternBatch` DDS topic [details](./TASK-DETAIL.md#task-gz019--stringinternmap-and-drawtextlong)

---

## Phase 2: Gizmo Contracts and Data-Driven Orchestration

**Goal:** A single generic ECS system that manages all entity-bound and behavior-bound gizmo
lifecycles and drives their execution O(K) in active instances.

- [x] **TASK-GZ004** Gizmo contracts (`IStatefulGizmo`, `IGizmoDefinition`, `IGizmoVisibilityPolicy`, `GizmoRegistry`) [details](./TASK-DETAIL.md#task-gz004--gizmo-contracts-interfaces)
- [x] **TASK-GZ005** `DataDrivenGizmoSystem` — entity lifecycle driven orchestrator [details](./TASK-DETAIL.md#task-gz005--datadrivengizmosystem-entity-bound)
- [x] **TASK-GZ006** `BehaviorGizmoManagerSystem` — behavior lifecycle driven orchestrator [details](./TASK-DETAIL.md#task-gz006--behaviorgizmomanagersystem-behavior-bound)

---

## Phase 3: Settings Store

**Goal:** Zero-allocation, hash-keyed global settings registry with disk persistence and
event-based cache invalidation for stateful gizmos.

- [x] **TASK-GZ007** `GizmoSettingValue` tagged union and `GizmoSettingsRegistry` managed singleton [details](./TASK-DETAIL.md#task-gz007--gizmosettingvalue-and-gizmosettingsregistry)
- [x] **TASK-GZ008** Settings persistence (disk save/load) and `GizmoSettingChangedEvent` [details](./TASK-DETAIL.md#task-gz008--settings-persistence-and-change-events)

---

## Phase 4: Interactive Input Routing

**Goal:** Safe, exclusive input capture with zero ECS mutation from gizmo draw code. All mutations
go through `IEntityCommandBuffer` after interaction commits.

- [x] **TASK-GZ009** Backend-neutral interaction events (`GizmoInteractionStartedEvent`, `GizmoDragUpdateEvent`, `GizmoInteractionCommitEvent`, `GizmoInteractionCancelEvent`) [details](./TASK-DETAIL.md#task-gz009--backend-neutral-interaction-events)
- [x] **TASK-GZ010** `GizmoInteractionProxyTool` — `IMapTool` adapter for 2D map input capture [details](./TASK-DETAIL.md#task-gz010--gizmointeractionproxytool)

---

## Phase 5: 2D Presentation Adapter

**Goal:** Local Raylib rendering of the primitive stream with layer/pipeline filtering, LOD zoom
culling, Painter's Algorithm sort, spatial projection (World/Screen/EntityLocal), and entity
badge rich text.

- [x] **TASK-GZ011** `DebugPrimitiveRenderer2D` — Raylib shape dispatch with layer filtering [details](./TASK-DETAIL.md#task-gz011--debugprimitiverenderer2d)
- [x] **TASK-GZ012** Spatial projection — `CoordinateSpace` resolution and `SizeMode` thickness scaling [details](./TASK-DETAIL.md#task-gz012--spatial-projection-coordinatespace--sizemode)
- [x] **TASK-GZ013** `DebugGizmoLayer` integration — wire renderer and hit-testing [details](./TASK-DETAIL.md#task-gz013--debuggizmolayer-integration)
- [x] **TASK-GZ014** Entity badge and rich text rendering [details](./TASK-DETAIL.md#task-gz014--entity-badge-and-rich-text-rendering)

---

## Phase 6: Remote Visualization Foundation

**Goal:** Prepare the primitive stream for network transport and establish the DDS topic contract
for headless/remote viewer scenarios.

- [x] **TASK-GZ015** `GlobalDebugSettings` ECS singleton (in `Hrot.IG`) [details](./TASK-DETAIL.md#task-gz015--globaldebug-settings-ecs-singleton)
- [x] **TASK-GZ016** `DebugPrimitivesBatch` DDS topic definition [details](./TASK-DETAIL.md#task-gz016--debugprimitivesbatch-dds-topic)
- [x] **TASK-GZ017** `GizmoSettingsPublisherSystem` and `GizmoUiState` DDS topic (StructEdit JSON side-channel for remote config UI) [details](./TASK-DETAIL.md#task-gz017--gizmosettingspublishersystem-and-gizmouistate-dds-topics)
- [x] **TASK-GZ018** `IGCapabilitiesAnnounce` DDS message and publisher system (terminal handshake) [details](./TASK-DETAIL.md#task-gz018--igcapabilitiesannounce-dds-message)


## Phase 7: Concrete gizmos

As an example usage of the gizmo framework, implement and integrate the following gizmos into clusterrunner, using a local raylib/ImGui based local renderer

- [x] **TASK-GZ020** Implement local raylib/ImGui based local gizmo renderer in cluster runner (BATCH-07)

**TASK-GZ021** Implement gizmos

- [ ] map measure tool
  - same functionality as the current map tool, just implemented as gizmo
  - global settings
    - measurement units
- [x] entity health bar - purely rendering gizmo, entity bound (BATCH-07)
  - same functionality as the one rendered in the IG subsystem
  - global settings
    - height of the health bar
    - width of the health bar
- [ ] platoon hill attack behavior bound gizmo
  - shows
    - green base line
    - blue fire line
    - base line slots (little numbered circles on the base line)
    - fire line slots (little numbered circles on the fire line)
  - global settings
    - whether to show slots
- [ ] spatial grid global gizmo
  - shows
    - the grid tiles
    - in the upper left corner of each tile little number of entities inside
  - global settings
    - show tiles (otherwise just outer bounds)
    - show number of entities per tile
- [ ] entity rotation - entity bound interactive gizmo
  - same functionality as the current implementation (rotating line with heading angle indicator)
- [ ] visibility cones - entity bound non interactive, entity local space gizmo
  - shows the visibility cone, as a sector

