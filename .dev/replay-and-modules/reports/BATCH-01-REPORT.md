# BATCH-01 Report: Togglable Group Foundation

**Batch:** BATCH-01
**Tasks:** T-RMF-01 through T-RMF-05
**Status:** COMPLETE

---

## Task Status

| Task | Status | Notes |
|------|--------|-------|
| T-RMF-01 | Complete | `TogglableSimulationGroup.cs` created, implements `ISystemGroup`, `[UpdateInPhase(SystemPhase.Simulation)]` |
| T-RMF-02 | Complete | `TogglableInputGroup.cs` created, implements `ISystemGroup`, `[UpdateInPhase(SystemPhase.Input)]` |
| T-RMF-03 | Complete | `TogglablePostSimulationGroup.cs` created, implements `ISystemGroup`, `[UpdateInPhase(SystemPhase.PostSimulation)]`, XML doc explains physics integration hazard |
| T-RMF-04 | Complete | `ReferenceReplayLoadHandler` updated: three togglable group fields, updated constructor (7 params), `SetSystemsEnabled` toggles all four groups |
| T-RMF-05 | Complete | `NodeBootstrapper.BuildOrchestration` updated: three new togglable parameters, null-guard covers any of the four groups |

---

## Build Result

**0 errors. Build succeeded.**

Command: `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet`

---

## Test Results

| Project | Passed | Failed | Skipped |
|---------|--------|--------|---------|
| `Fdp.ModuleHost.Tests` | 189 | 0 | 0 |
| `Hrot.SimHost.Tests` | 460 | 0 | 3 |

The 3 skipped in `Hrot.SimHost.Tests` are pre-existing skips (unrelated to this batch).

New tests added: 9 (in `Fdp.ModuleHost.Tests/TogglableGroupTests.cs`):
- `TogglableSimulationGroup_WhenEnabled_ExecutesAllInnerSystems`
- `TogglableSimulationGroup_WhenDisabled_SkipsAllInnerSystems`
- `TogglableSimulationGroup_GetSystems_ReturnsAllInnerSystems`
- `TogglableInputGroup_WhenEnabled_ExecutesAllInnerSystems`
- `TogglableInputGroup_WhenDisabled_SkipsAllInnerSystems`
- `TogglableInputGroup_GetSystems_ReturnsAllInnerSystems`
- `TogglablePostSimulationGroup_WhenEnabled_ExecutesAllInnerSystems`
- `TogglablePostSimulationGroup_WhenDisabled_SkipsAllInnerSystems`
- `TogglablePostSimulationGroup_GetSystems_ReturnsAllInnerSystems`

---

## Files Created

- `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglableSimulationGroup.cs` (new)
- `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglableInputGroup.cs` (new)
- `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglablePostSimulationGroup.cs` (new)
- `FDP/Engine/Fdp.ModuleHost.Tests/TogglableGroupTests.cs` (new)

## Files Modified

- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs`
- `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/ReplayLoadClusterOpHandlerTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/LiveFromReplayTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/NodeBootstrapperReplayTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/FullBranchPipelineTests.cs`
- `Hrot/Subsystems/Hrot.ExCon/ExConSubsystem.cs`
- `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs`
- `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

---

## Issues Encountered and Resolutions

1. **Additional callers not listed in spec.** The spec mentioned test files and `NodeBootstrapper.cs` as callers, but the build revealed four production callers that also construct `ReferenceReplayLoadHandler` directly: `ExConSubsystem.cs`, `CgfApplication.cs`, `CgfSubsystem.cs`, and `IgApplication.cs`. All were using the old 5-parameter constructor with `simGroup: null`. Updated all four to pass `inputGroup: null, postSimGroup: null` as well.

2. **`SimHostApp.cs` used `new SimulationSystemGroup()`.** The old bootstrapper call in `SimHostApp.cs` created a `SimulationSystemGroup` and passed it as `simGroup`. This was replaced with `simGroup: null` per the Batch 03 deferred-wiring pattern.

---

## Design Decisions Beyond the Spec

- All three togglable group classes include both a `params IEcsModuleSystem[]` constructor and an `IReadOnlyList<IEcsModuleSystem>` overload, as specified in the batch instruction (for callers that have property lists). The `IReadOnlyList` overload copies elements into an internal array for consistent storage.
- `_innerSystems` is null-guarded in all constructors (`?? Array.Empty<>()`) to prevent null-reference exceptions when constructors are called with explicit `null` arguments.

---

## Suggested Commit Message

```
feat(rmf-01-05): togglable group foundation (BATCH-01)

T-RMF-01: Add TogglableSimulationGroup -- ISystemGroup, [UpdateInPhase(Simulation)]
T-RMF-02: Add TogglableInputGroup -- ISystemGroup, [UpdateInPhase(Input)]
T-RMF-03: Add TogglablePostSimulationGroup -- ISystemGroup, [UpdateInPhase(PostSimulation)]
          XML doc explains why physics integration must be disabled during replay
T-RMF-04: Update ReferenceReplayLoadHandler -- replace SimulationSystemGroup field
          with TogglableInputGroup + TogglableSimulationGroup + TogglablePostSimulationGroup;
          SetSystemsEnabled now toggles all four groups
T-RMF-05: Update NodeBootstrapper.BuildOrchestration -- replace SimulationSystemGroup?
          param with three togglable group params; null-guard covers any of the four groups

Also fix all callers of ReferenceReplayLoadHandler (ExConSubsystem, CgfApplication,
CgfSubsystem, IgApplication, SimHostApp) -- pass null for new params (Batch 03 wires real instances)
Update tests: ReplayLoadClusterOpHandlerTests, LiveFromReplayTests, NodeBootstrapperReplayTests,
FullBranchPipelineTests -- replace SimulationSystemGroup with TogglableSimulationGroup("test")

New tests: 9 in Fdp.ModuleHost.Tests/TogglableGroupTests.cs
  WhenEnabled/WhenDisabled/GetSystems for all three group types
Build: 0 errors. Fdp.ModuleHost.Tests: 189 passed. Hrot.SimHost.Tests: 460 passed (3 skipped).
```
