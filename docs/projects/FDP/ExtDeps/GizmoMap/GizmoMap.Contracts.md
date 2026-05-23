# GizmoMap.Contracts

| Field       | Value                                                                              |
|-------------|------------------------------------------------------------------------------------|
| Project     | GizmoMap.Contracts                                                                 |
| Path        | `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/GizmoMap.Contracts.csproj`               |
| Namespace   | `Fdp.Toolkit.Diagnostics.Gizmos` / `Fdp.Toolkit.Diagnostics.Gizmos.Interaction`  |
| Targets     | `net8.0`, `netstandard2.1`                                                         |
| Date        | 2026-05-23                                                                         |

---

## README Validation

**Status: Missing** -- No `README.md` exists in `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/`
or in the GizmoMap root folder. All findings in this document are derived directly from
source code and inline comments.

---

## Executive Overview

`GizmoMap.Contracts` is the shared kernel of the GizmoMap debug-visualization subsystem.
It defines every type that crosses assembly boundaries -- the primitive data structure, the
draw-builder interface, interaction contracts, transport abstractions, and supporting value
types -- while having zero dependencies on ECS, DDS, or rendering frameworks.

The library targets both `net8.0` and `netstandard2.1`, making it consumable by simulation
nodes (which may target older runtimes) as well as viewer and presentation assemblies. This
dual-target policy is a hard architectural constraint: no assembly in the stack may take a
dependency that narrows the consumer surface below `netstandard2.1`.

The central type is `DebugPrimitive` -- a 64-byte blittable tagged union that fits exactly
in one CPU cache line. Every shape (line, sphere, box, arrow, text, icon, semantic symbol,
and several non-visual meta-primitives) is encoded in a single fixed-layout struct. This
makes frame-level serialization a zero-copy `MemoryMarshal.AsBytes` call, enables
`Interlocked` append into a pre-allocated ring buffer, and avoids heap allocation on the
hot draw path.

---

## Architecture

### Layering Principle

```
+-----------------------------------------------------+
|  GizmoMap.Contracts                                 |
|                                                     |
|  Namespace: Fdp.Toolkit.Diagnostics.Gizmos          |
|                                                     |
|  +---------------+   +-------------------------+   |
|  | Primitives/   |   | Abstractions/           |   |
|  | DebugPrimitive|   | IGizmoDrawBuilder       |   |
|  | (64-byte union)|  | (draw method contract)  |   |
|  +------+--------+   +-------------------------+   |
|         |                                           |
|  +------v--------+   +-------------------------+   |
|  | GizmoPrimitive|   | Interaction/            |   |
|  | Buffer        |   | IGizmoInteractionHandler|   |
|  | (thread-safe  |   | IStatefulGizmo          |   |
|  | ring buffer)  |   | GizmoPickToken          |   |
|  +---------------+   +-------------------------+   |
|                                                     |
|  +---------------+   +-------------------------+   |
|  | Sources/      |   | Transport/              |   |
|  | IGizmoSource  |   | IGizmoTransport         |   |
|  +---------------+   +-------------------------+   |
|                                                     |
|  +--------------------------------------------+    |
|  | StringInternMap (FNV-1a concurrent dict)   |    |
|  +--------------------------------------------+    |
|                                                     |
|  External deps: BCL only (+ System.Text.Json on    |
|  netstandard2.1, + CycloneDDS.NET on net8.0 for    |
|  PipelineTarget IDL generation only)               |
+-----------------------------------------------------+
```

### DebugPrimitive Memory Layout

The 64-byte struct uses explicit field offsets. All payload variants share offsets 24-63.
The header at offsets 0-23 is common to every shape.

```
Offset  Size  Field (header -- every shape)
------  ----  ----------------------------------
  0       1   Shape      (DebugPrimitiveShape enum)
  1       1   Space      (CoordinateSpace enum)
  2       4   Color      (Rgba32)
  6       1   TargetView (PipelineTarget flags)
  7       1   DebugLayer (0-15)
  8       4   AnchorIndex / StringHash (overlaid)
 12       2   AnchorGeneration
 14       1   SizeMode
 15       1   ZIndex
 16       2   ThicknessU16  (value * 10)
 18       1   MinZoomLod
 19       1   MaxZoomLod
 20       4   LifetimeSeconds

Offset  Size  Payload union (selected variants)
------  ----  ----------------------------------
 24-47       Line:    LineStart (Vec3) + LineEnd (Vec3)
 48-51       Line:    EndColor (gradient)
 54           Line:    LineStyle
 56-59        Line:    FillColor

 24-35       Sphere:  SphereCenter (Vec3)
 36           Sphere:  SphereRadius

 24-43       Box2D:   CenterX/Y, ExtentX/Y, AngleDeg
 44-51       Box2D:   BoxAnchorId (long)
 52-53       Box2D:   SubElementId
 54           Box2D:   LineStyle
 56-59        Box2D:   FillColor

 24-35       Arrow:   ArrowFrom (Vec3)
 36-47       Arrow:   ArrowTo (Vec3)
 48-51       Arrow:   ArrowHeadSize

 24-27       Text:    TextX
 28-31       Text:    TextY
 32-63       Text:    TextContent (FixedString32)

 24-27       SpatialAnchor: NetworkId lo32
 24-31       SpatialAnchor: NetworkId (long)
 32-35       Anchor:  AnchorWorldX
 36-39       Anchor:  AnchorWorldY
 40-43       Anchor:  AnchorWorldZ
 44-47       Anchor:  Heading (deg)
 48-51       Anchor:  Pitch (deg)
 52-55       Anchor:  Roll (deg)
```

The `SemanticShape` variant uses `ResolvedWorldX/Y/YawRad/PitchRad/RollRad` fields that
occupy otherwise-unused payload bytes and are written in-place by the renderer's two-pass
algorithm. These fields are transmitted as zeros over the network.

---

## Source Structure

```
GizmoMap.Contracts/
+-- GizmoMap.Contracts.csproj
+-- StringInternMap.cs              FNV-1a concurrent hash -> string dictionary
|
+-- Abstractions/
|   +-- IDebugDrawBuilder.cs        IGizmoDrawBuilder interface (all draw methods)
|
+-- Interaction/
|   +-- ContextMenuItemDto.cs       JSON-serializable context menu item DTO
|   +-- IGizmoInteractionHandler.cs Common interaction handler contract
|   +-- IStatefulGizmo.cs           Lifecycle-aware gizmo interface
|   +-- MapKeyboardKey.cs           Platform-agnostic keyboard key enum
|   +-- MapMouseButton.cs           Platform-agnostic mouse button enum
|
+-- Primitives/
|   +-- CoordinateSpace.cs          World / Screen / EntityLocal enum
|   +-- DebugPrimitive.cs           64-byte tagged union (core type)
|   +-- DebugPrimitiveBuffer.cs     GizmoPrimitiveBuffer (thread-safe ring)
|   +-- DebugPrimitiveShape.cs      Shape discriminant enum (15 values)
|   +-- FixedString32.cs            Zero-alloc 32-byte UTF-8 string
|   +-- LayerMask256.cs             256-bit layer visibility bitmask
|   +-- LineStyle.cs                Solid / Dashed / Dotted
|   +-- PipelineTarget.cs           Map2D / Viewport3D / NodeGraph flags
|   +-- Rgba32.cs                   4-byte RGBA color
|   +-- ScreenAnchor.cs             TopLeft / Center / BottomRight enum
|   +-- SizeMode.cs                 WorldMeters / ScreenPixels / ScreenPercent
|
+-- Sources/
|   +-- GizmoPickToken.cs           Network-stable pick identifier
|   +-- IGizmoSource.cs             Per-frame primitive emitter interface
|
+-- Transport/
    +-- IGizmoTransport.cs          Publish + poll transport abstraction
```

---

## Public API Reference

### Primitives

#### `DebugPrimitive` (struct, 64 bytes, `[StructLayout(Explicit)]`)

The central value type. A blittable tagged union encoding all visual shapes and
non-visual meta-primitives in exactly 64 bytes (one cache line).

**Header fields (every shape)**

| Member              | Type                 | Description                                   |
|---------------------|----------------------|-----------------------------------------------|
| `Shape`             | `DebugPrimitiveShape`| Discriminant; determines which payload is live |
| `Space`             | `CoordinateSpace`    | World, Screen, or EntityLocal                 |
| `Color`             | `Rgba32`             | Primary draw color                            |
| `TargetView`        | `PipelineTarget`     | Flags: Map2D, Viewport3D, NodeGraph           |
| `DebugLayer`        | `byte`               | Layer index 0-15                              |
| `AnchorIndex`       | `int`                | ECS entity index (EntityLocal space)          |
| `StringHash`        | `uint`               | Intern map key (overlaid with AnchorIndex)    |
| `AnchorGeneration`  | `ushort`             | ECS generation; 0 = null/uninitialized        |
| `SizeMode`          | `SizeMode`           | How thickness/radius are interpreted          |
| `ZIndex`            | `byte`               | Intra-layer sort order                        |
| `ThicknessU16`      | `ushort`             | Thickness * 10 (max 6553.5)                   |
| `MinZoomLod`        | `byte`               | Min camera zoom * 0.25; 0 = no limit          |
| `MaxZoomLod`        | `byte`               | Max camera zoom * 0.25; 0 = no limit          |
| `LifetimeSeconds`   | `float`              | 0 = one frame; > 0 = persistent               |

**Static factory methods**

| Method                    | Shape produced        |
|---------------------------|-----------------------|
| `MakeLine(...)`           | `Line`                |
| `MakeSphere(...)`         | `Sphere`              |
| `MakeBox2D(...)`          | `Box2D`               |
| `MakeArrow(...)`          | `Arrow`               |
| `MakeText(...)`           | `Text`                |
| `MakeSpatialAnchor(...)`  | `SpatialAnchor`       |
| `MakeSemanticShape(...)`  | `SemanticShape`       |
| `MakeMilStd2525(...)`     | `MilStd2525`          |
| `MakeLayerControlMask(...)` | `LayerControlMask`  |
| `MakeStructInspector(...)`| `StructInspector`     |

---

#### `DebugPrimitiveShape` (enum, `byte`)

| Value                  | Numeric | Description                                             |
|------------------------|---------|----------------------------------------------------------|
| `Line`                 | 0       | Straight line segment with optional gradient            |
| `Sphere`               | 1       | Circle (2D) or sphere (3D)                              |
| `Box2D`                | 2       | Rotatable 2D rectangle with optional interaction handle  |
| `Arrow`                | 3       | Line with arrowhead                                     |
| `Text`                 | 4       | Text at a 2D position                                   |
| `EntityBadge`          | 5       | Rich-text label anchored to an entity                   |
| `Icon`                 | 6       | Atlas icon at a 2D world position                       |
| `StructInspector`      | 7       | Non-visual: schedules a StructEdit property panel       |
| `SemanticShape`        | 8       | Entity tactical silhouette (DIS type)                   |
| `MilStd2525`           | 9       | NATO MIL-STD-2525 symbol                                |
| `SpatialAnchor`        | 10      | Pre-resolved world position + orientation               |
| `ContextMenuBinding`   | 11      | Non-visual: binds a JSON menu hash to a NetworkId        |
| `InputCaptureBinding`  | 12      | Non-visual: requests exclusive raw HW event routing     |
| `MainMenuBinding`      | 13      | Non-visual: injects a JSON menu into the global menu bar |
| `LayerControlMask`     | 14      | Non-visual: 256-bit layer visibility assertion           |

---

#### `GizmoPrimitiveBuffer` (class)

Thread-safe append-only ring buffer. Pre-allocated at construction. Overflow is silently
dropped and counted in `DroppedCount`. Persistence is managed via `EndFrame(deltaTime)`.

| Member                                       | Description                                          |
|----------------------------------------------|------------------------------------------------------|
| `GizmoPrimitiveBuffer(int capacity, ...)`    | Constructor; default capacity = 4096                 |
| `void AppendRaw(in DebugPrimitive)`          | Thread-safe append; used by network ingress          |
| `ReadOnlySpan<DebugPrimitive> GetFrame()`    | Zero-copy span of all primitives for the current frame |
| `void Clear()`                               | Reset transient cursor; persistent entries survive  |
| `void EndFrame(float deltaTime)`             | Advance persistence clock, evict expired, re-inject  |
| `int DroppedCount`                           | Overflow drop counter                               |
| `StringInternMap InternMap`                  | Shared intern map for long-text resolution           |

---

#### `IGizmoDrawBuilder` (interface)

All draw methods that simulation code uses to emit primitives into a buffer.

| Method                  | Description                                         |
|-------------------------|-----------------------------------------------------|
| `DrawLine(...)`         | Straight line, solid/dashed/dotted                  |
| `DrawLineGradient(...)` | Line with per-endpoint color                        |
| `DrawSphere(...)`       | Circle/sphere with optional fill                    |
| `DrawBox2D(...)`        | Rotatable box with optional fill and interaction ID |
| `DrawArrow(...)`        | Arrow with configurable head size                   |
| `DrawText(...)`         | Short text (up to 31 chars inline)                  |
| `DrawTextLong(...)`     | Long text via intern map; cold-path allocation only |
| `EndFrame(float)`       | Frame-boundary hook (no-op default)                 |
| `DrawMainMenuBinding(string)` | Inject menu JSON into main menu bar           |
| `EmitRaw(in DebugPrimitive)`  | Emit any shape not covered by typed methods   |

---

### Interaction

#### `IGizmoInteractionHandler` (interface)

Base interaction contract for all gizmos that respond to user input.

| Member                                   | Description                                       |
|------------------------------------------|---------------------------------------------------|
| `bool RequiresExclusiveFocus`            | Manager emits `InputCaptureBinding` when true     |
| `bool WantsRawInput`                     | Requests raw HW event routing (default false)     |
| `bool IsFocused`                         | Whether this gizmo currently holds focus          |
| `void SetFocus(bool)`                    | Called by manager to grant or revoke focus        |
| `void OnInteractionStarted(token, pos)`  | Spatial hit; token carries sub-element ID         |
| `void OnDragUpdate(Vector3)`             | Mouse drag update                                 |
| `void OnCommit(Vector3)`                 | Drag confirmed (mouse up)                         |
| `void OnCancel()`                        | Drag cancelled (ESC or right-click)               |
| `void OnMenuAction(int)`                 | Context menu item clicked                         |
| `void OnStructUpdate(string)`            | StructEdit panel committed a mutation             |
| `void OnMouseEvent(button, isPressed, pos)` | Raw mouse event (exclusive capture only)       |
| `void OnKeyEvent(key, isPressed)`        | Raw keyboard event (exclusive capture only)       |

---

#### `IStatefulGizmo` (interface, extends `IGizmoInteractionHandler`, `IDisposable`)

Adds lifecycle management and per-frame drawing.

| Member                                   | Description                                    |
|------------------------------------------|------------------------------------------------|
| `void UpdateAndDraw(float, IGizmoDrawBuilder)` | Called each frame; gizmo emits its shapes |

---

#### `GizmoPickToken` (struct)

Network-stable pick identifier used to route interaction events back to their gizmo.

| Field           | Type    | Description                                              |
|-----------------|---------|----------------------------------------------------------|
| `AnchorId`      | `long`  | NetworkId / semantic entity ID (0 = invalid)             |
| `SubElementId`  | `uint`  | Gizmo sub-element index within the anchored entity       |
| `StreamId`      | `uint`  | Publisher stream discriminator (multi-SimHost clusters)  |
| `GizmoTypeId`   | `uint`  | FNV-1a hash of the gizmo class for composite routing     |
| `IsValid`       | `bool`  | True when `AnchorId != 0`                                |

---

#### `ContextMenuItemDto` (sealed class)

Serializable DTO for a single entry in a context menu JSON array. Annotated with
`System.Text.Json` attributes; `JsonIgnoreCondition.WhenWritingNull/Default` keeps the
on-wire payload compact.

| Property     | Type               | JSON key    | Description                             |
|--------------|--------------------|-------------|------------------------------------------|
| `Id`         | `int`              | `"id"`      | Action ID sent on click; 0 for headers   |
| `Label`      | `string?`          | `"label"`   | Display text                             |
| `Icon`       | `string?`          | `"icon"`    | Atlas key for optional icon              |
| `Enabled`    | `bool?`            | `"enabled"` | False renders item greyed out            |
| `Style`      | `string?`          | `"style"`   | Visual hint, e.g. `"destructive"`       |
| `Shortcut`   | `string?`          | `"shortcut"`| Keyboard shortcut label                  |
| `Tooltip`    | `string?`          | `"tooltip"` | Hover tooltip text                       |
| `IsSeparator`| `bool?`            | `"separator"`| Renders a horizontal divider            |
| `Children`   | `ContextMenuItemDto[]?` | `"children"` | Nested submenu items              |

---

### Supporting Value Types

#### `Rgba32` (struct, 4 bytes)

| Member              | Description                              |
|---------------------|------------------------------------------|
| `R, G, B, A`        | Byte channels                            |
| Static constants    | `Red, Green, Yellow, White, Black, Transparent` |

#### `FixedString32` (struct, 32 bytes, unsafe)

Zero-allocation fixed-size UTF-8 string. Stores up to 31 bytes + null terminator.
Used in `DebugPrimitive.TextContent`, `SidcCode`, and `IconAtlasCoord` payload fields.

#### `LayerMask256` (struct, 32 bytes)

256-bit visibility bitmask backed by four `ulong` quads. Used by `LayerControlMask`
primitives to assert which layers are visible on a given frame.

| Method         | Description                        |
|----------------|------------------------------------|
| `SetAll()`     | Set all 256 bits                   |
| `SetBit(int)`  | Set one bit                        |
| `IsSet(int)`   | Test one bit                       |

#### `StringInternMap` (sealed class)

Concurrent-safe FNV-1a hash -> string dictionary. Used to escape `DebugPrimitive`'s
31-char text limit for long strings. Registration (cold path) allocates; lookup does not.

| Method                         | Description                           |
|--------------------------------|---------------------------------------|
| `Intern(uint hash, string)`    | Register full text under hash         |
| `TryResolve(uint hash)`        | Return text or null; no allocation    |
| `IReadOnlyDictionary Entries`  | All interned entries                  |
| `Flush()`                      | Clear all entries                     |
| `static Fnv1a32(string)`       | Compute the FNV-1a 32-bit hash        |

---

### Enumerations Summary

| Enum               | Values                                              |
|--------------------|-----------------------------------------------------|
| `CoordinateSpace`  | `World`, `Screen`, `EntityLocal`                    |
| `LineStyle`        | `Solid`, `Dashed`, `Dotted`                         |
| `PipelineTarget`   | `None=0`, `Map2D=1`, `Viewport3D=2`, `NodeGraph=4`, `All=7` |
| `SizeMode`         | `WorldMeters`, `ScreenPixels`, `ScreenPercent`      |
| `ScreenAnchor`     | `TopLeft`, `TopCenter`, `TopRight`, `Center`, `BottomLeft`, `BottomCenter`, `BottomRight` |
| `MapMouseButton`   | `Left=0`, `Right=1`, `Middle=2`, modifier masks     |
| `MapKeyboardKey`   | `Escape`, `Enter`, `Tab`, `Delete`, modifier keys, modifier masks |

---

### Transport

#### `IGizmoTransport` (interface, `IDisposable`)

| Method                                                      | Description                                  |
|-------------------------------------------------------------|----------------------------------------------|
| `PublishPrimitives(ReadOnlySpan<DebugPrimitive>, StringInternMap?)` | Serialize and send current frame   |
| `PollAndApply(GizmoPrimitiveBuffer)`                        | Receive incoming primitives and intern strings |

#### `IGizmoSource` (interface)

| Method                             | Description                                  |
|------------------------------------|----------------------------------------------|
| `Emit(float deltaTime, IGizmoDrawBuilder)` | Called once per frame to produce primitives |

---

## Dependencies

```
+-----------------------------+
| GizmoMap.Contracts          |
|                             |
| net8.0:                     |
|   CycloneDDS.NET 0.2.2      |  <-- PipelineTarget IDL generation only
|   (BCL built-in)            |
|                             |
| netstandard2.1:             |
|   System.Runtime.CompilerServices.Unsafe 6.0.0
|   System.Text.Json 8.0.5    |
|                             |
| NO project references       |
+-----------------------------+
```

The CycloneDDS.NET reference on `net8.0` exists only to trigger IDL code generation for
`PipelineTarget`. It does not create a runtime coupling: no DDS types are used in the
public API surface of this assembly.

---

## Usage Examples

### Example 1: Direct Buffer Use in a Simulation Frame

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
using System.Numerics;

// Allocate once (e.g. at startup).
var buffer = new GizmoPrimitiveBuffer(capacity: 2048);

void SimulationTick(float dt)
{
    // Clear transient primitives; re-injects persistent ones.
    buffer.EndFrame(dt);

    // Draw a red velocity arrow for an entity at position (100, 200).
    buffer.DrawArrow(
        from: new Vector3(100f, 200f, 0f),
        to:   new Vector3(130f, 200f, 0f),
        color: Rgba32.Red,
        headSize: 8f);

    // Draw a text label using the inline path (< 32 chars).
    buffer.DrawText(100f, 185f, new FixedString32("T-72B"), Rgba32.White);

    // Draw a long label via the intern map (cold path allocates once per unique string).
    buffer.DrawTextLong(100f, 170f, "Unit 42 - 1st Armoured Battalion", Rgba32.Yellow);

    // Obtain a zero-copy read-only span for rendering.
    ReadOnlySpan<DebugPrimitive> frame = buffer.GetFrame();
    // ... pass to renderer
}
```

### Example 2: Implementing a Stateful Gizmo

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using System;
using System.Numerics;

// A simple move-handle gizmo: draws a box at a position and lets the operator drag it.
public sealed class MoveHandleGizmo : IStatefulGizmo
{
    private Vector2 _pos;
    private Vector2 _savedPos;
    private bool _dragging;

    public bool RequiresExclusiveFocus => false;
    public bool IsFocused { get; private set; }
    public void SetFocus(bool v) => IsFocused = v;

    public MoveHandleGizmo(Vector2 initialPos) => _pos = initialPos;

    public void UpdateAndDraw(float dt, IGizmoDrawBuilder draw)
    {
        // Emit an interactive Box2D handle.
        var prim = default(DebugPrimitive);
        prim.Shape       = DebugPrimitiveShape.Box2D;
        prim.Space       = CoordinateSpace.World;
        prim.TargetView  = PipelineTarget.Map2D;
        prim.BoxCenterX  = _pos.X;
        prim.BoxCenterY  = _pos.Y;
        prim.BoxExtentX  = 10f;
        prim.BoxExtentY  = 10f;
        prim.Color       = _dragging ? Rgba32.Yellow : Rgba32.Green;
        prim.SubElementId = 1;
        prim.BoxAnchorId  = 42L;  // stable anchor ID
        draw.EmitRaw(in prim);
    }

    public void OnInteractionStarted(GizmoPickToken token, Vector3 pos)
    {
        _savedPos = _pos;
        _dragging = true;
    }

    public void OnDragUpdate(Vector3 pos) => _pos = new Vector2(pos.X, pos.Y);
    public void OnCommit(Vector3 pos)     { _pos = new Vector2(pos.X, pos.Y); _dragging = false; }
    public void OnCancel()                { _pos = _savedPos; _dragging = false; }

    public void OnMenuAction(int id) { }
    public void OnMouseEvent(MapMouseButton b, bool p, Vector3 pos) { }
    public void OnKeyEvent(MapKeyboardKey k, bool p) { }
    public void Dispose() { }
}
```

### Example 3: Serializing a Context Menu

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using System.Text.Json;

var options = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
};

var items = new[]
{
    new ContextMenuItemDto { Id = 1, Label = "Move", Shortcut = "M" },
    new ContextMenuItemDto { IsSeparator = true },
    new ContextMenuItemDto
    {
        Label = "Orders",
        Children = new[]
        {
            new ContextMenuItemDto { Id = 10, Label = "Attack" },
            new ContextMenuItemDto { Id = 11, Label = "Defend" },
        }
    },
    new ContextMenuItemDto { Id = 99, Label = "Delete", Style = "destructive" },
};

string json = JsonSerializer.Serialize(items, options);
// Intern and bind via buffer.DrawMainMenuBinding(json) or EmitRaw(MakeContextMenuBinding(...))
```

---

## Best Practices

1. **Allocate buffers once at startup.** `GizmoPrimitiveBuffer` is pre-sized; the 4096-slot
   default covers typical simulation frames. Re-using the same buffer instance eliminates all
   heap allocation on the hot draw path.

2. **Call `EndFrame(deltaTime)` before your simulation draw phase.** This evicts expired
   persistent primitives and resets the transient counter. Calling `Clear()` instead
   skips persistence management and is only appropriate for test scenarios.

3. **Use `DrawText` for strings up to 31 chars; use `DrawTextLong` for longer strings.**
   `DrawTextLong` allocates once per unique string on the intern map registration path.
   Subsequent calls with the same text are zero-allocation.

4. **Set `LifetimeSeconds > 0` for slow-moving or infrequently updated primitives.**
   Persistent primitives survive `EndFrame` calls without being re-emitted by simulation
   code every frame.

5. **Never evaluate `AnchorIndex == 0` to test handle validity.** Index 0 is a perfectly
   valid ECS entity offset in a data-oriented ECS. Always check `AnchorGeneration != 0`
   for handle validity.

6. **Set `TargetView` explicitly.** Default-constructed primitives have `TargetView = 0`
   (None), which means they are filtered out by all pipelines. Always set `PipelineTarget.Map2D`,
   `.Viewport3D`, or `.All`.

7. **Use `GizmoPickToken.GizmoTypeId` for composite routing.** When multiple gizmo types
   can be active on the same entity simultaneously, set `GizmoTypeId` to the FNV-1a hash
   of the implementing type name so the terminal can route hits unambiguously.

8. **Respect the 64-byte limit.** Do not extend `DebugPrimitive` beyond 64 bytes. For
   new shapes requiring more payload, introduce a companion `SpatialAnchor` primitive and
   implement a two-pass resolution in the renderer.

---

## Related Projects

| Project                  | Relationship                                                     |
|--------------------------|------------------------------------------------------------------|
| `GizmoMap.Network`       | Consumes Contracts types; adds DDS transport over `IGizmoTransport` |
| `GizmoMap.Presentation`  | Consumes Contracts types; renders primitives with Raylib + ImGui |
| `GizmoMap.Viewer`        | Standalone viewer; wires Network + Presentation                  |
| `GizmoMap.Example`       | Reference implementation; exercises all Contracts interfaces     |
| `Fdp.Diagnostics.Contracts` | ECS-extended sister library; adds entity-dependent draw methods that delegate to `IGizmoDrawBuilder` |
| `StructEdit.Core`        | Referenced indirectly; `StructInspector` shape is the bridge     |
