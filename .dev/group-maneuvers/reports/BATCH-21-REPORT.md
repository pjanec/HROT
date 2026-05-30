# BATCH-21 Report — Group Maneuvers Phase 1: Primitives Library

**Batch:** BATCH-21
**Date:** 2025-07-26
**Status:** COMPLETED — all 5 tasks implemented, 16/16 new tests passing, 0 regressions

---

## 1. Tasks Completed

| Task | Title | Status |
|------|-------|--------|
| P1-01 | ElementPartitionPrimitive — hysteresis partition | Done |
| P1-02 | TacticalFeatureHandles — acquire/refresh active feature | Done |
| P1-03a | GreedyMatrixAssigner — shared O(m*n) greedy assigner (extraction) | Done |
| P1-03b | ThreatMatrixAssignmentSystem — refactored to use GreedyMatrixAssigner | Done |
| P1-03c | RoleSlotAssignmentPrimitive — greedy role/slot assignment | Done |
| P1-04 | PhaseSequencer — squad HSM phase sequencer with veto/dwell-timeout | Done |
| P1-05 | SlotRotation — exposed-slot rotation with burn/reuse bitmask | Done |

---

## 2. Files Created

| File | Purpose |
|------|---------|
| `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/ElementPartitionPrimitive.cs` | Partitions squad members across N elements with hysteresis (decisive-gap anti-flip-flop) |
| `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/TacticalFeatureHandles.cs` | Acquire and refresh the active tactical feature (danger area) reference |
| `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/GreedyMatrixAssigner.cs` | Shared greedy O(m*n) assignment over a pre-built score matrix; extracted from ThreatMatrixAssignmentSystem for reuse |
| `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/RoleSlotAssignmentPrimitive.cs` | Greedy role/slot assignment into `state.Roles` via GreedyMatrixAssigner |
| `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/PhaseSequencer.cs` | Squad HSM phase sequencer with veto-detection override and dwell-timeout fallback |
| `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/SlotRotation.cs` | Exposed-slot rotation with per-slot burn/reuse bitmask tracking |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Primitives/ElementPartitionPrimitiveTests.cs` | 4 tests: SC-P1-01-1 through SC-P1-01-4 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Primitives/TacticalFeatureHandlesTests.cs` | 3 tests: SC-P1-02-1 through SC-P1-02-3 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Primitives/RoleSlotAssignmentPrimitiveTests.cs` | 3 tests: SC-P1-03-1 through SC-P1-03-3 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Primitives/PhaseSequencerTests.cs` | 3 tests: SC-P1-04-1 through SC-P1-04-3 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Primitives/SlotRotationTests.cs` | 3 tests: SC-P1-05-1 through SC-P1-05-3 |

---

## 3. Files Modified

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentSystem.cs` | Added `using Fdp.Toolkit.Squad.Primitives;`; `Run()` now builds a flat score matrix via `stackalloc float[maxMembers * maxTargets]`, fills it with `UtilityScorer.Evaluate` calls, delegates assignment to `GreedyMatrixAssigner.Assign`, then writes results back in a second pass for `FocusFireCount`. All existing public API preserved. |

---

## 4. Success Conditions

| SC | Description | Result |
|----|-------------|--------|
| SC-P1-01-1 | Member assigned to highest-scoring element | PASS |
| SC-P1-01-2 | Hysteresis holds on marginal gap (below decisiveGap) | PASS |
| SC-P1-01-3 | Member moves on decisive gap; repartitionsCount == 1 | PASS |
| SC-P1-01-4 | Zero managed-heap allocs on 10^4 Partition calls | PASS |
| SC-P1-02-1 | Acquire writes ActiveFeatureId; idempotent on repeat | PASS |
| SC-P1-02-2 | TryRefresh returns true only for matching featureId | PASS |
| SC-P1-02-3 | Evicted descriptor -> false; ActiveFeatureId unchanged | PASS |
| SC-P1-03-1 | 4-member 4-candidate greedy matches expected assignment | PASS |
| SC-P1-03-2 | Re-run after phase change overwrites assignment | PASS |
| SC-P1-03-3 | Empty candidates is no-op | PASS |
| SC-P1-04-1 | Matching event -> phase transition + tick bump | PASS |
| SC-P1-04-2 | Dwell timeout -> recovery phase | PASS |
| SC-P1-04-3 | VetoDetected overrides other events | PASS |
| SC-P1-05-1 | Sequential acquisition 0..7; 9th returns -1 | PASS |
| SC-P1-05-2 | Burn then release keeps slot unavailable | PASS |
| SC-P1-05-3 | All-burned -> -1 | PASS |

---

## 5. Test Results

### 5.1 Build

```
dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj
```

Result: **Build succeeded. 0 Warning(s). 0 Error(s).**

### 5.2 Squad filter (Phase-0 regression + all 16 new tests)

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Squad"
```

Result: **Passed: 31, Failed: 0, Total: 31**

Includes all 14 BATCH-20 Phase-0 tests (no regressions) and all 16 new BATCH-21 tests.

### 5.3 ThreatMatrix regression filter

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~ThreatMatrix|FullyQualifiedName~StarterPack|FullyQualifiedName~StandardInput"
```

Result: **Passed: 51, Failed: 0, Total: 51**

Zero regressions from the `ThreatMatrixAssignmentSystem` -> `GreedyMatrixAssigner` refactor.

### 5.4 Full suite

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
```

Result: **Passed: 1691, Failed: 67, Total: 1758**

Baseline (BATCH-20): Passed: 1675, Failed: 67. The increase of +16 passes matches the 16 new tests added. Failure count is **unchanged**.

Note: `DangerAreaProviderTests.FakeDangerAreaProvider_Refresh_ZeroAllocAfterWarmup` appears in the 67 failures in the full suite run. This is a **pre-existing flaky failure documented in the BATCH-20 report** (GC pollution from RecordingExportServiceTests running concurrently). It passes reliably in the Squad-filtered run (31/31) and in isolation.

---

## 6. Issues and Resolutions

| Issue | Resolution |
|-------|------------|
| `CS0169` compile errors on private padding fields (`RoleSlotCandidate._pad`, `PhaseTransitionEntry._pad`) — project settings promote this warning to an error | Added `#pragma warning disable CS0169` / `#pragma warning restore CS0169` around each padding field declaration |
| `Partition_ZeroAllocs` initially used `GC.GetTotalMemory(forceFullCollection: true)` which was too slow and left dirty GC state that caused `DangerAreaProvider_Refresh_ZeroAllocAfterWarmup` to fail in combined runs | Rewrote the test to use `GC.GetAllocatedBytesForCurrentThread()` (thread-local, monotonically increasing counter immune to GC background activity). Added `GC.Collect(2)` + `GC.WaitForPendingFinalizers()` + `GC.Collect(2)` AFTER the measurement loop so subsequent GC.GetTotalMemory-based tests see a clean baseline. Used `Assert.Equal(0, diff)` for exact zero-alloc verification. |
| `InlineArray` write via direct indexer silently no-ops at runtime | All writes to `MemberElementIndexArray` and `RoleAssignmentArray` fields use `MemoryMarshal.CreateSpan(ref Unsafe.As<...>(...), count)` per the codebase convention |

---

## 7. Key Design Notes

### GreedyMatrixAssigner layout
- Row-major: `scoreMatrix[m * candidateCount + c]` for member `m`, candidate `c`
- Assigns `-1` when best score is not strictly positive (`> 0f`)
- `focusCount[]` (stackalloc) enforces per-candidate `maxFocusFire` cap

### PhaseSequencer.Advance() scan order
1. Scan event queue for `VetoDetected` FIRST (routes to `recoveryPhaseId`, overrides all)
2. Scan events vs. transition table (first match wins)
3. Check dwell timeout if no event matched

### SlotRotation.BurnSlot
Burns a slot by setting `BurnedMask` AND clearing `UsedMask`. A burned slot is treated as "not in use" — `AcquireSlot` skips it via the `BurnedMask` check before checking `UsedMask`.

---

## 8. Suggested Commit Message

```
feat(squad/primitives): BATCH-21 Phase-1 primitives library

Adds five Brain-resident primitives (ElementPartitionPrimitive,
TacticalFeatureHandles, RoleSlotAssignmentPrimitive, PhaseSequencer,
SlotRotation) plus shared GreedyMatrixAssigner extracted from
ThreatMatrixAssignmentSystem.

- 6 new static classes under Fdp.Toolkit.Squad.Primitives
- ThreatMatrixAssignmentSystem refactored to use GreedyMatrixAssigner
- 16 new tests; 16/16 pass; 0 regressions (51 ThreatMatrix tests green)

Closes TASK-SQD-P1-01 through TASK-SQD-P1-05
```
