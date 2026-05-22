# Tasks T-20, T-21, T-22 — Demo Application

The demo proves the editor works end-to-end without a real Blueprint
subsystem. It's also the editor's primary visual test harness. After
T-22 the editor is feature-complete.

---

# T-20 — Demo: Fake Host + Scenarios 1–6

## Goal
Build a runnable `NodeEditor.Demo` executable using raylib-cs + rlImGui-cs
that hosts the editor against a "fake blueprint" data model. Implement six
canvas-focused scenarios exercising core editor features.

## Project
`NodeEditor.Demo`

## References

**Specs:** all in `../specs/` (the demo exercises every feature).

**Kernel:** all interfaces in `../kernel/01-interfaces.md`,
`../kernel/04-my-blueprint-and-rest.md`. The fake host implements them.

**Project dep:** `NodeEditor.UI` (and transitively all other layers).

## Deliverables

```
src/NodeEditor.Demo/
    Program.cs                          // raylib main loop
    DemoShell.cs                        // ImGui chrome (toolbar, statusbar, scenario switcher)
    HotkeyDispatcher.cs                 // input source → command invocation
    FakeBlueprint/
        FakeGraphModel.cs               // IGraphModel
        FakeNodeModel.cs                // INodeModel (simple POCO with mutable list)
        FakePinModel.cs                 // IPinModel
        FakeLinkModel.cs                // ILinkModel
        FakeCommentModel.cs             // ICommentModel
        FakeNodeCatalog.cs              // INodeCatalog with 30+ sample node kinds
        FakeTypeSystem.cs               // ITypeSystem with bool/int/float/string/Vector3
        FakeLinkValidator.cs            // ILinkValidator with Unreal-style rules
        FakeCommandSink.cs              // IGraphCommandSink (applies to fake model)
        FakeMyBlueprintModel.cs         // IMyBlueprintModel
        FakeIconProvider.cs             // procedural placeholder icons
        FakeClipboard.cs                // OS clipboard via raylib
        FakeInputSource.cs              // raylib → IInputSource adapter
        FakeEditorTheme.cs              // wraps DefaultTheme
        FakeHostServices.cs             // IEditorHostServices bundle
    Scenarios/
        Scenario.cs                     // abstract: Name, Description, Build(view)
        S01_HelloCanvas.cs              // 3 nodes, 2 wires, verify pan/zoom/select
        S02_DragWireDropToCanvas.cs     // wire drop opens picker
        S03_BoxSelectAndDrag.cs         // marquee + multi-drag
        S04_UndoRedo.cs                 // perform several ops, ctrl+Z, ctrl+Y
        S05_InlineEditors.cs            // node with bool/int/float/Vector3/Color
        S06_Reroutes.cs                 // wire with reroute, drag reroute, delete reroute
```

## raylib + rlImGui startup

```csharp
namespace NodeEditor.Demo;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1600, 1000, "NodeEditor Demo");
        Raylib.SetTargetFPS(60);
        rlImGui.Setup(darkTheme: true, enableDocking: true);

        var demo = new DemoShell();

        while (!Raylib.WindowShouldClose())
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DarkGray);

            rlImGui.Begin();
            demo.Frame();
            rlImGui.End();

            Raylib.EndDrawing();
        }

        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }
}
```

## DemoShell

Builds host services, instantiates the editor surfaces, and renders the
full editor UI each frame.

```csharp
namespace NodeEditor.Demo;

public sealed class DemoShell
{
    private readonly FakeHostServices _host;
    private FakeGraphModel _graph;
    private GraphView _view;
    private CanvasRenderer _canvas;
    private MyBlueprintPanel _myBlueprint;
    private DetailsPanel _details;
    private FindBar _findBar;
    private EditorCommandsImpl _commands;
    private EditorIndicatorsImpl _indicators;
    private HotkeyDispatcher _hotkeys;
    private readonly List<Scenario> _scenarios = new();
    private int _currentScenarioIndex = 0;

    public DemoShell()
    {
        _host = new FakeHostServices();
        _commands = new EditorCommandsImpl();
        _indicators = new EditorIndicatorsImpl(new ToastQueue());
        _graph = new FakeGraphModel(GraphId.NewId(), "EventGraph");
        _view = new GraphView(_graph, _host.CommandSink, _host.LinkValidator,
                              _host.TypeSystem, _host.NodeCatalog, _host);

        // Register all scenarios
        _scenarios.Add(new S01_HelloCanvas());
        _scenarios.Add(new S02_DragWireDropToCanvas());
        // … etc

        // Apply first scenario
        _scenarios[_currentScenarioIndex].Build(_view);

        _canvas = new CanvasRenderer();
        _myBlueprint = new MyBlueprintPanel(_host.MyBlueprint, _host, _commands,
                                            NavigateToGraph, NavigateToItem);
        _details = new DetailsPanel(/* registry */, /* context */);
        _findBar = new FindBar(_view, new FindEngine(_view.Model, null));

        BuiltinCommandHandlers.RegisterAll(_commands, _view, _canvas, _myBlueprint, _findBar);

        _hotkeys = new HotkeyDispatcher(_host.Input, _commands);
    }

    public void Frame()
    {
        ImGui.DockSpaceOverViewport();
        DrawMenuBar();
        DrawScenarioPicker();
        DrawCanvasWindow();
        DrawMyBlueprintWindow();
        DrawDetailsWindow();
        DrawStatusBar();
        DrawToasts();
        _hotkeys.ProcessThisFrame();
    }

    // … each Draw* method opens an ImGui.Begin window and calls the corresponding panel.
}
```

## FakeGraphModel — mutable, simple

Implementation hint: store nodes in `Dictionary<NodeId, FakeNodeModel>`,
links in `Dictionary<LinkId, FakeLinkModel>`. Apply commands in
`FakeCommandSink.Apply(...)` directly:

```csharp
public sealed class FakeCommandSink : IGraphCommandSink
{
    private readonly FakeGraphModel _graph;

    public GraphCommandResult Apply(GraphCommand cmd)
    {
        switch (cmd)
        {
            case GraphCommand.AddNode add:
                _graph.AddNode(add.AssignedId, add.Kind, add.Position, add.InitialProperties);
                return new GraphCommandResult(true, null);

            case GraphCommand.RemoveNodes remove:
                foreach (var id in remove.Nodes) _graph.RemoveNode(id);
                return new GraphCommandResult(true, null);

            case GraphCommand.MoveNodes move:
                foreach (var m in move.Moves) _graph.SetNodePosition(m.Node, m.NewPosition);
                return new GraphCommandResult(true, null);

            case GraphCommand.AddLink link:
                _graph.AddLink(link.AssignedId, link.From, link.To);
                return new GraphCommandResult(true, null);

            // … etc for all command kinds

            case GraphCommand.Batch batch:
                foreach (var inner in batch.Commands) Apply(inner);
                return new GraphCommandResult(true, null);

            default:
                return new GraphCommandResult(false, $"Unhandled command: {cmd.GetType().Name}");
        }
    }
}
```

## FakeNodeCatalog

Hard-code 30+ sample node kinds across categories:

```csharp
public sealed class FakeNodeCatalog : INodeCatalog
{
    public IReadOnlyList<NodeCatalogEntry> All { get; }

    public FakeNodeCatalog()
    {
        var entries = new List<NodeCatalogEntry>();

        // Math
        entries.Add(NewEntry("Math.Multiply", "Multiply", "Math",
            inputs: [Pin("A", "float"), Pin("B", "float")],
            outputs: [Pin("Result", "float")]));
        entries.Add(NewEntry("Math.Add", "Add", "Math", …));
        entries.Add(NewEntry("Math.Subtract", "Subtract", "Math", …));
        entries.Add(NewEntry("Math.Divide", "Divide", "Math", …));

        // Vector
        entries.Add(NewEntry("Math.MultiplyVector", "Vector Multiply", "Math/Vector", …));
        entries.Add(NewEntry("Math.DotProduct", "Dot Product", "Math/Vector", …));

        // Flow control
        entries.Add(NewEntry("Flow.Branch", "Branch", "Flow Control", …));
        entries.Add(NewEntry("Flow.Sequence", "Sequence", "Flow Control", …));
        entries.Add(NewEntry("Flow.ForEachLoop", "For Each Loop", "Flow Control", …));
        entries.Add(NewEntry("Flow.WhileLoop", "While Loop", "Flow Control", …));

        // Events
        entries.Add(NewEntry("Event.BeginPlay", "Begin Play", "Events", …));
        entries.Add(NewEntry("Event.Tick", "Tick", "Events", …));

        // Utility
        entries.Add(NewEntry("Util.Print", "Print String", "Utility", …));
        entries.Add(NewEntry("Util.Delay", "Delay", "Utility", …));

        // … 15 more

        All = entries;
    }

    // Helpers …
}
```

## Scenarios

Each scenario builds a starting graph state:

```csharp
namespace NodeEditor.Demo.Scenarios;

public abstract class Scenario
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract void Build(GraphView view);
}

public sealed class S01_HelloCanvas : Scenario
{
    public override string Name => "01 — Hello Canvas";
    public override string Description => "Three nodes, two wires. Try pan, zoom, select, drag.";

    public override void Build(GraphView view)
    {
        var beginPlay = AddNode(view, "Event.BeginPlay", new Vector2(100, 100));
        var print     = AddNode(view, "Util.Print",      new Vector2(400, 100));
        var delay     = AddNode(view, "Util.Delay",      new Vector2(700, 100));

        AddLink(view, beginPlay, exec_out: 0, print, exec_in: 0);
        AddLink(view, print, exec_out: 0, delay, exec_in: 0);
    }

    // Helpers AddNode, AddLink call into view.Commands directly (bypass undo for setup).
}
```

The scenario picker (drop-down at top of demo window) lets the user switch
between scenarios. Switching rebuilds the graph.

## Acceptance

- `dotnet run --project src/NodeEditor.Demo` opens a window.
- Window shows: menu bar, scenario dropdown, canvas (main), My Blueprint
  (left), Details (right), status bar (bottom).
- S01 shows 3 nodes connected by 2 wires.
- Pan with RMB / MMB, zoom with wheel, drag node, marquee-select, etc.
  All work per spec.
- All 6 scenarios are reachable and demonstrably functional.

## Estimated Size
~800 LOC across fake host + 6 scenarios.

## Status
Pending.

---

# T-21 — Demo: Picker Scenarios 7–12

## Goal
Exercise the picker (T-14) in 6 different invocation contexts.

## Project
`NodeEditor.Demo`

## References
- `../specs/C-picker.md` §C.16 — list of 12 test scenarios; this task
  covers the picker-specific ones.

## Deliverables

```
src/NodeEditor.Demo/
    Scenarios/
        S07_AddNodePicker.cs           // Tab on canvas → Standard layout picker
        S08_WireDropPicker.cs          // Drop wire on empty canvas → contextual picker
        S09_VariablePicker.cs          // Click variable mini-editor → variable picker
        S10_TypePicker.cs              // Pick a Type (nested category tree)
        S11_FlagsEnumMultiPicker.cs    // [Flags] enum → multi-select
        S12_AssetGridPicker.cs         // Grid layout, fake assets
```

## Implementation Approach

Each scenario registers a small `IPickerSource<T>` with the host's
`IPickerRegistry` and binds it to a button or interaction in the demo
chrome.

```csharp
public sealed class S10_TypePicker : Scenario
{
    public override string Name => "10 — Type Picker (nested)";
    public override string Description => "Click button to pick a type. Nested category tree.";

    public override void Build(GraphView view)
    {
        // Just a 'Pick Type' button widget in DemoShell, shown when this scenario active.
    }

    public void OnButtonClick(IPickerRegistry pickers, Vector2 screenPos, ITypeSystem typeSystem)
    {
        var source = new TypePickerSource(typeSystem);
        pickers.Open(
            sourceKey: "demo.types.all",
            screenPos: screenPos,
            onPick: pick =>
            {
                var chosen = (TypeKey)pick;
                Console.WriteLine($"Picked type: {chosen}");
            },
            onCancel: () => Console.WriteLine("Cancelled"));
    }
}

internal sealed class TypePickerSource : IPickerSource<TypeKey>
{
    private readonly ITypeSystem _types;
    public TypePickerSource(ITypeSystem t) { _types = t; }

    public string Title => "Pick a Type";
    public string EmptyResultText => "No matching types.";
    public PickerLayout PreferredLayout => PickerLayout.Standard;
    public PickerSelectionMode SelectionMode => PickerSelectionMode.Single;
    public QueryCost Cost => QueryCost.Cheap;
    public bool IsAsync => false;
    public bool AllowsDragOut => false;
    public bool AllowsDragIn => false;
    public bool AllowArbitraryTextInput => false;

    public IReadOnlyList<TypeKey> Query(string text, IReadOnlyDictionary<string, object?>? context)
    {
        // Return primitives, vectors, structs, etc. filtered by `text`.
        return new[]
        {
            new TypeKey("System.Boolean"),
            new TypeKey("System.Int32"),
            new TypeKey("System.Single"),
            new TypeKey("System.String"),
            new TypeKey("System.Numerics.Vector2"),
            new TypeKey("System.Numerics.Vector3"),
            new TypeKey("System.Numerics.Vector4"),
            new TypeKey("System.Numerics.Quaternion"),
            new TypeKey("NodeEditor.Color"),
        }.Where(t => text.Length == 0 || t.Id.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // … rest of the interface
}
```

## Acceptance

- Switching scenarios in the demo loads one with a "Pick" button.
- Clicking the button opens the picker.
- Each scenario uses a different layout (Standard, Wide, Compact, Tree,
  Grid).
- Multi-select works in S11.
- Favorites and recents are functional within a session.

## Estimated Size
~400 LOC.

## Status
Pending.

---

# T-22 — Demo: Debugger Visualization Mock

## Goal
Wire a mock `IDebugSession` into the demo to exercise the debug-viz code
paths from T-12.

## Project
`NodeEditor.Demo`

## References
- `../instructions/01-spec-brief-part2.md` §25 (debug visualization)
- `../kernel/01-interfaces.md` — `IDebugSession`

## Deliverables

```
src/NodeEditor.Demo/
    FakeBlueprint/
        FakeDebugSession.cs            // implements IDebugSession with scripted state changes
    Scenarios/
        S13_DebugVizMock.cs            // demonstrates breakpoint + executing + recently-executed
```

## FakeDebugSession behavior

- Implements `IDebugSession` with a scripted sequence of executing-node
  changes (cycles through 4 nodes every ~500 ms).
- Tracks 2 breakpoints.
- Periodically pauses for 2 seconds to demonstrate the pause overlay.

```csharp
public sealed class FakeDebugSession : IDebugSession
{
    public bool IsAttached => true;
    public bool IsPaused { get; private set; }
    public NodeId? CurrentlyExecutingNode { get; private set; }
    public IReadOnlySet<NodeId> RecentlyExecutedNodes { get; } = new HashSet<NodeId>();
    public IReadOnlySet<NodeId> Breakpoints { get; } = new HashSet<NodeId>();
    public IReadOnlySet<PinId> WatchedPins { get; } = new HashSet<PinId>();

    private readonly NodeId[] _cycle;
    private int _cycleIndex;
    private TimeSpan _lastAdvance;

    public void Update(TimeSpan now)
    {
        if (IsPaused) return;

        if (now - _lastAdvance > TimeSpan.FromMilliseconds(500))
        {
            _cycleIndex = (_cycleIndex + 1) % _cycle.Length;
            if (CurrentlyExecutingNode is { } prev)
                ((HashSet<NodeId>)RecentlyExecutedNodes).Add(prev);
            CurrentlyExecutingNode = _cycle[_cycleIndex];
            _lastAdvance = now;

            if (Breakpoints.Contains(CurrentlyExecutingNode.Value))
            {
                IsPaused = true;
                StateChanged?.Invoke();
            }
        }
    }

    // … other methods (Continue, StepOver, etc.)

    public event Action? StateChanged;
}
```

## Acceptance

- S13 builds a graph with ~5 nodes; toggle "Attach Debugger" button starts
  the FakeDebugSession.
- Executing node pulses yellow.
- Recently-executed nodes glow briefly.
- Breakpoint markers visible on flagged nodes.
- "Pause" overlay appears when paused.
- "Continue" button resumes the script.

## Estimated Size
~200 LOC.

## Status
Pending.
