# NodeEditor.Core

**Project path**: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/NodeEditor.Core.csproj`
**Date**: 2026-05-23
**Target framework**: net8.0
**Namespace root**: `NodeEditor.Core`

---

## README Validation

Status: **Missing** -- no `README.md` exists in the project folder or the NodeEdit root.
The public API is documented entirely through XML doc comments on all public types.

---

## Executive Overview

`NodeEditor.Core` is the rendering-agnostic brain of the NodeEditor library. It owns
the complete data-contract layer (interfaces), all transient editor state (viewport,
selection, interaction), the undo/redo system, the command model, spatial acceleration
structures, bookmarks, and a battery of default implementations for theme, type colors,
and expression evaluation.

The project deliberately contains no ImGui calls. It acts as a shared foundation that
both the `NodeEditor.UI` rendering layer and any future alternative renderer can build
upon. Host applications implement the interfaces defined here; the editor never writes
back to host data -- all mutations flow through `IGraphCommandSink` as typed commands.

Key design decisions:
- **Read-only model**: `IGraphModel` and its children are read-only interfaces; the host
  retains write ownership of graph data.
- **Command-based mutations**: all changes are `GraphCommand` discriminated-union records
  dispatched through `IGraphCommandSink`. This enables transparent undo/redo.
- **Interface-heavy**: 25+ interfaces in `Interfaces/` allow full host control over type
  systems, catalogs, clipboard, debug, icons, themes, and input without binding the
  editor to a specific framework.
- **Spatial index**: a uniform-grid `SpatialIndex` provides O(visible) hit-testing and
  culling for graphs up to ~2000 nodes without the complexity of a quadtree.

---

## Architecture

### Layered View

```
+---------------------------------------------------------------+
|                     NodeEditor.UI                             |
|  (ImGui rendering, input, panels, pickers -- separate proj)   |
+---------------------------------------------------------------+
                             |
                   uses GraphView, interfaces
                             |
+---------------------------------------------------------------+
|                    NodeEditor.Core                            |
|                                                               |
|  +-------------------+  +----------------------------+        |
|  |    GraphView      |  |   Interfaces/ (25+ types)  |        |
|  |  (aggregator)     |  |  IGraphModel               |        |
|  |                   |  |  IGraphCommandSink         |        |
|  |  .Model           |  |  INodeModel / IPinModel    |        |
|  |  .Commands        |  |  ITypeSystem / INodeCatalog|        |
|  |  .Validator       |  |  IEditorHostServices       |        |
|  |  .TypeSystem      |  |  ILinkValidator            |        |
|  |  .Catalog         |  |  IEditorTheme              |        |
|  |  .Host            |  |  IClipboard / IIconProvider|        |
|  |  .Viewport        |  |  IDebugSession             |        |
|  |  .Selection       |  |  IInputSource              |        |
|  |  .Interaction     |  |  ICustomCanvasRenderer     |        |
|  |  .Undo            |  +----------------------------+        |
|  +-------------------+                                        |
|                                                               |
|  +--------------------+  +------------------+                 |
|  |  Commands/         |  |  View/           |                 |
|  |  GraphCommand      |  |  ViewportState   |                 |
|  |  CommandBuilder    |  |  SelectionState  |                 |
|  |  UndoStack         |  |  InteractionState|                 |
|  +--------------------+  |  InteractionMode |                 |
|                          |  PendingWire     |                 |
|  +--------------------+  |  HoverInfo       |                 |
|  |  Spatial/          |  |  SelectionEntry  |                 |
|  |  SpatialIndex      |  +------------------+                 |
|  |  AttachmentLayout  |                                        |
|  |  ContainerBounds   |  +------------------+                 |
|  |  CycleDetector     |  |  Bookmarks/      |                 |
|  +--------------------+  |  BookmarkStore   |                 |
|                          |  Bookmark        |                 |
|  +--------------------+  +------------------+                 |
|  |  DefaultTheme      |  +------------------+                 |
|  |  DefaultTypeColors |  |  Expression/     |                 |
|  |  CommandCatalog    |  |  ExprEvaluator   |                 |
|  |  TimingConstants   |  +------------------+                 |
|  +--------------------+                                        |
+---------------------------------------------------------------+
                             |
               ProjectReference + PackageReference
                             |
+-------------------+   +-----------------+
| NodeEditor.       |   |  ImGui.NET      |
| Primitives        |   |  1.91.6.1       |
| (IDs, enums,      |   | (IPinDefaultValue|
|  RectF)           |   |  editor uses it)|
+-------------------+   +-----------------+
```

### Interaction State Machine

```
+----------+   LMB on pin    +-------------+  drop on pin   +----------+
|  Idle    |--------------->|  PendingWire |--------------->|   Idle   |
|          |                +-------------+  (addLink cmd)  |          |
|          |   RMB drag      +----------+                   |          |
|          |--------------->| Panning  |-- release -------->|          |
|          |                +----------+                   |          |
|          |   LMB on node   +--------------+              |          |
|          |--------------->| DraggingNodes|-- release --->|          |
|          |                +--------------+ (moveNodes cmd)|          |
|          |   LMB on bg    +-----------------+            |          |
|          |--------------->| MarqueeSelecting|-- release ->|          |
|          |                +-----------------+            |          |
|          |   Picker req   +-------------+                |          |
|          |--------------->| PickerOpen  |-- close ------>|          |
+----------+                +-------------+               +----------+
```

---

## Source Structure

```
NodeEditor.Core/
+-- Action/
|   +-- CommandRegistration.cs      -- hotkey-to-command wiring helpers
|   +-- EditorCommandsImpl.cs       -- IEditorCommands implementation
|   +-- EditorIndicatorsImpl.cs     -- status indicator state
|   +-- IEditorCommands.cs          -- command capability interface
|   +-- IEditorIndicators.cs        -- indicator state interface
|   +-- ToastQueue.cs               -- transient toast notification queue
+-- Bookmarks/
|   +-- Bookmark.cs                 -- single bookmark record
|   +-- BookmarkStore.cs            -- 9-slot + unbound bookmark collection
+-- Canvas/
|   +-- CanvasRenderPass.cs         -- render pass enum (BeforeContent/AfterWires/AfterNodes/TopMost)
+-- Commands/
|   +-- CommandBuilder.cs           -- fluent helpers for building commands
|   +-- GraphCommand.cs             -- discriminated union of all mutations
|   +-- UndoStack.cs                -- bounded undo/redo stack
+-- Expression/
|   +-- ExpressionEvaluator.cs      -- simple numeric expression parser
+-- Interfaces/                     -- 25 host-contract interfaces (see API ref)
+-- Layout/
|   +-- RegionLayoutComputer.cs     -- pin region / node height calculation
+-- Search/
|   +-- FuzzyMatcher.cs             -- fuzzy text matching for catalog search
+-- Spatial/
|   +-- AttachmentLayoutEngine.cs   -- positions attachment pills above nodes
|   +-- ContainerBoundsComputer.cs  -- computes container extents from children
|   +-- ContainerCycleDetector.cs   -- Floyd-Warshall cycle check for drops
|   +-- SpatialIndex.cs             -- uniform-grid spatial acceleration
+-- View/
|   +-- GraphView.cs                -- top-level aggregator (the "editor handle")
|   +-- HoverInfo.cs                -- what the cursor is over this frame
|   +-- InteractionMode.cs          -- state machine enum
|   +-- InteractionState.cs         -- drag, marquee, pending-wire, tween state
|   +-- PendingWire.cs              -- in-progress wire descriptor
|   +-- SelectionEntry.cs           -- polymorphic selection element
|   +-- SelectionState.cs           -- mutable selection set
|   +-- ViewportState.cs            -- pan/zoom + coordinate transforms
+-- CommandCatalog.cs               -- catalog of all editor action IDs
+-- DefaultTheme.cs                 -- ready-to-use IEditorTheme implementation
+-- DefaultTypeColors.cs            -- type-based pin color defaults
+-- TimingConstants.cs              -- shared timing values (double-click, drag)
+-- NodeEditor.Core.csproj
```

---

## Public API Reference

### GraphView

The primary handle passed from host to the UI layer. Created once per graph tab.

```csharp
public sealed class GraphView
{
    // Host-supplied (read-only)
    public IGraphModel         Model      { get; }
    public IGraphCommandSink   Commands   { get; }
    public ILinkValidator      Validator  { get; }
    public ITypeSystem         TypeSystem { get; }
    public INodeCatalog        Catalog    { get; }
    public IEditorHostServices Host       { get; }

    // Editor-owned transient state
    public ViewportState     Viewport    { get; }
    public SelectionState    Selection   { get; }
    public InteractionState  Interaction { get; }
    public UndoStack         Undo        { get; }

    // Convenience mutation
    public GraphCommandResult Execute(
        GraphCommand forward, GraphCommand inverse, string label);
    public void UndoLast();
    public void RedoLast();

    // Container coordinate helpers
    public Vector2 NodeCanvasPosition(NodeId id);
}
```

### IGraphModel

```csharp
public interface IGraphModel
{
    GraphId                          Id          { get; }
    string                           DisplayName { get; }
    GraphKindDescriptor              Kind        { get; }
    IReadOnlyCollection<INodeModel>  Nodes       { get; }
    IReadOnlyCollection<ILinkModel>  Links       { get; }
    IReadOnlyCollection<ICommentModel> Comments  { get; }
    IReadOnlyCollection<IAttachmentModel> Attachments { get; }

    INodeModel?       FindNode(NodeId id);
    IPinModel?        FindPin(PinId id);
    ILinkModel?       FindLink(LinkId id);
    IAttachmentModel? FindAttachment(AttachmentId id);
    IReadOnlyList<IAttachmentModel> GetAttachmentsForNode(NodeId hostId);

    event Action<GraphChangeNotification>? Changed;
}
```

### IGraphCommandSink

```csharp
public interface IGraphCommandSink
{
    GraphCommandResult Apply(GraphCommand command);
}
```

### GraphCommand (discriminated union)

Selected members -- all are `sealed record` nested inside `GraphCommand`:

| Command | Purpose |
|---|---|
| `MoveNodes` | Batch of node position updates |
| `AddNode` | Create a new node at a position |
| `RemoveNodes` | Delete nodes and incident links |
| `AddLink` | Connect two pins |
| `RemoveLinks` | Delete specific links |
| `ReplaceLinkEndpoint` | Re-route one end of a wire |
| `SetPinDefault` | Change an input pin's inline value |
| `SetNodeProperty` | Arbitrary host-defined key/value on a node |
| `SetNodeCollapsed` | Toggle collapse state |
| `SetNodeDisabled` | Toggle disabled state |
| `AddComment` / `UpdateComment` / `RemoveComment` | Comment box CRUD |
| `InsertReroute` / `MoveReroute` / `RemoveReroute` | Wire waypoint editing |
| `PromoteToVariable` | Refactoring: promote pin default to variable |
| `CollapseToFunction` | Refactoring: collapse selection to function |
| `CollapseToMacro` | Refactoring: collapse selection to macro |
| `AddAttachment` | Add an attachment pill to a host node |
| `RemoveAttachments` | Remove one or more attachments by ID |
| `SetAttachmentProperty` | Set a host-defined key/value on an attachment |
| `ReorderAttachments` | Reorder attachment stack for a host node |
| `MoveAttachment` | Move an attachment to a different host node |
| `ChangeParent` | Reparent a node into or out of a container node |
| `Batch` | Wraps a list of commands for atomic application |

### INodeModel

```csharp
public interface INodeModel
{
    NodeId          Id               { get; }
    NodeKindKey     Kind             { get; }
    string          Title            { get; }
    string?         Subtitle         { get; }
    NodeCategory    Category         { get; }
    Vector2         Position         { get; }
    Vector2?        SizeOverride     { get; }
    NodeState       State            { get; }
    string?         StatusTooltip    { get; }
    bool            IsCollapsed      { get; }
    bool            ShowAdvancedPins { get; }
    NodeId?         ParentContainerId { get; }  // null = root level
    IReadOnlyList<IPinModel> Pins    { get; }
}
```

### IPinModel

```csharp
public interface IPinModel
{
    PinId       Id               { get; }
    NodeId      OwnerNodeId      { get; }
    string      Label            { get; }
    PinDirection Direction       { get; }
    PinKind     Kind             { get; }
    TypeKey?    Type             { get; }       // null for Exec pins
    PinShape    Shape            { get; }
    bool        IsAdvanced       { get; }
    bool        IsOptional       { get; }
    string?     Tooltip          { get; }
    IPinDefaultValue? Default    { get; }
    bool        AcceptsMultipleConnections { get; }  // computed default impl
}
```

### IEditorHostServices

Bundle of all optional and required host adapters:

```csharp
public interface IEditorHostServices
{
    INodeCatalog                    NodeCatalog       { get; }
    ITypeSystem                     TypeSystem        { get; }
    ILinkValidator                  LinkValidator     { get; }
    IGraphCommandSink               CommandSink       { get; }
    IPickerRegistry                 Pickers           { get; }
    IClipboard                      Clipboard         { get; }
    IIconProvider                   Icons             { get; }
    IDiagnosticsSink?               Diagnostics       { get; }
    IDebugSession?                  Debug             { get; }
    IInputSource                    Input             { get; }
    IEditorTheme                    Theme             { get; }
    IAttachmentContextMenuProvider? AttachmentContextMenu  { get; }
    ICustomElementContextMenuProvider? CustomElementContextMenu { get; }
    IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers { get; }
}
```

### ViewportState

```csharp
public sealed class ViewportState
{
    public Vector2 PanGraph        { get; set; }
    public float   Zoom            { get; }        // clamped [0.25, 3.0]
    public Vector2 CanvasScreenOrigin { get; set; }
    public Vector2 CanvasScreenSize   { get; set; }
    public bool    IsLowZoom       { get; }        // Zoom < 0.5

    public Vector2 GraphToScreen(Vector2 graph);
    public Vector2 ScreenToGraph(Vector2 screen);
    public void    Pan(Vector2 deltaGraph);
    public void    PanScreen(Vector2 deltaScreen);
    public void    ZoomAt(Vector2 anchorScreen, float factor);
    public void    Reset();
    public void    FrameRect(RectF rect, float marginPx = 64f);
}
```

### UndoStack

```csharp
public sealed class UndoStack
{
    public int    UndoCount  { get; }
    public int    RedoCount  { get; }
    public bool   CanUndo    { get; }
    public bool   CanRedo    { get; }
    public string? UndoLabel { get; }
    public string? RedoLabel { get; }

    public GraphCommandResult ApplyAndRecord(
        GraphCommand forward, GraphCommand inverse, string label);
    public bool Undo();
    public bool Redo();
    public void Clear();
}
```

### SpatialIndex

Uniform-grid spatial acceleration for 2D canvas hit-testing:

```csharp
public sealed class SpatialIndex
{
    public SpatialIndex(float cellSize = 256f);
    public int Count { get; }

    public void Rebuild(IEnumerable<(NodeId Id, RectF Bounds)> nodes);
    public void Insert(NodeId id, RectF bounds);
    public bool Remove(NodeId id);
    public RectF? GetBounds(NodeId id);
    public IEnumerable<NodeId> Query(RectF area);
    public IEnumerable<NodeId> QueryPoint(Vector2 point);
}
```

### BookmarkStore

```csharp
public sealed class BookmarkStore
{
    public IReadOnlyCollection<Bookmark> All { get; }

    public Bookmark? GetSlot(int slot);            // slot 1-9
    public void SetSlot(int slot, Bookmark bookmark);
    public bool Remove(string bookmarkId);
    public int PurgeOrphans(IReadOnlyCollection<GraphId> validGraphIds);
    public string ToJson();
    public static BookmarkStore FromJson(string json);
}
```

### DefaultTheme

Ready-to-use `IEditorTheme` with Unreal-Engine-inspired dark palette. Can be used
directly or derived from with `init` overrides:

```csharp
public sealed class DefaultTheme : IEditorTheme
{
    public Vector4 BackgroundColor        { get; init; }  // #1E1E1E
    public Vector4 GridMinorColor         { get; init; }  // #2A2A2A
    public Vector4 SelectionAccent        { get; init; }  // #FFD700
    public Vector4 ErrorColor             { get; init; }  // #FF4444
    public float   NodeCornerRadius       { get; init; }  // 4 px
    public float   NodeHeaderHeight       { get; init; }  // 24 px
    public float   PinGlyphSize           { get; init; }  // 10 px
    // ...and more

    public Vector4 GetCategoryHeaderColor(NodeCategory category);
    public nint    GetFontForSize(float targetPixelSize);  // returns 0 (default font)
}
```

### ITypeSystem

```csharp
public interface ITypeSystem
{
    bool     TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info);
    Vector4  GetPinColor(TypeKey key);
    PinShape GetPinShape(TypeKey key, ContainerKind container);
    IPinDefaultValueEditor? GetDefaultEditor(TypeKey key);
    bool     AreCompatible(TypeKey from, TypeKey to);
    bool     IsImplicitCast(TypeKey from, TypeKey to);
}
```

### INodeCatalog

```csharp
public interface INodeCatalog
{
    IReadOnlyList<NodeCatalogEntry>      All        { get; }
    IReadOnlyList<NodeCategoryDescriptor> Categories { get; }
    IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q);
    IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q);
}
```

---

## Extension API Reference

Three post-v1 canvas extensions were added to NodeEditor.Core as part of the HROT AI
editor work. All interfaces live in `NodeEditor.Core.Interfaces` alongside the original
contracts. Support types are in `NodeEditor.Primitives` and `NodeEditor.Core.Spatial`.

---

### NodeAttachments Extension

Attachment pills are small, parameterized, visually-attached annotations whose lifetime
is tied to a host node. They render as horizontal rows of rounded rectangles above the
host node header.

Primary motivating uses: BTree decorator pills (Inverter, Repeater, Cooldown, etc.)
stacked above a composite or leaf node; HSM state-flag badges (deferred-event chips,
conflict markers) shown on states.

#### AttachmentId

```csharp
// In NodeEditor.Primitives
public readonly record struct AttachmentId(Guid Value)
{
    public static AttachmentId Empty  => default;
    public static AttachmentId NewId() => new(Guid.NewGuid());
}
```

#### IAttachmentModel

```csharp
// In NodeEditor.Core.Interfaces
public interface IAttachmentModel
{
    AttachmentId      Id         { get; }
    NodeId            HostNodeId { get; }
    AttachmentCategory Category  { get; }
    string?           Glyph      { get; }   // 1-2 char glyph, or null
    string?           Label      { get; }   // one-line label, or null
    string?           Tooltip    { get; }
    AttachmentState   State      { get; }
    int               StackIndex { get; }   // lower = left; ties broken by Id
}

public enum AttachmentCategory { Decorator, Flag, Pure, Custom }

[Flags]
public enum AttachmentState
{
    Normal           = 0,
    Disabled         = 1 << 0,
    Error            = 1 << 1,
    Warning          = 1 << 2,
    Executing        = 1 << 3,
    RecentlyExecuted = 1 << 4,
    Selected         = 1 << 5,   // managed by editor; do not set from host
}
```

#### IGraphModel attachment members

`IGraphModel` was extended with default-interface-method members:

```csharp
IReadOnlyCollection<IAttachmentModel> Attachments => Array.Empty<IAttachmentModel>();
IAttachmentModel?                     FindAttachment(AttachmentId id) => null;
IReadOnlyList<IAttachmentModel>       GetAttachmentsForNode(NodeId hostId) => ...;
```

Host implementations that support attachments override all three members.

#### AttachmentLayoutEngine (Spatial)

Pure math for computing pill positions. Called by the canvas before rendering.

```csharp
public static class AttachmentLayoutEngine
{
    public const float PillHeight         = 20f;
    public const float PillMinWidth       = 24f;
    public const float PillPaddingH       = 6f;   // each side
    public const float InterAttachmentGap = 4f;
    public const float InterRowGap        = 3f;
    public const float GapAboveHost       = 6f;

    public static AttachmentLayout Compute(
        IReadOnlyList<IAttachmentModel> attachments,
        float hostWidth,
        Func<IAttachmentModel, float> measureContentWidth);
}

public sealed class AttachmentLayout
{
    public IReadOnlyDictionary<AttachmentId, AttachmentPlacement> Placements { get; }
    public float TotalHeightAboveHost { get; }
    public static AttachmentLayout Empty { get; }
}

public readonly record struct AttachmentPlacement(
    AttachmentId Id,
    Vector2 TopLeft,     // relative to host node top-left; Y is negative (above)
    Vector2 Size);
```

#### IAttachmentContextMenuProvider

```csharp
public interface IAttachmentContextMenuProvider
{
    IReadOnlyList<ContextMenuItem> GetItemsFor(AttachmentId id);
}
```

Register via `IEditorHostServices.AttachmentContextMenu`.

#### Attachment GraphCommands

| Command | Primary fields | Purpose |
|---|---|---|
| `AddAttachment` | `NewId`, `HostNodeId`, `Category`, `Glyph`, `Label`, `Tooltip`, `StackIndex` | Create a new attachment on a host node |
| `RemoveAttachments` | `AttachmentIds` | Delete one or more attachments |
| `SetAttachmentProperty` | `Id`, `Key`, `Value` | Change a host-defined string property on an attachment |
| `ReorderAttachments` | `HostNodeId`, `NewOrder` | Reorder the stack for a host |
| `MoveAttachment` | `Id`, `NewHostNodeId`, `NewStackIndex` | Move an attachment to a different host |

---

### ContainerNodes Extension

Container nodes visually and logically enclose child nodes. The container's bounds
auto-resize to fit its children. Children's positions are stored in the container's
interior coordinate space (parent-local), not in canvas space.

Primary use: HSM composite states (nested state machines), HSM parallel states with
orthogonal regions, Blueprint composite-node groupings.

#### IContainerNodeModel

```csharp
// In NodeEditor.Core.Interfaces -- extends INodeModel
public interface IContainerNodeModel : INodeModel
{
    bool                          IsContainer        { get; }
    IReadOnlyList<NodeId>         ChildNodeIds       { get; }
    IReadOnlyList<RegionDescriptor> Regions          { get; }  // empty for non-parallel
    int                           GetRegionIndexForChild(NodeId childId);
    ContainerPadding              Padding            { get; }
    Vector2                       MinimumInteriorSize { get; }
}

public sealed record RegionDescriptor(
    int Index, string Name, int Priority, Vector4? CustomColor);

public sealed record ContainerPadding(
    float Top, float Right, float Bottom, float Left)
{
    public static ContainerPadding Default { get; } = new(8f, 12f, 12f, 12f);
}

// Extension methods on INodeModel:
public static bool IsContainerNode(this INodeModel node);
public static IContainerNodeModel? AsContainer(this INodeModel node);
```

`INodeModel.ParentContainerId` (already on `INodeModel`) stores the parent container
ID for nodes inside a container, or null for root-level nodes.

#### ContainerBoundsComputer (Spatial)

```csharp
public static class ContainerBoundsComputer
{
    public const float OutlineWidth = 1f;

    public static Vector2 ComputeOuterSize(
        IContainerNodeModel container,
        IGraphModel model,
        Func<NodeId, Vector2?> getChildGraphSize,
        float headerHeight);
}
```

#### ContainerCycleDetector (Spatial)

```csharp
// Detects cyclic parent relationships before applying ChangeParent commands.
public static class ContainerCycleDetector
{
    public static bool WouldCreateCycle(
        NodeId candidate,
        NodeId? proposedParent,
        IGraphModel model);
}
```

#### Container GraphCommand

| Command | Primary fields | Purpose |
|---|---|---|
| `ChangeParent` | `NodeId`, `NewParentContainerId`, `NewLocalPosition` | Reparent a node into (`NewParentContainerId != null`) or out of (`null`) a container, updating its local position |

---

### CustomCanvasRenderer Extension

Host-provided renderers that draw into the canvas at specific named passes, using the
same ImGui draw list NodeEditor uses but without owning the canvas.

#### CanvasRenderPass

```csharp
// In NodeEditor.Core.Canvas
public enum CanvasRenderPass
{
    BeforeContent,  // after background/grid; before any graph content
    AfterWires,     // after wires; before nodes -- use for wire decorations
    AfterNodes,     // after nodes/attachments/reroutes; before selection outlines
    TopMost,        // after all selection/hover feedback -- last layer
}
```

#### ICustomCanvasRenderer

```csharp
// In NodeEditor.Core.Interfaces
public interface ICustomCanvasRenderer : IDisposable
{
    string           Id     { get; }   // e.g. "hsm.transition_labels"
    CanvasRenderPass Pass   { get; }
    bool             IsActive => true;
    void             Render(ICanvasRenderContext ctx);
    void IDisposable.Dispose() { }    // default no-op
}
```

Registered via `IEditorHostServices.CustomCanvasRenderers`. Order in the list
determines draw order within a pass; across passes the pass order is fixed.

#### ICanvasRenderContext

```csharp
public interface ICanvasRenderContext
{
    ImDrawListPtr        DrawList       { get; }   // screen-coordinate ImGui draw list
    ViewportState        Viewport       { get; }
    CanvasRenderPass     Pass           { get; }
    IEditorTheme         Theme          { get; }
    IGraphModel          Graph          { get; }
    SelectionState       Selection      { get; }
    IReadOnlySet<NodeId> VisibleNodes   { get; }   // for culling
    IReadOnlySet<LinkId> VisibleLinks   { get; }   // for culling
    float                Zoom           { get; }
    bool                 IsLowZoom      { get; }   // Zoom < 0.5
    IDebugSession?       DebugSession   { get; }
    IDictionary<string, object?> FrameScratch { get; }  // per-frame; cleared each frame

    Vector2 CanvasToScreen(Vector2 canvasPoint);
    Vector2 ScreenToCanvas(Vector2 screenPoint);
    RectF   CanvasToScreen(RectF canvasRect);
}
```

#### ICustomCanvasHitTester

Optional companion interface for hit-testable custom content. Implement on the same
class as the renderer.

```csharp
public interface ICustomCanvasHitTester
{
    CustomElementHit? HitTest(Vector2 canvasPoint, IHitTestContext ctx);
}

public readonly record struct CustomElementHit(
    string ElementKey,
    CustomElementKind Kind,
    RectF Bounds);

public enum CustomElementKind
{
    LinkDecoration,   // decoration on a link (e.g., transition label)
    StateAnnotation,  // annotation on a state (e.g., HSM history glyph)
    CanvasAnnotation, // freestanding annotation (e.g., region-conflict line)
    Custom,
}
```

#### ICustomCanvasSelectable

Optional companion for custom elements that participate in the selection model.

```csharp
public interface ICustomCanvasSelectable
{
    void OnElementSelected(string elementKey, CustomElementHit hit);
    void OnElementDeselected(string elementKey);
}
```

#### ICustomElementContextMenuProvider

```csharp
public interface ICustomElementContextMenuProvider
{
    string RendererId { get; }   // must match ICustomCanvasRenderer.Id
    IReadOnlyList<ContextMenuItem> GetItemsFor(string elementKey, CustomElementHit hit);
}
```

Register via `IEditorHostServices.CustomElementContextMenu`.

---

## Dependencies

| Dependency | Version | Why |
|---|---|---|
| `NodeEditor.Primitives` | project ref | ID structs, RectF, enums |
| `ImGui.NET` | 1.91.6.1 | `IPinDefaultValueEditor.Draw` uses `ref object?` passed to ImGui widgets |

**Note**: ImGui.NET is listed but the Core layer does not render anything. The type
`IPinDefaultValueEditor` is the only interface that exposes an ImGui-shaped contract
(`Draw` callback). All actual ImGui calls are in `NodeEditor.UI`.

---

## Usage Examples

### Example 1: Create a GraphView from host implementations

```csharp
// Host implements all interfaces
var model    = new MyGraphModel(graphData);
var sink     = new MyCommandSink(graphData);
var host     = new MyHostServices(model, sink, myTypeSystem, myCatalog);

var view = new GraphView(
    model:      model,
    commands:   sink,
    validator:  host.LinkValidator,
    typeSystem: host.TypeSystem,
    catalog:    host.NodeCatalog,
    host:       host);

// The UI layer then calls:
//   canvasRenderer.Render(view);
```

### Example 2: Issue a command through undo stack

```csharp
// Capture pre-mutation state for the inverse
var nodesBefore = view.Selection.Nodes
    .Select(id => view.Model.FindNode(id))
    .Where(n => n != null)
    .Select(n => new GraphCommand.MoveNodes.NodeMove(n!.Id, n.Position))
    .ToList();

var forward = new GraphCommand.MoveNodes(newMoves);
var inverse = new GraphCommand.MoveNodes(nodesBefore);

view.Execute(forward, inverse, "Move Nodes");
// Stack now has one entry: undo will restore original positions.
```

### Example 3: Spatial query for hit-testing

```csharp
var index = new SpatialIndex(cellSize: 256f);

// Rebuild after model change
index.Rebuild(view.Model.Nodes.Select(n =>
    (n.Id, new RectF(n.Position, n.SizeOverride ?? new Vector2(180, 60)))));

// Hit-test a point in graph space
var graphPos = view.Viewport.ScreenToGraph(mouseScreen);
var hit = index.QueryPoint(graphPos).FirstOrDefault();
if (hit != NodeId.Empty)
    view.Selection.ReplaceWith(SelectionEntry.OfNode(hit));
```

### Example 4: Bookmark usage

```csharp
var store = new BookmarkStore();

// Create a bookmark at current viewport, assign to slot 1
var bm = new Bookmark(
    BookmarkId:   Guid.NewGuid().ToString("N"),
    TargetGraph:  view.Model.Id,
    Label:        "Event Begin Play",
    ViewportPan:  view.Viewport.PanGraph,
    ViewportZoom: view.Viewport.Zoom,
    SlotNumber:   1,
    CreatedAt:    DateTime.UtcNow);
store.SetSlot(1, bm);

// Navigate to it (Ctrl+1)
var slot1 = store.GetSlot(1);
if (slot1 != null)
    view.Interaction.BeginViewportTween(slot1.ViewportPan, slot1.ViewportZoom, 300);
```

---

## Best Practices

1. **Never write through IGraphModel** -- use `IGraphCommandSink.Apply` or
   `GraphView.Execute`. The read-only interface contract is enforced by convention;
   violating it breaks undo.

2. **Batch related commands** -- wrap multi-step operations in `GraphCommand.Batch` so
   undo collapses them into a single entry.

3. **Provide the inverse before applying** -- the `UndoStack` requires the caller to
   snapshot old state before issuing a command. Capture positions, link endpoints, or
   property values before calling `Execute`.

4. **Subscribe to `IGraphModel.Changed`** -- external changes (hot-reload, remote edit)
   fire this event. Clear the undo stack when `GraphChangeKind` implies a wholesale
   structural replacement to avoid applying stale inverses.

5. **Use `DefaultTheme` as a base** -- override specific `init` properties rather than
   implementing `IEditorTheme` from scratch:
   ```csharp
   var theme = new DefaultTheme { SelectionAccent = new Vector4(0f, 0.8f, 1f, 1f) };
   ```

6. **Keep node counts sane** -- the `SpatialIndex` targets ~2000 nodes. Graphs much
   larger than that should use subgraph containers rather than a flat layout.

7. **Implement `ILinkValidator` strictly** -- the UI displays a red "incompatible"
   indicator during wire dragging when `AreCompatible` returns false. Make validation
   symmetric and reflexive for Exec pins.

---

## Related Projects

| Project | Relationship |
|---|---|
| `NodeEditor.Primitives` | Direct dependency -- provides all ID types, `RectF`, enums |
| `NodeEditor.UI` | Consumer -- renders the canvas using `GraphView` and these interfaces |
| `NodeEditor.Demo` | Indirect consumer via `NodeEditor.UI` |
| `Fdp.Engine` (FDP) | Host -- implements all interfaces against the FDP data store |
| `FastHSM` (FDP ExtDeps) | Host -- implements interfaces for BTree/HSM visual editor |
