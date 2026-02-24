# BATCH-01 Review

**Batch:** BATCH-01  
**Reviewer:** Development Lead  
**Date:** 2026-02-23  
**Status:** ✅ APPROVED (with one P1 tracked for BATCH-02)

---

## Summary

Universal Spatial Primitives migration is complete. All 6 tasks delivered. Solution compiles clean, all pre-existing tests pass. Coordinate system is explicitly documented in `SimComponents.cs`  comments — good proactive decision.

---

## Issues Found

### Issue 1: Inconsistent forward-vector extraction in `CarKinematicsSystem` (P1 — fix in BATCH-02)

**File:** `Toolkits/FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs`

**Problem:** Two different base vectors are used to extract the "forward" direction from the rotation quaternion in the same file:

- Line 122: `Vector3.Transform(Vector3.UnitX, tf.Rotation)` — used in the main `UpdateVehicle` path.  
- Line 281: `Vector3.Transform(Vector3.UnitY, tf.Rotation)` — used in `GetFormationTarget` fallback.

`FormationTargetSystem.cs` line 55 also uses `UnitY`. This means the formation heading fallback is computed from a different axis than the main kinematics path.

`SimComponents.cs` defines the coordinate system clearly: X=east, Y=north, Z=up, yaw=0 is east. Since `CarKinematicsSystem` applies yaw via `Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw)` where `yaw = Atan2(fwd2D.Y, fwd2D.X)`, the stored quaternion's "forward in model space" is `UnitX` (east at yaw=0). The `UnitX` extraction on line 122 is **correct**. The `UnitY` usage on line 281 and in `FormationTargetSystem` is **wrong** — it returns the left vector, not forward.

**Fix for BATCH-02:**
- `CarKinematicsSystem.cs` line 281: change `Vector3.UnitY` → `Vector3.UnitX`.
- `FormationTargetSystem.cs` line 55: same fix.
- Add a regression test: spawn entity with known yaw, check that the extracted forward vector matches expectation.

---

### Issue 2: Missing `SpatialHashSystem` integration tests for BCS-P0-T3 (P2 — informational)

The batch spec required two tests: one for a non-vehicle entity (only `SimTransform`), one for a vehicle entity, both verified via `QueryNeighbors`. The delivered `SpatialHashGridTests.cs` tests the grid data structure directly (good), but there is no test that runs `SpatialHashSystem` with a plain non-vehicle entity and confirms it appears in a grid query. The `VehicleStateRefactorTests.cs` integration test indirectly exercises this, but only with a vehicle entity.

This is a coverage gap, not a blocking defect. A dedicated test in a follow-up batch is acceptable.

---

## Test Quality Assessment

Tests are behavioural and check actual values (position coordinates, moved distance, `NavigationMode` state). No shallow "object exists" tests. The `ParallelCorrectnessTests` — 100 entities, checks exact Y position to 2 decimal places — is a solid correctness guard. The `CarKinematicsSystemTests.System_UpdatesVehiclePosition` verifies `0.16m` movement to 2dp precision. Acceptable quality.

`EntityFactory.cs` stale comment on line 37 (`// Was RegisterComponent in snippet`) is leftover debug text; remove in next pass.

---

## Verdict

**Status: APPROVED**

Issue 1 (UnitX vs UnitY mismatch) is a latent bug that does not affect current tests (formation path is not exercised in Phase 0 tests) but **will** cause wrong heading in Phase 1 once formations are tested alongside behavior dispatchers. Fix mandated in BATCH-02 as Task 0 corrective.

---

## 📝 Commit Message

```
feat: universal spatial primitives — SimTransform/SimVelocity (BATCH-01)

Completes BCS-P0-T1, BCS-P0-T2, BCS-P0-T3, BCS-P0-T4, BCS-P0-T5, BCS-P0-T6

Introduces SimTransform (28 bytes) and SimVelocity (24 bytes) in Fdp.Kernel
as the engine-wide spatial vocabulary. Coordinate convention documented inline:
right-handed, X=east, Y=north, Z=up, yaw-0=east.

Fdp.Kernel:
- New: CoreComponents/SimComponents.cs (SimTransform, SimVelocity + coord comments)

FDP.Toolkit.CarKinem:
- VehicleState: removed Position, Forward, Pitch, Roll → motor-internals only (16 bytes)
- CarKinematicsSystem: queries SimTransform+SimVelocity; 3D↔2D bridge via UnitX convention
- SpatialHashSystem: queries SimTransform exclusively (all entity types now in grid)
- RVO avoidance: reads SimVelocity.Linear for neighbour velocities

Fdp.Examples.CarKinem:
- VehicleVisualizer, CarKinemApp, ScenarioManager: read SimTransform/SimVelocity

Fdp.Examples.BattleRoyale:
- Deleted Position.cs, Velocity.cs; migrated all sites to SimTransform/SimVelocity

Fdp.Examples.NetworkDemo:
- Deleted DemoPosition.cs; removed Position/Velocity from DemoComponents.cs
- PositionGeodetic left intact (domain-specific WGS84, not a spatial primitive)

Tests: SimComponentTests (3), VehicleStateRefactorTests (3),
       CarKinematicsSystemTests (3), ParallelCorrectnessTests (1) — all green

Related: FDP/Docs/projects/behavior-control/DESIGN.md §2
```

---

**Next Batch:** BATCH-02 (Phase 1 — FDP.Toolkit.Behavior core, with UnitX/UnitY fix as Task 0)
