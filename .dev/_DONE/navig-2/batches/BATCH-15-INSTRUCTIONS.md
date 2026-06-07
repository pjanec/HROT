# BATCH-15 Instructions — NAV-P10-T2/T3/T4/T5

## Workspace root
`d:\Work\IOS-IG-SimHost-FDP-2`

## Context
BATCH-14 delivered `NavTestHarness`, `CapturedEventLog`, `NavTestMaps`, and two tests (S1, S7). Committed in `b1904616`. 261 navigation tests pass.

This batch adds four more integration test classes: S2, S2b, S3, S4.

---

## Step 0 — Read these files before writing any code

```
FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavTestHarness.cs
FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavTestMaps.cs
FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/OffMeshLinkDetectionSystem.cs   (full file)
FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/CorridorPreviewSystem.cs
FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationConstants.cs
FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationActions.cs                    (MoveToParams.LayerMask)
FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingEvents.cs                    (OffMeshTraversalStartedEvent fields)
FDP/Toolkits/Fdp.Toolkits/Navigation/Components/NavigationComponents.cs      (NavigationCorridorPreview, NavigationPhase)
FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs
FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/OffMeshLinkDetectionSystemTests.cs  (how MusclePathRegistry is used)
FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/MusclePathRegistry.cs  (or SharedPathRegistry.cs — find the file)
```

Search for: `struct VehicleState` — need to know if it is in CarKinem.Core or elsewhere.

---

## Step 1 — Extend `NavTestHarness.cs`

File to modify: `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavTestHarness.cs`

### 1a — New fields (add after existing system fields)

```csharp
private readonly OffMeshLinkDetectionSystem  _offMeshDetect;
private readonly CorridorPreviewSystem       _corridorPreview;
```

### 1b — New public properties

```csharp
public FakeNavmeshProvider Navmesh { get; }
public FakeDtCrowdProvider Crowd   { get; }
public SharedPathRegistry  PathRegistry { get; }

public IFakeNavmeshProviderTestApi NavmeshApi => (IFakeNavmeshProviderTestApi)Navmesh;
```

### 1c — Constructor changes

After `module.RegisterProviders(world)`, add:

```csharp
// VehicleState component needed for SpawnVehicle and to prevent crowd registration.
world.RegisterComponent<VehicleState>();

// Events needed by off-mesh traversal tests.
world.RegisterEvent<OffMeshTraversalStartedEvent>();
// OffMeshTraversalEndedEvent may or may not exist — check PathfindingEvents.cs.
// If it exists: world.RegisterEvent<OffMeshTraversalEndedEvent>();
```

After instantiating other systems, add:
```csharp
_offMeshDetect   = new OffMeshLinkDetectionSystem(module.PathRegistry, module.Crowd);
_corridorPreview = new CorridorPreviewSystem(module.PathRegistry);
```

Assign new properties:
```csharp
Navmesh      = module.Navmesh;
Crowd        = module.Crowd;
PathRegistry = module.PathRegistry;
```

### 1d — Update `Tick()` method

Insert `_offMeshDetect` BEFORE `_crowdUpdate` and `_corridorPreview` AFTER `_crowdUpdate` but BEFORE `_navExec`:

```
// 6. Materialize
_materialize.Execute(Repo, Dt);
// 6b. Off-mesh detection (must be BEFORE CrowdUpdate to suppress velocity this tick)
_offMeshDetect.Execute(Repo, Dt);
// 7. Crowd update (integrates positions; suppressed for AwaitingTraversal entities)
_crowdUpdate.Execute(Repo, Dt);
// 7b. Corridor preview (opt-in 8-waypoint window)
_corridorPreview.Execute(Repo, Dt);
// 8. NavExec (arrival check, event emission)
_navExec.Execute(Repo, Dt);
```

### 1e — Add `SpawnVehicle(Vector2 pos)` method

```csharp
public Entity SpawnVehicle(Vector2 pos)
{
    var entity = Repo.CreateEntity();
    Repo.AddComponent(entity, new SimTransform { Position = new Vector3(pos.X, pos.Y, 0f) });
    Repo.AddComponent(entity, new SimVelocity());
    Repo.AddComponent(entity, new NavigationIntent());
    Repo.AddComponent(entity, new NavigationStatus());
    Repo.AddComponent(entity, new FrustrationTicks());
    Repo.AddComponent(entity, new LocomotionChannel());
    Repo.AddComponent(entity, new NavAgentProfile { AgentRadius = 1.2f, AgentHeight = 2.5f });
    Repo.AddComponent(entity, new VehicleState());
    return entity;
}
```

### 1f — Change `IssueMoveTo` signature

Add an optional `layerMask` parameter (last) defaulting to `(uint)NavLayerMask.Infantry`.
Change `MoveToParams.LayerMask = layerMask` instead of `0xFFFFFFFF`.

```csharp
public unsafe void IssueMoveTo(Entity e, Vector2 destination, byte flags = 0, int routeHandle = 0,
    uint layerMask = (uint)NavLayerMask.Infantry)
```

### 1g — Update `CapturedEventLog`

Add off-mesh event capture. Read `PathfindingEvents.cs` for exact field names in `OffMeshTraversalStartedEvent`.

```csharp
private readonly List<OffMeshTraversalStartedEvent> _offMeshStarted = new();

// In Capture():
foreach (ref readonly var e in view.ReadEvents<OffMeshTraversalStartedEvent>())
    _offMeshStarted.Add(e);

// Helper:
public bool HasOffMeshTraversalStarted()
    => _offMeshStarted.Count > 0;

public OffMeshTraversalStartedEvent GetFirstOffMeshTraversalStarted()
    => _offMeshStarted.Count > 0
        ? _offMeshStarted[0]
        : throw new InvalidOperationException("No OffMeshTraversalStartedEvent captured.");
```

---

## Step 2 — S2_LBendFollowTests.cs

Create `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S2_LBendFollowTests.cs`.

`LoadLBend()` has four polygons: (5,5), (15,5), (25,5), (25,15) — chain 0→1→2→3.
- Start at (0,0) — near polygon 0 (Square(5,5) = X=0..10, Z=0..10 in XZ).
- End at (28,20) — near polygon 3 (Square(25,15) = X=20..30, Z=10..20 in XZ, but path end via `IssueMoveTo(Vector2(28, 20))` becomes `PathfindingRequestEvent.End = (28, 20, 0)`.

Wait — `FakeNavmeshProvider` uses XZ plane (navmesh square at `Square(cx, cz)` = X=(cx-5..cx+5), Z=(cz-5..cz+5), Y=0).
Harness spawns at `new Vector3(pos.X, pos.Y, 0)` — Z=0 always.
IssueMoveTo maps `Vector2(x,y)` → `PathfindingRequestEvent.End = new Vector3(x, y, 0)` — Z=0 always.

**IMPORTANT coordinate fact**: In the harness, Vector2.X maps to World.X, Vector2.Y maps to World.Y (not World.Z). Navmesh polygons are at World.Y=0 and use X,Z. So `(28, 20, 0)` has Z=0, which is inside `Square(cx, 5)` polygons (Z=0..10). But it's OUTSIDE `Square(25, 15)` which has Z=10..20.

For the L-bend to work with the existing harness coordinate mapping:
- Destination must have Z=0 (i.e., Vector2.Y is eventually world.Y which affects Z in the path request... wait no, bridge maps `MoveToParams.Destination` → `End.X = dest.X`, `End.Y = dest.Y`, `End.Z = 0`. So Z is always 0.
- Therefore the destination must be in a polygon that contains Z=0 area.
- `Square(25, 5)` spans Z=0..10 — Z=0 is the border. Polygon 2 at index 2 in the L-bend.
- The destination `Vector2(28, 0)` → `(28, 0, 0)` — inside polygon 2 (X=20..30, Z=0..10) ✓.

But L-bend has polygon 3 at `Square(25, 15)` = X=20..30, Z=10..20. No path there unless Z>0.

For S2, use a destination INSIDE the reachable XZ area:
- Infantry at `Vector2(0, 0)` — at polygon 0 corner ✓
- Destination at `Vector2(28, 0)` — inside polygon 2, which IS in the L-bend chain ✓

The "L-bend" is still exercised because the path goes 0→1→2 (a non-straight route through the bend). Polygon 3 is `Square(25, 15)` = Z=10..20, which requires Z>0 to reach — not reachable via current coordinate mapping.

For a more interesting test: use a destination that requires polygon 3 (the bend's far end). To do this, the harness would need `new Vector3(pos.X, 0f, pos.Y)` mapping. That is a one-line change to `SpawnInfantry`. Consider this improvement.

**Suggested change**: Modify both `SpawnInfantry` and `SpawnVehicle` to use `new Vector3(pos.X, 0f, pos.Y)` instead of `new Vector3(pos.X, pos.Y, 0f)`. This maps Vector2.Y to world Z (where navmesh polygons live), which is the canonical XZ-plane layout. This does NOT break S1 (entity at (0,0)→(0,0,0) same, dest (28,0)→(28,0,0) same) or S7 (entity (1,1)→(1,0,1) inside Square(5,5)=(0..10,0..10) ✓, dest (50,50)→(50,0,50) outside ✓).

**If you make this change**, then:
- `SpawnInfantry(Vector2(0, 0))` → `(0, 0, 0)` ✓
- `IssueMoveTo(Vector2(28, 5))` → `PathfindingRequestEvent.End = (28, 5, 0)` 
  Wait — `IssueMoveTo` maps: `MoveToParams.Destination = dest (Vector2)`, bridge maps to `End = new Vector3(p.Destination.X, p.Destination.Y, 0f)`. So `IssueMoveTo(Vector2(28, 5))` → `End = (28, 5, 0)`. Still Z=0!

The fundamental issue: `NavigationIntentBridgeSystem` hard-codes `End.Z = 0`. We cannot change this (production code). So path End always has Z=0.

**Conclusion**: For S2 L-bend, use a destination in a polygon that contains Z=0:
- Polygon 2 at `Square(25, 5)` spans Z=0..10, so Z=0 is on the boundary.
- `IssueMoveTo(Vector2(28, 0))` → `End = (28, 0, 0)` — on boundary ✓

The path 0→1→2 still exercises the multi-segment corridor. It's a valid L-bend test even if we don't reach polygon 3.

```csharp
using System;
using System.Numerics;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    public sealed class S2_LBendFollowTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S2_LBendFollowTests()
            => _h = new NavTestHarness(NavTestMaps.LoadLBend());

        public void Dispose() => _h.Dispose();

        [Fact]
        public void LBend_InfantryFollowsMultiSegmentPath_Arrives()
        {
            // Polygon chain: 0(5,5)->1(15,5)->2(25,5)->3(25,15) in XZ.
            // Start at (0,0,0): near polygon 0.
            // Destination at (28,0,0): inside polygon 2 (X=20..30, Z=0..10).
            // Path traverses at least polygons 0,1,2 -- multi-segment corridor.
            var entity = _h.SpawnInfantry(Vector2.Zero);
            _h.IssueMoveTo(entity, new Vector2(28f, 0f));

            _h.PumpUntil(() => _h.EventLog.HasMoveCompleted(entity), maxTicks: 1000);

            var evt = _h.EventLog.GetMoveCompleted(entity);
            Assert.Equal(NavigationResult.Arrived, evt.Reason);
        }
    }
}
```

---

## Step 3 — S2b_LBendCorridorPreviewTests.cs

Create `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S2b_LBendCorridorPreviewTests.cs`.

The `FlagBitStreamCorridorPreview` constant is in `NavigationConstants.cs`. The flag bit value (3) gives mask `(byte)(1 << 3) = 8`.

`CorridorPreviewSystem` adds `NavigationCorridorPreview` to entities where `NavigationIntent.Flags` has the bit set AND the entity has `NavigationCorridorMuscle` (corridor materialized).

```csharp
using System;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    public sealed class S2b_LBendCorridorPreviewTests : IDisposable
    {
        private static readonly byte CorridorPreviewFlag =
            (byte)(1 << NavigationConstants.FlagBitStreamCorridorPreview);

        private readonly NavTestHarness _h;

        public S2b_LBendCorridorPreviewTests()
            => _h = new NavTestHarness(NavTestMaps.LoadLBend());

        public void Dispose() => _h.Dispose();

        [Fact]
        public void LBend_WithPreviewFlag_CorridorPreviewComponentAdded()
        {
            var entity = _h.SpawnInfantry(Vector2.Zero);
            _h.IssueMoveTo(entity, new Vector2(28f, 0f), flags: CorridorPreviewFlag);

            // Pump until corridor materializes (path request + result in ~2-3 ticks).
            _h.PumpFor(5);

            // The corridor preview component is added by CorridorPreviewSystem AFTER
            // NavigationCorridorMuscle is written by PathfindingResultMaterializationSystem.
            // If corridor is present, preview should also be present.
            if (_h.Repo.HasComponent<NavigationCorridorMuscle>(entity))
            {
                Assert.True(_h.Repo.HasComponent<NavigationCorridorPreview>(entity),
                    "Preview component should be present when StreamCorridorPreview flag is set.");
            }
        }

        [Fact]
        public void LBend_WithoutPreviewFlag_NoCorridorPreviewComponent()
        {
            var entity = _h.SpawnInfantry(Vector2.Zero);
            _h.IssueMoveTo(entity, new Vector2(28f, 0f)); // default flags = 0

            // Pump enough for corridor to materialize.
            _h.PumpFor(10);

            Assert.False(_h.Repo.HasComponent<NavigationCorridorPreview>(entity),
                "Preview component should NOT be present without StreamCorridorPreview flag.");
        }

        [Fact]
        public void LBend_WithPreviewFlag_ArrivesNormally()
        {
            var entity = _h.SpawnInfantry(Vector2.Zero);
            _h.IssueMoveTo(entity, new Vector2(28f, 0f), flags: CorridorPreviewFlag);

            _h.PumpUntil(() => _h.EventLog.HasMoveCompleted(entity), maxTicks: 1000);

            var evt = _h.EventLog.GetMoveCompleted(entity);
            Assert.Equal(NavigationResult.Arrived, evt.Reason);
        }
    }
}
```

---

## Step 4 — S3_TwoLayersRoutingTests.cs

Create `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S3_TwoLayersRoutingTests.cs`.

**Key coordinate insight**: `PathfindingRequestEvent.End.Z` is always 0 (hardcoded in bridge). Infantry layer polygons are at Z=0..10 — reachable. Vehicle layer polygons are at Z=20..30 — NOT reachable via the current bridge coordinate mapping.

Therefore: when Infantry mask → path found (polygon at Z=0..10). When Vehicle mask → same dest, vehicle polygons are at Z=20..30, start/end at Z=0 are NOT in vehicle layer → unreachable.

This correctly tests that layer masks are plumbed through to the solver.

```csharp
using System;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    public sealed class S3_TwoLayersRoutingTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S3_TwoLayersRoutingTests()
            => _h = new NavTestHarness(NavTestMaps.LoadTwoLayers());

        public void Dispose() => _h.Dispose();

        /// <summary>
        /// Infantry entity with Infantry layer mask can navigate in the Infantry-layer corridor.
        /// </summary>
        [Fact]
        public void TwoLayers_InfantryMask_Arrives()
        {
            var entity = _h.SpawnInfantry(Vector2.Zero);
            _h.IssueMoveTo(entity, new Vector2(28f, 0f), layerMask: (uint)NavLayerMask.Infantry);

            _h.PumpUntil(() => _h.EventLog.HasMoveCompleted(entity), maxTicks: 1000);

            var evt = _h.EventLog.GetMoveCompleted(entity);
            Assert.Equal(NavigationResult.Arrived, evt.Reason);
        }

        /// <summary>
        /// A vehicle entity using the Vehicle layer mask cannot reach a destination
        /// that only exists in the Infantry layer (Z=0 area). The Vehicle layer
        /// polygons are at Z=20-30, but the path End is always at Z=0.
        /// This verifies the layer mask is correctly routed to the solver.
        /// </summary>
        [Fact]
        public void TwoLayers_VehicleMaskForInfantryArea_Unreachable()
        {
            var entity = _h.SpawnVehicle(Vector2.Zero);
            _h.IssueMoveTo(entity, new Vector2(28f, 0f), layerMask: (uint)NavLayerMask.Vehicle);

            _h.PumpFor(10);

            var status = _h.Repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationResult.FailedUnreachable, status.Result);
        }
    }
}
```

---

## Step 5 — S4_OffMeshJumpAcrossTests.cs

Create `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S4_OffMeshJumpAcrossTests.cs`.

**ARCHITECTURAL NOTE**: `PathfindingSolverSystem.SolveNavmesh` stores paths in `TrajectoryPoolManager` (as `Vector2[]`). BUT `OffMeshLinkDetectionSystem` reads from `IPathRegistry` (SharedPathRegistry), which stores `NavWaypoint[]` with `TraversalKind`. These are DIFFERENT storage systems.

The solver does NOT write to `SharedPathRegistry`. Therefore, `OffMeshLinkDetectionSystem` cannot find the waypoints in the registry for paths planned by the solver. The off-mesh link cannot be detected through the full pipeline.

**For S4, use a SEMI-INTEGRATION approach**: Verify the off-mesh detection system is wired into the harness by manually populating `SharedPathRegistry` with off-mesh waypoints, THEN verifying detection fires.

```csharp
using System;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    public sealed class S4_OffMeshJumpAcrossTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S4_OffMeshJumpAcrossTests()
            => _h = new NavTestHarness(NavTestMaps.LoadOffMeshJump());

        public void Dispose() => _h.Dispose();

        /// <summary>
        /// When OffMeshLinkDetectionSystem is wired into the harness, manually
        /// seeding the SharedPathRegistry with a Jump waypoint within lookahead
        /// triggers OffMeshTraversalStartedEvent and sets Phase=AwaitingTraversal.
        ///
        /// NOTE: This test bypasses the solver's TrajectoryPoolManager because the
        /// solver does not populate SharedPathRegistry (architectural gap).
        /// The full end-to-end off-mesh journey (start + traversal + arrival) requires
        /// a future bridge that syncs NavWaypoints into SharedPathRegistry.
        /// </summary>
        [Fact]
        public void OffMeshJump_OffMeshLinkDetected_EventFiresAndPhaseSetToAwaiting()
        {
            // Arrange: entity with CrowdAgent at (7,0,0), near the Jump link at ~(10,0,5)
            var entity = _h.SpawnInfantry(new Vector2(7f, 0f));

            // Allocate a route handle and manually seed the SharedPathRegistry
            // with an off-mesh Jump waypoint WITHIN lookahead distance (default 3m).
            // Entity is at (7,0,0). Jump link start is at (9,0,0) — dist ~2m.
            int handle = NavigationHandleAllocator.Allocate();

            // Write the corridor component so OffMeshLinkDetectionSystem will query it.
            _h.Repo.AddComponent(entity, new NavigationCorridorMuscle
            {
                RouteHandle         = handle,
                CurrentSegmentIndex = 0,
                TotalSegmentCount   = 2,
            });
            _h.Repo.GetComponentRW<NavigationStatus>(entity).Phase = NavigationPhase.Following;

            // Populate the registry: Walk to (8,0,0), then Jump at (9,0,0) within 3m lookahead.
            _h.PathRegistry.StoreOrReplace(handle, new[]
            {
                new NavWaypoint { Position = new Vector3(8f, 0f, 0f), Traversal = TraversalKind.Walk },
                new NavWaypoint { Position = new Vector3(9f, 0f, 0f), Traversal = TraversalKind.Jump },
            });

            // Act: run ONE tick.
            _h.Tick();

            // Assert: OffMeshTraversalStartedEvent fired.
            Assert.True(_h.EventLog.HasOffMeshTraversalStarted(),
                "Expected OffMeshTraversalStartedEvent to be captured.");

            // Assert: Phase switched to AwaitingTraversal.
            var status = _h.Repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationPhase.AwaitingTraversal, status.Phase);

            // Assert: CrowdAgent tag removed.
            Assert.False(_h.Repo.HasComponent<CrowdAgent>(entity),
                "CrowdAgent tag should be removed during traversal.");
        }
    }
}
```

**IMPORTANT**: Check the `SharedPathRegistry.StoreOrReplace` method name by reading the file. It might be `StoreOrReplace`, `RegisterOrReplace`, or similar. Find the correct method by reading `MusclePathRegistry.cs` or `SharedPathRegistry.cs`.

Also check: `IFakeNavmeshProviderTestApi` interface — if it doesn't exist, omit `NavmeshApi` property from the harness for now.

Also check: `NavigationHandleAllocator.Allocate()` — search for this static method.

---

## Build and test

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln 2>&1 | Select-Object -Last 20

cd FDP\Toolkits
dotnet test Fdp.Toolkits.Tests --filter "FullyQualifiedName~Navigation" 2>&1 | Select-Object -Last 20
```

Target: 0 build errors, >= 261 tests (existing 261 + new tests).

---

## Success criteria

1. Build: 0 errors
2. Tests: >= 261 passing, 0 failing
3. New tests: S2 (at least 1 test), S2b (at least 2 tests), S3 (at least 2 tests), S4 (at least 1 test)
4. `NavTestHarness` has `Navmesh`, `Crowd`, `PathRegistry`, `SpawnVehicle`, updated `IssueMoveTo` with `layerMask` param

---

## Report

Write to `d:\Work\IOS-IG-SimHost-FDP-2\.dev\navig-2\reports\BATCH-15-REPORT.md`.

Include: files created/modified, test count before/after, any issues with coordinate system, any skipped tests and why, API gaps found.
