# BATCH-10 REPORT

## Tasks Completed
- **EQS-023** (partial) -- Offline EditorHarness round-trip test (T-RT1). Distributed test deferred to BATCH-11 as planned.
- **EQS-024** -- Top-K reduction and positional sentinel preservation (T-RT2).
- **EQS-029** -- TargetMemory threat threshold bypassing (T-RT3a, T-RT3b).

---

## Files Created

| File | Description |
|------|-------------|
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsRoundTripTests.cs` | New test class with 4 tests and 7 private inner types |

## Files Modified

| File | Change |
|------|--------|
| `.dev/eqs-2/TASK-TRACKER.md` | Marked EQS-023 (partial), EQS-024, EQS-029 as complete |

---

## Test Results

### New tests (EqsRoundTripTests)

| Test | Method | Blueprint ID | Result |
|------|--------|-------------|--------|
| T-RT1 | `Eqs_OfflineEditor_PopulatesCognitiveBufferWithCandidates` | 92u | PASS |
| T-RT2 | `Eqs_TopKReduction_PreservesPositionalSentinels` | 93u | PASS |
| T-RT3a | `Eqs_ThreatThreshold_AboveThreshold_RejectsAllExposedCandidates` | 94u | PASS |
| T-RT3b | `Eqs_ThreatThreshold_BelowThreshold_BypassesFilter` | 95u | PASS |

**New tests: 4/4 passed.**

### Full EQS integration suite (regression check)

```
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --no-build --filter "FullyQualifiedName~Eqs"
Total tests: 25
     Passed: 25
```

**No regressions. All 25 EQS integration tests pass.**

---

## Inner Types Created

All inner types are private to `EqsRoundTripTests`.

| Type | Purpose |
|------|---------|
| `SimpleEqsTemplateRegistry` | In-memory `IEqsTemplateRegistry` (same pattern as other test files) |
| `MockCoverProvider` | `ICoverProvider` returning 2 candidates relative to query center |
| `MockNavmeshProvider` | `INavmeshProvider` stub (Euclidean distance, all reachable) |
| `DeterministicPositionalGenerator` | `IEqsGenerator` yielding 5 candidates at X=10..50 |
| `SentinelRejectionFilterTest` | `FilterCheap` test rejecting indices 1 and 3 |
| `DummyScoreTest` | `ScoreCheap` test asserting 3 compacted entries with no -1L sentinels |
| `ExposedLosServiceMock` | `ILosService` always returning true (exposed) |

---

## Deviations from Plan

| Deviation | Justification |
|-----------|--------------|
| T-RT3a pump condition omits `Count > 0` | T-RT3a asserts `Count == 0`; including `Count > 0` in the pump predicate would cause a timeout. The pump waits on `IsReady` alone (which is set by `LastUpdateTick > 0` regardless of result count). This is consistent with the task intent. |
| `ManualCoverProvider` used from production code | Instruction says to use existing class -- no new class created. |
| Blueprint IDs: 92u, 93u, 94u, 95u | Matches the key decisions in the batch instructions. Existing tests use 97, 98, 99; no collision. |

---

## Build Confirmation

```
dotnet build Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-restore
Build succeeded. 0 Error(s). 0 Warning(s).
```
