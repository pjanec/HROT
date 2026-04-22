# DTE-BATCH-02 Review

**Batch:** DTE-BATCH-02  
**Reviewer:** Development Lead  
**Date:** 2026-02-28  
**Status:** ?? NEEDS FIXES

---

## Summary
Phase 2/3 features are mostly implemented with adequate tests, but there are design violations in SimHost egress ownership filtering and SimHostSubsystem still registering `EntityMaster` in ECS.

---

## Issues Found

### Issue 1: EntityMasterEgressTranslator ownership check uses local parameter instead of component

**File:** `Hrot.SimHost/Translators/EntityMasterEgressTranslator.cs`  
**Problem:** Ownership is filtered with `ownership.PrimaryOwnerId != _localNodeId`. The design requires comparing against `ownership.LocalNodeId` (`PrimaryOwnerId == LocalNodeId`) so the translator follows component state instead of the constructor parameter.  
**Fix:** Replace the ownership check with `if (ownership.PrimaryOwnerId != ownership.LocalNodeId) continue;`.

### Issue 2: SimHostSubsystem still registers DDS DTO `EntityMaster`

**File:** `Hrot.ClusterRunner/Services/SimHostSubsystem.cs`  
**Problem:** `RegisterSimComponents` still registers `EntityMaster`, violating the DDS DTO separation rules. This keeps the runner path out of compliance even though `SimHostApp` was fixed.  
**Fix:** Remove `world.RegisterComponent<EntityMaster>();` from the subsystem�s component registration.

---

## Test Quality Assessment
No issues found. Tests validate behaviors (component contents, DDS publish/dispose).

---

## Verdict

**Status:** NEEDS FIXES

**Required Actions:**
1. Fix ownership check in `EntityMasterEgressTranslator`.
2. Remove `EntityMaster` registration from `SimHostSubsystem`.

---

**Next Batch:** DTE-BATCH-03 prepared
