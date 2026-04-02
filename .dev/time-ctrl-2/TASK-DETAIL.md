# Time Control Phase 2 — Task Detail Document

**Reference:** See [DESIGN.md](./DESIGN.md) for architecture context and rationale.  
**Tracker:** See [TASK-TRACKER.md](./TASK-TRACKER.md) for progress status.

---

## Phase 1 — Fix Core Lockstep (Feature A)

Design reference: [DESIGN.md §3](./DESIGN.md#3-feature-a--runtime-slave-set-fix)

---

### TC2-P1-T1 — Fix MasterSyncController.SwitchToDeterministic

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs`

**What to do:**

1. Locate `SwitchToDeterministic(HashSet<int> slaveNodeIds)`.
2. At the very start of the method body (before the barrier calculation), replace the `_expectedSlaves` content with the provided runtime set:
   ```csharp
   _expectedSlaves.Clear();
   if (slaveNodeIds != null)
       _expectedSlaves.UnionWith(slaveNodeIds);
   ```
3. Remove or replace the existing doc comment on the `slaveNodeIds` parameter. Old text (incorrect):
   > "Accepted for API compatibility with the coordinator pattern; the effective slave set used for ACK tracking is the one supplied at construction time."
   
   Replace with:
   > "The roster of slave node IDs that must ACK every step during lockstep. Replaces any prior slave set."

4. Remove the "Note (DT-003)" comment in `OrchestratorSubsystem.Update` that references the now-fixed ignore behaviour (see TC2-P1-T2).

**Why:** Without this fix, `_pendingAcks` is always re-armed from an empty `_expectedSlaves`, so `Step()` never blocks for any ACK. Lockstep is therefore non-functional.

**Success conditions (unit tests in `MasterSyncControllerTests.cs`):**

- **TC2-P1-T1-SC1**: Create a controller with `slaveNodeIds = null` (or empty) at construction. Call `SwitchToDeterministic(new HashSet<int>{1,2})` and transition to Stepping. Call `Step(delta)`. Verify `frame.FrameNumber` does NOT advance on the second call to `Step()` before ACKs are received from nodes 1 and 2.

- **TC2-P1-T1-SC2**: After the step above, publish `FrameStepCompletedEvent` for node 1 and node 2 into the bus, swap, call `Update()`. Verify the next `Step()` NOW advances `FrameNumber`.

- **TC2-P1-T1-SC3**: Call `SwitchToDeterministic` a second time (second pause cycle) with a different `slaveNodeIds = {3}`. Verify that after the mode transition, a `Step()` followed by publishing only the ACK from node 3 unblocks the next step (nodes 1 and 2 are no longer expected).

**Test class location:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/MasterSyncControllerTests.cs`  
**New test method names:**
- `MasterSyncController_RuntimeSlaveSet_BlocksUntilRuntimeAcks`
- `MasterSyncController_RuntimeSlaveSet_StepAdvancesAfterAcks`
- `MasterSyncController_RuntimeSlaveSet_SecondCallReplacesFirstSet`

---

### TC2-P1-T2 — Update OrchestratorSubsystem construction and comment

**File:** `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs`

**What to do:**

1. In `Initialize()`, the current `MasterSyncController` constructor call passes `new HashSet<int>()` as `slaveNodeIds`. This is now the correct thing to pass (empty is fine, since the runtime call will populate it). No change to the constructor call is needed — but the comment `// Note (DT-003): SwitchToDeterministic ignores slaveNodeIds at call time…` in `Update()` must be removed (it described the now-fixed behaviour).

2. In `Update()`, find and remove the `// Note (DT-003): …` comment block above the `_masterSync.SwitchToDeterministic(slaveIds)` call. The call itself is already correct.

**Success conditions:**

- **TC2-P1-T2-SC1**: The integration test `PauseResume_SimTimeFreezes_ThenAdvances` in `TimeControlIntegrationTests` continues to pass — no regression.
- **TC2-P1-T2-SC2**: The `Note (DT-003)` comment no longer appears in the file (grep check).

---

## Phase 2 — Smooth SimTime Display on UI (Feature B)

Design reference: [DESIGN.md §4](./DESIGN.md#4-feature-b--smooth-simtime-display-on-ui)

---

### TC2-P2-T1 — Add ITimeController injection to ClusterUiCache

**File:** `Hrot.ClusterRunner/Services/ClusterUiCache.cs`

**What to do:**

1. Add a private field:
   ```csharp
   private readonly ITimeController? _localTimeController;
   ```

2. Add a backing field for the network-sourced sim time:
   ```csharp
   private double _networkSimTime;
   ```

3. Add the optional `ITimeController?` parameter to the constructor. The existing single-parameter `(DdsParticipant participant)` constructor is preserved unchanged for backward compatibility (or converted to a chained call).

   New constructor:
   ```csharp
   public ClusterUiCache(DdsParticipant participant, ITimeController? localTimeController = null)
   ```

4. Change the `MasterSimTime` property from an auto-property to a computed property:
   ```csharp
   public double MasterSimTime =>
       _localTimeController != null
           ? _localTimeController.GetCurrentState().TotalTime
           : _networkSimTime;
   ```

5. In `DrainTimePulse()`, replace the line that sets the `MasterSimTime` auto-property with one that sets the backing field `_networkSimTime`:
   ```csharp
   if (!IsPaused)
       _networkSimTime = s.Data.SimTimeSnapshot;
   ```

**Namespace import required:** `ModuleHost.Core.Time` (for `ITimeController`). Check existing usings in the file.

**Success conditions (unit tests in `ClusterUiCacheTests.cs`):**

- **TC2-P2-T1-SC1**: Construct `ClusterUiCache` with a `MockTimeController` that returns `TotalTime = 77.5`. Without calling `Update()` or writing any DDS messages, assert `cache.MasterSimTime == 77.5`.

- **TC2-P2-T1-SC2**: Construct `ClusterUiCache` without a controller (null). Write a `TimePulseDescriptor { SimTimeSnapshot = 33.0 }` over DDS, wait for propagation, call `Update()`. Assert `cache.MasterSimTime == 33.0` (network fallback).

- **TC2-P2-T1-SC3**: Construct `ClusterUiCache` with a `MockTimeController` that returns `TotalTime = 50.0`. Write a `TimePulseDescriptor { SimTimeSnapshot = 99.0 }` over DDS, wait, call `Update()`. Assert `cache.MasterSimTime == 50.0` (controller takes priority, network value is ignored for `MasterSimTime`).

- **TC2-P2-T1-SC4**: Existing `ClusterUiCache_UpdatesTimeScaleFromTimePulse` test continues to pass (no regression on `MasterWallTicks`, `MasterTimeScale`).

**Test class:** `Hrot.ClusterRunner.Tests/ClusterUiCacheTests.cs`  
**New test method names:**
- `ClusterUiCache_MasterSimTime_ReadsFromLocalController_WhenInjected`
- `ClusterUiCache_MasterSimTime_FallsBackToNetwork_WhenNoController`
- `ClusterUiCache_MasterSimTime_IgnoresNetworkPulse_WhenControllerInjected`

**Note on `MockTimeController`:** Create a minimal `private` or `internal` mock class in the test file implementing `ITimeController` returning a configurable `TotalTime` from `GetCurrentState()`.

---

### TC2-P2-T2 — Wire MasterSyncController into OrchestratorSubsystem's UI cache

**File:** `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs`

**What to do:**

1. In `Initialize()`, move the `_masterSync` and `_eventBus` creation to appear **before** the `_uiCache` instantiation (currently `_uiCache` is created first).

2. Change the `_uiCache =` line to pass `_masterSync`:
   ```csharp
   _uiCache = new ClusterUiCache(_participant, _masterSync);
   ```

3. The `_scenarioPanel` creation must follow `_uiCache` (already the case).

**Success conditions:**

- **TC2-P2-T2-SC1**: The existing integration test `PauseResume_SimTimeFreezes_ThenAdvances` passes with no regression.
- **TC2-P2-T2-SC2**: Manually verifiable (no automated test for render output): after the change, running the Orchestrator and observing the Time Control panel should show the sim time counter updating every frame, not once per second. Document this as a manual acceptance check in the batch report.

---

### TC2-P2-T3 — Wire slave controllers into SimHost and IG UI caches (stretch)

**Files:** `Hrot.SimHost/Services/SimHostSubsystem.cs`, `Hrot.IG/Services/IgSubsystem.cs` (exact paths TBD by developer).

**What to do:**

Locate where `ClusterUiCache` is constructed in each slave subsystem. If the subsystem has access to its `ModuleHostKernel`, update the construction call:

```csharp
_uiCache = new ClusterUiCache(_participant, _kernel.GetTimeController());
```

This is a **stretch goal**, not required for the primary deliverable. Implement only if time permits and the kernel is accessible at the point of cache construction.

**Success conditions:**

- **TC2-P2-T3-SC1**: SimHost/IG subsystem tests pass without regression.
- **TC2-P2-T3-SC2**: Manual check — Time Control panel on IG/SimHost windows shows smooth time.

---

## Phase 3 — ExCon Lockstep Participation (Feature C)

Design reference: [DESIGN.md §5](./DESIGN.md#5-feature-c--excon-lockstep-participation)

---

### TC2-P3-T1 — Add SlaveSyncController and translators to ExConSubsystem

**File:** `Hrot.ClusterRunner/Services/ExConSubsystem.cs`

**What to do:**

1. Add private fields (alongside existing time handler fields):
   ```csharp
   private FdpEventBus?           _timeEventBus;
   private SlaveSyncController?   _slaveSyncController;
   private IDescriptorTranslator? _timeModeTranslator;
   private IDescriptorTranslator? _slaveLockstepTranslator;
   private IDescriptorTranslator? _timePulseIngressTranslator;
   ```

2. In `Initialize()`, after the `iosNodeId` is derived, add:
   ```csharp
   _timeEventBus             = new FdpEventBus();
   _slaveSyncController      = new SlaveSyncController(_timeEventBus, iosNodeId, TimeConfig.Default);
   _timeModeTranslator       = TimeNetworkModule.CreateDescriptorTranslator(_participant, _timeEventBus);
   _slaveLockstepTranslator  = TimeNetworkModule.CreateSlaveLockstepTranslator(_participant, _timeEventBus, iosNodeId);
   _timePulseIngressTranslator = TimeNetworkModule.CreateTimePulseIngressTranslator(_participant, _timeEventBus);
   ```

3. **Required namespaces** (add if not present):
   - `FDP.Toolkit.Time.Controllers` (for `SlaveSyncController`, `TimeConfig`)
   - `FDP.Toolkit.Time` (for `TimeNetworkModule`)
   - `Fdp.Kernel` (for `FdpEventBus`)
   - `Fdp.Interfaces` (for `IDescriptorTranslator`)

**Success conditions:**

- **TC2-P3-T1-SC1**: `ExConSubsystem` compiles without errors.
- **TC2-P3-T1-SC2**: `Initialize_DoesNotThrow` test passes.
- **TC2-P3-T1-SC3**: New test `ExCon_Initialize_CreatesSlaveTimeController` asserts that after `Initialize()`, `_slaveSyncController` is not null (via a new `internal` test hook property `TestHook_SlaveSyncController`).

**Test class:** `Hrot.ClusterRunner.Tests/ExConSubsystemTests.cs`

---

### TC2-P3-T2 — Drive time pipeline in ExConSubsystem.Update

**File:** `Hrot.ClusterRunner/Services/ExConSubsystem.cs`

**What to do:**

In `Update(float deltaTime)`, add the following block **before** the existing `_clusterSlave?.Tick()` call:

```csharp
// Time sync pipeline: ingest DDS → advance controller → egress ACKs → swap bus.
_timeModeTranslator?.PollIngress(null!, null!);
_slaveLockstepTranslator?.PollIngress(null!, null!);
_timePulseIngressTranslator?.PollIngress(null!, null!);
_slaveSyncController?.Update();
_slaveLockstepTranslator?.ScanAndPublish(null!);
_timeEventBus?.SwapBuffers();
```

**Success conditions:**

- **TC2-P3-T2-SC1**: `Update_MultipleFrames_Succeeds` unit test passes without exceptions.
- **TC2-P3-T2-SC2**: New test `ExCon_Update_DoesNotThrow_WithTimePipeline` creates an `ExConSubsystem`, calls `Initialize(headless config)`, calls `Update(0.016f)` 30 times — asserts no exception is thrown.
- **TC2-P3-T2-SC3**: In-process relay test (new): Create an `FdpEventBus` acting as the "network relay". Publish a `SwitchTimeModeEvent(Deterministic, barrier=0)` on the relay bus, run the ExCon's ingress translator against that bus, call `Update()` three times — assert `_slaveSyncController.GetMode() == TimeMode.Deterministic`.

**Test class:** `Hrot.ClusterRunner.Tests/ExConSubsystemTests.cs`

---

### TC2-P3-T3 — Wire SlaveSyncController into ExCon's UI cache

**File:** `Hrot.ClusterRunner/Services/ExConSubsystem.cs`

**What to do:**

1. The `_slaveSyncController` must be created **before** `_uiCache` in `Initialize()`. Adjust initialization order if necessary.

2. Change the `_uiCache` construction line to inject the controller:
   ```csharp
   _uiCache = new ClusterUiCache(_participant, _slaveSyncController);
   ```

**Success conditions:**

- **TC2-P3-T3-SC1**: New test `ExCon_UiCache_MasterSimTime_AdvancesWithController`: initialize ExCon headless, call `Update(0.016f)` for 100 frames feeding timing ticks via the slave's tick source, assert that `_uiCache.MasterSimTime` is greater than zero at the end (read via a test hook or `ClusterScenarioPanel` accessor).

  > Because true DDS-driven PLL requires network traffic, this test can verify the property via a test hook rather than via a full DDS round-trip. An alternative is to verify via checking `TestHook_SlaveSyncController.GetCurrentState().TotalTime > 0` after 100 frames.

**Test class:** `Hrot.ClusterRunner.Tests/ExConSubsystemTests.cs`

---

### TC2-P3-T4 — Remove redundant time ingress handlers from ExConSubsystem

**File:** `Hrot.ClusterRunner/Services/ExConSubsystem.cs`

**What to do:**

The existing `_timePulseHandler` and `_timeModeHandler` (of type `TimePulseIngressHandler` / `TimeModeIngressHandler`) feed `logic.OnTimePulse(pulse)` and `logic.OnTimeMode(mode)`. Now that `ExConSubsystem` has a `SlaveSyncController` owning the time state, and the UI cache reads from the controller, these handlers are redundant for time display.

Assess whether `ExConLogic.OnTimePulse` and `ExConLogic.OnTimeMode` are still used for other purposes (e.g. pausing game logic, updating internal state). If they are purely for display, remove:

- `_timePulseHandler` field and its instantiation.
- `_timeModeHandler` field and its instantiation.
- The `_ingressDisposables.Add(...)` calls for both.
- `TimePulseIngressHandler` and `TimeModeIngressHandler` constructor calls.

If `OnTimePulse` / `OnTimeMode` callbacks are still needed for non-display logic, retain the handlers but mark with a comment explaining the dual purpose.

**Success conditions:**

- **TC2-P3-T4-SC1**: All existing `ExConSubsystem` unit tests pass.
- **TC2-P3-T4-SC2**: `FullLifecycle_Headless_CompletesCleanly` integration test passes.
- **TC2-P3-T4-SC3**: No compile errors after removal.

**Note:** This task may be deferred to a follow-up if the impact on `ExConLogic` is not clear during the initial implementation batch. Log as technical debt if deferred.

---

## Appendix: Mock Helpers

### MinimalTimeController (test mock)

Use this minimal implementation in `ClusterUiCacheTests.cs` for TC2-P2-T1 tests:

```csharp
private sealed class FakeTimeController : ITimeController
{
    public double TotalTime { get; set; }
    public GlobalTime Update()           => GetCurrentState();
    public void SetTimeScale(float s)    { }
    public float GetTimeScale()          => 1f;
    public TimeMode GetMode()            => TimeMode.Continuous;
    public GlobalTime GetCurrentState()  => new GlobalTime { TotalTime = TotalTime };
    public void SeedState(GlobalTime s)  => TotalTime = s.TotalTime;
    public void Dispose()                { }
}
```
