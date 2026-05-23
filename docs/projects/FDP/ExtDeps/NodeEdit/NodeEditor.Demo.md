# NodeEditor.Demo

**Project path**: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/NodeEditor.Demo.csproj`
**Date**: 2026-05-23
**Target framework**: net8.0
**Output type**: Executable
**Namespace root**: `NodeEditor.Demo`

---

## README Validation

Status: **Missing** -- no `README.md` exists in the project folder or the NodeEdit root.

---

## Executive Overview

`NodeEditor.Demo` is a standalone interactive demonstration application for the
NodeEditor library. It uses Raylib-cs as the window and GPU backend and rlImGui-cs to
bridge Raylib's render loop to Dear ImGui. The application presents a scrollable list of
self-contained scenario classes, each demonstrating a specific editor feature.

The project serves three purposes:

1. **Feature showcase** -- 35 scenarios cover the complete feature surface from basic
   pan/zoom through refactoring operations, debug visualization, hot-reload conflicts,
   container nodes, and custom renderers.
2. **Integration reference** -- the `FakeBlueprint` folder provides complete in-memory
   reference implementations of all `NodeEditor.Core` interfaces. Hosts building real
   integrations use these as starter templates.
3. **Regression harness** -- each scenario can be selected and verified visually without
   any external test infrastructure.

**Dependencies**: `NodeEditor.UI` (project ref), `Raylib-cs 7.0.2`, `rlImGui-cs 3.2.0`,
`ImGui.NET 1.91.6.1`.

---

## Architecture

### Process Layout

```
Program.cs
  |
  +-- Raylib.InitWindow(1600, 1000)
  +-- rlImGui.Setup(darkTheme, enableDocking)
  +-- Font atlas build (Arial 8/11/16/24/32 px + fallback to ImGui default)
  +-- rlImGui.ReloadFonts()
  |
  +-- new DemoShell(fonts)
  |       |
  |       +-- FakeGraphModel    (IGraphModel)
  |       +-- FakeHostServices  (IEditorHostServices)
  |       +-- GraphView         (NodeEditor.Core)
  |       +-- CanvasRenderer    (NodeEditor.UI)
  |       +-- PickerWindow
  |       +-- MyBlueprintPanel
  |       +-- DetailsPanel
  |       +-- FindBar
  |       +-- HotkeyDispatcher
  |       +-- List<Scenario>    (35 registered scenarios)
  |
  +-- Main loop:
        Raylib.BeginDrawing()
        rlImGui.Begin(elapsed)
        DemoShell.Render(elapsed)
        rlImGui.End()
        Raylib.EndDrawing()
```

### Scenario Architecture

```
+----------------------------+
|      Scenario (abstract)   |
|  + Name        : string    |
|  + Description : string    |
|  + Build(view, graph, ...)  |  <-- called once when scenario is selected
|  + Tick(view, elapsed)     |  <-- called every frame (optional)
|  # AddNode(graph, catalog) |  <-- helper
|  # LinkNodes(...)          |  <-- helper
+----------------------------+
         ^
         | (35 concrete classes: S01..S36)
         |
+----------------------------+
|  S01_HelloCanvas           |
|  S04_UndoRedo              |
|  S07_AddNodePicker         |
|  S13_DebugVizMock          |
|  S22_CollapseToFunction    |
|  ...                       |
+----------------------------+
```

### FakeBlueprint Interface Coverage

```
Interface              | Fake Implementation
-----------------------+---------------------
IGraphModel            | FakeGraphModel
IGraphCommandSink      | FakeCommandSink
INodeModel             | FakeNodeModel
IPinModel              | FakePinModel
ILinkModel             | FakeLinkModel
ICommentModel          | FakeCommentModel
IAttachmentModel       | FakeAttachmentModel
IContainerNodeModel    | FakeContainerModel
IEditorHostServices    | FakeHostServices
INodeCatalog           | FakeNodeCatalog
ITypeSystem            | FakeTypeSystem
ILinkValidator         | FakeLinkValidator
IEditorTheme           | FakeEditorTheme
IClipboard             | FakeClipboard
IIconProvider          | FakeIconProvider
IInputSource           | FakeInputSource
IDebugSession          | FakeDebugSession
IDiagnosticsSink       | FakeDiagnosticsSink
IMyBlueprintModel      | FakeMyBlueprintModel
IPickerSource (demo)   | FakeNodePickerSource
```

---

## Source Structure

```
NodeEditor.Demo/
+-- FakeBlueprint/
|   +-- FakeAttachmentModel.cs    -- attachment pill data
|   +-- FakeClipboard.cs          -- in-memory clipboard
|   +-- FakeCommandSink.cs        -- applies GraphCommands to FakeGraphModel
|   +-- FakeCommentModel.cs       -- comment box data
|   +-- FakeContainerModel.cs     -- container node extensions
|   +-- FakeDebugSession.cs       -- simulates breakpoints/execution badges
|   +-- FakeDiagnosticsSink.cs    -- logs warnings to console
|   +-- FakeEditorTheme.cs        -- thin wrapper over DefaultTheme
|   +-- FakeGraphContainer.cs     -- multi-tab graph container
|   +-- FakeGraphModel.cs         -- mutable in-memory IGraphModel
|   +-- FakeHostServices.cs       -- IEditorHostServices composition root
|   +-- FakeIconProvider.cs       -- returns empty icon handles
|   +-- FakeInputSource.cs        -- reads ImGui mouse/keyboard state
|   +-- FakeLinkModel.cs          -- link (wire) data
|   +-- FakeLinkValidator.cs      -- type-compatible + no-self-loop rules
|   +-- FakeMyBlueprintModel.cs   -- variables, functions, macro catalog
|   +-- FakeNodeCatalog.cs        -- 40+ node templates for the demo
|   +-- FakeNodeModel.cs          -- node data (title, pins, position, state)
|   +-- FakeNodePickerSource.cs   -- IPickerSource from catalog entries
|   +-- FakePinModel.cs           -- pin data
|   +-- FakeTypeSystem.cs         -- ~15 built-in types (float, int, bool, ...)
+-- Scenarios/
|   +-- Scenario.cs               -- abstract base with Build/Tick + helpers
|   +-- S01_HelloCanvas.cs        -- basic pan/zoom/select
|   +-- S02_DragWireDropToCanvas.cs
|   +-- S03_BoxSelectAndDrag.cs
|   +-- S04_UndoRedo.cs           -- undo/redo stack exercise
|   +-- S05_InlineEditors.cs      -- pin default value editors
|   +-- S06_Reroutes.cs           -- wire reroute waypoints
|   +-- S07_AddNodePicker.cs      -- search picker (compact list)
|   +-- S08_WireDropPicker.cs     -- context picker on wire drop
|   +-- S09_VariablePicker.cs     -- variable reference picker
|   +-- S10_TypePicker.cs         -- type selection picker
|   +-- S11_FlagsEnumMultiPicker.cs
|   +-- S12_AssetGridPicker.cs    -- grid layout picker
|   +-- S13_DebugVizMock.cs       -- execution badge / debug session
|   +-- S15_VariablesGetSet.cs
|   +-- S16_PromoteToVariable.cs  -- refactoring command
|   +-- S17_CustomEvent.cs
|   +-- S18_FunctionAuthoring.cs
|   +-- S19_MultipleReturnNodes.cs
|   +-- S20_MacroWithWildcards.cs
|   +-- S21_EventDispatcher.cs
|   +-- S22_CollapseToFunction.cs -- refactoring command
|   +-- S23_CollapseToMacro.cs
|   +-- S24_ExpandNode.cs
|   +-- S25_MultiTab.cs           -- multi-graph tab management
|   +-- S26_Comments.cs           -- comment boxes
|   +-- S27_NestedComments.cs
|   +-- S28_FindInGraph.cs        -- in-graph search
|   +-- S29_FindInAsset.cs
|   +-- S30_GoToDefinition.cs
|   +-- S31_Bookmarks.cs          -- bookmark store + navigation
|   +-- S32_HotReloadConflict.cs  -- hot-reload conflict badge
|   +-- S33_BigGraph.cs           -- large graph (performance)
|   +-- S34_NodeAttachments.cs    -- attachment pills
|   +-- S35_ContainerNodes.cs     -- nested container graphs
|   +-- S36_CustomRenderers.cs    -- ICustomCanvasRenderer
+-- DemoShell.cs                  -- orchestrator: scenarios + panels + frame loop
+-- HotkeyDispatcher.cs           -- maps key chords to editor commands
+-- Program.cs                    -- Raylib + rlImGui entry point
+-- NodeEditor.Demo.csproj
```

---

## Public API Reference

### DemoShell

```csharp
public sealed class DemoShell
{
    public DemoShell(Dictionary<float, nint>? fonts = null);

    /// <summary>Render one full ImGui frame including all panels and the canvas.</summary>
    public void Render(double elapsed);
}
```

`DemoShell` is a self-contained host. It manages scenario lifecycle, panel layout (left
sidebar + canvas + right panels), and the demo-specific toolbar.

### Scenario (abstract base)

```csharp
public abstract class Scenario
{
    public abstract string Name        { get; }
    public abstract string Description { get; }

    // Called once to populate the graph with initial content
    public abstract void Build(
        GraphView     view,
        FakeGraphModel graph,
        FakeCommandSink sink,
        FakeNodeCatalog catalog);

    // Called every frame; override for animated demos (default: no-op)
    public virtual void Tick(GraphView view, double elapsed) { }

    // Builder helpers
    protected FakeNodeModel AddNode(
        FakeGraphModel graph, FakeNodeCatalog catalog,
        string kindKey, Vector2 position);

    protected void LinkNodes(
        FakeGraphModel graph,
        FakeNodeModel from, int fromPinIndex,
        FakeNodeModel to,   int toPinIndex);
}
```

### FakeGraphModel (reference implementation)

```csharp
public sealed class FakeGraphModel : IGraphModel
{
    public GraphId             Id          { get; }
    public string              DisplayName { get; }
    public GraphKindDescriptor Kind        { get; }

    // IGraphModel collections
    public IReadOnlyCollection<INodeModel>    Nodes    { get; }
    public IReadOnlyCollection<ILinkModel>    Links    { get; }
    public IReadOnlyCollection<ICommentModel> Comments { get; }
    public IReadOnlyCollection<IAttachmentModel> Attachments { get; }

    // Mutation helpers (called by FakeCommandSink)
    public FakeNodeModel AddNode(NodeId id, NodeKindKey kind, string title, Vector2 pos);
    public bool          RemoveNode(NodeId id);
    public FakeLinkModel AddLink(LinkId id, PinId from, PinId to);
    public bool          RemoveLink(LinkId id);
    // ...additional mutators

    public event Action<GraphChangeNotification>? Changed;
}
```

### FakeCommandSink

```csharp
public sealed class FakeCommandSink : IGraphCommandSink
{
    public FakeCommandSink(FakeGraphModel graph);

    /// <summary>
    /// Pattern-matches on GraphCommand discriminated union and applies to FakeGraphModel.
    /// </summary>
    public GraphCommandResult Apply(GraphCommand command);
}
```

---

## Dependencies

| Dependency | Version | Why |
|---|---|---|
| `NodeEditor.UI` | project ref | Canvas rendering + panels |
| `Raylib-cs` | 7.0.2 | Window creation, input, GPU backend |
| `rlImGui-cs` | 3.2.0 | Bridge from Raylib render loop to Dear ImGui |
| `ImGui.NET` | 1.91.6.1 | Direct ImGui calls for demo UI chrome |

---

## Usage Examples

### Example 1: Running the demo

The demo is a standard .NET 8 console executable:

```
cd FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo
dotnet run
```

It opens a 1600x1000 resizable window. Use the left panel to select scenarios;
navigate between them with the arrow buttons or by clicking list entries.

### Example 2: Implementing a minimal host from FakeBlueprint stubs

The `FakeBlueprint` stubs can be used as a host template. Here is a stripped-down
version:

```csharp
// 1. Create graph model
var model = new FakeGraphModel(GraphId.NewId(), "My Graph");

// 2. Create command sink
var sink = new FakeCommandSink(model);

// 3. Compose host services
var host = new FakeHostServices(model, fonts: null);

// 4. Create the GraphView
var view = new GraphView(model, sink, host.LinkValidator,
                         host.TypeSystem, host.NodeCatalog, host);

// 5. Add some nodes
var n1 = model.AddNode(NodeId.NewId(), new NodeKindKey("Event.BeginPlay"),
                       "Begin Play", new Vector2(100, 200));
var n2 = model.AddNode(NodeId.NewId(), new NodeKindKey("Util.Print"),
                       "Print", new Vector2(350, 200));
model.AddLink(LinkId.NewId(), n1.Pins[0].Id, n2.Pins[0].Id);

// 6. Render (inside ImGui frame)
_canvas.Render(view);
```

### Example 3: Writing a custom scenario

```csharp
public sealed class MyScenario : Scenario
{
    public override string Name        => "My Custom Scenario";
    public override string Description => "Demonstrates custom nodes.";

    public override void Build(GraphView view, FakeGraphModel graph,
                               FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Add a custom node template to the catalog first
        catalog.Register(new NodeCatalogEntry(
            Kind:        new NodeKindKey("custom.my_node"),
            DisplayName: "My Node",
            Description: "Does something interesting",
            CategoryPath: "Custom",
            Keywords:    [],
            IconKey:     null,
            IsPure:      true,
            IsLatent:    false,
            IsDeprecated: false,
            Inputs:  [new PinSignature("Value", PinKind.Data, new TypeKey("float"), false)],
            Outputs: [new PinSignature("Result", PinKind.Data, new TypeKey("float"), false)]));

        // Add two of them and wire them
        var a = AddNode(graph, catalog, "custom.my_node", new Vector2(100, 200));
        var b = AddNode(graph, catalog, "custom.my_node", new Vector2(400, 200));
        LinkNodes(graph, a, 1, b, 0);  // output 0 of a -> input 0 of b
    }

    public override void Tick(GraphView view, double elapsed)
    {
        // Animate node state (e.g., toggle Executing badge)
    }
}
```

### Example 4: Extracting FakeInputSource for a different windowing backend

```csharp
// FakeInputSource wraps ImGui state. For other backends, implement IInputSource:
public sealed class MyInputSource : IInputSource
{
    public Vector2 MousePosition { get; private set; }
    public float   WheelDelta    { get; private set; }

    public bool IsMouseDown(MouseButton button)    => /* read from backend */;
    public bool IsMousePressed(MouseButton button) => /* read from backend */;
    public bool IsMouseReleased(MouseButton button)=> /* read from backend */;
    public bool IsKeyDown(int key)                 => /* read from backend */;
    public bool IsKeyPressed(int key)              => /* read from backend */;
    public KeyModifiers Modifiers                  => /* read from backend */;

    public void Update() { /* poll backend each frame */ }
}
```

---

## Best Practices

1. **Use `FakeCommandSink` as the reference `IGraphCommandSink` template** -- it
   demonstrates pattern-matching on all `GraphCommand` variants. Copy and adapt it
   when writing a real host, replacing in-memory dictionary mutations with application
   data-layer calls.

2. **Study scenarios in order** -- S01 through S08 cover fundamentals. S22 through S24
   cover refactoring. S32 covers hot-reload. Understanding each scenario group before
   implementing a feature in a host saves significant integration time.

3. **Font atlas must be rebuilt before `ReloadFonts`** -- see `Program.cs` for the
   correct order: add fonts to `io.Fonts`, then call `rlImGui.ReloadFonts()`. Calling
   `ReloadFonts` without adding fonts loses the custom atlas.

4. **Do not copy `FakeTypeSystem` verbatim** -- the fake has ~15 hard-coded type entries
   sufficient for the demo. Real type systems should build their registry from the
   application's type metadata rather than hard-coding strings.

5. **`FakeDebugSession` shows the debug badge API** -- study S13 to see how
   `IDebugSession.IsExecuting`, `IsRecentlyExecuted`, and watch values interact with
   `NodeState` flags to produce execution visualization overlays.

---

## Scenario Reference

The table below provides a one-line description of each scenario and the key feature it
exercises.

| Scenario | Name | Key Feature |
|---|---|---|
| S01 | Hello Canvas | Pan, zoom, select, drag |
| S02 | Drag Wire Drop to Canvas | Wire drop onto empty canvas triggers picker |
| S03 | Box Select and Drag | Marquee selection, multi-node drag |
| S04 | Undo / Redo | UndoStack, forward/inverse commands |
| S05 | Inline Editors | Pin default-value editors for float, int, bool, string |
| S06 | Reroutes | Insert, move, remove reroute waypoints |
| S07 | Add Node Picker | Search picker in compact list layout |
| S08 | Wire Drop Picker | Context-sensitive picker on wire drop |
| S09 | Variable Picker | Source-driven picker for variable references |
| S10 | Type Picker | Type selection via source-driven picker |
| S11 | Flags Enum Multi-Picker | Multi-select picker for flag enums |
| S12 | Asset Grid Picker | Grid-layout picker for asset references |
| S13 | Debug Viz Mock | Execution badge overlay, FakeDebugSession |
| S15 | Variables Get / Set | Variable get/set node patterns |
| S16 | Promote to Variable | PromoteToVariable refactoring command |
| S17 | Custom Event | Custom event node authoring |
| S18 | Function Authoring | Function graph entry/return nodes |
| S19 | Multiple Return Nodes | Multiple return value patterns |
| S20 | Macro with Wildcards | Wildcard (generic) pin types in macros |
| S21 | Event Dispatcher | Event dispatcher binding/unbinding patterns |
| S22 | Collapse to Function | CollapseToFunction refactoring command |
| S23 | Collapse to Macro | CollapseToMacro refactoring command |
| S24 | Expand Node | ExpandNode command (reverse of collapse) |
| S25 | Multi-Tab | Multiple graph tabs, FakeGraphContainer |
| S26 | Comments | Add, move, resize, rename comment boxes |
| S27 | Nested Comments | Comments containing other comments |
| S28 | Find in Graph | FindBar in-graph search with highlight |
| S29 | Find in Asset | Cross-graph search with FindResultsPanel |
| S30 | Go to Definition | Navigate to definition from reference node |
| S31 | Bookmarks | BookmarkStore, hotkey slots 1-9, tween navigation |
| S32 | Hot Reload Conflict | ChangeBadgeRenderer, conflict resolution UI |
| S33 | Big Graph | Performance: 500+ nodes, spatial culling |
| S34 | Node Attachments | Attachment pills stacked above nodes |
| S35 | Container Nodes | Nested subgraph containers |
| S36 | Custom Renderers | ICustomCanvasRenderer at Pre/Post/Overlay pass |

Note: S14 is absent; that number was reserved and not implemented in the current version.

---

## FakeNodeCatalog: Available Node Kinds

The demo registers the following node kinds, grouped by category:

```
Event/
  Event.BeginPlay      -- execution entry point
  Event.Tick           -- per-frame event
  Event.Custom         -- user-defined event
  Event.Dispatcher     -- event dispatcher

Math/
  Math.Add             -- float add (pure)
  Math.Subtract        -- float subtract (pure)
  Math.Multiply        -- float multiply (pure)
  Math.Lerp            -- linear interpolate (pure)
  Math.Clamp           -- clamp to range (pure)

Flow/
  Flow.Branch          -- boolean branch (if/else)
  Flow.Sequence        -- sequential execution
  Flow.ForLoop         -- integer for loop
  Flow.Delay           -- latent delay node

Variable/
  Variable.Get         -- variable getter
  Variable.Set         -- variable setter

Util/
  Util.Print           -- log to screen
  Util.Concat          -- string concatenation
  Util.Cast            -- type cast
```

---

## Advanced Notes

### Font Pipeline

The demo loads Arial at five sizes (8, 11, 16, 24, 32 px) for smooth text at all zoom
levels. The atlas is built before `rlImGui.ReloadFonts()`:

```
io.Fonts.AddFontDefault()           -- global default (ImGui built-in)
io.Fonts.AddFontFromFileTTF(arial, 8)
io.Fonts.AddFontFromFileTTF(arial, 11)
io.Fonts.AddFontFromFileTTF(arial, 16)
io.Fonts.AddFontFromFileTTF(arial, 24)
io.Fonts.AddFontFromFileTTF(arial, 32)
rlImGui.ReloadFonts()               -- uploads rebuilt atlas to GPU
```

If Arial is not found (non-Windows platforms), the font dictionary stays empty and
`IEditorTheme.GetFontForSize` returns `IntPtr.Zero`, which causes the ImGui default font
to be used at all zoom levels (acceptable for development use).

### ImGui Docking

`rlImGui.Setup(darkTheme: true, enableDocking: true)` enables Dear ImGui docking. The
demo does not use docking itself but enabling it allows hosts that embed the demo canvas
in a larger docked layout to test the canvas inside dock nodes.

---

## Related Projects

| Project | Relationship |
|---|---|
| `NodeEditor.UI` | Direct dependency -- the demo is a consumer of the UI layer |
| `NodeEditor.Core` | Transitive via UI -- all `Core` types used in scenarios |
| `NodeEditor.Primitives` | Transitive -- `NodeId`, `TypeKey`, etc. used everywhere |
| `Fdp.Engine` (FDP) | Parallel host -- uses the same pattern as FakeBlueprint |
