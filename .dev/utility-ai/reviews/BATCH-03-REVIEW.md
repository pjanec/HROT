# BATCH-03 Review

**Batch:** BATCH-03
**Reviewer:** Development Lead
**Date:** 2026-05-29
**Status:** APPROVED

---

## Summary

All three deliverables complete: Debt D-01 activated, TASK-UAI-P1-04 (UtilityResultBuffer + trace buffer) and TASK-UAI-P1-05 (UtilityScorer core) implemented. 70 utility tests pass (50 prior + 20 new). The 83 failures in the full suite are pre-existing across unrelated test classes (ReplayBrowser, Replication, Orchestration) and were present before BATCH-03.

---

## Issues Found

### P3 — Test namespace inconsistency

New test files (`UtilityResultBufferTests.cs`, `UtilityScorerTests.cs`) use namespace `Fdp.Toolkit.Utility.Tests`, while all existing utility test files use `Fdp.Toolkit.Tests`. xUnit discovers both correctly but the inconsistency should be normalized when a convenient opportunity arises. Not a blocker.

### P3 — SC-P1-04-1 trap test demonstrates via copy, not in-place silent loss

The test uses `var copy = buffer; copy.Results[0] = entry; Assert.Equal(0.9f, buffer.GetSpanRO()[0].Score)` — this correctly demonstrates the failure mode that actually occurs in ECS (component returned by value, write to copy is lost). The in-place silent loss scenario (e.g., `ref readonly var bufRO = ref …; bufRO.Results[0] = …`) would be a compile error in modern C#. The test is an adequate regression guard. No change required.

### P3 — `WinningPostureId` byte/ushort truncation

`UtilityResultEntry.WinningPostureId` is `byte`; `UtilityOption.OptionId` is `ushort`. The scorer uses `(byte)` cast. Phase 1 only uses option IDs in [0, 255]; if that constraint ever changes, silent truncation would occur. Should be defended with a debug assert at `UtilityDecisionDef` registration time in a future batch.

---

## Test Quality Assessment

**Debt D-01:** `Fnv1a32_CoverQuery_ProducesStableNonZeroValue` now has `Assert.Equal(0x72BE4C04u, hash1)`. Any algorithm change breaks this test. ✅

**P1-04:**
- `GetSpanRW_WriteIsPersisted_AndCopyTrapIsDocumented`: span write persists; explicit-copy write does not affect original. Strong documentation value; the copy-semantics scenario is the real ECS failure mode. ✅
- `TraceBuffer_DisabledPath_RecordCountStaysZero`: zero-record baseline verified. ✅
- `TraceBuffer_WriteConsiderationAndWinner_RecordCountIsCorrect`: exact count 7 asserted for 2 options × 3 considerations + 1 winner. ✅
- `TraceBuffer_WinnerRecord_ContainsCorrectValues`: winner OpCode, OptionIndex, Tick, RawValue and RunnerUpMargin all pinned. ✅
- `TraceBuffer_RecordCount_SaturatesAtCapacity`: 41 writes → RecordCount == 31 (saturation). ✅

**P1-05:**
- SC-P1-05-1 (step curve zero): Option with Step curve below threshold scores exactly 0.0f, confirmed by finding the zero entry and checking its `WinningPostureId == 1`. ✅
- SC-P1-05-2 (sort + margin): 0.9 / 0.6 / 0.3 scores produce RunnerUpMargin = 0.3f (pinned). ✅
- SC-P1-05-3a (hysteresis hold): A=0.70+0.08=0.78 > B=0.75; A wins. ✅
- SC-P1-05-3b (hysteresis switch): A=0.70+0.08=0.78 < B=0.80; B wins. ✅
- SC-P1-05-4 (16-option descending): Count==16 and strict descending order verified element-by-element. ✅

`SelectPosture` correctly applies hysteresis post-scoring (Step 1 scores all options normally, Step 5 takes a full stack snapshot before rewriting the output buffer to avoid reading half-overwritten data). No correctness issues. ✅

---

## Verdict

**Status: APPROVED**

All success criteria met. No P1 or P2 issues. Three P3 items noted above for debt tracker.

---

## Commit Message

```
feat(utility-ai): UtilityResultBuffer + trace buffer + UtilityScorer (BATCH-03)

Resolves D-01, TASK-UAI-P1-04, TASK-UAI-P1-05.

- D-01: Fnv1a32("CoverQuery") pinned hash assertion activated (0x72BE4C04)
- P1-04: UtilityApplicationComponentIds (IDs 149-150)
         UtilityResultEntry (16B), UtilityResultBuffer with GetSpanRW/RO
         UtilityDebugFlags component (trace gate)
         UtilityTraceWorkingMemory1024 (1024B ring; 31x32B records)
- P1-05: UtilityInputCtx struct, UtilityInputRegistrar (Phase-1 dict stub)
         UtilityScorer.Evaluate (stackalloc, insertion sort, trace emit)
         UtilityScorer.SelectPosture (post-scoring hysteresis, snapshot rewrite)

Tests: 20 new tests; 70 utility tests pass total.
```

---

**Next Batch:** BATCH-04 (Debt D-03 P3, TASK-UAI-P1-06 Standard input readers, TASK-UAI-P1-07 ThreatMatrixAssignmentSystem)
