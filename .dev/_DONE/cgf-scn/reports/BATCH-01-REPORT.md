# BATCH-01 Report

**Batch:** BATCH-01  
**Developer:** GitHub Copilot  
**Date:** 2025-07-14  
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| TASK-C001 | Done | `ScenarioEntityCreationRequestSource` created in `Hrot.Core.Network` |
| TASK-C002 | Done | `CompositeEntityCreationRequestSource` created in `Hrot.Core.Network` |
| TASK-C003 | Done | Wired into `CgfLogicPack`, `CgfSubsystem`, and `CgfApplication` |

---

## Testing Results

**Unit Tests — Hrot.Core.Tests:** 99 / 99 passed  
**Unit Tests — Hrot.SimHost.Tests:** 369 / 372 passed (3 pre-existing failures, confirmed via `git stash`)  
**Full Solution Build:** succeeded, 0 errors, 0 warnings

**Key Test Scenarios Verified:**
- [x] `ScenarioSource_BasicEnqueueDrain_FifoOrder` — FIFO ordering preserved under single-threaded drain
- [x] `ScenarioSource_MaxItemsPerTick_Cap500` — drain honours `maxRequestsPerTick` cap
- [x] `ScenarioSource_EmptyQueue_NoOp` — no handler calls when queue is empty
- [x] `ScenarioSource_ConcurrentSafety_1000Items` — 1000 items from 4 producer tasks, all drained exactly once
- [x] `CompositeSource_BothSourcesDrained_InOrder` — first source drained fully before second
- [x] `CompositeSource_EmptySources_NoOp` — no handler calls when all inner sources are empty
- [x] `CompositeSource_SingleSource_Passthrough` — single inner source behaves identically to direct call
- [x] `CompositeSource_EmptyListConstructor_ThrowsArgumentException` — empty list rejected at construction
- [x] `CompositeSource_InnerSourceThrows_PropagatesToCaller` — exceptions bubble up, not swallowed
- [x] `CgfLogicPack_NullScenarioSource_ThrowsArgumentNullException` — null guard on new parameter
- [x] `C003_NedRequestsProcessed_ViaCompositeSource` — NED-originated requests reach `SpawnEntityCommand` via composite
- [x] `C003_ScenarioRequestsProcessed_ViaCompositeSource` — scenario-originated requests reach `SpawnEntityCommand` via composite
- [x] `C003_BothSourcesProcessed_SameTick` — both sources drained in a single `CreateEntityRequestSystem.Execute` call

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Several call sites of `CgfLogicPack` existed outside the primary `CgfSubsystem`/`CgfApplication` files — specifically `EditorSubsystem.cs`, `Program.cs` (NetworkDemo), `OfflineKernelBootTests.cs`, and `EditorHarness.cs`. Each was found only when the full solution build failed after incremental test builds succeeded. All were fixed by adding `new ScenarioEntityCreationRequestSource()` as the third argument and adding `using Hrot.Core.Network;` where needed.

The test for `CgfLogicPack` also required discovering the correct API for the FDP event bus (`repo.Bus.SwapBuffers()` then `((ISimulationView)repo).ReadManagedEvents<T>()`), and the correct namespace for `ISimulationView` (`Fdp.ModuleHost.Abstractions`). These were found by tracing through existing passing tests.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

`CgfLogicPack` has a positional constructor requiring concrete types rather than interfaces — adding a new required parameter causes build failures across all callers with no way to provide a default. Accepting `IEntityCreationRequestSource` instead of the concrete `ScenarioEntityCreationRequestSource` in the constructor signature would widen the contract and allow easier substitution in tests without modifying the parameter type.

**Q3: What design decisions did you make beyond the instructions?**

- `maxRequestsPerTick` defaults to 500 in `ScenarioEntityCreationRequestSource`. The instructions did not specify a default; 500 was chosen to match the cap mentioned in TASK-C001's acceptance criteria and to prevent a single tick from draining an unbounded backlog under load.
- `CgfApplication` (the headless composition root used by `CgfSubsystem` in application mode) was given a `ScenarioEntityCreationRequestSource` field and property even though `CgfApplication` does not directly wire `CreateEntityRequestSystem`. This aligns with the instruction's requirement for an `internal` accessor on both `CgfSubsystem` and `CgfApplication` for test introspection.
- The `CompositeEntityCreationRequestSource` constructor rejects an empty list with `ArgumentException`. Alternative: allow empty list and treat as no-op. Rejection was chosen to catch mis-wiring at construction time rather than silently doing nothing at runtime.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- Concurrent enqueue from multiple producer threads while `ProcessRequests` drains from a single consumer thread: verified correct by the `ScenarioSource_ConcurrentSafety_1000Items` test.
- An inner source in `CompositeEntityCreationRequestSource` that throws: exceptions propagate to the caller and any remaining inner sources are skipped. This matches standard .NET fail-fast semantics; the test documents the behaviour explicitly.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

`CompositeEntityCreationRequestSource.ProcessRequests` iterates inner sources sequentially using a `foreach` over `IReadOnlyList<T>`. For the expected number of sources (two: NED adapter + scenario source), this is negligible. For a significantly larger number of sources the list could be iterated with an index-based loop to avoid the enumerator allocation, but this is premature at current scope.

---

## Modified Files

| File | Change |
|------|--------|
| `Hrot/Engine/Hrot.Core/Network/ScenarioEntityCreationRequestSource.cs` | **New** — thread-safe in-memory `IEntityCreationRequestSource` (TASK-C001) |
| `Hrot/Engine/Hrot.Core/Network/CompositeEntityCreationRequestSource.cs` | **New** — composite wrapper over ordered list of sources (TASK-C002) |
| `Hrot/Engine/Hrot.Core.Tests/EntityCreationRequestSourceTests.cs` | **New** — 9 unit tests for C001 and C002 |
| `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` | Added `scenarioSource` required parameter, `ScenarioSource` property (TASK-C003) |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | Creates `_scenarioSource`, builds `CompositeEntityCreationRequestSource`, wires into `CreateEntityRequestSystem` (TASK-C003) |
| `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` | Added `_scenarioEntityCreationSource` field and property (TASK-C003) |
| `Hrot/Subsystems/Hrot.SimHost.Tests/CgfLogicPackTests.cs` | Updated 3 existing tests; added 7 new C003 tests |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Updated `CgfLogicPack` constructor call — added `new ScenarioEntityCreationRequestSource()` |
| `Hrot/Subsystems/Hrot.Editor.Tests/OfflineKernelBootTests.cs` | Same — added missing argument and `using Hrot.Core.Network;` |
| `Hrot/Examples/Hrot.Examples.NetworkDemo/Program.cs` | Same — added missing argument and `using Hrot.Core.Network;` |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` | Same — added missing argument and `using Hrot.Core.Network;` |

---

## Pre-existing Test Failures (Not Introduced by This Batch)

The following 3 tests in `Hrot.SimHost.Tests` were failing before this batch (confirmed via `git stash` + test run):

| Test | Expected | Actual |
|------|----------|--------|
| `CgfLogicPack_EmptyWorld` | 14 systems | 15 systems |
| `SimHostCoreLogicPack_EmptyWorld` | 11 systems | 9 systems |
| `SimulationLogicModule_EmptyWorld` | 14 systems | 12 systems |

These reflect stale system-count assertions from a prior change unrelated to this batch.

---

## Outstanding Issues / Next Steps
- [ ] The 3 pre-existing system-count test failures should be fixed in a subsequent batch by updating the hardcoded expected counts to match current system registration.
