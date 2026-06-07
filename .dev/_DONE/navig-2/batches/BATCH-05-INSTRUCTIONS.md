# BATCH-05 Instructions — Phase 2 Part B: FakeDtCrowdProvider + NavTestMap + NavigationFakesModule

**Batch ID:** BATCH-05
**Phase:** 2 Part B
**Tasks:** Pre-fix + NAV-P2-T2 + NAV-P2-T5
**Depends on:** BATCH-04 committed (fake backends, path registries, NavigationTestWorldFactory fix)

**Design references:**
- DD-Fake-Nav.md §4 (FakeDtCrowdProvider)
- DD-Fake-Nav.md §7 (NavTestMap format)
- DD-Fake-Nav.md §2, §11, §16 (NavigationFakesModule)
- TASK-DETAILS.md NAV-P2-T2, NAV-P2-T5

---

## Critical rules (read before touching any file)

1. **No new assemblies** — all nav production code goes into `FDP/Toolkits/Fdp.Toolkits/` under namespace `Fdp.Toolkit.Navigation` (or sub-namespace `.Fake`). Tests go into `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/`.

2. **ComponentId collision avoidance** — `PerceptionReceptor` in `Fdp.Toolkit.Perception` has `[ComponentId(251)]`. Do NOT use IDs 250-261 for fake nav ECS components. Use 262-267 (the safe sub-block above the NavigationContractsComponentIds 257-261 range).

3. **Newtonsoft.Json** — the project already has `Newtonsoft.Json 13.0.3` as a reference in `Fdp.Toolkits.csproj`. Use it for JSON deserialization in `NavTestMapLoader`. Do NOT add `System.Text.Json` or any new package reference.

4. **`FrustrationTicks` clash** — NavigationContractsComponentIds constants were changed from `byte` to `int` and from 69-73 to 257-261 in the preceding fix. Do not reference the old values.

5. **`SurfaceType.Generic` not `.Default`** — DDS IDL uses `Default` as a keyword. Always use `SurfaceType.Generic` in any test data.

6. **No CA2014** — never `stackalloc` inside a loop body. Lift any fixed-size scratch arrays to static readonly fields.

7. **No `Moq` for `Span<T>` parameters** — use concrete stub/fake classes when you need to mock interfaces that expose `Span<T>`.

8. **Delete NavFakeIds comment about 250-279** — the DD-Fake-Nav.md §12 says "block 250-279" but that was wrong (conflict with PerceptionReceptor=251, NavigationContractsComponentIds 257-261). Update the comment in NavFakeIds.cs to "block 262-279".

---

## Pre-fix: Update NavFakeIds to safe block 262-279

The constants were defined in BATCH-04 but not yet referenced by any `[ComponentId]` attribute. Change them now before implementing the crowd ECS components in NAV-P2-T2:

```csharp
// In: FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavFakeIds.cs
public static class NavFakeIds
{
    // IDs 262-279: navigation fake ECS component block.
    // Block 250-261 is unavailable:
    //   250-256 originally reserved here but 251 conflicts with PerceptionReceptor,
    //   257-261 taken by NavigationContractsComponentIds (nav v2 production components).
    public const int FakeNavmeshState        = 262;
    public const int FakeCrowdGlobalState    = 263;
    public const int FakeCrowdAgentState     = 264;
    public const int FakeVolumetricState     = 265;
    // 266: reserved (formerly FakePathPoolEntry — not an ECS component; stored in dictionary)
    public const int FakeBrainPathCacheEntry = 267;
    public const int FakePathRegistryStats   = 268;
}
```

---

## Task 1: NAV-P2-T2 — `IDtCrowdProvider` + `FakeDtCrowdProvider`

### 1.1 Pin the interface in production code

Create `FDP/Toolkits/Fdp.Toolkits/Navigation/IDtCrowdProvider.cs`:

```csharp
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// Crowd steering provider. Implemented by <c>FakeDtCrowdProvider</c> for tests and
    /// eventually by a DotRecast/dtCrowd port for production.
    /// </summary>
    public interface IDtCrowdProvider
    {
        /// <summary>Add an agent. Returns false if the entity is already registered.</summary>
        bool RegisterAgent(Entity entity, in CrowdAgentParams parameters);

        /// <summary>Remove an agent. Safe to call if not registered.</summary>
        void UnregisterAgent(Entity entity);

        /// <summary>Update the agent's steering target. Idempotent within a tick.</summary>
        void SetAgentTarget(Entity entity, Vector3 target);

        /// <summary>
        /// Advance the crowd simulation by <paramref name="dt"/> seconds.
        /// Reads <see cref="SimTransform"/> from <paramref name="view"/> for each agent;
        /// writes per-agent velocity outputs via <see cref="GetAgentVelocity"/>.
        /// </summary>
        void Update(float dt, ISimulationView view);

        /// <summary>Get the crowd-computed velocity for an agent (set by last <see cref="Update"/>).</summary>
        Vector3 GetAgentVelocity(Entity entity);

        /// <summary>Read current agent state. Returns false if entity is not registered.</summary>
        bool TryGetAgentSnapshot(Entity entity, out CrowdAgentSnapshot snapshot);
    }

    /// <summary>
    /// Parameters used when registering an agent with <see cref="IDtCrowdProvider"/>.
    /// </summary>
    public struct CrowdAgentParams
    {
        /// <summary>Agent collision radius in metres (typically VehicleParams.Width * 0.5).</summary>
        public float Radius;

        /// <summary>Agent standing height in metres.</summary>
        public float Height;

        /// <summary>Maximum speed in m/s.</summary>
        public float MaxSpeed;

        /// <summary>Maximum acceleration in m/s^2.</summary>
        public float MaxAcceleration;

        /// <summary>Separation preference weight. Default 2. Higher = stronger separation.</summary>
        public byte SeparationWeight;
    }

    /// <summary>
    /// Read-only snapshot of an agent's current crowd-internal state (for diagnostics).
    /// </summary>
    public struct CrowdAgentSnapshot
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Target;
        public Vector3 DesiredVelocity;
        public bool    ReachedTarget;
        public int     NearbyAgentCount;
    }
}
```

### 1.2 FakeCrowdAgentState and FakeCrowdGlobalState ECS components

Add these as **unmanaged structs with `[ComponentId]` attributes** to a new file
`FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeCrowdComponents.cs`:

```csharp
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Per-agent crowd simulation state for <see cref="FakeDtCrowdProvider"/>.
    /// Stored as an ECS component to enable AAR recording and entity-inspector visibility.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavFakeIds.FakeCrowdAgentState)]
    public struct FakeCrowdAgentState
    {
        public Vector3 Target;
        public Vector3 LastSeenPosition;
        public Vector3 ComputedVelocity;
        public Vector3 DesiredVelocity;
        public float   Radius;
        public float   MaxSpeed;
        public float   MaxAcceleration;
        public byte    SeparationWeight;
        /// <summary>Bit 0: ReachedTarget, Bit 1: BlockedThisTick, Bit 2: VelocityOverrideActive.</summary>
        public byte    Flags;
        public ushort  NearbyAgentCount;
        // Velocity override: only valid when Flags bit 2 is set.
        public Vector3 VelocityOverride;
    }

    /// <summary>
    /// Singleton crowd simulation global state for <see cref="FakeDtCrowdProvider"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavFakeIds.FakeCrowdGlobalState)]
    public struct FakeCrowdGlobalState
    {
        public uint  TotalAgents;
        public uint  TickCount;
        public uint  TotalAvoidanceResolutions;
        public float LastTickWallTimeMs;
    }
}
```

### 1.3 Implement `FakeDtCrowdProvider`

Create `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeDtCrowdProvider.cs`.

**Interface:** implements `IDtCrowdProvider` + `IFakeDtCrowdProviderTestApi`.

**State storage:** a `Dictionary<Entity, FakeCrowdAgentState>` (NOT ECS — the ECS component is for recording/inspection only; the in-memory dictionary is the live steering table). The ECS singleton `FakeCrowdGlobalState` is also maintained separately as a field.

**Key invariants:**
- Agent iteration order in `Update` is deterministic: iterate by sorted entity index (ascending).
- No randomness, no `DateTime.UtcNow`.
- `FrustrationTicks` watchdog interaction: the fake writes `ComputedVelocity`; `CrowdAgentUpdateSystem` copies it to `SimVelocity`; the existing `NavigationExecutionSystem` detects stuck agents.

**`Update(dt, view)` algorithm** (exactly as described in DD-Fake-Nav §4.3):

```
For each agent (sorted by Entity.Index ascending):

1. Read LastSeenPosition from view.GetComponent<SimTransform>(entity).Position
   - If entity no longer exists in view: skip (agent was destroyed mid-tick)

2. Desired velocity:
   toTarget = agent.Target - LastSeenPosition
   distance = |toTarget|
   ArrivalEpsilon = 0.15f
   if distance < ArrivalEpsilon:
       Flags |= ReachedTarget (bit 0)
       DesiredVelocity = Vector3.Zero
   else:
       DesiredVelocity = normalize(toTarget) * min(MaxSpeed, distance / dt)

3. Separation force (O(N^2)):
   separationForce = Vector3.Zero
   NearbyAgentCount = 0
   for each other agent != self:
       delta = agent.LastSeenPosition - other.LastSeenPosition
       sqDist = |delta|^2
       combinedRadius = agent.Radius + other.Radius
       if sqDist < (combinedRadius * 4)^2:
           NearbyAgentCount++
           if sqDist < (combinedRadius * 1.5)^2:
               push = normalize(delta) / max(sqrt(sqDist), 0.01f)
               separationForce += push * agent.SeparationWeight

4. Combine and clamp acceleration:
   target_vel = DesiredVelocity + separationForce
   delta_vel = target_vel - ComputedVelocity
   maxDelta = MaxAcceleration * dt
   if |delta_vel| > maxDelta:
       delta_vel = normalize(delta_vel) * maxDelta
   ComputedVelocity += delta_vel

5. Final speed clamp:
   if |ComputedVelocity| > MaxSpeed:
       ComputedVelocity = normalize(ComputedVelocity) * MaxSpeed

6. Apply velocity override (test API):
   if Flags & VelocityOverrideActive (bit 2):
       ComputedVelocity = VelocityOverride

7. Update global tick counter and stats
```

**Test API** (`IFakeDtCrowdProviderTestApi`):

```csharp
public interface IFakeDtCrowdProviderTestApi
{
    void OverrideAgentVelocity(Entity entity, Vector3 velocity);
    void ClearAgentVelocityOverride(Entity entity);
    FakeCrowdAgentState GetAgentState(Entity entity);
    FakeCrowdGlobalState GetGlobalState();
}
```

Define this interface in the same file as `IDtCrowdProvider` or a separate `IFakeDtCrowdProviderTestApi.cs` in the `Fake` namespace.

### 1.4 Tests: `FakeDtCrowdProviderTests.cs`

Create `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/FakeDtCrowdProviderTests.cs`.

Implement exactly the 10 tests from DD-Tests-Nav §3.2:

| Test method | Scenario | Key assertion |
|---|---|---|
| `RegisterAgent_NewEntity_ReturnsTrue` | Fresh entity | `RegisterAgent` returns true |
| `RegisterAgent_AlreadyRegistered_ReturnsFalse` | Same entity twice | Second call returns false |
| `UnregisterAgent_PreviouslyRegistered_Removes` | Register then unregister | `TryGetAgentSnapshot` returns false afterwards |
| `Update_OneAgent_StraightToTarget_Converges` | Single agent, target 10 m away | Agent position converges within 60 ticks (use mock view updating SimTransform each tick) |
| `Update_AgentAtTarget_VelocityZero_ReachedFlag` | Agent already at target | `GetAgentVelocity` returns ~Vector3.Zero; `CrowdAgentSnapshot.ReachedTarget == true` |
| `Update_TwoAgentsCrossingPaths_Avoid` | Two agents crossing, starting from opposite corners | Both eventually reach their targets; minimum separation at any tick ≥ combined radius × 0.8 |
| `Update_AgentSurroundedByThreeStationary_VelocityNearZero` | One moving agent surrounded by 3 blockers | After 20 ticks, moving agent speed < 0.1 m/s |
| `OverrideAgentVelocity_TestApiBypassesSteering` | Override to zero | `GetAgentVelocity` returns the override value, not the steering output |
| `Determinism_SameInputs_SameOutputs` | Two providers, same updates in same order | Positions and velocities identical after 30 ticks |
| `Update_LargeAgentCount_Completes` | 200 agents, random targets | No exception, no NaN in velocities after 10 ticks |

**Important for `Update_OneAgent_StraightToTarget_Converges` and the crossing test:**
The `Update` method reads `SimTransform` from the `ISimulationView`. In tests, create a fake `ISimulationView` that applies the computed velocity to the entity's SimTransform each tick (simulating the `CrowdAgentUpdateSystem` writing to `SimVelocity` and kinematics applying it). 

Since `ISimulationView` may have `Span<T>` parameters, implement a concrete `TestSimulationView` helper class rather than using Moq.

---

## Task 2: NAV-P2-T5 — `NavTestMap` + `NavTestMapLoader` + `NavTestMapBuilder` + `NavigationFakesModule`

### 2.1 `NavTestMap` data model

Create `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavTestMap.cs`:

```csharp
namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Immutable in-memory representation of a navigation test map.
    /// Loaded from JSON via <see cref="NavTestMapLoader"/> or built with <see cref="NavTestMapBuilder"/>.
    /// </summary>
    public sealed class NavTestMap
    {
        public string         Name        { get; }
        public string         Description { get; }
        public float          MinAltitude { get; }
        public float          MaxAltitude { get; }
        public FakeNavLayer[] Layers      { get; }
        public NoFlyVolume[]  NoFlyZones  { get; }

        public NavTestMap(string name, string description, float minAltitude, float maxAltitude,
                          FakeNavLayer[] layers, NoFlyVolume[] noFlyZones)
        {
            Name        = name;
            Description = description;
            MinAltitude = minAltitude;
            MaxAltitude = maxAltitude;
            Layers      = layers;
            NoFlyZones  = noFlyZones;
        }
    }
}
```

The `FakeNavLayer`, `NavPolygon`, and `OffMeshLink` types were already created in BATCH-04.
`NoFlyVolume` was defined in `FakeVolumetricPathProvider.cs` (BATCH-04). Use that type.
`BoundingBox3D` was also created in BATCH-04.

### 2.2 `NavTestMapLoader` — JSON deserialization

Create `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavTestMapLoader.cs`.

Use **Newtonsoft.Json** (already a project reference). Parse the JSON schema defined in DD-Fake-Nav §7.1 (reproduced below for reference).

**JSON schema:**
```json
{
  "name": "corridor",
  "description": "...",
  "min_altitude": 0,
  "max_altitude": 200,
  "layers": [
    {
      "layer": "Infantry",
      "polygons": [
        {
          "id": 0,
          "vertices": [[0,0,0],[30,0,0],[30,5,0],[0,5,0]],
          "surface_type": "Generic",
          "is_blocked": false
        }
      ],
      "adjacency": [[1], [0]],
      "off_mesh_links": []
    }
  ],
  "no_fly_zones": [
    { "bounds": {"min":[10,10,0], "max":[20,20,100]}, "debug_name": "primary_no_fly" }
  ]
}
```

The `layer` field maps to `NavLayerMask` enum values by name (case-insensitive).
The `surface_type` field maps to `SurfaceType` enum values by name. Use `"Generic"` (not `"Default"` — IDL keyword).
The `adjacency` field is an array of arrays: `adjacency[i]` lists polygon indices adjacent to polygon `i`.
The `vertices` field is an array of `[x, y, z]` float triples → `Vector3[]`.

**Static API:**
```csharp
public static class NavTestMapLoader
{
    /// <summary>Load a NavTestMap from a JSON string.</summary>
    public static NavTestMap FromJson(string json) { /* ... */ }

    /// <summary>Load a NavTestMap from a file path.</summary>
    public static NavTestMap FromFile(string filePath) { /* ... */ }
}
```

### 2.3 `NavTestMapBuilder` — fluent DSL

Create `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavTestMapBuilder.cs`.

The builder produces a `NavTestMap` via a fluent API. Minimal required shape:

```csharp
public sealed class NavTestMapBuilder
{
    public NavTestMapBuilder WithName(string name);
    public NavTestMapBuilder WithDescription(string description);
    public NavTestMapBuilder WithAltitudeRange(float min, float max);
    public NavTestMapBuilder Layer(NavLayerMask mask, Action<NavLayerBuilder> configure);
    public NavTestMapBuilder NoFlyZone(BoundingBox3D bounds, string debugName = "");
    public NavTestMap Build();
}

public sealed class NavLayerBuilder
{
    public NavLayerBuilder Polygon(int id, Vector3[] vertices, SurfaceType surfaceType = SurfaceType.Generic);
    public NavLayerBuilder Adjacent(int fromId, int toId);
    public NavLayerBuilder OffMeshLink(int fromId, int toId, Vector3 startPos, Vector3 endPos,
                                       TraversalKind kind, float cost = 1f);
}
```

**Constraints:**
- `Build()` may NOT throw for missing optional fields (Name defaults to "unnamed", Description to "").
- If altitudes are not set, default MinAltitude=0, MaxAltitude=200.
- Adjacency is bidirectional by default: calling `Adjacent(0, 1)` adds both `0→1` and `1→0`.

### 2.4 `NavTestMaps` static helper — 10 canonical fixtures

Create `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavTestMaps.cs`.

This class provides 10 static methods that each return a `NavTestMap` built via the DSL. The JSON canonical files (§2.5) are the serialized equivalents; the static methods are the in-code versions. Both must produce identical `NavTestMap` instances (same polygons, adjacency, links, no-fly zones).

**10 canonical maps** (from DD-Fake-Nav §7.3):

```
LoadCorridor()       — Single Infantry layer, single 30m straight polygon, no obstacles.
LoadLBend()          — Single Infantry layer, two polygons joined at right angle.
LoadTwoLayers()      — Infantry layer (narrow passage) + Vehicle layer (wider wrap).
LoadOffMeshJump()    — Infantry path with one JumpAcross off-mesh link between two platforms.
LoadReplan()         — Infantry path; one middle polygon set blocked (use IsBlocked=true in the map).
LoadCrowded()        — 10m x 10m open Infantry polygon, multiple agent spawn points as off-mesh links.
LoadStuck()          — Destination polygon disconnected from start polygon (no adjacency).
LoadFrustration()    — Three-agent dead-end pocket (3 polygons, 2 of which are dead ends).
LoadFlying()         — Two Infantry polygons plus one NoFlyVolume between them (tests IVolumetricPathProvider).
LoadNaval()          — Naval layer polygon over water with an adjacent land obstacle.
```

Details for each map:
- `LoadCorridor`: One polygon — vertices `(0,0,0),(30,0,0),(30,5,0),(0,5,0)`. Adjacency: none (single polygon). No links. No fly zones.
- `LoadLBend`: Two Infantry polygons `[0]=(0,0,0)-(10,0,0)-(10,10,0)-(0,10,0)` and `[1]=(10,0,0)-(20,0,0)-(20,10,0)-(10,0,0)`. Adjacent: `0↔1`. No links.
- `LoadTwoLayers`: Infantry layer has polygon `[0]=(0,0,0)-(30,0,0)-(30,5,0)-(0,5,0)` (narrow passage). Vehicle layer has polygon `[0]=(0,-5,0)-(30,-5,0)-(30,10,0)-(0,10,0)` (wide wrap). Each layer's polygon is standalone.
- `LoadOffMeshJump`: Infantry layer. Polygon `[0]=(0,0,0)-(10,0,0)-(10,10,0)-(0,10,0)`, polygon `[1]=(14,0,0)-(24,0,0)-(24,10,0)-(14,10,0)`. Adjacent: none (gap between them). OffMeshLink from 0→1 at `start=(10,5,0)`,`end=(14,5,0)`, kind=`JumpAcross`, cost=5f.
- `LoadReplan`: Infantry layer. Three polygons in a line: `[0]=(0,0,0)-(10,0,0)-(10,5,0)-(0,5,0)`, `[1]=(10,0,0)-(20,0,0)-(20,5,0)-(10,5,0)` (middle, starts blocked `IsBlocked=true`), `[2]=(20,0,0)-(30,0,0)-(30,5,0)-(20,5,0)`. Adjacent: `0↔1`, `1↔2`.
- `LoadCrowded`: Infantry layer. Single 10m×10m polygon `(0,0,0)-(10,0,0)-(10,10,0)-(0,10,0)`. No links.
- `LoadStuck`: Infantry layer. Two polygons with NO adjacency and NO off-mesh links: `[0]=(0,0,0)-(10,0,0)-(10,10,0)-(0,10,0)`, `[1]=(20,0,0)-(30,0,0)-(30,10,0)-(20,10,0)`. No adjacent. No links.
- `LoadFrustration`: Infantry layer. Three polygons: `[0]=(0,0,0)-(10,0,0)-(10,10,0)-(0,10,0)` (entrance), `[1]=(10,0,0)-(15,0,0)-(15,5,0)-(10,5,0)` (dead end), `[2]=(10,5,0)-(15,5,0)-(15,10,0)-(10,10,0)` (dead end). Adjacent: `0↔1`, `0↔2`. No links.
- `LoadFlying`: Infantry layer. Two polygons: `[0]=(0,0,0)-(10,0,0)-(10,5,0)-(0,5,0)`, `[1]=(20,0,0)-(30,0,0)-(30,5,0)-(20,5,0)`. No adjacency. NoFlyZone: bounds `min=(10,0,0),max=(20,5,100)`, name=`"obstacle"`. MinAltitude=0, MaxAltitude=200.
- `LoadNaval`: Naval layer (use `NavLayerMask` value that represents naval — if such a value exists). One 30×5 water polygon. One adjacent land obstacle polygon to the north, connected but with `SurfaceType.Generic`. If `NavLayerMask` has no Naval enum value, use a separate layer named `"Naval"` in JSON (map to a bit-flagged value in the implementation).

### 2.5 JSON canonical fixtures

Create the directory `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/data/navmaps/`.

Create 10 JSON files (one per canonical map). The JSON must be parseable by `NavTestMapLoader.FromJson`. File names:
- `corridor.json`
- `l_bend.json`
- `two_layers.json`
- `off_mesh_jump.json`
- `replan.json`
- `crowded.json`
- `stuck.json`
- `frustration.json`
- `flying.json`
- `naval.json`

Each JSON file's content must be equivalent to (produce the same `NavTestMap` as) the corresponding `NavTestMaps.LoadX()` DSL method. Use `"Generic"` for `surface_type` (not `"Default"`).

**Important:** Mark the JSON files as **EmbeddedResource** in the `.csproj` file so they are accessible via `Assembly.GetManifestResourceStream`. Then in tests, load them via `NavTestMapLoader.FromJson(ReadEmbeddedResource("corridor.json"))`. Alternatively, load from disk relative path using `AppDomain.CurrentDomain.BaseDirectory`. Choose one approach and use it consistently for all 10 files.

### 2.6 `NavigationFakesModule`

Create `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavigationFakesModule.cs`.

`IEcsModule` is defined in `Fdp.ModuleHost.Abstractions`. Its required members are:
- `string Name { get; }`
- `ExecutionPolicy Policy { get; }`
- `void RegisterSystems(ISystemRegistry registry)` (default empty)
- `void Tick(ISimulationView view, float deltaTime)`
- `IReadOnlyList<Type>? WatchComponents => null;` (default)
- `IReadOnlyList<Type>? WatchEvents => null;` (default)
- `IEnumerable<Type>? GetRequiredComponents() => null;` (default)

```csharp
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Wires up the four navigation fake backends for test scenarios and early development.
    /// Use instead of NavigationRealBackendsModule when a real DtCrowd/DDS integration is
    /// not required.
    ///
    /// After construction, call <see cref="RegisterProviders"/> to install the fake providers
    /// into an <see cref="EntityRepository"/> so nav systems can read them via ECS singletons.
    /// </summary>
    public sealed class NavigationFakesModule : IEcsModule, IDisposable
    {
        // ── IEcsModule ───────────────────────────────────────────────────────────

        public string          Name   => "NavigationFakesModule";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        /// <summary>
        /// Phase 3+ will register CrowdAgentUpdateSystem, OffMeshLinkDetectionSystem, etc. here.
        /// Currently empty because the nav systems are added in BATCH-06+.
        /// </summary>
        public void RegisterSystems(ISystemRegistry registry) { }

        /// <summary>
        /// No per-frame logic is needed in the module itself; systems do the work.
        /// </summary>
        public void Tick(ISimulationView view, float deltaTime) { }

        // ── Provider properties ──────────────────────────────────────────────────

        /// <summary>In-memory navmesh provider built from <see cref="Map"/>.</summary>
        public FakeNavmeshProvider          Navmesh    { get; }

        /// <summary>O(N^2) deterministic crowd provider.</summary>
        public FakeDtCrowdProvider          Crowd      { get; }

        /// <summary>Volumetric pathfinder that respects <see cref="NoFlyVolume"/> zones in <see cref="Map"/>.</summary>
        public FakeVolumetricPathProvider   Volumetric { get; }

        /// <summary>
        /// Shared path registry used in all-in-one mode.
        /// Both Brain and Muscle sides read/write the same store.
        /// </summary>
        public SharedPathRegistry           PathRegistry { get; }

        /// <summary>The map this module was constructed from.</summary>
        public NavTestMap Map { get; }

        // ── Construction ─────────────────────────────────────────────────────────

        public NavigationFakesModule(NavTestMap map)
        {
            Map          = map;
            Navmesh      = new FakeNavmeshProvider(map);
            Crowd        = new FakeDtCrowdProvider();
            Volumetric   = new FakeVolumetricPathProvider(map);
            PathRegistry = new SharedPathRegistry();
        }

        // ── Setup helper ─────────────────────────────────────────────────────────

        /// <summary>
        /// Registers <see cref="Navmesh"/> as an ECS managed singleton so nav systems can
        /// access it via <c>repo.GetSingletonManaged&lt;INavmeshProvider&gt;()</c>.
        /// Call this once after constructing the module, before running ticks.
        ///
        /// <c>IDtCrowdProvider</c>, <c>IVolumetricPathProvider</c>, and <c>IPathRegistry</c>
        /// will be registered as ECS singletons in BATCH-06 when the nav systems that read
        /// them are added. For now, tests access them directly via <see cref="Crowd"/>,
        /// <see cref="Volumetric"/>, and <see cref="PathRegistry"/> properties.
        /// </summary>
        public void RegisterProviders(EntityRepository repo)
        {
            repo.SetSingletonManaged<INavmeshProvider>(Navmesh);
        }

        // ── IDisposable ──────────────────────────────────────────────────────────

        public void Dispose()
        {
            // Clears all registered crowd agents (per Navigation Design §16).
            Crowd.Dispose();
        }
    }
}
```

**Notes:**
- `FakeNavmeshProvider` must accept a `NavTestMap` argument. See §2.7 for the constructor compatibility check.
- `FakeVolumetricPathProvider` must accept a `NavTestMap` argument (no-fly zones).
- `FakeDtCrowdProvider.Dispose()` calls `UnregisterAgent` for all registered agents and clears the internal dictionary.
- The `IWindowRegistrar` implementation for the ImGui diagnostic window is **deferred to a later batch**.

### 2.7 Constructor compatibility — `FakeNavmeshProvider`, `FakeVolumetricPathProvider`, `NoFlyVolume`

`NoFlyVolume` does not yet exist. Create it as a simple struct alongside `NavTestMap.cs` (same file is fine, or separate `NoFlyVolume.cs`):

```csharp
namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>A named axis-aligned no-fly bounding box.</summary>
    public struct NoFlyVolume
    {
        public BoundingBox3D Bounds;
        /// <summary>Optional label for diagnostic windows and test failure messages.</summary>
        public string DebugName;
    }
}
```

`FakeNavmeshProvider` was implemented in BATCH-04 with a `params FakeNavLayer[]` constructor. Add a second constructor overload that accepts a `NavTestMap`:

```csharp
/// <summary>Initialise from a <see cref="NavTestMap"/> — convenience ctor for <see cref="NavigationFakesModule"/>.</summary>
public FakeNavmeshProvider(NavTestMap map) : this(map.Layers) { }
```

`FakeVolumetricPathProvider` was implemented in BATCH-04 with `(float minAltitude=0, float maxAltitude=5000)`. Add a constructor overload that accepts a `NavTestMap` and populates no-fly zones:

```csharp
/// <summary>Initialise from a <see cref="NavTestMap"/> — convenience ctor for <see cref="NavigationFakesModule"/>.</summary>
public FakeVolumetricPathProvider(NavTestMap map) : this(map.MinAltitude, map.MaxAltitude)
{
    foreach (var zone in map.NoFlyZones)
        AddNoFlyZone(zone.Bounds);
}
```

`AddNoFlyZone` is already the `IFakeVolumetricPathProviderTestApi` implementation method. Since the constructor is on the concrete class, it can call it directly (no cast needed).

---

## Task 3: Tests

### 3.1 `NavTestMapLoaderTests.cs`

Create `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavTestMapLoaderTests.cs`.

One test per canonical fixture, verifying round-trip equivalence between JSON and DSL:

| Test | What to assert |
|---|---|
| `LoadCorridor_JsonEquivalentToDsl` | Layer count, polygon count, adjacency |
| `LoadLBend_JsonEquivalentToDsl` | Two polygons; correct adjacency |
| `LoadTwoLayers_JsonEquivalentToDsl` | Two separate layers |
| `LoadOffMeshJump_JsonEquivalentToDsl` | Off-mesh link with correct kind and cost |
| `LoadReplan_JsonEquivalentToDsl` | Middle polygon has IsBlocked=true |
| `LoadCrowded_JsonEquivalentToDsl` | Single polygon |
| `LoadStuck_JsonEquivalentToDsl` | No adjacency between the two polygons |
| `LoadFrustration_JsonEquivalentToDsl` | Three polygons; dead-end structure |
| `LoadFlying_JsonEquivalentToDsl` | No-fly zone bounds match |
| `LoadNaval_JsonEquivalentToDsl` | Naval layer present |

Equivalence: compare name, layer count, polygon counts, off-mesh link counts, no-fly zone bounds (approx float comparison within 0.001 tolerance).

### 3.2 `NavigationFakesModuleTests.cs`

Create `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationFakesModuleTests.cs`.

| Test | What to assert |
|---|---|
| `Module_Constructor_AllProvidersNotNull` | `Navmesh`, `Crowd`, `Volumetric`, `PathRegistry` are all non-null after construction |
| `Module_Dispose_ClearsAgentState` | Register an agent in `Crowd`, call `module.Dispose()`, then `Crowd.TryGetAgentSnapshot` returns false |
| `Module_SharedRegistry_BrainAndMuscleShareSameStore` | Store a waypoint via `PathRegistry.RegisterOrReplace(...)` (MusclePathRegistry cast); read it back via `IPathRegistry.TryGetWaypoints` — same data, no replication delay |
| `RegisterProviders_SetsNavmeshSingleton` | After `module.RegisterProviders(repo)`, `repo.GetSingletonManaged<INavmeshProvider>()` returns the same instance as `module.Navmesh` |

---

## Checklist

- [ ] `NavFakeIds.cs` updated to 262-279 block (IDs 262-268)
- [ ] `IDtCrowdProvider.cs` created with `CrowdAgentParams` and `CrowdAgentSnapshot`
- [ ] `IFakeDtCrowdProviderTestApi.cs` created (or added to same file as IDtCrowdProvider)
- [ ] `FakeCrowdComponents.cs` created (`FakeCrowdAgentState` with `[ComponentId(264)]`, `FakeCrowdGlobalState` with `[ComponentId(263)]`)
- [ ] `FakeDtCrowdProvider.cs` created — implements `IDtCrowdProvider` + `IFakeDtCrowdProviderTestApi`; O(N^2) tick algorithm; entity-id ordering; NaN-safe normalize
- [ ] `FakeDtCrowdProvider.Dispose()` clears all agent state
- [ ] 10 `FakeDtCrowdProviderTests` pass
- [ ] `NoFlyVolume.cs` (or struct in NavTestMap.cs) created
- [ ] `NavTestMap.cs` created
- [ ] `FakeNavmeshProvider(NavTestMap)` overload added
- [ ] `FakeVolumetricPathProvider(NavTestMap)` overload added (populates no-fly zones)
- [ ] `NavTestMapLoader.cs` created (Newtonsoft.Json, `FromJson` + `FromFile`)
- [ ] `NavTestMapBuilder.cs` created (fluent DSL per §2.3)
- [ ] `NavTestMaps.cs` created (10 static methods per §2.4)
- [ ] `NavigationFakesModule.cs` created (IEcsModule + IDisposable, exposes providers as properties, `RegisterProviders(EntityRepository)` helper)
- [ ] `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/data/navmaps/` directory created
- [ ] 10 JSON canonical fixture files created (corridor.json ... naval.json)
- [ ] 10 `NavTestMapLoaderTests` pass (JSON round-trip equivalence)
- [ ] 4 `NavigationFakesModuleTests` pass
- [ ] `dotnet build` on `Fdp.Toolkits` + `Fdp.Toolkits.Tests`: **0 errors**
- [ ] `dotnet test --filter "Navigation"`: all 125 pre-existing tests still pass + new tests pass
- [ ] No NaN in crowd velocity output (safe normalize: if `|v| < 1e-6f`, return `Vector3.Zero`)
