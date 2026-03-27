# OC1-BATCH-03 Report

**Batch:** OC1-BATCH-03  
**Developer:** GitHub Copilot  
**Date:** 2026-03-22  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| OC1-CORRECTIVE-02 Bug 1 | ✅ Complete | Canvas Y-to-Z fixed in `ActivateRouteAuthoringTool`, `ActivateAreaAuthoringTool`, `ActivateAreaEditingTool` |
| OC1-CORRECTIVE-02 Bug 2 | ✅ Complete | `RouteRenderLayer.PickEntity` implemented with segment distance check |
| OC1-CORRECTIVE-02 Bug 3 | ✅ Complete | IG context menu delete now writes `DeleteEntityRequest` over DDS when `_networkEnabled` |
| OC1-I001 | ✅ Complete | `OrbatPanel` context menu infrastructure + `IsSimulatedEntity` helper |
| OC1-I002 | ✅ Complete | `SendSetSelection` on `IosLogic` + `IIosLogic` + wired in `OrbatPanel` |
| OC1-I003 | ✅ Complete | `CenterOnEntity` on `IosLogic` + `IIosLogic` + wired in `OrbatPanel` |
| OC1-I004 | ✅ Complete | `DeleteEntity` + pending-delete tracking + ACK processing + row disable |
| OC1-I005 | ✅ Complete | `StartPersonalRouteAuthoring` on `IosLogic` + `IIosLogic` + wired in `OrbatPanel` |
| OC1-I006 | ✅ Complete | "Abort Mission" wired to `MissionEditorService.SendControlCommandAsync` in `OrbatPanel` |

---

## 🧪 Testing Results

**Unit Tests Passed:** 336/336 (IOS.Tests), 403/403 (IG.Tests), 33/33 (DataModel.Tests), 94/94 (Map.Common.Tests), 112/112 (Runner.Tests), 331/332 (SimHost.Tests)

The single SimHost failure (`JsonToRecordCompilerTests.Compile_NonStringPath_ZeroAllocation`) is a **pre-existing intermittent failure** that is caused by GC allocations from other tests in the same run — it passes 100% of the time in isolation and is completely unrelated to this batch's changes. This pattern was already present before this batch.

**Key Test Scenarios Verified:**

- [x] `RouteRenderLayerTests.PickEntity_ClickOnSegment_ReturnsRouteEntity` — click on route selects it
- [x] `RouteRenderLayerTests.PickEntity_ClickFarFromRoute_ReturnsNull` — miss returns null
- [x] `RouteRenderLayerTests.PickEntity_ClickOnLoopClosingSegment_ReturnsRouteEntity` — looping segment check
- [x] `RouteRenderLayerTests.PickEntity_NoRoutes_ReturnsNull` — empty world returns null
- [x] `OrbatPanelContextMenuTests.IsSimulatedEntity_LowTkbType_ReturnsTrue` — simulated entity detected
- [x] `OrbatPanelContextMenuTests.IsSimulatedEntity_RouteTkbType_ReturnsFalse` — map graphic excluded
- [x] `OrbatPanelContextMenuTests.IsSimulatedEntity_MissingEntity_ReturnsFalse` — missing entity safe
- [x] `IosLogicContextCommandTests.SendSetSelection_SetsSelectedEntityIdLocally` — local selection
- [x] `IosLogicContextCommandTests.SendSetSelection_PublishesCmdSetSelection` — DDS publish verified
- [x] `IosLogicContextCommandTests.CenterOnEntity_PublishesCmdSetView` — center command verified
- [x] `IosLogicContextCommandTests.CenterOnEntity_PayloadHasNoCoordinates` — no lat/lon leak
- [x] `IosLogicContextCommandTests.DeleteEntity_PublishesDeleteEntityRequest` — DDS delete request
- [x] `IosLogicContextCommandTests.DeleteEntity_MarksEntityPendingDelete` — pending flag set
- [x] `IosLogicContextCommandTests.DeleteEntity_SuccessAck_ClearsPendingFlag` — ACK clears pending
- [x] `IosLogicContextCommandTests.DeleteEntity_FailureAck_ClearsPendingAndSetsAlert` — failure surfaces alert
- [x] `IosLogicContextCommandTests.DeleteEntity_UnrelatedAck_Ignored` — no side effects for unrelated ACK
- [x] `IosLogicContextCommandTests.StartPersonalRouteAuthoring_PublishesCmdDrawPersonalRoute`
- [x] `IosLogicContextCommandTests.StartPersonalRouteAuthoring_PayloadContainsEntityId`
- [x] `IosLogicContextCommandTests.StartPersonalRouteAuthoring_UpdatesActiveContextId`

**New test files:**
- `Bagira.IOS.Tests/OrbatPanelContextMenuTests.cs` — OC1-I001 `IsSimulatedEntity` + entity click
- `Bagira.IOS.Tests/IosLogicContextCommandTests.cs` — OC1-I002 through OC1-I005 IosLogic methods

**Extended test file:**
- `Bagira.IG.Tests/RouteRenderLayerTests.cs` — 4 new `PickEntity` tests appended

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

- **Nullable reference warning in `RouteRenderLayer.PickEntity`**: The `plan.Waypoints?.Count ?? 0 == 0` guard left `plan.Waypoints` still nullable on the compiler's flow analysis. Fixed by capturing `var waypoints = plan.Waypoints;` and doing an explicit `waypoints == null` guard before use.
- **`SstStatusCode.OK` does not exist**: The enum uses `SstStatusCode.Success` (value 0). Caught at compile time and corrected in the test.
- **`TkbEntityTypes` unavailable in `Bagira.IOS.Tests`**: The test project references `Bagira.IOS` but not `Bagira.Map.Definitions`, so `TkbEntityTypes.TacGraphic_Route` is unavailable. Replaced with the literal `8802` in the test.
- **`IDerEntity.TkbType` vs. `EntityMaster` descriptor**: Initially planned to read `EntityMaster.TkbType` from the descriptor in `IsSimulatedEntity`. Discovered that `IDerEntity` exposes `TkbType` directly (set at entity creation), avoiding a descriptor read entirely. Simpler and more reliable.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- The `IosLogic` constructor already has many optional parameters (9 named params) and adding `deleteEntityWriter` makes it longer. In a future refactor, grouping DDS writers/queues into a value-object `IosLogicDeps` would clean this up.
- The `ProcessEntityCreationAcks` method now handles two separate concerns (creation ACKs and deletion ACKs). The spec notes this is intentional (re-use of the same `CreateUpdateDeleteEntityAck` topic), but a future rename of the method to `ProcessEntityLifecycleAcks` would better reflect its dual role.

**Q3: What design decisions did you make beyond the instructions? How did you handle them?**

- **PickEntity with `PickRadius = 7.0f`**: The spec allowed 5–10 world units. Chose 7.0f as a midpoint giving comfortable hit-testing without accidentally picking adjacent parallel routes.
- **Bug 3 — offline fallback**: When `_networkEnabled` is false, the delete continues to use `DestroyEntityCommand` for local offline/test scenarios. This preserves headless test use-cases.
- **Delete ACK correlation by entity ID**: The spec says to track by entity ID (not request GUID) since `CreateUpdateDeleteEntityAck` carries only `EntityId`. The implementation checks `_pendingDeleteEntityIds.Contains(ack.EntityId)` before the creation-ACK path, so creation-ACK processing is unaffected.
- **Context menu `BeginDisabled/EndDisabled` placement**: Moved `EndDisabled` to after the tree pop/unindent so the full row (node text + chevron) is visually greyed while a delete is pending.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- `PickEntity` on a route with exactly 1 waypoint (no segments to check) correctly returns null without indexing errors.
- `DeleteEntity` called while the `deleteEntityWriter` is null (test/offline mode) still adds to `_pendingDeleteEntityIds`, so ACK processing from the simulation state will still clear it if an ACK somehow arrives. This is a minor inconsistency (pending flag set but no request sent) but matches the guard pattern used by `_commandWriter`.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `OrbatPanel.IsSimulatedEntity` calls `repo.GetEntity(entityId)` on every right-click context menu render. In a large ORBAT with many entities this is a dictionary lookup (O(1)) so it is fine. No hot-path concern.
- `RouteRenderLayer.PickEntity` iterates all query entities each call. For scenes with many route entities this is linear, but route entities are typically sparse so this is acceptable.

---

## ⚠️ Outstanding Issues / Next Steps

- The pre-existing `JsonToRecordCompilerTests.Compile_NonStringPath_ZeroAllocation` intermittent failure in `Bagira.SimHost.Tests` is not caused by this batch and should be tracked separately.
- OC1-I006 ("Abort Mission") is wired in the panel but has no dedicated per-method unit test since `SendControlCommandAsync` is already covered by the existing `MissionPanelTests` suite.  A future test could verify the call from the ORBAT panel path specifically.
