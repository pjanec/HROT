# BATCH-08 — GZ021 Remaining Gizmos: Rotation, Visibility Cone, Hill Attack

**Workstream:** FDP Declarative Gizmo & Presentation Framework
**Task reference:** `.dev/gizmos-1/TASK-DETAIL.md`, `.dev/gizmos-1/TASK-TRACKER.md`
**Design reference:** `.dev/gizmos-1/DESIGN.md`

---

## Overview

This batch implements three remaining concrete gizmos from TASK-GZ021, plus extends
`GizmoRegistrar` to register them all.

| Sub-task | Gizmo | Components required |
|---|---|---|
| GZ021-ROT | Entity rotation display gizmo | `SimTransform` |
| GZ021-VIS | Visibility cone gizmo | `SimTransform` + `PerceptionReceptor` |
| GZ021-HA | Platoon hill attack gizmo | `BrainBlackboard` + `BehaviorState` + `SimTransform` |

No FDP changes. All new files are in `Hrot/Subsystems/Hrot.IG/Gizmos/` and
`Hrot/Subsystems/Hrot.IG.Tests/Gizmos/`.

---

## Key Facts (Read before implementing)

### Gizmo framework patterns (CONFIRMED from prior batches)

- `IGizmoDefinition.RequiredComponents` is `Type[]` (not `int[]`)
- `IStatefulGizmo.UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder draw)` — no `isSelected` parameter
- `GizmoSettingValue` properties: `.FloatValue`, `.BoolValue`, `.IntValue`, `.Type` (NOT `.AsFloat`/`.AsBool`/`.Kind`)
- `GizmoSettingsRegistry.IsRegistered` is `internal` — tests use `EnumerateAll()` to verify registration
- `GizmoRegistrar.Register(GizmoRegistry registry, GizmoSettingsRegistry settings)` — extend this static method
- `IDebugDrawBuilder` is implemented by `DebugPrimitiveBuffer`

### Existing gizmo files (BATCH-07) — study before adding new ones

- `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoSettings.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoDefinition.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoInstance.cs`
- `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs`

### IDebugDrawBuilder draw methods available

```csharp
void DrawLine(Vector3 start, Vector3 end, Rgba32 color,
    float thickness = 1f,
    SizeMode sizeMode = SizeMode.ScreenPixels,
    PipelineTarget target = PipelineTarget.All,
    byte layer = 0);

void DrawArrow(Vector3 from, Vector3 to, Rgba32 color,
    float headSize = 1f,
    byte layer = 0);

void DrawText(float x, float y, FixedString32 text, Rgba32 color,
    CoordinateSpace space = CoordinateSpace.World,
    byte layer = 0);

void DrawSphere(Vector3 center, float radius, Rgba32 color,
    PipelineTarget target = PipelineTarget.All,
    byte layer = 0);

void DrawEntityLocal(Entity anchor, Vector3 localStart, Vector3 localEnd,
    Rgba32 color, float thickness = 1f, byte layer = 0);

void DrawEntityBadge(Entity target, FixedString32 richText,
    PipelineTarget targetPipeline = PipelineTarget.All);
```

### SimTransform (Fdp.Core)

```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.SimTransform)]
public struct SimTransform
{
    public Vector3 Position;    // X=east, Y=north, Z=up (meters)
    public Quaternion Rotation; // yaw=0 means east (+X), yaw=90deg means north (+Y)
                                // yaw-pitch-roll order (first around Z)
}
```

Yaw extraction from quaternion (clockwise from north, in radians):
```csharp
// Quaternion yaw: rotation around Z axis
// yaw=0 -> east, yaw=PI/2 -> north  (right-handed)
float yawRad = MathF.Atan2(
    2f * (q.W * q.Z + q.X * q.Y),
    1f - 2f * (q.Y * q.Y + q.Z * q.Z));
```

Heading in degrees (0=north, 90=east, clockwise) — matches existing EntityRotationTool:
```csharp
float compassDeg = ((90f - yawRad * (180f / MathF.PI)) % 360f + 360f) % 360f;
```

### PerceptionReceptor (Fdp.Toolkit.Perception.Components)

```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(251)]
public struct PerceptionReceptor
{
    public float HearingRange;   // meters
    public float VisionRange;    // meters
    public float FieldOfViewCos; // cos(half-FOV angle)
                                 // e.g. 60deg FOV -> half=30deg -> cos(PI/6) = 0.866
}
```

Half-angle in radians: `MathF.Acos(FieldOfViewCos)`.

### BrainBlackboard and PlatoonHillAttackParams

```csharp
// Fdp.Toolkit.Behavior.Components
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BrainBlackboard)]  // = 23
[DataPolicy(DataPolicy.NoSave)]
public unsafe struct BrainBlackboard
{
    public fixed byte Memory[128]; // bytes 0-59 = behavior params (polymorphic)
}

// Fdp.Toolkit.Behavior.Components
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BehaviorState)]
[DataPolicy(DataPolicy.NoSave)]
public struct BehaviorState
{
    public int ActiveBehaviorHash;
    public uint InstanceId;
    public byte BrainTier;
}

// Hrot.AI.Behaviors.Brains
[StructLayout(LayoutKind.Sequential)]
public struct PlatoonHillAttackParams
{
    public Entity TargetAreaEntity;  // 8 bytes, offset 0
    public float StartX;             // firing line start X (meters)
    public float StartY;
    public float EndX;               // firing line end X
    public float EndY;
    public float BaselineStartX;
    public float BaselineStartY;
    public float BaselineEndX;
    public float BaselineEndY;
    public float AttackDirX;
    public float AttackDirY;
    public float TankSpacing;        // meters between slots
}
```

PlatoonHillAttack behavior hash: `BehaviorIds.PlatoonHillAttack_BT = 3014`
(in `Hrot.Map.Definitions.Behavior.BehaviorIds`, which is `internal` — use literal `3014`)

To project params from blackboard (requires `unsafe` context):
```csharp
ref readonly var bb = ref view.GetComponentRO<BrainBlackboard>(entity);
// Project bytes 0..51 as PlatoonHillAttackParams via Unsafe.As
unsafe
{
    fixed (byte* ptr = bb.Memory)
    {
        ref var p = ref Unsafe.AsRef<PlatoonHillAttackParams>(ptr);
        // use p.StartX, p.StartY, etc.
    }
}
```

Slot positions on firing line:
```csharp
// n slots where: n = (int)(length / TankSpacing) + 1, clamped to [1..8]
// Slot i: lerp from start to end by fraction = i * TankSpacing / length
```

Slot positions on baseline: same formula using BaselineStart/End.

---

## GZ021-ROT: Entity Rotation Display Gizmo

### Purpose

Show each entity's current heading as a directional arrow pointing in the facing direction.
Global setting controls the arrow length.

### Files to create

**`Hrot/Subsystems/Hrot.IG/Gizmos/EntityRotationGizmoSettings.cs`**

```
namespace Hrot.IG.Gizmos
{
    internal static class EntityRotationGizmoSettings
    {
        public const string ArrowLength = "EntityRotation.ArrowLength";

        public static void Register(GizmoSettingsRegistry settings)
        {
            settings.RegisterSetting(ArrowLength, GizmoSettingValue.From(30f)); // meters
        }
    }
}
```

**`Hrot/Subsystems/Hrot.IG/Gizmos/EntityRotationGizmoDefinition.cs`**

```
[UpdateGizmoLayer(0)]
public sealed class EntityRotationGizmoDefinition : IGizmoDefinition
{
    public static readonly EntityRotationGizmoDefinition Instance = new();

    public Type[] RequiredComponents => new[] { typeof(SimTransform) };
    public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;
    public IStatefulGizmo CreateInstance() => new EntityRotationGizmoInstance();
}
```

**`Hrot/Subsystems/Hrot.IG/Gizmos/EntityRotationGizmoInstance.cs`**

```
public sealed class EntityRotationGizmoInstance : IStatefulGizmo
{
    public void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder draw)
    {
        ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
        var pos = tf.Position;
        var q = tf.Rotation;

        // Extract yaw from quaternion
        float yawRad = MathF.Atan2(
            2f * (q.W * q.Z + q.X * q.Y),
            1f - 2f * (q.Y * q.Y + q.Z * q.Z));

        float arrowLen = 30f; // default; read from settings if settings registry available
        // Direction: yawRad=0 = east (+X), PI/2 = north (+Y)
        var tip = new Vector3(
            pos.X + MathF.Cos(yawRad) * arrowLen,
            pos.Y + MathF.Sin(yawRad) * arrowLen,
            pos.Z);

        draw.DrawArrow(pos, tip, new Rgba32(255, 165, 0, 255), headSize: 3f);

        // Heading label: compass degrees, 0=north, 90=east, clockwise
        float compassDeg = ((90f - yawRad * (180f / MathF.PI)) % 360f + 360f) % 360f;
        FixedString32 label = default;
        // Write degree text (safe: FixedString32 truncates at 31 chars)
        label.TryWrite($"{compassDeg:F0}*");  // * = degree approximation in ASCII
        draw.DrawText(pos.X, pos.Y, label, new Rgba32(255, 165, 0, 200));
    }
}
```

Note: FixedString32 is `Fdp.Core.FixedString32`. Check its API — it may use `CopyFrom(string)` or
`TryWrite(...)` or similar. Look at existing code (e.g., `HealthBarGizmoInstance`) for the correct usage.

### Settings integration

The instance should accept an optional `GizmoSettingsRegistry` at construction to read arrow length.
Or read from a static setting reference. Use the same pattern as `HealthBarGizmoInstance`.

---

## GZ021-VIS: Visibility Cone Gizmo

### Purpose

Show each entity's vision cone as a sector — two edge lines and intermediate arc segments.
Only drawn for entities with `PerceptionReceptor`.

### Namespace needed

`using Fdp.Toolkit.Perception.Components;`

Check the actual namespace by reading
`FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs`.

### Files to create

**`Hrot/Subsystems/Hrot.IG/Gizmos/VisibilityConeGizmoDefinition.cs`**

```
public sealed class VisibilityConeGizmoDefinition : IGizmoDefinition
{
    public static readonly VisibilityConeGizmoDefinition Instance = new();

    public Type[] RequiredComponents => new[] { typeof(SimTransform), typeof(PerceptionReceptor) };
    public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;
    public IStatefulGizmo CreateInstance() => new VisibilityConeGizmoInstance();
}
```

**`Hrot/Subsystems/Hrot.IG/Gizmos/VisibilityConeGizmoInstance.cs`**

Draw the cone in world space derived from SimTransform:

1. Entity position = `(tf.Position.X, tf.Position.Y)`
2. Entity yaw = extracted from `tf.Rotation` quaternion (same formula as ROT)
3. Half-angle = `MathF.Acos(receptor.FieldOfViewCos)`
4. Range = `receptor.VisionRange`
5. Draw left edge line: from pos to `pos + range * direction(yaw - halfAngle)`
6. Draw right edge line: from pos to `pos + range * direction(yaw + halfAngle)`
7. Draw arc as N=8 intermediate line segments connecting the edge endpoints

Color: semi-transparent cyan `new Rgba32(0, 200, 255, 120)`.

No settings needed for this gizmo. No settings file required.

---

## GZ021-HA: Platoon Hill Attack Gizmo

### Purpose

Show the firing line (blue) and baseline (green) for entities executing the PlatoonHillAttack
commander behavior. Optionally render numbered slots.

### Global settings

Key constants:

```csharp
public const string ShowSlots = "HillAttack.ShowSlots";
// default: true (bool)
```

### Files to create

**`Hrot/Subsystems/Hrot.IG/Gizmos/HillAttackGizmoSettings.cs`**

```
internal static class HillAttackGizmoSettings
{
    public const string ShowSlots = "HillAttack.ShowSlots";

    public static void Register(GizmoSettingsRegistry settings)
    {
        settings.RegisterSetting(ShowSlots, GizmoSettingValue.From(true));
    }
}
```

**`Hrot/Subsystems/Hrot.IG/Gizmos/HillAttackGizmoDefinition.cs`**

```
public sealed class HillAttackGizmoDefinition : IGizmoDefinition
{
    private readonly GizmoSettingsRegistry _settings;

    public HillAttackGizmoDefinition(GizmoSettingsRegistry settings)
        => _settings = settings;

    public Type[] RequiredComponents => new[]
    {
        typeof(BrainBlackboard),
        typeof(BehaviorState),
        typeof(SimTransform)
    };
    public IGizmoVisibilityPolicy VisibilityPolicy => AlwaysVisiblePolicy.Instance;
    public IStatefulGizmo CreateInstance() => new HillAttackGizmoInstance(_settings);
}
```

**`Hrot/Subsystems/Hrot.IG/Gizmos/HillAttackGizmoInstance.cs`**

This file requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in the csproj (already set for
the project if it uses unsafe code elsewhere — check `Hrot.IG.csproj`).

```
public sealed class HillAttackGizmoInstance : IStatefulGizmo
{
    private const int PlatoonHillAttack_BT = 3014;  // BehaviorIds.PlatoonHillAttack_BT
    private readonly GizmoSettingsRegistry _settings;

    public HillAttackGizmoInstance(GizmoSettingsRegistry settings) => _settings = settings;

    public unsafe void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder draw)
    {
        ref readonly var bs = ref view.GetComponentRO<BehaviorState>(entity);
        if (bs.ActiveBehaviorHash != PlatoonHillAttack_BT)
            return;

        ref readonly var bb = ref view.GetComponentRO<BrainBlackboard>(entity);
        PlatoonHillAttackParams p;
        fixed (byte* mem = bb.Memory)
            p = Unsafe.AsRef<PlatoonHillAttackParams>(mem);

        var fireStart = new Vector3(p.StartX, p.StartY, 0f);
        var fireEnd   = new Vector3(p.EndX,   p.EndY,   0f);
        var baseStart = new Vector3(p.BaselineStartX, p.BaselineStartY, 0f);
        var baseEnd   = new Vector3(p.BaselineEndX,   p.BaselineEndY,   0f);

        var fireColor = new Rgba32(0,   0, 255, 220);   // blue
        var baseColor = new Rgba32(0, 200,   0, 220);   // green

        draw.DrawLine(fireStart, fireEnd, fireColor, thickness: 2f,
            sizeMode: SizeMode.ScreenPixels);
        draw.DrawLine(baseStart, baseEnd, baseColor, thickness: 2f,
            sizeMode: SizeMode.ScreenPixels);

        bool showSlots = _settings
            .Read(GizmoSettingsRegistry.ComputeHash(HillAttackGizmoSettings.ShowSlots))
            .BoolValue;

        if (showSlots && p.TankSpacing > 0f)
        {
            DrawSlots(draw, fireStart, fireEnd, p.TankSpacing, fireColor, 'F');
            DrawSlots(draw, baseStart, baseEnd, p.TankSpacing, baseColor, 'B');
        }
    }

    private static void DrawSlots(IDebugDrawBuilder draw, Vector3 start, Vector3 end,
        float spacing, Rgba32 color, char prefix)
    {
        float length = Vector3.Distance(start, end);
        if (length < 0.01f) return;

        int count = Math.Min(8, (int)(length / spacing) + 1);
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : (float)i / (count - 1);
            var pos = Vector3.Lerp(start, end, t);
            draw.DrawSphere(pos, 2f, color);
            FixedString32 label = default;
            label.TryWrite($"{prefix}{i + 1}");
            draw.DrawText(pos.X, pos.Y, label, color);
        }
    }
}
```

The `FixedString32.TryWrite(...)` format — look at how `HealthBarGizmoInstance` creates labels to
match the actual API. If `TryWrite` is not available, use `CopyFrom(string)` or the constructor
that takes a string.

---

## Update GizmoRegistrar

Extend `GizmoRegistrar.Register` to register settings and definitions for all three new gizmos:

```csharp
public static void Register(GizmoRegistry registry, GizmoSettingsRegistry settings)
{
    // --- existing health bar ---
    HealthBarGizmoSettings.Register(settings);
    registry.Register<IgHealthState>(new HealthBarGizmoDefinition(settings), ...);

    // --- entity rotation ---
    EntityRotationGizmoSettings.Register(settings);
    registry.Register<SimTransform>(new EntityRotationGizmoDefinition(), ...);

    // --- visibility cones ---
    registry.Register(new VisibilityConeGizmoDefinition(), ...);

    // --- hill attack ---
    HillAttackGizmoSettings.Register(settings);
    registry.Register(new HillAttackGizmoDefinition(settings), ...);
}
```

**IMPORTANT**: Check the exact `GizmoRegistry.Register` signature before implementing.
Read `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoRegistry.cs` to see how existing
gizmos are registered (the method uses the `RequiredComponents` from the definition, not a type
parameter). The existing `GizmoRegistrar.Register` from BATCH-07 is the authoritative reference.

---

## Tests to write

Test file: `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/EntityRotationGizmoTests.cs`
Test file: `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/VisibilityConeGizmoTests.cs`
Test file: `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/HillAttackGizmoTests.cs`

### EntityRotation tests (prefix SC-GZ021-ROT-)

1. **SC-GZ021-ROT-1** — `UpdateAndDraw` on entity with `SimTransform` (identity rotation → east) emits DrawArrow call pointing east.
2. **SC-GZ021-ROT-2** — `UpdateAndDraw` emits `DrawText` containing degree value.
3. **SC-GZ021-ROT-3** — `RequiredComponents` contains `typeof(SimTransform)`.

### VisibilityCone tests (prefix SC-GZ021-VIS-)

1. **SC-GZ021-VIS-1** — `RequiredComponents` contains both `typeof(SimTransform)` and `typeof(PerceptionReceptor)`.
2. **SC-GZ021-VIS-2** — `UpdateAndDraw` emits at least 2 DrawLine calls when `VisionRange > 0`.
3. **SC-GZ021-VIS-3** — `UpdateAndDraw` emits no draw calls when `VisionRange == 0`.

### HillAttack tests (prefix SC-GZ021-HA-)

1. **SC-GZ021-HA-1** — `RequiredComponents` contains `BrainBlackboard`, `BehaviorState`, `SimTransform`.
2. **SC-GZ021-HA-2** — `UpdateAndDraw` emits no draw calls when `BehaviorState.ActiveBehaviorHash != 3014`.
3. **SC-GZ021-HA-3** — `UpdateAndDraw` emits DrawLine calls (fire line + baseline) when hash == 3014.
4. **SC-GZ021-HA-4** — When ShowSlots=true, DrawSphere calls are emitted for slots.
5. **SC-GZ021-HA-5** — When ShowSlots=false, no DrawSphere calls emitted.

### CapturingDrawBuilder

Reuse or extend the `CapturingDrawBuilder`/`CapturingDebugDrawBuilder` from the existing tests
(check `HealthBarGizmoTests.cs` for the pattern). If the test double is already defined, do not
duplicate it — share it within the test project.

### Test entity setup

For tests needing BrainBlackboard (unsafe):
```csharp
// Create a BrainBlackboard with PlatoonHillAttack behavior hash set
unsafe
{
    var bb = new BrainBlackboard();
    var p = new PlatoonHillAttackParams
    {
        StartX = 100f, StartY = 0f, EndX = 200f, EndY = 0f,
        BaselineStartX = 100f, BaselineStartY = -50f,
        BaselineEndX = 200f, BaselineEndY = -50f,
        TankSpacing = 30f
    };
    fixed (byte* mem = bb.Memory)
        *(PlatoonHillAttackParams*)mem = p;
    // Set on entity via mock ISimulationView
}
```

The test `ISimulationView` mock (or stub) should be able to return unsafe struct types.
Look at how existing gizmo tests create mock views (HealthBarGizmoTests.cs).

---

## Build and test commands

```
dotnet build Hrot\Subsystems\Hrot.IG\Hrot.IG.csproj --nologo
dotnet build Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj --nologo
dotnet test Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj --nologo --filter "FullyQualifiedName~Gizmo"
```

Target: **0 build errors, 13+ new tests passing** (3 ROT + 3 VIS + 5 HA + 2 registrar), plus
existing 15 pass.

---

## Deliverables

1. 7 new `.cs` files in `Hrot.IG/Gizmos/` (3 definitions, 3 instances/settings + GizmoRegistrar modified)
2. 3 new test files in `Hrot.IG.Tests/Gizmos/`
3. Modified `GizmoRegistrar.cs` to register all gizmos
4. All 28+ gizmo tests pass
5. BATCH-08-REPORT.md in `.dev/gizmos-1/reports/`

---

## Known corrections from prior batches

- `FixedString32` API: check existing usage in `HealthBarGizmoInstance.cs` for exact method names
- `GizmoRegistry.Register` signature: check `GizmoRegistrar.cs` (BATCH-07) for actual call pattern
- `AllowUnsafeBlocks`: if `Hrot.IG.csproj` doesn't have it, add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
- `Unsafe.AsRef<T>(void*)` requires `System.Runtime.CompilerServices.Unsafe` NuGet or the BCL version
- BrainBlackboard is in `Fdp.Toolkit.Behavior.Components` namespace
- PerceptionReceptor is in `Fdp.Toolkit.Perception.Components` namespace
- BehaviorState is in `Fdp.Toolkit.Behavior.Components` namespace
- PlatoonHillAttackParams is in `Hrot.AI.Behaviors.Brains` namespace
