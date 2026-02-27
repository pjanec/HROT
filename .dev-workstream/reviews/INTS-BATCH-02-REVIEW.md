# INTS-BATCH-02 Review

**Batch:** INTS-BATCH-02  
**Reviewer:** Development Lead  
**Date:** 2026-02-27  
**Status:** ✅ APPROVED (With Corrective Tasks)

---

## Summary

Phase 2 architectural consolidation tasks are functionally complete and tests are passing. However, a significant architectural violation was introduced to bypass dependency constraints.

---

## Issues Found

### Issue 1: Reflection used to bypass circular dependencies

**File:** `Bagira.Map.Common/BagiraEnvironment.cs`  
**Problem:** Using reflection to invoke `BdcTkbCatalog.RegisterAll` is a brittle hack that circumvents standard compile-time safety and assembly dependency graphs. Elegance and clean architecture must be prioritized over simple, dirty workarounds.  
**Fix:** This is a P1 issue and will be scheduled as Corrective Task 0 in the next batch to extract a proper configuration or interface layer rather than relying on reflection.

---

## Verdict

**Status:** APPROVED

**All functional requirements met, but clean architecture rules were violated. Merging, but immediate correction follows.**

---

## 📝 Commit Message

```
refactor: Architecture consolidation & bootstrapper (INTS-BATCH-02)

Completes INTS-P2-006, INTS-P2-007, INTS-P2-008, INTS-P2-009, INTS-P2-010

Centralizes common setup parameters for SimHost, IG, and IOS through a shared BagiraEnvironment component.

- Created BagiraEnvironment to uniformize TKB, GeoTransform, and DDS Domain setups.
- Updated IgApplication, SimHostApp, and IosSubsystem to leverage the unified bootstrapper.
- Fixed SubsystemOrchestrator to automatically enforce headless mode on SimHost when the IG subsystem is present.

Note: BagiraEnvironment currently uses reflection for TKB registration to avoid circular dependencies. This architecture violation will be cleaned up in the next iteration.

Tests: 583 total tests passing, including dedicated orchestration assertions.

Related: TASK-DETAILS-Integration-Troubleshooting.md (Phase 2)
```

---

**Next Batch:** INTS-BATCH-03
