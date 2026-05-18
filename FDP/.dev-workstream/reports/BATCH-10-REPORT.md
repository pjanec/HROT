# BATCH-10 REPORT — Phase 5 Combat Systems: Fire / Ballistics / Damage

**Batch:** BATCH-10  
**Tasks:** BCS-P5-T4 (`FireProcessingSystem`, `BallisticsSystem`) · BCS-P5-T5 (`DamageSystem`)  
**Date:** 2025-07-14  
**Status:** ✅ Complete

---

## Build & Test Summary

| Project | Tests | Result |
|---|---|---|
| `FDP.Toolkit.Combat.Tests` | 27 / 27 | ✅ All pass |
| `FDP.Toolkit.Physics.Tests` | 16 / 16 | ✅ All pass |
| Full solution (`FDP.sln`) | 0 errors | ✅ Build succeeded |

New tests added this batch: **16** (5 `FireProcessingSystemTests` + 6 `BallisticsSystemTests` + 5 `DamageSystemTests`).

Pre-existing failing tests (unrelated to this batch — timing / concurrent / distributed network tests):

- `Fdp.Tests.ComponentDirtyTrackingTests.ComponentDirtyTracking_ConcurrentScanPerformance`
- `Fdp.Tests.EntityComplexityPerformanceTests.Lightweight_PlainUnmanaged_BestPerformance`
- `Fdp.Tests.EventBusTests.Publish_ConcurrentExpansion_NoDataLoss`
- `ModuleHost.Core.Tests.NonBlockingIntegrationTests.Integration_AccumulatedTime_Correct`
- `Fdp.Examples.NetworkDemo.Tests.*` (distributed replay / lifecycle)

These failures pre-date this batch and are not affected by the changes here.

---

## Files Changed / Created

### New production files

| File | Purpose |
|---|---|
| `Kernel/Fdp.Kernel/Events/HitEvent.cs` | Moved `HitEvent` here from `FDP.Toolkit.Combat.Events` to break circular dependency |
| `Toolkits/FDP.Toolkit.Combat/Systems/FireProcessingSystem.cs` | BCS-P5-T4 — spawns bullet entities from `FireRequestEvent` |
| `Toolkits/FDP.Toolkit.Combat/Systems/BallisticsSystem.cs` | BCS-P5-T4 — per-frame lifetime culling + raycast sweep submission |
| `Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` | BCS-P5-T5 — consumes `HitEvent`, applies damage, destroys at zero HP |

### New test files

| File | Tests |
|---|---|
| `Toolkits/FDP.Toolkit.Combat.Tests/FireProcessingSystemTests.cs` | 5 |
| `Toolkits/FDP.Toolkit.Combat.Tests/BallisticsSystemTests.cs` | 6 |
| `Toolkits/FDP.Toolkit.Combat.Tests/DamageSystemTests.cs` | 5 |

### Modified files

| File | Change |
|---|---|
| `Kernel/Fdp.Kernel/EntityRepository.cs` | Added `public Entity GetEntityByIndex(int index)` (DEBT-027 mitigated) |
| `Toolkits/FDP.Toolkit.Combat/Constants/CombatConstants.cs` | Added `DefaultBulletDamage`, `BulletColliderRadius`, `BulletCollisionLayer`, `BulletLifetimeTicks` |
| `Toolkits/FDP.Toolkit.Combat/Events/CombatEvents.cs` | Removed `HitEvent` struct (now in `Fdp.Kernel`) |
| `Toolkits/FDP.Toolkit.Combat/FDP.Toolkit.Combat.csproj` | Added `<ProjectReference>` to `FDP.Toolkit.Physics` |
| `Toolkits/FDP.Toolkit.Physics/FDP.Toolkit.Physics.csproj` | Removed `<ProjectReference>` to `FDP.Toolkit.Combat` |
| `Toolkits/FDP.Toolkit.Physics.Tests/FDP.Toolkit.Physics.Tests.csproj` | Removed `<ProjectReference>` to `FDP.Toolkit.Combat` |
| `Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs` | Removed `using FDP.Toolkit.Combat.Events` |
| `Toolkits/FDP.Toolkit.Physics.Tests/PhysicsTestWorldFactory.cs` | Removed `using FDP.Toolkit.Combat.Events` |
| `Toolkits/FDP.Toolkit.Physics.Tests/HitResolutionSystemTests.cs` | Removed `using FDP.Toolkit.Combat.Events` |
| `Toolkits/FDP.Toolkit.Physics/Events/PhysicsEvents.cs` | Updated comment re `HitEvent` new home |

---

## Q1 — Combat Pipeline Execution Order

Full ordered pipeline (this batch fills in 3 of the 6 stages):

| Order | System | Group | Ordering Attribute |
|---|---|---|---|
| 1 | `FireProcessingSystem` | `InputSystemGroup` | *(none — first in group by registration order)* |
| 2 | `BallisticsSystem` | `SimulationSystemGroup` | `// [UpdateBefore(typeof(LinearKinematicsSystem))]` — commented out; see note below |
| 3 | `LinearKinematicsSystem` | `SimulationSystemGroup` | *(not yet implemented — Phase 0 placeholder)* |
| 4 | `RaycastSolverSystem` | `InputSystemGroup` | *(none — runs after previous systems have submitted requests)* |
| 5 | `HitResolutionSystem` | `InputSystemGroup` | `[UpdateAfter(typeof(RaycastSolverSystem))]` |
| 6 | `DamageSystem` | `InputSystemGroup` | `[UpdateAfter(typeof(HitResolutionSystem))]` |

**Note on `BallisticsSystem`:** The spec requires `[UpdateBefore(typeof(LinearKinematicsSystem))]`. Because `LinearKinematicsSystem` does not yet exist in the codebase, the attribute would cause a compile error (the attribute constructor performs a type check). The attribute is present as a commented-out directive with an inline note: *"uncomment when LinearKinematicsSystem exists"*. The XML doc-comment also documents the intended ordering. When `LinearKinematicsSystem` is implemented (likely BATCH-11), the comment is uncommented — one-line change.

**Why `BallisticsSystem` is in `SimulationSystemGroup`:** It reads the current frame's `SimTransform` (position after velocity integration so far) to form the swept ray endpoint. Placing it in `SimulationSystemGroup` before kinematics runs ensures the ray starts at the bullet's pre-integration position and ends at the post-integration position — the physically correct swept segment for that frame.

---

## Q2 — DEBT-027 Mitigation in `DamageSystem`

`HitEvent.BulletIndex` is a raw entity slot index packed via `PhysicsConstants.PackBulletRayId`. `DamageSystem` retrieves the bullet entity via:

```csharp
var bulletEntity = World.GetEntityByIndex(evt.BulletIndex);
if (!World.IsAlive(bulletEntity)) continue;
if (!World.HasComponent<BallisticProjectile>(bulletEntity)) continue;
```

**Two-layer guard:**

1. **`IsAlive` check** — the primary generational-safety guard. `GetEntityByIndex` returns the raw `Entity` value at that slot (which may have a stale generation counter if the slot was recycled). `IsAlive` compares the stored generation against the slot's current generation and returns `false` for stale handles. This is the only public API in the kernel that provides this safety — there is no `TryGetEntity` or `TryGetComponent` that combines both steps.

2. **`HasComponent<BallisticProjectile>` check** — secondary type-safety guard. Even after `IsAlive` returns `true`, the entity at that slot might be a completely different entity (same slot, same generation, fresh recycle within the same frame boundary). Confirming the component is present ensures the entity is actually a bullet and not a recently-created unrelated entity that was assigned to the same slot in the same tick. This is an extra hardening step beyond what DEBT-027 strictly requires; it adds negligible overhead and eliminates an entire class of silent misapplication.

**What we cannot protect against:** If a bullet entity is created, killed, and the exact same slot recycled to an entity that also happens to carry `BallisticProjectile` — all within the same event-consumption window — we would apply damage using the wrong bullet's `Damage` value. This is an inherent limitation of raw-index identity passing and is the full scope of DEBT-027. The actual risk is astronomically low in practice (requires same-slot same-generation same-component coincidence in the same tick). Full resolution requires carrying `Entity` handles in `RaycastHit.RayId` — a larger pipeline change deferred per the DEBT-027 entry.

**No new API was needed beyond `IsAlive`.** `GetEntityByIndex` was added as a `public` wrapper over the existing `internal GetEntity(int)`.

---

## Q3 — `RaycastBatchData` Availability Guard in `BallisticsSystem`

`RaycastBatchData` is registered as an unmanaged singleton by `PhysicsToolkitModule.Initialize()`. `BallisticsSystem` runs in `SimulationSystemGroup` — there is no kernel-level guarantee that `PhysicsToolkitModule` has been initialised before `BallisticsSystem.OnUpdate` runs.

Guard applied at the top of `OnUpdate`:

```csharp
if (!World.HasSingleton<RaycastBatchData>()) return;
```

**Effect:** If the Physics module has not been set up (e.g., in unit tests that only initialise the Combat module, or if a future host omits `PhysicsToolkitModule`), `BallisticsSystem` silently skips the entire frame. Bullets continue to age and will eventually be destroyed by the lifetime check on a later frame when the singleton is present. No exception, no data corruption, no bullet "ghosting" — the worst outcome is one frame of missed raycasts.

**Unit test handling:** `BallisticsSystemTests` manually sets the `RaycastBatchData` singleton with `Persistent`-allocation `NativeArray<T>` fields and `Dispose()`s them in the `IDisposable` teardown to avoid native memory leaks under the test runner.

---

## Q4 — Additional Design Decisions and Edge Cases

### Circular dependency resolution (`HitEvent` → `Fdp.Kernel`)

Adding Physics as a dependency of Combat (`Combat → Physics`) creates a problem because Physics already depended on Combat (`Physics → Combat`) for `HitEvent`. The cycle `Combat → Physics → Combat` would prevent compilation.

**Resolution:** `HitEvent` was moved from `FDP.Toolkit.Combat.Events` to `Fdp.Kernel` (new file `Kernel/Fdp.Kernel/Events/HitEvent.cs`). Both `FDP.Toolkit.Combat` and `FDP.Toolkit.Physics` already depend on `Fdp.Kernel`, so no new project reference was required for either. `FDP.Toolkit.Physics.csproj` had its `<ProjectReference>` to `FDP.Toolkit.Combat` removed. All consumers of `HitEvent` in Physics tests already had `using Fdp.Kernel` in scope or it was added. The `[EventId(5001)]` attribute value and struct layout are identical to the original; no wire-format change.

### `FireRequestEvent` shooter alive-check

A `FireRequestEvent` can arrive with a `Shooter` entity that was destroyed in the same `InputSystemGroup` frame (e.g., the shooter died and fired in the same batch). `FireProcessingSystem` guards with `if (!World.IsAlive(evt.Shooter)) continue` before spawning the bullet. A ghost bullet from a dead shooter would carry a stale `Shooter` reference into `BallisticsSystem`, which would submit a raycast that ignores a nonexistent entity — harmless, but wasteful. The alive-check at spawn time prevents this.

### `GlobalTime` singleton optional handling

Both `FireProcessingSystem` and `BallisticsSystem` access `GlobalTime` via `HasSingleton` before reading `FrameNumber`. This allows the systems to function in minimal test worlds that do not register `GlobalTime`. Without `GlobalTime`, `SpawnTick = 0` and `currentTick = 0`, so the lifetime check (`currentTick - SpawnTick >= BulletLifetimeTicks`) never triggers prematurely (both stay at 0 → difference is 0). Test scaffolding can explicitly inject a `GlobalTime` singleton to control tick advancement.

### `BallisticsSystem` destroys-then-continues pattern

When a bullet's lifetime expires, `World.DestroyEntity(entity)` is called and `continue` skips the raycast submission and `PreviousPosition` update for that entity. This is correct: a destroyed entity must not write to any component storage (undefined behaviour). The `EntityQuery` iterator is safe under mid-iteration destruction in the FDP kernel (confirmed by existing test patterns in `HitResolutionSystem` and the kernel tests).

### No `VehicleState` references

Confirmed: no `VehicleState` type is referenced anywhere in the three new systems or their tests.

### `CombatConstants` raw-literal discipline

All numeric constants used in production code come from `CombatConstants` or `PhysicsConstants`. The only numeric literals in the new systems are `0f` (zero comparison/clamp) and `0u` (fallback tick when `GlobalTime` absent). No raw damage, radius, layer, or lifetime literals appear in the system bodies.

---

## Success Criteria Checklist

- [x] `FireProcessingSystem` — spawns bullet entities from `FireRequestEvent`; **5 tests pass**
- [x] `BallisticsSystem` — submits swept raycasts, updates `PreviousPosition`, lifetime culling; **6 tests pass**
- [x] `DamageSystem` — applies damage from `HitEvent`, destroys on zero health; **5 tests pass**
- [x] Full solution: **0 errors**
- [x] `FDP.Toolkit.Combat.Tests`: **27 / 27 tests green**
- [x] `FDP.Toolkit.Physics.Tests`: **16 / 16 tests green** (no regression from `HitEvent` move)
- [x] Report submitted ✅
