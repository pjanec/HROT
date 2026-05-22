# BATCH-20: TASK-DBG-006 -- Debug Protocol Test Suite (Remaining Tests)

**Batch Number:** BATCH-20
**Tasks:** TASK-DBG-006 (completion)
**Phase:** 5 -- Debug Protocol
**Estimated Effort:** 2-3 days
**Priority:** HIGH
**Dependencies:** BATCH-19 (Watch, MultiEntity, HotReload all implemented in BlueprintDebugSession)

---

## 0. Onboarding

### Current State

TASK-DBG-006 requires a comprehensive debug protocol test suite. The following tests were already created in earlier batches and cover these topics:
- `BreakpointTests.cs` (7 tests, BATCH-18)
- `StepTests.cs` (4 tests, BATCH-18)
- `WatchTests.cs` (6 tests, BATCH-19)
- `MultiEntityTests.cs` (4 tests, BATCH-19) -- includes hot reload tests SC3+SC4
- `DebugMapTests.cs` (16 tests, BATCH-17)

**Missing from TASK-DBG-006 scope:**
1. `NodeHistoryTests.cs` -- ring buffer wrap, entity-specific isolation
2. `StateInspectorTests.cs` -- `GetCurrentStateSnapshot()` and `GetNodeHistory()` state inspection
3. `HotReloadInteractionTests.cs` -- dedicated hot reload interaction coverage (some already in MultiEntityTests, add edge cases)
4. `ProbeDispatchTests.cs` -- null-sink no-op path, non-null forwarding, allocation-free dispatch
5. `ProbeOverheadBenchmarks.cs` -- BenchmarkDotNet benchmark (probe call overhead < 50ns)

### Required Reading

1. `.dev/blueprints-1/reviews/BATCH-19-REVIEW.md`
2. `.dev/blueprints-1/TASK-DETAIL.md` §DBG-006 (scope and SCs)
3. `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` §12 (test strategy)
4. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintDebugSession.cs` -- current implementation
5. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/ExecutionHistory.cs` -- ring buffer impl
6. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/DebugProbe.cs` -- probe dispatch
7. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/BreakpointTests.cs` -- reference for test file structure

### Report Submission

`.dev/blueprints-1/reports/BATCH-20-REPORT.md`

---

## 1. NodeHistoryTests.cs

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/NodeHistoryTests.cs`.

**SC1: `OnNodeEnter_RecordsHistoryEntry_WithCorrectFields`**
- Create session with `ConfigurableSimulationView(tick: 42, time: 1.5f)`.
- Call `OnNodeEnter(E1, "some-node-id")`.
- Call `session.GetNodeHistory(E1, maxCount: 10)`.
- Assert: count == 1, `NodeId == "some-node-id"`, `Tick == 42u`, `SimTime == 1.5f`.

**SC2: `GetNodeHistory_EntitiesAreIsolated`**
- Call `OnNodeEnter(E1, "node-a")` twice, `OnNodeEnter(E2, "node-b")` once.
- `GetNodeHistory(E1)` returns 2 entries; `GetNodeHistory(E2)` returns 1 entry.
- E1 history must not contain "node-b"; E2 history must not contain "node-a".

**SC3: `ExecutionHistory_RingBuffer_WrapsAt256`**
- Call `OnNodeEnter(E1, ...)` 260 times with distinct node IDs.
- `GetNodeHistory(E1, 500)` returns exactly 256 entries (ring capacity).
- First entry returned is node ID #5 (oldest after wrap), last is node ID #260.

**SC4: `GetNodeHistory_MaxCount_LimitsResult`**
- Record 100 entries for E1.
- `GetNodeHistory(E1, maxCount: 10)` returns exactly 10 entries (the 10 most recent).

Note: `GetNodeHistory(Entity, int)` is a non-interface method on `BlueprintDebugSession`. Cast to `BlueprintDebugSession` or expose via an internal accessor for the test.

---

## 2. StateInspectorTests.cs

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/StateInspectorTests.cs`.

**SC1: `GetCurrentStateSnapshot_WhenPaused_ReturnsSnapshot`**
- Set breakpoint, hit it (OnNodeEnter).
- `session.GetCurrentStateSnapshot()` must return non-null.
- `snapshot.PausedEntity` must equal E1.

**SC2: `GetCurrentStateSnapshot_WhenNotPaused_ReturnsNull`**
- Session has no breakpoints. Call `GetCurrentStateSnapshot()`.
- Assert: returns null.

**SC3: `MarshalFromBytes_Int_RoundTrip`**
- `bytes = BitConverter.GetBytes(42)`.
- `result = BlueprintDebugSession.MarshalFromBytes(bytes, typeof(int))`.
- Assert: `(int)result == 42`.

**SC4: `MarshalFromBytes_Float_RoundTrip`**
- `bytes = BitConverter.GetBytes(3.14f)`.
- `result = BlueprintDebugSession.MarshalFromBytes(bytes, typeof(float))`.
- Assert: `Math.Abs((float)result - 3.14f) < 0.001f`.

**SC5: `MarshalFromBytes_UnknownType_ReturnsByteArray`**
- Use a type not in the switch (e.g., `typeof(DateTime)`).
- Assert: result is `byte[]`.

---

## 3. HotReloadInteractionTests.cs

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/HotReloadInteractionTests.cs`.

Note: MultiEntityTests.cs already covers `OnHotReloadBegin_WhenPaused_CallsContinue` (SC3) and `OnHotReloadCompleted_ClearsStaleWatches` (SC4). This file adds edge cases.

**SC1: `OnHotReloadBegin_WhenNotPaused_DoesNotCallContinue`**
- Session is NOT paused. Call `OnHotReloadBegin()`.
- Assert: `tc.ResumeCount == 0` (no spurious resume calls).
- Assert: `session.IsPaused == false`.

**SC2: `OnHotReloadBegin_MarksAllWatchesStale`**
- Add 2 watches for different assets. Call `OnHotReloadBegin()`.
- Assert: both watches have `IsStale == true`.

**SC3: `OnHotReloadCompleted_OnlyClears_ReloadedAssetWatches`**
- Add watch for AssetIdA (stale) and watch for AssetIdB (stale).
- Call `OnHotReloadCompleted(new[] { AssetIdA })`.
- Assert: AssetIdA watch `IsStale == false`; AssetIdB watch `IsStale == true` (not reloaded).

**SC4: `RegisterDebugMap_NewHash_ClearsBreakpointsForThatAsset`**
- Register map v1. Set 2 breakpoints for AssetIdA. Set 1 breakpoint for AssetIdB (different asset).
- Register map v2 (same AssetId as v1, different StructureHash).
- Assert: `session.GetBreakpoints()` count == 1 (only AssetIdB's BP remains).

---

## 4. ProbeDispatchTests.cs

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/ProbeDispatchTests.cs`.

Read `DebugProbe.cs` to understand its dispatch pattern before writing these tests.

**SC1: `DebugProbe_NullSink_OnNodeEnter_IsNoOp`**
- Create `DebugProbe` with null sink. Call `OnNodeEnter(E1, "some-id")`. No exception. No state change.

**SC2: `DebugProbe_NonNullSink_OnNodeEnter_ForwardsToSink`**
- Create a `CapturingDebugSession`. Create `DebugProbe` with that session as sink.
- Call `OnNodeEnter(E1, "some-id")`.
- Assert: `CapturingDebugSession.NodeEnterCalls` contains `(E1, "some-id")`.

**SC3: `DebugProbe_NullSink_OnPinValueChanged_ZeroAllocation`**
- `[NoInlining]` helper. GC measure of `OnPinValueChanged<int>(E1, "pin-id", 42)` with null sink.
- Assert: 0 bytes allocated.

**SC4: `DebugProbe_NullSink_OnNodeEnter_ZeroAllocation`**
- `[NoInlining]` helper. GC measure of `OnNodeEnter(E1, "node-id")` with null sink.
- Assert: 0 bytes allocated.

Note: `CapturingDebugSession` already exists in the test project. Check its interface members before writing SC2.

---

## 5. ProbeOverheadBenchmarks.cs

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Benchmarks/ProbeOverheadBenchmarks.cs`.

**Important:** First check if BenchmarkDotNet is already a dependency in `Hrot.Blueprints.Tests.csproj`. If not, add it:
```xml
<PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
```

**Benchmark: `OnNodeEnter_NullSink_Overhead`**
```csharp
[Benchmark]
public void OnNodeEnter_NullSink_Overhead()
    => _probe.OnNodeEnter(_entity, _nodeId);
```
Setup: `_probe` with null sink, `_nodeId = Guid.NewGuid().ToString("D")` (pre-allocated in GlobalSetup).

**Benchmark: `OnPinValueChanged_Int_NullSink_Overhead`**
```csharp
[Benchmark]
public void OnPinValueChanged_Int_NullSink_Overhead()
    => _probe.OnPinValueChanged(_entity, _pinId, 42);
```

**Benchmark: `OnNodeEnter_WithBreakpoint_Miss`**
- Session with 1 breakpoint registered (for a different node). Probe connects to session.
- Measures: probe call that does NOT hit the breakpoint.

The benchmarks must:
- Use `[MemoryDiagnoser]` attribute.
- Be runnable with `[SimpleJob(RuntimeMoniker.Net80)]`.
- NOT run automatically in `dotnet test` (they use `BenchmarkRunner.Run` separately).
- Have a comment `// Target: < 50ns per call (SC7-13.5 CI gate)`.

**Note about running benchmarks in test context:** Do NOT call `BenchmarkRunner.Run<>` inside a `[Fact]` -- that requires Release build and won't work in debug test runs. Instead, create a separate `ProbeOverheadTests.cs` (next to benchmarks) with a simple `[Fact]` that measures allocation using `GC.GetAllocatedBytesForCurrentThread()` (already done in ProbeDispatchTests.cs SC3+SC4). The benchmark file is for standalone benchmark runs.

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Benchmarks/ProbeOverheadTests.cs` with one `[Fact]`:

**`ProbeOverhead_OnNodeEnter_NullSink_IsZeroAllocation`** (xUnit test):
- Mirrors SC3+SC4 from ProbeDispatchTests but specifically verifying the overhead is zero-allocation.
- (This test is the CI gate substitute for the BenchmarkDotNet < 50ns criterion.)

---

## 6. CapturingDebugSession Updates (if needed)

Read `CapturingDebugSession.cs` carefully. If any new `IBlueprintDebugSession` members added in BATCH-19 (e.g., `SetEntityFilter`, `GetEntityFilter`, `GetActiveEntities`, `RegisterPdbLocator`, `OnHotReloadBegin`, `OnHotReloadCompleted`) are missing as stub implementations, add them now. The file must compile cleanly.

---

## 7. Verification

```powershell
# Debug tests only
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~Debug" -v minimal

# Full suite
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

Expected: 0 failures. Total count >= 432 (411 + ~21 new tests).

---

## 8. Mandatory Task Progression

1. Read `DebugProbe.cs` and `CapturingDebugSession.cs`.
2. Update `CapturingDebugSession` if any BATCH-19 interface additions are missing.
3. Create `NodeHistoryTests.cs` (4 tests).
4. Create `StateInspectorTests.cs` (5 tests).
5. Create `HotReloadInteractionTests.cs` (4 tests).
6. Create `ProbeDispatchTests.cs` (4 tests).
7. Add BenchmarkDotNet package reference if missing.
8. Create `Benchmarks/ProbeOverheadBenchmarks.cs` (3 benchmarks).
9. Create `Benchmarks/ProbeOverheadTests.cs` (1 xUnit test for CI gate).
10. Build and fix all compilation errors.
11. Run `--filter "FullyQualifiedName~Debug"` tests, fix failures.
12. Run full suite, fix failures.
13. Commit.
14. Write report.

**DO NOT STOP.** Complete all tasks end-to-end.

---

## 9. Commit

```powershell
cd d:\WORK\IOS-IG-SimHost-FDP
git add -f Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/
git add Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Benchmarks/
git add Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj
git commit -m "feat(blueprints): BATCH-20 DBG-006 complete debug protocol test suite

- NodeHistoryTests.cs: SC1-SC4 (ring buffer, entity isolation, wrap-at-256)
- StateInspectorTests.cs: SC1-SC5 (snapshot, MarshalFromBytes roundtrip)
- HotReloadInteractionTests.cs: SC1-SC4 (hot reload edge cases, asset-selective)
- ProbeDispatchTests.cs: SC1-SC4 (null-sink no-op, forwarding, zero-alloc)
- ProbeOverheadBenchmarks.cs: BenchmarkDotNet setup (< 50ns CI gate)
- ProbeOverheadTests.cs: xUnit CI gate for zero-alloc probe path

Baseline: 411 total -> target: 432+ pass / 5 skip / 0 fail"
```

---

## 10. Report

`.dev/blueprints-1/reports/BATCH-20-REPORT.md`. Required:
- Work completed per sub-task
- Test results (before / after)
- Issues encountered + resolution
- Weak points / deferred items

---

## Success Criteria Summary

| SC | File | Check |
|----|------|-------|
| SC1 | NodeHistoryTests | `OnNodeEnter` records tick and simTime correctly |
| SC2 | NodeHistoryTests | Entity histories are isolated |
| SC3 | NodeHistoryTests | Ring buffer wraps at 256; oldest entry dropped |
| SC4 | NodeHistoryTests | MaxCount limits returned entries |
| SC1 | StateInspectorTests | `GetCurrentStateSnapshot()` non-null when paused |
| SC2 | StateInspectorTests | `GetCurrentStateSnapshot()` null when not paused |
| SC3-SC5 | StateInspectorTests | MarshalFromBytes roundtrips for int, float, unknown type |
| SC1 | HotReloadInteractionTests | Not-paused: `OnHotReloadBegin` does not call Resume |
| SC2 | HotReloadInteractionTests | `OnHotReloadBegin` marks ALL watches stale |
| SC3 | HotReloadInteractionTests | `OnHotReloadCompleted` only clears stale for reloaded assets |
| SC4 | HotReloadInteractionTests | Hash mismatch clears only same-asset breakpoints |
| SC1-SC4 | ProbeDispatchTests | Null-sink no-op, non-null forwarding, zero-alloc |
| Build | All | `dotnet build` zero errors |
| Tests | All | 0 failures full suite |
