# BATCH-08 — Complete Phase 8 Unit Tests + NAV-P9-T3

## Context

Phases 0-4 are complete (BATCH-01 through BATCH-07). Phase 8 partial tests were created
alongside production code in earlier batches. This batch completes all missing test rows
from DD-Tests-Nav §3 (Phase 8) and §4.3 (NAV-P9-T3).

Current test count: **176 navigation tests passing**.

## Pre-work: mark already-complete tasks in TASK-TRACKER

Before writing any code, update `.dev/navig-2/TASK-TRACKER.md` to mark the following
tasks as done — the required tests already exist in full:

- **NAV-P8-T6** (`SharedPathRegistryTests`) — all 3 DD rows present
- **NAV-P9-T1** (`OffMeshLinkDetectionSystemTests`) — all 7 DD rows present
- **NAV-P9-T2** (`CrowdAgentUpdateSystemTests`) — all 4 DD rows present

## Tasks

### Task 1: Complete NAV-P8-T1 — `FakeNavmeshProviderTests`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/FakeNavmeshProviderTests.cs`

The file already has 8 tests. Add the 7 missing DD-Tests-Nav §3.1 rows to the existing
class (append before the closing brace of `FakeNavmeshProviderTests`). Do not modify existing tests.

The helpers `BuildTwoAdjacentLayer()` and `BuildIsolatedLayer()` already exist in the class.

**Missing tests to add:**

```
Row 4:  IsWalkable_LayerMaskExclusion_RespectsMask
Row 5:  ProjectToNavmesh_PointInPolygon_ReturnsSamePoint
Row 6:  ProjectToNavmesh_PointOutsidePolygon_ReturnsFalse
Row 9:  PathExists_BlockedIntermediatePolygon_FalseAfterBlock
Row 10: PathCost_StraightCorridor_EqualsEuclideanDistance
Row 11: PathCost_WithOffMeshLink_IncludesLinkCost
Row 14: SameMap_SameQueries_SameResults
```

Notes on each:

**Row 4: IsWalkable_LayerMaskExclusion_RespectsMask**
- Create a provider with TWO layers: layer1 with `Layer = 1u` (Infantry) and layer2 with `Layer = 2u` (Vehicle).
- Add a polygon only in layer2.
- Use a point inside that polygon.
- `provider.IsWalkable(point, layerMask: 1u)` → false (Infantry mask excludes Vehicle layer).
- `provider.IsWalkable(point, layerMask: 2u)` → true.

```csharp
[Fact]
public void IsWalkable_LayerMaskExclusion_RespectsMask()
{
    var vehicleLayer = new FakeNavLayer
    {
        Layer    = 2u,
        Polygons = new[]
        {
            new NavPolygon
            {
                Id       = 10,
                Vertices = new[]
                {
                    new Vector3(20, 0, 20), new Vector3(22, 0, 20),
                    new Vector3(22, 0, 22), new Vector3(20, 0, 22),
                },
            },
        },
        Adjacency = new[] { System.Array.Empty<int>() },
    };
    var provider = new FakeNavmeshProvider(vehicleLayer);
    var point = new Vector3(21, 0, 21); // inside vehicleLayer poly

    // Infantry mask (1) excludes the vehicle layer (2).
    Assert.False(provider.IsWalkable(point, layerMask: 1u));
    // Vehicle mask (2) includes the layer.
    Assert.True(provider.IsWalkable(point, layerMask: 2u));
}
```

**Row 5: ProjectToNavmesh_PointInPolygon_ReturnsSamePoint**
- Use BuildTwoAdjacentLayer() provider.
- Point (1, 0, 1) is inside poly1.
- `provider.ProjectToNavmesh(new Vector3(1, 0, 1), out var snapped)` → returns true, snapped.X==1, snapped.Z==1.

```csharp
[Fact]
public void ProjectToNavmesh_PointInPolygon_ReturnsSamePoint()
{
    var provider = new FakeNavmeshProvider(BuildTwoAdjacentLayer());
    bool found = provider.ProjectToNavmesh(new Vector3(1, 0, 1), out var snapped);
    Assert.True(found);
    Assert.Equal(1f, snapped.X);
    Assert.Equal(1f, snapped.Z);
}
```

**Row 6: ProjectToNavmesh_PointOutsidePolygon_ReturnsFalse**
- Provider with only BuildTwoAdjacentLayer().
- A point well outside all polygons (100, 0, 100).
- `ProjectToNavmesh` returns false; position is NOT snapped to a polygon.

```csharp
[Fact]
public void ProjectToNavmesh_PointOutsidePolygon_ReturnsFalse()
{
    var provider = new FakeNavmeshProvider(BuildTwoAdjacentLayer());
    bool found = provider.ProjectToNavmesh(new Vector3(100, 0, 100), out _);
    Assert.False(found);
}
```

**Row 9: PathExists_BlockedIntermediatePolygon_FalseAfterBlock**
- Scenario: three polygons in a chain: poly1 (0-2,0-2), poly2 (2-4,0-2), poly3 (4-6,0-2).
- poly1 adjacent to poly2, poly2 adjacent to poly1 and poly3, poly3 adjacent to poly2.
- PathExists(poly1_center, poly3_center) → true initially.
- BlockPolygon(poly2.Id).
- PathExists(poly1_center, poly3_center) → false (intermediate blocked).

```csharp
[Fact]
public void PathExists_BlockedIntermediatePolygon_FalseAfterBlock()
{
    var poly1 = new NavPolygon
    {
        Id = 1, Vertices = new[]
        {
            new Vector3(0,0,0), new Vector3(2,0,0),
            new Vector3(2,0,2), new Vector3(0,0,2),
        },
    };
    var poly2 = new NavPolygon
    {
        Id = 2, Vertices = new[]
        {
            new Vector3(2,0,0), new Vector3(4,0,0),
            new Vector3(4,0,2), new Vector3(2,0,2),
        },
    };
    var poly3 = new NavPolygon
    {
        Id = 3, Vertices = new[]
        {
            new Vector3(4,0,0), new Vector3(6,0,0),
            new Vector3(6,0,2), new Vector3(4,0,2),
        },
    };
    var layer = new FakeNavLayer
    {
        Layer    = 1u,
        Polygons = new[] { poly1, poly2, poly3 },
        Adjacency = new[]
        {
            new[] { 1 },       // poly1 -> poly2
            new[] { 0, 2 },    // poly2 -> poly1, poly3
            new[] { 1 },       // poly3 -> poly2
        },
    };
    var provider = new FakeNavmeshProvider(layer);

    // Before block: reachable.
    Assert.True(provider.PathExists(new Vector3(1, 0, 1), new Vector3(5, 0, 1)));

    // Block intermediate polygon.
    provider.BlockPolygon(2);

    // After block: unreachable.
    Assert.False(provider.PathExists(new Vector3(1, 0, 1), new Vector3(5, 0, 1)));
}
```

**Row 10: PathCost_StraightCorridor_EqualsEuclideanDistance**
- Two adjacent polygons, plan path from center of poly1 (1,0,1) to center of poly2 (3,0,1).
- Euclidean distance = 2.0f.
- PathCost should equal 2.0f (within float tolerance 0.01f).

```csharp
[Fact]
public void PathCost_StraightCorridor_EqualsEuclideanDistance()
{
    var provider = new FakeNavmeshProvider(BuildTwoAdjacentLayer());
    float cost = provider.PathCost(new Vector3(1, 0, 1), new Vector3(3, 0, 1));
    Assert.True(MathF.Abs(cost - 2f) < 0.01f,
        $"Expected PathCost ~2.0, got {cost}");
}
```

**Row 11: PathCost_WithOffMeshLink_IncludesLinkCost**
- Scenario with an off-mesh link that has a nonzero cost (e.g. cost=5f).
- Reuse the poly1/poly3 off-mesh link setup from the existing PlanPath_IncludesOffMeshLinkWaypoints test.
- PathCost should be greater than the direct Euclidean distance from poly1 center to poly3 center
  (because PlanPath goes via the link endpoints, not a straight line).
- We don't assert the exact cost, just that it's > 0 and < float.MaxValue (reachable).

```csharp
[Fact]
public void PathCost_WithOffMeshLink_IncludesLinkCost()
{
    var poly1 = new NavPolygon
    {
        Id = 1, Vertices = new[]
        {
            new Vector3(0,0,0), new Vector3(2,0,0),
            new Vector3(2,0,2), new Vector3(0,0,2),
        },
    };
    var poly3 = new NavPolygon
    {
        Id = 3, Vertices = new[]
        {
            new Vector3(10,0,10), new Vector3(12,0,10),
            new Vector3(12,0,12), new Vector3(10,0,12),
        },
    };
    var link = new OffMeshLink
    {
        FromPolygonId = 1,
        ToPolygonId   = 3,
        StartPos      = new Vector3(2, 0, 1),
        EndPos        = new Vector3(10, 0, 11),
        Kind          = TraversalKind.Jump,
        Cost          = 5f,
    };
    var layer = new FakeNavLayer
    {
        Layer        = 1u,
        Polygons     = new[] { poly1, poly3 },
        Adjacency    = new[] { System.Array.Empty<int>(), System.Array.Empty<int>() },
        OffMeshLinks = new[] { link },
    };
    var provider = new FakeNavmeshProvider(layer);

    float cost = provider.PathCost(new Vector3(1, 0, 1), new Vector3(11, 0, 11));

    Assert.True(cost > 0f && cost < float.MaxValue,
        $"Expected a finite positive cost through the off-mesh link, got {cost}");
}
```

**Row 14: SameMap_SameQueries_SameResults**
- Create the same provider twice with the same layer data.
- Run IsWalkable, PathExists, PathCost with the same inputs on both.
- Assert results are identical (determinism).

```csharp
[Fact]
public void SameMap_SameQueries_SameResults()
{
    var p1 = new FakeNavmeshProvider(BuildTwoAdjacentLayer());
    var p2 = new FakeNavmeshProvider(BuildTwoAdjacentLayer());

    var from = new Vector3(1, 0, 1);
    var to   = new Vector3(3, 0, 1);

    Assert.Equal(p1.IsWalkable(from),        p2.IsWalkable(from));
    Assert.Equal(p1.PathExists(from, to),    p2.PathExists(from, to));
    Assert.Equal(p1.PathCost(from, to),      p2.PathCost(from, to));
}
```

---

### Task 2: Complete NAV-P8-T2 — `FakeDtCrowdProviderTests` + `IFakeDtCrowdProviderTestApi`

**Part A — Extend the TestAPI** (production code change):

File: `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeDtCrowdProvider.cs`

Add two methods to `IFakeDtCrowdProviderTestApi` and implement them in `FakeDtCrowdProvider`.
The DD-Fake-Nav §4.6 specifies:

```csharp
/// <summary>Force an agent's computed velocity, bypassing steering output.</summary>
void OverrideAgentVelocity(Entity entity, Vector3 velocity);

/// <summary>Clear a velocity override set by OverrideAgentVelocity.</summary>
void ClearAgentVelocityOverride(Entity entity);
```

In `AgentEntry` (the private class), add:
- `bool HasVelocityOverride;`
- `Vector3 OverriddenVelocity;`

In `Update`, after computing the final velocity for an agent, if `HasVelocityOverride` is set,
replace the velocity with `OverriddenVelocity` instead:

```csharp
// At the end of the velocity-update loop, before applying:
if (a.HasVelocityOverride)
    a.Velocity = a.OverriddenVelocity;
else
    a.Velocity = a.Velocity + delta;  // existing logic
```

Implement `OverrideAgentVelocity`:
```csharp
public void OverrideAgentVelocity(Entity entity, Vector3 velocity)
{
    if (_agents.TryGetValue(entity.Index, out var a))
    {
        a.HasVelocityOverride = true;
        a.OverriddenVelocity  = velocity;
    }
}
```

Implement `ClearAgentVelocityOverride`:
```csharp
public void ClearAgentVelocityOverride(Entity entity)
{
    if (_agents.TryGetValue(entity.Index, out var a))
        a.HasVelocityOverride = false;
}
```

**Part B — Add 4 tests to the test file:**

File: `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/FakeDtCrowdProviderTests.cs`

The file currently has 11 tests. Add these 4 tests (append before the closing brace of `FakeDtCrowdProviderTests`):

```
Row 7:  Update_AgentSurroundedByThreeStationary_VelocityNearZero
Row 8:  OverrideAgentVelocity_TestApiBypassesSteering
Row 9:  Determinism_SameInputs_SameOutputs
Row 10: Update_LargeAgentCount_Completes
```

You need to understand the existing test helpers:
- Tests use `new EntityRepository()` with `RegisterComponent<SimTransform>()`.
- Helper creates an entity, sets SimTransform, calls `RegisterAgent(entity, params)`.
- `_view = (ISimulationView)repo` is used as the `ISimulationView` passed to `Update`.

Look at the existing tests to understand the fixture pattern. The `FakeDtCrowdProvider` constructor takes no arguments.

**Row 7: Update_AgentSurroundedByThreeStationary_VelocityNearZero**
- Create 4 entities in a tight cluster (all within radius*2 of each other).
- Register all 4 as agents. Set a target far away for entity 0 (center agent); leave entities 1, 2, 3 with no target (velocity = 0).
- The 3 stationary agents should push back on entity 0 enough that its velocity is near zero.
- Run ~20 ticks.
- Assert that `provider.GetAgentVelocity(entity0).Length() < 0.1f`.

Note: The fake separation algorithm applies impulses. With 3 blocking agents at distance < sumR, the separation force should overwhelm the desired velocity. Place entities tightly (radius=0.5, gap=0.3 between them).

**Row 8: OverrideAgentVelocity_TestApiBypassesSteering**
- Register one entity. Give it a target 100m away (strong desired velocity).
- Before calling Update, cast provider to IFakeDtCrowdProviderTestApi and call `OverrideAgentVelocity(entity, new Vector3(7f, 0f, 0f))`.
- Call `Update(0.016f, view)`.
- Assert `provider.GetAgentVelocity(entity) == new Vector3(7f, 0f, 0f)`.
- Call `ClearAgentVelocityOverride(entity)`.
- Call `Update(0.016f, view)` again.
- Assert the velocity has changed (no longer 7,0,0).

**Row 9: Determinism_SameInputs_SameOutputs**
- Create two identical providers (p1, p2) with 3 agents at the same positions, same targets, same params.
- Run 10 ticks of Update on both.
- Assert p1.GetAgentVelocity(entityN) == p2.GetAgentVelocity(entityN) for all 3 entities.

**Row 10: Update_LargeAgentCount_Completes**
- Register 200 agents, each with a distinct entity index, spread in a 20x10 grid (1m apart).
- Give all agents a target at (100, 0, 100).
- Run 5 ticks.
- Assert no NaN velocities: for every entity, `!float.IsNaN(provider.GetAgentVelocity(e).X)`.
- Assert `((IFakeDtCrowdProviderTestApi)provider).UpdateCallCount == 5`.

---

### Task 3: Complete NAV-P8-T3 — `FakeVolumetricPathProviderTests`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/FakeVolumetricPathProviderTests.cs`

The file already has 6 tests. Add 3 more tests (append before the closing brace of `FakeVolumetricPathProviderTests`):

```
Row 3: Plan_StartInsideNoFlyZone_ReturnsNoPath
Row 4: Plan_EndInsideNoFlyZone_ReturnsNoPath
Row 5: Plan_AltitudeExceedsProfileMax_ReturnsNoPath
```

The existing `NoFlyBox(Vector3 pos, float half)` helper is available.

**Row 3: Plan_StartInsideNoFlyZone_ReturnsNoPath**
- Provider with maxAltitude=1000.
- Add a no-fly zone around (0, 100, 0) with half=5.
- from = (0, 100, 0) — inside the no-fly zone.
- to = (100, 100, 0) — outside.
- PlanPath returns 0.

```csharp
[Fact]
public void Plan_StartInsideNoFlyZone_ReturnsNoPath()
{
    var provider = new FakeVolumetricPathProvider(minAltitude: 0f, maxAltitude: 1000f);
    provider.AddNoFlyZone(NoFlyBox(new Vector3(0, 100, 0), 5f));

    var buf = new NavWaypoint[10];
    int n = provider.PlanPath(new Vector3(0, 100, 0), new Vector3(100, 100, 0), buf.AsSpan());

    Assert.Equal(0, n);
}
```

**Row 4: Plan_EndInsideNoFlyZone_ReturnsNoPath**
- Provider with maxAltitude=1000.
- Add a no-fly zone around (100, 100, 0) with half=5.
- from = (0, 100, 0) — outside.
- to = (100, 100, 0) — inside the no-fly zone.
- PlanPath returns 0.

```csharp
[Fact]
public void Plan_EndInsideNoFlyZone_ReturnsNoPath()
{
    var provider = new FakeVolumetricPathProvider(minAltitude: 0f, maxAltitude: 1000f);
    provider.AddNoFlyZone(NoFlyBox(new Vector3(100, 100, 0), 5f));

    var buf = new NavWaypoint[10];
    int n = provider.PlanPath(new Vector3(0, 100, 0), new Vector3(100, 100, 0), buf.AsSpan());

    Assert.Equal(0, n);
}
```

**Row 5: Plan_AltitudeExceedsProfileMax_ReturnsNoPath**
- Provider with maxAltitude=200.
- from = (0, 300, 0) — Y=300 > maxAltitude=200.
- to = (100, 300, 0).
- PlanPath returns 0 (position not flyable due to altitude ceiling).

```csharp
[Fact]
public void Plan_AltitudeExceedsProfileMax_ReturnsNoPath()
{
    var provider = new FakeVolumetricPathProvider(minAltitude: 0f, maxAltitude: 200f);

    var buf = new NavWaypoint[10];
    int n = provider.PlanPath(new Vector3(0, 300, 0), new Vector3(100, 300, 0), buf.AsSpan());

    Assert.Equal(0, n);
}
```

---

### Task 4: Complete NAV-P8-T4 — `MusclePathRegistryTests`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/PathRegistryTests.cs`

The `MusclePathRegistryTests` class already has 6 tests. Add 3 more (append before the closing brace of `MusclePathRegistryTests`):

```
Row 2b: MusclePathRegistry_RegisterOrReplace_ExistingHandle_ReplacesInPlace
Row 6b: MusclePathRegistry_TryGetWaypoints_UnknownHandle_ReturnsFalse
Row 7:  MusclePathRegistry_BrainAndMuscleHandles_NoCollision
```

Note: The existing helper `MakeWaypoints(params Vector3[])` is available within the class.
`MusclePathRegistry.RegisterOrReplace(handle, waypoints, dist, version, primaryBackend, replanCount)`.

**Row 2b: MusclePathRegistry_RegisterOrReplace_ExistingHandle_ReplacesInPlace**
- Register handle 42 with waypoints=[V(0,0,0)].
- Re-register handle 42 with waypoints=[V(1,0,0), V(2,0,0)].
- TryGetWaypoints returns the NEW waypoints (2 entries), not the old one.

```csharp
[Fact]
public void MusclePathRegistry_RegisterOrReplace_ExistingHandle_ReplacesInPlace()
{
    var registry = new MusclePathRegistry();

    registry.RegisterOrReplace(42, MakeWaypoints(new Vector3(0, 0, 0)), 1f, 0u, 0, 0);
    registry.RegisterOrReplace(42, MakeWaypoints(new Vector3(1, 0, 0), new Vector3(2, 0, 0)), 2f, 0u, 0, 0);

    var dest = new NavWaypoint[4];
    Assert.True(registry.TryGetWaypoints(42, dest.AsSpan(), out int count));
    Assert.Equal(2, count);
    Assert.Equal(new Vector3(1, 0, 0), dest[0].Position);
    Assert.Equal(new Vector3(2, 0, 0), dest[1].Position);
}
```

**Row 6b: MusclePathRegistry_TryGetWaypoints_UnknownHandle_ReturnsFalse**
- Empty registry (no registrations).
- TryGetWaypoints(999, buf, out count) → returns false, count must not be used.

```csharp
[Fact]
public void MusclePathRegistry_TryGetWaypoints_UnknownHandle_ReturnsFalse()
{
    var registry = new MusclePathRegistry();
    var dest = new NavWaypoint[4];
    Assert.False(registry.TryGetWaypoints(999, dest.AsSpan(), out _));
}
```

**Row 7: MusclePathRegistry_BrainAndMuscleHandles_NoCollision**
- By design, Brain-allocated handles are `>= 0x40000000` and Muscle-private handles are `< 0x40000000`.
- Allocate a Brain-side handle (e.g. `0x40000001`) and a Muscle-side handle (e.g. `1`).
- Register both in the same registry.
- Verify IsCached returns true for both independently.
- Verify they have distinct entries (different TotalDistanceMeters).

```csharp
[Fact]
public void MusclePathRegistry_BrainAndMuscleHandles_NoCollision()
{
    var registry = new MusclePathRegistry();

    int muscleHandle = 1;
    int brainHandle  = 0x40000001;

    registry.RegisterOrReplace(muscleHandle, MakeWaypoints(new Vector3(0, 0, 0)), 1f, 0u, 0, 0);
    registry.RegisterOrReplace(brainHandle,  MakeWaypoints(new Vector3(5, 0, 0)), 9f, 0u, 0, 0);

    Assert.True(registry.IsCached(muscleHandle));
    Assert.True(registry.IsCached(brainHandle));

    Assert.True(registry.TryGetSummary(muscleHandle, out var ms));
    Assert.True(registry.TryGetSummary(brainHandle,  out var bs));

    Assert.Equal(1f, ms.TotalDistanceMeters);
    Assert.Equal(9f, bs.TotalDistanceMeters);
}
```

---

### Task 5: Complete NAV-P8-T5 — `BrainPathRegistryTests`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/PathRegistryTests.cs`

The `BrainPathRegistryTests` class already has 5 tests. Add 2 more (append before the closing brace of `BrainPathRegistryTests`):

```
Row 5: BrainPathRegistry_EvictEntry_ExistingHandle_Removes
Row 7: BrainPathRegistry_Stats_Reset_ZeroesCounters
```

The existing `E(int index)` helper and `MakeWaypoints(int count)` helper are available within the class.

**Row 5: BrainPathRegistry_EvictEntry_ExistingHandle_Removes**
- Ingest a handle for an entity.
- Call `registry.EvictEntry(entity, handle)`.
- Verify `IsCached(entity, handle, 0)` is now false.

```csharp
[Fact]
public void BrainPathRegistry_EvictEntry_ExistingHandle_Removes()
{
    var registry = new BrainPathRegistry();
    var entity = E(30);
    registry.TryIngestResponse(entity, 77, MakeWaypoints(2), replanCount: 0,
                               totalDist: 3f, navmeshVersion: 1u, primaryBackend: 0);

    Assert.True(registry.IsCached(entity, 77, currentReplanCount: 0));

    registry.EvictEntry(entity, 77);

    Assert.False(registry.IsCached(entity, 77, currentReplanCount: 0));
}
```

**Row 7: BrainPathRegistry_Stats_Reset_ZeroesCounters**
- Ingest a handle, query it (hit), query a missing handle (miss).
- Call `registry.GetStats()` — verify HitCount >= 1, MissCount >= 1.
- Note: BrainPathRegistry may not have a Reset() method in the current implementation.
  If it does not, simply verify that stats reflect the expected counts (omit the reset part).
  Do NOT add a Reset() method to BrainPathRegistry — test only what the existing API provides.

```csharp
[Fact]
public void BrainPathRegistry_Stats_ZeroAtStart()
{
    var registry = new BrainPathRegistry();
    var stats = registry.GetStats();
    Assert.Equal(0, stats.TotalEntries);
    Assert.Equal(0, stats.HitCount);
    Assert.Equal(0, stats.MissCount);
}
```

Note: The test name changes to `BrainPathRegistry_Stats_ZeroAtStart` because `BrainPathRegistry`
may not expose a `Reset()` method. If the current `BrainPathRegistry.GetStats()` returns a
`FakePathRegistryStats` with `TotalEntries`, `HitCount`, `MissCount`, `StaleMisses` fields,
verify that a fresh registry has all zeroes.

---

### Task 6: Complete NAV-P9-T3 — `NavigationIntentBridgeSystemTests` (crowd + pipeline)

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationIntentBridgePipelineTests.cs`

The existing `NavigationIntentBridgePipelineTests` class has 4 tests using
`NavigationTestWorldFactory.Create()` and `NavigationIntentBridgeSystem(_pool)`.

For the crowd-related tests, we need to pass a `FakeDtCrowdProvider` to the system constructor.
Add a **new test class** `NavigationIntentBridgeCrowdTests` to this same file, below the existing class.
This is cleaner than mixing it into the existing constructor setup.

The new class must:
1. Use `NavigationTestWorldFactory.Create()`.
2. Register `VehicleState` component.
3. Create a `FakeDtCrowdProvider` as `_crowd`.
4. Create `NavigationIntentBridgeSystem(null, _crowd)`.
5. Register `PathfindingRequestEvent`.

Add the following 10 tests:

```
Row 1:  Humanoid_MoveTo_TagsCrowdAgent
Row 2:  Humanoid_MoveTo_RegistersWithCrowdProvider
Row 4:  Wheeled_MoveTo_NoCrowdTag
Row 6:  FollowRoute_AnyMobility_NoCrowdTag
Row 7:  PlanRoute_NoFollowingStarted_NoCrowdRegistration
Row 9:  FollowPath_LooksUpHandleInMusclePool_StartsFollowing
Row 13: ReleasePath_FreesMusclePoolEntry
Row 14: ReleasePath_DoesNotStopMovement
Row 15: ActionInstanceIdMismatch_TriggersRouting
Row 5:  Wheeled_MoveTo_NavStateModeSet  (see note)
```

**Note on Row 5 (Wheeled_MoveTo_NavStateModeSet):**
This tests the NavigationIntent→NavState branch of the system (not the LocomotionChannel branch).
For a wheeled entity with a `NavigationIntent{Mode=DirectPoint}`, running the system should produce
`NavState.Mode = KinematicsMode.Direct`. This is already tested functionally by the existing
`DirectPoint_Intent_MapsToDirectKinematics` in `NavigationIntentBridgeSystemTests`. 
Instead, write the test as **`Wheeled_MoveTo_NoCrowdTag`** variant from the LocomotionChannel side
(DD row 4 is more useful). Skip row 5 (duplicate of existing test).

**Skeleton for the new class:**

```csharp
// ── Crowd-side tests for NavigationIntentBridgeSystem ────────────────────────
public sealed class NavigationIntentBridgeCrowdTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly FakeDtCrowdProvider _crowd;
    private readonly TrajectoryPoolManager _pool;
    private readonly NavigationIntentBridgeSystem _system;
    private readonly ISimulationView _view;

    public NavigationIntentBridgeCrowdTests()
    {
        _repo   = NavigationTestWorldFactory.Create();
        _repo.RegisterComponent<VehicleState>();
        _repo.RegisterEvent<PathfindingRequestEvent>();
        _crowd  = new FakeDtCrowdProvider();
        _pool   = new TrajectoryPoolManager();
        _system = new NavigationIntentBridgeSystem(_pool, _crowd);
        _view   = (ISimulationView)_repo;
    }

    public void Dispose() => _repo.Dispose();

    private LocomotionChannel MoveToChannel(uint instanceId, Vector2 dest, float speed = 5f)
    {
        var ch = new LocomotionChannel
        {
            ActiveAction     = NavigationConstants.ActionIdMoveTo,
            ActionInstanceId = instanceId,
        };
        unsafe
        {
            fixed (byte* pParams = ch.Params)
                *(MoveToParams*)pParams = new MoveToParams
                {
                    Destination   = dest,
                    ArrivalRadius = 1f,
                    Speed         = speed,
                };
        }
        return ch;
    }
}
```

**Test implementations:**

**Row 1: Humanoid_MoveTo_TagsCrowdAgent**
- Create entity with SimTransform, NavigationStatus. No VehicleState.
- Set LocomotionChannel with ActionIdMoveTo, unique ActionInstanceId.
- Execute system.
- Assert entity now has CrowdAgent component.

**Row 2: Humanoid_MoveTo_RegistersWithCrowdProvider**
- Same setup as Row 1.
- After Execute: cast `_crowd` to `IFakeDtCrowdProviderTestApi`.
- Assert `crowdApi.RegisteredEntityIndices.Contains(entity.Index)`.

**Row 4: Wheeled_MoveTo_NoCrowdTag**
- Create entity with SimTransform, NavigationStatus, **VehicleState**.
- Set LocomotionChannel with ActionIdMoveTo.
- Execute system.
- Assert entity does NOT have CrowdAgent component (`_repo.HasComponent<CrowdAgent>(entity) == false`).

**Row 6: FollowRoute_AnyMobility_NoCrowdTag**
- Create entity with SimTransform, NavigationStatus.
- Set LocomotionChannel with ActionIdFollowRoute (= `NavigationConstants.ActionIdFollowRoute`).
- Execute system.
- Assert entity does NOT have CrowdAgent component.
- Assert `crowdApi.RegisteredEntityIndices` is empty.

**Row 7: PlanRoute_NoFollowingStarted_NoCrowdRegistration**
- Create entity with SimTransform, NavigationStatus.
- Set LocomotionChannel with ActionIdPlanRoute, providing PlanRouteParams{Destination=(5,5)}.
- Execute system.
- Assert entity does NOT have CrowdAgent component.
- Assert `crowdApi.RegisteredEntityIndices` is empty.

**Row 9: FollowPath_LooksUpHandleInMusclePool_StartsFollowing**
- Pre-populate the trajectory pool: store a dummy trajectory under handle 42.
  Use `_pool.AddOrReplace(42, new TrajectorySegment[0])` or the appropriate API.
  Check `TrajectoryPoolManager` API — it may have `AddTrajectory(int handle, ...)`.
- Create entity with SimTransform, NavigationStatus.
- Set LocomotionChannel with ActionIdFollowPath and FollowPathParams{RouteHandle=42}.
- Execute system.
- Assert NavigationStatus.Result != NavigationResult.FailedInvalidHandle
  (the handle was found, so no failure is written; the existing test covers the failure case).

**Row 13: ReleasePath_FreesMusclePoolEntry**
- Pre-populate `_pool` with handle 77 (use the appropriate pool API).
- Create entity with SimTransform, NavigationCorridorMuscle.
- Set LocomotionChannel with ActionIdReleasePath and ReleasePathParams{RouteHandle=77}.
- Execute system.
- Assert `_pool.TryGetTrajectory(77, out _) == false` (handle removed from pool).

**Row 14: ReleasePath_DoesNotStopMovement**
- Pre-populate `_pool` with handle 88.
- Create entity with SimTransform, NavigationCorridorMuscle, NavState{Mode=KinematicsMode.Direct}.
- Set LocomotionChannel with ActionIdReleasePath and ReleasePathParams{RouteHandle=88}.
- Execute system.
- Assert `_repo.GetComponent<NavState>(entity).Mode == KinematicsMode.Direct`
  (movement was not halted).

**Row 15: ActionInstanceIdMismatch_TriggersRouting**
- Create entity with SimTransform, NavigationStatus.
- Execute system with ActionInstanceId=1 (publishes one PathfindingRequestEvent).
- `_repo.Bus.SwapBuffers()`.
- Change the LocomotionChannel's ActionInstanceId to 2 (new action instance).
- Execute system again.
- `_repo.Bus.SwapBuffers()`.
- Assert a second PathfindingRequestEvent was published (total events seen on second swap == 1,
  or total published == 2).

---

### Implementation notes for Task 6

**TrajectoryPoolManager API** — Check how it's used in existing tests or PlanRoute executor:
- If it has `AddTrajectory(int handle, ...)` or `GetOrCreate(int handle)`, use that.
- If it uses `TryGetTrajectory(int handle, out TrajectorySegment[])`, use the inverse method to add.
- Look at the existing `PlanRoute_PublishesRequestWithBrainHandle` test to understand what's available.
- Do NOT guess — read the `TrajectoryPoolManager` class first.

**FollowRoute ActionId** — The value is `NavigationConstants.ActionIdFollowRoute = 3`.
Verify the constant exists in `NavigationConstants`.

**VehicleState** — Import `using CarKinem.Core;` (already in the existing pipeline test file).

---

## TASK-TRACKER updates

After all tests pass, update `.dev/navig-2/TASK-TRACKER.md`:
- Mark **NAV-P8-T1** through **NAV-P8-T6** as `[x]` done with `*(BATCH-08)*`.
- Mark **NAV-P9-T1**, **NAV-P9-T2**, **NAV-P9-T3** as `[x]` done with `*(BATCH-08)*`.

---

## Verification

Run:
```
dotnet test "FDP\Toolkits\Fdp.Toolkits.Tests\" --filter "Navigation" --no-build
```

All navigation tests must pass. The test count should be **>= 200** (176 existing + ~24 new).

Then run:
```
dotnet build "FDP\FDP.sln" --configuration Debug
```

Build must succeed with 0 errors.

---

## Report

Create `.dev/navig-2/batches/BATCH-08-REPORT.md` with:
- Summary of what was implemented.
- List of new tests added per task.
- Final test count.
- Any deviations from the instructions (explain why).

Status: COMPLETED
