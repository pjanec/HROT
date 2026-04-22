# BATCH-05 Report

**Batch:** BATCH-05  
**Developer:** AI Developer (Claude Sonnet)  
**Date:** 2026-04-02  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| CMC-S011 | ✅ Complete | JSON payload DTOs in `Hrot.Orchestrator/Translators/Payloads/OrchestrationPayloadDtos.cs` |
| CMC-S012 | ✅ Complete | `NodeOpSlaveTranslator` in `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs` |
| CMC-S013 | ✅ Complete | `NodeOpMasterTranslator` in `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs` |
| CMC-S014 | ✅ Complete | `ClusterOpMasterTranslator` in `Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs` |
| CMC-S015 | ✅ Complete | `EventDrivenStorageGateway` in `Hrot.Orchestrator/EventDrivenStorageGateway.cs` |

---

## 🧪 Testing Results

**New Tests Written:** 18 / 18 minimum (all pass)  
**Hrot.Orchestrator.Tests:** 79 / 79 (Passed)  
**Hrot.Orchestrator.Integration.Tests:** 5 / 5 (Passed)  
**Hrot.SimHost.Tests:** 395 / 397 (2 pre-existing failures unrelated to this batch)  
**Hrot.SimHost.Integration.Tests:** 36 / 38 (2 pre-existing failures unrelated to this batch)

### New Test Summary

| Test File | Project | Count | Result |
|-----------|---------|-------|--------|
| `TranslatorDtoTests.cs` | `Hrot.Orchestrator.Tests` | 4 | ✅ Pass |
| `NodeOpMasterTranslatorTests.cs` | `Hrot.Orchestrator.Tests` | 3 | ✅ Pass |
| `ClusterOpMasterTranslatorTests.cs` | `Hrot.Orchestrator.Tests` | 4 | ✅ Pass |
| `EventDrivenStorageGatewayTests.cs` | `Hrot.Orchestrator.Tests` | 3 | ✅ Pass |
| `NodeOpSlaveTranslatorTests.cs` | `Hrot.SimHost.Tests` | 4 | ✅ Pass |

### Pre-existing Test Failures (Not Caused by This Batch)

These failures existed before this batch (confirmed via `git stash` verification):
- `SimHostTimeSyncTests.SimHost_BroadcastsTimePulse_PerTick` — pre-existing
- `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose` — pre-existing
- `EntityLifecycleIntegrationTests.DomainIsolation_Domain0Spawn_DoesNotAffectDomain10` — documented flaky DDS contention test (see BATCH-03-REPORT.md)
- `TraceLoggingTests.SpawnVehicle_EmitsTraceSequence` — pre-existing

### Full Build

```
dotnet build IOS-IG-SimHost.sln → 0 Errors, warnings only (pre-existing)
```

---

## 📝 Developer Insights

### Q1: What issues did you encounter during implementation? How did you resolve them?

**Issue 1: Circular dependency between `Hrot.Common` and `Hrot.Orchestrator`**

The batch spec placed `NodeOpSlaveTranslator` in `Hrot.Common` and the DTOs in `Hrot.Orchestrator/Translators/Payloads/`. Since `Hrot.Orchestrator` references `Hrot.Common` (not the other way around), `NodeOpSlaveTranslator` cannot reference `OrchestrationPayloadDtos`. 

**Resolution:** `NodeOpSlaveTranslator` uses `JsonDocument` directly (no external DTO classes) with private static helpers (`GetString`, `GetBool`, `GetGuid`). This is clean, avoids circular dependency, and keeps the JSON boundary in the file (as required by the rule). The node-level DTOs in `Hrot.Orchestrator` are still used by `NodeOpMasterTranslator` (which is in `Hrot.Orchestrator`).

**Issue 2: `JsonStringEnumConverter` accepts integers by default**

The CMC-S011 test requires that `{\"TargetState\": 31}` throws `JsonException`. The default `JsonStringEnumConverter` silently accepts integers. 

**Resolution:** Created `StrictStringEnumConverter : JsonStringEnumConverter` with `allowIntegerValues: false`. Applied as the converter in `OrchestrationJsonOptions.Default` and on the `[JsonConverter]` property attribute for `TransitionPayloadDto.TargetState`.

**Issue 3: `ClusterMaster.ConsumeNodeOpStatuses` NPEs in bus mode**

`ClusterMaster.Tick()` calls `ConsumeNodeOpStatuses()` unconditionally, but in bus mode `_nodeOpStatusReader` is `null!`. This caused the CMC-S014 end-to-end test to fail with `NullReferenceException`.

**Resolution:** Added an early-return guard at the top of `ConsumeNodeOpStatuses`: `if (_eventBus != null) return;`. This is safe — in bus mode, `NodeOpCompletedEvent` arrives via bus (from `NodeOpMasterTranslator`), not DDS. Added a comment that the full 2PC bus path is a future batch item.

**Issue 4: Pre-existing compile error in `EpisodeInjectionTests.cs`**

BATCH-04 changed `PlanManageEpisode(ClusterState, ClusterOpRequest)` to `PlanManageEpisode(ClusterState, ManageEpisodeIntent)` but the integration test still passed a `ClusterOpRequest`. This caused the full solution to fail to build.

**Resolution:** Fixed the test to construct and pass a `ManageEpisodeIntent` instead of `ClusterOpRequest`. This is the correct API after the BATCH-04 migration.

### Q2: Did you spot any weak points in the existing codebase? What would you improve?

1. **`ClusterMaster` bus mode 2PC gap:** `ConsumeNodeOpStatuses` has no bus path. In bus mode, `NodeOpCompletedEvent` from the bus is published by `NodeOpMasterTranslator` but never consumed by `ClusterMaster`. This means 2PC transaction tracking (pending serialize tasks, branch tasks, episode tasks) doesn't function in bus mode. This should be addressed in a subsequent batch.

2. **`StorageGatewayModule` lacks high-level archive ops:** The batch spec describes `ExportArchiveAsync`, `ImportArchiveAsync`, `SaveScenarioAsync` on `StorageGatewayModule`, but these methods don't exist. The module only has low-level `PullToNasAsync`, `PushToNodesAsync` etc. I introduced `IArchiveStorageBackend` to bridge this gap. The actual implementation that wraps the low-level methods will need to be provided by the application layer.

3. **`JsonStringEnumConverter` pitfall:** The default behavior (accepting integers as enums) is a silent data corruption bug risk across an entire system. The `StrictStringEnumConverter` should be adopted globally in any assembly that processes external JSON.

### Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?

**Decision 1: `IArchiveStorageBackend` interface for CMC-S015**

The spec says `EventDrivenStorageGateway(FdpEventBus, StorageGatewayModule)` but `StorageGatewayModule` doesn't have the required methods. I introduced `IArchiveStorageBackend` with `ExportArchiveAsync`, `ImportArchiveAsync`, `SaveScenarioAsync`. This:
- Makes `EventDrivenStorageGateway` testable without the real gateway
- Follows the Dependency Inversion Principle
- Avoids coupling the gateway to the complex low-level SMB pull mechanics

Alternative considered: Add stub methods directly to `StorageGatewayModule`. Rejected because it would pollute a utility class with no-op stubs.

**Decision 2: Private JSON helpers in `NodeOpSlaveTranslator` instead of shared DTOs**

Since `Hrot.Common` can't reference `Hrot.Orchestrator`, I embedded private `GetString`/`GetBool`/`GetGuid` helpers using `JsonDocument`.

Alternative: Move all node-side DTOs to `Hrot.Common`. Rejected because it would add `System.Text.Json` record types to `Hrot.Common` which is a shared infrastructure library. The current approach confines JSON to the translator file, exactly as the spec requires.

**Decision 3: `ClusterOpMasterTranslator` also consumes `StorageOpCompletedEvent`**

The spec only mentions draining `ClusterOpCompletedEvent` for egress. However, in bus mode the `ClusterMaster.ProcessStorageOpIntents` will eventually publish `StorageOpCompletedEvent` (not `ClusterOpCompletedEvent`) when a storage operation completes. I added a second drain for `StorageOpCompletedEvent` in `ClusterOpMasterTranslator.Tick()` to ensure those completions also reach the DDS `ClusterOpStatus` topic.

### Q4: What edge cases did you discover that weren't mentioned in the spec?

1. **`CommitState` payload as raw int string:** The spec mentions this in the mapping table but doesn't explicitly call out that the `PayloadJson` for `CommitState` is `"31"` (not a JSON object). Both translator sides handle this special case.

2. **Null/empty `PayloadJson` for all operation types:** The slave translator returns `null` for empty payload instead of trying to deserialize, which would throw. This is important since some operations genuinely carry no payload.

3. **`ResultJson` → null `ResultPayload` logic:** When `NodeOpStatus.ResultJson` is empty string, `DeserializeResultPayload` returns `null`. This is verified by CMC-S012 test 3 and CMC-S013 test 3.

4. **CancellationToken cleanup on success path:** In `EventDrivenStorageGateway.ExecuteStorageOpAsync`, the CTS is removed from `_activeCancellations` in the `finally` block. This prevents memory leaks for both success and cancellation paths.

### Q5: Are there any performance concerns or optimization opportunities you noticed?

1. **Per-sample DDS scoped allocations:** The existing pattern of `using var scope = reader.Take()` creates a DDS "loan" every `Tick()`. For very high-frequency calls this may GC-pressure. Not a concern at the orchestration layer (low frequency), but worth noting for sensor-layer translators.

2. **`StubStorageBackend.BlockNext()` TaskCompletionSource:** The test stub uses TCS to simulate in-flight operations. The `CancellationToken.Register` callback keeps the TCS alive during cancellation tests. This is clean for tests but shouldn't be used in production code.

3. **`JsonDocument` creation per field read:** In `NodeOpSlaveTranslator`, each `GetString`/`GetBool`/`GetGuid` call creates and disposes a `JsonDocument`. For a payload with multiple fields (e.g., `NodeTransitionPayload`), this parses the same JSON 3 times. An optimization would be to parse once and read multiple properties. Not critical at the orchestration layer.

---

## 🏗️ Files Created / Modified

### New Files
| File | Purpose |
|------|---------|
| `Hrot.Orchestrator/Translators/Payloads/OrchestrationPayloadDtos.cs` | CMC-S011: JSON payload DTOs + strict converter |
| `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs` | CMC-S012: Slave-side DDS↔bus ACL |
| `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs` | CMC-S013: Master NodeOp DDS↔bus ACL |
| `Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs` | CMC-S014: ClusterOp DDS↔bus ACL |
| `Hrot.Orchestrator/EventDrivenStorageGateway.cs` | CMC-S015: Bus-driven async storage ops |
| `Hrot.Orchestrator.Tests/TranslatorDtoTests.cs` | CMC-S011 tests |
| `Hrot.Orchestrator.Tests/NodeOpMasterTranslatorTests.cs` | CMC-S013 tests |
| `Hrot.Orchestrator.Tests/ClusterOpMasterTranslatorTests.cs` | CMC-S014 tests |
| `Hrot.Orchestrator.Tests/EventDrivenStorageGatewayTests.cs` | CMC-S015 tests |
| `Hrot.SimHost.Tests/NodeOpSlaveTranslatorTests.cs` | CMC-S012 tests |

### Modified Files
| File | Change |
|------|--------|
| `Hrot.Orchestrator/ClusterMaster.cs` | Added bus-mode guard in `ConsumeNodeOpStatuses` to prevent NPE |
| `Hrot.SimHost.Integration.Tests/EpisodeInjectionTests.cs` | Fixed pre-existing BATCH-04 compile error (wrong type passed to `PlanManageEpisode`) |
