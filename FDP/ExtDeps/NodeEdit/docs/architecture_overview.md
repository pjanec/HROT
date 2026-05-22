# Architecture Overview

## The five layers

The editor is structured in five layers, dependencies flowing strictly downward:

```
┌─────────────────────────────────────────────────────────┐
│                  HOST application                       │
│  (raylib-cs window, asset loading, compilation,         │
│   debug protocol, file watching — the game engine)      │
└─────────────────────────────────────────────────────────┘
                            │
                            │ implements host contract
                            ▼
┌─────────────────────────────────────────────────────────┐
│        NodeEditor.Demo (sample / test host)             │
│  Fake blueprint host with fake catalog & validator.     │
│  Useful for visual smoke-tests with no engine wiring.   │
│  raylib-cs + rlImGui-cs + ImGui.NET                     │
└─────────────────────────────────────────────────────────┘
                            │
                            │ uses
                            ▼
┌─────────────────────────────────────────────────────────┐
│              NodeEditor.UI                              │
│  ImGui-based rendering, picker window, panels,          │
│  interaction dispatcher.                                │
│  Depends on ImGui.NET; renders into existing windows.   │
└─────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│              NodeEditor.Core                            │
│  Host contracts, view-model, undo, spatial index,       │
│  fuzzy matcher, command pipeline, picker state machine. │
│  No ImGui dependency. UI-framework-agnostic.            │
└─────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│            NodeEditor.Primitives                        │
│  ID types, geometric primitives, key/modifier types,    │
│  pure value types. Zero dependencies.                   │
└─────────────────────────────────────────────────────────┘
```

### Why this layering matters

- **Primitives** is dependency-free → can be referenced by anything, including third-party tools.
- **Core** has no ImGui dependency → testable headless, no graphics required.
- **UI** depends on ImGui.NET only — could be ported to Avalonia/WPF by replacing this layer alone.
- **Demo** demonstrates the public host contract; serves as the integration smoke test.
- **Host application** lives outside this package. The integration with your existing game engine is a separate effort (a thin adapter shaping your model into `IGraphModel` etc).

### Strict dependency direction

| Layer | Allowed dependencies |
|---|---|
| Primitives | (none — only .NET BCL) |
| Core | Primitives + .NET BCL |
| UI | Core + Primitives + ImGui.NET 1.91.6.1 |
| Demo | UI + Core + Primitives + raylib-cs + rlImGui-cs + ImGui.NET |
| Host application | UI + Core + Primitives + whatever |

Tests:

- `NodeEditor.Core.Tests` references Core + Primitives.
- `NodeEditor.UI.Tests` references UI + Core + Primitives + a minimal headless ImGui shim (see `K07_interaction_dispatcher.md` for the input abstraction that lets us test UI without ImGui).

## What each layer owns

### Primitives

- ID types: `NodeId`, `PinId`, `LinkId`, `GraphId`, `CommentId`, `RerouteId`, `TypeKey`, `NodeKindKey`.
- `KeyModifiers` (Shift/Ctrl/Alt/Super flags) and `EditorKey` enum.
- Color helpers (just `Vector4`-based, no widget code).
- `Rect2` (axis-aligned bounding box; we use a custom one rather than `System.Drawing.RectangleF` to keep zero BCL graphics dep and stay float-friendly).

### Core

The editor's brain. Everything happens here except actual draw calls:

- **Host interfaces** (`IGraphModel`, `INodeCatalog`, `ITypeSystem`, `ILinkValidator`, `IGraphCommandSink`, `IPickerRegistry`, `IDebugSession`, `IDiagnosticsSink`, `IInputSource`, `IEditorTheme` etc.). See `interfaces_host_contract.md`.
- **View-model** (`GraphView`, `SelectionState`, `ViewportState`, `InteractionState`, `DragOverrideMap`). See `interfaces_editor_internal.md`.
- **Spatial index** for hit-test and viewport culling. See `K06_spatial_index.md`.
- **Command pipeline** — turns user gestures into `GraphCommand` records and dispatches via `IGraphCommandSink`.
- **Undo stack** — keeps inverse commands, supports batching. See `K05_undo_stack.md`.
- **Fuzzy matcher** — the picker's matching brain. See `K04_fuzzy_matcher.md`.
- **Interaction dispatcher** — the top-level state machine for canvas gestures. See `K07_interaction_dispatcher.md`.
- **Command catalog** (`IEditorCommands` implementation). See `K09_command_catalog.md`.
- **Bookmark store**, **session state**, **theme** abstractions.

### UI

Just the ImGui rendering and input wiring:

- **Canvas renderer** — draws nodes, wires, comments, selection, decorations using ImGui DrawList.
- **Picker window** — the floating window with search + list + preview.
- **Mini-editors** — inline pin default-value editors (per `B_mini_editors.md`).
- **Panels** — My Blueprint, Details, Find Results.
- **Tab strip** — graph tabs across the top of the canvas.
- **Input adapter** — translates ImGui events into `IInputSource` calls.

The UI layer is "dumb": it draws what Core asks for and reports input back. All state lives in Core's view-model.

### Demo

A self-contained executable using raylib-cs to host an ImGui window, with:

- A fake `IGraphModel` (in-memory dictionary of nodes/links/comments).
- A fake `INodeCatalog` (~30 built-in nodes covering math, flow control, variables, debug).
- A fake `ILinkValidator` with Unreal-style compatibility rules.
- An `IGraphCommandSink` that just applies commands directly to the fake model.
- A test harness window that lets the developer open multiple graphs, exercise all interactions, take screenshots.

Demo is **not** the integration with your engine. It's the smoke-test app for the library itself.

## Cross-cutting concerns

### Threading model

**All editor code runs on the main thread.** ImGui is fundamentally single-threaded; the editor inherits that. The host may run on different threads (e.g., compile worker, debug protocol, file watcher), but all calls *into* the editor are marshalled to the main thread by the host.

Specifically:

- `IGraphModel.Changed` events: must fire on the main thread.
- `IDiagnosticsSink.Changed`: main thread.
- `IDebugSession.Paused`: main thread.
- File-watcher callbacks: host marshals to main thread before calling `IExternalChangeNotifier.Changed`.

The editor has no internal threads, no `Task.Run`, no async fire-and-forget. The picker's async source (`IPickerSource.QueryAsync`) is a single exception: it returns to the main thread before publishing results.

### Allocation budget

The editor must be cheap to run in the per-frame hot path. Allocation rules:

- **Per-frame draw code**: zero allocations on the steady-state path. Cached structures, reused arrays.
- **Hit-test**: zero allocations.
- **Spatial index queries**: zero allocations (use pre-sized output buffers).
- **ImGui drawing**: allocations limited to what ImGui.NET inherently does (mostly internal pools).

Allocations are OK during:

- Command dispatch (creating immutable command records).
- Model mutation (the host owns this).
- Picker open / close (small bursts).
- Drag start (snapshot positions).

See `code_conventions.md` for the explicit rules.

### Identity vs. position

Everything that's selectable, draggable, or referenceable has an **identity** (GUID-wrapped record struct). Position, size, color — all the visual attributes — are read from the model, not stored in editor state. Two consequences:

- The editor's view-model doesn't redundantly mirror model data; it only holds ephemeral interaction state.
- When the model changes externally (undo, hot reload), the view just re-renders.

The one exception: during drag, the editor holds `dragOverridePositions: Dictionary<NodeId, Vector2>?` so the visual position can differ from the model position. Cleared at mouse-up when the `MoveNodes` command is emitted.

### The command pipeline

All mutations flow through a single channel. See `command_pipeline.md`.

Concretely:

1. User gesture (drag, key, click) → interaction dispatcher.
2. Dispatcher composes one or more `GraphCommand` records → submits to `IGraphCommandSink`.
3. Sink (in the host) validates + applies → model changes.
4. Model fires `Changed`.
5. Editor re-renders next frame.

For undo support, the sink wraps each command with its inverse before applying, and pushes the pair onto the `UndoStack`.

### Per-graph state vs. cross-graph state

| Scope | What lives there |
|---|---|
| Per-graph (one per tab) | `ViewportState`, `SelectionState`, in-progress drag, breakpoint set, undo stack |
| Per-asset | Bookmarks, dirty state, recently-edited |
| Editor-global | Picker registry, theme, command catalog, fuzzy match favorites/recents, key bindings, last clipboard payload |

Tab-switching swaps the per-graph state but preserves the others.

## What this architecture deliberately does NOT do

- **No "scene graph"**. The canvas isn't an OOP tree of widget objects. It's a flat collection of nodes/wires/comments with hit-test in O(visible) via the spatial index. Faster, simpler.
- **No retained-mode UI in the canvas**. Each frame redraws from the model + view state. ImGui is immediate-mode by nature.
- **No reactive observable framework**. Plain events. The editor explicitly invalidates what it needs to.
- **No dependency injection container**. Constructor injection by hand. Maybe ~10 services total.
- **No async/await in the editor code**. (Picker async sources are scoped to one interaction, handed off via callback.)
- **No code generation, no Roslyn, no reflection emit**. The host might use these; the editor itself stays straightforward C#.
- **No file I/O**. The editor never reads or writes files. The host does all persistence.

## Mapping to namespaces

| Layer | Namespace |
|---|---|
| Primitives | `NodeEditor.Primitives` |
| Core | `NodeEditor.Core`, `NodeEditor.Core.Model`, `NodeEditor.Core.Commands`, `NodeEditor.Core.View`, `NodeEditor.Core.Picker`, `NodeEditor.Core.Search`, `NodeEditor.Core.Undo`, `NodeEditor.Core.Spatial`, `NodeEditor.Core.Hosting`, `NodeEditor.Core.Interaction` |
| UI | `NodeEditor.UI`, `NodeEditor.UI.Canvas`, `NodeEditor.UI.Panels`, `NodeEditor.UI.Picker`, `NodeEditor.UI.Mini`, `NodeEditor.UI.Theme` |
| Demo | `NodeEditor.Demo`, `NodeEditor.Demo.Fake` |

Tests:

- `NodeEditor.Core.Tests.*` mirror the Core namespace structure.
- `NodeEditor.UI.Tests.*` mirror the UI namespace structure.

## A note about file naming

C# files are named `ClassName.cs` and live in folders matching their namespace under `Core/`, `Primitives/`, `UI/`, etc. One public type per file by default. See `code_conventions.md`.
