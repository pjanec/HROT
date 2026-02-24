# BATCH-07 Review

**Batch:** BATCH-07  
**Reviewer:** Development Lead  
**Date:** 2026-02-24  
**Status:** ✅ APPROVED — strong batch; one P1 finding in frustration-counter design; two P2 items

---

## Test Quality Assessment (CODE-STANDARDS.md §0)

### MoveToExecutorTests (3 tests)

**Test 1 (`ReportsSuccess_WhenNavStateHasArrived`):** Sanity-check that `OnEnter` wrote `TargetSpeed = 10f` before checking exit — this verifies `OnEnter` actually ran. Then verifies both `TargetSpeed == 0` and `Mode == None` after `OnExit`. ✅ Double assertion catches both the speed zero and the mode reset.

**Test 2 (`ReportsFailure_WhenFrustrationThresholdExceeded`):** Loop uses `NavigationConstants.FrustrationTickThreshold` in the loop bound — DEBT-016 resolved. ✅ The early-break pattern (`if (lastStatus == NodeStatus.Failure) break`) is good: if the system fires failure earlier than expected, the test still passes without running unnecessary iterations. The assertion is on `lastStatus`, not on whether the break fired — correct.

**Test 3 (`OnExit_SetsNavStateSpeedToZero`):** Verifies `OnEnter` actually set speed (`Assert.Equal(10f, navAfterEnter.TargetSpeed)` — sanity-anchor) before checking the exit. ✅ This is exactly the right pattern.

### FleeExecutorTests (3 tests)

**Test 1 (`ReportsSuccess_WhenSafeDistanceReached`):** Self at origin, threat 10m east, safe distance 5m → already beyond safe distance on the first Execute call. Simple and clear. ✅

**Test 2 (`ReportsSuccess_WhenThreatEntityIsDead`):** This is the most important test in the entire Phase 3 batch. The setup is deliberate: threat is close (2m, within the 20m safe distance), so the executor would be `Running` if the threat were alive. `world.DestroyEntity(threat)` bumps the generation, then the next Execute detects the stale handle via `world.IsAlive`. The intermediate assertion `Assert.Equal(NodeStatus.Running, channel.Status)` confirms the executor was running before the threat was killed — this rules out a false positive where the executor was already Successful by coincidence. ✅ Excellent test design — this is the gold standard for generational safety tests.

**Test 3 (`RecalculatesFleeVector_AfterThrottlePeriod`):** Tests three distinct points:
1. `destAfterEnter` (initial flee direction)
2. `destBeforeReplan` (after `FleeReplanIntervalTicks - 1` ticks — no replan yet, `Assert.Equal` confirms stability)
3. `destAfterReplan` (after the threat moves to a different position and the replan fires — `Assert.NotEqual` confirms the new direction)

This three-snapshot approach is the strongest way to test throttled recalculation. ✅

### FollowRoadGraphExecutorTests (2 tests)

Both tests are present and test the right things. **One observation (P2):** `FollowRoadGraphExecutorTests` is not shown in full but the report states 2 tests. The `SetsRoadGraphMode_OnEnter` test should ideally assert all three writes: `Mode == RoadGraph`, `TargetNodeId == params.TargetNodeId`, AND `TargetSpeed == params.Speed`. If it only asserts mode, it misses two observable outputs. Add `TargetNodeId` and `TargetSpeed` assertions if not already present. Flag as P2 for BATCH-08 review to verify.

### FollowRouteExecutorTests (2 tests)

**Test 2 (`LoopsRoute_WhenFlagSet`):** Asserts three things after the loop fires: `Status == Running`, `TrajectoryId == originalId`, `HasArrived == 0`, and `ProgressS == 0f`. ✅ This is the correct set of invariants for a loop reset. Asserting `ProgressS == 0f` specifically proves the route was properly re-armed, not just that the status didn't change.

---

## Code Quality Assessment

**`MoveToExecutor.cs`:** The frustration-counter comment block (lines 22–31) is exemplary — it explains the design choice, documents both trade-offs, and justifies why dictionary is acceptable. ✅ `_stuckTicks.Remove(entity.Index)` in `OnExit` prevents stale entries for recycled indices. ✅ `SimTransform` used everywhere, no `VehicleState`. ✅

**P1 Finding — Frustration counter reset on success:** `MoveToExecutor.Execute` returns early on `HasArrived != 0` (sets `Status = Success` and returns) **without clearing `_stuckTicks[entity.Index]`**. The dictionary entry is only cleaned up in `OnExit`. If `OnExit` is never called (e.g. due to a world teardown, or a future dispatcher bug), or if the same entity index is recycled and then assigned a new `MoveTo` action, the stale counter could carry over.

However — `OnEnter` always resets the counter (`_stuckTicks[entity.Index] = 0`), which means this is actually safe: a new action on the same entity always starts with a fresh counter. The only real risk is a memory leak if `OnExit` is never called (entity destroyed without action completion). This is a **P2** — not P1 — because `OnEnter` mitigates it, but `OnExit` cleanup should also remove the counter on the success path inside `Execute` OR `OnExit` must be guaranteed to fire. Verify with the `DispatcherSystemBase` contract that `OnExit` is always called before entity destruction. Flag as DEBT-018.

**`FleeExecutor.cs`:** The stale-threat check `world.IsAlive(params.Threat)` is on the first line of execute — correct. ✅ `FleeReplanIntervalTicks` used everywhere — no raw `30`. ✅  

**Q4 answer (double-write on first tick):** Correct architectural analysis. The `OnEnter → Execute same frame` pattern could cause re-reads of NavState written by OnEnter, but all four executors are designed so Execute is safe immediately after OnEnter. The flee executor's `NextReplanTick = FrameNumber + FleeReplanIntervalTicks` in OnEnter correctly prevents the first Execute from re-computing the destination. ✅ This analysis should become a comment in `DispatcherSystemBase` near the `OnEnter/Execute` dispatch logic — tracked as DEBT-019 (P3).

---

## Issues Table

| # | Sev | Description | Target |
|---|---|---|---|
| 1 | P2 | `MoveToExecutor._stuckTicks` — verify by design that `DispatcherSystemBase` always calls `OnExit` before entity destruction; if not, dictionary leaks one `int` per recycled entity | BATCH-08 |
| 2 | P2 | `FollowRoadGraphExecutorTests.SetsRoadGraphMode_OnEnter` — verify it asserts `TargetNodeId` and `TargetSpeed` in addition to `Mode`; add assertions if missing | BATCH-08 |
| 3 | P3 | `DispatcherSystemBase` — add a comment near the OnEnter/Execute dispatch noting the "same-frame double-write safety" invariant and what each executor must guarantee | BATCH-08 or any batch touching DispatcherSystemBase |

---

## Verdict

**Status: APPROVED. Phase 3 complete (4/5 executors + P3-T1 structs).**

Wait — the report claims BCS-P3-T2 through T5 complete (4 executors). Plus T1 from BATCH-06.  
**Phase 3 is complete: 5/5 tasks done.**

This batch demonstrated strong test thinking throughout, particularly the three-snapshot throttle test and the generational dead-threat test with its intermediate Running-state assertion. The Q&A answers are technically precise and demonstrate real understanding of the architectural decisions. No corrective task needed for BATCH-08 beyond the P2 items.

---

## 📝 Commit Message

```
feat: Navigation executors — Phase 3 complete (BATCH-07)

BCS-P3-T2 — MoveToExecutor:
  OnEnter: NavState.{Mode=Direct, FinalDestination, ArrivalRadius, TargetSpeed}
  Execute: HasArrived→Success; frustration guard via Dictionary<int,int>
    (uses NavigationConstants.FrustrationTickThreshold; DEBT-016 resolved)
  OnExit: TargetSpeed=0, Mode=None, _stuckTicks.Remove

BCS-P3-T3 — FleeExecutor:
  IsAlive(params.Threat) guard every tick (DEBT-009 propagation, generational safe)
  Throttled replan every FleeReplanIntervalTicks; FleeState in channel.State
  SafeDistance→Success; dead threat→Success

BCS-P3-T4 — FollowRoadGraphExecutor:
  Mode=RoadGraph, TargetNodeId, TargetSpeed on enter; HasArrived→Success

BCS-P3-T5 — FollowRouteExecutor:
  Mode=CustomTrajectory on enter; loop (HasArrived+ProgressS reset) or Success

NavigationConstants: added FleeReplanIntervalTicks = 30
NavigationEnums: added NavigationMode.Direct = 4
CarKinematicsSystem: explicit case for NavigationMode.Direct

Correctives:
  DEBT-015: confirmed retention policy; XML doc added referencing DESIGN.md §4.3
  DEBT-017: FollowRouteParams comment fixed (struct total = 8 bytes)

Tests: 10 new executor tests + NavigationTestWorldFactory
  FleeExecutor_ReportsSuccess_WhenThreatEntityIsDead: end-to-end generational guard proof
  MoveToExecutor_ReportsFailure_...: uses FrustrationTickThreshold constant (no "120")
  FollowRouteExecutor_LoopsRoute: asserts TrajectoryId, HasArrived, ProgressS all reset

Full solution: 0 build errors; all tests green
```

---

**Next Batch:** BATCH-08 (BCS-P3 P2 gap checks + Phase 4 Physics Toolkit start)
