# BATCH-08 Report

**Batch:** BATCH-08 — GZ021 Remaining Gizmos: Rotation, Visibility Cone, Hill Attack
**Date:** 2026-05-06

---

## Build Result

**0 errors, 0 warnings** (both `Hrot.IG` and `Hrot.IG.Tests`).

```
Build succeeded.
    0 Error(s)
```

---

## Test Results

| Suite | Tests | Status |
|---|---|---|
| SC-GZ021-ROT (EntityRotation) | 4 | All pass |
| SC-GZ021-VIS (VisibilityCone) | 3 | All pass |
| SC-GZ021-HA (HillAttack) | 6 | All pass |
| Pre-existing (HB + GZ015/018/020) | 15 | All pass |
| **Total** | **28** | **All pass** |

New tests added: **13** (4 ROT + 3 VIS + 6 HA).

```
Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28
```

---

## Files Created

### `Hrot/Subsystems/Hrot.IG/Gizmos/`

| File | Purpose |
|---|---|
| `EntityRotationGizmoSettings.cs` | Setting key `EntityRotation.ArrowLength` (default 30 m) |
| `EntityRotationGizmoDefinition.cs` | Requires `SimTransform`; creates `EntityRotationGizmoInstance` |
| `EntityRotationGizmoInstance.cs` | Extracts yaw from quaternion, draws orange arrow + heading text |
| `VisibilityConeGizmoDefinition.cs` | Requires `SimTransform` + `PerceptionReceptor`; static `Instance` |
| `VisibilityConeGizmoInstance.cs` | Draws 2 edge lines + 8-segment arc; skips when `VisionRange <= 0` |
| `HillAttackGizmoSettings.cs` | Setting key `HillAttack.ShowSlots` (default true) |
| `HillAttackGizmoDefinition.cs` | Requires `BrainBlackboard`, `BehaviorState`, `SimTransform` |
| `HillAttackGizmoInstance.cs` | Draws firing line (blue) + baseline (green); optional slot spheres |

### `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/`

| File | Purpose |
|---|---|
| `FullCapturingDrawBuilder.cs` | Shared test double capturing Arrow, Text, Line, Sphere, Badge calls |
| `EntityRotationGizmoTests.cs` | SC-GZ021-ROT-1..4 (including registrar test) |
| `VisibilityConeGizmoTests.cs` | SC-GZ021-VIS-1..3 |
| `HillAttackGizmoTests.cs` | SC-GZ021-HA-1..6 (including registrar test) |

## Files Modified

| File | Change |
|---|---|
| `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs` | Added registration for EntityRotation, VisibilityCone, HillAttack gizmos |
| `Hrot/Subsystems/Hrot.IG/Hrot.IG.csproj` | Added `ProjectReference` to `Hrot.AI.Behaviors` (for `PlatoonHillAttackParams`) |
| `Hrot/Subsystems/Hrot.IG.Tests/Hrot.IG.Tests.csproj` | Added `ProjectReference` to `Hrot.AI.Behaviors` (for test setup of `BrainBlackboard`) |

---

## API Deviations Discovered

| Item | Instruction | Actual |
|---|---|---|
| `FixedString32` constructor | `TryWrite(...)` shown as possible | Constructor `new FixedString32(string)` used (matches `HealthBarGizmoInstance` pattern) |
| `GizmoRegistry.Register` | Shown with type param `Register<SimTransform>(...)` in instructions | Actual: `Register(IGizmoDefinition)` — no type param (confirmed from `GizmoRegistry.cs`) |
| `BrainBlackboard` fixed buffer access | `fixed (byte* mem = bb.Memory)` on `ref readonly` | Used `Unsafe.AsRef(in bb)` to obtain mutable ref before `fixed (&bbMut.Memory[0])` — matches pattern in `JoinFormationExecutor.cs` |
| `EntityRotationGizmoDefinition` static `Instance` | Instructions showed a parameterless static instance | Implemented with `GizmoSettingsRegistry` constructor argument (consistent with `HealthBarGizmoDefinition` pattern) |
| `VisibilityConeGizmoDefinition` static `Instance` | Shown in instructions | Kept — this gizmo has no settings, so static instance is appropriate |
| Test count | 13+ new + 15 existing = 28+ | 13 new + 15 existing = 28 exactly |

---

## Notes

- `AllowUnsafeBlocks` was already `true` in both `Hrot.IG.csproj` and `Hrot.IG.Tests.csproj` — no change needed.
- Added `Hrot.AI.Behaviors` project reference to both `Hrot.IG` and `Hrot.IG.Tests` (no circular dependency — `Hrot.AI.Behaviors` depends on `Fdp.Toolkits`, `Fdp.Core`, `Hrot.Core` only).
- `FullCapturingDrawBuilder` is a new class (not duplicating the existing `CapturingDrawBuilder` which captures only badge calls); both coexist in the test project.
