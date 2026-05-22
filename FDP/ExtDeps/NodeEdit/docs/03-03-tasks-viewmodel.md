# Tasks T-09 to T-11 — View-Model Layer

The view-model holds **all per-session editor state that does not belong to the host data model**. The host owns the graph (nodes, pins, links). The editor owns: viewport (pan/zoom), selection, hover, drag-in-progress, marquee box, pending wire, etc.

These tasks live in `src/NodeEditor.Core/View/`.

The view-model is purely data — no ImGui calls, no rendering. UI code (T-12+) reads from it and dispatches commands through it.

---

# T-09 — ViewportState and SelectionState

## Goal
Implement two value-typed view-model components:
- **ViewportState** — pan offset, zoom level, computes screen↔graph transforms.
- **SelectionState** — current selection of nodes/links/comments/reroutes with mutation helpers.

## Project
`NodeEditor.Core` (folder: `View/`)

## References
- `../instructions/01-spec-brief.md` §6 (canvas), §11 (selection)
- `../specs/A-canvas-interactions.md` §2 (camera), §4 (selection)
- `../kernel/03-search-spatial-constants.md` (TimingConstants — drag thresholds)

## Deliverables

- `src/NodeEditor.Core/View/ViewportState.cs`
- `src/NodeEditor.Core/View/SelectionState.cs`
- `src/NodeEditor.Core/View/SelectionEntry.cs`

## Implementation

### `ViewportState.cs`

```csharp
using System;
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// Editor viewport: pan (graph-space offset of canvas origin) and zoom (uniform scale).
/// Pure data plus deterministic transforms. No rendering, no input handling.
/// </summary>
public sealed class ViewportState
{
    /// <summary>Graph-space coordinate that maps to the canvas screen origin (top-left of canvas region).</summary>
    public Vector2 PanGraph { get; set; } = Vector2.Zero;

    /// <summary>Uniform scale factor. 1.0 = native. Clamped to [<see cref="MinZoom"/>, <see cref="MaxZoom"/>].</summary>
    public float Zoom { get; private set; } = 1.0f;

    /// <summary>Top-left of the canvas region in screen coordinates (set by the renderer each frame).</summary>
    public Vector2 CanvasScreenOrigin { get; set; } = Vector2.Zero;

    /// <summary>Size of the canvas region in screen pixels (set by the renderer each frame).</summary>
    public Vector2 CanvasScreenSize { get; set; } = Vector2.Zero;

    public const float MinZoom = 0.25f;
    public const float MaxZoom = 3.0f;
    public const float LowZoomThreshold = 0.5f;

    /// <summary>True when zoom is below the simplified-rendering threshold.</summary>
    public bool IsLowZoom => Zoom < LowZoomThreshold;

    /// <summary>Convert a graph-space point to screen coordinates.</summary>
    public Vector2 GraphToScreen(Vector2 graph)
        => CanvasScreenOrigin + (graph - PanGraph) * Zoom;

    /// <summary>Convert a screen-space point to graph coordinates.</summary>
    public Vector2 ScreenToGraph(Vector2 screen)
        => PanGraph + (screen - CanvasScreenOrigin) / Zoom;

    /// <summary>Apply a pan delta in graph-space units.</summary>
    public void Pan(Vector2 deltaGraph) => PanGraph += deltaGraph;

    /// <summary>Apply a pan delta in screen-space pixels (scaled by current zoom).</summary>
    public void PanScreen(Vector2 deltaScreen) => PanGraph += deltaScreen / Zoom;

    /// <summary>
    /// Zoom by a multiplicative factor centered on the given screen position.
    /// The graph point under <paramref name="anchorScreen"/> stays anchored after the zoom.
    /// </summary>
    public void ZoomAt(Vector2 anchorScreen, float factor)
    {
        var anchorGraphBefore = ScreenToGraph(anchorScreen);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var anchorGraphAfter = ScreenToGraph(anchorScreen);
        PanGraph += anchorGraphBefore - anchorGraphAfter;
    }

    /// <summary>Reset the viewport to identity (zoom=1, pan=0).</summary>
    public void Reset()
    {
        PanGraph = Vector2.Zero;
        Zoom = 1.0f;
    }

    /// <summary>
    /// Frame a graph-space rect into the canvas (centered, with margin).
    /// Zoom is clamped to <see cref="MaxZoom"/> so framing a single point doesn't max out.
    /// </summary>
    public void FrameRect(RectF rect, float marginPx = 64f)
    {
        if (CanvasScreenSize.X <= 0 || CanvasScreenSize.Y <= 0) return;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var avail = CanvasScreenSize - new Vector2(marginPx * 2f, marginPx * 2f);
        if (avail.X <= 0 || avail.Y <= 0) return;

        float zx = avail.X / rect.Width;
        float zy = avail.Y / rect.Height;
        Zoom = Math.Clamp(MathF.Min(zx, zy), MinZoom, MaxZoom);

        var rectCenterGraph = new Vector2(rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.5f);
        var canvasCenterScreen = CanvasScreenOrigin + CanvasScreenSize * 0.5f;
        PanGraph = rectCenterGraph - (canvasCenterScreen - CanvasScreenOrigin) / Zoom;
    }
}
```

### `SelectionEntry.cs`

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>Identifies one selectable element in the editor.</summary>
public readonly record struct SelectionEntry
{
    public SelectionEntryKind Kind { get; }
    public NodeId Node { get; }
    public LinkId Link { get; }
    public CommentId Comment { get; }
    public RerouteRef Reroute { get; }

    private SelectionEntry(SelectionEntryKind k, NodeId n, LinkId l, CommentId c, RerouteRef r)
    {
        Kind = k; Node = n; Link = l; Comment = c; Reroute = r;
    }

    public static SelectionEntry OfNode(NodeId id) =>
        new(SelectionEntryKind.Node, id, LinkId.Empty, CommentId.Empty, default);

    public static SelectionEntry OfLink(LinkId id) =>
        new(SelectionEntryKind.Link, NodeId.Empty, id, CommentId.Empty, default);

    public static SelectionEntry OfComment(CommentId id) =>
        new(SelectionEntryKind.Comment, NodeId.Empty, LinkId.Empty, id, default);

    public static SelectionEntry OfReroute(RerouteRef r) =>
        new(SelectionEntryKind.Reroute, NodeId.Empty, LinkId.Empty, CommentId.Empty, r);
}

public enum SelectionEntryKind { Node, Link, Comment, Reroute }
```

### `SelectionState.cs`

```csharp
using System.Collections.Generic;
using System.Linq;
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// Mutable selection of editor elements. Equality is by identity, not order.
/// Methods follow the click-modifier semantics in the canvas spec:
/// no modifier → replace; Shift → add; Ctrl → toggle.
/// </summary>
public sealed class SelectionState
{
    private readonly HashSet<SelectionEntry> _items = new();

    public IReadOnlyCollection<SelectionEntry> Items => _items;
    public int Count => _items.Count;
    public bool IsEmpty => _items.Count == 0;

    public bool Contains(SelectionEntry e) => _items.Contains(e);

    public IEnumerable<NodeId> Nodes =>
        _items.Where(e => e.Kind == SelectionEntryKind.Node).Select(e => e.Node);
    public IEnumerable<LinkId> Links =>
        _items.Where(e => e.Kind == SelectionEntryKind.Link).Select(e => e.Link);
    public IEnumerable<CommentId> Comments =>
        _items.Where(e => e.Kind == SelectionEntryKind.Comment).Select(e => e.Comment);
    public IEnumerable<RerouteRef> Reroutes =>
        _items.Where(e => e.Kind == SelectionEntryKind.Reroute).Select(e => e.Reroute);

    /// <summary>Replace the selection with exactly one entry.</summary>
    public void ReplaceWith(SelectionEntry entry)
    {
        _items.Clear();
        _items.Add(entry);
    }

    /// <summary>Replace the selection with a set of entries.</summary>
    public void ReplaceWith(IEnumerable<SelectionEntry> entries)
    {
        _items.Clear();
        foreach (var e in entries) _items.Add(e);
    }

    /// <summary>Add an entry (Shift+click semantics).</summary>
    public void Add(SelectionEntry entry) => _items.Add(entry);

    /// <summary>Add many entries.</summary>
    public void AddRange(IEnumerable<SelectionEntry> entries)
    {
        foreach (var e in entries) _items.Add(e);
    }

    /// <summary>Toggle an entry (Ctrl+click semantics).</summary>
    public void Toggle(SelectionEntry entry)
    {
        if (!_items.Add(entry)) _items.Remove(entry);
    }

    /// <summary>Remove an entry if present.</summary>
    public void Remove(SelectionEntry entry) => _items.Remove(entry);

    /// <summary>Clear the selection.</summary>
    public void Clear() => _items.Clear();
}
```

## Acceptance
- All files compile.
- Add tests in `tests/NodeEditor.Core.Tests/View/ViewportStateTests.cs`:
    - `GraphToScreen_then_ScreenToGraph_RoundTrips` (uses identity viewport, then non-identity).
    - `ZoomAt_KeepsAnchorPointStable` (graph point under the anchor screen position is unchanged across zoom).
    - `ZoomAt_ClampedToMin` / `ZoomAt_ClampedToMax`.
    - `FrameRect_CentersRect` (rect center maps to canvas center after framing).
- Add tests in `tests/NodeEditor.Core.Tests/View/SelectionStateTests.cs`:
    - `ReplaceWith_Single_HasOneItem`.
    - `Add_AddsWithoutRemovingOthers`.
    - `Toggle_AddsThenRemoves`.
    - `Nodes_Filters` (mixed entries → only node IDs returned).

## Estimated Size
- ViewportState ~120 LOC, SelectionState ~90 LOC, SelectionEntry ~30 LOC, tests ~150 LOC.

## Status
Pending.

---

# T-10 — InteractionState

## Goal
Hold all per-frame and across-frames **transient interaction state**: hover, drag-in-progress, marquee box, pending wire, picker visibility, etc. This is the canvas state machine's backing store.

## Project
`NodeEditor.Core` (folder: `View/`)

## References
- `../instructions/01-spec-brief.md` §12 (state machine)
- `../specs/A-canvas-interactions.md` §1 (states), §3 (drag), §5 (marquee), §6 (pending wire)

## Deliverables

- `src/NodeEditor.Core/View/InteractionState.cs`
- `src/NodeEditor.Core/View/InteractionMode.cs`
- `src/NodeEditor.Core/View/HoverInfo.cs`
- `src/NodeEditor.Core/View/PendingWire.cs`

## Implementation

### `InteractionMode.cs`

```csharp
namespace NodeEditor.Core.View;

/// <summary>
/// Top-level state of the canvas interaction state machine.
/// Exactly one mode is active at any time.
/// </summary>
public enum InteractionMode
{
    /// <summary>Mouse is hovering or idle; clicks may trigger transitions.</summary>
    Idle,
    /// <summary>RMB-drag panning.</summary>
    Panning,
    /// <summary>Selected nodes are being dragged (drag threshold has been crossed).</summary>
    DraggingNodes,
    /// <summary>One or more reroute waypoints are being dragged.</summary>
    DraggingReroutes,
    /// <summary>A comment box is being moved.</summary>
    DraggingComment,
    /// <summary>A comment box is being resized.</summary>
    ResizingComment,
    /// <summary>LMB-drag from empty canvas is drawing a marquee selection rect.</summary>
    MarqueeSelecting,
    /// <summary>LMB-drag from a pin is drawing a pending connection wire.</summary>
    PendingWire,
    /// <summary>The contextual node-creation picker is open and consuming input.</summary>
    PickerOpen
}
```

### `HoverInfo.cs`

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// What the cursor is currently over. Computed every frame by the canvas renderer
/// during hit-testing and consumed by event-handling code.
/// Mutually exclusive: only one of the IDs is non-empty.
/// </summary>
public readonly record struct HoverInfo
{
    public HoverKind Kind { get; init; }
    public NodeId Node { get; init; }
    public PinId Pin { get; init; }
    public LinkId Link { get; init; }
    public CommentId Comment { get; init; }
    public RerouteRef Reroute { get; init; }
    /// <summary>For comments: whether the cursor is on the title bar (drag), the body, or a resize handle.</summary>
    public CommentHoverZone CommentZone { get; init; }

    public static HoverInfo None => default;
}

public enum HoverKind { None, Node, Pin, Link, Comment, Reroute }

public enum CommentHoverZone { None, Header, Body, ResizeHandle }
```

### `PendingWire.cs`

```csharp
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// State for a wire currently being dragged from a source pin.
/// While set, <see cref="InteractionMode.PendingWire"/> is active.
/// </summary>
public sealed class PendingWire
{
    /// <summary>Pin the drag started from.</summary>
    public required PinId SourcePin { get; init; }

    /// <summary>Current mouse position in graph space (updated every frame).</summary>
    public Vector2 CursorGraph { get; set; }

    /// <summary>
    /// Optional candidate target pin under the cursor (within snap radius).
    /// Snap radius defined in <c>TimingConstants.PinSnapRadiusPx</c>.
    /// </summary>
    public PinId? CandidateTarget { get; set; }

    /// <summary>Whether the candidate is a valid connection per the validator.</summary>
    public bool CandidateValid { get; set; }

    /// <summary>Whether the candidate would require an auto-cast (validator returned ValidWithCast).</summary>
    public bool CandidateNeedsCast { get; set; }
}
```

### `InteractionState.cs`

```csharp
using System.Collections.Generic;
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// All transient editor state that is not in the host data model and not in the viewport.
/// Includes the current interaction mode, what the cursor is over, drag bookkeeping
/// (per-node graph-space overrides during a drag), and the pending-wire descriptor.
/// </summary>
public sealed class InteractionState
{
    public InteractionMode Mode { get; set; } = InteractionMode.Idle;

    public HoverInfo Hover { get; set; } = HoverInfo.None;

    /// <summary>Screen position where the current LMB drag (if any) began.</summary>
    public Vector2 DragStartScreen { get; set; }

    /// <summary>Graph-space position where the current LMB drag began.</summary>
    public Vector2 DragStartGraph { get; set; }

    /// <summary>True once the cursor has moved past the drag threshold since LMB-down.</summary>
    public bool DragThresholdCrossed { get; set; }

    /// <summary>Marquee rect in graph space (only valid while Mode == MarqueeSelecting).</summary>
    public RectF MarqueeGraph { get; set; }

    /// <summary>Whether the marquee uses touch (Alt) instead of fully-enclosed mode.</summary>
    public bool MarqueeTouchMode { get; set; }

    /// <summary>
    /// Per-node graph-space position overrides while a drag is in progress.
    /// The renderer reads from here in preference to the host model; on mouse-up
    /// the final positions are flushed via a single MoveNodes command and this dict is cleared.
    /// </summary>
    public Dictionary<NodeId, Vector2> DragOverridePositions { get; } = new();

    /// <summary>Snapshot of nodes that are dragged together with a comment ("contained" set, captured at drag-start).</summary>
    public HashSet<NodeId> CommentDragContents { get; } = new();

    /// <summary>The pending-wire descriptor, set while Mode == PendingWire.</summary>
    public PendingWire? PendingWire { get; set; }

    /// <summary>Screen position of the right-click that opened a context menu (if any).</summary>
    public Vector2? ContextMenuScreen { get; set; }

    /// <summary>Reset to Idle: clears mode, drag overrides, marquee, pending wire.</summary>
    public void ResetToIdle()
    {
        Mode = InteractionMode.Idle;
        DragThresholdCrossed = false;
        DragOverridePositions.Clear();
        CommentDragContents.Clear();
        MarqueeGraph = default;
        MarqueeTouchMode = false;
        PendingWire = null;
        ContextMenuScreen = null;
    }
}
```

## Acceptance
- Compiles.
- Add `tests/NodeEditor.Core.Tests/View/InteractionStateTests.cs`:
    - `ResetToIdle_ClearsDragOverrides`.
    - `Default_ModeIsIdle`.
    - `Hover_DefaultIsNone`.

## Estimated Size
- ~250 LOC across all files, ~50 LOC tests.

## Status
Pending.

---

# T-11 — GraphView Aggregator

## Goal
A single class that wires together the host model, host services, viewport, selection, interaction state, and command sink. This is the **only object the UI layer needs to be handed** — it is the editor's "instance".

## Project
`NodeEditor.Core` (folder: `View/`)

## References
- `../kernel/01-interfaces.md` — all host interfaces
- `../instructions/01-spec-brief.md` §3 (host contract), §5 (view-model)

## Deliverables

- `src/NodeEditor.Core/View/GraphView.cs`

## Implementation

```csharp
using NodeEditor.Core.Commands;
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// Top-level aggregator for a single graph being edited.
/// Holds references to the host (read-only model + services), and owns the editor-side
/// transient state (viewport, selection, interaction). Hands itself to the UI layer.
/// Editor mutations always go through <see cref="Commands"/>; the editor never writes to <see cref="Model"/> directly.
/// </summary>
public sealed class GraphView
{
    /// <summary>Host-provided read-only view of the graph data.</summary>
    public IGraphModel Model { get; }

    /// <summary>Host command sink. All mutations go here.</summary>
    public IGraphCommandSink Commands { get; }

    /// <summary>Connection validation rules.</summary>
    public ILinkValidator Validator { get; }

    /// <summary>Type system (colors, compatibility, cast resolution).</summary>
    public ITypeSystem TypeSystem { get; }

    /// <summary>Node catalog (right-click menu, contextual picker, search).</summary>
    public INodeCatalog Catalog { get; }

    /// <summary>Host services bag (clipboard, icons, diagnostics, debug session, theme, picker registry, input).</summary>
    public IEditorHostServices Host { get; }

    /// <summary>Viewport (pan/zoom).</summary>
    public ViewportState Viewport { get; } = new();

    /// <summary>Selection set.</summary>
    public SelectionState Selection { get; } = new();

    /// <summary>Transient interaction state.</summary>
    public InteractionState Interaction { get; } = new();

    /// <summary>
    /// Undo/redo stack. Owned by the editor (not the host) so the editor can group
    /// multi-step authoring actions into single user-visible operations.
    /// </summary>
    public UndoStack Undo { get; } = new();

    public GraphView(
        IGraphModel model,
        IGraphCommandSink commands,
        ILinkValidator validator,
        ITypeSystem typeSystem,
        INodeCatalog catalog,
        IEditorHostServices host)
    {
        Model = model;
        Commands = commands;
        Validator = validator;
        TypeSystem = typeSystem;
        Catalog = catalog;
        Host = host;
    }

    /// <summary>Convenience: dispatch a single command and push its inverse on the undo stack.</summary>
    public void Execute(GraphCommand command)
    {
        var inverse = CommandBuilder.BuildInverse(Model, command);
        Commands.Dispatch(command);
        Undo.Push(new UndoEntry(command, inverse));
    }

    /// <summary>Execute multiple commands as a single undo unit ("batch").</summary>
    public void ExecuteBatch(string label, params GraphCommand[] commands)
    {
        var inverses = new GraphCommand[commands.Length];
        for (int i = 0; i < commands.Length; i++)
            inverses[i] = CommandBuilder.BuildInverse(Model, commands[i]);

        foreach (var c in commands) Commands.Dispatch(c);
        Undo.PushBatch(label, commands, inverses);
    }

    /// <summary>Undo the most recent operation (if any).</summary>
    public void UndoLast()
    {
        var entry = Undo.PopUndo();
        if (entry is null) return;
        foreach (var c in entry.Inverses) Commands.Dispatch(c);
    }

    /// <summary>Redo the most recently undone operation (if any).</summary>
    public void RedoLast()
    {
        var entry = Undo.PopRedo();
        if (entry is null) return;
        foreach (var c in entry.Commands) Commands.Dispatch(c);
    }
}
```

> **Note on `UndoStack` API:** the inverse-snapshot pattern in `kernel/02-commands-and-undo.md` already
> defines `Push(UndoEntry)`, `PushBatch`, `PopUndo()`, `PopRedo()`. Re-read that file to mirror the exact
> signatures and the `UndoEntry`/batch record shapes; adapt the helper methods above if names differ.

## Acceptance
- Compiles against the host interfaces from T-03.
- Add `tests/NodeEditor.Core.Tests/View/GraphViewTests.cs`:
    - Construct a `GraphView` from a minimal fake host (use a simple stub `IGraphModel` returning empty enumerables and a fake `IGraphCommandSink` that records dispatched commands).
    - `Execute_DispatchesCommand` — assert the fake sink saw the command.
    - `Execute_PushesInverseToUndo` — assert undo stack count increments.
    - `UndoLast_DispatchesInverse` — assert the inverse command shows up in the sink.

## Estimated Size
- ~200 LOC, ~150 LOC tests.

## Status
Pending.
