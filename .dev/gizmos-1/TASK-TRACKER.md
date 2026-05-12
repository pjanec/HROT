# Task Tracker -- FDP Declarative Gizmo & Presentation Framework

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 16: Execution Flaw Repairs

**Goal:** Fix confirmed structural execution flaws. All are P1 blockers for the interactive remote-viewer scenario.

- [x] **TASK-GZ043** Fix PipelineTarget enum -- add NodeGraph = 4, update All to 7
- [x] **TASK-GZ044** Fix IGCapabilitiesPublisherSystem -- add RegisteredGizmosJson; change SupportedShapes from byte to uint; derive via reflection
- [x] **TASK-GZ045** Wire composition roots -- register interaction egress/ingress systems and translators
- [x] **TASK-GZ046** Fix GizmoInteractionProxyTool click-away commit hazard
- [x] **TASK-GZ047** Fix screen-space coordinate mismatch -- add CoordinateSpace to drag/commit events

---

## Phase 17: Expanded Feature Set

- [ ] **DEFERRED** TASK-GZ048 Integrate DebugPrimitiveBuffer into FlightRecorder
- [x] **TASK-GZ049** Add SettingScope (Global / Project / Session) to GizmoSettingsRegistry

---

## Phase 18: Data Plane Correctness and Schema Discovery

**Goal:** Secure the remote primitive stream by replacing local ECS indices with stable IDs, introducing routing primitives, and broadcasting schemas.

- [x] **TASK-GZ050** Introduce semantic and routing primitives (SemanticShape, MilStd2525, SpatialAnchor)
  - SemanticShape: SpatialAnchor-dependent; payload = ulong ProfileId + float LengthMeters + float WidthMeters + uint ConditionMask (20 bytes, 20 bytes unused)
  - SpatialAnchor: long NetworkId + float WorldX/Y/Z + float Heading/Pitch/Roll in degrees (32 bytes, 8 bytes unused)
- [x] **TASK-GZ051** Fix ComponentInspector abstraction leak -- use long InspNetworkId and uint InspSchemaHash
- [x] **TASK-GZ052** Entity Attribute Schema Broadcast -- add EntityAttributeSchema TransientLocal DDS topic

---

## Phase 19: Library Segregation -- Extract GizmoMap to ExtDeps

**Goal:** Extract the framework into a self-contained ExtDeps/GizmoMap dependency with strict internal assembly boundaries. GizmoMap assemblies must NEVER contain Entity, ISimulationView, BitMask256, DataDrivenGizmoSystem, or any FDP ECS type.

- [x] **TASK-GZ053** Create GizmoMap.Contracts -- zero-dependency assembly (BCL only; GizmoPickToken, IGizmoSource, DebugPrimitive, enums, IDebugDrawBuilder)
- [x] **TASK-GZ054** Create GizmoMap.Network -- references only GizmoMap.Contracts + CycloneDDS (DDS topic structs + stateless transport adapters; no IEcsModuleSystem)
- [x] **TASK-GZ055** Create GizmoMap.Presentation -- references only GizmoMap.* + Raylib + ImGui (renderer, proxy tool, SemanticShape/MilStd2525 renderers; no ECS producer systems)
- [x] **TASK-GZ056** Unified example application -- --mode local and --mode dds (proves transport switching, showcases all primitives, DDS round-trip)



#### Phase 20: Production Map Rendering Migration
**Goal:** Completely replace the legacy hardcoded map layers and entity visualizers in SimHost, CGF, and IG with pure `GizmoMap`-based declarative rendering.
* [x] **TASK-GZ057** Convert Base Entity Visualizers to Stateless Gizmos
* [x] **TASK-GZ058** Migrate Domain Map Layers to Gizmo Projectors
* [x] **TASK-GZ059** Eradicate Legacy Rendering Infrastructure & Wire Composition Roots

#### Phase 21: Tool Rendering Decoupling
**Goal:** Eradicate direct `Raylib_cs` dependencies from all interactive map tools, converting them into "gizmo generators" that emit backend-neutral `DebugPrimitive`s while retaining their stateful ECS logic.
* [x] **TASK-GZ060** Decouple Vis2D Abstractions from Raylib
* [x] **TASK-GZ061** Convert Measurement and Placement Tools to Gizmo Generators
* [x] **TASK-GZ062** Convert EntityRotationTool to Gizmo Generator
* [x] **TASK-GZ063** Convert Polyline & Route Edit Tools to Gizmo Generators

---

## Phase 22: Composite Gizmo Identity

**Goal:** Introduce `GizmoTypeId` as the third routing-key component so that multiple independent
gizmos on the same entity can coexist without interaction routing collisions or StructInspector
ImGui window merging.

- [ ] **TASK-GZ064** Add GizmoTypeId to network contracts [details](./TASK-DETAIL.md#task-gz064--add-gizmotypeid-to-network-contracts)
- [ ] **TASK-GZ065** GizmoTypeId injection into emitted primitives [details](./TASK-DETAIL.md#task-gz065--gizmotypeid-injection-into-emitted-primitives)
- [ ] **TASK-GZ066** Fix DataDrivenGizmoSystem routing and wire egress/ingress translators [details](./TASK-DETAIL.md#task-gz066--fix-datadrivengizmosystem-routing-and-wire-egressingress-translators)
- [ ] **TASK-GZ067** Populate GizmoTypeId in terminal pick-token [details](./TASK-DETAIL.md#task-gz067--populate-gizmotypeid-in-terminal-pick-token)

---

## Phase 23: StructInspector Refinements

**Goal:** Fix StructInspector ImGui window collision, eliminate the redundant root tree node,
add a per-inspector Viewing/Editing state machine, and wire the GizmoUiState DDS subscription
so live host state reaches the terminal inspector panel.

- [ ] **TASK-GZ068** Fix ImGui window stable ID and eliminate redundant root node [details](./TASK-DETAIL.md#task-gz068--fix-imgui-window-stable-id-and-eliminate-redundant-root-node)
- [ ] **TASK-GZ069** Add per-inspector Viewing/Editing state machine [details](./TASK-DETAIL.md#task-gz069--add-per-inspector-viewingediting-state-machine)
- [ ] **TASK-GZ070** Wire GizmoUiState subscription on terminal side [details](./TASK-DETAIL.md#task-gz070--wire-gizmouistate-subscription-on-terminal-side)
