# NodeEditor.Primitives

**Project path**: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Primitives/NodeEditor.Primitives.csproj`
**Date**: 2026-05-23
**Target framework**: net8.0
**Namespace root**: `NodeEditor.Primitives`

---

## README Validation

Status: **Missing** -- no `README.md` exists in the project folder or the NodeEdit root.
All types are documented through XML doc comments.

---

## Executive Overview

`NodeEditor.Primitives` is the zero-dependency foundation layer of the NodeEditor library.
It defines:

- **Typed ID wrappers** for every addressable graph element (node, pin, link, comment,
  attachment, graph, reroute). Each is a `readonly record struct` wrapping a `Guid`,
  preventing accidental mix-ups between id kinds at compile time.
- **Semantic key types** (`TypeKey`, `NodeKindKey`, `EditorKey`) for catalog and type
  system lookups.
- **Geometry**: `RectF`, an immutable axis-aligned rectangle in canvas or screen space.
- **Enumerations**: `PinDirection`, `PinKind`, `PinShape`, `NodeCategory`, `NodeState`,
  `ContainerKind`, `MouseButton`, `KeyModifiers`.
- **`IdGenerator`**: a simple monotonic sequence for generating unique integer IDs
  within a session.

The project has **no NuGet or project references**. It can be consumed independently by
host applications that only need the primitive vocabulary without the editor logic.

---

## Architecture

### Dependency Position

```
+------------------------------------------+
|           NodeEditor.Core                |
+------------------------------------------+
                    |
         ProjectReference (read-only)
                    |
+------------------------------------------+
|         NodeEditor.Primitives            |
|                                          |
|  ID types      Keys        Geometry      |
|  NodeId        TypeKey     RectF         |
|  PinId         NodeKindKey               |
|  LinkId        EditorKey                 |
|  CommentId                               |
|  GraphId       Enumerations              |
|  AttachmentId  PinDirection              |
|  RerouteRef    PinKind / PinShape        |
|                NodeCategory              |
|  Utilities     NodeState (flags)         |
|  IdGenerator   ContainerKind             |
|                MouseButton               |
|                KeyModifiers (flags)      |
+------------------------------------------+
         NO dependencies
```

### ID Hierarchy

```
+-------------+   owns    +----------+   owns    +--------+
|   GraphId   |---------->|  NodeId  |---------->|  PinId |
+-------------+           +----------+           +--------+
                               |
                    AttachmentId (stacked above node)
                    CommentId   (free-floating box)
                               |
                    +---------+-------+
                    |                 |
                  LinkId          RerouteRef
               (NodeId,PinId) -- (LinkId, int index)
```

---

## Source Structure

```
NodeEditor.Primitives/
+-- AttachmentId.cs     -- ID for attachment pills stacked above a node
+-- CommentId.cs        -- ID for comment/annotation boxes
+-- EditorKey.cs        -- string-keyed handle for named editor instances
+-- Enums.cs            -- all enumerations (PinDirection, NodeCategory, ...)
+-- GraphId.cs          -- ID for a graph (tab / asset)
+-- IdGenerator.cs      -- monotonic integer sequence
+-- LinkId.cs           -- ID for a wire/connection between two pins
+-- NodeId.cs           -- ID for a node on the canvas
+-- NodeKindKey.cs      -- string key identifying a node template in the catalog
+-- PinId.cs            -- ID for a single pin on a node
+-- RectF.cs            -- immutable axis-aligned rectangle
+-- RerouteRef.cs       -- reference to a reroute waypoint (link + index)
+-- TypeKey.cs          -- string key for a data type in the type system
+-- NodeEditor.Primitives.csproj
```

---

## Public API Reference

### ID Types

All six entity ID types follow the same pattern: `readonly record struct` wrapping a
`Guid`. Each has `Empty`, `NewId()`, and a short `ToString()`.

```csharp
// Shared pattern (shown for NodeId; PinId, LinkId, CommentId, GraphId, AttachmentId identical)
public readonly record struct NodeId(Guid Value)
{
    public static NodeId Empty  => default;
    public static NodeId NewId() => new(Guid.NewGuid());
    public override string ToString();
}
```

| Type | Wraps | Identifies |
|---|---|---|
| `NodeId` | `Guid` | A node on the canvas |
| `PinId` | `Guid` | A single pin (input or output) |
| `LinkId` | `Guid` | A wire connecting two pins |
| `CommentId` | `Guid` | A free-floating comment/annotation box |
| `GraphId` | `Guid` | A graph (one per editor tab / asset) |
| `AttachmentId` | `Guid` | An attachment pill stacked above a node |

### RerouteRef

A lightweight value-type reference to a waypoint within a link:

```csharp
public readonly record struct RerouteRef(LinkId Link, int WaypointIndex);
```

### Key Types

String-backed semantic keys:

```csharp
public readonly record struct TypeKey(string Value);      // e.g. "float", "MyStruct"
public readonly record struct NodeKindKey(string Value);  // e.g. "math.add", "flow.if"
public readonly record struct EditorKey(string Value);    // e.g. "blueprint_editor"
```

### RectF

Immutable axis-aligned rectangle in canvas or screen space. `Min` is top-left.

```csharp
public readonly record struct RectF(Vector2 Min, Vector2 Size)
{
    public Vector2 Max    { get; }
    public Vector2 Center { get; }
    public float   Width  { get; }
    public float   Height { get; }

    public bool Contains(Vector2 p);
    public bool Intersects(RectF other);
    public bool FullyContains(RectF other);
    public RectF Expand(float amount);

    public static RectF FromMinMax(Vector2 min, Vector2 max);
    public static RectF FromCenterSize(Vector2 center, Vector2 size);
    public static RectF Empty { get; }
}
```

### Enumerations

#### PinDirection
```csharp
public enum PinDirection { Input, Output }
```

#### PinKind
```csharp
public enum PinKind
{
    Exec,   // execution-control (triangle glyph, white wire)
    Data,   // typed data (circle/diamond glyph, colored wire)
}
```

#### PinShape
```csharp
public enum PinShape { Circle, Diamond, Square, Pentagon, Triangle }
```
Shape is determined by `ITypeSystem.GetPinShape(TypeKey, ContainerKind)`. Typical
mapping: `Single` -> `Circle`, `Array` -> `Diamond`, `Map` -> `Square`.

#### NodeCategory
```csharp
public enum NodeCategory
{
    Function, Event, Pure, VariableGet, VariableSet,
    FlowControl, Macro, Comment, Custom,
}
```
Drives header color selection in `IEditorTheme.GetCategoryHeaderColor`.

#### NodeState (flags)
```csharp
[Flags]
public enum NodeState
{
    Normal           = 0,
    Disabled         = 1 << 0,
    Error            = 1 << 1,
    Warning          = 1 << 2,
    Executing        = 1 << 3,
    RecentlyExecuted = 1 << 4,
}
```
`Executing` and `RecentlyExecuted` are set by a connected `IDebugSession` to visualize
runtime execution flow.

#### ContainerKind
```csharp
public enum ContainerKind { Single, Array, Map, Set }
```
Combined with `TypeKey` to drive pin shape selection.

#### MouseButton
```csharp
public enum MouseButton { Left, Right, Middle, X1, X2 }
```

#### KeyModifiers (flags)
```csharp
[Flags]
public enum KeyModifiers { None = 0, Ctrl = 1<<0, Shift = 1<<1, Alt = 1<<2, Super = 1<<3 }
```

### IdGenerator

```csharp
public sealed class IdGenerator
{
    public IdGenerator(int seed = 0);
    public int Next();
    public void Reset(int value = 0);
}
```

---

## Dependencies

**None.** The project has no NuGet or project references. It targets `net8.0` and uses
only BCL types (`System.Numerics.Vector2`, `System.Guid`).

---

## Usage Examples

### Example 1: Creating and comparing IDs

```csharp
// Create new IDs
var nodeId1 = NodeId.NewId();
var nodeId2 = NodeId.NewId();

// Value-equality via record struct
bool same = nodeId1 == nodeId2;     // false
bool empty = nodeId1 == NodeId.Empty; // false

// Use in a dictionary (safe because Guid is hash-friendly)
var positions = new Dictionary<NodeId, Vector2>
{
    [nodeId1] = new Vector2(100, 200),
};
```

### Example 2: Working with RectF

```csharp
// Build a node bounding rect
var nodePos  = new Vector2(100, 200);
var nodeSize = new Vector2(180, 80);
var bounds   = new RectF(nodePos, nodeSize);

// Check if a graph-space point hits the node
bool hit = bounds.Contains(graphPoint);

// Expand by a few pixels for selection highlight
var highlight = bounds.Expand(3f);

// Build from two corner points (e.g. marquee selection)
var marquee = RectF.FromMinMax(dragStart, dragEnd);
bool fullyInside = marquee.FullyContains(bounds);

// Intersection test for visibility culling
var viewportRect = RectF.FromMinMax(view.Min, view.Max);
bool visible = viewportRect.Intersects(bounds);
```

### Example 3: Using TypeKey and NodeKindKey

```csharp
// Register a custom node kind
var myKind = new NodeKindKey("mylib.lerp_vector3");

// Register a custom type
var vec3Type = new TypeKey("Vector3");

// In a catalog entry
var entry = new NodeCatalogEntry(
    Kind:        myKind,
    DisplayName: "Lerp Vector3",
    Description: "Linearly interpolate between two Vector3 values",
    CategoryPath: "Math/Vector",
    Keywords:    new[] { "lerp", "interpolate", "vector" },
    IconKey:     null,
    IsPure:      true,
    IsLatent:    false,
    IsDeprecated: false,
    Inputs:  new[]
    {
        new PinSignature("A",     PinKind.Data, vec3Type, IsWildcard: false),
        new PinSignature("B",     PinKind.Data, vec3Type, IsWildcard: false),
        new PinSignature("Alpha", PinKind.Data, new TypeKey("float"), IsWildcard: false),
    },
    Outputs: new[]
    {
        new PinSignature("Result", PinKind.Data, vec3Type, IsWildcard: false),
    });
```

---

## Best Practices

1. **Never use raw Guids in the API** -- pass `NodeId`, `PinId`, etc. The compiler
   enforces correct pairing; a method that takes `NodeId` cannot accidentally receive
   a `PinId`.

2. **Treat Empty as null** -- `NodeId.Empty` (default) means "not set". Always check
   before using: `if (id == NodeId.Empty) return;`

3. **Prefer `NewId()` over constructing from Guid** -- the static factory makes intent
   clear and is the same cost as `new NodeId(Guid.NewGuid())`.

4. **Use `RectF.FromMinMax` for drag rects** -- when building marquee or viewport rects
   from two arbitrary corner points, `FromMinMax` normalizes them correctly regardless
   of drag direction.

5. **Do not serialize raw Guid strings when round-tripping IDs** -- always serialize as
   `NodeId` to preserve type information, even when the transport is JSON:
   ```csharp
   // Good: store in a typed dictionary, JSON-convert via Value
   var dto = new NodeDto { Id = nodeId.Value.ToString("D") };
   var restored = new NodeId(Guid.Parse(dto.Id));
   ```

6. **Use `NodeState` as a bit field** -- multiple states can be set simultaneously:
   ```csharp
   bool isWarningAndDisabled = (node.State & (NodeState.Warning | NodeState.Disabled))
                               == (NodeState.Warning | NodeState.Disabled);
   ```

---

## Advanced Notes

### Value Semantics and Dictionary Use

All ID types are `readonly record struct`. This means:

- They support structural equality: `==` compares the underlying `Guid`.
- They implement `IEquatable<T>` efficiently -- no boxing.
- They are safe as `Dictionary<TKey, TValue>` keys without a custom comparer.
- They can be stored inline in arrays and `Span<T>` without heap allocation.

```csharp
// All of these are allocation-free:
var dict = new Dictionary<NodeId, Vector2>();
dict[nodeId] = new Vector2(100, 200);

var list = new List<PinId>();
list.Add(pinId);

Span<NodeId> span = stackalloc NodeId[4];
span[0] = NodeId.NewId();
```

### JSON Serialization Pattern

When serializing IDs to JSON (e.g. for undo snapshots or clipboard), use the `Guid`
value directly via `NodeId.Value`:

```csharp
// Serialize
var dto = new NodeDto { Id = nodeId.Value.ToString("D") };

// Deserialize
var restored = new NodeId(Guid.Parse(dto.Id));
```

The format string `"D"` produces the standard `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`
form, which is lossless and human-readable in debug output.

### RectF Coordinate Conventions

`RectF` is used in two coordinate spaces:

- **Graph space**: absolute positions in the infinite canvas. `INodeModel.Position` and
  node size produce graph-space rects. All spatial-index operations use graph space.
- **Screen space**: positions in screen pixels. `ViewportState.GraphToScreen` converts a
  graph-space `RectF` into a screen-space one for rendering.

The `Contains` and `Intersects` methods work in whichever space you provide -- they have
no inherent coordinate frame.

```csharp
// Node in graph space
var graphRect = new RectF(node.Position, node.SizeOverride ?? new Vector2(180, 60));

// Same rect in screen space (for ImGui DrawList calls)
var screenMin = viewport.GraphToScreen(graphRect.Min);
var screenMax = viewport.GraphToScreen(graphRect.Max);
var screenRect = RectF.FromMinMax(screenMin, screenMax);
```

### IdGenerator Thread Safety

`IdGenerator` is not thread-safe. In the demo and most editors, it is accessed only from
the UI thread. If you need a shared sequence across threads, wrap calls in a lock or use
`Interlocked.Increment` on a shared counter and construct the ID from the result:

```csharp
private static int _counter;
public static NodeId NextNodeId() => new NodeId(Guid.NewGuid());  // Guid is always unique
public static EditNodeId NextEditId() =>
    new EditNodeId(Interlocked.Increment(ref _counter));
```

### NodeKindKey and TypeKey Normalization

Both `NodeKindKey` and `TypeKey` are string-backed. Convention in the library is:

- **`NodeKindKey`**: dot-separated category path, e.g. `"math.add"`, `"flow.branch"`,
  `"variable.get"`. Case-sensitive; use lowercase.
- **`TypeKey`**: matches the C# type name as used in the type system, e.g. `"float"`,
  `"Vector3"`, `"MyGameLib.ItemId"`. The library does not enforce a naming scheme; match
  whatever the host's `ITypeSystem` registers.

There is no normalization or case folding in the primitives layer itself. Hosts are
responsible for consistent key usage.

### Extending Enumerations

`NodeCategory`, `PinKind`, and `ContainerKind` are closed enumerations -- they cannot
be extended. If a host needs a visual category not covered by `NodeCategory`, use
`NodeCategory.Custom` and differentiate via `NodeKindKey` in the Details panel or theme.

For extended pin shapes, `PinShape` offers five options (`Circle`, `Diamond`, `Square`,
`Pentagon`, `Triangle`). These map to specific glyph renderers in `NodeEditor.UI`. If
none of the five shapes fits a new semantic, use the closest available shape and rely on
pin color (via `ITypeSystem.GetPinColor`) for further differentiation.

---

## Diagram: ID Lifecycle

```
Host creates IDs          Editor reads IDs         Commands carry IDs
+------------------+      +------------------+      +------------------+
|                  |      |                  |      |                  |
| NodeId.NewId()   |----->| INodeModel.Id    |<-----| MoveNodes.Moves  |
| PinId.NewId()    |----->| IPinModel.Id     |<-----| AddLink.From/To  |
| LinkId.NewId()   |----->| ILinkModel.Id    |<-----| RemoveLinks.Links|
| CommentId.NewId()|----->| ICommentModel.Id |<-----| UpdateComment.Id |
| GraphId.NewId()  |----->| IGraphModel.Id   |<-----| BookmarkStore    |
|                  |      |                  |      |                  |
+------------------+      +------------------+      +------------------+
     (host side)               (read-only)              (mutations)
```

---

## Diagram: Enum Usage Mapping

```
PinDirection   IPinModel.Direction    -> determines left/right side of node
PinKind        IPinModel.Kind         -> Exec (triangle glyph) vs Data (circle)
PinShape       ITypeSystem.GetPinShape(TypeKey, ContainerKind)
                                       -> glyph shape on pin
NodeCategory   INodeModel.Category    -> IEditorTheme.GetCategoryHeaderColor()
NodeState      INodeModel.State       -> overlay badges (error, executing, ...)
ContainerKind  per-pin descriptor     -> Single=circle, Array=diamond, Map=square
MouseButton    IInputSource           -> LMB=Left, RMB=Right, MMB=Middle
KeyModifiers   IInputSource.Modifiers -> Ctrl/Shift/Alt/Super combinations
```

---

## Related Projects

| Project | Relationship |
|---|---|
| `NodeEditor.Core` | Direct consumer -- builds its entire API on these primitives |
| `NodeEditor.UI` | Indirect consumer via `NodeEditor.Core` |
| `NodeEditor.Demo` | Indirect consumer via `NodeEditor.UI` |
| `Fdp.Engine` (FDP) | Host application -- uses these types as graph element handles |
