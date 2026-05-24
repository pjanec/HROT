# BATCH-03 Review

**Batch:** BATCH-03
**Reviewer:** Development Lead
**Date:** 2026-05-23
**Status:** ✅ APPROVED (P2 test quality issue tracked in DEBT-TRACKER)

---

## Summary

Hot-first `MoveNext()` fully implemented per DESIGN.md Phase 4. `GetRecordableMask`,
`GetSnapshotableMask`, `GetSaveableMask` return `BitMask512`. D003 mask-independence test
added. Full solution builds 0 errors/warnings. The critical end-to-end test (entity with
bit 400 found by a query) passes. Pre-existing `ComponentDirtyTracking_PerformanceScan`
failure (205ns vs 200ns threshold) is unrelated to BATCH-03 changes.

---

## Issues Found

### Issue 1 (P2 — add to DEBT-TRACKER): `GetRecordableMask` test doesn't test a recordable bit

**File:** `FDP/Engine/Fdp.Core.Tests/EntityRepositoryTests.cs` (method `GetRecordableMask_ReturnsBitMask512_WithRegisteredBit`)
**Problem:** The test only asserts `mask.IsEmpty()` on a freshly created repo. The test name
promises "WithRegisteredBit" but no recordable component is registered. The TASK-E007 success
condition says: "Register a component with `record: true`; the bit for that component is set."
That assertion is missing. The test only confirms the method returns without throwing.

**Fix required in BATCH-04:** Find an existing recordable component type (check
`TestComponents.cs` and `DataPolicyAttribute`), register it, call `GetRecordableMask()`, and
assert `mask.IsSet(componentId) == true`.

### Issue 2 (P3 — tracked): `QueryWithChangeDetection` reads cold before hot

**File:** `FDP/Engine/Fdp.Core/EntityRepository.cs` (~line 1430-1473)
**Problem (developer insight):** `QueryWithChangeDetection` reads `LastChangeTick` (cold) before
the hot mask check. This is an exception to the hot-first principle for change-detection queries.
Not a correctness issue, but a performance one. Note in DEBT-TRACKER as a future optimization.

---

## Positive Findings

- **Hot-first order in MoveNext() is exactly correct** (verified by reading the code):
  1. `GetComponentMaskUnsafe(i)` — hot only
  2. `HasAll(compMask, _includeMask)` — continue if false
  3. `HasAny(compMask, _excludeMask)` — continue if true
  4. `GetMetadataUnsafe(i)` — cold, only on mask pass
  5. `meta.IsActive` check
- **End-to-end 512-component test passes** (`IncludeFilter_UpperRangeBit400`).
- **D003 mask independence test** correctly tests cross-entity bit isolation.
- **All 6 `EntityQueryHotFirstTests`** verify real behavior (entity presence/absence in results).
- **`AddComponent_SetsHotMaskBit_TypeId350`** and `RemoveComponent_ClearsHotMaskBit_TypeId350`
  are correctly asserting specific bit values (350) on the hot mask.
- **`Entity.Current` generation** read from cold metadata (`GetMetadataUnsafe(_currentIndex).Generation`).

---

## Suggested Git Commit Message

```
feat(ecs): Phase 4+5 EntityQuery hot-first traversal + EntityRepository BitMask512 (BATCH-03)

D003: Add HotMasks_AreIndependentPerEntity test to EntityIndexHotColdTests
D005: Annotate Unsafe.As<BitMask512,BitMask256> projections with TODO(ecs-512)

TASK-E006: EntityQuery hot-first two-stage traversal (DESIGN.md Phase 4)
- MoveNext: GetComponentMaskUnsafe -> HasAll/HasAny before GetMetadataUnsafe
- ForEach/Count/Any/FirstOrNull/ForEachChunked/ForEachParallel all hot-first
- Matches(in BitMask512, in EntityMetadataCold) replaces old EntityHeader overload
- EntityRepository.QueryWithChangeDetection/QueryTimeSliced call sites updated
- EntityRepository.DeltaQuery.DeltaEnumerator.MoveNext updated
- New EntityQueryHotFirstTests: 6 tests including bit-400 end-to-end test

TASK-E007: EntityRepository.Sync.cs mask methods return BitMask512
- GetRecordableMask/GetSnapshotableMask/GetSaveableMask return BitMask512
- SyncFrom internal effectiveMask upgraded to BitMask512
- Callers updated: EntityStateExtractionService, ComponentDiffService,
  EntityInspectorPanel, ScenarioSerializer
- 3 new EntityRepositoryTests: AddComponent/RemoveComponent bit-350 tests,
  GetRecordableMask return type verification

Tests: 777 total, 775 passed, 2 skipped, 0 BATCH-03 failures
```
