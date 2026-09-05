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

---

## §10 Gizmo-Driven Context Menus

### 10.1 Design Goal

Context menus must be associated with remote map entities (identified by `NetworkId`) without
introducing any new DDS topics. The back-end declares which menu belongs to which entity by
emitting a metadata primitive each frame. The menu content is transported via the existing
`StringInternBatch` side-channel and cached by the dumb terminal, so a menu definition that
never changes is transmitted only once regardless of how many entity instances reference it.

### 10.2 New Primitive: ContextMenuBinding

`DebugPrimitiveShape.ContextMenuBinding = 11` is a non-visual metadata primitive. It reuses the
two existing payload overlay fields in the 64-byte header:

| Offset | Field | Reused as |
|--------|-------|-----------|
| 8-11 | `uint StringHash` | FNV-1a hash of the menu JSON string (same overlay as `AnchorIndex`) |
| 24-31 | `long InspNetworkId` | Stable network-level entity ID to bind the menu to |

All other fields remain zero. The primitive is **never dispatched to the renderer** (the pass-2
loop skips it, alongside `SpatialAnchor`). The factory method is:

```csharp
DebugPrimitive.MakeContextMenuBinding(long networkId, uint menuJsonHash)
```

**Payload budget:** the binding fits entirely within the existing 40-byte payload union (8 bytes
used) and does not violate the 64-byte size invariant.

### 10.3 Menu Definition JSON Schema

The menu is defined as a JSON array of item objects. The same schema is used by the server to
author menus and by the presentation layer to render them. The complete schema is:

```json
[
  { "id": 1,   "label": "Center View",   "shortcut": "C" },
  { "separator": true },
  { "id": 10,  "label": "Order: Move",   "shortcut": "M", "enabled": true },
  { "id": 11,  "label": "Order: Engage", "shortcut": "E" },
  { "separator": true },
  {
    "label": "Logistics",
    "children": [
      { "id": 20, "label": "Resupply", "enabled": false, "tooltip": "Cannot resupply: Unit is moving" },
      { "id": 21, "label": "Repair" }
    ]
  },
  { "id": 99, "label": "DELETE", "style": "destructive" }
]
```

**Per-item properties:**

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `id` | integer | yes (leaf items) | Action identifier echoed back in `GizmoInteractionBatch.ActionId` |
| `label` | string | yes | Display text |
| `shortcut` | string | no | Keyboard shortcut hint displayed alongside the label |
| `enabled` | boolean | no | Defaults to `true`; if `false` the item renders grayed-out |
| `style` | string | no | Visual hint for the presentation layer; `"destructive"` renders the item in a warning colour |
| `tooltip` | string | no | Hover tooltip text (informational; current implementation ignores) |
| `separator` | boolean | no | When `true` the object is a visual divider; all other fields are ignored |
| `children` | array | no | Nested items; presence makes the entry a sub-menu header (`id` must be absent) |

### 10.4 Back-End Emission (Producer Side)

The producer node evaluates entity state, selects the appropriate menu JSON string, and interns
it via `StringInternMap`. Because `Intern` is idempotent, the string is allocated in the map
only on the very first call; subsequent calls for the same hash are no-ops:

```csharp
string menuJson = GetMenuJsonForEntity(entity);
uint   hash     = StringInternMap.Fnv1a32(menuJson);
buffer.InternMap.Intern(hash, menuJson);                           // idempotent
buffer.AppendRaw(DebugPrimitive.MakeContextMenuBinding(networkId, hash));
```

The `StringInternBatch` DDS topic transports only new hash/string pairs (entries not yet
delivered to the subscriber). The `ContextMenuBinding` primitive is emitted every frame
(like any other visual primitive) but costs only 64 bytes because it carries only the 4-byte
hash, not the full string.

**Finite permutation property:** because context menus vary by entity *type*, not by entity
*instance*, a deployment with N entity types produces at most N distinct menu JSON strings,
regardless of how many instances are alive. After each distinct string has been delivered once,
the `StringInternBatch` topic carries no new menu payload.

### 10.5 Dumb Terminal Processing (Consumer Side)

`DebugGizmoLayer.HandleInput` performs a per-frame sweep of the primitive span before any
hit-testing:

```
for each prim in frame:
    if prim.Shape == ContextMenuBinding:
        menuBindings[prim.InspNetworkId] = prim.StringHash
```

`menuBindings` is a transient `Dictionary<long, uint>` built fresh each frame; it is not a
persistent cache. The actual JSON is cached in the `StringInternMap` on the consumer's
`GizmoPrimitiveBuffer`.

On right-click, the layer performs spatial hit-testing against `Box2D` primitives with a non-zero
`SubElementId`. When a hit is found it looks up the entity ID in `menuBindings`:

```csharp
if (menuBindings.TryGetValue(entityId, out uint hash))
{
    string? json = _buffer.InternMap.TryResolve(hash);
    if (json != null)
        _contextMenuAdapter.Schedule(entityId, json);
}
```

If the JSON is not yet in the local `StringInternMap` (first frame before `StringInternBatch`
arrives), the right-click is silently ignored. No error state is needed; the menu appears on the
next right-click after the batch is received.

### 10.6 ContextMenuAdapter (Presentation)

`ContextMenuAdapter` in `GizmoMap.Presentation.UI` wraps an ImGui `BeginPopup` / `EndPopup`
lifecycle. It is owned by `DebugGizmoLayer`.

**Interface:**
```csharp
void Schedule(long anchorId, string menuJson); // called from HandleInput (before ImGui pass)
void DrawScheduled(Action<long, int>? onAction); // called inside rlImGui.Begin/End block
```

`DrawScheduled` calls `ImGui.OpenPopup` on the first frame after `Schedule`, then renders the
menu hierarchy recursively from the JSON using `ImGui.MenuItem` / `ImGui.BeginMenu`. When the
operator clicks a leaf item, `onAction(anchorId, actionId)` is invoked before
`ImGui.CloseCurrentPopup`.

The rendering pipeline requires `rlImGui.Setup` / `.Begin` / `.End` / `.Shutdown` in the Raylib
loop. `DrawContextMenu` (the public entry point on `DebugGizmoLayer`) wraps the adapter call and
translates the `(long anchorId, int actionId)` tuple back to a `GizmoPickToken`:

```csharp
public void DrawContextMenu(Action<GizmoPickToken, int>? onMenuAction = null)
{
    _contextMenuAdapter.DrawScheduled((anchorId, actionId) =>
        onMenuAction?.Invoke(new GizmoPickToken { AnchorId = anchorId }, actionId));
}
```

### 10.7 Return Trip: MenuAction Event

When an operator clicks a menu item the presentation layer publishes a
`GizmoInteractionEventKind.MenuAction` event via the existing `GizmoInteractionBatch` DDS topic.
The `ActionId` field added to `GizmoInteractionBatch` carries the integer item id:

```
GizmoInteractionBatch
  Kind        = MenuAction (4)
  PickAnchorId = networkId of the right-clicked entity
  ActionId     = id of the clicked menu item (from the JSON "id" field)
  (WorldX/Y/Z, Space remain zero for MenuAction)
```

No new DDS topics are required. The back-end ingress system routes `MenuAction` events:

```csharp
case GizmoInteractionEventKind.MenuAction:
    repo.Bus.Publish(new ContextActionTriggered
    {
        EntityNetworkId = (int)batch.PickAnchorId,
        ActionId        = batch.ActionId,
    });
    break;
```

### 10.8 GizmoMap.Example Demonstration

`DemoSceneGenerator` demonstrates the full cycle using the orange interactive Box2D (entity id
`1L`). Three menu JSON strings are pre-defined (Idle, Moving, Engaging); the active one cycles
every 3 seconds based on `_elapsedTime`. Each `EmitScene` call:

1. Selects the active menu via `GetActiveMenuJson(t)`.
2. Computes its FNV-1a hash.
3. Interns the string into `buffer.InternMap`.
4. Emits a `ContextMenuBinding` primitive.

`OnMenuAction(GizmoPickToken token, int actionId)` receives the callback from
`DebugGizmoLayer.DrawContextMenu` and logs the entity id and resolved label to the console,
confirming the full round-trip without requiring a live DDS stack.

`Program.cs` initialises `rlImGui_cs.rlImGui` and places `layer.DrawContextMenu` and
`propertyAdapter.DrawScheduled` inside the `rlImGui.Begin / End` block so both the context menu
popup and the component inspector overlay share the same ImGui frame.

### 10.9 Architectural Invariants

| Invariant | Rationale |
|-----------|-----------|
| `ContextMenuBinding` is never dispatched by the renderer | It is a metadata primitive, not a visual shape; pass-2 skips it the same way it skips `SpatialAnchor` |
| Menu JSON is transported via `StringInternBatch`, not a new topic | Re-uses the existing intern mechanism; avoids polluting the transport layer |
| `ActionId` is appended to `GizmoInteractionBatch` (not a new topic) | DDS schemas support appended fields; no wire-format break for existing consumers that ignore unknown fields |
| Per-frame `menuBindings` dictionary is transient | Avoids stale entries when an entity loses its menu binding; cost is a single O(n) scan where n is the total primitive count |
| Producer interns once, emits hash every frame | After the first delivery the `StringInternBatch` carries no new data for that menu; 64-byte binding primitive is the only per-frame overhead |
| `GizmoMap.*` assemblies have no domain-logic dependency | `ContextMenuAdapter` only takes `string menuJson`; the JSON schema is defined by the producer, not by the presentation library |

---

## §11 Composite Gizmo Identity and StructInspector Live State

### 11.1 Problem: Routing Collisions in the Flat ID Space

The current routing key is `AnchorId + SubElementId`. Two independent failure modes exist when
multiple gizmos coexist on the same ECS entity:

**Interaction routing bottleneck.** `DataDrivenGizmoSystem.FindGizmo(Entity entity)` returns
`list[0].Instance` — the first gizmo in registration order always captures all interaction events.
Sub-element IDs are not globally unique across gizmos: Gizmo A and Gizmo B may both emit `Box2D`
handles with `SubElementId = 1`. The terminal sends back identical pick tokens and the host
routes all of them to Gizmo A, silently ignoring Gizmo B.

**StructInspector ImGui window collision.** `ImGuiPropertyTreeAdapter.DrawScheduled` constructs
window stable IDs as `###StructInsp_{NetworkId}`. Two `StructInspector` primitives for the same
entity (different schema hashes) map to the same ImGui stable ID. ImGui merges them into a single
window with undefined rendering behaviour.

**Redundant root tree node.** `DrawScheduled` calls `DrawEditNode(doc!.Root, isReadOnly)`, which
wraps the root struct in a collapsible `TreeNode`. The window title already shows the struct name,
making the root node a redundant extra click before any fields become accessible.

**No live-data path from host to terminal.** The `GizmoUiState` DDS topic exists and is
published by host gizmos via `IGizmoUiStatePublisher`, but the terminal never subscribes to it.
The `ImGuiPropertyTreeAdapter` shows only the initial schema-registered values; live host-side
state changes are never reflected in the inspector panel.

**No edit-isolation for the terminal inspector.** When a `GizmoUiState` subscription is wired,
an incoming state update would clobber in-progress edits the operator has not yet committed.

**Missing StructUpdate routing in `DataDrivenGizmoSystem`.** `DataDrivenGizmoSystem.RouteInteractionEvents`
handles Started/Drag/Commit/Cancel/Menu/Key events but has no `StructUpdate` case. Entity-bound
gizmos with StructInspector panels never receive `OnStructUpdate` calls over the DDS path; only
standalone `GlobalGizmoManager` gizmos are reached.

### 11.2 Solution: Composite Key `[AnchorId] + [GizmoTypeId] + [SubElementId]`

Introduce `GizmoTypeId` (FNV-1a hash of the gizmo class full name — the same pattern as
`StructSchemaHash` for schemas) as the third routing-key component. `GizmoTypeId` uniquely
identifies the gizmo *definition* that produced a primitive; gizmo implementations need not be
aware of each other.

Key properties of the composite key:
- `AnchorId` — identifies the entity (or standalone-tool slot).
- `GizmoTypeId` — identifies the gizmo definition (class-level) within the entity's gizmo list.
- `SubElementId` — identifies a handle within a single gizmo instance.

`SubElementId` is now isolated per gizmo instance: Gizmo A and Gizmo B can both use
`SubElementId = 1` without collision because the composite key differentiates them.

### 11.3 Network Contract Changes

| Location | Change |
|----------|--------|
| `DebugPrimitive` (`GizmoMap.Contracts`) | Add `[FieldOffset(60)] public uint GizmoTypeId;`. Bytes 60-63 are free in the `Box2D`, `Arrow`, `StructInspector`, and `ContextMenuBinding` payload unions. Offset 60 is chosen because it is the first offset that is simultaneously free in both `Box2D` (payload ends at `FillColor` [56-59]) and `StructInspector` (bytes [48-63] unused). Bytes [48-51] are **not** available for `Box2D`: `BoxAngleDeg` occupies [40-43] and `BoxAnchorId` (a `long`) follows at [44-51], so [48-51] fall inside that field. `SemanticShape` uses offset 60 for `ResolvedRollRad` but is visual-only — stamping is shape-gated so `SemanticShape` is never stamped. |
| `GizmoPickToken` (`GizmoMap.Contracts`) | Add `uint GizmoTypeId;` |
| `GizmoInteractionBatch` (`GizmoMap.Network`) | Add `uint PickGizmoTypeId;` |
| `PickToken` (`Fdp.Diagnostics.Contracts`) | Add `uint GizmoTypeId;` to carry the field through the ECS event bus from ingress translator to routing system. |
| `IGizmoDefinition` (`Fdp.Toolkits`) | Add `uint GizmoTypeId { get; }`. Implementations derive the value at class-definition time via `FnvHash.Of(typeof(TGizmo).FullName)`. |

### 11.4 GizmoTypeId Injection into Emitted Primitives (Host Side)

Gizmo implementations are not required to set `GizmoTypeId` on each emitted primitive. The
orchestrating system stamps the field transparently after each `UpdateAndDraw` call:

1. `DebugPrimitiveBuffer` exposes a new `int Count { get; }` property (current write cursor) and
   a `void StampGizmoTypeId(int fromIndex, uint gizmoTypeId)` method that iterates
   `[fromIndex, Count)` and sets `primitive.GizmoTypeId = gizmoTypeId` on each primitive whose
   `Shape` is `Box2D`, `StructInspector`, or `ContextMenuBinding`. Other shapes are not stamped.
2. `DataDrivenGizmoSystem.Execute` records a watermark (`int mark = _drawBuilder.Count`) before
   each `gi.Instance.UpdateAndDraw(deltaTime, _drawBuilder)`, then calls
   `_drawBuilder.StampGizmoTypeId(mark, gi.Definition.GizmoTypeId)` immediately after.
3. `GlobalGizmoManager.Execute` applies the same watermark-stamp pattern for its standalone gizmos.

**Shape-gating invariant:** stamping only touches `Box2D`, `StructInspector`, and
`ContextMenuBinding` shapes. `SemanticShape` and other visual-only primitives are never stamped,
ensuring `ResolvedRollRad` at offset 60 is never corrupted.

### 11.5 Host-Side Router Fix (`DataDrivenGizmoSystem`)

`FindGizmo(Entity entity)` is replaced by `FindGizmo(Entity entity, uint gizmoTypeId)`.
Injected (on-demand) gizmos retain strict priority. For the base active-gizmo list:

```csharp
return list.FirstOrDefault(gi => gi.Definition.GizmoTypeId == gizmoTypeId)?.Instance;
```

All `RouteInteractionEvents` call sites pass `evt.Token.GizmoTypeId` into `FindGizmo`.

`DataDrivenGizmoSystem.RouteInteractionEvents` gains a new `StructUpdate` routing case
(previously absent). The target gizmo is resolved by `FindGizmo(entity, evt.GizmoTypeId)` and
its `OnStructUpdate(payloadJson)` method is called. This requires:

- `GizmoStructUpdateEvent` to carry a `uint GizmoTypeId` field (set from
  `GizmoInteractionBatch.PickGizmoTypeId` by the ingress translator).
- `onStructUpdate` callback in `ImGuiPropertyTreeAdapter` to change from
  `Action<long, string>?` to `Action<long, uint, string>?` (networkId, gizmoTypeId, json).
  `ScheduledItem` gains a `uint GizmoTypeId` field populated from `primitive.GizmoTypeId`
  when a `StructInspector` primitive is scheduled. The callback passes `item.GizmoTypeId` —
  never `item.SchemaHash` — so the egress translator correctly targets the gizmo class on the
  host. (`SchemaHash` and `GizmoTypeId` are different hashes of different types and must not
  be mixed.)

**Context-menu routing.** `GizmoMenuActionEvent` gains a `uint GizmoTypeId` field populated
from `PickGizmoTypeId` by the ingress translator. `DataDrivenGizmoSystem` routes
`GizmoMenuActionEvent` through `FindGizmo(entity, evt.GizmoTypeId)`, just like all other
interaction types. This prevents a menu action emitted by one gizmo from being delivered to a
sibling gizmo on the same entity.

### 11.6 Terminal-Side Pick Token and Transport Changes

**`DebugGizmoLayer.HandleInput`** reads `hit.GizmoTypeId` from the hit `DebugPrimitive` and sets
`token.GizmoTypeId = hit.GizmoTypeId` before forwarding the pick token to the interaction
callback.

**`DdsGizmoInteractionPublisher.Publish`** sets `batch.PickGizmoTypeId = token.GizmoTypeId`.

**`GizmoInteractionEgressTranslator.WriteRecord`** sets `PickGizmoTypeId = token.Target.GizmoTypeId`
(where `token.Target` is the `PickToken` containing the new field).

**`GizmoInteractionEgressTranslator.WriteStructUpdate`** sets
`PickGizmoTypeId = gizmoTypeId` (the second argument of the updated `onStructUpdate` callback
from `ImGuiPropertyTreeAdapter`, originating from `ScheduledItem.GizmoTypeId`). This value is
the FNV-1a hash of the *gizmo class* type name, not the schema hash; conflating the two would
cause `FindGizmo` on the host to silently drop every `StructUpdate` event.

**`GizmoInteractionIngressTranslator.Translate`** populates `token.GizmoTypeId = batch.PickGizmoTypeId`
and also populates `GizmoStructUpdateEvent.GizmoTypeId = batch.PickGizmoTypeId` for the
`StructUpdate` case.

### 11.7 ImGui Window Stable ID Fix and Root Node Elimination

**Window stable ID fix.** `ImGuiPropertyTreeAdapter.DrawScheduled` appends `GizmoTypeId` to the
ImGui stable ID:

```
Before: $"...###StructInsp_{item.NetworkId}"
After:  $"...###StructInsp_{item.NetworkId}_{item.GizmoTypeId}"
```

Two `StructInspector` panels on the same entity now render as independent ImGui windows even if
both gizmos project the exact same DTO schema type (identical `SchemaHash`). Using `GizmoTypeId`
(hash of the gizmo class) rather than `SchemaHash` (hash of the DTO struct) as the
discriminator ensures the stable ID is unique per gizmo instance, not per schema.

**Root node elimination.** Instead of `DrawEditNode(doc!.Root, isReadOnly)`, the method iterates
the root's children directly:

```csharp
foreach (var child in doc!.Root.Children)
    DrawEditNode(child, item.IsReadOnly);
```

Fields at the top level of the struct appear directly inside the panel without the extra
collapsible wrapper. Nested structs within those fields still render their own `TreeNode`
hierarchy.

### 11.8 StructInspector Viewing/Editing State Machine

`ImGuiPropertyTreeAdapter` maintains a
`Dictionary<(long NetworkId, uint GizmoTypeId), InspectorState> _inspectorStates` to track
focus per inspector window. `GizmoTypeId` is read from `ScheduledItem.GizmoTypeId` (which was
populated from the stamped `StructInspector` primitive). Using `GizmoTypeId` instead of
`SchemaHash` as the key ensures state isolation even if two gizmos on the same entity happen to
publish the same generic schema type. The state machine has two states:

| State | Description |
|-------|-------------|
| `Viewing` | Incoming `GizmoUiState` updates are applied immediately. The Apply button is still shown; the operator can interact with any field. |
| `Editing` | The operator has focused the window. Incoming `GizmoUiState` updates are discarded. |

State transitions:

| Trigger | Transition | Action |
|---------|-----------|--------|
| `ImGui.IsWindowFocused(RootAndChildWindows)` returns `true` while in `Viewing` | Viewing → Editing | None (begin blocking updates) |
| Window loses focus (`!isFocused`) while in `Editing` | Editing → Viewing | Invoke `onStructUpdate(networkId, gizmoTypeId, json)` to commit changes |
| Operator clicks "Apply" while in `Editing` | Editing → Viewing | Invoke `onStructUpdate(networkId, gizmoTypeId, json)` to commit changes |

Note: the key is `(NetworkId, GizmoTypeId)` rather than `NetworkId` alone so that two gizmo
inspectors on the same entity track focus independently, even if they happen to share the same
schema type.

### 11.9 GizmoUiState Subscription (Terminal Side)

The terminal application subscribes to the `GizmoUiState` DDS topic
(`TransientLocal`, `KeepLast(1)`) and calls `ImGuiPropertyTreeAdapter.ReceiveUiState(GizmoUiState state)`
when a sample arrives.

`ReceiveUiState` implementation:

1. Look up `EditDocument` by `state.GizmoInstanceId` (= `StructSchemaHash`) in
   `GizmoSchemaRegistry`.
2. Find all active `ScheduledItem`s whose `SchemaHash == state.GizmoInstanceId`.
3. If **any** of those items has `InspectorState == Editing`, discard the incoming sample
   entirely and return. The `EditDocument` is a singleton per schema hash; all matching items
   share the same `IValueBinding` memory. Deserializing while any one of them is in `Editing`
   would overwrite the user's active edits.
4. If **all** matching items are in `Viewing` (or absent from the state dict, which implies
   `Viewing`), call `EditDocumentJsonSerializer.Deserialize(state.EditDocumentJson, doc)` once
   to inject the host's live values into the shared `IValueBinding` objects.

**Host-side publish discipline.** Backend gizmos publish `GizmoUiState` only when their internal
configuration changes, not every frame. The `TransientLocal` / `KeepLast(1)` QoS guarantees that
late-joining terminals receive the current state without repeated broadcasts.

`GizmoInstanceId` (uint) is set to `StructSchemaHash` by convention. If two entity types share a
schema, they share the same `GizmoUiState` sample (last-write-wins), which is acceptable for
schema-level initial values.

### 11.10 Architectural Invariants

| Invariant | Rationale |
|-----------|-----------|
| `GizmoTypeId` stamping is shape-gated to `Box2D`, `StructInspector`, and `ContextMenuBinding` | Prevents corrupting `SemanticShape.ResolvedRollRad` which occupies the same offset 60; other visual-only shapes are never stamped |
| `GizmoTypeId` is derived from class name, not assigned manually | Guarantees uniqueness within a project without developer coordination |
| `IGizmoDefinition.GizmoTypeId` is a property, not a field | Allows derivation at type-registration time; value is stable for the process lifetime |
| `SubElementId` is unique only within a gizmo instance, not globally | Composite key semantics; the fix is at the routing layer, not in gizmo code |
| State machine key is `(NetworkId, GizmoTypeId)` | Guarantees isolation per gizmo instance; two inspectors on the same entity track focus independently even if they share the same schema type |
| `GizmoUiState.GizmoInstanceId` == `StructSchemaHash` | Reuses existing hash convention; avoids a new ID allocation scheme |
| `DataDrivenGizmoSystem.RouteInteractionEvents` gains `StructUpdate` case | Entity-bound gizmos can now receive inspector mutations over DDS; previously only `GlobalGizmoManager` gizmos received `OnStructUpdate` calls |