<!--STATUS
state: HISTORICAL
updated: 2026-08-21
current-answer: the three features A/B/C as IMPLEMENTED intent.
stale-below: the header's "Status: Design phase" is stale — this shipped.
known-rot: feature C says ExCon "must participate in cluster lockstep". ⚠ Measured 2026-08-21: the
  orchestrator's roster filter is `SubsystemName is "SimHost" or "IG" or "CGF"`, which EXCLUDES
  ExCon, though ExConSubsystem does construct its own SlaveLockstepTranslator. Unreconciled — see
  Area H when it is next touched.
superseded-by: ../../blueprints/DESIGN_Time_Architecture.md for the current target.
known-conflict: none.
-->
# Time Control — Phase 2 Design Document

**Project:** `time-ctrl-2`  
**Status:** Design phase  
**Source:** [design_talk.md](./design_talk.md)

---

## 1. Overview

This workstream delivers three focused fixes/enhancements to the time-synchronisation subsystem that were identified during the design talk:

| # | Feature | Target area |
|---|---------|-------------|
| A | **Runtime Slave-Set Fix** — `MasterSyncController.SwitchToDeterministic` must use the slave-roster provided at call time rather than ignoring it. | `FDP.Toolkit.Time` |
| B | **Smooth SimTime UI** — `ClusterUiCache.MasterSimTime` must read from the local `ITimeController` on nodes that own one, eliminating the 1 Hz visual stutter. | `Hrot.ClusterRunner` |
| C | **ExCon Lockstep Participation** — `ExConSubsystem` must host a `SlaveSyncController` so it participates in cluster lockstep and provides smooth self-sourced time to its UI. | `Hrot.ClusterRunner` |

All three enhancements are independent and can be implemented in any order, but B depends on A conceptually (the ITimeController injection pattern is shared) and C depends on B (ExCon's UI cache is wired like the slave path of B).

---

## 2. Background & Architecture

### 2.1 Time Controller State Machine

Both `MasterSyncController` and `SlaveSyncController` implement a three-state machine:

```
Continuous → BarrierPending → Stepping → Continuous
```

- **Continuous** — real-time wall-clock; `TimePulseDescriptor` broadcast by master at ~1 Hz; slaves run PLL.
- **BarrierPending** — intermediate state after `SwitchToDeterministic`; PLL and time pulse continue until the virtual wall clock crosses the `BarrierWallTicks` value.
- **Stepping** — deterministic lockstep; master waits for ACKs from all registered slaves before advancing each frame; time pulse suppressed; virtual wall clock advanced by fixed math, NOT real-time.

### 2.2 Virtual Wall Clock vs. Hardware Clock

The **virtual wall clock** (`TotalWallTicks`) is the time base that drives simulation time:

- In **Continuous** mode it tracks real hardware time, periodically corrected via PLL driven by `TimePulseDescriptor`.
- In **Stepping** mode it advances by the deterministic constant `(long)(fixedDelta * Stopwatch.Frequency)` per step. The hardware clock is not read; hardware drift is irrelevant.
- The authoritative baseline is established at the moment the `BarrierWallTicks` is crossed: all nodes reach the same virtual wall tick value before the first lockstep frame, so their clocks are bit-identical throughout.

### 2.3 TimePulse Role

`TimePulseDescriptor` is the 1 Hz broadcast from master to slaves used for PLL-based clock syncing in **Continuous** mode only.

During **Stepping** mode the pulse is suppressed because:
- The simulation clock does not advance in real time; the PLL would misinterpret the frozen delta as a large error and slew aggressively.
- Determinism is guaranteed by fixed-increment math, not network-driven correction.

When the cluster returns to Continuous, the master's `SwitchToContinuous` event carries a `SimTimeSnapshot` that snaps every slave's time, and the PLL resumes "warm" (state is preserved across mode transitions).

### 2.4 Key Classes

| Class | Project | Role |
|-------|---------|------|
| `MasterSyncController` | `FDP.Toolkit.Time` | Authoritative master; publishes `TimePulseDescriptor`, `SwitchTimeModeEvent`, `AdvanceFrameIntent` |
| `SlaveSyncController` | `FDP.Toolkit.Time` | Slave PLL; consumes `TimePulseDescriptor`, `SwitchTimeModeEvent`, `AdvanceFrameIntent`; publishes `FrameStepCompletedEvent` |
| `ITimeController` | `ModuleHost.Core.Time` | Interface; `Update()`, `GetCurrentState()`, `GetMode()`, `SeedState()` |
| `ClusterUiCache` | `Hrot.ClusterRunner` | CQRS read-model; currently network-only; updated via DDS readers |
| `ClusterScenarioPanel` | `Hrot.ClusterRunner` | ImGui panel; reads from `ClusterUiCache` |
| `OrchestratorSubsystem` | `Hrot.ClusterRunner` | Hosts `MasterSyncController` |
| `ExConSubsystem` | `Hrot.ClusterRunner` | Instructor station; currently no time controller |
| `TimeNetworkModule` | `FDP.Toolkit.Time` | Factory for DDS translators |

---

## 3. Feature A — Runtime Slave-Set Fix

### 3.1 Problem

`MasterSyncController.SwitchToDeterministic(HashSet<int> slaveNodeIds)` has a doc comment stating:

> "Accepted for API compatibility with the coordinator pattern; the effective slave set used for ACK tracking is the one supplied at construction time."

This is a **bug**. The orchestrator always passes `new HashSet<int>()` at construction time (empty), then passes the real active roster at call time, which is silently discarded. Result: the master never waits for any slave ACKs — lockstep has no actual synchronization.

### 3.2 Fix

`SwitchToDeterministic` must replace `_expectedSlaves` with the runtime-provided set. Because `_expectedSlaves` is `readonly`, in-place mutation via `.Clear()` + `.UnionWith()` is used:

```csharp
public void SwitchToDeterministic(HashSet<int> slaveNodeIds)
{
    // Capture the runtime roster; overwrite the construction-time placeholder.
    _expectedSlaves.Clear();
    if (slaveNodeIds != null)
        _expectedSlaves.UnionWith(slaveNodeIds);

    long barrierWallTicks    = _totalWallTicks + _config.LookaheadWallTicks;
    _pendingBarrierWallTicks = barrierWallTicks;
    _mode                    = MasterMode.BarrierPending;

    _eventBus.Publish(new SwitchTimeModeEvent
    {
        TargetMode       = TimeMode.Deterministic,
        BarrierWallTicks = barrierWallTicks,
        FixedDelta       = _config.FixedDeltaSeconds,
        TimeScale        = _timeScale,
        SimTimeSnapshot  = 0,
    });
}
```

The fix cascades correctly: `Step()` re-arms `_pendingAcks` from `_expectedSlaves` on every call, so the updated set is immediately effective for the first step.

### 3.3 Constructor Change

The obsolete `slaveNodeIds` parameter at construction becomes meaningless for runtime scenarios. The constructor signature is preserved as-is for test compatibility, but the `OrchestratorSubsystem` no longer needs to pass a non-empty set at construction — passing `null` or an empty set is correct.

The misleading doc comment must be removed and replaced with the accurate description.

---

## 4. Feature B — Smooth SimTime Display on UI

### 4.1 Problem

`ClusterScenarioPanel` reads `MasterSimTime` from `ClusterUiCache`. The cache updates this only from incoming `TimePulseDescriptor` DDS messages, which the master broadcasts at 1 Hz. On the Orchestrator node, the master's internal `_totalTime` is updated every frame (~60 Hz), but the UI only sees stale 1-second-old snapshots.

### 4.2 Design

`ClusterUiCache` is modified to accept an optional `ITimeController` reference injected at construction:

- If `_localTimeController` is non-null, `MasterSimTime` returns `_localTimeController.GetCurrentState().TotalTime` directly.
- If null (remote node, no local controller), falls back to the 1 Hz network value stored in `_networkSimTime`.

The DDS `DrainTimePulse()` path continues to update `_networkSimTime` (the fallback) and other fields like `MasterWallTicks` and `MasterTimeScale`.

```csharp
// ClusterUiCache constructor signature change
public ClusterUiCache(DdsParticipant participant, ITimeController? localTimeController = null)

// Property change
public double MasterSimTime =>
    _localTimeController != null
        ? _localTimeController.GetCurrentState().TotalTime
        : _networkSimTime;
```

### 4.3 Wiring on Orchestrator

`OrchestratorSubsystem.Initialize()` must create `_masterSync` **before** `_uiCache` so it can be passed in:

```csharp
_eventBus   = new FdpEventBus();
_masterSync = new MasterSyncController(_eventBus, null, TimeConfig.Default);

// Pass master controller to cache — UI reads sim time directly from controller memory
_uiCache    = new ClusterUiCache(_participant, _masterSync);
```

`ClusterScenarioPanel` on the Orchestrator is instantiated via the `ClusterMaster` constructor path and already has access to the cache — no change needed there.

### 4.4 Wiring on Slave Nodes (SimHost, IG)

Slave subsystems that render `ClusterScenarioPanel` already have a `SlaveSyncController` managed by their `ModuleHostKernel`. To wire it into the same cache pattern, `SubsystemConfig` optionally provides the kernel's time controller reference. The slave subsystems call:

```csharp
_uiCache = new ClusterUiCache(_participant, _kernel.GetTimeController());
```

This is the optional extension; the primary change is the Orchestrator path (§4.3).

---

## 5. Feature C — ExCon Lockstep Participation

### 5.1 Problem

`ExConSubsystem` does not participate in cluster lockstep:
1. It has no `SlaveSyncController`, so the master's `FrameOrderDescriptor` packets are silently ignored.
2. It never sends `FrameAckDescriptor`, so if the Orchestrator includes ExCon's node ID in the slave roster, lockstep stalls forever.
3. Its UI cache reads `MasterSimTime` entirely from 1 Hz DDS pulses, so the Time Control panel stutters.

`SlaveSyncController` has **no dependency on ECS or `ModuleHostKernel`** — it only requires an `FdpEventBus`. This makes it directly usable in the non-ECS `ExConSubsystem`.

### 5.2 Design

Add to `ExConSubsystem`:

```csharp
private FdpEventBus?              _timeEventBus;
private SlaveSyncController?      _slaveSyncController;
private IDescriptorTranslator?    _timeModeTranslator;
private IDescriptorTranslator?    _lockstepTranslator;
private IDescriptorTranslator?    _timePulseIngressTranslator;
```

**Initialize:**

```csharp
var iosNodeId          = config.NodeId != 0 ? config.NodeId : 500;
_timeEventBus          = new FdpEventBus();
_slaveSyncController   = new SlaveSyncController(_timeEventBus, iosNodeId, TimeConfig.Default);
_timeModeTranslator    = TimeNetworkModule.CreateDescriptorTranslator(_participant, _timeEventBus);
_lockstepTranslator    = TimeNetworkModule.CreateSlaveLockstepTranslator(_participant, _timeEventBus, iosNodeId);
_timePulseIngressTranslator = TimeNetworkModule.CreateTimePulseIngressTranslator(_participant, _timeEventBus);

// Inject into UI cache for smooth time display
_uiCache = new ClusterUiCache(_participant, _slaveSyncController);
```

**Update** — the time pipeline must run each frame before existing ExCon logic:

```csharp
// 1. Pull network → event bus
_timeModeTranslator?.PollIngress(null!, null!);
_lockstepTranslator?.PollIngress(null!, null!);
_timePulseIngressTranslator?.PollIngress(null!, null!);

// 2. Advance slave time controller (PLL or lockstep step)
_slaveSyncController?.Update();

// 3. Push ACKs from event bus → network
_lockstepTranslator?.ScanAndPublish(null!);

// 4. Swap event bus buffers
_timeEventBus?.SwapBuffers();
```

**Shutdown:** dispose `_slaveSyncController`, null all translator references, dispose `_timeEventBus`.

### 5.3 Orchestrator Slave Roster

With the Feature A fix, the Orchestrator now uses the runtime roster. The `iosNodeId` of ExCon (default 500) will be included in `_clusterMaster.NodeRoster.ActiveNodes` when ExCon is running, meaning the master will correctly wait for ExCon's ACK during lockstep steps.

The `TimePulseIngressHandler` and `TimeModeIngressHandler` that currently exist in `ExConSubsystem` (the old path that fed `ExConLogic.OnTimePulse` / `OnTimeMode`) become redundant with Feature C and should be removed or left as legacy-only paths.

---

## 6. Phase / Task Breakdown

### Phase 1 — Fix Core Lockstep (Feature A)

| Task | Description |
|------|-------------|
| TC2-P1-T1 | Fix `MasterSyncController.SwitchToDeterministic` to capture runtime slave set |
| TC2-P1-T2 | Update `OrchestratorSubsystem.Initialize` — pass null/empty at construction |

### Phase 2 — Smooth UI Time (Feature B)

| Task | Description |
|------|-------------|
| TC2-P2-T1 | Add `ITimeController?` parameter to `ClusterUiCache` constructor; change `MasterSimTime` property |
| TC2-P2-T2 | Reorder `OrchestratorSubsystem.Initialize` and wire `_masterSync` into cache |
| TC2-P2-T3 | Wire slave controllers into `ClusterUiCache` on SimHost and IG subsystems (optional stretch) |

### Phase 3 — ExCon Lockstep (Feature C)

| Task | Description |
|------|-------------|
| TC2-P3-T1 | Add `SlaveSyncController` + translators to `ExConSubsystem` |
| TC2-P3-T2 | Drive time pipeline in `ExConSubsystem.Update` |
| TC2-P3-T3 | Wire `_slaveSyncController` into `_uiCache` |
| TC2-P3-T4 | Clean up redundant `TimePulseIngressHandler`/`TimeModeIngressHandler` paths |

---

## 7. Testing Strategy

All changes are covered by unit and/or integration tests as specified in [TASK-DETAIL.md](./TASK-DETAIL.md).

- **Feature A** — unit tests on `MasterSyncController` verifying that `_pendingAcks` reflects the runtime-provided slave set, not the construction-time set.
- **Feature B** — unit tests on `ClusterUiCache` verifying direct-read from injected controller vs. network fallback; no DDS required.
- **Feature C** — `ExConSubsystem` unit tests verifying that `SlaveSyncController` is initialized, updated, and ACKs correctly; integration tested via `HrotRunnerHarness`.

---

## 8. Files Affected

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs` | Feature A |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/MasterSyncControllerTests.cs` | Feature A tests |
| `Hrot.ClusterRunner/Services/ClusterUiCache.cs` | Feature B |
| `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` | Features A + B |
| `Hrot.ClusterRunner.Tests/ClusterUiCacheTests.cs` | Feature B tests |
| `Hrot.ClusterRunner/Services/ExConSubsystem.cs` | Feature C |
| `Hrot.ClusterRunner.Tests/ExConSubsystemTests.cs` | Feature C tests |
| `Hrot.ClusterRunner.Integration.Tests/TimeControlIntegrationTests.cs` | Integration coverage |
