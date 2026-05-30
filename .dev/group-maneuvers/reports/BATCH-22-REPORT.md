# BATCH-22 Report

**Batch:** BATCH-22
**Developer:** GitHub Copilot
**Date:** 2025-07-15
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| P2-00   | [x]    | Pre-flight renames: `Flags`, `SourceMembersMask`, `_memberEpochChecksum`; `ChangeEpoch` added to `TargetMemory` |
| P2-01   | [x]    | `SquadPerceptionMergeSystem` created; 5/5 success criteria pass |
| P2-02   | [x]    | `SquadInputs` created with `SquadKnowsContact` and `SquadContactThreatLevel`; hash constants verified; 4/4 criteria pass |

---

## Testing Results

**Unit Tests Passed:** 9 / 9
**Integration Tests (full Squad suite):** 40 / 40

**Key Test Scenarios Verified:**
- [x] SC-P2-01-1: Three distinct contacts from three members merge to Count==3, each with correct single-bit SourceMembersMask
- [x] SC-P2-01-2: Two members reporting the same contact: max ThreatScore wins, both bits set in SourceMembersMask, most-recent LastSeenTick kept
- [x] SC-P2-01-3: Cadence gate -- skips within interval, first run always proceeds (LastMergeTick==0 guard), runs at boundary
- [x] SC-P2-01-4: Event-driven forced re-merge when any member TargetMemory.ChangeEpoch changes
- [x] SC-P2-01-5: Capacity eviction -- 17th contact rejected if lower than all 16; higher-score entry evicts the lowest
- [x] SC-P2-02-1: `SquadKnowsContact` returns 1f when squad pool contains the candidate entity
- [x] SC-P2-02-2: `SquadKnowsContact` returns 0f when candidate entity is absent from squad pool
- [x] SC-P2-02-3: `SquadKnowsContact` returns 0f when Self has no `UnitSubordinate` (non-squad member)
- [x] SC-P2-02-4: `SquadContactThreatLevel` returns the pool's stored ThreatScore (clamped to [0,1])

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Two issues arose:

1. `SquadPerceptionMergeSystem` used `fixed long` array access (`SubordinateEntities`) which requires an `unsafe` context. Resolved by marking the class `public static unsafe class SquadPerceptionMergeSystem`.

2. The first `Run` call with both members having empty `TargetMemory` was skipped incorrectly. The cadence gate computed `checksum = 0` which equalled the initial `_memberEpochChecksum = 0` (no epoch change), and `currentTick - 0 < mergeIntervalTicks` when the interval was large. The gate was updated to treat `LastMergeTick == 0` as "never populated" and always run the first time.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

`SquadContactPoolSlots` uses a `[InlineArray(16)]` attribute backed by a single `SquadContact` field. The only way to write individual slots is via `MemoryMarshal.CreateSpan(ref Unsafe.As<...>(...), 16)`. This is consistent with the rest of the codebase, but it is easy to forget -- a future contributor might write `pool.Contacts[i] = ...` and produce a silent defensive copy. A runtime assert on the struct size would not help there. A code-review checklist note for `InlineArray` fields would reduce risk.

**Q3: What design decisions did you make beyond the instructions?**

The "first-run" guard (`LastMergeTick == 0`) was added to the cadence gate. The instructions described the dwell condition as `currentTick - state.Contacts.LastMergeTick >= mergeIntervalTicks` without explicit mention of the initialization edge case. Treating tick 0 as sentinel for "never run" is consistent with how `TargetMemory.ChangeEpoch` starts at 0 and the checksum also starts at 0 -- it is the minimal fix that does not introduce a dedicated bool flag.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

When both members have empty `TargetMemory` at the first call, the epoch checksum is 0 and `_memberEpochChecksum` is also 0 at initialisation, so the epoch-change branch never fires. The first run must be forced by checking `LastMergeTick == 0`. The test `EventDriven_ForcedRemergeOnEpochChange` (SC-P2-01-4) caught this immediately.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

None beyond what the spec already addresses. The insertion sort over at most 16 contacts and the per-member epoch XOR are both O(N) or better and stay well within a 60-tps game tick.

---

## Outstanding Issues / Next Steps

None. All tasks complete, all 9 new tests pass, no regressions in the existing 31 Squad tests.
