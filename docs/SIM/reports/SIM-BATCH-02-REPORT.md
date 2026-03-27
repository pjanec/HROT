# SIM-BATCH-02 Report

**Batch:** SIM-BATCH-02  
**Task:** TASK-S4.1  
**Developer:** GitHub Copilot  
**Date:** 2026-02-25  
**Status:** ✅ COMPLETE

---

## Summary

Wired `FDP.Toolkit.Behavior` and `FDP.Toolkit.Navigation` systems into a new `SimulationLogicModule`, added empty stubs for `MissionAdapterSystem` (S4.3) and `JoinFormationExecutor` (S4.4), and verified the system topology with an empty-world integration test.

---

## Changes Made

### New Files

| File | Description |
|------|-------------|
| `Bagira.SimHost/Modules/SimulationLogicModule.cs` | Core module; constructor accepts `DoctrineRegistry` + `NetworkEntityMap`; `RegisterSystems(SystemGroup)` registers all 9 systems in strict spec order |
| `Bagira.SimHost/Systems/MissionAdapterSystem.cs` | Empty `ComponentSystem` stub — full implementation deferred to TASK-S4.3 |
| `Bagira.SimHost/Systems/JoinFormationExecutor.cs` | `IActionExecutor<LocomotionChannel>` stub — full implementation deferred to TASK-S4.4 |
| `Bagira.SimHost.Tests/SimulationLogicModuleTests.cs` | 2 xUnit tests verifying empty-world topology and `LinearKinematicsSystem` presence |

### Modified Files

| File | Change |
|------|--------|
| `Bagira.SimHost/Bagira.SimHost.csproj` | Added `FDP.Toolkit.Behavior`, `FDP.Toolkit.Navigation`, `FDP.Toolkit.Physics` project references |
| `Bagira.SimHost.Tests/Bagira.SimHost.Tests.csproj` | Added `FDP.Toolkit.Behavior`, `FDP.Toolkit.Physics` project references |

---

## System Registration Order (as implemented)

1. `MissionAdapterSystem(_doctrineRegistry, _entityMap)` — stub, S4.3
2. `ChannelArbitrationSystem()` — preempts stale channels
3. `BTreeTickSystem(_doctrineRegistry)` — zero-alloc BTree tick
4. `LocomotionDispatcherSystem()` + `MoveToExecutor` + `FollowRouteExecutor` (executors); `JoinFormationExecutor` commented-out stub (S4.4)
5. `SpatialHashSystem()` — builds spatial grid each frame
6. `FormationTargetSystem(_formationTemplateManager, _trajectoryPool)` — formation slot targets
7. `VehicleCommandSystem()` — high-level command events
8. `CarKinematicsSystem(_roadNetwork, _trajectoryPool)` — wheeled/tracked physics
9. `LinearKinematicsSystem()` — non-wheeled position integration

---

## Test Results

```
Passed!  - Failed: 0, Passed: 22, Skipped: 0, Total: 22
```

All 22 tests pass including both new tests:
- `SimulationLogicModule_EmptyWorld_AllSystemsRegisterAndUpdateWithoutException`
- `SimulationLogicModule_ContainsLinearKinematicsSystem`

---

## Report Questions

**Q1 Initialization Blockers:** Did you need to construct any mock arguments to satisfy system constructors?

Yes. `CarKinematicsSystem` requires a `RoadNetworkBlob` and `TrajectoryPoolManager`. For tests and default use, an empty road network (`new RoadNetworkBuilder().Build(10f, 10, 10)`) and a fresh `TrajectoryPoolManager()` are used. `SimulationLogicModule` creates these internally when not supplied by the caller (i.e., when `null` is passed for optional parameters). `FormationTemplateManager` is similarly auto-created with default templates.

**Q2 Structure Concerns:** Is `SimulationLogicModule` getting too bloated? Would you recommend breaking it down further for clarity?

Not yet — 9 systems is still manageable in a single `RegisterSystems` method. If S4.3 and S4.4 add significant initialization logic inside the stubs, it may be worth extracting `BehaviorSubModule` (items 1–4) and `PhysicsSubModule` (items 5–9) as separate classes, but that split can wait until the stubs are filled in.

**Q3 Stubs:** Which specific empty stubs did you have to create?

- **`MissionAdapterSystem`**: Created as a `ComponentSystem` subclass with a `DoctrineRegistry` + `NetworkEntityMap` constructor and an empty `OnUpdate()`. Marked `[UpdateInGroup(typeof(SimulationSystemGroup))]`. No queries, no component access.

- **`JoinFormationExecutor`**: Created as a `sealed class` implementing `IActionExecutor<LocomotionChannel>` with `VehicleAPI?` + `NetworkEntityMap` constructor. All three interface methods (`OnEnter`, `Execute`, `OnExit`) are no-ops. Its registration in `LocomotionDispatcherSystem` is left commented out pending `ActionIdJoinFormation` constant definition in `NavigationConstants` (S4.4).

---

## Notes

- `LinearKinematicsSystem` carries `[UpdateBefore(typeof(SpatialHashSystem))]`. In the flat `SystemGroup`, the topological sort places it before `SpatialHashSystem`, which is correct: linear entities update their positions before the hash grid is rebuilt for the next frame. This is by design (see `LinearKinematicsSystem` XML doc).
- The test registers 13 component types to satisfy all system queries on the empty world. No entities are created — all queries return zero results. The test's primary assertion is that the sort completes without a cycle exception and `group.Run()` does not throw.
