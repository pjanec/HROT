# SIM-BATCH-05: Main Application Shell (Phase S5)

**Batch Number:** SIM-BATCH-05  
**Tasks:** TASK-S5.1, S5.2, S5.3, S5.4  
**Phase:** S5 (Complete Shell)
**Estimated Effort:** 24 hours (3.0 days)  
**Priority:** HIGH  
**Dependencies:** Phase S4

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back! With Phase S4 fully complete and all our underlying behavior / navigation systems wired up to the SimulationLogicModule, it is time to assemble the main application integration point.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `docs/design/TASK-DETAILS-SIMHOST.md#task-s51-implement-programcs-entry-point`

### Source Code Location
- **Primary Work Area:** `Hrot.SimHost/Program.cs`, `Hrot.SimHost/Configuration/SimHostConfig.cs`, `Hrot.SimHost/Utilities/Logger.cs`
- **Secondary Work Area:** `Hrot.SimHost/Hrot.SimHost.csproj`
- **Test Project:** `Hrot.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/SIM-BATCH-05-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/SIM-BATCH-05-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅
4. **Task 4:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

Inside `Hrot.SimHost/Program.cs`, the core simulation currently spins up with only a `SimHostModule`. We have since created the `SimulationLogicModule` which initializes the behavior nodes, but it isn't wired into the primary execution loop yet!

Additionally, `Fdp.Examples.UrbanCombat` demonstrates how to host multiple models cleanly. This batch focuses cleanly registering everything required to tie the whole loop together.

---

## 🎯 Batch Objectives
- Introduce `SimulationLogicModule` into `Program.cs`.
- Start passing and managing `NetworkEntityMap` around accurately.
- Add configuration loading (`SimHostConfig.cs` with JSON).
- Add custom logging system (`Logger.cs`) and integrate it.
- Add graceful Ctrl+C shutdown mechanism.
- Ensure the main executable continues to build and run perfectly.

---

## ✅ Tasks

### Task 1: Implement Program.cs Entry Point (TASK-S5.1)

**File:** `Hrot.SimHost/Program.cs`

**Task Definition:** See [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md#task-s51-implement-programcs-entry-point)

**Description:**
Update the main entry point to include the new logic topologies.

**Requirements:**
1. In `Program.cs`, initialize a `DoctrineRegistry` and dummy `VehicleAPI` as per the spec.
2. Register `SimulationLogicModule` by instantiating it with the `DoctrineRegistry`, `NetworkEntityMap`, and dummy `VehicleAPI`. Call `RegisterSystems(kernelGroup)` where appropriate. (You may need to modify how `Program.cs` builds its `ModuleHostKernel` to include explicit system group configuration, or use `kernel.RegisterModule(...)` if `SimulationLogicModule` implements `IModule`. According to the spec, `SimulationLogicModule` requires you to call `RegisterSystems` manually on a dedicated `SystemGroup`. Refer to `TASK-DETAILS-SIMHOST.md`).
3. Ensure the project still compiles perfectly. 

**Tests Required:**
- ✅ Note: S5.1 is an integration-level change mostly inside `Program.cs`. Standard component tests are less helpful here. Please ensure `dotnet build` succeeds, and run the main application executable `dotnet run --project Hrot.SimHost` manually; it should not throw startup exceptions, and output the log `[SimHost] Running. Press Ctrl+C to exit.`.

---

### Task 2: Create Configuration System (TASK-S5.2)

**File:** `Hrot.SimHost/Configuration/SimHostConfig.cs`

**Task Definition:** See [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md#task-s52-create-configuration-system)

**Description:**
Create a JSON-backed configuration system to replace hardcoded values in `Program.cs`.

**Requirements:**
1. Create `SimHostConfig.cs`.
2. Add properties for `DomainId`, `SimulationRateHz`, and `GeodeticOrigin`.
3. Implement `Load()` falling back to defaults, and `Save()`.
4. Ensure default `config.json` is generated if missing at startup.

**Tests Required:**
- ✅ Valid payload parses.
- ✅ Missing configuration saves file with defaults correctly.

---

### Task 3: Add Logging and Diagnostics (TASK-S5.3)

**File:** `Hrot.SimHost/Utilities/Logger.cs`

**Task Definition:** See [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md#task-s53-add-logging-and-diagnostics)

**Description:**
Implement a standard custom Logger utility and deploy it across `Program.cs` replacing `Console.WriteLine`.

**Requirements:**
1. Create `Logger` static class.
2. Add `LogLevel` enum (Debug, Info, Warning, Error).
3. Append formatted timestamps and string interpolation.
4. Go through `Program.cs` and replace `Console.WriteLine` with `Logger.Info` or equivalent.

**Tests Required:**
- ✅ Tests covering minimum level filtering and output string generation. (You can redirect `Console.Out` temporarily in tests).

---

### Task 4: Add Graceful Shutdown (TASK-S5.4)

**File:** `Hrot.SimHost/Program.cs`

**Task Definition:** See [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md#task-s54-add-graceful-shutdown)

**Description:**
Introduce `CancellationTokenSource` and graceful disposal into the main simulation loop.

**Requirements:**
1. Wrap `Console.CancelKeyPress`.
2. Call cancellation on the token.
3. Pass token down into the `RunSimulationLoop` method natively.
4. Perform `idAllocator.Stop()` cleanly.

**Tests Required:**
- ✅ N/A - Execution pipeline verification handles this successfully.

---

## 🧪 Testing Requirements
As this is primarily an executable modification, the true validation is compiling the console application and asserting it spins up to a running loop without faulting.

---

## 📊 Report Requirements

**Q1 Program Flow Execution:** Does the program initialization sequence look logically sound at this stage? Is there anything you'd reorganize inside `Program.cs` to make it a cleaner setup script?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TASK-S5.1, S5.2, S5.3, S5.4 completed.
- [ ] `Program.cs` successfully executes a fully integrated `SimulationLogicModule`.
- [ ] Execution loop uses `SimHostConfig` loaded safely from JSON.
- [ ] Application logs output via newly deployed `Logger`.
- [ ] Application responds to `Ctrl+C` with safe shutdown lifecycle logic.
- [ ] Report submitted via markdown file.

---

## 📚 Reference Materials
- **Task Defs:** [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md) - See Phase S5
