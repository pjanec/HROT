# Fdp.Diagnostics.Contracts

**Project file**: `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Fdp.Diagnostics.Contracts.csproj`
**Documentation date**: 2026-05-23

---

## README Validation

**Missing** -- no `README.md` exists in the project folder.

---

## Executive Overview

`Fdp.Diagnostics.Contracts` is the **contracts layer** of the FDP diagnostics subsystem. It defines the
types, interfaces, and lightweight implementations that allow any FDP module -- simulation engines, ECS
systems, network bridges, toolkits -- to emit debug visualization primitives without depending on heavier
rendering or networking assemblies.

### Role in the FDP Framework

The project sits at the boundary between the **simulation layer** and the **visualization/monitoring
layer**. Simulation code calls `IDebugDrawBuilder` methods to record what it wants drawn. The resulting
`DebugPrimitive` stream is then consumed downstream by:

- **Fdp.Presentation** -- renders primitives in the in-process imgui/raylib viewport.
- **GizmoMap** -- serializes the stream over DDS and delivers it to the IG terminal.
- **Hrot.IG** -- renders the received stream in the separate image-generator process.

### Architectural Layer

```
+------------------------------------------+
|           Application / Toolkit          |
|  (gizmo systems, ECS, scenario scripts)  |
+------------------------------------------+
              |  IDebugDrawBuilder
              v
+------------------------------------------+
|      Fdp.Diagnostics.Contracts           |  <-- THIS PROJECT
|  IDebugDrawBuilder / DebugPrimitiveBuffer|
|  PickToken / EcsDebugPrimitiveExtensions |
+------------------------------------------+
     |                         |
     | (type forwarding)       | (Fdp.Core types)
     v                         v
+------------------+  +------------------+
| GizmoMap.Contracts|  |    Fdp.Core      |
| (DebugPrimitive, |  | (Entity,         |
|  enums, structs) |  |  FixedString32)  |
+------------------+  +------------------+
```

The project is deliberately **shallow**: it declares one interface, one concrete buffer, one extension
class, one primitive struct, and a file of global type aliases. The thin surface minimises coupling and
keeps compile times short for all consumers.

---

## Architecture

### Design Principles

**1. Zero per-frame allocation on the draw path.**
`DebugPrimitiveBuffer` is pre-allocated at construction time. The hot path uses
`Interlocked.Increment` to claim a slot; if capacity is exhausted the primitive is silently
dropped and `DroppedCount` incremented. No GC pressure during simulation ticks.

**2. Single CLR type for `DebugPrimitive`.**
All FDP code was previously subject to a type-identity fracture: `GizmoMap.Contracts.DebugPrimitive`
and an FDP-local copy diverged at the CLR level. `TypeForwards.cs` resolves this with `global using`
aliases backed by `extern alias GizmoMapContracts`, so every assembly that references this project
uses the single canonical type from `GizmoMap.Contracts`.

**3. `FixedString32` intentional split.**
`Fdp.Core.FixedString32` is deliberately **not** forwarded. The interface layer (`IDebugDrawBuilder`)
uses the FDP type for backward compatibility. `DebugPrimitiveBuffer` bridges the two types at write
boundaries via `Unsafe.As` reinterpret-cast, which is safe because both types share an identical
32-byte sequential layout.

**4. `IDebugDrawBuilder` does not inherit `IGizmoDrawBuilder`.**
Inheriting `IGizmoDrawBuilder` would expose `FixedString32` overloads that refer to the
GizmoMap-side type, creating an ambiguity for all callers that have both assemblies in scope.
Instead, `DebugPrimitiveBuffer` satisfies both interfaces at the class level via explicit
`IGizmoDrawBuilder` implementation, and `IDebugDrawBuilder` is kept fully independent.

**5. ECS coupling is pushed to extension methods.**
The core `DebugPrimitive` struct (owned by `GizmoMap.Contracts`) carries `AnchorIndex` and
`AnchorGeneration` as plain integer fields. `EcsDebugPrimitiveExtensions` recomposes them into
`Entity` and `PickToken` values, keeping the ECS dependency out of `GizmoMap.Contracts` while
still being conveniently accessible via dot-notation in FDP code.

**6. Persistence is frame-lifecycle-aware.**
Primitives with `LifetimeSeconds > 0` are copied into a fixed-size (`PersistentCapacity = 256`)
secondary array. `EndFrame(deltaTime)` compacts this array and re-injects surviving entries into
the transient buffer before gizmo systems run.

**7. Two-pass network rendering for entity shapes.**
`DebugPrimitive` is a 64-byte struct (one CPU cache line). A full 3D transform plus SemanticShape
payload would overflow the 40-byte union budget. The solution uses two separate primitives:
- `SpatialAnchor` -- carries world position and full 3D orientation, keyed by `NetworkId`.
- `SemanticShape` -- carries profile and dimensions, references the anchor via `AnchorIndex = (int)networkId`.

The terminal renderer performs a two-pass resolve: pass 1 caches all `SpatialAnchor` entries by
`NetworkId`; pass 2 looks up the anchor for each `SemanticShape`, computes absolute world
coordinates, and writes them into the in-place `ResolvedWorld*` fields of the struct.

**8. `GizmoTypeId` routing key.**
The `GizmoTypeId` field (FNV-1a hash of the implementing gizmo type's full CLR name) is stamped
into emitted primitives by `StampGizmoTypeId`. It propagates through the gizmo stream to the IG
terminal so that multiple gizmos on the same entity can be disambiguated by the interaction
dispatcher. Shape-gated stamping avoids corrupting the `SemanticShape.ResolvedRollRad` field that
occupies the same offset (60).

---

## ASCII Block Diagrams

### 1. Component Relationships

```
+-------------------------------+
|  Gizmo system / ECS module   |
|  (caller of IDebugDrawBuilder)|
+-------------------------------+
            |
            | calls Draw* methods
            v
+-------------------------------+
|     IDebugDrawBuilder         |
|   (interface contract)        |
+-------------------------------+
            |
            | implemented by
            v
+-------------------------------+        +-----------------------+
|   DebugPrimitiveBuffer        |------->|  DebugPrimitive[]     |
|   - _primitives[capacity]     |        |  (blittable, 64 bytes)|
|   - _persistent[256]          |        +-----------------------+
|   - _count (Interlocked)      |
|   - _internMap                |
+-------------------------------+
            |
            | GetFrame() returns
            v
+-------------------------------+
|   ReadOnlySpan<DebugPrimitive>|
|   (zero-copy frame snapshot)  |
+-------------------------------+
            |
            |   consumed by
   +--------+--------+
   |                 |
   v                 v
+--------+   +---------------+
|Fdp.    |   | GizmoMap      |
|Present.|   | (DDS transport|
|(local  |   |  to IG)       |
| render)|   +---------------+
+--------+
```

### 2. DebugPrimitive 64-byte Memory Layout

```
Offset  Size  Field(s)
+------+------+--------------------------------------------------+
|  0   |  1   | Shape (DebugPrimitiveShape byte enum)            |
|  1   |  1   | CoordinateSpace                                  |
|  2   |  4   | Color (Rgba32)                                   |
|  6   |  1   | TargetView (PipelineTarget flags byte)           |
|  7   |  1   | DebugLayer                                       |
+------+------+--------------------------------------------------+
|  8   |  4   | AnchorIndex  /  StringHash  (union)             |
| 12   |  2   | AnchorGeneration                                 |
| 14   |  1   | SizeMode                                         |
| 15   |  1   | ZIndex                                           |
| 16   |  2   | ThicknessU16 (value * 10)                        |
| 18   |  1   | MinZoomLod                                       |
| 19   |  1   | MaxZoomLod                                       |
| 20   |  4   | LifetimeSeconds                                  |
+------+------+--------------------------------------------------+
| 24   | 40   | Payload union (shape-dependent, see below)       |
+------+------+--------------------------------------------------+
| 60   |  4   | GizmoTypeId (Box2D / StructInspector / CtxMenu)  |
|      |      | (= ResolvedRollRad for SemanticShape -- no stamp)|
+------+------+--------------------------------------------------+

Payload union variants:
  Line:          LineStart(V3) @ 24, LineEnd(V3) @ 36, EndColor @ 48
  Sphere:        SphereCenter(V3) @ 24, SphereRadius @ 36
  Box2D:         BoxCenter @ 24/28, BoxExtent @ 32/36, BoxAngle @ 40, BoxAnchorId @ 44
  Arrow:         ArrowFrom(V3) @ 24, ArrowTo(V3) @ 36, ArrowHeadSize @ 48
  Text:          TextX/Y @ 24/28, TextContent(FixedString32) @ 32
  EntityBadge:   BadgeTargetIndex @ 24, BadgeTargetGen @ 28, BadgeRichText @ 32
  SemanticShape: ProfileId @ 24, LengthMeters @ 32, WidthMeters @ 36, ConditionMask @ 40
  SpatialAnchor: NetworkId @ 24, AnchorWorld(X/Y/Z) @ 32/36/40, Heading @ 44, Pitch @ 48, Roll @ 52
```

### 3. IDebugDrawBuilder Interface Hierarchy and Implementations

```
+-------------------------+        +--------------------------+
|  GizmoMapContracts::    |        |  Fdp.Diagnostics.        |
|  IGizmoDrawBuilder      |        |  Contracts ::            |
|  (GizmoMap.Contracts)   |        |  IDebugDrawBuilder       |
+-------------------------+        +-----------+--------------+
           |                                   |
           | explicit implementation           | implements
           |    at class level                 |
           +-------------------+---------------+
                               |
                               v
                  +---------------------------+
                  |   DebugPrimitiveBuffer    |
                  |   (sealed class)          |
                  | - Append()                |
                  | - AppendRaw()             |
                  | - EmitRaw()               |
                  | - StampGizmoTypeId()      |
                  | - EndFrame(deltaTime)     |
                  | - GetFrame()              |
                  | - Clear()                 |
                  +---------------------------+
```

### 4. Frame Lifecycle with Persistence

```
  Frame N-1 ends
       |
       v
+----------------+
| EndFrame(dt)   |
|                |
|  1. compact    |
|     _persistent|-----> evict expired (remainingLife <= dt)
|                |
|  2. reset      |
|     transient  |-----> _count = 0, _droppedCount = 0
|     buffer     |
|                |
|  3. re-inject  |-----> surviving entries copied into
|     persistent |       _primitives[0..persistentCount-1]
|     entries    |
+----------------+
       |
       v
  Frame N gizmo systems run
       |
       v
  Draw* calls append new transient primitives
  (slot N+persistentCount .. N+persistentCount+transient)
       |
       v
  GetFrame() returns span [0 .. count)
  (persistent survivors + new transients interleaved)
```

### 5. Two-pass Network Rendering for Entity Shapes

```
  Simulation host (FDP process)
  +-----------------------------------+
  | DrawSpatialAnchor(networkId, ...) |--> SpatialAnchor primitive
  | DrawSemanticShape(networkId, ...) |--> SemanticShape primitive
  +-----------------------------------+
            |  DDS gizmo stream
            v
  IG Terminal (remote process)
  +-----------------------------------+
  | PASS 1: cache SpatialAnchors      |
  |   anchors[networkId] = {X,Y,Z,    |
  |                         Hdg,Pitch,Roll}
  +-----------------------------------+
            |
            v
  +-----------------------------------+
  | PASS 2: resolve SemanticShapes    |
  |   anchor = anchors[AnchorIndex]   |
  |   prim.ResolvedWorldX = anchor.X  |
  |   prim.ResolvedWorldY = anchor.Y  |
  |   prim.ResolvedYawRad = ...       |
  +-----------------------------------+
            |
            v
  Render pass: draw shape at ResolvedWorld* coords
  (zero dictionary lookups at draw time)
```

---

## Source Structure

### Namespaces

| Namespace | Description |
|---|---|
| `Fdp.Toolkit.Diagnostics.Gizmos` | All types in this project (declared namespace) |
| `Fdp.Diagnostics.Contracts` | Root namespace from .csproj (no types declared here directly) |

> Note: The .csproj sets `RootNamespace` to `Fdp.Diagnostics.Contracts`, but all source files
> explicitly declare `namespace Fdp.Toolkit.Diagnostics.Gizmos`. This is intentional for
> compatibility with the broader GizmoMap type hierarchy.

### Files

| File | Type(s) | Description |
|---|---|---|
| `TypeForwards.cs` | `global using` aliases | Re-exports 9 canonical types from `GizmoMap.Contracts` |
| `IDebugDrawBuilder.cs` | `IDebugDrawBuilder` (interface) | FDP-extended draw command contract |
| `DebugPrimitiveBuffer.cs` | `DebugPrimitiveBuffer` (sealed class) | Thread-safe, pre-allocated primitive buffer |
| `EcsDebugPrimitiveExtensions.cs` | `EcsDebugPrimitiveExtensions` (static class) | ECS-specific extensions on `DebugPrimitive` |
| `Primitives/PickToken.cs` | `PickToken` (struct) | Hit-test token carrying entity target + routing keys |

---

## Public API Reference

### Type: `IDebugDrawBuilder` (interface)

**Namespace**: `Fdp.Toolkit.Diagnostics.Gizmos`
**File**: `IDebugDrawBuilder.cs`

The primary draw contract. Systems that want to emit debug visualization call methods on this
interface without needing to know whether rendering is local, deferred, or remote.

Does **not** inherit `GizmoMapContracts::IGizmoDrawBuilder` to avoid the `FixedString32` type
conflict. `DebugPrimitiveBuffer` satisfies both interfaces at the class level.

#### Methods

| Signature | Description |
|---|---|
| `DrawLine(Vector3 start, Vector3 end, Rgba32 color, float thickness, SizeMode, PipelineTarget, byte layer, LineStyle)` | Emits a straight line segment. Defaults: thickness=1, ScreenPixels, All targets, layer=0, Solid. |
| `DrawLineGradient(Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor, float thickness, SizeMode, PipelineTarget, byte layer, LineStyle)` | Emits a line with per-endpoint colors. |
| `DrawSphere(Vector3 center, float radius, Rgba32 color, float thickness, SizeMode, PipelineTarget, byte layer, Rgba32 fillColor, LineStyle)` | Emits a sphere outline. `thickness=0` means filled. |
| `DrawBox2D(Vector2 center, Vector2 extents, Rgba32 color, float angleDeg, float thickness, SizeMode, PipelineTarget, byte layer, Rgba32 fillColor, LineStyle, long anchorId, ushort subElementId)` | Emits a 2D oriented rectangle. Default implementation is a no-op; `DebugPrimitiveBuffer` overrides it. |
| `DrawArrow(Vector3 from, Vector3 to, Rgba32 color, float headSize, byte layer)` | Emits a directional arrow. |
| `DrawText(float x, float y, FixedString32 text, Rgba32 color, CoordinateSpace, byte layer)` | Emits inline text (31-char max). Uses FDP-side `FixedString32`. |
| `DrawTextLong(float x, float y, string text, Rgba32 color, CoordinateSpace, byte layer)` | Interns full managed string on first call (allocates); subsequent identical strings are allocation-free. Stores FNV-1a hash in `StringHash` overlay. |
| `DrawEntityBadge(Entity target, FixedString32 richText, PipelineTarget)` | Attaches a floating rich-text badge to an ECS entity. |
| `DrawEntityLocal(Entity anchor, Vector3 localStart, Vector3 localEnd, Rgba32 color, float thickness, byte layer)` | Emits a line in entity-local space, anchored to the given entity. |
| `DrawEntityLocalInteractive(Entity anchor, Vector3 localStart, Vector3 localEnd, Rgba32 color, ushort subElementId, float thickness, byte layer)` | Same as `DrawEntityLocal` but marks the primitive as interactive with a `subElementId` for hit routing. |
| `EndFrame(float deltaTime)` | Default no-op. `DebugPrimitiveBuffer` overrides to advance persistence clock, evict expired entries, and re-inject survivors. |
| `DrawSpatialAnchor(long networkId, float worldX, float worldY, float worldZ, float headingDeg, float pitchDeg, float rollDeg, byte layer)` | Emits a `SpatialAnchor` primitive. Must precede `DrawSemanticShape` with the same `networkId`. Default no-op. |
| `DrawSemanticShape(long networkId, ulong profileId, float lengthMeters, float widthMeters, uint conditionMask, byte layer)` | Emits a `SemanticShape` primitive in entity-local space. Linked to the preceding `SpatialAnchor` via `networkId`. Default no-op. |
| `DrawContextMenuBinding(long networkId, string menuJson)` | Emits a `ContextMenuBinding` meta-primitive. Interns the JSON string; subsequent identical strings allocate nothing. Default no-op. |
| `EmitRaw(in DebugPrimitive prim)` | Appends a pre-constructed `DebugPrimitive` directly. Used by the interaction manager to inject `InputCaptureBinding` meta-primitives. Default no-op. |
| `DrawEntitySphere(Entity anchor, Vector3 worldCenter, float radius, Rgba32 color, byte layer)` | Emits a hit-testable sphere anchored to an entity. Clicking triggers `GizmoInteractionStartedEvent`. Default no-op. |
| `DrawMainMenuBinding(string menuJson)` | Emits a `MainMenuBinding` meta-primitive injecting menu items into the host's main menu bar. Default no-op. |

---

### Type: `DebugPrimitiveBuffer` (sealed class)

**Namespace**: `Fdp.Toolkit.Diagnostics.Gizmos`
**File**: `DebugPrimitiveBuffer.cs`
**Implements**: `IDebugDrawBuilder`

Thread-safe, pre-allocated append buffer. All `Draw*` methods are safe to call from multiple
threads simultaneously. Overflow is handled gracefully via silent drop + counter.

#### Constructor

```csharp
public DebugPrimitiveBuffer(int capacity = 4096, StringInternMap? internMap = null)
```

Creates a buffer with pre-allocated `capacity` slots and an optional shared `StringInternMap`.
When `internMap` is `null`, a private map is created. The persistence array is always
`PersistentCapacity = 256` entries.

#### Properties

| Property | Type | Description |
|---|---|---|
| `Count` | `int` | Current transient write cursor (clamped to capacity). Safe to read from the same thread as `UpdateAndDraw`. |
| `DroppedCount` | `int` | Number of primitives dropped due to capacity overflow since the last `Clear()` or `EndFrame()`. |
| `InternMap` | `StringInternMap` | The string intern map used by `DrawTextLong`. Exposed for consumers that resolve long-text hashes (e.g. the DDS bridge). |

#### Methods

| Signature | Description |
|---|---|
| `GetFrame()` | Returns a `ReadOnlySpan<DebugPrimitive>` of all primitives written so far this frame. Zero-copy. |
| `Clear()` | Resets the transient write cursor. Does NOT affect persistent entries. For frame-boundary use call `EndFrame(dt)` instead. |
| `AppendRaw(in DebugPrimitive primitive)` | Appends a primitive directly without persistence tracking. Used by the network ingress translator to restore received primitives. Thread-safe. |
| `EmitRaw(in DebugPrimitive prim)` | `IDebugDrawBuilder.EmitRaw` implementation; delegates to `AppendRaw`. |
| `StampGizmoTypeId(int fromIndex, uint gizmoTypeId)` | Stamps `gizmoTypeId` into the `GizmoTypeId` field of transient primitives in `[fromIndex, Count)`. Only stamps `Box2D`, `StructInspector`, and `ContextMenuBinding` shapes; all others are skipped to prevent payload corruption. |
| `EndFrame(float deltaTime)` | Advances the persistence clock by `deltaTime`, evicts expired persistent entries, resets the transient buffer, and re-injects survivors. |
| All `Draw*` methods | Full implementations of every `IDebugDrawBuilder` member. |

#### Internal details

- `_count` and `_persistentCount` are incremented via `Interlocked.Increment` to support concurrent writers.
- The `Append(DebugPrimitive p)` helper is `internal` (visible to test projects via `InternalsVisibleTo`).
- `FixedString32` reinterpret: `DrawText` and `DrawEntityBadge` use `Unsafe.As<FixedString32, GizmoStr>` to bridge the two `FixedString32` types without copying.
- `DrawTextLong` stores the FNV-1a hash in `p.StringHash` (offset 8 union) and stores the first 31 chars inline as a preview fallback.

---

### Type: `EcsDebugPrimitiveExtensions` (static class)

**Namespace**: `Fdp.Toolkit.Diagnostics.Gizmos`
**File**: `EcsDebugPrimitiveExtensions.cs`

Extension methods on `DebugPrimitive` that reconstruct ECS-specific values from the integer
fields stored in the primitive. These replace former instance properties that would have
coupled `GizmoMap.Contracts.DebugPrimitive` to `Fdp.Core.Entity` and `PickToken`.

#### Methods

| Signature | Return | Description |
|---|---|---|
| `GetAnchor(this DebugPrimitive p)` | `Entity` | Reconstructs the entity anchor from `p.AnchorIndex` and `p.AnchorGeneration`. |
| `GetPickToken(this DebugPrimitive p)` | `PickToken` | Returns a pick token with `Target = GetAnchor()` and `SubElementId = p.SubElementId`. `IsValid` is true when `AnchorIndex >= 0` and `AnchorGeneration != 0`. |

---

### Type: `PickToken` (struct)

**Namespace**: `Fdp.Toolkit.Diagnostics.Gizmos`
**File**: `Primitives/PickToken.cs`
**Layout**: `LayoutKind.Sequential`

Carries the hit-test result through the ECS event bus. Produced by `EcsDebugPrimitiveExtensions.GetPickToken`.

#### Fields

| Field | Type | Description |
|---|---|---|
| `Target` | `Entity` | The ECS entity that was hit. `Entity.Null` means no entity. |
| `SubElementId` | `uint` | Sub-element discriminator for interactive primitives with multiple handles on the same entity. |
| `GizmoTypeId` | `uint` | FNV-1a hash of the gizmo type's full CLR name. Composite routing key: `(Target, GizmoTypeId)` uniquely identifies the emitting gizmo. |

#### Properties

| Property | Type | Description |
|---|---|---|
| `IsValid` | `bool` | Returns `true` when `!Target.IsNull`, i.e. `Target` refers to a live entity. |

---

### Type-forwarded Types (from `GizmoMap.Contracts`)

The following types are re-exported via `global using` aliases in `TypeForwards.cs`. Code in any
assembly that references `Fdp.Diagnostics.Contracts` can use these names without any additional
`using` directives and without the `GizmoMapContracts::` prefix.

| Alias | Source type | Description |
|---|---|---|
| `CoordinateSpace` | `GizmoMapContracts::...CoordinateSpace` | Enum: `World`, `Screen`, `EntityLocal` |
| `DebugPrimitive` | `GizmoMapContracts::...DebugPrimitive` | 64-byte blittable tagged union (see layout diagram above) |
| `DebugPrimitiveShape` | `GizmoMapContracts::...DebugPrimitiveShape` | Byte enum discriminating the payload union (15 values) |
| `PipelineTarget` | `GizmoMapContracts::...PipelineTarget` | Flags byte enum: `Map2D=1`, `Viewport3D=2`, `NodeGraph=4`, `All=7` |
| `Rgba32` | `GizmoMapContracts::...Rgba32` | 4-byte RGBA color struct |
| `LineStyle` | `GizmoMapContracts::...LineStyle` | Enum: `Solid`, `Dashed`, ... |
| `ScreenAnchor` | `GizmoMapContracts::...ScreenAnchor` | Screen-space anchor point enum |
| `SizeMode` | `GizmoMapContracts::...SizeMode` | Enum: `ScreenPixels`, `WorldMeters` |
| `StringInternMap` | `GizmoMapContracts::...StringInternMap` | Thread-safe FNV-1a-keyed string intern map |

> `FixedString32` is intentionally **not** aliased. The FDP interface layer uses `Fdp.Core.FixedString32`
> for backward compatibility. `DebugPrimitiveBuffer` bridges the two at write boundaries with `Unsafe.As`.

---

### `DebugPrimitiveShape` Enum Values (from GizmoMap.Contracts)

| Value | Integer | Description |
|---|---|---|
| `Line` | 0 | Straight line segment (world or screen space). |
| `Sphere` | 1 | Circle/sphere overlay. |
| `Box2D` | 2 | Oriented 2D rectangle. Interactive via `BoxAnchorId`. |
| `Arrow` | 3 | Directional arrow with arrowhead. |
| `Text` | 4 | Floating text label (inline or long-string interned). |
| `EntityBadge` | 5 | Rich-text badge anchored to an entity. |
| `Icon` | 6 | Atlas icon at a 2D world position. |
| `StructInspector` | 7 | Generic struct editor (StructEdit schema). |
| `SemanticShape` | 8 | Entity semantic profile primitive (DIS type / tactical shape). |
| `MilStd2525` | 9 | NATO MIL-STD-2525 symbology frame. |
| `SpatialAnchor` | 10 | Pre-resolved world position + orientation. |
| `ContextMenuBinding` | 11 | Non-visual: binds an interned JSON context-menu to a NetworkId. |
| `InputCaptureBinding` | 12 | Non-visual: declares the bound token wants raw HW events. |
| `MainMenuBinding` | 13 | Non-visual: injects an interned JSON menu array into the global main menu. |
| `LayerControlMask` | 14 | Non-visual: 256-bit layer visibility mask asserted by the backend. |

---

## Dependencies

### Project References

| Assembly | Path | Notes |
|---|---|---|
| `Fdp.Core` | `Engine/Fdp.Core/Fdp.Core.csproj` | Provides `Entity`, `FixedString32`. Direct reference. |
| `GizmoMap.Contracts` | `ExtDeps/GizmoMap/GizmoMap.Contracts/GizmoMap.Contracts.csproj` | Aliased as `GizmoMapContracts`. Provides `DebugPrimitive`, enums, `StringInternMap`. |

### NuGet Packages

None -- the project has no NuGet package references. All dependencies are project references within
the same solution.

### Compiler Settings

| Setting | Value | Significance |
|---|---|---|
| `TargetFramework` | `net8.0` | .NET 8 required for `Interlocked` improvements and C# 12 features. |
| `Nullable` | `enable` | Nullable reference types enforced throughout. |
| `AllowUnsafeBlocks` | `true` | Required for `Unsafe.As` reinterpret in `DebugPrimitiveBuffer`. |
| `LangVersion` | `12.0` | C# 12 for primary constructors and collection expressions if needed. |
| `TreatWarningsAsErrors` | `true` | Zero-warning policy enforced at build time. |

### InternalsVisibleTo

| Assembly | Purpose |
|---|---|
| `Fdp.Toolkits.Tests` | Tests for the toolkit layer that uses `DebugPrimitiveBuffer`. |
| `Fdp.Diagnostics.Contracts.Tests` | Dedicated unit tests for this project (see `ContractsStandaloneTests`). |
| `Fdp.Presentation.Tests` | Presentation layer tests that exercise the draw pipeline end-to-end. |
| `Hrot.IG.Tests` | IG terminal tests that verify primitive rendering at the remote end. |

---

## Usage Examples

### Example 1: Basic draw calls from a simulation system

```csharp
// Inject IDebugDrawBuilder via the module's draw callback.
// The buffer is typically provided by Fdp.Presentation or the gizmo system.
public void OnDraw(IDebugDrawBuilder draw, Entity tank, Vector3 position, float heading)
{
    // Draw world-space line
    draw.DrawLine(
        position,
        position + new Vector3(MathF.Cos(heading), 0f, MathF.Sin(heading)) * 10f,
        new Rgba32(0, 255, 0, 200),
        thickness: 2f,
        sizeMode: SizeMode.WorldMeters);

    // Draw entity-local axes
    draw.DrawEntityLocal(
        tank,
        localStart: Vector3.Zero,
        localEnd:   new Vector3(5f, 0f, 0f),
        color:      Rgba32.Red);

    // Draw text label
    var label = new FixedString32("T-72B3");
    draw.DrawText(position.X, position.Y, label, Rgba32.White, CoordinateSpace.World);
}
```

### Example 2: Standalone buffer usage (no framework)

```csharp
// Mirrors ContractsStandaloneTests.SC_GZ041_3 pattern.
var buffer = new DebugPrimitiveBuffer(capacity: 256);

// Draw phase
buffer.DrawLine(Vector3.Zero, Vector3.UnitX, new Rgba32(255, 0, 0, 255));
buffer.DrawSphere(new Vector3(10f, 0f, 5f), radius: 3f, color: Rgba32.Yellow);

// Consume phase (e.g., send over DDS or render locally)
ReadOnlySpan<DebugPrimitive> frame = buffer.GetFrame();
foreach (ref readonly DebugPrimitive prim in frame)
{
    switch (prim.Shape)
    {
        case DebugPrimitiveShape.Line:
            RenderLine(prim.LineStart, prim.LineEnd, prim.Color);
            break;
        case DebugPrimitiveShape.Sphere:
            RenderSphere(prim.SphereCenter, prim.SphereRadius, prim.Color);
            break;
        default:
            break; // silently skip unrecognized shapes
    }
}

// Frame boundary
buffer.EndFrame(deltaTimeSeconds: 0.016f);
```

### Example 3: Long text and string intern resolution

```csharp
var internMap = new StringInternMap();
var buffer    = new DebugPrimitiveBuffer(capacity: 64, internMap: internMap);

// First call: allocates entry in intern map (cold path).
// Subsequent calls with the same string: allocation-free.
buffer.DrawTextLong(
    x: 100f, y: 200f,
    text:  "Very long tactical label exceeding 31 characters in length",
    color: Rgba32.White,
    space: CoordinateSpace.Screen);

// The renderer on the other end resolves the hash to the full string:
ReadOnlySpan<DebugPrimitive> frame = buffer.GetFrame();
var textPrim = frame[0];
if (textPrim.StringHash != 0)
{
    string? fullText = internMap.TryResolve(textPrim.StringHash);
    // fullText == "Very long tactical label exceeding 31 characters in length"
}
```

### Example 4: Entity pick token extraction

```csharp
// After receiving a frame from the buffer (e.g. in an interaction handler):
ReadOnlySpan<DebugPrimitive> frame = buffer.GetFrame();
foreach (ref readonly DebugPrimitive prim in frame)
{
    PickToken token = prim.GetPickToken();
    if (token.IsValid)
    {
        // token.Target  = ECS Entity that owns this primitive
        // token.SubElementId = which handle was hit (for multi-handle gizmos)
        // token.GizmoTypeId  = FNV-1a hash of the gizmo type name for routing
        DispatchInteraction(token);
    }
}
```

### Example 5: Network entity shape emission (SpatialAnchor + SemanticShape)

```csharp
// Called by the entity presentation gizmo system once per entity per frame.
// networkId must be globally unique and stable across process restarts.
public void DrawEntityPresentationShape(
    IDebugDrawBuilder draw,
    long   networkId,
    float  worldX, float worldY, float worldZ,
    float  headingDeg,
    ulong  profileId,
    float  lengthM, float widthM,
    uint   conditionMask)
{
    // SpatialAnchor MUST be emitted first.
    draw.DrawSpatialAnchor(networkId, worldX, worldY, worldZ, headingDeg);

    // SemanticShape references the anchor by AnchorIndex == (int)networkId.
    draw.DrawSemanticShape(networkId, profileId, lengthM, widthM, conditionMask);
}
```

---

## Best Practices

**1. Always call `EndFrame(deltaTime)` at the frame boundary, not `Clear()`.**
`Clear()` discards the transient counter only. `EndFrame(dt)` additionally ages the persistence
pool and re-injects survivors so that persistent primitives remain visible across frames.

**2. Size the buffer generously; overflow is silent.**
When `_count` exceeds `capacity`, primitives are dropped without any exception or log entry.
`DroppedCount` is the only signal. Monitor it during testing and increase capacity if non-zero.

**3. Always emit `DrawSpatialAnchor` before `DrawSemanticShape` for the same `networkId`.**
The two-pass renderer in the IG terminal requires the anchor to be present in the same frame as
the shape. Emitting them out of order means the shape will not resolve on the first frame.

**4. Use `DrawText` for short labels; use `DrawTextLong` only when needed.**
`DrawText` is completely allocation-free. `DrawTextLong` allocates one `ConcurrentDictionary`
entry per unique string (cold path). Prefer `DrawText` with a `FixedString32` for labels that
fit in 31 characters.

**5. Do not cast or compare `DebugPrimitive` values across assembly reloads.**
The struct is blittable and correct only when both ends use the same version of `GizmoMap.Contracts`.
Version mismatches manifest as silent corruption, not exceptions.

**6. Use `StampGizmoTypeId` from the same thread that calls `Draw*`.**
`StampGizmoTypeId` reads `Count` and stamps a range of entries. It is not safe to call it
concurrently with other writers, as the range could be overwritten before stamping completes.

**7. Avoid holding onto the `GetFrame()` span across `EndFrame()` or `Clear()` calls.**
`GetFrame()` returns a `ReadOnlySpan<DebugPrimitive>` backed by the internal array. Calling
`EndFrame()` or `Clear()` modifies `_count` and re-injects entries, making the prior span stale.
Consume and release the span within the same frame scope.

**8. Treat default-interface-method no-ops as opt-in.**
Several `IDebugDrawBuilder` methods (e.g. `DrawSpatialAnchor`, `DrawSemanticShape`,
`DrawContextMenuBinding`, `EmitRaw`) have empty default implementations so that stub
implementations compile without changes. If you write a stub `IDebugDrawBuilder`, verify
which methods your callers actually need and override only those.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fdp.Core` | **Direct dependency.** Provides `Entity`, `FixedString32`. |
| `GizmoMap.Contracts` | **Direct dependency (aliased).** Owns `DebugPrimitive` and all primitive enums. |
| `GizmoMap` | Consumes `DebugPrimitiveBuffer.GetFrame()` and serializes the stream over DDS. Not referenced by this project. |
| `Fdp.Diagnostics.Network` | Sibling project in the Diagnostics subsystem. Handles DDS transport of the gizmo stream. References this project. |
| `Fdp.Presentation` | Uses `DebugPrimitiveBuffer` to render primitives locally in the imgui/raylib viewport. |
| `Fdp.Toolkits` | Gizmo systems call `IDebugDrawBuilder` methods. References this project. |
| `Hrot.IG` | Remote IG terminal. Receives the serialized stream and renders `DebugPrimitive` values. Uses `EcsDebugPrimitiveExtensions` for entity hit-testing. |
| `Fdp.Diagnostics.Contracts.Tests` | Sibling test project. Contains `ContractsStandaloneTests` covering buffer behavior, struct layout invariants, and shape field round-trips. |
