# BATCH-01 Review

**Batch:** BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-05-27
**Status:** ⚠️ APPROVED WITH P1 CORRECTIVE TASK

---

## Summary

NAV-P0-T1/T2/T3 implemented correctly. Build clean, EQS integration tests all pass. One
pre-existing navigation test is broken and requires a fix in BATCH-02. The developer's
test count in the report was inaccurate (reported 23/0 but the navigation filter reveals
64 passed / 1 failed).

---

## Issues Found

### Issue 1 (P1): Pre-existing test failure not fixed — must go into BATCH-02

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationIntentBridgeSystemTests.cs` (line ~94–107)
**Problem:** `NoneIntent_IsSkipped_NavStateUnchanged` asserts `Expected: Direct, Actual: None`.
The test comment says "None intent is skipped — NavState retains current values unchanged",
but `NavigationIntentBridgeSystem.cs` explicitly writes `nav.Mode = KinematicsMode.None`
when `NavigationMode.None` is encountered. The test's assumption is wrong.

This failure predates BATCH-01 (`NavigationIntentBridgeSystem.cs` was not changed by this
batch). The developer's report did not mention it — either missed, or the filter used for
reporting excluded it.

**Fix for BATCH-02:**
The `NavigationMode.None` means "stop the entity / cancel the navigation order". The bridge
system correctly halts it. Update the test to assert `KinematicsMode.None` and `TargetSpeed==0`
as the correct post-condition. Also update the test name to
`NoneIntent_StopsNavigation_NavStateSetToNone` and fix the comment.

### Issue 2 (P3): Inaccurate report test count

**Problem:** Developer reported "23 nav tests green" but running `--filter Navigation` shows
64 passed / 1 failed. This suggests the developer either used a tighter filter or didn't
run the full set. No impact on code quality, but worth raising.

---

## Test Quality Assessment

Newly added tests (`NavigationEnumsTests`, `NavmeshProviderTests`) are solid — they verify
actual enum values by integer equality and verify actual PlanPath return counts and waypoint
positions. No shallow tests. EQS migration tests exercise the full EQS pipeline via the
existing integration test harness.

---

## Verdict

**Status:** APPROVED — P1 fix (`NoneIntent_IsSkipped_...`) added as Corrective Task 0 in
BATCH-02. The BATCH-01 implementation work is correct.

---

## Commit Message

```
feat: nav-v2 Phase 0 foundations — KinematicsMode, INavmeshProvider, assembly policy (BATCH-01)

Completes NAV-P0-T1, NAV-P0-T2, NAV-P0-T3

Establishes navigation subsystem v2 foundations:
- NAV-P0-T1: Assembly placement policy documented; all new production code in Fdp.Toolkits.
- NAV-P0-T2: KinematicsMode extended with Crowd=5, Naval=6, Flying=7 (DSC-2: design proposed
  Crowd=4 colliding with Direct=4; corrected to next free values).
- NAV-P0-T3: INavmeshProvider redefined with 7-method 3D interface (IsWalkable, ProjectToNavmesh,
  SampleNavmeshPoints, PathExists, PathCost, QueryVersion, PlanPath). All EQS callers
  (NavmeshReachableTest, PathCostScoreTest, NavmeshSamplesGenerator, StubNavmeshProvider)
  migrated to new 3D API with layerMask defaults and TODO-NAV-P0-T5 markers.

Tests: 23 new/updated tests (NavigationEnumsTests, NavmeshProviderTests); 62 EQS integration
tests pass; 4 AccurateLos tests pass.
```

---

**Next Batch:** BATCH-02 — P1 corrective fix + NAV-P0-T4, NAV-P0-T5
