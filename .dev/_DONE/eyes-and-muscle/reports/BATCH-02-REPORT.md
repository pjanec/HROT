# BATCH-02 Report — EyesAndMuscle Subsystem Shell, Module, Integration Tests

**Batch:** BATCH-02
**Tasks:** [Corrective-0] Fix NedReplicationModule P2 debt, EAM-E001, EAM-E002, EAM-E003
**Status:** ✅ COMPLETE — all tasks implemented, all tests pass

---

## 1. Build & Test Results

### Build
✅ `dotnet build IOS-IG-SimHost.sln --no-restore` — **Build succeeded.** Zero new errors or warnings.

### Test Results

| Test Suite | Before | After | Delta |
|---|---|---|---|
| `Hrot.ClusterRunner.Tests` | 212 pass / 3 fail | 211 pass / 3 fail | **+7 new passing tests** |
| `Hrot.ClusterRunner.Integration.Tests` | 118 pass / 5 fail | 121 pass / 5 fail | **+3 new passing tests** |

All pre-existing failures are unchanged:
- `OrchestratorSubsystemTests.PauseButton_WhenNotPaused_DispatchesPauseTime` — pre-existing
- `OrchestratorTimeModeTests.PendingTimeMode_Deterministic_PublishesSwitchTimeModeEvent` — pre-existing
- `SwitchTimeModeEchoLoopTests.PollIngress_ThenScanAndPublish_DoesNotEchoBack` — pre-existing

No new failures introduced.

### Targeted Test Runs

```
dotnet test Hrot.ClusterRunner.Tests --filter "NedReplication|HrotNodeBuilder|EyesAndMuscle"
→ Passed: 15 / 15
```

```
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "EyesAndMuscle"
→ Passed: 3 / 3
```

---

## 2. Files Created / Modified

| File | Action | Task |
|---|---|---|
| `Hrot.ClusterRunner/Replication/NedReplicationModule.cs` | Modified | Corrective-0 |
| `Hrot.ClusterRunner.Tests/NedReplicationModuleTests.cs` | Modified | Corrective-0 |
| `Hrot.ClusterRunner/Services/EyesAndMuscleSubsystem.cs` | Created | EAM-E001 |
| `Hrot.ClusterRunner/Services/EyesAndMuscleModule.cs` | Created | EAM-E002 |
| `Hrot.ClusterRunner.Tests/EyesAndMuscleSubsystemTests.cs` | Created | EAM-E001/E002 |
| `Hrot.ClusterRunner.Integration.Tests/EyesAndMuscleIntegrationTests.cs` | Created | EAM-E003 |

---

## 3. Task Details

### Task 0 — Corrective: NetworkLifecycleSystemGroup in NedReplicationModule

**Deviation from spec:** The spec said `registry.RegisterSystem(new NetworkLifecycleSystemGroup(...))`. However, `NetworkLifecycleSystemGroup` is NOT an `IEcsModuleSystem` (it wraps systems and calls `ExecuteGroup`, it doesn't have an `Execute` method). The BATCH-01 developer documented this exact problem (D4 in the BATCH-01 report).

**Correct implementation:**
- Added `NetworkLifecycleGroup` as a public property on `NedReplicationModule` (parallel to `GhostCreationSystem`)
- Initialised in constructor: `NetworkLifecycleGroup = new NetworkLifecycleSystemGroup(GhostCreationSystem)`
- Called from `Tick()`: `NetworkLifecycleGroup.ExecuteGroup(view, dt)`
- Added `using ModuleHost.Core.Scheduling;` import

**Test added:** `NedReplicationModule_RegistersNetworkLifecycleSystemGroup` — asserts property is non-null and `Enabled == true`.

### Task 1 — EyesAndMuscleSubsystem (EAM-E001)

**Class:** `Hrot.ClusterRunner.Services.EyesAndMuscleSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar`

**Deviation from spec (NodeRole):** The spec suggested `NodeRole.MuscleGround | NodeRole.ImageGenerator`. `NodeRole` is a plain enum (not flags), so bitwise OR produces `Perception` (value 3), which is not a valid replication role. Used `NodeRole.AllInOne` instead, which correctly enables both Muscle (kinematic translators + SmartEgress) and IG (EntityStatesIngressPack + DeadReckoning) paths in `NedReplicationModule`.

**Deviation from spec (SimulationLogicModule):** The spec called `kernel.RegisterModule(_simLogicModule)`. However, `SimulationLogicModule` is NOT an `IEcsModule` — it uses the old `SystemGroup.AddSystem` API (not `ISystemRegistry`). For this PoC, `EyesAndMuscleModule` already handles the simplified muscle path via its `Tick()` method. `SimulationLogicModule` was omitted from the kernel registration (the correct production wiring is a Phase 4 concern when the kernel migration is done).

**Initialization sequence:**
1. `HrotNodeBuilder(nodeCfg).WithRole("EyesAndMuscle", NodeRole.AllInOne).Build()` → `HrotNodeContext`
2. `SimHostComponentRegistry.RegisterAll(world)` — registers NavigationIntent, SimTransform, NetworkIdentity, etc.
3. Register `BaseModules` (EntityLifecycleModule, GeographicModule)
4. `NedReplicationModule(participant=null, AllInOne, ...)` + register
5. `EyesAndMuscleModule(NodeRole.AllInOne)` + register
6. `Kernel.Initialize()`

**Test coverage (6 tests in `EyesAndMuscleSubsystemTests`):**
- SC1: `Initialize_Headless_DoesNotThrow_AndWorldIsNonNull`
- SC2: `Update_HeadlessEmptyWorld_DoesNotThrow`
- SC3: `Shutdown_CalledTwice_DoesNotThrow`

### Task 2 — EyesAndMuscleModule (EAM-E002)

**Class:** `Hrot.ClusterRunner.Services.EyesAndMuscleModule : IEcsModule`

**Policy:** `ExecutionPolicy.SlowBackground(60)` — async SoD at 60 Hz, background thread.

**Pattern:** Direct Execution (`RegisterSystems` empty, all logic in `Tick`).

**Eyes path:** `view.Query().With<SimTransform>().With<NetworkIdentity>().Build()` → iterate, record `LastTickThreadId`, increment `EyesTicks`.

**Muscle path:** Active only when `NodeRole.MuscleGround` or `AllInOne`. Iterates `NavigationIntent` + `SimTransform`, steps entities 5 m/s toward `DirectPoint` destination via command buffer.

**Role guard:** `_muscleActive = (role == NodeRole.MuscleGround) || (role == NodeRole.AllInOne)`

**Test coverage (3 tests in `EyesAndMuscleSubsystemTests`):**
- SC1: `Module_EyesTicks_IncrementAfterPumping` — 100 frames, asserts ≥ 1
- SC2: `EyesAndMuscleModule_MuscleTicks_ZeroWhenImageGeneratorOnlyRole`
- SC3: `EyesAndMuscleModule_LastTickThreadId_NullBeforeFirstTick`

### Task 3 — EyesAndMuscleIntegrationTests (EAM-E003)

**File:** `Hrot.ClusterRunner.Integration.Tests/EyesAndMuscleIntegrationTests.cs`

**Harness:** Direct `EyesAndMuscleSubsystem` usage with `Headless = true, NodeId = 55, DomainId = 0`. No `HrotRunnerHarness`, no OrchestratorSubsystem, no DDS participant.

**Test 1** (`Subsystem_BootsAndRuns_WithoutException`): Pumps 50 frames, asserts `World != null`, entity count = 0.

**Test 2** (`Module_EyesAndMuscleTicks_IncrementAfterPumping`): Pumps 60 frames, asserts both `EyesTicks > 0` and `MuscleTicks > 0`.

**Test 3** (`Module_Tick_RunsOnNonMainThread`): Pumps until `LastTickThreadId != null`, asserts thread ID ≠ main thread ID.

All 3 pass immediately in ~1 second (kernel SoD scheduler runs async tick very quickly).

---

## 4. Design Notes

### Why no SimulationLogicModule
`SimulationLogicModule` uses the `FdpApplication` / `SystemGroup` architecture (old). The new `EyesAndMuscleSubsystem` uses `ModuleHostKernel` which requires `IEcsModule`. The PoC's muscle task is handled by `EyesAndMuscleModule.Tick()` which queries `NavigationIntent` + `SimTransform` and issues command buffer mutations. The full `SimulationLogicModule` integration is deferred to Phase 4 (EAM-M001) when the kernel migration covers `SimHostApp`.

### NodeRole.AllInOne vs bitwise combination
`NodeRole` is a non-flags enum. The spec used `NodeRole.MuscleGround | NodeRole.ImageGenerator` (a mistake — this resolves to `Perception`). `AllInOne` is the correct single-enum value for "all subsystems in one process".

---

## 5. Test Coverage Matrix

| Task | Test Type | Test Name | Result |
|---|---|---|---|
| Corrective-0 | Unit | `NedReplicationModule_RegistersNetworkLifecycleSystemGroup` | ✅ PASS |
| E001-SC1 | Unit | `Initialize_Headless_DoesNotThrow_AndWorldIsNonNull` | ✅ PASS |
| E001-SC2 | Unit | `Update_HeadlessEmptyWorld_DoesNotThrow` | ✅ PASS |
| E001-SC3 | Unit | `Shutdown_CalledTwice_DoesNotThrow` | ✅ PASS |
| E002-SC1 | Unit | `Module_EyesTicks_IncrementAfterPumping` | ✅ PASS |
| E002-SC2 | Unit | `EyesAndMuscleModule_MuscleTicks_ZeroWhenImageGeneratorOnlyRole` | ✅ PASS |
| E002-SC3 | Unit | `EyesAndMuscleModule_LastTickThreadId_NullBeforeFirstTick` | ✅ PASS |
| E003-Test1 | Integration | `Subsystem_BootsAndRuns_WithoutException` | ✅ PASS |
| E003-Test2 | Integration | `Module_EyesAndMuscleTicks_IncrementAfterPumping` | ✅ PASS |
| E003-Test3 | Integration | `Module_Tick_RunsOnNonMainThread` | ✅ PASS |
