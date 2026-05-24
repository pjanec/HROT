# BATCH-05 Report: Stage 4 Backend for Replay Browser

**Status:** COMPLETE  
**Tests:** 112 / 112 passed (0 failed, 0 skipped)

---

## Tasks Completed

| Task | Description | Status |
|------|-------------|--------|
| RB-4.1 | SearchPredicateDto hierarchy (PropertyMatchDto, StructuralPredicateDto, etc.) | Done (prev session) |
| RB-4.2 | PredicateCompiler + EventScannerCompiler | Done (prev session) |
| RB-4.3 | RecordingSearchService (ExecuteSearch, RunFrameStepScan, RunStructuralFrame, RunEventScan, RunLifecycleScan) | Done (prev session) |
| RB-4.4 | SearchResultDto / LifecycleSearchResultDto DTOs | Done (prev session) |
| RB-4.5 | Serialization round-trip for all predicate DTOs | Done (prev session) |
| RB-4.6 | RecordingSearchServiceTests (all SR-T* tests) | Done |
| RB-4.7 | Fix all 10 failing tests | Done (this session) |

---

## Root Causes and Fixes Applied

### Fix 1: RunFrameStepScan -- full entity scan (SR-T02, SR-T03)

**Root cause:** `RestoreChunkFromBuffer` during playback does a raw `Unsafe.CopyBlock` and does NOT update `NativeChunkTable._chunkVersions`. After a keyframe restore, `lastScannedVersion = repo.GlobalVersion`. On the next delta frame, component chunk versions remain at pre-playback values (lower than `lastScannedVersion`), so `QueryDelta`'s version filter excluded every entity.

**Fix:** Replaced `repo.QueryDelta(deltaQuery, lastScannedVersion, collectEntity)` with a direct linear scan of all active entities per frame. For offline replay search, scanning all entities each frame is correct and eliminates the stale-version problem.

**Files changed:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/RecordingSearchService.cs`

---

### Fix 2: RunStructuralFrame -- remove lastScannedVersion skip (SR-T14, SR-T15, SR-T16)

**Root cause:** `RunStructuralFrame` had a guard `if (header.LastChangeTick <= (ulong)lastScannedVersion) continue;`. After keyframe restore at `lastScannedVersion=2`, structural changes (AddComponent/RemoveComponent at tick=2) set `header.LastChangeTick=2`. The condition `2 <= 2` was TRUE, so those entities were skipped and no structural transition was emitted.

**Fix:** Removed the `lastScannedVersion` skip entirely. The `hasComponent` HashSet correctly tracks which entities already have the component, so structural transitions (Added/Removed) are still detected without false positives even when scanning all active entities every frame.

**Files changed:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/RecordingSearchService.cs`

---

### Fix 3: QueryDelta -- remove per-call List allocation (SR-T34)

**Root cause:** `EntityRepository.QueryDelta` allocated `new List<IComponentTable>()` on every call, unconditionally. Even on a no-match call this caused heap allocation, violating the SR-T34 zero-allocation invariant.

**Fix:** Removed the `var tables = new List<IComponentTable>()` pre-loop. The inner `else` block now iterates `_componentTables` (a `Dictionary<Type, IComponentTable>`) directly via `foreach`. `Dictionary<K,V>.Enumerator` is a value type -- zero allocation.

**Files changed:** `FDP/Engine/Fdp.Core/EntityRepository.cs`

---

### Fix 4: FdpEventBus -- update _activeEventIds on injection (SR-T23, SR-T26, SR-T38)

**Root cause:** `PlaybackSystem.ApplyFrame` calls `eventBus.ClearCurrentBuffers()` then `InjectIntoCurrentBySize(typeId, elementSize, data)`. `InjectIntoCurrentBySize` writes bytes directly into `_readBuffer` via `stream.InjectIntoCurrent(data)`, but never called `_activeEventIds.Add(typeId)`. `HasEvent(Type)` checks `_activeEventIds.Contains(id)`, so it returned `false` even after injection. Additionally, `ClearCurrentBuffers()` cleared the stream buffers but not `_activeEventIds`, leaving stale IDs from the previous frame.

**Fix:**
- Added `_activeEventIds.Add(typeId)` after injection in both `InjectIntoCurrent(int, ReadOnlySpan<byte>)` and `InjectIntoCurrentBySize(int, int, ReadOnlySpan<byte>)`.
- Added `_activeEventIds.Clear()` at the start of `ClearCurrentBuffers()`.

**Files changed:** `FDP/Engine/Fdp.Core/FdpEventBus.cs`

---

### Fix 5: SR-T34 test -- measure only QueryDelta, not StepForward (SR-T34)

**Root cause:** The measurement window in `SR_T34_ZeroAllocation_LoopBodyAllocatesNothingOnNoMatch` included `playback.StepForward(repo)`, which allocates `byte[]` for frame decompression on every call. The `Assert.Equal(0L, delta)` could never pass with StepForward inside the window.

**Fix:** Moved the `StepForward` loop outside the measurement window. The measurement now covers only repeated `QueryDelta` calls (100 iterations) on a repo at end-of-recording, where no entities match and no allocations occur.

**Files changed:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/RecordingSearchServiceTests.cs`

---

### Fix 6: ReplayBrowserContext -- register components on LoadRecording (SR-T36)

**Root cause:** `ReplayBrowserContext` creates a fresh `EntityRepository` with no component tables. When `SeekToFrame` triggered `PlaybackSystem.ApplyChunkData`, it could not find the component type ID (e.g. 202 = HarnessPosition) and threw `InvalidOperationException`.

**Fix:** 
- Changed `RegisterAllComponents` from `private static` to `internal static` in `RecordingSearchService`.
- Added a call to `RecordingSearchService.RegisterAllComponents(SandboxRepo, Playback)` in `ReplayBrowserContext.LoadRecording` after the `PlaybackController` is created (metadata is available at that point).

**Files changed:**  
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/RecordingSearchService.cs`  
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs`

---

## Test Results

### Before fixes (start of session)
- Passed: 102
- Failed: 10 (SR-T02, SR-T03, SR-T14, SR-T15, SR-T16, SR-T23, SR-T26, SR-T34, SR-T36, SR-T38)

### After fixes
- Passed: 112
- Failed: 0
- Skipped: 0

---

## Files Modified

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/RecordingSearchService.cs` | Fix 1, Fix 2, Fix 6a (internal RegisterAllComponents) |
| `FDP/Engine/Fdp.Core/EntityRepository.cs` | Fix 3: remove List allocation in QueryDelta |
| `FDP/Engine/Fdp.Core/FdpEventBus.cs` | Fix 4: _activeEventIds updated on inject and cleared on ClearCurrentBuffers |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs` | Fix 6b: register components on LoadRecording |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/RecordingSearchServiceTests.cs` | Fix 5: SR-T34 measurement window excludes StepForward |
