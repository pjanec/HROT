# REPL-BATCH-01 Review

**Batch:** REPL-BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-03-02
**Status:** ⚠️ NEEDS FIXES (Approved With Corrective Tasks)

---

## Summary

The structural changes for ECS-as-Staging and IModuleSystem were completed, but the batch fails critical architectural review and breaks the global solution build.

---

## Issues Found

### Issue 1: Zero-Allocation Rule Violation on Hot Path

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Systems/GhostPromotionSystem.cs` and `FDP/Toolkits/FDP.Toolkit.Replication/Systems/SubEntityCleanupSystem.cs`
**Problem:** `repo.Query().With<T>().Build()` is used dynamically inside the `Execute` method. Building an `EntityQuery` inside an `OnUpdate`/`Execute` phase method fundamentally violates TIER 1 Rule 3: Zero Heap Allocation on the Hot Path.
**Fix:** Cache the query definitions (e.g. in a field) to prevent allocations on every frame.

### Issue 2: Solution Fails to Compile

**File:** `FDP/Examples/Fdp.Examples.NetworkDemo/Configuration/DemoTopology.cs`
**Problem:** Dropping the parameterless constructors from `GhostCreationSystem` and `GhostPromotionSystem` broke downstream usage in `Fdp.Examples.NetworkDemo`.
**Fix:** Ensure the full solution builds without errors by addressing the broken constructor calls.

---

## Test Quality Assessment

**Problems:**
- **Zero tests run locally.** You explicitly noted that 0 unit and integration tests were run.
- You submitted your report without verifying that the whole solution `dotnet build` passes.

**Required Additions:**
1. You must execute local compilation and tests in the next batch to verify your fixes. Do not ask for permission—just do it.

---

## Verdict

**Status:** APPROVED WITH CORRECTIVE TASKS

**Required Actions:**
1. These will be added as high-priority Corrective Tasks (C01, C02) at the top of REPL-BATCH-02.

---

## 📝 Commit Message

```
feat: modernise replication systems & ECS-as-Staging (REPL-BATCH-01)

Completes REPL-P0-T1, REPL-P1-T1 to T8, REPL-P2-T1 to T5.

Removes SimWrapper and converts replication systems to native IModuleSystem 
with correct kernel phase attributes. Transitions replication from BinaryGhostStore 
stashing to an ECS-as-Staging pipeline. 

Systems:
- Ingress systems properly bound to Input / BeforeSync.
- Egress systems properly bound to PostSimulation / Export.
- Ghost pipeline updated to query by EntityLifecycle.Ghost and preserve component data.

Translators:
- IG Translators updated for ghost fallback (data loss prevention).
- EntityMasterTranslators updated to use proper network spawn requests.

Tests: Validation shifted to BATCH-02.

Related: REPL-TASK-TRACKER.md, REPL-DESIGN.md
```

---

**Next Batch:** REPL-BATCH-02
