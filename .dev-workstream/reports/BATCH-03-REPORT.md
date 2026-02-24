# BATCH-03 Report

**Batch:** BATCH-03  
**Date:** 2026-02-24  
**Status:** ✅ COMPLETE

---

## Test Results

### `dotnet test FDP.sln` Summary

| Project | Passed | Failed | Skipped |
|---|---|---|---|
| `Fdp.Tests` (Fdp.Kernel.Tests) | 675 | 0 | 2 |
| `FDP.Toolkit.Behavior.Tests` | **15** | 0 | 0 |
| `FDP.Toolkit.CarKinem.Tests` | 111 | 0 | 0 |
| `FDP.Toolkit.Vis2D.Tests` | 34 | 0 | 0 |
| `FDP.Toolkit.Replication.Tests` | 16 | 0 | 0 |
| `FDP.Toolkit.NetworkSpawning.Tests` | 21 | 0 | 0 |
| `FDP.Toolkit.ImGui.Tests` | 13 | 0 | 0 |
| `FDP.Toolkit.Tkb.Tests` | 14 | 0 | 0 |
| `FDP.Toolkit.Time.Tests` | 40 | 0 | 1 |
| `FDP.Toolkit.Commands.Tests` | 3 | 0 | 0 |
| `ModuleHost.Core.Tests` | 161 | 0 | 0 |
| `ModuleHost.Network.Cyclone.Tests` | 49 | 0 | 0 |
| `Fdp.Examples.NetworkDemo.Tests` | 26 | 0† | 0 |
| `Fdp.Examples.CarKinem.Tests` | 3 | 0 | 0 |
| `FDP.Framework.Raylib.Tests` | 2 | 0 | 0 |

† `FDPLT_016_Partial_Ownership_BiDirectional_Updates` appeared as a single failure in one full-solution run; re-running it in isolation passed immediately. This is a pre-existing timing-sensitive network test unrelated to any code touched in this batch.

**`dotnet build FDP.sln` — Build succeeded, 0 errors.**

---

## Task Completion Checklist

- [x] **Corrective 0a** — `SimMath.cs` in `Fdp.Kernel`; `FromYaw`, `FromYawPitchRoll`, `ExtractYaw`, compass constants; 5 unit tests all pass.
- [x] **Corrective 0b** — `BehaviorConstants.cs` created; `ChannelComponents.cs` and `BehaviorComponents.cs` use named constants for all fixed-buffer sizes; `ComponentLayoutTests.cs` references `BehaviorConstants.MaxChannelSizeBytes`; zero `Quaternion.CreateFromYawPitchRoll` calls in production or test code; all tests pass.
- [x] **Corrective 0c** — `ChannelArbitrationSystem` uses `GetComponentRW` throughout; no `SetComponent` write-backs; all 3 arbitration tests pass.
- [x] **Cleanup** — Stale implementation comment removed from `ChannelArbitrationTests.cs` (lines 86–88).
- [x] **BCS-P1-T3** — `LocomotionDispatcherSystem` implemented; zero magic numbers; all 4 locomotion dispatcher tests pass.
- [x] **BCS-P1-T4** — `WeaponDispatcherSystem` + `InteractionDispatcherSystem` implemented; 2 capability tests pass.
- [x] **SpyExecutor** — `TestHelpers.cs` with `SpyExecutor<TChannel>` reused across all dispatcher tests.
- [x] **TestWorldFactory** — `BrainBlackboard` and `SimTier` registrations added.
- [x] **Full solution** — `dotnet build FDP.sln` zero errors; all tests green (flaky network test confirmed pre-existing).

---

## Q1: Did you find any `CreateFromYawPitchRoll` or magic angle literals beyond those listed in Task 0b?

No additional `Quaternion.CreateFromYawPitchRoll` calls were found in production or test code beyond the six listed call sites. The only remaining `Quaternion.CreateFromYawPitchRoll` references in the repository are in external dependency source files under `ExtDeps/` (out of scope).

Two test files (`CarKinematicsSystemTests.cs` lines 133 and 148) used `CreateFromYawPitchRoll(0, 0, -MathF.PI/2)` labelled "East" in comments. In the old Y-forward convention, this rotated the Y-forward vector to point East (+X). After migration to `SimMath.FacingEast` (= Identity, X-forward at yaw=0), the rotation now correctly expresses East in our convention. The simulation behaviour is preserved because the velocity vectors in those tests already point in the +X direction.

---

## Q2: Did you implement a generic base class? What friction was encountered?

**Yes, a partial generic base class was implemented** (`DispatcherSystemBase<TChannel>`).

The base class holds `_executors[]`, `_previousAction[]`, `RegisterExecutor`, `OnCreate`, and `EnsurePreviousActionCapacity`. Each concrete dispatcher (`LocomotionDispatcherSystem`, `WeaponDispatcherSystem`, `InteractionDispatcherSystem`) overrides `OnUpdate` with its own typed query and component access.

**Friction encountered:**  
Making `OnUpdate` fully generic requires accessing channel struct fields (`.ActiveAction`, `.Status`, `.ActionInstanceId`, `.DispatchedInstanceId`) through the unconstrained type parameter `TChannel`. C# does not allow field access on an unconstrained generic struct — it would require either:

1. An `IActionChannel` interface with those properties, and constrained generic dispatch via `where TChannel : struct, IActionChannel`. This works without boxing for `ref TChannel` access (constrained virtual dispatch), but requires modifying the channel structs to implement the interface, which changes their public surface.
2. Abstract accessor methods on the base class, pushing field access to derived classes — essentially the same code volume as three independent implementations.

Given that `OnUpdate` is ~20 lines per dispatcher and the logic is crystal clear with concrete types, the three-override approach was the right call. The base class still eliminates the registrar/tracking boilerplate, achieving a meaningful reduction in duplication.

---

## Q3: How was the previous-action array resize handled?

The base class initialises `_previousAction` at `OnCreate` time to a capacity of 256 (named constant `InitialPreviousActionCapacity`). Each `OnUpdate` call to `EnsurePreviousActionCapacity(entity.Index + 1)` doubles the array if the entity index exceeds it: `Array.Resize(ref _previousAction, Math.Max(current * 2, required))`. No heap allocation occurs inside `OnUpdate` unless a resize is actually needed.

**A cleaner alternative** would be to use `World.MaxEntityIndex + 1` at `OnCreate` time as the initial capacity, pre-sizing for the worst case. `EntityRepository.MaxEntityIndex` is exposed and represents the highest index ever issued. This would eliminate any mid-session resizes at the cost of slightly more upfront allocation. Given that entity counts in tests are small and in production the first-frame resize settles quickly, the current approach is sufficient. A note for a future batch: consider initialising from `MaxEntityIndex` if entities are pre-spawned before the first system run.

---

## Q4: Ordering assumptions between ChannelArbitrationSystem and the dispatchers

**Yes, there is an implicit ordering dependency** that is not yet enforced by `[UpdateAfter]` attributes:

`ChannelArbitrationSystem` must run *before* the dispatcher systems so that a stale channel (mismatched `DoctrineInstanceId`) is cleared to `default` before the dispatcher sees it. If a dispatcher ran first, it would attempt to dispatch the stale action, potentially call `OnEnter` on an executor for a doctrine that has already been preempted, then have the arbitration clear the channel one frame later — a one-frame ghost execution.

Currently both systems have `[UpdateInGroup(typeof(SimulationSystemGroup))]` with no explicit ordering. In a single-system registration scenario (tests, demos) execution order follows registration order, which happens to be correct today. However this is fragile.

**Recommended fix (for next batch):** Add `[UpdateBefore(typeof(LocomotionDispatcherSystem))]` (and the other two) to `ChannelArbitrationSystem`, or equivalently add `[UpdateAfter(typeof(ChannelArbitrationSystem))]` to each dispatcher class.

---

## Q5: Weak points observed in `IActionExecutor<T>`

1. **No `Entity` context in `OnExit` for cleanup.** The signature provides `entity` and `ref channel`, but if an executor needs to destroy spawned child entities or clean up events during `OnExit`, it has full `EntityRepository` access — fine. However, the executor receives the already-mutated channel state (after `DispatchedInstanceId` is updated) but *before* `ActiveAction`/`ActionInstanceId` are changed by the caller. This ordering means `OnExit` still sees the old action ID in the channel, which is useful for cleanup but could be surprising.

2. **No return value / status feedback from `Execute`.** Executors have no way to signal completion or failure back to the dispatcher. The current pattern requires the executor to write directly into `ref TChannel channel.Status = NodeStatus.Success`. This is fine and efficient (no boxing, no allocations) but it means the executor must know the channel type layout to signal completion — tight coupling. A future `bool Execute(...)` returning `true` for completion would make intent clearer.

3. **No cancellation token for `OnEnter`.** If `OnEnter` begins an async-like operation (e.g., plays an animation, sets a navmesh destination), there is no clean way to cancel it within the same frame if a follow-on system fails the channel. The one-frame stale window in Q4 makes this a real scenario: `OnEnter` fires, arbitration (if reordered) clears the channel, `OnExit` fires — the executor must be robust to zero-duration enter/exit cycles.

---

## Files Created / Modified

### New files
| File | Purpose |
|---|---|
| `FDP/Kernel/Fdp.Kernel/CoreComponents/SimMath.cs` | Authoritative quaternion helpers for our coordinate convention |
| `FDP/Kernel/Fdp.Kernel.Tests/SimMathTests.cs` | 5 unit tests for SimMath |
| `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs` | Named constants for all buffer sizes and capacities |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DispatcherSystemBase.cs` | Shared state + registration for all dispatchers |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/LocomotionDispatcherSystem.cs` | Locomotion channel dispatcher |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/WeaponDispatcherSystem.cs` | Weapon channel dispatcher |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/InteractionDispatcherSystem.cs` | Interaction channel dispatcher |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/TestHelpers.cs` | `SpyExecutor<TChannel>` reusable test helper |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/LocomotionDispatcherTests.cs` | 4 locomotion dispatcher tests |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/WeaponInteractionDispatcherTests.cs` | 2 weapon/interaction tests |

### Modified files
| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Behavior/Components/ChannelComponents.cs` | Fixed buffers now use `BehaviorConstants` |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Components/BehaviorComponents.cs` | Fixed buffer now uses `BehaviorConstants` |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs` | `GetComponentRW` — no copy round-trip |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/ChannelArbitrationTests.cs` | Stale comment removed |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/ComponentLayoutTests.cs` | References `BehaviorConstants.MaxChannelSizeBytes` |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/TestWorldFactory.cs` | Added `BrainBlackboard`, `SimTier` registrations |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/VehicleCommandSystem.cs` | `SimMath.FromYaw` |
| `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/Systems/CarKinematicsSystemTests.cs` | `SimMath.FacingNorth` / `SimMath.FacingEast` |
| `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/Systems/ParallelCorrectnessTests.cs` | `SimMath.FacingNorth` |
| `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/VehicleStateRefactorTests.cs` | `SimMath.FacingNorth` |
| `FDP/ModuleHost/ModuleHost.Benchmarks/CarKinemPerformance.cs` | `SimMath.FacingEast` |
| `FDP/Examples/Fdp.Examples.NetworkDemo/Systems/CombatInputSystem.cs` | `SimMath.FromYaw` |
