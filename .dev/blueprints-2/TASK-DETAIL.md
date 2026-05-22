# AI Editor — Task Detail

> **Purpose:** Per-task descriptions for the AI editor implementation. Each task references the relevant specification rather than duplicating it. Hand-off-ready: an implementer should be able to open the referenced spec section and start working.
> **Companion:** `TASK-TRACKER.md` — the one-line-per-task tracker with status checkboxes.
> **Specs referenced:**
> - `AI_Editor_Shared_Infrastructure.md` (shared)
> - `NodeEditor_Extension_NodeAttachments.md` (NEA)
> - `NodeEditor_Extension_ContainerNodes.md` (NEC)
> - `NodeEditor_Extension_CustomCanvasRenderer.md` (NER)
> - `BTree_Editor_NodeEditor_Host_Design.md` (BTH)
> - `HSM_Editor_NodeEditor_Host_Design.md` (HSH)

---

## Table of Contents

- Phase 0 — Kernel-side prerequisites
- Phase 1 — Shared infrastructure foundation
- Phase 2 — NodeEditor: NodeAttachments extension
- Phase 3 — NodeEditor: ContainerNodes extension
- Phase 4 — NodeEditor: CustomCanvasRenderer extension
- Phase 5 — BTree host: Slice 1 (authoring)
- Phase 6 — HSM host: Slice 1 (authoring)
- Phase 7 — Shared infrastructure: refactor + find-references
- Phase 8 — BTree host: Slice 2 (runtime read-only)
- Phase 9 — HSM host: Slice 2 (runtime read-only)
- Phase 10 — Both hosts: Slice 3 (stepping + breakpoints)
- Phase 11 — Multi-instance, polish (Slices 4–5)

Phase ordering rationale: kernel work in phase 0 must complete before any host can faithfully round-trip; phase 1 (shared infra) unblocks both hosts; phases 2–4 are the NodeEditor extensions that hosts depend on; phases 5–6 are host Slice 1 (BTree first because simpler, smaller host; HSM second since it depends on ContainerNodes + CustomCanvasRenderer); phase 7 lands the shared refactor surface once both hosts exist (need at least two hosts to validate "cross-asset" actually works); phases 8–10 add runtime debug capability; phase 11 is polish.

---

## Phase 0 — Kernel-side prerequisites

These additions to FastBTree, FastHSM, and the source generators must land before the editor can faithfully round-trip. They are all additive (default values preserve existing behavior) and small in scope.

### TASK-K-01 — Add `Lane` property to `[HsmAction]`

The `[HsmAction]` attribute needs a `Lane = CommandLane.X` property to enable OutputLaneMask inference in the editor.

- **Where:** `Fhsm.Compiler` (attribute definition); `Fhsm.SourceGen` (reading the property).
- **Default:** sentinel value `CommandLane.None` (or equivalent) meaning "no lane / inferred." Existing usages without the property unchanged.
- **Consumed by:** HSH §10.3, §10.4.
- **Verifies:** existing HSM tests still pass; new test confirms `Lane` defaults work and explicit values are read at compile time.

### TASK-K-02 — Add `stableId` parameter to `HsmBuilder.State()` and `StateBuilder.AddChild()`

The editor needs stable Guid identity per state, surfaced through the fluent builder so it can be emitted into source.

- **Where:** `Fhsm.Compiler` builder API.
- **Default:** `Guid.NewGuid()` so existing handwritten code stays valid.
- **Consumed by:** HSH §1.4, §4.1 emit example.
- **Verifies:** existing tests pass; round-trip test confirms emitted stableId is preserved across compile.

### TASK-K-03 — Add `visualId` parameter to `TransitionBuilder.GoTo()` and `HsmBuilder.GlobalTransition()`

Same as TASK-K-02 but for transition identity.

- **Where:** `Fhsm.Compiler` builder API.
- **Default:** `Guid.NewGuid()`.
- **Consumed by:** HSH §1.4, §4.1.
- **Verifies:** same approach as TASK-K-02.

### TASK-K-04 — Add `Paused` flag to HSM `InstanceFlags`

Step-control semantics (§13.2) require pausing an instance. Today `InstanceFlags` has `DebugTrace` but no pause bit.

- **Where:** `Fhsm.Kernel.Data.InstanceFlags` enum + the RTC loop that respects it.
- **Default:** flag clear; instances tick normally.
- **Consumed by:** HSH §13.2; shared infra §12.1 step semantics.
- **Verifies:** kernel test that an instance with `Paused` set doesn't advance microsteps.

### TASK-K-05 — Add `Paused` flag to BTree `InstanceFlags` (or equivalent)

The BTree kernel doesn't have `InstanceFlags` at all today; debug-state-driven pause needs to be wired in.

- **Where:** `Fbt.Kernel` — likely a new field on `DebugState` or a new `BehaviorInstanceFlags` struct.
- **Default:** clear.
- **Consumed by:** BTH §12.2; BTH §17 open question #1.
- **Verifies:** kernel test that a behavior with Paused set doesn't advance.

### TASK-K-06 — `visualId` parameter on BTree fluent builder

Already exists in the current source (per FastBTree examples) but verify it's consistently present on every fluent method emit consumes: `.Sequence(...)`, `.Selector(...)`, `.ObserverSelector(...)`, `.Parallel(...)`, `.Action(...)`, `.Condition(...)`, `.Wait(...)`, `.Subtree(...)`, `.Inverter(...)`, `.Repeater(...)`, `.Cooldown(...)`, `.ForceSuccess(...)`, `.ForceFailure(...)`, `.UntilSuccess(...)`, `.UntilFailure(...)`.

- **Where:** `Fbt.Compiler` fluent builders.
- **Default:** `Guid.NewGuid()` where missing.
- **Consumed by:** BTH §4.1.
- **Verifies:** round-trip test as TASK-K-02.

---

## Phase 1 — Shared infrastructure foundation

Build the `Hrot.Editor.AiShared` assembly. This unblocks every subsequent phase. No NodeEditor extensions or host code yet.

### TASK-S1-01 — `IEditableAsset` and AssetKind enum

The marker interface for any editable AI asset (BTree, HSM, Blueprint) plus the enum.

- **Spec:** shared infra §4.2.
- **Public surface:** `IEditableAsset` interface, `AssetKind` enum (Blueprint | BTree | Hsm).
- **No dependencies.**

### TASK-S1-02 — AssetId hashing primitive

FNV-1a-32 hash used for converting editor Guids to kernel-side ints.

- **Spec:** shared infra §3.3.
- **Public surface:** `AssetIdHash.Fnv1a32(ReadOnlySpan<byte>) → int`.
- **Verifies:** unit tests against a vector of known hash outputs.

### TASK-S1-03 — `EditorSelectionStore` (per-asset model)

The selection bus, per-asset selection model (per design decision in shared infra §5).

- **Spec:** shared infra §5.
- **Public surface:** `EditorSelectionStore` class, `IAssetSubSelection` marker interface, the three subsystem-specific subselection records.
- **Verifies:** unit tests for per-asset isolation, `ActiveAsset` switch behavior, `Forget` eviction, single `OnSelectionChanged` event per mutation.

### TASK-S1-04 — `IAssetCatalog` and contributor pattern

Catalog merging assets from all subsystems.

- **Spec:** shared infra §3.6.
- **Public surface:** `IAssetCatalog`, `IAssetCatalogContributor`, default `AssetCatalog` implementation.
- **Dependencies:** TASK-S1-01.
- **Verifies:** unit tests for contributor merge, `Changed` event propagation on contributor change.

### TASK-S1-05 — FQN reference catalog

The reference catalog used by find-references and refactor.

- **Spec:** shared infra §4.3.
- **Public surface:** `IReferenceCatalog`, `IAssetSubElement`, `AssetReference` record, `SubElementKind` enum.
- **Dependencies:** TASK-S1-04 (catalog rebuilds on `IAssetCatalog.Changed`).
- **Verifies:** unit tests for find-references queries, rebuild on contributor change, collision diagnostics.

### TASK-S1-06 — `FluentCSharpEmitter` framework

The deterministic-emit framework. The per-host emitter implementations come later (in their respective host tasks).

- **Spec:** shared infra §6.
- **Public surface:** `IFluentCSharpEmitter<TAsset>`, `FluentCSharpEmitter` base class with `using` ordering, marker placement, atomic write, round-trip self-test helpers.
- **Verifies:** unit tests for `using` sort order, deterministic output across runs, marker correctness.

### TASK-S1-07 — `[…Layout]` method discovery

Reflection helper for finding the `[BTreeLayout]` / `[HsmLayout]` / `[BlueprintLayout]` sibling methods.

- **Spec:** shared infra §7.3.
- **Public surface:** `LayoutDiscovery.TryGetLayout<TAttr, TLayout>(Assembly, Guid)`.
- **Verifies:** unit tests for found/missing/mismatched cases.

### TASK-S1-08 — `IGSelectionBridge` (DDS adapter)

Adapter consuming the DDS-published `SelectionChangedEvent`.

- **Spec:** shared infra §5.3.
- **Public surface:** `IGSelectionBridge` interface + default implementation.
- **Dependencies:** TASK-S1-03 (writes to the selection store).
- **Verifies:** integration test that a DDS publish updates `SelectedEntity`.

### TASK-S1-09 — `AssetBrowserWindow`

The shared asset browser window.

- **Spec:** shared infra §9.
- **Public surface:** the window class, registered under `ai_asset_browser`.
- **Dependencies:** TASK-S1-04 (catalog source), TASK-S1-03 (writes ActiveAsset).
- **Verifies:** manual/visual test of asset list, type filter chips, double-click → opens canvas.

### TASK-S1-10 — `InspectorWindow` with StructEdit dispatch

The shared inspector window routing on `ActiveSubSelection`.

- **Spec:** shared infra §10.
- **Public surface:** the window class, registered under `ai_inspector`. Dispatches via subsystem-provided `IInspectorFacetProvider`s.
- **Dependencies:** TASK-S1-03.
- **Verifies:** manual test that selecting various subselection kinds shows the right facet drawer.

### TASK-S1-11 — `AiTracerCoordinator` and tracer-observer base

Reference-counted asset-observation infrastructure.

- **Spec:** shared infra §11.4, §13.
- **Public surface:** `IAiTraceObserver`, `AiTracerCoordinator` with refcount logic.
- **Verifies:** unit test that overlapping observation requests refcount correctly; ending one observer leaves the asset still observed.

### TASK-S1-12 — `IAiDebugSession` and `IDebugSessionRegistry`

The exclusive control-session surface plus the registry that mediates acquisition.

- **Spec:** shared infra §11.2, §12.
- **Public surface:** `IAiDebugSession`, `AiDebugSessionBase` abstract class, `IDebugSessionRegistry` + default implementation.
- **Dependencies:** TASK-S1-11.
- **Verifies:** unit tests for `TryAcquireSession` exclusivity, observer registration is unlimited.

### TASK-S1-13 — Hot-reload classification

The Cosmetic / Soft / Hard tier classification (host-side; subsystems plug in their hash sources).

- **Spec:** shared infra §17.
- **Public surface:** `HotReloadClassifier`, `HotReloadStatusIndicator`.
- **Verifies:** unit test with synthetic before/after hash pairs covering each tier.

### TASK-S1-14 — Window registration and DI wiring

The `SharedAiWindowRegistrar` and DI setup.

- **Spec:** shared infra §19.
- **Public surface:** the registrar class plus the `AddSharedAiEditor()` DI extension method.
- **Dependencies:** all S1 tasks above.
- **Verifies:** integration test that all four shared windows register and resolve.

### TASK-S1-15 — RuntimeInspectorWindow and TraceTimelineWindow shells

The shared runtime windows, registered but with no per-subsystem panes yet. Hosts plug their panes in later.

- **Spec:** shared infra §14, §15.
- **Public surface:** the window classes, `IRuntimeInspectorPane`, `ITraceLaneProvider`.
- **Dependencies:** TASK-S1-14.
- **Verifies:** manual test that empty-state renders correctly.

---

## Phase 2 — NodeEditor: NodeAttachments extension

The first NodeEditor extension. Unblocks BTree decorator pills and HSM state-flag badges.

### TASK-NEA-01 — `AttachmentId` and `IAttachmentModel`

The identity type and model interface.

- **Spec:** NEA §4.1, §4.2.
- **Verifies:** compiles and is consumed by stub host.

### TASK-NEA-02 — `IGraphModel` extension for attachments

Add `Attachments`, `FindAttachment`, `GetAttachmentsForNode` to the existing interface. Backwards-compatible (default empty for non-implementing hosts).

- **Spec:** NEA §4.3.

### TASK-NEA-03 — `GraphChangeKind` and `GraphChangeNotification` extensions

Add the three new change kinds + the `AffectedAttachments` set.

- **Spec:** NEA §4.4.
- **Verifies:** existing host code compiles unchanged (null is passed for the new field).

### TASK-NEA-04 — Attachment layout engine

The wrap-and-stack layout algorithm.

- **Spec:** NEA §5.1, §5.2.
- **Verifies:** `AttachmentLayoutTests` (NEA §14.1).

### TASK-NEA-05 — Attachment rendering

The pill rendering (rounded-pill, glyph, label, state-based outlines).

- **Spec:** NEA §5, §10 (theme additions).
- **Dependencies:** TASK-NEA-04.

### TASK-NEA-06 — Hit-testing for attachments

Spatial-index updates and hit-test priority.

- **Spec:** NEA §6.1, §6.2.
- **Verifies:** `AttachmentHitTestTests`.

### TASK-NEA-07 — Selection of attachments

Extension to `SelectionState` and the selection-mutation flow.

- **Spec:** NEA §7.
- **Dependencies:** TASK-NEA-06.

### TASK-NEA-08 — Attachment commands

The five new `GraphCommand` records (`AddAttachment`, `RemoveAttachments`, `SetAttachmentProperty`, `ReorderAttachments`, `MoveAttachment`) plus their inverses.

- **Spec:** NEA §8.
- **Verifies:** `AttachmentCommandsTests`.

### TASK-NEA-09 — Context menu provider

The `IAttachmentContextMenuProvider` hook.

- **Spec:** NEA §6.4.

### TASK-NEA-10 — Low-zoom rendering for attachments

Collapse to category-colored bar below zoom 0.5.

- **Spec:** NEA §5.4.

### TASK-NEA-11 — Theme additions and demo scenario

Theme entries + a demo scene exercising all features.

- **Spec:** NEA §10, §14.2.

---

## Phase 3 — NodeEditor: ContainerNodes extension

Unblocks HSM composite states and parallel regions.

### TASK-NEC-01 — `IContainerNodeModel` and supporting records

Interface, `RegionDescriptor`, `ContainerPadding`, plus the `INodeModel.ParentContainerId` extension.

- **Spec:** NEC §4.
- **Critical:** this is the model-invariant change. Existing host code that reads `Position` must be reviewed; canvas-absolute vs. parent-local semantics shift only for containers.
- **Verifies:** existing hosts still compile and behave unchanged when no container is in use.

### TASK-NEC-02 — `GraphView` transform helpers

`NodeCanvasPosition`, `NodeLocalPosition`, `NodeCanvasBounds`, `NodeInteriorBounds`, `GetParentContainer`.

- **Spec:** NEC §4.3, §4.4.
- **Verifies:** `ContainerTransformTests` (NEC §19.1).

### TASK-NEC-03 — Container bounds computation and auto-resize

Recursive bounds with caching + invalidation rules.

- **Spec:** NEC §5, §12.
- **Verifies:** `ContainerBoundsTests`.

### TASK-NEC-04 — Container rendering passes

Fill / header / outline (pass 3); children rendered recursively after wires (pass 5).

- **Spec:** NEC §6.

### TASK-NEC-05 — Container hit-testing

Header hot zone, interior empty-area selection, collapse-arrow.

- **Spec:** NEC §7.
- **Verifies:** `ContainerHitTestTests`.

### TASK-NEC-06 — Drag-and-drop into containers

Drop-target highlighting, reparenting semantics, cycle prevention.

- **Spec:** NEC §10.
- **Verifies:** `ContainerDragTests`, `ContainerCycleDetectionTests`.

### TASK-NEC-07 — Container commands

`ChangeParent`, `ChangeParentMultiple`, `SetContainerCollapsed`, `AddRegion`, `RemoveRegion`, `ReorderRegions`, `SetRegionProperty` + inverses.

- **Spec:** NEC §11.
- **Verifies:** `ContainerCommandsTests`.

### TASK-NEC-08 — Region rendering and interactions

Dashed dividers, region headers, region-scoped drag.

- **Spec:** NEC §13.
- **Verifies:** `RegionLayoutTests`.

### TASK-NEC-09 — Z-order and overlap rules

Containers behind wires, in front of comments. Children rendered after wires.

- **Spec:** NEC §14.

### TASK-NEC-10 — Serialization order determinism

`ChildNodeIds` ordering rules for emit determinism.

- **Spec:** NEC §15.
- **Verifies:** `ChildOrderDeterminismTests`.

### TASK-NEC-11 — Low-zoom container rendering

Solid rectangle + brighter header strip at zoom < 0.5.

- **Spec:** NEC §6.5.

### TASK-NEC-12 — Theme additions and demo scenario

Theme entries + demo scene exercising flat, nested, parallel-region, collapsed cases.

- **Spec:** NEC §16, §19.2.

---

## Phase 4 — NodeEditor: CustomCanvasRenderer extension

Unblocks HSM transition labels (critical for HSM Slice 1), BTree observer-guard badges and runtime overlays.

### TASK-NER-01 — `ICustomCanvasRenderer` interface and registration

The core interface + `CanvasRenderPass` enum + `IEditorHostServices.CustomCanvasRenderers`.

- **Spec:** NER §4, §5.

### TASK-NER-02 — `ICanvasRenderContext` and per-pass invocation

The context object + the canvas's render-loop integration at the four named passes.

- **Spec:** NER §6.

### TASK-NER-03 — Coordinate-space helpers

`CanvasToScreen` / `ScreenToCanvas` ergonomics on the context.

- **Spec:** NER §7.

### TASK-NER-04 — `ICustomCanvasHitTester` and hit-test integration

Companion interface + hit-test priority extension.

- **Spec:** NER §5.1, §8.
- **Verifies:** `CustomRendererHitTestTests`.

### TASK-NER-05 — `ICustomCanvasSelectable` and selection extension

Companion interface + `SelectionState.SelectedCustomElements` field.

- **Spec:** NER §5.2, §9.
- **Verifies:** `CustomRendererSelectionTests`.

### TASK-NER-06 — Details panel target extension

`DetailsTarget.CustomElement` + routing.

- **Spec:** NER §9.3.

### TASK-NER-07 — Custom element context menu provider

`ICustomElementContextMenuProvider` registration.

- **Spec:** NER §9.4.

### TASK-NER-08 — Per-renderer perf accounting

Optional debug instrumentation.

- **Spec:** NER §12.3.

### TASK-NER-09 — Theme additions and demo scenario

Theme entries + demo scene exercising all four passes.

- **Spec:** NER §13, §15.2.

---

## Phase 5 — BTree host: Slice 1 (authoring)

The first host implementation. End-to-end authoring of BTree assets via the editor.

### TASK-BT-S1-01 — `BehaviorTreeAsset` model

Editor-side model (the projection target).

- **Spec:** BTH §3.1.
- **Dependencies:** TASK-S1-01.

### TASK-BT-S1-02 — Projection from compiled assembly

Walking `BehaviorTreeBlob` + `NodeDebugMetadata[]` + layout method into the editor model.

- **Spec:** BTH §3.2.
- **Dependencies:** TASK-S1-07, TASK-K-06.
- **Verifies:** `BehaviorTreeAssetProjectionTests`.

### TASK-BT-S1-03 — Identity bridges and lookup tables

`_visualIdToBlobIndex`, `_visualIdToNode`, `_visualIdToPill` dictionaries.

- **Spec:** BTH §3.3.

### TASK-BT-S1-04 — Tidy-tree auto-layout

Reingold-Tilford for newly-authored nodes.

- **Spec:** BTH §3.4.

### TASK-BT-S1-05 — `BTreeFluentEmitter`

Deterministic emit producing builder + `[BTreeDefinition]` + `[BTreeLayout]` methods.

- **Spec:** BTH §4.
- **Dependencies:** TASK-S1-06.
- **Verifies:** `BTreeFluentEmitterDeterminismTests` and round-trip property test.

### TASK-BT-S1-06 — `BTreeNodeCatalog`

Static composite/decorator/leaf entries + dynamic actions/conditions from `BehaviorRegistry`.

- **Spec:** BTH §5.1.

### TASK-BT-S1-07 — `BTreeTypeSystem` and `BTreeLinkValidator`

Minimal type system (single exec-edge) + structural link rules. Note the reversed-pin convention.

- **Spec:** BTH §5.2, §5.3.
- **Verifies:** `BTreeLinkValidatorTests`.

### TASK-BT-S1-08 — `BTreeCommandSink`

`GraphCommand` translation including attachment commands.

- **Spec:** BTH §5.4.
- **Dependencies:** TASK-NEA-08.
- **Verifies:** `BTreeCommandSinkTests`.

### TASK-BT-S1-09 — Decorator pill collapse / round-trip

The signature feature: kernel decorator wrappers projected to attachments, emitted back to nested fluent calls.

- **Spec:** BTH §6 entire section.
- **Dependencies:** TASK-NEA-08, TASK-BT-S1-05.
- **Verifies:** `DecoratorPillCollapseTests`.

### TASK-BT-S1-10 — Observer Selector palette + visual

Distinct palette entry, eye glyph in header, slightly darker header tint.

- **Spec:** BTH §7.1, §7.2.

### TASK-BT-S1-11 — `btree.observer_guard_badges` custom renderer

`👁 OBSERVES` badges on observer-child connections leading to guard children.

- **Spec:** BTH §7.3.
- **Dependencies:** TASK-NER-01 through TASK-NER-05.

### TASK-BT-S1-12 — Subtree node visual + navigation

Black-box rendering, double-click navigation, resolution.

- **Spec:** BTH §8.

### TASK-BT-S1-13 — Blackboard reflection + panel

Reflect user-defined blackboard struct, render schema (read-only mode for Slice 1).

- **Spec:** BTH §9.

### TASK-BT-S1-14 — BTree facet structs

`BTreeActionFacet`, `BTreeConditionFacet`, `BTreeWaitFacet`, decorator facets, composite facets.

- **Spec:** BTH §10.1.

### TASK-BT-S1-15 — `BlackboardFieldPicker` StructEdit attribute

The new picker for expression-target fields.

- **Spec:** BTH §10.3.

### TASK-BT-S1-16 — BTree validation rules

Diagnostic codes + the 12 validation rules from BTH §11.1.

- **Spec:** BTH §11.
- **Verifies:** `BTreeValidationTests`.

### TASK-BT-S1-17 — `BTreeAssetContributor`

`IAssetCatalogContributor` implementation reflecting `[BTreeDefinition]`s.

- **Spec:** BTH §1.1.
- **Dependencies:** TASK-S1-04.

### TASK-BT-S1-18 — Host services wiring + DI

The `BTreeEditorHostServices` factory + DI registrations.

- **Spec:** BTH §5.
- **Dependencies:** all BT-S1 tasks above.

### TASK-BT-S1-19 — Quick reload classification (BTree)

Subsystem-specific structure/param hash producers consumed by the shared classifier.

- **Spec:** BTH §14, shared infra §17.
- **Dependencies:** TASK-S1-13.

---

## Phase 6 — HSM host: Slice 1 (authoring)

End-to-end authoring of HSM assets.

### TASK-HS-S1-01 — `HsmAsset` model

Editor-side model.

- **Spec:** HSH §3.1.
- **Dependencies:** TASK-K-01, TASK-K-02, TASK-K-03.

### TASK-HS-S1-02 — Projection from compiled assembly

Walking `HsmDefinitionBlob` + `MachineMetadata` + layout method into the editor model.

- **Spec:** HSH §3.2.
- **Verifies:** `HsmAssetProjectionTests`.

### TASK-HS-S1-03 — Identity bridges

`_stableIdToState`, `_visualIdToTransition`, etc.

- **Spec:** HSH §3.3.

### TASK-HS-S1-04 — Statechart auto-layout

Hierarchy-aware grid layout for new assets.

- **Spec:** HSH §3.4.

### TASK-HS-S1-05 — `HsmFluentEmitter`

Deterministic emit per HSH §4.

- **Spec:** HSH §4 entire section.
- **Dependencies:** TASK-S1-06.
- **Verifies:** `HsmFluentEmitterDeterminismTests`.

### TASK-HS-S1-06 — `HsmNodeCatalog`

Static state-kind entries + dynamic actions/guards from `HsmActionDispatcher`.

- **Spec:** HSH §5.1.

### TASK-HS-S1-07 — `HsmTypeSystem` and `HsmLinkValidator`

Stub type system + structural transition rules (no final-state outgoing, history target rules).

- **Spec:** HSH §5.2, §5.3.
- **Verifies:** `HsmLinkValidatorTests`.

### TASK-HS-S1-08 — `HsmCommandSink`

Command translation including container + attachment commands.

- **Spec:** HSH §5.4.
- **Dependencies:** TASK-NEC-07, TASK-NEA-08.

### TASK-HS-S1-09 — Composite states as containers

State nodes implementing `IContainerNodeModel`.

- **Spec:** HSH §6.1.
- **Dependencies:** TASK-NEC-01.

### TASK-HS-S1-10 — Parallel composites with regions

Region rendering, region-scoped drag, region commands.

- **Spec:** HSH §6.2.
- **Dependencies:** TASK-NEC-08.

### TASK-HS-S1-11 — Composite collapse + transition indicators

Per-NEC §6.4 plus the dot-indicator on collapsed-container boundaries when a transition's endpoint is hidden.

- **Spec:** HSH §6.4.

### TASK-HS-S1-12 — Transition link bridge

`HsmTransitionLink` adapting `TransitionNode` to NodeEditor's `ILinkModel` via hidden any-pins.

- **Spec:** HSH §7.1, §7.2.

### TASK-HS-S1-13 — `hsm.transition_labels` custom renderer

The critical-path renderer: `Event[Guard]/Action` at link midpoints, hit-testable, sync-group + priority badges.

- **Spec:** HSH §7.3, §15.1.
- **Dependencies:** TASK-NER-01 through TASK-NER-05.

### TASK-HS-S1-14 — Internal-transition rendering

Dashed loop inside source state for `Kind == Internal` (the §19.3 open question must be resolved with NodeEditor implementer first).

- **Spec:** HSH §7.4, §19.3.

### TASK-HS-S1-15 — `hsm.initial_state_arrows` custom renderer

⦿─→ markers inside composites and regions. LCA highlight deferred to Slice 2.

- **Spec:** HSH §8.1, §15.2 (initial-arrow portion only).
- **Dependencies:** TASK-NER-01.

### TASK-HS-S1-16 — Events table window

The `hsm_events` window with the columns from HSH §9.1.

- **Spec:** HSH §9.

### TASK-HS-S1-17 — Global transitions strip

Window-chrome strip showing global transitions.

- **Spec:** HSH §9.3.

### TASK-HS-S1-18 — Action / Guard pickers (HSM)

StructEdit attributes wired to `HsmActionDispatcher`.

- **Spec:** HSH §10.1, §10.2.

### TASK-HS-S1-19 — OutputLaneMask inference

The reflection-driven computation per HSH §10.3.

- **Spec:** HSH §10.3.
- **Dependencies:** TASK-K-01.
- **Verifies:** `OutputLaneMaskInferenceTests`.

### TASK-HS-S1-20 — HSM facet structs

`StateFacet`, `TransitionFacet`, `RegionFacet`, `EventFacet`, `GlobalTransitionFacet`.

- **Spec:** HSH §11.1.

### TASK-HS-S1-21 — Inspector dispatch + LCA computation

The dispatch switch and `FindLca` helper.

- **Spec:** HSH §11.2, §11.3.
- **Verifies:** `LcaComputationTests`.

### TASK-HS-S1-22 — HSM validation rules

The 14 diagnostic codes + rules from HSH §12.1, including OutputLaneMask conflict detection §12.2.

- **Spec:** HSH §12.
- **Verifies:** `HsmValidationTests`.

### TASK-HS-S1-23 — `HsmAssetContributor`

`IAssetCatalogContributor` implementation reflecting `[HsmDefinition]`s.

- **Spec:** HSH §1.1.

### TASK-HS-S1-24 — Host services wiring + DI

The `HsmEditorHostServices` factory + DI registrations.

- **Spec:** HSH §5.
- **Dependencies:** all HS-S1 tasks above.

### TASK-HS-S1-25 — Quick reload classification (HSM)

XxHash64-based structure/param hash producers.

- **Spec:** HSH §16.
- **Dependencies:** TASK-S1-13.

---

## Phase 7 — Shared infrastructure: refactor + find-references

With both hosts in place producing reference data, the cross-asset refactor can be implemented and validated.

### TASK-S7-01 — `IRefactorService` core

Find-references + preview + apply pipeline.

- **Spec:** shared infra §16.2, §16.3.
- **Dependencies:** TASK-S1-05.
- **Verifies:** `RefactorServiceTests`.

### TASK-S7-02 — `AtomicMultiFileWriter`

Temp-file + rename batch write.

- **Spec:** shared infra §16.5.
- **Verifies:** `AtomicMultiFileWriterTests`.

### TASK-S7-03 — `FindResultsWindow`

The shared window for find-references results and refactor preview.

- **Spec:** shared infra §16.4.
- **Dependencies:** TASK-S7-01.

### TASK-S7-04 — Inspector right-click integration

Find References / Rename / Go to Definition on action / event / asset references in facet drawers.

- **Spec:** shared infra §10.6.
- **Dependencies:** TASK-S7-03.

### TASK-S7-05 — Asset Browser refactor integration

Right-click asset row → Find References / Rename / Delete with dangling-reference report.

- **Spec:** shared infra §9, §16.6.
- **Dependencies:** TASK-S7-03.

### TASK-S7-06 — Refactor end-to-end test

Integration test renaming an action across Blueprint + BTree + HSM fixtures.

- **Spec:** shared infra §20.2 last bullet.
- **Dependencies:** TASK-S7-01 through TASK-S7-05, both hosts' Slice 1.

---

## Phase 8 — BTree host: Slice 2 (runtime read-only)

### TASK-BT-S2-01 — `IBTreeDebugSession` + `BTreeDebugSession`

Production implementation including `GetCurrentStateSnapshot()` and observer-mode lifecycle.

- **Spec:** BTH §12.1.
- **Dependencies:** TASK-S1-12, TASK-K-05.

### TASK-BT-S2-02 — `btree.runtime_overlay` custom renderer

Running-node pulse, stack-ancestry glow.

- **Spec:** BTH §12.4.

### TASK-BT-S2-03 — Live blackboard panel (read-only mode)

Reflect schema + show live values from debug session.

- **Spec:** BTH §9.2.

### TASK-BT-S2-04 — `BTreeRuntimeInspectorPane`

The BTree-specific Runtime Inspector pane.

- **Spec:** BTH §12.7.
- **Dependencies:** TASK-S1-15.

### TASK-BT-S2-05 — `BTreeTraceLaneProvider`

Register four BTree lanes: nodes, stack, async, errors.

- **Spec:** BTH §13.

---

## Phase 9 — HSM host: Slice 2 (runtime read-only)

### TASK-HS-S2-01 — `IHsmDebugSession` + `HsmDebugSession`

Production implementation including snapshot.

- **Spec:** HSH §13.1.
- **Dependencies:** TASK-S1-12, TASK-K-04.

### TASK-HS-S2-02 — `hsm.runtime_overlay` custom renderer

Active configuration glow, ancestor diminishing, last-transition pulse.

- **Spec:** HSH §13.4, §15.5.

### TASK-HS-S2-03 — LCA highlight in initial-state-arrows renderer

Extend the `hsm.initial_state_arrows` renderer with the LCA highlight when a transition is selected.

- **Spec:** HSH §15.2 (LCA portion).
- **Dependencies:** TASK-HS-S1-15.

### TASK-HS-S2-04 — `HsmRuntimeInspectorPane`

The HSM-specific Runtime Inspector pane.

- **Spec:** HSH §13.5.
- **Dependencies:** TASK-S1-15.

### TASK-HS-S2-05 — `HsmTraceLaneProvider`

Register six HSM lanes: states, events, actions, guards, timers, conflicts.

- **Spec:** HSH §14.

---

## Phase 10 — Both hosts: Slice 3 (stepping + breakpoints)

### TASK-BT-S3-01 — Breakpoint registry + UI gutter (BTree)

Per-VisualId breakpoints + canvas gutter clicks.

- **Spec:** BTH §12.3.

### TASK-BT-S3-02 — Step controls (BTree)

Continue / Pause / Step Into / Step Over / Step Out wired through the debug session.

- **Spec:** BTH §12.2.

### TASK-BT-S3-03 — `btree.subtree_boundaries` custom renderer

Faint blue dashed rectangle around the current subtree.

- **Spec:** BTH §12.5.

### TASK-BT-S3-04 — Async event lane in trace timeline (BTree)

The async lane uses `BTreeAsyncEvent` records.

- **Spec:** BTH §13.

### TASK-HS-S3-01 — Breakpoint registry + UI gutter (HSM)

State / transition / region / event breakpoints.

- **Spec:** HSH §13.3.

### TASK-HS-S3-02 — Step controls (HSM)

The four step operations.

- **Spec:** HSH §13.2.

### TASK-HS-S3-03 — `hsm.region_conflicts` custom renderer

Connector lines + ⚠ glyphs + click-to-popup.

- **Spec:** HSH §15.3.

### TASK-HS-S3-04 — `hsm.history_glyphs` custom renderer

H / H* / ⊙ glyph rendering with rendering-bypass.

- **Spec:** HSH §15.4.

---

## Phase 11 — Multi-instance, polish (Slices 4–5)

### TASK-BT-S4-01 — Aggregate counters (BTree)

Per-VisualId entry-frequency tracking across instances.

- **Spec:** BTH §12.6.

### TASK-BT-S4-02 — `btree.heatmap_overlay` custom renderer

Colored fills based on aggregate counters.

- **Spec:** BTH §12.6.

### TASK-HS-S4-01 — Aggregate state-entry counters (HSM)

Per-StableId frequency tracking.

- **Spec:** HSH §17 Slice 4.

### TASK-HS-S4-02 — Heatmap on states (HSM)

Same rendering pattern as BTree's.

- **Spec:** HSH §17 Slice 4.

### TASK-S11-01 — Asset Browser live-instance count

Per-asset "🟢 N live" indicator.

- **Spec:** shared infra §9.5.
- **Dependencies:** both hosts' Slice 2.

### TASK-S11-02 — Cross-asset rename surfaces (full)

Event rename (HSM, machine-scoped), action rename (cross-host), asset rename.

- **Spec:** shared infra §16; HSH §17 Slice 4.
- **Dependencies:** Phase 7.

### TASK-S11-03 — Reset-layout actions

Toolbar action in both hosts.

- **Spec:** BTH §17 Slice 5, HSH §17 Slice 5.

### TASK-S11-04 — Comments on transitions and regions (HSM)

Layout method extension + inspector + emit.

- **Spec:** HSH §17 Slice 5.

### TASK-S11-05 — Polish: drag-to-create-transition refinement

Snap-to-state behavior, hover preview.

- **Spec:** HSH §17 Slice 5.

### TASK-S11-06 — Diagnostics aggregation window

Cross-asset diagnostics summary (deferred from Slice 1).

- **Spec:** BTH §11.2 / HSH §12.3.

---
