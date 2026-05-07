# FDP Declarative Gizmo & Presentation Framework -- Design

## Summary

This document describes the **target architecture** of the FDP declarative gizmo and
presentation framework. It reflects the end-state design: GizmoMap is already extracted to
`ExtDeps/GizmoMap` (see Phase 19 in TASK-DETAIL.md). The FDP ECS adapter layer delegates to
`GizmoMap.*`; all new feature work targets `GizmoMap.*` assemblies directly.

The fundamental design principle is **Evaluate Once, Present Anywhere**: gizmo logic runs once on
the authoritative simulation node, emits a stream of backend-neutral draw commands, and those
commands are routed to any number of local or remote presentation clients. The simulation node
knows nothing about rendering; the presentation client knows nothing about the ECS.

**GizmoMap isolation rule:** The three `GizmoMap.*` assemblies (Contracts, Network, Presentation)
must NEVER reference `Entity`, `ISimulationView`, `BitMask256`, `IStatefulGizmo`,
`IGizmoDefinition`, `GizmoRegistry`, `StatelessGizmoRegistry`, or any FDP/ECS type. All
ECS-dependent code stays in `Fdp.Toolkits` or `Hrot.*` and delegates to `GizmoMap.*`.

---

## §1 Core Primitive Protocol and Memory Layout

### 1.1 Type Definitions

**`Rgba32`** -- a 4-byte RGBA struct defined in `GizmoMap.Contracts`. Avoids a dependency on
`Hrot.IG.Components.Color32` from the toolkit layer. Presentation adapters convert trivially.

**`PipelineTarget`** (flags byte): `Map2D = 1`, `Viewport3D = 2`, `NodeGraph = 4`, `All = 7`.
Controls which rendering pipelines consume a given primitive. `All` is the bitwise OR of all
three targets: `1 | 2 | 4 = 7`.

**`CoordinateSpace`** (byte):
- `World` -- absolute simulation meters; primitive pans with the camera.
- `Screen` -- absolute screen pixels; primitive is "glued to glass", bypassing camera projection.
- `EntityLocal` -- relative to a `SpatialAnchor` primitive with the same `NetworkId`; the
  two-pass renderer resolves the anchor's world position and orientation before drawing.

**`SizeMode`** (byte):
- `WorldMeters` -- thickness/radius scales with camera zoom naturally.
- `ScreenPixels` -- thickness divided by zoom to maintain constant screen presence.

**`DebugPrimitiveShape`** (byte):
- `Line = 0`, `Sphere = 1`, `Box2D = 2`, `Arrow = 3`, `Text = 4`, `EntityBadge = 5`,
  `Icon = 6`, `ComponentInspector = 7`
- `SemanticShape = 8` -- entity semantic shape/profile primitive. Carries a DIS-style profile key,
  dimensional overrides, and a pre-evaluated condition bitmask so the renderer can draw a
  perspective-exaggerated tactical silhouette without ECS access. Always preceded in the stream
  by a `SpatialAnchor` with the same `NetworkId`.
- `MilStd2525 = 9` -- NATO MIL-STD-2525 symbology frame.
- `SpatialAnchor = 10` -- pre-resolved world position + full 3D orientation (heading, pitch, roll
  in degrees). Severs the renderer's dependency on `SimTransform` for decoupled map viewers.

### 1.2 DebugPrimitive Tagged Union

A single `[StructLayout(LayoutKind.Explicit, Size=64)]` struct representing one backend-neutral
draw command. The 64-byte limit (one CPU cache line) is **inviolable**: primitives requiring
larger payloads use an out-of-band side-channel (see SS6).

**Header fields (bytes 0-23, explicit offsets):**

| Field | Type | Bytes | Description |
|-------|------|-------|-------------|
| `Shape` | `DebugPrimitiveShape` | 1 | Discriminator |
| `Space` | `CoordinateSpace` | 1 | Origin anchor |
| `Color` | `Rgba32` | 4 | Primary / start color |
| `TargetView` | `PipelineTarget` | 1 | Pipeline filter mask |
| `DebugLayer` | byte | 1 | Layer 0-15; macro Z-order bucket |
| `AnchorIndex` + `AnchorGeneration` | int + ushort | 6 | Entity anchor (split for explicit layout); valid when `Space == EntityLocal`. When `Space != EntityLocal` and `Shape` is `Text` or `EntityBadge`, bytes 8-11 are reused as `uint StringHash` (see SS1.3) |
| `SizeMode` | `SizeMode` | 1 | WorldMeters vs ScreenPixels |
| `ZIndex` | byte | 1 | Intra-layer fine-grained sort key; 0 = background |
| `ThicknessU16` | ushort | 2 | Thickness in 0.1-unit steps; `float Thickness => ThicknessU16 * 0.1f` (max 6553.5) |
| `MinZoomLod` | byte | 1 | 0 = no min limit; n x 0.25 = minimum zoom; hidden below this |
| `MaxZoomLod` | byte | 1 | 0 = no max limit; n x 0.25 = maximum zoom; hidden above this |
| `LifetimeSeconds` | float | 4 | 0 = one frame; >0 = persists N seconds |

**Payload union (bytes 24-63, 40 bytes):**

| Shape | Payload fields |
|-------|---------------|
| `Line` | `Vector3 LineStart`, `Vector3 LineEnd`, `Rgba32 EndColor` (gradient; equal colors = fast path) |
| `Sphere` | `Vector3 Center`, `float Radius` |
| `Box2D` | `Vector2 Center`, `Vector2 Extents`, `float AngleDeg` |
| `Arrow` | `Vector3 From`, `Vector3 To`, `float HeadSize` |
| `Text` | `Vector2 Position`, `FixedString32 Content` (<=31 chars inline; `StringHash != 0` resolves from `StringInternMap`) |
| `EntityBadge` | `Entity Target`, `FixedString32 RichText` (same `StringHash` escape hatch) |
| `Icon` | `Vector3 WorldPos`, `FixedString32 AtlasCoord` (e.g. `"b12"`) |
| `ComponentInspector` | `long NetworkId` (stable network ID, not ECS index), `uint SchemaHash` (FNV-1a of type full name), `ScreenAnchor Anchor`, `Vector2 Offset`, `bool IsReadOnly` |
| `SemanticShape` | `ulong ProfileId` (8 bytes: DIS enumeration / shape profile registry key), `float LengthMeters` (4 bytes), `float WidthMeters` (4 bytes), `uint ConditionMask` (4 bytes: pre-evaluated `EntityShapeCondition` bitfield, e.g. `Damaged`, `Firing`); bytes 44-63 unused. MUST be preceded by a `SpatialAnchor` with the same `NetworkId`. |
| `MilStd2525` | `float WorldPosX/Y`, `FixedString32 SidcCode` |
| `SpatialAnchor` | `long NetworkId` (8 bytes: negative = synthetic/ephemeral anchor for free-floating shapes), `float AnchorWorldX` (4), `float AnchorWorldY` (4), `float AnchorWorldZ` (4), `float Heading` (4: degrees, same convention as `SimMath.ToYawPitchRollDeg`), `float Pitch` (4), `float Roll` (4); bytes 56-63 unused. |

**Payload budget rationale:** A full 3D transform (Vector3=12 + Quaternion=16 = 28 bytes) plus
SemanticShape data (ProfileId=8 + Dims=8 + ConditionMask=4 = 20 bytes) totals 48 bytes, which
overflows the 40-byte payload union. The solution is: `SemanticShape` carries only profile and
condition data (20 bytes used); `SpatialAnchor` carries position and orientation (32 bytes used).
The two-pass renderer stitches them together by `NetworkId` (see SS2).

### 1.3 String Interning

`FixedString32` allows at most 31 usable ASCII characters plus a null terminator. For longer
diagnostic strings, when `Space != EntityLocal`, bytes 8-11 (normally `AnchorIndex`) are reused
as a `uint StringHash` (FNV-1a) key into a `StringInternMap` side-channel. The `FixedString32`
payload contains the first 31 characters as a local-client preview fallback; the full string is
published via the `StringInternTopic` DDS topic or populated in the local `StringInternMap`
before the render pass.

`DrawTextLong(string text, ...)` computes the hash, registers the full string, and fills the
first 31 characters. `DrawText(FixedString32 text, ...)` always uses inline mode (hash = 0).

### 1.4 IDebugDrawBuilder and DebugPrimitiveBuffer

`IDebugDrawBuilder` is the write-side API that all gizmo code uses. It hides the buffer internals:

```csharp
void DrawLine(Vector3 start, Vector3 end, Rgba32 color, float thickness = 1f,
              SizeMode sizeMode = ScreenPixels, PipelineTarget target = All, byte layer = 0);
void DrawLineGradient(Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
                      float thickness = 1f, PipelineTarget target = All, byte layer = 0);
void DrawSphere(Vector3 center, float radius, Rgba32 color, PipelineTarget target = All,
                byte layer = 0);
void DrawArrow(Vector3 from, Vector3 to, Rgba32 color, float headSize = 1f, byte layer = 0);
void DrawText(Vector2 position, FixedString32 text, Rgba32 color,
              CoordinateSpace space = World, byte layer = 0);
void DrawTextLong(Vector2 position, string text, Rgba32 color,
                  CoordinateSpace space = World, byte layer = 0);
void DrawEntityBadge(Entity target, FixedString32 richText, PipelineTarget target = All);
void DrawIcon(Vector3 worldPos, FixedString32 atlasCoord, PipelineTarget target = All);
void DrawComponentInspector<T>(Entity target, ScreenAnchor anchor, Vector2 offset,
                                bool isReadOnly = false) where T : unmanaged;
```

`DebugPrimitiveBuffer` is a thread-safe growable array of `DebugPrimitive` that implements
`IDebugDrawBuilder`. It is allocated once at startup (no per-frame heap allocation). At frame end
the orchestrator reads all accumulated primitives, routes them to the transport, and clears the
buffer for the next frame.

---

## §2 Two-Pass Renderer (Dumb Terminal)

The presentation client is a **dumb terminal**: it never reads ECS state. Instead it relies on
two routing primitives -- `SpatialAnchor` and `SemanticShape` -- to reconstruct world positions
and entity states from the primitive stream alone.

### 2.1 Pass 1: Anchor Cache

Before any draw calls, the renderer makes a single pass over the incoming
`ReadOnlySpan<DebugPrimitive>`:

```
foreach primitive in span:
    if primitive.Shape == SpatialAnchor:
        anchorCache[primitive.Payload.NetworkId] = Matrix4x4.CreateFromYawPitchRollDeg(
            primitive.Payload.Heading,
            primitive.Payload.Pitch,
            primitive.Payload.Roll,
            new Vector3(primitive.Payload.AnchorWorldX,
                        primitive.Payload.AnchorWorldY,
                        primitive.Payload.AnchorWorldZ))
```

`NetworkId` is the map key. Negative `NetworkId` values denote synthetic/ephemeral anchors (e.g.
drag-preview spawn points) that have no backing ECS entity; they are cached identically.

### 2.2 Pass 2: Shape Rendering

The second pass renders all non-anchor primitives using the cached matrices:

- **`SemanticShape`**: look up `anchorCache[primitive.Payload.NetworkId]`. If not found, skip
  silently (anchor not yet received). Apply the cached matrix to transform the profile polylines
  from `EntityLocal` to world space. Before rendering each profile layer, evaluate:
  `if ((layer.ShowWhen & primitive.Payload.ConditionMask) == layer.ShowWhen) render;`
  `if ((layer.HideWhen & primitive.Payload.ConditionMask) != 0) skip;`
  `ConditionMask` is a pre-evaluated `EntityShapeCondition` bitfield (e.g. `Damaged`, `Firing`)
  condensed by the ECS adapter on the server. The dumb terminal never reads ECS state.
- **`MilStd2525`**: resolved directly from `WorldPosX/Y` + `SidcCode` without anchor lookup
  (these are absolute world positions self-contained in the payload).
- **`EntityLocal` primitives** other than `SemanticShape`: look up the cached anchor matrix
  derived from `AnchorIndex` + `AnchorGeneration`, apply matrix.

### 2.3 Spatial Projection and Thickness Scaling

**CoordinateSpace resolution:**
- `World` -- leave camera matrix active; primitive pans with map.
- `Screen` -- pop camera matrix before draw call; primitive is "glued to glass".
- `EntityLocal` -- apply the cached `SpatialAnchor` matrix (see SS2.2).

**SizeMode scaling:**
- `ScreenPixels`: `finalThickness = (prim.ThicknessU16 * 0.1f) / safeZoom`
- `WorldMeters`: `finalThickness = prim.ThicknessU16 * 0.1f`

**Layer and zoom culling (applied before shape dispatch):**
1. Pipeline check: `if ((prim.TargetView & PipelineTarget.Map2D) == 0) skip;`
2. Layer mask: `if ((activeLayerMask & (1u << prim.DebugLayer)) == 0) skip;`
3. Zoom LOD: `if (MinZoomLod != 0 && zoom < MinZoomLod * 0.25f) skip;`
   `if (MaxZoomLod != 0 && zoom > MaxZoomLod * 0.25f) skip;`

**Painter's Algorithm sort:** Before draw calls the renderer stable-sorts by
`(prim.DebugLayer << 8) | prim.ZIndex` to ensure tooltip backgrounds render before text labels
regardless of ECS chunk iteration order.

---

## §3 Library Segregation and Assembly Boundaries

The framework lives in `ExtDeps/GizmoMap` as three assemblies with strict layering. The FDP ECS
adapter layer in `Fdp.Toolkits` and `Hrot.*` delegates to these assemblies.

### 3.1 GizmoMap.Contracts (BCL only)

Zero external dependencies. Only BCL types and `netstandard2.1`.

**Contains:** `DebugPrimitive`, all enums (`PipelineTarget`, `CoordinateSpace`, `SizeMode`,
`DebugPrimitiveShape`), `Rgba32`, `IDebugDrawBuilder`, `DebugPrimitiveBuffer`, `StringInternMap`,
`GizmoPickToken` (stable-ID pick token with no ECS handle), `IGizmoSource` (generic ECS-free
source interface), `GizmoSettingValue`, `GizmoSettingsRegistry`, `SettingScope`,
interaction event DTOs (`GizmoInteractionStartedEvent`, `GizmoDragUpdateEvent`,
`GizmoInteractionCommitEvent`, `GizmoInteractionCancelEvent`), `IGizmoTransport`.

**NOT included:** `Entity`, `ISimulationView`, `BitMask256`, `IStatefulGizmo`, `IGizmoDefinition`,
`GizmoRegistry`, `StatelessGizmoRegistry`, or any FDP/ECS type. `ConditionMask` in
`SemanticShape` is a `uint` -- safe for this assembly.

### 3.2 GizmoMap.Network

References `GizmoMap.Contracts` and CycloneDDS bindings only.

**Contains:** DDS topic structs (`DebugPrimitivesBatch`, `GizmoInteractionBatch`, `GizmoUiState`,
`StringInternBatch`, `EntityAttributeSchema`) and stateless transport adapters
(`DdsDebugPrimitivePublisher/Subscriber`, `DdsGizmoInteractionPublisher/Subscriber`).

**NOT included:** any `IEcsModuleSystem` implementation. FDP-specific ECS wrappers
(`GizmoInteractionEgressSystem`, `GizmoInteractionIngressSystem`,
`EntityAttributeSchemaPublisherSystem`) remain in FDP/HROT and delegate to the transport adapters.

### 3.3 GizmoMap.Presentation

References `GizmoMap.Contracts`, `GizmoMap.Network`, Raylib, and ImGui.

**Contains:** `DebugPrimitiveRenderer2D` (with `SemanticShape`/`MilStd2525` two-pass render
paths), `GizmoInteractionProxyTool`, `RichTextRenderer`, `DebugGizmoLayer`, `GizmoUndoStack`,
`IconAtlasAdapter`, `MilStd2525Renderer`, `SemanticShapeRenderer`.

**NOT included:** `DataDrivenGizmoSystem`, `StatelessGizmoSystem`, `GizmoSettingsPublisherSystem`
(ECS-dependent producer systems stay in `Fdp.Toolkits`).

### 3.4 FDP ECS Adapter Layer

The ECS-aware orchestration stays outside GizmoMap:

| Project | Contents |
|---------|----------|
| `Fdp.Toolkits` | `IStatefulGizmo`, `IGizmoDefinition`, `GizmoRegistry`, `DataDrivenGizmoSystem`, `BehaviorGizmoManagerSystem`, `GizmoSettingsPublisherSystem`, interaction events (FDP event bus wiring) |
| `Fdp.Presentation` | `DebugGizmoLayer` integration point; delegates to `GizmoMap.Presentation` |
| `Hrot.IG` | Concrete gizmo implementations, `SelectedEntityPolicy`, `GlobalDebugSettings` ECS singleton |
| `Hrot.Network.NED` | ECS wrappers: `GizmoInteractionEgressSystem`, `GizmoInteractionIngressSystem`, `DebugPrimitivesIngressTranslator`, `EntityAttributeSchemaPublisherSystem`; all delegate to `GizmoMap.Network` |

### 3.5 Transport Abstraction

The composition root selects the transport at startup:

```csharp
public interface IGizmoTransport : IDisposable
{
    void PublishPrimitives(ReadOnlySpan<DebugPrimitive> primitives);
    void PollAndApply(DebugPrimitiveBuffer target);
}
```

`LocalGizmoTransport` (in-process direct copy) is used for local mode and unit tests.
`DdsGizmoTransport` (CycloneDDS publish/subscribe) is used in distributed deployments.
The main rendering loop is transport-agnostic and depends only on `IGizmoTransport`.

### 3.6 Backward Compatibility

`Fdp.Diagnostics.Contracts`, `Fdp.Diagnostics.Network`, and `Fdp.Presentation` become thin
facades that re-export types from their `GizmoMap.*` counterparts via `global using` type aliases.
All existing consumer code continues to compile unchanged. The facade assemblies can be deprecated
and removed in a subsequent cleanup sprint.

---

## §4 Gizmo Orchestration

### 4.1 Gizmo Taxonomy

Gizmos are described along three orthogonal axes:

**Statefulness:**
- *Stateless* -- no instantiation; pure projectors that read ECS state each frame and emit
  primitives. Driven by a direct ECS query. No lifecycle management needed.
- *Stateful (ephemeral)* -- instantiated C# objects rented from an object pool. Hold transient
  presentation state across frames (trails, timers, cached projection data). Lifecycle is strictly
  event-driven (not poll-driven).

**Attachment scope:**
- *Entity-bound* -- activated for entities that match a component mask or blueprint ID.
- *Behavior-bound* -- activated for entities currently executing a specific named behavior.
- *Global* -- entity-independent (navmesh overlay, spatial grids, event bus trace).

**Visibility:** Controlled by an `IGizmoVisibilityPolicy`.

### 4.2 Core Contracts

```csharp
public interface IStatefulGizmo
{
    void OnInitialize(ISimulationView view, Entity entity);
    void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime,
                       IDebugDrawBuilder drawBuilder);
    void OnTeardown();
}

public interface IGizmoDefinition
{
    Type[] RequiredComponents { get; }
    IGizmoVisibilityPolicy VisibilityPolicy { get; }
    IStatefulGizmo CreateInstance();
}

public interface IGizmoVisibilityPolicy
{
    bool IsGloballyEnabled(ISimulationView view);
    bool IsEntityVisible(ISimulationView view, Entity entity);
}
```

### 4.3 DataDrivenGizmoSystem

A single `[UpdateInPhase(SystemPhase.PostSimulation)]` system manages all entity-bound gizmo
lifecycles. It never changes to accommodate new gizmo types (Open-Closed Principle).

1. **Teardown** (per frame): drain `DestructionOrder` events; call `OnTeardown()` on each instance.
2. **Setup** (per frame): drain `ConstructionOrder` events; evaluate all `CompiledGizmoRule`
   entries using `BitMask256.HasAll(entityHeader.ComponentMask, rule.RequiredMask)`; for matches
   rent an instance, call `OnInitialize`, store in dictionary keyed by `Entity`.
3. **Execute** (per frame): two modes:
   - *Global force*: iterate the active dictionary; call `UpdateAndDraw` for all gizmos.
   - *Selection only* (default): SIMD-accelerated ECS query for entities with `SelectionState`;
     for each selected entity do O(1) dictionary lookup. Global singleton check is hoisted outside
     the loop.

**Global visibility singleton:** `GlobalDebugSettings` ECS singleton exposes
`bool ForceAllGizmosVisible`. The system checks this once per frame and switches execution modes.

### 4.4 BehaviorGizmoManagerSystem

A companion system in the same phase manages behavior-bound gizmos:

- Drains `view.ReadManagedEvents<AssignBehaviorEvent>()` (managed class, not unmanaged struct).
- Drains `view.ReadEvents<ClearBehaviorEvent>()` and `view.ReadEvents<DestructionOrder>()` for
  teardown.
- Executes active instances via `UpdateAndDraw` with the same two-mode visibility pattern.

`AssignBehaviorEvent` is a managed class so `ReadManagedEvents<T>` is used. `ClearBehaviorEvent`
and `DestructionOrder` are unmanaged structs so `ReadEvents<T>` is used.

---

## §5 Interaction Pipeline

### 5.1 PickToken

A 12-byte blittable struct embedded in interactive `DebugPrimitive`s:

```csharp
public struct GizmoPickToken
{
    public Entity Target;     // ECS entity (includes generational safety)
    public uint SubElementId; // 0 = whole entity; >0 = sub-element index
    public bool IsValid => !Target.IsNull;
}
```

Non-interactive primitives leave `Target = Entity.Null`.

### 5.2 Backend-Neutral Interaction Events

All interaction between the presentation layer and the simulation kernel is mediated by
serializable events (usable both locally via `FdpEventBus` and remotely via DDS):

```csharp
[EventId(...)] public struct GizmoInteractionStartedEvent  { public GizmoPickToken Token; public Vector3 WorldPos; }
[EventId(...)] public struct GizmoDragUpdateEvent          { public GizmoPickToken Token; public Vector3 WorldPos; public CoordinateSpace Space; }
[EventId(...)] public struct GizmoInteractionCommitEvent   { public GizmoPickToken Token; public Vector3 WorldPos; public CoordinateSpace Space; }
[EventId(...)] public struct GizmoInteractionCancelEvent   { public GizmoPickToken Token; }
```

`GizmoDragUpdateEvent` and `GizmoInteractionCommitEvent` carry the `CoordinateSpace` of the
picked primitive. For `Space == World`, `WorldPos` is an absolute simulation-space position. For
`Space == Screen`, `WorldPos.XY` is a screen-pixel delta relative to the gizmo anchor position
(the backend MUST NOT interpret it as absolute world coordinates). The tool populates `Space` from
the `CoordinateSpace` field of the hit primitive; this context is also transported through
`GizmoInteractionBatch` over DDS.

### 5.3 GizmoInteractionProxyTool

A `sealed class : IMapTool` in `GizmoMap.Presentation`. It does NOT contain gizmo logic; it is a
dumb terminal proxy:

1. **Activation**: pushed onto the `MapCanvas` stack when a pickable `DebugPrimitive` is clicked;
   publishes `GizmoInteractionStartedEvent`.
2. **Focus capture**: while active, `HandleDrag` streams `GizmoDragUpdateEvent` each frame.
3. **Self-deactivation** (3 paths):
   - Mouse release on commit -- publishes `GizmoInteractionCommitEvent`, calls `_canvas.PopTool()`.
   - `Escape` or right-click -- publishes `GizmoInteractionCancelEvent`, calls `PopTool()`.
   - Click-away in sticky mode -- publishes `GizmoInteractionCancelEvent`, calls `PopTool()`, and
     returns `false` from `HandleClick` so the underlying `StandardInteractionTool` processes the
     click in the same frame (seamless entity selection handoff).

Simulation-side systems receive these events via `view.ReadEvents<T>()` in `PostSimulation` phase
and apply deferred mutations through `view.GetCommandBuffer()`. Gizmos must never directly mutate
ECS component memory.

---

## §6 DDS Topics and Remote Transport

### 6.1 DebugPrimitivesBatch

`[DdsTopic("DebugPrimitivesBatch")]` with `DdsReliability.BestEffort` (high frequency). The
64-byte fixed-size `DebugPrimitive` struct is directly blittable -- no serialization overhead.
Carries a `FrameNumber` key and a `DebugPrimitive[]` payload. Persistent primitives
(`LifetimeSeconds > 0`) are re-emitted each frame until they expire.

### 6.2 GizmoSettingsPublisherSystem

Watches for `GizmoSettingChangedEvent` (or fires once at boot) and rebuilds a `StructEdit`
`EditDocument` from `GizmoSettingsRegistry`. Serializes to JSON via `EditDocumentJsonSerializer`
and publishes on a `TransientLocal` DDS topic. Remote clients subscribe and render an
`ImGuiPropertyTree` from the JSON schema without needing to reference any C# gizmo assemblies.

### 6.3 GizmoUiState DDS Topic

For gizmo-specific interactive DTOs (not ECS components), a `[DdsTopic("GizmoUiState")]` with
`TransientLocal` durability carries `uint GizmoInstanceId` and `string EditDocumentJson`. The
`ComponentInspector` primitive references the `GizmoInstanceId`; remote clients match the ID and
render the property grid.

### 6.4 Terminal Capability Announcement

Remote clients (IG nodes) publish an `IGCapabilitiesAnnounce` DDS message on startup declaring
supported pipeline targets, layer structure, and supported primitive shapes. The simulation node
can use this to tailor its output (e.g. skip `Viewport3D` primitives when the client has no 3D
viewport).

IDL purity rule: each field has exactly one semantic purpose -- do NOT overload a field with
unrelated data.

| Field | Type | Semantic |
|-------|------|----------|
| `MapId` | `int` (key) | Identifies the IG instance |
| `SupportedShapeMask` | `uint` | Bitmask of `DebugPrimitiveShape` values this renderer can display; derived via reflection at startup. `uint` is required -- shapes 8/9/10 do not fit in a `byte` bitmask (`1 << 8 = 256`). |
| `SupportedLayerMask` | `ushort` | Bitmask of layer indices (0-15) this renderer accepts; defaults to `0xFFFF` (all layers). |
| `SupportedTargets` | `PipelineTarget` | Pipeline targets this IG instance has active (e.g. `Map2D`, `Map2D|Viewport3D`). |
| `LayerTreeJson` | `string` | JSON tree describing the layer folder hierarchy for the ExCon Layers panel. |
| `ConfigurationSchemasJson` | `string` | JSON schemas for valid IG configuration options. |
| `OverlayStyleSchemaJson` | `string` | JSON schema for overlay style overrides. |
| `TkbManifestJson` | `string` | JSON manifest of TKB types with special IG visuals. |
| `RegisteredGizmosJson` | `string` | JSON array of local presentation-plugin gizmo names (empty `"[]"` when IG is a dumb terminal). MUST NOT be merged into `LayerTreeJson`. Backend gizmo definitions are published separately via `EntityAttributeSchema`. |

This is a best-effort enhancement: simulation nodes can ignore capabilities and emit `All`
target primitives; clients silently drop unsupported shapes.

### 6.5 Entity Attribute Schema Broadcast

The ExCon UI must not rely on a hardcoded DTO to know which fields `JsonAttributeCompiler`
supports. The SimHost node publishes a `TransientLocal` DDS topic `EntityAttributeSchema` on
startup carrying the full JSON schema of supported attribute paths, their types, and validation
constraints. The ExCon subscribes and builds its attribute-editing UI from the received schema.

The `isDefaultProcessor` gate prevents a broadcast storm in multi-node SimHost clusters: only
the node elected as the default processor publishes; all others stay silent.

```
EntityAttributeSchema DDS topic (TransientLocal, HistoryDepth=1)
  int     NodeId     [key]
  string  SchemaJson  [managed; JSON Schema compatible with StructEdit EditDocument format]
```

Late-joining ExCon clients always receive the current schema because `TransientLocal` with
`HistoryDepth = 1` caches the last published sample.

---

## §7 Settings and Persistence

### 7.1 GizmoSettingValue Tagged Union

A blittable 8-byte struct with a `SettingType` discriminator and a 4-byte payload union holding
`bool BoolValue`, `int IntValue`, or `float FloatValue`.

### 7.2 GizmoSettingsRegistry

A `sealed class` injected as a managed singleton into `EntityRepository`. Internally:

- `Dictionary<uint, GizmoSettingValue> _active` -- hot-path store keyed by FNV-1a 32-bit hash.
- `Dictionary<uint, GizmoSettingValue> _defaults` -- default values for "reset to default".
- `Dictionary<uint, string> _hashToName` -- reverse map for persistence (cold path).

**API:**
- `void RegisterSetting(string key, GizmoSettingValue defaultValue)` -- idempotent.
- `GizmoSettingValue Read(uint keyHash)` -- O(1), hot path.
- `void Write(uint keyHash, GizmoSettingValue value)` -- publishes `GizmoSettingChangedEvent`.
- `void ResetToDefault(uint keyHash)` -- restores default.
- `void SaveToDisk(string path)` -- serializes only values that differ from defaults.
- `void LoadFromDisk(string path)` -- applies overrides on top of registered defaults.
- `static uint ComputeHash(string name)` -- FNV-1a: `hash=2166136261; foreach(c){hash^=c; hash*=16777619;}`.

**Hoisting rule:** Systems must compute hashes at construction time (`static readonly uint` fields)
and call `Read(hash)` once before the entity loop, never inside the loop.

### 7.3 SettingScope

```csharp
public enum SettingScope { Global, Project, Session }
```

- `Global` -- survives application restarts; written to the user profile.
- `Project` -- tied to the currently loaded scenario file.
- `Session` -- ephemeral; resets on restart.

`RegisterSetting(string key, GizmoSettingValue defaultValue, SettingScope scope)` stores the
scope metadata. `SaveToDisk` and `LoadFromDisk` filter by scope to apply the correct persistence
strategy.

### 7.4 Settings Change Notification

`GizmoSettingChangedEvent` is an unmanaged struct with a `uint KeyHash` field. Published when
`Write` or `ResetToDefault` is called.

Gizmos with expensive cached state (mesh bakes, layout trees) drain
`view.ReadEvents<GizmoSettingChangedEvent>()` at the top of `UpdateAndDraw` and invalidate their
cache when a relevant key changes.

**Known 1-frame tear (acknowledged, acceptable):** `Write()` immediately updates the active
dictionary so `Read()` returns the new value in frame N. The `FdpEventBus` is strictly
double-buffered: `GizmoSettingChangedEvent` is not readable until frame N+1. Stateless projectors
using hoisted polling see the new value in frame N; stateful gizmos using the event drain see it
in frame N+1. For diagnostic tooling this latency is acceptable.

---

## §8 Architectural Decisions

| Decision | Rationale |
|----------|-----------|
| Own `Rgba32` color type (not `Hrot.IG.Components.Color32`) | `Fdp.Toolkits` cannot reference `Hrot.Core`; presentation adapters trivially convert |
| `DebugPrimitive` fixed to 64 bytes | One cache line; safe for `NativeChunkTable` and DDS transport |
| No per-gizmo system; single `DataDrivenGizmoSystem` | Open-Closed Principle; adding a new gizmo type never touches the system |
| No callbacks/events for the 95% case (color, thickness) | Hoisted polling is faster, simpler, deterministic |
| `BitMask256.HasAll` for component matching | AVX2 path; O(1) per entity regardless of gizmo count |
| `GizmoInteractionProxyTool` lives in `GizmoMap.Presentation` | Depends on `IMapTool`, `MapCanvas`, `Raylib_cs` |
| Gizmo visibility policies live in `Hrot.IG` | They reference `SelectionState` from `Hrot.IG.Components` |
| No direct `ImGui` or `Raylib` calls in gizmo `UpdateAndDraw` | Keeps simulation node headless and remote-visualizer-compatible |
| `AssignBehaviorEvent` read via `ReadManagedEvents<T>` | It is a managed class, not an unmanaged struct |
| Settings side-channel uses StructEdit JSON, not shared assemblies | Remote clients have no knowledge of concrete gizmo DTOs |
| `ComponentInspector` uses `NetworkId` + `SchemaHash`, not ECS indices | ECS slot indices are process-local; DDS transport requires globally stable IDs |
| `SemanticShape` is `SpatialAnchor`-dependent (no position fields) | Packing full 3D transform (28 bytes) + profile data (20 bytes) = 48 bytes overflows the 40-byte payload union; split across two primitives stitched by `NetworkId` |
| `SpatialAnchor.NetworkId` is first (negative = synthetic anchor) | Free-floating shapes (no backing ECS entity) use negative `NetworkId` synthetic anchors; `NetworkId` first enables fast anchor-cache lookup without reading further fields |
| `SpatialAnchor` carries full H/P/R in degrees, not quaternion | A quaternion requires 16 bytes; H/P/R requires 12 bytes; degrees match `SimMath.ToYawPitchRollDeg` entity inspector convention |
| `SemanticShape.ConditionMask` is `uint`, not `BitMask256` | `BitMask256` is an FDP ECS type; `uint` is safe in `GizmoMap.Contracts` and is sufficient for `EntityShapeCondition` (currently fewer than 32 conditions) |
| `GizmoDragUpdateEvent` carries `CoordinateSpace` | Screen-space gizmo handles send pixel deltas, not world positions; the backend must know which interpretation to apply |
| `IGCapabilitiesAnnounce.SupportedShapeMask` is `uint` | Shapes 8/9/10 require `1 << 8 = 256`; a `byte` bitmask cannot represent them |
| Distinct `RegisteredGizmosJson` field in `IGCapabilitiesAnnounce` | IDL purity: one field per concept; `LayerTreeJson` must NOT be overloaded with gizmo schema data |
| `isDefaultProcessor` gate on `EntityAttributeSchemaPublisherSystem` | Only one node in a multi-node cluster publishes the attribute schema; prevents broadcast storm |

---

## §9 Project Layout

### Current: FDP-embedded (Phases 1-17)

| Project | Namespace | Contents |
|---------|-----------|----------|
| `Fdp.Diagnostics.Contracts` | `Fdp.Toolkit.Diagnostics.Gizmos` | `Rgba32`, `DebugPrimitive`, all enums, `IDebugDrawBuilder`, `DebugPrimitiveBuffer`, `StringInternMap` |
| `Fdp.Diagnostics.Network` | `Fdp.Toolkit.Diagnostics.Network` | DDS topic structs: `DebugPrimitivesBatch`, `GizmoInteractionBatch`, `GizmoUiState`, `StringInternBatch` |
| `Fdp.Toolkits` | `Fdp.Toolkit.Diagnostics.Gizmos` | `IStatefulGizmo`, `IGizmoDefinition`, `IStatelessGizmo`, `GizmoRegistry`, `DataDrivenGizmoSystem`, `BehaviorGizmoManagerSystem`, settings, interaction events; ECS/FDP-specific adapter layer -- NOT migrated to `GizmoMap.Contracts` |
| `Fdp.Presentation` | `Fdp.Toolkit.Vis2D.Gizmos` | `DebugPrimitiveRenderer2D`, `GizmoInteractionProxyTool`, `RichTextRenderer` |
| `Hrot.IG` | `Hrot.IG.Gizmos` | Concrete gizmo implementations, `SelectedEntityPolicy`, `GlobalDebugSettings` ECS singleton |
| `Hrot.Network.NED` | `Hrot.Network.NED.Gizmos` | ECS wrappers: `GizmoInteractionEgressSystem`, `GizmoInteractionIngressSystem`, `DebugPrimitivesIngressTranslator`, `EntityAttributeSchemaPublisherSystem` (delegate to `GizmoMap.Network` transport adapters) |

### Target: GizmoMap extracted to ExtDeps (Phase 19)

| Project | References | Contents |
|---------|------------|----------|
| `ExtDeps/GizmoMap/GizmoMap.Contracts` | BCL only | Primitive protocol, stream DTOs, `GizmoPickToken`, `IGizmoSource`, settings value types, interaction event DTOs |
| `ExtDeps/GizmoMap/GizmoMap.Network` | `GizmoMap.Contracts`, CycloneDDS | DDS topic structs + stateless transport adapters (`DdsDebugPrimitivePublisher/Subscriber`, `DdsGizmoInteractionPublisher/Subscriber`) |
| `ExtDeps/GizmoMap/GizmoMap.Presentation` | `GizmoMap.Contracts`, `GizmoMap.Network`, Raylib, ImGui | Renderer, proxy tool, icon atlas adapter, MilStd2525/SemanticShape renderers, undo stack |
| `ExtDeps/GizmoMap/GizmoMap.Example` | `GizmoMap.*` only | Unified example with `--mode local` / `--mode dds` |

`Fdp.Diagnostics.Contracts`, `Fdp.Diagnostics.Network`, and `Fdp.Presentation` become thin
facades that re-export types from their `GizmoMap.*` counterparts via type aliases, preserving
backward compatibility for the FDP solution.