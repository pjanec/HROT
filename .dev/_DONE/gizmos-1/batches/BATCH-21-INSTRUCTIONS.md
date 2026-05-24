# BATCH-21: Production Map Rendering Migration — Part 1 (Additive)

**Batch Number:** BATCH-21
**Tasks:** TASK-GZ057 (partial: SimHost + IG gizmos), TASK-GZ058 (all layer projectors)
**Phase:** Phase 20 — Production Map Rendering Migration
**Priority:** HIGH
**Dependencies:** BATCH-20 (GZ055-GZ056) — APPROVED

> **SCOPE BOUNDARY**: This batch is **purely additive**. Do NOT delete or modify any existing
> `IMapLayer` implementations, `IVisualizerAdapter` subclasses, `PerspectiveEntityVisualizerBase`,
> `IVisualizerAdapter`, `EntityRenderLayer`, or any other legacy rendering infrastructure.
> Deletion is deferred to BATCH-22 (GZ059) after the new gizmos are verified to render correctly.

---

## Onboarding & Workflow

### Developer Instructions

This batch converts hardcoded map layers and entity visualizers into declarative
`IStatelessGizmo` projectors that emit `DebugPrimitive` structs via `IDebugDrawBuilder`.
The new gizmos coexist alongside the legacy layers — both render simultaneously during
this transitional batch.

### Required Reading (IN ORDER)

1. **Task Definitions:** `.dev/gizmos-1/TASK-DETAIL.md` — GZ057 and GZ058 specs (lines ~1512-1588)
2. **Design Document:** `.dev/gizmos-1/DESIGN.md` — DebugPrimitive layout, CoordinateSpace, SizeMode
3. **Previous Review:** `.dev/gizmos-1/reviews/BATCH-20-REVIEW.md` — context from previous batch
4. **Existing Gizmo Pattern:** `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/HealthBarGizmo.cs` — reference implementation

### Source Code Locations

- **FDP draw builder interface:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/IDebugDrawBuilder.cs`
- **FDP buffer implementation:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/DebugPrimitiveBuffer.cs`
- **FDP primitive struct:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitive.cs`
- **Gizmo infrastructure:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`
- **SimHost composition root:** `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
- **IG composition root:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
- **IG GizmoRegistrar:** `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs`
- **CGF composition root:** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`
- **Legacy layers (READ ONLY — do not modify):**
  - `Hrot/Subsystems/Hrot.SimHost/Visualization/SimHostVehicleVisualizer.cs`
  - `Hrot/Subsystems/Hrot.CGF/CgfDebugVisualizerAdapter.cs`
  - `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Adapters/SstVisualizerAdapter.cs`
  - `Hrot/Subsystems/Hrot.IG/Layers/EffectRenderLayer.cs`
  - `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/RouteRenderLayer.cs`
  - `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/MapOverlayRenderLayer.cs`
  - `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Rendering/MissionRenderLayer.cs`

### Test Projects

- `Hrot/Subsystems/Hrot.SimHost.Tests/` — for SimHostEntityPresentationGizmo tests
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/` — for IG and GZ058 gizmo tests

### Build & Test Commands

```
dotnet build IOS-IG-SimHost.sln --no-incremental
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --filter "FullyQualifiedName~Gizmo"
dotnet test Hrot/Subsystems/Hrot.IG.Tests/Hrot.IG.Tests.csproj --filter "FullyQualifiedName~Gizmo"
```

### Report Submission

**When done, submit your report to:** `.dev/gizmos-1/reports/BATCH-21-REPORT.md`

**If you have questions, create:** `.dev/gizmos-1/questions/BATCH-21-QUESTIONS.md`

---

## Context

The `GizmoMap` presentation pipeline (`DebugPrimitiveBuffer` → DDS → `DebugPrimitiveRenderer2D`)
is now fully operational (BATCH-19/20). The final migration step is to:

1. **GZ057**: Replace per-entity Raylib visualizers (`PerspectiveEntityVisualizerBase` subclasses)
   with `IStatelessGizmo` projectors that emit `SpatialAnchor` + `SemanticShape` primitives.
2. **GZ058**: Replace IG-side `IMapLayer` implementations (EffectRenderLayer, RouteRenderLayer,
   MapOverlayRenderLayer, MissionRenderLayer) with `IStatelessGizmo` projectors that emit
   `Sphere` and `Line` primitives.

Both sets of new gizmos will coexist with the old layers during this batch (BATCH-22/GZ059
will delete the legacy code once visual parity is verified).

### Key Coordinate Conventions

- **`SimTransform.Position`**: X = East (canvas X), Y = North (canvas Y), Z = Up (altitude)
  — same convention as `EffectRenderLayer` and `PerspectiveEntityVisualizerBase.GetPosition`.
- **`RouteWaypoint.Position`**: X = East (canvas X), Y = unused, Z = North (canvas Y)
  — as documented in `RouteRenderLayer.ToCanvas()`.
- **`SpatialAnchor`** payload fields: `AnchorWorldX` = canvas X, `AnchorWorldY` = canvas Y.
- **`NetworkIdentity.Value`** (`long`) is the stable network ID used to link
  `SpatialAnchor.NetworkId` to `SemanticShape.AnchorIndex` (`(int)networkId`).

---

## Batch Objectives

1. Add `DrawSpatialAnchor` and `DrawSemanticShape` default methods to `IDebugDrawBuilder`.
2. Implement them in `DebugPrimitiveBuffer`.
3. Create three `IStatelessGizmo` entity presentation gizmos (GZ057).
4. Create four `IStatelessGizmo` layer projector gizmos (GZ058).
5. Wire all new gizmos into their respective composition roots.
6. Write unit tests verifying primitive emission correctness for each new gizmo.

---

## Tasks

---

### Task 1: Extend IDebugDrawBuilder with SpatialAnchor and SemanticShape methods

**Files:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/IDebugDrawBuilder.cs` (MODIFY),
`FDP/Diagnostics/Fdp.Diagnostics.Contracts/DebugPrimitiveBuffer.cs` (MODIFY)

Add two new **default interface methods** to `IDebugDrawBuilder` (C# 8 default implementations
— no-op bodies so all existing stub implementations compile without changes):

```csharp
// Emits a SpatialAnchor primitive: pre-resolved world position + heading for decoupled viewers.
// Must be emitted BEFORE the corresponding SemanticShape (same networkId).
void DrawSpatialAnchor(
    long networkId,
    float worldX,
    float worldY,
    float worldZ,
    float headingDeg,
    byte layer = 0) { }

// Emits a SemanticShape in CoordinateSpace.EntityLocal, linked to a SpatialAnchor
// via AnchorIndex = (int)networkId.
void DrawSemanticShape(
    long networkId,
    ulong profileId,
    float lengthMeters = 0f,
    float widthMeters  = 0f,
    uint  conditionMask = 0,
    byte  layer = 0) { }
```

**Implement both in `DebugPrimitiveBuffer`** by overriding the defaults and calling `Append()`:

For `DrawSpatialAnchor`:
- `p.Shape = DebugPrimitiveShape.SpatialAnchor`
- `p.TargetView = PipelineTarget.All`
- `p.DebugLayer = layer`
- `p.NetworkId = networkId`
- `p.AnchorWorldX = worldX; p.AnchorWorldY = worldY; p.AnchorWorldZ = worldZ`
- `p.Heading = headingDeg`

For `DrawSemanticShape`:
- `p.Shape = DebugPrimitiveShape.SemanticShape`
- `p.Space = CoordinateSpace.EntityLocal`
- `p.TargetView = PipelineTarget.All`
- `p.DebugLayer = layer`
- `p.AnchorIndex = (int)networkId`  ← links to the SpatialAnchor with matching NetworkId
- `p.ProfileId = profileId`
- `p.LengthMeters = lengthMeters; p.WidthMeters = widthMeters`
- `p.ConditionMask = conditionMask`

> Note: `AnchorIndex` is an `int`, `networkId` is a `long`. For realistic network IDs
> (assigned sequentially from small integers), the narrowing cast is safe.

**Tests** (add to `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ContractsStandaloneTests.cs`
or a new file in the same project):
- `DrawSpatialAnchor_EmitsCorrectShape`: assert `GetFrame()[0].Shape == SpatialAnchor`,
  `NetworkId == 42L`, `AnchorWorldX == 100f`, `Heading == 45f`.
- `DrawSemanticShape_EmitsCorrectShape`: assert `Shape == SemanticShape`,
  `Space == EntityLocal`, `AnchorIndex == 42`, `ProfileId == 0xCAFEUL`,
  `LengthMeters == 8f`, `ConditionMask == 1u`.

---

### Task 2: SimHostEntityPresentationGizmo (GZ057 — SimHost)

**File:** `Hrot/Subsystems/Hrot.SimHost/Gizmos/SimHostEntityPresentationGizmo.cs` (NEW)

```csharp
[GizmoProjector(typeof(SimTransform), typeof(NetworkIdentity))]
public sealed class SimHostEntityPresentationGizmo : IStatelessGizmo
```

**`Draw()` logic:**
1. Read `NetworkIdentity.Value` → `long networkId`.
2. Read `SimTransform.Position` (X=East, Y=North) and `SimTransform.Rotation`.
3. Extract heading yaw in degrees from the quaternion (rotation around Z axis, Z=Up):
   ```csharp
   float yaw = MathF.Atan2(
       2f * (q.W * q.Z + q.X * q.Y),
       1f - 2f * (q.Y * q.Y + q.Z * q.Z));
   float headingDeg = yaw * (180f / MathF.PI);
   ```
4. Call `draw.DrawSpatialAnchor(networkId, tf.Position.X, tf.Position.Y, tf.Position.Z, headingDeg)`.
5. Read `VehicleParams` if present (`view.HasComponent<VehicleParams>(entity)`) for `length` and `width`; otherwise use `0f`.
6. Call `draw.DrawSemanticShape(networkId, profileId: 0UL, length, width, conditionMask: 0u)`.

**Namespace:** `Hrot.SimHost.Gizmos`

**Required usings:** `CarKinem.Core` (VehicleParams), `Fdp.Core`, `Fdp.ModuleHost.Abstractions`,
`Fdp.Toolkit.Diagnostics.Gizmos`, `Fdp.Toolkit.Replication.Components` (NetworkIdentity),
`Hrot.Common.Components` or wherever `SimTransform` lives in the SimHost assembly.

**Registration in `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`:**

The source generator creates `Hrot.SimHost.Gizmos.GizmoRegistrar.RegisterAll()` automatically
when `[GizmoProjector]` is on the class. However, `SimHostApp.cs` does not currently call
a `GizmoRegistrar` for Hrot.SimHost. Choose one of:

**Option A** (preferred): Create `Hrot/Subsystems/Hrot.SimHost/Gizmos/GizmoRegistrar.cs`:
```csharp
namespace Hrot.SimHost.Gizmos
{
    public static partial class GizmoRegistrar
    {
        public static void Register(
            Fdp.Toolkit.Diagnostics.Gizmos.GizmoRegistry gizmoRegistry,
            Fdp.Toolkit.Diagnostics.Gizmos.StatelessGizmoRegistry statelessRegistry)
        {
            RegisterAll(gizmoRegistry, statelessRegistry, settings: null!);
        }
    }
}
```
Then in `SimHostApp.cs`, after creating `_statelessGizmoRegistry`, add:
```csharp
Hrot.SimHost.Gizmos.GizmoRegistrar.RegisterAll(
    _gizmoRegistry!, _statelessGizmoRegistry!, settings: new Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingsRegistry());
```

**Option B** (simpler): Manually register in `SimHostApp.cs`:
```csharp
_statelessGizmoRegistry!.Register(
    new Hrot.SimHost.Gizmos.SimHostEntityPresentationGizmo(),
    new[] { typeof(Fdp.Toolkit.Movement.SimTransform), typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity) });
```
Find the exact component types by looking at the `[GizmoProjector]` attribute you declared.

Registration must occur **after** all component types are registered in the ECS world
(i.e., after the `HrotSharedComponentRegistry.RegisterAll(_world)` or equivalent calls)
and **before** `_kernel.Initialize()`.

---

### Task 3: CgfEntityPresentationGizmo (GZ057 — CGF)

**File:** `Hrot/Subsystems/Hrot.CGF/Gizmos/CgfEntityPresentationGizmo.cs` (NEW)

```csharp
[GizmoProjector(typeof(SimTransform), typeof(NetworkIdentity))]
public sealed class CgfEntityPresentationGizmo : IStatelessGizmo
```

**`Draw()` logic** (similar to SimHostEntityPresentationGizmo with one difference):
1. Read `NetworkIdentity.Value` → `networkId`.
2. Read position from `SimTransform` (same as above).
3. Optionally: if `view.HasComponent<NetworkTransform>(entity)`, prefer
   `NetworkTransform`'s position/rotation when available (CGF nodes may use
   `NetworkTransform` as a more current position source). Fallback to `SimTransform`.
   Check `CgfDebugVisualizerAdapter.cs` for the exact NetworkTransform field names.
4. Call `draw.DrawSpatialAnchor(...)` and `draw.DrawSemanticShape(...)` same as SimHost gizmo.

**Namespace:** `Hrot.CGF.Gizmos`

**Registration in `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`:**

CGF currently has no `StatelessGizmoSystem`. Add the full infrastructure in the
`// ── Visualization (non-headless only)` block (line ~425):

```csharp
// Gizmo infrastructure for CGF entity presentation.
var cgfGizmoBuffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
var cgfStatelessRegistry = new Fdp.Toolkit.Diagnostics.Gizmos.StatelessGizmoRegistry();
cgfStatelessRegistry.Register(
    new Hrot.CGF.Gizmos.CgfEntityPresentationGizmo(),
    new[] { typeof(Fdp.Toolkit.Movement.SimTransform), typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity) });
_context.Kernel.RegisterGlobalSystem(
    new Fdp.Toolkit.Diagnostics.Gizmos.Systems.StatelessGizmoSystem(cgfStatelessRegistry, cgfGizmoBuffer));
// Add a DebugGizmoLayer so the buffer is rendered on CGF's canvas:
if (!_headless)
{
    var cgfGizmoLayer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, cgfGizmoBuffer, _context.World.Bus, _canvas);
    _canvas.AddLayer(cgfGizmoLayer);
}
```

> NOTE: The `StatelessGizmoRegistry.Register` call must occur BEFORE `_context.Kernel.Initialize()`.
> Check the actual kernel init call location in `CgfSubsystem.cs` (it is in `Initialize()` at
> `_context.Kernel.Initialize()` around line 419).

---

### Task 4: IgEntityPresentationGizmo (GZ057 — IG)

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/IgEntityPresentationGizmo.cs` (NEW)

```csharp
[GizmoProjector(typeof(SimTransform), typeof(NetworkIdentity), typeof(CullingState))]
public sealed class IgEntityPresentationGizmo : IStatelessGizmo
```

**`Draw()` logic:**
1. Check `view.GetComponentRO<CullingState>(entity).IsVisible` — return early if not visible.
2. Read `NetworkIdentity.Value` → `networkId`.
3. Read `SimTransform.Position` and `Rotation` for heading.
4. Read `IgHealthState` (if present) to compute `conditionMask`:
   - `Damage >= 50f` → set `EntityShapeCondition.Damaged` bit
   - `Damage >= 90f` → also set `EntityShapeCondition.Immobile` bit (or equivalent)
5. Call `draw.DrawSpatialAnchor(networkId, tf.Position.X, tf.Position.Y, tf.Position.Z, headingDeg)`.
6. Read `VehicleParams` if available for dimensions.
7. Call `draw.DrawSemanticShape(networkId, 0UL, length, width, (uint)conditionMask)`.

**Namespace:** `Hrot.ScenarioEditor.Gizmos`

Check `CullingState` field names in `Hrot.IG.Components` (look for `IsVisible` or equivalent).

**Registration:** The Roslyn source generator will auto-create
`Hrot.ScenarioEditor.Gizmos.GizmoRegistrar.RegisterAll()`. The existing
`Hrot.IG.Gizmos.GizmoRegistrar.Register()` at
`Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs` must be updated to call it:

```csharp
// Add this line to the Register() method body:
Hrot.ScenarioEditor.Gizmos.GizmoRegistrar.RegisterAll(registry, statelessRegistry, settings);
```

**StatelessGizmoSystem in IgApplication:**

IgApplication currently has `_statelessGizmoRegistry` populated (via `GizmoRegistrar.Register`)
but `StatelessGizmoSystem` is NOT registered in the kernel (comment at line ~1250 says GZ038 removed it).

Add the system registration in `IgApplication.cs` in `InitializeNetwork()`, after
`GizmoRegistrar.Register(_gizmoRegistry, _statelessGizmoRegistry, _gizmoSettingsRegistry)`:

```csharp
// GZ057-058: re-add StatelessGizmoSystem for local presentation gizmos.
// IG remains a dumb terminal for entity position/appearance (received from SimHost via DDS).
// But IG-local data (routes, overlays, effects) is rendered here via StatelessGizmoSystem.
_kernel.RegisterGlobalSystem(new StatelessGizmoSystem(
    _statelessGizmoRegistry!,
    _gizmoBuffer!));
```

Remove or update the comment that says "StatelessGizmoSystem is NOT registered in IG."

---

### Task 5: EffectPresentationGizmo (GZ058 — IG)

**File:** `Hrot/Subsystems/Hrot.IG/Gizmos/EffectPresentationGizmo.cs` (NEW)

Replaces the rendering logic of `EffectRenderLayer` via the StatelessGizmoSystem.

```csharp
[GizmoProjector(typeof(SimTransform), typeof(VisualEffectState))]
public sealed class EffectPresentationGizmo : IStatelessGizmo
```

**`Draw()` logic** (mirror `EffectRenderLayer.Draw()` logic):
1. Read `SimTransform` for position: `worldX = tf.Position.X`, `worldY = tf.Position.Y`.
2. Read `VisualEffectState`: `effect.Type`, `effect.ColorR/G/B`, `effect.Alpha`, `effect.Scale`.
3. Compute RGBA: `byte alpha = (byte)(effect.ColorA * effect.Alpha)` (or just use `effect.Alpha`
   if `ColorA` doesn't exist — check `VisualEffectState` field names from `EffectRenderLayer.cs`).
4. If `effect.Type == EffectType.Explosion`:
   ```csharp
   draw.DrawSphere(
       new Vector3(worldX, worldY, 0f),
       effect.Scale,
       new Rgba32(effect.ColorR, effect.ColorG, effect.ColorB, alpha));
   ```
   Use `SizeMode.WorldMeters` (radius is in world units, same as Raylib inside BeginMode2D).
   
   > `DrawSphere` signature: `void DrawSphere(Vector3 center, float radius, Rgba32 color,
   > PipelineTarget target = PipelineTarget.All, byte layer = 0)` — but it does NOT have
   > a `SizeMode` parameter! Check the actual `DebugPrimitiveBuffer.DrawSphere` signature
   > and `DebugPrimitive.MakeSphere`. If SizeMode is not in the signature, set it via
   > `AppendRaw` with a manually-built primitive, OR add a `SizeMode` overload.
   >
   > Alternatively, emit the sphere using `AppendRaw`:
   > ```csharp
   > var p = DebugPrimitive.MakeSphere(new Vector3(worldX, worldY, 0f), effect.Scale, color);
   > p.SizeMode = SizeMode.WorldMeters;
   > ((DebugPrimitiveBuffer)draw).AppendRaw(p);
   > ```
   > If the `draw` parameter is not a `DebugPrimitiveBuffer` (e.g. in tests), add a
   > `DrawSphere` overload to `IDebugDrawBuilder` that accepts `SizeMode`, or cast
   > carefully. Choose the simplest approach that compiles.

5. Else if `effect.Type == EffectType.Tracer` AND `view.HasComponent<TracerTarget>(entity)`:
   ```csharp
   ref readonly var tracer = ref view.GetComponentRO<TracerTarget>(entity);
   draw.DrawLine(
       new Vector3(worldX, worldY, 0f),
       new Vector3(tracer.EndX, tracer.EndY, 0f),
       new Rgba32(effect.ColorR, effect.ColorG, effect.ColorB, alpha),
       thickness: 1f,
       SizeMode.ScreenPixels);
   ```
   Check `TracerTarget` for the exact field names (`EndX`/`EndY` or similar — look in `EffectRenderLayer.cs`).

**Namespace:** `Hrot.IG.Gizmos`

**Registration:** The source generator creates `Hrot.IG.Gizmos.GizmoRegistrar.RegisterAll()`.
The existing hand-written `Hrot.IG.Gizmos.GizmoRegistrar` partial class in `GizmoRegistrar.cs`
should call `RegisterAll(...)` (on the same class = self):

```csharp
// Add to the Register() method body:
RegisterAll(registry, statelessRegistry, settings);
```

> This works because `GizmoRegistrar.cs` and the generated `GizmoRegistrar.g.cs` are both
> `partial class GizmoRegistrar` in `namespace Hrot.IG.Gizmos`. The generated `RegisterAll()`
> becomes a method of the same partial class.

---

### Task 6: RouteGizmo (GZ058)

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/RouteGizmo.cs` (NEW)

Replaces the rendering logic of `RouteRenderLayer`.

```csharp
[GizmoProjector(typeof(TkbIdentity))]
public sealed class RouteGizmo : IStatelessGizmo
```

> `[GizmoProjector(typeof(TkbIdentity))]` — `RoutePlan` (managed) cannot always be used as
> a required component in `GizmoProjector` if the StatelessGizmoRegistry uses component IDs
> from `ComponentTypeRegistry`. Check whether managed components with `RegisterManagedComponent<T>()`
> are queryable this way. If they are (and they should be, since managed components also get IDs),
> use `[GizmoProjector(typeof(TkbIdentity), typeof(RoutePlan))]` for cleaner filtering.
> Otherwise, use `[GizmoProjector(typeof(TkbIdentity))]` and check `HasManagedComponent` inside.

**`Draw()` logic** (mirror `RouteRenderLayer.DrawRoute()` logic):
1. `if (!view.HasComponent<TkbIdentity>(entity)) return;`
2. `ref readonly var tkb = ref view.GetComponentRO<TkbIdentity>(entity);`
3. `if (tkb.TkbType != TkbEntityTypes.TacGraphic_Route) return;`
4. `if (!view.HasManagedComponent<RoutePlan>(entity)) return;`
5. `var plan = view.GetManagedComponentRO<RoutePlan>(entity);`
6. `if (plan.Waypoints == null || plan.Waypoints.Count == 0) return;`
7. For each consecutive waypoint pair:
   ```csharp
   // RouteWaypoint positions use X=East, Z=North (not Y=North like SimTransform)
   var a = new Vector3(plan.Waypoints[i].Position.X, plan.Waypoints[i].Position.Z, 0f);
   var b = new Vector3(plan.Waypoints[(i+1) % n].Position.X, plan.Waypoints[(i+1) % n].Position.Z, 0f);
   draw.DrawLine(a, b, NormalColor, 1f, SizeMode.ScreenPixels);
   ```
   Use `NormalColor = new Rgba32(0x44, 0x88, 0xFF, 0xFF)` (same blue as existing layer).
   Handle looping routes: if `plan.IsLoop`, add segment from last to first waypoint.

**Namespace:** `Hrot.ScenarioEditor.Gizmos`

**Registration:** auto-generated `Hrot.ScenarioEditor.Gizmos.GizmoRegistrar.RegisterAll()`.
Add the call in `Hrot.IG.Gizmos.GizmoRegistrar.Register()`:
```csharp
Hrot.ScenarioEditor.Gizmos.GizmoRegistrar.RegisterAll(registry, statelessRegistry, settings);
```
(This one call registers ALL `Hrot.ScenarioEditor.Gizmos` gizmos: IgEntityPresentationGizmo,
RouteGizmo, MapOverlayGizmo, MissionPresentationGizmo.)

---

### Task 7: MapOverlayGizmo (GZ058)

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/MapOverlayGizmo.cs` (NEW)

Replaces the rendering logic of `MapOverlayRenderLayer`.

```csharp
[GizmoProjector(typeof(SimTransform), typeof(MapOverlayStyle))]
public sealed class MapOverlayGizmo : IStatelessGizmo
```

**`Draw()` logic** (study `MapOverlayRenderLayer.Draw()` for exact rendering logic):
1. `if (!view.HasManagedComponent<EditablePolyline>(entity)) return;`
2. `var polyline = view.GetManagedComponentRO<EditablePolyline>(entity);`
3. Read `MapOverlayStyle` for color.
4. For each consecutive point pair in `polyline.Points`, emit `draw.DrawLine(...)`.
5. Position convention: check `MapOverlayRenderLayer.cs` to see whether it uses Position.X/Y
   or Position.X/Z for canvas coordinates.

**Namespace:** `Hrot.ScenarioEditor.Gizmos`

---

### Task 8: MissionPresentationGizmo (GZ058)

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/MissionPresentationGizmo.cs` (NEW)

Replaces the rendering logic of `MissionRenderLayer`.

```csharp
[GizmoProjector(typeof(SimTransform), typeof(SelectionState))]
public sealed class MissionPresentationGizmo : IStatelessGizmo
```

**Constructor injection:** `MissionRenderLayer` requires `IGeographicTransform` for
lat/lon → world coordinate conversion. Inject via constructor:
```csharp
private readonly IGeographicTransform _geoTransform;

public MissionPresentationGizmo(IGeographicTransform geoTransform)
{
    _geoTransform = geoTransform;
}
```

**`Draw()` logic** (mirror `MissionRenderLayer.Draw()` logic):
1. `if (!view.GetComponentRO<SelectionState>(entity).IsSelected) return;`
2. `if (!view.HasManagedComponent<ActiveMissionPlan>(entity)) return;`
3. `var activePlan = view.GetManagedComponentRO<ActiveMissionPlan>(entity);`
4. `if (activePlan?.Plan?.Tasks == null) return;`
5. Read `SimTransform.Position` for `currentPos`.
6. For each task in `activePlan.Plan.Tasks`: parse `task.BehaviorParams` JSON for
   target lat/lon (using `_geoTransform.GeoToWorld(lat, lon)` or equivalent),
   emit gradient `draw.DrawLineGradient(from, to, startColor, endColor, ...)`.

Study `MissionRenderLayer.cs` for the full JSON parsing and color gradient logic.

**Registration:** The source generator auto-registers it. However, `MissionPresentationGizmo`
has a constructor that takes `IGeographicTransform`. The generated `RegisterAll()` calls
`new MissionPresentationGizmo()` (no-arg constructor) — this WILL FAIL at compile time
since there is no default constructor.

To resolve this, do NOT decorate `MissionPresentationGizmo` with `[GizmoProjector]`.
Instead, register it manually in the composition roots:

**In `IgApplication.InitializeNetwork()`**, after creating `_statelessGizmoRegistry`:
```csharp
_statelessGizmoRegistry!.Register(
    new MissionPresentationGizmo(_geoTransform),
    new[] { typeof(SimTransform), typeof(SelectionState) });
```

**In `CgfSubsystem.Initialize()`** (non-headless block):
```csharp
cgfStatelessRegistry.Register(
    new MissionPresentationGizmo(_context.GeoTransform!),
    new[] { typeof(SimTransform), typeof(SelectionState) });
```

> If `ActiveMissionPlan` is a managed component registered in the ECS world, consider
> adding `typeof(ActiveMissionPlan)` to the required component array for tighter filtering.
> Check if it's registered with `RegisterManagedComponent<ActiveMissionPlan>()` in the
> respective composition root.

---

## Testing Requirements

Write all tests using `xUnit` (`[Fact]`). Minimum **10 tests** across the new test files.

### Key Test Patterns

**For SpatialAnchor + SemanticShape emission tests**, use `DebugPrimitiveBuffer` directly
as the draw builder (it implements `IDebugDrawBuilder`), then verify via `GetFrame()`:

```csharp
var repo = new EntityRepository();
repo.RegisterComponent<SimTransform>();
repo.RegisterComponent<NetworkIdentity>();
var entity = repo.CreateEntity();
repo.AddComponent(entity, new SimTransform { Position = new Vector3(100f, 200f, 0f) });
repo.AddComponent(entity, new NetworkIdentity(42L));

var buffer = new DebugPrimitiveBuffer();
var gizmo = new SimHostEntityPresentationGizmo();
gizmo.Draw(repo, entity, buffer);

var frame = buffer.GetFrame();
Assert.True(frame.Length >= 2);
var anchor = frame[0];
Assert.Equal(DebugPrimitiveShape.SpatialAnchor, anchor.Shape);
Assert.Equal(42L, anchor.NetworkId);
Assert.Equal(100f, anchor.AnchorWorldX);
Assert.Equal(200f, anchor.AnchorWorldY);

var shape = frame[1];
Assert.Equal(DebugPrimitiveShape.SemanticShape, shape.Shape);
Assert.Equal(CoordinateSpace.EntityLocal, shape.Space);
Assert.Equal(42, shape.AnchorIndex);
```

**For EffectPresentationGizmo** tests, verify the correct primitive type based on EffectType.

### Minimum Test Coverage

**File: `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/SimHostEntityPresentationGizmoTests.cs`**
- `SC_GZ057_1_GizmoProjectorAttribute_ContainsSimTransformAndNetworkIdentity`
- `SC_GZ057_2_Draw_EmitsSpatialAnchorWithCorrectNetworkId`
- `SC_GZ057_3_Draw_EmitsSemanticShapeWithMatchingAnchorIndex`
- `SC_GZ057_4_Draw_WithVehicleParams_EmitsNonZeroDimensions`

**File: `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/PresentationGizmoTests.cs`**
- `SC_GZ057_5_IgGizmoProjectorAttribute_ContainsCullingState`
- `SC_GZ057_6_IgGizmo_Draw_SkipsEntityWhenNotVisible`
- `SC_GZ058_1_EffectGizmo_Explosion_EmitsSphere`
- `SC_GZ058_2_EffectGizmo_Tracer_EmitsLine`
- `SC_GZ058_3_RouteGizmo_EmitsLinesForWaypoints`
- `SC_GZ058_4_DrawSpatialAnchor_EmitsCorrectPrimitive`  (or put in ContractsStandaloneTests)

### Test Setup Notes

- `EntityRepository` requires components to be registered before adding them to entities.
- `NetworkIdentity` uses `[ComponentId(GlobalComponentIds.NetworkIdentity)]` → needs
  `repo.RegisterComponent<NetworkIdentity>()` (or the equivalent registry call).
- For tests involving managed components like `RoutePlan`, use `repo.RegisterManagedComponent<RoutePlan>()`.
- `StatelessGizmoRegistry.Register()` throws `InvalidOperationException` if component types
  are not registered in `ComponentTypeRegistry` first. Use the simple 2-arg `Register(gizmo, types[])`
  overload in tests, making sure all types in `types[]` are first registered with the repo.
- For `CullingState` and other IG-specific components, look at `HealthBarGizmoTests.cs`
  to see how they set up the test `EntityRepository`.

---

## Build Verification

Before submitting:
```
dotnet build IOS-IG-SimHost.sln --no-incremental
```
The build must produce **0 errors**. Pre-existing warnings in `Hrot.ClusterRunner.Tests`
(xUnit2017) are acceptable.

Run:
```
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --filter "FullyQualifiedName~Gizmo"
dotnet test Hrot/Subsystems/Hrot.IG.Tests/Hrot.IG.Tests.csproj --filter "FullyQualifiedName~Gizmo"
```
All new tests must pass. Pre-existing test failures (non-gizmo tests) are not your concern.

---

## Report Requirements

Submit `.dev/gizmos-1/reports/BATCH-21-REPORT.md` with:

| Section | Content |
|---------|---------|
| **Files Changed** | Table: file path, task, new/modified |
| **Tests** | Table: test name, task, pass/fail |
| **Build Output** | Paste final `0 errors` build line |
| **Issues Encountered** | Problems hit and how resolved |
| **Design Decisions** | Choices made beyond the spec |
| **Technical Debt Spotted** | Issues observed but not addressed |
| **Suggested Commit Message** | One-liner git commit message |
