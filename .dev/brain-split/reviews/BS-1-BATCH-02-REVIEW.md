# BS-1-BATCH-02 Review

**Batch:** BS-1-BATCH-02  
**Reviewer:** Development Lead  
**Date:** 2026-03-26  
**Status:** ✅ APPROVED

---

## Summary

Implemented the first end-to-end Weapon Fire CQRS transport cut: added `WeaponFireIntentEgressTranslator` (BS1-T005), `WeaponFireRequestIngressTranslator` (BS1-T006), and refactored `FireProcessingSystem` to consume `WeaponFireIntent` and publish `WeaponFireNotification` (BS1-T007). Headless scenario wiring and restored UrbanAmbush milestones were fixed via TD-1/TD-2, and `AuthorityExtensions.HasAuthority` was corrected via TD-3.

---

## Issues Found

### Issue 1: “Skip silently” requirement vs debug logging

**File:** `Hrot.SimHost/Network/Ingress/WeaponFireRequestIngressTranslator.cs`  
**Problem:** When entities are missing, `ProcessSample` logs `FdpLog<WeaponFireRequestIngressTranslator>.Debug(...)`. The task spec for BS1-T006 says to “skip silently (no exception)” when either entity is not found. Logging at debug level may still violate the spec intent (high-frequency noise).
**Fix:** Remove the log lines or gate them behind a build-time / config flag that defaults off.

---

### Issue 2: T007 ordering constraint not asserted

**File:** `FDP/Toolkits/FDP.Toolkit.Combat.Tests/FireProcessingSystemTests.cs`  
**Problem:** The BS1-T007 constraint requires `WeaponFireNotification` be published **after** the bullet entity exists. The current tests validate notification payload and bullet existence after the system runs, but they do not prove the ordering within the same frame.
**Fix:** Strengthen the test to validate ordering (e.g., publish-time instrumentation or a “bullet exists at moment notification is produced” assertion pattern).

---

### Issue 3: Design risk (not in this batch’s scope)

**File:** `Hrot.SimHost/Modules/CombatModule.cs`, `Hrot.SimHost/NodeBootstrapper.cs` (context)  
**Problem:** `FireProcessingSystem` spawns bullets without an authority gate. This is acceptable as a POC as long as role assignment (BS1-T016) is handled next; otherwise Brain-tier execution could spawn bullets in a distributed topology.
**Fix:** Address in Phase 4 via BS1-T016 role reconfiguration + any needed authority gating.

---

## Test Quality Assessment

New/updated tests validate meaningful correctness:
- DDS topic/serialization-level behavior is covered for T005/T006 translators.
- `FireProcessingSystemTests` verify bullet component creation and key notification payload values.

The main remaining gap is the explicit ordering assertion mentioned in Issue 2.

---

## Verdict

**Status:** ✅ APPROVED

No blockers for moving to the next batch, but please address Issue 1 and Issue 2 in the next BS-1 batch.

---

## 📝 Commit Message

```
feat: BS-1 weapon CQRS transport + fire intent refactor (BS-1-BATCH-02)

Completes BS1-T005, BS1-T006, BS1-T007 and restores headless/UrbanAmbush integration milestones via TD-1/TD-2/TD-3.

Adds WeaponFireRequest egress/ingress translators and updates FireProcessingSystem to spawn bullets from WeaponFireIntent and emit WeaponFireNotification.

Tests: all relevant translator/unit/integration test suites.
Related: docs/brain-split/BS-1-DESIGN.md, docs/brain-split/BS-1-TASK-DETAIL.md
```

---

**Next Batch:** BS-1-BATCH-03

