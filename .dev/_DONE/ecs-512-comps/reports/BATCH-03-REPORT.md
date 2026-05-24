# BATCH-03 Report

**Batch:** BATCH-03
**Developer:** AI Developer (GitHub Copilot)
**Date:** 2025-01-19
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| D003 | Complete | `HotMasks_AreIndependentPerEntity` added to `EntityIndexHotColdTests.cs` |
| D005 | Complete | All `Unsafe.As<BitMask512, BitMask256>` projections annotated with `// TODO(ecs-512)` comments |
| TASK-E006 | Complete | All iteration methods rewritten hot-first; new `EntityQueryHotFirstTests.cs` with 6 tests |
| TASK-E007 | Complete | `GetSnapshotableMask`/`GetRecordableMask`/`GetSaveableMask` return `BitMask512`; all callers updated; 3 new tests in `EntityRepositoryTests.cs` |

---

## Testing Results

**Unit Tests Passed:** 774-775 / 777 (2 pre-existing flaky failures, see below)
**Skipped:** 2 (unchanged from BATCH-02)
**New tests added:** 10 (6 `EntityQueryHotFirstTests`, 3 `EntityRepositoryTests`, 1 `EntityIndexHotColdTests`)

**Key Test Scenarios Verified:**
- [x] D003: Setting bit 400 on entity A does not affect entity B's mask
- [x] TASK-E006: Entity with bit 400 appears in query; entity without does not
- [x] TASK-E006: Exclude filter correctly skips entities with excluded bit
- [x] TASK-E006: Destroyed entity never appears even with empty include mask
- [x] TASK-E006: `ForEachParallel` result matches `ForEach` result
- [x] TASK-E006: `Count()`/`Any()` correct for empty world and 3-entity world
- [x] TASK-E007: `AddComponent` sets hot mask bit 350
- [x] TASK-E007: `RemoveComponent` clears hot mask bit 350
- [x] TASK-E007: `GetRecordableMask()` returns `BitMask512`; empty mask for clear registry
- [x] All pre-existing `QueryTests.cs`, `EntityQueryEnumeratorTests.cs`, `EntityRepositoryTests.cs` tests pass

**Pre-existing flaky failures (NOT caused by BATCH-03):**
- `EventBusTests.Publish_MultiThreaded_AllEventsRecorded` — timing-sensitive multi-threaded test; fails intermittently due to resource contention; was failing before BATCH-03
- `ComponentDirtyTrackingTests.ComponentDirtyTracking_PerformanceScan` — performance-threshold test; fails intermittently under load
- Both tests pass when run in isolation (`dotnet test --filter`) with no failures

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Three unexpected call sites for the old 3-param `Matches(int, in BitMask512, in EntityMetadataCold)`
signature were missed when the signature was changed to 2-param in TASK-E006:
- `EntityRepository.cs` line ~1468 (inside `QueryWithChangeDetection`)
- `EntityRepository.cs` line ~1506 (inside `QueryTimeSliced`)
- `EntityRepository.DeltaQuery.cs` line ~174 (inside `DeltaEnumerator.MoveNext`)

These were caught immediately on the first build after the `Matches` signature change and fixed
by dropping the unused entity index argument from each call site.

A second minor issue: the TASK-E007 test for `GetRecordableMask` used `mask.IsEmpty` as a
property, but `BitMask512.IsEmpty` is a method. Fixed by using `mask.IsEmpty()`.

**Q2: Did you find any other call sites that still referenced `EntityHeader` or the old `GetHeader` API? What did you find?**

No remaining `EntityHeader` or `GetHeader` references were found in `EntityQuery.cs`,
`EntityRepository.cs`, or `EntityRepository.DeltaQuery.cs` after BATCH-02. The three callers of
`Matches` that were missed used the split `GetComponentMask`/`GetMetadata` API already — they
just had not been updated to the new 2-param `Matches` signature, which was a straightforward
mechanical fix.

**Q3: What design decisions did you make beyond the spec?**

1. **`SyncFrom` internal mask type**: The spec said to change the internal `effectiveMask` from
   `BitMask256` to `BitMask512`. The `SyncFrom` method's public `mask:` parameter stays
   `BitMask256?` for backward compatibility. The conversion uses
   `Unsafe.As<BitMask512, BitMask256>(ref effectiveMask) = mask.Value` to copy the lower 256 bits
   into the new `BitMask512`, with upper 256 bits staying zero — safe because `BitMask512` and
   `BitMask256` share identical explicit layout for the lower 32 bytes.

2. **`EntityInspectorPanel.cs` BitwiseAnd order**: Both D005-annotated locations previously did
   `BitMask256.BitwiseAnd(BitMask512)` (which is not an overload that exists). After TASK-E007
   made `GetSnapshotableMask()` return `BitMask512`, the fix was to perform `BitwiseAnd` on the
   `BitMask512` component mask first, then project to `BitMask256` for `SerializeEntity`.

3. **`ScenarioSerializer.cs` BitwiseAnd order**: Same pattern as EntityInspectorPanel — moved
   `entityComponents512.BitwiseAnd(globalSaveable)` before the `Unsafe.As` projection, so the
   AND is done on 512-bit masks and then only the lower 256 bits are projected for the 0-255
   auto-serializer loop.

**Q4: What edge cases did you discover during the `MoveNext()` rewrite?**

The empty-include-mask case: when a query has no required components, `HasAll(mask, emptyInclude)`
returns `true` for every entity including destroyed ones. The hot-first rewrite ensures destroyed
entities still get rejected — after BATCH-02, `DestroyEntity` zeroes the hot mask, so a destroyed
entity with a non-empty include mask never reaches the cold `IsActive` check. For an empty include
mask, the entity reaches the cold check and `IsActive == false` rejects it. Both paths are tested
in `DestroyedEntity_NeverAppearsInQuery`.

The `EntityRepository.DeltaQuery.cs` enumerator already had a hot check (`compDQ` passed before
`metaDQ`) but called the old 3-param `Matches`. The fix was purely a call-site signature update;
no logic change was required.

**Q5: Are there any concerns about the hot-first ordering that the design lead should know about?**

The `EntityRepository.QueryWithChangeDetection` method (lines ~1430-1473) mixes concerns:
it reads cold metadata (`LastChangeTick`) as part of the change detection logic **before**
calling `Matches`, which does the hot mask check. This means cold memory is accessed for every
entity regardless of component mask. This is arguably correct for change detection (we need
`LastChangeTick` to decide if anything changed), but it creates an exception to the hot-first
principle in that code path. It is not part of the TASK-E006 scope (which covers `EntityQuery`
iteration only), but it is worth noting as a potential future optimization target.

---

## Suggested Commit Message

```
feat(ecs): Phase 4+5 EntityQuery hot-first + EntityRepository BitMask512 masks (BATCH-03)

D003: Add HotMasks_AreIndependentPerEntity test to EntityIndexHotColdTests
D005: Annotate Unsafe.As<BitMask512,BitMask256> projections with TODO(ecs-512)

TASK-E006: EntityQuery hot-first two-stage traversal
- Hot mask check (include/exclude) before any cold metadata access
- ForEach/Count/Any/FirstOrNull/ForEachChunked/ForEachParallel all hot-first
- Matches(in BitMask512, in EntityMetadataCold) replaces 3-param overload
- EntityRepository.QueryWithChangeDetection and QueryTimeSliced updated
- EntityRepository.DeltaQuery.DeltaEnumerator.MoveNext updated
- New EntityQueryHotFirstTests: include@400, exclude@310, dead-entity
  short-circuit, parallel==serial, Count/Any correctness (6 tests)

TASK-E007: EntityRepository.Sync.cs mask methods return BitMask512
- GetRecordableMask/GetSnapshotableMask/GetSaveableMask return BitMask512
- SyncFrom internal effectiveMask upgraded to BitMask512
- All callers updated with Unsafe.As downcast + TODO(ecs-512):
  EntityStateExtractionService, ComponentDiffService,
  EntityInspectorPanel (both locations), ScenarioSerializer
- 3 new EntityRepositoryTests: AddComponent sets bit 350,
  RemoveComponent clears bit 350, GetRecordableMask returns BitMask512

Tests: 777 total (10 new), 774-775 passed, 2 skipped, 0 BATCH-03 failures
```

---

## Outstanding Issues / Next Steps

- [ ] Pre-existing flakiness in `EventBusTests.Publish_MultiThreaded_AllEventsRecorded` and
      `ComponentDirtyTrackingTests.ComponentDirtyTracking_PerformanceScan` should be addressed
      in a separate stabilization batch (not BATCH-03 scope).
- [ ] `ScenarioSerializer.SerializeEntity`, `ImGui/EntityInspectorPanel`, and
      `EntityStateExtractionService` all have `TODO(ecs-512)` markers pointing to the eventual
      upgrade of their `BitMask256` parameters to `BitMask512`. This is Phase 6 scope.
- [ ] `EntityRepository.QueryWithChangeDetection` reads cold metadata before the hot mask check.
      Future optimization opportunity (not blocking).
