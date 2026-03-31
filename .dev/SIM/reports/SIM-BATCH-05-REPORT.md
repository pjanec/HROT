# SIM-BATCH-05-REPORT: Main Application Shell (Phase S5)

**Batch:** SIM-BATCH-05  
**Tasks:** TASK-S5.1, S5.2, S5.3, S5.4  
**Status:** ✅ COMPLETE  
**Test Result:** Passed! — Failed: 0, Passed: 50, Skipped: 0

---

## ✅ Completed Tasks

### TASK-S5.1: Implement Program.cs Entry Point

**File modified:** `Hrot.SimHost/Program.cs`

**Changes:**
- Created a `DoctrineRegistry` and registered all four SimHost doctrines using stable integer IDs from `SimHostDoctrineIds`:
  - `MoveTo_BT (3001)` → `"MoveToLocation"`, `BrainTier = BrainTierBTree`
  - `FollowRoute_BT (3002)` → `"FollowRoute"`, `BrainTier = BrainTierBTree`
  - `JoinFormation_BT (3003)` → `"JoinFormation"`, `BrainTier = BrainTierBTree`
  - `Idle_HSM (3010)` → `"Idle"`, `BrainTier = BrainTierHsm`
- Instantiated `SimulationLogicModule(doctrineRegistry, entityMap, vehicleAPI: null)`.
- Created a dedicated `SystemGroup`, called `group.Create(world)`, then `simLogicModule.RegisterSystems(kernelGroup)` — wiring all 9 systems into the ECS.
- Seeded `GlobalTime` singleton on the world before the loop (`DeltaTime = 1/Hz, TimeScale = 1.0`).
- Both `kernel.Update()` (handles time + modules + network) and `kernelGroup.Run()` (behavior / nav / physics) are called every frame.

**Note on VehicleAPI:** Registered as `null` (dummy). `JoinFormationExecutor` accepts a nullable `VehicleAPI` so this is safe for Phase S5. Full wiring deferred to a later phase.

**Note on Doctrine Interpreters:** BTree/HSM interpreter blobs are `null` for all four doctrines. `BTreeTickSystem` and `HsmTickSystem` guard on per-entity doctrine tier before accessing the interpreter, so on an empty world (no spawned entities) this is safe. Full BTree/HSM assets are a Phase S6+ concern.

---

### TASK-S5.2: Create Configuration System

**File created/modified:**
- `Hrot.SimHost/Configuration/SimHostConfig.cs` — replaced the old static-constants class with a JSON-backed instance class.
- `Hrot.SimHost/config.json` — default configuration file checked into source.
- `Hrot.SimHost/Hrot.SimHost.csproj` — added `<Content CopyToOutputDirectory="PreserveNewest">` for `config.json`.

**Design:**
- `SimHostConfig` has three properties: `DomainId` (int, default 0), `SimulationRateHz` (int, default 60), `GeodeticOrigin` (`GeodeticOriginConfig`, defaults to Tel Aviv coordinates matching the previous constants).
- `GeodeticOriginConfig` is a simple POCO class (not a DDS struct) for clean `System.Text.Json` round-tripping.
- `Load()` writes defaults and returns them if the file is missing; catches parse errors and falls back to defaults.
- `Program.cs` calls `SimHostConfig.Load("config.json")` at startup before any other initialization.

**Tests added:** `Hrot.SimHost.Tests/SimHostConfigTests.cs` (3 tests):
- `SimHostConfig_Load_ValidJson_ReturnsCorrectValues` ✅
- `SimHostConfig_Load_MissingFile_WritesDefaultsToDisk` ✅
- `SimHostConfig_Save_RoundTrip_PreservesAllValues` ✅

---

### TASK-S5.3: Add Logging and Diagnostics

**File created:** `Hrot.SimHost/Utilities/Logger.cs`

**Design:**
- `LogLevel` enum: `Debug (0)`, `Info (1)`, `Warning (2)`, `Error (3)`.
- `Logger.MinimumLevel` static property (default `Info`) — messages below threshold are discarded.
- Format: `[HH:mm:ss.fff] [LEVEL] message` — consistent, grep-friendly.
- `Error`-level messages go to `Console.Error`; all others go to `Console.Out`.
- All `Console.WriteLine` calls in `Program.cs` replaced with `Logger.Info` / `Logger.Warning` / `Logger.Error`.

**Tests added:** `Hrot.SimHost.Tests/LoggerTests.cs` (6 tests):
- `Logger_MinimumLevel_FiltersLowerPriorityMessages` ✅
- `Logger_MinimumLevelDebug_AllMessagesAppear` ✅
- `Logger_Info_OutputContainsTimestampAndLevelAndMessage` ✅
- `Logger_Warning_OutputContainsWarnTag` ✅
- `Logger_Debug_OutputContainsDebugTag` ✅
- `Logger_Error_WritesToStdErr` ✅

---

### TASK-S5.4: Add Graceful Shutdown

**File modified:** `Hrot.SimHost/Program.cs`

**Changes:**
- `CancellationTokenSource cts` created at program start.
- `Console.CancelKeyPress` handler calls `cts.Cancel()` and sets `e.Cancel = true` (prevents hard kill).
- Main loop extracted to `static void RunSimulationLoop(ModuleHostKernel, SystemGroup, CancellationToken)` — loops until `cancellationToken.IsCancellationRequested`.
- After the loop returns, `idAllocator.Dispose()` is called for clean resource release.
- `kernelGroup?.Dispose()` is called in a `finally` block to ensure cleanup even on exceptions.
- Frame counter logged on loop termination.

---

## 📊 Q1: Program Flow Execution

**Does the initialization sequence look logically sound?**

Yes. The current order in `Program.cs` is well-structured:

1. **Config load** — must be first (DomainId, SimulationRateHz, GeodeticOrigin are needed by subsequent steps).
2. **Kernel + ECS world** — must precede all module and system registration.
3. **DDS participant** — created before any network services.
4. **Doctrine registry** — must be populated before `SimulationLogicModule` is constructed (it's passed in the constructor).
5. **SimulationLogicModule + SystemGroup** — must come before `kernel.Initialize()` since the group holds live system state.
6. **GlobalTime seed** — set before `kernel.Initialize()` so the very first `ComponentSystem.DeltaTime` read is valid.
7. **Modules registered** — `GeographicModule`, `EntityLifecycleModule`, `SimHostModule`, `CycloneNetworkModule`.
8. **`kernel.Initialize()`** — finalises all module state.
9. **Main loop** — both `kernel.Update()` and `kernelGroup.Run()` called every frame.
10. **Cleanup** — `idAllocator.Dispose()`, `kernelGroup.Dispose()` after loop exits.

**Possible future reorganisation:** The `SimulationLogicModule` and `SystemGroup` creation currently sits between "Data Model Services" and "Toolkit Modules". Once the VehicleAPI is fully wired, it will require the ELM and NetworkSpawningSystem to be initialised before `SimulationLogicModule` is constructed, which may require reordering. A factory method (`SimulationLogicModule.Build(IKernelServices)`) would make this cleaner. For now the order is correct and readable.

---

## 📁 Files Created / Modified

| File | Action |
|------|--------|
| `Hrot.SimHost/Program.cs` | Modified — S5.1 + S5.3 + S5.4 |
| `Hrot.SimHost/Configuration/SimHostConfig.cs` | Modified — S5.2 |
| `Hrot.SimHost/config.json` | Created — S5.2 |
| `Hrot.SimHost/Utilities/Logger.cs` | Created — S5.3 |
| `Hrot.SimHost/Hrot.SimHost.csproj` | Modified — config.json content item |
| `Hrot.SimHost.Tests/SimHostConfigTests.cs` | Created — S5.2 tests |
| `Hrot.SimHost.Tests/LoggerTests.cs` | Created — S5.3 tests |

---

## 🧪 Test Summary

```
Passed!  - Failed: 0, Passed: 50, Skipped: 0, Total: 50
```

All 50 tests pass including all previous batch tests and the 9 new tests added in this batch.
