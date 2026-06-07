# DD-Fake-Nav — Fake Navigation Providers — Detailed Design

> **Scope:** `FakeNavmeshProvider` (in place of DotRecast),
> `FakeDtCrowdProvider` (in place of the dtCrowd port),
> `FakeVolumetricPathProvider` (in place of the deferred 3D pather),
> and the `IPathRegistry` fake implementations that provide Brain-side
> path-data access. Per-fake ECS state component, deterministic tick
> algorithms, diagnostic ImGui window with four tabs and live state
> inspection, JSON snapshot export, AAR recording integration.
>
> **Out of scope:** The eventual real implementations (DotRecast,
> dtCrowd, volumetric pather — separate docs each). The provider
> interfaces themselves (Navigation Design §§4, 5, 7, 8, 9 — referenced
> here, not redefined). The integration tests that consume these
> fakes (DD-Tests-Nav).
>
> **Audience:** Navigation/Muscle implementation team (primary),
> AI editor team (informational — your behaviors will run against
> these fakes during early development).
>
> **Reads alongside:** Navigation Design §§4 (CQRS components),
> §5 (Muscle ↔ Solver path query), §6 (Brain-side execution,
> `IPathRegistry`), §7 (Muscle-side execution), §8 (multi-layer
> navmesh), §9 (flying), DD-Fake (FakeAnimationBackend) for the
> ImGui window precedent and `IWindowRegistrar` pattern.

---

## Table of contents

1. Design principles
2. The four fakes — overview
3. `FakeNavmeshProvider`
4. `FakeDtCrowdProvider`
5. `FakeVolumetricPathProvider`
6. `IPathRegistry` fake implementations
7. Test-map data format (`NavTestMap`)
8. The diagnostic ImGui window
9. JSON snapshot export
10. AAR recording integration
11. Determinism and hard-assert discipline
12. ComponentId allocation
13. What the fakes intentionally don't do

---

## 1. Design principles

Three principles shape every choice in this document. The first two are inherited verbatim from DD-Fake (FakeAnimationBackend) because they apply identically here; the third is navigation-specific.

**Principle 1: Fakes are best-effort approximations aiming to be close to the real backends.** Behaviors authored against the fakes should behave similarly enough against real DotRecast/dtCrowd that AI work doesn't regress when those land — but cross-backend test parity is not the design goal. The fakes unblock AI behavior development without requiring 3D rendering, asset import, navmesh baking, or third-party-library integration during early development.

**Principle 2: All per-entity/per-agent working state lives in an ECS component.** Buys entity inspector integration, AAR recording, and gizmo/scene-view possibilities for free. The fake classes themselves stay thin — stateless processors reading/writing the component.

**Principle 3: The world-model fakes share a single test-map data source.** A `NavTestMap` (§7) is loaded once at scenario startup and provides ground truth for `FakeNavmeshProvider`, `FakeDtCrowdProvider`, and `FakeVolumetricPathProvider` — polygon adjacency for navmesh, no-fly zones for volumetric, obstacle positions for crowd. This guarantees the three views of the world stay consistent and makes test fixtures shareable. The path-registry fakes (§6) don't read from the map — they're storage adapters keyed by Brain-assigned `RouteHandle`.

A non-principle: performance. Fakes are for development and debugging only. They use plain dictionaries, `List<T>`, and straightforward algorithms throughout. Production deployments use the real backends.

---

## 2. The four fakes — overview

| Fake | Replaces | Implements | State component |
|---|---|---|---|
| `FakeNavmeshProvider` | DotRecast / proprietary Recast | `INavmeshProvider` | `FakeNavmeshState` (singleton) |
| `FakeDtCrowdProvider` | `dtCrowd` port | `IDtCrowdProvider` | `FakeCrowdAgentState` (per-agent) + `FakeCrowdGlobalState` (singleton) |
| `FakeVolumetricPathProvider` | Future volumetric pather | `IVolumetricPathProvider` | `FakeVolumetricState` (singleton) |
| `MusclePathRegistry` / `BrainPathRegistry` | n/a (no real backend yet — first impl) | `IPathRegistry` | `FakePathPoolEntry` (per-handle, Muscle side) + `FakeBrainPathCacheEntry` (per-handle per-entity, Brain side) |

All four are loaded by a single `NavigationFakesModule : IEcsModule, IDisposable, IWindowRegistrar`. The module:
- Reads the active scenario's `NavTestMap` asset at scenario load.
- Instantiates the four providers and registers them in the ECS singleton table (mirroring `INavmeshProvider` registration per existing pattern).
- Implements `IDisposable` per Navigation Design §16 — the dtCrowd-style native resources need explicit teardown, even if the fakes themselves don't allocate any.
- Implements `IWindowRegistrar` to register the diagnostic ImGui window (§8).
- Headless-guards the window registration: only call `RegisterWindow` outside headless builds (per existing convention).
- In the **all-in-one** deployment mode, instantiates a single shared `SharedPathRegistry` that satisfies both `MusclePathRegistry` and `BrainPathRegistry` requests — see §6.4.

When the real backends land, this module is swapped for a `NavigationRealBackendsModule` with identical lifecycle. The rest of the navigation system is untouched.

---

## 3. `FakeNavmeshProvider`

### 3.1 Interface

Implements `INavmeshProvider` exactly (Navigation Design §8.1):

```csharp
public interface INavmeshProvider
{
    bool      IsWalkable(Vector2 point, ushort layerMask);
    Vector3   ProjectToNavmesh(Vector2 point, float maxDist, ushort layerMask);
    void      SampleNavmeshPoints(BoundingVolume v, float density, ushort layerMask, ICandidateSink sink);
    bool      PathExists(Vector2 a, Vector2 b, ushort layerMask, float maxCost);
    float     PathCost(Vector2 a, Vector2 b, ushort layerMask);
    uint      QueryVersion(BoundingBox2D bounds, ushort layerMask);
    // Solver-side: returns a path as a list of waypoints. Not strictly part of
    // INavmeshProvider itself (the solver owns A* and calls the lower-level
    // queries above), but the fake exposes a convenience entry point for use
    // by FakePathfindingSolverSystem in tests. Real Recast implementations
    // expose their own A* through dtNavMeshQuery.
    int PlanPath(Vector2 a, Vector2 b, ushort layerMask, Span<NavWaypoint> output);
}
```

### 3.2 Internal representation

The fake holds, per layer:

```csharp
// inside FakeNavmeshState (ECS singleton)
struct FakeNavLayer {
    NavLayerMask   Layer;
    NavPolygon[]   Polygons;       // immutable after load
    int[][]        Adjacency;      // polygon-index → neighbor-polygon-indices
    OffMeshLink[]  OffMeshLinks;   // jump/climb/door annotations
    uint           Version;        // bumped on test-API patch
}

struct NavPolygon {
    int       Id;                  // index in Polygons[]
    Vector3[] Vertices;            // 3D — Z is the polygon's elevation
    SurfaceType SurfaceType;
    bool      IsBlocked;           // toggled by test API for "obstacle appears" scenarios
}

struct OffMeshLink {
    int            FromPolygonId;
    int            ToPolygonId;
    Vector3        StartPos;       // entry point on FromPolygon
    Vector3        EndPos;         // exit point on ToPolygon
    TraversalKind  Kind;           // Jump | JumpDown | JumpAcross | Climb | Door
    float          Cost;           // additive cost to traverse this link
}
```

The data is loaded from a `NavTestMap` asset (§7). After load, the polygon array is immutable except for `IsBlocked` toggles via the test API.

### 3.3 Query algorithms

All algorithms are straightforward; performance is irrelevant.

- **`IsWalkable(p, mask)`** — for each layer with `(layer.Layer & mask) != 0`: linear scan polygons; point-in-polygon test on (p.X, p.Y, polygon's Z plane). Return true if any walkable polygon contains p.
- **`ProjectToNavmesh(p, maxDist, mask)`** — for each candidate polygon: compute distance from p to nearest edge or interior point. Return the nearest result within `maxDist`, with Z = polygon's interpolated elevation. Return `Vector3.NaN` if nothing within range.
- **`PathExists(a, b, mask, maxCost)`** — A* over polygon adjacency graph, starting from the polygon containing `a` and goal-testing the polygon containing `b`. Off-mesh links count as edges. Return true iff cost ≤ maxCost.
- **`PathCost(a, b, mask)`** — same A* but returns the final cost (or `float.PositiveInfinity` on unreachable).
- **`SampleNavmeshPoints(bounds, density, mask, sink)`** — grid sample within bounds at given density; for each grid point, `IsWalkable` test, project, push into sink. Used by EQS positional generators.
- **`QueryVersion(bounds, mask)`** — returns `max(layer.Version for each layer in mask that overlaps bounds)`. The version grows only when test code calls the patch API.
- **`PlanPath(a, b, mask, output)`** — same A* but reconstructs the polygon-sequence-to-waypoint-sequence path, including off-mesh link entry/exit positions tagged with their `TraversalKind`. Returns the count of waypoints written, capped by `output.Length`. Each `NavWaypoint` populated per Navigation Design §4.5.

### 3.4 Test API surface

Test code accesses the fake through `IFakeNavmeshProviderTestApi` (cast from the singleton):

```csharp
public interface IFakeNavmeshProviderTestApi
{
    /// <summary>Mark a polygon as blocked; bumps version, invalidates paths.</summary>
    void BlockPolygon(int polygonId, NavLayerMask layer);

    /// <summary>Restore a previously-blocked polygon.</summary>
    void UnblockPolygon(int polygonId, NavLayerMask layer);

    /// <summary>Force the version on a region; simulates a navmesh patch arrival.</summary>
    void BumpVersion(BoundingBox2D bounds, NavLayerMask layer);

    /// <summary>Inspect: read the loaded map (for assertion in tests).</summary>
    NavTestMap GetLoadedMap();
}
```

These exist only on the fake, never on the real interface. Test code casts: `((IFakeNavmeshProviderTestApi)provider).BlockPolygon(...)`.

### 3.5 What it doesn't model

- **Polygon-mesh stitching nuances** that real Recast handles (tile boundaries, partition strategies). The fake's polygons are pre-stitched at load.
- **Continuous-elevation navmesh** (each polygon has a single Z value; ramps aren't modeled — they're just multiple polygons of slightly different Z).
- **Dynamic obstacle carving.** `BlockPolygon` is a whole-polygon toggle; real Recast does sub-polygon obstacle annotation.
- **Multiple disjoint regions per layer** with cost discontinuities between them (works, but no attempt to model real-Recast's tile-cost mechanics).

---

## 4. `FakeDtCrowdProvider`

### 4.1 Interface

Implements `IDtCrowdProvider`. The interface itself isn't fully specified in Navigation Design §7.2 — let's pin it here:

```csharp
public interface IDtCrowdProvider
{
    /// <summary>Add an agent to the crowd. Returns false if the agent already exists.</summary>
    bool RegisterAgent(Entity entity, in CrowdAgentParams parameters);

    /// <summary>Remove an agent from the crowd. Safe to call if not registered.</summary>
    void UnregisterAgent(Entity entity);

    /// <summary>Update the agent's target position. Idempotent within a tick.</summary>
    void SetAgentTarget(Entity entity, Vector3 target);

    /// <summary>Advance the crowd simulation by dt seconds. Writes per-agent
    /// velocity outputs that the CrowdAgentUpdateSystem applies to SimVelocity.</summary>
    void Update(float dt, ISimulationView view);

    /// <summary>Read the agent's current crowd-computed velocity (for the
    /// CrowdAgentUpdateSystem to write into SimVelocity).</summary>
    Vector3 GetAgentVelocity(Entity entity);

    /// <summary>Read the agent's current crowd-internal state (for diagnostics).</summary>
    bool TryGetAgentSnapshot(Entity entity, out CrowdAgentSnapshot snapshot);
}

public struct CrowdAgentParams {
    public float  Radius;            // from VehicleParametersDto.Width * 0.5f
    public float  Height;
    public float  MaxSpeed;          // from VehicleParametersDto.MaxSpeedFwd
    public float  MaxAcceleration;   // from VehicleParametersDto.MaxAccel
    public byte   SeparationWeight;  // soft preference parameter; default 2
}

public struct CrowdAgentSnapshot {
    public Vector3 Position;         // last-observed SimTransform
    public Vector3 Velocity;
    public Vector3 Target;
    public Vector3 DesiredVelocity;
    public bool    ReachedTarget;
    public int     NearbyAgentCount;
}
```

### 4.2 ECS state

```csharp
// Per-agent — Tier-1 unmanaged component
[ComponentId(NavFakeIds.FakeCrowdAgentState)]
struct FakeCrowdAgentState {
    public Vector3 Target;
    public Vector3 LastSeenPosition;
    public Vector3 ComputedVelocity;
    public Vector3 DesiredVelocity;
    public float   Radius;
    public float   MaxSpeed;
    public float   MaxAcceleration;
    public byte    SeparationWeight;
    public byte    Flags;            // bit 0: ReachedTarget, bit 1: BlockedThisTick
    public ushort  NearbyAgentCount; // populated during Update
}

// Singleton — Tier-1 unmanaged component
[ComponentId(NavFakeIds.FakeCrowdGlobalState)]
struct FakeCrowdGlobalState {
    public uint TotalAgents;
    public uint TickCount;
    public float LastTickWallTimeMs;       // diagnostics
    public uint  TotalAvoidanceResolutions; // counter, diagnostics
}
```

### 4.3 Tick algorithm

`FakeDtCrowdProvider.Update(dt, view)` is called by `CrowdAgentUpdateSystem` once per simulation tick. Algorithm:

```
foreach agent in agents (in deterministic entity-id order):
    agent.LastSeenPosition := view.GetComponent<SimTransform>(entity).Position

    // 1. Desired velocity — straight toward target, capped by MaxSpeed
    toTarget := agent.Target - agent.LastSeenPosition
    distance := toTarget.Length
    if distance < ArrivalEpsilon (default 0.15 m):
        agent.Flags |= ReachedTarget
        agent.DesiredVelocity := Vector3.Zero
    else:
        agent.DesiredVelocity := toTarget.Normalized * min(MaxSpeed, distance / dt)

    // 2. Simple O(N²) separation force against nearby agents
    separationForce := Vector3.Zero
    agent.NearbyAgentCount := 0
    foreach other in agents where other != agent:
        delta := agent.LastSeenPosition - other.LastSeenPosition
        sqDist := delta.SqrMagnitude
        combinedRadius := agent.Radius + other.Radius
        if sqDist < (combinedRadius * 4)²:           // 4× radius perception range
            agent.NearbyAgentCount++
            if sqDist < (combinedRadius * 1.5)²:    // 1.5× radius separation kicks in
                push := delta.Normalized / max(sqrt(sqDist), 0.01)
                separationForce += push * agent.SeparationWeight

    // 3. Combine desired + separation, clamp acceleration
    target := agent.DesiredVelocity + separationForce
    delta := target - agent.ComputedVelocity
    maxDeltaThisTick := agent.MaxAcceleration * dt
    if delta.Length > maxDeltaThisTick:
        delta := delta.Normalized * maxDeltaThisTick
    agent.ComputedVelocity += delta

    // 4. Final speed clamp
    if agent.ComputedVelocity.Length > agent.MaxSpeed:
        agent.ComputedVelocity = agent.ComputedVelocity.Normalized * agent.MaxSpeed
```

Deterministic: agents iterated in entity-id order; no random sampling; no time-source other than `dt`. Same inputs across runs → same outputs.

O(N²) is fine: tests use ≤ 50 agents typically, ≤ 200 in stress tests. Real dtCrowd is O(N · neighbor-cap) via spatial grid; not modeled here.

### 4.4 Stuck/frustration interaction

The fake doesn't emit "stuck" events itself. Per Navigation Design §7.2, the existing `NavigationExecutionSystem` is the universal frustration watchdog reading `SimVelocity`. Because the fake writes `ComputedVelocity` and the `CrowdAgentUpdateSystem` copies it to `SimVelocity`, the watchdog inherits stuck-detection naturally:

- Test scenario: corner two agents into a deadlock → both `ComputedVelocity` ≈ 0 → `SimVelocity` ≈ 0 → `NavigationExecutionSystem` increments `FrustrationTicks` → after 120 ticks, writes `FailedBlocked`. Mechanism proves out.

### 4.5 The O10 suppression hook

`CrowdAgentUpdateSystem` is **engine code** (Navigation Design §7.2.1), not part of this fake. The fake's `Update(dt, view)` is called by it. The engine system is the one that early-outs on `Phase == AwaitingTraversal` — the fake is happy to be called or not called for any tick.

When the engine system suppresses an entity, it simply doesn't call `SetAgentTarget` or write `SimVelocity` for that entity that tick. The fake's `ComputedVelocity` is stale until the next non-suppressed tick. That's fine — no animation is reading the agent's crowd velocity during a montage anyway.

### 4.6 Test API surface

```csharp
public interface IFakeDtCrowdProviderTestApi
{
    /// <summary>Force an agent's computed velocity (override the steering output).
    /// Used to simulate "this agent can't make progress" scenarios deterministically.</summary>
    void OverrideAgentVelocity(Entity entity, Vector3 velocity);

    /// <summary>Clear an override.</summary>
    void ClearAgentVelocityOverride(Entity entity);

    /// <summary>Read the full snapshot for an agent (super-set of CrowdAgentSnapshot).</summary>
    FakeCrowdAgentState GetAgentState(Entity entity);

    /// <summary>Read global tick stats.</summary>
    FakeCrowdGlobalState GetGlobalState();
}
```

### 4.7 What it doesn't model

- **ORCA velocity-obstacles.** Real dtCrowd uses precise reciprocal velocity obstacles; the fake uses ad-hoc separation forces. Behavior under heavy crowd density will look quite different.
- **Path-corridor following with funnel string-pull.** Real dtCrowd follows a corridor with funnel smoothing; the fake just steers toward the next waypoint, with the `NavigationExecutionSystem` advancing waypoints on `ProgressS`. Diagonals are clipped sharply rather than smoothed.
- **Velocity prediction lookahead** for collision avoidance. The fake reacts only to current positions.
- **Crowd manager priorities** (some agents yield to others). All agents are peers in the fake.

---

## 5. `FakeVolumetricPathProvider`

### 5.1 Interface

Implements `IVolumetricPathProvider` per Navigation Design §9:

```csharp
public interface IVolumetricPathProvider
{
    bool   IsFlyable(Vector3 point);
    bool   PathExists(Vector3 a, Vector3 b, FlyProfile profile, float maxCost);
    int    Plan(Vector3 a, Vector3 b, FlyProfile profile, Span<NavWaypoint> output);
    uint   QueryVersion(BoundingBox3D bounds);
}

public struct FlyProfile {
    public float MinAltitude;
    public float MaxAltitude;
    public float ObstacleAvoidanceRadius;
}
```

### 5.2 Internal representation

```csharp
struct FakeVolumetricState {
    NoFlyVolume[] NoFlyZones;       // immutable after load
    float         MinAltitude;       // global floor (from test map)
    float         MaxAltitude;       // global ceiling
    uint          Version;
}

struct NoFlyVolume {
    BoundingBox3D Bounds;
    string        DebugName;
}
```

### 5.3 Algorithm

`Plan(a, b, profile, output)`:
- If neither `a` nor `b` is in any no-fly zone, and the straight line `a→b` doesn't intersect any no-fly zone bounds → return single waypoint at `b`. Direct route.
- If the straight line intersects no-fly zones → simple 3D A* over a coarse grid (e.g., 5 m cells) restricted to flyable cells. Returns waypoints at cell centers.
- All produced waypoints carry `TraversalKind = Walk` (the executor treats them as straight-line flight segments). `SurfaceType = Default`. `LayerMask = 0` (volumetric is not navmesh-layered).

This is the minimum viable volumetric pather — sufficient to prove `MobilityProfile = Flying` routes correctly through `PathfindingSolverSystem` to `IVolumetricPathProvider` instead of `INavmeshProvider`. It does not pretend to be a real volumetric pather.

### 5.4 Test API

```csharp
public interface IFakeVolumetricPathProviderTestApi
{
    void AddNoFlyZone(BoundingBox3D bounds, string debugName);
    void RemoveNoFlyZone(string debugName);
    NoFlyVolume[] GetNoFlyZones();
}
```

### 5.5 What it doesn't model

- **Wind, air density, lift constraints.**
- **Banking/turn-radius constraints** on the path itself (an aircraft path planner would consider min turn radius; the fake doesn't).
- **Altitude bands** (real volumetric pathers often slice into altitude layers; the fake treats the whole volume uniformly).

---

## 6. `IPathRegistry` fake implementations

The `IPathRegistry` interface is the shared read-API for path data (Navigation Design §6.2). Two implementations live alongside each other: `MusclePathRegistry` (the authoritative pool) and `BrainPathRegistry` (the on-demand Brain-side cache). In all-in-one deployment mode they collapse to a single shared instance.

### 6.1 `MusclePathRegistry`

The Muscle side is the authoritative storage. Backed by a `Dictionary<int, FakePathPoolEntry>` keyed by `RouteHandle`. Entries are inserted by the response handler when the Solver returns a path (Navigation Design §5.3); entries are removed when `ActionIdReleasePath` arrives, or when a subsequent `MoveTo`/`PlanRoute` overrides them for the same handle.

```csharp
struct FakePathPoolEntry {
    public int    RouteHandle;
    public NavWaypoint[] Waypoints;       // managed array — fake only
    public float  TotalDistanceMeters;
    public uint   NavmeshVersionAtPlan;
    public byte   PrimaryBackend;         // 0=Navmesh, 1=RoadGraph, 2=Spliced, 3=Volumetric
    public byte   Flags;                  // bit 0: HasOffMeshLinks
    public byte   ReplanCount;            // copied here on each in-place refresh
}

public sealed class MusclePathRegistry : IPathRegistry, IFakeMusclePathRegistryTestApi
{
    private readonly Dictionary<int, FakePathPoolEntry> _entries = new();

    public bool IsCached(int routeHandle)
        => _entries.ContainsKey(routeHandle);

    public bool TryGetSummary(int routeHandle, out PathSummary summary) { /* ... */ }
    public bool TryGetWaypoints(int routeHandle, Span<NavWaypoint> dest, out int count) { /* ... */ }
    public bool TryGetWaypointsSlice(int routeHandle, int startSegment, int maxCount,
                                     Span<NavWaypoint> dest, out int actualCount) { /* ... */ }

    // Muscle-internal mutation API (called by the response handler / replan logic)
    public void RegisterOrReplace(int routeHandle, NavWaypoint[] waypoints,
                                  float totalDist, uint navmeshVersion,
                                  byte primaryBackend, byte flags) { /* ... */ }
    public bool Free(int routeHandle) { /* ... */ }
}
```

**Allocation semantics for `RouteHandle = 0`:** When Brain passes `RouteHandle = 0` (fire-and-forget), `MusclePathRegistry.RegisterOrReplace` is called with a Muscle-allocated private handle starting at `0x40000000` and incrementing per registration. These private handles never leave Muscle. Brain-allocated handles use the composition `((entityIndex & 0xFFFFFF) << 8) | counter` and are guaranteed to be `< 0x40000000`, so the two ranges never collide.

### 6.2 `BrainPathRegistry`

The Brain side is a cache, populated only when Brain explicitly fetches details (via `Action_FetchPathDetails` or `PlanRouteParams.Flags.IncludeFullPathDetails`). Backed by an LRU dictionary keyed by `RouteHandle`, with a configurable cap (default 32 entries per Brain entity).

```csharp
[ComponentId(NavFakeIds.FakeBrainPathCacheEntry)]
struct FakeBrainPathCacheEntry {
    public int   RouteHandle;
    public byte  LastObservedReplanCount;     // for strict cache-miss policy
    public float TotalDistanceMeters;
    public uint  NavmeshVersionAtPlan;
    public byte  PrimaryBackend;
    public byte  WaypointCount;
    public ulong LastUsedTick;                // for LRU eviction
    // [InlineArray<NavWaypoint, MaxBrainCachedWaypoints>] Waypoints
}

public sealed class BrainPathRegistry : IPathRegistry, IFakeBrainPathRegistryTestApi
{
    private readonly EntityRepository _repo;
    private readonly int _maxEntriesPerEntity;  // default 32

    // The cache is stored as a FakeBrainPathCacheEntry component on the
    // Brain-side entity, one row per RouteHandle. LRU eviction iterates
    // the entity's entries and drops the oldest LastUsedTick.

    public bool IsCached(int routeHandle) { /* lookup; verify ReplanCount fresh */ }
    public bool TryGetWaypoints(int routeHandle, Span<NavWaypoint> dest, out int count) {
        // strict cache-miss policy:
        //   if entry not present → return false
        //   if entry's LastObservedReplanCount != current NavigationStatus.ReplanCount
        //     → return false (cache stale; entry stays for now and may be replaced by
        //       a subsequent NavigationPathDetailsResponseEvent materialization)
    }
    // ... etc
}
```

Cache materialization happens in a dedicated `NavigationPathDetailsUpdateSystem` (Brain-side) that consumes `NavigationPathDetailsResponseEvent`s and writes entries via the registry's internal API:

```csharp
internal void IngestResponse(Entity brainEntity, int routeHandle,
                             NavWaypoint[] waypoints, byte replanCount, ...);
```

Eviction occurs:
- **LRU**: when the cap is exceeded, the oldest-`LastUsedTick` entry is removed.
- **Explicit**: `Action_ReleasePath(handle)` triggers a remove (the Muscle frees its pool entry; the Brain ingress translator catches the corresponding `NavigationStatus` transition or release-ack and evicts).

### 6.3 The Brain-side handle allocator

`NavigationHandleAllocator` is a static helper (not a fake — it ships as engine code), but the fake registry must understand its handle scheme to allocate non-colliding Muscle-private handles. The allocator composition:

```csharp
public static class NavigationHandleAllocator {
    // Brain calls this when a BTree wants an introspectable handle.
    // Composition: ((entityIndex & 0xFFFFFF) << 8) | (counter & 0xFF)
    // Always returns > 0 and < 0x40000000.
    // 0 is reserved for "Brain not providing a handle"; values
    // >= 0x40000000 are reserved for Muscle-internal handles.
    public static int Allocate(Entity brainEntity) { /* ... */ }
}
```

The fake doesn't try to enforce uniqueness across non-cooperating Brain entities — it trusts the allocator. Tests can construct collisions deliberately to verify failure-mode handling (see DD-Tests-Nav scenario S12).

### 6.4 All-in-one shared implementation

In all-in-one deployment mode, Brain and Muscle live in the same process. Their `TrajectoryPoolManager` is logically the same memory, so the registries collapse to a single shared instance:

```csharp
public sealed class SharedPathRegistry : IPathRegistry
{
    private readonly MusclePathRegistry _muscle;

    // Both Brain and Muscle look up the same path data via this instance.
    // The MusclePathRegistry's pool IS the cache; there's no separate
    // Brain-side cache when in all-in-one mode.

    public bool IsCached(int handle) => _muscle.IsCached(handle);
    public bool TryGetWaypoints(int handle, Span<NavWaypoint> dest, out int count)
        => _muscle.TryGetWaypoints(handle, dest, out count);
    // ... etc — pure forwarding
}
```

The `NavigationFakesModule` decides which to register based on the deployment mode it detects at startup (number of distinct `ModuleHostKernel` instances in-process, or an explicit configuration flag).

The integration-test default is **all-in-one with `SharedPathRegistry`** — the registry calls round-trip locally, no DDS, no serialization, and the test harness can verify cache contents by direct inspection of the underlying `Dictionary`.

### 6.5 Test API surface

```csharp
public interface IFakeMusclePathRegistryTestApi
{
    /// <summary>Inspect the full pool (handle → entry).</summary>
    IReadOnlyDictionary<int, FakePathPoolEntry> Snapshot();

    /// <summary>Force-corrupt an entry's ReplanCount — used to simulate
    /// stale-cache scenarios in tests.</summary>
    void ForceReplanCountBump(int routeHandle);

    /// <summary>Clear the pool (test cleanup).</summary>
    void Clear();
}

public interface IFakeBrainPathRegistryTestApi
{
    /// <summary>Inspect a Brain entity's cache contents.</summary>
    IReadOnlyList<FakeBrainPathCacheEntry> SnapshotEntityCache(Entity brainEntity);

    /// <summary>Force-evict a specific entry (test-driven eviction).</summary>
    bool EvictEntry(Entity brainEntity, int routeHandle);

    /// <summary>Stats: hits, misses, evictions, current size per entity.</summary>
    FakePathRegistryStats GetStats();
}

struct FakePathRegistryStats {
    public long Hits;
    public long Misses;
    public long StaleMisses;        // entry was present but ReplanCount differed
    public long Evictions;
    public int  TotalEntriesAcrossEntities;
}
```

These exist only on the fake, never on the real interface.

### 6.6 What it doesn't model

- **Cross-process serialization cost.** The real `BrainPathRegistry` populates from `NavigationPathDetailsResponseEvent`s that have been DDS-deserialized; in tests this is direct memory copy.
- **Concurrent access patterns.** Real registries may need locking for the Solver-thread/Muscle-thread interaction; the fake assumes single-threaded test execution.
- **Memory pressure / large-path streaming.** Real navmesh paths might exceed `MaxBrainCachedWaypoints` and require slice-based access; the fake silently truncates and reports `actualCount < requestedCount`.

---

## 7. Test-map data format (`NavTestMap`)

Two authoring formats are supported:
1. **JSON** — for canonical, version-controlled, shareable fixtures.
2. **Fluent in-code DSL** — for quick one-off test setup.

### 6.1 JSON schema

```json
{
  "name": "off_mesh_jump_map",
  "description": "Single layer, 30m x 30m, two adjacent platforms with a 4m gap requiring JumpAcross.",
  "min_altitude": 0,
  "max_altitude": 200,
  "layers": [
    {
      "layer": "Infantry",
      "polygons": [
        {
          "id": 0,
          "vertices": [[0,0,0],[10,0,0],[10,10,0],[0,10,0]],
          "surface_type": "Concrete",
          "is_blocked": false
        },
        {
          "id": 1,
          "vertices": [[14,0,0],[24,0,0],[24,10,0],[14,10,0]],
          "surface_type": "Concrete",
          "is_blocked": false
        }
      ],
      "adjacency": [[], []],
      "off_mesh_links": [
        {
          "from": 0, "to": 1,
          "start": [10, 5, 0], "end": [14, 5, 0],
          "kind": "JumpAcross",
          "cost": 5.0
        }
      ]
    }
  ],
  "no_fly_zones": []
}
```

Stored under `tests/data/navmaps/`. Parsed at scenario load by `NavTestMapLoader` (a thin JSON deserializer using the engine's existing JSON conventions; see Navigation Design §17).

### 6.2 In-code DSL

```csharp
var map = new NavTestMapBuilder()
    .WithName("off_mesh_jump_map")
    .Layer(NavLayerMask.Infantry, l => l
        .Polygon(0, new Vector3[] { (0,0,0), (10,0,0), (10,10,0), (0,10,0) }, SurfaceType.Concrete)
        .Polygon(1, new Vector3[] { (14,0,0), (24,0,0), (24,10,0), (14,10,0) }, SurfaceType.Concrete)
        .OffMeshLink(from: 0, to: 1,
                     start: (10,5,0), end: (14,5,0),
                     kind: TraversalKind.JumpAcross, cost: 5.0f))
    .Build();
```

Useful for tests that construct ad-hoc maps inline without round-tripping through disk.

### 6.3 Canonical fixtures shipped with the engine

The following maps live under `tests/data/navmaps/` and are referenced by name from integration tests (DD-Tests-Nav §6):

| Filename | Purpose |
|---|---|
| `corridor.json` | Single layer, single straight 30 m path, no obstacles. Sanity. |
| `l_bend.json` | Single layer, two polygons meeting at right angle. Tests window-slide on corridor turn. |
| `two_layers.json` | Infantry layer with narrow passage, Vehicle layer wrapping around. Tests `NavLayerMask` routing. |
| `off_mesh_jump.json` | Infantry path with one `JumpAcross` link. Tests off-mesh montage trigger. |
| `replan.json` | Path exists at start; test API blocks middle polygon to force replan. |
| `crowded.json` | 10m × 10m open polygon, multiple spawn/destination points for crossing flow. Tests dtCrowd avoidance. |
| `stuck.json` | Destination polygon disconnected from start polygon → `FailedUnreachable`. |
| `frustration.json` | Three-agent dead-end pocket; forces deadlock for frustration watchdog. |
| `flying.json` | Two flying waypoints with a no-fly box between them. Tests `IVolumetricPathProvider` routing. |
| `naval.json` | Naval layer over a water polygon, with a land obstacle. |

Each canonical map is also exposed via a `NavTestMaps` static helper (`NavTestMaps.LoadCorridor()`, etc.) for IDE-friendly test authoring.

---

## 8. The diagnostic ImGui window

A standalone ImGui window registered through the engine's `IWindowRegistrar` pattern (matching DD-Fake §7.3). Opt-in (off by default; developer toggles via standard window menu). Title: `"Fake Navigation Backends"`. Menu category: `"Navigation"`.

### 8.1 Four-tab layout

The window has four top-level tabs, one per fake category, plus a global header:

```
┌── Fake Navigation Backends ────────────────────────────────────────────┐
│  Map: corridor.json    Tick: 4327    Frustration agents: 0            │
│  [ Navmesh ]  [ Crowd ]  [ Volumetric ]  [ Paths ]                    │
│                                                                        │
│  <selected tab content>                                                │
│                                                                        │
│  [ Snapshot JSON ]  [ Reset crowd ]  [ Reload map ]                   │
└────────────────────────────────────────────────────────────────────────┘
```

### 8.2 Navmesh tab

Shows the loaded map's structure per layer:

```
Layer: Infantry [v=1]
  Polygons: 4
    ▸ Poly 0  surface=Concrete  blocked=false  area=100.0 m²
    ▸ Poly 1  surface=Concrete  blocked=false  area=100.0 m²
    ▾ Poly 2  surface=Grass     blocked=TRUE   area=50.0 m²
        Adjacent: [0, 1, 3]
        OffMesh:  ← from 1 (JumpAcross, cost=5.0)
        [ Unblock ]
    ▸ Poly 3  surface=Concrete  blocked=false  area=200.0 m²
  Off-mesh links: 1
    JumpAcross (1 → 2)  start=(10, 5, 0)  end=(14, 5, 0)

Layer: Vehicle [v=1]
  Polygons: 2
  ...

Buttons: [ Block selected polygon ]  [ Bump version ]
```

Each polygon row is expandable. The "Block / Unblock" buttons drive `IFakeNavmeshProviderTestApi`. The "Bump version" simulates a navmesh patch arriving on that layer/region — useful to test the version-handshake replan path manually.

### 8.3 Crowd tab

Two views: agent list and per-agent detail.

**List view** (default when no agent selected):
```
Crowd agents: 14
NetId  Entity  Pos               Vel        Target          ToTarget   Nearby  Status
 #042  e_15g1  (12.3, 4.2, 0)  (2.1, 0.0)  (24, 4.2, 0)    11.7 m       2     Following
 #043  e_16g1  (12.5, 5.8, 0)  (0.0, 0.0)  (24, 5.8, 0)    11.5 m       2     BLOCKED ← frustration ticks: 47
 #044  e_17g2  (3.2, 8.0, 0)  (1.8, 0.5)  (15, 12, 0)    13.0 m       0     Following
 ...
[ Click row to drill into details ]
```

**Detail view** (when an agent is selected):
```
Agent #043  Entity e_16g1
  Position:        (12.5, 5.8, 0)
  Velocity:        (0.000, 0.000, 0.000)
  Desired:         (2.000, 0.000, 0.000)
  Target:          (24, 5.8, 0)
  Distance:        11.5 m
  Reached:         no
  Nearby agents:   2 → [#042, #045]
  Radius:          0.4 m
  MaxSpeed:        3.5 m/s
  MaxAccel:        8 m/s²
  Separation wt:   2
  
  NavigationStatus  (read-only mirror)
  Phase:           Following
  Result:          InProgress
  RouteHandle:     0x18FA01  (← jumps to Paths tab on click)
  ProgressS:       8.3 m  /  total 21.5 m  (38.6%)
  SegmentIndex:    2  /  4
  ReplanCount:     0
  FrustrationTicks: 47   ← elevated, agent is stuck
  
  Recent events (last 8):
    Tick 4280  MoveStartedEvent
    Tick 4310  WaypointReachedEvent (idx 1)
    Tick 4321  WaypointReachedEvent (idx 2)
    
  Buttons:
    [ Override velocity → 0,0,0 ]
    [ Override velocity → custom ]
    [ Clear override ]
    [ Back to list ]
```

### 8.4 Volumetric tab

```
No-fly zones: 2
  ▸ "primary_no_fly"   bounds: (10,10,0)..(20,20,100)
  ▸ "secondary"         bounds: (30,5,50)..(35,15,80)

Active flying agents: 0
  (no agents currently flying — register one or load a flying scenario)

[ Add no-fly zone ]  [ Clear all zones ]
```

### 8.5 Paths tab

Three sub-views: Muscle pool, Brain caches, and stats.

**Sub-view: Muscle pool** (the authoritative path storage):
```
MusclePathRegistry — total entries: 7

Handle      Owner Entity   Waypts  Dist     Backend     Replan   Off-mesh
0x00018F01  e_15g1            14   21.5 m   Navmesh         0    yes
0x00018F02  e_15g1             8    9.0 m   Navmesh         0    no
0x00019202  e_17g2             5   12.0 m   RoadGraph       0    no
0x40000003  (muscle-private)  21   45.0 m   Spliced         2    no   ← fire-and-forget MoveTo
...

[ Click row to expand and show full waypoint list ]
[ Click on Owner Entity to jump to Crowd tab detail view ]
```

Expanded row shows the full waypoint list:
```
Handle 0x00018F01  (owner: e_15g1)
  TotalDistance:       21.5 m
  NavmeshVersionAtPlan: 3
  PrimaryBackend:      Navmesh
  Flags:               HasOffMeshLinks
  ReplanCount:         0
  Waypoints (14):
    [ 0]  Position=(12.0, 4.0, 0.0)   Walk        Concrete   layer=Infantry  segLen=2.0
    [ 1]  Position=(14.0, 4.0, 0.0)   Walk        Concrete   layer=Infantry  segLen=2.0
    [ 2]  Position=(14.0, 4.0, 0.0)   JumpAcross  Concrete   layer=Infantry  segLen=4.0
    [ 3]  Position=(18.0, 4.0, 0.0)   Walk        Concrete   layer=Infantry  segLen=2.0
    ...
  [ Force ReplanCount++ ]  [ Free entry ]
```

**Sub-view: Brain caches** (per-entity cache contents):
```
BrainPathRegistry — total entities with caches: 2

Entity e_15g1   cache entries: 2 / 32   hits: 47  misses: 3  stale-misses: 1
  Handle      Replan-obs  Replan-cur  Waypts  Last-used  Status
  0x00018F01      0           0          14   tick 4321  fresh
  0x00018F02      0           0           8   tick 4280  fresh
  [ Evict 0x00018F02 ]  [ Clear entity cache ]

Entity e_17g2   cache entries: 1 / 32   hits: 12  misses: 0  stale-misses: 0
  Handle      Replan-obs  Replan-cur  Waypts  Last-used  Status
  0x00019202      1           2           5   tick 4310  STALE ← will return false from TryGetWaypoints
  [ Evict 0x00019202 ]  [ Clear entity cache ]
```

The "Replan-cur" column reads the live `NavigationStatus.ReplanCount` from the Muscle's view; "Replan-obs" reads the cached `LastObservedReplanCount`. When they differ, the row is highlighted "STALE" and a `TryGetWaypoints` would return false (strict cache-miss policy).

**Sub-view: Stats** (global registry stats):
```
Global registry stats (since scenario start):
  Muscle pool:
    Total entries currently:           7
    Peak entries:                     12
    Total registrations:              34
    Total frees:                      27
    Total in-place replaces (replan): 11
  
  Brain caches (aggregate across all Brain entities):
    Total hits:                       59
    Total misses:                      3
    Total stale-misses:                1
    Total LRU evictions:               2
    Total explicit-release evictions:  4
    Current total cached entries:      3
    Largest entity cache:              2 (e_15g1)
  
  Deployment mode: All-in-one (SharedPathRegistry active)
    → Brain and Muscle share the same dictionary; "Brain caches"
      sub-view shows logical cache view based on which entries
      Brain has touched.
```

The stats sub-view is the primary surface for verifying cache behavior is healthy: if "stale-misses" is climbing rapidly, an `AutoSendPathOnReplan` flag is probably missing somewhere. If "LRU evictions" is climbing, the per-entity cache cap may be too small for the workload.

### 8.6 Footer actions

- **Snapshot JSON** — captures the entire fake state (all four backends) to clipboard as JSON (§9).
- **Reset crowd** — calls `UnregisterAgent` for every agent. Used to recover from confused states during interactive debugging.
- **Reload map** — re-parse the loaded map file from disk (development convenience; not for tests, which use in-code construction).

### 8.7 Registration

```csharp
public sealed class FakeNavigationInspectorWindow : IDiagnosticWindow
{
    public string Title => "Fake Navigation Backends";
    public string MenuCategory => "Navigation";
    public bool   IsVisible { get; set; }
    
    private readonly ISimulationView _view;
    private Entity _selectedAgent;
    private int    _selectedRouteHandle;
    private Entity _selectedBrainEntity;
    
    public void Draw(IImGuiContext ctx)
    {
        if (!IsVisible) return;
        if (ImGui.Begin(Title, ref _visible))
        {
            DrawHeader(ctx);
            if (ImGui.BeginTabBar("nav_tabs"))
            {
                if (ImGui.BeginTabItem("Navmesh"))    { DrawNavmeshTab(ctx);    ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Crowd"))      { DrawCrowdTab(ctx);      ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Volumetric")) { DrawVolumetricTab(ctx); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("Paths"))      { DrawPathsTab(ctx);      ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
            DrawFooter(ctx);
        }
        ImGui.End();
    }
}

// NavigationFakesModule implements IWindowRegistrar:
public sealed class NavigationFakesModule : IEcsModule, IDisposable, IWindowRegistrar
{
    public void RegisterWindows(IWindowManager wm)
    {
        if (HeadlessMode.IsHeadless) return;        // standard convention
        wm.RegisterWindow(new FakeNavigationInspectorWindow(_view));
    }
}
```

Same registration shape as `FakeAnimBackendInspectorWindow` in DD-Fake — no novelty, just navigation-specific tab contents.

---

## 9. JSON snapshot export

The "Snapshot JSON" button captures the entire fake state (all four backends + global tick count + currently-loaded map name) and writes a structured JSON payload to clipboard. Intended uses:

- Paste into a bug report when a behavior is misbehaving.
- Diff snapshots across two runs to find a non-determinism source.
- Feed into the integration-test harness as a starting state for a regression test.

```json
{
  "captured_at_tick": 4327,
  "loaded_map": "off_mesh_jump_map",
  "navmesh": { "layers": [ { "layer": "Infantry", "version": 3, "blocked_polygons": [2] }, ... ] },
  "crowd": {
    "tick_count": 4327,
    "agents": [
      { "entity": "e_15g1", "pos": [12.3, 4.2, 0], "vel": [2.1, 0, 0], "target": [24, 4.2, 0],
        "nav_status": { "phase": "Following", "result": "InProgress", "progress_s": 8.3, "frustration_ticks": 0 } },
      ...
    ]
  },
  "volumetric": { "no_fly_zones": [...] },
  "path_registry": {
    "muscle_pool": {
      "entries": [
        { "handle": 1638401, "waypoint_count": 14, "total_distance_m": 21.5, "navmesh_version": 3, "primary_backend": "Navmesh" },
        ...
      ]
    },
    "brain_caches": [
      { "entity": "e_15g1",
        "cache_entries": [
          { "handle": 1638401, "last_observed_replan_count": 0, "waypoint_count": 14 }
        ] },
      ...
    ]
  }
}
```

The structure matches the snapshot of the equivalent FakeAnimationBackend (DD-Fake §8) so engineers familiar with one can quickly read the other.

---

## 10. AAR recording integration

The state components are `[ComponentId]`-allocated Tier-1 unmanaged components (§12). The engine's existing Flight Recorder records them automatically via its raw-memory-copy fast path — no per-fake recording code needed.

Replay then naturally restores the fake's agent table and per-agent state by replaying the component data into chunks. During replay, the fakes' `Update` methods are suppressed by the engine's standard replay-isolation pattern (`TogglableSimulationGroup` disabled per `ReferenceReplayLoadHandler`).

Caveats:
- The navmesh map structure (polygons/adjacency/off-mesh-links) is **not** in the per-tick ECS data. It's loaded once at scenario load. Replays therefore depend on the same map being available on the replay host. This is consistent with how baked-navmesh data behaves in production (also static-per-scenario).
- The `FakeNavmeshState.layer.Version` is in the singleton component and is recorded — so test-API patches (`BlockPolygon`, `BumpVersion`) replay correctly.

---

## 11. Determinism and hard-assert discipline

**Determinism guarantees:**
- Identical sequence of `Update` calls + identical initial `NavTestMap` → identical outputs across runs.
- Entity iteration order: by sorted entity index, not insertion order.
- No floating-point reductions that depend on iteration order.
- No use of `DateTime.UtcNow`, `Random.Shared`, or any unseeded RNG.

**Hard-assert discipline** (mirrors DD-Fake §6):
- Invariants checked with `Debug.Assert` in dev builds, no-op in release.
- Checked invariants include: every registered crowd agent has a `FakeCrowdAgentState` component; every polygon ID referenced by adjacency or off-mesh-link exists; no agent has `Radius <= 0`; etc.
- A failed assertion in dev throws and fails the test deterministically; no soft "log and continue" paths.

---

## 12. ComponentId allocation

Following the precedent in DD-Fake §11 (block 220-249 for animation fakes), navigation fakes occupy **block 250-279**:

```csharp
public static class NavFakeIds
{
    public const int FakeNavmeshState         = 250;  // singleton — navmesh + layers
    public const int FakeCrowdGlobalState     = 251;  // singleton — tick stats
    public const int FakeCrowdAgentState      = 252;  // per-agent
    public const int FakeVolumetricState      = 253;  // singleton — no-fly zones
    public const int FakePathPoolEntry        = 254;  // singleton-keyed (Muscle pool entries)
    public const int FakeBrainPathCacheEntry  = 255;  // per-entity (Brain-side cache buffer)
    public const int FakePathRegistryStats    = 256;  // singleton — registry diagnostics
    // 257–279 reserved for future expansion (additional agent kinds, debug overlays).
}
```

---

## 13. What the fakes intentionally don't do

- **No rendering.** No SVG output, no debug-draw of polygons into the game viewport. The ImGui window is the only visualization. (A future DD could add a `IMapLayer` for the 2D scenario editor to render polygon outlines — would be a small addition, not in this design.)
- **No asset hot-reload of the map.** "Reload map" in the inspector window is a development convenience; tests construct maps in-code or load JSON at setup.
- **No cross-fake consistency enforcement.** If a test loads a navmesh with an off-mesh link but the crowd radius is larger than the link width, the fakes won't warn — the test would manifest as an entity getting stuck mid-jump. This is acceptable; test authors are responsible for sensible setups.
- **No fault injection beyond the test APIs.** No "randomly drop 10% of paths" mode. If a test needs a path failure, it uses `IFakeNavmeshProviderTestApi.BlockPolygon` directly.
- **No partial dynamic obstacles.** Block/unblock is at polygon granularity.

---

*End DD-Fake-Nav.*
