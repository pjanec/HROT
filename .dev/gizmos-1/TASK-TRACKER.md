# Task Tracker — FDP Declarative Gizmo & Presentation Framework

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: Core Primitive Protocol

**Goal:** Establish the 64-byte `DebugPrimitive` tagged union and `IDebugDrawBuilder` as the
sole output interface for all gizmos. No gizmo logic; pure data model. Includes the string
interning side-channel for AI diagnostic text exceeding 31 characters.

- [x] **TASK-GZ001** Color type and primitive enums (`Rgba32`, `PipelineTarget`, `CoordinateSpace`, `SizeMode`, `PickToken`) [details](./TASK-DETAIL.md#task-gz001--color-type-and-primitive-enums)
- [x] **TASK-GZ002** `DebugPrimitive` 64-byte tagged union (with `ThicknessU16`, `MinZoomLod`, `MaxZoomLod`, `StringHash` overlay) [details](./TASK-DETAIL.md#task-gz002--debugprimitive-tagged-union)
  - **KNOWN GAP:** `Token` computed property hardcodes `SubElementId = 0`, making multi-handle interactive gizmos impossible. Fixed by TASK-GZ030.
  - **KNOWN GAP:** `LifetimeSeconds` field exists but `DebugPrimitiveBuffer.Clear()` destroys all primitives every frame — persistent primitives never re-emit. Fixed by TASK-GZ029.
- [x] **TASK-GZ003** `IDebugDrawBuilder` interface and `DebugPrimitiveBuffer` accumulator (includes `DrawTextLong`) [details](./TASK-DETAIL.md#task-gz003--idebugdrawbuilder-and-debugprimitivebuffer)
- [x] **TASK-GZ019** `StringInternMap`, `DrawTextLong` implementation, and `StringInternBatch` DDS topic [details](./TASK-DETAIL.md#task-gz019--stringinternmap-and-drawtextlong)

---

## Phase 2: Gizmo Contracts and Data-Driven Orchestration

**Goal:** A single generic ECS system that manages all entity-bound and behavior-bound gizmo
lifecycles and drives their execution O(K) in active instances.

- [x] **TASK-GZ004** Gizmo contracts (`IStatefulGizmo`, `IGizmoDefinition`, `IGizmoVisibilityPolicy`, `GizmoRegistry`) [details](./TASK-DETAIL.md#task-gz004--gizmo-contracts-interfaces)
  - **KNOWN GAP:** `IStatelessGizmo` interface and its execution path are completely absent. All four concrete gizmos (`HealthBarGizmo`, `EntityRotationGizmo`, `VisibilityConeGizmo`, `HillAttackGizmo`) are logically stateless but forced into the stateful dictionary path with empty `OnInitialize`/`OnTeardown`. Fixed by TASK-GZ022.
- [x] **TASK-GZ005** `DataDrivenGizmoSystem` — entity lifecycle driven orchestrator [details](./TASK-DETAIL.md#task-gz005--datadrivengizmosystem-entity-bound)
  - **KNOWN FLAW:** Registered with `isSelectedPredicate: null` in `IgApplication.cs` — all gizmos render for all entities every frame, ignoring selection state. Fixed by TASK-GZ031.
- [x] **TASK-GZ006** `BehaviorGizmoManagerSystem` — behavior lifecycle driven orchestrator [details](./TASK-DETAIL.md#task-gz006--behaviorgizmomanagersystem-behavior-bound)
  - **KNOWN FLAW:** B-Tree/HSM behavior interrupts that preempt a running behavior without emitting `ClearBehaviorEvent` leave orphaned gizmo instances. Fixed by TASK-GZ035.

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
  - **KNOWN FLAW:** `DebugGizmoLayer.HandleInput` has documented DEVIATION — canvas is not injected so `GizmoInteractionProxyTool` is never pushed onto the tool stack. Fixed by TASK-GZ025.

---

## Phase 5: 2D Presentation Adapter

**Goal:** Local Raylib rendering of the primitive stream with layer/pipeline filtering, LOD zoom
culling, Painter's Algorithm sort, spatial projection (World/Screen/EntityLocal), and entity
badge rich text.

- [x] **TASK-GZ011** `DebugPrimitiveRenderer2D` — Raylib shape dispatch with layer filtering [details](./TASK-DETAIL.md#task-gz011--debugprimitiverenderer2d)
- [x] **TASK-GZ012** Spatial projection — `CoordinateSpace` resolution and `SizeMode` thickness scaling [details](./TASK-DETAIL.md#task-gz012--spatial-projection-coordinatespace--sizemode)
  - **KNOWN FLAW (EntityLocal):** `Arrow`, `Sphere`, `Box2D`, `Text`, `Icon` in EntityLocal space fall through to world-space rendering (comment "deferred"). Fixed by TASK-GZ027.
  - **KNOWN FLAW (SizeMode):** `SizeMode.ScreenPixels` is applied only to stroke thickness, not to `SphereRadius`, `ArrowHeadSize`, or `Box2D` extents. Fixed by TASK-GZ028.
- [x] **TASK-GZ013** `DebugGizmoLayer` integration — wire renderer and hit-testing [details](./TASK-DETAIL.md#task-gz013--debuggizmolayer-integration)
  - **KNOWN FLAW (activation chain):** Proxy tool push broken (see TASK-GZ010 note). Fixed by TASK-GZ025.
  - **KNOWN FLAW (hit-testing):** Only tests distance to `SphereCenter`/`LineStart`; ignores line body, Box2D area, Arrow, SizeMode, and Screen-space primitives. Fixed by TASK-GZ026.
  - **KNOWN FLAW (SimHost missing):** `DebugGizmoLayer` is NOT wired into `SimHostVisualization`. Fixed by TASK-GZ032.
- [x] **TASK-GZ014** Entity badge and rich text rendering [details](./TASK-DETAIL.md#task-gz014--entity-badge-and-rich-text-rendering)

---

## Phase 6: Remote Visualization Foundation

**Goal:** Prepare the primitive stream for network transport and establish the DDS topic contract
for headless/remote viewer scenarios.

- [x] **TASK-GZ015** `GlobalDebugSettings` ECS singleton (in `Hrot.IG`) [details](./TASK-DETAIL.md#task-gz015--globaldebug-settings-ecs-singleton)
- [x] **TASK-GZ016** `DebugPrimitivesBatch` DDS topic definition [details](./TASK-DETAIL.md#task-gz016--debugprimitivesbatch-dds-topic)
  - **KNOWN GAP:** DDS struct exists but no publisher system broadcasts the buffer over the network. Fixed by TASK-GZ033.
- [x] **TASK-GZ017** `GizmoSettingsPublisherSystem` and `GizmoUiState` DDS topic (StructEdit JSON side-channel for remote config UI) [details](./TASK-DETAIL.md#task-gz017--gizmosettingspublishersystem-and-gizmouistate-dds-topics)
  - **KNOWN FLAW:** System publishes a flat `{"key": value}` JSON string via `Utf8JsonWriter` instead of a StructEdit `EditDocument` schema. Remote UIs have no type metadata or validation rules. Fixed by TASK-GZ034.
- [x] **TASK-GZ018** `IGCapabilitiesAnnounce` DDS message and publisher system (terminal handshake) [details](./TASK-DETAIL.md#task-gz018--igcapabilitiesannounce-dds-message)


## Phase 7: Concrete gizmos

As an example usage of the gizmo framework, implement and integrate the following gizmos into clusterrunner, using a local raylib/ImGui based local renderer

- [x] **TASK-GZ020** Implement local raylib/ImGui based local gizmo renderer in cluster runner (BATCH-07)

**TASK-GZ021** Implement gizmos

- [x] map measure tool (BATCH-09 — MeasureToolGizmoAdapter + GizmoSettings Active/Units)
  - same functionality as the current map tool, just implemented as gizmo
  - global settings
    - measurement units
- [x] entity health bar - purely rendering gizmo, entity bound (BATCH-07)
  - same functionality as the one rendered in the IG subsystem
  - global settings
    - height of the health bar
    - width of the health bar
- [x] platoon hill attack behavior bound gizmo (BATCH-08)
  - shows
    - green base line
    - blue fire line
    - base line slots (little numbered circles on the base line)
    - fire line slots (little numbered circles on the fire line)
  - global settings
    - whether to show slots
- [ ] spatial grid global gizmo (DEFERRED — D-005, requires ISpatialGridView interface in FDP)
  - shows
    - the grid tiles
    - in the upper left corner of each tile little number of entities inside
  - global settings
    - show tiles (otherwise just outer bounds)
    - show number of entities per tile
- [x] entity rotation - entity bound interactive gizmo (BATCH-08)
  - same functionality as the current implementation (rotating line with heading angle indicator)
  - **KNOWN FLAW:** Implemented as stateful (`IStatefulGizmo`) with empty lifecycle stubs; should be stateless. Fixed by TASK-GZ023.
- [x] visibility cones - entity bound non interactive, entity local space gizmo (BATCH-08)
  - shows the visibility cone, as a sector
  - **KNOWN FLAW:** Implemented as stateful with empty lifecycle stubs; should be stateless. Fixed by TASK-GZ023.

---

## Phase 8: Stateless Gizmo Execution Path

**Goal:** Introduce the missing stateless gizmo taxonomy from the design — pure projectors that
query ECS state each frame without object instantiation, dictionary lookups, or lifecycle
management. Migrate existing pure-projector gizmos to the new path and establish compile-time
auto-registration via a Roslyn source generator.

- [x] **TASK-GZ022** `IStatelessGizmo` contract, `StatelessGizmoRegistry`, and `StatelessGizmoSystem` [details](./TASK-DETAIL.md#task-gz022--istatelessgizmo-contract-and-statelessgizmosystem)
- [x] **TASK-GZ023** Migrate pure-projector gizmos to stateless and correct project placement (HealthBar, EntityRotation, VisibilityCone → `Hrot.Common`; HillAttack → `Hrot.AI.Behaviors`) [details](./TASK-DETAIL.md#task-gz023--migrate-pure-projector-gizmos-to-stateless-and-correct-project-placement)
- [x] **TASK-GZ024** Unified `[GizmoProjector]` attribute and Roslyn source generator (replaces hand-written `GizmoRegistrar.cs`) [details](./TASK-DETAIL.md#task-gz024--unified-gizmoprojector-attribute-and-roslyn-source-generator)

---

## Phase 9: Presentation Fidelity Fixes

**Goal:** Repair the interactive activation chain, spatial hit-testing, and coordinate/size-mode
rendering gaps in `DebugPrimitiveRenderer2D` and `DebugGizmoLayer`.

- [x] **TASK-GZ025** Fix broken `DebugGizmoLayer` activation chain — inject `MapCanvas` and push `GizmoInteractionProxyTool` [details](./TASK-DETAIL.md#task-gz025--fix-broken-debuggizmolayer-activation-chain)
- [x] **TASK-GZ026** Fix spatial hit-testing — geometry-aware (line segment, Box2D, Sphere area), SizeMode-correct, CoordinateSpace.Screen-aware [details](./TASK-DETAIL.md#task-gz026--fix-spatial-hit-testing-in-debuggizmolayer)
- [x] **TASK-GZ027** Fix `EntityLocal` rendering for all primitive shapes (Arrow, Sphere, Box2D, Text, Icon) [details](./TASK-DETAIL.md#task-gz027--fix-entitylocal-rendering-for-all-primitive-shapes)
- [x] **TASK-GZ028** Fix `SizeMode.ScreenPixels` for shape radii and extents (`SphereRadius`, `ArrowHeadSize`, `Box2D` extents) [details](./TASK-DETAIL.md#task-gz028--fix-sizemodesscreenpixels-for-shape-radii-and-extents)

---

## Phase 10: Data Plane Correctness

**Goal:** Honour the `LifetimeSeconds` persistence contract and restore full `PickToken`
sub-element identity so multi-handle interactive gizmos can function.

- [x] **TASK-GZ029** Implement `LifetimeSeconds` persistent primitive re-emission in `DebugPrimitiveBuffer` [details](./TASK-DETAIL.md#task-gz029--implement-lifetimeseconds-persistent-primitive-re-emission)
- [x] **TASK-GZ030** Restore `PickToken.SubElementId` storage in interactive primitives [details](./TASK-DETAIL.md#task-gz030--restore-picktoken-subelementid-storage-in-interactive-primitives)

---

## Phase 11: System Integration and Wiring

**Goal:** Fix wiring gaps that leave the framework mathematically correct but visually and
distributively inert: selection predicate, SimHost layer, DDS egress, settings schema, and
behavior lifecycle safety.

- [x] **TASK-GZ031** Fix selection filtering — replace `isSelectedPredicate: null` with proper `SelectionState.IsSelected` predicate in `IgApplication` [details](./TASK-DETAIL.md#task-gz031--fix-selection-filtering-in-igapplication)
- [x] **TASK-GZ032** Wire `DebugGizmoLayer` into `SimHostVisualization` composition root [details](./TASK-DETAIL.md#task-gz032--wire-debuggizmolayer-into-simhostvisualization)
- [x] **TASK-GZ033** Wire `DebugPrimitivesBatch` DDS egress from SimHost (`DebugPrimitivesBatchPublisherSystem`) [details](./TASK-DETAIL.md#task-gz033--wire-debugprimitivesbatch-dds-egress-from-simhost)
- [x] **TASK-GZ034** Fix `GizmoSettingsPublisherSystem` to emit StructEdit schema instead of flat JSON [details](./TASK-DETAIL.md#task-gz034--fix-gizmosettingspublishersystem-to-emit-structedit-schema)
- [x] **TASK-GZ035** Fix behavior lifecycle leak — ensure `ClearBehaviorEvent` is emitted on B-Tree/HSM behavior interrupt [details](./TASK-DETAIL.md#task-gz035--fix-behavior-lifecycle-leak-on-ai-behavior-abort)
- [x] **TASK-GZ036** CPU performance budget — integrate `TimeSliceMetric.WallClockTime` into `DataDrivenGizmoSystem` and `StatelessGizmoSystem` [details](./TASK-DETAIL.md#task-gz036--cpu-performance-budget-for-gizmo-systems)


## Phase 12: Networked Interaction and Dumb Terminal

**Goal:** Close the remote-IG interaction loop: wire bidirectional DDS translation for gizmo
interaction events (IG drag commits reach SimHost), and convert IG from a full ECS evaluator
into a pure rendering terminal that only renders what the network provides.

- [x] **TASK-GZ037** Networked `GizmoInteractionEvent` DDS translators — `GizmoInteractionBatch` topic, `GizmoInteractionEgressSystem` (IG side), `GizmoInteractionIngressSystem` (SimHost side) [details](./TASK-DETAIL.md#task-gz037--networked-gizmointeractionevent-dds-translators)
- [x] **TASK-GZ038** IG dumb terminal — `DebugPrimitivesIngressTranslator` + remove `DataDrivenGizmoSystem`/`StatelessGizmoSystem` from `IgApplication` [details](./TASK-DETAIL.md#task-gz038--ig-dumb-terminal-ingress-debugprimitivesingresstranslator)

---

## Phase 13: Undo/Redo Semantics

**Goal:** Make committed gizmo interactions reversible. Without undo, a single misplaced drag
commit permanently corrupts the scenario with no recovery path short of a reload.

- [ ] **TASK-GZ039** Undo/redo stack — `IGizmoUndoRecord`, `GizmoUndoStack`, `DataDrivenGizmoSystem` integration, Ctrl+Z/Y shortcut handling [details](./TASK-DETAIL.md#task-gz039--undoredo-stack-for-gizmo-interactions)

---

## Phase 14: Infrastructure Safety Fixes

**Goal:** Fix D-001 (P1 blocking): `StringInternMap` uses a raw `Dictionary` that will corrupt
under parallel ECS iteration.

- [x] **TASK-GZ040** Fix `StringInternMap` concurrency hazard — replace `Dictionary<uint,string>` with `ConcurrentDictionary`, remove false thread-safe comment [details](./TASK-DETAIL.md#task-gz040--fix-stringinternmap-concurrency-hazard-d-001-p1-blocking)

---

## Phase 15: Assembly Segregation

**Goal:** Extract the primitive protocol and DDS schema types from the monolithic `Fdp.Toolkits`
assembly into two focused standalone projects (`Fdp.Diagnostics.Contracts` and
`Fdp.Diagnostics.Network`). External tools that need only the diagnostic protocol should not be
forced to depend on all of `Fdp.Toolkits`.

- [ ] **TASK-GZ041** Create `Fdp.Diagnostics.Contracts` assembly — migrate Phase 1 types (`Rgba32`, `DebugPrimitive`, `IDebugDrawBuilder`, `DebugPrimitiveBuffer`, `StringInternMap`); references only `Fdp.Core` [details](./TASK-DETAIL.md#task-gz041--create-fdpdiagnosticscontracts-assembly-and-migrate-phase-1-types)
- [ ] **TASK-GZ042** Create `Fdp.Diagnostics.Network` assembly — migrate Phase 6 DDS schemas (`DebugPrimitivesBatch`, `GizmoUiState`, `StringInternBatch`, `GizmoInteractionBatch`); references only `Fdp.Diagnostics.Contracts` + CycloneDDS [details](./TASK-DETAIL.md#task-gz042--create-fdpdiagnosticsnetwork-assembly-and-migrate-phase-6-dds-schemas)

