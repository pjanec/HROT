# BATCH-05: Application Wiring, Deletion, and E2E Integration Test

**Batch Number:** BATCH-05  
**Tasks:** TCU-W001, TCU-W002, TCU-W003, TCU-W004, TCU-W006, TCU-T006  
**Phase:** Phase 5 — Application Wiring + Phase 6 — Integration Test  
**Estimated Effort:** 8–10 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 through BATCH-04 fully complete

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch wires the new controllers and translators into the four application hosts, deletes the obsolete classes, and adds the end-to-end integration test. This is the final implementation batch. After it completes, the Time Controller Unification is done.

**Build verification is crucial in this batch:** the top-level solution `IOS-IG-SimHost.sln` must build cleanly. The FDP sub-solution must also build cleanly.

### Required Reading (IN ORDER)

1. **Design Document:** `.dev/time-ctrl-unif/docs/DESIGN.md` — §3 Cluster Role Topology, §5 Distributed Flow Diagrams, §6 What Gets Deleted
2. **Task Definitions:** `.dev/time-ctrl-unif/docs/TASK-DETAIL.md` — read TCU-W001, TCU-W002, TCU-W003, TCU-W004, TCU-W006, TCU-T006 in full
3. **Previous Reviews:** `.dev/time-ctrl-unif/reviews/BATCH-04-REVIEW.md`
4. **DEBT-TRACKER:** `.dev/time-ctrl-unif/DEBT-TRACKER.md` — note DT-003 (slaveNodeIds param in SwitchToDeterministic)

### Source Code Location

#### Files to MODIFY in application hosts:

- `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` — replace DistributedTimeCoordinator usage
- `Hrot.SimHost/SimHostApp.cs` — replace SlaveTimeModeListener + MasterTimeController factory
- `Hrot.CGF/CgfApplication.cs` — replace SlaveTimeController + SlaveTimeModeListener
- `Hrot.IG/IgApplication.cs` — replace SlaveTimeController + SlaveTimeModeListener

#### Files to DELETE in FDP toolkit:

- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterTimeController.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SteppedMasterController.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SteppedSlaveController.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveTimeController.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SwitchableTimeController.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/DistributedTimeCoordinator.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveTimeModeListener.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/FrameLockstepDescriptorTranslator.cs`

Also **remove** `TimeNetworkModule.CreateLockstepTranslator` method (was marked `[Obsolete]` in BATCH-04; all call sites migrated in this batch).

#### New test file:

- `FDP/Toolkits/FDP.Toolkit.Time.Tests/UnifiedControllerE2ETests.cs` (NEW)

#### Build solutions:

- `FDP/FDP.sln`
- `IOS-IG-SimHost.sln` (root)

### Report Submission

**When done, submit your report to:**  
`.dev/time-ctrl-unif/reports/BATCH-05-REPORT.md`

---

## Context

The new unified controllers are complete and tested. This batch wires them into the running application code and removes the old scaffolding.

**Key constraints to prioritize (DT-003 from debt tracker):**
`MasterSyncController.SwitchToDeterministic(slaveNodeIds)` currently ignores the `slaveNodeIds` parameter — the effective slave set is fixed at construction time. When wiring in `OrchestratorSubsystem`, pass the current `ActiveNodes.Keys` at *construction time*, not just at `SwitchToDeterministic` call time.

---

## ✅ Tasks

### Task 1: Wire MasterSyncController in Orchestrator (TCU-W001)

**File:** `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` (UPDATE)  
**Task Definition:** See [TCU-W001 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-w001--wire-mastersynccotroller-in-orchestrator)

**Current state in Initialize():**
```csharp
_timeKernel = new ModuleHostKernel(...);
TimeControllerFactory.Create(_eventBus, timeConfig);  → now returns MasterSyncController
_timeKernel.SetTimeController(timeCtrl);
_timeCoordinator = new DistributedTimeCoordinator(...)
_timeModeTranslator = TimeNetworkModule.CreateDescriptorTranslator(...)
```

**New state — what to do:**

1. **Keep** `_eventBus` (needed for MasterSyncController and translator).
2. **Remove** `_timeWorld`, `_timeKernel`, the `ModuleHostKernel` + `EventAccumulator` setup.
3. **Replace** `_timeCoordinator: DistributedTimeCoordinator?` field with `_masterSync: MasterSyncController?`.
4. **In Initialize():** Construct `_masterSync` directly:
   ```csharp
   var slaveIds = new HashSet<int>(); // starts empty; slaves join dynamically
   _masterSync = new MasterSyncController(_eventBus, slaveIds, TimeConfig.Default);
   ```
5. **Wire `MasterLockstepTranslator`:** Replace `_timeModeTranslator` with two translators:
   - Keep `_timeModeTranslator = TimeNetworkModule.CreateDescriptorTranslator(_participant, _eventBus)` (SwitchTimeModeEvent relay)
   - Add `_lockstepTranslator = TimeNetworkModule.CreateMasterLockstepTranslator(_participant, _eventBus)`
   - Add `_timePulseTranslator = TimeNetworkModule.CreateTimePulseEgressTranslator(_participant, _eventBus)`
6. **Update handler references:**
   - `_timeCoordinator?.SwitchToDeterministic(ids)` → `_masterSync?.SwitchToDeterministic(ids)`
   - `_timeCoordinator?.SwitchToContinuous()` → `_masterSync?.SwitchToContinuous()`
   - `_timeKernel?.StepFrame(1f / 60f)` → `_masterSync?.Step(1f / 60f)` (inside `StepTime` case)
   - `_timeKernel?.GetTimeController()?.SetTimeScale(s)` → `_masterSync?.SetTimeScale(s)`
   - `_timeKernel?.GetTimeController()?.SeedState(...)` → `_masterSync?.SeedState(...)`
7. **Update Update():**
   - Replace `_timeKernel?.Update()` with `_masterSync?.Update()`
   - Replace `_timeCoordinator?.Update()` with nothing (MasterSyncController handles its own update)
   - Add calls for new translators: `_lockstepTranslator?.ScanAndPublish(null!); _lockstepTranslator?.PollIngress(null!, null!)`
   - Add `_timePulseTranslator?.ScanAndPublish(null!)`
   - Keep `_timeModeTranslator?.ScanAndPublish(null!); _timeModeTranslator?.PollIngress(null!, null!)`
8. **Update Shutdown():** dispose `_masterSync`, null out `_lockstepTranslator`, `_timePulseTranslator`.

**Note on DT-003:** Since `SwitchToDeterministic` ignores its parameter (uses construction-time slaves), the comment in the handler needs updating to reflect this. The slave set passed at construction is empty because nodes join dynamically — for now this means MasterSyncController has no ACK targets. This is acceptable for initial wiring; ACK tracking correctness depends on how `_clusterMaster.NodeRoster` is populated.

**Success Conditions (TCU-W001):**
1. Orchestrator builds without errors (`dotnet build IOS-IG-SimHost.sln`)
2. Integration test: `OrchestratorSubsystem_PausePublishesSwitchTimeModeEvent` — init subsystem; trigger `PauseTime` op; assert `SwitchTimeModeEvent(Deterministic)` on bus
3. Integration test: `OrchestratorSubsystem_ResumePublishesContinuousEvent`

---

### Task 2: Wire SlaveSyncController in SimHost (TCU-W002)

**File:** `Hrot.SimHost/SimHostApp.cs` (UPDATE)  
**Task Definition:** See [TCU-W002 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-w002--wire-slavesynccontroller-in-simhost)

**Current state (around lines 254–270):**
```csharp
var slaveTimeCfg = new TimeControllerConfig { Role = TimeRole.Slave, LocalNodeId = localNodeId, ... };
_slaveTimeModeListener = new SlaveTimeModeListener(_eventBus, _kernel, slaveTimeCfg, 
    continuousControllerFactory: () => new MasterTimeController(capturedEventBus, masterSyncConfig));
```

And the TimeController is set via `TimeControllerFactory.Create(_eventBus, timeConfig)` where `timeConfig.Role = TimeRole.Master`.

**New state:**
1. **Remove** `_slaveTimeModeListener` field and all references.
2. **Change** `TimeControllerConfig.Role = TimeRole.Slave` (not Master). `TimeControllerFactory.Create` now returns `SlaveSyncController` for Slave role.
3. **Remove** `continuousControllerFactory` lambda entirely.
4. **Remove** `TimePulseEgressTranslator` from `egressTranslators.Add(new TimePulseEgressTranslator(...))` — Orchestrator now is the sole pulse source.
5. **Replace** `egressTranslators.Add(TimeNetworkModule.CreateLockstepTranslator(...))`
   with `egressTranslators.Add(TimeNetworkModule.CreateSlaveLockstepTranslator(ddsParticipant, _eventBus, localNodeId))`.
6. **Remove** `_slaveTimeModeListener?.Update()` call from the update loop (line ~551).

**Note:** The `SlaveSyncController` is constructed via `TimeControllerFactory.Create` with `Role = TimeRole.Slave`. The `SwitchTimeModeDescriptorTranslator` (already in `egressTranslators`) handles receiving mode-switch commands from DDS.

---

### Task 3: Wire SlaveSyncController in CGF (TCU-W003)

**File:** `Hrot.CGF/CgfApplication.cs` (UPDATE)  
**Task Definition:** See [TCU-W003 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-w003--wire-slavesynccontroller-in-cgf)

**Current state (around lines 36, 64, 76–78, 151):**
```csharp
private readonly SlaveTimeModeListener _slaveTimeModeListener;
_lockstepTranslator = TimeNetworkModule.CreateLockstepTranslator(...);
_timeKernel.SetTimeController(new SlaveTimeController(_world.Bus));
_slaveTimeModeListener = new SlaveTimeModeListener(_eventBus, _kernel, slaveTimeCfg, null);
// ...
_slaveTimeModeListener.Update();
```

**New state:**
1. Remove `_slaveTimeModeListener` field and field initializer.
2. Replace `new SlaveTimeController(_world.Bus)` with `new SlaveSyncController(_world.Bus, localNodeId)` (or use `TimeControllerFactory.Create` with `Role = TimeRole.Slave`).
3. Replace `TimeNetworkModule.CreateLockstepTranslator(...)` with `TimeNetworkModule.CreateSlaveLockstepTranslator(_, _, localNodeId)`.
4. Remove `_slaveTimeModeListener.Update()` from the tick loop.

---

### Task 4: Wire SlaveSyncController in IG (TCU-W004)

**File:** `Hrot.IG/IgApplication.cs` (UPDATE)  
**Task Definition:** See [TCU-W004 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-w004--wire-slavesynccontroller-in-ig)

**Current state (around line 1172):**
```csharp
var timeController = new SlaveTimeController(_world.Bus);
```

**New state:**
1. Replace `new SlaveTimeController(_world.Bus)` with `new SlaveSyncController(_world.Bus, localNodeId)`.
2. If any `SlaveTimeModeListener` or `CreateLockstepTranslator` is used, replace similarly.
3. Search for any other time-related wiring and update as needed.

---

### Task 5: Delete Obsolete Classes (TCU-W006)

**Task Definition:** See [TCU-W006 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-w006--delete-obsolete-classes)

**Delete these files** (only after build succeeds in Tasks 1–4):

```
FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterTimeController.cs
FDP/Toolkits/FDP.Toolkit.Time/Controllers/SteppedMasterController.cs
FDP/Toolkits/FDP.Toolkit.Time/Controllers/SteppedSlaveController.cs
FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveTimeController.cs
FDP/Toolkits/FDP.Toolkit.Time/Controllers/SwitchableTimeController.cs
FDP/Toolkits/FDP.Toolkit.Time/Controllers/DistributedTimeCoordinator.cs
FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveTimeModeListener.cs
FDP/Toolkits/FDP.Toolkit.Time/FrameLockstepDescriptorTranslator.cs
```

Also **remove** the `CreateLockstepTranslator` method from `TimeNetworkModule.cs` (now all call sites use the role-specific versions).

**Also delete tests for deleted classes** (tests for classes that no longer exist):
- `FDP/Toolkits/FDP.Toolkit.Time.Tests/MasterTimeControllerTests.cs`
- `FDP/Toolkits/FDP.Toolkit.Time.Tests/SteppedMasterControllerTests.cs`
- `FDP/Toolkits/FDP.Toolkit.Time.Tests/SteppedSlaveControllerTests.cs`
- `FDP/Toolkits/FDP.Toolkit.Time.Tests/SlaveTimeControllerTests.cs`
- `FDP/Toolkits/FDP.Toolkit.Time.Tests/SwitchableTimeControllerTests.cs`
- `FDP/Toolkits/FDP.Toolkit.Time.Tests/WcrBatch02TimeControllerTests.cs` (if references deleted classes)

**DO NOT delete:**
- `SteppingTimeController.cs` — still in use
- Any tests that test `SwitchTimeModeDescriptorTranslator`, `TimePulseEgressTranslator`, `MasterSyncController`, `SlaveSyncController`

After deletion: verify `dotnet build FDP/FDP.sln` and `dotnet build IOS-IG-SimHost.sln` succeed.

---

### Task 6: Integration Test for Full Pause/Step/Resume Cycle (TCU-T006)

**File:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/UnifiedControllerE2ETests.cs` (NEW FILE)  
**Task Definition:** See [TCU-T006 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-t006--integration-test-full-pausestepresume-cycle-in-process) — read it in full.

**Test name:** `FullCycle_Pause_Step_Resume_NoPllLoss`

**Setup (in-process; no DDS):**
- One master bus, two slave buses.
- One `MasterSyncController`, two `SlaveSyncController` instances.
- Manual bridge: after each `SwapBuffers()`, copy `AdvanceFrameIntent` from master bus to both slave buses; copy `FrameStepCompletedEvent` from both slave buses back to master bus.

**Test steps:**
1. Run 20 Continuous frames; record `slave.TotalTime` after frame 20.
2. `master.SwitchToDeterministic()` with both slave IDs; relay `SwitchTimeModeEvent` to slave buses.
3. Drive frames until both slaves transition to Stepping (barrier crossed).
4. `master.Step(0.016f)` × 5; relay `AdvanceFrameIntent` to slaves each time; relay `FrameStepCompletedEvent` back to master; assert `master.TotalTime` advances by `5 × 0.016f`.
5. `master.SwitchToContinuous()`; relay event to slaves.
6. Run 20 more Continuous frames.

**Assertions:**
- `slave.GetMode() == Continuous` after step 6 ✅
- `slave.TotalTime` is within 5% of `master.TotalTime` after step 6 (PLL convergence after resume)
- No `TimePulseDescriptor` was ever published by either slave
- `slave.TotalTime` after resume ≈ `master.SimTimeSnapshot` from the resume event (not stale pre-pause value)

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**Complete in this exact sequence:**

1. **Tasks 1–4 (Wiring):** Update all application files → `dotnet build IOS-IG-SimHost.sln` — zero errors ✅
2. **Task 5 (Deletion):** Delete files → `dotnet build FDP/FDP.sln` AND `dotnet build IOS-IG-SimHost.sln` — both zero errors ✅
3. **Task 6 (E2E Test):** Write the integration test → `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj` — all pass ✅
4. **Final check:** Also run `dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj` — all existing tests pass ✅

**DO NOT delete files until ALL application hosts build cleanly.** Fix all errors before proceeding. No asking for permission.

---

## 🧪 Testing Requirements

- **E2E test:** 1 scenario test (`FullCycle_Pause_Step_Resume_NoPllLoss`) with the 4 assertions above
- **All pre-existing tests must pass** (or be legitimately deleted because the class they tested was removed)
- **Integration tests:** `dotnet test Hrot.ClusterRunner.Integration.Tests/...` must pass

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `OrchestratorSubsystem` uses `MasterSyncController` (no `DistributedTimeCoordinator`)
- [ ] `SimHostApp` uses `SlaveSyncController` (no `SlaveTimeModeListener`, no `continuousControllerFactory`, no `TimePulseEgressTranslator`)
- [ ] `CgfApplication` uses `SlaveSyncController` (no `SlaveTimeModeListener`)
- [ ] `IgApplication` uses `SlaveSyncController`
- [ ] All 8 obsolete class files deleted from FDP toolkit
- [ ] `grep -r "SlaveTimeController\|SteppedMasterController\|DistributedTimeCoordinator\|SlaveTimeModeListener\|FrameLockstepDescriptorTranslator" --include="*.cs"` returns no matches outside deleted/test files
- [ ] `dotnet build FDP/FDP.sln` — zero errors
- [ ] `dotnet build IOS-IG-SimHost.sln` — zero errors
- [ ] `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj` — all pass (≥124 tests + new E2E)
- [ ] `dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj` — all pass
- [ ] `BATCH-05-REPORT.md` submitted

---

## 📊 Report Requirements

Submit to `.dev/time-ctrl-unif/reports/BATCH-05-REPORT.md`.

```markdown
# BATCH-05 Report
## Completion Status
## Build Results (both solutions)
## Test Results (FDP time tests + integration tests)
## Developer Insights
**Q1:** What wiring surprises did you encounter in the application hosts?
**Q2:** Any DT-003/DT-004/DT-006 debt items that manifested during wiring?
**Q3:** Design decisions made beyond the spec?
**Q4:** What's the state of SequenceID (DT-006) now that translators are wired?
**Q5:** Suggested commit message
```

---

## ⚠️ Common Pitfalls

- Do NOT delete obsolete classes before all application builds succeed — you'll create cascading build errors.
- `SlaveSyncController` constructor signature: `(FdpEventBus eventBus, int localNodeId, TimeConfig? config, Func<long>? tickSource)` — provide `localNodeId`.
- `MasterSyncController` needs the slave node IDs at construction: `new MasterSyncController(bus, slaveIds, TimeConfig.Default)`.  Since slaves join dynamically, an empty set is acceptable for initial wiring. (See DT-003.)
- `TimePulseEgressTranslator` must be removed from SimHost and added to Orchestrator (Orchestrator is the sole pulse source per the design).
- Check `WcrBatch02TimeControllerTests.cs` to see if it references deleted classes before deciding to delete it.
- The `SteppingTimeController` must NOT be deleted.
- After deleting test files for removed classes, run `dotnet test` to confirm the remaining tests all pass.

---

## 📚 Reference Materials

- **Task Definitions:** `.dev/time-ctrl-unif/docs/TASK-DETAIL.md` — §TCU-W001, §TCU-W002, §TCU-W003, §TCU-W004, §TCU-W006, §TCU-T006
- **Design:** `.dev/time-ctrl-unif/docs/DESIGN.md` — §3 Role Topology, §5 Flow Diagrams, §6 What Gets Deleted
- **Debt Tracker:** `.dev/time-ctrl-unif/DEBT-TRACKER.md`
- **Developer Skill Guide:** `.github/skills/developer/SKILL.md`
