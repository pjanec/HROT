# MOD1-BATCH-03 Review

**Batch:** MOD1-BATCH-03  
**Reviewer:** Development Lead  
**Date:** 2026-03-16  
**Status:** ⚠️ NEEDS FIXES (Partial Accept)

---

## Summary

The developer successfully implemented Phase 3 tasks involving the `NodeBootstrapper` and translator packs. `CT-MOD1-C2` effectively addressed the blueprint parsing for entity creation because the prior crash is indeed gone. The `NodeConfiguration` parses cleanly. 

**However, the primary mission-critical functionality remains broken, and several structural regressions from Batch 02 remain unaddressed.**

---

## Issues Found

### Issue 1: `MoveToLocation` Command Fails in Runtime (CT-MOD1-D)

**Problem:** While the null-ref/missing component exception was resolved, the vehicle still **does not move** when instructed via the `Bagira.Runner` (which runs in `-x all` configuration). This suggests that although the entities have components, the systems or executors responsible for navigating or bridging those intents are either missing from the `Runner` node profile or are failing silently under integration conditions. 

**Required Fix:** This is a blocking regression. We must restore full moving capability. Integration tests must assert actual position mutation (coordinate alteration) over successive frames, not just "Did Not Throw" assertions!

### Issue 2: `ActionDispatchModule` Circular Dependency Excusability (CT-MOD1-E)

**Problem:** In MOD1-BATCH-02, the developer left `ActionDispatchModule` in the `Bagira.SimHost` domain because of `JoinFormationExecutor` depending on Bagira constants/logic. As stated by the user, **this is an unacceptable deviation from the modularisation goal.** What belongs in FDP must be made generic and moved to FDP.

**Required Fix:** Break this dependency immediately. You are authorized to extract generic dispatch logic into `FDP.Toolkit.Behavior` or `FDP.Toolkit.Combat`. Use Dependency Inversion (interfaces/delegates) for Bagira-specific executors (like `JoinFormationExecutor`) and register them separately during composition. DO NOT leave generic dispatch engines in the `Bagira.SimHost` aggregate.

### Issue 3: `LinearKinematicsSystem` Circular Dependency (CT-MOD1-F)

**Problem:** A cycle between `FDP.Toolkit.Physics` and `FDP.Toolkit.CarKinem` caused `LinearKinematicsSystem` to be dumped into `Bagira.SimHost` earlier.

**Required Fix:** Resolve this cycle. You may extract shared structs/components into `FDP.Toolkit.Kinematics.Core` or `FDP.Kernel` and restructure the system registrations correctly natively. 

---

## Verdict

**Status:** NEEDS FIXES (Progressing to next batch under mandate)

**Required Actions:**
1. Fix the Bagira.Runner vehicle movement logic completely as Top Priority. 
2. Correct the BATCH-02 architectural regressions (CT-MOD1-E, CT-MOD1-F) before starting Phase 4.

---

**Next Batch:** MOD1-BATCH-04
