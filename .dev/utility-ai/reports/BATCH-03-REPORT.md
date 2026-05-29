# BATCH-03 Report: Utility AI — UtilityResultBuffer + Trace Buffer + UtilityScorer Core

**Date:** 2026-05-30  
**Tasks:** Debt D-01 (P2 fix), TASK-UAI-P1-04, TASK-UAI-P1-05  
**Status:** COMPLETE — all new tests pass, 70 utility tests total (50 prior + 20 new)

---

## Q1: Issues Encountered and Resolutions

### Issue 1: `Entity` vs `EntityId` in UtilityScorer

The session summary referred to the ECS entity handle as `EntityId`, but the actual type in `Fdp.Core` is `Entity` (a `readonly struct` with `Index` and `Generation` fields). Initial compile failed with CS0246 on seven sites. The fix was a targeted multi-replace across `UtilityScorer.cs`: all `EntityId` occurrences in method signatures became `Entity`, and the XML doc comment was corrected (`Entity.Null` instead of `EntityId.Invalid`). Build then succeeded cleanly.

### Issue 2: `WinningPostureId` truncation — ushort to byte

`UtilityOption.OptionId` is `ushort`; `UtilityResultEntry.WinningPostureId` is `byte`. The compiler correctly rejected the implicit narrowing (CS0266). The fix was a `(byte)` cast at the assignment site in `Evaluate`. Phase 1 only uses option IDs in [0, 255]; if that invariant ever changes, a debug assert should be added.

### Issue 3: Multi-replacement partial failure

The `multi_replace_string_in_file` tool succeeded on some replacements in one call but not others when the target strings varied slightly in whitespace from the actual file. The four-replacement batch silently applied two of the four changes. The remaining replacements were completed by individual targeted calls. No data was lost; the partial apply was detectable by re-reading the file immediately after.

### Issue 4: Test namespace convention deviation

Existing utility tests live in `Fdp.Toolkit.Tests`. The two new test files use `Fdp.Toolkit.Utility.Tests`, which is deeper and more descriptive. xUnit discovers tests by type name, not namespace, so both conventions coexist without conflict. No change was made since the tests pass and consistency across all test files in the solution is a separate concern.

---

## Q2: `sizeof(UtilityResultEntry)` — Field Layout

```
Field             Type    Size   Cumulative
----------------  ------  -----  ----------
CandidateHandle   long    8      8
Score             float   4      12
WinningPostureId  byte    1      13
_pad0             byte    1      14
_pad1             byte    1      15
_pad2             byte    1      16
```

**`sizeof(UtilityResultEntry) == 16 bytes`** — verified by `UtilityResultEntry_SizeIs16Bytes` test.

`UtilityResultBuffer` total = `int Count` (4) + `float RunnerUpMargin` (4) + `UtilityResultArray` (16 * 16 = 256) = 264 bytes minimum. Actual size may be slightly larger depending on struct alignment; test asserts >= 264.

`UtilityTraceRecord` is `[StructLayout(LayoutKind.Sequential, Size=32)]` — exactly 32 bytes verified by `UtilityTraceRecord_SizeIs32Bytes` test.

`UtilityTraceWorkingMemory1024` is `[StructLayout(LayoutKind.Sequential, Size=1024)]` — verified by `UtilityTraceWorkingMemory1024_SizeIs1024Bytes` test.

---

## Q3: Design Decisions

### 3.1 UtilityResultBuffer

- `UtilityResultArray` declared as `[InlineArray(UtilityConstants.TopN)]` with `private UtilityResultEntry _element`. The `TopN = 16` constant keeps the inline array and the scorer's `stackalloc` bounds in sync without magic numbers.
- `GetSpanRW()` and `GetSpanRO()` mirror `EqsCognitiveBuffer` exactly: `MemoryMarshal.CreateSpan(ref Unsafe.As<UtilityResultArray, UtilityResultEntry>(ref Results), TopN)`.
- A doc comment on the `Results` field explicitly names the defensive-copy trap. The test `GetSpanRW_WriteIsPersisted_AndCopyTrapIsDocumented` proves the trap is real (not just documented) by asserting both that the span write persists and that a direct indexer write on a copy is silently lost.

### 3.2 UtilityTraceWorkingMemory1024

- Header (8 bytes): `ushort WritePos`, `ushort RecordCount`, `uint LastTick`. `WritePos` is pre-wrapped into `[0, PayloadBytes)` on every write, so `ReadRecord` never needs a double-wrap.
- `CapacityRecords = 31` (not 32): `1024 - 8 (header) = 1016` payload candidate, but to keep `WritePos` arithmetic on a clean multiple of 32, `PayloadBytes = 31 * 32 = 992` with the remaining 24 bytes (`BufferBytes = 1016`) unused padding. `RecordCount` is saturated at `CapacityRecords` rather than allowed to overflow.
- `NextRecord(ushort tick)`: atomically advances `WritePos` with `(WritePos + 32) % 992`, saturates `RecordCount`, zeroes the new record slot via `Unsafe.InitBlockUnaligned`, then stamps `Tick`.
- `ReadRecord(int index)`: when the ring is full, reading starts from `WritePos` (oldest); when not full, starts from 0.

### 3.3 UtilityScorer

- `UtilityInputRegistrar.Clear()` is `public` (not `internal`) because test classes in a separate assembly need it for test isolation. Production code is expected never to call it outside of tests; a comment in the doc string documents this intent.
- `EvaluateOption` accumulates a `runningAgg` estimate for trace purposes. This is an approximation (computed incrementally before the final `Aggregator.Aggregate` call). The actual final score is always what `Aggregator.Aggregate` returns; `runningAgg` in the trace is labeled "running estimate" in doc comments.
- `SelectPosture` applies hysteresis post-scoring (not as a pre-bias). The raw scores are written to `output` by the first `Evaluate` call, then a stack-local copy is modified before a second sort. Output is rewritten from a stack snapshot to avoid reading half-overwritten data.
- `InsertionSort` is `[MethodImpl(AggressiveInlining)]` and works on `float*`/`int*` stackalloc arrays with no heap allocation. Ties are broken by lower original index (≡ lower OptionId for options authored in definition order).

### 3.4 UtilityApplicationComponentIds

A dedicated static class was created (mirroring `BehaviorApplicationComponentIds`) with constants `UtilityDebugFlags = 149` and `UtilityTraceWorkingMemory = 150`. These IDs were confirmed unallocated in `GlobalComponentIds.cs` (IDs 145–158 were free in the ModuleHost range).

---

## Q4: Edge Cases Discovered

1. **16-option stackalloc sizing:** `EvaluateOption` and `Evaluate` both use `stackalloc` sized to `optionCount` (not `TopN`). Since `optionCount <= TopN = 16`, this is always within bounds. An overrun cannot happen if the def is well-formed.

2. **Zero-consideration option:** `EvaluateOption` returns `0f` immediately when `consCount == 0`. This avoids a `Aggregator.Aggregate` call on empty spans, which would otherwise return 0 anyway (but with a potential divide-by-zero in WeightedSum if both spans are empty).

3. **`SelectPosture` with 1 option:** `output.Count = 1`, `RunnerUpMargin = 0`. The hysteresis loop finds the active posture at index 0, applies the bonus, sorts 1 element (nop), rewrites 1 entry. Return value is always `outSpan[0].WinningPostureId`. Correct.

4. **`SelectPosture` when active posture not in output:** If `activePostureId` does not match any `WinningPostureId` in the output (e.g., an option was pruned), the hysteresis loop completes without adding any bonus. This is the safe fallback: the highest-scoring option wins outright.

5. **Trace ring overflow:** After 31 records, `WritePos` wraps around and overwrites the oldest slot. `RecordCount` saturates at 31 and never exceeds it. Test `TraceBuffer_RecordCount_SaturatesAtCapacity` writes 41 records and asserts `RecordCount == 31`.

---

## Q5: Performance Notes

- `UtilityScorer.Evaluate` is fully allocation-free: all intermediate arrays are `stackalloc float[optionCount]` (≤ 16 elements).
- `UtilityInputRegistrar` uses a `Dictionary<ushort, nint>` — acceptable for Phase 1 (registered at startup, not per-frame). Phase 2 plan: source-gen a flat array indexed directly by `InputId` for O(1) read with no dictionary overhead.
- `UtilityTraceWorkingMemory1024.NextRecord` uses `Unsafe.InitBlockUnaligned` to zero the new slot (32 bytes) rather than a field-by-field assignment. This is branchless and cache-friendly.

---

## Q6: Debt Update

- **D-01 (Fnv1a32 pinned hash assertion):** RESOLVED. `UtilityTestWorldTests.Fnv1a32_CoverQuery_ProducesStableNonZeroValue` now asserts `Assert.Equal(0x72BE4C04u, hash1); // Pinned: algorithm regression guard`. The correct value was determined in a prior session by running a temporary dotnet program. The previously-commented wrong value (`0x9317A97Bu`) was removed.

---

## Q7: Suggested Git Commit Message

```
feat(utility-ai): Result buffer, trace buffer, scorer core (BATCH-03)

Resolves Debt D-01, completes TASK-UAI-P1-04 and TASK-UAI-P1-05.

D-01: Activate FNV-1a32 pinned-hash assertion (0x72BE4C04) in
  UtilityTestWorldTests.Fnv1a32_CoverQuery_ProducesStableNonZeroValue

P1-04: UtilityApplicationComponentIds (IDs 149, 150)
  UtilityResultBuffer -- [InlineArray(16)] top-N result buffer with
    GetSpanRW/GetSpanRO accessors; UtilityResultEntry (16B); UtilityDebugFlags
  UtilityTraceWorkingMemory1024 -- 1024B ring buffer with 8B header + 31x32B
    records; WriteConsiderationRecord; WriteWinnerRecord; ReadRecord

P1-05: UtilityInputCtx struct; UtilityInputRegistrar (function-pointer map);
  UtilityScorer.Evaluate (stackalloc sort, trace, result write);
  UtilityScorer.SelectPosture (post-scoring hysteresis bonus);
  UtilityScorer.EvaluateOption (per-option aggregator drive);
  InsertionSort (descending, tie-break by lower index, inlined)

Tests: 20 new tests (13 buffer/trace, 7 scorer).
Utility total: 70 tests, all pass.
sizeof(UtilityResultEntry)==16, sizeof(UtilityTraceRecord)==32,
sizeof(UtilityTraceWorkingMemory1024)==1024.
```

---

## Test Results Summary

| Suite | New Tests | Result |
|-------|-----------|--------|
| UtilityTestWorldTests (D-01 fix) | 0 new (1 existing activated) | PASS |
| UtilityResultBufferTests (P1-04) | 13 | PASS |
| UtilityScorerTests (P1-05) | 7 | PASS |
| **Total new** | **20** | **ALL PASS** |

**Utility filter total:** 70 tests — 70 passed, 0 failed.

Prior utility tests (50) are all still passing: AggregatorTests (8), CurveEvaluationTests (26), UtilityCoreTests (7), Phase0IntegrationTests (1 — counted as part of UtilityTestWorldTests), UtilityTestWorldTests (8).
