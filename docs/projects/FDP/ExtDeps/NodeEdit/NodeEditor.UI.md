# NodeEditor.UI

**Project path**: `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/NodeEditor.UI.csproj`
**Date**: 2026-05-23
**Target framework**: net8.0
**Namespace root**: `NodeEditor.UI`

---

## README Validation

Status: **Missing** -- no `README.md` exists in the project folder or the NodeEdit root.
All public types carry XML doc comments.

---

## Executive Overview

`NodeEditor.UI` is the ImGui-based rendering and input layer of the NodeEditor library.
It depends on `NodeEditor.Core` for all data contracts and state and on `ImGui.NET`
(wrapping Dear ImGui 1.91.6) for all drawing primitives.

Responsibilities:
- **Canvas rendering**: draws the grid, nodes, wires, pins, comments, containers,
  attachment pills, reroute waypoints, marquee selection box, and pending wire in a
  multi-pass pipeline.
- **Input handling**: drives the `InteractionMode` state machine in `Core` in response
  to ImGui mouse and keyboard events.
- **Node picker**: a floating search-and-pick window invoked on canvas right-click, wire
  drop, or hotkey. Supports compact list, wide tree, and grid layout modes.
- **Panels**: a side-panel system including `MyBlueprintPanel` (asset browser) and
  `DetailsPanel` (property inspector).
- **Find**: an in-graph search bar with cross-graph search scope.
- **Inline editors**: per-type default-value editors (float, int, bool, string, enum,
  color, vector, quaternion, guid, etc.) displayed inside unconnected input pins.
- **Hot-reload badges**: overlays shown when a hot-reload modifies nodes the editor has
  open, with conflict resolution UI.
- **Mini-editors**: compact property editors suitable for embedding inside node bodies.

The UI layer is intentionally stateless with respect to graph data. Everything it draws
comes from `GraphView` (provided by the host). No UI state is stored in the project
beyond per-session UI configuration (fold states, search query, picker state).

---

## Architecture

### Component Diagram

```
+----------------------------------------------------------+
|                  Host Application                        |
|   Creates GraphView, calls canvasRenderer.Render(view)   |
+----------------------------------------------------------+
                          |
                   GraphView (from Core)
                          |
+----------------------------------------------------------+
|                   NodeEditor.UI                          |
|                                                          |
|  +------------------+   +------------------------------+ |
|  |  CanvasRenderer  |-->|  CanvasLayoutBuilder          | |
|  |  (entry point)   |   |  (sizes nodes & pin positions)| |
|  +------------------+   +------------------------------+ |
|         |                                                |
|         |-- GridRenderer        (background grid)        |
|         |-- CommentsRenderer    (comment boxes, back)     |
|         |-- WireRenderer        (spline wires)            |
|         |-- ReroutesRenderer    (reroute waypoints)       |
|         |-- NodeRenderer        (node bodies + pins)      |
|         |-- ContainerRenderer   (container nodes)         |
|         |-- AttachmentRenderer  (attachment pills)        |
|         |-- CanvasInput         (state machine driver)    |
|         |-- [pending wire overlay]                        |
|         |-- [marquee overlay]                             |
|                                                          |
|  +------------------+   +-----------------------------+  |
|  |   PickerWindow   |   |  DetailsPanel               |  |
|  |   PickerRegistry |   |  MyBlueprintPanel            |  |
|  |   PickerState    |   |  DetailsViewRegistry         |  |
|  +------------------+   +-----------------------------+  |
|                                                          |
|  +------------------+   +-----------------------------+  |
|  |  FindBar         |   |  MiniEditors/               |  |
|  |  FindEngine      |   |  FloatPinEditor             |  |
|  |  FindResultsPanel|   |  IntPinEditor / BoolPinEditor|  |
|  +------------------+   |  ColorPinEditor             |  |
|                         |  EnumPinEditor              |  |
|  +------------------+   |  VectorPinEditor            |  |
|  |  HotReload/      |   |  ...15 editors total        |  |
|  |  ChangeBadgeRend.|   +-----------------------------+  |
|  |  ChangeNotifier  |                                    |
|  +------------------+   +-----------------------------+  |
|                         |  Action/ (hotkey binding)   |  |
|                         |  BuiltinCommandHandlers     |  |
|                         |  CanvasCommands / EditCmds  |  |
|                         +-----------------------------+  |
+----------------------------------------------------------+
                          |
                 ProjectReference
                          |
          +---------------+------------------+
          |                                  |
  NodeEditor.Core                       ImGui.NET 1.91.6.1
  (GraphView, interfaces,               (Dear ImGui bindings)
   commands, spatial index)
```

### Canvas Frame Pipeline

```
CanvasRenderer.Render(view)
  |
  +-- Set CanvasScreenOrigin / CanvasScreenSize in viewport
  +-- [Optional] FindBar.Draw()
  |
  +-- ImGui.BeginChild("##ne_canvas")
  |
  +-- CanvasLayoutBuilder.Build(view, layout)
  |     -- computes NodeRect, PinPositions, ConnectedInputPins per visible node
  |
  +-- SpatialIndex.Rebuild (if dirty)
  |
  +-- CanvasInput.Handle(view, ...)    <-- drives InteractionMode FSM
  |
  +-- GridRenderer.Draw(view, dl)
  +-- CommentsRenderer.DrawBack(view, dl)
  +-- WireRenderer.Draw(view, dl, layout)
  +-- ReroutesRenderer.Draw(view, dl, layout)
  +-- NodeRenderer.DrawAll(view, dl, layout)
  +-- ContainerRenderer.DrawAll(view, dl, layout)
  +-- AttachmentRenderer.DrawAll(view, dl, layout)
  +-- CommentsRenderer.DrawFront(view, dl)
  +-- [pending wire bezier overlay]
  +-- [marquee rect overlay]
  +-- [custom canvas renderers (ICustomCanvasRenderer)]
  |
  +-- ImGui.EndChild()
```

---

## Source Structure

```
NodeEditor.UI/
+-- Action/
|   +-- BuiltinCommandHandlers.cs  -- Ctrl+Z/Y, Delete, Ctrl+A etc.
|   +-- CanvasCommands.cs          -- canvas-level command handler
|   +-- EditCommands.cs            -- copy/cut/paste/duplicate commands
|   +-- ViewCommands.cs            -- frame-all, zoom-to-selection commands
+-- Bookmarks/
|   +-- (bookmark navigation UI support)
+-- Canvas/
|   +-- AttachmentRenderer.cs      -- draws attachment pills above nodes
|   +-- CanvasInput.cs             -- input state machine
|   +-- CanvasLayout.cs            -- per-frame layout snapshot
|   +-- CanvasLayoutBuilder.cs     -- builds CanvasLayout from GraphView
|   +-- CanvasRenderContextImpl.cs -- ICanvasRenderContext implementation
|   +-- CanvasRenderer.cs          -- top-level orchestrator
|   +-- CommentsRenderer.cs        -- comment boxes (back + front pass)
|   +-- ContainerRenderer.cs       -- container/subgraph node bodies
|   +-- GridRenderer.cs            -- dotted/lined background grid
|   +-- HitTester.cs               -- resolves HoverInfo from mouse pos
|   +-- NodeRenderer.cs            -- node bodies, headers, pins, inline editors
|   +-- PinRenderer.cs             -- individual pin glyphs and labels
|   +-- ReroutesRenderer.cs        -- reroute waypoint handles
|   +-- WireRenderer.cs            -- bezier/Manhattan wire curves
+-- Find/
|   +-- FindBar.cs                 -- search-as-you-type overlay band
|   +-- FindEngine.cs              -- searches nodes/pins/comments in graph
|   +-- FindQuery.cs               -- query model (text + scope flags)
|   +-- FindResult.cs              -- hit record (node/pin/comment, match spans)
|   +-- FindResultsPanel.cs        -- dockable panel for cross-graph results
|   +-- FindScope.cs               -- enum: CurrentGraph | AllGraphs | Asset
+-- HotReload/
|   +-- ChangeBadgeRenderer.cs     -- renders diff badges on modified nodes
|   +-- ChangeNotifier.cs          -- receives hot-reload notifications
|   +-- RecentChanges.cs           -- tracks per-node recent change state
+-- MiniEditors/
|   +-- ArrayPinEditor.cs          -- inline array element editor
|   +-- AssetPinEditor.cs          -- asset reference picker editor
|   +-- BoolPinEditor.cs           -- checkbox editor
|   +-- ColorPinEditor.cs          -- RGBA color picker
|   +-- DragFloatWithExpression.cs -- float field that accepts expressions
|   +-- EntityPinEditor.cs         -- entity/actor reference editor
|   +-- EnumPinEditor.cs           -- dropdown enum selector
|   +-- FloatPinEditor.cs          -- drag-float with optional expression
|   +-- GuidPinEditor.cs           -- UUID text field
|   +-- IntPinEditor.cs            -- integer drag field
|   +-- PinDefaultValueEditorRegistry.cs -- maps TypeKey -> IPinDefaultValueEditor
|   +-- QuaternionPinEditor.cs     -- quaternion as euler angles
|   +-- StringPinEditor.cs         -- text input
|   +-- StructPinEditor.cs         -- nested struct fields
|   +-- VectorPinEditor.cs         -- Vector2/3/4 component fields
+-- Panels/
|   +-- DetailsContextImpl.cs      -- IDetailsContext implementation
|   +-- DetailsPanel.cs            -- property inspector panel
|   +-- DetailsViewRegistry.cs     -- maps node kind to IDetailsViewProvider
|   +-- MyBlueprintContextMenu.cs  -- context menu for asset tree items
|   +-- MyBlueprintDragSource.cs   -- drag-source for variables/functions
|   +-- MyBlueprintItemRenderer.cs -- renders asset tree rows
|   +-- MyBlueprintPanel.cs        -- asset browser / my-blueprint panel
|   +-- Views/                     -- per-kind Details view implementations
+-- Picker/
|   +-- FavoritesStore.cs          -- persists favorited catalog entries
|   +-- Layouts/                   -- Compact, Wide, Grid layout renderers
|   +-- PickerEntry.cs             -- single item in the picker list
|   +-- PickerItemListHelper.cs    -- filtering + scoring helpers
|   +-- PickerRegistry.cs          -- maps host-registered picker sources
|   +-- PickerRequest.cs           -- invocation descriptor (title, layout, ...)
|   +-- PickerResult.cs            -- chosen item wrapper
|   +-- PickerSourceAdapter.cs     -- adapts IPickerSource to picker entries
|   +-- PickerState.cs             -- search query + scroll + selection state
|   +-- PickerWindow.cs            -- floating picker popup window
|   +-- RecentStore.cs             -- persists recently used entries
+-- Util/
|   +-- (drawing helpers, color utilities)
+-- NodeEditor.UI.csproj
```

---

## Public API Reference

### CanvasRenderer

The primary entry point for hosts rendering a graph tab:

```csharp
public sealed class CanvasRenderer
{
    /// <summary>
    /// Render one frame of the node-editor canvas inside the current ImGui window.
    /// Opens and closes its own child window.
    /// </summary>
    public void Render(GraphView view);

    /// <summary>
    /// Render with an optional find overlay above the canvas.
    /// </summary>
    public void Render(GraphView view, FindBar? findBar);
}
```

Usage pattern (inside the ImGui render loop):

```csharp
// Once per graph tab that is visible
_canvasRenderer.Render(_graphView, _findBar);
```

### PickerWindow

The floating node-creation picker. Shared across all picker invocations; a second
`Open` call cancels the first.

```csharp
public sealed class PickerWindow
{
    public bool IsOpen { get; }

    // Open from a fully specified PickerRequest
    public void Open(PickerRequest request, Action<PickerResult> onChosen);

    // Open from a registered IPickerSource via the registry
    public void OpenSource(string sourceKey, IReadOnlyDictionary<string,object?> ctx,
                           Action<object> onPicked, Action? onCancel = null);

    // Close without selecting
    public void Cancel();

    // Render each frame (call inside the host window, not inside the canvas)
    public void Draw();
}
```

### DetailsPanel

```csharp
public sealed class DetailsPanel
{
    public DetailsTarget Target     { get; set; }  // drives view selection
    public bool ShowAdvanced        { get; set; }
    public bool ShowHelpTooltips    { get; set; }

    // Render each frame
    public void Draw();
}
```

### MyBlueprintPanel

Asset browser panel (variables, functions, macros, event dispatchers):

```csharp
public sealed class MyBlueprintPanel
{
    // Render each frame
    public void Draw(Vector2 size = default);
}
```

### FindBar

```csharp
public sealed class FindBar
{
    public bool IsVisible { get; set; }
    public FindQuery CurrentQuery { get; }

    // Toggle visibility (Ctrl+F)
    public void Toggle();
    // Render the narrow search band; call before CanvasRenderer.Render
    public void Draw();
}
```

### PinDefaultValueEditorRegistry

Host-configurable mapping from `TypeKey` to `IPinDefaultValueEditor`:

```csharp
public sealed class PinDefaultValueEditorRegistry : IPinDefaultValueEditorRegistry
{
    public void Register(TypeKey key, IPinDefaultValueEditor editor);
    public IPinDefaultValueEditor? Get(TypeKey key);
}
```

### Built-in MiniEditors

| Class | Handles TypeKey | Widget |
|---|---|---|
| `BoolPinEditor` | `"bool"` | Checkbox |
| `IntPinEditor` | `"int"`, `"int32"` | DragInt with range clamping |
| `FloatPinEditor` | `"float"` | DragFloat, supports expressions |
| `StringPinEditor` | `"string"` | InputText |
| `EnumPinEditor` | any registered enum | Combo dropdown |
| `ColorPinEditor` | `"Color"`, `"Vector4Color"` | ColorEdit4 |
| `VectorPinEditor` | `"Vector2"`, `"Vector3"`, `"Vector4"` | Multi-component drag |
| `QuaternionPinEditor` | `"Quaternion"` | Euler angles DragFloat3 |
| `GuidPinEditor` | `"Guid"` | InputText with UUID validation |
| `AssetPinEditor` | host-defined asset types | Picker button |
| `StructPinEditor` | host-defined struct types | Expandable sub-fields |
| `ArrayPinEditor` | array-container pins | Expandable element list |

---

## Dependencies

| Dependency | Version | Why |
|---|---|---|
| `NodeEditor.Core` | project ref | `GraphView`, interfaces, commands, spatial |
| `ImGui.NET` | 1.91.6.1 | All drawing calls (`ImDrawListPtr`, `ImGui.*`) |

---

## Usage Examples

### Example 1: Minimal canvas render loop (Raylib host)

```csharp
// Initialization (once)
var canvasRenderer = new CanvasRenderer();
var findBar = new FindBar();

// Setup host services + GraphView
var view = new GraphView(model, sink, validator, typeSystem, catalog, host);

// Per-frame render (inside rlImGui / ImGui frame)
ImGui.Begin("Graph Editor");

// Optional: draw find bar above canvas
if (ImGui.IsKeyChordPressed(ImGuiKey.ModCtrl | ImGuiKey.F))
    findBar.Toggle();

canvasRenderer.Render(view, findBar);

ImGui.End();
```

### Example 2: Invoking the node-creation picker

```csharp
// When user right-clicks on the canvas background
if (ImGui.BeginPopupContextWindow("##canvas_ctx"))
{
    if (ImGui.MenuItem("Add Node..."))
    {
        var screenPos = ImGui.GetMousePosOnOpeningCurrentPopup();
        var graphPos  = view.Viewport.ScreenToGraph(screenPos);

        var request = new PickerRequest
        {
            Title          = "Add Node",
            Layout         = PickerLayout.Wide,
            SelectionMode  = PickerSelectionMode.Single,
            AnchorScreen   = screenPos,
            ContextKey     = "add_node",
            ItemsProvider  = () => view.Catalog.All.Select(e => new PickerEntry(
                                   e.Kind.Value, e.DisplayName, e.Description,
                                   e.CategoryPath, e.IconKey)),
        };

        _pickerWindow.Open(request, result =>
        {
            var kind = new NodeKindKey(result.Key);
            var cmd  = new GraphCommand.AddNode(NodeId.NewId(), kind, graphPos, null);
            view.Execute(cmd, new GraphCommand.RemoveNodes([cmd.AssignedId]), "Add Node");
        });
    }
    ImGui.EndPopup();
}

// Each frame (outside canvas child window)
_pickerWindow.Draw();
```

### Example 3: Wiring up inline pin editors

```csharp
// During host services setup
var editorRegistry = new PinDefaultValueEditorRegistry();

// Register built-in editors
editorRegistry.Register(new TypeKey("float"),  new FloatPinEditor());
editorRegistry.Register(new TypeKey("int"),    new IntPinEditor());
editorRegistry.Register(new TypeKey("bool"),   new BoolPinEditor());
editorRegistry.Register(new TypeKey("string"), new StringPinEditor());
editorRegistry.Register(new TypeKey("Color"),  new ColorPinEditor());

// Register for a custom game type
editorRegistry.Register(new TypeKey("MyDamageType"), new EnumPinEditor(
    Enum.GetNames<MyDamageType>(),
    Enum.GetValues<MyDamageType>().Cast<int>().ToArray()));

// Then provide the registry through IEditorHostServices.Pickers (or theme adapter).
// The NodeRenderer automatically uses it when drawing unconnected input data pins.
```

### Example 4: Using the DetailsPanel

```csharp
// Create once
var detailsRegistry = new DetailsViewRegistry();
detailsRegistry.Register(new MyNodeDetailsProvider());

var detailsCtx  = new DetailsContextImpl(view);
var detailsPanel = new DetailsPanel(detailsRegistry, detailsCtx);

// Per-frame: update target based on selection
if (view.Selection.Count == 1 && view.Selection.Nodes.Any())
{
    var nodeId = view.Selection.Nodes.First();
    detailsPanel.Target = new DetailsTarget.Node(nodeId);
}
else
{
    detailsPanel.Target = new DetailsTarget.None();
}

// Render inside an ImGui panel
ImGui.Begin("Details");
detailsPanel.Draw();
ImGui.End();
```

---

## Best Practices

1. **Call `CanvasRenderer.Render` inside a window, not a child** -- the renderer opens
   its own `BeginChild` internally. Nesting child windows incorrectly causes clipping
   and scroll issues.

2. **One `CanvasRenderer` per `GraphView`** -- renderers own per-frame layout caches
   keyed to the view's model. Sharing renderers across views produces stale cache hits.

3. **Draw `PickerWindow` outside the canvas child window** -- the picker is a floating
   ImGui window; it must be drawn at the top level, not inside `BeginChild`.

4. **Register all custom pin editors before the first frame** -- the
   `PinDefaultValueEditorRegistry` is read every time an unconnected input pin is drawn.
   Registrations after the first `Render` call take effect immediately but may cause a
   one-frame flash with no editor.

5. **Respect `AllowUnsafeBlocks = true`** -- the csproj enables unsafe code. ImGui draw
   list calls that take `ReadOnlySpan<byte>` or native pointers are the primary users.
   Do not add unsafe code outside of ImGui interop paths.

6. **Use `FindBar` for in-graph navigation** -- pass the `FindBar` instance to
   `CanvasRenderer.Render(view, findBar)` rather than drawing it separately; the
   renderer positions the band correctly above the canvas content region.

7. **Hot-reload support** -- if the host supports assembly hot-reload, subscribe the
   `ChangeNotifier` to change events and pass it to `ChangeBadgeRenderer` so users see
   per-node diff badges. This avoids silent data loss during live iteration.

---

## Related Projects

| Project | Relationship |
|---|---|
| `NodeEditor.Core` | Direct dependency -- all data contracts and state |
| `NodeEditor.Primitives` | Transitive dependency via Core |
| `NodeEditor.Demo` | Consumer -- exercises all rendering features |
| `Fdp.Engine` (FDP) | Production host -- integrates canvas into Blueprint editor |
| `FastHSM` (FDP ExtDeps) | Production host -- uses canvas for BTree/HSM visual editor |
