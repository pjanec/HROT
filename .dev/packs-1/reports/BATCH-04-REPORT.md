# BATCH-04 Report

**Batch:** BATCH-04  
**Developer:** GitHub Copilot  
**Date:** 2025-07-15  
**Status:** Complete

---

## 📊 Task Completion

| Task ID   | Status | Notes |
|-----------|--------|-------|
| DEBT-006  | ✅ Done | `MissionControlRequestSystem.cs` deleted; all references removed |
| PACK-C001 | ✅ Done | ClusterMaster fully purged of DDS; `AssetInventoryUpdateEvent` added; translator updated |
| PACK-C002 | ✅ Done | `OrchestrationObserverTranslator` created; `ClusterUiCache` rewritten bus-only; all wiring updated |

---

## 🧪 Testing Results

**Hrot.Orchestrator.Tests:** 88 / 88 ✅  
**Hrot.ClusterRunner.Tests:** 192 / 195 (3 pre-existing failures — see below)

**Key Test Scenarios Verified:**
- [x] `ClusterMaster` constructs with `FdpEventBus` only — no DDS
- [x] `PublishAssetInventory` publishes `AssetInventoryUpdateEvent` on first tick
- [x] `RejectsCommands_UntilMandatoryNodesReady` — Phase 3 verifies `ExecuteNodeOpIntent` fan-out
- [x] `ClusterUiCacheTests` — all 9 tests rewritten to use `FdpEventBus`, zero DDS
- [x] `ClusterScenarioPanelTests` — fixed to pass `FdpEventBus` to `ClusterUiCache`
- [x] `SimHostSubsystemTests` — fixed to use `DdsIdAllocatorServer` instead of `ClusterMaster`

**Pre-existing failures (documented in BATCH-01-REPORT.md, not introduced by this batch):**
- `OrchestratorSubsystemTests.PauseButton_WhenNotPaused_DispatchesPauseTime` — DDS timing
- `OrchestratorTimeModeTests.PendingTimeMode_Deterministic_PublishesSwitchTimeModeEvent` — DDS timing
- `SwitchTimeModeEchoLoopTests.PollIngress_ThenScanAndPublish_DoesNotEchoBack` — DDS timing

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

1. **`RejectsCommands_UntilMandatoryNodesReady` Phase 3 failing:** 
   The test expected an immediate `ClusterOpCompletedEvent` after `TransitionState()` with nodes,
   but `TransitionState` on a populated cluster fans out `ExecuteNodeOpIntent` messages and waits
   for ACKs before completing. Fixed by checking for the fan-out intents instead, which proves
   the command was accepted.

2. **`replace_string_in_file` left duplicate class bodies:**
   When rewriting `ClusterUiCache.cs` and `ClusterUiCacheTests.cs` in multiple passes, partial
   replacements left duplicate sections. Resolved via PowerShell line truncation (`Get-Content |
   Select-Object -First N | Set-Content`) to strip the stale tail.

3. **`ClusterState` ambiguous reference in `ClusterUiCacheTests.cs`:**
   Both `Hrot.NED.Descriptors.Orchestration.ClusterState` (the enum used by the event) and
   another `ClusterState` symbol were in scope. Fixed with an explicit `using` alias:
   `using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;`

4. **3 additional `ClusterMaster(DdsParticipant)` call sites not in scope doc:**
   After removing the DDS constructors the full solution build revealed three more files calling
   the deleted constructor: `SimHostTimeSyncTests.cs`, `SimHostComponentRegistrationTests.cs`,
   and `DdsIdAllocatorMigrationTests.cs`. All were updated to `DdsIdAllocatorServer` with a
   background pump thread — same pattern as the previously-fixed `SimHostSubsystemTests.cs`.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `OrchestratorSubsystem.Update()` now bridges `SwitchTimeModeEvent` from `_eventBus` to
  `_orchestrationBus` by iterating a consume loop. If the same frame produces both an event on
  `_eventBus` and a swap on `_orchestrationBus`, ordering is correct — but the bridge is an
  extra allocation-free copy. A future cleanup could unify the two buses or pass a single
  `SwitchTimeModeEvent` bus through both consumers.

- `ClusterUiCache._activeNodes` remains `Dictionary<int, NodeHeartbeat>` rather than a more
  CQRS-idiomatic projection. This was intentional to preserve the `ClusterScenarioPanel` API,
  but it is a mixed-concern survivor.

**Q3: What design decisions did you make beyond the instructions? How did you handle them?**

1. **`NodeOpSlaveTranslator.DeserializeNodePayload` made `internal static`:**
   `OrchestrationObserverTranslator` needed to deserialize `NodeOpCommand` payloads for the
   `ExecuteNodeOpIntent.DomainPayload` field. Rather than duplicating the deserialization switch,
   the existing private method was promoted to `internal static`. This is strictly additive — the
   method's logic didn't change.

2. **`ForgetEpisode` case added to `DeserializeNodePayload`:**
   The method was missing the `ForgetEpisode` episode family, which would produce a null payload
   for that op type. Added a no-op case returning `null` to keep the switch exhaustive.

3. **`OrchestrationObserverTranslator` uses `PublishManaged` for all string-bearing events:**
   `NodeHeartbeatEvent.SubsystemName` is a string, so it cannot go through the unmanaged `Publish`
   path. All 7 event types were audited for reference-type fields before choosing the bus tier.
   Only `SwitchTimeModeEvent` is primitive-only and uses the native `Publish` path.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- `DdsIdAllocatorMigrationTests` is an _integration_ test whose whole premise is validating that
  `SimHostApp` has no `DdsIdAllocatorServer` field. The field assertion runs reflection over
  `SimHostApp` — this was unaffected. But the test body used `ClusterMaster` as the server
  provider and had to be updated to `DdsIdAllocatorServer` for the same reason as other
  SimHost tests.

- `SimHostComponentRegistrationTests` creates allocators on **five** domains (0, 96, 97, 98, 99).
  Because `ClusterMaster` was never ticked in the original code, allocation might have been
  blocked but the tests still passed (most tests in that class don't exercise network paths).
  The replacement now correctly pumps `ProcessRequests()` on all five domains.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `OrchestrationObserverTranslator.Tick()` serializes JSON for `AssetInventoryUpdateEvent`
  via `DeserializeStringArray()` — this is a string parse per frame even when the inventory
  hasn't changed. A future optimisation could compare hash/version before re-parsing, but the
  inventory is not a hot path.

---

## ⚠️ Outstanding Issues / Next Steps

- The 3 pre-existing DDS-timing test failures (`PauseButton`, `PendingTimeMode_Deterministic`,
  `PollIngress_ThenScanAndPublish_DoesNotEchoBack`) are tracked separately and were not
  introduced by this batch.
- `ClusterUiCache.Process2PcNetworkTraffic()` payload dispatch uses a cascade of `is` casts
  (`EditLoadHandlerPayload`, `CommitStatePayload`, `int`). This is correct but could be
  replaced with a visitor/discriminated union if the op type list grows.

---

## 📁 Files Changed

### Created
- `Hrot.Common/Orchestration/OrchestrationObserverTranslator.cs` — new anti-corruption layer

### Modified — Production
- `Hrot.Common/Hrot.Common.csproj` — added `FDP.Toolkit.Time` project reference
- `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs` — `DeserializeNodePayload` → `internal static`; added `ForgetEpisode` case
- `Hrot.Orchestrator/Events/ClusterCqrsEvents.cs` — added `SystemStateUpdateEvent [9016]`, `AssetInventoryUpdateEvent [9017]`
- `Hrot.Orchestrator/ClusterMaster.cs` — deleted both DDS constructors and all DDS fields/branches; `PublishAssetInventory` publishes `AssetInventoryUpdateEvent`
- `Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs` — consummes `AssetInventoryUpdateEvent` and writes DDS
- `Hrot.ClusterRunner/Services/ClusterUiCache.cs` — complete rewrite; zero `CycloneDDS.Runtime` references
- `Hrot.ClusterRunner/Services/ExConSubsystem.cs` — added `_uiCacheBus` + `OrchestrationObserverTranslator` wiring
- `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` — `ClusterUiCache` now receives `_orchestrationBus`; added `SwitchTimeModeEvent` bridge

### Modified — Tests
- `Hrot.Orchestrator.Tests/ClusterMasterBootstrapTests.cs` — fixed `RejectsCommands_UntilMandatoryNodesReady` Phase 3
- `Hrot.Orchestrator.Tests/ClusterMasterArchiveTests.cs` — added `PublishAssetInventory_PublishesAssetInventoryUpdateEvent_OnFirstTick`
- `Hrot.ClusterRunner.Tests/ClusterUiCacheTests.cs` — complete rewrite; all tests bus-based, zero DDS
- `Hrot.ClusterRunner.Tests/ClusterScenarioPanelTests.cs` — added `FdpEventBus _uiCacheBus`; updated `ClusterUiCache` constructor call
- `Hrot.ClusterRunner.Tests/SimHostSubsystemTests.cs` — replaced `ClusterMaster(_participant)` with `DdsIdAllocatorServer` + pump thread
- `Hrot.SimHost.Tests/SimHostTimeSyncTests.cs` — replaced `ClusterMaster(_allocatorParticipant)` with `DdsIdAllocatorServer` + pump thread
- `Hrot.SimHost.Tests/SimHostComponentRegistrationTests.cs` — replaced `(DdsParticipant, ClusterMaster)[]` with `(DdsParticipant, DdsIdAllocatorServer, Thread, CancellationTokenSource)[]`
- `Hrot.SimHost.Integration.Tests/DdsIdAllocatorMigrationTests.cs` — replaced `ClusterMaster` + `Tick()` with `DdsIdAllocatorServer` + `ProcessRequests()`

### Deleted
- `Hrot.SimHost/Systems/MissionControlRequestSystem.cs` (DEBT-006)
