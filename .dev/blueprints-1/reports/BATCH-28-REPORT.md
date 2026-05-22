# BATCH-28 Report

**Batch:** BATCH-28  
**Status:** APPROVED  
**Tasks completed:** CT0 (DEBT-019 fix), Task 1 (TASK-TRACKER housekeeping), Task 2 (DEBT-TRACKER housekeeping)

---

## Files Modified

### New file
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/DebugProbeCollection.cs` (created)

### CT0 -- [Collection("DebugProbe")] added to 27 test classes
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/AlcUnloadTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixtureTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/MockDispatcherSystemTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Demos/DoorActorDoorSensorDemoTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Demos/HasVisibleTargetDemoTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Demos/HealthRegenDemoTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Demos/LibraryMathDemoTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Demos/MoveToAndFireDemoTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/Coordinator/AlcLifecycleTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/Coordinator/FailureRollbackTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/Coordinator/QuickReloadTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/Coordinator/RegistrarInjectionTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/PdbLoading/PdbLoadTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/RuntimeIntegration/AiPrimitiveReloadTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/RuntimeIntegration/HardReloadTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/RuntimeIntegration/LatentCursorReloadTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/RuntimeIntegration/SoftReloadTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockContractTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/AllocationFreeTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintMaintenanceSystem/TierUpgrade_1024_to_4096_Tests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintMaintenanceSystem/TwoFrameUpgradeTimingTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/PhaseOrderingTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/ReloadReconciliationTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/SingleSlotTickTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/WorldSingletonTickTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/DebugSessionInterfaceTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/ProbeDispatchTests.cs`

### CT0 -- Dispose() reset added
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`
  - Added `DebugProbe.Sink = NullProbeSink.Instance;` as first line of `Dispose()`

### Task 1 -- TASK-TRACKER housekeeping
- `.dev/blueprints-1/TASK-TRACKER.md`
  - TASK-HR-001: `[ ]` -> `[x]`
  - TASK-HR-002: `[ ]` -> `[x]`
  - TASK-HR-003: `[ ]` -> `[x]`

### Task 2 -- DEBT-TRACKER housekeeping
- `.dev/blueprints-1/DEBT-TRACKER.md`
  - DEBT-019 Status: `OPEN` -> `RESOLVED (BATCH-28)`
  - DEBT-023 row appended

---

## Test Results

**Run 1:**
```
Passed!  - Failed: 0, Passed: 490, Skipped: 7, Total: 497, Duration: 14 s
```

**Run 2:**
```
Passed!  - Failed: 0, Passed: 490, Skipped: 7, Total: 497, Duration: 14 s
```

Both runs: 0 failed. The previously flaky
`BlueprintTestFixtureTests.Constructor_InitializesAllProperties` test no longer races.

---

## Deviations

None. All changes follow the instructions exactly.
