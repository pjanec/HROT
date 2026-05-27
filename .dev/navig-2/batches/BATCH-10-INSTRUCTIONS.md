# BATCH-10: NAV-P5-T2 CorridorPreview + NAV-P6-T1/T2/T3 Engine-backed providers

**Batch Number:** BATCH-10
**Tasks:** NAV-P5-T2, NAV-P6-T1, NAV-P6-T2, NAV-P6-T3
**Phase:** Phase 5 (remaining) + Phase 6 (providers only)
**Estimated Effort:** 3-5 hours
**Priority:** HIGH
**Dependencies:** BATCH-09 (committed, hash 7945af16)

---

## Onboarding & Workflow

### Developer Instructions

BATCH-10 adds two independent feature groups:

**Group A — NAV-P5-T2:** Implements the `NavigationCorridorPreview` sliding 8-waypoint window.
When `intent.Flags` bit 3 (`StreamCorridorPreview`) is set, a new `CorridorPreviewSystem`
reads the active path from the `MusclePathRegistry` and populates the component on the entity.
When the flag is not set (or intent is cleared), the component is absent.

**Group B — NAV-P6-T1/T2/T3:** Implements the three stub engine-backed navigation providers:
`EngineBackedNavmeshProvider`, `EngineBackedDtCrowdProvider`, and `EngineBackedVolumetricPathProvider`.
All three live in `Fdp.Toolkit.Navigation.EngineBacked` and satisfy their respective interfaces.

### Required Reading (IN ORDER)

1. `.dev/navig-2/Navigation_Design_v2_0.md` §4.2 (CorridorPreview), §13.4 (flags)
2. `.dev/navig-2/DD-EngineBacked-Nav.md` §3 (EngineBackedNavmeshProvider), §4 (DtCrowd), §5 (Volumetric)
3. `.dev/navig-2/TASK-DETAILS.md` — NAV-P5-T2, NAV-P6-T1, NAV-P6-T2, NAV-P6-T3

### Source Code Locations

- **Interfaces:** `FDP/Toolkits/Fdp.Toolkits/Navigation/INavmeshProvider.cs`,
  `IVolumetricPathProvider.cs`, `IDtCrowdProvider.cs`
- **Components:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs`
  (see `NavigationCorridorPreview`, `NavigationCorridorMuscle`, `PreviewWaypoint`)
- **Constants:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationConstants.cs`
- **Path registry (Muscle side):** `FDP/Toolkits/Fdp.Toolkits/Navigation/MusclePathRegistry.cs`
- **Test factory:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactory.cs`
- **Test project:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/`

### Build & Test Command

```powershell
cd "d:\Work\IOS-IG-SimHost-FDP-2"
dotnet build "FDP\FDP.sln" --configuration Debug
dotnet test "FDP\Toolkits\Fdp.Toolkits.Tests" --filter "Navigation" --configuration Debug
```

### Report Submission

Create `.dev/navig-2/batches/BATCH-10-REPORT.md` when done.

---

## Group A — NAV-P5-T2: NavigationCorridorPreview

### A1. Add `FlagBitStreamCorridorPreview` constant

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationConstants.cs`

After `FlagBitAutoSendPathOnReplan`:

```csharp
/// <summary>
/// Bit index in <see cref="NavigationIntent.Flags"/>: stream the 8-waypoint
/// corridor preview to Brain via <see cref="NavigationCorridorPreview"/>.
/// </summary>
public const byte FlagBitStreamCorridorPreview = 3;
```

### A2. Create `CorridorPreviewSystem`

**New file:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/CorridorPreviewSystem.cs`

This system reads waypoints from `MusclePathRegistry` and populates (or removes)
`NavigationCorridorPreview` based on the flag in `NavigationIntent`.

Read `MusclePathRegistry.cs` to confirm the exact API. It has:
- `bool TryGetWaypoints(int handle, out ReadOnlySpan<NavWaypoint> waypoints)`

The system must run AFTER `NavigationIntentBridgeSystem` (so `NavigationCorridorMuscle`
has the latest `CurrentSegmentIndex`) and AFTER any replan events.

```csharp
using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Maintains the opt-in <see cref="NavigationCorridorPreview"/> sliding window (N=8).
    /// Present on an entity only when <see cref="NavigationConstants.FlagBitStreamCorridorPreview"/>
    /// is set in <see cref="NavigationIntent.Flags"/>; absent otherwise.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class CorridorPreviewSystem : IEcsModuleSystem
    {
        private readonly MusclePathRegistry _registry;

        public CorridorPreviewSystem(MusclePathRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(CorridorPreviewSystem)} requires direct EntityRepository access.");

            const byte previewBit = 1 << NavigationConstants.FlagBitStreamCorridorPreview;

            var query = repo.Query()
                .With<NavigationIntent>()
                .With<NavigationCorridorMuscle>()
                .Build();

            foreach (var entity in query)
            {
                var intent   = repo.GetComponent<NavigationIntent>(entity);
                var corridor = repo.GetComponent<NavigationCorridorMuscle>(entity);

                bool wantsPreview = (intent.Flags & previewBit) != 0;

                if (!wantsPreview)
                {
                    // Remove component if present.
                    if (repo.HasComponent<NavigationCorridorPreview>(entity))
                        repo.RemoveComponent<NavigationCorridorPreview>(entity);
                    continue;
                }

                if (corridor.RouteHandle == 0)
                {
                    // No active path — clear the preview.
                    if (repo.HasComponent<NavigationCorridorPreview>(entity))
                        repo.RemoveComponent<NavigationCorridorPreview>(entity);
                    continue;
                }

                // Read waypoints from the Muscle path registry.
                if (!_registry.TryGetWaypoints(corridor.RouteHandle, out var allWaypoints))
                {
                    if (repo.HasComponent<NavigationCorridorPreview>(entity))
                        repo.RemoveComponent<NavigationCorridorPreview>(entity);
                    continue;
                }

                int startIdx = Math.Max(0, corridor.CurrentSegmentIndex);
                int count    = Math.Min(8, allWaypoints.Length - startIdx);
                if (count <= 0)
                {
                    if (repo.HasComponent<NavigationCorridorPreview>(entity))
                        repo.RemoveComponent<NavigationCorridorPreview>(entity);
                    continue;
                }

                // Build the new preview value.
                var newPreview = BuildPreview(allWaypoints, startIdx, count);

                if (repo.HasComponent<NavigationCorridorPreview>(entity))
                {
                    var existing = repo.GetComponent<NavigationCorridorPreview>(entity);
                    // Bump PreviewVersion only if the window changed.
                    if (existing.GlobalSegmentStart != startIdx || existing.WaypointCount != count)
                    {
                        newPreview.PreviewVersion = existing.PreviewVersion + 1;
                        repo.SetComponent(entity, newPreview);
                    }
                }
                else
                {
                    newPreview.PreviewVersion = 1;
                    repo.AddComponent(entity, newPreview);
                }
            }
        }

        private static NavigationCorridorPreview BuildPreview(
            ReadOnlySpan<NavWaypoint> all, int startIdx, int count)
        {
            var p = new NavigationCorridorPreview
            {
                GlobalSegmentStart = startIdx,
                WaypointCount      = count,
            };

            // Inline assignment of up to 8 waypoints.
            if (count > 0) p.W0 = ToPreview(all[startIdx + 0]);
            if (count > 1) p.W1 = ToPreview(all[startIdx + 1]);
            if (count > 2) p.W2 = ToPreview(all[startIdx + 2]);
            if (count > 3) p.W3 = ToPreview(all[startIdx + 3]);
            if (count > 4) p.W4 = ToPreview(all[startIdx + 4]);
            if (count > 5) p.W5 = ToPreview(all[startIdx + 5]);
            if (count > 6) p.W6 = ToPreview(all[startIdx + 6]);
            if (count > 7) p.W7 = ToPreview(all[startIdx + 7]);

            return p;
        }

        private static PreviewWaypoint ToPreview(in NavWaypoint wp) => new PreviewWaypoint
        {
            Position = wp.Position,
            Traversal = wp.TraversalKind,
            Surface   = wp.SurfaceType,
        };
    }
}
```

**Important:** Before implementing, read `NavWaypoint` fields in `NavigationComponents.cs`
to confirm field names (`TraversalKind`, `SurfaceType`, `Position`). Also read
`MusclePathRegistry.TryGetWaypoints` signature.

Also check whether `repo.HasComponent<T>` and `repo.RemoveComponent<T>` exist in
`EntityRepository`. If `RemoveComponent` is not available, use the pattern used in
other systems (may be `repo.RemoveComponentIfPresent` or conditional).

### A3. Register `NavigationCorridorPreview` in the test world

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactory.cs`

Add:
```csharp
world.RegisterComponent<NavigationCorridorPreview>();
world.RegisterComponent<NavigationCorridorMuscle>();  // if not already present
```

### A4. Create `CorridorPreviewSystemTests.cs`

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/CorridorPreviewSystemTests.cs`

**Namespace:** `Fdp.Toolkit.Navigation.Tests`

This file needs 6 tests:

#### Test helper: `CreateEntityWithCorridor`

```csharp
private static Entity CreateEntityWithCorridor(
    EntityRepository repo,
    MusclePathRegistry registry,
    int routeHandle,
    int totalWaypoints,
    int currentSegment = 0,
    byte intentFlags = 0)
{
    // Register a path with `totalWaypoints` waypoints.
    var waypoints = new NavWaypoint[totalWaypoints];
    for (int i = 0; i < totalWaypoints; i++)
        waypoints[i] = new NavWaypoint
        {
            Position = new System.Numerics.Vector3(i * 10f, 0f, 0f),
            TraversalKind = TraversalKind.Walk,
            SurfaceType   = SurfaceType.Default,
        };
    registry.Register(routeHandle, waypoints);

    var entity = repo.CreateEntity();
    repo.AddComponent(entity, new NavigationIntent
    {
        Mode     = NavigationMode.DirectPoint,
        IntentId = 1,
        Flags    = intentFlags,
    });
    repo.AddComponent(entity, new NavigationCorridorMuscle
    {
        RouteHandle          = routeHandle,
        CurrentSegmentIndex  = currentSegment,
        TotalSegmentCount    = totalWaypoints,
    });
    return entity;
}
```

Note: check `MusclePathRegistry.Register` signature (it may be `RegisterOrReplace` or accept
a span). Read the source file before writing the test.

#### Test 1: `StreamFlag_Set_PopulatesComponent`

```csharp
[Fact]
public void StreamFlag_Set_PopulatesComponent()
{
    using var repo     = NavigationTestWorldFactory.Create();
    var view           = (ISimulationView)repo;
    var registry       = new MusclePathRegistry();
    var sys            = new CorridorPreviewSystem(registry);

    const byte previewBit = 1 << NavigationConstants.FlagBitStreamCorridorPreview;
    var entity = CreateEntityWithCorridor(repo, registry,
                     routeHandle: 1, totalWaypoints: 10, intentFlags: previewBit);

    sys.Execute(view, 0.016f);

    Assert.True(repo.HasComponent<NavigationCorridorPreview>(entity));
    var preview = repo.GetComponent<NavigationCorridorPreview>(entity);
    Assert.True(preview.WaypointCount > 0);
    Assert.Equal(0, preview.GlobalSegmentStart);
}
```

#### Test 2: `StreamFlag_NotSet_ComponentAbsent`

```csharp
[Fact]
public void StreamFlag_NotSet_ComponentAbsent()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view       = (ISimulationView)repo;
    var registry   = new MusclePathRegistry();
    var sys        = new CorridorPreviewSystem(registry);

    var entity = CreateEntityWithCorridor(repo, registry,
                     routeHandle: 1, totalWaypoints: 10, intentFlags: 0 /* no flag */);

    sys.Execute(view, 0.016f);

    Assert.False(repo.HasComponent<NavigationCorridorPreview>(entity));
}
```

#### Test 3: `WaypointCount_Capped_At8`

```csharp
[Fact]
public void WaypointCount_Capped_At8()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view       = (ISimulationView)repo;
    var registry   = new MusclePathRegistry();
    var sys        = new CorridorPreviewSystem(registry);

    const byte previewBit = 1 << NavigationConstants.FlagBitStreamCorridorPreview;
    var entity = CreateEntityWithCorridor(repo, registry,
                     routeHandle: 1, totalWaypoints: 20, intentFlags: previewBit);

    sys.Execute(view, 0.016f);

    var preview = repo.GetComponent<NavigationCorridorPreview>(entity);
    Assert.Equal(8, preview.WaypointCount);
}
```

#### Test 4: `SegmentAdvance_BumpsPreviewVersion`

```csharp
[Fact]
public void SegmentAdvance_BumpsPreviewVersion()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view       = (ISimulationView)repo;
    var registry   = new MusclePathRegistry();
    var sys        = new CorridorPreviewSystem(registry);

    const byte previewBit = 1 << NavigationConstants.FlagBitStreamCorridorPreview;
    var entity = CreateEntityWithCorridor(repo, registry,
                     routeHandle: 1, totalWaypoints: 20,
                     currentSegment: 0, intentFlags: previewBit);

    sys.Execute(view, 0.016f);
    uint version1 = repo.GetComponent<NavigationCorridorPreview>(entity).PreviewVersion;

    // Advance the corridor.
    var corridor = repo.GetComponent<NavigationCorridorMuscle>(entity);
    corridor.CurrentSegmentIndex = 3;
    repo.SetComponent(entity, corridor);

    sys.Execute(view, 0.016f);
    uint version2 = repo.GetComponent<NavigationCorridorPreview>(entity).PreviewVersion;

    Assert.True(version2 > version1);
    Assert.Equal(3, repo.GetComponent<NavigationCorridorPreview>(entity).GlobalSegmentStart);
}
```

#### Test 5: `FlagCleared_RemovesComponent`

```csharp
[Fact]
public void FlagCleared_RemovesComponent()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view       = (ISimulationView)repo;
    var registry   = new MusclePathRegistry();
    var sys        = new CorridorPreviewSystem(registry);

    const byte previewBit = 1 << NavigationConstants.FlagBitStreamCorridorPreview;
    var entity = CreateEntityWithCorridor(repo, registry,
                     routeHandle: 1, totalWaypoints: 10, intentFlags: previewBit);

    sys.Execute(view, 0.016f);
    Assert.True(repo.HasComponent<NavigationCorridorPreview>(entity));

    // Clear the flag.
    var intent = repo.GetComponent<NavigationIntent>(entity);
    intent.Flags = 0;
    repo.SetComponent(entity, intent);

    sys.Execute(view, 0.016f);
    Assert.False(repo.HasComponent<NavigationCorridorPreview>(entity));
}
```

#### Test 6: `InvalidRouteHandle_NoComponent`

```csharp
[Fact]
public void InvalidRouteHandle_NoComponent()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view       = (ISimulationView)repo;
    var registry   = new MusclePathRegistry();
    var sys        = new CorridorPreviewSystem(registry);

    const byte previewBit = 1 << NavigationConstants.FlagBitStreamCorridorPreview;
    var entity = repo.CreateEntity();
    repo.AddComponent(entity, new NavigationIntent
    {
        Mode     = NavigationMode.DirectPoint,
        IntentId = 1,
        Flags    = previewBit,
    });
    // RouteHandle = 99 is not registered in the registry.
    repo.AddComponent(entity, new NavigationCorridorMuscle
    {
        RouteHandle         = 99,
        CurrentSegmentIndex = 0,
        TotalSegmentCount   = 0,
    });

    sys.Execute(view, 0.016f);

    Assert.False(repo.HasComponent<NavigationCorridorPreview>(entity));
}
```

---

## Group B — NAV-P6-T1/T2/T3: Engine-backed providers

### B1. Create `EngineBackedNavmeshProvider`

**New file:** `FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedNavmeshProvider.cs`

Read `INavmeshProvider.cs` to confirm all method signatures before implementing.
The interface uses `Vector3` (not `Vector2`) and `uint layerMask`.

```csharp
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Navigation.EngineBacked
{
    /// <summary>
    /// Direct-line placeholder navmesh provider for engine-backed scenarios.
    /// All walkability queries return true; <c>PlanPath</c> returns a straight two-waypoint path.
    /// </summary>
    public sealed class EngineBackedNavmeshProvider : INavmeshProvider
    {
        /// <inheritdoc/>
        public bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF) => true;

        /// <inheritdoc/>
        public bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF)
        {
            snapped = position;
            return true;
        }

        /// <inheritdoc/>
        public int SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF)
        {
            // Return the center point as the only sample for simplicity.
            if (results.Length > 0)
            {
                results[0] = center;
                return 1;
            }
            return 0;
        }

        /// <inheritdoc/>
        public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => true;

        /// <inheritdoc/>
        public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF)
            => Vector3.Distance(from, to);

        /// <inheritdoc/>
        public uint QueryVersion() => 1;

        /// <inheritdoc/>
        public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF)
        {
            if (waypoints.Length < 2) return 0;
            waypoints[0] = new NavWaypoint
            {
                Position            = from,
                TraversalKind       = TraversalKind.Walk,
                SurfaceType         = SurfaceType.Default,
                LayerMask           = layerMask,
                SegmentLengthMeters = 0f,
            };
            waypoints[1] = new NavWaypoint
            {
                Position            = to,
                TraversalKind       = TraversalKind.Walk,
                SurfaceType         = SurfaceType.Default,
                LayerMask           = layerMask,
                SegmentLengthMeters = Vector3.Distance(from, to),
            };
            return 2;
        }
    }
}
```

**IMPORTANT:** Before writing field names in `NavWaypoint` (e.g., `TraversalKind`, `SurfaceType`,
`LayerMask`, `SegmentLengthMeters`), read `NavigationComponents.cs` to verify the exact field names.

### B2. Create `EngineBackedDtCrowdProvider`

**New file:** `FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedDtCrowdProvider.cs`

Read `IDtCrowdProvider.cs` to confirm all method signatures.

```csharp
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Navigation.EngineBacked
{
    /// <summary>
    /// No-op crowd provider stub for engine-backed scenarios.
    /// All methods are safe no-ops; <c>GetAgentVelocity</c> always returns Zero.
    /// Humanoid navigation in this mode is handled by <c>LinearKinematicsSystem</c>.
    /// </summary>
    public sealed class EngineBackedDtCrowdProvider : IDtCrowdProvider
    {
        /// <inheritdoc/>
        public bool RegisterAgent(Entity entity, in CrowdAgentParams parameters) => true;

        /// <inheritdoc/>
        public void UnregisterAgent(Entity entity) { }

        /// <inheritdoc/>
        public void SetAgentTarget(Entity entity, Vector3 target) { }

        /// <inheritdoc/>
        public void Update(float dt, ISimulationView view) { }

        /// <inheritdoc/>
        public Vector3 GetAgentVelocity(Entity entity) => Vector3.Zero;

        /// <inheritdoc/>
        public bool TryGetAgentSnapshot(Entity entity, out CrowdAgentSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }
    }
}
```

### B3. Create `EngineBackedVolumetricPathProvider`

**New file:** `FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedVolumetricPathProvider.cs`

Read `IVolumetricPathProvider.cs` to confirm all method signatures. Note that the interface has
default implementations for some methods (they throw `NotSupportedException`), so only override
the non-default ones plus the optional ones needed for engine-backed.

```csharp
using System;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake; // FlyProfile may be here

namespace Fdp.Toolkit.Navigation.EngineBacked
{
    /// <summary>
    /// Direct-line 3D volumetric path provider for engine-backed scenarios.
    /// <c>Plan</c> returns two waypoints (start and end). All positions are flyable.
    /// </summary>
    public sealed class EngineBackedVolumetricPathProvider : IVolumetricPathProvider
    {
        /// <inheritdoc/>
        public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints)
        {
            if (waypoints.Length < 2) return 0;
            waypoints[0] = new NavWaypoint
            {
                Position            = from,
                TraversalKind       = TraversalKind.Fly,
                SurfaceType         = SurfaceType.Default,
                SegmentLengthMeters = 0f,
            };
            waypoints[1] = new NavWaypoint
            {
                Position            = to,
                TraversalKind       = TraversalKind.Fly,
                SurfaceType         = SurfaceType.Default,
                SegmentLengthMeters = Vector3.Distance(from, to),
            };
            return 2;
        }

        /// <inheritdoc/>
        public uint QueryVersion() => 1;

        /// <inheritdoc/>
        public bool IsFlyable(Vector3 position) => true;

        /// <inheritdoc/>
        public bool PathExists(Vector3 from, Vector3 to, FlyProfile profile, float maxCost = 0f) => true;
    }
}
```

**Note:** Check if `TraversalKind.Fly` exists. Look in `NavigationComponents.cs` or
`NavigationEnums.cs` for the enum values. If `Fly` doesn't exist, use `Walk` or check
the actual enum. Also verify `FlyProfile`'s namespace (may be `Fdp.Toolkit.Navigation`
without `.Fake`).

---

## Group B Tests: Create `EngineBackedProviderTests.cs`

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/EngineBackedProviderTests.cs`

**Namespace:** `Fdp.Toolkit.Navigation.Tests`

Tests for all three providers — 9 tests total.

```csharp
using System;
using System.Numerics;
using Fdp.Toolkit.Navigation.EngineBacked;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    public class EngineBackedNavmeshProviderTests
    {
        [Fact]
        public void IsWalkable_AnyPoint_ReturnsTrue()
        {
            var p = new EngineBackedNavmeshProvider();
            Assert.True(p.IsWalkable(new Vector3(999f, 0f, 999f)));
        }

        [Fact]
        public void ProjectToNavmesh_PreservesInputPosition()
        {
            var p = new EngineBackedNavmeshProvider();
            Assert.True(p.ProjectToNavmesh(new Vector3(5f, 10f, 3f), out var snapped));
            Assert.Equal(new Vector3(5f, 10f, 3f), snapped);
        }

        [Fact]
        public void PathCost_ReturnsEuclideanDistance()
        {
            var p    = new EngineBackedNavmeshProvider();
            var from = new Vector3(0f, 0f, 0f);
            var to   = new Vector3(3f, 0f, 4f); // 5 metres away
            Assert.Equal(5f, p.PathCost(from, to), precision: 4);
        }

        [Fact]
        public void QueryVersion_ReturnsOne()
        {
            var p = new EngineBackedNavmeshProvider();
            Assert.Equal(1u, p.QueryVersion());
        }

        [Fact]
        public void PlanPath_ReturnsTwoWaypoints_StartAndEnd()
        {
            var p = new EngineBackedNavmeshProvider();
            var from = new Vector3(0f, 0f, 0f);
            var to   = new Vector3(10f, 0f, 0f);
            var buf  = new NavWaypoint[4];

            int count = p.PlanPath(from, to, buf);

            Assert.Equal(2, count);
            Assert.Equal(from, buf[0].Position);
            Assert.Equal(to, buf[1].Position);
        }

        [Fact]
        public void PlanPath_SmallBuffer_ReturnsZero()
        {
            var p   = new EngineBackedNavmeshProvider();
            var buf = new NavWaypoint[1];
            Assert.Equal(0, p.PlanPath(Vector3.Zero, Vector3.One, buf));
        }
    }

    public class EngineBackedDtCrowdProviderTests
    {
        [Fact]
        public void GetAgentVelocity_ReturnsZero()
        {
            var p = new EngineBackedDtCrowdProvider();
            Assert.Equal(Vector3.Zero, p.GetAgentVelocity(new Fdp.Core.Entity(1, 0)));
        }

        [Fact]
        public void RegisterAgent_ReturnsTrue()
        {
            var p = new EngineBackedDtCrowdProvider();
            var result = p.RegisterAgent(
                new Fdp.Core.Entity(1, 0),
                new CrowdAgentParams { Radius = 0.5f, MaxSpeed = 5f });
            Assert.True(result);
        }

        [Fact]
        public void TryGetAgentSnapshot_ReturnsFalse()
        {
            var p = new EngineBackedDtCrowdProvider();
            Assert.False(p.TryGetAgentSnapshot(new Fdp.Core.Entity(1, 0), out _));
        }
    }

    public class EngineBackedVolumetricPathProviderTests
    {
        [Fact]
        public void IsFlyable_AnyPoint_ReturnsTrue()
        {
            var p = new EngineBackedVolumetricPathProvider();
            Assert.True(p.IsFlyable(new Vector3(0f, 100f, 0f)));
        }

        [Fact]
        public void PlanPath_ReturnsTwoWaypoints()
        {
            var p   = new EngineBackedVolumetricPathProvider();
            var buf = new NavWaypoint[4];
            int cnt = p.PlanPath(Vector3.Zero, new Vector3(0f, 10f, 0f), buf);
            Assert.Equal(2, cnt);
        }

        [Fact]
        public void PlanPath_SmallBuffer_ReturnsZero()
        {
            var p   = new EngineBackedVolumetricPathProvider();
            var buf = new NavWaypoint[1];
            Assert.Equal(0, p.PlanPath(Vector3.Zero, Vector3.One, buf));
        }
    }
}
```

---

## EntityRepository API Reference

Before writing `CorridorPreviewSystem`, check these methods exist on `EntityRepository`:
- `repo.HasComponent<T>(Entity)` — check if component present
- `repo.AddComponent<T>(Entity, T)` — add component
- `repo.SetComponent<T>(Entity, T)` — update component
- `repo.RemoveComponent<T>(Entity)` — remove component

If `RemoveComponent` uses a different name, check other systems that remove components
(e.g., `OffMeshLinkDetectionSystem` removing `CrowdAgent`).

---

## MusclePathRegistry API Reference

Before writing `CorridorPreviewSystem`, read `MusclePathRegistry.cs` to confirm:
- The method to register a path: `Register(int handle, NavWaypoint[] waypoints)` or `RegisterOrReplace(...)`
- The method to get waypoints: `TryGetWaypoints(int handle, out ReadOnlySpan<NavWaypoint> waypoints)` or similar

---

## TraversalKind Enum

Check the `TraversalKind` enum in `NavigationComponents.cs`. If `Fly` doesn't exist, use
whatever value represents air travel (check existing fake volumetric tests for reference).

---

## Verification

After implementing all tasks:

1. Build: `dotnet build "FDP\FDP.sln" --configuration Debug` — must have **0 errors**.
2. Test: `dotnet test "FDP\Toolkits\Fdp.Toolkits.Tests" --filter "Navigation" --configuration Debug`
   — must have **0 failures**.

Expected total Navigation test count: ~229 (214 existing + 6 CorridorPreview + 9 EngineBacked).
