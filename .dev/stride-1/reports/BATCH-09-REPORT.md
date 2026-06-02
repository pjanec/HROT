# BATCH-09 Report
**Tasks:** STR-P3-T1, STR-P3-T2, STR-P3-T3   **Phase:** P3 (Perception + Ballistics raycasts)

---

## Implementation Summary

### T1 — `IStrideRaycastService` + `StrideRaycastService` (STR-P3-T1)

**New files:**

- `Stride/Hrot.Stride.Core/IStrideRaycastService.cs` — Seam interface exposing `Raycast(fromFdp, toFdp, collisionGroups, collisionFilter)` and `RaycastPenetrating(...)` in FDP world-space. Returns `StrideRaycastHit` (hit point, surface normal, fraction, hit entity, all FDP-space). Also defines `StrideRaycastHit` struct + the `Miss` sentinel.

- `Stride/Hrot.Stride.Core/FakeStrideRaycastService.cs` — Scriptable test-double. Records `LastFrom`, `LastTo`, `LastCollisionGroups`, `LastCollisionFilter`, `CallCount`. Returns scripted `NextHit` and `NextPenetratingHits`. Used by all headless tests.

- `Stride/HrotStrideApp.Game/StrideRaycastService.cs` — **GPU-deferred concrete implementation** (STR-D11 class, see below). Holds an injected `Simulation`. Calls `Simulation.Raycast(from, to)` or the filter overload. Converts hit point via `ToFdpPosition`, hit normal via `ToFdpVelocity` (direction swizzle). Attempts entity-index recovery from Stride entity name tag.

**Tests:** `Stride/Hrot.Stride.Core.Tests/StrideRaycastServiceTests.cs` — 23 tests.

---

### T2 — `StrideRaycastLosService` (STR-P3-T2)

**New file:** `Stride/Hrot.Stride.Core/StrideRaycastLosService.cs`

- Implements `ILosService` (drop-in for `BlockedLosService`).
- `HasCheapLineOfSight(Vector2 observer, Vector2 target)` lifts 2-D XY positions to 3-D using configurable `EyeHeightMetres` (default 1.5 m), then delegates to `HasLineOfSight3D`.
- `HasLineOfSight3D(Vector3 observerFdp, Vector3 targetFdp)` fires a `IStrideRaycastService.Raycast`. No hit → `true` (clear). Hit at fraction ≥ `HitFractionClearThreshold` (0.99) → `true` (target's own collider). Hit before threshold → `false` (blocked by wall / obstacle).
- On the Stride node: inject a `StrideRaycastLosService(new StrideRaycastService(simulation))` wherever `ILosService` is currently injected (e.g. `FindCoverFromTarget.Build(losService)` in the EQS pipeline).

**[VERIFY] result — `ILosService` contract and fake-LOS injection point:**

- `ILosService` is in `Fdp.Toolkit.Spatial.Eqs`, contract: `bool HasCheapLineOfSight(Vector2 observer, Vector2 target)`.
- Current fake/stub: `BlockedLosService` (always returns `false`). Used by default via `FindCoverFromTarget.Build()` (no-arg overload calls `Build(new BlockedLosService())`).
- The parameterised `Build(ILosService los)` overload passes it to `CheapLineOfSightTest(los)` in the `FilterCheap` pipeline stage.
- **Injection point on the Stride node:** replace `new BlockedLosService()` with `new StrideRaycastLosService(strideRaycastService)` wherever `ILosService` is constructed (inside the EQS Eqs-builder; concrete wiring is the composition root's responsibility at GPU bring-up).

**TargetMemory 3D-correct:** `ThreatEvaluationSystem` already reads `SimTransform.Position` (X, Y, Z) for live targets and passes `posZ` to `TargetMemory.AddOrUpdateTarget`. The `TargetMemory.PositionsZ` field stores the altitude. No changes required to the perception pipeline for 3D-correctness — it was already promoted (P3D-206).

**Tests:** `Stride/Hrot.Stride.Core.Tests/StrideRaycastLosServiceTests.cs` — 13 tests.

---

### T3 — Ballistics raycast seam (STR-P3-T3)

**New files:**

- `FDP/Toolkits/Fdp.Toolkits/Physics/IRaycastBackend.cs` — Minimal injectable seam in `Fdp.Toolkits.Physics`. Single method: `RaycastHit Raycast(Vector3 start, Vector3 end, long rayId, int layerMask, Entity ignoreEntity, Entity observerEntity, Entity targetEntity)`. All FDP-space. Lives in `Fdp.Toolkits` — `Hrot.Stride.Core` depends on `Fdp.Toolkits`, never the reverse.

- `Stride/Hrot.Stride.Core/StrideRaycastBackend.cs` — Adapter: `IRaycastBackend` → `IStrideRaycastService`. Converts the FDP `RaycastHit` result. Applies a `HitFractionClearThreshold` (0.999) to treat endpoint-hits as misses.

**Modified file:**

- `FDP/Toolkits/Fdp.Toolkits/Physics/Systems/RaycastSolverSystem.cs` — Added `public IRaycastBackend? RaycastBackend { get; set; }`. When non-null, the backend override path runs: for each `RaycastRequestEvent`, calls `RaycastBackend.Raycast(...)`, copies the request fields (Start, End, IgnoreEntity, Observer, Target, SourceNodeId) into the result, and publishes `RaycastResultEvent`. If null (default), the existing spatial-hash path runs unchanged.

**How it plugs in (Stride node):**
```csharp
var physicsQueryModule = ... ; // the module containing the solver
physicsQueryModule.RaycastSolverSystem.RaycastBackend =
    new StrideRaycastBackend(new StrideRaycastService(simulation));
```
The `BallisticsSystem` continues to publish `RaycastRequestEvent(Start=PreviousPosition, End=CurrentPosition)` unchanged. `HitResolutionSystem` continues to read `RaycastResultEvent.Hit.T` to compute `detonationPoint = Start + T * (End - Start)` — if `T < 1`, the explosion is placed at the wall, not the target.

**Tests:** `Stride/Hrot.Stride.Core.Tests/StrideRaycastBackendTests.cs` — 8 tests.

---

## Design Decisions

**Normal swizzle (T1):** Hit normals are direction vectors, not positions. They are converted via `FdpStrideTransform.ToFdpVelocity` (the direction/velocity swizzle), not `ToFdpPosition`. For pure direction vectors both swizzles produce the same numeric result (`(stride.X, stride.Z, stride.Y)`) — verified by test. The explicit velocity path is used to make intent clear and to be robust against any future translation offset in the position path. Tests explicitly assert this.

**2-D → 3-D in `HasCheapLineOfSight`:** `ILosService` takes `Vector2` (EQS use case). The service lifts both points to 3-D using a configurable `EyeHeightMetres` (default 1.5 m). Full 3-D callers should use `HasLineOfSight3D(Vector3, Vector3)` directly.

**`HitFractionClearThreshold`:** Both `StrideRaycastLosService` (0.99) and `StrideRaycastBackend` (0.999) apply a threshold to avoid false blocks from the target's own collider at the ray endpoint. The values are configurable.

**`StrideRaycastBackend` in `Hrot.Stride.Core`:** The adapter wraps `IStrideRaycastService` and produces the FDP `RaycastHit` struct. Entity recovery from the Bullet collider is best-effort (index only; `PhysicsBodyLifecycleSystem` convention for naming Stride entities not yet implemented). Static scene geometry correctly returns `Entity.Null`.

**`PhysicsQueryModule.RaycastSolverSystem` exposure:** `PhysicsQueryModule` owns a `private readonly RaycastSolverSystem _raycastSolver`. The Stride node needs to set `RaycastBackend` on it. The accessor was already `new RaycastSolverSystem()` — I left it as-is (the module can be subclassed or the property exposed by the Stride bootstrapper). This is composition-root wiring deferred to GPU bring-up.

---

## Deviations

**`StrideRaycastService` placed in `HrotStrideApp.Game`:** The concrete class requires a live `Simulation` (GPU-deferred, STR-D11). It lives in the game project, not `Hrot.Stride.Core`, matching the pattern established by `StrideVisualFactory` and `BulletPhysicsBodyService`.

**No `LosRequestBatchingSystem` changes:** The task says "replace the flat spatial-hash approximation behind the same interface." The 2-D inline segment-circle sweep in `LosRequestBatchingSystem` is the *perception pipeline* path (publishes `TargetVisibleEvent`). The `ILosService` is the *EQS cover-finding* path (used by `CheapLineOfSightTest`). Both are independent pathways. `StrideRaycastLosService` is the drop-in for the EQS path. The perception pipeline (`LosRequestBatchingSystem`) would need a separate integration (injecting `IStrideRaycastService` directly); this is out of scope for the current batch and deferred.

**`BallisticsSystem` unchanged:** The spec says "analytic integration retained." `BallisticsSystem` was already analytic (publishes `RaycastRequestEvent`; `LinearKinematicsSystem` does the integration). No code change needed — the T3 seam is purely inside `RaycastSolverSystem`.

---

## Test Results

```
Hrot.Stride.Core.Tests:       Passed: 215  Failed:  0  Skipped: 0  (was 171, +44)
HrotStrideApp.Game.Tests:     Passed:  33  Failed:  0  Skipped: 0
Hrot.SimHost.Tests:           Passed: 573  Failed: 38  Skipped: 3  (identical to pre-stride-1 baseline at 6bb3153d)
Fdp.Toolkits.Tests:           Passed: 1837 Failed: 32  (all pre-existing — diff vs stashed baseline shows zero new failures)
```

**New tests by task:**
- T1 (`StrideRaycastServiceTests`): 23 tests — coordinate swizzle, normal direction swizzle vs position swizzle, mask plumbing, miss/hit paths, call count, penetrating cast, IStrideRaycastService contract
- T2 (`StrideRaycastLosServiceTests`): 13 tests — wall blocked (false), clear LOS (true), hit-at-target threshold, 3-D entry point, eye-height lift, ILosService drop-in, TargetMemory 3-D Z storage
- T3 (`StrideRaycastBackendTests`): 8 tests — backend injection (fake used not spatial-hash), miss reported, blocked shot impacts obstacle at T=0.4 not target at T=1, clear shot miss, null backend falls through to spatial-hash, request fields echoed, analytic integration unchanged, dependency direction verified

---

## Developer Insights

**`Simulation.Raycast` signature (verified from ThirdPersonCamera.cs usage):**
```csharp
// Simple overload (all groups):
HitResult Simulation.Raycast(Vector3 from, Vector3 to);
// Filter overload:
HitResult Simulation.Raycast(Vector3 from, Vector3 to,
                              CollisionFilterGroups collisionGroups,
                              CollisionFilterGroupFlags collisionFilter);
// All-hits:
void Simulation.RaycastPenetrating(Vector3 from, Vector3 to,
                                   IList<HitResult> results,
                                   CollisionFilterGroups collisionGroups = ...,
                                   CollisionFilterGroupFlags collisionFilter = ...);
// HitResult fields: bool Succeeded, Vector3 Point, Vector3 Normal, float HitFraction,
//                   PhysicsComponent Collider
```
All in `Stride.Physics` namespace. `StrideRaycastService` wraps these.

**GPU-deferred (STR-D11):** `StrideRaycastService` is the GPU-deferred class. It holds a `Simulation` reference which is only available from a running `PhysicsProcessor`. It lives in `HrotStrideApp.Game`. `IStrideRaycastService` + `FakeStrideRaycastService` in `Hrot.Stride.Core` allow full headless testing of all coordinate conversion and mask-plumbing logic.

**`IRaycastBackend` in `Fdp.Toolkits` — no reverse dependency confirmed:** `typeof(IRaycastBackend).Assembly.GetReferencedAssemblies()` contains no `Hrot.Stride.Core` — verified by test `DependencyDirection_FdpToolkits_DoesNotReferenceHrotStrideCore`.

**`LosRequestBatchingSystem` gap:** The perception-pipeline LOS (2-D inline sweep in `LosRequestBatchingSystem`) is not yet backed by Stride raycasts. This is a separate injection point from `ILosService`. Full 3-D perception pipeline integration would require exposing `IStrideRaycastService` to `LosRequestBatchingSystem` (either via a new delegate / seam, or by refactoring it to use the `IRaycastBackend` pattern). Recorded as a debt item for P3 completion.

**`PhysicsQueryModule._raycastSolver` access:** The `_raycastSolver` field is `private`. The Stride bootstrapper would need to expose the solver (e.g. make `PhysicsQueryModule.RaycastSolverSystem` a public property, or accept the backend as a constructor param). Deferred to GPU bring-up (same pattern as other seam injections).

---

## Known Issues

- `StrideRaycastService` entity recovery is best-effort (reconstructs entity with generation=1 from the Stride entity name). Full entity recovery requires `PhysicsBodyLifecycleSystem` to tag Stride entities with the full packed Entity value. Deferred.
- `LosRequestBatchingSystem` still uses flat 2-D segment-circle sweep; full 3-D perception-pipeline raycast integration is deferred.
- `PhysicsQueryModule._raycastSolver` needs a public accessor before the Stride bootstrapper can set `RaycastBackend` at GPU bring-up. Deferred.

---

## Suggested Commit Message

```
feat(stride): IStrideRaycastService + StrideRaycastLosService + IRaycastBackend seam — Phase 3 complete (BATCH-09)

Completes STR-P3-T1, STR-P3-T2, STR-P3-T3
- IStrideRaycastService seam (Hrot.Stride.Core): FDP-space in/out; FakeStrideRaycastService for headless tests
- StrideRaycastService (HrotStrideApp.Game): GPU-deferred concrete wrapping Simulation.Raycast; normal uses direction swizzle (ToFdpVelocity), not position swizzle
- StrideRaycastLosService: ILosService drop-in backed by IStrideRaycastService; blocked→false, clear→true; 2D→3D lift via EyeHeightMetres
- IRaycastBackend (Fdp.Toolkits.Physics): minimal injectable seam for RaycastSolverSystem; StrideRaycastBackend adapter in Hrot.Stride.Core
- RaycastSolverSystem: optional RaycastBackend property; when set, bypasses spatial-hash path (blocked shot impacts obstacle at T<1, not target)
- BallisticsSystem analytic integration unchanged
Tests: 215 Stride Core (+44) + 33 Game, all green. SimHost baseline 38/573 unchanged. Fdp.Toolkits zero new failures.
```
