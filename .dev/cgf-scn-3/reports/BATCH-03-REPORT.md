# BATCH-03 Report — Phase 3/4 Editor System Group Wiring and Child Entity Remapping

**Batch:** BATCH-03
**Tasks:** S309, S310, S311, S312, S313
**Status:** COMPLETE

---

## Summary

All five Phase 3/4 tasks have been implemented. Build: succeeded (zero errors).
Tests: 456 passed in Hrot.SimHost.Tests (up from 455; +1 new S313 test),
4 new integration tests pass in Hrot.ClusterRunner.Integration.Tests (T-ES28–T-ES31),
0 failed, 3 skipped (pre-existing).

---

## Changes Made

### TASK-S309 — EditorSubsystem System Group Wiring

**File:** `Hrot\Subsystems\Hrot.Editor\EditorSubsystem.cs`

Replaced `_kernel.RegisterModule(simHostCorePack)` and `_kernel.RegisterModule(cgfLogicPackInst)` with
the three-group wiring pattern matching `CgfSubsystem`:

```csharp
var inputGroup   = new SystemGroup(); inputGroup.Create(_world);
var simGroup     = new SystemGroup(); simGroup.Create(_world);
var postSimGroup = new SystemGroup(); postSimGroup.Create(_world);
simHostCorePack.RegisterSystems(inputGroup, simGroup, postSimGroup);
cgfLogicPackInst.RegisterSystems(inputGroup, simGroup);
_kernel.RegisterGlobalSystem(new CgfInputGroupAdapter(inputGroup));
_kernel.RegisterModule(new SimGroupModule(simGroup));
_kernel.RegisterModule(new PostSimGroupModule(postSimGroup));
```

Added nested `SimGroupModule` and `PostSimGroupModule` IEcsModule implementations
(same pattern as `CgfSimGroupModule` in `CgfSubsystem.cs`).

Added `using Hrot.Common.Infrastructure;` import.

---

### TASK-S310 — EditorSubsystem MasterSyncController Replacement

**File:** `Hrot\Subsystems\Hrot.Editor\EditorSubsystem.cs`

- Replaced `private SteppingTimeController? _stepping;` with
  `private MasterSyncController? _timeController;`.
- Time controller initialization in `Initialize()`:
  ```csharp
  var timeConfig = new TimeControllerConfig { Role = TimeRole.Standalone };
  _timeController = (MasterSyncController)TimeControllerFactory.Create(_world.Bus, timeConfig);
  _kernel.SetTimeController(_timeController);
  _timeController.SwitchToDeterministic(new System.Collections.Generic.HashSet<int>());
  ```
- Removed `_stepping?.Step(deltaTime)` from `Update()`.
- Replaced `_stepping = null` with `_timeController = null` in `Shutdown()`.
- Added internal test accessors:
  ```csharp
  internal MasterSyncController TimeController => _timeController ?? throw ...;
  internal IPreviewController PreviewController => _previewController ?? throw ...;
  ```

---

### TASK-S311 — EditorPreviewController Time Mode Wiring

**File:** `Hrot\Subsystems\Hrot.Editor\EditorSubsystem.cs`

Added private nested `EditorPreviewController : IPreviewController`:

```csharp
public void EnterPreviewMode()
{
    _handler.TriggerLoadingPreview();
    _timeController.SwitchToContinuous();
    _inPreview = true;
}

public void ExitPreviewMode()
{
    _handler.TriggerUnloadingPreview();
    _timeController.SwitchToDeterministic(new HashSet<int>());
    _inPreview = false;
}
```

Updated `EditorPreviewController` construction site to pass `_timeController!`.

---

### TASK-S312 — EditorHarness Fix

**File:** `Hrot\Runner\Hrot.ClusterRunner.Integration.Tests\EditorHarness.cs`

Applied the same three-group wiring pattern as S309 to the offline test harness.
Added `SimGroupModule` and `PostSimGroupModule` nested classes
(Name="HarnessSimGroup" / "HarnessPostSimGroup").
Replaced `_stepping?.Step(...)` with `_timeController?.Step(...)` in `PumpFrames`/`PumpUntil`.
Added `using Hrot.Common.Infrastructure;` import.

---

### TASK-S313 — StagingEntityExtractor Child Entity Remapping

**File:** `Hrot\Subsystems\Hrot.CGF\Orchestration\StagingEntityExtractor.cs`

- Extracted new private static method `RemapComponentNetworkIds(comps, oldToNewMap, behaviorRemapper)`
  that handles both BehaviorParams patching and Intent DTO network ID remapping.
- Called for root entity components (was inline).
- Called for child entity components before assembling `overrideDict[kvp.Key]`.
- Supported Intent DTO types: `InitialPassengersIntent`, `InitialVehicleIntent`,
  `InitialHierarchyIntent`, `InitialRouteIntent`, `InitialTargetsIntent`.

---

## Tests Added

### EditorSubsystemBootTests.cs (T-ES28 through T-ES31)

| Test | Task | Description |
|------|------|-------------|
| `TimeController_AfterInit_IsInDeterministicMode` | S310 | Pumps Kernel.Update() until `GetMode() == Deterministic` (barrier crossing). |
| `KernelUpdate_WithoutStep_DoesNotThrow` | S309 | Calls `Kernel.Update()` once after Initialize(); asserts no exception. |
| `EnterPreviewMode_SwitchesTimeModeToContinuous` | S311 | After entering deterministic, enter preview; asserts `Continuous`. |
| `ExitPreviewMode_SwitchesTimeModeToDeterministic` | S311 | After exit preview; pumps until deterministic again; asserts `Deterministic`. |

Added `using Fdp.ModuleHost.Time;` and `using Hrot.UI.Common.Facades;` to the test file.

### StagingEntityExtractorTests.cs (Test 15)

| Test | Task | Description |
|------|------|-------------|
| `Extract_ChildEntity_InitialPassengersIntent_NetworkIdIsRemapped` | S313 | Child entity gets `InitialPassengersIntent` via translator; verifies PassengerNetworkIds are remapped. |

---

## Issues Encountered

1. **MasterSyncController barrier delay:** `GetMode()` returns `Continuous` immediately after
   `SwitchToDeterministic()` because the Future Barrier protocol uses a 200ms real-time lookahead.
   Tests T-ES28/T-ES30/T-ES31 use a poll loop (deadline = 3s) to wait for the barrier to be
   crossed before asserting.

2. **Passenger entity as extra root request:** In Test 15, the passenger entity (which provides
   the network ID to remap into the child's Intent DTO) has no `PartMetadata`, so it also
   becomes a root-level `EntityCreationRequest`. The assertion was updated to find the root
   entity request by `ChildComponentOverrides != null` and assert `Count == 2`.
