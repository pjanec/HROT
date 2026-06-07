# DD-EngineBacked-Nav — Engine-Backed Navigation Module — Detailed Design

> **Scope:** A second implementation of the navigation provider
> interfaces that wires the new navigation pipeline (`MoveTo`,
> `PlanRoute`, `FollowPath`, `FetchPathDetails`, `ReleasePath`) to
> the **existing engine machinery** — `RoadNetworkBlob`,
> `PathfindingSolverSystem` (Dijkstra), `TrajectoryPoolManager`,
> `CarKinematicsSystem`, `LinearKinematicsSystem`. This makes the
> navigation contract demoable end-to-end on real scenarios (real
> vehicles, real road networks, real arrival behavior) before
> DotRecast or dtCrowd lands.
>
> **Out of scope:** The real backends (DotRecast, dtCrowd, volumetric
> pather — separate docs each). The deterministic test-oriented fakes
> (DD-Fake-Nav). Real navmesh-driven pathing (the navmesh provider
> here is a direct-line placeholder).
>
> **Audience:** Navigation/Muscle implementation team, mission/scenario
> authors who need movable vehicles right now, AI editor team
> (informational — your behaviors will work against this module in
> early demos).
>
> **Reads alongside:** Navigation Design (architecture — contracts
> are identical), DD-Fake-Nav (the deterministic-fake variant —
> shares `IPathRegistry` interface shape, ComponentId conventions,
> and the diagnostic window).

---

## Table of contents

1. Why a second implementation
2. The four providers — overview
3. `EngineBackedNavmeshProvider`
4. `EngineBackedDtCrowdProvider`
5. `EngineBackedVolumetricPathProvider`
6. `EngineBackedPathRegistry`
7. Module wiring — `EngineBackedNavigationModule`
8. Kinematics routing — vehicles vs. humanoids
9. Diagnostic window reuse
10. What this module intentionally doesn't do

---

## 1. Why a second implementation

The deterministic fakes in DD-Fake-Nav are excellent for unit and integration tests — they're self-contained, repeatable, and visualizable. But they don't move real vehicles on real road networks.

The engine has a fully working road-graph subsystem: `RoadNetworkBlob` loaded from scenario data, `PathfindingSolverSystem` performing Dijkstra over road nodes, `TrajectoryPoolManager` holding `CustomTrajectory` instances with Catmull-Rom interpolation, `CarKinematicsSystem` driving vehicles along those trajectories with a bicycle model. None of this needs to be rebuilt — it needs to be **adapted to the new navigation contract**.

`EngineBackedNavigationModule` is that adapter. It:

- Lets `Action_MoveTo(destination)` work end-to-end against an existing road network. The path is real (graph-shortest), the vehicle moves with real physics, the entity arrives. No mock data required.
- Lets `Action_PlanRoute` / `Action_FollowPath` work the same way — the handle is real, refers to a real entry in the existing `TrajectoryPoolManager`.
- Lets `Action_FetchPathDetails` return the actual route waypoints from the existing trajectory.
- Lets demo scenarios use the new BTree action surface without waiting for navmesh tooling.

It is **not** a long-term solution — the navmesh side is a placeholder, crowd is disabled, off-mesh links are unsupported. When DotRecast and dtCrowd land, the real backends replace this module. But during the development phase where the contract needs to be exercised against real game data, this module is the integration vehicle.

The deterministic fakes (DD-Fake-Nav) and this engine-backed module coexist in the codebase. Tests pick the fakes; demo scenarios with real road networks pick this module. The choice is made once at scenario load — see §7.

---

## 2. The four providers — overview

| Provider | Backed by | Behavior |
|---|---|---|
| `EngineBackedNavmeshProvider` | nothing real | Direct-line placeholder — `IsWalkable` returns true, `PathExists` returns true, `PlanPath` returns `[start, end]` as two waypoints. No real navmesh queries. |
| `EngineBackedDtCrowdProvider` | nothing real | Stub — `RegisterAgent` accepts but produces no avoidance behavior. Used only to satisfy the interface for humanoid `MoveTo`; the entity actually moves via `LinearKinematicsSystem`. |
| `EngineBackedVolumetricPathProvider` | nothing real | Direct-line, same shape as the navmesh placeholder but for 3D paths. Almost never invoked in early demos. |
| `EngineBackedPathRegistry` | existing `TrajectoryPoolManager` | Real implementation. The `RouteHandle` is the existing `NavState.TrajectoryId`. `IPathRegistry.TryGetWaypoints` reads waypoints from the pool's `CustomTrajectory` entries. |

The module also installs a path-routing system that intercepts `PathfindingRequestEvent`s with `BackendForce == RoadGraph` (or `Auto` for vehicles in the default config) and routes them to the existing `PathfindingSolverSystem` — which is the real Dijkstra over `RoadNetworkBlob`. This is where the actual interesting paths come from.

All four providers are loaded by a single `EngineBackedNavigationModule : IEcsModule, IDisposable, IWindowRegistrar`. Same lifecycle shape as `NavigationFakesModule`; only one is registered per scenario.

---

## 3. `EngineBackedNavmeshProvider`

### 3.1 Behavior

Implements `INavmeshProvider` with a direct-line placeholder strategy:

```csharp
public sealed class EngineBackedNavmeshProvider : INavmeshProvider
{
    public bool IsWalkable(Vector2 point, ushort layerMask) => true;

    public Vector3 ProjectToNavmesh(Vector2 point, float maxDist, ushort layerMask)
        => new Vector3(point.X, point.Y, 0);   // Z = 0; no real elevation data

    public bool PathExists(Vector2 a, Vector2 b, ushort layerMask, float maxCost)
        => Vector2.Distance(a, b) <= maxCost || maxCost == 0;

    public float PathCost(Vector2 a, Vector2 b, ushort layerMask)
        => Vector2.Distance(a, b);

    public void SampleNavmeshPoints(BoundingVolume v, float density,
                                    ushort layerMask, ICandidateSink sink)
    {
        // Grid-sample within bounds and push every sample as walkable.
        // EQS templates that use this expect a uniform distribution.
        // ...
    }

    public uint QueryVersion(BoundingBox2D bounds, ushort layerMask) => 1;

    public int PlanPath(Vector2 a, Vector2 b, ushort layerMask,
                        Span<NavWaypoint> output)
    {
        if (output.Length < 2) return 0;
        output[0] = new NavWaypoint { Position = new Vector3(a.X, a.Y, 0),
                                       TraversalKind = TraversalKind.Walk,
                                       SurfaceType   = SurfaceType.Default,
                                       LayerMask     = layerMask,
                                       SegmentLengthMeters = 0 };
        output[1] = new NavWaypoint { Position = new Vector3(b.X, b.Y, 0),
                                       TraversalKind = TraversalKind.Walk,
                                       SurfaceType   = SurfaceType.Default,
                                       LayerMask     = layerMask,
                                       SegmentLengthMeters = Vector2.Distance(a, b) };
        return 2;
    }
}
```

### 3.2 When this provider is invoked

`PathfindingSolverSystem` selects backends per Navigation Design §5.2:

- `BackendForce == Navmesh` → invokes this provider's `PlanPath` → straight-line two-waypoint path.
- `BackendForce == RoadGraph` → invokes the road-graph planner instead (§5 below); this provider is bypassed entirely.
- `BackendForce == Auto` → vehicle defaults to road-graph; humanoid defaults to navmesh (so humanoids get straight lines).

In practice, engine-backed scenarios mostly use vehicles on `RoadGraph`, so this navmesh provider is consulted rarely. It exists primarily for:

- EQS templates that call `IsWalkable` / `ProjectToNavmesh` / `SampleNavmeshPoints` — these get "everything is walkable" results, which is correct for demo scenarios where the world has no impassable terrain.
- Humanoid `MoveTo` with no road graph available — the entity gets a straight-line path and `LinearKinematicsSystem` drives it.

### 3.3 What it doesn't model

- **No actual obstacle avoidance.** A humanoid asked to go from A to B will walk through walls. Engine-backed mode is for scenarios where the world has open space or where the test author doesn't care about geometry.
- **No layer differentiation.** All `layerMask` queries return identical results.
- **No off-mesh links.** `PlanPath` never emits a `TraversalKind != Walk`. The `OffMeshLinkDetectionSystem` therefore never triggers in this mode — no jumps, climbs, doors.
- **No version churn.** `QueryVersion` always returns 1. Navmesh-patch replans never trigger.

---

## 4. `EngineBackedDtCrowdProvider`

### 4.1 Behavior

A non-functional stub that satisfies the `IDtCrowdProvider` interface but produces no behavior:

```csharp
public sealed class EngineBackedDtCrowdProvider : IDtCrowdProvider
{
    public bool RegisterAgent(Entity entity, in CrowdAgentParams parameters)
        => true;                                  // pretend to accept

    public void UnregisterAgent(Entity entity) { }

    public void SetAgentTarget(Entity entity, Vector3 target) { }

    public void Update(float dt, ISimulationView view) { }

    public Vector3 GetAgentVelocity(Entity entity)
        => Vector3.Zero;                          // never write SimVelocity

    public bool TryGetAgentSnapshot(Entity entity, out CrowdAgentSnapshot snapshot)
    {
        snapshot = default;
        return false;
    }
}
```

### 4.2 Why a stub instead of a real implementation

The engine has no humanoid avoidance system. dtCrowd is the eventual home for that; until it lands, there is no avoidance. The stub exists to:

- Satisfy `NavigationIntentBridgeSystem`'s `RegisterAgent` call without throwing.
- Let `CrowdAgentUpdateSystem` run without effect (it reads `GetAgentVelocity == 0` and writes zero to `SimVelocity`).
- Leave the actual movement to `LinearKinematicsSystem` — see §8.

The `CrowdAgent` tag **is not added** to humanoid entities by `NavigationIntentBridgeSystem` when this module is active. The tag-routing in `NavigationIntentBridgeSystem` checks a deployment-mode flag (or queries the active `IDtCrowdProvider`'s capability bit) and skips the tag for entities that would otherwise be crowd-managed. Vehicles never get the tag either (same as in the real design).

### 4.3 What it doesn't model

- **No avoidance.** Humanoids will walk through each other.
- **No stuck detection from crowd-velocity inputs.** The frustration watchdog still runs against `SimVelocity`, but since humanoids in this mode have `SimVelocity` driven by `LinearKinematicsSystem`, the watchdog functions correctly — just based on whether the linear kinematics is making progress, not on crowd-resolved velocity.

---

## 5. `EngineBackedVolumetricPathProvider`

### 5.1 Behavior

Direct-line in 3D — same shape as `EngineBackedNavmeshProvider` but returns 3D waypoints:

```csharp
public sealed class EngineBackedVolumetricPathProvider : IVolumetricPathProvider
{
    public bool   IsFlyable(Vector3 point) => true;

    public bool   PathExists(Vector3 a, Vector3 b, FlyProfile profile, float maxCost)
        => true;

    public int    Plan(Vector3 a, Vector3 b, FlyProfile profile, Span<NavWaypoint> output)
    {
        if (output.Length < 2) return 0;
        output[0] = new NavWaypoint { Position = a, /* ... */ };
        output[1] = new NavWaypoint { Position = b, /* ... */ };
        return 2;
    }

    public uint   QueryVersion(BoundingBox3D bounds) => 1;
}
```

### 5.2 Usage

Almost never invoked in early engine-backed demos — flying agents aren't a near-term focus. The provider exists to satisfy the interface so flying `MoveTo` doesn't throw if a scenario does spawn a flying entity.

---

## 6. `EngineBackedPathRegistry`

This is the only **real** provider in the module. It's the bridge between the new `IPathRegistry` interface and the existing `TrajectoryPoolManager`.

### 6.1 Storage model

The existing engine stores paths as `CustomTrajectory` entries in `TrajectoryPoolManager`, keyed by an `int` handle. `NavState.TrajectoryId` already references this handle. The new `RouteHandle` is the same `int`:

- Brain-allocated handles (`((entityIndex & 0xFFFFFF) << 8) | counter`, always `< 0x40000000`) flow into `NavigationIntent.RouteHandle`.
- The path-response handler (see §7) registers the resulting `CustomTrajectory` into `TrajectoryPoolManager` keyed by that exact handle.
- `NavState.TrajectoryId` is set to the same value.
- `EngineBackedPathRegistry.TryGetWaypoints(handle, ...)` reads from `TrajectoryPoolManager.Get(handle)` and copies the trajectory's interior waypoints into the destination span.

Result: one storage. The handle Brain passes in `NavigationIntent` is the same handle `CarKinematicsSystem` reads from `NavState.TrajectoryId`. No translation.

### 6.2 Brain-side cache vs. Muscle-side pool

In all-in-one mode (the only deployment mode the engine-backed module currently supports — see §7), Brain and Muscle share the process. The `EngineBackedPathRegistry` is a single implementation serving both `IPathRegistry` references (parallel to `SharedPathRegistry` in DD-Fake-Nav §6.4).

- `BrainPathRegistry` calls go to this same instance.
- `MusclePathRegistry` calls go to this same instance.
- There's no separate Brain-side cache — the trajectory pool is the only storage.

`TryGetWaypoints` extracts waypoints from the `CustomTrajectory` by sampling its interior at segment boundaries. The `CustomTrajectory` stores raw `Vector3` waypoints; the registry wraps each in a `NavWaypoint` with `TraversalKind = Walk` and `SurfaceType = Default` (no surface metadata in the existing pool).

### 6.3 Lifecycle

- **Register** — called by the path-response handler when Solver returns a path. Inserts a new `CustomTrajectory` into `TrajectoryPoolManager` keyed by the Brain-provided handle.
- **Refresh in place** — called on Muscle-internal replan. Replaces the `CustomTrajectory` content under the same handle; existing `NavState.TrajectoryId` references remain valid.
- **Free** — called by `Action_ReleasePath` handler. Removes the entry from `TrajectoryPoolManager`. The `NavState.TrajectoryId` reference is **not** automatically cleared (caller's responsibility to also clear it if the entity should stop, which `Action_ReleasePath` semantically doesn't do — see Navigation Design §13.2's `ReleasePathParams` notes).

### 6.4 Cache-miss policy

Strict (matching `BrainPathRegistry` in DD-Fake-Nav §6.2):

- If the handle is not in the pool → `TryGetWaypoints` returns false.
- If `NavigationStatus.ReplanCount` doesn't match the entry's stored `LastObservedReplanCount` → returns false (stale).
- The pool stores `ReplanCount` per entry; the path-response handler updates it on every refresh-in-place.

In practice, in all-in-one mode the entry is always fresh because both sides of the registry are the same object. The stale-miss path is exercised only when scenarios use the auto-refresh mechanism (`Flags.AutoSendPathOnReplan`) and the BTree calls `TryGetWaypoints` between the replan and the explicit fetch — a narrow window.

---

## 7. Module wiring — `EngineBackedNavigationModule`

### 7.1 Module shape

```csharp
public sealed class EngineBackedNavigationModule : IEcsModule, IDisposable, IWindowRegistrar
{
    private RoadNetworkBlob _roadNetwork;
    private TrajectoryPoolManager _trajectoryPool;
    private EngineBackedNavmeshProvider _navmesh;
    private EngineBackedDtCrowdProvider _crowd;
    private EngineBackedVolumetricPathProvider _volumetric;
    private EngineBackedPathRegistry _registry;

    public EngineBackedNavigationModule(RoadNetworkBlob road, TrajectoryPoolManager pool)
    {
        _roadNetwork    = road;
        _trajectoryPool = pool;
    }

    public void Register(IEcsModuleRegistrar reg)
    {
        _navmesh     = new EngineBackedNavmeshProvider();
        _crowd       = new EngineBackedDtCrowdProvider();
        _volumetric  = new EngineBackedVolumetricPathProvider();
        _registry    = new EngineBackedPathRegistry(_trajectoryPool);

        reg.RegisterSingleton<INavmeshProvider>(_navmesh);
        reg.RegisterSingleton<IDtCrowdProvider>(_crowd);
        reg.RegisterSingleton<IVolumetricPathProvider>(_volumetric);
        reg.RegisterSingleton<IPathRegistry>(_registry);

        // The path-request routing system — see §7.2
        reg.RegisterSystem(new EngineBackedPathResponseSystem(_registry, _roadNetwork));
    }

    public void Dispose()
    {
        // RoadNetworkBlob is owned by the host (scenario), not by this module.
        // TrajectoryPoolManager likewise.
    }

    public void RegisterWindows(IWindowManager wm)
    {
        if (HeadlessMode.IsHeadless) return;
        wm.RegisterWindow(new FakeNavigationInspectorWindow(/* shared with the fake */));
    }
}
```

### 7.2 The path-response handler

`EngineBackedPathResponseSystem` consumes `PathResponseEvent`s and adapts them to the existing trajectory pool:

```
on PathResponseEvent { RouteHandle, IsReachable, ... }:
  if not reachable:
    NavigationStatus.Result := (intent.ActiveAction == PlanRoute ? NoPath : FailedUnreachable)
    return
  
  // Solver populated the response's waypoint list (either via the road-graph
  // Dijkstra path or via EngineBackedNavmeshProvider.PlanPath for navmesh mode).
  // Build a CustomTrajectory from those waypoints.
  
  trajectory := new CustomTrajectory(waypoints, TrajectoryInterpolation.CatmullRom)
  _trajectoryPool.RegisterOrReplace(RouteHandle, trajectory)
  
  // Wire into NavState so CarKinematicsSystem / LinearKinematicsSystem pick it up.
  NavState.TrajectoryId := RouteHandle
  NavState.Mode := (vehicle ? CustomTrajectory : DirectPoint)
  NavState.HasArrived := false
  
  // Populate NavigationCorridorMuscle for the rest of the nav pipeline.
  NavigationCorridorMuscle { LocalRouteHandle = RouteHandle, ... }
  
  // Action-specific tail:
  if intent.ActiveAction == MoveTo:
    NavigationStatus { Result = InProgress, Phase = Following, ... }
    fire MoveStartedEvent
  else if intent.ActiveAction == PlanRoute:
    NavigationStatus { Result = PathFound, RouteHandle = RouteHandle }
    if intent.PlanRouteParams.Flags.IncludeFullPathDetails:
      fire NavigationPathDetailsResponseEvent
    // Note: NavState.Mode stays None until a subsequent FollowPath
```

The existing `PathfindingSolverSystem` (Dijkstra over `RoadNetworkBlob`) handles `BackendForce == RoadGraph`. The `EngineBackedNavmeshProvider.PlanPath` handles `BackendForce == Navmesh`. The dispatch logic inside `PathfindingSolverSystem` is the same as Navigation Design §5.2 — no change required to that system.

### 7.3 Host selection

Exactly one of `NavigationFakesModule` or `EngineBackedNavigationModule` is registered per scenario. The host's startup code:

```csharp
// In scenario setup:
if (scenario.NavigationBackend == NavigationBackend.EngineBacked)
{
    host.RegisterModule(new EngineBackedNavigationModule(scenario.Road, scenario.TrajectoryPool));
}
else
{
    host.RegisterModule(new NavigationFakesModule(scenario.NavTestMap));
}
```

Modules are mutually exclusive. The host enforces this; attempting to register both raises an exception at module-resolution time.

### 7.4 Deployment-mode support

This module supports the **all-in-one** deployment mode only (Brain + Muscle + NavigationSolver in one process — Navigation Design §2). It does not currently support default-collocated or scale-out:

- The existing `TrajectoryPoolManager` is a non-replicated in-process resource.
- The existing `CarKinematicsSystem` reads it directly via `NavState.TrajectoryId`.
- Replicating the pool across DDS would require contract changes outside this module's scope.

When the real backends land, they will support all three deployment modes. The engine-backed module is a transitional convenience, not a deployment target.

---

## 8. Kinematics routing — vehicles vs. humanoids

`NavigationIntentBridgeSystem` (Navigation Design §7.1) routes by `MobilityProfile`. When the engine-backed module is active, the routing collapses to two paths:

### 8.1 Vehicles (Wheeled, Tracked, Naval)

- `NavState.Mode := CustomTrajectory` (vehicles) or `Naval` (naval craft, eventually).
- `NavState.TrajectoryId := RouteHandle` from the response handler.
- **`CarKinematicsSystem`** picks it up next tick and drives the vehicle along the trajectory with the existing bicycle model.

Vehicles route via `PathfindingSolverSystem` with `BackendForce == RoadGraph` (default for vehicles in engine-backed scenarios) — real Dijkstra path, real interpolated trajectory, real arrival behavior. This is the path that "just works" out of the box.

### 8.2 Humanoids

- `NavState.Mode := DirectPoint`.
- `NavState.TrajectoryId := RouteHandle` (a 2-waypoint trajectory from `EngineBackedNavmeshProvider.PlanPath`).
- **`LinearKinematicsSystem`** picks it up and integrates `SimVelocity` toward the next waypoint at the humanoid's `MaxMoveSpeed`.
- No `CrowdAgent` tag is added (the stub crowd provider doesn't manage anything).
- `NavigationExecutionSystem` advances `ProgressS` as the entity reaches each waypoint and detects arrival via the standard mechanism.

Humanoids move in straight lines and walk through obstacles. Acceptable for demo scenarios where the world is open or the test author doesn't care about geometry. Real navmesh-driven humanoid pathing waits for DotRecast.

### 8.3 Flying

Routed to `EngineBackedVolumetricPathProvider`, which returns a direct 3D path. `NavState.Mode := DirectPoint`. `LinearKinematicsSystem` integrates toward the waypoints. Same caveats as humanoids — direct line, no avoidance.

### 8.4 Frustration watchdog

`NavigationExecutionSystem` reads `SimVelocity` regardless of which kinematics system wrote it. In engine-backed mode:

- Vehicles stuck behind a road obstruction — watchdog fires after `FrustrationTickLimit` ticks of low `SimVelocity`. Same code path as real-backend.
- Humanoids stuck at a wall they were walking toward — watchdog fires when the wall blocks linear progress. The fact that there's no real navmesh just means the wall is geometric only; `SimVelocity` going to zero still triggers the watchdog.

The watchdog therefore works in engine-backed mode without modification.

---

## 9. Diagnostic window reuse

The `FakeNavigationInspectorWindow` (DD-Fake-Nav §8) is reused unchanged. With the engine-backed module active, the tabs behave as follows:

**Navmesh tab.** Displays a single placeholder entry: "No navmesh layers loaded — direct-line provider in use. All `IsWalkable` queries return true." No polygon table, no off-mesh links list. The "Block polygon" / "Bump version" buttons are disabled with a tooltip explaining they're navmesh-only and this module has no navmesh data.

**Crowd tab.** Displays "Crowd avoidance disabled — stub provider in use. Humanoids move via LinearKinematicsSystem." No agent list. The "Reset crowd" footer button is disabled.

**Volumetric tab.** Same as navmesh — placeholder message, disabled controls.

**Paths tab.** Fully functional. Reads from the existing `TrajectoryPoolManager` (which is what `EngineBackedPathRegistry` wraps). Each entry shows the existing `CustomTrajectory`'s waypoint list. The Brain-cache sub-view shows the same data (no separate Brain cache exists in all-in-one mode). Stats counters reflect real activity. This is the **primary use of the window in engine-backed mode** — operators monitoring path activity on real scenarios.

The window class itself doesn't need a per-mode subclass; the tab-draw methods detect the active provider type at draw time and switch their rendering accordingly:

```csharp
void DrawNavmeshTab(IImGuiContext ctx)
{
    var nav = _view.GetSingleton<INavmeshProvider>();
    if (nav is EngineBackedNavmeshProvider)
    {
        ImGui.TextDisabled("No navmesh layers loaded — direct-line provider in use.");
        ImGui.TextDisabled("All IsWalkable queries return true.");
        return;
    }
    // ... regular FakeNavmeshProvider rendering
}
```

The header line at the top of the window shows the active provider set: "Backend: EngineBacked (road graph + direct-line)" vs. "Backend: FakeNavmeshProvider + FakeDtCrowdProvider + FakeVolumetricPathProvider".

---

## 10. What this module intentionally doesn't do

- **No real navmesh queries.** No polygon adjacency, no off-mesh links, no per-layer baking. The navmesh provider is a placeholder.
- **No crowd avoidance.** Humanoids ignore each other.
- **No off-mesh montages.** `OffMeshLinkDetectionSystem` never fires in this mode — no `TraversalKind != Walk` waypoints exist. Scenarios that need jumps, climbs, or doors should use the deterministic fakes (DD-Fake-Nav) until DotRecast lands.
- **No distributed deployment.** All-in-one only — see §7.4.
- **No fault injection.** The deterministic test APIs that the fake-mode providers expose (`BlockPolygon`, `OverrideAgentVelocity`, etc.) are absent. If a scenario needs to test failure modes, use the fakes.
- **No support for `Flags.StreamCorridorPreview` against real road-graph paths.** The preview component would technically work, but the corridor data semantics (segment-by-segment `NavWaypoint` advance) don't align cleanly with the existing `CustomTrajectory` interpolation model. Treat preview as a fake-mode-only feature for now.
- **No support for `Flags.AutoSendPathOnReplan` for vehicle replans.** The existing `CarKinematicsSystem` doesn't issue replan requests; vehicle stuck-behavior surfaces directly as `FailedBlocked`. This may be revisited if engine-backed demos need replan behavior.

---

*End DD-EngineBacked-Nav.*
