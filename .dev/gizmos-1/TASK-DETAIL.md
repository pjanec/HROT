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
- `PipelineTarget`: `[Flags] enum : byte` — `None=0`, `Map2D=1`, `Viewport3D=2`, `All=3`.
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

**Success conditions:**
- SC-GZ014-1: A `FixedString32` with bytes `[0x01, 'H', 'i', 0x02, '!', 0x00]` renders "Hi" in
  red and "!" in green.
- SC-GZ014-2: A badge with no control bytes renders entirely in the default color.
- SC-GZ014-3: Two badges for the same entity render on separate lines (Y offset per badge).
- SC-GZ014-4 (negative): A badge primitive for an entity without a `SimTransform` is silently
  skipped (no exception).
- SC-GZ014-5: `RichTextRenderer` produces no heap allocations per call (verify via allocation test).

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
- Reads local capability configuration (from a startup config file or hardcoded defaults for the
  local 2D viewer) and publishes one `IGCapabilitiesAnnounce` record.
- For the local 2D IG viewer: `SupportedTargets = PipelineTarget.Map2D`,
  `SupportedLayerMask = 0xFFFF`, `SupportedShapes = 0xFF` (all shapes).

**Note:** The simulation node receiving these announcements can use them as an optimization hint
to skip emitting `Viewport3D`-only primitives when no 3D client is connected. However, the
simulation node must continue emitting `All`-target primitives regardless (clients silently drop
unsupported shapes). This is a **best-effort optimization** path, not a hard requirement.

**Constraints:**
- `IGCapabilitiesAnnounce` topic name must not collide with existing DDS topics.
- System runs once; does not re-publish each frame.

**Success conditions:**
- SC-GZ018-1: `IGCapabilitiesAnnounce` DDS schema compiles without errors.
- SC-GZ018-2: `IGCapabilitiesPublisherSystem` publishes exactly one `IGCapabilitiesAnnounce` record
  during the `Initialization` phase (verified by asserting exactly one DDS writer call).
- SC-GZ018-3: A record with `SupportedTargets = Map2D` and `SupportedLayerMask = 0xFFFF` is
  serialized and deserialized correctly in a round-trip test.

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

