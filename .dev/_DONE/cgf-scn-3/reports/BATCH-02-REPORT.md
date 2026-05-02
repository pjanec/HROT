# BATCH-02 Report — Phase 2 CGF Multi-Phase Architecture

**Batch:** BATCH-02  
**Tasks:** S305, S306, S307, S308  
**Status:** COMPLETE  

---

## Summary

All four Phase 2 tasks have been implemented. Build: succeeded (zero errors).
All 455 SimHost tests pass (3 skipped, 0 failed).

---

## Changes Made

### TASK-S305 — MissionControlModule Two-Group Registration Overload

**File:** `FDP\Toolkits\Fdp.Toolkits\Behavior\Modules\MissionControlModule.cs`

Added `using System;` and new overload:
```csharp
public void RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup)
{
    if (inputGroup == null) throw new ArgumentNullException(nameof(inputGroup));
    if (simGroup   == null) throw new ArgumentNullException(nameof(simGroup));
    inputGroup.AddSystem(new BehaviorIngressSystem(_registry));
    simGroup.AddSystem(new MissionDirectorSystem());
}
```

The existing single-group overload is unchanged.

---

### TASK-S306 — CgfLogicPack Two-Group Registration Overload

**File:** `Hrot\Subsystems\Hrot.CGF\CgfLogicPack.cs`

Added new overload:
- `inputGroup`: receives `_missionExecutionSystem` (MissionControlExecutionSystem) and
  `BehaviorIngressSystem` (via `_missionControlModule.RegisterSystems(inputGroup, simGroup)`).
- `simGroup`: receives all remaining systems — `_missionAdapterSystem`, `MissionDirectorSystem`,
  `HealthApplicationSystem`, `CgfThreatEvaluationSystem`, CognitiveRuntimeModule systems,
  ActionDispatchModule systems, `RouteContextSystem` (13 total).

**Tests updated/added** (`Hrot\Subsystems\Hrot.SimHost.Tests\CgfLogicPackTests.cs`):
- Corrected comment in existing `CgfLogicPack_EmptyWorld_AllSystemsRegisterAndRunWithoutException`
  (removed erroneous CreateEntityRequestSystem reference from comment).
- Added `Hrot.Common.Systems` using.
- `CgfLogicPack_TwoGroupOverload_RoutesSystemsCorrectly` — SC1 (MissionControlExecutionSystem
  in inputGroup), SC2 (BehaviorIngressSystem in inputGroup), SC3 (MissionDirectorSystem in
  simGroup), correct counts (2 in inputGroup, 13 in simGroup).
- `CgfLogicPack_SingleGroupOverload_StillAddsAllSystemsToOneGroup` — SC4 regression check.
- `CgfLogicPack_TwoGroupOverload_NullInputGroup_Throws` — SC5a.
- `CgfLogicPack_TwoGroupOverload_NullSimGroup_Throws` — SC5b.

---

### TASK-S307 — CgfInputGroupAdapter in Hrot.Common

**File created:** `Hrot\Engine\Hrot.Common\Infrastructure\CgfInputGroupAdapter.cs`

```csharp
[UpdateInPhase(SystemPhase.Input)]
public sealed class CgfInputGroupAdapter : IEcsModuleSystem
{
    private readonly SystemGroup _group;
    public CgfInputGroupAdapter(SystemGroup group) { ... }
    public void Execute(ISimulationView view, float deltaTime) { _group.Run(); }
}
```

Placed in `Hrot.Common.Infrastructure` namespace. No new project reference needed (Fdp.ModuleHost
is already a transitive dependency of Hrot.Common via Fdp.Network.Cyclone).

---

### TASK-S308 — CgfSubsystem Registration Update

**File:** `Hrot\Subsystems\Hrot.CGF\CgfSubsystem.cs`

- Added `private SystemGroup? _inputGroup;` field.
- In `Initialize()`: replaced single-group registration with two-group pattern:
  - `inputGroup` created, filled via `cgfLogicPack.RegisterSystems(inputGroup, simGroup)`.
  - `_context.Kernel.RegisterGlobalSystem(new CgfInputGroupAdapter(_inputGroup))` for Input phase.
  - `_context.Kernel.RegisterModule(new CgfSimGroupModule(_simGroup))` unchanged for Sim phase.
- In `Shutdown()`: `_inputGroup?.Dispose()` added before `_simGroup?.Dispose()`.

---

## Test Results

| Project | Passed | Failed | Skipped |
|---|---|---|---|
| `Hrot.SimHost.Tests` | 455 | 0 | 3 |

All new tests pass. No regressions.
