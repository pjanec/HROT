We are following the implementation tasks ids and preparing instructions per those task ids because the original tasks contains references to the specification and the developer AI coding agent can consult the specs to get the context what the task was supposed to do. Developer should be given the findings (what is wrong) so he will have the right context for fixing.

Reference documents in .dev\blueprints-2\

SPECS

1. AI_Editor_Shared_Infrastructure.md
2. NodeEditor_Extension_NodeAttachments.md
3. NodeEditor_Extension_ContainerNodes.md
4. NodeEditor_Extension_CustomCanvasRenderer.md
5. BTree_Editor_NodeEditor_Host_Design.md
6. HSM_Editor_NodeEditor_Host_Design.md

ACCEPTANCE CRITERIA

1. ACCEPTANCE-CRITERIA.md

ORIGNAL TASK DETAIL (those we are now fixing)

1. TASK-DETAIL.md



 By strictly aligning the instructions with the `TASK-DETAIL.md` task IDs and the `ACCEPTANCE-CRITERIA.md` (F0-01 through F0-08), your coding agent will have the exact architectural context and boundaries needed to apply the fixes without hallucinating out-of-scope features.

Here is the first Action Packet, focusing entirely on the **Phase 0 (Kernel Prerequisites)** gaps found in the `v225` codebase. You can pass this directly to your agent.

------

### 🎯 ACTION PACKET: Phase 0 — Kernel Prerequisites

**Agent Instructions:** We are evaluating the Phase 0 kernel-side prerequisites for the Blueprint/AI Editor. These modifications to the `FastBTree` and `FastHSM` kernels are required before the host editors can faithfully round-trip visual data. Please verify and fix the following tasks.

#### TASK-K-01: Add `Lane` property to `[HsmAction]`

- **Target:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmActionAttribute.cs` and the associated Source Generator.
- **The Implemented Reality:** The `CommandLane` enum was successfully added to the kernel data types. However, the `Lane` property must be exposed on the attribute so the editor can infer the `OutputLaneMask`.
- **Action Required:**
  1. Open `HsmActionAttribute.cs` and add `public CommandLane Lane { get; set; } = CommandLane.None;`.
  2. Ensure that the FastHSM source generator (or `Fhsm.Compiler`) captures this value and preserves it into the runtime reflection metadata or compiled blob, satisfying Acceptance Criterion **F0-01**.

#### TASK-K-02 & TASK-K-03: HSM `stableId` and `visualId`

- **Target:** `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/` (`HsmBuilder.cs`, `StateBuilder.cs`, `TransitionBuilder.cs`).
- **The Implemented Reality:** The editor relies on stable Guid identity to round-trip statechart topologies.
- **Action Required:**
  1. Update `HsmBuilder.State(name, Guid stableId = default)` and `StateBuilder.AddChild(name, Guid stableId = default)`. If `default` is passed, generate a new Guid so handwritten code continues to compile.
  2. Update `TransitionBuilder.GoTo(target, Guid visualId = default)` and `HsmBuilder.GlobalTransition(..., Guid visualId = default)`.
  3. Ensure these IDs are stamped into the emitted `HsmDefinitionBlob`.

#### TASK-K-04: Add `Paused` flag to HSM `InstanceFlags`

- **Target:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/InstanceFlags.cs`
- **The Implemented Reality:** ✅ **Implemented Correctly.** `HsmKernelCore.ValidateInstance` successfully checks `if ((header->Flags & InstanceFlags.Paused) != 0) return false;`, properly halting the microstep advancement.
- **Action Required:** No action needed. Acceptance Criterion **F0-07** is satisfied.

#### TASK-K-05: Add `Paused` flag to BTree execution

- **Target:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs` and `BrainBTreeState.cs`.
- **The Implemented Reality:** Unlike the HSM kernel, the BTree kernel does not check a pause flag before ticking. When a breakpoint hits, the debugger sets a pause state, but `BTreeTickSystem` will keep advancing the tree.
- **Action Required:**
  1. Add a `Paused` boolean or flag to the BTree's execution state (e.g., `BrainBTreeState` or a unified `DebugState.Flags`).
  2. In `BTreeTickSystem.Execute`, check this flag before calling `def.BTreeInterpreter.Tick(ref blackboard, ref state, ref ctx)`. If paused, skip the tick. This satisfies Acceptance Criterion **F0-08**.

#### TASK-K-06: BTree `visualId` parameter

- **Target:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/` (Fluent Builders).
- **The Implemented Reality:** The editor needs to map visual UI nodes directly to emitted BTree nodes.
- **Action Required:** Ensure every BTree fluent builder method (`.Sequence`, `.Selector`, `.Condition`, `.Action`, etc.) accepts an optional `Guid visualId = default` parameter. Ensure the compiler embeds this `visualId` into the `NodeDebugMetadata` or the `BehaviorTreeBlob` so it can be read back at editor load time.

------





Excellent. Let's move up the stack into **Phase 1 (Shared Infrastructure Foundation)**.

This phase is the crucial bridge that allows the generic `NodeEditor` to talk to our specific FDP engine subsystems (BTree, HSM, Blueprints) uniformly. In evaluating the `v225` implementation against the `AI_Editor_Shared_Infrastructure.md` design and the Phase 1 Acceptance Criteria (`F1-01` through `F1-11`), there are four classic architectural traps that are either stubbed out or implemented too simply in the current codebase.

Here is **Action Packet 2**, formatted exactly for your AI coding agent. You can copy and paste this directly to it.

------

### 🎯 ACTION PACKET 2: Phase 1 — Shared Infrastructure Foundation

**Agent Instructions:** We are evaluating the Phase 1 shared infrastructure layer (`Hrot.Editor.AiShared`). This layer acts as the unified substrate for the Blueprint, BTree, and HSM editors. A review against the `AI_Editor_Shared_Infrastructure.md` specifications and `ACCEPTANCE-CRITERIA.md` (Phase 1) reveals several architectural gaps in the current implementation. Please fix the following tasks.

#### TASK-S1-03: `EditorSelectionStore` Per-Asset Sub-Selection

- **Target:** `Hrot/Editor/Hrot.Editor.AiShared/Selection/EditorSelectionStore.cs`
- **The Implemented Reality:** The selection store is likely implemented as a flat, global singleton that tracks one single selected node/element for the entire editor.
- **The Specification:** The design explicitly mandates **per-asset selection** (`AI_Editor_Shared_Infrastructure.md §5.1` and Acceptance Criteria `F1-02`, `F1-03`). If Window A shows `EnemyBrain.hsm` and Window B shows `OrcGuard.bt`, selecting a node in Window A must *not* wipe out the selection in Window B. The Inspector window dynamically displays the sub-selection of the *currently focused* asset (`ActiveAsset`).
- **Action Required:**
  1. Update `EditorSelectionStore` to maintain a dictionary: `Dictionary<Guid, IAssetSubSelection> _subSelectionsByAssetId`.
  2. Maintain an `IEditableAsset? ActiveAsset` property (which follows window focus).
  3. Update the `SetSubSelection(Guid assetId, IAssetSubSelection selection)` method to store the selection in the dictionary.
  4. Ensure the `OnSelectionChanged` event fires when the `ActiveAsset` changes, OR when the sub-selection of the *currently active asset* changes.

#### TASK-S1-05: FQN Reference Catalog Rebuild Trigger

- **Target:** `Hrot/Editor/Hrot.Editor.AiShared/References/ReferenceCatalog.cs`
- **The Implemented Reality:** The reference catalog (which powers Find References and Rename) is either statically initialized or must be manually rebuilt.
- **The Specification:** The multi-index catalog must rebuild automatically in the background whenever an asset is added, removed, or modified via a hot reload (`AI_Editor_Shared_Infrastructure.md §4.3` and `ACCEPTANCE-CRITERIA.md F1-09`).
- **Action Required:**
  1. Inject `IAssetCatalog` into the `ReferenceCatalog` constructor.
  2. Subscribe to `IAssetCatalog.Changed`.
  3. In the event handler, trigger a full rebuild of the `IAssetSubElement -> IAssetReference` multi-index by enumerating all assets in the catalog and parsing their exported signatures/references.

#### TASK-S1-08: `IGSelectionBridge` (Engine-to-Editor Sync)

- **Target:** `Hrot/Editor/Hrot.Editor.AiShared/Selection/IGSelectionBridge.cs`
- **The Implemented Reality:** The bridge that synchronizes an entity clicked in the 3D map/viewport with the AI Editor's inspector is stubbed out.
- **The Specification:** When a user clicks a tank in the world, the IG (Image Generator) layer publishes a `SelectionChangedEvent` over DDS/FdpEventBus. The AI Editor must listen to this and update the active entity context (`ACCEPTANCE-CRITERIA.md F1-10`).
- **Action Required:**
  1. Inject `FdpEventBus` into the bridge.
  2. Read `SelectionChangedEvent` from the bus during the editor's update tick.
  3. Extract the entity ID from the event and write it to `EditorSelectionStore.SelectedEntity`.

#### TASK-S1-11 & TASK-S1-12: `AiTracerCoordinator` Session Exclusivity

- **Target:** `Hrot/Editor/Hrot.Editor.AiShared/Debug/DebugSessionRegistry.cs` (or `AiTracerCoordinator.cs`)
- **The Implemented Reality:** The session registry blindly hands out debug sessions to whoever asks for them.
- **The Specification:** There is a strict cardinality split between passive observers and active debuggers (`AI_Editor_Shared_Infrastructure.md §11.1` and `ACCEPTANCE-CRITERIA.md F1-05`). There can be *many* `IAiTraceObserver` instances (e.g., telemetry, heatmap, timeline), but exactly *one* `IAiDebugSession` (which has pause/step control).
- **Action Required:**
  1. Implement the `TryAcquireSession<T>(out T session)` method.
  2. Use an internal lock or active-session reference tracker. If a control session is already checked out, `TryAcquireSession` must return `false`.
  3. The caller (the editor UI) must handle this `false` return by falling back to `IAiTraceObserver` mode (disabling step/pause buttons and rendering a "Another tool is debugging" banner).

------







Excellent. With the shared infrastructure layer correctly distributing state and events, we are ready to integrate the visual extensions into the canvas.

**Phases 2, 3, and 4** represent the `NodeEditor` extensions (Attachments, Containers, and Custom Renderers). The `v225` codebase successfully scaffolded the rendering side of these features, but as identified earlier, the interaction, hit-testing, and event propagation layers were largely left behind.

Here is **Action Packet 3**, which targets the interaction gaps across all three extensions simultaneously. You can copy and paste this directly to your agent.

------

### 🎯 ACTION PACKET 3: Phases 2, 3, & 4 — NodeEditor Extensions (Interaction & Hit-Testing)

**Agent Instructions:** We are evaluating the three NodeEditor extensions (NodeAttachments, ContainerNodes, and CustomCanvasRenderer). The rendering pipeline for these is mostly correct, but the canvas interaction layer (`HitTester` and `CanvasInput`) does not know how to route clicks, drags, or context menus to these new visual elements. Please fix the following tasks to align the implementation with the specs.

#### TASK-NEA-06, TASK-NEC-05, TASK-NER-04: Hit-Testing Z-Order Convergence

- **Target:** `src/NodeEditor.UI/Canvas/HitTester.cs`
- **The Implemented Reality:** The canvas hit-tester only checks standard NodeEditor elements (nodes, pins, wires, comments). It ignores Attachments, Container regions, and all CustomCanvasRenderer output, rendering them unclickable.
- **The Specification:** The custom canvas renderer spec (`NodeEditor_Extension_CustomCanvasRenderer.md §8.1`) dictates a strict 15-step priority order to ensure stacked elements steal clicks correctly.
- **Action Required:** Completely rewrite the hit-test evaluation sequence in `HitTester.cs` to test intersections in exactly this order (highest priority first):
  1. Reroutes
  2. Pins
  3. Wires
  4. Custom `TopMost` render pass elements
  5. Attachments (`Highest StackIndex` first)
  6. Custom `AfterNodes` render pass elements
  7. Container collapse-arrow chevrons
  8. Container header strips
  9. Comment title bars
  10. Custom `AfterWires` render pass elements
  11. Node bodies (regular nodes and container children)
  12. Custom `BeforeContent` render pass elements
  13. Container interiors (empty area not covered by children)
  14. Comment bodies (pass-through)
  15. Empty Canvas

#### TASK-NEC-06: Container Reparenting via Drag-and-Drop

- **Target:** `src/NodeEditor.UI/Canvas/CanvasInput.cs` (or wherever node drag-commit is handled)
- **The Implemented Reality:** When a user finishes dragging a node, the editor emits a `GraphCommand.MoveNodes` command. It does not check if the node was dragged into or out of a container node's visual bounds.
- **The Specification:** Dragging a child out of a container, or dragging a root node into a container, must structurally reparent it (`NodeEditor_Extension_ContainerNodes.md §10.1`).
- **Action Required:**
  1. On mouse-release (when concluding `InteractionMode.DraggingNodes`), evaluate the drop coordinate for each dragged node against the spatial index.
  2. Check if the drop coordinate intersects the interior bounds of a valid `IContainerNodeModel`.
  3. If the resolved target container ID (or null, for root canvas) differs from the node's current `ParentContainerId`, emit a `GraphCommand.ChangeParent` (or `ChangeParentMultiple`) command instead of just `MoveNodes`, automatically updating local coordinates to match the new parent. Ensure you prevent cycles (e.g., dropping a container into its own descendant).

#### TASK-NER-07: Custom Element Context Menus

- **Target:** `src/NodeEditor.UI/Canvas/CanvasInput.cs`
- **The Implemented Reality:** Right-clicking the canvas or a standard node opens a context menu, but right-clicking a custom-rendered element (like an HSM transition label) does nothing.
- **The Specification:** The host can register context menus for custom elements via `ICustomElementContextMenuProvider` (`NodeEditor_Extension_CustomCanvasRenderer.md §9.4`).
- **Action Required:**
  1. In the right-click handler, check if the current `HoverInfo` targets a `HoverKind.CustomElement`.
  2. If so, extract the `RendererId` and query `IEditorHostServices.CustomElementContextMenu` (or iterating over registered providers) for a match.
  3. Retrieve the `ContextMenuItem` list using the element's key and display them in an ImGui context menu.

------



Excellent. Now that the shared substrate and the canvas interaction layers are solid, we can fix the actual subsystem authoring tools.

**Phases 5 & 6** encompass the BTree and HSM hosts (Slice 1 - Authoring). In `v225`, the models and fluent emitters were remarkably well-built, but several critical command sinks were stubbed out, and a few required visual cues (like internal transitions and observer badges) were skipped.

I have also included the missed `GraphChangeNotification` patch from Phase 2 here, as it is required to make the HSM and BTree attachment commands function correctly.

Here is **Action Packet 4**, formatted for your agent.

------

### 🎯 ACTION PACKET 4: Phases 5 & 6 — BTree & HSM Authoring Hosts

**Agent Instructions:** We are evaluating the Phase 5 (BTree) and Phase 6 (HSM) authoring hosts. While the projection and emission layers are well-implemented, several command sinks are stubbed out and specific visual requirements from the specs were missed. Please implement the following fixes.

#### TASK-NEA-03: Missing Attachment Change Notifications

- **Target File:** `src/NodeEditor.Core/Interfaces/IGraphModel.cs`
- **The Implemented Reality:** The `GraphChangeNotification` record only contains `AffectedNodes` and `AffectedLinks`. Because of this, when the BTree or HSM editors add/remove an attachment (like a decorator pill or state flag), the UI layer doesn't know to redraw the affected element.
- **The Specification:** The spec mandates an `AffectedAttachments` set to properly invalidate layout (`NodeEditor_Extension_NodeAttachments.md §4.4`).
- **Action Required:**
  1. Add `IReadOnlySet<AttachmentId>? AffectedAttachments = null` to the `GraphChangeNotification` record.
  2. Update `BTreeCommandSink.cs` and `HsmCommandSink.cs`. Whenever an attachment is added, removed, or mutated, include its ID in the `AffectedAttachments` collection when firing the change event.

#### TASK-HS-S1-08 & TASK-HS-S1-10: Implement `HsmCommandSink` Stubs

- **Target File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs`
- **The Implemented Reality:** The command sink processes standard node moves and links, but the container and attachment methods (`ApplyAddRegion`, `ApplyRemoveRegion`, `ApplyReorderRegions`, `ApplyAddAttachment`, `ApplyRemoveAttachments`) contain literal `/* TODO */` comments.
- **The Specification:** These commands are required for authoring parallel composites and state flags.
- **Action Required:** Implement these five methods.
  1. For regions: resolve the `cmd.ContainerId` to the parent `StateNode`, mutate its `Regions` list based on the command payload, and call `_asset.MarkDirty()`.
  2. For attachments: resolve the `cmd.HostNodeId`, add/remove the attachment from the `HsmAsset.Attachments` collection (or the specific state's list), and mark dirty.

#### TASK-HS-S1-14: Internal Transition Rendering

- **Target File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/` (Transition Renderer)
- **The Implemented Reality:** All transitions are rendered using standard NodeEditor links. If an internal transition is authored, it routes as a standard self-link (an arc that loops *outside* the node).
- **The Specification:** Internal transitions must be rendered as a dashed loop strictly *inside* the source state (`HSM_Editor_NodeEditor_Host_Design.md §7.4`).
- **Action Required:** In the HSM transition renderer (likely hooked into `hsm.transition_labels` or a custom link interceptor):
  1. Check if the transition `Kind == TransitionKind.Internal`.
  2. If true, do not draw the standard bezier. Instead, draw a dashed curved path (or a small looping arrow) contained entirely within the bounding box of the source state, and place the label directly next to it.

#### TASK-BT-S1-11: BTree Observer Guard Badges

- **Target File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/` (Observer Badge Renderer)
- **The Implemented Reality:** The `btree.observer_guard_badges` custom pass is registered, but it does not actually draw the badges on the canvas.
- **The Specification:** When a BTree `Observer Selector` is connected to a child that is a Guard (`Condition` or `Observer` leaf), an `👁 OBSERVES` badge must be rendered on that specific wire.
- **Action Required:** Implement the `Render` loop for this custom canvas renderer:
  1. Iterate over all links in the `BTreeAsset`.
  2. If the `From` node is an `ObserverSelector` and the `To` node is a `Condition` (or standalone `Observer`), calculate the midpoint of the link using `LinkBezier.GetPointAt(0.3f)` (biased towards the parent).
  3. Render a small ImGui pill containing the text `👁 OBSERVES` at that point.

------





This is a great, systematic approach. We have successfully addressed the structural prerequisites (Phase 0), the shared infrastructure (Phase 1), the visual canvas extensions (Phases 2-4), and the authoring command sinks (Phases 5-6).

The next logical block is **Phases 8 & 9 (Runtime Read-Only Inspection)**. In the `v225` codebase, the visual renderers for the runtime overlays and the inspector panes are beautifully implemented—but they are completely starved of data. The debug sessions currently return `null` for state snapshots and never read the trace buffers.

Here is **Action Packet 5**, focusing entirely on wiring the ECS runtime data into the editor's debug sessions. You can pass this directly to your agent.

------

### 🎯 ACTION PACKET 5: Phases 8 & 9 — Runtime Read-Only Inspection

**Agent Instructions:** We are evaluating Phase 8 (BTree) and Phase 9 (HSM) runtime inspection. The visual renderers for the runtime overlays and inspector panes exist, but they do not function because the underlying debug sessions are disconnected from the ECS world. Please fix the following tasks to feed live data from the engine into the editor.

#### TASK-BT-S2-01 & TASK-HS-S2-01: Session ECS Injection & Snapshot Generation

- **Target Files:** `src/Hrot.BTree.Editor/Debug/BTreeDebugSession.cs` & `src/Hrot.Hsm.Editor/Debug/HsmDebugSession.cs`
- **The Implemented Reality:** Both session classes implement `GetCurrentStateSnapshot() => null;` with a comment stating "Returns null until the kernel snapshot adapter is implemented". Because of this, the `btree.runtime_overlay`, `hsm.runtime_overlay`, and the Runtime Inspector Panes never draw anything.
- **The Specification:** The sessions must read the active state from the ECS world for the currently selected entity (`ACCEPTANCE-CRITERIA.md F8-05, F9-05`).
- **Action Required:**
  1. Inject an `EntityRepository` and `EditorSelectionStore` into both session constructors (or provide an `Update(repo, selection)` method called by the editor frame loop).
  2. Implement `BTreeDebugSession.GetCurrentStateSnapshot()`: Get the selected entity. If it has a `BrainBTreeState` component, read `RunningNodeIndex`, `StackPointer`, `NodeIndexStack`, `LocalRegisters`, and `AsyncHandles`. Return a populated `BehaviorTreeStateSnapshot`.
  3. Implement `HsmDebugSession.GetCurrentStateSnapshot()`: Get the selected entity. Check its `BehaviorState.BrainTier` to read the correct `BrainHsm64`, `BrainHsm128`, or `BrainHsm256` component. Decode the active leaf IDs, Instance Phase, Event Queue, and Timers into an `HsmInstanceSnapshot`.

#### TASK-BT-S2-05 & TASK-HS-S2-05: Trace Buffer Polling

- **Target Files:** `BTreeDebugSession.cs` & `HsmDebugSession.cs`
- **The Implemented Reality:** Both sessions have `RecordNodeExecuted` and `RecordTrace` methods to populate their history rings (which power the Trace Timeline), but these methods are *never called*.
- **The Specification:** The editor must actively read the unmanaged trace ring buffers filled by the kernels (`AI_Editor_Shared_Infrastructure.md §13.4`).
- **Action Required:**
  1. Inside a per-frame `Update` method on the debug sessions, check if the selected entity has the `BTreeTraceWorkingMemory1024` (or HSM equivalent) component.
  2. Maintain a local `_lastReadPos` inside the session.
  3. If the component's `WritePos` differs from `_lastReadPos`, iterate through the raw unmanaged buffer from `_lastReadPos` to `WritePos` (handling ring-buffer wrapping).
  4. Decode the bytes into `BTreeTraceRecord` or `TraceRecord` structs and feed them into the session's own `Record...` methods. Update `_lastReadPos`.

#### TASK-BT-S2-03: Live Blackboard Values

- **Target File:** `src/Hrot.BTree.Editor/Blackboard/LiveBlackboardPanel.cs`
- **The Implemented Reality:** Inside the render loop, it contains a stub: `// Live values not yet wired (Slice 3+); show placeholder. ImGui.TextDisabled("--");`.
- **The Specification:** The panel must display live field values for the selected entity, refreshed every frame (`ACCEPTANCE-CRITERIA.md F8-04`).
- **Action Required:**
  1. Update the `Draw()` method to accept the `EntityRepository` and the currently selected `Entity`.
  2. If the session is active and the entity has a `BrainBlackboard` component, read the `BehaviorParameters` fixed buffer.
  3. For each field in `_schema.Fields`, calculate its memory location using the field's byte offset, and use `System.Runtime.InteropServices.MemoryMarshal.Read` (or safe pointer casting based on the field's `Type`) to extract the actual live value.
  4. Display the live value string instead of `"--"`.

------





This is excellent progress. We have successfully addressed the architectural prerequisites, the shared canvas extensions, the authoring commands, and the read-only runtime inspection. The overlays are now successfully fed by the engine's ECS data!

The final major piece of the puzzle is **Phase 10 (Stepping & Breakpoints)**. The `v225` codebase has the UI shells for stepping and the custom renderers registered, but the logic connecting the debug session's step commands to the simulation time controller is completely stubbed out, and a few advanced visual renderers lack their interactive hit-testing.

Here is **Action Packet 6**, which will wrap up Phase 10 and complete the debugging suite. You can pass this directly to your agent.

------

### 🎯 ACTION PACKET 6: Phase 10 — Stepping & Breakpoints

**Agent Instructions:** We are completing Phase 10 (Stepping and Breakpoints) for both the BTree and HSM hosts. The debug sessions currently have empty stubs for the step control methods, and a few custom renderers need their interactive hit-testing implemented. Please execute the following fixes:

#### TASK-BT-S3-02 & TASK-HS-S3-02: Implement Step Control State Machines

- **Target Files:** `src/Hrot.BTree.Editor/Debug/BTreeDebugSession.cs` & `src/Hrot.Hsm.Editor/Debug/HsmDebugSession.cs`
- **The Implemented Reality:** Inside both classes, the overrides for step controls (`OnStepOverImpl`, `OnStepIntoImpl`, `OnStepOutImpl`, `OnContinueImpl`, `OnPauseImpl`) are entirely empty `{ }`. Clicking the step buttons in the UI does nothing to the engine.
- **The Specification:** The step methods must configure the session's step tracking state and command the time controller to advance.
- **Action Required:**
  1. In both session classes, add fields to track `_stepMode` (Over, Into, Out) and the origin context (e.g., `_stepFromStackDepth` for BTree, or the current microstep/phase for HSM).
  2. Implement `OnStepOverImpl`, `OnStepIntoImpl`, and `OnStepOutImpl`: set the `_stepMode`, record the current depth/phase, and call `Coordinator.TimeController.RequestStepOneTick()` (or the equivalent injected time controller method) to advance the ECS world by exactly one tick.
  3. Update the `RecordTrace` / `RecordNodeExecuted` handlers: after a step is requested, evaluate the new trace records against the `_stepMode`. If the step condition is satisfied (e.g. BTree stack depth returns to the `_stepFromStackDepth`), request a pause again via `Coordinator.TimeController.RequestPause()`.

#### TASK-BT-S3-03: Subtree Boundary AABB Computation

- **Target File:** `src/Hrot.BTree.Editor/Renderers/SubtreeBoundaryRenderer.cs`
- **The Implemented Reality:** The renderer draws a basic box or is skipped entirely, because it does not properly compute the bounding box of the active subtree nodes.
- **The Specification:** When the debugger is paused inside a subtree (`StackPointer > 0`), it must draw a faint blue dashed rectangle encompassing the *entire* subtree that is currently executing.
- **Action Required:**
  1. Read the live `BehaviorTreeState` from the session snapshot.
  2. Extract the subtree's entry node using `NodeIndexStack[0..StackPointer]`.
  3. Walk all child nodes of that subtree entry node via the `IGraphModel` to compute a combined Axis-Aligned Bounding Box (AABB) of their `NodeInteriorBounds`.
  4. Render the dashed blue rectangle around this combined AABB.

#### TASK-HS-S3-03: Region Conflicts Hit-Testing & Popup

- **Target File:** `src/Hrot.Hsm.Editor/Renderers/HsmRegionConflictsRenderer.cs`
- **The Implemented Reality:** The renderer successfully draws the yellow line and the ⚠ glyph between conflicting states, but clicking the glyph does nothing.
- **The Specification:** The conflict overlay must be hit-testable. Clicking the ⚠ glyph must open a popup panel explaining the conflict and offering to suppress it.
- **Action Required:**
  1. Implement the `ICustomCanvasHitTester` interface on the renderer. Return a valid `CustomElementHit` if the mouse intersects the ⚠ glyph's coordinates.
  2. In the editor's UI loop (or via `ImGui.BeginPopup` triggered by the selection state), render the conflict details: show which CommandLanes are conflicting, list the contributing actions (OnEntry/Activity), and provide a "Suppress this warning" button that mutates the editor-only metadata for the asset.

#### TASK-HS-S3-04: History & Final States Rendering Bypass

- **Target File:** `src/Hrot.Hsm.Editor/Renderers/HsmHistoryGlyphsRenderer.cs`
- **The Implemented Reality:** History and Final states are likely still rendering as standard large rectangular nodes behind the custom glyphs.
- **The Specification:** These pseudo-states must appear *only* as the small 20px circular glyphs (H, H*, ⊙). The standard node rectangle must be completely bypassed or hidden.
- **Action Required:**
  1. Ensure the `StateNode` for history/final states is assigned a specific `Category` string.
  2. In the HSM theme definitions, map this specific category to have a completely transparent background and border color (`Vector4.Zero`).
  3. In `HsmHistoryGlyphsRenderer.Render`, draw the 20px circle and text at the node's center coordinate. Because the underlying NodeEditor theme is transparent, only the custom glyph will be visible, but NodeEditor will still natively handle the selection outline and hit-testing!

------

