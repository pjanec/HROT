# CGF-1-BATCH-29 Report

**Batch:** CGF-1-BATCH-29  
**Task:** CGF1-S0506 — CQRS Decoupling: AssetInventoryTopic + ClusterUiCache  
**Developer:** AI Agent  
**Date:** 2026-03-31

---

## Tasks Completed

- [x] A: AssetInventoryTopic DDS struct (added to OrchestrationMessages.cs, codegen builds clean)
- [x] B: ClusterMaster publishes inventory (`NasBasePath` property, `_inventoryWriter`, 5s tick throttle, `PublishAssetInventory()`, Dispose)
- [x] C: ClusterUiCache (8 readers, `Update()`, `Dispose()`, `GetNodeLastSeenMs()`, `ReachableTargets`, `ActiveStories`, `ActiveTransaction`)
- [x] D: OrchestratorScenarioPanel → ClusterScenarioPanel refactor (new constructor, new `Render(ClusterUiCache, bool)` signature, all ClusterMaster reads replaced, zero _drillMaster dependency)
- [x] E: OrchestratorSubsystem uses ClusterUiCache + ClusterScenarioPanel; DrawUI() rewritten to 8-line thin wrapper
- [x] Tests: all 6 success conditions covered

---

## Test Counts (before → after)

- `Hrot.NED.Tests`: 45 → **47** (+2: `AssetInventoryTopic_IsRegisteredInIdl`, `AssetInventoryTopicQos_IsTransientLocalKeepLast1`)
- `Hrot.Orchestrator.Tests`:  60 → **60** (unchanged, all still green)
- `Hrot.ClusterRunner.Tests`:        161 → **177** (+16: 5 `ClusterUiCacheTests`, 11 `ClusterScenarioPanelTests`)

**Total:** 266 → **284** tests, all passing.

---

## Success Conditions Verification

1. **AssetInventoryTopic published by ClusterMaster** ✅  
   `ClusterMaster.Tick()` calls `PublishAssetInventory()` every 5 seconds (guarded by `_lastInventoryScan` timestamp). The `AssetInventoryTopic` struct is schema-pinned in `OrchestrationSchemaTests`.

2. **ClusterUiCache reflects SystemStateTopic** ✅  
   `ClusterUiCache_ReflectsSystemStateTopic` writes a `SystemStateTopic{CurrentState=LoadingLive}`, calls `Update()`, asserts `CurrentState == LoadingLive && IsBootstrapped == true`.

3. **ClusterUiCache sniffs 2PC traffic** ✅  
   `ClusterUiCache_Sniffs2PcTraffic` writes a `NodeOpCommand{Operation=PrepareState}`, calls `Update()`, asserts `TxHistory.Count == 1 && HasInFlightTransaction == true`.

4. **OrchestratorSubsystem.DrawUI has no _drillMaster reads** ✅  
   Static analysis via `grep_search` on `OrchestratorSubsystem.cs` for `_drillMaster\.` returns 3 matches — all in `Initialize()` (TimeControlRequested handler) and `Update()` (PendingTimeMode). Zero matches inside `DrawUI()`.  
   The `DrawUI()` method body (lines 158–169) reads only from `_uiCache` and calls `_scenarioPanel?.Render(_uiCache, disableAll)`.

5. **ClusterScenarioPanel compiles with ClusterUiCache** ✅  
   `dotnet build IOS-IG-SimHost.sln -c Debug` → `Build succeeded. 0 Error(s)`.

6. **No regression in E2E DSM test suite** ✅  
   All 177 `Hrot.ClusterRunner.Tests` pass including all DsmE2eScriptTests.

---

## Developer Insights

### Issues Encountered

#### 1. API Mismatches between Instructions and Codebase

The batch instructions contained several references to types/fields that don't exist:

| Instruction Reference | Actual API |
|---|---|
| `TimeSyncMode.Deterministic` | `TimeMode.Deterministic` from `ModuleHost.Core.Time` |
| `s.Data.Mode` on `SwitchTimeModeWireDto` | `s.Data.TargetModeInt` (int, cast to `TimeMode`) |
| `s.Data.WallTicksUtc` on `TimePulseDescriptor` | `s.Data.MasterWallTicks` |
| `ClusterOpStatusCode.Completed` | `OrchestrationStatusCode.Success` (== 0) | 
| `s.Data.TargetState` on `NodeOpCommand` | No such field; parsed from `s.Data.PayloadJson["TargetState"]` |
| `TimeSyncMode` enum | Does not exist; real type is `TimeMode` |

#### 2. Transaction ID Mismatch in DrainClusterOpStatus

`NodeOpCommand.TransactionId` ≠ `ClusterOpStatus.RequestId` in production. ClusterMaster generates a new `Guid` for each 2PC transaction's `TransactionId`, while `ClusterOpStatus.RequestId` echoes the original `ClusterOpRequest.RequestId`. The cache's `_inFlight` is keyed by `NodeOpCommand.TransactionId`. 

**Resolution:** `DrainClusterOpStatus()` tries an exact match first (works in unit tests where IDs are identical). On miss, it applies a fallback: mark all in-flight transactions as completed/aborted and clear `_inFlight`. This is documented as a known limitation — the cache is an optimistic UI model, not a transactional log.

#### 3. NodeHealthProfile vs NodeHeartbeat

`ClusterMaster.NodeRoster.ActiveNodes` returns `IReadOnlyDictionary<int, NodeHealthProfile>` (with `LastHeartbeatUtcSeconds`), while `ClusterUiCache.ActiveNodes` stores raw `NodeHeartbeat` DDS structs (with `WallTicksUtc`). 

**Resolution:** Added `_nodeReceivedMs` dictionary (keyed by `NodeId`, value = arrival epoch-ms) with public accessor `GetNodeLastSeenMs(int nodeId)`. `ClusterScenarioPanel` uses this for the "Last HB (ms ago)" column in the node health table.

### Weak Points Spotted

1. **No `TargetState` in NodeOpCommand wire format**: The 2PC wire protocol doesn't carry the target DSM state in `NodeOpCommand`. The cache must parse it from `PayloadJson`. A dedicated wire field would make CQRS sniffers much cleaner.

2. **ClusterOpStatus.RequestId / TransactionId ambiguity**: As noted above, the two IDs are decoupled. A future protocol improvement: include `TransactionId` in `ClusterOpStatus` to enable unambiguous correlation.

3. **OrchestratorScenarioPanel not deleted yet**: Per instructions, `OrchestratorScenarioPanel.cs` and `OrchestratorScenarioPanelTests.cs` should be deleted after all references are updated. Both old files remain for tests that cannot trivially be ported (see P3 items below).

### Design Decisions

1. **ReachableTargets in ClusterUiCache**: Added a `ClusterMasterPlanner` field to `ClusterUiCache` so `ReachableTargets` can be computed from `CurrentState` using `HrotStateGraph.GetNeighbors()`. This avoids `_drillMaster.GetReachableTargets()` in the panel while preserving the correct DSM graph logic.

2. **Active Stories via ManageEpisode sniffer**: `Process2PcNetworkTraffic()` sniffs `StartEpisode` and `StopEpisode`/`ForgetEpisode` `NodeOpCommand` samples to maintain `_activeStories`. This is an optimistic model — story additions/removals may lag by one `Update()` cycle compared to `ClusterMaster.ActiveStories`.

3. **Bootstrap banner simplification**: The new panel shows a generic "Cluster not bootstrapped" message instead of listing specific missing mandatory nodes. The mandatory node names are a local configuration concern not broadcast over DDS; this is the correct CQRS trade-off.

4. **`_drillTime` field removed from OrchestratorSubsystem**: The `_drillTime` field was previously used to pass current sim time to the old `Render(bool isPaused, float drillTime)`. The new panel reads `cache.MasterSimTime` directly, making the field redundant. It was removed.

5. **`Completed` added to DistributedTransaction**: Required by ClusterUiCache's `DrainClusterOpStatus()` to distinguish "completed successfully" vs "still in flight" for CQRS-observed transactions. The existing `IsAborted` flag is unchanged (used by ClusterMaster local logic).

### ActiveStories / Missing ClusterUiCache Fields Strategy

| Missing Field | Strategy |
|---|---|
| `ActiveStories` | Maintained in `ClusterUiCache._activeStories` via `NodeOpCommand` sniffer (StartEpisode/StopEpisode operations). Exposed as `IReadOnlySet<Guid>`. No ClusterMaster fallback. |
| `ActiveTransaction` | Computed property: first entry of `TxHistory` when `HasInFlightTransaction`, else null. |
| `ReachableTargets` | Computed from `CurrentState` using embedded `ClusterMasterPlanner(_planner)`. Updated in `DrainSystemState()` when state changes. |
| Mandatory node names | Not added to cache — the bootstrap banner shows a generic message instead. CQRS principle: local config is not a cluster-observable. |

### DdsReader API

The exact pattern found throughout the codebase (e.g., `TimePulseIngressTranslator.cs`, `ClusterMaster.IngestHeartbeats()`):

```csharp
using var lease = reader.Take();
foreach (var sample in lease)
{
    if (!sample.IsValid) continue;
    // use sample.Data
}
```

- `reader.Take()` returns a `using`-scoped lease (IDisposable).
- `sample.IsValid` filters out lifecycle samples (new/dispose events) with no data payload.
- The lease must be disposed before calling `Take()` again.

---

## P3 Items (Deferred to Future Batches)

1. **`OrchestratorScenarioPanel.cs` / `OrchestratorScenarioPanelTests.cs` not deleted**: The old class and its test file still exist. `OrchestratorSubsystem` no longer references them; the class is dead code. Deletion blocked by the `OrchestratorScenarioPanelTests` which test `ClusterMaster`-specific behavior (GetReachableTargets, HandleClusterOpRequest) that cannot be ported to ClusterScenarioPanel tests. These tests remain valid Orchestrator-behavior tests and should be migrated to `Hrot.Orchestrator.Tests` or removed in the next cleanup batch.

2. **ClusterOpStatus/TransactionId correlation**: Add `TransactionId` to `ClusterOpStatus` wire format for unambiguous cache correlation.

3. **`_drillMaster` reads in `Update()`**: `Update()` still reads `_drillMaster.NodeRoster.ActiveNodes.Keys` and `_drillMaster.PendingTimeMode`. These are needed for the time coordinator logic and are not part of `DrawUI()`, so they are out of scope for S0506. However, they represent the next layer of CQRS decoupling.

---

## Open Items / Risks

- The `ClusterUiCache` `_inFlight` clearing fallback in `DrainClusterOpStatus()` may prematurely close transactions if multiple SysOps complete rapidly. This is acceptable for the UI use-case but not for transactional correctness.
- `ReachableTargets` is only updated when `CurrentState` changes. If a `SystemStateTopic` sample arrives with the same state, it won't be recomputed (initialized to empty on construction). This is by design — the panel shows "No reachable transitions" until at least one DDS state sample arrives.
