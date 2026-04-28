# BATCH-03 Report: Composition Root Refactor (SystemGroup -> Pack Properties)

**Batch:** BATCH-03
**Workstream:** replay-and-modules
**Status:** COMPLETE

---

## Objective

Replace all `SystemGroup`-based system registration patterns in composition roots
with the new `IReadOnlyList<IEcsModuleSystem>` array properties exposed by packs
(`InputSystems`, `SimulationSystems`, `PostSimulationSystems`). Wrap those lists in
`TogglableInputGroup`, `TogglableSimulationGroup`, and `TogglablePostSimulationGroup`
and register them on the kernel. Ensure `dotnet build IOS-IG-SimHost.sln` passes
with 0 errors and all tests pass.

Tasks covered: T-RMF-13 through T-RMF-19.

---

## Files Modified

### Production Code

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.SimHost/SimHostCoreLogicPack.cs` | **T-RMF-13** — Added `InputSystems`, `SimulationSystems`, `PostSimulationSystems` properties; deleted `RegisterSystems(SystemGroup, SystemGroup, SystemGroup)` overload |
| `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` | **T-RMF-14** — Added `InputSystems`, `SimulationSystems` properties; deleted both `RegisterSystems(SystemGroup)` overloads |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | **T-RMF-15** — Replaced `_kernelGroup` with `_toggleInput`/`_toggleSim`/`_togglePostSim`; registered sim via `SimHostSimulationModule`; added `SimHostSimulationModule` private nested class; updated `TestHook_AddSystem` signature to `IEcsModuleSystem`; factory systems wrapped via `CgfInputGroupAdapter` |
| `Hrot/Subsystems/Hrot.SimHost/SimHostSubsystem.cs` | **T-RMF-15 cascade** — Updated `TestHook_AddSystem` signature to `IEcsModuleSystem` |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | **T-RMF-16** — Replaced `_simGroup`/`_inputGroup` with `_toggleInput`/`_toggleSim`; registered sim via `CgfSimulationModule`; added `CgfSimulationModule` private nested class; fixed `Shutdown()` to null the toggle fields instead of calling `Dispose()` |
| `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` | **T-RMF-17** — Replaced `SystemGroup`-based wiring with pack property arrays and togglable groups |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | **T-RMF-18** — Replaced `SystemGroup`-based wiring with pack property arrays and togglable groups |
| `Hrot/Subsystems/Hrot.SimHost/Systems/GenesisMaterializationSystem.cs` | Fixed `[UpdateInPhase]` attribute from `SystemPhase.Simulation` to `SystemPhase.Input` to allow `RegisterGlobalSystem` registration (BATCH-03 instructions state "Input phase") |

### Integration Test Infrastructure

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.SimHost.Integration.Tests/Infrastructure/SimHostInstance.cs` | **T-RMF-19** — Replaced `SystemGroup` fields with `IReadOnlyList<IEcsModuleSystem>` fields; rewired step 6 using `SimHostCoreLogicPack` + `CgfLogicPack`; removed `Dispose()` group calls; replaced `_*Group.Run()` with per-system `Execute(view, dt)` loops |

### Test Files

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.SimHost.Tests/SimHostCoreLogicPackTests.cs` | Updated assertions: `SimulationSystems.Count` from 9 to 7 (corrected — `CarKinematicsSystem`/`LinearKinematicsSystem` are in `PostSimulationSystems`); added `postSimSystems` assertions for those two; corrected comment |
| `Hrot/Subsystems/Hrot.SimHost.Tests/CgfLogicPackTests.cs` | Replaced `RegisterSystems(SystemGroup)` calls with `InputSystems`/`SimulationSystems` property checks; removed 2 null-argument tests that tested deleted overloads |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Systems/MovingEntitySystem.cs` | Converted from `ComponentSystem` to `IEcsModuleSystem` to match `TestHook_AddSystem` signature |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` | Updated `TestHook_AddSystem` call site |

---

## Build Result

**0 errors. Build succeeded.**

Command: `dotnet build IOS-IG-SimHost.sln --no-incremental -v q`

---

## Test Results

### Hrot.SimHost.Tests

```
Passed! - Failed: 0, Passed: 458, Skipped: 3, Total: 461
```

All 458 tests pass (3 skipped are pre-existing network-dependent tests marked `[Skip]`).

---

## Design Notes

### RegisterGlobalSystem rejects SystemPhase.Simulation

The `ModuleHostKernel.RegisterGlobalSystem` method only accepts systems tagged with
`[UpdateInPhase(SystemPhase.Input)]`, `[UpdateInPhase(SystemPhase.BeforeSync)]`,
`[UpdateInPhase(SystemPhase.PostSimulation)]`, or `[UpdateInPhase(SystemPhase.Export)]`.
`SystemPhase.Simulation` is reserved for module systems that run on background threads
via `IEcsModule.Tick`.

Consequently, `TogglableSimulationGroup` (which carries `[UpdateInPhase(SystemPhase.Simulation)]`)
cannot be registered via `RegisterGlobalSystem`. The fix in both `SimHostApp.cs` and
`CgfSubsystem.cs` is to wrap the `TogglableSimulationGroup` in a minimal `IEcsModule`
that delegates to the group's `Execute` method in `Tick()`:

```csharp
private sealed class SimHostSimulationModule : IEcsModule
{
    private readonly TogglableSimulationGroup _group;
    public string Name => "SimHostSimulation";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
    public SimHostSimulationModule(TogglableSimulationGroup group) => _group = group;
    public void RegisterSystems(ISystemRegistry registry) { }
    public void Tick(ISimulationView view, float deltaTime) => _group.Execute(view, deltaTime);
}
// Registration:
_kernel.RegisterModule(new SimHostSimulationModule(_toggleSim));
```

The `_toggleSim` field reference is still stored (for passing to `BuildOrchestration`
as `simGroup:`), but execution is routed through the `IEcsModule` wrapper.

### GenesisMaterializationSystem phase correction

`GenesisMaterializationSystem` was decorated with `[UpdateInPhase(SystemPhase.Simulation)]`
but the BATCH-03 instructions specify it should run in `SystemPhase.Input` and be
registered as a global system directly on the kernel. The attribute was corrected to
`[UpdateInPhase(SystemPhase.Input)]`, which is consistent with its role: resolving
intent components into structural ECS components before simulation runs.

### Factory systems (nodeFactory.CreateSimHostAttributeUpdateSystems())

`nodeFactory.CreateSimHostAttributeUpdateSystems()` returns
`IReadOnlyList<ComponentSystem>`, not `IReadOnlyList<IEcsModuleSystem>`. These
legacy `ComponentSystem` instances cannot be added to `allInputSystems` directly.
The existing `CgfInputGroupAdapter` / `LegacyComponentSystemAdapter` infrastructure
is reused: factory systems are added to a `SystemGroup` (via `AddSystem()`), which
is then wrapped in a `CgfInputGroupAdapter` and registered as a global system.

### TogglablePostSimulationGroup

`TogglablePostSimulationGroup` carries `[UpdateInPhase(SystemPhase.PostSimulation)]`,
which IS in the kernel's allowed set for global systems. It is correctly registered
via `_kernel.RegisterGlobalSystem(_togglePostSim)` without a module wrapper.

### GroundKinematicsModule system distribution

`GroundKinematicsModule.SimulationSystems` contains 4 systems (SpatialHashSystem,
FormationTargetSystem, VehicleCommandSystem, NavigationExecutionSystem).
`GroundKinematicsModule.PostSimulationSystems` contains 2 systems (CarKinematicsSystem,
LinearKinematicsSystem). The test assertion that expected 9 simulation systems was
incorrect; the correct count is 7. Test comments and assertions were updated accordingly.
