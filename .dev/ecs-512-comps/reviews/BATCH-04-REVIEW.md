# BATCH-04 Review

**Reviewer:** Dev Lead  
**Date:** 2026-05-23  
**Verdict:** APPROVED WITH MINOR FIX APPLIED

---

## Summary

BATCH-04 added binary-level test coverage for the dual-stream flight recorder (TASK-E008),
playback routing verification (TASK-E009), fixed the D006 test quality gap, and documented
the D004 known limitation with a regression test.

A test isolation bug (`ComponentTypeRegistry.Clear()` called from a test method) was found
during review and fixed inline before committing. All other deliverables are solid.

---

## Findings

### Critical — Fixed Before Commit

**C1: `ComponentTypeRegistry.Clear()` in `DualStream_RecordableMaskFilter_NonRecordableBitIsCleared`**

The test called `ComponentTypeRegistry.Clear()` on the global static registry at the start of
the test body. When xUnit runs tests in parallel (the default), this Clear() races with
`CheckpointIOWorkerTests` and other tests that call `RegisterComponent`, causing intermittent
`ComponentTypeRegistry.GetOrRegister<T>()` exceptions. Reproduced on the first full run
(5 failures in CheckpointIOWorkerTests stack traces).

**Fix:** Added `NoRecordTestComponent` (`[ComponentId(240)]`) to `TestComponents.cs` and
rewrote the test to use that component instead of `IntComponent`. The Clear() call was
removed. No other test uses ID 240, so registration with `DataPolicy.NoRecord` is safe
without touching global state.

Status: Fixed. Full test suite re-run confirms BATCH-04 tests pass and CheckpointIOWorkerTests
are stable.

---

### Minor — Noted, No Action Required

**M1: `Delta_OnlyRecordsModifiedComponents` guard is slightly brittle**

The existing test (pre-BATCH-04) uses `if (tId != -1)` to skip entity-index hot chunks.
With dual-stream, typeId==-2 cold chunks also need excluding. The test happens to be correct
today because the specific scenario produces no entity-index chunks, but the guard should be
`if (tId != -1 && tId != -2)` for long-term robustness. Logged in DEBT-TRACKER (new item D008).

**M2: `GetRecordableMask()` called inside the delta flush loop**

In `RecordDeltaFrame`, `GetRecordableMask()` is rebuilt on every chunk loop iteration via
LINQ. It is already moved outside the loop in `RecordAllChunks`. Logged in DEBT-TRACKER
as D009 (low priority).

---

### Pre-existing Flaky Tests (Not Introduced by BATCH-04)

The following tests fail intermittently under load due to timing thresholds:
- `MilitarySimulationPerformanceTest.RealisticMilitrarySimulation_CompleteScenario_MeasuresPerformance`
- `LifeCycleSchemaTests.EntityLifecycle_CreationDeletionRecreation_VerifiesSchemaAndState`
- `ComponentDirtyTracking_PerformanceScan`

All pass in isolation. These are pre-existing P3 issues unrelated to this workstream.

---

## Test Quality Assessment

| Test | Quality | Notes |
|------|---------|-------|
| `DualStream_Keyframe_WritesHotAndColdChunks` | PASS | Parses raw binary, asserts typeId==-1 hot AND typeId==-2 cold chunks both present |
| `DualStream_HotChunkSize_EqualsCapacityTimes64` | PASS | Asserts `dataLen == capacity * sizeof(BitMask512)` from raw bytes |
| `DualStream_Sanitization_DeadEntitySlotIsAllZeros` | PASS | Checks actual 64-byte zero block at destroyed entity offset in recorded bytes |
| `DualStream_RecordableMaskFilter_NonRecordableBitIsCleared` | PASS (after fix) | Bit-level assertion on recorded bytes; isolation bug fixed by reviewer |
| `FormatVersion_WrittenInGlobalHeader_Is5` | PASS | Both constant-level check and file-level binary parse; covers full AsyncRecorder write path |
| `RoundTrip_EntityIndexHotAndColdMatchOriginal` | PASS | Checks entity count, hot mask bit 164, cold Generation, AND cold IsActive after playback |
| `VersionMismatch_OldFormat_ThrowsInvalidDataException` | PASS | Writes a minimal v4 header to temp file and asserts RecordingReader throws InvalidDataException |
| `GetRecordableMask_ReturnsBitMask512_WithRegisteredBit` | PASS | Registers IntComponent, asserts mask.IsSet(164) and !mask.IsEmpty() |
| `Delta_ColdOnlyDirectMutation_KnownLimitation_NotCaptured` | PASS | Proves D004 gap: direct cold mutation without LastChangeTick stamp not captured in delta |

All TASK-E008 and TASK-E009 success criteria are met with real binary-level assertions, not
just behavioural smoke tests.

---

## Verdict

BATCH-04 is **APPROVED**. The implementation was already correct (dual-stream writes and
playback routing were in place from BATCH-02). BATCH-04 added the required test coverage
and fixed D006 test quality.

The one test isolation bug (C1) was fixed by the reviewer inline; no further developer action
required on that item.

New technical debt items D008 and D009 are logged below.
