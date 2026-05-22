# Data Model

## Identity types

All identity is GUID-wrapped record struct. This pattern (same as your existing `EditNodeId`) gives:

- Strong typing (can't pass a NodeId where a PinId is expected).
- Cheap copies (16 bytes, no allocation).
- Stable across serialization.
- Dictionary-friendly.

Full source in `04_kernel_code/K01_primitives.md`.

The set:

| Type | Purpose |
|---|---|
| `NodeId` | Identifies a node within a graph |
| `PinId` | Identifies a pin globally (host's choice of uniqueness) |
| `LinkId` | Identifies a wire/link |
| `RerouteId` | Identifies a reroute within a link |
| `GraphId` | Identifies a graph (event graph, function, custom event, macro body) |
| `CommentId` | Identifies a comment box |
| `TypeKey` | Identifies a data type (e.g., "System.Single", or host-defined) |
| `NodeKindKey` | Identifies a node kind in the catalog |

Why `TypeKey` is string-keyed (in a wrapper) rather than `Type`: the editor cannot assume CLR types. Host catalogs may describe blueprint-only types (engine struct types, etc.) that don't correspond to a CLR `Type` at all.

## The two-tier model

There's a strict split between **what the host owns** (the asset model) and **what the editor owns** (the view-model).

### Host-owned (the asset)

The host implements `IGraphModel` over its own data structures. The editor never directly mutates these.

```csharp
public interface IGraphModel
{
    IReadOnlyList<INodeModel> Nodes { get; }
    IReadOnlyList<ILinkModel> Links { get; }
    IReadOnlyList<ICommentModel> Comments { get; }
    IReadOnlyList<IGraphModel> Subgraphs { get; }

    INodeModel? GetNode(NodeId id);
    IPinModel? GetPin(PinId id);
    ILinkModel? GetLink(LinkId id);
    ICommentModel? GetComment(CommentId id);

    string Name { get; }
    GraphId Id { get; }

    event Action? Changed;
    event Action<GraphChangeEvent>? ChangedDetailed;
}
```

`Changed` fires after any mutation. `ChangedDetailed` carries info about what changed for fine-grained re-rendering:

```csharp
public sealed record GraphChangeEvent(
    IReadOnlyList<NodeId>? AddedNodes,
    IReadOnlyList<NodeId>? RemovedNodes,
    IReadOnlyList<NodeId>? MovedNodes,
    IReadOnlyList<LinkId>? AddedLinks,
    IReadOnlyList<LinkId>? RemovedLinks,
    IReadOnlyList<CommentId>? AddedComments,
    IReadOnlyList<CommentId>? RemovedComments);
```

Empty lists are null (don't allocate empties).

### Editor-owned (the view-model)

These exist only in editor memory, never persisted to the asset:

| Class | Purpose | Per-tab or global? |
|---|---|---|
| `GraphView` | Composes everything below for one open graph | Per-tab |
| `ViewportState` | Pan, zoom, animated transitions | Per-tab |
| `SelectionState` | Currently selected nodes, comments, reroutes | Per-tab |
| `InteractionState` | The state machine for active drag/hover | Per-tab |
| `DragOverrideMap` | Temporary position overrides during drag | Per-tab |
| `SpatialIndex` | Coarse-grid hit-test structure | Per-tab |
| `BookmarkStore` | Saved viewports | Per-asset |
| `UndoStack` | Per-graph undo history | Per-graph (asset-wide visibility) |
| `ClipboardManager` | OS clipboard interop + payload caching | Global |
| `FuzzyMatchHistory` | Favorites + recents per source key | Global |
| `EditorSession` | Tab list, layout, persistence | Global |

Their full interfaces are in `K03_editor_interfaces.md`.

## Node / pin / link contracts

The host implements these. Read-only from the editor's perspective.

### Node

```csharp
public interface INodeModel
{
    NodeId Id { get; }
    NodeKindKey Kind { get; }
    string Title { get; }
    string? Subtitle { get; }
    Vector2 Position { get; }
    Vector2? Size { get; }                 // null = computed from content
    Vector4 HeaderColor { get; }
    string? IconKey { get; }

    IReadOnlyList<IPinModel> InputPins { get; }
    IReadOnlyList<IPinModel> OutputPins { get; }

    bool IsCollapsed { get; }
    bool IsDisabled { get; }
    bool ShowAdvancedPins { get; }

    string? CommentText { get; }           // tooltip-like, attached to node
}
```

### Pin

```csharp
public interface IPinModel
{
    PinId Id { get; }
    NodeId OwnerNode { get; }
    string Name { get; }
    string DisplayName { get; }
    string? Tooltip { get; }
    TypeKey Type { get; }
    PinDirection Direction { get; }        // Input | Output
    PinKind Kind { get; }                  // Data | Exec
    bool IsAdvanced { get; }
    bool IsHidden { get; }

    object? DefaultValue { get; }          // for unconnected input data pins
    PinDefaultMetadata? Metadata { get; }  // range, step, units, etc.

    bool IsSplit { get; }                  // struct pin split into sub-pins
}
```

Note `AcceptsMultipleConnections` is **not** a property — it's computed from `Direction` and `Kind` (see `A_canvas_interactions.md §A.6` "Wire merge / replace rules").

### Link

```csharp
public interface ILinkModel
{
    LinkId Id { get; }
    PinId FromPin { get; }
    PinId ToPin { get; }
    IReadOnlyList<RerouteWaypoint> Reroutes { get; }
}

public sealed record RerouteWaypoint(RerouteId Id, Vector2 Position);
```

Reroutes are part of the link, never standalone (confirmed in design — see `D8_comments_and_reroutes.md §D.8.18`).

### Comment

```csharp
public interface ICommentModel
{
    CommentId Id { get; }
    string Text { get; }
    Vector2 Position { get; }
    Vector2 Size { get; }
    Vector4 Color { get; }
    int ZOrder { get; }
    bool MoveWithContents { get; }
}
```

## Why the editor doesn't define structs for these

For each contract, the editor defines only an **interface**, not a struct. The host wraps its own data with adapter classes implementing the interfaces.

Trade-offs:

- **Pro**: hosts can use whatever internal representation suits them — your engine has `BlueprintNode` with channels, generations, history; that goes behind the interface.
- **Pro**: no double bookkeeping — the editor doesn't mirror the host's data.
- **Con**: per-frame access to the model is via virtual dispatch. We accept the cost; pins are read maybe ~hundreds-of-thousands of times per frame across rendering and hit-test, and modern dispatch is fast enough. Spot-fix with caching if profiling shows issues.

## ViewportState — per-tab

```csharp
public sealed class ViewportState
{
    public Vector2 Pan { get; private set; }      // canvas point at viewport center
    public float Zoom { get; private set; }       // 1.0 = native; 0.25..3.0

    public Rect2 VisibleCanvasRect { get; }       // computed from Pan, Zoom, viewport size
    public Vector2 CanvasToScreen(Vector2 canvas);
    public Vector2 ScreenToCanvas(Vector2 screen);

    public void Set(Vector2 pan, float zoom);
    public void AnimateTo(Vector2 pan, float zoom, TimeSpan duration);
    public void Update(float deltaSeconds);       // advances animations

    public bool IsAnimating { get; }
}
```

Pan is in canvas coordinates (logical, infinite). Zoom is a scalar. The transformation to ImGui screen-space is `screen = (canvas - pan) * zoom + viewportCenter`. Inverse: `canvas = (screen - viewportCenter) / zoom + pan`.

## SelectionState — per-tab

```csharp
public sealed class SelectionState
{
    public IReadOnlySet<NodeId> Nodes { get; }
    public IReadOnlySet<CommentId> Comments { get; }
    public IReadOnlySet<(LinkId Link, RerouteId Reroute)> Reroutes { get; }
    public IReadOnlySet<LinkId> Links { get; }    // for "select wires" mode

    public NodeId? Primary { get; }               // for Details panel target
    public bool IsEmpty { get; }

    public void Clear();
    public void Set(NodeId id);                   // replace with single node
    public void Add(NodeId id);
    public void Remove(NodeId id);
    public void Toggle(NodeId id);
    public bool Contains(NodeId id);

    // same family for comments, reroutes, links

    public void ReplaceWithNodes(IEnumerable<NodeId> ids);

    public event Action? Changed;
}
```

Selection is a set, not a list. Order doesn't matter for most operations; primary is tracked separately. The `Primary` is the most-recently-added or most-recently-clicked node — drives the Details panel target.

## DragOverrideMap — ephemeral position overrides

```csharp
public sealed class DragOverrideMap
{
    private Dictionary<NodeId, Vector2>? _overrides;

    public Vector2? Get(NodeId id);
    public bool HasOverride(NodeId id);
    public void Set(NodeId id, Vector2 position);
    public void SetAll(IEnumerable<KeyValuePair<NodeId, Vector2>> overrides);
    public void Clear();
    public bool IsActive { get; }                 // true if any overrides set
}
```

Renderer asks: `var displayPos = dragOverride.Get(node.Id) ?? node.Position;` per frame per node. Constant-time lookup; empty dictionary doesn't allocate.

Cleared at drag end after the `MoveNodes` command is dispatched.

## InteractionState — the state machine

```csharp
public abstract record InteractionState
{
    public sealed record Idle : InteractionState;
    public sealed record HoverNode(NodeId Node) : InteractionState;
    public sealed record HoverPin(PinId Pin) : InteractionState;
    public sealed record HoverWire(LinkId Link, float TForPosition) : InteractionState;
    public sealed record HoverComment(CommentId Comment, CommentHoverPart Part) : InteractionState;
    public sealed record HoverReroute(LinkId Link, RerouteId Reroute) : InteractionState;
    public sealed record BoxSelect(Vector2 Start, BoxSelectMode Mode) : InteractionState;
    public sealed record DragNodes(IReadOnlyDictionary<NodeId, Vector2> Snapshots, Vector2 DragStart) : InteractionState;
    public sealed record DragWireFromPin(PinId Source, bool SourceIsOutput, PinId? CurrentTarget, LinkValidationResult? Validation) : InteractionState;
    public sealed record DragComment(CommentId Comment, Vector2 SnapshotPos, IReadOnlyDictionary<NodeId, Vector2> SnapshotContentPositions) : InteractionState;
    public sealed record DragReroute(LinkId Link, RerouteId Reroute, Vector2 SnapshotPos) : InteractionState;
    public sealed record ResizeComment(CommentId Comment, Rect2 Snapshot, ResizeHandle Handle) : InteractionState;
    public sealed record PanCanvas(Vector2 LastMousePos) : InteractionState;
}

public enum CommentHoverPart { Header, Body, ResizeHandle }
public enum BoxSelectMode { Replace, Add, Toggle }
public enum ResizeHandle { Top, Right, Bottom, Left, TopLeft, TopRight, BottomLeft, BottomRight }
```

Full state machine: see `A_canvas_interactions.md §A.1`.

## When the host model changes externally

The editor's flow:

1. Host applies a mutation internally → fires `IGraphModel.Changed`.
2. Editor sees the event, marks the spatial index dirty if positions changed, marks viewport for redraw.
3. Next frame: rebuild spatial index for moved nodes (incremental), re-fetch data on read.
4. Selection is filtered to only include IDs that still exist after the mutation (auto-prune).

This means the editor doesn't store node positions in its own data structures — it always asks the model. The spatial index is the only "redundant" structure, and it's pure cache (rebuildable from scratch).

## State persistence

What gets saved between editor sessions, by whom:

| State | Owner | Storage |
|---|---|---|
| Asset content (nodes, links, comments, defaults) | Host | Host's asset format |
| Open tabs, active tab | Editor | Editor session state JSON |
| Tab layout (which panels are visible) | Host | Host's window layout |
| Per-graph viewport (pan/zoom) | Editor | Editor session state JSON, keyed by graph |
| Bookmarks | Editor | Editor session state JSON, per asset |
| Picker favorites/recents | Editor | Editor session state JSON, global |
| Selection | Editor | Not persisted (cleared on close) |
| Undo stack | Editor | Not persisted |
| Breakpoints | Host (asset-level) or Editor (transient) | Host decides |

Editor session state JSON lives at a path the host provides via `IEditorHostServices.SessionStatePath`.
