# Editor-Internal Interfaces

This file describes the editor's internal building blocks — the contracts between subsystems *inside* the editor library. None of these are implemented by the host. Their full source lives in `04_kernel_code/K03_editor_interfaces.md`.

## The composition

```
                    NodeEditorInstance
                       │
       ┌───────────────┼─────────────────────────────┐
       ▼               ▼                             ▼
    EditorSession   GraphView (per-tab)        PickerWindow
       │           ┌──┴──┬─────┬─────┬──────┐         │
       │           ▼     ▼     ▼     ▼      ▼        ▼
       │     Viewport  Selection InteractionDispatcher
       │     State    State   ▲      SpatialIndex    │
       │                      │                       │
       └────────────────►  EditorCommands  ◄──────────┘
                                │
                                ▼
                       IGraphCommandSink (host)
```

Top-down:

- **NodeEditorInstance** — root object. Created by the host with all its services. Single instance per editor session.
- **EditorSession** — globally cross-tab state: open tabs, layout, picker registry, undo stacks per graph, clipboard.
- **GraphView** — one per open tab; owns viewport, selection, interaction state, spatial index for that graph.
- **InteractionDispatcher** — the canvas's input → command pipeline for one GraphView.
- **PickerWindow** — singleton; only one picker open at a time.
- **EditorCommands** — `IEditorCommands` implementation; routes host commands to the active GraphView.

## `NodeEditorInstance`

```csharp
public sealed class NodeEditorInstance
{
    public NodeEditorInstance(HostServices host, NodeEditorOptions options);

    public IEditorCommands Commands { get; }
    public IEditorIndicators Indicators { get; }
    public IPickerRegistry Pickers { get; }
    public EditorSession Session { get; }

    public GraphView? ActiveView { get; }   // null if no graph open
    public PickerWindow Picker { get; }

    /// Called by host once per frame, before rendering.
    public void Update(float deltaSeconds);

    /// Called by host once per frame to render. The host's window must be open
    /// and an ImGui frame must be active. The editor draws into the current
    /// ImGui context.
    public void Render();

    /// Save current session state to disk.
    public void SaveSession();
}

public sealed record NodeEditorOptions(
    bool EnableMinimap = false,
    bool EnableGrid = true,
    bool EnableSnapToGrid = false,
    bool EnableAlignmentGuides = true,
    float MaxZoom = 3.0f,
    float MinZoom = 0.25f);

public sealed record HostServices(
    IEditorHostServices Host,
    INodeCatalog Catalog,
    ITypeSystem TypeSystem,
    ILinkValidator Validator,
    IGraphCommandSink CommandSink,
    IInputSource Input,
    IClipboard? Clipboard = null,
    IIconProvider? Icons = null,
    IDiagnosticsSink? Diagnostics = null,
    IDebugSession? Debug = null,
    IEditorTheme? Theme = null,
    IExternalChangeNotifier? ExternalChanges = null,
    IEditorReadOnlyState? ReadOnly = null,
    IMyBlueprintModel? MyBlueprint = null);
```

## `EditorSession`

Cross-tab and cross-graph state.

```csharp
public sealed class EditorSession
{
    public IReadOnlyList<GraphTab> Tabs { get; }
    public GraphTab? ActiveTab { get; }

    public GraphTab OpenTab(GraphId id, IGraphModel model);
    public void CloseTab(GraphTab tab);
    public void ActivateTab(GraphTab tab);

    public UndoStack GetUndoStackFor(GraphId graph);
    public BookmarkStore Bookmarks { get; }      // per asset
    public ClipboardManager Clipboard { get; }

    public void Persist(string path);
    public void Restore(string path);

    public event Action? TabsChanged;
    public event Action? ActiveTabChanged;
}

public sealed class GraphTab
{
    public GraphId GraphId { get; }
    public IGraphModel Model { get; }
    public GraphView View { get; }
    public string DisplayName { get; }
    public bool IsDirty { get; }
    public int ErrorCount { get; }
}
```

## `GraphView` — per tab

```csharp
public sealed class GraphView
{
    public IGraphModel Model { get; }
    public ViewportState Viewport { get; }
    public SelectionState Selection { get; }
    public InteractionDispatcher Interaction { get; }
    public SpatialIndex Spatial { get; }
    public DragOverrideMap DragOverrides { get; }

    public Rect2 ComputeContentBounds();
    public Vector2 ComputeContentCenter();

    public void Update(float deltaSeconds);
    public void OnModelChanged(GraphChangeEvent evt);
}
```

The `GraphView` is a composition root for everything that's per-graph. Its `Update` advances viewport animations and processes pending interaction state changes.

## `InteractionDispatcher`

The top-level canvas state machine. Full source in `K07_interaction_dispatcher.md`.

```csharp
public sealed class InteractionDispatcher
{
    public InteractionState State { get; }

    public void Update(InteractionInputs inputs, GraphView view, HostServices host);

    public IReadOnlyList<GraphCommand>? FlushPendingCommands();
    public event Action<InteractionState>? StateChanged;
}

public readonly record struct InteractionInputs(
    Vector2 MouseCanvas,
    Vector2 MouseScreen,
    Vector2 MouseDelta,
    float MouseWheel,
    KeyModifiers Modifiers,
    bool LeftPressed, bool LeftDown, bool LeftReleased,
    bool MiddlePressed, bool MiddleDown, bool MiddleReleased,
    bool RightPressed, bool RightDown, bool RightReleased,
    bool IsCapturedByUi);
```

`InteractionInputs` is a frame-snapshot of all relevant input. The dispatcher reads this + the model and updates its state. Any side-effects (commands) are queued for the host loop to flush.

The dispatcher's `Update` is the single function with the most behavior in the editor. It dispatches based on current `InteractionState` (Idle, Hover*, Drag*, BoxSelect, etc.) and produces transitions. The contained logic implements every rule from `A_canvas_interactions.md`.

## `SpatialIndex`

```csharp
public sealed class SpatialIndex
{
    public SpatialIndex(IGraphModel model, ITypeSystem typeSystem, float cellSize = 64f);

    public void Rebuild();
    public void OnModelChanged(GraphChangeEvent evt);

    /// Hit-test in priority order. Returns the first match found.
    public HitResult HitTest(Vector2 canvasPoint, float pinSnapRadius = 14f);

    /// Enumerate all entities whose AABB intersects the rect.
    public void Query(Rect2 rect, SpatialQueryResults output);

    /// Just the visible nodes.
    public void QueryVisibleNodes(Rect2 viewport, List<NodeId> output);
}

public abstract record HitResult
{
    public sealed record None : HitResult;
    public sealed record Node(NodeId Id) : HitResult;
    public sealed record Pin(PinId Id) : HitResult;
    public sealed record Wire(LinkId Id, float TForPosition) : HitResult;
    public sealed record CommentHeader(CommentId Id) : HitResult;
    public sealed record CommentBody(CommentId Id) : HitResult;
    public sealed record CommentResize(CommentId Id, ResizeHandle Handle) : HitResult;
    public sealed record Reroute(LinkId Link, RerouteId Reroute) : HitResult;
}

public sealed class SpatialQueryResults
{
    public List<NodeId> Nodes { get; } = new();
    public List<LinkId> Links { get; } = new();
    public List<CommentId> Comments { get; } = new();
    public List<(LinkId, RerouteId)> Reroutes { get; } = new();
    public void Clear();
}
```

Full implementation: `K06_spatial_index.md`.

## `UndoStack`

```csharp
public sealed class UndoStack
{
    public bool CanUndo { get; }
    public bool CanRedo { get; }
    public int UndoCount { get; }
    public int RedoCount { get; }
    public string? UndoLabel { get; }   // for menu items "Undo: Move 5 Nodes"
    public string? RedoLabel { get; }

    public void Push(UndoEntry entry);
    public void Begin(string label);
    public void End();                  // commits the batch
    public void Cancel();               // discards the open batch

    public void Undo(IGraphCommandSink sink);
    public void Redo(IGraphCommandSink sink);

    public void Clear();

    public event Action? Changed;
}

public sealed record UndoEntry(
    string Label,
    GraphCommand Forward,
    GraphCommand Inverse);
```

The undo stack pushes entries with both forward and inverse commands. Multi-step operations (drag, batch) wrap in `Begin` / `End`. Full impl in `K05_undo_stack.md`.

## `PickerWindow`

Singleton; one picker open at a time across the whole editor.

```csharp
public sealed class PickerWindow
{
    public bool IsOpen { get; }
    public PickerContext? Current { get; }

    public void Open<TItem>(
        IPickerSource<TItem> source,
        Vector2 screenPos,
        Action<TItem> onPick,
        Action? onCancel = null,
        string? initialQuery = null,
        IReadOnlyDictionary<string, object?>? context = null);

    public void Close();
    public void Cancel();

    public void Update(float deltaSeconds);
    public void Draw();

    public event Action? Opened;
    public event Action? Closed;
}
```

Full picker source contract in `C_generic_picker.md`.

## `EditorCommands` — concrete `IEditorCommands` implementation

Routes named commands to internal handlers. The full catalog with all built-in commands is in `K09_command_catalog.md`. Each command is registered with:

```csharp
public sealed class EditorCommandRegistry : IEditorCommands
{
    public void Register(EditorCommandDescriptor descriptor, Func<EditorCommandContext?, EditorCommandResult> handler);
    public void Unregister(string commandId);

    public IReadOnlyList<EditorCommandDescriptor> All { get; }
    public EditorCommandDescriptor? Get(string id);
    public EditorCommandResult Invoke(string id, EditorCommandContext? ctx = null);

    public event Action<string>? AvailabilityChanged;
}
```

## `EditorIndicators`

Read-only status snapshot. Maintained internally; published to host via `IEditorIndicators`.

```csharp
public sealed class EditorIndicators : IEditorIndicators
{
    public EditorStatusSnapshot Snapshot { get; private set; }
    public event Action? Changed;

    public void Notify(EditorNotification notification);

    internal void Update();   // called by main loop each frame
}
```

`Update` reads current state from `GraphView`, `EditorSession`, etc. and rebuilds the snapshot. Fires `Changed` if anything in the snapshot differs from last frame.

## Interaction flow at a high level

```
host calls Update(dt):
    foreach tab:
        tab.View.Update(dt)
            - viewport animation step
    activeTab.View.Interaction.Update(inputs, view, host)
        - reads inputs
        - updates state machine
        - possibly emits commands → pending queue
    pendingCommands = activeTab.View.Interaction.FlushPendingCommands()
    for cmd in pendingCommands:
        sink.Apply(cmd) → host applies, fires Changed
        undoStack.Push(...) with inverse from sink
    pickerWindow.Update(dt)
    indicators.Update()

host calls Render():
    canvasRenderer.Draw(activeTab.View)
        - hit-tested decorations
        - nodes
        - wires
        - selection
        - debug visuals
    panels.Draw()
    pickerWindow.Draw()    (if open, drawn over everything)
```

## Per-graph vs editor-global state — visualized

```
NodeEditorInstance (editor-global)
├── Commands             (global)
├── Indicators           (global)
├── Pickers              (global registry)
├── Session              (global)
│   ├── Tabs[]           ← list of GraphTabs
│   ├── Bookmarks        (per-asset, indexed inside)
│   ├── Clipboard        (global)
│   └── UndoStacks{}     (per-graph)
└── PickerWindow         (global; only one open)

GraphTab[i] (per-graph)
├── Model                (host's IGraphModel)
└── View
    ├── Viewport         (per-tab)
    ├── Selection        (per-tab)
    ├── Interaction      (per-tab)
    ├── Spatial          (per-tab)
    └── DragOverrides    (per-tab)
```

## Testability

Each of these internals is constructed via simple public constructors. Tests instantiate the parts they need with mock `HostServices`, drive `Update` with synthetic `InteractionInputs`, and assert on command output.

The `IInputSource` abstraction is essential here — tests use a `FakeInputSource` to script gestures.

The full test strategy lives in `code_conventions.md` and `task_list.md`.
