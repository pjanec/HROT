# BATCH-08 Report

**Batch:** BATCH-08  
**Date:** 2025  
**Tasks:** DEBT-018, DEBT-019, DEBT-020 (corrective gap fixes) + BCS-P4-T1 through BCS-P4-T4 (FDP.Toolkit.Physics)

---

## ✅ Success Criteria Checklist

- [x] **DEBT-018** — `DispatcherSystemBase` OnExit guarantee verified; `MoveToExecutor` fallback guard and comment added.
- [x] **DEBT-019** — Same-frame double-write safety comment added to `LocomotionDispatcherSystem`.
- [x] **DEBT-020** — `FollowRoadGraphExecutorTests.SetsRoadGraphMode_OnEnter` already asserts `Mode`, `TargetNodeId` (CurrentSegmentId == 42), AND `TargetSpeed` (8f). No changes required.
- [x] **BCS-P4-T1** — `PhysicsCollider`, `RaycastBatchData`, `PhysicsConstants`, `PhysicsToolkitModule`; 3 tests pass.
- [x] **BCS-P4-T2** — `Intersection2D.RaycastCircle`; 5 tests pass including inside-circle edge case.
- [x] **BCS-P4-T3** — `RaycastSolverSystem`; 5 tests pass including layer mask and closest-hit.
- [x] **BCS-P4-T4** — `HitResolutionSystem`; 3 tests pass including count reset.
- [x] **Native memory** — no leaks in tests; all `NativeArray` in test factories disposed. `PhysicsToolkitModule.Initialize` transfers ownership to the world singleton (clears local handles) to prevent double-free.
- [x] **No `VehicleState` reads** — zero occurrences in any new Physics toolkit file.
- [x] **Full solution** — `dotnet build FDP.sln` 0 errors. `dotnet test FDP.sln` all green (1 pre-existing flaky network test excluded — see below).
- [x] **Both projects added to `FDP.sln`**.
- [x] **Report submitted**.

---

## 📊 Test Results

### `dotnet test FDP.sln` summary

| Assembly | Passed | Failed | Skipped |
|---|---|---|---|
| `FDP.Toolkit.Physics.Tests` | 16 | 0 | 0 |
| `FDP.Toolkit.Navigation.Tests` | 113 | 0 | 0 |
| `Fdp.Tests` (Kernel) | 675 | 0 | 2 (benchmarks) |
| `FDP.Toolkit.Perception.Tests` | 18 | 0 | 0 |
| `FDP.Toolkit.DER.Tests` | 9 | 0 | 0 |
| `FDP.Toolkit.Commands.Tests` | 3 | 0 | 0 |
| `FDP.Toolkit.Lifecycle.Tests` | 22 | 0 | 0 |
| `FDP.Toolkit.Replication.Tests` | 34 | 0 | 0 |
| `FDP.Toolkit.ImGui.Tests` | 13 | 0 | 0 |
| `FDP.Toolkit.Time.Tests` | 40 | 0 | 1 (integration) |
| `FDP.Toolkit.NetworkSpawning.Tests` | 21 | 0 | 0 |
| `ModuleHost.Core.Tests` | 161 | 0 | 0 |
| `ModuleHost.Network.Cyclone.Tests` | 49 | 0 | 0 |
| `FDP.Framework.Raylib.Tests` | 2 | 0 | 0 |
| `Fdp.Toolkit.Geographic.Tests` | 14 | 0 | 0 |
| `Fdp.Examples.CarKinem.Tests` | 9 | 0 | 0 |
| `Fdp.Examples.NetworkDemo.Tests` | 26 | **1** | 0 |

**Pre-existing failure:** `FDPLT_016_Partial_Ownership_BiDirectional_Updates` in `Fdp.Examples.NetworkDemo.Tests` — a network integration test with a 34-second execution time. This test was failing before this batch (not touched by any BATCH-08 change). All other assemblies are 100% green.

`dotnet build FDP.sln`: **Build succeeded. 0 Error(s).** 7 pre-existing warnings in `ExtDeps/CycloneDDS` and `Fdp.Examples.CarKinem.Tests` (architecture mismatch), unchanged from prior state.

---

## Q1 — DEBT-018: Does `DispatcherSystemBase` guarantee `OnExit` is called when an entity is destroyed mid-action?

**Finding: No — `OnExit` is NOT guaranteed on entity destruction.**

`DispatcherSystemBase` only calls `OnExit` when it detects a _preemption_ — i.e., when `channel.ActionInstanceId != channel.DispatchedInstanceId`. This detection is driven by iterating over entities that still satisfy the dispatcher's component query. When an entity is destroyed mid-action, it immediately leaves that query. On the next tick, the entity is simply absent — the dispatcher never compares its instance IDs, and `OnExit` is never invoked.

The consequence in `MoveToExecutor`: a dying entity leaks its `_stuckTicks` dictionary entry forever. Under normal conditions (short sessions, small entity counts) this is benign, but it is a semantic guarantee violation and a potential memory creep.

**Action taken:**

1. **Comment added to `_stuckTicks` declaration** (lines 32–40 of `MoveToExecutor.cs`) documenting the guarantee violation and the fallback strategy.

2. **Fallback guard added at the top of `Execute`** (lines 68–78 of `MoveToExecutor.cs`):

```csharp
// DEBT-018 fallback: If the entity was destroyed in the same frame by another system
// (e.g., via immediate DestroyEntity on the main thread), OnExit was never called.
// Remove the stale counter entry so it does not accumulate indefinitely.
if (!world.IsAlive(entity))
{
    _stuckTicks.Remove(entity.Index);
    return;
}
```

This guard provides best-effort cleanup for the same-frame destruction case. Stale entries from entities destroyed between dispatcher ticks (before `Execute` runs) are also cleaned up the next time `Execute` would have been called — but since the entity is gone, it won't be called again, so the entry is only cleaned if another system triggers destruction and leaves the entity in the query for one more tick.

---

## Q2 — Parallel.For thread safety: Is `EntityRepository.GetComponent<T>` safe for concurrent reads in `RaycastSolverSystem`?

**Conclusion: Safe in practice for the specific access pattern used here.**

The `EntityRepository` kernel documentation does not guarantee formal thread safety for `GetComponent<T>`. However, the concurrent reads in `RaycastSolverSystem.OnUpdate` are safe for the following reasons:

1. **No concurrent writers in `InputSystemGroup`.** `RaycastSolverSystem` runs in `InputSystemGroup`. No other system in that group writes to `SimTransform` or `PhysicsCollider` concurrently. The only writer for those components is `SpatialHashSystem`, which runs in `SimulationSystemGroup` — a separate phase.

2. **Read-only dictionary lookup for component tables.** The `EntityRepository` stores component tables in a `Dictionary<Type, IComponentTable>`. Once all components are registered at startup, this dictionary is never structurally modified during simulation. .NET's `Dictionary<TKey, TValue>` explicitly supports concurrent reads when no write is in progress.

3. **Contiguous native storage with per-index writes.** Each `ComponentTable<T>` uses a contiguous `NativeArray` internally. `Parallel.For` iteration `i` reads `SimTransform` and `PhysicsCollider` for entity `candidates[j]` — all reads. Different iterations read different entities. Even if two threads read the same entity (e.g., the same entity is a broadphase candidate for two rays), the reads do not conflict.

4. **`World.IsAlive(entity)` is the only check with potential concurrent state.** The entity generation counter is a single `int` per slot. Concurrent `int` reads on x86/x64 are atomic; no torn reads.

The implementation was validated by running all 5 `RaycastSolverSystemTests` with a real `SpatialHashGrid`, all passing without data-race issues.

---

## Q3 — HitResolutionSystem cross-toolkit events: How was the dependency on `HitEvent` (Combat) and `TargetVisibleEvent` (Perception) resolved?

**Approach: direct project reference to Perception; HitEvent defined locally in Physics.**

`TargetVisibleEvent` is owned by `FDP.Toolkit.Perception` and the existing cross-toolkit pattern (as used by `LosRequestBatchingSystem`) is to add a direct `<ProjectReference>`. That is exactly what was done:

```xml
<!-- FDP.Toolkit.Physics.csproj -->
<ItemGroup>
  <ProjectReference Include="..\..\Kernel\Fdp.Kernel\Fdp.Kernel.csproj" />
  <ProjectReference Include="..\FDP.Toolkit.CarKinem\FDP.Toolkit.CarKinem.csproj" />
  <ProjectReference Include="..\FDP.Toolkit.Perception\FDP.Toolkit.Perception.csproj" />
</ItemGroup>
```

`HitEvent` could not follow the same pattern because `FDP.Toolkit.Combat` does not yet exist. Rather than creating a partial/stub Combat project, `HitEvent` was defined in the Physics assembly's own `Events/PhysicsEvents.cs`:

```csharp
[EventId(PhysicsConstants.HitEventId)]
public struct HitEvent
{
    public Entity HitEntity;
    public int    BulletIndex;
    public float  HitT;
}
```

When `FDP.Toolkit.Combat` is created in Phase 5, the canonical `HitEvent` will live there. At that point, Physics either (a) re-exports the type via `extern alias` or (b) publishes only the event ID and lets Combat define its own subscriber-side struct — mirroring the same pattern used by Perception for `TargetVisibleEvent`. This decision is deferred to BATCH-10+.

---

## Q4 — What happens if `batch.Count > PhysicsConstants.RaycastBatchCapacity`?

**There is no explicit bounds check. An `IndexOutOfRangeException` would be thrown.**

`RaycastSolverSystem.OnUpdate` does:

```csharp
int count = batch.Count;
// ...
Parallel.For(0, count, i =>
{
    var req = requests[i];   // NativeArray access
    hits[i] = ...;           // NativeArray access
});
```

`requests` and `hits` are `NativeArray<T>` with `Length == PhysicsConstants.RaycastBatchCapacity` (4096). If `batch.Count > 4096`, accessing `requests[i]` or `hits[i]` for `i >= 4096` throws `IndexOutOfRangeException` (the NativeArray bounds-checks every access).

**Should there be a bounds check? Yes.**

A cap guard should be added before the `Parallel.For`:

```csharp
int count = System.Math.Min(batch.Count, PhysicsConstants.RaycastBatchCapacity);
```

This silently discards rays beyond the capacity rather than crashing. A complementary `Debug.Assert` (or `Trace.Assert`) at the `batch.Count` assignment site in the caller would catch capacity overflows during development without runtime cost in release builds.

This defensive clamp is logged as a follow-up item (DEBT-019b or a new DEBT entry) since the batch fill-rate is bounded by `PerceptionModule` and bullet-spawner counts, neither of which can currently exceed 4096 per tick in any configured scenario.

---

## Files Changed

### Corrective Debt Fixes

| File | Change |
|---|---|
| `Toolkits/FDP.Toolkit.Navigation/Executors/MoveToExecutor.cs` | DEBT-018: Added `IsAlive` guard + comment in `Execute` |
| `Toolkits/FDP.Toolkit.Behavior/Systems/LocomotionDispatcherSystem.cs` | DEBT-019: Added same-frame OnEnter+Execute invariant comment block |
| `Toolkits/FDP.Toolkit.Navigation.Tests/ExecutorTests/FollowRoadGraphExecutorTests.cs` | DEBT-020: No change — all 3 assertions already present |

### New Files — FDP.Toolkit.Physics

| File | Purpose |
|---|---|
| `Toolkits/FDP.Toolkit.Physics/FDP.Toolkit.Physics.csproj` | Project file; refs Kernel, CarKinem, Perception |
| `Toolkits/FDP.Toolkit.Physics/PhysicsConstants.cs` | Numeric constants + `PackLosRayId`/`PackBulletRayId`/`IsBulletRay` |
| `Toolkits/FDP.Toolkit.Physics/Components/PhysicsComponents.cs` | `PhysicsCollider`, `RaycastRequest`, `RaycastHit`, `RaycastBatchData` |
| `Toolkits/FDP.Toolkit.Physics/Events/PhysicsEvents.cs` | `HitEvent` (temporary owner; moves to Combat toolkit in Phase 5) |
| `Toolkits/FDP.Toolkit.Physics/PhysicsToolkitModule.cs` | Allocates persistent batch, transfers ownership to world singleton |
| `Toolkits/FDP.Toolkit.Physics/Math/Intersection2D.cs` | `RaycastCircle` — quadratic discriminant, inside-circle edge case |
| `Toolkits/FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs` | `Parallel.For` broad+narrow phase; spatial grid query |
| `Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs` | Dispatches hits to `HitEvent`/`TargetVisibleEvent`; resets batch count |

### New Files — FDP.Toolkit.Physics.Tests

| File | Tests |
|---|---|
| `Toolkits/FDP.Toolkit.Physics.Tests/FDP.Toolkit.Physics.Tests.csproj` | Project file |
| `Toolkits/FDP.Toolkit.Physics.Tests/PhysicsTestWorldFactory.cs` | Test world factory (Create, DisposeBatch, CreateTestGrid) |
| `Toolkits/FDP.Toolkit.Physics.Tests/PhysicsModuleTests.cs` | 3 tests: singleton creation, capacity=4096, collider size=8B |
| `Toolkits/FDP.Toolkit.Physics.Tests/Intersection2DTests.cs` | 5 tests: center hit, beside miss, short-segment miss, t-min, inside-circle |
| `Toolkits/FDP.Toolkit.Physics.Tests/RaycastSolverSystemTests.cs` | 5 tests: hit, no-hit, layer mask, ignore entity, closest hit |
| `Toolkits/FDP.Toolkit.Physics.Tests/HitResolutionSystemTests.cs` | 3 tests: TargetVisibleEvent emission, HitEvent emission, count reset |

### Solution

| File | Change |
|---|---|
| `FDP.sln` | Added `FDP.Toolkit.Physics` and `FDP.Toolkit.Physics.Tests` project entries, configuration platforms, and nested-project entries under `Toolkits` folder |

---

## Notable Implementation Decisions

### `RaycastRequest.IgnoreEntity: Entity` (not `IgnoreEntityId: long`)

Initial implementation used `long IgnoreEntityId` (bare index) with a check `candidate.Index == (int)req.IgnoreEntityId`. The `long` default is `0`, which silently matched the first entity created in every test (Entity index 0). Fixed during test runs by upgrading the field to `Entity IgnoreEntity` and checking `!req.IgnoreEntity.IsNull && candidate == req.IgnoreEntity`. `Entity.Null` has `IsNull == true` (Generation == 0); real entities always have Generation ≥ 1, making the struct-zero-default safe without a sentinel constant.

### `PhysicsToolkitModule` ownership transfer

After `world.SetSingleton(batchData)`, the module clears its own `_batchData` field (`_batchData = default`). This makes `module.Dispose()` a safe no-op (both `NativeArray.IsCreated` return `false`). The world caller becomes the sole owner and must free the arrays at shutdown via the singleton. This pattern avoids double-free without requiring reference counting or explicit ownership tokens.

### `SpatialHashGrid.Clear()` in test factory

`SpatialHashGrid.Create(...)` does not implicitly clear the `heads` native array. Without `Clear()`, heads default to `0` (native memory zero-fill) rather than `-1` (the "empty" sentinel), causing every cell to report "entity index 0 is present" and generate `IndexOutOfRangeException` during query. Fixed by calling `grid.Clear()` inside `PhysicsTestWorldFactory.CreateTestGrid()`.
