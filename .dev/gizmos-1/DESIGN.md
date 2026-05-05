# FDP Declarative Gizmo & Presentation Framework — Design

## Summary

We are building a **runtime-extensible declarative visualization and interaction framework** for the
FDP/HROT engine. The framework serves dual purpose: it is the debug visualization toolbox used by
developers during development, and it is the **first-class map UI rendering and interaction engine**
for production tactical presentations.

The fundamental design principle is **Evaluate Once, Present Anywhere**: gizmo logic runs once on
the authoritative simulation node, emits a stream of backend-neutral draw commands, and those
commands are routed to any number of local or remote presentation clients.

---

## Codebase Status

The following existing infrastructure is used as the foundation:

| Type | Location | Notes |
|------|----------|-------|
| `FixedString32/64` | `Fdp.Core` | Zero-alloc fixed buffers for text |
| `BitMask256` | `Fdp.Core` | SIMD HasAll/Matches, used for component filtering |
| `NativeChunkTable<T>` | `Fdp.Core` | Unmanaged ECS storage |
| `ISimulationView` / `EntityRepository` | `Fdp.Core` | Simulation read access |
| `IEcsModuleSystem` + `SystemPhase` | `Fdp.ModuleHost.Abstractions` | System registration |
| `FdpEventBus` | `Fdp.Core` | Double-buffered event bus |
| `ConstructionOrder` / `DestructionOrder` | `Fdp.Toolkit.Lifecycle.Events` | Entity lifecycle events |
| `AssignBehaviorEvent` / `ClearBehaviorEvent` | `Fdp.Toolkit.Behavior.Events` | Behavior lifecycle |
| `SelectionState` | `Hrot.IG.Components` | IsSelected, IsPrimarySelection |
| `SimTransform` | `Fdp.Core` | Position (Vector3), Rotation (Quaternion) |
| `IMapTool` / `MapCanvas` | `Fdp.Toolkit.Vis2D` | Tool stack, PushTool/PopTool |
| `IEntityShapeLibrary` | `Fdp.Toolkit.Vis2D.Shapes` | Entity shape profiles |
| `PerspectiveShapeRenderer` | `Fdp.Toolkit.Vis2D.Rendering` | Zero-alloc 2D shape renderer |
| `DebugGizmoLayer` | `Fdp.Toolkit.Vis2D.Layers` | Exists but is empty stub |
| `StructEdit` / `ImGuiPropertyTree` | `Fdp.Presentation` | Generic DTO property editor |
| `IconAtlas` | `Fdp.Presentation.Icons` | Sprite sheet for icons |
| `CullingState` | `Hrot.IG.Components` | IsVisible, LodLevel |

**Key dependency note:** `Color32` is defined in `Hrot.IG.Components` (a higher-level package that
`Fdp.Core` and `Fdp.Toolkits` cannot reference). Therefore the gizmo framework defines its own
`Rgba32` struct in `Fdp.Toolkit.Diagnostics.Gizmos`. Presentation adapters convert trivially.

**Key event note:** `AssignBehaviorEvent` is a managed class (not an unmanaged struct); it must be
read via `view.ReadManagedEvents<AssignBehaviorEvent>()`, not `ReadEvents<T>()`.

---

## Phase 1: Core Primitive Protocol

**Goal:** Establish the 64-byte blittable `DebugPrimitive` struct and `IDebugDrawBuilder`
accumulation contract as the sole output interface for all gizmos.

### 1.1 Color and Coordinate Enums

Define `Rgba32` (4-byte RGBA struct) in `Fdp.Toolkit.Diagnostics.Gizmos`. This avoids a
dependency on `Hrot.IG.Components.Color32` from the toolkit layer.

Define the following enum types alongside `DebugPrimitive`:

- **`PipelineTarget`** (flags byte): `Map2D = 1`, `Viewport3D = 2`, `All = 3`. Controls which
  rendering pipelines consume this primitive. A single emission feeds both 2D and 3D renderers.
- **`CoordinateSpace`** (byte): `World` — absolute simulation meters; `Screen` — absolute screen
  pixels bypassing camera projection; `EntityLocal` — relative to an anchor entity's `SimTransform`.
- **`SizeMode`** (byte): `WorldMeters` — thickness/radius scales with camera zoom;
  `ScreenPixels` — thickness/radius mathematically defeats camera zoom for constant screen presence.
- **`DebugPrimitiveShape`** (byte): `Line`, `Sphere`, `Box2D`, `Arrow`, `Text`, `EntityBadge`,
  `Icon`, `ComponentInspector`, and others as needed.

### 1.2 The DebugPrimitive Tagged Union

A single 64-byte `[StructLayout(LayoutKind.Explicit, Size=64)]` struct representing one
backend-neutral draw command. The header (first ~24 bytes) stores universal metadata; the remaining
bytes form a payload union that varies by `Shape`.

**Header fields (explicit offsets):**

| Field | Type | Bytes | Description |
|-------|------|-------|-------------|
| `Shape` | `DebugPrimitiveShape` | 1 | Discriminator |
| `Space` | `CoordinateSpace` | 1 | Origin anchor |
| `Color` | `Rgba32` | 4 | Primary / start color |
| `TargetView` | `PipelineTarget` | 1 | Pipeline filter mask |
| `DebugLayer` | byte | 1 | Layer 0–15; macro Z-order bucket |
| `AnchorIndex` + `AnchorGeneration` | int + ushort | 6 | Entity anchor (split for explicit layout); valid when `Space == EntityLocal`. When `Space != EntityLocal` and `Shape` is `Text` or `EntityBadge`, bytes 8–11 are reused as `uint StringHash` (see string interning below) |
| `SizeMode` | `SizeMode` | 1 | WorldMeters vs ScreenPixels |
| `ZIndex` | byte | 1 | Intra-layer fine-grained sort key (Painter's Algorithm); 0 = background |
| `ThicknessU16` | ushort | 2 | Thickness in 0.1-unit steps; `float Thickness => ThicknessU16 * 0.1f` (max 6553.5) |
| `MinZoomLod` | byte | 1 | 0 = no min limit; n × 0.25 = minimum zoom; primitive hidden below this zoom level |
| `MaxZoomLod` | byte | 1 | 0 = no max limit; n × 0.25 = maximum zoom; primitive hidden above this zoom level |
| `LifetimeSeconds` | float | 4 | 0 = one frame; >0 = persists |

**String interning escape hatch** (addresses the FixedString32 32-char limit):

`FixedString32` allows at most 31 usable ASCII characters plus a null terminator. For AI
diagnostic text that exceeds this (e.g. multi-line state dumps), the `StringHash` overlay is used.
When bytes 8–11 (normally `AnchorIndex`) are non-zero and `Space != EntityLocal`, the renderer
treats this as a `uint StringHash` key into a `StringInternMap` side-channel. The `FixedString32`
payload may contain the first 31 characters as a local-client preview fallback. The full string is
published separately via the `StringInternTopic` DDS topic or populated in the local
`StringInternMap` by the gizmo system before the render pass (see GZ019).

For `DrawTextLong(string text, ...)`: the `IDebugDrawBuilder` computes `FNV1a(text)` as the
hash, registers the full string in the `StringInternMap`, and fills the `FixedString32` with the
first 31 chars. `DrawText(FixedString32 text, ...)` always uses inline mode (StringHash = 0).

**Payload union examples** (from header end to byte 63):

- `Line`: `Vector3 LineStart`, `Vector3 LineEnd`, `Rgba32 EndColor` (for gradient alpha support)
- `Sphere`: `Vector3 Center`, `float Radius`
- `Box2D`: `Vector2 Center`, `Vector2 Extents`, `float AngleDeg`
- `Arrow`: `Vector3 From`, `Vector3 To`, `float HeadSize`
- `Text`: `Vector2 Position` (world or screen), `FixedString32 Content` (<=31 chars inline; when
  header `StringHash != 0` the full string is resolved from `StringInternMap`)
- `EntityBadge`: `Entity Target`, `FixedString32 RichText` (control codes for color switching;
  same `StringHash` escape hatch applies for long badge text)
- `Icon`: `Vector3 WorldPos`, `FixedString32 AtlasCoord` (e.g. `"b12"`)
- `ComponentInspector`: `Entity Target`, `int ComponentTypeId`, `ScreenAnchor Anchor`, `Vector2 Offset`

The size constraint (64 bytes = one cache line) is inviolable. Primitives that require larger
payloads (e.g. StructEdit JSON schemas) use a separate out-of-band side-channel (Phase 6).

A **gradient line** is expressed by setting `EndColor != Color`: the renderer uses `Color` as
the start and `EndColor` as the end, interpolating alpha and RGB across the line vertices. When
both colors are equal the renderer takes a fast path using a simple `DrawLineEx` call.

### 1.3 IDebugDrawBuilder and DebugPrimitiveBuffer

`IDebugDrawBuilder` is the write-side API that gizmo code uses. It hides the buffer internals and
provides ergonomic methods:

```csharp
// Core geometric
void DrawLine(Vector3 start, Vector3 end, Rgba32 color, float thickness = 1f,
              SizeMode sizeMode = ScreenPixels, PipelineTarget target = All, byte layer = 0);
void DrawLineGradient(Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
                      float thickness = 1f, PipelineTarget target = All, byte layer = 0);
void DrawSphere(Vector3 center, float radius, Rgba32 color, PipelineTarget target = All,
                byte layer = 0);
void DrawArrow(Vector3 from, Vector3 to, Rgba32 color, float headSize = 1f,
               byte layer = 0);
void DrawText(Vector2 position, FixedString32 text, Rgba32 color, CoordinateSpace space = World,
              byte layer = 0);
void DrawTextLong(Vector2 position, string text, Rgba32 color, CoordinateSpace space = World,
                  byte layer = 0); // Interns full string; emits StringHash != 0
// Entity-anchored
void DrawEntityBadge(Entity target, FixedString32 richText, PipelineTarget target = All);
void DrawIcon(Vector3 worldPos, FixedString32 atlasCoord, PipelineTarget target = All);
// Inspector (StructEdit round-trip)
void DrawComponentInspector<T>(Entity target, ScreenAnchor anchor, Vector2 offset,
                                bool isReadOnly = false) where T : unmanaged;
```

`DebugPrimitiveBuffer` is a thread-safe growable array of `DebugPrimitive`. It implements
`IDebugDrawBuilder`. At frame end the orchestrator reads all accumulated primitives, routes them
to the presentation adapter, and clears the buffer for the next frame.

The buffer is allocated once at startup. There is no per-frame heap allocation.

---

## Phase 2: Gizmo Contracts and Data-Driven Orchestration

**Goal:** A single generic ECS system manages all entity-bound and behavior-bound gizmos. No
specialised per-gizmo manager system is ever written by developers.

### 2.1 Orthogonal Gizmo Taxonomy

Gizmos are described along three orthogonal axes:

**Statefulness:**
- *Stateless*: No instantiation. Pure projectors that read ECS state each frame and emit
  primitives. Driven by a direct ECS query. No lifecycle management needed.
- *Stateful (ephemeral)*: Instantiated C# objects rented from an object pool. Hold transient
  presentation state across frames (trails, timers, cached projection data). Lifecycle is strictly
  event-driven (not poll-driven).

**Attachment scope:**
- *Entity-bound*: Activated for entities that match a component mask or blueprint ID.
- *Behavior-bound*: Activated for entities currently executing a specific named behavior.
- *Global*: Entity-independent (navmesh overlay, spatial grids, event bus trace).

**Visibility:**
- Controlled by an `IGizmoVisibilityPolicy` — see §2.3.

### 2.2 Core Contracts

```csharp
// Fdp.Toolkit.Diagnostics.Gizmos

public interface IStatefulGizmo
{
    void OnInitialize(ISimulationView view, Entity entity);
    void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime,
                       IDebugDrawBuilder drawBuilder);
    void OnTeardown();
}

public interface IGizmoDefinition
{
    // Component types the entity must have for this gizmo to activate
    Type[] RequiredComponents { get; }

    // Defines when the gizmo emits primitives (selection, global force, etc.)
    IGizmoVisibilityPolicy VisibilityPolicy { get; }

    IStatefulGizmo CreateInstance();
}

public interface IGizmoVisibilityPolicy
{
    // Called ONCE per frame before entity loop. True = skip per-entity check.
    bool IsGloballyEnabled(ISimulationView view);

    // Called per entity only when IsGloballyEnabled returned false.
    bool IsEntityVisible(ISimulationView view, Entity entity);
}
```

### 2.3 GizmoRegistry

`GizmoRegistry` is a managed singleton created at startup and injected via `SetSingletonManaged`.
It contains a list of `CompiledGizmoRule` records, each holding an `IGizmoDefinition` and the
`BitMask256` of required component IDs precompiled via `ComponentTypeRegistry.GetId(Type)`.

Registration is open: any module can call `registry.Register(IGizmoDefinition)` at startup. The
core system never changes to accommodate new gizmo types (Open-Closed Principle).

For behavior-bound gizmos a parallel `BehaviorGizmoRegistry` maps behavior name hash to a
`IBehaviorGizmoFactory`. It is injected separately.

### 2.4 DataDrivenGizmoSystem

A single `[UpdateInPhase(SystemPhase.PostSimulation)]` system manages all entity-bound gizmo
lifecycles and drives their execution:

1. **Teardown** (per frame): Drain `DestructionOrder` events; for each, remove gizmos from the
   active dictionary and call `OnTeardown()` on each instance.
2. **Setup** (per frame): Drain `ConstructionOrder` events; for each, evaluate all `CompiledGizmoRule`
   entries using `BitMask256.HasAll(entityHeader.ComponentMask, rule.RequiredMask)`. For matches,
   rent an instance from the rule's factory, call `OnInitialize`, and store in the dictionary keyed
   by `Entity`.
3. **Execute** (per frame): Two modes:
   - *Global force*: Iterate the active dictionary; call `UpdateAndDraw` for all gizmos on all
     living entities.
   - *Selection only* (default): Run a SIMD-accelerated ECS query for entities with
     `SelectionState`; for each selected entity do an O(1) dictionary lookup and drive only those
     gizmos. The global singleton check is hoisted outside the loop to avoid repeated lookups.

The system accesses `EntityRepository` directly (via `(EntityRepository)view`) to read
`EntityHeader.ComponentMask`. `ISimulationView` is used for all event reads and component reads.

**Global visibility singleton:** A `GlobalDebugSettings` ECS singleton (unmanaged struct, will be
defined in `Hrot.IG` or the runner) exposes `bool ForceAllGizmosVisible`. The system checks this
once per frame and switches execution modes accordingly.

### 2.5 BehaviorGizmoManagerSystem

A companion system in the same phase manages behavior-bound gizmos:

- Drains `view.ReadManagedEvents<AssignBehaviorEvent>()`: checks if the behavior name matches a
  registered `IBehaviorGizmoFactory`; if so, rents and initialises an instance keyed by entity.
- Drains `view.ReadEvents<ClearBehaviorEvent>()` and `view.ReadEvents<DestructionOrder>()` for
  teardown.
- Executes active instances via `UpdateAndDraw`, applying the same two-mode visibility pattern.

`AssignBehaviorEvent` is a managed event (class) so `ReadManagedEvents<T>` is used. `ClearBehaviorEvent`
and `DestructionOrder` are unmanaged structs so `ReadEvents<T>` is used.

---

## Phase 3: Settings Store

**Goal:** A globally shared, schema-less, zero-allocation key-value store for per-gizmo settings
with disk persistence.

### 3.1 GizmoSettingValue Tagged Union

A blittable 8-byte struct (modelled after `AttributeValueUnion`) with a `SettingType` discriminator
and a 4-byte payload union holding `bool BoolValue`, `int IntValue`, or `float FloatValue`.

### 3.2 GizmoSettingsRegistry Managed Singleton

`GizmoSettingsRegistry` is a `sealed class` injected as a managed singleton into `EntityRepository`.
Internally it uses two dictionaries:

- `Dictionary<uint, GizmoSettingValue> _active` — the hot-path store, keyed by FNV-1a 32-bit hash.
- `Dictionary<uint, GizmoSettingValue> _defaults` — default values for "reset to default".
- `Dictionary<uint, string> _hashToName` — reverse map for persistence (cold path).

**API:**
- `void RegisterSetting(string key, GizmoSettingValue defaultValue)` — idempotent.
- `GizmoSettingValue Read(uint keyHash)` — O(1), hot path.
- `void Write(uint keyHash, GizmoSettingValue value)` — publishes `GizmoSettingChangedEvent`.
- `void ResetToDefault(uint keyHash)` — restores default and removes saved override.
- `void SaveToDisk(string path)` — serializes only values that differ from defaults.
- `void LoadFromDisk(string path)` — applies overrides on top of registered defaults.
- `static uint ComputeHash(string name)` — FNV-1a: `hash=2166136261; foreach(c){hash^=c; hash*=16777619;}`.

**Hoisting rule:** Systems must compute hashes at construction time (using `static readonly uint`
fields) and call `Read(hash)` once before the entity loop, never inside the loop.

### 3.3 Settings Change Notification

`GizmoSettingChangedEvent` is an `[EventId(...)]` unmanaged struct with a `uint KeyHash` field.
When `Write` or `ResetToDefault` is called, the caller publishes this event via the command buffer
or directly to the bus.

Gizmos with expensive cached state (mesh bakes, layout trees) must drain
`view.ReadEvents<GizmoSettingChangedEvent>()` at the top of `UpdateAndDraw` and invalidate their
cache when a relevant key changes.

The overwhelming majority of gizmos (thickness, color, visibility toggles) should use simple
hoisted polling and require no event subscription.

**Known 1-frame tear (acknowledged, acceptable):** `GizmoSettingsRegistry.Write()` immediately
updates the active value dictionary so `Read()` returns the new value in the same frame N.
However, the `FdpEventBus` is strictly double-buffered: `GizmoSettingChangedEvent` is not readable
until frame N+1. Stateless projectors using hoisted polling therefore see the new value in frame N,
while stateful gizmo caches using the event drain see it in frame N+1 — a 1-frame visual
discontinuity. For diagnostic tooling this latency is **acceptable**. Stateful gizmos that require
zero-tear behaviour should supplement the event drain with a direct value comparison:
```csharp
var current = _registry.Read(_myHash);
if (current != _cachedSettingValue) { _cachedSettingValue = current; RebuildCache(); }
```
This makes the gizmo robust to event timing without adding frame latency.

---

## Phase 4: Interactive Input Routing

**Goal:** Safe, exclusive input capture with zero ECS mutation from gizmo logic; all mutations go
through `IEntityCommandBuffer`.

### 4.1 PickToken

A 12-byte blittable struct embedded in interactive `DebugPrimitive`s:

```csharp
public struct PickToken
{
    public Entity Target;   // ECS entity (includes generational safety)
    public uint SubElementId; // 0 = whole entity; >0 = sub-element index
    public bool IsValid => !Target.IsNull;
}
```

`PickToken` is packed into the primitive payload for any shape that is pickable. Non-interactive
primitives leave `Target = Entity.Null`.

### 4.2 Backend-Neutral Interaction Events

All interaction between the presentation layer and the simulation kernel is mediated by serializable
events (usable both locally via `FdpEventBus` and remotely via DDS):

```csharp
[EventId(...)] public struct GizmoInteractionStartedEvent  { public PickToken Token; public Vector3 WorldPos; }
[EventId(...)] public struct GizmoDragUpdateEvent          { public PickToken Token; public Vector3 WorldPos; }
[EventId(...)] public struct GizmoInteractionCommitEvent   { public PickToken Token; public Vector3 WorldPos; }
[EventId(...)] public struct GizmoInteractionCancelEvent   { public PickToken Token; }
```

These events carry world-space coordinates, not screen-space coordinates. The presentation adapter
converts screen to world before publishing.

### 4.3 GizmoInteractionProxyTool

A `sealed class : IMapTool` living in `Fdp.Presentation`. It does NOT contain gizmo logic; it is
a "dumb terminal" proxy that:

1. **Activation**: Pushed onto the `MapCanvas` stack by the hit-test code when a pickable
   `DebugPrimitive` is clicked. Publishes `GizmoInteractionStartedEvent`.
2. **Focus capture**: While active, `HandleDrag` streams `GizmoDragUpdateEvent` each frame.
3. **Self-deactivation** (3 paths):
   - Mouse release on commit → publishes `GizmoInteractionCommitEvent`, calls `_canvas.PopTool()`.
   - `Escape` or right-click → publishes `GizmoInteractionCancelEvent`, calls `PopTool()`.
   - Click-away in sticky mode → publishes `GizmoInteractionCancelEvent`, calls `PopTool()`, and
     returns `false` from `HandleClick` so the underlying `StandardInteractionTool` can process the
     click in the same frame (seamless entity selection handoff).

The simulation-side systems receive these events via `view.ReadEvents<T>()` in `PostSimulation`
phase and apply deferred mutations through `view.GetCommandBuffer()`.

### 4.4 Safe ECS Mutation

Gizmos must never directly mutate ECS component memory. All mutations go through
`IEntityCommandBuffer`:
- `cmd.SetComponent<T>(entity, updatedValue)` for component patches.
- `cmd.PublishEvent(new SomeEvent {...})` for triggering AI/behavior reactions.

The command buffer is played back deterministically during the next kernel sync point.

---

## Phase 5: 2D Presentation Adapter

**Goal:** Local Raylib rendering of the primitive stream inside the existing `DebugGizmoLayer`
stub, with correct filtering and spatial resolution.

### 5.1 DebugPrimitiveRenderer2D

A class in `Fdp.Toolkit.Vis2D.Gizmos` (in `Fdp.Presentation`) that receives
`ReadOnlySpan<DebugPrimitive>` and iterates it to issue Raylib draw calls.

**Per-primitive evaluation:**
1. Pipeline check: `if ((prim.TargetView & PipelineTarget.Map2D) == 0) continue;`
2. Layer check: `if ((activeLayerMask & (1u << prim.DebugLayer)) == 0) continue;`
3. Coordinate resolution: see §5.2.
4. Shape dispatch: `DrawLineEx`, `DrawCircleV`, `DrawText`, etc.

**Layer mask**: The renderer holds a `ushort ActiveDebugLayerMask` (bits 0–15). Layer 0 is always
enabled (`1 << 0 = 1`). The user can toggle layers via the ImGui debug panel.

**Painter's Algorithm sort**: Before issuing any draw calls the renderer performs a stable sort
on the span using the composite key `(prim.DebugLayer << 8) | prim.ZIndex`. This ensures that
within a layer, a tooltip background (ZIndex=0) always renders before its text label (ZIndex=1)
regardless of ECS chunk iteration order.

**LOD zoom culling**: After the layer mask check, the renderer evaluates `MinZoomLod`/`MaxZoomLod`:
- If `MinZoomLod != 0` and `ctx.Zoom < MinZoomLod * 0.25f` -> skip (zoomed too far out).
- If `MaxZoomLod != 0` and `ctx.Zoom > MaxZoomLod * 0.25f` -> skip (zoomed too far in).
This allows text labels to disappear at low zoom and detail overlays to appear only when the
operator zooms in, without any ECS query changes on the simulation node.

### 5.2 Spatial Projection and Thickness Scaling

Two orthogonal decisions per primitive:

**CoordinateSpace resolution:**
- `World`: Leave camera matrix active. Primitive origin pans with map.
- `Screen`: Pop camera matrix before issuing draw call. Primitive is "glued to glass".
- `EntityLocal`: Resolve anchor entity's `SimTransform` via `view.GetComponentRO<SimTransform>(anchor)`.
  Apply quaternion rotation and position offset to local coordinates before rendering. If the anchor
  entity is not alive, the primitive is silently skipped.

**SizeMode scaling:**
- `ScreenPixels`: `finalThickness = (prim.ThicknessU16 * 0.1f) / safeZoom` (mirrors existing
  pattern in `PerspectiveShapeRenderer` and current map tools).
- `WorldMeters`: `finalThickness = prim.ThicknessU16 * 0.1f` — the camera matrix handles natural scaling.

The two axes are independent and fully combinable (e.g. `World + ScreenPixels` = world-anchored
position with constant-pixel line thickness, the most common case for tactical overlays).

### 5.3 DebugGizmoLayer Integration

The existing empty `DebugGizmoLayer.Draw(RenderContext ctx)` method is wired to call
`DebugPrimitiveRenderer2D.RenderPrimitives(...)` with the current frame's primitive buffer. The
`DataDrivenGizmoSystem` and `BehaviorGizmoManagerSystem` write into the shared
`DebugPrimitiveBuffer` before the presentation layer renders.

For interactive hit testing, `DebugGizmoLayer.HandleInput` is updated to perform spatial
intersection tests against the last frame's primitive buffer. When a primitive with a valid
`PickToken` is hit, the layer pushes a `GizmoInteractionProxyTool` onto the `MapCanvas`.

### 5.4 Entity Badges and Rich Text

The `EntityBadge` primitive carries an `Entity Target` and a `FixedString32 RichText`. The
renderer aggregates all badges for each entity and renders them below the entity's label in
`ResolvedStyle` order.

Rich text uses inline control bytes to switch color during rendering (modelled after the existing
`LogSyntaxHighlighter` span-slicing pattern):
- `\x01` = Red, `\x02` = Green, `\x03` = Yellow, `\x04` = White (default)

`RichTextRenderer.DrawRichTextBadge` iterates the raw bytes of `FixedString32` without any string
allocations, calls `Raylib.DrawText` for each contiguous monochrome chunk, and advances the X
cursor by the measured width.

**Important constraint:** Gizmos must never write to `ResolvedStyle._labelText`. That component
is a production rendering component strictly padded to `ResolvedStyleConstants.MaxStyleBytes = 64`
for cache efficiency. Badge text is purely additive, rendered via the `EntityBadge` primitive in
the gizmo layer above the standard entity renderer.

---

## Phase 6: Remote Visualization Foundation

**Goal:** Prepare the primitive stream and settings for network transport over CycloneDDS, enabling
headless `ClusterRunner` + remote viewer scenarios.

### 6.1 DebugPrimitivesBatch DDS Topic

Define a `[DdsTopic("DebugPrimitivesBatch")]` DDS topic with `DdsReliability.BestEffort` (high
frequency) to carry per-frame arrays of `DebugPrimitive`. The 64-byte fixed-size struct is
directly blittable — no serialization overhead.

The topic carries a `FrameNumber` key and a `DebugPrimitive[]` payload. Persistent primitives
(those with `LifetimeSeconds > 0`) are re-emitted each frame until they expire.

### 6.2 GizmoSettingsPublisherSystem

A system that watches for `GizmoSettingChangedEvent` (or fires once at boot) and rebuilds a
`StructEdit` `EditDocument` from the `GizmoSettingsRegistry`. The document is serialized to JSON
via `EditDocumentJsonSerializer` and published on a `TransientLocal` DDS topic. Remote clients
subscribe and render an `ImGuiPropertyTree` from the JSON schema without needing to reference any
C# gizmo assemblies.

### 6.3 GizmoUiState DDS Topic

For gizmo-specific interactive DTOs (not ECS components), a separate `[DdsTopic("GizmoUiState")]`
with `TransientLocal` durability carries `uint GizmoInstanceId` and `string EditDocumentJson`. The
spatial anchor (`DrawComponentInspector` primitive) references the `GizmoInstanceId`. Remote clients
match the ID and render the property grid.

### 6.4 Terminal Capability Announcement

Remote clients (IG nodes) publish an `IGCapabilitiesAnnounce` DDS message on startup declaring
supported pipeline targets, layer names, and supported primitive shapes. The simulation node can
use this to tailor its output (e.g. skip `Viewport3D` primitives when the client has no 3D viewport).

This is a **best-effort enhancement**: simulation nodes can ignore the capabilities and emit `All`
target primitives; clients silently drop unsupported shapes.

---

## Architectural Decisions

| Decision | Rationale |
|----------|-----------|
| Own `Rgba32` color type (not `Hrot.IG.Components.Color32`) | `Fdp.Toolkits` cannot reference `Hrot.Core`; presentation adapters trivially convert |
| `DebugPrimitive` fixed to 64 bytes | One cache line; safe for `NativeChunkTable` and DDS transport |
| No per-gizmo system; single `DataDrivenGizmoSystem` | Open-Closed Principle; adding a new gizmo type never touches the system |
| No callbacks/events for the 95% case (color, thickness) | Hoisted polling is faster, simpler, deterministic |
| `BitMask256.HasAll` for component matching | AVX2 path; O(1) per entity regardless of gizmo count |
| `GizmoInteractionProxyTool` lives in `Fdp.Presentation` | Depends on `IMapTool`, `MapCanvas`, `Raylib_cs` — all in Presentation |
| Gizmo visibility policies live in `Hrot.IG` | They reference `SelectionState` from `Hrot.IG.Components` |
| No direct `ImGui` or `Raylib` calls in gizmo `UpdateAndDraw` | Keeps simulation node headless and remote-visualizer-compatible |
| `AssignBehaviorEvent` read via `ReadManagedEvents<T>` | It is a managed class, not an unmanaged struct |
| Settings side-channel uses StructEdit JSON, not shared assemblies | Remote clients have no knowledge of concrete gizmo DTOs |

---

## Project Layout

| Project | Namespace | Contents |
|---------|-----------|----------|
| `Fdp.Toolkits` | `Fdp.Toolkit.Diagnostics.Gizmos` | `Rgba32`, `DebugPrimitive`, all enums, `IDebugDrawBuilder`, `DebugPrimitiveBuffer`, `IStatefulGizmo`, `IGizmoDefinition`, `IGizmoVisibilityPolicy`, `GizmoRegistry`, `DataDrivenGizmoSystem`, `BehaviorGizmoManagerSystem`, settings, interaction events |
| `Fdp.Presentation` | `Fdp.Toolkit.Vis2D.Gizmos` | `DebugPrimitiveRenderer2D`, `GizmoInteractionProxyTool`, `RichTextRenderer` |
| `Hrot.IG` | `Hrot.IG.Gizmos` | Concrete gizmo implementations, `SelectedEntityPolicy`, `GlobalDebugSettings` ECS singleton |

No new project references are required. All new dependencies are within the existing project graph.
