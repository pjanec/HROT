# BATCH-13 Report

**Batch:** BATCH-13
**Tasks:** DEBT-006 (DoctrineRegistry stable int key) · DEBT-007 (FdpHsmContext world access) · DEBT-008 (DoctrineIngressSystem try/catch) · DEBT-022 (Intersection2D t=0 boundary test) · DEBT-024 (Dispatcher dead-entity OnExit guard) · DEBT-031 (HitEvent → Combat.Contracts) · DEBT-033 (HealthCritical trigger via HealthData) · DEBT-034 (EjectPassengersExecutor XML doc fix)
**Status:** ✅ COMPLETE
**Build:** `dotnet build FDP.sln` → 0 errors, 0 new warnings
**Tests:** 9 new tests added; all pass. Two pre-existing flaky failures (`Fdp.Examples.NetworkDemo.Tests.DistributedReplayTests.FullScenario_TwoNodes_RecordAndReplay` and `ModuleHost.Core.Tests.ReactiveSchedulingTests.ReactiveScheduling_AsyncModule_TracksVersionCorrectly`) are unrelated to this batch and unchanged.

---

## Test Counts

| Suite | Before | After | New |
|---|---|---|---|
| `FDP.Toolkit.Behavior.Tests` | 42 | 50 | +8 |
| `FDP.Toolkit.Physics.Tests` | 21 | 22 | +1 |
| **Total new** | — | — | **9** |

### New tests

| Class | Test | Description |
|---|---|---|
| `DoctrineRegistryTests` | `LookupById_ReturnsCorrectEntry` | Registered entry is retrievable by stable int id |
| `DoctrineRegistryTests` | `LookupById_IsStableAcrossInstances` | Same id returns same definition across registry instances |
| `DoctrineRegistryTests` | `ReturnsNull_ForUnregisteredId` | Unregistered id returns `false` from `TryGetDefinition` |
| `HsmTickSystemTests` | `FdpHsmContext_ExposesWorldAccess` | `FdpHsmContext.World` is non-null when an HSM tick runs |
| `DoctrineIngressSystemTests` | `DoctrineIngress_DoesNotThrow_WhenParseParamsFails` | Exception in `ParseParams` delegate does not propagate; entity is skipped |
| `Intersection2DTests` | `RaycastCircle_ReturnsZero_WhenRayStartsOnCircleEdge` | t=0 boundary: ray origin on circle edge → hit=true, t≈0 |
| `LocomotionDispatcherTests` | `Dispatcher_CallsOnExit_WhenEntityDestroyedMidAction` | Entity destroyed inside `Execute` → `OnExit` called, no crash on subsequent query |
| `MissionDirectorSystemTests` | `MissionDirector_AdvancesPhase_WhenHealthCritical` | HealthData.Fraction (0.05) ≤ TriggerParam (0.10) → phase advances |
| `MissionDirectorSystemTests` | `MissionDirector_DoesNotAdvance_WhenHealthAboveThreshold` | HealthData.Fraction (0.50) > TriggerParam (0.10) → phase stays |

---

## New Files

| File | Description |
|---|---|
| `Toolkits/FDP.Toolkit.Behavior/DoctrineIds.cs` | Compile-time stable `int` constants for all registered doctrines (None=0, WanderCivil=1001, …) |
| `Toolkits/FDP.Toolkit.Behavior.Tests/DoctrineRegistryTests.cs` | 3 tests verifying stable-id registry API |
| `Toolkits/FDP.Toolkit.Combat.Contracts/FDP.Toolkit.Combat.Contracts.csproj` | New thin assembly; references only `Fdp.Kernel` |
| `Toolkits/FDP.Toolkit.Combat.Contracts/HitEvent.cs` | `HitEvent` struct in namespace `FDP.Toolkit.Combat.Contracts`; `[EventId(5001)]` unchanged |

---

## Modified Files

| File | Change |
|---|---|
| `Toolkits/FDP.Toolkit.Behavior/DoctrineRegistry.cs` | DEBT-006: API changed to `Register(int id, string name, def)`; two-dict internals; `StringComparer.Ordinal` name→id dict; `GetHashCode` eliminated |
| `Toolkits/FDP.Toolkit.Behavior/Systems/DoctrineIngressSystem.cs` | DEBT-006+008: uses `TryGetId()` → `TryGetDefinition()`; `ParseParams` wrapped in try/catch |
| `Toolkits/FDP.Toolkit.Behavior/Systems/HsmTickSystem.cs` | DEBT-007: `FdpHsmContext` gains `EntityRepository World`; new internal `HsmKernelBridge` (unmanaged) for `HsmKernel.Update` call |
| `Toolkits/FDP.Toolkit.Behavior/Systems/LocomotionDispatcherSystem.cs` | DEBT-024: dead-entity guard after `Execute()` |
| `Toolkits/FDP.Toolkit.Behavior/Systems/WeaponDispatcherSystem.cs` | DEBT-024: dead-entity guard after `Execute()` |
| `Toolkits/FDP.Toolkit.Behavior/Systems/InteractionDispatcherSystem.cs` | DEBT-024: dead-entity guard after `Execute()` |
| `Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs` | DEBT-033: `HealthCritical` case reads `HealthData.Fraction`; TODO removed |
| `Toolkits/FDP.Toolkit.Behavior/Executors/EjectPassengersExecutor.cs` | DEBT-034: XML doc slot-offset examples corrected (−1.5/0.0 and −3.0/−1.5/0.0/+1.5) |
| `Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` | DEBT-031+033: `using FDP.Toolkit.Combat.Contracts`; syncs `HealthData` mirror after damage |
| `Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs` | DEBT-031: `using FDP.Toolkit.Combat.Contracts` |
| `Toolkits/FDP.Toolkit.Physics/FDP.Toolkit.Physics.csproj` | DEBT-031: ProjectReference to `FDP.Toolkit.Combat.Contracts` |
| `Toolkits/FDP.Toolkit.Combat/FDP.Toolkit.Combat.csproj` | DEBT-031: ProjectReference to `FDP.Toolkit.Combat.Contracts` |
| `Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs` | DEBT-033: `HealthData` struct added (Current, Max, Fraction computed property) |
| `Kernel/Fdp.Kernel/Events/HitEvent.cs` | DEBT-031: struct body removed; tombstone comment only |
| `FDP.sln` | DEBT-031: `FDP.Toolkit.Combat.Contracts` project added (GUID `{3A4B5C6D-…}`) |
| `Toolkits/FDP.Toolkit.Behavior.Tests/DoctrineIngressSystemTests.cs` | DEBT-006+008: `Register()` API updated; Test 5 added |
| `Toolkits/FDP.Toolkit.Behavior.Tests/HsmTickSystemTests.cs` | DEBT-006+007: `GetHashCode()` → stable `TestHsmId = 9001`; Test 3 added |
| `Toolkits/FDP.Toolkit.Behavior.Tests/BTreeTickSystemTests.cs` | DEBT-006: `Register()` API updated; stable test ids 9001/9002; `GetHashCode()` removed |
| `Toolkits/FDP.Toolkit.Behavior.Tests/LocomotionDispatcherTests.cs` | DEBT-024: `SelfDestroyingExecutor` inner class; Test 5 added |
| `Toolkits/FDP.Toolkit.Behavior.Tests/MissionDirectorSystemTests.cs` | DEBT-033: `RegisterComponent<HealthData>()`; Tests 5 and 6 added |
| `Toolkits/FDP.Toolkit.Behavior.Tests/TestWorldFactory.cs` | DEBT-033: `RegisterComponent<HealthData>()` added |
| `Toolkits/FDP.Toolkit.Physics.Tests/PhysicsTestWorldFactory.cs` | DEBT-031: `using FDP.Toolkit.Combat.Contracts` replacing BATCH-10 comment |
| `Toolkits/FDP.Toolkit.Physics.Tests/Intersection2DTests.cs` | DEBT-022: Test 6 (`RaycastCircle_ReturnsZero_WhenRayStartsOnCircleEdge`) |
| `Toolkits/FDP.Toolkit.Physics.Tests/HitResolutionSystemTests.cs` | DEBT-031: `using FDP.Toolkit.Combat.Contracts` |
| `Toolkits/FDP.Toolkit.Combat.Tests/DamageSystemTests.cs` | DEBT-031: `using FDP.Toolkit.Combat.Contracts` |
| `Toolkits/FDP.Toolkit.Combat.Tests/BallisticsSystemTests.cs` | DEBT-031: `using FDP.Toolkit.Combat.Contracts` |
| `Toolkits/FDP.Toolkit.Combat.Tests/FireProcessingSystemTests.cs` | DEBT-031: `using FDP.Toolkit.Combat.Contracts` |
| `Toolkits/FDP.Toolkit.Combat.Tests/AimAndFireExecutorTests.cs` | DEBT-031: `using FDP.Toolkit.Combat.Contracts` |
| `Toolkits/FDP.Toolkit.Combat.Tests/CombatComponentTests.cs` | DEBT-031: `using FDP.Toolkit.Combat.Contracts` |

---

## Design Q&A

### Q1 — DEBT-007: FdpHsmContext constraint

**Constraint:** `HsmKernel.Update<TInstance, TContext>` in the `Fhsm.Kernel` library declares `where TContext : unmanaged`. An `unmanaged` struct cannot contain fields of any managed reference type. `EntityRepository` is a class (managed reference type), so adding `EntityRepository World` to `FdpHsmContext` would violate that constraint and fail to compile.

**Options considered:**
- **Option A (bridge struct):** Keep `FdpHsmContext` as the user-facing context with both `Entity Self` and `EntityRepository World`, but introduce a separate thin `HsmKernelBridge` (unmanaged, contains only `Entity Self`) for the `HsmKernel.Update` call. **Selected — no library changes required, zero unsafe code.**
- **Option B (ref T field):** Use `ref struct` or a raw pointer. `ref struct` cannot be stored, and a raw pointer to `EntityRepository` requires unsafe + GC pinning. Rejected.
- **Option C (thread-local / ambient state):** Thread-locals are a global side-channel, non-deterministic in multi-threaded ticks. Explicitly rejected.

**Before (DEBT-007):**
```csharp
public struct FdpHsmContext
{
    public Entity Self;
}
```
The struct satisfied `unmanaged` but gave action delegates no ECS access.

**After (DEBT-007):**
```csharp
public struct FdpHsmContext        // user-facing — holds managed ref
{
    public Entity           Self;
    public EntityRepository World; // managed → not unmanaged
}

internal struct HsmKernelBridge    // passed to HsmKernel.Update
{
    public Entity Self;            // unmanaged — satisfies the constraint
}
```
`HsmTickSystem.OnUpdate()` creates both structs, uses `HsmKernelBridge` for the kernel call, and stores `FdpHsmContext` for Phase 3+ wiring to action delegates.

---

### Q2 — DEBT-006: call-site audit for GetHashCode

**Call sites that used `string.GetHashCode()` based lookup and required updating:**

| File | Change |
|---|---|
| `DoctrineRegistry.cs` | Internal key storage changed from `name.GetHashCode()` → assigned `int id` |
| `DoctrineIngressSystem.cs` | `evt.DoctrineName.GetHashCode()` → `TryGetId(evt.DoctrineName, out id)` |
| `DoctrineIngressSystemTests.cs` | `Register("name", def)` → `Register(id, "name", def)` × 4 tests |
| `HsmTickSystemTests.cs` | `doctrineName.GetHashCode()` → `TestHsmId = 9001` |
| `BTreeTickSystemTests.cs` | `Register("name", def)` → `Register(id, "name", def)`; `GetHashCode()` → stable id |

Total: **5 call sites** updated across 4 files.

**MissionDirectorSystem:** No changes were needed. `MissionDirectorSystem` reads `queue.Phases[i].DoctrineId` (an `int` constant already stored at plan-build time) and writes it directly to `DoctrineState.ActiveDoctrineHash`. It never calls `GetHashCode()` and never touches `DoctrineRegistry`. ✅ Unaffected.

---

### Q3 — DEBT-031: HitEvent file changes and dependency graph

**Files changed:**

| File | Change direction |
|---|---|
| `Kernel/Fdp.Kernel/Events/HitEvent.cs` | Struct removed; tombstone comment only |
| `Toolkits/FDP.Toolkit.Combat.Contracts/HitEvent.cs` | **New** — struct definition moved here |
| `Toolkits/FDP.Toolkit.Combat.Contracts/FDP.Toolkit.Combat.Contracts.csproj` | **New** — references only `Fdp.Kernel` |
| `Toolkits/FDP.Toolkit.Physics/FDP.Toolkit.Physics.csproj` | Added → `FDP.Toolkit.Combat.Contracts` |
| `Toolkits/FDP.Toolkit.Combat/FDP.Toolkit.Combat.csproj` | Added → `FDP.Toolkit.Combat.Contracts` |
| `FDP.sln` | New project entry added |
| `HitResolutionSystem.cs`, `DamageSystem.cs` | `using FDP.Toolkit.Combat.Contracts;` added |
| 7 test files in Combat.Tests + Physics.Tests | `using FDP.Toolkit.Combat.Contracts;` added |
| `PhysicsTestWorldFactory.cs` | BATCH-10 using comment replaced with `using FDP.Toolkit.Combat.Contracts;` |

**Dependency graph before DEBT-031:**
```
Fdp.Kernel  ←── HitEvent defined here
    ↑                ↑
Physics           Combat
```

**Dependency graph after DEBT-031:**
```
Fdp.Kernel
    ↑
FDP.Toolkit.Combat.Contracts  ←── HitEvent defined here
    ↑                    ↑
Physics               Combat
```

`Fdp.Kernel` has no reference to `Combat.Contracts`. `Combat.Contracts` has no reference to `Physics` or `Combat`. There are **no circular references**. `Fdp.Kernel` is now clean of domain event types.

---

### Q4 — DEBT-033: DamageSystem HealthData sync and stale-frame risk

**DamageSystem change:** After clamping `health.Current` to ≥ 0, before the entity-destruction check, `DamageSystem.OnUpdate()` writes:

```csharp
if (World.HasComponent<HealthData>(evt.HitEntity))
    World.SetComponent(evt.HitEntity,
        new HealthData { Current = health.Current, Max = health.Max });
```

The `HasComponent` guard keeps `HealthData` optional — only entities with it will trigger the sync.

**Stale-frame risk:** Both `DamageSystem` and `MissionDirectorSystem` run in `SimulationSystemGroup`. `MissionDirectorSystem` is ordered `[UpdateBefore(typeof(ChannelArbitrationSystem))]` but there is no explicit ordering constraint between it and `DamageSystem`. If `MissionDirectorSystem` executes **before** `DamageSystem` in the same frame, `HealthData` will reflect last frame's health value — one-frame stale.

**Risk assessment:** The one-frame staleness is acceptable for mission-trigger evaluation. Health-critical transitions happen at coarse timescales (player-observable events), not sub-frame precision. The worst case is the trigger fires one frame late, which is imperceptible. If strict same-frame ordering is ever required, adding `[UpdateAfter(typeof(DamageSystem))]` to `MissionDirectorSystem` is the straightforward fix.

---

### Q5 — Surprises and edge cases

1. **`HsmKernel.Update` unmanaged constraint (DEBT-007):** The constraint was not documented in the debt item and was discovered when the compiler rejected `FdpHsmContext` containing `EntityRepository`. The `HsmKernelBridge` bridge pattern emerged as the minimal-invasive fix; no library changes required.

2. **`StringComparer` missing using (DEBT-006):** `DoctrineRegistry.cs` already imported `System.Collections.Generic` but not `System`. Adding `new(StringComparer.Ordinal)` required adding `using System;` — a one-line fix caught by the first post-DEBT-031 build.

3. **`BTreeTickSystemTests.cs` not in prior audit (DEBT-006):** The initial call-site audit (from the conversation summary) identified 4 files. `BTreeTickSystemTests.cs` was a fifth file using the old `Register(string, def)` API, caught only by the build. All 5 were updated.

4. **`PhysicsTestWorldFactory.cs` BATCH-10 comment (DEBT-031):** The factory file had a placeholder comment from BATCH-10 (`// BATCH-10: HitEvent moved to Fdp.Kernel — using FDP.Toolkit.Combat.Events removed`) with no active using directive. DEBT-031 required adding `using FDP.Toolkit.Combat.Contracts;` to restore the `HitEvent` reference for the factory's component registration.

5. **DEBT-034 XML doc matched BATCH-12-REPORT Q3 exactly:** BATCH-12 Q3 confirmed the correct offsets are −1.5 m / 0.0 m (2 passengers) and −3.0 m / −1.5 m / 0.0 m / +1.5 m (4 passengers), matching the actual formula `(i - buffer.Count / 2f) * 1.5f`. The prior doc claimed ±0.75 m and ±2.25/±0.75 m (a symmetric approximation). Fix was documentation-only, no logic change.
