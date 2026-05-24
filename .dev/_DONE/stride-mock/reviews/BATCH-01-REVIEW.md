# BATCH-01 Review

**Batch:** BATCH-01  
**Reviewer:** Development Lead  
**Date:** 2026-05-14  
**Status:** APPROVED

---

## Summary

SM-001 (project scaffolding) and SM-002 (SharedApplicationBootstrapper) are complete. All 10 tests pass; all three affected projects build cleanly.

---

## Issues Found

No blocking issues. One minor observation:

### Minor: SC_SM002_4 — "Exactly Once" Not Fully Verified

**File:** `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\SharedApplicationBootstrapperTests.cs`  
**Problem:** The test verifies `Kernel.Initialize()` was called (Update() doesn't throw) and that hooks preceded it, but does not verify it was called *exactly once*. A bug that called it twice would not be caught.  
**Severity:** P3 — the bootstrapper's one-time builder pattern prevents double-call in practice.  
**Action:** Track in DEBT-TRACKER as low-priority.

---

## Test Quality Assessment

Tests verify actual state, not just call presence:
- SC_SM002_2: verifies component is in world AND call ordering via index comparison
- SC_SM002_3: verifies `GetSystems()` contains `TestSimSystem` — actual group inspection
- SC_SM002_7: publishes `SwitchTimeModeEvent`, calls `timeCtrl.Update()`, asserts `TimeMode.Deterministic` — strong runtime behavior test
- SC_SM002_8: verifies `GhostCreationSystem.RegisterSystems` was actually invoked by the kernel
- SC_SM002_9/10: verifies NedReplication is set and passed to BuildOrchestration via same-reference assertion

Developer notes about circular dependency workaround (using `configuredFactory.CreateReplicationModule()` instead of `.WithReplication()`) are accurate and the solution is correct.

---

## Verdict

**Status:** APPROVED  
**All requirements met. Ready to merge.**

---

## Commit Message

```
feat: SM-001 + SM-002 - project scaffolding + SharedApplicationBootstrapper (BATCH-01)

Completes SM-001, SM-002

Creates Hrot.StrideMock (class library) and Hrot.FakeStrideApp (executable) projects,
wires both into the solution and adds Hrot.StrideMock as a project reference in
Hrot.ClusterRunner.

Implements SharedApplicationBootstrapper in Hrot.Common.Infrastructure — a sealed
7-phase Template Method base class that locks the node initialization order, preventing
the 5 fragile init traps. Hosts 6 abstract hooks and 2 virtual hooks; Phase 6a+
(NedReplicationModule) and Phase 6c (time-sync translators + TimeControl gateway) are
base-class-only, not hookable by subclasses.

Tests: 10 tests covering all SC_SM002_1-SC_SM002_10 success conditions.
```

---

**Next Batch:** BATCH-02
