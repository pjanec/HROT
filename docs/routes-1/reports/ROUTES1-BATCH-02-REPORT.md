# ROUTES1-BATCH-02 Report

**Batch:** ROUTES1-BATCH-02  
**Developer:** GitHub Copilot  
**Date:** 2025-07-18  
**Status:** Complete

---

## 📊 Task Completion

| Task ID      | Status | Notes |
|--------------|--------|-------|
| CT-0 | ✅ Done | `EDescriptorType` enum: all six members now carry explicit int values; `dtMapRoute = 5` preserved permanently |
| CT-1 | ✅ Done | `RoutePlan.Waypoints` made private; `Mutate(Action<List<RouteWaypoint>>)` auto-increments `Version`; all callers updated |
| ROUTES1-T006 | ✅ Done | `RouteTrajectorySyncSystem` in `simGroup` (`BeforeSync` phase); version-delta tracking; ≥2 waypoint guard |
| ROUTES1-T007 | ✅ Done | `ParseCommandAndActivateAreaTool` branches on `TkbType == TacGraphic_Route`; `ActivateRouteAuthoringTool` emits 3-descriptor `CreateEntityRequest` |
| ROUTES1-T008 | ✅ Done | `PersonalRouteAuthoringSystem` in `inputGroup` (`Input` phase); direct `World.CreateEntity()` (not ECB) for child entity handle |
| ROUTES1-T009 | ✅ Done | `OnCanvasWorldClick` shift+right-click branch; iterates `SelectionState`; publishes `CmdAppendPersonalWaypoint` per selected vehicle |

---

## 🧪 Testing Results

**New tests added this batch:** 25  
**Total solution tests after batch:**  
- `Bagira.Map.Common.Tests`: 84 Passed  
- `Bagira.SimHost.Tests`: 298 Passed  
- `Bagira.IG.Tests`: 351 Passed  

**Test files added:**
- `Bagira.SimHost.Tests/RouteTrajectorySyncSystemTests.cs` — 7 tests (T006)
- `Bagira.SimHost.Tests/PersonalRouteAuthoringSystemTests.cs` — 8 tests (T008)
- `Bagira.IG.Tests/RouteAuthoringTests.cs` — 8 tests (T007)
- `Bagira.IG.Tests/ShiftRightClickTests.cs` — 8 tests (T009)

**Key test scenarios verified:**
- [x] CT-1: `RoutePlan.Mutate()` increments `Version`; direct `Waypoints` assignment no longer compiles
- [x] T006: Route entity with ≥2 waypoints registers a trajectory; `TrajectoryId` in `RouteTrajectoryCache` updates on new version
- [x] T006: Second tick with same `Version` does NOT re-register (no-op dirty check)
- [x] T006: Destroyed entity causes trajectory removal from pool
- [x] T006: 0- and 1-waypoint routes do not throw and produce `TrajectoryId == 0`
- [x] T007: `ParseCommand` with `tkbType == 8802` pushes `PointSequenceTool`
- [x] T007: Finishing the tool with 3 points emits exactly one `CreateEntityRequest`
- [x] T007: Emitted request has 3 descriptors: `dtEntityMaster`, `dtGeoSpatial`, `dtMapRoute`
- [x] T007: `EntityMaster.TkbType == TkbEntityTypes.TacGraphic_Route`
- [x] T007: `MapRoute.Points.Count` matches input point count; tool pops after finish
- [x] T008: First personal waypoint spawns child route entity with `PartMetadata`, `TkbIdentity`, `SimTransform`
- [x] T008: Second personal waypoint appends to existing route and increments `Version`
- [x] T008: Dead/null vehicle entity events are silently skipped
- [x] T009: Shift+right-click on 1 selected vehicle emits 1 `CmdAppendPersonalWaypoint`
- [x] T009: Two selected vehicles emit two independent events
- [x] T009: Plain right-click emits zero waypoint events
- [x] T009: `WorldPosition.X/Z` match click position; `WorldPosition.Y` comes from vehicle `SimTransform.Position.Y`
- [x] T009: Vehicle without `SimTransform` falls back to altitude 0

---

## 📝 Developer Insights

### Q1: Did you find issues migrating `RoutePlan` mutations to explicit calls during CT-1?

Yes. Two call sites required non-trivial changes:

1. **`TacGraphicRouteBlueprintTests.cs`**: A test was assigning `plan.Version = 99` directly to pre-set the version state. After CT-1 made `Version` read-only, this test was fixed to use `plan.Mutate(wps => wps.Add(...))` and assert the auto-incremented value instead. The old approach was testing an implementation detail; the new approach is semantically correct.

2. **Translators quoting `RoutePlan.Waypoints` directly**: `MapRouteEgressTranslator` accessed `plan.Waypoints` as a stored property reference for enumerating. After CT-1, `Waypoints` is `IReadOnlyList<RouteWaypoint>` — the translator reads it fine for enumeration and comparison, requiring no change. The key invariant is that *write* paths were the only ones affected, and there were no write paths in the translator (it only reads).

### Q2: What challenges occurred coordinating `RouteTrajectorySyncSystem` phases natively?

The main coordination challenge was phase ordering: `RouteTrajectorySyncSystem` must fire *after* ingestion (so `RoutePlan` is up to date from DDS) but *before* kinematic systems start reading trajectory IDs. The `SystemPhase.BeforeSync` tag resolves this cleanly — it sits exactly in the window between ingress processing and kinematic update.

A secondary concern was the `TrajectoryPoolManager` contract requiring ≥2 waypoints. Routes in-progress (1 waypoint deposited so far by a shift-click) should not crash the system. The solution is an explicit guard: `if (waypoints.Count < 2) return` before calling `RegisterTrajectory`. Such routes produce no pool entry until they mature, and `RouteTrajectoryCache.TrajectoryId` stays 0 in the meantime — a sentinel the kinematic layer already handles via `NavState.TrajectoryId == 0`.

The lifecycle removal path (entity destroyed) needed a `Dictionary<Entity, int>` to track which pool entries were issued per entity. Without it, `RemoveTrajectory` could not be called correctly on entity destruction. The dictionary is O(1) lookup and bounded by the number of live route entities, making it negligible in practice.

### Q3: What design safety guards did you place inside the Shift+Right-Click hooks?

Several defensive choices were made inside the `OnCanvasWorldClick` shift-branch:

1. **Explicit `IsSelected || IsPrimarySelection` gate:** The query fetches all entities with `SelectionState`, then filters to those with either flag set. This guards against accidentally publishing events for entities whose `SelectionState` component was added but never set to selected.

2. **Altitude fallback to 0:** If the selected vehicle has no `SimTransform` (e.g. a freshly spawned but not yet positioned entity), the altitude defaults to `0f` rather than throwing a missing-component exception. The SimHost system can handle `WorldPosition.Y == 0` gracefully.

3. **`return` after the shift-branch:** The function returns immediately after processing the shift+right-click, preventing the context menu from opening on what is clearly an operator authoring gesture.

4. **Entity alive check in `PersonalRouteAuthoringSystem`:** The consuming system (T008) additionally checks `!World.IsAlive(vehicle)` before acting on each event, guarding against events published for entities destroyed in the same frame.

### Q4: Did you observe any bottlenecks regarding mapping multi-entity selections?

For small selection counts (≤100 vehicles, typical IG scenario), the approach is O(n) ECS query iteration and O(n) event publishes with no allocations — entirely acceptable.

The only note is that `World.Query().With<SelectionState>().Build()` constructs a new query object each invocation. In the existing codebase, similar patterns cache the query; if the shift-click becomes hot code at high entity counts, caching the query would be the straightforward fix.

For the event bus, each publish to an unmanaged blittable buffer is a memcpy of ~24 bytes (`Entity` + `Vector3`), so 100 selected vehicles is 2.4 KB of event data — negligible.

### Q5: What edge cases popped up simulating trajectory buffer lifecycle deletions?

Two edge cases surfaced:

1. **ECB placeholder entity references not remapped:** `PersonalRouteAuthoringSystem` originally used an `EntityCommandBuffer` to create the child route entity and then stored the EC handle as `PersonalRouteRef.RouteEntity`. During `ecb.Playback()`, ECB remaps entity IDs when used as component *identities* but does NOT remap entity values embedded *inside* struct component data (i.e., a field of type `Entity` within another component). The result was `PersonalRouteRef.RouteEntity` holding an unmapped negative placeholder ID, causing every subsequent lookup to return `Entity.Null`. The fix was to use direct `World.CreateEntity()` + `World.AddComponent()`, returning the real entity handle immediately before the component is written.

2. **FdpEventBus double-buffering in tests:** Events published via `Bus.Publish()` land in the *pending* buffer and are only visible via `Bus.Consume<T>()` after `Bus.SwapBuffers()` promotes them. The first iteration of `PersonalRouteAuthoringSystemTests` called `_system.Run()` and then tried to assert component state, but the events weren't visible yet. All test setups were corrected to call `_repo.Bus.SwapBuffers()` *before* `_system.Run()` — matching the frame lifecycle where SwapBuffers happens at the start of each simulation tick. Similarly, in the IG `ShiftRightClickTests`, calling `TestHook_SimulateShiftRightClick()` publishes to the pending buffer and `Bus.SwapBuffers()` inside `ConsumeWaypointEvents()` makes them consumable.

---

## ⚠️ Bug-Fix Applied: `IgMissionHolder` ComponentId Collision

During IG test execution, a `ComponentId` collision was detected at runtime: both `GlobalComponentIds.PersonalRouteRef = 221` (added in ROUTES1-BATCH-01) and `IgComponentIds.IgMissionHolder = 221` (a pre-existing IG-local constant) claimed the same ID. The collision was resolved by reassigning `IgComponentIds.IgMissionHolder` to `123`, which sits in the IG-reserved block (IDs 123–139), and registering it in `GlobalComponentIds.cs` with a summary comment.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] `RouteTrajectorySyncSystem` does not yet notify the kinematic layer to re-plan when a personal route's trajectory ID changes — `CmdFollowTrajectory` dispatch is stubbed in `PersonalRouteAuthoringSystem` (Phase 1 deferred command) but the downstream behavior needs end-to-end testing once T010/T011 rendering is in place
- [ ] T010 `RouteRenderLayer` and T011 `SimHostTrajectoryLayer` extension are not yet implemented (Phase 6 scope)
- [ ] `ActivateRouteAuthoringTool` uses `_geoTransform` which may be `null` in edge cases where no geographic origin is configured — currently falls back to raw XY values, which are probably incorrect as geodetic coordinates for maps far from the origin
