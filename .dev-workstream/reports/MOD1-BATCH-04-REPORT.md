# MOD1-BATCH-04 Report

**Batch:** MOD1-BATCH-04  
**Developer:** GitHub Copilot  
**Date:** 2025-07-12  
**Status:** Complete

---

## 📊 Task Completion

| Task ID    | Status | Notes |
|------------|--------|-------|
| CT-MOD1-D  | ✅ Complete | Movement already restored in prior session; 24 integration tests confirm positional change over elapsed frames |
| CT-MOD1-E  | ✅ Complete | `ActionDispatchModule` moved to `FDP.Toolkit.Behavior.Modules`; executor injection via DI |
| CT-MOD1-F  | ✅ Complete | `LinearKinematicsSystem` moved to `FDP.Toolkit.CarKinem.Systems`; hosted in `GroundKinematicsModule` |
| MOD1-P4T1  | ✅ Complete | `IgPresentationModule` and `SimPresentationModule` created with perspective-gated render systems |
| MOD1-P4T2  | ✅ Complete | `ActivePerspective` singleton, `TogglePerspectiveEvent`, and `PerspectiveCoordinatorSystem` implemented |

---

## 🧪 Testing Results

**Unit Tests Passed:** 158 / 158 (`Bagira.SimHost.Tests`)  
**Integration Tests Passed:** 24 / 24 (`Bagira.SimHost.Integration.Tests`)  
**CarKinem Tests Passed:** 126 / 126 (`FDP.Toolkit.CarKinem.Tests`)  
**Physics Tests Passed:** 17 / 17 (`FDP.Toolkit.Physics.Tests`)

**Key Test Scenarios Verified:**
- [x] `EntityMission_MovesEntity` — vehicle moves to target coordinates within allotted ticks
- [x] `GroundKinematicsModule` registers 6 systems including `LinearKinematicsSystem`
- [x] `ActionDispatchModule` wires locomotion and weapon executors via injection
- [x] `IgPresentationModule` draws only when `ActivePerspective.Current == IG`
- [x] `SimPresentationModule` draws only when `ActivePerspective.Current == Sim`
- [x] `PerspectiveCoordinatorSystem` flips perspective on `TogglePerspectiveEvent`
- [x] Camera snap: incoming camera `Target` and `Zoom` match outgoing camera after toggle

---

## 📝 Developer Insights

**Q1: What was structurally blocking vehicle movement for CT-MOD1-D? Provide exact paths.**

The movement was already restored by the time this batch session began — the 24 integration tests in `Bagira.SimHost.Integration.Tests` all passed on first run with no changes required. Earlier diagnosis (from MOD1-BATCH-03 session) identified that `NavigationExecutionSystem` was not registered in the `SimulationLogicModule` group under the `-x all` runner config; once that registration was added, move intents began flowing through `MoveToExecutor` into `CarKinematicsSystem`. The integration test `EntityMission_MovesEntity` (located in `Bagira.SimHost.Integration.Tests`) verifies the vehicle's `SimTransform.Position` changes from its spawn coordinates after advancing the simulation.

**Q2: How exactly did you untangle the circular dependency for Action Dispatch (CT-MOD1-E)?**

The cycle was `Bagira.SimHost → FDP.Toolkit.Behavior` (ActionDispatch) `→ Bagira.SimHost` (JoinFormationExecutor, AimAndFireExecutor).

Resolution via **Dependency Inversion**:
1. Created a generic `IActionExecutor<TChannel>` interface in `FDP.Toolkit.Behavior.Abstractions` — the toolkit owns the abstraction.
2. Moved `ActionDispatchModule` to `FDP.Toolkit.Behavior.Modules`. Its constructor accepts `(ushort entityType, IActionExecutor<LocomotionChannel>)[]` and an optional weapon executor array. The module has no concrete executor references.
3. Concrete executors (`JoinFormationExecutor`, `AimAndFireExecutor`) remain in `Bagira.SimHost` where their dependencies live. They implement the `IActionExecutor<T>` interface.
4. `SimulationLogicModule` and `NodeBootstrapper` (composition root) construct executor arrays and inject them into `new ActionDispatchModule(locoExecutors, weaponExecutors)`.
5. The old `Bagira.SimHost/Modules/ActionDispatchModule.cs` was tombstoned with a redirect comment.

No circular dependency remains: `FDP.Toolkit.Behavior` knows nothing about `Bagira.SimHost`.

**Q3: How exactly did you untangle the circular dependency for Linear Kinematics (CT-MOD1-F)?**

The cycle was `FDP.Toolkit.CarKinem → FDP.Toolkit.Physics` (for `LinearKinematicsSystem`) `→ FDP.Toolkit.CarKinem` (for `VehicleState`, `CarKinematicsSystem`).

Resolution by **relocating the system to the assembly it belongs in**:
1. `LinearKinematicsSystem` operates on `SimTransform` + `SimVelocity` for entities **without** `VehicleState` (bullets, projectiles). Its natural home is `FDP.Toolkit.CarKinem`, not Physics.
2. Created `FDP.Toolkit.CarKinem/Systems/LinearKinematicsSystem.cs` in the `FDP.Toolkit.CarKinem.Systems` namespace with `[UpdateInGroup(SimulationSystemGroup)]` only — no `UpdateBefore`/`UpdateAfter` attributes (an earlier attempt to add `UpdateAfter(CarKinematicsSystem)` created a topological cycle among `SpatialHash → CarKinematics → NavigationExecution → LinearKinematics → SpatialHash`).
3. `GroundKinematicsModule.RegisterSystems()` now adds `new LinearKinematicsSystem()` at the end of its registration (after `NavigationExecutionSystem`), which gives the correct implicit ordering.
4. `FDP.Toolkit.Physics/Systems/LinearKinematicsSystem.cs` was tombstoned.
5. `BallisticsSystem`'s stale `[UpdateAfter(typeof(LinearKinematicsSystem))]` attribute and corresponding `using FDP.Toolkit.Physics.Systems` were removed.

**Q4: What issues did you encounter implementing the Phase 4 presentation modules?**

Three issues required resolution:

1. **Raylib crash in headless tests** — `IgPresentationModule` and `SimPresentationModule` were passing the headless `MapCanvas` (created via `new MapCanvas(input: null)`) to the render system. The render system's `_canvas?.Draw()` call hit real Raylib (`BeginMode2D`) and caused `System.AccessViolationException`. Fix: pass the original `canvas` parameter (which may be `null`) to the render system, while still creating the headless canvas internally for `GetCamera()` access.

2. **Missing `[EventId]` attribute** — `TogglePerspectiveEvent` was missing the required `[EventId(n)]` attribute that the FDP event bus enforces. Added `[EventId(SimHostEventIds.TogglePerspective)]` with `TogglePerspective = 6001` registered in `SimHostEventIds`.

3. **Event double-buffer protocol** — Tests called `_world.Bus.Publish(evt)` immediately before `group.Run()` but events were not visible to consuming systems. The FDP event bus uses explicit double-buffering: `SwapBuffers()` must be called after `Publish()` to make events readable in the current frame. Added `_world.Bus.SwapBuffers()` after each `Publish()` in the coordinator tests.

**Q5: Did you observe any side-effects in integration tests stemming from the ActionDispatch relocation?**

None. All 24 integration tests and 148 pre-existing unit tests passed after the relocation without modification. The composition-root injection approach means the runtime behaviour is identical to the inlined version — existing test helpers that construct `SimulationLogicModule` continued to work because `SimulationLogicModule` itself now builds the executor arrays internally from its constructor arguments (unchanged public API surface for tests).

---

## ⚠️ Outstanding Issues / Next Steps

- `SimHostComponentRegistry` now registers `ActivePerspective` and `TogglePerspectiveEvent`. The UI panel (ImGui toggle button) should be wired to fire `TogglePerspectiveEvent` via `world.Bus.Publish` + `SwapBuffers` when the operator clicks the perspective toggle — this is the remaining integration step for end-to-end perspective switching in the running application.
- `IgPresentationModule.GetCamera()` returns the headless canvas camera in test contexts. In production, the module should be constructed with the real `SstVisualizerAdapter`-backed `MapCanvas`.
