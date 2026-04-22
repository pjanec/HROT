# BATCH-01 Review

**Batch:** BATCH-01
**Status:** APPROVED
**Reviewer:** Dev Lead

---

## Summary

Batch 01 is approved. `Fdp.Core` consolidation is complete and clean:
- Build: 0 errors, 0 warnings
- Fdp.Core.Tests: 912 passed, 2 skipped, 0 failed
- All project references updated across 53 projects
- Both solution files updated correctly

The developer handled the EventId and ComponentId collision issues pragmatically
without violating the "no logic change" constraint.

## Issues Found

### P1 Issues (must block this batch)

None.

### P2 Issues (add to DEBT-TRACKER)

1. **Pre-existing test failures** in Hrot layer (24 Hrot.SimHost.Tests,
   7 Hrot.IG.Tests, 4 Hrot.ClusterRunner.Tests). These are confirmed pre-existing
   (caused by routing guard added in commit `23a0a63` and
   ActionDispatch counter change). Must be fixed before project completion.

2. **Stale `InternalsVisibleTo` for `ModuleHost.Core`** in Fdp.Core.csproj.
   The assembly `ModuleHost.Core` no longer exists. Harmless but misleading.
   Clean up when convenient.

3. **Developer script files** (`.dev/fix-empty-refs.ps1`, `.dev/remap-component-ids.ps1`,
   etc.) were committed to the repo. These should be moved to `.dev/scripts/` or
   deleted before final cleanup.

### P3 Issues (log for awareness)

4. Old source directories (`FDP/Common/FDP.Interfaces/`, `FDP/Kernel/Fdp.Kernel/`,
   `FDP/ModuleHost/ModuleHost.Core/`) still exist on disk with their `.cs` files.
   They are no longer part of any project but could cause confusion.
   Clean up in a future batch.

## Suggested Git Commit Message

Already committed. See commit `ef2a59e` (FDP) and `f4bb3f1` (top-level).

---

## Next Steps

- Record P2 items in DEBT-TRACKER.md
- Proceed with BATCH-02: Create Fdp.Engine
