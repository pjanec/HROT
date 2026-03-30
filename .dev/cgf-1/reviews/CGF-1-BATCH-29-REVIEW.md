# CGF-1-BATCH-29 Review

**Batch:** CGF-1-BATCH-29  
**Reviewer:** Development Lead  
**Date:** 2026-03-30  
**Status:** ✅ APPROVED

---

## Summary

All six success conditions met. `AssetInventoryTopic` DDS struct added to DataModel with
correct QoS. `DrillMaster` publishes inventory every 5 s. `ClusterUiCache` built with 8
readers. `OrchestratorScenarioPanel` replaced by `ClusterScenarioPanel` with zero
`_drillMaster` dependency. `OrchestratorSubsystem.DrawUI()` is a clean 8-line wrapper.
Net new tests: +18 across two assemblies. All 284 tests green.

---

## Scope Check

| Task | Delivered? |
|------|-----------|
| `AssetInventoryTopic` struct with TransientLocal/KeepLast(1) QoS | ✅ |
| Schema pin tests (DataModel.Tests: 45 → 47) | ✅ |
| `DrillMaster.NasBasePath`, `_inventoryWriter`, 5s throttle, `PublishAssetInventory()` | ✅ |
| `ClusterUiCache` (8 readers, `Update()`, `Dispose()`) | ✅ |
| `ClusterScenarioPanel` replaces `OrchestratorScenarioPanel` (zero `_drillMaster` field) | ✅ |
| `OrchestratorSubsystem.DrawUI()` — zero `_drillMaster.*` reads | ✅ |
| 4 `ClusterUiCacheTests` facts covering all 4 success conditions | ✅ |
| All 6 success conditions verified | ✅ |

---

## CQRS Constraint Verification

**`DrawUI()` audit:** Static grep of `OrchestratorSubsystem.cs` for `_drillMaster\.`
returns 3 matches — all in `Initialize()` (TimeControlRequested event wiring) and
`Update()` (PendingTimeMode → TimeCoordinator). **Zero matches inside `DrawUI()`.**
The CQRS separation is correct.

**Note (P3):** `Update()` still reads `_drillMaster.PendingTimeMode` and
`_drillMaster.NodeRoster.ActiveNodes.Keys` for the time coordinator. These are **data
plane** operations (not UI), which is architecturally acceptable for S0506 scope. Moving
the time coordinator logic to a `ClusterUiCache`-driven update path is deferred to
opportunistic cleanup.

---

## Issues Found

### Issue 1 (P3 / Hygiene): `OrchestratorScenarioPanel.cs` and `OrchestratorScenarioPanelTests.cs` not deleted

**Files:** `Bagira.Runner/Services/OrchestratorScenarioPanel.cs`,
`Bagira.Runner.Tests/OrchestratorScenarioPanelTests.cs`  
**Problem:** Both old files still exist and compile. `OrchestratorScenarioPanel` is now
dead code — no production code instantiates it. `OrchestratorScenarioPanelTests` tests
the dead class.  
**Impact:** Dead code compiles and inflates test count. The old tests may be misleading
for future developers.  
**Fix:** P3 debt. Delete `OrchestratorScenarioPanel.cs`; migrate orchestrator behavior
tests from `OrchestratorScenarioPanelTests` to `ClusterScenarioPanelTests` or
`Bagira.Orchestrator.Tests`, then delete the old test file. Schedule for BATCH-30 or
opportunistic.

### Issue 2 (P3 / Architecture): `_drillTime` field is dead code in `OrchestratorSubsystem`

**File:** `Bagira.Runner/Services/OrchestratorSubsystem.cs` line 152  
**Problem:** `_drillTime = (float)(_timeKernel?.CurrentTime.TotalTime ?? 0.0)` is still
computed but never passed to the panel (the panel now reads `cache.MasterSimTime`
directly). The field computes a value that is never read.  
**Fix:** P3. Remove `_drillTime` field and the assignment line.

### Issue 3 (P3 / Architecture): Transaction ID correlation gap in `ClusterUiCache`

**File:** `Bagira.Runner/Services/ClusterUiCache.cs`  
**Problem:** `DrainSysOpStatus()` uses a fallback that clears all in-flight transactions
when an exact `RequestId`→`TransactionId` match fails. This is pragmatic for a UI cache
but architecturally fragile.  
**Fix:** P3. Add `TransactionId` to `SysOpStatus` wire format in a future DataModel
revision to enable unambiguous correlation.

### Issue 4 (Validated, no fix needed): API mismatches corrected by developer

`TimeSyncMode` → `TimeMode`, `TargetModeInt`, `MasterWallTicks`, etc. — all corrected
by the developer by reading existing production code. Good autonomous debugging.

---

## Test Quality Assessment

- **`ClusterUiCacheTests`**: 5 facts exercise real DDS write → Update() → property read
  chain. Assertions are behavioral (not string-existence). `Sniffs2PcTraffic` verifies
  `HasInFlightTransaction` and `TxHistory.Count`. Strong.
- **`ClusterScenarioPanelTests`**: Tests update constructor calls to the new
  `(DdsWriter<SysOpRequest>, ClusterUiCache)` signature. DDS round-trip tests exercise the
  full sysop write path.
- **`OrchestratorScenarioPanelTests`**: Still passes (old class still compiles). P3 to
  clean up (Issue 1).
- **`AssetInventoryTopicQos_IsTransientLocalKeepLast1`**: schema-level QoS pin — confirms
  the generated IDL attributes match the C# attributes. Good practice.

No shallow assertions. Accepted.

Final test counts:
- `Bagira.DDS.DataModel.Tests`: 47 (was 45; +2 schema pin)
- `Bagira.Orchestrator.Tests`: 60 (unchanged)
- `Bagira.Runner.Tests`: 177 (was 161; +16: 5 cache + 11 panel)

---

## Developer Insights (from Report)

Key findings worth recording in DEBT-TRACKER:

1. **No `TargetState` field in `NodeOpCommand`** — the 2PC wire format does not carry the
   target DSM state; cache must parse `PayloadJson`. A wire field would make CQRS sniffers
   cleaner. Record as P3 protocol note.

2. **`SysOpStatus.RequestId` ≠ 2PC `TransactionId`** — the cache uses a fallback to handle
   this mismatch. Record as P3 architectural debt.

3. **Active Stories sniffer** — `Process2PcNetworkTraffic()` sniffs `StartStory`/`StopStory`
   NodeOpCommands to maintain `_activeStories` in the cache. Optimistic, one-cycle lag.
   Acceptable for UI use.

---

## Suggested Git Commit Message

```
feat(orchestrator): S0506 CQRS Decoupling — AssetInventoryTopic + ClusterUiCache (BATCH-29)

Completes CGF1-S0506.

AssetInventoryTopic: new DDS struct (TransientLocal/KeepLast/1) added to
OrchestrationMessages.cs; DrillMaster publishes inventory every 5 s via
ScanLocal*/ScanNas* helpers.

ClusterUiCache: 8-reader CQRS projection (SystemState, AssetInventory,
NodeHeartbeat, SysOpStatus, NodeOpCommand, NodeOpStatus, TimePulse,
SwitchTimeModeWireDto); Update(); Dispose(); ActiveStories sniffer;
ReachableTargets from BagiraStateGraph.

ClusterScenarioPanel (renamed from OrchestratorScenarioPanel): constructor
takes (DdsWriter<SysOpRequest>, ClusterUiCache); zero _drillMaster field;
Render(ClusterUiCache, bool) reads all data from cache.

OrchestratorSubsystem: DrawUI() is now a thin 8-line wrapper with zero
_drillMaster.* reads; _uiCache constructed + disposed; Update() calls
_uiCache.Update().

Tests: +18 total (DataModel.Tests +2, Runner.Tests +16). All 284 passing.
```
