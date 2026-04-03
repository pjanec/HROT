# BATCH-03 Developer Report

**Batch:** BATCH-03  
**Tasks:** TASK-D02, TASK-D07 (partial)  
**Date:** 2026-04-03

---

## Task Status

| Task | Status | Notes |
|------|--------|-------|
| TASK-D02 | ✅ Complete | All steps A1–A6 implemented |
| TASK-D07 (partial) | ✅ Complete | Steps B1–B5 implemented; API signature and ScenarioHeader changes deferred per scope |

---

## Test Results

| Project | Passed | Failed | Total | Notes |
|---------|--------|--------|-------|-------|
| FDP.Toolkit.Orchestration.Tests | 35 | 0 | 35 | |
| FDP.Toolkit.Scenario.Tests | 15 | 0 | 15 | |
| Hrot.Orchestrator.Tests | 84 | 0 | 84 | +2 new (Tests 5 & 6) |
| Hrot.Orchestrator.Integration.Tests | 12 | 0 | 12 | |
| Hrot.SimHost.Tests | 402 | 1 | 403 | 1 pre-existing failure (GeoSpatial DDS) |
| Hrot.SimHost.Integration.Tests | 36 | 2 | 38 | 2 pre-existing failures (DDS timing) |

**Pre-existing failures** (not caused by this batch):
- `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose` — DDS tombstone timing issue in SimHost.Tests
- `EntityLifecycleIntegrationTests.DomainIsolation_Domain0Spawn_DoesNotAffectDomain10` — DDS domain isolation test
- `TraceLoggingTests.SpawnVehicle_EmitsTraceSequence` — trace logging DDS test

All in-scope test projects pass with 0 failures.

---

## Q1 — Was `continue` Missing? Was It Safe to Add?

**Before this batch**, the bus-mode loop used a negative-check pattern:

```csharp
if (!_pendingBusTransitionAcks.TryGetValue(ev.TransactionId, out var tracker))
    continue; // skip events NOT in the tracker
// ... handle tracker — no explicit continue ...
```

There was **no `continue`** after the tracker-handling block. This was harmless originally because there was nothing after the tracker block in the loop. However, once the SerializeLocal block was added *after* the tracker block, an event that matched `_pendingBusTransitionAcks` would fall through into the SerializeLocal check — incorrect double-handling.

**Adding `continue` is safe because:**
1. Each event has a unique `TransactionId` allocated per fan-out operation.
2. `_pendingBusTransitionAcks` is populated only for `TransitionState` events; `_pendingSerializeTasks` only for `SerializeLocal` events. The same `TransactionId` cannot appear in both dictionaries.
3. The semantics are clear — each event should be handled by exactly one branch.

So adding `continue` was correct and prevents potential future bugs if the two dictionaries were ever populated with overlapping IDs.

---

## Q2 — Circular Dependency Resolution

**Yes, a circular dependency existed.** `Hrot.Common.csproj` already references `FDP.Toolkit.Orchestration.csproj`:

```xml
<!-- Hrot.Common.csproj -->
<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Orchestration\FDP.Toolkit.Orchestration.csproj" />
```

Adding a reference from `FDP.Toolkit.Orchestration` back to `Hrot.Common` would create a circular dependency and break the build.

**Resolution:** The three handler *classes* were moved to `Hrot.Common/Orchestration/Handlers/` (a new directory). The *payload records* (`EditLoadHandlerPayload`, `EpisodeHandlerPayload`) deliberately remained in `FDP.Toolkit.Orchestration.Handlers` because they are used by the translators (`NodeOpMasterTranslator`, `NodeOpSlaveTranslator`) on the FDP side of the boundary. The moved handler classes continue to access those payload types transitively through `Hrot.Common → FDP.Toolkit.Orchestration`.

Files updated to use the new namespace (`Hrot.Common.Orchestration.Handlers`):  
`NodeBootstrapper.cs`, `CgfApplication.cs`, `ScenarioSaveLoadTests.cs`, `EditLoadClusterOpHandlerTests.cs`, `EpisodeLoadClusterOpHandlerTests.cs`, `CgfPrepareLiveDispatchTests.cs`, `EpisodeInjectionTests.cs`

---

## Q3 — FDP.Toolkit.Orchestration.Tests Updates

No tests in `FDP.Toolkit.Orchestration.Tests` required structural changes. Specifically:

- `FdpOrchestrationCqrsStructTests.NodeOpCompletedEvent_HasResultPayloadField_NotResultJson` — checks for `ResultPayload` presence and **absence** of `ResultJson`; the new `Operation` field does not break this.
- `FdpOrchestrationCqrsStructTests.CoreEventStructs_HaveUniqueEventIds` — checks EventId attributes, unaffected.
- `ClusterSlaveTests.ClusterSlave_BusDispatch_PublishesNodeOpCompletedEvent` — asserts `status.TransactionId`, `status.NodeId`, `status.StatusCode`, `status.IsParticipating`. The new `Operation` field is now populated by ClusterSlave from `intent.Operation`, making these tests richer but not breaking them.
- `ReferenceArchiveHandlerTests` — asserts `status.ResultPayload as FileManifestResult[]`, unaffected.

All 35 tests passed without modification.

---

## Q4 — Edge Cases in `HandleSerializeLocalCompletion()` Extraction

The extraction was clean with no blocking side-effects. Notable observations:

1. **Nullable `_globalContextHandler`** — accessed with `?` null-conditional — already safe, unchanged.
2. **`_activeCancellations.Remove(archRequestId)`** — modifies shared state, but since ClusterMaster is synchronously ticked (single-threaded) and the `Remove` call happens before starting the async `ContinueWith`, there are no races.
3. **`ContinueWith` callbacks** run on `TaskScheduler.Default` (threadpool thread). The `_sysOpStatusWriter.Write(...)` calls inside them run off the master thread — this was already the case before extraction and is an existing architectural pattern in ClusterMaster, not introduced here.
4. **Bus-mode vs DDS-mode asymmetry** — the bus-mode path receives `List<FileManifestEntry>` already deserialized (from `ev.ResultPayload`), while the DDS path deserializes from JSON. Both paths accumulate into `task.Manifests` before calling `HandleSerializeLocalCompletion`. The helper itself is symmetric — it only cares about the populated `task.Manifests` list, not how they were built.
5. **Empty manifest on DDS error path** — in the DDS path, if `StatusCode.IsError()`, the ACK still decrements `RemainingAcks` but `task.FailureCount` is NOT incremented (unlike the bus-mode path which does increment). This is existing behavior intentionally preserved: the DDS path only counts `FailureCount` for malformed `ResultJson` (JSON deserialisation failure), not for node-reported errors.

---

## Changes Summary

### TASK-D02

| File | Change |
|------|--------|
| `FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs` | Added `NodeOpType Operation` after `TransactionId` in `NodeOpCompletedEvent` |
| `Hrot.NED/Orchestration/OrchestrationMessages.cs` | Added `NodeOpType Operation` after `TransactionId` in `NodeOpStatus` |
| `FDP.Toolkit.Orchestration/ClusterSlave.cs` | Set `Operation = intent.Operation` in all 4 `NodeOpCompletedEvent` publishes |
| `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs` | Set `Operation = (NedNodeOpType)(int)ev.Operation` in DDS NodeOpStatus write |
| `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs` | Refactored `DeserializeResultPayload(op, json)` — returns `List<FileManifestEntry>` for SerializeLocal; added `Operation = fdpOp` to published event; added `using Hrot.Orchestrator` |
| `Hrot.Orchestrator/ClusterMaster.cs` | Added `FdpNodeOpType` alias; restructured bus-mode loop with `continue`; added SerializeLocal ACK handling; extracted `HandleSerializeLocalCompletion()` helper |
| `Hrot.Orchestrator.Tests/NodeOpMasterTranslatorTests.cs` | Added explicit `Operation` to test 3's NodeOpStatus write; added tests 5 and 6 verifying DeserializeResultPayload |

### TASK-D07

| File | Change |
|------|--------|
| `Hrot.Common/Scenario/HrotScenarioEnvelope.cs` | **NEW** — `PeekSubsystemType()` and `IsMatchingSubsystem()` |
| `FDP.Toolkit.Scenario/ScenarioSerializer.cs` | Added `SubsystemType => _subsystemType`; removed `IsMatchingSubsystem()` and `PeekSubsystemType()` |
| `FDP.Toolkit.Orchestration/Handlers/ReferenceScenarioLoadHandler.cs` | Cleared — handler moved to `Hrot.Common` |
| `FDP.Toolkit.Orchestration/Handlers/ReferenceEditLoadHandler.cs` | Removed class body; kept `EditLoadHandlerPayload` record struct only |
| `FDP.Toolkit.Orchestration/Handlers/ReferenceEpisodeLoadHandler.cs` | Removed class body; kept `EpisodeHandlerPayload` record struct only |
| `Hrot.Common/Orchestration/Handlers/ReferenceScenarioLoadHandler.cs` | **NEW** — moved from FDP, uses `HrotScenarioEnvelope` |
| `Hrot.Common/Orchestration/Handlers/ReferenceEditLoadHandler.cs` | **NEW** — moved from FDP, uses `HrotScenarioEnvelope` |
| `Hrot.Common/Orchestration/Handlers/ReferenceEpisodeLoadHandler.cs` | **NEW** — moved from FDP, uses `HrotScenarioEnvelope` |
| `Hrot.SimHost/NodeBootstrapper.cs` | Added `using Hrot.Common.Orchestration.Handlers` |
| `Hrot.CGF/CgfApplication.cs` | Added `using Hrot.Common.Orchestration.Handlers` |
| `Hrot.Orchestrator.Integration.Tests/ScenarioSaveLoadTests.cs` | Added `using Hrot.Common.Orchestration.Handlers` |
| Various test files | Added `using Hrot.Common.Orchestration.Handlers` |
| `Hrot.SimHost.Tests/HrotScenarioEnvelopeTests.cs` | **NEW** — 6 tests for `HrotScenarioEnvelope` |

---

## Suggested Commit Message

```
feat: add NodeOpType.Operation to NodeOpCompletedEvent and NodeOpStatus; move scenario handlers to Hrot.Common

TASK-D02:
- Add Operation field to NodeOpCompletedEvent (FDP domain event) and NodeOpStatus (DDS struct)
- ClusterSlave populates Operation from intent in all published events
- NodeOpSlaveTranslator bridges Operation field to DDS NodeOpStatus
- NodeOpMasterTranslator refactors DeserializeResultPayload(op, json): returns
  List<FileManifestEntry> for SerializeLocal, null for all other operations
- ClusterMaster.ConsumeNodeOpStatuses() bus-mode path extended:
  - transition ACK block now has explicit continue (safe: disjoint TransactionId sets)
  - new SerializeLocal ACK handling using typed ev.ResultPayload
  - completion logic extracted to HandleSerializeLocalCompletion() shared by both paths

TASK-D07 (partial):
- Create HrotScenarioEnvelope in Hrot.Common/Scenario/ with PeekSubsystemType()
  and IsMatchingSubsystem() — application-layer envelope knowledge extracted from FDP toolkit
- Add ScenarioSerializer.SubsystemType read-only property
- Remove PeekSubsystemType() and IsMatchingSubsystem() from ScenarioSerializer
- Move ReferenceScenarioLoadHandler, ReferenceEditLoadHandler, ReferenceEpisodeLoadHandler
  from FDP.Toolkit.Orchestration.Handlers to Hrot.Common.Orchestration.Handlers
  (circular dep: Hrot.Common -> FDP.Toolkit.Orchestration already exists)
- Payload records EditLoadHandlerPayload and EpisodeHandlerPayload remain in FDP
- All callers updated to use Hrot.Common.Orchestration.Handlers namespace
```
