# FDP Declarative Gizmo & Presentation Framework — Task Detail

**Design Reference:** [DESIGN.md](./DESIGN.md)

---

## Phase 1: Core Primitive Protocol

---

### TASK-GZ001 — Color Type and Primitive Enums

**Design reference:** DESIGN.md §1.1

**Scope:**
Define the foundational value types that every other task depends on.
Does NOT include `DebugPrimitive` itself (GZ002) or any system code.

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/`:
- `Rgba32.cs`
- `PipelineTarget.cs`
- `CoordinateSpace.cs`
- `SizeMode.cs`
- `DebugPrimitiveShape.cs`
- `ScreenAnchor.cs`
- `PickToken.cs`

**Constraints:**
- `Rgba32`: `[StructLayout(Sequential, Size=4)]` with `byte R, G, B, A`. Must NOT reference
  `Hrot.IG.Components.Color32`. Provide an implicit conversion helper or a `ToRgba32()` extension
  in the Presentation project (GZ012). Named constants: `Rgba32.Red`, `Rgba32.Green`,
  `Rgba32.Yellow`, `Rgba32.White`, `Rgba32.Black`, `Rgba32.Transparent`.
- `PipelineTarget`: `[Flags] enum : byte` — `None=0`, `Map2D=1`, `Viewport3D=2`, `NodeGraph = 4`, `All=7`.
- `CoordinateSpace`: `enum : byte` — `World=0`, `Screen=1`, `EntityLocal=2`.
- `SizeMode`: `enum : byte` — `WorldMeters=0`, `ScreenPixels=1`.
- `DebugPrimitiveShape`: `enum : byte` — `Line=0`, `Sphere=1`, `Box2D=2`, `Arrow=3`, `Text=4`,
  `EntityBadge=5`, `Icon=6`, `ComponentInspector=7`. More can be added; enum values must never be
  renumbered once assigned.
- `ScreenAnchor`: `enum : byte` — `TopLeft=0`, `TopCenter=1`, `TopRight=2`, `Center=3`,
  `BottomLeft=4`, `BottomCenter=5`, `BottomRight=6`.
- `PickToken`: `[StructLayout(Sequential)]` struct with `Entity Target` and `uint SubElementId`.
  Property `bool IsValid => !Target.IsNull`. A zero-value `PickToken` is non-interactive.
- All types must be in namespace `Fdp.Toolkit.Diagnostics.Gizmos`.
- All types must be `public`.

**Success conditions:**
- SC-GZ001-1: `Rgba32` can be created from four bytes and round-trips R/G/B/A correctly.
  Test: `var c = new Rgba32(255, 128, 0, 64); Assert.Equal(255, c.R); Assert.Equal(128, c.G); Assert.Equal(0, c.B); Assert.Equal(64, c.A);`
- SC-GZ001-2: `Rgba32` struct is exactly 4 bytes.
  Test: `Assert.Equal(4, Marshal.SizeOf<Rgba32>());`
- SC-GZ001-3: `PipelineTarget.All == (PipelineTarget.Map2D | PipelineTarget.Viewport3D)`.
- SC-GZ001-4: `PickToken` with `Entity.Null` has `IsValid == false`.
- SC-GZ001-5: `PickToken` with a live entity has `IsValid == true`.
- SC-GZ001-6 (negative): `PickToken` zero-initialised (all bytes zero) has `IsValid == false`
  (because `Entity.Null` has `Generation == 0`).

---

### TASK-GZ002 — DebugPrimitive Tagged Union

**Design reference:** DESIGN.md §1.2

**Scope:**
The 64-byte `DebugPrimitive` struct and its payload subtype helpers.
Does NOT include `IDebugDrawBuilder` (GZ003) or rendering code.

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/`:
- `DebugPrimitive.cs`

**Constraints:**
- `[StructLayout(LayoutKind.Explicit, Size = 64)]` — total size must be exactly 64 bytes.
- `[FieldOffset(0)]  DebugPrimitiveShape Shape` (byte)
- `[FieldOffset(1)]  CoordinateSpace Space` (byte)
- `[FieldOffset(2)]  Rgba32 Color` (4 bytes)
- `[FieldOffset(6)]  PipelineTarget TargetView` (byte)
- `[FieldOffset(7)]  byte DebugLayer` (0–15; enforce via API only, not struct)
- `[FieldOffset(8)]  int AnchorIndex` — `Entity.Index` part; AnchorIndex + AnchorGeneration
  together encode the anchor entity for `EntityLocal` space.
  **String interning overlay** (when `Space != EntityLocal` and `Shape` is `Text` or `EntityBadge`):
  `[FieldOffset(8)] uint StringHash` overlaps `AnchorIndex`. `StringHash == 0` = inline
  `FixedString32` (<=31 chars). `StringHash != 0` = key in `StringInternMap`; `FixedString32`
  holds a 31-char preview/truncation.
- `[FieldOffset(12)] ushort AnchorGeneration`
- `[FieldOffset(14)] SizeMode SizeMode` (byte)
- `[FieldOffset(15)] byte ZIndex` (intra-layer sort key, 0 = background / lowest; higher = rendered on top within same DebugLayer)
- `[FieldOffset(16)] ushort ThicknessU16` — thickness in 0.1-unit steps (0–6553.5);
  helper property: `float Thickness => ThicknessU16 * 0.1f`
- `[FieldOffset(18)] byte MinZoomLod` — 0 = no minimum; n x 0.25 = minimum zoom threshold;
  renderer skips primitive when `ctx.Zoom < MinZoomLod * 0.25f`
- `[FieldOffset(19)] byte MaxZoomLod` — 0 = no maximum; n x 0.25 = maximum zoom threshold;
  renderer skips primitive when `ctx.Zoom > MaxZoomLod * 0.25f`
- `[FieldOffset(20)] float LifetimeSeconds`
- `[FieldOffset(24)] ...` payload union starts here (40 bytes available)

**Payload layouts** (all start at offset 24):
- `Line`: `[24] Vector3 LineStart`, `[36] Vector3 LineEnd`, `[48] Rgba32 EndColor` (gradient)
  = 28 bytes. Total with header = 52; remaining 12 bytes padding/unused.
- `Sphere`: `[24] Vector3 SphereCenter`, `[36] float SphereRadius` = 16 bytes.
- `Box2D`: `[24] float BoxCenterX`, `[28] float BoxCenterY`, `[32] float BoxExtentX`,
  `[36] float BoxExtentY`, `[40] float BoxAngleDeg` = 20 bytes.
- `Arrow`: `[24] Vector3 ArrowFrom`, `[36] Vector3 ArrowTo`, `[48] float ArrowHeadSize` = 28 bytes.
- `Text`: `[24] float TextX`, `[28] float TextY`, `[32] FixedString32 TextContent`
  = 8 + 32 = 40 bytes exactly. Note: positions are 2D (X/Y); Z is ignored.
  When header `StringHash != 0`, `TextContent` holds the first 31 chars as a preview;
  the full string is resolved from `StringInternMap` keyed by `StringHash`.
- `EntityBadge`: `[24] int BadgeTargetIndex`, `[28] ushort BadgeTargetGen`, `[30] byte _pad`,
  `[31] byte _pad`, `[32] FixedString32 BadgeRichText` = 40 bytes exactly.
- `Icon`: `[24] Vector3 IconWorldPos`, `[36] FixedString32 IconAtlasCoord`
  = 12 + 32 = 44 bytes. (Note: will require a `FixedString32` at offset 36 — verify alignment.)
- `ComponentInspector`: `[24] int InspTargetIndex`, `[28] ushort InspTargetGen`, `[30] ScreenAnchor InspAnchor`,
  `[31] byte _pad`, `[32] int InspComponentTypeId`, `[36] float InspOffsetX`, `[40] float InspOffsetY`,
  `[44] byte InspIsReadOnly` = 21 bytes, rest padding.

`FixedString32` at non-aligned offsets: verify with `Marshal.SizeOf<FixedString32>() == 32`.
Use `unsafe` overlapping where needed and verify with unit test.

**Helper property:** `Entity Anchor { get { return new Entity(AnchorIndex, AnchorGeneration); } }`.

**Static factory helpers** (optional, for readability):
```csharp
public static DebugPrimitive MakeLine(Vector3 from, Vector3 to, Rgba32 color,
    float thickness = 1f, SizeMode sizeMode = SizeMode.ScreenPixels,
    PipelineTarget target = PipelineTarget.All, byte layer = 0);
```

Conversion note: `thickness` float parameters are stored as `(ushort)(thickness * 10f)`. The
factory helpers handle the conversion. Callers always pass `float`; `ThicknessU16` is an
implementation detail of the struct layout.

**Constraints:**
- `unsafe` is allowed for `[StructLayout(Explicit)]` overlapping. Fdp.Toolkits already enables
  `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`.
- `FixedString32` is in `Fdp.Core` — no new project reference needed.
- All payload overlaps must be declared with explicit `[FieldOffset]`. No implicit padding.

**Success conditions:**
- SC-GZ002-1: `Marshal.SizeOf<DebugPrimitive>() == 64`.
- SC-GZ002-2: A `Line` primitive written with `LineStart = Vector3.Zero`, `LineEnd = Vector3.UnitX`
  reads back the correct values without corruption of other fields.
- SC-GZ002-3: Changing `Color` does not corrupt the payload bytes (offset isolation test).
- SC-GZ002-4: A `Text` primitive with `FixedString32("Hello")` reads back `"Hello"` correctly.
- SC-GZ002-5: `DebugLayer = 15` round-trips without altering adjacent fields (`SizeMode`, `ThicknessU16`).
- SC-GZ002-6: A `Line` primitive with `EndColor = Rgba32.Red` (gradient) reads back the correct
  end color and does not corrupt `LineStart` or `LineEnd`.
- SC-GZ002-7: Zero-value `DebugPrimitive` (all bytes zero) is a valid non-rendered primitive
  (Shape=0=Line, TargetView=0=None — will be silently culled by the renderer).
- SC-GZ002-8: `ZIndex = 5` round-trips without corrupting adjacent `SizeMode` or `ThicknessU16` fields.
- SC-GZ002-9: `ThicknessU16 = 15` (represents 1.5 units) round-trips; `Thickness` property returns `1.5f`
  within `float.Epsilon` tolerance.
- SC-GZ002-10: `MinZoomLod = 4` (threshold 1.0f), `MaxZoomLod = 40` (threshold 10.0f) round-trip
  without corrupting adjacent `ThicknessU16` or `LifetimeSeconds`.
- SC-GZ002-11: A `Text` primitive with `StringHash != 0` reads back the same `StringHash` via the
  `uint` overlay at offset 8, without corrupting `TextX`, `TextY`, or `TextContent`.

---

### TASK-GZ003 — IDebugDrawBuilder and DebugPrimitiveBuffer

**Design reference:** DESIGN.md §1.3

**Scope:**
The write-side API for gizmos and the thread-safe accumulation buffer.
Does NOT include rendering (GZ012) or the systems that call the builder.

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`:
- `IDebugDrawBuilder.cs`
- `DebugPrimitiveBuffer.cs`

**`IDebugDrawBuilder` contract:**
```csharp
public interface IDebugDrawBuilder
{
    void DrawLine(Vector3 start, Vector3 end, Rgba32 color,
                  float thickness = 1f, SizeMode sizeMode = SizeMode.ScreenPixels,
                  PipelineTarget target = PipelineTarget.All, byte layer = 0);

    void DrawLineGradient(Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
                          float thickness = 1f, SizeMode sizeMode = SizeMode.ScreenPixels,
                          PipelineTarget target = PipelineTarget.All, byte layer = 0);

    void DrawSphere(Vector3 center, float radius, Rgba32 color,
                    PipelineTarget target = PipelineTarget.All, byte layer = 0);

    void DrawArrow(Vector3 from, Vector3 to, Rgba32 color, float headSize = 1f,
                   byte layer = 0);

    void DrawText(float x, float y, FixedString32 text, Rgba32 color,
                  CoordinateSpace space = CoordinateSpace.World, byte layer = 0);

    // Interns full managed string for text exceeding 31 chars; emits StringHash != 0.
    // The first 31 chars are stored inline as a preview fallback.
    // NOTE: this method allocates on the intern map registration path (cold path only;
    // subsequent calls with the same text hit the map and allocate nothing).
    void DrawTextLong(float x, float y, string text, Rgba32 color,
                      CoordinateSpace space = CoordinateSpace.World, byte layer = 0);

    void DrawEntityBadge(Entity target, FixedString32 richText,
                         PipelineTarget targetPipeline = PipelineTarget.All);

    void DrawEntityLocal(Entity anchor, Vector3 localStart, Vector3 localEnd,
                         Rgba32 color, float thickness = 1f, byte layer = 0);
}
```

**`DebugPrimitiveBuffer`:**
- `sealed class` implementing `IDebugDrawBuilder`.
- Internally owns a `DebugPrimitive[]` (plain managed array, pre-allocated at construction).
- `int _count` field tracks current count.
- Thread-safe writes via `Interlocked.Increment` for count reservation; slot is written
  without locking (append-only, single-writer preferred but safe for multi-writer with reservation).
- `ReadOnlySpan<DebugPrimitive> GetFrame()` — returns a span of `_count` written primitives.
- `void Clear()` — resets `_count = 0` without reallocating.
- Constructor: `DebugPrimitiveBuffer(int capacity = 4096)`.
- If capacity is exhausted, primitives are silently dropped (no exceptions on hot path). A counter
  `int DroppedCount` tracks overflow for diagnostics.

**Constraints:**
- The buffer's `GetFrame()` must return a zero-copy span (no copy of the internal array).
- `Clear()` must be safe to call from the render thread between frames.
- No managed heap allocation on any draw method call.

**Success conditions:**
- SC-GZ003-1: Drawing 5 lines and calling `GetFrame()` returns exactly 5 primitives in order.
- SC-GZ003-2: `Clear()` followed by `GetFrame()` returns an empty span (Length == 0).
- SC-GZ003-3: Drawing more than `capacity` primitives does not throw; `DroppedCount > 0`.
- SC-GZ003-4: `DrawEntityLocal` emits a `DebugPrimitive` with `Space == EntityLocal` and the
  correct `AnchorIndex` / `AnchorGeneration` matching the provided entity.
- SC-GZ003-5: `DrawLineGradient` emits a `Line` primitive where `EndColor != Color` when different
  colors are supplied.
- SC-GZ003-6: `DrawLine` (solid) emits a `Line` primitive where `EndColor == Color`.
- SC-GZ003-7: `DrawTextLong` with a 60-character string emits a `Text` primitive with
  `StringHash != 0`; the `FixedString32` payload contains the first 31 characters.
- SC-GZ003-8: `DrawTextLong` with the same string called twice emits the same `StringHash`
  both times (FNV-1a is deterministic).
- SC-GZ003-9: `DrawText` with a `FixedString32` always emits `StringHash == 0` (inline mode).

---

## Phase 2: Gizmo Contracts and Data-Driven Orchestration

---

### TASK-GZ004 — Gizmo Contracts (Interfaces)

**Design reference:** DESIGN.md §2.1–2.3

**Scope:**
`IStatefulGizmo`, `IGizmoDefinition`, `IGizmoVisibilityPolicy`, `GizmoRegistry`.
Does NOT include the system that drives them (GZ005/GZ006).

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`:
- `IStatefulGizmo.cs`
- `IGizmoDefinition.cs`
- `IGizmoVisibilityPolicy.cs`
- `GizmoRegistry.cs`

**`IStatefulGizmo`:**
```csharp
public interface IStatefulGizmo
{
    void OnInitialize(ISimulationView view, Entity entity);
    void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime,
                       IDebugDrawBuilder drawBuilder);
    void OnTeardown();
}
```

**`IGizmoDefinition`:**
```csharp
public interface IGizmoDefinition
{
    Type[] RequiredComponents { get; }
    IGizmoVisibilityPolicy VisibilityPolicy { get; }
    IStatefulGizmo CreateInstance();
}
```

**`IGizmoVisibilityPolicy`:**
```csharp
public interface IGizmoVisibilityPolicy
{
    bool IsGloballyEnabled(ISimulationView view);
    bool IsEntityVisible(ISimulationView view, Entity entity);
}
```

Provide two built-in implementations:
- `AlwaysVisiblePolicy : IGizmoVisibilityPolicy` — returns `true` from both methods.
- `NeverVisiblePolicy : IGizmoVisibilityPolicy` — returns `false` from both (useful for
  temporarily disabling a gizmo type without unregistering it).

**`GizmoRegistry`:**
`sealed class`. Holds `List<CompiledGizmoRule>` where:
```csharp
internal struct CompiledGizmoRule
{
    public IGizmoDefinition Definition;
    public BitMask256 RequiredMask;
    public int RuleIndex; // position in registry list; used for global visibility cache indexing
}
```

`void Register(IGizmoDefinition definition)`: converts `definition.RequiredComponents` to IDs via
`ComponentTypeRegistry.GetId(Type)`. If any ID is -1 (unregistered), throw
`InvalidOperationException` with a message naming the unregistered type. Computes `BitMask256`
from the IDs. Assigns `RuleIndex = Rules.Count`. Adds to `Rules`.

`IReadOnlyList<CompiledGizmoRule> Rules` — read-only exposure for the system.

**Constraints:**
- `GizmoRegistry` must be callable from startup code before any ECS frame ticks.
- `Register` is not thread-safe; it must only be called during startup initialization.
- `AlwaysVisiblePolicy` should be a singleton (`static readonly AlwaysVisiblePolicy Instance`).

**Success conditions:**
- SC-GZ004-1: Registering a definition with two required component types produces a
  `CompiledGizmoRule.RequiredMask` that has exactly those two component bits set.
- SC-GZ004-2: Registering a definition with an unregistered component type throws `InvalidOperationException`.
- SC-GZ004-3: `AlwaysVisiblePolicy.IsGloballyEnabled(...)` returns `true` regardless of view state.
- SC-GZ004-4: `AlwaysVisiblePolicy.IsEntityVisible(...)` returns `true` regardless of entity state.
- SC-GZ004-5: `NeverVisiblePolicy` returns `false` from both methods.
- SC-GZ004-6: Multiple registrations accumulate in `Rules` (count grows correctly).

---

### TASK-GZ005 — DataDrivenGizmoSystem (Entity-Bound)

**Design reference:** DESIGN.md §2.4

**Scope:**
The single ECS system that manages lifecycle and drives execution of all entity-bound gizmos.
Does NOT include behavior-bound gizmos (GZ006).

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/`:
- `DataDrivenGizmoSystem.cs`

**Constraints:**
- `[UpdateInPhase(SystemPhase.PostSimulation)]`.
- Drains `view.ReadEvents<DestructionOrder>()` for teardown.
- Drains `view.ReadEvents<ConstructionOrder>()` for setup.
- On `ConstructionOrder`: cast `view` to `EntityRepository` to access
  `repo.GetHeader(evt.Entity.Index).ComponentMask`. Evaluate each `CompiledGizmoRule` with
  `BitMask256.HasAll(header.ComponentMask, rule.RequiredMask)`.
- Maintains `Dictionary<Entity, List<CompiledGizmoInstance>> _activeGizmos`.
- `CompiledGizmoInstance` has fields: `IStatefulGizmo Instance`, `IGizmoDefinition Definition`,
  `int RuleIndex`.
- Pre-evaluates global visibility for all rules once per frame into a `bool[]` array (allocated
  once at startup, sized to `registry.Rules.Count`).
- Default execute mode (no global force): Use ECS query with `SelectionState` component; for each
  entity in query, check if selected, then do `_activeGizmos.TryGetValue` and drive visible gizmos.
  The `SelectionState` query uses `view.Query().With<SelectionState>().Build()`.
- Global force mode: iterate `_activeGizmos` directly.
- Switching between modes is controlled by reading `GlobalDebugSettings` singleton (see below).
- `GlobalDebugSettings` access: the system checks `(view is EntityRepository repo && repo.HasSingleton<GlobalDebugSettings>())`.
  If the singleton is absent, default to selection-only mode.
- The `_globalVisibilityCache` array must be pre-allocated once and reused each frame.
- Calls `view.IsAlive(entity)` before each `UpdateAndDraw` call.

**Dependency injection:**
Constructor takes `GizmoRegistry registry`, `IDebugDrawBuilder drawBuilder`.
Both are injected at system creation time.

**Success conditions:**
- SC-GZ005-1: Setup — When a `ConstructionOrder` event is published for entity E with component mask
  matching a registered gizmo definition, `_activeGizmos` contains an entry for E with an
  initialized `IStatefulGizmo` instance. `OnInitialize` is called exactly once.
- SC-GZ005-2: Teardown — After a `DestructionOrder` event for entity E, `_activeGizmos` no longer
  contains E. `OnTeardown()` was called on all instances.
- SC-GZ005-3: Execute (selection mode) — A gizmo for an entity with `IsSelected == false` does NOT
  call `UpdateAndDraw`. After setting `IsSelected = true`, it calls `UpdateAndDraw`.
- SC-GZ005-4: Execute (global force) — With `GlobalDebugSettings.ForceAllGizmosVisible = true`,
  `UpdateAndDraw` is called for all active gizmos regardless of selection state.
- SC-GZ005-5: `NeverVisiblePolicy` — even in global-force mode, a gizmo with `NeverVisiblePolicy`
  whose `IsGloballyEnabled` returns `false` and `IsEntityVisible` returns `false` is not drawn.
- SC-GZ005-6: Gizmo not matching component mask — entity with insufficient components does NOT get
  a gizmo activated.
- SC-GZ005-7: Entity with `IsAlive == false` is skipped during execute (generational safety).
- SC-GZ005-8: Global visibility cache is evaluated once per frame (not once per entity). Verify by
  counting invocations of a mock `IGizmoVisibilityPolicy.IsGloballyEnabled`.

---

### TASK-GZ006 — BehaviorGizmoManagerSystem (Behavior-Bound)

**Design reference:** DESIGN.md §2.5

**Scope:**
System for behavior-bound gizmos activated by `AssignBehaviorEvent`/`ClearBehaviorEvent`.

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/`:
- `BehaviorGizmoManagerSystem.cs`
- `IBehaviorGizmoFactory.cs`
- `BehaviorGizmoRegistry.cs`

**`IBehaviorGizmoFactory`:**
```csharp
public interface IBehaviorGizmoFactory
{
    string BehaviorName { get; }
    IStatefulGizmo Rent();
    void Return(IStatefulGizmo gizmo);
}
```

**`BehaviorGizmoRegistry`:**
`sealed class` with:
- `void Register(IBehaviorGizmoFactory factory)` — keyed by `factory.BehaviorName`.
- `bool TryGetFactory(string behaviorName, out IBehaviorGizmoFactory factory)`.

**`BehaviorGizmoManagerSystem`:**
- `[UpdateInPhase(SystemPhase.PostSimulation)]`
- Drains `view.ReadManagedEvents<AssignBehaviorEvent>()` for startup.
- Drains `view.ReadEvents<ClearBehaviorEvent>()` for teardown.
- Drains `view.ReadEvents<DestructionOrder>()` for entity death teardown.
- Same two-mode visibility pattern as `DataDrivenGizmoSystem` (selection-only vs global force).
- Uses `IBehaviorGizmoFactory.Rent()` / `Return()` for zero-allocation pooling.

**Constraints:**
- `AssignBehaviorEvent` is a managed class; use `ReadManagedEvents<AssignBehaviorEvent>()`.
- `ClearBehaviorEvent` is an unmanaged struct; use `ReadEvents<ClearBehaviorEvent>()`.
- One entity can have at most one active behavior gizmo at a time (the system replaces any
  existing gizmo when a new `AssignBehaviorEvent` arrives for the same entity).

**Success conditions:**
- SC-GZ006-1: `AssignBehaviorEvent` for a registered behavior name activates the gizmo instance
  on that entity; `OnInitialize` is called with the correct entity.
- SC-GZ006-2: `ClearBehaviorEvent` triggers `OnTeardown`; the instance is returned to the factory.
- SC-GZ006-3: `DestructionOrder` also triggers `OnTeardown` for any active behavior gizmo.
- SC-GZ006-4: A new `AssignBehaviorEvent` for an entity that already has an active behavior gizmo
  tears down the old gizmo first, then initializes the new one.
- SC-GZ006-5: `AssignBehaviorEvent` for an unregistered behavior name is silently ignored.
- SC-GZ006-6 (negative): `IBehaviorGizmoFactory.Rent()` is called on activation;
  `Return()` is called on teardown (verified via a mock factory with call counts).

---

## Phase 3: Settings Store

---

### TASK-GZ007 — GizmoSettingValue and GizmoSettingsRegistry

**Design reference:** DESIGN.md §3.1–3.2

**Scope:**
The zero-allocation settings store (registry + value type). Does NOT include persistence or
change events (GZ008).

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/`:
- `GizmoSettingValue.cs`
- `GizmoSettingsRegistry.cs`

**`GizmoSettingValue`:**
```csharp
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct GizmoSettingValue : IEquatable<GizmoSettingValue>
{
    [FieldOffset(0)] public SettingType Type;
    [FieldOffset(4)] public bool BoolValue;
    [FieldOffset(4)] public int IntValue;
    [FieldOffset(4)] public float FloatValue;

    public static GizmoSettingValue From(bool v) => new() { Type = SettingType.Bool, BoolValue = v };
    public static GizmoSettingValue From(int v) => new() { Type = SettingType.Int32, IntValue = v };
    public static GizmoSettingValue From(float v) => new() { Type = SettingType.Float32, FloatValue = v };
}

public enum SettingType : byte { Bool = 0, Int32 = 1, Float32 = 2 }
```

**`GizmoSettingsRegistry`:**
- `sealed class`.
- `void RegisterSetting(string keyName, GizmoSettingValue defaultValue)` — adds to both
  `_active` and `_defaults` if not already present. If already registered, only updates `_defaults`
  if the default value changed (migration support).
- `GizmoSettingValue Read(uint keyHash)` — returns `_active[keyHash]` or `default` if missing.
- `void Write(uint keyHash, GizmoSettingValue value)` — updates `_active`, marks dirty for
  persistence. Does NOT publish event (that is GZ008's responsibility).
- `void ResetToDefault(uint keyHash)` — copies default into active, marks as clean.
- `static uint ComputeHash(string name)` — FNV-1a 32-bit:
  `uint h = 2166136261; foreach(char c in name) { h ^= c; h *= 16777619; } return h;`
- `IEnumerable<(string Key, GizmoSettingValue Active, GizmoSettingValue Default)> EnumerateAll()` —
  cold path, for UI display and persistence.

**Constraints:**
- Hot-path `Read` and `Write` methods must not allocate.
- `ComputeHash` is a `public static` method so systems can precompute hashes at construction time.
- Registry is NOT thread-safe; reads and writes happen on the ECS execute thread only.

**Success conditions:**
- SC-GZ007-1: `RegisterSetting("NavMesh.ShowGrid", GizmoSettingValue.From(false))` then
  `Read(ComputeHash("NavMesh.ShowGrid"))` returns a value with `BoolValue == false`.
- SC-GZ007-2: `Write(hash, GizmoSettingValue.From(true))` followed by
  `Read(hash)` returns `BoolValue == true`.
- SC-GZ007-3: `ResetToDefault(hash)` after a write restores the original default.
- SC-GZ007-4: `Read` for an unregistered hash returns `default(GizmoSettingValue)` (no exception).
- SC-GZ007-5: Two different string keys with the same hash collision are stored separately.
  (Use test strings that are known to collide or verify hash uniqueness property.)
- SC-GZ007-6: `GizmoSettingValue.From(3.14f)` stores and reads back `3.14f` via `FloatValue`
  without corruption.
- SC-GZ007-7: `Marshal.SizeOf<GizmoSettingValue>() == 8`.

---

### TASK-GZ008 — Settings Persistence and Change Events

**Design reference:** DESIGN.md §3.3

**Scope:**
Disk save/load and the `GizmoSettingChangedEvent` notification mechanism.

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/`:
- `GizmoSettingChangedEvent.cs`
- `GizmoSettingsPersistence.cs` (static helper class)

Modify `GizmoSettingsRegistry` (from GZ007) to:
- Add `event Action<uint>? OnSettingChanged` (optional, cold path only).
- Change `Write` to accept an optional `IEntityCommandBuffer? cmd` param; when provided, call
  `cmd.PublishEvent(new GizmoSettingChangedEvent { KeyHash = keyHash })`.
- Add `bool IsDirty` property.

**`GizmoSettingChangedEvent`:**
```csharp
[EventId(8050)]
public struct GizmoSettingChangedEvent
{
    public uint KeyHash;
}
```
EventId 8050 must be verified against existing `[EventId]` usages in the codebase to avoid collision.
Check `FDP/Toolkits/Fdp.Toolkits` for the highest assigned EventId. If 8050 is taken, use the next
available ID.

**`GizmoSettingsPersistence`:**
- `static void SaveOverrides(GizmoSettingsRegistry registry, string filePath)` — enumerates only
  values where `active != default` AND `active != defaultValue`; writes JSON object with string keys
  and typed values. Uses `System.Text.Json.JsonSerializer`.
- `static void LoadOverrides(GizmoSettingsRegistry registry, string filePath)` — reads JSON;
  calls `registry.Write(hash, value)` for each key. If file does not exist, silently returns.

**Constraints:**
- `SaveOverrides` writes only user-changed settings, not all registered defaults. This keeps files
  small and immune to configuration drift.
- `LoadOverrides` must call `registry.RegisterSetting(key, defaultValue)` if a key from the file
  is not yet registered (forward-compatibility for settings loaded before modules register).
- EventId 8050 block is reserved for the diagnostics subsystem. Verify no collision.

**Success conditions:**
- SC-GZ008-1: `SaveOverrides` creates a valid JSON file. `LoadOverrides` on that file restores all
  saved overrides. After load, `registry.Read(hash) == savedValue` for each saved key.
- SC-GZ008-2: Settings at their default values are NOT written to disk (file does not contain them).
- SC-GZ008-3: `LoadOverrides` with a missing file does not throw.
- SC-GZ008-4: After `Write(hash, newValue, cmd)` with a non-null command buffer, draining
  `view.ReadEvents<GizmoSettingChangedEvent>()` yields an event with `KeyHash == hash`.
- SC-GZ008-5 (negative): After `ResetToDefault(hash)`, a subsequent `SaveOverrides` does NOT
  include that key in the output file.

---

## Phase 4: Interactive Input Routing

---

### TASK-GZ009 — Backend-Neutral Interaction Events

**Design reference:** DESIGN.md §4.2

**Scope:**
The four interaction event structs only. No proxy tool, no systems.

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/`:
- `GizmoInteractionEvents.cs` (all four in one file)

```csharp
[EventId(8051)] public struct GizmoInteractionStartedEvent { public PickToken Token; public Vector3 WorldPos; }
[EventId(8052)] public struct GizmoDragUpdateEvent          { public PickToken Token; public Vector3 WorldPos; }
[EventId(8053)] public struct GizmoInteractionCommitEvent   { public PickToken Token; public Vector3 WorldPos; }
[EventId(8054)] public struct GizmoInteractionCancelEvent   { public PickToken Token; }
```

**Constraints:**
- EventIds 8051–8054 reserved for gizmo interaction. Verify no collision with existing EventIds.
- All structs must be unmanaged (blittable) — `PickToken` contains `Entity` (blittable) and `uint`.
- `Vector3` is `System.Numerics.Vector3` (already used throughout the engine).

**Success conditions:**
- SC-GZ009-1: All four structs satisfy `where T : unmanaged` constraint (compilable with
  `view.ReadEvents<GizmoInteractionStartedEvent>()`).
- SC-GZ009-2: Publishing `GizmoDragUpdateEvent` to `FdpEventBus` and reading it back in the next
  frame yields the same `Token` and `WorldPos`.

---

### TASK-GZ010 — GizmoInteractionProxyTool

**Design reference:** DESIGN.md §4.3

**Scope:**
The `IMapTool` adapter that captures local 2D map input and routes it to the simulation layer.
Lives in `Fdp.Presentation`.

**Files to create** in `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/`:
- `GizmoInteractionProxyTool.cs`

**Constraints:**
- `sealed class : IMapTool`; namespace `Fdp.Toolkit.Vis2D.Gizmos`.
- Constructor: `GizmoInteractionProxyTool(PickToken token, FdpEventBus eventBus)`.
- `OnEnter(MapCanvas canvas)` saves reference to `_canvas`.
- `OnExit()` clears `_canvas`.
- `Update(float dt)` — no-op.
- `Draw(RenderContext ctx)` — no-op (the debug renderer draws the primitives, not the tool).
- `HandleDrag(Vector2 worldPos, Vector2 delta)`:
  - Publishes `GizmoDragUpdateEvent { Token = _token, WorldPos = new Vector3(worldPos.X, worldPos.Y, 0) }`.
  - Returns `true` to consume input (prevents map pan).
- `HandleHover(Vector2 worldPos)` — returns `true` (consume hover while focused).
- `HandleClick(Vector2 worldPos, MouseButton button)`:
  - Left button, mouse released → publish `GizmoInteractionCommitEvent`, call `_canvas.PopTool()`, return `true`.
  - Right button → publish `GizmoInteractionCancelEvent`, call `_canvas.PopTool()`, return `true`.
  - Left button pressed, not on our primitive (click-away) → publish `GizmoInteractionCancelEvent`,
    call `_canvas.PopTool()`, return `false` (yield click to underlying tool).
- `HandleKeyPressed(KeyboardKey key)`:
  - `Escape` → publish `GizmoInteractionCancelEvent`, call `_canvas.PopTool()`, return `true`.
  - Other keys → return `false`.
- Name property: `"GizmoInteractionProxy"`.

**Click-away detection:** `GizmoInteractionProxyTool` does NOT perform hit testing itself; that
logic is in `DebugGizmoLayer.HandleInput` (GZ013). For click-away, the proxy always returns `false`
on a left-click-pressed that is not a drag continuation. The `DebugGizmoLayer` will re-evaluate the
click and potentially push a new proxy if another primitive was hit.

**Constraints:**
- Must compile in `Fdp.Presentation` which references `Fdp.Toolkits` (and thus the gizmo events).
- `FdpEventBus` is in `Fdp.Core`. `IMapTool`, `MapCanvas`, `RenderContext` are in `Fdp.Presentation`.

**Success conditions:**
- SC-GZ010-1: `HandleDrag` publishes `GizmoDragUpdateEvent` with the correct `WorldPos`.
- SC-GZ010-2: Right-click publishes `GizmoInteractionCancelEvent` and pops the tool stack.
- SC-GZ010-3: Escape key publishes `GizmoInteractionCancelEvent` and pops the tool stack.
- SC-GZ010-4: Left-click-commit publishes `GizmoInteractionCommitEvent` and pops the tool stack.
- SC-GZ010-5: Click-away returns `false` (allows underlying tool to process the click).
- SC-GZ010-6 (negative): Other keys (e.g., 'A') return `false` from `HandleKeyPressed`.

---

## Phase 5: 2D Presentation Adapter

---

### TASK-GZ011 — DebugPrimitiveRenderer2D

**Design reference:** DESIGN.md §5.1

**Scope:**
The Raylib-based renderer that iterates a span of `DebugPrimitive` and issues draw calls.
Does NOT include EntityLocal resolution (GZ012) or integration into `DebugGizmoLayer` (GZ013).

**Files to create** in `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/`:
- `DebugPrimitiveRenderer2D.cs`

**Class structure:**
```csharp
public sealed class DebugPrimitiveRenderer2D
{
    private ushort _activeLayerMask = 0xFFFF; // All layers visible by default
    private readonly ISimulationView? _view;   // For EntityLocal resolution (GZ012)

    public DebugPrimitiveRenderer2D(ISimulationView? view = null) { _view = view; }

    public void SetLayerMask(ushort mask) => _activeLayerMask = mask;

    public void Render(ReadOnlySpan<DebugPrimitive> primitives, RenderContext ctx)
    {
        // Stable sort by (DebugLayer, ZIndex) before dispatch (Painter's Algorithm)
        // Use a small stackalloc sort buffer or Array.Sort on a rented temp array.
        float zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;
        foreach (ref readonly var prim in primitives)
        {
            if ((prim.TargetView & PipelineTarget.Map2D) == 0) continue;
            if ((prim.DebugLayer >= 16) || (_activeLayerMask & (1u << prim.DebugLayer)) == 0) continue;
            // LOD zoom culling
            if (prim.MinZoomLod != 0 && zoom < prim.MinZoomLod * 0.25f) continue;
            if (prim.MaxZoomLod != 0 && zoom > prim.MaxZoomLod * 0.25f) continue;
            DispatchShape(in prim, ctx);
        }
    }

    private void DispatchShape(in DebugPrimitive prim, RenderContext ctx) { ... }
}
```

**Shape dispatch** (implement at minimum):
- `Line`: Resolve `LineStart`/`LineEnd` through spatial projection (§5.2 logic, GZ012). If
  `EndColor != Color` (gradient), call a helper to draw a thick gradient quad (see below). Otherwise
  call `Raylib.DrawLineEx`.
- `Sphere`: Project `SphereCenter` to 2D; draw `Raylib.DrawCircleV`.
- `Arrow`: Draw line + arrowhead polygon.
- `Text`: Draw `Raylib.DrawText` at resolved position.
- Unknown/unsupported shapes: silently skip.

**Gradient rendering:** For `Line` primitives with different start/end colors, synthesize a
textured quad using `Rlgl.Begin/End` with 4 vertices:
- Two start-side vertices with `prim.Color`.
- Two end-side vertices with `prim.EndColor`.
The quad normal is the perpendicular of the line direction. This isolates Raylib's limitation from
the simulation layer.

**`SizeMode` scaling:**
- `ScreenPixels`: `float t = prim.Thickness / (ctx.Zoom > 0f ? ctx.Zoom : 1f)`
- `WorldMeters`: `float t = prim.Thickness`

**Constraints:**
- All rendering must happen with the Raylib camera matrix active (world-space coordinates are
  passed to Raylib directly in meters).
- For `Screen` coordinate space, the caller (GZ013) must push/pop a screen-space mode before/after
  the relevant primitives. The renderer itself does not manage camera state.
- No managed heap allocations per primitive. Use `stackalloc` for any temporary vertex arrays.

**Success conditions:**
- SC-GZ011-1: A `Line` primitive with `TargetView = None` is silently skipped (no draw call).
- SC-GZ011-2: A `Line` primitive on layer 5 is rendered when `_activeLayerMask` has bit 5 set.
- SC-GZ011-3: A `Line` primitive on layer 5 is skipped when `_activeLayerMask` does NOT have bit 5 set.
- SC-GZ011-4 (manual verification): Rendering a gradient `Line` (different start/end colors)
  visually shows alpha interpolation from one end to the other.
- SC-GZ011-5: `SizeMode.ScreenPixels` with zoom=2.0 renders with half the `ThicknessU16 * 0.1f`
  value (verified via a Raylib draw call capture or mock).
- SC-GZ011-6: Two primitives on the same `DebugLayer` but with different `ZIndex` values (0 and 1)
  are rendered in ascending `ZIndex` order (ZIndex=0 first, ZIndex=1 on top). Verified by capturing
  draw call order from a mock renderer.
- SC-GZ011-7: A primitive with `MinZoomLod = 8` (threshold 2.0f) is skipped when `ctx.Zoom = 1.0f`
  and rendered when `ctx.Zoom = 3.0f`.
- SC-GZ011-8: A primitive with `MaxZoomLod = 8` (threshold 2.0f) is rendered when `ctx.Zoom = 1.0f`
  and skipped when `ctx.Zoom = 3.0f`.
- SC-GZ011-9: A primitive with `MinZoomLod = 0` and `MaxZoomLod = 0` is never culled by zoom
  (both limits inactive).

---

### TASK-GZ012 — Spatial Projection (CoordinateSpace + SizeMode)

**Design reference:** DESIGN.md §5.2

**Scope:**
Extends `DebugPrimitiveRenderer2D` with full coordinate resolution for `EntityLocal` and
`Screen` spaces, and SizeMode-aware thickness.

**Modify** `DebugPrimitiveRenderer2D.cs` from GZ011:
- `EntityLocal` resolution: before calling `DispatchShape`, if `prim.Space == EntityLocal`:
  - Access `_view.IsAlive(anchor)` and `_view.HasComponent<SimTransform>(anchor)`.
  - If either fails, skip the primitive.
  - Read `ref readonly var tf = ref _view.GetComponentRO<SimTransform>(anchor)`.
  - Transform `LocalStart`: `worldStart = tf.Position + Vector3.Transform(localStart, tf.Rotation)`.
  - For `Line` shape: also transform `LocalEnd`.
- `Screen` space: set a flag before `DispatchShape`. The dispatch method calls `Rlgl.LoadIdentity()`
  temporarily to bypass the camera transform, then calls `Rlgl.LoadProjectionMatrix(screenOrtho)`.
  (Or use `Raylib.EndMode2D()` / `Raylib.BeginMode2D()` to bracket screen-space primitives.)
- For `Text` shape with `Screen` space: use screen-pixel coordinates directly with `Raylib.DrawText`.

**Note on `Screen` space implementation:** The current Raylib camera in the engine is a 2D
`Camera2D`. To draw in screen space, call `Raylib.EndMode2D()` before the screen-space draw call
and `Raylib.BeginMode2D(ctx.Camera)` after. The renderer is responsible for this transition.
Group screen-space primitives at the end of the render loop to minimize camera matrix switches.

**Success conditions:**
- SC-GZ012-1: An `EntityLocal` line primitive tracks the entity's world position. When the
  entity moves (simulated by updating `SimTransform`), the primitive renders at the new location.
- SC-GZ012-2: An `EntityLocal` primitive for a dead (non-alive) entity is silently skipped.
- SC-GZ012-3: A `Screen`-space `Text` primitive at `(10, 10)` renders at the top-left corner
  of the screen, regardless of camera zoom or pan.
- SC-GZ012-4: A `World + ScreenPixels` line renders at the correct world position but maintains
  constant pixel thickness as the camera zoom changes (verified by checking `Raylib.DrawLineEx`
  arguments).

---

### TASK-GZ013 — DebugGizmoLayer Integration

**Design reference:** DESIGN.md §5.3

**Scope:**
Wire `DebugPrimitiveRenderer2D` into the existing `DebugGizmoLayer`, and wire hit-testing for
interactive gizmos. Update (not replace) the existing `DebugGizmoLayer.cs`.

**Modify** `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`:
- Add `DebugPrimitiveBuffer _buffer` field (injected or shared reference).
- Add `DebugPrimitiveRenderer2D _renderer` field.
- Add `FdpEventBus _eventBus` field.
- `Draw(RenderContext ctx)`: replace the `// Debug gizmos not implemented yet` comment with:
  ```csharp
  var primitives = _buffer.GetFrame();
  _renderer.Render(primitives, ctx);
  ```
- `HandleInput(Vector2 worldPos, MouseButton button, bool isPressed)`:
  When `isPressed && button == MouseButton.Left`, iterate `_buffer.GetFrame()` to find the closest
  pickable primitive (one with `Token.IsValid`) within a configurable hit radius. If found, push a
  new `GizmoInteractionProxyTool` onto `_canvas` and publish `GizmoInteractionStartedEvent`.

**Constructor** update: `DebugGizmoLayer(int layerBitIndex, DebugPrimitiveBuffer buffer,
FdpEventBus eventBus, ISimulationView view)`.

**Constraints:**
- `PickEntity(Vector2 worldPos)` — `DebugGizmoLayer.PickEntity` can return `null` for now;
  entity selection is handled by the existing `EntityRenderLayer`.
- Hit radius: default 5 pixels. Convert to world units using `ctx.Zoom`.
- When multiple pickable primitives overlap, pick the one with the highest `DebugLayer` value
  (topmost rendered layer).

**Success conditions:**
- SC-GZ013-1: After the `DataDrivenGizmoSystem` populates the buffer, `DebugGizmoLayer.Draw`
  renders the primitives (integration: confirm no exception; visual verification manual).
- SC-GZ013-2: Clicking within hit radius of a pickable primitive pushes a
  `GizmoInteractionProxyTool` onto the `MapCanvas` stack.
- SC-GZ013-3: Clicking outside any pickable primitive does NOT push the proxy tool.
- SC-GZ013-4: The layer respects `ctx.VisibleLayersMask & (1u << LayerBitIndex)` — if the
  layer's own bit is off, neither rendering nor hit-testing occurs.

---

### TASK-GZ014 — Entity Badge and Rich Text Rendering

**Design reference:** DESIGN.md §5.4

**Scope:**
Rendering of `EntityBadge` primitives with rich text control code support.

**Files to create** in `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/`:
- `RichTextRenderer.cs`

**Modify** `DebugPrimitiveRenderer2D.DispatchShape` to handle `EntityBadge`:
- Collect all `EntityBadge` primitives for a given entity (aggregate step, see constraints).
- For each badge, call `RichTextRenderer.DrawRichTextBadge`.

**`RichTextRenderer`:**
`static class` with:
```csharp
public static unsafe void DrawRichTextBadge(
    ref FixedString32 text,
    int screenX, int screenY,
    int fontSize)
```
- Iterates raw bytes of `FixedString32` using `MemoryMarshal` / unsafe span techniques modelled
  after the existing `LogSyntaxHighlighter` span-slicing pattern in the codebase.
- Control bytes:
  - `0x01` = Red (`Raylib_cs.Color.Red`)
  - `0x02` = Green (`Raylib_cs.Color.Green`)
  - `0x03` = Yellow (`Raylib_cs.Color.Yellow`)
  - `0x04` (or any other byte) = White/default
- When a control byte is encountered: flush the current monochrome text chunk with `Raylib.DrawText`,
  switch active color, advance X cursor by measured width.
- Uses `stackalloc byte[32]` for null-terminated chunk buffer to avoid heap allocation.

**Badge rendering location:**
Badges are drawn at the entity's screen position offset below the entity's label. The renderer
must resolve the entity's world position (via `SimTransform` read from `_view`) and convert to
screen coordinates via `Raylib.GetWorldToScreen2D` (or equivalent). Stack badges vertically with
a configurable line height (default 14 pixels).

**Constraint:** Gizmo code must never write to `ResolvedStyle._labelText`. Badges are strictly
additive overlays.

**Zero-allocation parsing constraint (from D-004):** `RichTextRenderer` must NOT allocate a
`List<>` (or any heap object) per draw call. Parse the `FixedString32` bytes using
`ReadOnlySpan<byte>` iteration or a `stackalloc byte[32]` chunk buffer. If a temporary collection
is needed during parsing, use `stackalloc` or a fixed-size stack-allocated struct array — never
`new List<>()` on the rendering hot path.

**Layout-safety constraint (from D-006):** Add the following assertion to `RichTextRenderer`'s
static constructor (or to the enclosing class's `TypeInitializer`) to catch FixedString32 layout
changes at program startup rather than silently corrupting data at runtime:
```csharp
static RichTextRenderer()
{
    Debug.Assert(
        Unsafe.SizeOf<FixedString32>() == 32,
        "FixedString32 layout changed — update RichTextRenderer byte-level parsing.");
}
```

**Success conditions:**
- SC-GZ014-1: A `FixedString32` with bytes `[0x01, 'H', 'i', 0x02, '!', 0x00]` renders "Hi" in
  red and "!" in green.
- SC-GZ014-2: A badge with no control bytes renders entirely in the default color.
- SC-GZ014-3: Two badges for the same entity render on separate lines (Y offset per badge).
- SC-GZ014-4 (negative): A badge primitive for an entity without a `SimTransform` is silently
  skipped (no exception).
- SC-GZ014-5: `RichTextRenderer` produces no heap allocations per call (verify via allocation test).
- SC-GZ014-6: The static constructor `Debug.Assert(Unsafe.SizeOf<FixedString32>() == 32)` is
  present and fires if the size changes (verified by temporarily changing the value in a test and
  confirming the assertion triggers).

---

## Phase 6: Remote Visualization Foundation

---

### TASK-GZ015 — GlobalDebugSettings ECS Singleton

**Design reference:** DESIGN.md §2.4

**Scope:**
The `GlobalDebugSettings` unmanaged ECS singleton that the gizmo systems read to switch modes.

**Files to create** in `Hrot/Subsystems/Hrot.IG/Gizmos/`:
- `GlobalDebugSettings.cs`

```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(/* choose an available ID in the 160-199 application range */)]
[DataPolicy(DataPolicy.Transient)]
public struct GlobalDebugSettings
{
    [MarshalAs(UnmanagedType.I1)] public bool ForceAllGizmosVisible;
    public ushort DebugLayerMask; // 16 bits for layer 0-15; default 0xFFFF (all on)
}
```

The component ID must be chosen from an available slot in the application descriptor range
(160–199). Verify against `GlobalComponentIds` in the codebase before assigning.

Also create an `ImGui`-based settings panel snippet (or document the expected UI hook) so that
the IG operator can toggle `ForceAllGizmosVisible` and the `DebugLayerMask` bits at runtime.

**Success conditions:**
- SC-GZ015-1: `repo.HasSingleton<GlobalDebugSettings>()` returns `true` after the singleton is
  set during IG startup.
- SC-GZ015-2: `DataDrivenGizmoSystem` reads `ForceAllGizmosVisible` and switches execution modes
  correctly (covered by SC-GZ005-4).
- SC-GZ015-3: `GlobalDebugSettings` is not persisted or recorded (`DataPolicy.Transient`).

---

### TASK-GZ016 — DebugPrimitivesBatch DDS Topic

**Design reference:** DESIGN.md §6.1

**Scope:**
DDS topic definition for remote transport of the primitive stream.
Out-of-scope: wire-up to the simulation pipeline (that is a future batch).

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/`:
- `DebugPrimitivesBatch.cs`

```csharp
[DdsTopic("DebugPrimitivesBatch")]
[DdsQos(Reliability = DdsReliability.BestEffort,
        Durability = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
public partial struct DebugPrimitivesBatch
{
    [DdsKey] public uint FrameNumber;
    [DdsKey] public byte NodeId;     // Source simulation node
    [DdsManaged] public DebugPrimitive[] Primitives;
}
```

**Constraints:**
- `DebugPrimitive` is a blittable struct — DDS can serialize it as a fixed-size byte sequence.
- `[DdsManaged]` is used because the array length is dynamic per frame.
- Topic name `"DebugPrimitivesBatch"` must not collide with existing DDS topics. Verify against
  existing DDS topic definitions in `Hrot.Network.*` and `FDP.Network.*` projects.

**Success conditions:**
- SC-GZ016-1: The DDS schema compiles without errors (partial class code generation passes).
- SC-GZ016-2: `DebugPrimitivesBatch` can be serialized and deserialized with a round-trip test
  preserving all 64 bytes of each `DebugPrimitive` in `Primitives`.

---

### TASK-GZ017 — GizmoSettingsPublisherSystem and GizmoUiState DDS Topics

**Design reference:** DESIGN.md §6.2–6.3

**Scope:**
Define the two settings-related DDS topics and a stub system that rebuilds the StructEdit schema
when settings change. This is the "side-channel" that enables remote clients to render
`ImGuiPropertyTree` panels for gizmo settings without sharing C# assemblies.

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/`:
- `GizmoUiState.cs`

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/`:
- `GizmoSettingsPublisherSystem.cs`

**`GizmoUiState` DDS topic:**
```csharp
[DdsTopic("GizmoUiState")]
[DdsQos(Reliability = DdsReliability.Reliable,
        Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
public partial struct GizmoUiState
{
    [DdsKey] public uint GizmoInstanceId;
    [DdsManaged] public string EditDocumentJson;
}
```

Used by `DrawComponentInspector` (Phase 5) to carry gizmo-specific DTO schemas keyed by
`GizmoInstanceId`. Remote clients match the ID from the `ComponentInspector` primitive's
`InspComponentTypeId` field and render the property grid.

**`GizmoSettingsPublisherSystem`:**
- `[UpdateInPhase(SystemPhase.PostSimulation)]`
- On first frame (dirty flag) and whenever `view.ReadEvents<GizmoSettingChangedEvent>()` yields
  events: enumerate `GizmoSettingsRegistry.EnumerateAll()`, build a minimal JSON object (name →
  value pairs), publish as a `GizmoUiState` record with a reserved `GizmoInstanceId = 0`
  (global settings slot).
- Uses `System.Text.Json.JsonSerializer` for serialization — same as `GizmoSettingsPersistence`.
- Publication is via the DDS writer injected at construction time. If no DDS writer is available
  (local-only mode), the system is a no-op.

**Constraints:**
- `GizmoUiState` topic name must not collide with existing DDS topics.
- The publisher must not re-publish if settings are unchanged between frames (use `IsDirty` flag
  from `GizmoSettingsRegistry`).
- System is optional: it must not be a hard startup requirement; local-only deployments skip it.

**Success conditions:**
- SC-GZ017-1: The `GizmoUiState` DDS schema compiles without errors.
- SC-GZ017-2: After calling `registry.Write(hash, value)`, `GizmoSettingsPublisherSystem.Execute`
  publishes a `GizmoUiState` record with `GizmoInstanceId == 0` and non-empty `EditDocumentJson`.
- SC-GZ017-3: If no `GizmoSettingChangedEvent` is in the bus and `IsDirty == false`, the system
  does NOT publish (verified by asserting zero DDS writer calls in that frame).
- SC-GZ017-4: `GizmoUiState` round-trips (serialize + deserialize) with `GizmoInstanceId` and
  `EditDocumentJson` preserved.

---

### TASK-GZ018 — IGCapabilitiesAnnounce DDS Message

**Design reference:** DESIGN.md §6.4

**Scope:**
The terminal capability announcement message that IG (Image Generator) clients publish on startup.
Allows the simulation node to tailor its output to each connected viewer's actual capabilities.

**Files to create** in `Hrot/Subsystems/Hrot.IG/Gizmos/`:
- `IGCapabilitiesAnnounce.cs`
- `IGCapabilitiesPublisherSystem.cs`

**`IGCapabilitiesAnnounce` DDS topic:**
```csharp
[DdsTopic("IGCapabilitiesAnnounce")]
[DdsQos(Reliability = DdsReliability.Reliable,
        Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
public partial struct IGCapabilitiesAnnounce
{
    [DdsKey] public uint NodeId;            // Matches DebugPrimitivesBatch.NodeId
    public PipelineTarget SupportedTargets; // Which pipelines this terminal supports
    public ushort SupportedLayerMask;       // Which DebugLayers this terminal renders
    public byte SupportedShapes;            // Bitmask of DebugPrimitiveShape values supported
    [DdsManaged] public string LayerNamesJson; // Optional: JSON array of user-visible layer names
}
```

**`IGCapabilitiesPublisherSystem`:**
- `[UpdateInPhase(SystemPhase.Initialization)]` — publishes once at startup.
- **Must NOT use hardcoded capability values.** Instead, it must reflect over `GizmoRegistry` and
  `StatelessGizmoRegistry` to dynamically compute the capabilities of this terminal at the moment
  of startup, so that the backend can tailor its output without any frontend coupling.

**Reflection logic (`BuildCapabilitiesJson`):**
```csharp
private string BuildCapabilitiesJson(GizmoRegistry stateful, StatelessGizmoRegistry stateless)
{
    // Collect all component type IDs across all registered rules.
    var requiredComponents = new HashSet<Type>();
    foreach (var rule in stateful.Rules)
        foreach (var t in rule.Definition.RequiredComponents)
            requiredComponents.Add(t);
    foreach (var rule in stateless.Rules)
        foreach (var t in rule.RequiredTypes)       // RequiredTypes: Type[] stored on CompiledStatelessRule
            requiredComponents.Add(t);

    // Collect all supported gizmo class names for schema documentation.
    var gizmoNames = new List<string>();
    foreach (var rule in stateful.Rules)
        gizmoNames.Add(rule.Definition.GetType().Name);
    foreach (var rule in stateless.Rules)
        gizmoNames.Add(rule.Projector.GetType().Name);

    using var ms = new MemoryStream();
    using var w = new Utf8JsonWriter(ms);
    w.WriteStartObject();
    w.WriteString("nodeType", "IG");
    w.WriteString("nodeVersion", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0");
    w.WriteStartArray("registeredGizmos");
    foreach (var name in gizmoNames) w.WriteStringValue(name);
    w.WriteEndArray();
    w.WriteStartArray("requiredComponents");
    foreach (var t in requiredComponents) w.WriteStringValue(t.FullName);
    w.WriteEndArray();
    w.WriteEndObject();
    w.Flush();
    return Encoding.UTF8.GetString(ms.ToArray());
}
```

Constructor: `IGCapabilitiesPublisherSystem(GizmoRegistry statefulRegistry,
StatelessGizmoRegistry statelessRegistry, IDdsWriter<IGCapabilitiesAnnounce> writer, byte nodeId)`.

On `Execute` (Initialization phase):
1. Call `BuildCapabilitiesJson` to derive actual capabilities from the registries.
2. Publish:
   ```csharp
   _writer.Write(new IGCapabilitiesAnnounce
   {
       NodeId          = _nodeId,
       SupportedTargets = PipelineTarget.Map2D,
       SupportedLayerMask = 0xFFFF,
       SupportedShapes  = 0xFF,
       LayerNamesJson   = BuildCapabilitiesJson(_stateful, _stateless),
   });
   ```
   `LayerNamesJson` is repurposed to carry the full reflection-derived schema (the field name is
   a misnomer from the original schema; it carries any opaque JSON blob).

**Note:** The simulation node receiving these announcements can use them as an optimization hint
to skip emitting `Viewport3D`-only primitives when no 3D client is connected. However, the
simulation node must continue emitting `All`-target primitives regardless (clients silently drop
unsupported shapes). This is a **best-effort optimization** path, not a hard requirement.

**Constraints:**
- `IGCapabilitiesAnnounce` topic name must not collide with existing DDS topics.
- System runs once; does not re-publish each frame.
- The JSON content of `LayerNamesJson` must include at minimum the `registeredGizmos` array and
  `requiredComponents` array. Downstream ExCon UI renders a capabilities table from this data.
- `CompiledStatelessRule` must expose `Type[] RequiredTypes` (add this field to the struct if
  not already present, storing the original `Type[]` passed to `StatelessGizmoRegistry.Register`).
- No hardcoded capability values (`SupportedTargets`, `SupportedLayerMask`, `SupportedShapes`
  have hardcoded constants because these represent the fixed rendering backend capability of the
  local 2D IG client, NOT the registered gizmos — the gizmo list is reflected dynamically).

**Success conditions:**
- SC-GZ018-1: `IGCapabilitiesAnnounce` DDS schema compiles without errors.
- SC-GZ018-2: `IGCapabilitiesPublisherSystem` publishes exactly one `IGCapabilitiesAnnounce` record
  during the `Initialization` phase (verified by asserting exactly one DDS writer call).
- SC-GZ018-3: A record with `SupportedTargets = Map2D` and `SupportedLayerMask = 0xFFFF` is
  serialized and deserialized correctly in a round-trip test.
- SC-GZ018-4: When two gizmos are registered in `StatelessGizmoRegistry`, the emitted
  `LayerNamesJson` contains both gizmo class names in the `registeredGizmos` array.
- SC-GZ018-5: When zero gizmos are registered (empty registries), `BuildCapabilitiesJson` still
  returns valid JSON with empty `registeredGizmos` and `requiredComponents` arrays.
- SC-GZ018-6: Adding a new gizmo class to `StatelessGizmoRegistry` at startup automatically
  causes it to appear in the next `IGCapabilitiesAnnounce` without any manual edits to the
  publisher system (verified by adding a mock gizmo and asserting its name in the JSON).

---

## Phase 1 Extension: String Interning Side-Channel

---

### TASK-GZ019 — StringInternMap and DrawTextLong

**Design reference:** DESIGN.md §1.2 (String interning escape hatch)

**Scope:**
The managed side-channel that enables AI diagnostic text longer than 31 characters without
violating the 64-byte primitive constraint. Extends `DebugPrimitiveBuffer` with `DrawTextLong`
and adds `StringInternMap` for local-client resolution. Provides the network transport topic for
remote viewers.

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`:
- `StringInternMap.cs`

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/`:
- `StringInternBatch.cs`

**Modify** `DebugPrimitiveBuffer.cs` (from GZ003) to implement `DrawTextLong`.

**`StringInternMap`:**
```csharp
public sealed class StringInternMap
{
    private readonly Dictionary<uint, string> _map = new();

    // Called by DrawTextLong before emitting the primitive.
    // If hash already registered, silently skips (idempotent).
    public void Intern(uint hash, string fullText) { ... }

    // Called by the renderer when StringHash != 0.
    // Returns null if hash not present (renderer falls back to FixedString32 preview).
    public string? TryResolve(uint hash) { ... }

    // Returns all currently interned entries for network publication.
    public IReadOnlyDictionary<uint, string> Entries => _map;

    // Clears entries older than current frame (optional; GZ019 may leave this as no-op initially).
    public void Flush() { }
}
```

**`DrawTextLong` implementation** (in `DebugPrimitiveBuffer`):
1. Compute `hash = GizmoSettingsRegistry.ComputeHash(text)` (FNV-1a, reuse existing method).
   Note: using FNV-1a on the text content, not on a setting key name.
2. Call `_internMap.Intern(hash, text)` (idempotent).
3. Build a `DebugPrimitive` with `Shape = Text`, `Space` as provided.
4. Set `[FieldOffset(8)] StringHash = hash` (the overlay on `AnchorIndex`). `AnchorGeneration` is
   left as zero (not used for `Text` in non-EntityLocal space).
5. Copy the first 31 characters of `text` into `TextContent` (as a 31-char preview truncation).
6. Append the primitive to the buffer.

**`StringInternBatch` DDS topic:**
```csharp
[DdsTopic("StringInternBatch")]
[DdsQos(Reliability = DdsReliability.Reliable,
        Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
public partial struct StringInternBatch
{
    [DdsKey] public uint FrameNumber;
    [DdsManaged] public uint[] Hashes;
    [DdsManaged] public string[] Texts;
}
```

A publisher system (stub, runs in `PostSimulation`) publishes all entries in
`StringInternMap.Entries` whenever the map has new additions since the last frame.
Remote clients subscribe and populate their own `StringInternMap`.

**Constraints:**
- `DrawTextLong` IS allowed to call `Dictionary.ContainsKey` on the intern map (not hot path
  since it handles unbounded strings — if performance is a concern, callers switch to
  `DrawText(FixedString32, ...)` for short strings).
- `DrawText(FixedString32, ...)` must never write a non-zero `StringHash` (it is inline-only).
- `StringInternMap.TryResolve` must not allocate (dictionary lookup only).
- The `StringHash` overlay occupies bytes 8–11. The `AnchorGeneration` field at bytes 12–13
  must be explicitly zeroed when writing a `Text` primitive in non-EntityLocal space (the
  `MakeText` factory helper handles this).
- Topic name `"StringInternBatch"` must not collide with existing DDS topics.

**Renderer integration** (update `DebugPrimitiveRenderer2D.DispatchShape` in GZ011/GZ012):
When `Shape == Text` and `prim.StringHash != 0`:
- Call `_internMap?.TryResolve(prim.StringHash)`.
- If resolved: pass the full string to `Raylib.DrawText`.
- If null (hash not in local map): fall back to `prim.TextContent` (the 31-char preview).
`_internMap` is an optional constructor parameter; `null` = always use inline mode.

**Success conditions:**
- SC-GZ019-1: `DrawTextLong` with a 60-character string emits a `Text` primitive with
  `StringHash == FNV1a(text)` and `TextContent` containing the first 31 characters.
- SC-GZ019-2: `StringInternMap.TryResolve(hash)` returns the full 60-character string after
  `DrawTextLong` was called.
- SC-GZ019-3: `DrawTextLong` called twice with the same string emits the same `StringHash` and
  does NOT add a duplicate entry to the `StringInternMap`.
- SC-GZ019-4: `DrawText(FixedString32 text)` always emits a primitive with `StringHash == 0`.
- SC-GZ019-5: `StringInternBatch` DDS schema compiles without errors.
- SC-GZ019-6: A `StringInternBatch` with 3 hash/text pairs round-trips (serialize + deserialize)
  preserving all hashes and full strings.
- SC-GZ019-7: When the renderer has a `StringInternMap` and receives a `Text` primitive with
  `StringHash != 0`, it calls `Raylib.DrawText` with the full resolved string (not the 31-char
  preview). Verified via mock.
- SC-GZ019-8: When the renderer receives a `Text` primitive with `StringHash != 0` but the hash
  is absent from the intern map, it falls back to `TextContent` without throwing.

---

## Phase 8: Stateless Gizmo Execution Path

**Background:** The design defines a stateless gizmo taxonomy — pure projectors that read ECS
state each frame and emit primitives without any lifecycle management. The implementation missed
this entirely: every registered gizmo is forced through the `IStatefulGizmo` dictionary path.
`HealthBarGizmoInstance`, `EntityRotationGizmoInstance`, `VisibilityConeGizmoInstance`, and
`HillAttackGizmoInstance` all have empty `OnInitialize`/`OnTeardown` methods — they are logically
stateless but pay the full stateful object overhead every frame. These three tasks rectify that.

---

### TASK-GZ022 — IStatelessGizmo Contract and StatelessGizmoSystem

**Design reference:** DESIGN.md §2.1 (Statefulness taxonomy — Stateless row), feedback1.md

**Scope:**
Introduce the missing stateless execution path: the `IStatelessGizmo` interface, a companion
`StatelessGizmoRegistry`, and a `StatelessGizmoSystem` that executes bulk ECS queries instead of
per-entity dictionary lookups.

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`:
- `IStatelessGizmo.cs`
- `StatelessGizmoRegistry.cs`

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/`:
- `StatelessGizmoSystem.cs`

**`IStatelessGizmo`:**
```csharp
// Namespace: Fdp.Toolkit.Diagnostics.Gizmos
public interface IStatelessGizmo
{
    // Called once per entity per frame that matches the gizmo's component mask.
    // Must not retain any state between calls; all output goes through drawBuilder.
    void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder);
}
```

No `OnInitialize`, no `OnTeardown`, no object pooling. Pure projector contract.

**`StatelessGizmoRegistry`:**
`sealed class`. Holds `List<CompiledStatelessRule>` where:
```csharp
internal struct CompiledStatelessRule
{
    public IStatelessGizmo Projector;
    public BitMask256 RequiredMask;
    public IGizmoVisibilityPolicy VisibilityPolicy;
}
```

`void Register(IStatelessGizmo projector, Type[] requiredComponents,
               IGizmoVisibilityPolicy? visibilityPolicy = null)`:
- Converts `requiredComponents` to IDs via `ComponentTypeRegistry.GetId(Type)`.
- Throws `InvalidOperationException` if any ID is -1.
- Uses `AlwaysVisiblePolicy.Instance` when `visibilityPolicy` is null.
- Appends a `CompiledStatelessRule` to `Rules`.

`IReadOnlyList<CompiledStatelessRule> Rules` — read-only exposure.

**`StatelessGizmoSystem`:**
- `[UpdateInPhase(SystemPhase.PostSimulation)]`
- Constructor: `StatelessGizmoSystem(StatelessGizmoRegistry registry, IDebugDrawBuilder drawBuilder)`
- `Execute(ISimulationView view, float deltaTime)`:
  1. Cast `view` to `EntityRepository repo`.
  2. Evaluate global visibility cache for all rules once (same pattern as `DataDrivenGizmoSystem`).
  3. For each rule whose visibility policy is active, call
     `repo.Query().WithMask(rule.RequiredMask).Build()` and iterate matching entities.
     For each entity call `rule.Projector.Draw(view, entity, _drawBuilder)`.
  4. If `GlobalDebugSettings.ForceAllGizmosVisible` is false (selection mode), wrap the
     Draw call with a `SelectionState` check: `view.GetComponentRO<SelectionState>(e).IsSelected`.

The system must NOT maintain any per-entity dictionaries, rent objects from pools, or call
`OnInitialize`/`OnTeardown`. It is a stateless bulk scanner.

**Constraints:**
- `StatelessGizmoRegistry` is not thread-safe; `Register` is startup-only.
- The global visibility cache `bool[]` is allocated once at construction time (sized to
  `registry.Rules.Count`). Do not resize it; the registry is sealed after startup.
- `Draw` must be called with the entity alive. Guard with `view.IsAlive(entity)`.

**Success conditions:**
- SC-GZ022-1: `Register(projector, [typeof(SimTransform)])` compiles a rule with a `RequiredMask`
  having exactly the `SimTransform` component bit set.
- SC-GZ022-2: `Register` with an unregistered component type throws `InvalidOperationException`.
- SC-GZ022-3: `StatelessGizmoSystem.Execute` calls `projector.Draw` for every entity matching
  the required mask (verified with a mock projector that counts Draw invocations).
- SC-GZ022-4: An entity that does NOT have all required components does NOT trigger a Draw call.
- SC-GZ022-5: With `GlobalDebugSettings.ForceAllGizmosVisible = false`, only selected entities
  (IsSelected == true) trigger Draw calls.
- SC-GZ022-6: With `GlobalDebugSettings.ForceAllGizmosVisible = true`, all matching entities
  trigger Draw calls regardless of selection state.
- SC-GZ022-7: Global visibility cache is evaluated once per frame per rule, not once per entity.
  Verify by counting `IsGloballyEnabled` invocations on a mock policy with 100 entities.
- SC-GZ022-8: A projector registered with `NeverVisiblePolicy` never triggers Draw even in
  global-force mode.

---

### TASK-GZ023 — Migrate Pure-Projector Gizmos to Stateless and Correct Project Placement

**Design reference:** feedback1.md ("All the gizmos are now in Hrot.IG. But I need to use them
also in Hrot.SimHost, Hrot.CGF"), DESIGN.md §2.1

**Scope:**
Refactor the four pure-projector gizmos out of `Hrot.IG` and into their architecturally correct
home assemblies, converting them to implement `IStatelessGizmo` instead of `IStatefulGizmo`.
The `Hrot.IG` module keeps only the `GizmoRegistrar` call-site (no gizmo logic).

**Gizmos to migrate and their destination:**

| Gizmo | Current location | New location | Reason |
|-------|-----------------|--------------|--------|
| `HealthBarGizmo` | `Hrot.IG/Gizmos/` | `Hrot.Common/Diagnostics/Gizmos/` | References `IgHealthState` which lives in `Hrot.Core` — fully accessible from `Hrot.Common` |
| `EntityRotationGizmo` | `Hrot.IG/Gizmos/` | `Hrot.Common/Diagnostics/Gizmos/` | References `SimTransform` from `Fdp.Core` |
| `VisibilityConeGizmo` | `Hrot.IG/Gizmos/` | `Hrot.Common/Diagnostics/Gizmos/` | References `SimTransform` and `PerceptionReceptor` from `Hrot.Core` |
| `HillAttackGizmo` | `Hrot.IG/Gizmos/` | `Hrot.AI.Behaviors/Gizmos/` | References `PlatoonHillAttackParams`, `BrainBlackboard`, `BehaviorState` — all in `Hrot.AI.Behaviors` |

**For each migrated gizmo:**
1. Delete the `*Instance.cs` and `*Definition.cs` wrapper files from `Hrot.IG`.
2. Create `*Gizmo.cs` in the new project, implementing `IStatelessGizmo`:
   ```csharp
   [GizmoProjector(typeof(RequiredComponent1), typeof(RequiredComponent2))]
   public sealed class HealthBarGizmo : IStatelessGizmo
   {
       private readonly GizmoSettingsRegistry _settings;
       public HealthBarGizmo(GizmoSettingsRegistry settings) { _settings = settings; }

       public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder)
       {
           // Exact logic from the former HealthBarGizmoInstance.UpdateAndDraw —
           // no changes to rendering math; only the wrapper overhead is removed.
       }
   }
   ```
3. Remove `OnInitialize(ISimulationView view, Entity entity) { }` and `OnTeardown() { }` stubs.
4. Update `GizmoRegistrar` in `Hrot.IG` to call
   `statelessRegistry.Register(new HealthBarGizmo(settings), HealthBarGizmo.RequiredComponents)`
   instead of `registry.Register(new HealthBarGizmoDefinition(settings))`.

**Settings classes** (`HealthBarGizmoSettings`, `EntityRotationGizmoSettings`,
`HillAttackGizmoSettings`) move with the gizmo to the new project.

**`VisibilityConeGizmoDefinition`** had no settings; just delete the definition wrapper.

**Constraints:**
- Zero changes to the rendering math inside any gizmo's `Draw` method; only the class hierarchy
  changes (IStatelessGizmo replaces IStatefulGizmo + IGizmoDefinition).
- `Hrot.Common` and `Hrot.AI.Behaviors` do NOT reference `Hrot.IG`. The dependency is one-way:
  `Hrot.IG` references `Hrot.Common` and `Hrot.AI.Behaviors` for `GizmoRegistrar` call-sites.
- The existing tests in `Hrot.IG.Tests` that verify gizmo rendering output must be preserved;
  only the assembly reference changes from `Hrot.IG` to `Hrot.Common`/`Hrot.AI.Behaviors`.

**Success conditions:**
- SC-GZ023-1: `HealthBarGizmo`, `EntityRotationGizmo`, `VisibilityConeGizmo` compile in
  `Hrot.Common` with no `Hrot.IG` dependency.
- SC-GZ023-2: `HillAttackGizmo` compiles in `Hrot.AI.Behaviors` with no `Hrot.IG` dependency.
- SC-GZ023-3: All rendering output tests from the former `HealthBarGizmoTests`, etc., pass
  unchanged against the new `IStatelessGizmo.Draw` signature.
- SC-GZ023-4: `Hrot.IG` no longer contains `HealthBarGizmoInstance.cs`,
  `EntityRotationGizmoInstance.cs`, `VisibilityConeGizmoInstance.cs`, `HillAttackGizmoInstance.cs`
  (or their Definition wrappers).
- SC-GZ023-5: `StatelessGizmoSystem` registered with these four gizmos calls `Draw` for a
  matching entity (integration: use the existing `GizmosSystemTests` patterns with the new system).

---

### TASK-GZ024 — Unified [GizmoProjector] Attribute and Roslyn Source Generator

**Design reference:** feedback1.md ("How should we register stateless gizmos? ...Option B: Roslyn
Source Generators"), DESIGN.md §2.3

**Scope:**
Introduce a compile-time `[GizmoProjector]` attribute that declares component dependencies for
any gizmo class, and a Roslyn source generator that emits a `GizmoRegistrar.g.cs` bootstrap file,
eliminating the hand-written `GizmoRegistrar.cs` entirely.

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`:
- `GizmoProjectorAttribute.cs`

**Files to create in `FDP/Toolkits/Fdp.Toolkits.Analyzers/`:**
- `GizmoRegistrarGenerator.cs` (the ISourceGenerator implementation)

**Modify** `FDP/Toolkits/Fdp.Toolkits.Analyzers/Fdp.Toolkits.Analyzers.csproj`:
- Add `<PackageReference Include="Microsoft.CodeAnalysis.CSharp" .../>` (already present).
- Ensure `<IsRoslynComponent>true</IsRoslynComponent>` (already set).

**`GizmoProjectorAttribute`:**
```csharp
// Namespace: Fdp.Toolkit.Diagnostics.Gizmos
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GizmoProjectorAttribute : Attribute
{
    public Type[] RequiredComponents { get; }
    public GizmoProjectorAttribute(params Type[] requiredComponents)
        => RequiredComponents = requiredComponents;
}
```

All gizmo classes decorated with `[GizmoProjector(...)]` in an assembly that references
`Fdp.Toolkits` will be auto-discovered by the generator.

**Generator logic** (`GizmoRegistrarGenerator : ISourceGenerator`):
1. Find all classes decorated with `[GizmoProjectorAttribute]` across the compilation.
2. For each such class, inspect the semantic model:
   - If `class : IStatelessGizmo` → emit a `statelessRegistry.Register(new T(), requiredComponents)` call.
   - If `class : IGizmoDefinition` → emit a `gizmoRegistry.Register(new T())` call.
3. Emit a `partial static class GizmoRegistrar` with a `RegisterAll(GizmoRegistry gizmoRegistry,
   StatelessGizmoRegistry statelessRegistry, GizmoSettingsRegistry settings)` method containing
   all discovered registrations.
4. Place the generated file in the namespace of the containing assembly (e.g., `Hrot.IG.Gizmos`).

**Emitted output example (`GizmoRegistrar.g.cs`):**
```csharp
// <auto-generated/>
namespace Hrot.Common.Diagnostics.Gizmos
{
    public static partial class GizmoRegistrar
    {
        public static void RegisterAll(
            global::Fdp.Toolkit.Diagnostics.Gizmos.GizmoRegistry gizmoRegistry,
            global::Fdp.Toolkit.Diagnostics.Gizmos.StatelessGizmoRegistry statelessRegistry,
            global::Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingsRegistry settings)
        {
            statelessRegistry.Register(
                new global::Hrot.Common.Diagnostics.Gizmos.HealthBarGizmo(settings),
                new[] { typeof(global::Hrot.Core.Components.IgHealthState),
                         typeof(global::Fdp.Core.SimTransform) });
            // ... one line per decorated class ...
        }
    }
}
```

**Constraints:**
- The generator must target `netstandard2.0` (already satisfied by the project).
- The generator must handle classes with constructors that take `GizmoSettingsRegistry` as a
  parameter: emit `new T(settings)` for those; `new T()` for parameterless constructors.
- The hand-written `GizmoRegistrar.cs` in `Hrot.IG` is deleted once the generator is functional.
- No runtime reflection. All type resolution happens at compile time inside the generator.
- If a class has `[GizmoProjector]` but implements neither `IStatelessGizmo` nor `IGizmoDefinition`,
  the generator emits a compiler warning (`FDP_002`): "GizmoProjector class does not implement
  IStatelessGizmo or IGizmoDefinition; registration skipped."

**Success conditions:**
- SC-GZ024-1: A class decorated with `[GizmoProjector(typeof(SimTransform))]` implementing
  `IStatelessGizmo` appears in the generated `GizmoRegistrar.g.cs` as a `statelessRegistry.Register`
  call with the correct component types.
- SC-GZ024-2: A class decorated with `[GizmoProjector]` implementing `IGizmoDefinition` appears
  as a `gizmoRegistry.Register` call.
- SC-GZ024-3: The generated `RegisterAll` compiles without errors against the existing `Hrot.Common`
  and `Hrot.AI.Behaviors` assemblies.
- SC-GZ024-4: Adding a new `IStatelessGizmo` with `[GizmoProjector]` to any project in the
  solution causes the generator to include it in the next build without any manual edits.
- SC-GZ024-5: A `[GizmoProjector]` class implementing neither interface triggers compiler warning
  `FDP_002`.
- SC-GZ024-6 (regression): The existing `GizmosSystemTests` and rendering tests all pass after
  the manual `GizmoRegistrar.cs` is deleted.

---

## Phase 9: Presentation Fidelity Fixes

**Background:** Several presentation-layer contracts from the design are either broken or
incomplete in the current implementation. The following tasks repair the activation chain for
interactive gizmos, the spatial hit-testing logic, and the `DebugPrimitiveRenderer2D` gaps.

---

### TASK-GZ025 — Fix Broken DebugGizmoLayer Activation Chain

**Design reference:** DESIGN.md §4.3 and §5.3, feedback1.md ("The Broken Activation Chain")

**Scope:**
The current `DebugGizmoLayer.HandleInput` has a documented DEVIATION: it cannot push
`GizmoInteractionProxyTool` because the canvas is inaccessible. This breaks the entire
interactive-gizmo input pipeline. Fix by injecting `MapCanvas` into `DebugGizmoLayer`.

**Modify** `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`:
- Add `MapCanvas? _canvas` field.
- Add `MapCanvas` parameter to the production constructor:
  ```csharp
  public DebugGizmoLayer(int layerBitIndex, DebugPrimitiveBuffer buffer,
                          FdpEventBus eventBus, MapCanvas canvas,
                          ISimulationView? view = null)
  ```
  The parameterless constructor (used for no-buffer/stub mode) keeps its existing signature.
- In `HandleInput`, replace the DEVIATION comment block with:
  ```csharp
  if (_canvas != null)
  {
      var proxy = new GizmoInteractionProxyTool(best.Value.Token, _eventBus!);
      _canvas.PushTool(proxy);
      // GizmoInteractionProxyTool publishes GizmoInteractionStartedEvent on OnEnter.
  }
  else
  {
      _eventBus!.Publish(new GizmoInteractionStartedEvent
      {
          Token    = best.Value.Token,
          WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
      });
  }
  ```
  The fallback path (no canvas) preserves backward compat for test setups without a canvas.

**Modify** call sites that construct `DebugGizmoLayer` with a canvas:
- `Hrot.IG/IgApplication.cs` line 1129: pass `_map` (the `MapCanvas`) as the new argument.
- `Hrot.SimHost/SimHostVisualization.cs` (see TASK-GZ032): same pattern.

**Modify** `GizmoInteractionProxyTool.OnEnter(MapCanvas canvas)`:
- Publish `GizmoInteractionStartedEvent` in `OnEnter` (the tool has the token from construction),
  NOT from within `DebugGizmoLayer.HandleInput`. This keeps the event publication inside the tool.

**Success conditions:**
- SC-GZ025-1: After `DebugGizmoLayer.HandleInput` is called with a world position on top of a
  pickable primitive, `MapCanvas.PeekTool()` returns a `GizmoInteractionProxyTool` instance.
- SC-GZ025-2: `GizmoInteractionStartedEvent` is published exactly once (in `OnEnter`) and
  contains the correct `Token` and `WorldPos`.
- SC-GZ025-3: Clicking outside any pickable primitive does NOT push any tool.
- SC-GZ025-4 (regression): Existing `GizmoInteractionProxyToolTests` all pass unchanged.
- SC-GZ025-5: When `_canvas` is null (test constructor), the event is still published via
  `_eventBus` as the fallback (backward compat preserved).

---

### TASK-GZ026 — Fix Spatial Hit-Testing in DebugGizmoLayer

**Design reference:** DESIGN.md §5.3, feedback1.md ("Naive Spatial Hit-Testing")

**Scope:**
The current hit-test logic in `DebugGizmoLayer.HandleInput` only evaluates Euclidean distance to
`SphereCenter` or `LineStart`, ignoring line body, shape bounds, `SizeMode`, and screen-space
primitives. Replace it with geometry-aware intersection tests.

**Modify** `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`:

Replace `GetPrimitive2DPos` with a family of `HitTest(in DebugPrimitive prim, Vector2 worldPos,
float hitRadiusWorld, RenderContext ctx) : bool` methods:
- **Line / Arrow**: Point-to-line-segment distance using the standard perpendicular formula.
  `d = ||(q-p) x (p1-p0)|| / ||p1-p0||` where `p0` = `LineStart.XY`, `p1` = `LineEnd.XY`.
- **Sphere**: Euclidean distance from `worldPos` to `SphereCenter.XY` < `SphereRadius + hitRadius`.
- **Box2D**: Point-in-OBB test using the box center, extents, and rotation angle.
- **Text / Icon / EntityBadge**: AABB around the text anchor position (`TextX, TextY`).
- **Default**: Distance to payload origin (fallback for unknown shapes).

**SizeMode correction:**
- When `prim.SizeMode == SizeMode.ScreenPixels`, the rendered hit radius scales with zoom:
  `effectiveHitRadius = HitRadiusWorld / ctx.Zoom` (same formula used by the renderer).

**CoordinateSpace.Screen handling:**
- Primitives in `Screen` space use screen-pixel coordinates; convert `worldPos` to screen via
  `Raylib.GetWorldToScreen2D(worldPos, ctx.Camera)` before testing.

**Topmost layer preference:** keep the existing logic that prefers the highest `DebugLayer`.

**Add `RenderContext ctx` parameter** to `HandleInput` signature:
```csharp
public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed, RenderContext ctx)
```
Update the `IMapLayer` interface (or use the existing overload pattern in the codebase).

**Success conditions:**
- SC-GZ026-1: A click on the midpoint of a 100-unit Line primitive (not at LineStart) triggers
  a hit (distance to segment < HitRadiusWorld).
- SC-GZ026-2: A click at LineStart of a 100-unit Line but 10 pixels beyond the endpoint does
  NOT trigger a hit.
- SC-GZ026-3: A click within `SphereRadius` of a Sphere's center triggers a hit.
- SC-GZ026-4: With `SizeMode.ScreenPixels` at zoom=2, the effective hit radius is halved
  (point 3 world-units away registers hit when logical HitRadius=5 and zoom=2).
- SC-GZ026-5: A Screen-space primitive is tested in screen-pixel space, not world space.
  Verified by supplying a world position that maps to within the screen-space primitive's anchor.

---

### TASK-GZ027 — Fix EntityLocal Rendering for All Primitive Shapes

**Design reference:** DESIGN.md §5.2, feedback1.md ("EntityLocal (Flawed)")

**Scope:**
The current `DebugPrimitiveRenderer2D` applies the anchor entity's `SimTransform` rotation and
translation only to `Line` primitives. `Arrow`, `Sphere`, `Box2D`, `Text`, and `Icon` shapes with
`CoordinateSpace.EntityLocal` fall through to world-space rendering at the wrong position.

**Modify** `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs`:

In the `EntityLocal` resolution block (currently around line 55), extract the transform helper:
```csharp
private static Vector3 ApplyTransform(SimTransform tf, Vector3 local)
    => tf.Position + Vector3.Transform(local, tf.Rotation);

private static Vector2 ApplyTransform2D(SimTransform tf, float localX, float localY)
{
    var world = ApplyTransform(tf, new Vector3(localX, localY, 0f));
    return new Vector2(world.X, world.Y);
}
```

Apply it in `DispatchShape` for each shape when `prim.Space == EntityLocal`:
- **Arrow**: transform `prim.ArrowFrom` and `prim.ArrowTo` using `ApplyTransform`.
- **Sphere**: transform `prim.SphereCenter` using `ApplyTransform`.
- **Box2D**: transform `(prim.BoxCenterX, prim.BoxCenterY, 0)` using `ApplyTransform2D`; add
  `tf.RotationDegrees` to `prim.BoxAngleDeg` for the box rotation.
- **Text**: transform `(prim.TextX, prim.TextY, 0)` using `ApplyTransform2D`.
- **Icon**: transform `(prim.IconWorldPosX, prim.IconWorldPosY, 0)` using `ApplyTransform2D`.
- Remove the `// Arrow/Text EntityLocal: deferred (not yet supported).` comment.

**Constraints:**
- Entity liveness check (`view.IsAlive(anchor)`) and `SimTransform` component presence check must
  occur before `DispatchShape` for any EntityLocal primitive. If the anchor is dead or lacks
  `SimTransform`, silently skip the primitive (existing behavior for `Line`).
- `tf.RotationDegrees` helper: `SimTransform.Rotation` is a quaternion; convert to 2D heading via
  `MathF.Atan2(2f*(q.W*q.Z + q.X*q.Y), 1f - 2f*(q.Y*q.Y + q.Z*q.Z)) * (180f / MathF.PI)`.

**Success conditions:**
- SC-GZ027-1: An `EntityLocal` `Sphere` primitive at local offset (5, 0, 0) renders at
  `entity.Position + (5, 0, 0)` in world space when entity has no rotation.
- SC-GZ027-2: An `EntityLocal` `Arrow` primitive rotates with the entity: when the entity
  rotates 90 degrees, the arrow direction also rotates 90 degrees.
- SC-GZ027-3: An `EntityLocal` `Text` primitive at local (0, 2, 0) renders 2 meters above
  the entity's world position.
- SC-GZ027-4: An `EntityLocal` primitive for a dead entity is silently skipped (no exception,
  no draw call).
- SC-GZ027-5 (regression): Existing `EntityLocal` Line primitive tests continue to pass.

---

### TASK-GZ028 — Fix SizeMode.ScreenPixels for Shape Radii and Extents

**Design reference:** DESIGN.md §5.2, feedback1.md ("SizeMode Flaw")

**Scope:**
`SizeMode.ScreenPixels` currently scales only `ThicknessU16` (stroke width). The shape-specific
geometric dimensions (`SphereRadius`, `ArrowHeadSize`, `BoxExtentX/Y`) are passed as raw world
units and grow/shrink with zoom, violating the "mathematically defeats camera zoom" contract.

**Modify** `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs`:

In `DispatchShape`, after resolving `float zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;`, add:
```csharp
float geomScale = prim.SizeMode == SizeMode.ScreenPixels ? 1f / zoom : 1f;
```

Apply `geomScale` to:
- `Sphere`: `float radius = prim.SphereRadius * geomScale;` — passed to `DrawCircleV`.
- `Arrow`: `float headSize = prim.ArrowHeadSize * geomScale;` — passed to `DrawArrow`.
- `Box2D`: `float extX = prim.BoxExtentX * geomScale; float extY = prim.BoxExtentY * geomScale;`
  — passed to the box draw call.
- **`Line`/`Arrow` thickness**: already handled by the existing `ThicknessU16` path; no change.

**Constraints:**
- `WorldMeters` primitives (`geomScale = 1f`) are unaffected.
- Do not alter the `Text` shape: font size is already specified in screen pixels by convention.

**Success conditions:**
- SC-GZ028-1: A `Sphere` with `SizeMode.ScreenPixels`, `SphereRadius = 10`, at zoom=1.0 renders
  with radius 10. At zoom=2.0 the `DrawCircleV` call receives radius 5 (verified via mock).
- SC-GZ028-2: A `Sphere` with `SizeMode.WorldMeters`, `SphereRadius = 10`, at zoom=2.0 renders
  with radius 10 (unchanged). Verified via mock.
- SC-GZ028-3: An `Arrow` with `SizeMode.ScreenPixels`, `ArrowHeadSize = 8`, at zoom=4.0 renders
  with head size 2 (verified via mock).
- SC-GZ028-4: A `Box2D` with `SizeMode.ScreenPixels`, extents (20, 15), at zoom=2.0 renders
  with extents (10, 7.5) (verified via mock).

---

## Phase 10: Data Plane Correctness

---

### TASK-GZ029 — Implement LifetimeSeconds Persistent Primitive Re-emission

**Design reference:** DESIGN.md §6.1 ("Persistent primitives are re-emitted each frame until
they expire"), feedback1.md ("The Persistent Primitive Lie")

**Scope:**
`DebugPrimitive.LifetimeSeconds > 0` is supposed to persist a primitive across multiple frames
without the emitting gizmo needing to re-draw it each tick. Currently `DebugPrimitiveBuffer.Clear()`
resets `_count = 0` every frame, destroying all primitives. Implement the re-emission cache.

**Modify** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/DebugPrimitiveBuffer.cs`:
- Add `DebugPrimitive[] _persistent` (pre-allocated, same capacity).
- Add `float[] _remainingLife` (parallel array tracking remaining seconds per persistent slot).
- Add `int _persistentCount` tracking live persistent entries.

**New `void EndFrame(float deltaTime)` method** (replaces direct `Clear()` calls in the orchestrator):
1. Compact the `_persistent` array: remove entries where `_remainingLife[i] <= 0`.
2. Decrement `_remainingLife[i] -= deltaTime` for surviving entries.
3. Re-inject all surviving persistent primitives into the main buffer at the START of the next
   `GetFrame()` span: call `Clear()` (resets `_count = 0`), then append each persistent entry.

**Modified draw methods:** When any `Draw*` method emits a primitive with `LifetimeSeconds > 0`:
1. Attempt to add to `_persistent` (drop silently if persistent capacity exhausted; increment
   `DroppedCount`).
2. Also emit into the main buffer for the current frame (so it renders in frame N too).

**Modified `Clear()`:** Now only resets the transient part. For the frame controller, call
`EndFrame(deltaTime)` instead of `Clear()` directly.

**`DataDrivenGizmoSystem` and `StatelessGizmoSystem` change:** After all gizmos have executed,
call `_drawBuilder.EndFrame(deltaTime)` (if `_drawBuilder` is a `DebugPrimitiveBuffer`). Use
interface covariance: add `void EndFrame(float deltaTime)` to `IDebugDrawBuilder` with a default
no-op implementation so existing implementations stay valid.

**Constraints:**
- Persistent capacity defaults to 256 (separate from the main transient capacity of 4096).
- Persistent primitives that expire (remaining life reaches 0) are NOT re-emitted in the next frame.
- A primitive with `LifetimeSeconds == 0` is a one-frame transient — current behavior unchanged.
- No per-frame heap allocation: `_persistent` and `_remainingLife` are allocated once at startup.

**Success conditions:**
- SC-GZ029-1: A primitive with `LifetimeSeconds = 0.5f` emitted in frame N appears in `GetFrame()`
  for frames N, N+1, N+2 (assuming 0.1s deltaTime each), and is absent in frame N+5.
- SC-GZ029-2: A primitive with `LifetimeSeconds = 0` does NOT appear in frame N+1's `GetFrame()`.
- SC-GZ029-3: After persistent capacity is exhausted, additional persistent primitives are dropped;
  `DroppedCount` increments. No exception thrown.
- SC-GZ029-4: Persistent primitives survive across a `Clear()` cycle (they are re-injected at
  the start of each frame).
- SC-GZ029-5: `EndFrame(deltaTime)` called with `deltaTime > LifetimeSeconds` causes the
  primitive to expire and disappear in the next frame.

---

### TASK-GZ030 — Restore PickToken SubElementId Storage in Interactive Primitives

**Design reference:** DESIGN.md §4.1, feedback1.md ("Sub-Element Identity Loss")

**Scope:**
`DebugPrimitive.Token` is a computed property that synthesises `PickToken { Target = Anchor,
SubElementId = 0 }`. `SubElementId` is always zero, making it impossible for multi-handle
interactive gizmos (e.g. a path node editor with multiple draggable vertices on the same entity)
to distinguish which sub-element the operator is dragging.

Store `SubElementId` explicitly in the struct's unused padding bytes.

**Modify** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/DebugPrimitive.cs`:
- Add `[FieldOffset(30)] public ushort SubElementId;` — offset 30-31 are currently unused padding
  in the `EntityBadge` payload layout (bytes `BadgeTargetGen` ends at 30, two pad bytes follow).
  Verify against the struct layout that this offset is genuinely free across ALL payload unions.
  If the `ComponentInspector` layout uses offset 30 (it uses `InspAnchor ScreenAnchor` at 30),
  reclaim instead from the `Line` payload: offset 52-55 are currently `EndColor` (4 bytes, ends
  at 52), leaving 12 bytes at 52-63 for future use. Place `SubElementId` at `[FieldOffset(52)]`
  for the `EntityLocal` / interactive path only, and document the dual use.
  **Preferred solution:** Use `[FieldOffset(30)] public ushort SubElementId` — this byte is free
  in EntityBadge. For other shape payloads that don't use `SubElementId`, this field is a don't-care.
- Update the `Token` computed property:
  ```csharp
  public PickToken Token => new PickToken { Target = Anchor, SubElementId = SubElementId };
  ```
- Add `DrawEntityLocalInteractive` overload to `IDebugDrawBuilder` and `DebugPrimitiveBuffer`:
  ```csharp
  void DrawEntityLocalInteractive(Entity anchor, Vector3 localStart, Vector3 localEnd,
                                   Rgba32 color, ushort subElementId,
                                   float thickness = 1f, byte layer = 0);
  ```
  This sets both `AnchorIndex`/`AnchorGeneration` (for the entity) and `SubElementId`.

**Constraints:**
- The 64-byte size constraint is inviolable. `SubElementId` at offset 30 occupies two bytes that
  were explicit padding in the EntityBadge layout. Verify no other payload writes to offset 30-31.
- Non-interactive primitives leave `SubElementId = 0` (default).
- Update `DebugPrimitive.MakeLine` and similar factory helpers to default `SubElementId = 0`.

**Success conditions:**
- SC-GZ030-1: `Marshal.SizeOf<DebugPrimitive>() == 64` still holds after adding `SubElementId`.
- SC-GZ030-2: `DrawEntityLocalInteractive(entity, start, end, color, subElementId: 3)` emits
  a primitive with `Token.SubElementId == 3`.
- SC-GZ030-3: Two calls with the same entity but different `subElementId` values produce
  primitives with different `Token.SubElementId` values (1 and 2 are distinguishable).
- SC-GZ030-4: A zero-value `DebugPrimitive` still has `Token.SubElementId == 0`.
- SC-GZ030-5 (regression): Existing `DebugPrimitive` size and offset tests (SC-GZ002-*) all pass.

---

## Phase 11: System Integration and Wiring

---

### TASK-GZ031 — Fix Selection Filtering in IgApplication

**Design reference:** DESIGN.md §2.4, feedback1.md ("Broken Selection Filtering")

**Scope:**
`IgApplication.cs` registers `DataDrivenGizmoSystem` with `isSelectedPredicate: null`, which
disables selection filtering and causes every active gizmo to render for every entity every frame.
Pass a proper predicate that checks `SelectionState.IsSelected`.

**Modify** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`:
- Locate the `DataDrivenGizmoSystem` registration (currently at line 1235-1238).
- Replace `isSelectedPredicate: null` with:
  ```csharp
  isSelectedPredicate: (view, entity) =>
      view.HasComponent<SelectionState>(entity) &&
      view.GetComponentRO<SelectionState>(entity).IsSelected
  ```

**Also update** the `BehaviorGizmoManagerSystem` registration with the same predicate if it
also currently passes null.

**Also update** the `StatelessGizmoSystem` registration (from TASK-GZ022) with the same predicate
pattern when `GlobalDebugSettings.ForceAllGizmosVisible` is false.

**Constraints:**
- The predicate is only evaluated when `GlobalDebugSettings.ForceAllGizmosVisible == false`.
  When the flag is true, the system ignores the predicate and renders all gizmos.
- Do not remove the `null`-accepting constructor overload; it is used in unit tests.

**Success conditions:**
- SC-GZ031-1: With `ForceAllGizmosVisible = false`, a gizmo for an entity with
  `SelectionState.IsSelected == false` does NOT emit any draw calls.
- SC-GZ031-2: After selecting the entity (`IsSelected = true`), the same gizmo emits draw calls.
- SC-GZ031-3: With `ForceAllGizmosVisible = true`, the gizmo renders for all entities regardless
  of selection state.
- SC-GZ031-4 (regression): Existing `DataDrivenGizmoPredicateTests` all pass.

---

### TASK-GZ032 — Wire DebugGizmoLayer into SimHostVisualization

**Design reference:** DESIGN.md §5.3, feedback1.md ("Presentation Canvas Wiring"), feedback1.md
("All gizmos are in Hrot.IG but I need them in Hrot.SimHost")

**Scope:**
`SimHostVisualization` registers `EntityRenderLayer`, `ProjectileLayer`, and `TrajectoryLayer` but
omits `DebugGizmoLayer`. This means gizmo primitives accumulated by `DataDrivenGizmoSystem` and
`StatelessGizmoSystem` in the SimHost process are never rendered. Wire the layer into the
composition root.

**Modify** `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs`:
- Add a `DebugPrimitiveBuffer _gizmoBuffer` field (initialized in the constructor or `Initialize`).
- After the existing `AddLayer(new SimHostTrajectoryLayer(...))` call, add:
  ```csharp
  _map.AddLayer(new DebugGizmoLayer(31, _gizmoBuffer, _world.Bus, _map, repo));
  ```
- Expose `DebugPrimitiveBuffer GizmoBuffer => _gizmoBuffer` so `SimHostApp` can pass it to
  `DataDrivenGizmoSystem` and `StatelessGizmoSystem` at kernel registration time.

**Modify** `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`:
- After the world initialization block, register:
  ```csharp
  _kernel.RegisterGlobalSystem(new DataDrivenGizmoSystem(
      _gizmoRegistry, _vis!.GizmoBuffer,
      isSelectedPredicate: (view, entity) =>
          view.HasComponent<SelectionState>(entity) &&
          view.GetComponentRO<SelectionState>(entity).IsSelected));

  _kernel.RegisterGlobalSystem(new StatelessGizmoSystem(
      _statelessGizmoRegistry, _vis!.GizmoBuffer));
  ```
- Wire `GizmoRegistrar.RegisterAll(...)` to populate both registries at startup.

**Constraints:**
- `DebugPrimitiveBuffer` capacity defaults to 4096.
- Do not add `Hrot.IG`-specific gizmo definitions to `SimHostApp`; use only the gizmos from
  `Hrot.Common` and `Hrot.AI.Behaviors` (migrated in TASK-GZ023).
- `DebugGizmoLayer` construction passes the `MapCanvas` (`_map`) so TASK-GZ025 activation
  chain works in SimHost too.

**Success conditions:**
- SC-GZ032-1: After `SimHostVisualization.Initialize(...)`, `_map.Layers` contains a
  `DebugGizmoLayer` instance.
- SC-GZ032-2: Primitives emitted by `DataDrivenGizmoSystem` or `StatelessGizmoSystem` appear
  in `_vis.GizmoBuffer.GetFrame()` after the system executes.
- SC-GZ032-3: `SimHostVisualizationTests` compile and pass with the new `GizmoBuffer` property.
- SC-GZ032-4: Integration test (existing `GizmoRendererWiringTests` or new): a full
  `SimHostApp` startup does not throw due to missing gizmo registrations.

---

### TASK-GZ033 — Wire DebugPrimitivesBatch DDS Egress from SimHost

**Design reference:** DESIGN.md §6.1, feedback1.md ("The Remote Transport Protocol",
"The Missing Implementation")

**Scope:**
`DebugPrimitivesBatch` DDS topic struct exists (TASK-GZ016), but there is no publisher system
that reads the `DebugPrimitiveBuffer` and broadcasts it over the network. Create a
`DebugPrimitivesBatchPublisherSystem` and register it in `SimHostApp`.

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/`:
- `DebugPrimitivesBatchPublisherSystem.cs`

**`DebugPrimitivesBatchPublisherSystem`:**
```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class DebugPrimitivesBatchPublisherSystem : IEcsModuleSystem
{
    private readonly DebugPrimitiveBuffer _buffer;
    private readonly IDdsWriter<DebugPrimitivesBatch>? _writer; // null = local-only, no-op
    private readonly byte _nodeId;
    private uint _frameNumber;

    public DebugPrimitivesBatchPublisherSystem(DebugPrimitiveBuffer buffer,
                                               byte nodeId,
                                               IDdsWriter<DebugPrimitivesBatch>? writer = null)
    {
        _buffer = buffer;
        _nodeId = nodeId;
        _writer = writer;
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (_writer == null) return;
        var frame = _buffer.GetFrame();
        if (frame.Length == 0) return;

        // Copy primitives to a managed array for DDS transport.
        var primitives = new DebugPrimitive[frame.Length];
        frame.CopyTo(primitives);

        _writer.Write(new DebugPrimitivesBatch
        {
            FrameNumber = _frameNumber++,
            NodeId      = _nodeId,
            Primitives  = primitives,
        });
    }
}
```

The system must run AFTER `DataDrivenGizmoSystem`, `StatelessGizmoSystem`, and
`BehaviorGizmoManagerSystem` have written their primitives for the frame. Use
`[UpdateInPhase(SystemPhase.PostSimulation)]` — ordering within the phase is determined by
registration order; register the publisher last.

**Constraints:**
- When `_writer` is null, the system is a no-op (local-only mode).
- The managed array allocation (`new DebugPrimitive[...]`) is acceptable on this path; DDS
  serialization already allocates. This is not a zero-alloc hot path.
- `FrameNumber` wraps at `uint.MaxValue` (natural overflow is fine).
- System is registered only in `SimHostApp` (and `CgfApplication` if present), NOT in `IgApplication`.

**Success conditions:**
- SC-GZ033-1: When the buffer contains N primitives, the publisher writes exactly one
  `DebugPrimitivesBatch` record with `Primitives.Length == N` and all 64 bytes preserved.
- SC-GZ033-2: When the buffer is empty (`GetFrame().Length == 0`), the publisher does NOT call
  `_writer.Write` (verified by asserting zero DDS writer calls).
- SC-GZ033-3: When `_writer` is null, `Execute` returns without exception.
- SC-GZ033-4: `FrameNumber` increments by 1 per call.
- SC-GZ033-5 (round-trip): A batch published with primitives P1, P2 can be deserialized and
  the primitives read back with identical byte contents (reuse SC-GZ016-2 pattern).

---

### TASK-GZ034 — Fix GizmoSettingsPublisherSystem to Emit StructEdit Schema

**Design reference:** DESIGN.md §6.2, feedback1.md ("Hardcoded JSON vs. Declarative Inspector
Extensions")

**Scope:**
`GizmoSettingsPublisherSystem` builds a flat key→value JSON string using `Utf8JsonWriter` instead
of using the `StructEdit`/`EditDocument` pipeline. Remote UIs receive flat JSON with no type
metadata or validation rules, preventing the `ImGuiPropertyTree` from rendering structured editors.
Replace the flat JSON with a proper StructEdit `EditDocument` schema.

**Modify** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GizmoSettingsPublisherSystem.cs`:
- Replace the `Utf8JsonWriter` block with a call to a new `BuildEditDocument()` helper:
  ```csharp
  private EditDocument BuildEditDocument()
  {
      var doc = new EditDocument();
      foreach (var (key, active, defaultVal) in _registry.EnumerateAll())
      {
          switch (active.Type)
          {
              case SettingType.Bool:
                  doc.AddField(key, active.BoolValue);
                  break;
              case SettingType.Int32:
                  doc.AddField(key, active.IntValue);
                  break;
              case SettingType.Float32:
                  doc.AddField(key, active.FloatValue);
                  break;
          }
      }
      return doc;
  }
  ```
- Serialize via `EditDocumentJsonSerializer.Serialize(doc)` instead of manual JSON writing.
- The result is a StructEdit JSON schema string (with type metadata, field names, default values)
  rather than a flat `{"key": value}` JSON object.

**Constraints:**
- `EditDocument`, `EditDocumentJsonSerializer` are in the `StructEdit` ExtDep project (already
  referenced by `Fdp.Presentation`). If not yet referenced by `Fdp.Toolkits`, add the reference.
- The published `GizmoUiState.EditDocumentJson` field must be parseable by `ImGuiPropertyTree`
  on the receiving end (verified by round-trip deserialization test).
- Preserve the `IsDirty` guard — no re-publish when settings are unchanged.

**Success conditions:**
- SC-GZ034-1: `GizmoSettingsPublisherSystem.Execute` publishes a `GizmoUiState` whose
  `EditDocumentJson` deserializes to an `EditDocument` with the correct field names, types, and
  current values.
- SC-GZ034-2: A `Bool` setting named `"HealthBar.Active"` with value `true` appears in the
  schema as a boolean field with value `true`.
- SC-GZ034-3: A `Float32` setting named `"HealthBar.BarHeight"` with value `3.5f` appears in
  the schema as a float field with value `3.5`.
- SC-GZ034-4 (regression): SC-GZ017-2 and SC-GZ017-3 still pass (IsDirty guard preserved).

---

### TASK-GZ035 — Fix Behavior Lifecycle Leak on AI Behavior Abort

**Design reference:** DESIGN.md §2.5, feedback1.md ("Behavior Lifecycle Leaks")

**Scope:**
`BehaviorGizmoManagerSystem` tears down behavior gizmos on `ClearBehaviorEvent` and
`DestructionOrder`. However, B-Tree high-priority interrupts and HSM aborts that preempt a running
behavior do not guarantee a `ClearBehaviorEvent` emission before the next `AssignBehaviorEvent`.
This causes orphaned gizmo instances that remain visible after the behavior is logically gone.

**Verify and fix** the behavior preemption path:

1. **Audit** `Hrot.AI.Behaviors` and `Hrot.CGF` for the B-Tree and HSM abort/interrupt sequences.
   Find every code path that transitions an entity from one behavior to another without going
   through the `ClearBehaviorEvent` → `AssignBehaviorEvent` sequence.
2. For each such path, emit `ClearBehaviorEvent` for the old behavior BEFORE emitting
   `AssignBehaviorEvent` for the new behavior. The event bus ordering guarantees that
   `BehaviorGizmoManagerSystem` sees `Clear` before `Assign` in the same frame (double-buffered
   bus: both events are in the write buffer and appear in the read buffer next frame together,
   so the system processes `Clear` first because it drains clears before setups).
3. Add a **defensive guard** in `BehaviorGizmoManagerSystem`: on `AssignBehaviorEvent`, if there
   is already an active gizmo for the entity, call `OnTeardown` and return the old instance before
   initializing the new one (this is already specified in SC-GZ006-4 but verify the implementation).

**Files to modify:** depends on audit, but likely:
- `Hrot.AI.Behaviors/BTreeInterruptHandler.cs` (or equivalent)
- `BehaviorGizmoManagerSystem.cs` (defensive guard already required by SC-GZ006-4)

**Success conditions:**
- SC-GZ035-1: When a B-Tree interrupt transitions entity E from behavior A to behavior B,
  the gizmo for behavior A is torn down (OnTeardown called) and the gizmo for behavior B is
  initialized (OnInitialize called) within the same frame processing cycle.
- SC-GZ035-2: After the interrupt, `_activeBehaviorGizmos[entity]` holds the gizmo for behavior B,
  not behavior A.
- SC-GZ035-3: Repeated rapid behavior switches (A→B→C in quick succession) do not leak gizmo
  instances (no gizmo for A or B remains after C is assigned).
- SC-GZ035-4 (regression): Existing SC-GZ006-* tests all pass.
- SC-GZ035-5: Write a test that simulates a behavior interrupt (publish AssignBehaviorEvent
  without a preceding ClearBehaviorEvent for the same entity) and verify the old gizmo is
  torn down via the defensive guard in BehaviorGizmoManagerSystem.

---

### TASK-GZ036 — CPU Performance Budget for Gizmo Systems

**Design reference:** feedback1.md ("Gap B: CPU Performance Budgets for Gizmos"), DESIGN.md §2.4

**Scope:**
Prevent expensive gizmo projectors from exceeding the frame budget. Integrate
`TimeSliceMetric.WallClockTime` with `repo.QueryTimeSliced` in both `DataDrivenGizmoSystem` and
`StatelessGizmoSystem`, controlled by a `MaxGizmoFrameMs` field on `GlobalDebugSettings`.

**Modify** `Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettings.cs`:
- Add field: `public float MaxGizmoFrameMs;` — defaults to `2.0f` (ms per frame for all gizmos).
  `0` means unlimited (backward compat: treat 0 as infinity).

**Modify** `DataDrivenGizmoSystem.Execute(ISimulationView view, float deltaTime)`:
- Read `GlobalDebugSettings.MaxGizmoFrameMs` from the ECS singleton.
- If `MaxGizmoFrameMs > 0`, replace the direct iteration of `_activeGizmos` with
  `repo.QueryTimeSliced(query, _timeSliceState, MaxGizmoFrameMs, TimeSliceMetric.WallClockTime,
   entity => { ... })`. Use a persistent `TimeSlicedIteratorState _timeSliceState` field
   (allocated once at construction).
- Entities not processed in the current frame carry over to the next frame (time-sliced semantics).

**Modify** `StatelessGizmoSystem.Execute(ISimulationView view, float deltaTime)`:
- Same pattern: if `MaxGizmoFrameMs > 0`, use `repo.QueryTimeSliced` per registered rule.
- Budget is shared across all rules in proportion to their entity counts (simplest: allocate
  `MaxGizmoFrameMs / rules.Count` per rule).

**Constraints:**
- `MaxGizmoFrameMs = 0` disables time slicing (backward compat).
- `TimeSlicedIteratorState` is allocated once at `DataDrivenGizmoSystem` construction.
- The time-sliced path is used only in global-force mode (all entities); selection-mode
  already limits work to selected entities and does not need time slicing.

**Success conditions:**
- SC-GZ036-1: With `MaxGizmoFrameMs = 0.001` (effectively zero budget), only a small subset
  of entities are processed per frame (not all 1000 in a 1000-entity test scenario).
- SC-GZ036-2: With `MaxGizmoFrameMs = 1000` (infinite budget), all entities are processed
  in a single frame (same behavior as the non-time-sliced path).
- SC-GZ036-3: `MaxGizmoFrameMs = 0` (unlimited) processes all entities regardless of wall time.
- SC-GZ036-4: `TimeSlicedIteratorState` is not re-allocated on each `Execute` call (verified
  by asserting the same object reference across multiple frames).

---

## Phase 12: Networked Interaction and Dumb Terminal

**Background:** Interactive gizmos work end-to-end in local mode. However, when the operator
runs IG as a remote viewer (separate process from SimHost), drag interactions entered on the IG
terminal never reach the SimHost ECS bus, and SimHost never receives them. Additionally, the IG
process currently runs `DataDrivenGizmoSystem` and `StatelessGizmoSystem` that re-evaluate the
full ECS — duplicating work done in SimHost and wasting CPU on the display terminal. These two
tasks separate IG into a "dumb terminal" that only renders what the network provides, and wire
the missing bidirectional interaction event channel.

---

### TASK-GZ037 — Networked GizmoInteractionEvent DDS Translators

**Design reference:** DESIGN.md §4.2, DESIGN.md §6.1

**Scope:**
Define a DDS topic that carries gizmo interaction events across the network, and implement
bidirectional translators: one egress system on the IG side (reads local `FdpEventBus`, writes to
DDS) and one ingress system on the SimHost side (reads DDS, publishes to local `FdpEventBus`).
Without this, every drag interaction initiated by a remote IG terminal is silently discarded.

**Files to create** in `Hrot/Network/Hrot.Network.NED/Gizmos/`:
- `GizmoInteractionEventKind.cs`
- `GizmoInteractionBatch.cs`
- `GizmoInteractionEgressSystem.cs`
- `GizmoInteractionIngressSystem.cs`

**`GizmoInteractionEventKind`:**
```csharp
public enum GizmoInteractionEventKind : byte
{
    Started    = 0,
    DragUpdate = 1,
    Commit     = 2,
    Cancel     = 3,
}
```

**`GizmoInteractionBatch` DDS topic:**
```csharp
[DdsTopic("GizmoInteractionBatch")]
[DdsQos(Reliability = DdsReliability.Reliable,
        Durability  = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 10)]
public partial struct GizmoInteractionBatch
{
    [DdsKey] public byte   SourceNodeId;
    [DdsKey] public uint   SequenceNumber;

    public GizmoInteractionEventKind Kind;

    // PickToken fields (blittable breakdown of Entity + SubElementId)
    public int    PickEntityIndex;
    public ushort PickEntityGeneration;
    public ushort PickSubElementId;

    // WorldPos (present for Started/DragUpdate/Commit; zero for Cancel)
    public float WorldX;
    public float WorldY;
    public float WorldZ;
}
```

This is a single-event-per-record design (not a batch array) because interaction events are
low-frequency (at most a handful per frame). The `HistoryDepth = 10` ensures rapid sequences
(e.g. burst of DragUpdate records) are not dropped by slow readers.

**`GizmoInteractionEgressSystem` (IG side):**
- `[UpdateInPhase(SystemPhase.PreSimulation)]` — run before the simulation tick so events
  generated in the UI thread are forwarded before the next ECS frame begins.
- Drains all four interaction event types from `view.ReadEvents<T>()`:
  `GizmoInteractionStartedEvent`, `GizmoDragUpdateEvent`, `GizmoInteractionCommitEvent`,
  `GizmoInteractionCancelEvent`.
- For each event, writes one `GizmoInteractionBatch` record via
  `IDdsWriter<GizmoInteractionBatch>` with the appropriate `Kind`, `PickToken` fields, and
  `WorldPos`.
- When `IDdsWriter` is null (local-only mode), the system is a no-op.

**Constructor:**
```csharp
GizmoInteractionEgressSystem(byte nodeId,
                              IDdsWriter<GizmoInteractionBatch>? writer = null)
```

**`GizmoInteractionIngressSystem` (SimHost side):**
- `[UpdateInPhase(SystemPhase.PreSimulation)]` — inject events before the simulation tick so
  `DataDrivenGizmoSystem`/`StatelessGizmoSystem` see them in the same frame.
- Reads all pending `GizmoInteractionBatch` records from
  `IDdsReader<GizmoInteractionBatch>`.
- For each record, reconstructs the `Entity` from `PickEntityIndex`/`PickEntityGeneration` and
  translates to the corresponding typed event:
  - `Kind == Started`    → publish `GizmoInteractionStartedEvent`
  - `Kind == DragUpdate` → publish `GizmoDragUpdateEvent`
  - `Kind == Commit`     → publish `GizmoInteractionCommitEvent`
  - `Kind == Cancel`     → publish `GizmoInteractionCancelEvent`
- For `DragUpdate` and `Commit` events: guard with `view.IsAlive(entity)` before publishing.
  If the entity is no longer alive, substitute a `GizmoInteractionCancelEvent` for safety
  (the drag target no longer exists).
- `Cancel` events are always forwarded regardless of entity liveness.

**Constructor:**
```csharp
GizmoInteractionIngressSystem(FdpEventBus bus,
                               IDdsReader<GizmoInteractionBatch>? reader = null)
```
When `reader` is null, the system is a no-op (local-only mode).

**Constraints:**
- `GizmoInteractionBatch` topic name must not collide with existing DDS topics.
- The egress system drains events it did NOT originate (it just mirrors whatever is on the local
  bus). This means in local mode the events are drained and discarded, which is correct because
  the local `GizmoInteractionProxyTool` already published them to the same bus.
- Sequence number wraps at `uint.MaxValue` (natural overflow).
- The ingress system must not republish events back to DDS (no infinite loop). Because the
  ingress system only reads from DDS and writes to the local bus, and the egress system reads
  from the local bus, there is no loop risk — they are in different processes.

**Success conditions:**
- SC-GZ037-1: `GizmoInteractionBatch` DDS schema compiles without errors.
- SC-GZ037-2: `GizmoInteractionEgressSystem` with a `GizmoDragUpdateEvent` on the bus writes
  exactly one `GizmoInteractionBatch` record with `Kind = DragUpdate` and the correct
  `PickEntityIndex`, `WorldX/Y/Z` values.
- SC-GZ037-3: `GizmoInteractionIngressSystem` with a `GizmoInteractionBatch` record of
  `Kind = Commit` publishes exactly one `GizmoInteractionCommitEvent` to the local bus
  with the correct `PickToken` and `WorldPos`.
- SC-GZ037-4: When the entity is not alive, a received `DragUpdate` or `Commit` batch record
  is translated to `GizmoInteractionCancelEvent` (entity-dead safety guard).
- SC-GZ037-5: A `Cancel` batch record is always forwarded, even if the entity is not alive.
- SC-GZ037-6: Round-trip test — serialize a `GizmoInteractionBatch`, deserialize it, and
  confirm all fields (`SequenceNumber`, `Kind`, `PickEntityIndex`, `WorldX`, etc.) are preserved.
- SC-GZ037-7: When `IDdsWriter` is null (local-only mode), egress `Execute` returns immediately
  without exception.
- SC-GZ037-8: When `IDdsReader` is null (local-only mode), ingress `Execute` returns immediately
  without exception.

---

### TASK-GZ038 — IG Dumb Terminal Ingress (DebugPrimitivesIngressTranslator)

**Design reference:** DESIGN.md §6.1, DESIGN.md §5.3

**Scope:**
Create `DebugPrimitivesIngressTranslator` — a DDS subscriber adapter that receives
`DebugPrimitivesBatch` records from the network and populates the local `DebugPrimitiveBuffer`.
Then, remove `DataDrivenGizmoSystem` and `StatelessGizmoSystem` from `IgApplication`'s
composition root so that IG becomes a pure rendering terminal that does not re-evaluate ECS.

**Files to create** in `Hrot/Network/Hrot.Network.NED/Gizmos/`:
- `DebugPrimitivesIngressTranslator.cs`

**Modify:**
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

**`DebugPrimitivesIngressTranslator`:**
```csharp
public sealed class DebugPrimitivesIngressTranslator
{
    private readonly DebugPrimitiveBuffer _buffer;
    private readonly IDdsReader<DebugPrimitivesBatch>? _reader; // null = no-op
    private readonly byte? _filterNodeId; // null = accept all nodes

    public DebugPrimitivesIngressTranslator(
        DebugPrimitiveBuffer buffer,
        IDdsReader<DebugPrimitivesBatch>? reader = null,
        byte? filterNodeId = null)
    {
        _buffer       = buffer;
        _reader       = reader;
        _filterNodeId = filterNodeId;
    }

    // Called by IgApplication every render tick (not ECS frame).
    // Reads any pending DDS batches and replaces the buffer contents.
    public void PollAndApply()
    {
        if (_reader == null) return;

        DebugPrimitivesBatch? latest = null;
        while (_reader.TryRead(out var batch))
        {
            if (_filterNodeId.HasValue && batch.NodeId != _filterNodeId.Value) continue;
            latest = batch;
        }

        if (!latest.HasValue) return;

        _buffer.Clear();
        foreach (ref readonly var p in latest.Value.Primitives.AsSpan())
            _buffer.AppendRaw(in p); // new zero-copy append method; see constraints.
    }
}
```

**New `AppendRaw` method** on `DebugPrimitiveBuffer`:
```csharp
// Appends a primitive directly without going through a draw method.
// Used by network ingress to restore received primitives into the buffer.
public void AppendRaw(in DebugPrimitive primitive)
{
    int slot = Interlocked.Increment(ref _count) - 1;
    if (slot >= _buffer.Length)
    {
        Interlocked.Decrement(ref _count);
        DroppedCount++;
        return;
    }
    _buffer[slot] = primitive;
}
```

**`IgApplication.cs` modifications:**
1. Remove the `DataDrivenGizmoSystem` registration from the ECS kernel. Comment with:
   ```csharp
   // DataDrivenGizmoSystem is NOT registered in IG. IG is a dumb terminal.
   // Primitives arrive via DebugPrimitivesIngressTranslator (see _ingressTranslator).
   ```
2. Remove the `StatelessGizmoSystem` registration from the ECS kernel. Same comment.
3. Keep `GizmoInteractionEgressSystem` — IG still sends interaction events.
4. Construct `DebugPrimitivesIngressTranslator` with the DDS reader and wire its `PollAndApply()`
   call into the render loop (before `DebugGizmoLayer.Draw`).

**IG process summary after this task:**
- Receives `DebugPrimitivesBatch` from DDS → populates local `DebugPrimitiveBuffer`.
- `DebugGizmoLayer.Draw` renders whatever is in the buffer.
- Operator clicks → `GizmoInteractionProxyTool` → `FdpEventBus` → `GizmoInteractionEgressSystem`
  → DDS → SimHost's `GizmoInteractionIngressSystem` → SimHost `FdpEventBus` → gizmo logic.

**Constraints:**
- `IgApplication` must continue to compile without `DataDrivenGizmoSystem` or
  `StatelessGizmoSystem`. Remove any registration calls and unused field declarations.
- `GizmoRegistrar.RegisterAll` is also removed from IG: the local process no longer needs
  gizmo definitions. Remove the call and the associated DI setup.
- `_buffer.AppendRaw` must be safe to call in a tight loop (no allocation, same overflow
  behavior as other draw methods).
- If `_reader` is null (local development mode without a running SimHost), the buffer is never
  populated by the translator; primitives must still be populatable by calling draw methods
  directly (for standalone tests).
- `PollAndApply` is not called on the ECS thread; it is called from the Raylib render loop
  thread. This is safe because the buffer uses `Interlocked` for count reservation.
- Use `latest` (most recent batch) semantics: discard all but the newest received batch each
  render tick. This prevents stale primitive accumulation on slow networks.

**Success conditions:**
- SC-GZ038-1: After `DebugPrimitivesIngressTranslator.PollAndApply()`, the `DebugPrimitiveBuffer`
  contains exactly the primitives from the most recent received `DebugPrimitivesBatch` (verified
  with a mock DDS reader supplying two batches in sequence; buffer should hold the second one only).
- SC-GZ038-2: `IgApplication` compiles and starts without `DataDrivenGizmoSystem` or
  `StatelessGizmoSystem` being registered.
- SC-GZ038-3: When `_reader` is null, `PollAndApply` returns without exception and the buffer is
  unchanged.
- SC-GZ038-4: When `_filterNodeId` is set, batches from other nodes are discarded (buffer not
  populated from them).
- SC-GZ038-5: `_buffer.AppendRaw` with a full buffer (capacity exhausted) increments
  `DroppedCount` and does not throw.
- SC-GZ038-6: The `GizmoInteractionEgressSystem` is still registered in `IgApplication` and
  correctly forwards interaction events to DDS after this change.
- SC-GZ038-7 (regression): `DebugGizmoLayer.Draw` still renders primitives correctly after they
  were populated via `AppendRaw` rather than via draw methods.

---

## Phase 13: Undo/Redo Semantics

**Background:** An interactive remote visualization framework without undo/redo is a liability
for tactical authoring: one misplaced drag commit permanently mutates the ECS state with no
recovery path short of scenario reload. The design explicitly allows gizmo interactions to trigger
ECS mutations (via `IEntityCommandBuffer` on commit), making an undo stack a structural necessity.

---

### TASK-GZ039 — Undo/Redo Stack for Gizmo Interactions

**Design reference:** DESIGN.md §4.2 (`GizmoInteractionCommitEvent`), DESIGN.md §2.4

**Scope:**
Define the `IGizmoUndoRecord` contract and `GizmoUndoStack` data structure. Integrate with the
`GizmoInteractionCommitEvent` pipeline so that committed gizmo interactions can be undone.
Add keyboard shortcut handling (Ctrl+Z / Ctrl+Y) in the IG input layer.

**Files to create** in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/UndoRedo/`:
- `IGizmoUndoRecord.cs`
- `GizmoUndoStack.cs`

**Modify:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`

**`IGizmoUndoRecord`:**
```csharp
public interface IGizmoUndoRecord
{
    // Human-readable description for status bar (e.g. "Move entity 42 from (0,0) to (5,3)").
    string Description { get; }

    // Called when the user triggers Redo. Must reapply the change via cmd.
    void Redo(IEntityCommandBuffer cmd);

    // Called when the user triggers Undo. Must revert the change via cmd.
    void Undo(IEntityCommandBuffer cmd);
}
```

Both `Redo` and `Undo` must be idempotent (calling them twice in sequence must leave ECS in the
same state as calling them once).

**`GizmoUndoStack`:**
```csharp
public sealed class GizmoUndoStack
{
    private readonly Stack<IGizmoUndoRecord> _undoStack = new();
    private readonly Stack<IGizmoUndoRecord> _redoStack = new();

    public int MaxDepth { get; init; } = 50;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string UndoDescription => CanUndo ? _undoStack.Peek().Description : string.Empty;
    public string RedoDescription => CanRedo ? _redoStack.Peek().Description : string.Empty;

    // Call after a successful GizmoInteractionCommitEvent is handled.
    // Clears the redo stack (new branch).
    public void Push(IGizmoUndoRecord record) { ... }

    // Call when the user presses Ctrl+Z.
    public void Undo(IEntityCommandBuffer cmd) { ... }

    // Call when the user presses Ctrl+Y or Ctrl+Shift+Z.
    public void Redo(IEntityCommandBuffer cmd) { ... }
}
```

`Push` behavior: push `record` onto `_undoStack`. If `_undoStack.Count > MaxDepth`, remove the
bottom entry (oldest). Clear `_redoStack` (a new action invalidates the redo history).

`Undo` behavior: pop from `_undoStack`, call `record.Undo(cmd)`, push to `_redoStack`.

`Redo` behavior: pop from `_redoStack`, call `record.Redo(cmd)`, push to `_undoStack`.

**Extend `IStatefulGizmo`** with an optional default-implemented method:
```csharp
// Returns a record for the most recent commit, or null if the gizmo does not support undo.
// Called by DataDrivenGizmoSystem immediately after processing GizmoInteractionCommitEvent.
// Default implementation returns null (no undo support).
virtual IGizmoUndoRecord? CreateUndoRecord(GizmoInteractionCommitEvent commit) => null;
```

**`DataDrivenGizmoSystem` integration:**
After each `GizmoInteractionCommitEvent` is drained from the bus, for the gizmo instance
that owns the committed `PickToken.Target`:
```csharp
var record = gizmoInstance.CreateUndoRecord(commitEvent);
if (record != null)
    _undoStack?.Push(record);
```
`_undoStack` is an optional constructor parameter (null in tests that do not need undo).

**Keyboard shortcut integration:**
In `Hrot.IG`, the `IgApplication` keyboard handler (or a dedicated `GizmoUndoShortcutHandler`):
- Ctrl+Z → `if (_undoStack.CanUndo) _undoStack.Undo(_commandBuffer)`
- Ctrl+Y or Ctrl+Shift+Z → `if (_undoStack.CanRedo) _undoStack.Redo(_commandBuffer)`
The `IEntityCommandBuffer` here is the IG-side command buffer (mutations are then serialized
and forwarded to SimHost via the network command channel — or applied locally in standalone mode).

**Constraints:**
- `GizmoUndoStack` is not thread-safe; it is only touched from the ECS/render thread.
- Gizmos that do not override `CreateUndoRecord` implicitly opt out of undo (return null).
- The undo stack is cleared on scenario reload or `SimulationResetEvent` (the ECS system
  subscribes to the reset event and calls a new `GizmoUndoStack.Clear()` method).
- Maximum depth defaults to 50 to bound memory usage. Each `IGizmoUndoRecord` is a tiny object
  (two world positions + entity reference is typical). At 50 records the memory impact is trivial.

**Success conditions:**
- SC-GZ039-1: `Push(record)` followed by `Undo(cmd)` calls `record.Undo(cmd)` and moves the
  record to `_redoStack`.
- SC-GZ039-2: `Undo(cmd)` followed by `Redo(cmd)` calls `record.Redo(cmd)` and moves the
  record back to `_undoStack`.
- SC-GZ039-3: `Push` when `_undoStack.Count == MaxDepth` drops the oldest entry and adds the
  new one (verified: `_undoStack.Count` does not exceed `MaxDepth`).
- SC-GZ039-4: `Push` clears `_redoStack` (verified: after `Undo` then `Push`, `CanRedo == false`).
- SC-GZ039-5: `Undo` when `CanUndo == false` is a no-op (no exception).
- SC-GZ039-6: `Redo` when `CanRedo == false` is a no-op (no exception).
- SC-GZ039-7: `DataDrivenGizmoSystem` calls `CreateUndoRecord` after a commit event and pushes
  the returned record onto `_undoStack` (verified via a mock gizmo with a stub
  `CreateUndoRecord` that returns a mock record, then asserting `_undoStack.CanUndo == true`).
- SC-GZ039-8: A gizmo returning `null` from `CreateUndoRecord` does NOT push anything onto
  `_undoStack` (verified: `CanUndo == false` after commit from a no-undo gizmo).

---

## Phase 14: Infrastructure Safety Fixes

**Background:** D-001 in the debt tracker identifies a severe concurrency hazard in
`StringInternMap`: the internal `Dictionary<uint, string>` is not thread-safe, yet
`DrawTextLong` (which calls `Intern`) can be invoked by multiple ECS systems concurrently
during parallel entity iteration. This is P1 blocking — it will eventually throw
`IndexOutOfRangeException` or silently corrupt string resolution in production. This phase
contains the single-task P1 fix.

---

### TASK-GZ040 — Fix StringInternMap Concurrency Hazard (D-001, P1 Blocking)

**Design reference:** DESIGN.md §1.2, DEBT-TRACKER.md D-001

**Scope:**
Replace the unsynchronized `Dictionary<uint, string>` in `StringInternMap` with a
`ConcurrentDictionary<uint, string>`. Remove the false comment claiming thread-safety.
Verify that all call sites are compatible with the new implementation.

**Files to modify:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/StringInternMap.cs`

**Required changes:**

1. Replace field declaration:
   ```csharp
   // BEFORE:
   private readonly Dictionary<uint, string> _map = new();

   // AFTER:
   private readonly ConcurrentDictionary<uint, string> _map = new();
   ```

2. Update `Intern` method:
   ```csharp
   // BEFORE:
   public void Intern(uint hash, string fullText)
   {
       if (!_map.ContainsKey(hash))
           _map[hash] = fullText;
   }

   // AFTER:
   public void Intern(uint hash, string fullText)
       => _map.TryAdd(hash, fullText);
   ```
   `TryAdd` is atomic: if the key already exists, the call is a no-op. No lock needed.

3. Update `TryResolve` method:
   ```csharp
   // BEFORE:
   public string? TryResolve(uint hash)
       => _map.TryGetValue(hash, out var v) ? v : null;

   // AFTER (no change needed — TryGetValue is already lock-free on ConcurrentDictionary):
   public string? TryResolve(uint hash)
       => _map.TryGetValue(hash, out var v) ? v : null;
   ```

4. Remove the false comment. Locate and delete the comment that reads
   `// Thread-safe string intern side-channel` (or any equivalent claim).
   Add the correct comment: `// Concurrent-safe intern map; TryAdd/TryGetValue are lock-free.`

5. Update `Entries` property:
   ```csharp
   // No change needed for the property signature; ConcurrentDictionary implements
   // IReadOnlyDictionary<TKey, TValue>. Verify the return type is compatible.
   public IReadOnlyDictionary<uint, string> Entries => _map;
   ```

6. Update `Flush` method (if it iterates and removes entries):
   ```csharp
   // ConcurrentDictionary.Clear() is thread-safe; no change to the API.
   public void Flush() => _map.Clear();
   ```

**Verify call sites:**
- `DebugPrimitiveBuffer.DrawTextLong` calls `_internMap.Intern(hash, text)` — compatible.
- `DebugPrimitiveRenderer2D.DispatchShape` calls `_internMap?.TryResolve(hash)` — compatible.
- `StringInternBatchPublisherSystem` reads `_internMap.Entries` — compatible (snapshot
  enumeration over a `ConcurrentDictionary` is safe, though not atomic; for the publisher's
  purposes, a best-effort snapshot is sufficient).

**Constraints:**
- Do not introduce explicit locks (`lock`, `Monitor`, `Mutex`). `ConcurrentDictionary`'s
  built-in lock striping is sufficient for this use case.
- The `Flush` semantics remain: clear the entire map. This is acceptable because the renderer
  falls back to the `FixedString32` preview when a hash is absent.
- No change to the public interface beyond the false comment removal.

**Success conditions:**
- SC-GZ040-1: `StringInternMap` compiles with `ConcurrentDictionary` as the backing store.
- SC-GZ040-2: `Intern(hash, text)` called concurrently from two threads does not throw and
  leaves exactly one entry for the given hash (verified via a `Parallel.For` stress test with
  the same hash from 32 concurrent threads).
- SC-GZ040-3: `TryResolve(hash)` called concurrently while `Intern` is active does not throw
  (verified via concurrent read/write stress test; result is either the value or null — both
  are acceptable, never an exception).
- SC-GZ040-4: The false `// Thread-safe string intern side-channel` comment (or equivalent) is
  absent from the source file after this change.
- SC-GZ040-5: `DrawTextLong` called from `DataDrivenGizmoSystem` and `StatelessGizmoSystem` in
  parallel (multi-threaded ECS) does not throw over 10,000 iterations.

---

## Phase 15: Assembly Segregation

**Background:** Phase 1 types (`DebugPrimitive`, `IDebugDrawBuilder`, etc.) are currently
compiled into `Fdp.Toolkits`, which also contains UI helpers, analyzers, and general-purpose
utilities. Any external tool that needs only the primitive protocol is forced to depend on all
of `Fdp.Toolkits`. Similarly, the Phase 6 DDS schemas are in `Fdp.Toolkits`, forcing a full
`Fdp.Toolkits` reference just to subscribe to a DDS topic. The correct architecture is four
distinct planes: Contracts, Network, Execution, Presentation. This phase creates the first two
standalone assemblies and migrates the relevant types.

---

### TASK-GZ041 — Create Fdp.Diagnostics.Contracts Assembly and Migrate Phase 1 Types

**Design reference:** DESIGN.md §1 (Core Primitive Protocol)

**Scope:**
Create a new standalone project `Fdp.Diagnostics.Contracts` that references only `Fdp.Core`.
Migrate all Phase 1 types into it. Update all downstream projects to reference the new assembly
instead of `Fdp.Toolkits` for these types.

**Files to create:**
- `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Fdp.Diagnostics.Contracts.csproj`
- Move (not copy) from `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Primitives/`:
  - `Rgba32.cs`
  - `PipelineTarget.cs` (and `CoordinateSpace.cs`, `SizeMode.cs`, `PickToken.cs` if separate files)
  - `DebugPrimitive.cs`
- Move from `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`:
  - `IDebugDrawBuilder.cs`
  - `DebugPrimitiveBuffer.cs`
  - `StringInternMap.cs`

**New `.csproj`:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <RootNamespace>Fdp.Diagnostics.Contracts</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Engine\Fdp.Core\Fdp.Core.csproj" />
  </ItemGroup>
</Project>
```

No reference to `Fdp.Toolkits`, `Fdp.Presentation`, or any subsystem project.

**Namespace:** Retain `Fdp.Toolkit.Diagnostics.Gizmos` in the moved source files to avoid
breaking changes in calling code. (The new project RootNamespace is cosmetic; the type
namespaces in source files remain unchanged.)

**Add to `FDP.sln`:** Register the new project in the `FDP.sln` solution file.

**Update references in downstream projects:**
- `Fdp.Toolkits` loses the migrated source files; add a project reference to
  `Fdp.Diagnostics.Contracts` so its own code and any types that still remain in it can use
  the primitives via a transitive reference.
- `Fdp.Presentation` and `Hrot.IG`, `Hrot.SimHost`, `Hrot.CGF`, `Hrot.Network.NED`: replace
  or supplement their `Fdp.Toolkits` reference with `Fdp.Diagnostics.Contracts` where the only
  dependency is on the primitive protocol types.
- All `*.Tests` projects that reference `Fdp.Toolkits` only for GZ001–GZ003 primitives: update
  to reference `Fdp.Diagnostics.Contracts` instead.

**Constraints:**
- The `AllowUnsafeBlocks` flag is required for `DebugPrimitive`'s `[StructLayout(Explicit)]`
  overlapping and `StringInternMap`'s unsafe span techniques.
- The migration is a file-move (delete from old location, create in new). Do not leave copies
  in both places.
- `Fdp.Diagnostics.Contracts` must NOT reference `Fdp.Toolkits` (would reintroduce the cycle).

**Success conditions:**
- SC-GZ041-1: `Fdp.Diagnostics.Contracts` project compiles standalone (without `Fdp.Toolkits`
  in the build graph) and all GZ001–GZ003/GZ019 unit tests pass when their project reference
  is changed to `Fdp.Diagnostics.Contracts`.
- SC-GZ041-2: `Fdp.Toolkits` still compiles after the migration (it references
  `Fdp.Diagnostics.Contracts` transitively and the moved types are resolved correctly).
- SC-GZ041-3: A brand-new test project that references only `Fdp.Diagnostics.Contracts` and
  `Fdp.Core` can instantiate `DebugPrimitiveBuffer`, call `DrawLine`, and read back the
  primitive — with no reference to `Fdp.Toolkits` in its `.csproj`.
- SC-GZ041-4: All existing tests in the solution continue to pass (no regressions from the
  reference updates).
- SC-GZ041-5: `Fdp.Diagnostics.Contracts.csproj` is listed in `FDP.sln` and the solution
  builds cleanly with `dotnet build FDP/FDP.sln`.

---

### TASK-GZ042 — Create Fdp.Diagnostics.Network Assembly and Migrate Phase 6 DDS Schemas

**Design reference:** DESIGN.md §6

**Scope:**
Create a new standalone project `Fdp.Diagnostics.Network` that references only
`Fdp.Diagnostics.Contracts` and the CycloneDDS ExtDep. Migrate all Phase 6 DDS schema types
into it. Clients that only need to subscribe to `DebugPrimitivesBatch` no longer pull in
all of `Fdp.Toolkits`.

**Files to create:**
- `FDP/Diagnostics/Fdp.Diagnostics.Network/Fdp.Diagnostics.Network.csproj`
- Move from `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/`:
  - `DebugPrimitivesBatch.cs`
  - `GizmoUiState.cs`
  - `StringInternBatch.cs`
- Move from `Hrot/Network/Hrot.Network.NED/Gizmos/` (once created by GZ037):
  - `GizmoInteractionBatch.cs`
  - `GizmoInteractionEventKind.cs`

**New `.csproj`:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <RootNamespace>Fdp.Diagnostics.Network</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Fdp.Diagnostics.Contracts\Fdp.Diagnostics.Contracts.csproj" />
    <ProjectReference Include="..\..\ExtDeps\FastCycloneDds\FastCycloneDds.csproj" />
  </ItemGroup>
</Project>
```

No reference to `Fdp.Toolkits` or `Fdp.Core` directly (`Fdp.Core` arrives transitively through
`Fdp.Diagnostics.Contracts`).

**Add to `FDP.sln`:** Register the new project.

**Update references in downstream projects:**
- `Hrot.Network.NED` currently references `Fdp.Toolkits` for the DDS schemas. Replace with
  `Fdp.Diagnostics.Network`.
- `Hrot.IG` (ingress translator for GZ038): add `Fdp.Diagnostics.Network` reference.
- `Hrot.SimHost` (egress publisher for GZ033): add `Fdp.Diagnostics.Network` reference.
- `Fdp.Toolkits` loses the migrated DDS source files; add `Fdp.Diagnostics.Network` reference
  for any remaining code in `Fdp.Toolkits` that uses these types.

**Constraints:**
- `Fdp.Diagnostics.Network` must NOT reference `Fdp.Toolkits` or any subsystem project.
- The CycloneDDS partial class code generation (from `[DdsTopic]` attributes) must run
  correctly in the new project. Verify the analyzer/generator is hooked up in the `.csproj`.
- The `GizmoInteractionBatch` migration from `Hrot.Network.NED` into `Fdp.Diagnostics.Network`
  moves it from the application layer to the contracts/network layer — this is architecturally
  correct because interaction events are part of the diagnostic protocol, not the simulation
  domain.

**Success conditions:**
- SC-GZ042-1: `Fdp.Diagnostics.Network` compiles standalone (without `Fdp.Toolkits` in the
  build graph).
- SC-GZ042-2: DDS round-trip test for `DebugPrimitivesBatch` passes when the test references
  only `Fdp.Diagnostics.Network` and `Fdp.Diagnostics.Contracts`.
- SC-GZ042-3: `Hrot.Network.NED` compiles after replacing its `Fdp.Toolkits`-based DDS schema
  reference with `Fdp.Diagnostics.Network`.
- SC-GZ042-4: All existing `DebugPrimitivesBatch` and `StringInternBatch` tests continue to
  pass against the new project location.
- SC-GZ042-5: `Fdp.Diagnostics.Network.csproj` is listed in `FDP.sln` and the full solution
  builds cleanly with `dotnet build FDP/FDP.sln`.

