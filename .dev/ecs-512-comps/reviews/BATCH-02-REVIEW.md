# BATCH-02 Review

**Batch:** BATCH-02
**Reviewer:** Development Lead
**Date:** 2026-05-23
**Status:** ✅ APPROVED (P2/P3 issues tracked in DEBT-TRACKER)

---

## Summary

Corrective tasks D001 and D002 fixed. `EntityIndex` fully rewritten with parallel hot
(`NativeChunkTable<BitMask512>`) and cold (`NativeChunkTable<EntityMetadataCold>`) tables.
`EntityHeader.cs` deleted. All call sites across engine, toolkits, and flight recorder
updated. Full solution builds with 0 errors, 0 warnings. 765 tests pass.

---

## Issues Found

### Issue 1 (P2 — add to DEBT-TRACKER): Missing "Mask Independence" test

**Expected test:** Create entity A and entity B. Set bit 400 on A's hot mask. Assert B's hot
mask does NOT have bit 400 set.

The developer substituted `HotAndCold_ChunkCapacities_AreDifferent` for this test. While
the substitution is useful, it doesn't cover the key invariant that individual entity slots in
the hot array are truly independent. If there were a buffer-level aliasing bug in the hot
table, this test wouldn't catch it. Add the mask independence test in BATCH-03.

### Issue 2 (P2 — add to DEBT-TRACKER): Delta recorder misses cold-only changes

**File:** `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs`
**Problem:** Cold chunks are only written when the corresponding hot chunk has structural
changes (dirty version). If cold-only fields (`LastChangeTick`, `DisType`, `LifecycleState`)
change without any component add/remove, those changes are not recorded in delta frames. They
will appear in the next keyframe, creating a window where playback diverges from the live state.
**Priority:** P2 — deferred, document in DEBT-TRACKER. Full fix requires per-cold-chunk dirty
tracking (analogous to the existing hot chunk version check).

### Issue 3 (P3 — note in DEBT-TRACKER): Transient `Unsafe.As<BitMask512, BitMask256>` usage

**Files:** `ScenarioSerializer.cs`, `ImGui/EntityInspectorPanel.cs`
**Problem:** Several non-upgraded callers still expect `BitMask256`. The developer used
`Unsafe.As<BitMask512, BitMask256>(ref mask512)` as a safe lower-half projection. This is
correct given the explicit layout, but it ties these callers to an implementation detail.
These should be properly upgraded to `BitMask512` in BATCH-03 (Phase 5 EntityRepository work).

---

## Positive Findings

- **Test quality: good.** All 7 new hot/cold tests verify actual values (bit states, generation
  numbers, population counts, liveness flags). No "compile-only" tests.
- **D001 corrective correctly fixed:** `f.FieldType == typeof(int)` with `Assert.NotEmpty(fields)`.
- **D002 corrective correctly applied:** `Pack = 64` on `BitMask512`.
- **Dispose:** Both `_hotMasks` and `_coldMeta` properly disposed.
- **Invariant correctness:** `DestroyEntity` clears hot mask AND cold `IsActive` AND decrements
  population on both tables — all in the same lock scope.
- **RecorderSystem cold chunk sanitization** issue found and fixed autonomously (correct insight:
  liveness-based zeroing was required for cold chunks just as for hot).
- **Developer insights** on cold dirty-tracking gap and `Unsafe.As` downcast are valuable.

---

## Suggested Git Commit Message

```
feat(ecs): Phase 3 EntityIndex hot/cold rewrite (BATCH-02)

TASK-E005: Replace EntityHeader with parallel BitMask512 + EntityMetadataCold tables
- EntityHeader.cs deleted
- EntityIndex._hotMasks: NativeChunkTable<BitMask512> (64 bytes/entity)
- EntityIndex._coldMeta: NativeChunkTable<EntityMetadataCold> (128 bytes/entity)
- New API: GetComponentMask/GetMetadata (and unsafe variants)
- New API: CopyHot/ColdChunkToBuffer, RestoreHot/ColdChunkFromBuffer, SanitizeHot/ColdChunk
- CreateEntity/DestroyEntity/SyncFrom/ForceRestoreEntity all maintain both tables
- RecorderSystem: dual-stream write (typeId=-1 hot, typeId=-2 cold) with cold sanitization
- PlaybackSystem: ApplyChunkData routes -1->RestoreHot, -2->RestoreCold
- All call sites updated: EntityQuery, EntityRepository, Toolkits, ImGui panel

D001: GlobalComponentIds_NoToolkitBlockDuplicates filter fixed (byte->int)
D002: BitMask512 StructLayout Pack=64 added

Tests: 765 passed, 2 skipped, 0 failed
```
