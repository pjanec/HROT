# BATCH-02 Report: Explicit Domain Payload Structs

**Batch:** BATCH-02  
**Task:** TASK-D01  
**Status:** ✅ COMPLETE  
**Date:** 2026-04-03

---

## Task Status

### TASK-D01: Add CommitStatePayload, ReplaySeekPayload, AbortTransactionPayload

**Status:** Complete ✅

All six source files updated + three new tests added + four pre-existing tests updated.

---

## Changes Implemented

### New file
- `FDP/Toolkits/FDP.Toolkit.Orchestration/NodeOpPayloads.cs` — three `readonly record struct` types: `CommitStatePayload(int TargetStateId)`, `ReplaySeekPayload(long TargetWallTicks)`, `AbortTransactionPayload(Guid TargetTransactionId)`.

### Updated files
1. **`FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs`** — three pattern-match sites updated:
   - `Tick()` buffered intent dedup: `is int v` → `is CommitStatePayload csp2`
   - `DispatchIntent()` dedup discriminant: `is int sd` → `is CommitStatePayload csp`
   - `DispatchIntent()` state extraction: `is int stateId` → `is CommitStatePayload cp`

2. **`Hrot.Orchestrator/ClusterMaster.cs`** — four changes:
   - CommitState fan-out: `(int)tStep.TargetState` → `new CommitStatePayload((int)tStep.TargetState)`
   - ProcessSeekReplayIntent: `intent.TargetWallTicks` → `new ReplaySeekPayload(intent.TargetWallTicks)`
   - ProcessCancelOperationIntent: `targetId` → `new AbortTransactionPayload(targetId)`
   - `DomainPayloadToString()`: replaced `int i`, `long l`, `Guid g` raw branches with typed struct branches

3. **`Hrot.Orchestrator/TransitionPlanner.cs`** — `PlanTrajectory()` ReplaySeek step: `intent.TargetWallTicks` → `new ReplaySeekPayload(intent.TargetWallTicks)`

4. **`Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`** — `DeserializeNodePayload()`:
   - `CommitState`: returns `new CommitStatePayload(stateId)` instead of boxed `(object)stateId`
   - Added explicit `NodeReplaySeek` case: returns `new ReplaySeekPayload(ticks)`
   - Added explicit `AbortTransaction` case: returns `new AbortTransactionPayload(txId)`

5. **`Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`** — `SerializeNodePayload()`: removed `if (domainPayload is int stateId)` guard; added `CommitStatePayload`, `ReplaySeekPayload`, `AbortTransactionPayload` cases in switch.

### Tests updated (pre-existing tests broken by this batch)
- **`FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ClusterSlaveTests.cs`** — 4 tests updated: `ClusterSlave_PublishesTkClusterStateChangedEvent_OnCommitState`, `Queue_Survives_SwapBuffers_When_AsyncPrepareIsActive`, `MultiStep_Trajectory_BothCommitStatesApplied`, `FaultedPrepare_ClearsPendingQueue`
- **`Hrot.SimHost.Tests/ClusterSlaveHandlerTests.cs`** — 3 tests updated: `CommitState_RaisesClusterStateChangedEvent`, `DuplicateTransactionId_IsDropped`, `PrepareAndCommit_SameTransactionId_BothDispatched`, `LocalClusterState_ReflectsCommittedState_AfterCommitState`
- **`Hrot.Orchestrator.Tests/TransitionPlannerTests.cs`** — 1 test updated: `RunningLiveToRunningReplayWithSeek_Produces_FiveSteps` (was casting DomainPayload to `long`, now uses `Assert.IsType<ReplaySeekPayload>`)

### New tests added
- **`FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ClusterSlaveTests.cs`**:
  - `ClusterSlave_CommitState_WithCommitStatePayload_UpdatesLocalState`
  - `ClusterSlave_CommitState_DeduplicatesOnStateId`
- **`Hrot.Orchestrator.Tests/NodeOpMasterTranslatorTests.cs`**:
  - `CommitStatePayload_RoundTrips_ThroughTranslators` (uses both `NodeOpMasterTranslator` and `NodeOpSlaveTranslator` with DDS in-process loopback)

---

## Test Results

| Project | Passed | Failed | Total |
|---|---|---|---|
| `FDP.Toolkit.Orchestration.Tests` | 35 | 0 | 35 |
| `Hrot.Orchestrator.Tests` | 82 | 0 | 82 |
| `Hrot.Orchestrator.Integration.Tests` | 12 | 0 | 12 |
| `Hrot.SimHost.Tests` | 396 | 1* | 397 |

\* `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose` — **pre-existing failure unrelated to this batch** (DDS tombstone test for GeoSpatial topic, no connection to payload structs or CommitState).

---

## Q&A

**Q1: Which existing test files needed updating because they passed raw `int` as `CommitState` DomainPayload? How many tests were affected?**

Two files required updates:
- `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ClusterSlaveTests.cs` — 4 tests
- `Hrot.SimHost.Tests/ClusterSlaveHandlerTests.cs` — 4 tests (including `LocalClusterState_ReflectsCommittedState_AfterCommitState`)

Additionally `Hrot.Orchestrator.Tests/TransitionPlannerTests.cs` needed 1 fix for the `ReplaySeekPayload` change (was casting `DomainPayload` to `long`).

Total: **9 tests** updated across 3 files.

**Q2: Did you find any other sites that pattern-match on `DomainPayload is int` that weren't listed?**

No additional `is int` pattern-match sites for `DomainPayload` were found beyond the listed ones. The `NodeOpMasterTranslator.SerializeNodePayload()` had a top-level `if (domainPayload is int stateId)` guard (not a switch arm) which was removed. All three `is int` sites in `ClusterSlave.cs` were listed in the batch instructions.

**Q3: What was the challenge (if any) with the `TransitionPlanner` ReplaySeek DomainPayload update? Did it require adding a `using` directive?**

The `ReplaySeek` line to update is in `ClusterMasterPlanner.PlanTrajectory()` in `Hrot.Orchestrator/TransitionPlanner.cs`. No `using` directive was needed — `FDP.Toolkit.Orchestration` was already imported at the top of that file. There was one additional consequence: the existing test `RunningLiveToRunningReplayWithSeek_Produces_FiveSteps` in `TransitionPlannerTests.cs` was casting `seekStep.DomainPayload` to `long` directly, which threw `InvalidCastException` at runtime. This test was updated to use `Assert.IsType<ReplaySeekPayload>`.

**Q4: Did you discover any edge cases in the serialization/deserialization round-trip for `AbortTransactionPayload` or `ReplaySeekPayload`?**

For `AbortTransactionPayload`: the EjectNode code path at line ~620 of `ClusterMaster.cs` passes `null` as the payload (`FanOutNodeOp(NodeOpType.AbortTransaction, Guid.NewGuid(), null, survivingIds)`). This was intentionally left as `null` — the slave translator's `AbortTransaction` case gracefully handles `null` payloads (returns `null` when `hasPayload` is false), so no breaking change occurs on this path.

For `ReplaySeekPayload`: the `opStep.DomainPayload` path in `ClusterMaster.ProcessTransitionStateIntent` at line ~878 (`FanOutNodeOp(NodeOpType.NodeReplaySeek, Guid.NewGuid(), opStep.DomainPayload, activeNodeIds)`) was already passing through `DomainPayload` unchanged. After updating `TransitionPlanner.PlanTrajectory()` to store a `ReplaySeekPayload`, this path automatically transmits the struct — no change needed at the fan-out site itself.

---

## Issues Encountered

1. **`ClusterSlave` constructor ambiguity** — The new test code initially used named-argument form `new ClusterSlave(eventBus: bus, nodeId: 1, subsystemName: "Test")`, which was ambiguous between the production and test constructors. Fixed by using positional args: `new ClusterSlave(1, "Test", bus)`.

2. **`TransitionPlannerTests` cast failure** — The existing test `RunningLiveToRunningReplayWithSeek_Produces_FiveSteps` directly cast `DomainPayload` to `long`. This became an `InvalidCastException` after changing `TransitionPlanner` to store `ReplaySeekPayload`. Fixed by updating the assertion to `Assert.IsType<ReplaySeekPayload>`.

3. **Missing `default: return null;`** — After replacing the `CommitState` case and adding `NodeReplaySeek`/`AbortTransaction` cases in `NodeOpSlaveTranslator.DeserializeNodePayload()`, the original `default: return null;` was removed. Added back explicitly.

---

## Suggested Commit Message

```
feat(orchestration): replace boxed primitives with typed payload structs (TASK-D01)

Introduce CommitStatePayload, ReplaySeekPayload, and AbortTransactionPayload as
readonly record structs in FDP.Toolkit.Orchestration, replacing the previously
boxed int/long/Guid values in ExecuteNodeOpIntent.DomainPayload.

- NodeOpPayloads.cs: new file with three readonly record structs
- ClusterSlave: pattern-match on CommitStatePayload (3 sites)
- ClusterMaster: wrap primitives in structs at fan-out sites; update DomainPayloadToString
- TransitionPlanner: wrap TargetWallTicks in ReplaySeekPayload
- NodeOpSlaveTranslator: deserialize to struct types; add NodeReplaySeek/AbortTransaction cases
- NodeOpMasterTranslator: serialize struct types; remove boxed-int guard
- Tests: update 9 tests across 3 files; add 3 new tests (ClusterSlave dispatch/dedup, round-trip)
```
