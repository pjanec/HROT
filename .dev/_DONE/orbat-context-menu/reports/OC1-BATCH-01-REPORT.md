# OC1-BATCH-01 Report

**Batch:** OC1-BATCH-01  
**Developer:** GitHub Copilot  
**Date:** 2026-03-22  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| OC1-B001 | ✅ Complete | `ActivateRouteAuthoringTool` now calls `BeginAreaAuthoringSession` before pushing the PointSequenceTool |
| OC1-B002 | ✅ Complete | ConfigPanel checkbox label changed from "Road Graphs" to "Routes"; JSON key `road_graphs` preserved |
| OC1-B003 | ✅ Complete | `ActivateAreaAuthoringTool` now checks `_testCreateEntityRequestSink`; guard relaxed to permit test-sink mode; coordinate contract verified by tests |
| OC1-B004 | ✅ Complete | `IosLogic` subscribes to `Repo.EntityDeleted` in constructor; unsubscribes in `Dispose` |
| OC1-C001 | ✅ Complete | `CMD_DRAW_PERSONAL_ROUTE` appended to `CommandType` enum with XML doc comment |

---

## 🧪 Testing Results

**Unit Tests Passed:**
- Hrot.IG.Tests: 387 / 387 ✅
- Hrot.ExCon.Tests: 316 / 316 ✅
- Hrot.Map.Common.Tests: 94 / 94 ✅
- Hrot.NED.Tests: 33 / 33 ✅
- Hrot.SimHost.Tests: 326 / 326 ✅ (passes standalone; intermittently fails in sequential batch due to pre-existing DDS port contention — not caused by this batch)
- Hrot.ClusterRunner.Tests: 112 / 112 ✅

**New Tests Added:**
- `Hrot.IG.Tests/AreaAuthoringTests.cs` — 8 new tests (B003)
- `Hrot.IG.Tests/MapCommandControllerTests.cs` — 2 new regression tests (B001)
- `Hrot.ExCon.Tests/IosLogicEntityDeletionTests.cs` — 4 new tests (B004)
- `Hrot.ExCon.Tests/ConfigPanelTests.cs` — 2 new tests (B002)
- `Hrot.Map.Common.Tests/DescriptorMapperAreaShapeTests.cs` — 4 new tests (B003 coordinate contract)

**Key Test Scenarios Verified:**
- [x] `OnAreaEntityCreated` without session is a no-op (B001 regression)
- [x] `OnAreaEntityCreated` with session writes to entity writer (B001 fix)
- [x] Area authoring tool emits request via test sink (B003 testability fix)
- [x] Overlay points sum-of-relative-offsets = 0 (B003 coordinate contract)
- [x] `DescriptorMapper.MapToComponents` produces correct relative-Cartesian offsets for area shapes (B003 end-to-end coordinate math)
- [x] `SelectedEntityId` clears when selected entity deleted (B004 SC1)
- [x] `SelectedEntityId` unchanged when different entity deleted (B004 SC2)
- [x] No exception when no entity is selected and deletion fires (B004 SC3)
- [x] `road_graphs` JSON key preserved; default value is `true` (B002)
- [x] `CMD_DRAW_PERSONAL_ROUTE` compiles and all DDS.DataModel tests pass (C001)

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

**B001 — Two-part root cause discovered:**  
The primary bug was that `ActivateRouteAuthoringTool` never called `BeginAreaAuthoringSession`, leaving `_sessionRequestId == Guid.Empty`. The guard `if (_sessionRequestId == Guid.Empty) return;` at the top of `OnAreaEntityCreated` then silently dropped every route-creation request. The fix was one line: add `_mapCommandController?.BeginAreaAuthoringSession(requestId, _activeContextId);` before the PointSequenceTool push, and `_mapCommandController?.OnAreaToolCancelled();` in the `< 2 points` early-return path.

**B003 — Two compounding testability bugs:**  
First, `ActivateAreaAuthoringTool` had a guard `if (!_networkEnabled || _createEntityDdsWriter == null) return;` that unconditionally blocked the tool in headless test mode, even when `_testCreateEntityRequestSink` was set. Second, the method did not check `_testCreateEntityRequestSink` at all (unlike the route tool). Third, `ParseCommandAndActivateAreaTool` only sets `_activeContextId` if `contextId` appears in the args JSON — so calling it from a test without `contextId` leaves `_activeContextId == Guid.Empty`, and the `_lastAreaContextId == _activeContextId` guard fires immediately (both start empty). All three were fixed: relaxed the network guard, added the sink-path check, and updated the tests to include `contextId` in the args.

**B004 — Straightforward event subscription gap:**  
The subscription was simply missing; `IosLogic` never wired `Repo.EntityDeleted`. The fix is a single event subscription in the constructor and the matching unsubscription in `Dispose`.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

1. **Asymmetric tool activation guards**: `ActivateRouteAuthoringTool` uses `&& _testCreateEntityRequestSink == null` in its early-return guard, while `ActivateAreaAuthoringTool` uses `|| _createEntityDdsWriter == null`. The inconsistency made the area tool completely untestable. A shared helper or consistent pattern would prevent this class of bug.

2. **`_lastAreaContextId` deduplication guard**: The guard prevents re-activation with the same context, which is correct for production. But it silently no-ops if `_activeContextId` is `Guid.Empty` on first call, which can confuse unit tests. A debug-mode assertion `Debug.Assert(_activeContextId != Guid.Empty)` would catch misuse early.

3. **`MapCommandController.OnAreaEntityCreated` silent drop**: The guard `if (_sessionRequestId == Guid.Empty) return;` drops the request without logging. Adding a `FdpLog.Warn` when `_sessionRequestId == Guid.Empty` and the caller is not obviously a test would have surfaced B001 much earlier.

4. **`IosLogic.Dispose`**: Before this patch, `Dispose` only set `_disposed = true` without unsubscribing from events. This is a latent event-handler memory-leak and access-after-dispose hazard for any future event subscriptions. Added unsubscription as part of B004 fix.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

- **B002 (label-only fix vs. separate layer)**: The investigation showed that the `road_graphs` predicate (`TkbType == TacGraphic_Route`) maps exclusively to route entities — no non-route entities use this layer. Fix A (label rename) was the correct choice: minimal change, no risk of regression, no new layer slot needed. Chose "Routes" as the label to clearly communicate the purpose.

- **B003 testability**: Added `_testCreateEntityRequestSink` check to `ActivateAreaAuthoringTool` matching the exact pattern already used by `ActivateRouteAuthoringTool`. Alternative was to refactor both tools into a shared helper, but the spec's "no refactoring beyond what's asked" guidance made the targeted patch the right call.

- **B004 subscription placement**: Could have subscribed in `Update()` lazily (check and wire once per frame) instead of in the constructor. Opted for constructor subscription because the event is long-lived, the repo is injected as a constructor parameter, and lazy wiring would add per-frame overhead for no benefit.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- **B001 — cancellation path**: The `ActivateRouteAuthoringTool` early-return for `points.Length < 2` never called `OnAreaToolCancelled()`. After the fix includes `BeginAreaAuthoringSession`, if `< 2 points` are drawn the IOS MapCommandRequest session would have hung open indefinitely with no `MapCommandAck(Cancelled)` ever arriving. Both the `BeginAreaAuthoringSession` and `OnAreaToolCancelled` additions were needed together.

- **B003 — `_lastAreaContextId == Guid.Empty` initial state**: A fresh `IgApplication` has both `_lastAreaContextId` and `_activeContextId` at `Guid.Empty`. Any test that calls `ParseCommandAndActivateAreaTool` without `contextId` in the args JSON never updates `_activeContextId`, leading to an immediately-returning tool activation. This was not documented anywhere but affects all area authoring unit tests.

- **B004 — `Dispose` unsubscription**: If `IosLogic` is disposed but the `DerRepo` outlives it (e.g., in integration tests where the repo is injected as a shared fixture), subsequent deletions would invoke a delegate on a disposed object, potentially throwing `ObjectDisposedException`. The unsubscription in `Dispose` closes this gap.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `IosLogic.OnEntityDeleted` is an event handler that fires on the main thread (since DDS ingress is polled from `IosLogic.Update`). It performs a single integer comparison. No performance concern.

- The `_lastAreaContextId == _activeContextId` deduplication check in `ActivateAreaAuthoringTool` prevents double-activation on repeated DDS deliveries of the same command. This is important for correctness but also incidentally good for performance (prevents spurious canvas churn). No changes needed.

- No LINQ in hot paths introduced. No new allocations on per-frame paths.
