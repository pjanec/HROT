# BATCH-06 Report

**Batch:** BATCH-06  
**Date:** 2026-02-24  
**Status:** ✅ COMPLETE

---

## Test Results

### `FDP.Toolkit.CarKinem.Tests` (updated)

| Suite | Passed | Failed | Skipped |
|---|---|---|---|
| `SpatialHashGridTests` | 6 | 0 | 0 |
| `SpatialHashSystemTests` (new) | 1 | 0 | 0 |
| All other CarKinem suites | 106 | 0 | 0 |
| **Total** | **113** | **0** | **0** |

New tests added: `SpatialHashGrid_QueryNeighbors_ReturnsFullEntity_NotRawIndex` (DEBT-009 proof), `SpatialHashSystem_IndexesEntity_WithSimTransformButNoVehicleState` (DEBT-001).

### `FDP.Toolkit.Perception.Tests` (updated)

| Suite | Passed | Failed | Skipped |
|---|---|---|---|
| `PerceptionComponentTests` | 5 | 0 | 0 |
| `AudioPerceptionSystemTests` | 3 | 0 | 0 |
| `VisionBroadphaseSystemTests` | 5 | 0 | 0 |
| `ThreatEvaluationSystemTests` | 3 | 0 | 0 |
| `LosRequestBatchingSystemTests` | 2 | 0 | 0 |
| **Total** | **18** | **0** | **0** |

New tests added: `VisionBroadphase_UsesLocalGrid_DoesNotBruteForce` (DEBT-011 isolation proof), `ThreatEvaluation_BoostsScore_OnTargetVisibleEvent` (DEBT-013), `ThreatEvaluation_ZeroScoreEntry_IsRetained` (DEBT-013).

### `FDP.Toolkit.Navigation.Tests` (new project)

| Suite | Passed | Failed | Skipped |
|---|---|---|---|
| `NavigationActionTests` | 7 | 0 | 0 |
| **Total** | **7** | **0** | **0** |

### Full solution

```
dotnet build FDP.sln   → Build succeeded, 0 Error(s)
dotnet test  FDP.sln   → Failed: 0, Passed: 1269, Skipped: 3
```

---

## Task Completion Checklist

### P1 Correctives

- [x] **DEBT-009** — `SpatialHashGrid.GridValues` changed to `NativeArray<Entity>`; `Add` takes `Entity`; `QueryNeighbors` returns `Span<(Entity entity, Vector2 pos)>`. Callers updated: `SpatialHashSystem`, `CarKinematicsSystem`, `AudioPerceptionSystem`. Generational round-trip test added.

- [x] **DEBT-010** — `EntityRepository.GetEntity(int)` changed from `public` to `internal`. XML doc added explaining the two valid call sites (C++ plugin interop + kernel-internal bit-scanning). Zero toolkit call sites remained after fixing DEBT-009 (see Q1).

- [x] **DEBT-011** — `LocalGridBuilderSystem` created; `PerceptionModule` allocates a private `SpatialHashGrid` in its constructor (Persistent allocator), implements `IDisposable`, disposes in `Dispose()`; `VisionBroadphaseSystem` accepts the grid via constructor and queries it. Brute-force target scan eliminated.

### Debt integrations

- [x] **DEBT-001** — `SpatialHashSystemTests.SpatialHashSystem_IndexesEntity_WithSimTransformButNoVehicleState`: creates entity with `SimTransform` only (no `VehicleState`), runs `SpatialHashSystem`, verifies entity is returned from `QueryNeighbors`.

- [x] **DEBT-012** — Replaced `0.866f` literals in `VisionBroadphaseSystemTests` Tests 2 and 3 with `MathF.Cos(MathF.PI / 6f)`.

- [x] **DEBT-013** — Added `ThreatEvaluation_BoostsScore_OnTargetVisibleEvent` (verifies score ≥ 50 after event) and `ThreatEvaluation_ZeroScoreEntry_IsRetained` (documents retention policy: count stays 1 after decay to 0).

- [x] **DEBT-014** — `AudioPerceptionSystemTests` Test 2: replaced `SourceEntityIndex = 99` (fabricated constant) with `dummySource.Index` where `dummySource` is a real entity created in the world via `world.CreateEntity()` + `world.AddComponent(dummySource, new SimTransform {...})`.

### BCS-P3-T1 — Navigation Actions

- [x] `FDP.Toolkit.Navigation.csproj` created; references `Fdp.Kernel`, `FDP.Toolkit.Behavior`, `FDP.Toolkit.CarKinem`.
- [x] `NavigationConstants.cs` — `ActionIdMoveTo=1`, `ActionIdFlee=2`, `ActionIdFollowRoute=3`, `ActionIdFollowRoadGraph=4`; `FrustrationTickThreshold=120`, `FrustrationSpeedThreshold=0.1f`.
- [x] `NavigationActions.cs` — `MoveToParams` (16 B), `FleeParams` (16 B, `Entity Threat`), `FleeState` (4 B), `FollowRouteParams` (8 B), `FollowRoadGraphParams` (8 B); all `[StructLayout(LayoutKind.Sequential)]`, all ≤ 32 B.
- [x] `FDP.Toolkit.Navigation.Tests.csproj` created; 7 tests all green.
- [x] Both projects added to `FDP.sln` with `NestedProjects` entries under the Toolkits solution folder.

---

## Q1 — Compiler errors after `GetEntity(int)` made `internal`

**2 call sites** produced compiler errors:

| File | Line | Error | Fix applied |
|---|---|---|---|
| `FDP.Toolkit.Perception/Systems/AudioPerceptionSystem.cs` | ~66 | CS0122 — `GetEntity(int)` inaccessible | Replaced `Entity listener = World.GetEntity(candidateIdx)` with `Entity listener = neighbors[i].entity` (entity comes directly from `QueryNeighbors` after DEBT-009 refactor) |
| `FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs` | ~308 | CS0122 — `GetEntity(int)` inaccessible | Replaced `var entity = World.GetEntity(entityId)` with direct use of `neighborEntity` from the `(Entity, Vector2)` tuple returned by `QueryNeighbors` |

Both fixes are zero-overhead: the `Entity` handle is already present in the span tuple after DEBT-009. No API workarounds; no casting. The `internal` modifier enforced the architectural contract as intended.

---

## Q2 — Memory lifecycle of `PerceptionModule._localGrid`

**Allocated:** In `PerceptionModule`'s constructor via `SpatialHashGrid.Create(LocalGridWidth, LocalGridHeight, LocalGridCellSize, LocalGridMaxEntities, Allocator.Persistent)`. `Allocator.Persistent` calls `Marshal.AllocHGlobal` internally, placing all three `NativeArray<T>` backing buffers (`GridHead`, `GridNext`, `GridValues`) in unmanaged memory outside the GC heap.

**Cleared:** At the start of each tick by `LocalGridBuilderSystem.Execute` — `_grid.Clear()` resets all `GridHead` entries to `−1` and the entity count to 0 in O(W×H) time.

**Rebuilt:** Immediately after `Clear()`, `LocalGridBuilderSystem` iterates every live entity with a `SimTransform` (via `view.Query().With<SimTransform>().Build()`) and calls `_grid.Add(entity, pos)` for each, inserting the full `Entity` handle into the appropriate cell chain.

**Disposed:** In `PerceptionModule.Dispose()` — `_localGrid.Dispose()` calls `Marshal.FreeHGlobal` on all three backing arrays.

**If `Dispose()` is never called** (e.g. a test that creates `PerceptionModule` but does not dispose it): all three `NativeArray` allocations — sized at `LocalGridMaxEntities = 50,000` entries of `Entity` (8 B), `int` (4 B), and `int` (4 B) respectively — remain pinned in native memory for the process lifetime. Total leak: ~800 KB per module instance. The GC cannot collect unmanaged memory. Tests that use `PerceptionTestWorldFactory` do not instantiate `PerceptionModule` directly, so they are unaffected; but any integration test or production host that constructs a `PerceptionModule` must call `Dispose()` (or wrap it in a `using` block).

---

## Q3 — `VisionBroadphase_UsesLocalGrid_DoesNotBruteForce` test design

**Setup:**
1. Two `Enemy` entities (`targetA` and `targetB`) are created in the world and given `SimTransform`, `Faction(factionId=2)` components.
2. A `SpatialHashGrid` (100×100 cells, 5 m/cell, 1,000 max) is created independently of the world.
3. `grid.Clear()` then `grid.Add(targetA, pos2DA)` — **only `targetA` is added to the grid**. `targetB` is in the world but not in the grid.
4. A `VisionBroadphaseSystem(grid)` is constructed with this grid (not a brute-force scan).
5. An `Observer` entity is created with `SimTransform`, `Faction(factionId=1)`, `PerceptionReceptor(VisionRange=50f, FieldOfViewCos=-1f)` (full 360° FOV so geometry cannot explain any exclusion).

**Assertion:**
After `sys.Execute(view, dt)` and ECB flush, exactly **1** `LosCheckRequestEvent` is consumed. The event's `TargetEntityIndex` is `targetA.Index`, not `targetB.Index`.

**Why this is not trivially satisfied by coincidence:**
Both `targetA` and `targetB` are live in the world with identical faction and within visual range. A brute-force implementation scanning all `With<Faction>().With<SimTransform>()` entities would emit 2 events (one for each enemy). The test asserts `count == 1` — which is only possible if the system queries the grid exclusively and ignores world entities not present in the grid. This design directly falsifies any residual brute-force path.

---

## Q4 — Stale `FleeParams.Threat` entity mid-flee

`FleeParams.Threat` is a full `Entity` handle (8 bytes: `int Index` + `ushort Generation`). When the threat entity is destroyed, `EntityRepository` increments that slot's generation counter, making the stored `Entity` stale.

**Where to handle it in `FleeExecutor` (Phase 3 T3):**

At the start of each `Execute` tick, before any pathfinding or steering logic:

```csharp
if (!view.IsAlive(params.Threat))
{
    // Threat is dead — flee objective is satisfied (or target lost).
    ecb.SetComponent(self, new NavState { Mode = NavigationMode.None });
    return BTreeStatus.Success; // or Failure if "lost sight" semantics preferred
}
```

`view.IsAlive(entity)` performs a generational check (`repo.Entities[index].Generation == entity.Generation`), which is O(1) and safe to call every tick. Without this guard, `view.GetComponentRO<SimTransform>(params.Threat)` would throw on a recycled or dead slot, or silently read stale data from a newly spawned entity that reused the same index — the exact bug DEBT-009 was designed to prevent.

**What the executor should report:** `BTreeStatus.Success` (threat eliminated → mission accomplished) or `BTreeStatus.Failure` (threat lost → parent BTree decides whether to re-acquire). The choice depends on doctrine; the architectural requirement is that the check is made **every tick**, not just on entry.
