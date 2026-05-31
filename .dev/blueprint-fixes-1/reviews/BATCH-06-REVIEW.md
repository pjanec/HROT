# BATCH-06 Review

**Batch:** BATCH-06  
**Reviewer:** Development Lead  
**Date:** 2026-06-03  
**Status:** APPROVED

---

## Summary

12 tasks completed (7 code fixes + 3 verified as already done + 2 debt tracker updates). 876 + 116 + 538 + 4 tests passing. 11 new tests added.

---

## Issues Found

No issues found.

---

## Test Quality Assessment

- **BPF-042**: `ApplyReload_ThrowingRegistrar_DoesNotMutateLiveRegistry` pre-populates live registry with "PreExisting", injects a throwing registrar that would add "First", asserts after failed reload that `TryGetId("First") == false` and `TryGetId("PreExisting") == true`. This verifies rollback precisely.
- **BPF-043**: `DrainPendingCallbacks_AtMostOneReloadPerCall_WhenTwoEnqueued` -- checks after first drain that exactly 1 was applied; checks after second drain that 2 total. Counts, not just "did not throw".
- **BPF-044**: `DrainPendingCallbacks_BackgroundScanFailure_FiresOnReloadFailed` -- spy captures the exception; asserts `OnReloadFailed` was called with non-null exception.
- **BPF-036**: `OnHotReloadCompleted_WatchForDeletedPin_RemainsStale` has both a surviving pin watch (asserts `IsStale == false`) and a deleted pin watch (asserts `IsStale == true`). Both branches verified.
- **BPF-037**: `Write_MidMoveFails_ReturnsFalse_AndLeavesNoTempFiles` -- uses real temp filesystem, injects failure via directory-at-target-path, asserts `Success == false`, `FailureReason != null`, and no `.tmp` files remain.
- **BPF-038**: `HardReload_ChangedStructureHash_ResetsPayloadAndBumpsVersion` now captures `versionBefore` and asserts `versionAfter > versionBefore`.
- **BPF-046**: `TierUpgrade_HappensInBeforeSync_NotInSimulation` now applies real `EntityCommandBuffer` and asserts BB4096 present, BB1024 absent.

---

## Cross-cutting Debt

BPF-011/012/013 debt tracker updates are appropriate -- prior batch fixes correctly resolved D-02, and the remaining debt items are properly documented as deferred/intentional.

---

## Verdict

**Status: APPROVED**

All requirements met. This is the final batch for blueprint-fixes-1. Ready to merge.

---

## Commit Message

```
fix: Hot reload + medium fixes + cross-cutting debt (BATCH-06)

Completes BPF-042, BPF-043, BPF-044, BPF-036, BPF-037, BPF-038, BPF-046,
and confirms BPF-049/BPF-010 as pre-existing, closes BPF-011/012/013 debt

Hot reload:
- BPF-042: ApplyReload uses staging registry; rolls back on partial failure
- BPF-043: DrainPendingCallbacks applies at most 1 reload per frame call
- BPF-044: Background scan failures enqueued and reported via OnReloadFailed

Debug:
- BPF-036: OnHotReloadCompleted only clears IsStale for pins present in new debug map

Shared infra:
- BPF-037: AtomicMultiFileWriter mid-move rollback test added

Test quality:
- BPF-038: HardReload test now asserts InstanceVersion bump
- BPF-046: TierUpgrade test uses real ECB path

Cross-cutting debt:
- BPF-011: blueprints-1 DEBT-003/004/023 updated
- BPF-012: blueprints-2 D-02 marked RESOLVED (BPF-018)
- BPF-013: breakpoints-1 D-BP-01/D-BP-04 status updated

Tests: 876 blueprint + 116 editor + 538 AiShared + 4 Fdp.Toolkits. 11 new tests.
```

---

**Next: Start other-fixes-1** (OFX tasks)
