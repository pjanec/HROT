# BATCH-11 Implementation Instructions

## Scope
NAV-P6-T4 (`EngineBackedPathRegistry`) + NAV-P6-T5 (`EngineBackedNavigationModule` + `EngineBackedPathResponseSystem`)

**Current test count**: 232 passing (after BATCH-10, commit `55d78dc0`)  
**Target**: ≥ 248 passing (+16 minimum)

---

## Context

### Existing types to know
- `TrajectoryPoolManager` (namespace `CarKinem.Trajectory`):
  - `RegisterTrajectoryWithKey(Vector2[] positions, int key)` — stores a linear trajectory keyed by `key`
  - `RemoveTrajectory(int id) → bool`
  - `TryGetTrajectory(int id, out CustomTrajectory traj) → bool`
- `CustomTrajectory`: has `NativeArray<TrajectoryWaypoint> Waypoints`, `float TotalLength`
- `TrajectoryWaypoint`: `Vector2 Position`, `Vector2 Tangent`, `float DesiredSpeed`, `float CumulativeDistance`
- `NavWaypoint` (namespace `Fdp.Toolkit.Navigation`): `Vector3 Position { get; init; }`, `TraversalKind Traversal { get; init; }`, `SurfaceType Surface { get; init; }` (readonly struct)
- `IPathRegistry` (namespace `Fdp.Toolkit.Navigation`): `IsCached(int)`, `TryGetSummary(int, out PathSummary)`, `TryGetWaypoints(int, Span<NavWaypoint>, out int)`, `TryGetWaypointsSlice(int, int, int, Span<NavWaypoint>, out int)`
- `PathSummary`: struct with `RouteHandle`, `TotalDistanceMeters`, `WaypointCount`, `NavmeshVersionAtPlan`, `PrimaryBackend` (byte), `Flags` (byte), `ReplanCount` (byte)
- `NavigationFakesModule` (namespace `Fdp.Toolkit.Navigation.Fake`): has `RegisterProviders(EntityRepository repo)` which calls `repo.SetSingletonManaged<INavmeshProvider>(Navmesh)`
- `PathfindingResultEvent` (namespace `Fdp.Toolkit.Navigation`): `long RequestId`, `bool IsReachable`, `float TotalDistanceMeters`, `int RouteHandle`, `int SourceNodeId`, `int NavmeshVersionAtPlan`, `NavigationBackend PrimaryBackend`, `NavigationFailureReason FailureReason`
- `MoveStartedEvent` (namespace `Fdp.Toolkit.Navigation`): `long RequestId`, `int RouteHandle`, `int SourceNodeId`
- `NavigationCorridorMuscle` (namespace `Fdp.Toolkit.Navigation`): `int RouteHandle`, `uint NavmeshVersion`, `int CurrentSegmentIndex`, `int TotalSegmentCount`, `float TotalDistance`, `byte PrimaryBackend`, `byte Flags`, pad bytes
- `KinematicsMode` enum: has `CustomTrajectory`, `DirectPoint`, `None`
- `NavState` (ECS component): `KinematicsMode Mode`, `int TrajectoryId`, `float ProgressS`, `bool HasArrived`
- `INavmeshProvider`, `IDtCrowdProvider`, `IVolumetricPathProvider` — interfaces for providers
- `EngineBackedNavmeshProvider`, `EngineBackedDtCrowdProvider`, `EngineBackedVolumetricPathProvider` — already exist in `Fdp.Toolkit.Navigation.EngineBacked`
- `NavigationBackend` enum (byte): `Auto=0`, `NavRoadGraph`, `Navmesh`, `Volumetric`
- `IEcsModuleSystem` — `void Execute(ISimulationView view, float deltaTime)`
- `IEcsModule` — `string Name { get; }`, `ExecutionPolicy Policy { get; }`, `void RegisterSystems(ISystemRegistry registry)`, `void Tick(ISimulationView view, float deltaTime)`
- `ExecutionPolicy.Synchronous()` — already used by `NavigationFakesModule`
- `EntityRepository` — `GetSingletonManaged<T>()` returns null if not present; `SetSingletonManaged<T>(value)`

### Key architectural understanding
- `PathfindingSolverSystem.SolvePath` and `SolveNavmesh` **already call** `_trajectoryPool.RegisterTrajectoryWithKey(positions, handle)` before publishing `PathfindingResultEvent`. So when `EngineBackedPathResponseSystem` processes the result event, the trajectory is ALREADY in the pool.
- `PathfindingResultMaterializationSystem` (always registered via `NavigationSolverModule`) handles: `NavigationCorridorMuscle`, `NavigationStatus`, `MoveStartedEvent` publication. Do NOT duplicate these in `EngineBackedPathResponseSystem`.
- `EngineBackedPathResponseSystem` only needs to:
  1. Read trajectory from pool → convert to `NavWaypoint[]` → register in `EngineBackedPathRegistry`
  2. Set `NavState.TrajectoryId = handle` and `NavState.Mode = KinematicsMode.CustomTrajectory`

### Position coordinate convention
- `TrajectoryWaypoint.Position` is `Vector2(X=East, Y=North)` (2D flat world)
- `NavWaypoint.Position` is `Vector3(X=East, Y=0, Z=North)` — when converting from `TrajectoryWaypoint`, use `new Vector3(tw.Position.X, 0f, tw.Position.Y)`

---

## Task 1: `EngineBackedPathRegistry` (NAV-P6-T4)

### File to create
`FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedPathRegistry.cs`

### Namespace
`Fdp.Toolkit.Navigation.EngineBacked`

### Design
```
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using CarKinem.Trajectory;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation.EngineBacked
{
    /// <summary>
    /// Real <see cref="IPathRegistry"/> adapter backed by <see cref="TrajectoryPoolManager"/>.
    /// RouteHandle equals NavState.TrajectoryId. All-in-one mode only.
    /// </summary>
    public sealed class EngineBackedPathRegistry : IPathRegistry
    {
        private readonly TrajectoryPoolManager _pool;

        // Metadata per registered handle (not stored in pool).
        private record struct EntryMeta(byte ReplanCount, float TotalDistanceMeters, byte PrimaryBackend);
        private readonly Dictionary<int, EntryMeta> _meta = new();
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

        private long _hits;
        private long _misses;
        private long _staleMisses;

        public EngineBackedPathRegistry(TrajectoryPoolManager pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        // ── Registration API (not part of IPathRegistry) ─────────────────────

        /// <summary>
        /// Register (or replace) an entry. The caller must have already populated the
        /// trajectory in the pool via RegisterTrajectoryWithKey before calling this.
        /// </summary>
        public void Register(int handle, byte replanCount = 0,
                             float totalDistanceMeters = 0f, byte primaryBackend = 0)
        {
            _lock.EnterWriteLock();
            try { _meta[handle] = new EntryMeta(replanCount, totalDistanceMeters, primaryBackend); }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>Remove entry from registry and from pool.</summary>
        public bool Free(int handle)
        {
            _lock.EnterWriteLock();
            try
            {
                bool had = _meta.Remove(handle);
                _pool.RemoveTrajectory(handle);
                return had;
            }
            finally { _lock.ExitWriteLock(); }
        }

        // ── IPathRegistry ────────────────────────────────────────────────────

        public bool IsCached(int routeHandle)
        {
            _lock.EnterReadLock();
            try
            {
                bool found = _meta.ContainsKey(routeHandle) && _pool.TryGetTrajectory(routeHandle, out _);
                if (found) Interlocked.Increment(ref _hits); else Interlocked.Increment(ref _misses);
                return found;
            }
            finally { _lock.ExitReadLock(); }
        }

        public bool TryGetSummary(int routeHandle, out PathSummary summary)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_meta.TryGetValue(routeHandle, out var m) || !_pool.TryGetTrajectory(routeHandle, out var traj))
                {
                    Interlocked.Increment(ref _misses);
                    summary = default;
                    return false;
                }
                Interlocked.Increment(ref _hits);
                summary = new PathSummary
                {
                    RouteHandle         = routeHandle,
                    TotalDistanceMeters = m.TotalDistanceMeters > 0f ? m.TotalDistanceMeters : traj.TotalLength,
                    WaypointCount       = traj.Waypoints.Length,
                    PrimaryBackend      = m.PrimaryBackend,
                    ReplanCount         = m.ReplanCount,
                };
                return true;
            }
            finally { _lock.ExitReadLock(); }
        }

        public bool TryGetWaypoints(int routeHandle, Span<NavWaypoint> dest, out int count)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_meta.ContainsKey(routeHandle) || !_pool.TryGetTrajectory(routeHandle, out var traj))
                {
                    Interlocked.Increment(ref _misses);
                    count = 0;
                    return false;
                }
                Interlocked.Increment(ref _hits);
                count = Math.Min(traj.Waypoints.Length, dest.Length);
                for (int i = 0; i < count; i++)
                {
                    var tw = traj.Waypoints[i];
                    dest[i] = new NavWaypoint
                    {
                        Position  = new Vector3(tw.Position.X, 0f, tw.Position.Y),
                        Traversal = TraversalKind.Walk,
                        Surface   = SurfaceType.Generic,
                    };
                }
                return true;
            }
            finally { _lock.ExitReadLock(); }
        }

        public bool TryGetWaypointsSlice(int routeHandle, int startSegment, int maxCount,
                                         Span<NavWaypoint> dest, out int actualCount)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_meta.ContainsKey(routeHandle) || !_pool.TryGetTrajectory(routeHandle, out var traj))
                {
                    Interlocked.Increment(ref _misses);
                    actualCount = 0;
                    return false;
                }
                Interlocked.Increment(ref _hits);
                int start = Math.Max(0, startSegment);
                int available = traj.Waypoints.Length - start;
                actualCount = Math.Min(Math.Min(available, maxCount), dest.Length);
                if (actualCount <= 0) { actualCount = 0; return false; }
                for (int i = 0; i < actualCount; i++)
                {
                    var tw = traj.Waypoints[start + i];
                    dest[i] = new NavWaypoint
                    {
                        Position  = new Vector3(tw.Position.X, 0f, tw.Position.Y),
                        Traversal = TraversalKind.Walk,
                        Surface   = SurfaceType.Generic,
                    };
                }
                return true;
            }
            finally { _lock.ExitReadLock(); }
        }

        // ── Replan-aware overload (strict cache-miss policy) ─────────────────

        /// <summary>
        /// ReplanCount-aware lookup. Returns false (stale miss) if stored ReplanCount
        /// doesn't match <paramref name="expectedReplanCount"/>.
        /// </summary>
        public bool TryGetWaypoints(int routeHandle, byte expectedReplanCount,
                                    Span<NavWaypoint> dest, out int count)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_meta.TryGetValue(routeHandle, out var m))
                {
                    Interlocked.Increment(ref _misses);
                    count = 0;
                    return false;
                }
                if (m.ReplanCount != expectedReplanCount)
                {
                    Interlocked.Increment(ref _staleMisses);
                    count = 0;
                    return false;
                }
            }
            finally { _lock.ExitReadLock(); }

            // Delegate to the standard overload for the actual read.
            return TryGetWaypoints(routeHandle, dest, out count);
        }
    }
}
```

**IMPORTANT notes**:
- `PathSummary` may have slightly different field names. Look at `MusclePathRegistry.TryGetSummary` to see exact field names and copy that pattern.
- `TraversalKind.Walk` — check the enum values; it may be just `Walk` or `TraversalKind.Walk`.
- `SurfaceType.Generic` — check the enum; it may be `Generic`, `Default`, or another name.
- Look at `EngineBackedNavmeshProvider.PlanPath` in the existing file to see what `SurfaceType` and `TraversalKind` values were used — use the SAME values.
- The `ReaderWriterLockSlim` is used for thread safety. `TryGetWaypoints(handle, expectedReplanCount, ...)` first checks meta under read lock, then calls the standard overload (which also takes read lock). This double-lock is safe because `ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)` will fail on reentrance — use `LockRecursionPolicy.SupportsRecursion` instead, OR refactor to not call the overload from within a lock. The simplest fix: extract a private `ReadWaypointsFromPool(int handle, Span<NavWaypoint> dest, out int count)` that does the actual pool read WITHOUT locking, and call it from both overloads (which both hold the read lock themselves).

---

## Task 2: `EngineBackedPathResponseSystem` (NAV-P6-T5)

### File to create
`FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedPathResponseSystem.cs`

### Design
```
[UpdateInPhase(SystemPhase.Input)]  // runs same phase as PathfindingResultMaterializationSystem
public sealed class EngineBackedPathResponseSystem : IEcsModuleSystem
{
    private readonly EngineBackedPathRegistry _registry;

    public EngineBackedPathResponseSystem(EngineBackedPathRegistry registry)
    {
        _registry = registry;
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        var events = view.ReadEvents<PathfindingResultEvent>();
        if (events.IsEmpty) return;
        if (view is not EntityRepository repo) return;

        for (int i = 0; i < events.Length; i++)
        {
            ref readonly var evt = ref events[i];
            if (!evt.IsReachable) continue;

            int handle = evt.RouteHandle;

            // Register in path registry (pool already populated by PathfindingSolverSystem).
            _registry.Register(handle,
                replanCount: 0,
                totalDistanceMeters: evt.TotalDistanceMeters,
                primaryBackend: (byte)evt.PrimaryBackend);

            // Wire NavState so CarKinematicsSystem picks up the trajectory.
            int entityIndex = (int)((ulong)evt.RequestId >> 32);
            var entity = repo.GetEntityByIndex(entityIndex);
            if (!repo.IsAlive(entity)) continue;
            if (!repo.HasComponent<NavState>(entity)) continue;

            ref var navState = ref repo.GetComponentRW<NavState>(entity);
            navState.TrajectoryId = handle;
            navState.Mode         = KinematicsMode.CustomTrajectory;
            navState.ProgressS    = 0f;
            navState.HasArrived   = false;
        }
    }
}
```

**IMPORTANT**:
- `NavState` is a component type. Check `FDP/Toolkits/Fdp.Toolkits/CarKinem/` for the exact struct and its fields (may be `KinematicsMode`, `int TrajectoryId`, etc.)
- Use `repo.GetComponentRW<NavState>(entity)` (or `repo.GetComponent<NavState>(entity)` if there's no RW variant — check how other systems do it)
- The `NavState.Mode` field type may be `KinematicsMode` — check the existing `NavState` struct
- `KinematicsMode.CustomTrajectory` — verify enum name by searching for it in the codebase

---

## Task 3: `EngineBackedNavigationModule` (NAV-P6-T5)

### File to create
`FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedNavigationModule.cs`

### Design
```
using System;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.EngineBacked
{
    /// <summary>
    /// All-in-one ECS module wiring engine-backed navigation providers.
    /// Mutually exclusive with NavigationFakesModule.
    /// </summary>
    public sealed class EngineBackedNavigationModule : IEcsModule, IDisposable
    {
        public string Name => "EngineBackedNavigationModule";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly RoadNetworkBlob        _roadNetwork;
        private readonly TrajectoryPoolManager  _pool;

        private EngineBackedNavmeshProvider?        _navmesh;
        private EngineBackedDtCrowdProvider?        _crowd;
        private EngineBackedVolumetricPathProvider? _volumetric;
        private EngineBackedPathRegistry?           _registry;
        private EngineBackedPathResponseSystem?     _responseSystem;

        public EngineBackedNavigationModule(RoadNetworkBlob roadNetwork, TrajectoryPoolManager pool)
        {
            _roadNetwork = roadNetwork;
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        public void RegisterSystems(ISystemRegistry reg)
        {
            _navmesh        = new EngineBackedNavmeshProvider();
            _crowd          = new EngineBackedDtCrowdProvider();
            _volumetric     = new EngineBackedVolumetricPathProvider();
            _registry       = new EngineBackedPathRegistry(_pool);
            _responseSystem = new EngineBackedPathResponseSystem(_registry);

            reg.RegisterSystem(_responseSystem);
        }

        /// <summary>
        /// Register providers into the ECS world. Throws if providers are already registered
        /// (mutual exclusion with NavigationFakesModule).
        /// </summary>
        public void RegisterProviders(EntityRepository repo)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));

            // Mutual exclusion guard.
            if (repo.GetSingletonManaged<INavmeshProvider>() != null)
                throw new InvalidOperationException(
                    "Navigation providers are already registered. " +
                    "Only one of EngineBackedNavigationModule / NavigationFakesModule may be active.");

            // RegisterSystems must have been called first.
            if (_navmesh == null || _registry == null)
                throw new InvalidOperationException(
                    "Call RegisterSystems before RegisterProviders.");

            repo.SetSingletonManaged<INavmeshProvider>(_navmesh);
            repo.SetSingletonManaged<IDtCrowdProvider>(_crowd!);
            repo.SetSingletonManaged<IVolumetricPathProvider>(_volumetric!);
            repo.SetSingletonManaged<IPathRegistry>(_registry);
        }

        public void Tick(ISimulationView view, float deltaTime)
        {
            // No work here — systems handle everything.
        }

        public void Dispose()
        {
            // Road network and pool are owned by the host.
        }
    }
}
```

**IMPORTANT**:
- Check whether `EntityRepository.GetSingletonManaged<T>()` is nullable-returning (returns null if not set). Look at how other code uses it.
- Check whether `ISystemRegistry.RegisterSystem(IEcsModuleSystem)` or `RegisterSystem<T>()` is the correct API. Look at `NavigationSolverModule.RegisterSystems` for the pattern.
- The `NavigationFakesModule.RegisterProviders` does NOT currently guard against double registration. Do NOT modify `NavigationFakesModule` — only the engine-backed module needs the guard for now (it's the one being tested).

---

## Task 4: Tests for NAV-P6-T4 (EngineBackedPathRegistry)

### File to create
`FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/EngineBackedPathRegistryTests.cs`

### Test list (write at least 8 tests)
1. `Register_ThenIsCached_ReturnsTrue` — register handle 42, `IsCached(42)` returns true
2. `Register_ThenTryGetSummary_ReturnsSummaryWithCorrectWaypointCount` — register 3-point path, summary has `WaypointCount == 3`
3. `TryGetWaypoints_PositionsMatchTrajectory` — register a 2-point path with known Vector2 positions, read back `NavWaypoint[]`, verify `Position.X` and `Position.Z` match (Y should be 0)
4. `TryGetWaypoints_TraversalKindIsWalk` — verify `Traversal == TraversalKind.Walk` for all waypoints
5. `TryGetWaypointsSlice_ReturnsCorrectSubset` — register 4-point path, slice `[1, 2]`, verify 2 waypoints returned at correct positions
6. `Free_RemovesEntry_IsCachedReturnsFalse` — register, then free, verify `IsCached` returns false
7. `Free_RemovesFromPool` — after free, `pool.TryGetTrajectory` returns false
8. `TryGetWaypoints_StaleReplanCount_ReturnsFalse` — register with replanCount=0, call `TryGetWaypoints(handle, expectedReplanCount:1, ...)`, verify returns false
9. `TryGetWaypoints_MatchingReplanCount_ReturnsTrue` — register with replanCount=2, call `TryGetWaypoints(handle, expectedReplanCount:2, ...)`, verify returns true
10. `TryGetWaypoints_UnknownHandle_ReturnsFalse` — call with handle never registered

### Setup hints
```csharp
var pool = new TrajectoryPoolManager();
var registry = new EngineBackedPathRegistry(pool);

// To register a trajectory in the pool, call pool.RegisterTrajectoryWithKey(positions, handle)
// then call registry.Register(handle, replanCount, totalDist, primaryBackend)

var positions = new[] { new Vector2(0f, 0f), new Vector2(10f, 20f) };
pool.RegisterTrajectoryWithKey(positions, handle: 42);
registry.Register(42, replanCount: 0, totalDistanceMeters: 22.36f, primaryBackend: 0);
```

---

## Task 5: Tests for NAV-P6-T5 (EngineBackedNavigationModule + EngineBackedPathResponseSystem)

### File to create
`FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/EngineBackedNavigationModuleTests.cs`

### Test list (write at least 8 tests)

**Module registration tests:**
1. `RegisterProviders_WithoutPriorProviders_Succeeds` — new repo, `RegisterSystems` + `RegisterProviders` succeeds without throwing
2. `RegisterProviders_WithExistingProvider_Throws` — register once, then register again → throws `InvalidOperationException`
3. `RegisterProviders_SetsINavmeshProviderSingleton` — after registration, `repo.GetSingletonManaged<INavmeshProvider>()` is not null and is `EngineBackedNavmeshProvider`
4. `RegisterProviders_SetsIPathRegistrySingleton` — `repo.GetSingletonManaged<IPathRegistry>()` is not null

**EngineBackedPathResponseSystem tests:**

Set up: create a simple test world, create an entity with `NavState`, publish a `PathfindingResultEvent`, run the system.

```csharp
// Test setup pattern:
var pool = new TrajectoryPoolManager();
var registry = new EngineBackedPathRegistry(pool);
var system = new EngineBackedPathResponseSystem(registry);

// Create a world/repo
var repo = new EntityRepository(new ComponentTypeRegistry());
repo.RegisterEvent<PathfindingResultEvent>();
// Register NavState component type
repo.RegisterComponent<NavState>();

// Create entity with NavState
var entity = repo.CreateEntity();
repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None, TrajectoryId = 0 });

// Pre-populate pool (mimicking what PathfindingSolverSystem does)
pool.RegisterTrajectoryWithKey(new[] { new Vector2(0f,0f), new Vector2(5f,5f) }, key: 7);

// Compute RequestId (high 32 bits = entity index)
long requestId = ((long)entity.Index << 32) | 1L;

// Publish event
repo.Bus.Publish(new PathfindingResultEvent
{
    RequestId = requestId,
    IsReachable = true,
    RouteHandle = 7,
    TotalDistanceMeters = 7.07f,
    PrimaryBackend = NavigationBackend.NavRoadGraph,
});

// Execute the system
var view = repo.GetSimulationView();
system.Execute(view, 0.016f);
```

5. `Execute_WithReachableResult_RegistersInRegistry` — after execution, `registry.IsCached(7)` is true
6. `Execute_WithReachableResult_SetsNavStateTrajectoryId` — `NavState.TrajectoryId == 7`
7. `Execute_WithReachableResult_SetsNavStateModeCustomTrajectory` — `NavState.Mode == KinematicsMode.CustomTrajectory`
8. `Execute_WithUnreachableResult_DoesNotRegister` — publish event with `IsReachable = false`, verify `registry.IsCached(7)` is false
9. `Execute_WithUnreachableResult_DoesNotModifyNavState` — NavState remains `Mode = None, TrajectoryId = 0`

### IMPORTANT lookup needed before implementing tests
- Check the exact `Entity.Index` property name (may be `Index`, `Id`, etc.) by looking at `Entity` struct
- Check how `repo.GetSimulationView()` or equivalent is called in other tests (look at `EngineBackedProviderTests.cs` or `NavigationTestWorldFactory.cs`)
- Check how `repo.GetEntityByIndex(entityIndex)` works — is it `GetEntityByIndex(int)`?
- Look at the test world pattern in `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactory.cs` for the proper setup

---

## Implementation order

1. Create `EngineBackedPathRegistry.cs` (Task 1)
2. Create `EngineBackedPathResponseSystem.cs` (Task 2)  
3. Create `EngineBackedNavigationModule.cs` (Task 3)
4. Create `EngineBackedPathRegistryTests.cs` (Task 4)
5. Create `EngineBackedNavigationModuleTests.cs` (Task 5)
6. Build and verify all tests pass

## Build command
```
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits
dotnet test Fdp.Toolkits.Tests --no-build 2>&1 | tail -20
```
or 
```
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits
dotnet build
dotnet test Fdp.Toolkits.Tests 2>&1 | tail -30
```

## Critical file references (read before implementing)

Before implementing, read these files to understand exact types, field names, and API shapes:

1. `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/MusclePathRegistry.cs` — to understand `PathSummary` field names and the TryGetWaypoints pattern (COPY the structure)
2. `FDP/Toolkits/Fdp.Toolkits/Navigation/NavWaypoint.cs` — exact field names (`Traversal` not `TraversalKind`, `Surface` not `SurfaceType`)
3. `FDP/Toolkits/Fdp.Toolkits/Navigation/EngineBacked/EngineBackedNavmeshProvider.cs` — exact `TraversalKind` and `SurfaceType` enum values used (use the SAME)
4. `FDP/Toolkits/Fdp.Toolkits/CarKinem/Trajectory/TrajectoryPoolManager.cs` — `RegisterTrajectoryWithKey`, `RemoveTrajectory`, `TryGetTrajectory` signatures
5. `FDP/Toolkits/Fdp.Toolkits/Navigation/Modules/NavigationSolverModule.cs` — `RegisterSystems` pattern (use `ISystemRegistry`)
6. `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavigationFakesModule.cs` — `RegisterProviders` pattern, `ExecutionPolicy.Synchronous()`
7. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/EngineBackedProviderTests.cs` — existing test pattern for engine-backed providers
8. `FDP/Toolkits/Fdp.Toolkits/CarKinem/Components/NavState.cs` (or wherever `NavState` is defined) — exact fields
9. `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/NavigationExecutionSystem.cs` — how it reads `NavState`, `GetComponentRW<NavState>()` pattern

## Validation checklist
- [ ] `dotnet build` on `FDP/Toolkits` succeeds with 0 errors
- [ ] `dotnet test Fdp.Toolkits.Tests` shows ≥ 248 passing tests, 0 failures
- [ ] `EngineBackedPathRegistry.Free` removes from both `_meta` dict AND pool
- [ ] Replan count stale-miss test is included and passes
- [ ] Module mutual exclusion test is included and throws `InvalidOperationException`
- [ ] No CS0246, CS0103, or CS0117 errors (verify all enum member names match actual code)
