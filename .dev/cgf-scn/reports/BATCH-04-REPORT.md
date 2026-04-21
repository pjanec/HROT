# BATCH-04 Report

**Batch:** BATCH-04
**Tasks:** TASK-C006, TASK-C007, TASK-C012
**Developer:** AI Agent
**Date:** 2026-04-28
**Status:** COMPLETE

---

## 1. Completion Summary

### New Files

| File | Description |
|------|-------------|
| `Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/CgfScenarioLoadHandler.cs` | CGF-authoritative scenario load handler (TASK-C006): `PrepareLive` op, calls `StagingEntityExtractor.Extract` and enqueues into `ScenarioEntityCreationRequestSource` |
| `Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/CgfEpisodeLoadHandler.cs` | CGF-authoritative episode load handler (TASK-C007): `StartEpisode` injects `EpisodeTag`-tagged entities; `StopEpisode` publishes `DestroyEntityCommand` per `EpisodeTag` entity via event bus |
| `Hrot/Subsystems/Hrot.SimHost.Tests/CgfScenarioLoadHandlerTests.cs` | 4 unit tests for TASK-C006 |
| `Hrot/Subsystems/Hrot.SimHost.Tests/CgfEpisodeLoadHandlerTests.cs` | 5 unit tests for TASK-C007 |

### Modified Files

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` | Replaced `ReferenceScenarioLoadHandler`/`ReferenceEpisodeLoadHandler` registrations with `CgfScenarioLoadHandler`/`CgfEpisodeLoadHandler`; wired shared `SequentialIdAllocator` |
| `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` | TASK-C012: changed `ReferenceEpisodeLoadHandler` registration from `world: <liveRepo>` to `world: null` |

---

## 2. Implementation Notes

### CgfScenarioLoadHandler (TASK-C006)

Lives in `Hrot.CGF.Orchestration.Handlers` namespace.

- `CanHandle`: returns `true` only for `NodeOpType.PrepareLive`.
- `PrepareAsync`: calls `_loader.TryLoadScenarioJson(intent.GetScenarioId())`, stores the result (may be `null`) and `intent.TransactionId`. No side effects on the creation source.
- `Commit`: if `_pendingJson` is `null` → no-op. Otherwise calls `_extractor.Extract(_serializer, _pendingJson, _idAllocator, behaviorRemapper: _remapper)`, then calls `_source.Enqueue(req)` for every returned `EntityCreationRequest`. Clears pending state after.
- `Abort`: clears `_pendingJson` and `_pendingTransactionId`.
- Constructor optional parameters: `ScenarioBehaviorRemapper? remapper = null`.

### CgfEpisodeLoadHandler (TASK-C007)

Lives in `Hrot.CGF.Orchestration.Handlers` namespace.

- `CanHandle`: returns `true` for `NodeOpType.StartEpisode` and `NodeOpType.StopEpisode`.
- `PrepareAsync (StartEpisode)`: calls `_loader.TryLoadScenarioJson(intent.GetScenarioId())`, stores JSON and episode id.
- `Commit (StartEpisode)`: if `_pendingJson` is `null` → no-op. Otherwise extracts with `episodeId` set (so `StagingEntityExtractor` appends `EpisodeTag` to every request). Enqueues each request.
- `PrepareAsync (StopEpisode)`: no-op (world state loaded at commit time).
- `Commit (StopEpisode)`: queries `_world` for all entities bearing `EpisodeTag` (including `Constructing` lifecycle to catch partially-spawned entities). For each entity that has `NetworkIdentity`, publishes `DestroyEntityCommand { NetworkId = netId.Value, Reason = "CgfEpisodeStop" }` via `_world.Bus.PublishManaged(...)`. Does **not** call `repo.DestroyEntity` — destruction is delegated to the network authority pipeline.
- `Abort`: clears pending state.

### TASK-C012 — SimHost Passive Demotion

The single line change in `NodeBootstrapper.cs`:

```csharp
// Before
new ReferenceEpisodeLoadHandler(scenarioSerializer, scenarioLoader, world))

// After
new ReferenceEpisodeLoadHandler(scenarioSerializer, scenarioLoader, world: null))
```

This demotes SimHost's episode handler to a header-peek observer. Without this change, both CGF and SimHost would independently materialize episode entities when an episode starts — a split-brain condition. The two changes (TASK-C007 + TASK-C012) ship together in this batch per the design constraint.

### CgfApplication.cs Wiring

```csharp
var idAllocator = new SequentialIdAllocator();
_clusterSlave.RegisterHandler(
    new CgfScenarioLoadHandler(scenarioSerializer, scenarioLoader,
        new StagingEntityExtractor(), _scenarioEntityCreationSource, idAllocator));
_clusterSlave.RegisterHandler(
    new CgfEpisodeLoadHandler(scenarioSerializer, scenarioLoader,
        new StagingEntityExtractor(), _scenarioEntityCreationSource, idAllocator, _world));
```

A single `SequentialIdAllocator` instance is shared between the two handlers so that network IDs allocated during scenario load and episode load come from the same sequence and do not collide.

### Build Complications

Two rounds of missing `using` directives were resolved:

1. `INetworkIdAllocator` is in `Fdp.Toolkit.NetworkSpawning`, not `Hrot.Core.Network`.
2. `ScenarioBehaviorRemapper` is in `Fdp.Toolkit.Behavior`.
3. `ScenarioEntityCreationRequestSource` and `EntityCreationRequest` are in `Hrot.Core.Network`.

Both handler files received all three using directives before the final clean build.

---

## 3. Test Results

```
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9
```

### CgfScenarioLoadHandlerTests (4 tests)

| Test | Result |
|------|--------|
| `HappyPath_TwoEntities_QueueHasTwoRequests` | PASS |
| `ScenarioNotFound_LoaderReturnsNull_QueueEmpty` | PASS |
| `Abort_ClearsState_CommitDoesNothing` | PASS |
| `CanHandle_PrepareLive_ReturnsTrue_OtherOpsReturnFalse` | PASS |

### CgfEpisodeLoadHandlerTests (5 tests)

| Test | Result |
|------|--------|
| `StartEpisode_Commit_EnqueuesRequestsWithEpisodeTag` | PASS |
| `StopEpisode_Commit_PublishesDestroyEntityCommandsViaEventBus` | PASS |
| `CanHandle_StartAndStopEpisode_TrueOthersReturnFalse` | PASS |
| `Abort_BeforeCommit_LeavesQueueEmpty` | PASS |
| `StartEpisode_MissingJson_QueueEmpty` | PASS |

---

## 4. Build Status

```
Build succeeded.
    0 Error(s)
```

Full solution (`IOS-IG-SimHost.sln`) builds cleanly with no warnings added by this batch.
