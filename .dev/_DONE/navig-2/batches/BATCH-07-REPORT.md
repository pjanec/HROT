# BATCH-07 Report: Phase 4 — New Executors, PathDetails System, Catalog Entries

**Batch:** BATCH-07
**Phase tasks:** NAV-P4-T1, NAV-P4-T2, NAV-P4-T3, NAV-P4-T4, NAV-P9-T5, NAV-P9-T6
**Status:** COMPLETE

---

## Summary

BATCH-07 completes Phase 4 of the navigation subsystem: four new CQRS locomotion executors
(PlanRoute, FollowPath, FetchPathDetails, ReleasePath), the NavigationPathDetailsUpdateSystem
for Brain-side path cache ingestion, 7 new event structs (EventId 2036-2042), catalog
registration for navigation events in BuiltInEngineEventCatalog, and retirement of the
obsolete FollowRoadGraph executor.

---

## Files Deleted

| File | Reason |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/FollowRoadGraphExecutor.cs` | ActionIdFollowRoadGraph=4 is [Obsolete]; executor removed |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/ExecutorTests/FollowRoadGraphExecutorTests.cs` | Tests for removed executor retired (4 tests removed) |

---

## Files Created

### Fdp.Toolkits (production)

| File | Description |
|------|-------------|
| `Navigation/Executors/PlanRouteExecutor.cs` | Issues pathfinding request without movement. Polls NavigationStatus for PathFound->Success, NoPath->Failure. |
| `Navigation/Executors/FollowPathExecutor.cs` | Follows a pre-loaded route by RouteHandle. Polls for Arrived->Success, FailedInvalidHandle->Failure. |
| `Navigation/Executors/FetchPathDetailsExecutor.cs` | Blocking or non-blocking path details fetch. Takes IPathRegistry in constructor; polls IsCached in blocking mode; immediate Success in non-blocking mode. |
| `Navigation/Executors/ReleasePathExecutor.cs` | Fire-and-forget path release. Sets Success immediately in OnEnter. |
| `Navigation/Systems/NavigationPathDetailsUpdateSystem.cs` | Reads NavigationPathDetailsResponseEvent; populates BrainPathRegistry via TryIngestResponse; updates NavigationPathDetailsBuffer; emits NavigationPathDetailsArrivedEvent. |

### Fdp.Toolkits.Tests

| File | Description |
|------|-------------|
| `Navigation/ExecutorTests/PlanRouteExecutorTests.cs` | 3 tests: WritesIntent, PathFound->Success, NoPath->Failure |
| `Navigation/ExecutorTests/FollowPathExecutorTests.cs` | 3 tests: WritesIntentWithHandle, Arrived->Success, FailedInvalidHandle->Failure |
| `Navigation/ExecutorTests/FetchPathDetailsExecutorTests.cs` | 2 tests: Blocking_PollsRegistryUntilCached, NonBlocking_ReturnsSuccessImmediately |
| `Navigation/ExecutorTests/ReleasePathExecutorTests.cs` | 1 test: WritesIntentAndSucceeds |
| `Navigation/NavigationPathDetailsUpdateSystemTests.cs` | 5 tests: PopulatesBrainRegistry, FiresArrivedEvent, IsAutoRefresh flag, ReplanCountUpdated, LruCapEviction |

---

## Files Modified

### Fdp.Toolkits (production)

| File | Change |
|------|--------|
| `Navigation/PathfindingEvents.cs` | Added 7 new event structs: MoveCompletedEvent(2036), MoveBlockedEvent(2037), WaypointReachedEvent(2038), PathReplannedEvent(2039), OffMeshTraversalEndedEvent(2040), NavigationPathDetailsResponseEvent(2041), NavigationPathDetailsArrivedEvent(2042) |
| `Navigation/Executors/MoveToExecutor.cs` | Restructured Execute() switch: merged failure cases, added MoveCompletedEvent emission on Arrived and Failure, added explicit PathFound/InProgress keep-Running cases |

### Fdp.Toolkits.Tests

| File | Change |
|------|--------|
| `Navigation/NavigationTestWorldFactory.cs` | Added RegisterEvent<MoveCompletedEvent>(), RegisterEvent<NavigationPathDetailsResponseEvent>(), RegisterEvent<NavigationPathDetailsArrivedEvent>(); updated comment removing FollowRoadGraphExecutor reference |
| `Navigation/ExecutorTests/MoveToExecutorTests.cs` | Added 4 new tests: DefaultRouteHandle_IsZero, ExplicitRouteHandle_PassedThrough, Arrived_EmitsMoveCompletedEvent, BTreeInstanceIdBump_AbandonsCurrentMove |

### Hrot.Blueprints.Compiler

| File | Change |
|------|--------|
| `Compiler/Catalogs/BuiltInEngineEventCatalog.cs` | Added NavFqn file-scoped helper; added 8 navigation event catalog entries (MoveStartedEvent, MoveCompletedEvent, PathReplannedEvent, OffMeshTraversalStartedEvent, OffMeshTraversalEndedEvent, MoveBlockedEvent, WaypointReachedEvent, NavigationPathDetailsArrivedEvent) |

---

## Test Results

| Metric | Value |
|--------|-------|
| Baseline (after BATCH-06) | 162 |
| Removed (FollowRoadGraph retired) | -4 |
| Added (new tests) | +18 |
| **Final** | **176** |
| Failed | 0 |
| Build errors | 0 |

All 176 navigation tests pass. FDP.sln and Hrot.Blueprints.Compiler both build with 0 errors.
