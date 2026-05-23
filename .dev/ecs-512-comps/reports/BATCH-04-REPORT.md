# BATCH-04 Report

**Batch:** BATCH-04
**Developer:** GitHub Copilot
**Date:** 2026-05-23
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| D006 | Complete | `GetRecordableMask_ReturnsBitMask512_WithRegisteredBit` now registers `IntComponent` and asserts `mask.IsSet(164)`. |
| D004 | Documented + test added | Comment added to `RecorderSystem.cs` entity index loop. Test `Delta_ColdOnlyDirectMutation_KnownLimitation_NotCaptured` in `RecorderDeltaLogicTests.cs` proves the limitation. |
| TASK-E008 | Complete | 5 new tests in `RecorderSystemTests.cs`; dual-stream code verified correct as-is. |
| TASK-E009 | Complete | 2 new tests in `PlaybackSystemTests.cs`; routing code verified correct as-is. |

---

## Testing Results

**Unit Tests Before:** 777 total (774 passed, 1 failed [pre-existing perf flake], 2 skipped)
**Unit Tests After:** 785 total (783 passed, 0 failed, 2 skipped)
**New tests added:** 8

**Key Test Scenarios Verified:**

- [x] TASK-E008 SC-1: `DualStream_Keyframe_WritesHotAndColdChunks` — binary parse confirms typeId==-1 hot chunk AND typeId==-2 cold chunk both present.
- [x] TASK-E008 SC-2: `DualStream_HotChunkSize_EqualsCapacityTimes64` — data length == `GetChunkCapacity() * 64`.
- [x] TASK-E008 SC-3: `DualStream_Sanitization_DeadEntitySlotIsAllZeros` — 64-byte zero block at destroyed-entity slot confirmed.
- [x] TASK-E008 SC-4: `DualStream_RecordableMaskFilter_NonRecordableBitIsCleared` — bit 164 absent from recorded hot mask when component is `NoRecord`.
- [x] TASK-E008 SC-5: `FormatVersion_WrittenInGlobalHeader_Is5` — `AsyncRecorder` writes `FORMAT_VERSION == 5u`.
- [x] TASK-E009 SC-1/2/3: `RoundTrip_EntityIndexHotAndColdMatchOriginal` — entity count, hot mask bit, cold generation, and `IsActive` all match after playback.
- [x] TASK-E009 SC-5: `VersionMismatch_OldFormat_ThrowsInvalidDataException` — `RecordingReader` throws `InvalidDataException` on FORMAT_VERSION 4 file.
- [x] D006: `GetRecordableMask_ReturnsBitMask512_WithRegisteredBit` — bit 164 confirmed set after registering `IntComponent`.
- [x] D004: `Delta_ColdOnlyDirectMutation_KnownLimitation_NotCaptured` — delta produces no entity index chunks when cold data is mutated without `LastChangeTick` stamp.
- [x] All existing `RecorderSystemTests`, `RecorderDeltaLogicTests`, `PlaybackSystemTests`, `FlightRecorderIntegrationTests`, `ManagedComponentPlaybackTests` pass.

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

No blocking issues. The dual-stream code in `RecorderSystem.cs` and the routing logic in
`PlaybackSystem.cs` were already correctly implemented from BATCH-02. The BATCH-04 work
was entirely additive: fixing the D006 test quality gap, documenting D004, and adding the
binary-level test coverage specified in TASK-E008 and TASK-E009.

The main discovery was the cold-chunk capacity relationship: each hot chunk (capacity 1024)
maps to exactly 2 cold chunks (capacity 512 each), but in a single-entity world only
cold chunk 0 exists, so the hot/cold correspondence is 1:1. The tests use `>= 1` assertions
to stay stable across both configurations.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

1. **D004 (P2, open):** `RecordDeltaFrame` relies entirely on `EntityMetadataCold.LastChangeTick`
   to detect structural changes. Any path that modifies cold fields (e.g., `LifecycleState`,
   `DisType`) without going through a stamping method silently drops those changes in delta
   frames. The proper fix is to additionally check the `NativeChunkTable<EntityMetadataCold>`
   per-chunk version, mirroring how component tables are checked. This is captured in
   DEBT-TRACKER D004.

2. **D007 (P3, open):** `QueryWithChangeDetection` reads cold `LastChangeTick` before the hot
   mask check, violating the hot-first principle. Not a correctness issue but a cache-miss cost.

3. **`Delta_OnlyRecordsModifiedComponents` in `RecorderDeltaLogicTests.cs`** uses the guard
   `if (tId != -1)` to exclude entity-index chunks from the component count. With dual-stream,
   typeId==-2 cold chunks also need excluding. The test happens to be safe today because no
   entity-index chunks appear in that specific scenario, but the guard is brittle. A cleaner
   guard would be `if (tId > 0)` or `if (tId != -1 && tId != -2)`.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

For the FORMAT_VERSION test (TASK-E008 SC-5), the instruction says to verify the
`RecordingGlobalHeader` in the stream. Since `RecorderSystem.RecordKeyframe/RecordDeltaFrame`
do not write the global header (that is `AsyncRecorder`'s responsibility), the test was
written to go through `AsyncRecorder` and read back the raw bytes from the file. An alternative
was to just assert `FdpConfig.FORMAT_VERSION == 5` as a pure unit assertion; the test does
both to cover the entire write path.

For D004, the BATCH-04 instructions explicitly permitted deferring the fix as a P2 item with
a documenting comment and test. I chose documentation + test over implementation because
the fix requires adding per-cold-chunk version tracking to the entity index flush loop, which
is adjacent to the frame synchronisation subsystem and carries non-trivial test risk.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- When the cold chunk capacity (512) is exactly half of the hot chunk capacity (1024), a
  single hot chunk maps to up to 2 cold chunks. In the keyframe path this is handled by
  the `firstColdKF / lastColdKF` range loop. The delta path uses the same calculation.
  This was already correct in the existing code; the tests confirm it works with a small world.

- `ComponentTypeRegistry.Clear()` must be called at the start of the recordable-mask-filter
  test to prevent state leakage from parallel test classes. Without it, `IntComponent` could
  already be registered as recordable (default) from a prior test, so the `NoRecord` policy
  override via `RegisterComponent<IntComponent>(DataPolicy.NoRecord)` must be applied on a
  clean slate to guarantee the recorded mask omits bit 164.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- **Cold-chunk fan-out in RecordAllChunks:** For worlds with many hot chunks (e.g., 1M+ entities),
  the cold-chunk inner loop re-calls `FillLiveness` and `CopyColdChunkToBuffer` for each cold
  chunk that overlaps a hot chunk. With capacity ratio 2:1, this is fine; if the ratio grows
  (e.g., by reducing cold struct size), the fan-out increases proportionally.
- **`GetRecordableMask()` called once per flush loop iteration** in `RecordDeltaFrame`. It
  rebuilds the mask from scratch each call via LINQ. Moving the call outside the loop would
  be a micro-optimization (already done in `RecordAllChunks`; missed in `RecordDeltaFrame`).

---

## Outstanding Issues / Next Steps

- [ ] D004 (P2): Implement per-cold-chunk dirty version tracking in `RecordDeltaFrame`.
      Fix: check `entityIndex._coldMeta.GetChunkVersion(cc) > prevTick` alongside the
      `ChunkHasStructuralChanges` hot check, then write cold chunks independently.
- [ ] D007 (P3): Move `LastChangeTick` check in `QueryWithChangeDetection` after the hot
      mask check to restore hot-first ordering.
- [ ] `Delta_OnlyRecordsModifiedComponents`: update guard from `tId != -1` to
      `tId != -1 && tId != -2` for correctness under dual-stream format.
