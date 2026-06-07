# BATCH-04: Phase 2 (Part A) — FakeNavmeshProvider, FakeVolumetricPathProvider, PathRegistry

**Batch Number:** BATCH-04
**Tasks:** Debt-02 (fix), NAV-P2-T1, NAV-P2-T3, NAV-P2-T4
**Phase:** Phase 2 (Part A) — Fake backends foundations
**Estimated Effort:** 16-20 hours
**Priority:** HIGH — fake providers unlock integration tests and Phase 3+
**Dependencies:** BATCH-01, BATCH-02, BATCH-03 (Phases 0-1 complete)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements the first three of the five Phase 2 tasks: the fake navmesh provider
(T1), the fake volumetric path provider (T3), and the path registry implementations (T4).
The fake crowd provider (T2) and the test-map + module (T5) come in BATCH-05.

Also fix tech debt item #2: extend `NavigationTestWorldFactory` to register
`NavigationCorridorMuscle` by default.

Work in this order: Debt-02 fix → T4 (IPathRegistry/registries) → T1 (FakeNavmeshProvider)
→ T3 (FakeVolumetricPathProvider). T4 is foundational to T1 and T3.

After each task: **build full solution (0 errors), run nav tests (all pass), then proceed.**
Do NOT stop to ask questions unless there is a breaking design conflict.

### Required Reading (in order)

1. **Previous review:** `.dev/navig-2/reviews/BATCH-03-REVIEW.md`
2. **Workflow guide:** `.dev/.guides/DEV-GUIDE.md`
3. **Code standards:** `.dev/.guides/CODE-STANDARDS.md`
4. **Task definitions:** `.dev/navig-2/TASK-DETAILS.md` — sections NAV-P2-T1, NAV-P2-T3, NAV-P2-T4
5. **DD-Fake-Nav §1-3 (FakeNavmeshProvider):** `.dev/navig-2/DD-Fake-Nav.md`
6. **DD-Fake-Nav §5 (FakeVolumetricPathProvider):** same file
7. **DD-Fake-Nav §6 (IPathRegistry fakes):** same file
8. **DD-Fake-Nav §11 (Determinism + hard-assert discipline):** same file
9. **DD-Fake-Nav §12 (ComponentId allocation — fake block 250-279):** same file
10. **Navigation Design §6.2 (IPathRegistry interface + PathSummary):** `.dev/navig-2/Navigation_Design_v2_0.md`
11. **Navigation Design §8 (INavmeshProvider interface):** same file
12. **Navigation Design §9 (IVolumetricPathProvider interface):** same file

### Source Code Locations

- **Existing `INavmeshProvider`:** `FDP/Toolkits/Fdp.Toolkits/Navigation/INavmeshProvider.cs`
- **Existing `IVolumetricPathProvider`:** `FDP/Toolkits/Fdp.Toolkits/Navigation/IVolumetricPathProvider.cs`
- **Navigation constants + enums:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs`
- **NavigationHandleAllocator:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationHandleAllocator.cs`
- **NavWaypoint:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavWaypoint.cs`
- **Test world factory:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactory.cs`
- **New fakes folder:** create `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/` if it does not exist
- **Test project:** `FDP/Toolkits/Fdp.Toolkits.Tests/`

**All fake nav code stays in `Fdp.Toolkit.Navigation` namespace (no new assemblies).**
Use a sub-namespace `Fdp.Toolkit.Navigation.Fake` for the fake implementations.

### Build & Test Commands

```powershell
# Build full solution
dotnet build IOS-IG-SimHost.sln

# Run navigation tests only
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --filter "Navigation" -v quiet

# Run all tests (final gate)
dotnet test IOS-IG-SimHost.sln -v quiet
```

### Report Submission

**When done, submit:** `.dev/navig-2/reports/BATCH-04-REPORT.md`

**If you have questions:** `.dev/navig-2/questions/BATCH-04-QUESTIONS.md`

---

## Context

Phase 1 wired the path-query pipeline. Phase 2 populates it with fake provider implementations
so the pipeline becomes end-to-end exercisable without real DotRecast/dtCrowd. This batch
delivers the navmesh fake (T1), volumetric fake (T3), and the path-registry storage layer (T4).
The crowd fake (T2) and test-map fixtures/module (T5) follow in BATCH-05.

At the end of this batch:
- `PathfindingSolverSystem` with `INavmeshProvider = FakeNavmeshProvider` will plan polygon-
  A* paths deterministically.
- `PathfindingSolverSystem` with `IVolumetricPathProvider = FakeVolumetricPathProvider` will
  plan no-fly-zone-avoiding 3D paths.
- `MusclePathRegistry`, `BrainPathRegistry`, and `SharedPathRegistry` are available as
  storage backed by dictionary/LRU structures.

---

## Tasks

### Debt-02 Fix — Extend `NavigationTestWorldFactory`
*Source:* BATCH-03-REVIEW Debt item #2.

`NavigationTestWorldFactory.Create()` must register `NavigationCorridorMuscle` (and any other
nav components added in Phase 1 that are commonly needed in tests). This prevents every new
test class from having to manually call `RegisterComponent<NavigationCorridorMuscle>()`.

Check the factory method and add all missing nav-component registrations:
`NavigationCorridorMuscle`, `NavigationCorridorPreview`, `NavigationPathDetailsBuffer`,
`CrowdAgent`, `NavAgentProfile`. If the factory is not yet extended, add these calls.

Write a test `NavigationTestWorldFactory_RegistersAllNavComponents` in
`NavigationTestWorldFactory` or an adjacent test helper test class that verifies:
- No exception thrown when accessing all registered components on a freshly created entity.
- The components that existed before Phase 1 still register correctly.

---

### T4 — `IPathRegistry` + `MusclePathRegistry` / `BrainPathRegistry` / `SharedPathRegistry`
*Full spec:* `.dev/navig-2/TASK-DETAILS.md#nav-p2-t4`
*Design:* DD-Fake-Nav §6; Navigation Design §6.2.

**Step A — Define the public interfaces in `Fdp.Toolkit.Navigation`:**
```
IPathRegistry (Navigation Design §6.2):
  bool IsCached(int routeHandle)
  bool TryGetSummary(int routeHandle, out PathSummary summary)
  bool TryGetWaypoints(int routeHandle, Span<NavWaypoint> dest, out int count)
  bool TryGetWaypointsSlice(int routeHandle, int startSegment, int maxCount,
                             Span<NavWaypoint> dest, out int actualCount)

PathSummary (value struct):
  int RouteHandle
  float TotalDistanceMeters
  uint NavmeshVersionAtPlan
  byte PrimaryBackend
  byte Flags       // bit 0: HasOffMeshLinks
  byte ReplanCount

IFakeMusclePathRegistryTestApi:
  void RegisterOrReplace(int routeHandle, NavWaypoint[] waypoints,
                         float totalDist, uint navmeshVersion,
                         byte primaryBackend, byte flags)
  bool Free(int routeHandle)
  FakePathRegistryStats GetStats()

IFakeBrainPathRegistryTestApi:
  bool TryIngestResponse(Entity entity, int routeHandle, NavWaypoint[] waypoints,
                         byte replanCount, float totalDist, uint navmeshVersion,
                         byte primaryBackend)
  FakePathRegistryStats GetStats()

FakePathRegistryStats (struct):
  int TotalEntries
  int HitCount
  int MissCount
```

**Step B — Implement `MusclePathRegistry`:**
- `Dictionary<int, FakePathPoolEntry>` backing store.
- `FakePathPoolEntry` per DD-Fake-Nav §6.1.
- `RegisterOrReplace` inserts or overwrites; `Free` removes.
- All `IPathRegistry` query methods implemented over the dictionary.
- Thread-safe read (the Muscle's query path may be called from background threads in Phase 3).
  Use a `ReaderWriterLockSlim` for the dictionary (write on `RegisterOrReplace`/`Free`; read on queries).

**Step C — Implement `BrainPathRegistry`:**
- Per-entity LRU cache; max 32 entries (configurable via constructor). Dictionary keyed by `(Entity, int routeHandle)`.
- **Strict `ReplanCount` cache-miss policy:** if `entry.LastObservedReplanCount != currentStatus.ReplanCount`,
  `IsCached` returns false and `TryGetWaypoints` returns false (cache stale).
  Pass `replanCount` into `TryGetWaypoints` for this check.
- LRU eviction: when cap exceeded, remove the entry with the smallest `LastUsedTick`.
- `IngestResponse` writes a new or replaces an existing entry.
- For now, `TryGetSummary`/`TryGetWaypointsSlice` not required to apply the replan check
  (mark with TODO comment for Phase 4 completion).

**Step D — Implement `SharedPathRegistry`:**
- In all-in-one mode, single instance satisfying both Muscle and Brain roles.
- Extends `MusclePathRegistry` and adds the Brain-side LRU cache atop the same dictionary.
- Constructor accepts an optional `int maxBrainCacheEntries = 32`.

**Component IDs for fake-only components (DD-Fake-Nav §12 block 250-279):**
- Add `NavFakeComponentIds.cs` (or `NavigationFakeComponentIds.cs`) in the Fake/ folder.
- `FakePathPoolEntry` is not an ECS component — it is a plain class stored in the dictionary.
- If you add any ECS singleton component for registry stats, use IDs in the 250-279 range.

**Tests** (will be the foundation for NAV-P8-T4, T5, T6):
- `MusclePathRegistry_RegisterAndQuery_ReturnsEntry`
- `MusclePathRegistry_Free_RemovesEntry`
- `BrainPathRegistry_StaleReplanCount_CacheMiss`
- `BrainPathRegistry_FreshReplanCount_CacheHit`
- `BrainPathRegistry_LruEviction_DropsOldestEntry`
- `SharedPathRegistry_QueryFromBothRoles_ReturnsConsistentData`

---

### T1 — `FakeNavmeshProvider` + polygon A* + test API
*Full spec:* `.dev/navig-2/TASK-DETAILS.md#nav-p2-t1`
*Design:* DD-Fake-Nav §3; Navigation Design §8.

Implement in `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/`:

**`FakeNavLayer`, `NavPolygon`, `OffMeshLink` data structures** (per DD-Fake-Nav §3.2):
- `NavPolygon` has `int Id`, `Vector3[] Vertices` (polygon on Z plane), `SurfaceType`,
  `bool IsBlocked`.
- `OffMeshLink` has `int FromPolygonId`, `int ToPolygonId`, `Vector3 StartPos`, `Vector3 EndPos`,
  `TraversalKind Kind`, `float Cost`.
- `FakeNavLayer` has `NavLayerMask Layer`, `NavPolygon[] Polygons`, adjacency list, links,
  `uint Version`.

**`FakeNavmeshState` singleton ECS component** (ComponentId in 250-279 block):
- Holds an array of `FakeNavLayer[]` layers.

**`FakeNavmeshProvider` class** implementing `INavmeshProvider` + `IFakeNavmeshProviderTestApi`:

Query algorithms (DD-Fake-Nav §3.3) — keep them straightforward:
- `IsWalkable(point, mask)` — for each layer matching mask: linear polygon scan,
  point-in-polygon on (X, Z) plane. True if any non-blocked polygon contains the point.
- `ProjectToNavmesh(point, maxDist, mask)` — nearest polygon point within `maxDist`; Z = polygon elevation.
- `PathExists(a, b, mask, maxCost)` — A* over polygon adjacency + off-mesh links; return `cost <= maxCost`.
- `PathCost(a, b, mask)` — A* returning cost (or `float.PositiveInfinity`).
- `SampleNavmeshPoints(bounds, density, mask, sink)` — grid sample within bounds; push walkable+projected points to sink.
- `QueryVersion(bounds, mask)` — max version of layers overlapping bounds and matching mask.
- `PlanPath(a, b, mask, output)` — A* returning `NavWaypoint[]` into the output span;
  include off-mesh link waypoints with their `TraversalKind`.

`IFakeNavmeshProviderTestApi`:
- `BlockPolygon(id, layer)` — sets `IsBlocked = true`; bumps version.
- `UnblockPolygon(id, layer)` — sets `IsBlocked = false`; bumps version.
- `BumpVersion(bounds, layer)` — increments `Version` on all matching layers.
- `GetLoadedMap()` — returns the source `NavTestMap` (or a reconstructed summary). OK to
  return null here since `NavTestMap` is not defined until BATCH-05; just stub the return.

**A* implementation notes:**
- For Phase 2, it is acceptable to implement A* as a simple BFS/Dijkstra (no heuristic).
- Use Manhattan or Euclidean distance as the heuristic (optional optimization).
- Off-mesh links are directed edges with their `Cost` added to the path cost.
- If `a` or `b` is not contained in any polygon, project to the nearest polygon center first.

**Tests** (foundation for NAV-P8-T1):
- `FakeNavmeshProvider_IsWalkable_InsidePolygon_ReturnsTrue`
- `FakeNavmeshProvider_IsWalkable_OutsideAllPolygons_ReturnsFalse`
- `FakeNavmeshProvider_IsWalkable_BlockedPolygon_ReturnsFalse`
- `FakeNavmeshProvider_PathExists_ConnectedPolygons_ReturnsTrue`
- `FakeNavmeshProvider_PathExists_DisconnectedPolygons_ReturnsFalse`
- `FakeNavmeshProvider_PlanPath_IncludesOffMeshLinkWaypoints`
- `FakeNavmeshProvider_BlockPolygon_BumpsVersion`
- `FakeNavmeshProvider_QueryVersion_ReturnsMaxLayerVersion`

---

### T3 — `FakeVolumetricPathProvider`
*Full spec:* `.dev/navig-2/TASK-DETAILS.md#nav-p2-t3`
*Design:* DD-Fake-Nav §5; Navigation Design §9.

Note: `IVolumetricPathProvider` was already defined in BATCH-03 (`IVolumetricPathProvider.cs`).
Verify it already has the full interface signature from DD-Fake-Nav §5.1 (`IsFlyable`, `PathExists`,
`Plan`, `QueryVersion`). If any method is missing, add it now.

Implement in `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/`:

**`FakeVolumetricState` singleton** (ComponentId in 250-279 block):
```csharp
class FakeVolumetricState {
    public NoFlyVolume[] NoFlyZones;
    public float MinAltitude;
    public float MaxAltitude;
    public uint Version;
}

struct NoFlyVolume {
    public BoundingBox3D Bounds;
    public string DebugName;
}
```

**`FakeVolumetricPathProvider`** implementing `IVolumetricPathProvider` + `IFakeVolumetricPathProviderTestApi`:

Algorithm (DD-Fake-Nav §5.3):
- `IsFlyable(point)` — true if point is within [MinAltitude, MaxAltitude] and not inside any no-fly zone.
- `PathExists(a, b, profile, maxCost)` — check if straight-line a→b is clear of no-fly zones
  (all points along the line are flyable). If clear, cost = distance; return `cost <= maxCost`.
  Otherwise, attempt coarse-grid A* (5m cells) and return true if a path is found within budget.
- `Plan(a, b, profile, output)` — straight line if clear; else coarse 3D grid A*.
  Waypoints use `TraversalKind.Walk` (or `Fly` if that enum value exists; otherwise `Walk`).
- `QueryVersion(bounds)` — returns `Version` if any no-fly zone overlaps bounds, else 0.

`BoundingBox3D` — define if not already present:
```csharp
public struct BoundingBox3D {
    public Vector3 Min;
    public Vector3 Max;
    public bool Contains(Vector3 p) => ...
    public bool IntersectsLine(Vector3 a, Vector3 b) => ...
}
```

`IFakeVolumetricPathProviderTestApi`:
- `AddNoFlyZone(bounds, debugName)` — adds zone; bumps version.
- `RemoveNoFlyZone(debugName)` — removes zone; bumps version.
- `NoFlyVolume[] GetNoFlyZones()`.

**Tests** (foundation for NAV-P8-T3):
- `FakeVolumetricPathProvider_IsFlyable_ClearPoint_ReturnsTrue`
- `FakeVolumetricPathProvider_IsFlyable_InNoFlyZone_ReturnsFalse`
- `FakeVolumetricPathProvider_IsFlyable_BelowMinAltitude_ReturnsFalse`
- `FakeVolumetricPathProvider_Plan_ClearPath_ReturnsSingleWaypointAtDestination`
- `FakeVolumetricPathProvider_Plan_BlockedStraightLine_FindsDetourAroundNoFlyZone`
- `FakeVolumetricPathProvider_AddNoFlyZone_BumpsVersion`

---

## Mandatory Workflow: Test-Driven Task Progression

For each task in this batch, follow this exact sequence:

1. **Write failing tests first** (at minimum the listed tests above).
2. **Implement** the feature.
3. **Run tests** — all must pass before moving to the next task.
4. **Build full solution** — 0 errors.
5. **Only then proceed** to the next task.

Do not batch all implementation first and test at the end.

---

## Developer Insights Required in Report

Your report MUST answer:

1. **What issues were encountered?** (API mismatches, missing types, algorithm challenges, etc.)
2. **What weak points were spotted in the codebase?**
3. **What design decisions were made beyond the spec?**
4. **Test coverage gaps** — scenarios not testable yet (e.g., anything waiting on NavTestMap from T5).
5. **A* implementation choice** — which algorithm did you implement (BFS vs Dijkstra vs A*)?
   Justify the choice.

---

## Success Criteria

- [ ] `NavigationTestWorldFactory` registers `NavigationCorridorMuscle`, `NavigationCorridorPreview`,
  `NavigationPathDetailsBuffer`, `CrowdAgent`, `NavAgentProfile`.
- [ ] `IPathRegistry`, `PathSummary` interfaces defined in `Fdp.Toolkit.Navigation`.
- [ ] `MusclePathRegistry`, `BrainPathRegistry`, `SharedPathRegistry` implemented with the above APIs.
- [ ] `FakeNavmeshProvider` implements `INavmeshProvider` + all 7 query methods + `IFakeNavmeshProviderTestApi`.
- [ ] `FakeVolumetricPathProvider` implements `IVolumetricPathProvider` + `IFakeVolumetricPathProviderTestApi`.
- [ ] Fake ComponentIds in the 250-279 block (no collision with existing blocks).
- [ ] All listed tests pass.
- [ ] All pre-existing tests remain green.
- [ ] `dotnet build IOS-IG-SimHost.sln` → 0 errors.
