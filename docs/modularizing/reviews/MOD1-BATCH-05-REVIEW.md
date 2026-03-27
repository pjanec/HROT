# MOD1-BATCH-05 Review

**Batch:** MOD1-BATCH-05  
**Reviewer:** Development Lead  
**Date:** 2026-03-16  
**Status:** ⚠️ NEEDS FIXES (Partial Accept)

---

## Summary

The developer successfully completed the Phase 5 (Component ID Split) and initial Phase 6 (Perception) structs, managing to isolate the Bagira native structs into the `BagiraComponentIds` while retaining FDP generics in `GlobalComponentIds`. The `FDP.Toolkit.Combat` toolkit extraction was properly executed for `JoinFormationExecutor`. 

**However, the developer's attitude towards the test failures in `Bagira.IG.Tests` is completely unacceptable.**

---

## Issues Found

### Issue 1: Ignoring `Bagira.IG.Tests` Failures (CT-MOD1-J)

**Problem:** In Task P5T1, the developer fixed a pre-existing build error inside `Bagira.IG.Tests`. This subsequently exposed 4 failing tests in `EditToolTests` and `AdvancedFeaturesIntegrationTests.Phase4`. The developer dismissed them as "pre-existing" and left them failing, stating they should be investigated in a follow-up batch. 
- **Why It Matters:** We are conducting a massive modularization refactoring. The fact that the EditTool or Integration suite is failing could very well be a subtle repercussion of our recent structural changes (e.g. system ordering, components relocation, event-firing mechanics, or ECS singleton states). Leaving tests failing is how tech debt rots a codebase. **A green build means ALL tests pass.**

**Required Fix:** The 4 failing tests must be investigated and fixed immediately before continuing. 

---

## Verdict

**Status:** NEEDS FIXES (Progressing to BATCH-06 with immediate mandate)

**Required Actions for Next Batch:**
1. Extremely high priority: Investigate and completely fix the 4 failing tests in `Bagira.IG.Tests`. You must trace exactly why `HandleDrag` and `Phase4` integration fail and restore them to green.
2. After the tests pass perfectly, continue with the remaining Phase 6 architecture.

---

**Next Batch:** MOD1-BATCH-06
