# ROUTES1-BATCH-04 Report

**Batch:** ROUTES1-BATCH-04-DEBT-BURNDOWN
**Developer:** GitHub Copilot
**Date:** 2026-03-21
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| CT-1 (Safety Guards) | ✅ Complete | `World.IsAlive` guard + `?.Count ?? 0` null-safe access |
| CT-2 (ImGui UX Tweaks) | ✅ Complete | `_wasRouteToolActive` focus guard + `_lastWpIndex` buffer cache |
| CT-3 (Query Caching) | ✅ Complete | `RouteContextSystem` and `SimHostTrajectoryLayer` queries cached in `OnCreate`/constructor |

---

## 🧪 Testing Results

**Unit Tests Passed (Hrot.IG.Tests):** 378 / 378  
**Unit Tests Passed (Hrot.SimHost.Tests):** 313 / 313  
**Unit Tests Passed (Hrot.ClusterRunner.Tests):** 112 / 112  
**Unit Tests Passed (Hrot.Map.Common.Tests):** 86 / 86  
**Unit Tests Passed (Hrot.NED.Tests):** 23 / 23

**New tests added: 9**

**Key Test Scenarios Verified:**
- [x] `CommitHandler_EntityDestroyedBeforeCommit_DropsUpdateSilently` — entity destroyed mid-edit → `World.IsAlive` guard → silent return, no crash, no network publish
- [x] `Draw_SingleWaypointRoute_ZeroLinesOneCircle` — 1-waypoint plan correctly renders zero segments (n-1=0) and one vertex circle; exercises `?.Count ?? 0` path
- [x] `WaypointEditorPanelTests.InitialState_LastWpIndexMinusOne_WasRouteToolActiveFalse` — sentinel values correct after construction
- [x] `WaypointEditorPanelTests.JsonBuffer_NotUpdatedWhenWaypointIndexUnchanged_SameReference` — string reference identity preserved across consecutive draws of the same selection (CT-2 allocation check)
- [x] `WaypointEditorPanelTests.JsonBuffer_UpdatedWhenWaypointIndexChanges_ReflectsNewJson` — buffer correctly refreshes when index changes
- [x] `WaypointEditorPanelTests.WasRouteToolActive_TransitionsToFalse_WhenToolDeactivated` — `_lastWpIndex` resets to -1 and flag clears on deactivation
- [x] `RouteContextSystemTests.OnUpdate_SharedRouteFallback_CachedQueryWritesDangerLevelToBlackboard` — `_routeQuery` cached field correctly finds shared routes via `RouteTrajectoryCache`
- [x] `RouteContextSystemTests.OnUpdate_MultipleConsecutiveRuns_CachedQueriesRetainCorrectBehavior` — three consecutive `Run()` calls with cached queries all produce correct results
- [x] `SimHostTrajectoryLayerTests.Draw_MultipleConsecutiveDraws_SharedRoute_StableDrawCounts` — cached `_routeQuery` produces identical draw counts across 3 frames
- [x] `SimHostTrajectoryLayerTests.Draw_MultipleConsecutiveDraws_PersonalRoute_StableDrawCounts` — personal-route path unaffected by cached query field

---

## 📝 Developer Insights

**Q1: What behaviour did you observe when the editor commit triggered over deleted elements?**

When `World.IsAlive(committedEntity)` was absent, the commit lambda proceeded to call `view2.GetManagedComponentRO<RoutePlan>(committedEntity)` on a dead entity. The ECS kernel throws an exception (entity not found in component storage) when accessing components on destroyed entities. With the guard in place, the commit handler now checks liveness first and returns immediately — the entire update pipeline including the DDS `SendUpdateDescriptor` is skipped. No exception, no misleading network traffic.

**Q2: What memory layout differences occurred post `_lastWpIndex` caching?**

Before the fix, `_jsonBuffer = wp.ExtensionJson ?? string.Empty` created a new string object every frame even when `ExtensionJson` was the same string (since `??` forces a read of `wp.ExtensionJson` and string concatenation/assignment). The `_lastWpIndex` cache intercepts this: the string assignment only runs on the first frame for a given waypoint index. For all subsequent frames with the same selection, the `ReferenceEquals` assertion in the test confirms the same string object is retained — zero allocations for the JSON path in steady state. The test `JsonBuffer_NotUpdatedWhenWaypointIndexUnchanged_SameReference` directly validates this via `ReferenceEquals`.

**Q3: Design decisions beyond the instructions**

- **`UpdatePanelState` extraction in `WaypointEditorPanel`**: Rather than scattering `if (!skipImGui)` guards throughout `Draw()`, the non-ImGui state logic was extracted into a dedicated `internal void UpdatePanelState(RouteEditTool? activeRouteTool)` method. `Draw()` calls it after establishing the ImGui window context. This gives headless tests a clean seam to call directly. Tests for `_lastWpIndex` and `_wasRouteToolActive` exercise `UpdatePanelState` directly without needing an ImGui context.
- **Focus yield placement**: `ImGui.SetKeyboardFocusHere(-1)` is called in `Draw()` when `_wasRouteToolActive` has just transitioned to `false` (transition detected before calling `UpdatePanelState`). This ensures the signal fires exactly once at the transition frame, not on every subsequent no-tool frame. The `UpdatePanelState(null)` call then writes the new `_wasRouteToolActive=false` value for future frames.
- **`_routeQuery` field naming in `SimHostTrajectoryLayer`**: Named `_routeQuery` (consistent with `RouteContextSystem`) and marked `readonly` so any future accidental reassignment is a compile-time error.

**Q4: Edge cases discovered beyond the spec**

- The `RouteContextSystem`'s inner `routeQuery` was only built when `plan == null` (shared-route fallback branch). If most vehicles use `PersonalRouteRef`, the shared-route query was rarely allocated, making the per-tick cost variable. Caching it in `OnCreate` makes the cost constant and O(0) per tick regardless of which branch is taken.
- `WaypointEditorPanel._lastWpIndex` must reset to `-1` when the tool deactivates (`UpdatePanelState(null)`), not just left at the last value. If it stayed set, re-activating the same waypoint index next session would skip the `_jsonBuffer` refresh, showing stale JSON from the previous editing session.

**Q5: Performance observations**

- `RouteContextSystem` builds queries inside `OnUpdate` at a tick interval of ~0.5 s. Even at 0.5 s the absolute allocation frequency is low. However, a query `Build()` call typically allocates a small descriptor object on the managed heap — caching eliminates this completely.
- `SimHostTrajectoryLayer._routeQuery` is called every `Draw()` which runs at 60 fps. For the shared-route path, this was a 60 Hz allocation. With the `readonly` cached field the hot path is now entirely allocation-free.

---

## ⚠️ Outstanding Issues / Next Steps

None introduced by this batch. The following pre-existing debt items identified in the Debt Tracker remain deferred per their stated target batches:

- `P3 Architecture BD1-BATCH-02` — `MissionDirectorSystem` publishing delay (BATCH-05)
- `P3 Performance BD1-BATCH-03` — `ComponentReflector` byte cache allocation (BD1-BATCH-04)
- `P3 Physics BATCH-03` — RVO lateral avoidance fixed-magnitude (BATCH-05)
