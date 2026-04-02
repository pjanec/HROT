# Exercise Clock Control — Root Cause Analysis & Implementation Plan

## Problem Statement

Pressing **Pause** in the Orchestrator UI works once (sim time freezes).  
Pressing **Resume** works once (sim time resumes).  
But:
- **Step** never advances sim time
- **Successive Pause/Resume cycles** stop working after the first

The goal is autonomous headless integration tests that prove all three operations
work correctly in the full `-m all` configuration (all subsystems in one process,
communicating only via CycloneDDS loopback — no intra-process shortcuts).

---

## Root Cause Map

### Why Step doesn't work

1. `OrchestratorSubsystem.PauseTime` calls `_timeCoordinator.SwitchToDeterministic(ids)`.  
   This swaps the Orchestrator's mini-kernel to `SteppedMasterController(bus, slaveNodeIds, …)`.

2. `StepTime` calls `_timeKernel.StepFrame(delta)` → `SteppedMasterController.Step()`:
   - Publishes `FrameOrderDescriptor` on the Orchestrator's **private FdpEventBus** (`_eventBus`).
   - Sets `_waitingForAcks = true`.

3. **No DDS bridge for `FrameOrderDescriptor`/`FrameAckDescriptor` exists.**
   - These messages never leave the Orchestrator's private bus.
   - Slave nodes never receive a `FrameOrderDescriptor`, so they never produce a `FrameAckDescriptor`.
   - `SteppedMasterController` waits forever with `_waitingForAcks = true`.
   - Every subsequent `Step()` call is silently ignored (`if (_waitingForAcks) return`).

4. **`SlaveTimeModeListener` is not wired into any simulation subsystem.**
   - SimHost, IG, ExCon each have their `SwitchTimeModeDescriptorTranslator` wired (DDS→bus), so `SwitchTimeModeEvent` does land on their private buses.
   - But nobody calls `SlaveTimeModeListener.Update()` to consume it and swap their kernel.
   - SimHost's kernel stays on `MasterTimeController` → keeps ticking → keeps publishing `TimePulseDescriptor`.
   - The ClusterUiCache fix (don't update `MasterSimTime` when `IsPaused`) hides this at the UI level but the simulation is never truly paused.

### Why successive Pause/Resume cycles fail

- `SwitchToContinuous()` swaps the Orchestrator's kernel back to `MasterTimeController`.
- `SwitchToDeterministic()` on the second Pause tries to compute `barrierWallTicks = currentTotalWallTicks + lookahead`.
- Because no slaves ever ACK (fix above pending), `_slaveNodeIds` passed into `SteppedMasterController` — if ExCon is included — means we forever wait on ExCon which has no simulation kernel.
- Additionally: the `SwitchTimeModeDescriptorTranslator` echo-loop prevention on the Orchestrator side (`_lastIngressed`) may silently drop the re-published event on second Pause because the cache matches the previous Deterministic event.

### Architecture constraints (per user)

- Orchestrator is the ONLY time master.  
- SimHost, IG, CGF, ExCon are time slaves.  
- **SimHost, IG and CGF all own a `ModuleHostKernel`** and must have `SlaveTimeModeListener` wired. They participate in lockstep ACK.  
- **ExCon has no simulation kernel** — it is a pure presentation node. It must NOT be in `slaveNodeIds` and does NOT get `SlaveTimeModeListener`.  
- All communication must go through DDS — no intra-process bus sharing.  
- FDP toolkit must never reference application-layer types (no DDS writer wired from inside toolkit classes; toolkit uses delegates/events).  
- Only simulation-kernel nodes (SimHost nodeId=1, IG nodeId=300, CGF nodeId=400) participate in lockstep ACK. ExCon (nodeId=500) is not in `slaveNodeIds`.

---

## Solution Design

### Fix 1 — Add DDS bridge for FrameOrder / FrameAck  (toolkit-only, zero app-layer refs)

Create **`FDP.Toolkit.Time.Controllers.FrameLockstepDescriptorTranslator`** (new file):

1. Add `[DdsTopic("FrameOrder")]` + `[DdsId]` attributes to `FrameOrderDescriptor`.
2. Add `[DdsTopic("FrameAck")]` + `[DdsId]` attributes to `FrameAckDescriptor`.
3. `FrameLockstepDescriptorTranslator` implements `IDescriptorTranslator`:
   - `ScanAndPublish`: drains `FrameOrderDescriptor` **and** `FrameAckDescriptor` from `FdpEventBus`, writes each to its DDS topic.
   - `PollIngress`: reads from both DDS topics, publishes to `FdpEventBus`.
   - Echo-loop prevention: skip a `FrameAckDescriptor` we just ingested (so the master doesn't send its own ACK back as a DDS-sourced one).
4. Factory: `TimeNetworkModule.CreateLockstepTranslator(DdsParticipant?, FdpEventBus)`.

Wire this translator in:
- **OrchestratorSubsystem** (`_timeModeTranslator` pattern — already calls `ScanAndPublish`/`PollIngress` each frame).
- **SimHostApp** (add alongside existing `SwitchTimeModeDescriptorTranslator`).

### Fix 2 — Wire `SlaveTimeModeListener` in SimHostApp

SimHostApp already has `_eventBus` and `_kernel`.  
After time-controller setup:
```csharp
var slaveTimeCfg = new TimeControllerConfig { Role = TimeRole.Slave, LocalNodeId = localNodeId };
_slaveTimeModeListener = new SlaveTimeModeListener(_eventBus, _kernel, slaveTimeCfg);
```
In `OnUpdate()`, before `_kernel.Update()`:
```csharp
_slaveTimeModeListener?.Update();
```

### Fix 3 — Wire `SlaveTimeModeListener` in IgApplication (REQUIRED — IG has a kernel)

IG has a full ECS world and kernel (`_kernel`, `_world.Bus`). IG nodeId = 300.  
After `_kernel.SetTimeController(timeController)` in `InitializeNetwork`:  
```csharp
var slaveTimeCfg = new TimeControllerConfig { Role = TimeRole.Slave, LocalNodeId = _effectiveInstanceId };
_slaveTimeModeListener = new SlaveTimeModeListener(_world.Bus, _kernel, slaveTimeCfg);
```
Also add `TimeNetworkModule.CreateLockstepTranslator(participant, _world.Bus)` to `customTranslators`.  
In `Update()`, before `_kernel.Update()`:  
```csharp
_slaveTimeModeListener?.Update();
```

### Fix 3b — Wire `SlaveTimeModeListener` in CgfApplication (REQUIRED — CGF has a kernel)

CGF nodeId = 400. CGF has `_eventBus` but no kernel yet — add a minimal time kernel:  
```csharp
_cgfWorld  = new EntityRepository();
_cgfKernel = new ModuleHostKernel(_cgfWorld, new EventAccumulator());
var slaveCfg = new TimeControllerConfig { Role = TimeRole.Slave, LocalNodeId = DefaultNodeId };
_cgfKernel.SetTimeController(new SlaveTimeController(_eventBus, slaveCfg.SyncConfig));
_cgfKernel.Initialize();
var slaveTimeCfg = new TimeControllerConfig { Role = TimeRole.Slave, LocalNodeId = DefaultNodeId };
_slaveTimeModeListener = new SlaveTimeModeListener(_eventBus, _cgfKernel, slaveTimeCfg);
_lockstepTranslator    = TimeNetworkModule.CreateLockstepTranslator(_participant, _eventBus);
```
In `Tick()`:  
```csharp
_slaveTimeModeListener?.Update();
_cgfKernel?.Update();
_lockstepTranslator?.ScanAndPublish(null!);
_lockstepTranslator?.PollIngress(null!, null!);
```

ExCon has no simulation kernel → skip.

### Fix 4 — `slaveNodeIds` must contain only simulation-kernel nodes

In `OrchestratorSubsystem.PauseTime` handler:  
```csharp
// Only include simulation-kernel nodes (SimHost, IG, CGF).
// ExCon is a pure presentation node with no kernel — excluding it prevents
// SteppedMasterController from waiting forever for ACKs that never arrive.
static readonly HashSet<string> KernelSubsystems = new() { "SimHost", "IG", "CGF" };
var ids = _clusterMaster.NodeRoster.ActiveNodes
    .Where(n => KernelSubsystems.Contains(n.Value.SubsystemName))
    .Select(n => n.Key)
    .ToHashSet();
_timeCoordinator?.SwitchToDeterministic(ids);
```

### Fix 5 — Expose test observable without intra-process shortcuts

Expose `SimHostApp.TestHook_CurrentSimTime → double` (reads `_kernel.CurrentTime.TotalTime` directly) — only for integration test assertions, clearly marked as a test hook.

Also: `OrchestratorSubsystem.TestHook_IsPaused` already exists.  
Add: `OrchestratorSubsystem.TestHook_SteppedMasterController` (returns current controller cast) — for verifying step count.

---

## Test Infrastructure

A **`TimeControlActionHandler`** class registered in the test harness:

| Action name | Args | Behaviour |
|---|---|---|
| `pause_time` | ─ | Sends `PauseTime` via `ClusterMaster.HandleClusterOpRequest` |
| `resume_time` | ─ | Sends `ResumeTime` |
| `step_time` | `DeltaSeconds` (float) | Sends `StepTime` with payload |
| `assert_sim_time_frozen` | `SampleIntervalMs`, `ToleranceSec` | Polls SimHost `TotalTime` twice; asserts diff < tolerance |
| `assert_sim_time_advancing` | `WaitMs`, `MinAdvanceSec` | Polls SimHost `TotalTime` before/after wait; asserts diff ≥ min |
| `assert_sim_time_approx` | `ExpectedSec`, `ToleranceSec` | Asserts SimHost `TotalTime` ≈ expected |
| `assert_is_paused` | ─ | Asserts `OrchestratorSubsystem.TestHook_IsPaused == true` |
| `assert_is_running` | ─ | Asserts `OrchestratorSubsystem.TestHook_IsPaused == false` |

All waits use `Task.Delay` so the orchestrator run loop (background thread) keeps ticking.  
An initial `TransitionToOperatingLive` step is always first so the clock is running before testing.

---

## Test Scenarios (JSON scripts in TestScripts/)

### Scenario A — `e2e_time_pause_resume.json`
Tests single Pause → Resume cycle.

```
T=2s   assert_slave_transition → OperatingLive
T=5s   assert_is_running
T=5.5s pause_time
T=6s   assert_is_paused
T=6.2s assert_sim_time_frozen  (poll 500ms apart, tol 0.05s)
T=7.5s resume_time
T=8s   assert_is_running
T=9.5s assert_sim_time_advancing  (wait 1000ms, min 0.5s)
```

### Scenario B — `e2e_time_pause_step_resume.json`
Tests Pause → 3×Step → Resume.

```
T=2s   assert_slave_transition → OperatingLive
T=5s   assert_is_running
T=5.5s pause_time
T=6s   assert_is_paused
T=6.1s assert_sim_time_frozen
T=6.3s step_time delta=1.0
T=6.6s step_time delta=1.0
T=6.9s step_time delta=1.0
T=7.2s assert_sim_time_approx  expected=+3s_from_pause  tol=0.2s
T=7.5s resume_time
T=8s   assert_is_running
T=9.5s assert_sim_time_advancing
```

### Scenario C — `e2e_time_multi_pause_resume.json`
Tests 3 successive Pause/Resume cycles.

```
T=2s   assert_slave_transition → OperatingLive
T=5s   pause_time,  assert_is_paused
T=6s   assert_sim_time_frozen
T=7s   resume_time, assert_is_running
T=8s   assert_sim_time_advancing
T=9s   pause_time,  assert_is_paused
T=10s  assert_sim_time_frozen
T=11s  resume_time, assert_is_running
T=12s  assert_sim_time_advancing
T=13s  pause_time,  assert_is_paused
T=14s  assert_sim_time_frozen
T=15s  resume_time, assert_is_running
T=16s  assert_sim_time_advancing
```

### Scenario D — `e2e_time_pause_step_multiresume.json`
Combined: multi-step blocks, then resume, then pause again.

---

## Staged Implementation Checklist

- [ ] **Stage 0** — Write plan (this file) ✓ in progress
- [ ] **Stage 1** — Add `[DdsTopic]` annotations to `FrameOrderDescriptor` / `FrameAckDescriptor`
- [ ] **Stage 2** — Implement `FrameLockstepDescriptorTranslator` + `TimeNetworkModule.CreateLockstepTranslator()`
- [ ] **Stage 3** — Wire `FrameLockstepDescriptorTranslator` in `OrchestratorSubsystem` (alongside `_timeModeTranslator`)
- [ ] **Stage 4** — Wire `FrameLockstepDescriptorTranslator` + `SlaveTimeModeListener` in `SimHostApp`
- [ ] **Stage 5** — Wire `FrameLockstepDescriptorTranslator` + `SlaveTimeModeListener` in `IgApplication` (IG has kernel, is a lockstep participant)
- [ ] **Stage 5b** — Add minimal time kernel + `FrameLockstepDescriptorTranslator` + `SlaveTimeModeListener` in `CgfApplication` (CGF has kernel, is a lockstep participant)
- [ ] **Stage 6** — Filter `slaveNodeIds` to `"SimHost"` | `"IG"` | `"CGF"` in `OrchestratorSubsystem.PauseTime` (ExCon excluded)
- [ ] **Stage 7** — Expose `SimHostApp.TestHook_CurrentSimTime`; `OrchestratorSubsystem.IsPausedForTest` already exists
- [ ] **Stage 8** — Build; verify 0 errors
- [ ] **Stage 9** — Implement `TimeControlActionHandler` in `Hrot.ClusterRunner.Integration.Tests`
- [ ] **Stage 10** — Write scenario A: single pause/resume test + JSON script
- [ ] **Stage 11** — Run scenario A; iterate until green
- [ ] **Stage 12** — Write scenario B: pause/step/resume test + JSON script
- [ ] **Stage 13** — Run scenario B; iterate until green
- [ ] **Stage 14** — Write scenario C: multi-cycle pause/resume test + JSON script
- [ ] **Stage 15** — Run scenario C; iterate until green
- [ ] **Stage 16** — Write scenario D: combined test + JSON script
- [ ] **Stage 17** — Run scenario D; iterate until green
- [ ] **Stage 18** — Run full integration test suite; no regressions

---

## Key Files to Touch

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs` | Add `[DdsTopic]`/`[DdsId]` to `FrameOrderDescriptor` and `FrameAckDescriptor` |
| `FDP/Toolkits/FDP.Toolkit.Time/FrameLockstepDescriptorTranslator.cs` | **NEW** — DDS bridge for frame lockstep |
| `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs` | Add `CreateLockstepTranslator()` factory method |
| `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` | Wire lockstep translator; filter `slaveNodeIds` to "SimHost" |
| `Hrot.SimHost/SimHostApp.cs` | Wire lockstep translator + `SlaveTimeModeListener`; expose `TestHook_CurrentSimTime` |
| `Hrot.IG/IgApplication.cs` | Wire lockstep translator + `SlaveTimeModeListener` (IG owns a kernel) |
| `Hrot.CGF/CgfApplication.cs` | Add minimal time kernel + lockstep translator + `SlaveTimeModeListener` |
| `Hrot.ClusterRunner.Integration.Tests/TimeControlIntegrationTests.cs` | **NEW** — test class with `TimeControlActionHandler` |
| `Hrot.ClusterRunner.Integration.Tests/TestScripts/e2e_time_*.json` | **NEW** — scenario scripts |

---

## Diagrams

### Pause flow (after fixes)

```
[UI/Test]
    │ PauseTime
    ▼
OrchestratorSubsystem (handler)
    │ slaveIds = roster.Where(SubsystemName=="SimHost")
    │ _timeCoordinator.SwitchToDeterministic(slaveIds)
    ▼
DistributedTimeCoordinator
    │ Publishes SwitchTimeModeEvent(Deterministic, barrier) → Orchestrator FdpEventBus
    │ Sets pendingBarrier
    ▼
[Next OrchestratorSubsystem.Update()]
    │ _timeCoordinator.Update()  → barrier reached → swaps Orchestrator kernel to SteppedMasterController(slaveIds)
    │ _timeModeTranslator.ScanAndPublish() → SwitchTimeModeWireDto → DDS
    │ _lockstepTranslator.ScanAndPublish() → noop (no FrameOrders yet)
    ▼
[DDS loopback]
    ▼
SimHostApp.OnUpdate()
    │ SwitchTimeModeDescriptorTranslator.PollIngress() → SwitchTimeModeEvent lands on SimHost FdpEventBus
    │ _slaveTimeModeListener.Update()
    │   → TotalWallTicks >= barrier → swap SimHost kernel to SteppedSlaveController(nodeId)
    │ _kernel.Update()   [SteppedSlaveController.Update() → no FrameOrder → returns frozen time]
    │ TimePulseEgressTranslator → no new pulse (SimTimeSnapshot stays frozen)
    │ _lockstepTranslator.ScanAndPublish() → noop
    ▼
OrchestratorSubsystem._uiCache.Update()
    │ DrainTimeMode() → IsPaused = true
    │ DrainTimePulse() → IsPaused → MasterSimTime NOT updated ✓
```

### Step flow (after fixes)

```
[UI/Test]  StepTime delta=1.0
    ▼
OrchestratorSubsystem (handler)
    │ _timeKernel.StepFrame(1.0)
    ▼
SteppedMasterController.Step(1.0)
    │ Advances _totalTime += 1.0 * scale
    │ Publishes FrameOrderDescriptor(frameId) → Orchestrator FdpEventBus
    │ _waitingForAcks = true (waiting for SimHost nodeId ACK)
    ▼
[Next OrchestratorSubsystem.Update()]
    │ _lockstepTranslator.ScanAndPublish()
    │   → drains FrameOrderDescriptor → writes to DDS topic "FrameOrder"
    ▼
[DDS loopback]
    ▼
SimHostApp.OnUpdate()
    │ _lockstepTranslator.PollIngress()
    │   → reads FrameOrderDescriptor from DDS → publishes to SimHost FdpEventBus
    │ _kernel.Update()  [SteppedSlaveController.Update()]
    │   → dequeues FrameOrderDescriptor → advances _totalTime += 1.0
    │   → publishes FrameAckDescriptor(frameId, nodeId) → SimHost FdpEventBus
    │ _lockstepTranslator.ScanAndPublish()
    │   → drains FrameAckDescriptor → writes to DDS topic "FrameAck"
    │ TimePulseEgressTranslator → no pulse (SteppedSlaveController doesn't publish)
    ▼
[DDS loopback]
    ▼
OrchestratorSubsystem.Update()
    │ _lockstepTranslator.PollIngress()
    │   → reads FrameAckDescriptor → publishes to Orchestrator FdpEventBus
    │ SteppedMasterController.Update()
    │   → OnAckReceived(frameId, SimHostNodeId) → _pendingAcks empty → _waitingForAcks = false
    ▼
Next Step() call is now unblocked ✓
```

---

## Notes

- `TimePulseDescriptor` is published at 1 Hz by `MasterTimeController` and NOT by `SteppedMasterController` or `SteppedSlaveController`. This means `MasterSimTime` in `ClusterUiCache` stops updating when paused (last value retained). After step, it won't update until resume (since `SteppedSlaveController` doesn't emit pulses). Test assertions should use `TestHook_CurrentSimTime` for precise step verification, and `MasterSimTime` sampling for coarse pause/resume verification.
- `SlaveTimeModeListener` is toolkit code — no app-layer refs. ✓
- `FrameLockstepDescriptorTranslator` is toolkit code — no app-layer refs. ✓
- The `SwitchTimeModeDescriptorTranslator` echo-loop prevention (`_lastIngressed`) should be OK for repeated Pause cycles because `BarrierWallTicks` is different each time (always `currentWallTicks + lookahead`).
- ExCon has no `ModuleHostKernel` → no `SlaveTimeModeListener` needed → not included in `slaveNodeIds`.
- IG can optionally get `SlaveTimeModeListener` wired so its rendering freezes visually during Pause, but it's not required for correctness.
