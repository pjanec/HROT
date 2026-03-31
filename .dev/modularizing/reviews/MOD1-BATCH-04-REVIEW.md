# MOD1-BATCH-04 Review

**Batch:** MOD1-BATCH-04  
**Reviewer:** Development Lead  
**Date:** 2026-03-16  
**Status:** ⚠️ APPROVED WITH CAVEATS

---

## Summary

The developer executed Phase 4 cleanly, creating the `IgPresentationModule` and `SimPresentationModule` and handling the complex perspective switching infrastructure correctly. 

Moreover, the circular dependency fixes generated working structural solutions: 
- `LinearKinematicsSystem` was successfully rehoused. 
- `ActionDispatchModule` leveraged Dependency Inversion (DI) via `IActionExecutor<T>` to successfully break the compile-time cycle and move the dispatch engine to `FDP.Toolkit.Behavior`.

**However, significant functional and architectural gaps remain:**

---

## Issues Found

### Issue 1: Partial Generalization of Combat/Formation Executors (CT-MOD1-I)
While using DI (`IActionExecutor<T>`) successfully unlinked `ActionDispatchModule` from `Hrot.SimHost` at compile time, **the actual executors (`AimAndFireExecutor` and `JoinFormationExecutor`) were left behind in the `Hrot` domain.**
- **Why It Matters:** This violates the spirit of the generalization directive. Reusable kinetic and combat executors are generic engine features. If they belong in FDP, they must be moved to FDP. The user has explicitly authorized the creation of new toolkits (e.g., `FDP.Toolkit.Combat`) to house these properly.

---

## Verdict

**Status:** APPROVED WITH CAVEATS

**Required Actions for Next Batch:**
1. Formally extract the combat and formation executors into brand-new FDP toolkits, eliminating the Hrot domain residency for these systems once and for all.

---

**Next Batch:** MOD1-BATCH-05
