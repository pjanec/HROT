# CGF-1-BATCH-21 Report

**Batch:** CGF-1-BATCH-21  
**Developer:** GitHub Copilot  
**Date:** 2026-04-05  
**Status:** Part A complete; Phase 4 G0401/G0402/G0403 complete; G0404 partial — `LocalDiskStorageProvider` + `ReferencePrefetchHandler` done; remaining G0404 handlers + G0405/G0406 deferred to BATCH-22.

---

## Summary

CGF-1-BATCH-21 closes all Part A tech-debt rows (ManageEpisode 2PC, StoryLoadDsmHandler
fail-loud, DESIGN §5.8 note, test rename) and advances Phase 4 generalization
(G0401–G0404 partial).

**Part A — Tech debt (complete):**
- A.1: `ClusterMaster.ManageEpisode` 2PC — `ActiveStories` mutation deferred to ACK
  collection; 2 new `ClusterMasterStoryTests`.
- A.2: `Hrot.SimHost` `StoryLoadDsmHandler` — invalid payloads always set a
  `_pendingTransactionId`; null-repo Commit throws; 5 new `StoryLoadDsmHandlerTests`.
- A.3: `CGF-1-DESIGN.md` §5.8 MVP delta note added; integration test renamed.
- A.4: 5 DEBT-TRACKER rows closed ✅.

**Part B — Phase 4 (G0401–G0404 partial):**
- G0401 ✅ — `FDP.Toolkit.Orchestration` core contracts project, all 3 contract tests pass.
- G0402 ✅ — Generic `ClusterSlave`, `DdsOrchestrationTransport`, `HrotHandlerAdapter`; 4 tests pass.
- G0403 ✅ — Toolkit `TransitionPlanner` (BFS/int), `HrotStateGraph`, `ClusterMasterPlanner`; 31/31 orchestrator tests pass + 2 new toolkit BFS tests.
- G0404 🔄 partial — `LocalDiskStorageProvider` + `ReferencePrefetchHandler` + 3 tests; remaining handlers deferred.

**Deferred to BATCH-22:** G0404 remainder (`ReferenceScenarioLoadHandler`,
`ReferenceEditLoadHandler`, `ReferenceStoryLoadHandler`, NodeBootstrapper/CgfApplication
wiring) + G0405 + G0406 + S0310 + S0106 (S0310/S0106 resume after Phase 4 CI is green).

Build: clean (0 `error CS*`); pre-existing Fhsm.SourceGen DLL lock from SharpLens MCP
is unrelated infrastructure noise (acknowledged since BATCH-19).  
Tests: `Hrot.Orchestrator.Tests` 31/31 ✅; `FDP.Toolkit.Orchestration.Tests` 11/11 ✅.

**Review:** [CGF-1-BATCH-21-REVIEW.md](../reviews/CGF-1-BATCH-21-REVIEW.md)

---

## Part A — Tech Debt

### A.1 — `ClusterMaster.ManageEpisode` 2PC (P2)

**Problem:** `FanOutNodeOp` for `StartEpisode`/`StopEpisode` immediately advanced
`ActiveStories` and emitted `ClusterOpStatus.InProgress` w/ `CompletedSteps == totalSteps`
with no `NodeOpStatus` round-trip.

**Solution:** A `_pendingManageEpisodeTasks` dictionary (keyed by `transactionId`) now
tracks a `PendingStoryTask` record containing the expected set of `RemainingNodeIds` and
the intended mutation lambda. On each tick, `ClusterMaster` drains `NodeOpStatus` messages:
each ACK (regardless of `IsParticipating`) removes the sending node from
`RemainingNodeIds`. When the set empties, `ActiveStories` is mutated and
`ClusterOpStatus.Completed` is emitted. The policy is **participating-only is not required** —
all targeted nodes must respond (participating or not) before completion, which is the
minimal safe default until multi-node policy is defined.

A `if (_gateway != null)` guard was also added in the `ManageEpisode` foreach loop over
`PrefetchScenario` steps so that unit tests (no gateway) do not throw before `StartEpisode`
fan-out.

**Tests added:**
- `ClusterMasterStoryTests.StartEpisode_ActiveStoriesUpdated_AfterNodeAck_NotBefore` — verifies `ActiveStories` is empty before ACK and populated after.
- `ClusterMasterStoryTests.StartEpisode_NonParticipatingAck_CountsTowardCompletion` — verifies a non-participating node ACK still satisfies the set.

**Files changed:**
- `Hrot.Orchestrator/ClusterMaster.cs` — `_pendingManageEpisodeTasks`, `PendingStoryTask` record, `if (_gateway != null)` guard, ACK drain in `Tick`
- `Hrot.Orchestrator.Tests/ClusterMasterStoryTests.cs` — 2 new tests

---

### A.2 — `Hrot.SimHost` `StoryLoadDsmHandler`: always ACK or fail loud (P2)

**Problem:** Invalid `StartEpisode`/`StopEpisode` payloads could leave
`_pendingTransactionId = Guid.Empty`, causing `CommitStartEpisode` / `CommitStopEpisode` to
no-op silently (no `NodeOpStatus` for that transaction). Null `EntityRepository` with
`IsParticipating = true` also returned without publishing.

**Solution:**
- `PrepareAsync(StartEpisode)` and `PrepareAsync(StopEpisode)` always set
  `_pendingTransactionId` (even for bad payloads). Invalid payloads set
  `_pendingIsParticipating = false`.
- `CommitStartEpisode` / `CommitStopEpisode`: if `_pendingIsParticipating = true` and
  `_entityRepository == null`, throw `InvalidOperationException` (fail-loud; orchestrator
  sees the exception rather than stalling on a missing ACK).
- ACK is always published via `PublishAck` on every `Commit*` path.

**Tests added (`StoryLoadDsmHandlerTests.cs`):**
- `StoryLoadDsmHandler_InvalidStartEpisodePayload_AcksNonParticipating`
- `StoryLoadDsmHandler_InvalidStopEpisodePayload_AcksNonParticipating`
- `StoryLoadDsmHandler_NullRepository_Participating_ThrowsOnCommitStart`
- `StoryLoadDsmHandler_NullRepository_Participating_ThrowsOnCommitStop`
- `StoryLoadDsmHandler_ValidPayload_NoRepo_NonParticipating_AcksOk`

**Files changed:**
- `Hrot.SimHost/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs` — invalid-payload paths, null-repo guard throws
- `Hrot.SimHost.Tests/StoryLoadDsmHandlerTests.cs` — 5 new tests

---

### A.3 — DESIGN + TASK-DETAIL hygiene (P3)

**Design note added:** `CGF-1-DESIGN.md` §5.8 *ManageEpisode 2PC — MVP Implementation Note*
documents the deferred full-2PC design (orchestrator-side `NodeOpStatus` subscription,
per-transaction participation map, timeout logic) as an intentional MVP delta, with a
cross-reference to `CGF-1-TASK-DETAIL.md` §CGF1-S0308.

**Test rename:** `RecordReplayIntegrationTests.NodeBootstrapper_BrainRole_RegistersEcsRecordReplayController`
→ `NodeBootstrapper_BrainRole_RegistersLiveLoadDsmHandler` (assertion is `LiveLoadDsmHandler`, not `EcsRecordReplayController`).

**Files changed:**
- `.dev/cgf-1/CGF-1-DESIGN.md` — §5.8 added
- `Hrot.SimHost.Integration.Tests/RecordReplayIntegrationTests.cs` — test renamed

---

### A.4 — DEBT-TRACKER

5 rows closed ✅ in `.dev/DEBT-TRACKER.md`:

| Row | Description |
|-----|-------------|
| P2 Architecture | `ClusterMaster.ManageEpisode`: no `NodeOpStatus` round-trip → 2PC implemented |
| P2 Correctness | `StoryLoadDsmHandler`: missing ACK on invalid payload / null repo → fixed |
| P3 Documentation | `CGF-1-DESIGN.md` ManageEpisode/story ACK MVP delta note missing → §5.8 added |
| P3 Hygiene | `NodeBootstrapper_BrainRole_RegistersEcsRecordReplayController` stale name → renamed |
| P3 Testing | No test for `ManageEpisode → NodeOpStatus` aggregation or malformed payload ACK → covered |

---

## Part B — Phase 4: FDP Toolkit Orchestration

### G0401 — `FDP.Toolkit.Orchestration` Core Contracts ✅

**New project:** `FDP/Toolkits/FDP.Toolkit.Orchestration/` — toolkit-pure (no `Hrot.*`
references). Contains:

| File | Description |
|------|-------------|
| `IDsmHandler.cs` | `bool CanHandle(int opId); Task PrepareAsync(OrchestrationCommand); void Commit(OrchestrationCommand); void Abort(OrchestrationCommand)` |
| `ITickableDsmHandler.cs` | Extends `IDsmHandler` with `void DrainDeferredAcks()` |
| `IOrchestrationTransport.cs` | `void PublishHeartbeat(int nodeId, int stateId, string subsystem); void PublishStatus(OrchestrationStatus); bool TryDequeueCommand(out OrchestrationCommand)` |
| `ITransitionGraph.cs` | `IReadOnlyList<int> GetNeighbours(int stateId)` |
| `TransitionGraphBuilder.cs` | Fluent builder: `AddEdge(int,int)`, `Build() → ITransitionGraph` |
| `IScenarioStorageProvider.cs` | `Stream OpenScenarioFile(…); string EnsureStagingDirectory(…); IEnumerable<string> EnumerateScenarioFiles(…)` |
| `OrchestrationCommand.cs` | `record OrchestrationCommand(int OperationId, Guid TransactionId, string? Payload)` |
| `OrchestrationStatus.cs` | `record OrchestrationStatus(OrchestrationStatusCode StatusCode, Guid TransactionId, bool IsParticipating, int NodeId)` |
| `OrchestrationStatusCode.cs` | `enum { Success, Failure }` |
| `TkClusterStateChangedEvent.cs` | `record TkClusterStateChangedEvent(int FromStateId, int ToStateId, string SubsystemName)` |
| `Handlers/` | (empty — populated by G0404/G0405) |

**New test project:** `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/` with
`OrchestrationContractTests.cs` — 3 tests covering transport round-trip contract,
`TransitionGraphBuilder` cycle detection, and `OrchestrationStatusCode` serialization
roundtrip.

**Solution registration:** Both projects added to `IOS-IG-SimHost.sln`
(GUIDs `{E7A3C82F-5B4D-4F81-9A3E-D2B7E1C5F8A4}` /
`{C4D9B7E2-8F3A-46C5-B8D1-A7E6C3F2D9B5}`) under FDP Toolkits folder
`{3DFBA611-AEBE-D6DE-A5E3-4D7D40152939}`.

**Build fixes required during G0401:**
- `OrchestrationContractTests.cs` — missing `using System;` (Guid undefined) → added.
- 5 Hrot handler files (`Hrot.CGF/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs`
  and 4 `Hrot.SimHost` handlers) — `IDsmHandler` now ambiguous between
  `Hrot.Common.Orchestration.IDsmHandler` and `FDP.Toolkit.Orchestration.IDsmHandler`
  → all base-class declarations qualified as `Hrot.Common.Orchestration.IDsmHandler`.

**Tests:** 3/3 pass.

**Files changed:**
- `FDP/Toolkits/FDP.Toolkit.Orchestration/` — 10 source files (new project)
- `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/` — `OrchestrationContractTests.cs`, `.csproj` (new project)
- `IOS-IG-SimHost.sln` — 2 new project entries + config + NestedProjects
- `Hrot.CGF/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs` — qualified `IDsmHandler`
- `Hrot.SimHost/Modules/Orchestration/Handlers/{Checkpoint,PrefetchFiles,ReplayLoad,StoryLoad}DsmHandler.cs` — qualified `IDsmHandler`

---

### G0402 — Generic `ClusterSlave` + `DdsOrchestrationTransport` ✅

**Toolkit `ClusterSlave`** (`FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs`):
- Uses `IOrchestrationTransport`, `ITickableDsmHandler` (handler list), publishes
  `TkClusterStateChangedEvent` via `FdpEventBus` on `CommitState`.
- `CommitStateOperationId = 2` (integer constant — toolkit cannot reference
  `NodeOpType` from `Hrot.NED`).
- Deduplicates transactions: re-delivery of the same `TransactionId` during prepare
  is a no-op.
- Async-prepare deferral: if `PrepareAsync` is not synchronously complete, `Commit`
  is held until the next `Tick()` after the task resolves.
- Constructors: `public ClusterSlave(IOrchestrationTransport, int nodeId, string subsystem, FdpEventBus?)` + `internal ClusterSlave(FdpEventBus? = null)` (test-only).
- Internal test helpers: `EnqueueCommandForTest(OrchestrationCommand)`, `LocalStateIdForTest` (int).
- `InternalsVisibleTo("FDP.Toolkit.Orchestration.Tests")` added to `.csproj`.

**`DdsOrchestrationTransport`** (`Hrot.Common/Orchestration/DdsOrchestrationTransport.cs`):
- Implements `IOrchestrationTransport` via CycloneDDS.
- `PublishHeartbeat` → `DdsWriter<NodeHeartbeat>`; `PublishStatus` → `DdsWriter<NodeOpStatus>`.
- Background listener thread maps `NodeOpCommand` → `OrchestrationCommand` and enqueues to
  `ConcurrentQueue<OrchestrationCommand>`.
- `TryDequeueCommand` pops from the queue.
- `StatusWriter` property exposed for backward compatibility (legacy Hrot handlers still
  need raw `DdsWriter<NodeOpStatus>` until G0404/G0405 migration).

**`HrotHandlerAdapter`** (`Hrot.Common/Orchestration/HrotHandlerAdapter.cs`):
- Wraps `Hrot.Common.Orchestration.IDsmHandler` as `FDP.Toolkit.Orchestration.ITickableDsmHandler`.
- `CanHandle(int) → (NodeOpType)int` conversion; `DrainDeferredAcks()` forwards to inner
  if inner implements `Hrot.Common.Orchestration.ITickableDsmHandler`.
- Bridges `OrchestrationCommand ↔ NodeOpCommand`.
- **Migration window:** Remove once all handlers adopt `FDP.Toolkit.Orchestration.IDsmHandler` (G0404/G0405).

**Project dependency added:** `Hrot.Common.csproj` now references `FDP.Toolkit.Orchestration`.
`FDP.Toolkit.Orchestration.Tests.csproj` references `Hrot.Common` (for DDS transport integration test).

**Note:** The 4 original Hrot `ClusterSlave` copies (SimHost, CGF, IG, IOS) are
**intentionally not deleted** in this batch — deletion is G0406 and requires all handlers
to be migrated first.

**Tests (ClusterSlaveTests.cs):**
- `ClusterSlave_DispatchesPrepareAsyncAndCommit_SynchronousHandler` — sync handler dispatched immediately.
- `ClusterSlave_DeferCommit_WhenPrepareAsyncIsAsync` — async prepare defers Commit to next Tick.
- `ClusterSlave_DeduplicatesTransactions` — second PrepareAsync for same TransactionId is a no-op.
- `DdsTransport_DeliversCommand_ToClusterSlave` — end-to-end DDS round-trip on domain 17.

**Files changed:**
- `FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs` — new
- `Hrot.Common/Orchestration/DdsOrchestrationTransport.cs` — new
- `Hrot.Common/Orchestration/HrotHandlerAdapter.cs` — new
- `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ClusterSlaveTests.cs` — new (4 tests)
- `FDP/Toolkits/FDP.Toolkit.Orchestration/FDP.Toolkit.Orchestration.csproj` — `InternalsVisibleTo`
- `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/FDP.Toolkit.Orchestration.Tests.csproj` — `Hrot.Common` reference
- `Hrot.Common/Hrot.Common.csproj` — `FDP.Toolkit.Orchestration` reference

---

### G0403 — `TransitionPlanner` generalized on `ITransitionGraph` ✅

**Toolkit `TransitionPlanner`** (`FDP/Toolkits/FDP.Toolkit.Orchestration/TransitionPlanner.cs`):
- Pure BFS on `ITransitionGraph`; integer state IDs only; zero Hrot dependencies.
- `IReadOnlyList<int> CalculateShortestPath(int fromStateId, int toStateId)`.
- Throws `InvalidOperationException` with integer state IDs when no path exists.

**`HrotStateGraph`** (`Hrot.Orchestrator/HrotStateGraph.cs`):
- `public static ITransitionGraph Build()` — constructs the canonical Hrot DSM edge set via `TransitionGraphBuilder`, matching the prior hardcoded adjacency dictionary in the old `TransitionPlanner`.
- Edges: `Standby → {LoadingEdit, LoadingLive, LoadingReplay}`; full edit/live/replay/dry-run cycles.

**`Hrot.Orchestrator/TransitionPlanner.cs` → `ClusterMasterPlanner`:**
- Class renamed from `TransitionPlanner` to `ClusterMasterPlanner`.
- Old hardcoded `Adjacency` dictionary removed.
- New constructor: `ClusterMasterPlanner(ITransitionGraph graph)` — creates toolkit `TransitionPlanner` internally.
- `CalculateShortestPath(ClusterState, ClusterState)` delegates to toolkit BFS with `(int)` casts, converts result list and catches toolkit exceptions to re-throw with ClusterState enum names in the message (so test assertions on "Degraded", "RunningLive" etc. still hold).
- `PlanTrajectory`, `PlanManageEpisode`, step classes (`ISysOpStep`, `TransitionStep`, `OperationStep`) unchanged.

**`ClusterMaster.cs` updated:** `_planner` field changed to `ClusterMasterPlanner`,
initialized with `new ClusterMasterPlanner(HrotStateGraph.Build())`.

**`TransitionPlannerTests.cs` updated:**
- `_planner` → `ClusterMasterPlanner(HrotStateGraph.Build())`.
- `_tkPlanner` field added: `new FDP.Toolkit.Orchestration.TransitionPlanner(HrotStateGraph.Build())`.
- `TransitionToDegraded_ThrowsInvalidOperationException` replaced by `TkPlanner_ImpossibleRequest_ThrowsInvalidOperationException` (uses int IDs via toolkit planner).
- New test: `TkPlanner_StandbyToRunningLive_BfsPathPreserved` — verifies toolkit BFS produces same int-path as prior Hrot planner for Standby→RunningLive.
- `RunningDryRunToRunningReplay_Produces_SixSteps` updated to call `_planner.CalculateShortestPath(ClusterState, ClusterState)`.

**Tests:** 31/31 orchestrator tests pass (all pre-existing + 2 new G0403).

**Files changed:**
- `FDP/Toolkits/FDP.Toolkit.Orchestration/TransitionPlanner.cs` — new
- `Hrot.Orchestrator/HrotStateGraph.cs` — new
- `Hrot.Orchestrator/TransitionPlanner.cs` — class renamed `ClusterMasterPlanner`, BFS delegated to toolkit
- `Hrot.Orchestrator/ClusterMaster.cs` — `_planner` type + initialization updated
- `Hrot.Orchestrator.Tests/TransitionPlannerTests.cs` — `ClusterMasterPlanner`, `_tkPlanner`, 2 new tests

---

### G0404 — Reference Handlers (partial 🔄)

#### Completed

**`LocalDiskStorageProvider`** (`Hrot.Common/Orchestration/LocalDiskStorageProvider.cs`):
- Implements `IScenarioStorageProvider`.
- Default storage root: `C:\FDP_Temp`.
- `OpenScenarioFile(scenarioId, fileName)` → `FileStream`.
- `EnsureStagingDirectory(scenarioId)` → `Directory.CreateDirectory`, returns path.
- `EnumerateScenarioFiles(scenarioId)` → `*.json` files in `<root>\<scenarioId>\`.

**`ReferencePrefetchHandler`** (`FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferencePrefetchHandler.cs`):
- Implements `FDP.Toolkit.Orchestration.IDsmHandler`.
- `PrefetchFilesOperationId = 25` (integer constant matching `NodeOpType.PrefetchFiles`).
- `PrepareAsync`: parses `ScenarioId` from command payload; calls
  `storageProvider.EnsureStagingDirectory()`.
- `Commit`: calls `transport?.PublishStatus(OrchestrationStatus{ Success, IsParticipating=true })`.
- Constructor: `(IOrchestrationTransport? transport, int nodeId, IScenarioStorageProvider storageProvider)`.

**Tests (`ReferenceHandlerTests.cs`):**
- `LocalDiskStorageProvider_EnsureStagingDirectory_CreatesDir` — verifies directory is created on disk.
- `ReferencePrefetchHandler_AcksViaTransport_OnCommit` — G0404 success condition: transport `PublishStatus` called with `Success`.
- `ReferencePrefetchHandler_NullTransport_NoException` — null transport guard; no throw on Commit.

**Files changed:**
- `Hrot.Common/Orchestration/LocalDiskStorageProvider.cs` — new
- `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferencePrefetchHandler.cs` — new
- `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ReferenceHandlerTests.cs` — new (3 tests)

#### Deferred to BATCH-22

The following G0404 items are **not** in this batch:

| Item | Reason |
|------|--------|
| `ReferenceScenarioLoadHandler` | Requires `ScenarioSerializer` + `EntityRepository?` integration; significant scope |
| `ReferenceEditLoadHandler` | Same as above (edit-state variant) |
| `ReferenceStoryLoadHandler` | Adds `IOrchestrationTransport?` + `int nodeId` to serialization path |
| `NodeBootstrapper.BuildOrchestration` wiring | Depends on all reference handlers being complete |
| `CgfApplication` wiring update | Depends on G0404 handlers + NodeBootstrapper |

---

## Deferred Work Summary

| Item | Deferred to | Condition |
|------|-------------|-----------|
| G0404 remainder (3 handlers + wiring) | BATCH-22 | — |
| G0405 (`CheckpointIOWorker` relocation, 4 reference handlers) | BATCH-22 | — |
| G0406 (delete 4 old Hrot `ClusterSlave` copies, full CI validation) | BATCH-22 | G0404+G0405 complete |
| CGF1-S0310 (E2E DSM test script suite) | Post Phase-4 | Phase 4 CI green |
| CGF1-S0106 (ImGui scenario & story controls) | Post Phase-4 | Phase 4 CI green |

---

## Test Counts

| Project | Result |
|---------|--------|
| `Hrot.Orchestrator.Tests` | 31 / 31 ✅ |
| `FDP.Toolkit.Orchestration.Tests` | 11 / 11 ✅ (3 G0401 + 4 G0402 + 2 G0403 BFS + 2 G0404) |

*`Hrot.SimHost.Tests`, `Hrot.SimHost.Integration.Tests`, `Hrot.IG.Tests` etc. are
blocked during this session by the Fhsm.SourceGen DLL lock from SharpLens MCP
(acknowledged since BATCH-19; unrelated to this batch's changes).*

---

## Build Status

Solution builds clean (0 `error CS*`). Only pre-existing `MSB3021`/`MSB3027`
Fhsm.SourceGen file-lock warnings under SharpLens MCP.
