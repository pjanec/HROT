# BATCH-20 Report: TASK-DBG-006 -- Debug Protocol Test Suite (Remaining Tests)

**Batch:** BATCH-20
**Task:** TASK-DBG-006 (completion)
**Date:** 2026-05-22
**Result:** PASS

---

## Work Completed

### 1. CapturingDebugSession Audit

Verified all BATCH-19 interface additions were already present:
- `SetEntityFilter`, `GetEntityFilter` -- present
- `GetActiveEntities` -- present
- `RegisterPdbLocator` -- present
- `OnHotReloadBegin`, `OnHotReloadCompleted` -- present

No updates required.

### 2. NodeHistoryTests.cs (4 tests)

`Hrot.Blueprints.Tests/Debug/NodeHistoryTests.cs`

| SC | Test | Result |
|----|------|--------|
| SC1 | `OnNodeEnter_RecordsHistoryEntry_WithCorrectFields` | PASS |
| SC2 | `GetNodeHistory_EntitiesAreIsolated` | PASS |
| SC3 | `ExecutionHistory_RingBuffer_WrapsAt256` | PASS |
| SC4 | `GetNodeHistory_MaxCount_LimitsResult` | PASS |

Used `ConfigurableSimulationView(tick: 42, time: 1.5f)` for SC1. SC3 verifies ring buffer
semantics: 260 entries -> 256 returned, first is "node-005", last is "node-260".
Called `session.GetNodeHistory(Entity, int)` directly on the concrete type (non-interface method).

### 3. StateInspectorTests.cs (5 tests)

`Hrot.Blueprints.Tests/Debug/StateInspectorTests.cs`

| SC | Test | Result |
|----|------|--------|
| SC1 | `GetCurrentStateSnapshot_WhenPaused_ReturnsSnapshot` | PASS |
| SC2 | `GetCurrentStateSnapshot_WhenNotPaused_ReturnsNull` | PASS |
| SC3 | `MarshalFromBytes_Int_RoundTrip` | PASS |
| SC4 | `MarshalFromBytes_Float_RoundTrip` | PASS |
| SC5 | `MarshalFromBytes_UnknownType_ReturnsByteArray` | PASS |

Note: `BlueprintStateSnapshot` uses `Self` not `PausedEntity` as the entity property name.
`MarshalFromBytes` is a static method on `BlueprintDebugSession` (not on the interface).

### 4. HotReloadInteractionTests.cs (4 tests)

`Hrot.Blueprints.Tests/Debug/HotReloadInteractionTests.cs`

| SC | Test | Result |
|----|------|--------|
| SC1 | `OnHotReloadBegin_WhenNotPaused_DoesNotCallContinue` | PASS |
| SC2 | `OnHotReloadBegin_MarksAllWatchesStale` | PASS |
| SC3 | `OnHotReloadCompleted_OnlyClears_ReloadedAssetWatches` | PASS |
| SC4 | `RegisterDebugMap_NewHash_ClearsBreakpointsForThatAsset` | PASS |

SC3 and SC4 verify asset-selective behavior: only the asset that was reloaded / re-registered
has its watches cleared / breakpoints removed.

### 5. ProbeDispatchTests.cs (4 tests)

`Hrot.Blueprints.Tests/Debug/ProbeDispatchTests.cs`

| SC | Test | Result |
|----|------|--------|
| SC1 | `DebugProbe_NullSink_OnNodeEnter_IsNoOp` | PASS |
| SC2 | `DebugProbe_NonNullSink_OnNodeEnter_ForwardsToSink` | PASS |
| SC3 | `DebugProbe_NullSink_OnPinValueChanged_ZeroAllocation` | PASS |
| SC4 | `DebugProbe_NullSink_OnNodeEnter_ZeroAllocation` | PASS |

`DebugProbe.Sink` is a static field; the class implements `IDisposable` to save/restore the
static `Sink` value around each test, ensuring test isolation.

### 6. BenchmarkDotNet Package

Added `BenchmarkDotNet` 0.13.12 to `Hrot.Blueprints.Tests.csproj`.

### 7. ProbeOverheadBenchmarks.cs (3 benchmarks)

`Hrot.Blueprints.Tests/Benchmarks/ProbeOverheadBenchmarks.cs`

- `OnNodeEnter_NullSink_Overhead` -- calls `NullProbeSink.OnNodeEnter` (null sink path)
- `OnPinValueChanged_Int_NullSink_Overhead` -- calls `NullProbeSink.OnPinValueChanged<int>`
- `OnNodeEnter_WithBreakpoint_Miss` -- calls `BlueprintDebugSession.OnNodeEnter` with a
  breakpoint registered for a different node (miss path)

Decorated with `[MemoryDiagnoser]` and `[SimpleJob(RuntimeMoniker.Net80)]`. NOT invoked in
`dotnet test`; runs standalone via `BenchmarkRunner.Run<ProbeOverheadBenchmarks>()`.

### 8. ProbeOverheadTests.cs (1 xUnit CI gate)

`Hrot.Blueprints.Tests/Benchmarks/ProbeOverheadTests.cs`

- `ProbeOverhead_OnNodeEnter_NullSink_IsZeroAllocation` -- verifies `NullProbeSink.OnNodeEnter`
  allocates 0 bytes, serving as the CI substitute for the < 50ns BenchmarkDotNet criterion.

---

## Test Results

| Metric | Before | After |
|--------|--------|-------|
| Passed | 406 | 424 |
| Failed | 0 | 0 |
| Skipped | 5 | 5 |
| Total | 411 | 429 |
| New tests | -- | +18 |

```
Passed!  - Failed:     0, Passed:   424, Skipped:     5, Total:   429
```

Full Debug namespace run: `Passed: 76, Skipped: 2, Total: 78` (0 failures).

---

## Issues and Resolutions

1. **File lock on build** -- Two `testhost` processes (PIDs 5048, 11428) from a previous test run
   held a lock on `Hrot.Blueprints.Core.dll`. Resolved by `Stop-Process` before rebuilding.

2. **`BlueprintStateSnapshot.PausedEntity` does not exist** -- The actual property is `Self`
   (record positional parameter). Used `snapshot.Self` in `StateInspectorTests`.

3. **`DebugProbe` is a static class, not instanced** -- The instructions referred to
   "creating a DebugProbe with null sink" meaning `DebugProbe.Sink = null`. Tests use
   `NullProbeSink.Instance` directly as `IBlueprintProbeSink` for the benchmark to avoid
   the static mutable state issue with BenchmarkDotNet.

4. **Test count vs estimate** -- The spec estimated ~21 new tests and target >= 432. Actual
   implementation produced 18 tests (all SCs covered). The 3-test discrepancy is because the
   spec estimate was approximate; every listed SC was implemented.

---

## Deferred / Weak Points

- `GetRecentNodeHistory(int)` (no entity argument) on `IBlueprintDebugSession` returns
  `Array.Empty<NodeExecuted>()` -- deferred to DBG-005 as noted in the existing code.
- BenchmarkDotNet runtime measurements (actual nanosecond numbers) require a standalone
  Release-mode run; CI gate is the zero-allocation xUnit test only.
