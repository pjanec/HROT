# Task List

Tasks are numbered in execution order. Each is sized for ~200–400 LOC of agent output. Dependencies and authoritative spec references are listed per task. Complete one fully (with tests) before starting the next, except where explicitly marked parallelizable.

Legend:

- **Spec refs**: which spec sections to read before starting.
- **Kernel refs**: which kernel files provide reference code or interfaces.
- **Outputs**: files to create or modify.
- **Verification**: how to confirm done.
- **Tests**: what test fixtures to create.

---

## TASK-01 — Solution scaffolding

Create the .NET 8 solution with all six projects, project references, and central package management. No code yet — just the empty scaffold that builds.

**Spec refs**: `03_implementation_plan/project_structure.md` (all of it).

**Kernel refs**: none.

**Outputs**:

- `NodeEditor.sln`
- `Directory.Build.props`
- `Directory.Packages.props`
- `.editorconfig`
- `src/NodeEditor.Primitives/NodeEditor.Primitives.csproj`
- `src/NodeEditor.Core/NodeEditor.Core.csproj`
- `src/NodeEditor.UI/NodeEditor.UI.csproj`
- `src/NodeEditor.Demo/NodeEditor.Demo.csproj`
- `tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj`
- `tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj`
- Empty `Program.cs` in Demo (with a `Hello, world` Console.WriteLine).
- One placeholder file per project so it compiles (e.g., `AssemblyInfo.cs` or a `public class Placeholder {}` in each).

**Verification**:

```sh
dotnet restore
dotnet build --configuration Release
dotnet test
```

All should succeed with no warnings. `dotnet run --project src/NodeEditor.Demo` prints "Hello, world."

**Tests**: none yet (empty test projects fine; xUnit discovers nothing, exits 0).

---

## TASK-02 — Primitives (identity, geometry, input)

Implement everything in `NodeEditor.Primitives`. Identity types, key types, Rect2, BezierUtil.

**Spec refs**: `02_architecture/data_model.md` §"Identity types".

**Kernel refs**: `04_kernel_code/K01_primitives.md` — code is fully written; you copy it into the right files.

**Outputs**:

- Every file in `src/NodeEditor.Primitives/` per `project_structure.md` "Folder structure".

**Verification**: `dotnet build` succeeds with zero warnings. Identity types are immutable record structs, all properties XML-documented.

**Tests**:

- `tests/NodeEditor.Core.Tests/Primitives/NodeIdTests.cs`: equality, hash code, default value behavior.
- `tests/NodeEditor.Core.Tests/Primitives/Rect2Tests.cs`: intersection, containment, union, expansion.

---

## TASK-03 — Host contract interfaces

Implement all host-contract interfaces. No logic, just the contracts.

**Spec refs**: `02_architecture/interfaces_host_contract.md` (full).

**Kernel refs**: `04_kernel_code/K02_host_interfaces.md` (code is written; copy in).

**Outputs**:

- All interfaces in `src/NodeEditor.Core/Hosting/` per `project_structure.md`.
- Supporting records (`Diagnostic`, `LinkValidationResult`, `PinDefaultMetadata`, `NodeCatalogEntry`, `GraphChangeEvent`).
- `GraphCommand` discriminated union in `src/NodeEditor.Core/Commands/GraphCommand.cs`.
- `CommandResult` in `src/NodeEditor.Core/Commands/CommandResult.cs`.

**Verification**: `dotnet build` clean. All public types have XML doc summaries.

**Tests**: minimal — just compile-time checks (`tests/NodeEditor.Core.Tests/Hosting/LinkValidationResultTests.cs` for record equality, etc.).

---

## TASK-04 — `FakeHost` test utility

Build a reusable fake implementation of the entire host contract, for use in all subsequent Core tests. In-memory only, no persistence.

**Spec refs**: `02_architecture/interfaces_host_contract.md`, `02_architecture/command_pipeline.md`.

**Kernel refs**: none — original work.

**Outputs**:

- `tests/NodeEditor.Core.Tests/TestUtilities/FakeHost.cs`
- `tests/NodeEditor.Core.Tests/TestUtilities/FakeGraphModel.cs`
- `tests/NodeEditor.Core.Tests/TestUtilities/FakeNodeCatalog.cs`
- `tests/NodeEditor.Core.Tests/TestUtilities/FakeTypeSystem.cs`
- `tests/NodeEditor.Core.Tests/TestUtilities/FakeLinkValidator.cs` (Unreal-style rules from `A_canvas_interactions.md §A.6`)
- `tests/NodeEditor.Core.Tests/TestUtilities/FakeCommandSink.cs` (computes inverses, applies to FakeGraphModel)
- `tests/NodeEditor.Core.Tests/TestUtilities/FakeHostServices.cs`
- `tests/NodeEditor.Core.Tests/TestUtilities/FakeInputSource.cs` (scriptable input for interaction tests)

**Verification**: build clean. The FakeGraphModel correctly applies AddNode, RemoveNodes, AddLink, RemoveLinks, MoveNodes commands.

**Tests**:

- `tests/NodeEditor.Core.Tests/TestUtilities/FakeHostTests.cs`: validates basic operations on the fake. Future tests rely on this.

---

## TASK-05 — `ViewportState`, `SelectionState`, `DragOverrideMap`

Pure data structures with simple invariants. No animation logic yet (just `Set`); animation arrives in TASK-09.

**Spec refs**: `02_architecture/data_model.md` "ViewportState", "SelectionState", "DragOverrideMap".

**Kernel refs**: none — original work.

**Outputs**:

- `src/NodeEditor.Core/View/ViewportState.cs`
- `src/NodeEditor.Core/View/SelectionState.cs`
- `src/NodeEditor.Core/View/DragOverrideMap.cs`

**Verification**: all properties immutable except where they should mutate; `Changed` events fire on selection mutation; canvas/screen transform math correct.

**Tests**:

- `tests/NodeEditor.Core.Tests/View/ViewportStateTests.cs`: CanvasToScreen and ScreenToCanvas roundtrip. VisibleCanvasRect computed correctly.
- `tests/NodeEditor.Core.Tests/View/SelectionStateTests.cs`: Add, Remove, Toggle, Clear, Primary tracking. Set fires Changed exactly once.
- `tests/NodeEditor.Core.Tests/View/DragOverrideMapTests.cs`: Set/Get/Clear, HasOverride, no allocation for empty.

---

## TASK-06 — `FuzzyMatcher`

The picker's matching brain. Pure algorithm, no UI dependency. Includes match-position output for highlighting.

**Spec refs**: `01_interaction_specs/C_generic_picker.md` §C.4 "Matching algorithm".

**Kernel refs**: `04_kernel_code/K04_fuzzy_matcher.md` — code is written; copy in. The kernel file already implements the 7-tier algorithm with score and matched-position output.

**Outputs**:

- `src/NodeEditor.Core/Search/FuzzyMatcher.cs`
- `src/NodeEditor.Core/Search/MatchResult.cs`

**Verification**: matches the kernel reference exactly. All tier scoring functions present.

**Tests**:

- `tests/NodeEditor.Core.Tests/Search/FuzzyMatcherTests.cs`: each tier (exact, prefix, word-start, camel-case, substring, keyword, fuzzy) exercised with at least 2 cases. Matched positions verified for highlighting.
- `tests/NodeEditor.Core.Tests/Search/FuzzyMatcherTests.cs`: corner cases (empty query, empty source, all-mismatch).

---

## TASK-07 — `UndoStack`

Per-graph undo / redo with batching.

**Spec refs**: `02_architecture/command_pipeline.md` §"Undo / Redo flow".

**Kernel refs**: `04_kernel_code/K05_undo_stack.md` — code is written; copy in.

**Outputs**:

- `src/NodeEditor.Core/Undo/UndoStack.cs`
- `src/NodeEditor.Core/Undo/UndoEntry.cs`

**Verification**: Begin/End/Cancel work; can undo/redo a sequence; redo history cleared on new push after undo.

**Tests**:

- `tests/NodeEditor.Core.Tests/Undo/UndoStackTests.cs`: push/undo/redo round-trip; batches; cancel.

---

## TASK-08 — `SpatialIndex`

Coarse-grid hit-test and viewport-culling structure. Critical for performance.

**Spec refs**: `02_architecture/performance_model.md` §"What we virtualize", `01_interaction_specs/A_canvas_interactions.md` §A.2 hit-test priority.

**Kernel refs**: `04_kernel_code/K06_spatial_index.md` — code is written; copy in.

**Outputs**:

- `src/NodeEditor.Core/Spatial/SpatialIndex.cs`
- `src/NodeEditor.Core/Spatial/HitResult.cs`
- `src/NodeEditor.Core/Spatial/SpatialQueryResults.cs`

**Verification**: hit-test priority order honored. Empty grid handled. Rebuild on full invalidate; incremental update on partial changes.

**Tests**:

- `tests/NodeEditor.Core.Tests/Spatial/SpatialIndexTests.cs`: build, query, hit-test all priority orders.
- `tests/NodeEditor.Core.Tests/Spatial/HitTestTests.cs`: edge cases — overlapping nodes, pin-snap radius, comment headers vs bodies.

---

## TASK-09 — `ViewportState` animation

Add the AnimateTo / Update / IsAnimating functionality to `ViewportState`. Frame-Selection animation per `A_canvas_interactions.md §A.11`.

**Spec refs**: `01_interaction_specs/A_canvas_interactions.md` §A.11, §A.17 (180ms ease-out-cubic).

**Kernel refs**: none.

**Outputs**:

- Update `src/NodeEditor.Core/View/ViewportState.cs` with animation state and easing function.

**Verification**: AnimateTo(target, 180ms) reaches target after exactly that duration when Update is called incrementally. Easing is ease-out-cubic.

**Tests**:

- `tests/NodeEditor.Core.Tests/View/ViewportAnimationTests.cs`: animation starts/ends at correct values; IsAnimating true mid-flight; easing curve correct (sample 3 mid-points).

---

## TASK-10 — `InteractionDispatcher` skeleton

Top-level state machine for canvas. Defines `InteractionState` discriminated union and the `Update` method's high-level dispatch. Handles only IDLE→HOVER_* transitions. Drag and box-select come in later tasks.

**Spec refs**: `01_interaction_specs/A_canvas_interactions.md` §A.1 (state machine diagram), §A.2 (hover behaviors).

**Kernel refs**: `04_kernel_code/K07_interaction_dispatcher.md` — provides the state machine structure as reference.

**Outputs**:

- `src/NodeEditor.Core/View/InteractionState.cs`
- `src/NodeEditor.Core/Interaction/InteractionDispatcher.cs`
- `src/NodeEditor.Core/Interaction/InteractionInputs.cs`
- `src/NodeEditor.Core/Interaction/DragThreshold.cs` (constants)

**Verification**: dispatcher transitions to correct Hover* state for each entity type. No drag/select yet.

**Tests**:

- `tests/NodeEditor.Core.Tests/Interaction/InteractionDispatcherTests.cs`: idle→hover transitions for node, pin, wire, comment, reroute.

---

## TASK-11 — Selection rules

Implement click/Shift+click/Ctrl+click selection logic in the dispatcher. Match `A_canvas_interactions.md §A.3` exactly.

**Spec refs**: `01_interaction_specs/A_canvas_interactions.md` §A.3.

**Kernel refs**: none.

**Outputs**:

- Update `InteractionDispatcher` with selection handling on mouse-down/up.

**Verification**: click unselected node = becomes only selection. Shift+click adds. Ctrl+click toggles. Primary tracking matches spec.

**Tests**:

- `tests/NodeEditor.Core.Tests/Interaction/SelectionRulesTests.cs`: all selection combinations with modifier permutations. Mouse-up beyond threshold should drag; below threshold should select only.

---

## TASK-12 — Box select

Box-select rubber band with Replace/Add/Toggle modes; Alt-touch behavior.

**Spec refs**: `01_interaction_specs/A_canvas_interactions.md` §A.4.

**Kernel refs**: none.

**Outputs**:

- Extend `InteractionDispatcher` with `BoxSelect` state handling.

**Verification**: rubber band activated only after threshold crossed. Replace clears prior, Add unions, Toggle XORs. Alt switches to touch-mode (intersection vs containment).

**Tests**:

- `tests/NodeEditor.Core.Tests/Interaction/BoxSelectTests.cs`: setup 4 nodes in known positions, drag selection box, verify final selection. Alt-touch variant. Esc cancellation.

---

## TASK-13 — Drag nodes

DragNodes state: snapshot on threshold cross, override positions, commit single MoveNodes command on mouse-up.

**Spec refs**: `01_interaction_specs/A_canvas_interactions.md` §A.5.

**Kernel refs**: none.

**Outputs**:

- Extend `InteractionDispatcher` with `DragNodes` state.

**Verification**: drag updates `DragOverrideMap` per frame; mouse-up emits one `MoveNodes` command with all final positions; Esc cancels (clears overrides, no command). No commands during drag.

**Tests**:

- `tests/NodeEditor.Core.Tests/Interaction/DragNodesTests.cs`: single-node drag, multi-node drag, Esc-cancel, snap to grid when enabled.

---

## TASK-14 — Drag wire

DragWireFromPin state: validate against `ILinkValidator`, snap-to-pin within 14px, auto-cast on ValidWithCast, popup on empty drop.

**Spec refs**: `01_interaction_specs/A_canvas_interactions.md` §A.6.

**Kernel refs**: none.

**Outputs**:

- Extend `InteractionDispatcher` with `DragWireFromPin` state.
- Stub `IPickerWindow` interface for the popup-open call (actual picker impl comes later).

**Verification**: validation result drives visual halo state. Snap-to-pin works. Drop on pin creates link; drop on empty triggers picker callback; drop on incompatible silently returns.

**Tests**:

- `tests/NodeEditor.Core.Tests/Interaction/DragWireTests.cs`: valid drop, invalid drop, with-cast drop (batch command emitted), replacement when target already has connection, snap-to-pin within radius.

---

## TASK-15 — Drag wire from mid (Ctrl+drag)

Pick up an existing wire by its midpoint; the closer end follows the cursor.

**Spec refs**: `01_interaction_specs/A_canvas_interactions.md` §A.7.

**Kernel refs**: none.

**Outputs**:

- Extend `InteractionDispatcher`.

**Verification**: Ctrl+LMB-drag on wire transitions to DragWireFromPin with the closer endpoint as the source.

**Tests**:

- `tests/NodeEditor.Core.Tests/Interaction/DragWireFromMidTests.cs`: midpoint pickup; closer endpoint stays anchored; collapses into DragWireFromPin behavior.

---

## TASK-16 — Drag comment, drag reroute, resize comment

The other drag types.

**Spec refs**: `01_interaction_specs/D8_comments_and_reroutes.md`.

**Kernel refs**: none.

**Outputs**:

- Extend `InteractionDispatcher` with DragComment, DragReroute, ResizeComment states.

**Verification**: comment drag (with move-with-contents); reroute drag updates link waypoint; comment resize updates Position+Size.

**Tests**:

- `tests/NodeEditor.Core.Tests/Interaction/DragCommentTests.cs`: header-drag moves comment + contents. Shift skips contents.
- `tests/NodeEditor.Core.Tests/Interaction/DragRerouteTests.cs`: drag updates waypoint position via single command at end.
- `tests/NodeEditor.Core.Tests/Interaction/ResizeCommentTests.cs`: corner and edge handles, min-size clamp.

---

## TASK-17 — Pan and zoom

Mouse-wheel zoom toward cursor; middle-mouse pan; range clamp; low-zoom threshold.

**Spec refs**: `01_interaction_specs/A_canvas_interactions.md` §A.11.

**Kernel refs**: none.

**Outputs**:

- Extend `InteractionDispatcher` with `PanCanvas` state.
- Add zoom logic that recomputes pan so the cursor-pointed canvas point stays fixed.

**Verification**: wheel zooms toward cursor; world point under cursor stays under cursor. Range 0.25–3.0. Pan via MMB drag.

**Tests**:

- `tests/NodeEditor.Core.Tests/Interaction/ZoomAndPanTests.cs`: cursor-centered zoom math; clamp; reset-zoom.

---

## TASK-18 — Frame all / Frame selection

Animated viewport transitions.

**Spec refs**: `01_interaction_specs/A_canvas_interactions.md` §A.11 "Frame all / Frame selection".

**Kernel refs**: none.

**Outputs**:

- Methods on `GraphView` or `EditorCommands` that compute target viewport and call `ViewportState.AnimateTo`.

**Verification**: F key frames the selection (or all if empty selection); Home frames all; 180ms ease-out animation.

**Tests**:

- `tests/NodeEditor.Core.Tests/View/FrameSelectionTests.cs`: target rect computed correctly with ~10% padding.

---

## TASK-19 — `EditorCommands` registry and built-in commands

`IEditorCommands` implementation with all built-in commands registered. See `K09_command_catalog.md` for the full list with their handlers.

**Spec refs**: `01_interaction_specs/D0_command_action_api.md`.

**Kernel refs**: `04_kernel_code/K09_command_catalog.md` — handler implementations.

**Outputs**:

- `src/NodeEditor.Core/Commands/EditorCommands/EditorCommandRegistry.cs`
- All built-in command descriptors and handlers from K09.

**Verification**: Every MVP command from `D0` is registered. `IsEnabled` reflects live state. AvailabilityChanged fires.

**Tests**:

- `tests/NodeEditor.Core.Tests/Commands/EditorCommandRegistryTests.cs`: registration, lookup, invoke; availability events.

---

## TASK-20 — Bookmarks

Per-asset bookmark store with 9 slots; jump animation.

**Spec refs**: `01_interaction_specs/D9_bookmarks.md`.

**Kernel refs**: none.

**Outputs**:

- `src/NodeEditor.Core/Bookmarks/Bookmark.cs`
- `src/NodeEditor.Core/Bookmarks/IBookmarkStore.cs`
- `src/NodeEditor.Core/Bookmarks/BookmarkStore.cs`
- Register `editor.bookmark-*` commands in registry.

**Verification**: Set, Get, Clear slot 1..9; jump triggers `ViewportState.AnimateTo`.

**Tests**:

- `tests/NodeEditor.Core.Tests/Bookmarks/BookmarkStoreTests.cs`.

---

## TASK-21 — Picker state machine and registry

Core picker state (current source, query, results, highlighted index, navigation stack for nested levels). No UI yet — pure state.

**Spec refs**: `01_interaction_specs/C_generic_picker.md` §C.3–C.11 except rendering specifics.

**Kernel refs**: none.

**Outputs**:

- `src/NodeEditor.Core/Picker/IPickerSource.cs`
- `src/NodeEditor.Core/Picker/IPickerRegistry.cs`
- `src/NodeEditor.Core/Picker/PickerRegistry.cs`
- `src/NodeEditor.Core/Picker/PickerState.cs` (the open-picker's state machine)
- `src/NodeEditor.Core/Picker/PickerSelectionMode.cs` and `PickerLayout.cs`
- `src/NodeEditor.Core/Picker/Sources/NodeCatalogPickerSource.cs` (one source impl for testing)

**Verification**: open with source; query updates results; arrow keys move highlight; Enter selects; Esc cancels.

**Tests**:

- `tests/NodeEditor.Core.Tests/Picker/PickerStateTests.cs`: open/close/cancel; query refilter; highlight navigation; favorites/recents tracking.

---

## TASK-22 — `EditorSession`, `GraphTab`, `ClipboardManager`

Cross-tab state. Multi-graph tab management.

**Spec refs**: `01_interaction_specs/A_canvas_interactions.md` §A.15, `02_architecture/interfaces_editor_internal.md`.

**Kernel refs**: none.

**Outputs**:

- `src/NodeEditor.Core/Session/EditorSession.cs`
- `src/NodeEditor.Core/Session/GraphTab.cs`
- `src/NodeEditor.Core/Session/ClipboardManager.cs`

**Verification**: open/close/activate tabs; events fire; per-graph undo stacks isolated.

**Tests**:

- `tests/NodeEditor.Core.Tests/Session/EditorSessionTests.cs`.
- `tests/NodeEditor.Core.Tests/Session/ClipboardManagerTests.cs`: serialize selection to JSON, deserialize, restore with new IDs.

---

## TASK-23 — `NodeEditorInstance` composition root

The top-level class that ties everything together.

**Spec refs**: `02_architecture/interfaces_editor_internal.md` §"NodeEditorInstance".

**Kernel refs**: none.

**Outputs**:

- `src/NodeEditor.Core/NodeEditorInstance.cs`
- `src/NodeEditor.Core/NodeEditorOptions.cs`
- `src/NodeEditor.Core/HostServices.cs` (the services bundle)

**Verification**: constructs cleanly with a `FakeHost` bundle; `Update` and `Render` methods work without exceptions.

**Tests**:

- `tests/NodeEditor.Core.Tests/NodeEditorInstanceTests.cs`: construct + dispose, basic update loop.

---

## TASK-24 — `ImGuiInputSource`

Adapter from ImGui state to `IInputSource`. The only non-trivial piece is mapping `ImGuiKey` to `EditorKey` and reading `ImGui.IsAnyItemActive` for `IsCapturedByUi`.

**Spec refs**: `02_architecture/interfaces_host_contract.md` §"IInputSource", `03_implementation_plan/code_conventions.md` §"ImGui patterns".

**Kernel refs**: none.

**Outputs**:

- `src/NodeEditor.UI/Input/ImGuiInputSource.cs`
- `src/NodeEditor.UI/Input/InputKeyMapping.cs`

**Verification**: maps every key in `EditorKey` to `ImGuiKey`. `IsCapturedByUi` returns true when ImGui owns input.

**Tests**:

- Difficult to unit-test ImGui directly; use a minimal in-process ImGui fixture if available, otherwise integration-test via Demo.

---

## TASK-25 — Canvas renderer: nodes only

Render visible nodes using `ImDrawList`. Header + body + pins (no editors yet, no wires yet).

**Spec refs**: `01_interaction_specs/A_canvas_interactions.md` §A.19 (perf budget), `02_architecture/performance_model.md`.

**Kernel refs**: none.

**Outputs**:

- `src/NodeEditor.UI/Canvas/CanvasRenderer.cs`
- `src/NodeEditor.UI/Canvas/NodeRenderer.cs`
- `src/NodeEditor.UI/Canvas/NodeMeasurementCache.cs`
- `src/NodeEditor.UI/Canvas/GridRenderer.cs`

**Verification**: in Demo, a graph with 10 nodes renders. Hit-test via `SpatialIndex` still works. Pins are shown but not yet editable.

**Tests**:

- `tests/NodeEditor.UI.Tests/Canvas/NodeMeasurementCacheTests.cs`: cache hit/miss invalidation.

---

## TASK-26 — Canvas renderer: wires

Bezier wire rendering. Per-frame cache of sample points.

**Spec refs**: `01_interaction_specs/A_canvas_interactions.md` §A.6 (tangent formula).

**Kernel refs**: none.

**Outputs**:

- `src/NodeEditor.UI/Canvas/WireRenderer.cs`
- `src/NodeEditor.UI/Canvas/WireSampleCache.cs`

**Verification**: wires render with correct colors and tangents. Reroutes split a wire into segments. Exec wires get directional arrow; data wires don't.

**Tests**:

- minimal — visual verification in Demo.

---

## TASK-27 — Canvas renderer: comments, reroutes, selection

The remaining canvas visuals.

**Spec refs**: `01_interaction_specs/D8_comments_and_reroutes.md`, `01_interaction_specs/A_canvas_interactions.md` §A.3.

**Kernel refs**: none.

**Outputs**:

- `src/NodeEditor.UI/Canvas/CommentRenderer.cs`
- `src/NodeEditor.UI/Canvas/RerouteRenderer.cs`
- `src/NodeEditor.UI/Canvas/SelectionRenderer.cs`
- `src/NodeEditor.UI/Canvas/DragVisualsRenderer.cs`

**Verification**: comments render with header + transparent body (correct alpha). Selection outlines drawn. Drag rubber band and wire preview render.

**Tests**: visual verification.

---

## TASK-28 — Mini-editors: bool, int, float

The first three built-in pin default editors.

**Spec refs**: `01_interaction_specs/B_mini_editors.md` §"Built-in editors".

**Kernel refs**: none.

**Outputs**:

- `src/NodeEditor.UI/Mini/IPinDefaultValueEditor.cs`
- `src/NodeEditor.UI/Mini/IPinDefaultValueEditorRegistry.cs`
- `src/NodeEditor.UI/Mini/PinDefaultValueEditorRegistry.cs`
- `src/NodeEditor.UI/Mini/DefaultEditors/BoolEditor.cs`
- `src/NodeEditor.UI/Mini/DefaultEditors/IntEditor.cs`
- `src/NodeEditor.UI/Mini/DefaultEditors/FloatEditor.cs`
- `src/NodeEditor.UI/Mini/DragFloat.cs` (shared helper)
- `src/NodeEditor.UI/Mini/ExpressionEvaluator.cs`

**Verification**: in Demo, bool/int/float pin defaults edit inline. One undo entry per drag.

**Tests**:

- `tests/NodeEditor.UI.Tests/Mini/ExpressionEvaluatorTests.cs`: parse `2*pi`, `45 deg`, etc.

---

## TASK-29 — Mini-editors: string, enum (combo + picker), Vector2/3/4

The next batch.

**Spec refs**: `01_interaction_specs/B_mini_editors.md`.

**Kernel refs**: none.

**Outputs**:

- Editors per file in `src/NodeEditor.UI/Mini/DefaultEditors/`.

**Verification**: each editor works in Demo. Tab navigation between Vector3 components works.

**Tests**: integration via Demo.

---

## TASK-30 — Mini-editors: color, quaternion, array

Color picker popup with hex/named/recent. Quaternion as YPR degrees. Array editor with add/remove.

**Spec refs**: `01_interaction_specs/B_mini_editors.md`.

**Kernel refs**: none.

**Outputs**:

- Editors per file in `src/NodeEditor.UI/Mini/DefaultEditors/`.

**Verification**: in Demo, all three editor types work end-to-end.

**Tests**: integration via Demo.

---

## TASK-31 — Picker window UI

Render the picker as a floating ImGui window. Standard layout (most common). Other layouts can come later.

**Spec refs**: `01_interaction_specs/C_generic_picker.md` §C.2, §C.6, §C.7, §C.10.

**Kernel refs**: none.

**Outputs**:

- `src/NodeEditor.UI/Picker/PickerWindow.cs`
- `src/NodeEditor.UI/Picker/PickerLayoutStandard.cs`
- `src/NodeEditor.UI/Picker/PickerHighlighter.cs`

**Verification**: in Demo, Tab on canvas opens the picker. Search filters. Enter creates a node at cursor.

**Tests**: integration via Demo.

---

## TASK-32 — Picker other layouts (compact, wide, grid, tree)

The remaining layouts.

**Spec refs**: `01_interaction_specs/C_generic_picker.md` §C.10.

**Kernel refs**: none.

**Outputs**:

- `src/NodeEditor.UI/Picker/PickerLayoutCompact.cs`
- `src/NodeEditor.UI/Picker/PickerLayoutWide.cs`
- `src/NodeEditor.UI/Picker/PickerLayoutGrid.cs`
- `src/NodeEditor.UI/Picker/PickerLayoutTree.cs`

**Verification**: each layout renders correctly per layout's screenshot in spec.

**Tests**: integration.

---

## TASK-33 — Tab strip

Multi-graph tab bar.

**Spec refs**: `01_interaction_specs/A_canvas_interactions.
