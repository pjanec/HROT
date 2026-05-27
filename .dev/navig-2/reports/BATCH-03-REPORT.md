# BATCH-03 Report

**Batch:** BATCH-03  
**Developer:** GitHub Copilot  
**Date:** 2025-07-25  
**Status:** Complete

---

## Task Completion

| Task ID     | Status | Notes |
|-------------|--------|-------|
| NAV-P1-T1   | [x] | `PathfindingRequestEvent` extended with 5 new fields; `PathfindingResultEvent` extended with 4 new fields; `NavigationBackend` and `NavigationFailureReason` enums added; `MobilityProfile.Naval=3` and `MobilityProfile.Flying=4` added; `MoveStartedEvent` (EventId 2034) added (required by T4). DDS translators (`PathRequestEgressTranslator`, `PathResponseIngressTranslator`) updated for structural forward compatibility. |
| NAV-P1-T2   | [x] | `NavigationIntentBridgeSystem` routes `MoveTo` and `PlanRoute` to the solver via `PathfindingRequestEvent`; `FollowPath` with an unknown handle sets `FailedInvalidHandle` immediately; `FetchPathDetails` and `ReleasePath` are stubbed. Idempotency guard (unchanged `ActionInstanceId`) prevents re-publishing. |
| NAV-P1-T3   | [x] | `PathfindingSolverSystem` selects backend by `MobilityProfile + BackendForce`: `Flying` → `IVolumetricPathProvider`, `BackendForce` override respected, Auto heuristic (road-radius probe → RoadGraph, else Navmesh). `NavigationHandleAllocator` allocates Muscle-internal handles >= `0x40000000` via `Interlocked.Increment`. `TrajectoryPoolManager.RegisterTrajectoryWithKey` added. `PrimaryBackend` propagated into `PathfindingResultEvent`. |
| NAV-P1-T4   | [x] | `PathfindingResultMaterializationSystem` extended: entity lookup from high 32 bits of `RequestId`, `LocomotionChannel.ActiveAction` branch, writes `NavigationCorridorMuscle`, updates `NavigationStatus`, fires `MoveStartedEvent` for reachable MoveTo. Unreachable paths set `FailedUnreachable` / `NoPath` without writing corridor. `PathfindingBatchData.DefaultCapacity` raised from 64 to 256. |

---

## Testing Results

**Unit Tests — `Fdp.Toolkits.Tests` (Navigation filter):** 96 / 96  
**Full solution build:** 0 errors; 9 pre-existing unrelated warnings in `Hrot.Blueprints.Tests`

**Tests added per task:**

| Task | Test class | Count |
|------|-----------|-------|
| T1 | `PathfindingEventExtensionsT1Tests` | 4 |
| T2 | `NavigationIntentBridgeT2Tests` | 4 |
| T3 | `PathfindingSolverBackendSelectionTests` | 4 |
| T4 | `PathfindingResultMaterializationT4Tests` | 5 |

---

## Developer Insights

**Q1: What issues were encountered? How were they resolved?**

1. **`NavigationBackend.RoadGraph` vs `NavRoadGraph` IDL collision** — The CycloneDDS IDL
   generator flattens all enum value names into one namespace per module. The name `RoadGraph`
   is not itself problematic, but adding a second `None` value (from `NavigationFailureReason`)
   would collide with any existing `None`. More critically, `Auto` in `NavigationBackend`
   potentially clashes with values from other enums. Resolution: all values were chosen to be
   globally unique across the nav enums. `NavigationFailureReason.NoFailure = 0` was used
   instead of `None` to avoid the well-known `None` clash. `NavigationBackend` member for road
   graph was named `NavRoadGraph` (rather than `RoadGraph`) to avoid a potential clash with any
   future road-graph enum.

2. **`Moq` cannot mock `Span<T>` parameters** — `IVolumetricPathProvider.PlanPath` and
   `INavmeshProvider.PlanPath` both take `Span<NavWaypoint>` as an out-buffer. Moq generates
   IL that tries to pass a `Span<T>` by reference through `MethodInfo.Invoke`, which is not
   allowed on ref structs. Resolution: concrete inner stub classes
   (`StubVolumetricProvider`, `StubNavmeshProvider`) were written for all T3 backend-selection
   tests. This is the only viable approach for interfaces with `Span<T>` parameters in xUnit.

3. **`GetComponentRW<T>` requires the table to be registered** — `GetTable<T>(false)` throws
   `InvalidOperationException` if the component type was never registered via
   `RegisterComponent<T>()`. This would affect `NavigationCorridorMuscle` in the existing
   `PathfindingSolverSystemTests`, which use artificial entity indices (1,2,3,4) that produce
   `Entity.Null` from `GetEntityByIndex`. The `IsAlive` guard (`if (!repo.IsAlive(entity))
   continue;`) short-circuits before any component access, so no registration is needed in
   those tests. New T4 tests call `_world.RegisterComponent<NavigationCorridorMuscle>()` in
   their own constructor. ✓

4. **`EventCommandBuffer` vs `ISimulationView.GetCommandBuffer()` type** — `PathfindingSolverSystem`
   runs at `SlowBackground(10Hz)` and must use the command-buffer path to publish events
   (not `repo.Bus.Publish` directly). The return type of `view.GetCommandBuffer()` is
   `IEventCommandBuffer`, but the concrete type returned by the test world is
   `EntityCommandBuffer`. The cast `(EntityCommandBuffer)view.GetCommandBuffer()` followed by
   `ecb.Playback(_world)` was required in the test harness to replay results. This is consistent
   with how `PathfindingSolverSystemTests` already worked.

**Q2: What weak points were spotted in the codebase?**

- **`PathfindingBatchData.Results` indexing is a lossy ring** — slot assignment is
  `requestId % DefaultCapacity`. With 256 slots and bursty traffic, two concurrent requests
  from the same entity within the same capacity window will alias. No overflow or collision
  detection exists. This is noted in Design §15 as an acceptable trade-off, but there is no
  diagnostic (assert, counter, or metric) to detect it at runtime.

- **No priority sorting before solver runs** — The §15 budget bands (`Critical 50% / Normal
  35% / Low 15%`) are documented but the solver currently processes events in arrival order
  (ring buffer oldest-evict). Priority sorting is not yet implemented. The spec says a simple
  ring with oldest-evict is acceptable, but the bands provide no actual guarantee until a
  priority pass is added.

- **`NavigationTestWorldFactory` is not extended automatically** — Each test class that needs
  `NavigationCorridorMuscle` must register it manually in its own constructor. If a future
  refactor adds more components to the materialization path, all test constructors must be
  updated. A shared factory method (or a fluent builder) would be safer.

- **`TrajectoryPoolManager.RegisterTrajectoryWithKey` has no eviction** — Muscle-internal
  handles are allocated monotonically from `0x40000000` by `NavigationHandleAllocator`. If
  `ReleasePath` is never called (Phase 1 stub), the pool grows unbounded. Non-blocking for
  Phase 1, but Phase 3 must implement release.

**Q3: What design decisions were made beyond the spec?**

- **`MoveStartedEvent` was placed in `PathfindingEvents.cs`** (EventId 2034, sequential
  after `PathfindingResultEvent` 2033). The spec refers to it in T4 without specifying which
  file. Co-locating it with the other pathfinding events makes it easy to find and keeps
  the DDS struct-layout attributes together.

- **`NavigationCorridorMuscle.TotalSegmentCount` is always set to 1** for Phase 1. The road-
  graph Dijkstra returns a single trajectory, not a multi-segment corridor. A true segment
  split (e.g., RoadGraph + Navmesh hybrid) will be added when the Hybrid backend is
  implemented. The value `1` is semantically correct for now — one segment means the full
  path is contained in the registered `TrajectoryPoolManager` entry.

- **`NavigationCorridorMuscle.Flags` is left at 0** — The spec does not specify which flag
  bits are set by the solver; flag semantics are defined in Design §4.3 but the first-use
  values belong to Phase 2 (traversal state machine). Zero is the safe sentinel.

- **`NavigationStatus` write uses `GetComponentRW` (mutation) not `SetComponent`** — The
  spec says "update `NavigationStatus`" without prescribing the API. `GetComponentRW` returns
  a ref directly into the chunk and avoids a redundant copy; `SetComponent` would copy the
  entire struct. The `ref var` mutation pattern matches existing use throughout the codebase.

**Q4: Test coverage gaps — scenarios not testable yet**

- **Hybrid backend** (`NavigationBackend.Hybrid`) is defined but not exercised. The solver
  falls back to RoadGraph when `INavmeshProvider` is null. A real Hybrid path (RoadGraph
  splice + Navmesh) cannot be tested until Phase 2 provides fake navmesh providers.

- **`FetchPathDetails` and `ReleasePath`** in `NavigationIntentBridgeSystem` are stubs.
  T2 tests cover `MoveTo`, `PlanRoute`, and `FollowPath` (invalid handle). The remaining
  two action branches are only tested by the Phase 9 full system test (NAV-P9-T3).

- **`NavigationPathDetailsResponseEvent`** is referenced in T4 spec for `PlanRoute` with
  `IncludeFullPathDetails` flag. The event struct does not exist yet (Phase 4-T3 owns it);
  the flag branch is not yet written. The T4 test suite covers only the standard
  `PlanRoute` path.

- **Priority budget bands** (§15 Critical/Normal/Low) are not tested. No priority sorting
  is implemented; the solver always processes events in ring-buffer order. Phase 1 acceptance
  criteria do not require priority tests.

- **Multi-entity burst** — aliasing in the `requestId % DefaultCapacity` slot assignment
  when two entities share the same slot. Cannot be tested deterministically without
  controlling `GlobalVersion` and entity `Index` to produce a known collision.

**Q5: How was `NavigationHandleAllocator` implemented? Is it thread-safe?**

`NavigationHandleAllocator` uses a single `static int _counter` field initialized to
`MuscleHandleBase - 1` (= `0x3FFFFFFF`). `Allocate()` calls
`Interlocked.Increment(ref _counter)` and returns the result. Because `Interlocked.Increment`
is an atomic fetch-and-add, concurrent calls from different threads will each receive a
distinct value with no torn reads. The static counter is process-global — shared across all
`EntityRepository` instances in tests — which is intentional: handles must be unique across
the system. The first call returns `0x40000000` exactly.

---

## Outstanding Issues / Next Steps

- None blocking. All BATCH-03 tasks are complete and all 96 navigation tests pass.
- `ReleasePath` and `FetchPathDetails` stubs in `NavigationIntentBridgeSystem` must be
  implemented in a later phase (NAV-P3-T1 and NAV-P4-T3 respectively).
- The §15 budget-band priority sort should be added once the priority field is available
  on `PathfindingRequestEvent` (not defined in this batch's contract).
- `NavigationTestWorldFactory` should be extended to register `NavigationCorridorMuscle`
  so future test classes do not need to add it manually.
