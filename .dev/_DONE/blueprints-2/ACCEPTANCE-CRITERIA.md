# AI Editor — Acceptance Criteria

> **Purpose:** Per-phase/per-slice acceptance criteria. A slice is "done" when every criterion in its section is verifiably true.
> **Companion docs:** `TASK-TRACKER.md` for task status, `TASK-DETAIL.md` for per-task descriptions.
> **How to use:** When a slice is proposed for completion, walk this document for that slice top to bottom. Each criterion is a yes/no question. If any answer is no, the slice is not done.
> **Specs referenced:** same set as TASK-DETAIL.md.

---

## Table of Contents

- Phase 0 — Kernel prerequisites
- Phase 1 — Shared infrastructure foundation
- Phase 2 — NodeAttachments extension
- Phase 3 — ContainerNodes extension
- Phase 4 — CustomCanvasRenderer extension
- Phase 5 — BTree host Slice 1 (authoring)
- Phase 6 — HSM host Slice 1 (authoring)
- Phase 7 — Refactor + find-references
- Phase 8 — BTree host Slice 2 (runtime read-only)
- Phase 9 — HSM host Slice 2 (runtime read-only)
- Phase 10 — Stepping + breakpoints (both hosts)
- Phase 11 — Multi-instance + polish

---

## Phase 0 — Kernel prerequisites

### Functional acceptance

- F0-01. `[HsmAction(Name = "X", Lane = CommandLane.Animation)]` compiles and the source generator preserves the `Lane` value into a runtime-reachable attribute property. (TASK-K-01)
- F0-02. `[HsmAction(Name = "X")]` without `Lane` still compiles and behaves identically to the pre-change attribute. (TASK-K-01)
- F0-03. `HsmBuilder.State("X")` and `HsmBuilder.State("X", stableId: someGuid)` both compile; the supplied Guid round-trips through compile → reflection back to the editor. (TASK-K-02)
- F0-04. `StateBuilder.AddChild(...)` accepts an optional `stableId`. (TASK-K-02)
- F0-05. `TransitionBuilder.GoTo(...)` and `HsmBuilder.GlobalTransition(...)` accept an optional `visualId`. (TASK-K-03)
- F0-06. Every BTree fluent builder method that emits a node accepts an optional `visualId`. (TASK-K-06)
- F0-07. HSM `InstanceFlags.Paused` exists; an instance with the flag set does not advance microsteps during a tick; clearing it resumes normal RTC. (TASK-K-04)
- F0-08. BTree instance Paused flag (on `DebugState` or equivalent) exists; a behavior with the flag set does not advance during a tick. (TASK-K-05)

### Quality acceptance

- Q0-01. All existing FastHSM and FastBTree unit tests continue to pass without modification.
- Q0-02. No existing handwritten asset code needs to change to keep working. (Defaults preserve old behavior.)
- Q0-03. The pause behavior in F0-07 and F0-08 has a kernel-level unit test verifying it.

### Out of scope

- Pause-on-breakpoint mechanics (lives in the editor's debug session, Phase 10).
- Editor-side reflection of these additions (handled in Phase 5/6).
- Persistence of `stableId`/`visualId` in serialized form (the kernel doesn't care; the editor's layout method handles persistence).

---

## Phase 1 — Shared infrastructure foundation

### Functional acceptance

- F1-01. A test harness that registers a single mock `IAssetCatalogContributor` can open the editor, see exactly one asset in the asset browser, and click it to update `EditorSelectionStore.ActiveAsset`. (TASK-S1-04, S1-09)
- F1-02. Two windows opened on the same mock asset share sub-selection: writing `ActiveSubSelection` from one window updates the other window's display. (TASK-S1-03, S1-09)
- F1-03. Two windows opened on *different* mock assets have independent sub-selections. (TASK-S1-03)
- F1-04. The `Inspector` window opens an appropriate StructEdit drawer when a mock subselection is set; switching subselection commits the previous drawer and opens the new one. (TASK-S1-10)
- F1-05. A mock `IAiTraceObserver` and a mock `IAiDebugSession` can coexist on the same registry — the observer is registered, the session is `TryAcquireSession`-acquired. A second session-acquire fails; a second observer-register succeeds. (TASK-S1-11, S1-12)
- F1-06. The hot-reload status indicator displays Cosmetic / Soft / Hard correctly given hand-fed structure/param hashes. (TASK-S1-13)
- F1-07. `LayoutDiscovery.TryGetLayout<TAttr, TLayout>(assembly, assetId)` returns the matching layout method's result; returns null for missing or mismatched-AssetId. (TASK-S1-07)
- F1-08. A `FluentCSharpEmitter` round-trip on a trivial fixture (3-node model) produces byte-identical output across 10 consecutive runs on the same machine. (TASK-S1-06)
- F1-09. The reference catalog, fed two mock assets where asset A references an action FQN that asset B also references, returns both references from `FindReferences(actionFqn)`. (TASK-S1-05)
- F1-10. A DDS-published `SelectionChangedEvent` updates `EditorSelectionStore.SelectedEntity`. (TASK-S1-08)
- F1-11. All four shared windows (`ai_asset_browser`, `ai_inspector`, `ai_runtime_inspector`, `ai_trace_timeline`) register correctly and can be shown via the WindowManager. (TASK-S1-09, S1-10, S1-14, S1-15)

### Quality acceptance

- Q1-01. All `Hrot.Editor.AiShared.Tests` unit tests pass.
- Q1-02. `EditorSelectionStore` fires `OnSelectionChanged` exactly once per mutation; no duplicate notifications, no missed notifications (verified by counter in unit test).
- Q1-03. The fluent emitter's deterministic-output property holds for an empty model, a single-node model, and a deeply nested model.
- Q1-04. Asset catalog rebuild after a `Changed` event takes under 10 ms for a synthetic project of 100 mock assets.

### Out of scope

- Any subsystem-specific facet drawers, panes, lane providers, or contributors (Phases 5/6/8/9).
- Refactor surfaces (Phase 7).
- The Find Results window (Phase 7).
- Per-window `ChainToMap` toggle UI (defer to Slice 2; the field exists, the wiring is incomplete).

---

## Phase 2 — NodeAttachments extension

### Functional acceptance

- F2-01. The demo scenario renders a node with three attachments visible above its header. (TASK-NEA-05, NEA-11)
- F2-02. Clicking an attachment selects it; the selection-changed event includes the attachment ID. (TASK-NEA-06, NEA-07)
- F2-03. Right-clicking an attachment shows a host-provided context menu (the demo's stub provider). (TASK-NEA-09)
- F2-04. Adding a 4th attachment that doesn't fit on the first row causes the row to wrap; container of the host node grows accordingly in height. (TASK-NEA-04)
- F2-05. Dragging a host node carries its attachments with it; attachments don't have their own position. (TASK-NEA-04)
- F2-06. At zoom < 0.5, attachments collapse to a single colored bar; at zoom ≥ 0.5, they render as pills. (TASK-NEA-10)
- F2-07. Tab cycles selection through a node's attachments in stack order. (TASK-NEA-07)
- F2-08. Pressing Delete with attachments selected fires `RemoveAttachments`; undoing restores them. (TASK-NEA-08)
- F2-09. A host that does NOT implement attachment-related interfaces continues to compile and render exactly as before the extension. (TASK-NEA-02, NEA-03)

### Quality acceptance

- Q2-01. All `AttachmentLayoutTests`, `AttachmentHitTestTests`, `AttachmentCommandsTests`, `AttachmentSelectionTests`, `AttachmentSpatialIndexTests` pass.
- Q2-02. With 200 nodes × 1.5 average attachments at zoom 1.0, total custom-render contribution stays under 2 ms per frame.
- Q2-03. The Blueprint editor (which does not use attachments) shows no behavioral change after the extension lands.

### Out of scope

- `IAttachmentRenderer` custom-rendering plugin (declared but not implemented in v1).
- Drag-and-drop of attachments between hosts.
- Animation of pill add/remove.

---

## Phase 3 — ContainerNodes extension

### Functional acceptance

- F3-01. The demo scenario renders a container holding three children. (TASK-NEC-04)
- F3-02. Dragging a child within its container's interior triggers auto-resize when needed. (TASK-NEC-03, NEC-06)
- F3-03. Dragging a child past the container's edge into the parent container reparents it; `ChangeParent` command fires; child's `Position` is updated to the new parent-local coords. (TASK-NEC-06, NEC-07)
- F3-04. Dragging a container into one of its own descendants is rejected with red drop-target indication; the drop is not applied. (TASK-NEC-06)
- F3-05. Collapsing a container hides its children; a wire from outside terminating inside the collapsed container shows a boundary indicator dot. (TASK-NEC-04, NEC-09)
- F3-06. A parallel-region container renders region dividers and region headers; dragging a child across a region boundary updates the child's `RegionIndex`. (TASK-NEC-08)
- F3-07. A nested-container demo (4 levels deep) renders correctly with each level's children visible. (TASK-NEC-03, NEC-04)
- F3-08. Selecting a container alone, then dragging, moves the container with its children naturally; selecting a container AND one of its children, then dragging, applies only the container's drag delta to the child (no double-application). (TASK-NEC-06)
- F3-09. A host that does NOT use containers continues to behave exactly as before (no model invariant violations on non-container nodes). (TASK-NEC-01)
- F3-10. Emitter round-trip on a fixture with containers produces children in deterministic order matching the model's `ChildNodeIds` order. (TASK-NEC-10)

### Quality acceptance

- Q3-01. All `ContainerBoundsTests`, `ContainerTransformTests`, `ContainerHitTestTests`, `ContainerDragTests`, `ContainerCycleDetectionTests`, `RegionLayoutTests`, `ContainerCommandsTests`, `ChildOrderDeterminismTests` pass.
- Q3-02. 80-state scenario with 5 composite containers at zoom 1.0 renders in under 7 ms per frame.
- Q3-03. Existing hosts pass their pre-extension test suites with no modifications.

### Out of scope

- User-driven manual container resize (auto-resize only).
- Dive-into-container navigation (deferred Slice 2+; v1 keeps everything visible on one canvas).
- Drag-into-container reordering with snap-to-position (placement is wherever the cursor lands).

---

## Phase 4 — CustomCanvasRenderer extension

### Functional acceptance

- F4-01. The demo scenario registers four renderers (one per pass) and each draws content visible at its expected layer. (TASK-NER-01, NER-02, NER-09)
- F4-02. Pan and zoom of the canvas transform all custom-rendered content correctly (using `CanvasToScreen`). (TASK-NER-03)
- F4-03. A hit-testable custom renderer participates in the canvas hit-test; clicking on a custom-drawn element selects it. (TASK-NER-04, NER-05)
- F4-04. Multi-select with Ctrl+click extends the custom-element selection list. (TASK-NER-05)
- F4-05. Right-clicking a custom-drawn element shows a host-provided context menu. (TASK-NER-07)
- F4-06. Selecting a custom-drawn element routes the Details panel target appropriately. (TASK-NER-06)
- F4-07. A renderer with `IsActive => false` is fully skipped — its time does not appear in the perf accounting. (TASK-NER-08)
- F4-08. Across passes, draw order matches enum order (`BeforeContent → AfterWires → AfterNodes → TopMost`). Within a pass, order matches registration order. Verified by a test using a fake draw-list recorder. (TASK-NER-01, NER-02)
- F4-09. A host with zero registered custom renderers sees no performance change vs. a build without the extension. (TASK-NER-01)

### Quality acceptance

- Q4-01. All `CustomRendererRegistrationTests`, `CustomRendererPassOrderingTests`, `CustomRendererHitTestTests`, `CustomRendererSelectionTests`, `CustomRendererPerfAccountingTests` pass.
- Q4-02. 100 custom-drawn elements across passes render in under 1.5 ms per frame.
- Q4-03. With 90% of elements off-screen (visible-set culling), render time drops to under 0.3 ms.

### Out of scope

- GPU-direct rendering (ImGui draw list only).
- Animated transitions; the renderer is invoked once per frame and reads whatever state the host provides.
- A real production renderer (those land with their host phases).

---

## Phase 5 — BTree host Slice 1 (authoring)

### Functional acceptance — opening an asset

- F5-01. Opening the OrcGuard sample asset (or equivalent) produces a canvas showing the correct tree shape: composites, leaves, and decorator pills on the right nodes. (TASK-BT-S1-02, BT-S1-09)
- F5-02. Decorator pills appear above their host nodes in source-order (innermost wrapper = leftmost pill, outermost = rightmost). (TASK-BT-S1-09)
- F5-03. The Inspector populates when a node is selected; for an Action, shows `BTreeActionFacet` with method dropdown (`BehaviorHashPicker`) populated from `BehaviorRegistry`. (TASK-BT-S1-14)
- F5-04. Subtree nodes render as black-box rectangles with the subtree name in the title; the kernel's subtree is not inline-expanded. (TASK-BT-S1-12)
- F5-05. Blackboard panel shows the reflected fields of the asset's blackboard struct, with correct field names and types. (TASK-BT-S1-13)
- F5-06. The Asset Browser shows the BTree asset under its file-system folder; double-click opens the canvas. (TASK-BT-S1-17 plus shared)

### Functional acceptance — editing

- F5-07. Dragging a node moves it; layout method updates within the debounce window (≤ 500 ms idle); next save persists the new position. (TASK-BT-S1-08)
- F5-08. Adding a child to a Sequence via right-click → palette inserts the child and rewires the kernel-side parent-child link; emit produces the new node in the correct position in the fluent builder. (TASK-BT-S1-08)
- F5-09. Adding a Repeater decorator pill via right-click → "Add decorator → Repeater" produces a pill on the selected node; emit produces a `.Repeater(...)` wrapper around the corresponding child. (TASK-BT-S1-09)
- F5-10. Editing a Wait node's Duration in the Inspector updates the value; save produces the new float. (TASK-BT-S1-14)
- F5-11. Setting a Condition's expression target field via `BlackboardFieldPicker` produces the correct `dto => dto.Field` lambda in the emit. (TASK-BT-S1-15)
- F5-12. Selecting Observer Selector from the palette produces a node with the eye glyph in the header. (TASK-BT-S1-10)
- F5-13. A Condition or Observer child of an Observer Selector shows the `👁 OBSERVES` badge on its connecting link. (TASK-BT-S1-11) [Slip-OK: if Phase 4 is late, this single criterion may be descoped; the rest of Slice 1 still ships.]
- F5-14. Double-clicking a Subtree node switches `ActiveAsset` to the referenced asset; clicking the breadcrumb returns. (TASK-BT-S1-12)
- F5-15. A validation error on a node (e.g., unbound action method) outlines the node in red and shows the diagnostic in the Inspector. (TASK-BT-S1-16)

### Functional acceptance — saving

- F5-16. Save produces a `.cs` file that compiles; loading the saved file via reflection reproduces the editor model byte-for-byte. (TASK-BT-S1-05; round-trip property)
- F5-17. Save with no changes performs no file write. (TASK-BT-S1-05; shared §6.5 idempotence)
- F5-18. The saved file carries the `HROT_EDITOR_GENERATED` marker and the `AssetId` Guid comment at the top. (TASK-BT-S1-05)
- F5-19. The `[BTreeLayout]` method emits with entries sorted by Guid; positions, comments, expression-target fields preserved.
- F5-20. Hot reload classification reports Cosmetic for layout-only edits, Soft for parameter edits, Hard for topology changes. (TASK-BT-S1-19)

### Quality acceptance

- Q5-01. All `BehaviorTreeAssetProjectionTests`, `DecoratorPillCollapseTests`, `BTreeFluentEmitterDeterminismTests`, `BTreeCommandSinkTests`, `BTreeLinkValidatorTests`, `BTreeValidationTests`, `BTreeNodeCatalogTests`, `BlackboardSchemaReflectionTests` pass.
- Q5-02. A representative 100-node asset opens (project → render) in under 1 second.
- Q5-03. Drag of a single node is smooth at 60 fps (no perceptible jank).
- Q5-04. Save → MSBuild → reload total latency ≤ 100 ms author-perceived for a Cosmetic change. (Best-effort target; longer rebuilds are OK if the editor itself adds < 50 ms.)
- Q5-05. Emitter round-trip property holds for the OrcGuard sample plus at least 4 additional fixtures of varying complexity.

### Out of scope (Slice 1)

- Live debug overlay, runtime inspector content, breakpoints, step controls.
- Aggregate / heatmap rendering.
- Find references on actions (deferred to Phase 7).
- Refactor / rename surfaces (deferred to Phase 7).
- Multi-instance display in the Runtime Inspector.
- The async-trace lane in the trace timeline (deferred to Slice 3).
- A diagnostics aggregation window (deferred to polish).
- Cross-asset drag (palette nodes from another asset into this canvas).

---

## Phase 6 — HSM host Slice 1 (authoring)

### Functional acceptance — opening an asset

- F6-01. Opening the TrafficLight sample (or EnemyBrain equivalent) produces a canvas showing the correct state hierarchy: simple states, one or more composites, and any parallel composites with their regions. (TASK-HS-S1-02, HS-S1-09)
- F6-02. Composite states render with their children visible inside their interior; the container outline encloses children with correct padding. (TASK-HS-S1-09)
- F6-03. Parallel composites render with dashed region dividers and region headers showing region name and priority. (TASK-HS-S1-10)
- F6-04. Transitions render between states; each transition shows its `Event[Guard]/Action` label at the midpoint. (TASK-HS-S1-13)
- F6-05. Internal transitions render as a dashed loop *inside* the source state (not as a normal arrow). (TASK-HS-S1-14)
- F6-06. Each composite shows the ⦿─→ initial-state marker pointing to its initial child; each region in a parallel composite shows its own marker. (TASK-HS-S1-15)
- F6-07. The Inspector populates when a state, transition, region, event, or global transition is selected, showing the corresponding facet (StateFacet / TransitionFacet / RegionFacet / EventFacet / GlobalTransitionFacet). (TASK-HS-S1-20, HS-S1-21)
- F6-08. The Events table window lists all events with ID, Name, Payload, Flags, Priority, Global. (TASK-HS-S1-16)
- F6-09. The global transitions strip lists any `[HsmDefinition]`-level global transitions; clicking one highlights its target state. (TASK-HS-S1-17)
- F6-10. Selecting a transition shows the read-only LCA name and LCA cost in the inspector. (TASK-HS-S1-21)
- F6-11. A state's `OutputLanes` is shown read-only in the inspector, summarizing which CommandLanes its OnEntry/OnExit/Activity actions write to. (TASK-HS-S1-19)

### Functional acceptance — editing

- F6-12. Dragging a state moves it; if the state is inside a composite, the composite auto-resizes; the layout method captures the new position. (TASK-HS-S1-08, NEC-03)
- F6-13. Adding a new state via palette → drop on canvas (root level) creates a simple state; drop inside a composite makes it a child. (TASK-HS-S1-08)
- F6-14. Dragging a state from one region to another in a parallel composite updates its RegionIndex. (TASK-HS-S1-10)
- F6-15. Drawing a transition from State A's edge to State B's edge creates a new transition with default event (None / placeholder) and default priority. (TASK-HS-S1-08, HS-S1-12)
- F6-16. Editing a transition's Event via the inspector picker updates the label rendered at its midpoint. (TASK-HS-S1-13, HS-S1-18)
- F6-17. Adding a new event via the Events table's "+ Add Event" assigns an unused EventId and appears in transition pickers. (TASK-HS-S1-16)
- F6-18. Adding a region to a composite via right-click → "Add Region" inserts a new region; dragging existing children into it correctly populates it. (TASK-HS-S1-10)
- F6-19. Adding a History pseudo-state from the palette produces a small circled-H glyph; selecting it shows a History facet. (Phase 10 covers H glyph rendering. Slice 1 can use the standard rectangle until Slice 3 renderer lands; mention this in the slice review.)
- F6-20. A validation error (e.g., composite with no initial child, or two parallel regions writing to the same lane) shows a red/yellow outline on the affected state(s); the inspector shows the diagnostic. (TASK-HS-S1-22)

### Functional acceptance — saving

- F6-21. Save produces a `.cs` file that compiles; reflection-load reproduces the editor model. (TASK-HS-S1-05; round-trip property)
- F6-22. Saved file carries the marker and AssetId comment. (TASK-HS-S1-05)
- F6-23. Events emit in EventId-ascending order; actions/guards emit in alphabetical FQN order; states emit depth-first; state config emits in the canonical subsection order from HSH §4.2 rule 4. (TASK-HS-S1-05)
- F6-24. Hot reload classification reports Cosmetic / Soft / Hard correctly. (TASK-HS-S1-25)

### Quality acceptance

- Q6-01. All `HsmAssetProjectionTests`, `HsmFluentEmitterDeterminismTests`, `HsmCommandSinkTests`, `HsmLinkValidatorTests`, `HsmValidationTests`, `OutputLaneMaskInferenceTests`, `LcaComputationTests`, `HsmFacetMapperTests` pass.
- Q6-02. A representative 50-state HSM opens in under 1.5 seconds.
- Q6-03. Drag of a single state is smooth at 60 fps; dragging a composite with 10 children stays smooth.
- Q6-04. Save → MSBuild → reload Cosmetic-tier latency ≤ 100 ms editor-side (excluding MSBuild itself).
- Q6-05. Round-trip property holds for at least 5 fixture HSMs including: simple, with-composites, with-parallel-regions, with-history-state, with-final-state.

### Out of scope (Slice 1)

- Live debug overlay, runtime state on canvas, breakpoints, step controls.
- LCA highlight (deferred to Slice 2; LCA shown as text in inspector only).
- `hsm.region_conflicts` renderer (deferred to Slice 3; conflicts shown as outline + inspector diagnostic only).
- `hsm.history_glyphs` renderer (deferred to Slice 3; history states render as normal rectangles in Slice 1 with the History flag set).
- Refactor / rename surfaces (deferred to Phase 7).
- Aggregate / heatmap rendering.
- Comments on transitions and regions (deferred to polish).
- Drag-to-create-transition snap-to-state refinements (basic drag-create works; polish is Phase 11).

---

## Phase 7 — Refactor + find-references

### Functional acceptance

- F7-01. Right-clicking an action method name in any BTree or HSM inspector field shows a context menu with "Find References" and "Rename." (TASK-S7-04)
- F7-02. "Find References" opens the FindResults window listing every referencing element across BTree, HSM, and Blueprint assets, grouped by host asset. (TASK-S7-03)
- F7-03. Clicking a reference in the results panel sets `ActiveAsset` to the host asset and selects the referencing element. (TASK-S7-03)
- F7-04. "Rename" opens a preview pane showing every file that would be modified and the line-level changes. The user can selectively exclude individual files or references. (TASK-S7-01, S7-03)
- F7-05. Clicking "Apply" in the preview writes all modified files atomically: either all succeed or all fail (no half-renamed state). (TASK-S7-02)
- F7-06. After a successful rename, MSBuild rebuilds; the reference catalog rebuilds; the new key resolves correctly in subsequent find-references queries. (TASK-S7-01)
- F7-07. Renaming an event in an HSM asset updates only references *within that machine*; same-named events in sibling HSMs are unaffected. (Machine-scoping per shared §4.6.)
- F7-08. Right-click on an asset in the Asset Browser → "Delete with dangling-reference report" produces a list of every reference that would be broken; the user must explicitly confirm before deletion. (TASK-S7-05)

### Quality acceptance

- Q7-01. All `RefactorServiceTests` and `AtomicMultiFileWriterTests` pass.
- Q7-02. End-to-end test (TASK-S7-06): rename an action referenced by 5+ Blueprint, BTree, and HSM assets completes in under 500 ms editor-side (excluding MSBuild).
- Q7-03. Atomic-write rollback test: simulating a file-lock mid-batch leaves no `.tmp` debris and reports clear failure.
- Q7-04. Find-references query on an action with 30+ references returns in under 100 ms.

### Out of scope

- Moving an action between declaring types (the user does this in their IDE; the editor offers post-reload reconciliation but not proactive cross-IDE refactor).
- Batch-rename of multiple unrelated keys in one transaction.
- Search/replace within asset bodies (Wait durations, Repeater counts, etc.).
- Cross-project refactor (assumes single `.csproj`).
- In-editor undo of refactor (use git; out of v1 scope).

---

## Phase 8 — BTree host Slice 2 (runtime read-only)

### Functional acceptance

- F8-01. Attaching the editor to a running game (entity selected, asset open) populates `IBTreeDebugSession.IsAttached = true`. (TASK-BT-S2-01)
- F8-02. The currently-running node on the selected entity is visually highlighted on the canvas with a pulsing border. (TASK-BT-S2-02)
- F8-03. The stack ancestry from the running node up to the root is rendered with diminishing-intensity glow. (TASK-BT-S2-02)
- F8-04. The Blackboard panel displays live field values for the selected entity, refreshed every frame. (TASK-BT-S2-03)
- F8-05. The `BTreeRuntimeInspectorPane` shows: RunningNode (symbolicated name), StackPointer / StackDepth, NodeIndexStack contents, LocalRegisters, AsyncHandles. (TASK-BT-S2-04)
- F8-06. The trace timeline shows the `bt.nodes` lane with NodeStatus-colored ribbons; the `bt.stack` lane with push/pop bracketed ranges; the `bt.errors` lane with red marks for tracer-emitted errors. (TASK-BT-S2-05)
- F8-07. Detaching the debug session clears all overlays and disables observation on previously-watched assets. (TASK-BT-S2-01)
- F8-08. Multiple entities running the same asset: the Runtime Inspector shows only the focused entity's state. (Multi-entity views are deferred.)
- F8-09. The Asset Browser displays a "🟢 N live" badge for an asset with N running entities — refreshed every 500 ms. (Optional; covers shared §9.5 if implemented here.)

### Quality acceptance

- Q8-01. Attach → first overlay render ≤ 200 ms.
- Q8-02. Per-frame runtime-overlay cost ≤ 2 ms for a 100-node asset.
- Q8-03. The trace timeline scrolls smoothly at 60 fps with a full 1024-record buffer.
- Q8-04. Detach → kernel returns to zero-overhead state; `DebugState.Flags` cleared on all matching entities within one tick.

### Out of scope (Slice 2)

- Breakpoints, pause, step controls (Slice 3).
- Subtree-boundary renderer (Slice 3).
- Async-trace lane (Slice 3).
- Heatmap (Slice 4).
- Live mutation of blackboard fields (Slice 3 "Make Editable" toggle).

---

## Phase 9 — HSM host Slice 2 (runtime read-only)

### Functional acceptance

- F9-01. Attaching the editor populates `IHsmDebugSession.IsAttached = true`. (TASK-HS-S2-01)
- F9-02. The active configuration is highlighted: each currently-active leaf glows at full intensity; ancestors glow with diminishing intensity. (TASK-HS-S2-02)
- F9-03. The last-fired transition (if recent) pulses briefly. (TASK-HS-S2-02)
- F9-04. Selecting a transition shows the LCA highlight on the correct ancestor composite. (TASK-HS-S2-03)
- F9-05. The `HsmRuntimeInspectorPane` shows: active leaves (symbolicated path-to-leaf), event queue, timer slots, history slots, RNG state, generation, instance phase, microstep. (TASK-HS-S2-04)
- F9-06. The trace timeline shows the six HSM lanes: states, events, actions, guards, timers, conflicts. (TASK-HS-S2-05)
- F9-07. Detaching clears overlays and disables observation. (TASK-HS-S2-01)

### Quality acceptance

- Q9-01. Attach → first overlay render ≤ 200 ms.
- Q9-02. Per-frame runtime-overlay cost ≤ 2 ms for a 50-state HSM with 4-level depth.
- Q9-03. The trace timeline handles ~30 events/sec without lag.

### Out of scope (Slice 2)

- Breakpoints, pause, step controls (Slice 3).
- Region-conflict renderer (Slice 3 — Slice 2 still shows them via state outlines + inspector).
- History-glyph renderer (Slice 3).
- Live mutation of state internals.
- Heatmap on states (Slice 4).

---

## Phase 10 — Stepping + breakpoints (both hosts)

### Functional acceptance — BTree

- F10-01. Clicking the gutter to the left of a BTree node toggles a breakpoint marker. (TASK-BT-S3-01)
- F10-02. When the kernel reaches a breakpoint-set node during execution on the selected entity, the instance pauses; the editor's pause indicator activates; the inspector shows the paused-at element. (TASK-BT-S3-01, BT-S3-02)
- F10-03. Continue resumes execution. (TASK-BT-S3-02)
- F10-04. Step Into / Step Over / Step Out advance the kernel one logical unit at a time per the semantics in BTH §12.2. (TASK-BT-S3-02)
- F10-05. The subtree-boundary renderer draws a faint blue dashed rectangle when the kernel is inside a subtree (`StackPointer > 0`). (TASK-BT-S3-03)
- F10-06. The trace timeline's `bt.async` lane shows issued / resolved / aborted async events with phase-coloring. (TASK-BT-S3-04)

### Functional acceptance — HSM

- F10-07. Clicking the gutter to the left of an HSM state header toggles a state breakpoint. (TASK-HS-S3-01)
- F10-08. Clicking a small dot affordance on a transition label toggles a transition breakpoint. (TASK-HS-S3-01)
- F10-09. State / transition / region / event breakpoints fire correctly when their condition is met; instance pauses; editor highlights the paused-at element. (TASK-HS-S3-01, HS-S3-02)
- F10-10. Step Into / Step Over / Step Out semantics per HSH §13.2 — Step Into processes the next event from queue; Step Over advances one microstep; Step Out runs to RTC quiescence. (TASK-HS-S3-02)
- F10-11. The `hsm.region_conflicts` renderer draws yellow lines + ⚠ glyphs between conflicting states across regions when validation reports conflicts. Clicking the glyph opens the popup explaining the conflict. (TASK-HS-S3-03)
- F10-12. The `hsm.history_glyphs` renderer draws H / H\* / ⊙ glyphs for history shallow / history deep / final states. These glyphs are selectable like normal states. (TASK-HS-S3-04)

### Quality acceptance

- Q10-01. Breakpoint hit → editor visible pause indicator ≤ 100 ms.
- Q10-02. Step operations advance kernel state without dropping events from the queue.
- Q10-03. Clearing all breakpoints returns the kernel to zero-overhead state for breakpoint checks (verify via perf counter).
- Q10-04. Glyph rendering and selection work correctly at all zoom levels including < 0.5×.

### Out of scope (Slice 3)

- Conditional breakpoints (only "break when reached").
- Watch expressions on blackboard fields.
- Reverse execution.
- Cross-asset coordinated stepping.

---

## Phase 11 — Multi-instance + polish

### Functional acceptance — multi-instance

- F11-01. BTree heatmap mode tints nodes by aggregate entry frequency across all entities running the asset. (TASK-BT-S4-01, BT-S4-02)
- F11-02. HSM heatmap mode tints states by aggregate entry frequency. (TASK-HS-S4-01, HS-S4-02)
- F11-03. Asset Browser shows live-instance count per asset; refreshed every 500 ms. (TASK-S11-01)

### Functional acceptance — refactor surfaces

- F11-04. Full event-rename UX (machine-scoped) ships from the Events table. (TASK-S11-02)
- F11-05. Cross-host action rename (BTree + HSM + Blueprint) works from any inspector field. (TASK-S11-02)
- F11-06. Asset rename (file + class + AssetId attribute argument) works; transitions/subtree references update across all dependents. (TASK-S11-02)

### Functional acceptance — polish

- F11-07. Reset-layout toolbar action re-runs auto-layout on the active asset, overwriting stored positions. (TASK-S11-03)
- F11-08. Comments on transitions and regions persist in the layout method and render as tooltips. (TASK-S11-04)
- F11-09. Drag-to-create-transition snaps to state edges and previews the connection during drag. (TASK-S11-05)
- F11-10. Diagnostics aggregation window shows all diagnostics across all open assets, grouped by severity. (TASK-S11-06)

### Quality acceptance

- Q11-01. Heatmap aggregation overhead ≤ 0.5 ms per frame per attached asset.
- Q11-02. Asset-Browser live-count refresh does not cause frame hitches.
- Q11-03. Cross-asset rename of an action with 50+ references completes in under 2 seconds (editor-side).

### Out of scope (v1 overall)

- AI-assisted refactor suggestions.
- Coordinated multi-asset debugging (single asset / single entity remains the v1 focus).
- Asset-version history within the editor (use git).
- Collaborative editing (single-user editor).
- Mobile/web editor.

---
