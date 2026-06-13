# BATCH-S2-F Report — Diagnostic logging insertions

**Date:** 2026-06-13
**Batch:** BATCH-S2-F (READ-ONLY DIAGNOSTIC)
**Status:** COMPLETE — 0 errors, both projects built successfully.

---

## Files changed

1. `Stride/Hrot.Stride.Core/PhysicsBodyLifecycleSystem.cs`
2. `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`
3. `Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs`

No other files were touched.

---

## Insertion 1 — PhysicsBodyLifecycleSystem.cs (Task 1)

Location: In `Execute`, creation loop (`foreach (var entity in ownedQuery)`), after `ref readonly var simTf = ...` and before `_bodyService.CreateBody(...)`.

```csharp
Log.Info("[DIAG-POS] LC-CREATE entity=#{0} shape={1} SimTransform.Position=({2:F3},{3:F3},{4:F3})",
    entity.Index, visualRef.ShapeKind,
    simTf.Position.X, simTf.Position.Y, simTf.Position.Z);
```

---

## Insertion 2a — BulletPhysicsBodyService.cs (Task 2, CreateBody log)

Location: In `CreateBody`, immediately after:
```csharp
var initialPos = FdpStrideTransform.ToStridePosition(initialPose.Position);
var initialRot = FdpStrideTransform.ToStrideRotation(initialPose.Rotation);
```

```csharp
Log.Info("[DIAG-POS] CreateBody entity=#{0} shape={1} FDP=({2:F3},{3:F3},{4:F3}) StrideInit=({5:F3},{6:F3},{7:F3})",
    entity.Index, shapeKind,
    initialPose.Position.X, initialPose.Position.Y, initialPose.Position.Z,
    initialPos.X, initialPos.Y, initialPos.Z);
```

---

## Insertion 2b — BulletPhysicsBodyService.cs (Task 2, DiagState field + GetBodyState early log)

### New field added to DiagState class:

```csharp
public int  EarlyPosCount       { get; set; }
```

### Early-frame log in GetBodyState, inside the existing `if (_diagState.TryGetValue(bodyHandle, out var diag))` block, placed before the existing `FrameCounter` throttle logic:

```csharp
if (diag.EarlyPosCount < 5)
{
    diag.EarlyPosCount++;
    Log.Info("[DIAG-POS] GetBodyState '{0}' earlyFrame={1} StridePos=({2:F3},{3:F3},{4:F3}) shape={5} kinematic={6}",
        entry.StrideEntity.Name, diag.EarlyPosCount, pos.X, pos.Y, pos.Z, entry.ShapeKind, entry.IsKinematic);
}
```

---

## Insertion 3 — BulletReverseSyncSystem.cs (Task 3)

### New private field (added after `_velocityLogCounter` declaration):

```csharp
private readonly Dictionary<ulong, int> _diagEarlyWriteCount = new();
```

### Early-write log in Execute, immediately after `repo.SetComponent(entity, newTransform)`:

```csharp
ulong dkey = entity.PackedValue;
if (!_diagEarlyWriteCount.TryGetValue(dkey, out var dn)) dn = 0;
if (dn < 5)
{
    _diagEarlyWriteCount[dkey] = dn + 1;
    Log.Info("[DIAG-POS] ReverseSync entity=#{0} earlyFrame={1} StrideStatePos=({2:F3},{3:F3},{4:F3}) -> wroteSimPos=({5:F3},{6:F3},{7:F3}) kinematic={8}",
        entity.Index, dn + 1,
        state.Position.X, state.Position.Y, state.Position.Z,
        newTransform.Position.X, newTransform.Position.Y, newTransform.Position.Z,
        state.IsKinematic);
}
```

---

## Build result

| Project | Errors | Warnings (pre-existing) |
|---|---|---|
| `Hrot.Stride.Core.csproj` | 0 | 0 |
| `HrotStrideApp.Game.csproj` | 0 | 5 (all pre-existing: NU1608 x4, CS0108 x1) |

---

## Name adaptations

None. All field/method names in the spec matched the actual code exactly:
- `Log` static NLog logger present in all three classes.
- `DiagState` class exists in `BulletPhysicsBodyService.cs` with the expected fields.
- `repo.SetComponent(entity, newTransform)` exists at the expected location.
- `entity.PackedValue`, `state.Position`, `newTransform.Position`, `state.IsKinematic` all match.
