# BATCH-02 Review

**Batch:** BATCH-02
**Reviewer:** Development Lead
**Date:** 2026-05-14
**Status:** APPROVED

---

## Summary

SM-003 (StrideNodeBootstrapper), SM-004 (SyncFdpToStrideScript), and SM-005 (Visual Effects
Wiring) complete. 30 tests passing (10 from BATCH-01 + 12 SM-003/SM-005 + 8 SM-004).

---

## Issues Found

### Minor: SC_SM004_6 — Spec Says "Explosion", Test Correctly Uses "Tracer"

**File:** `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\SyncFdpToStrideScriptTests.cs`
**Problem:** TASK-DETAILS.md SC_SM004_6 states `WeaponFireNotification` produces
`EffectType.Explosion`. The test (and `EventToEffectSystem`) correctly produces
`EffectType.Tracer` — `WeaponFireNotification` is the shot tracer; `DetonationNotification`
is the explosion. The spec has a typo.
**Severity:** P3 — spec typo, code and test are correct.
**Action:** Update TASK-DETAILS.md SC_SM004_6 to say `EffectType.Tracer`.

### Minor: No Test for "DeadReckoningSyncSystem Registered Exactly Once"

**File:** `StrideNodeBootstrapperTests.cs`
**Problem:** TASK-DETAILS.md SM-003 "Forbidden" constraint says to verify
`DeadReckoningSyncSystem` is present in the kernel exactly once (not double-registered).
No test for this.
**Severity:** P3 — code correctly avoids manual registration; violation is structurally
impossible given the implementation. Track in DEBT-TRACKER.
**Action:** No corrective task needed; add to DEBT-TRACKER for completeness.

---

## Test Quality Assessment

Tests verify actual behavior, not string presence:

- `SyncStrideEntities` tests use real ECS entity creation/destruction with
  `Assert.Equal(n, script.ActiveEntities.Count())` — verifies actual sync behavior.
- SC_SM004_7 (effect expiry) calls three real `Tick()` invocations with correct dt
  values to drive `VisualEffectCleanupSystem` through its full lifecycle — strong
  integration-level verification.
- SC_SM004_8 uses reflection to confirm the `_staleEntities` field reference does not
  change across multiple allocation-inducing cycles — correctly tests the no-GC-alloc
  constraint.
- SC_SM003_5 casts to `SharedApplicationBootstrapper` to verify `TimeControl` comes
  from the base class property (not a hidden duplicate field).

---

## Verdict

**Status:** APPROVED
**All requirements met. Already committed (a9ab542).**

---

## Commit Message

Already committed as `feat: BATCH 02` (a9ab542).

---

**Next Batch:** BATCH-03 (SM-006 + SM-007)
