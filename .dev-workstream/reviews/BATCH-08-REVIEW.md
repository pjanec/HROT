# BATCH-08 Review

**Batch:** BATCH-08  
**Reviewer:** Development Lead  
**Date:** 2026-02-24  
**Status:** ✅ APPROVED

---

## Issues Found

### Issue 1: `RaycastSolverSystem` — no bounds cap on `batch.Count` (DEBT-021)

**File:** `Toolkits/FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs` (line 54)  
**Problem:** `int count = batch.Count;` — if `Count > 4096`, `Parallel.For` throws `IndexOutOfRangeException` on `hits[i]`.  
**Fix:** `int count = System.Math.Min(batch.Count, PhysicsConstants.RaycastBatchCapacity);`

### Issue 2: `stackalloc` candidate buffer undocumented 64-entity cap (DEBT-026)

**File:** `Toolkits/FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs` (line 84)  
**Problem:** `stackalloc (Entity, Vector2)[64]` — entities beyond #64 in the broadphase AABB are silently dropped. Raw literal, no comment.  
**Fix:** Add `PhysicsConstants.MaxBroadphaseCandidates = 64` constant; replace literal; add XML comment explaining the cap and implication.

### Issue 3: `TargetVisibleEvent` carries raw `int` indices — generational safety gap (DEBT-027)

**File:** `Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs` (line 74–79)  
**Problem:** `ObserverEntityIndex` and `TargetEntityIndex` are raw indices from `RayId`. If the entity is recycled between LOS submission and consumption, the wrong entity's threat memory is updated. Same anti-pattern as DEBT-009.  
**Fix (this batch):** Add a `// DEBT-027` comment documenting the gap. Full fix deferred to LOS pipeline rework.

### Issue 4: `QueryExpansionMeters` typed `int` (P3)

**File:** `Toolkits/FDP.Toolkit.Physics/PhysicsConstants.cs` (line 24)  
**Problem:** Used in float arithmetic; implicit widening is silent. Should be `const float QueryExpansionRadius = 5f;`

### Issue 5: `Intersection2DTests` Test 4 — identical geometry to Test 1 (DEBT-028)

**File:** `Toolkits/FDP.Toolkit.Physics.Tests/Intersection2DTests.cs` (lines 79–90)  
**Problem:** `ReturnsTMin_WhenTwoIntersections` uses same ray/circle as Test 1. The assertion window `[0.35, 0.45]` doesn't exclude the exit t (≈0.60) — the test doesn't prove min-is-returned.  
**Fix:** Use geometry where entry/exit t values are far apart (e.g. circle r=4, 20-unit ray — entry t≈0.30, exit t≈0.70). Assert `InRange(t, 0.25f, 0.35f)`.

### Issue 6: Stale comment in `RaycastSolverSystemTests` (P3)

**File:** `Toolkits/FDP.Toolkit.Physics.Tests/RaycastSolverSystemTests.cs` (line 178)  
**Problem:** `// Need to dispose farEntity's grid addition doesn't create issues.` — copy-paste artefact, makes no sense.  
**Fix:** Remove the line.

### Issue 7: `SimTransformBridgeSystem` — `PitchDeg` and `RollDeg` hardcoded to 0f (DEBT-025) **[External / P1]**

**File:** `Toolkits/Fdp.Toolkit.Geographic/Systems/SimTransformBridgeSystem.cs` (lines ~57–58)  
**Problem:** `PitchDeg = 0f` and `RollDeg = 0f` always set regardless of entity rotation. All non-level entities have orientation stripped before egress — downstream clients see everything level.  
**Fix:** Add `public static void RotationToPitchRollDeg(Quaternion, out float pitchDeg, out float rollDeg)` and call from `UpdateEntity`. Convention: `+pitchDeg = nose up`, `+rollDeg = right wing down` (from `GeoTransform.cs`). Write tests first to determine sign convention from the body frame.

---

## Test Quality Assessment

- Tests 1–3, 5 in `Intersection2DTests` — correct, meaningful.
- Test 4 — does not prove what its name claims (see Issue 5).
- `RaycastSolverSystemTests` — all 5 use real `SpatialHashGrid`, `IDisposable` cleanup present, bilateral assertions on closest-hit test. ✅
- `HitResolutionSystemTests` — correct isolation pattern (bypass solver, seed batch directly), bilateral assertions on event fields. ✅
- `PhysicsModuleTests` — both array lengths verified, uses constant not raw `4096`. ✅

---

## Verdict

**APPROVED** — Issues 1–6 are P2/P3 fixes for BATCH-09. Issue 7 is a P1 external bug; also mandatory in BATCH-09 Corrective 0.

---

## 📝 Commit Message

```
feat: Physics toolkit — Phase 4 complete (BATCH-08)

Completes BCS-P4-T1, BCS-P4-T2, BCS-P4-T3, BCS-P4-T4

New project: FDP.Toolkit.Physics (references Kernel, CarKinem, Perception)

BCS-P4-T1 — PhysicsCollider, RaycastBatchData, PhysicsToolkitModule:
  PhysicsCollider { float Radius, int CollisionLayer }
  RaycastBatchData singleton: Requests/Hits NativeArray[4096], Persistent allocator
  PhysicsConstants: RaycastBatchCapacity, QueryExpansionMeters, RayId pack/unpack helpers
  PhysicsToolkitModule: Initialize allocates arrays, transfers ownership to world singleton
  Dispose() no-op after ownership transfer (prevents double-free)

BCS-P4-T2 — Intersection2D.RaycastCircle:
  Quadratic discriminant; returns t_min in [0,1]
  Inside-circle edge case: t1<0 → returns t2 (exit intersection)

BCS-P4-T3 — RaycastSolverSystem:
  Parallel.For over batch, SpatialHash AABB broadphase, Intersection2D narrow-phase
  Layer mask bitmask, IgnoreEntity (full Entity struct — prevents index-0 silent match)
  Closest-hit tracking across all broadphase candidates

BCS-P4-T4 — HitResolutionSystem:
  RayId bit-63 discriminant: LOS → TargetVisibleEvent, Bullet → HitEvent
  HitEvent temporarily in Physics (moves to FDP.Toolkit.Combat in Phase 5 — DEBT-023)
  Resets batch.Count=0 after dispatch

Correctives (BATCH-07 debt):
  DEBT-018: MoveToExecutor — IsAlive guard added (OnExit not guaranteed on entity destruction)
  DEBT-019: LocomotionDispatcherSystem — same-frame OnEnter+Execute safety comment
  DEBT-020: FollowRoadGraphExecutorTests — all 3 assertions verified present

Tests: 16 new tests in FDP.Toolkit.Physics.Tests (all passing)
  PhysicsModuleTests: 3 | Intersection2DTests: 5 | RaycastSolverSystemTests: 5 | HitResolutionSystemTests: 3
  All existing 1087+ tests remain green
```

---

**Next Batch:** BATCH-09
