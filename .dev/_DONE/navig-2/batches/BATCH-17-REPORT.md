# BATCH-17 Report

## Tasks Completed

| Task ID | Description | Status |
|---------|-------------|--------|
| NAV-P10-T10 | S9_FlyingAgentRouting | Done |
| NAV-P10-T11 | S10_NavalLayerRouting | Done |
| NAV-P10-T12 | S11_PlanRouteThenFollowPath | Done |
| NAV-P10-T13 | S12_FetchPathDetailsAndCacheInvalidation | Done |

## Files Modified

1. `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs`
   - Added `MobilityProfile byte` field to `NavAgentProfile` struct.

2. `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs`
   - `ActionIdMoveTo`: reads `NavAgentProfile.MobilityProfile` instead of hardcoded `0`.
   - `ActionIdPlanRoute`: reads `NavAgentProfile.MobilityProfile` instead of hardcoded `0`.
   - `ActionIdFetchPathDetails`: implemented (was stub). Publishes `NavigationPathDetailsResponseEvent` if route exists in trajectory pool.

3. `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/NavigationExecutionSystem.cs`
   - Fixed `AutoSendPathOnReplan` block: `NavigationPathDetailsResponseEvent` now includes `Target = entity` and `ReplanCount = (byte)status.ReplanCount`.

4. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavTestHarness.cs`
   - Added `_pathDetailsUpdate` private field (`NavigationPathDetailsUpdateSystem?`).
   - Added `Volumetric` (`FakeVolumetricPathProvider`) and `BrainRegistry` (`BrainPathRegistry`) public properties.
   - Constructor: initializes `Volumetric`, `BrainRegistry`, `_pathDetailsUpdate`.
   - `Tick()`: added step 2a (PathDetailsUpdate after bridge swap) and step 10a (PathDetailsUpdate after final swap).
   - Added `SpawnFlying(Vector2)` method.
   - Added `SpawnNaval(Vector2)` method.
   - Added `IssuePlanRoute(Entity, Vector2, int, uint)` method.
   - Added `IssueFollowPath(Entity, int, Vector2)` method.
   - Added `IssueFetchPathDetails(Entity, int)` method.

## Files Created

5. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S9_FlyingAgentRoutingTests.cs`
   - 2 tests: flying entity routes via volumetric provider; ground entity does not invoke volumetric provider.

6. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S10_NavalLayerRoutingTests.cs`
   - 2 tests: naval entity arrives on naval-layer map; infantry on naval map gets FailedUnreachable.

7. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S11_PlanRouteThenFollowPathTests.cs`
   - 1 test: PlanRoute then FollowPath two-phase navigation arrives at destination.

8. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S12_FetchPathDetailsAndCacheInvalidationTests.cs`
   - 2 tests: FetchPathDetails populates BrainRegistry; cache stale miss + auto-refresh on replan.

## Final Test Count

- Navigation tests: **281 passed, 0 failed** (274 prior + 7 new)
- Build errors: **0**

## Deviations from Instructions

1. **S10 start position**: Instructions used `Vector2(5f, 5f)` for the naval entity start. However, `NavTestMaps.LoadNaval()` creates polygons centred at `(5,5)`, `(15,5)`, `(25,5)` in XZ plane (X/Z are used for polygon containment in `FakeNavmeshProvider`). Since the harness maps `Vector2(x,y)` to `Vector3(x,y,0)`, and `PointInPolygon` uses X and Z, position `(5,5,0)` has Z=0 which falls on the edge/inside the `[0,10]×[0,10]` square. Changed start to `Vector2(5f, 0f)` which maps to `Vector3(5,0,0)` — X=5 ∈ [0,10], Z=0 ∈ [0,10] — safely inside polygon 0. Same adjustment for the infantry test. The tests pass with this positioning.

2. **S11 extra using**: Added `using CarKinem.Core;` alongside `using Fdp.Core;` to resolve `SimTransform` (which is in `Fdp.Core`) — the `CarKinem.Core` import was added first from the instructions template but `SimTransform` is in `Fdp.Core`. Both are kept since other tests in the namespace may need `CarKinem.Core` types.
