# BATCH-51 Review

**Verdict: APPROVED** (with one minor fix applied by reviewer)

---

## Build & Test Summary

| Metric | Result |
|---|---|
| Build errors | 0 |
| Build warnings | 0 |
| Unit tests (Breakpoints.Tests) | 119 / 119 passed |
| Integration tests (BreakpointSubsystemWiring) | 20 / 20 passed |
| BTree editor tests | 167 / 167 passed |
| HSM editor tests | 192 / 192 passed |
| **Total** | **498 / 498** |

---

## Reviewer Fix Applied

**Stale TODO comment removed** from `DataBreakpointSystem.cs`:
```diff
- // TODO: optimise by tracking the last-scanned version per breakpoint
-         //       and passing it here instead of 0 so unchanged entities are skipped.
  int componentId = ComponentTypeRegistry.GetId(t);
```
The optimization was implemented (P11T2) but the developer left the TODO comment in place. Removed by reviewer during review.

---

## Tasks Reviewed

### P11T1 — Zero-allocation DataBreakpointSystem.Execute

**APPROVED.**
- `private readonly List<Entity> _pendingHitsBuffer = new();` field added to `DataBreakpointSystem`.
- `var pendingHits = new List<Entity>()` replaced with `_pendingHitsBuffer.Clear()` — eliminates per-tick `List<Entity>` allocation.
- `repo.QueryDelta(query, 0u, entity => {...})` (lambda overload) replaced with `foreach (var entity in repo.QueryDelta(query, compiled.LastScanVersion))` — uses zero-allocation `DeltaQueryEnumerable` ref struct. No closure object created per tick.
- `_pendingHitsBuffer.Clear()` is called at the top of each breakpoint's loop iteration, preventing hits from one BP leaking into another.

Tests:
- `DataBreakpointSystem_StillFiresHits_AfterZeroAllocRefactor`: Substantive regression test — 3 entities with `AllocTestHealth { Current = 50 }`, BP `Current > 0`, confirms `IsPaused == true` and `PauseRequestCount == 1`. ✓
- `DataBreakpointSystem_ReusableBuffer_ClearedBetweenBreakpoints`: BP-A never matches; BP-B matches all 3 entities. Asserts `IsPaused == true` and `PauseRequestCount == 1`. Proves buffer is cleared between breakpoints (otherwise BP-A might get phantom hits). ✓

### P11T2 — Chunk-version-aware QueryDelta scanning

**APPROVED.**
- `CompiledComponentPredicate` record gains `public uint LastScanVersion { get; set; } = 0u;` in a record body block. Well-documented with XML-doc noting that hot-reload creates new instances (auto-reset to 0).
- `compiled.LastScanVersion` read as `sinceVersion` before `QueryDelta` loop.
- `compiled.LastScanVersion = repo.GlobalVersion` set AFTER the QueryDelta loop and BEFORE the `OnHit` calls. This is correct ordering — the version is captured from the live repo before `OnHit` rewrites `liveRepo` via `SyncFrom`.
- Hot-reload reset: `TryMountDelegate` calls `_componentPredicates[id] = new CompiledComponentPredicate(del, mandatory)`, creating a fresh instance with `LastScanVersion = 0u`. No extra code needed. ✓

**Deviation — `OccurrenceThreshold_PausesOnNthHit` updated**: The test previously called `system.Execute` three times on an UNCHANGED entity, relying on `sinceVersion = 0u` returning entities on every call. With `LastScanVersion` tracking, unchanged chunks are skipped. The fix calls `repo.Tick() + repo.GetComponentRW<TestDamage>(entity)` between each `Execute` to advance the chunk version (simulating what a real ECS tick does). This is the correct semantic: OccurrenceThreshold now counts "Nth mutation-tick in which condition holds", not "Nth consecutive tick where condition holds". This aligns with DESIGN intent.

Tests:
- `DataBreakpointSystem_OnSecondExecute_DoesNotFireIfNoMutation`: First execute fires BP (pauses), `RequestContinue()`, re-sync snapshot, second execute with no mutations → no fire. Asserts `IsPaused == false` and `PauseRequestCount == 1`. ✓
- `DataBreakpointSystem_AfterMutation_DetectsNewEntity`: Same setup, then adds a new entity after version is advanced → third execute detects the new entity → `IsPaused == true`, `PauseRequestCount == 2`. ✓
- Both tests call `liveRepo.Tick()` before the initial populate to ensure the component additions are "in the past" relative to the first execute's `LastScanVersion` update. ✓

### P11T9 — Eliminate Mounted* accessor allocations

**APPROVED.**
- `_cachedComponentPredicates` and `_cachedEventScanners` nullable fields added near other private fields.
- `MountedComponentPredicates` and `MountedEventScanners` getters: return cached list if non-null, otherwise build and cache.
- Cache invalidation at END of `TryMountDelegate` (both caches) and after `Remove` calls in `UnmountDelegate` (both caches). All mutation paths covered.

The `CompiledComponentPredicate` instances in the cached list are the SAME objects stored in `_componentPredicates`. When `DataBreakpointSystem` reads `compiled.LastScanVersion` and updates it, it modifies the actual stored object — works correctly with the cache.

Tests:
- `MountedComponentPredicates_ReturnsSameInstance_BetweenMutations`: Calls getter twice, asserts `ReferenceEquals`. ✓
- `MountedComponentPredicates_Invalidated_AfterNewBreakpointAdded`: Gets cache, adds BP (invalidates), gets again → `NotSame`, `Count == 2`. ✓

---

## Issues for Next Batch

None. No debt introduced by BATCH-51.
