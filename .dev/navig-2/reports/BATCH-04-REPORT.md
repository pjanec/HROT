# BATCH-04 Report

## 1. Issues Encountered

### 1.1 Tuple member names in SortedSet comparer (FakeNavmeshProvider)

`Comparer<(float, int)>.Create(...)` does not propagate tuple member names inside
the lambda; the compiler resolved the parameter type as anonymous `(float, int)` so
`.cost` and `.id` were not found. Fix: change the generic parameter from
`Comparer<(float, int)>` to `Comparer<(float cost, int id)>` so the names are
part of the closed type.

### 1.2 CA2014 stackalloc inside loop (FakeVolumetricPathProvider)

The 4-directional `stackalloc (int, int)[]` was inside the inner A* loop body,
triggering `CA2014` (treated as error by `Directory.Build.props`). Fix: lifted the
directions array to a `private static readonly (int, int)[]` field.

### 1.3 Pre-existing test failures (not introduced by this batch)

Running `--filter Navigation` revealed 24 pre-existing failures in
`FollowRouteExecutorTests`, `FollowRoadGraphExecutorTests`,
`FleeExecutorTests`, `MoveToExecutorTests`, and `NavigationIntentBridgePipelineTests`.
These exist before BATCH-04 and were unaffected by any changes in this batch
(verified: 87 passed before, 87 still pass alongside 29 new passes added by this batch).

---

## 2. Weak Points Spotted

### 2.1 BrainPathRegistry entity-agnostic IPathRegistry methods are incomplete

The three `IPathRegistry` methods on `BrainPathRegistry` that do NOT receive an
`Entity` argument (`IsCached(int)`, `TryGetSummary`, `TryGetWaypointsSlice`) perform
a linear scan over all cache entries and **do not apply the ReplanCount policy**.
This is noted with `// TODO (Phase 4)` comments. The entity-scoped overloads
(`IsCached(Entity, int, byte)`, `TryGetWaypoints(Entity, int, byte, ...)`) are the
correct API for tests that need the strict staleness check.

### 2.2 SharedPathRegistry exposes Muscle directly

`SharedPathRegistry.Muscle` is a public property returning the inner
`MusclePathRegistry`. This gives consumers write access (via `IFakeMusclePathRegistryTestApi`)
without going through `SharedPathRegistry`. Intentional for test convenience, but
could be narrowed to `IPathRegistry` if production usage is added.

### 2.3 FakeVolumetricPathProvider grid A* uses (X, Z) plane only

The grid A* ignores Y variation beyond clamping waypoints to `midY`. For paths where
altitude must change significantly (e.g., climbing over terrain) the detour quality
is poor. Sufficient for unit tests but documented as a known limitation.

### 2.4 FakeNavmeshProvider PlanPath omits intermediate centroids for the final hop

When the polygon path is `[fromPoly, toPoly]` (two hops), the code skips the
intermediate centroid insertion because `i < polyPath.Count - 2` is false for the
last pair. This means for a two-polygon path only start + end waypoints are emitted.
This is correct for the common case but may suppress useful debug information for
longer paths.

---

## 3. Design Decisions Beyond Specification

### 3.1 BrainPathRegistry provides entity-scoped overloads in addition to IPathRegistry

The spec asked for `IsCached(int routeHandle, byte expectedReplanCount)` as a
public test-only overload. The implementation instead provides
`IsCached(Entity, int, byte)` and `TryGetWaypoints(Entity, int, byte, Span, out int)`,
which are more precise because the Brain cache is keyed by (Entity, handle).
The interface-required `IsCached(int)` is still present but performs a linear scan
without replan checking.

### 3.2 SharedPathRegistry wraps MusclePathRegistry rather than duplicating it

The spec described `SharedPathRegistry` as a "forwarding wrapper". This was
implemented as a thin delegating class with a single `MusclePathRegistry` field.
Exposing `Muscle` as a public property was added to allow tests to call
`IFakeMusclePathRegistryTestApi` methods without an explicit cast.

### 3.3 FakeVolumetricPathProvider grid step is 5 m (configurable as a constant)

The spec said "5-metre grid A*". This is implemented as `private const float GridStep = 5f`.
For tests that need a coarser or finer grid the constant would need to become a
constructor parameter. Not done to avoid over-engineering.

### 3.4 FakeNavmeshProvider.BlockPolygon uses mutation of NavPolygon.IsBlocked

`NavPolygon` is a `sealed class`, not a struct, so `BlockPolygon` mutates the existing
instance in place. This is simpler and avoids rebuilding the layer array, but means
callers holding a reference to a `NavPolygon` before the block call will observe the
change (reference semantics). Acceptable for a fake; documented in the class summary.

---

## 4. Test Coverage Gaps

| Area | What is not covered |
|------|---------------------|
| `MusclePathRegistry` | Concurrent read/write race (no parallel stress test) |
| `BrainPathRegistry` | LRU eviction when the oldest entry belongs to a different entity than the inserting entity |
| `BrainPathRegistry` | `TryGetSummary` and `TryGetWaypointsSlice` with stale ReplanCount (TODO Phase 4) |
| `FakeNavmeshProvider` | `ProjectToNavmesh` result accuracy |
| `FakeNavmeshProvider` | `SampleNavmeshPoints` radius boundary |
| `FakeNavmeshProvider` | `PathCost` vs manually summed Dijkstra distance |
| `FakeNavmeshProvider` | LayerMask filtering (multi-layer, mask excludes a layer) |
| `FakeVolumetricPathProvider` | `PathExists(FlyProfile)` with `maxCost` cutoff |
| `FakeVolumetricPathProvider` | `QueryVersion(BoundingBox3D)` non-overlapping case returns same version |
| `FakeVolumetricPathProvider` | `ClearNoFlyZones` after paths were cached |
| `IVolumetricPathProvider` DIM | `StubVolumetricProvider` throws `NotSupportedException` for new methods |

---

## 5. A* Implementation Choice Justification

### FakeNavmeshProvider (polygon graph)

**Choice**: Dijkstra over explicit polygon adjacency lists.

Justification: the polygon count in tests is always small (< 50). A* with a
Euclidean heuristic would provide no measurable benefit. Dijkstra is simpler to
verify by inspection, has no heuristic admissibility concerns, and produces correct
shortest paths. `SortedSet<(float cost, int id)>` was chosen over `PriorityQueue`
because `PriorityQueue<T,P>` does not support `Min`-peek without dequeue; the named
tuple comparer keeps the implementation self-contained.

### FakeVolumetricPathProvider (grid A*)

**Choice**: A* on a regular 5-metre 4-connected grid in the (X, Z) plane.

Justification:
- Straight-line planning covers the common test case (no no-fly zones) in O(1).
- The grid A* is reserved for the detour test only, so performance is not critical.
- A 4-connected grid is simpler to reason about than Theta* or a visibility graph.
- The grid uses a Manhattan-distance heuristic, which is admissible for axis-aligned
  movement at uniform cost, guaranteeing optimal paths on the grid.
- `MaxGridSteps = 200` bounds worst-case memory to ~40k dictionary entries, well
  within test process limits.

---

## 6. Files Created / Modified

| File | Action |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactory.cs` | Modified (Debt-02) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactoryTests.cs` | Created (Debt-02) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/IPathRegistry.cs` | Created (T4) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavFakeIds.cs` | Created (T4 support) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/MusclePathRegistry.cs` | Created (T4) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/BrainPathRegistry.cs` | Created (T4) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/SharedPathRegistry.cs` | Created (T4) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/PathRegistryTests.cs` | Created (T4 tests) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavPolygon.cs` | Created (T1) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/OffMeshLink.cs` | Created (T1) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavLayer.cs` | Created (T1) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs` | Created (T1) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/FakeNavmeshProviderTests.cs` | Created (T1 tests) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/IVolumetricPathProvider.cs` | Modified (T3 -- added 3 DIM methods) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/BoundingBox3D.cs` | Created (T3) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FlyProfile.cs` | Created (T3) |
| `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeVolumetricPathProvider.cs` | Created (T3) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/FakeVolumetricPathProviderTests.cs` | Created (T3 tests) |

## 7. Test Summary

| Suite | Tests | Result |
|-------|-------|--------|
| NavigationTestWorldFactoryTests (Debt-02) | 1 | All pass |
| PathRegistryTests — Muscle (T4) | 6 | All pass |
| PathRegistryTests — Brain (T4) | 5 | All pass |
| PathRegistryTests — Shared (T4) | 3 | All pass |
| FakeNavmeshProviderTests (T1) | 8 | All pass |
| FakeVolumetricPathProviderTests (T3) | 6 | All pass |
| **Total new tests** | **29** | **All pass** |
| Pre-existing failures (unrelated) | 24 | Unchanged |
| Full solution build | -- | 0 errors, 9 pre-existing warnings |
