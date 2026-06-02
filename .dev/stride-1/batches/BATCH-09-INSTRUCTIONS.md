# BATCH-09: Perception + ballistics via Stride raycasts (Phase 3)
**Tasks:** STR-P3-T1, STR-P3-T2, STR-P3-T3   **Phase:** P3 (Perception)   **Est:** ~9–11h
**Dependencies:** Phases 0–2 complete. `FdpStrideTransform` (BATCH-01); the `IPhysicsBodyService`/seam pattern (BATCH-04).

Goal — real LOS/occlusion + ballistics against scene geometry. (T1) `StrideRaycastService` wraps `Simulation.Raycast` (+ penetrating/overlap/sweep), all I/O via `FdpStrideTransform`; (T2) inject it as the LOS/occlusion backend behind the existing vision seam → `TargetMemory` (3D); (T3) back `BallisticsSystem` hit resolution via the existing `RaycastSolver`/`HitResolution` seam. **Same constraint as physics: Stride's `Simulation`/`Simulation.Raycast` need a running `PhysicsProcessor` and are not headlessly creatable — so `StrideRaycastService` is the seam (`IStrideRaycastService`); the LOS/ballistics injection + coordinate conversion logic is validated with a scriptable fake, and the concrete `Simulation.Raycast` wrapper is GPU-deferred (STR-D11 class).**

No Corrective Task 0 (BATCH-08 approved).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/stride-1/Stride-Integration_v0_3.md` §6.3 (raycasts — perception + ballistics — the spec), §4 (coordinate seam).
3. `.dev/stride-1/TASK-DETAIL.md` — STR-P3-T1, STR-P3-T2, STR-P3-T3.
4. `reviews/BATCH-08-REVIEW.md` + `DEBT-TRACKER.md` (STR-D11 seam pattern).

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

### Verified facts & exact references
- **LOS seam (T2)** = `ILosService` ([ILosService.cs](../../../FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/ILosService.cs)) with `HasCheapLineOfSight(...)` and the `BlockedLosService` fallback; consumed by `CheapLineOfSightTest`/`AccurateLineOfSightTest`. **[VERIFY]** the exact `ILosService` contract + where the current fake-LOS is injected (the "fake LOS entry point"), and how perception feeds `TargetMemory` ([PerceptionComponents.cs](../../../FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs) — `TargetMemory`, now 3D per the cognitive promotion).
- **Ballistics seam (T3)** = `RaycastSolverSystem` ([RaycastSolverSystem.cs](../../../FDP/Toolkits/Fdp.Toolkits/Physics/Systems/RaycastSolverSystem.cs)) + `HitResolutionSystem` ([HitResolutionSystem.cs](../../../FDP/Toolkits/Fdp.Toolkits/Physics/Systems/HitResolutionSystem.cs)); `BallisticsSystem` ([BallisticsSystem.cs](../../../FDP/Toolkits/Fdp.Toolkits/Combat/Systems/BallisticsSystem.cs)) keeps analytic integration. **[VERIFY]** how `RaycastSolverSystem` performs raycasts today (spatial-hash) and the cleanest injection point to back it with an external raycast provider (an injectable backend interface in `Fdp.Toolkits` that `Hrot.Stride.Core` implements — `Fdp.Toolkits` must NOT reference `Hrot.Stride.Core`; the dependency goes the other way).
- **Stride `Simulation.Raycast` [VERIFY]**: signature `Simulation.Raycast(from, to, out HitResult, CollisionFilterGroups, CollisionFilterGroupFlags)` + penetrating/overlap/sweep variants on 4.2.1.2487; and that `Simulation` is only available from a running game (so the concrete wrapper is GPU-deferred, like `BulletPhysicsBodyService`).
- All ray inputs/outputs cross `FdpStrideTransform` (BATCH-01). `Hrot.Stride.Core` references `Stride.Physics`.

**Complete tasks in sequence (T1 → T2 → T3); do NOT start the next until the current is implemented, tested, and ALL tests (incl. prior batches') pass.** If T2/T3 require changing a `Fdp.Toolkits` seam to accept an injectable raycast backend, do it minimally and **re-verify no regression in the broader FDP/SimHost suite** (run the affected test projects; the pre-stride-1 baseline has 38 pre-existing `Hrot.SimHost.Tests` failures — don't add to them). Work autonomously. Only stop on a genuine breaking design flaw or unrecoverable blocker.

---

## Task 1: `StrideRaycastService` (STR-P3-T1)
**File:** `Stride/Hrot.Stride.Core/StrideRaycastService.cs` + `IStrideRaycastService` seam (NEW). Spec: design §6.3.
Define `IStrideRaycastService` in `Hrot.Stride.Core` exposing raycast (and penetrating/overlap/sweep) queries in **FDP coordinates** — inputs FDP `from`/`to` + collision mask, outputs an FDP-space hit (point, normal, hit entity/collider, fraction). The concrete `StrideRaycastService` converts FDP↔Stride via `FdpStrideTransform`, calls `Simulation.Raycast`, and converts the hit back to FDP (it holds a `Simulation` injected from the running game — GPU-deferred, document it). Provide a thin coordinate-conversion core that is unit-testable without a live `Simulation`.

**Tests required** (headless):
- Coordinate round-trip: an FDP ray `from`/`to` converts to the expected Stride endpoints, and a Stride hit point/normal converts back to the expected FDP values (assert numeric values via `FdpStrideTransform`; normal uses the direction swizzle, not the position one — verify).
- Collision-mask plumbing: the mask passed in reaches the (faked) `Simulation.Raycast` call unchanged.
- (Document, don't headlessly test, the live `Simulation.Raycast` against real geometry — GPU-deferred, STR-D11 class.)

## Task 2: Perception LOS via Stride raycasts (STR-P3-T2)
**File:** `Stride/Hrot.Stride.Core/StrideRaycastLosService.cs` (or similar) implementing `ILosService` (NEW). Spec: design §6.3.
Implement the LOS/occlusion check behind the existing `ILosService` seam using `IStrideRaycastService`: a clear ray ⇒ visible; a ray blocked by scene geometry ⇒ not visible. Feed results into the existing perception/`TargetMemory` pathway (3D-correct). Replace the flat spatial-hash approximation behind the same interface (drop-in). **[VERIFY]** the injection point so the Stride LOS service is selected on the Stride node.

**Tests required** (headless, fake `IStrideRaycastService`):
- Wall between observer and target (fake returns a blocking hit before the target) ⇒ `HasCheapLineOfSight` (or the LOS result) is **false**.
- Clear LOS (fake returns no hit / hit beyond the target) ⇒ visible; `TargetMemory` updated with 3D-correct data (assert the 3D position/modality written).
- Drop-in: the service satisfies `ILosService` and is selected behind the same interface.

## Task 3: Ballistics raycast seam (STR-P3-T3)
**Files:** the injectable raycast-backend wiring in `Fdp.Toolkits` (minimal) + the `Hrot.Stride.Core` adapter. Spec: design §6.3.
Back `RaycastSolverSystem`/`HitResolutionSystem` hit resolution with `IStrideRaycastService` via the existing seam, keeping `BallisticsSystem`'s analytic projectile integration. A shot whose path is blocked by geometry resolves an impact at the obstacle, not the target.

**Tests required** (headless, fake `IStrideRaycastService`):
- Analytic integration retained (the projectile path is still computed analytically; assert the integration is unchanged).
- Hit tests use the Stride raycast backend: a shot with a blocking obstacle between shooter and target resolves the impact at the **obstacle** (assert the resolved hit point/entity is the obstacle, not the target); a clear shot resolves at the target.

---

## Success Criteria
- [ ] STR-P3-T1: `IStrideRaycastService` + `StrideRaycastService` with FDP↔Stride conversion (incl. normal direction-swizzle) and mask plumbing; concrete `Simulation.Raycast` wrapper GPU-deferred + documented.
- [ ] STR-P3-T2: LOS via Stride raycasts behind `ILosService` (drop-in); blocked-by-wall ⇒ not visible; clear ⇒ visible + `TargetMemory` 3D-correct.
- [ ] STR-P3-T3: ballistics hit resolution backed by the Stride raycast seam (analytic integration retained); blocked shot impacts the obstacle.
- [ ] Full test suite green (all prior batches + this); no new `Hrot.SimHost.Tests` failures beyond the 38 pre-existing; Stride solution builds clean; report submitted.

## Report Requirements (`reports/BATCH-09-REPORT.md`)
Answer: the `ILosService` contract + fake-LOS injection point you found, and how the Stride LOS service is selected (drop-in) ([VERIFY] result); the `RaycastSolverSystem` raycast mechanism today + the minimal injectable-backend seam you added in `Fdp.Toolkits` (and confirmation `Fdp.Toolkits` does not reference `Hrot.Stride.Core`); the `Simulation.Raycast` signature; how the normal is swizzled (direction vs position); how blocked-LOS / blocked-shot are asserted via the fake; whether any `Fdp.Toolkits` seam change risked broader regressions (and the SimHost-baseline check result); what remains GPU-deferred (concrete `StrideRaycastService`); weak points; suggested one-line commit message. Report actual test counts/output. Do NOT ask comprehension questions.
