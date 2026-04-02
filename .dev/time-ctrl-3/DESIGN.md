# Time Control — Phase 3 Design Document

**Project:** `time-ctrl-3`  
**Status:** Design phase  
**Source:** [design_talk.md](./design_talk.md)

---

## 1. Overview

This workstream upgrades the distributed time-synchronisation subsystem from a single-machine
loopback architecture to a genuine multi-process / multi-computer deterministic lockstep clock.
The core protocol is inspired by the C++ `SynClock` / `synclock.txt` reference (NTP-style
two-way handshake), mapped to the existing C# / CycloneDDS framework.

| # | Feature | Target area |
|---|---------|-------------|
| A | **NTP-Style Baseline Sync** — introduce `TimeSyncRequest` / `TimeSyncResponse` DDS topics and an NTP formula inside `SlaveSyncController` to establish a per-node `_masterWallClockOffset` | `FDP.Toolkit.Time` |
| B | **MasterSyncController Bug Fixes** — initialise `_totalWallTicks = now` (constructor) and populate `TargetSimTime = _totalTime` in `Step()` | `FDP.Toolkit.Time` |
| C | **SlaveSyncController NTP Integration** — `SyncedWallTicks` property; periodic re-sync; barrier and PLL evaluated in the synchronised time domain | `FDP.Toolkit.Time` |
| D | **Translators & Network Module** — `MasterTimeSyncTranslator`, `SlaveTimeSyncTranslator`, factory additions in `TimeNetworkModule` | `FDP.Toolkit.Time` |
| E | **Autonomous Multi-Computer Tests** — test suite that simulates offset OS clocks via injected tick sources; proves pause-barrier accuracy and step determinism before any application-layer wiring is touched | `FDP.Toolkit.Time.Tests` |
| F | **Application Integration Validation** — verify that the public `ITimeController` / `ISteppableTimeController` API surface is unchanged and that existing application integration tests stay green | `Hrot.ClusterRunner.Integration.Tests` |

Features A–D touch only `FDP.Toolkit.Time` (the pure toolkit, no application layer).  
Feature E is the self-contained test suite for A–D.  
Feature F confirms that the application layer (`Hrot.*`) is unaffected.

---

## 2. Background & Architecture

### 2.1 The Problem with the Current Implementation

The current implementation uses `Stopwatch.GetTimestamp()` — a hardware counter that starts from
OS boot time — both as the `_totalWallTicks` baseline on the master and as `_virtualWallTicks`
on the slave.  This causes three categories of bugs:

| Bug | Description | Symptom |
|-----|-------------|---------|
| **B-01** | `MasterSyncController` constructor leaves `_totalWallTicks = 0` | Continuous-mode wall-tick accumulation starts from zero, meaning the first call to `SwitchToDeterministic` (if it uses `_totalWallTicks`) would emit a near-zero barrier; corrected by `_totalWallTicks = now` in the constructor |
| **B-02** | `MasterSyncController.Step()` hard-codes `TargetSimTime = 0` | Each slave adds `fixedDelta` to its *individually drifted* `_totalTime` (which could already be 200 ms behind the master), locking an accumulating discrepancy into every step |
| **B-03** | `SlaveSyncController._virtualWallTicks` starts from the local OS boot-time ticks; barrier evaluation uses raw local ticks vs. master-sourced ticks | On multi-computer setups the comparison is garbage (different `Stopwatch.GetTimestamp()` origins); even on single-machine loopback the barrier mismatch produces an immediate false trigger |
| **B-04** | `MasterSyncController.SwitchToDeterministic` computes `BarrierWallTicks = _totalWallTicks + Lookahead`. During lockstep, `Step()` advances `_totalWallTicks` synthetically (faster or slower than real time). After step→resume→pause, `_totalWallTicks` is permanently decoupled from the physical OS clock while the slave's `SyncedWallTicks` tracks the *physical* OS clock. The barrier fires at the wrong physical moment. | Second and subsequent pauses (after any stepping session) desync by an amount proportional to the total stepping time |

### 2.2 The Solution — NTP-Style Offset Calculation

Instead of hoping that local OS clocks are comparable, we perform a brief two-way ping (similar to
NTP/PTP) to compute the exact offset between the Master's OS clock and each Slave's OS clock.

```
Client (Slave)                        Master
  |                                     |
  |-- TimeSyncRequest(ClientSendTicks) -->|   t1 = ClientSendTicks
  |                                     |   t2 = MasterReceiveTicks  (recorded immediately)
  |                                     |   t3 = MasterTransmitTicks (recorded just before reply)
  |<-- TimeSyncResponse(t1, t2, t3) ----|
  |   t4 = localReceiveTicks            |
  |                                     |
  RTT    = (t4 - t1) - (t3 - t2)
  Offset = ((t2 - t1) + (t3 - t4)) / 2
```

The slave adds `_masterWallClockOffset` to every local `_getTick()` read, giving it a
`SyncedWallTicks` value that lives in the master's time domain.

Once `SyncedWallTicks` is accurate:
- The `BarrierWallTicks` issued by the master (absolute OS ticks) is correctly evaluated on the slave.
- The Time Pulse network transit correction (`timeSincePulse`) uses matching tick domains.

### 2.3 Relationship to the Existing Architecture

The new architecture is **backward-compatible** at the ITimeController API level:

```
┌───────────────────────────────────────────────────────┐
│  Application Layer (Hrot.* — UNCHANGED)               │
│  OrchestratorSubsystem, ExConSubsystem, IgApplication  │
└──────────────┬────────────────────────────────────────┘
               │  ISteppableTimeController / ITimeController
               │  (unchanged interfaces)
┌──────────────▼────────────────────────────────────────┐
│  FDP.Toolkit.Time (UPDATED)                            │
│  MasterSyncController  ←── TC3-P2 fixes               │
│  SlaveSyncController   ←── TC3-P3 NTP handshake       │
│  MasterTimeSyncTranslator  ←── TC3-P4 new             │
│  SlaveTimeSyncTranslator   ←── TC3-P4 new             │
│  TimeMessages: TimeSyncRequest/Response  ←── TC3-P1   │
└──────────────┬────────────────────────────────────────┘
               │  DDS (CycloneDDS)
┌──────────────▼───────────────────┐
│  Network (unchanged wire topics) │
└──────────────────────────────────┘
```

### 2.4 Key Clock Concepts

| Concept | Owner | Description |
|---------|-------|-------------|
| `_getTick()` | both controllers | Raw OS `Stopwatch.GetTimestamp()`; different baseline per machine |
| `SyncedWallTicks` | `SlaveSyncController` | `_getTick() + _masterWallClockOffset`; lives in master's OS-tick domain |
| `_totalWallTicks` | `MasterSyncController` | Cumulative wall ticks initialised to `now` at construction — used only for **continuous-mode time accumulation**. Must **not** be used for barrier issuance because `Step()` advances it synthetically, permanently decoupling it from physical time after any stepping session. |
| `BarrierWallTicks` | wire: `SwitchTimeModeEvent` | Absolute physical OS-tick value computed as `_getTick() + LookaheadWallTicks` at the precise moment `SwitchToDeterministic` is called. Evaluated against `_getTick()` on the master and against `SyncedWallTicks` on the slave — both live in the master's physical-clock domain. |
| `_masterWallClockOffset` | `SlaveSyncController` | The offset applied to raw local ticks to convert them to master OS-tick domain |
| `_isTimeSynced` | `SlaveSyncController` | `false` until the first valid `TimeSyncResponse` is received. While `false`, all `TimePulseDescriptor` and `SwitchTimeModeEvent` processing is silently suppressed to prevent garbage network-transit-time calculations from corrupting `_totalTime`. |

### 2.5 Debug Logging Requirements

All affected classes must emit structured debug log lines at every significant state transition
and at every sync event.  This is essential for diagnosing timing issues during development and
in the field.

**Required log points:**

| Where | Log |
|-------|-----|
| `MasterSyncController` ctor | `[TC3][Master] Initialized. _totalWallTicks={0}, Stopwatch.Frequency={1}` |
| `MasterSyncController.SwitchToDeterministic` | `[TC3][Master] PAUSE issued. BarrierTicks={0}, SimTime={1:hh:mm:ss.fff}` |
| `MasterSyncController.Step` | `[TC3][Master] STEP #{0}. TargetSimTime={1:hh:mm:ss.fff}, Delta={2:F4}s, AwaitingACKs=[{3}]` |
| `MasterSyncController.UpdateStepping` | `[TC3][Master] ACKs remaining={0}` (only when count changes) |
| `MasterTimeSyncTranslator.PollIngress` (per request) | `[TC3][MasterSync] Received TimeSyncRequest from node={0}, ClientSendTicks={1}. Responding with MasterRx={2}, MasterTx={3}` |
| `SlaveSyncController` ctor | `[TC3][Slave#{0}] Initialized. _virtualWallTicks={1}` |
| `SlaveSyncController.ProcessTimePulses` (while unsynced) | `[TC3][Slave#{0}] Ignoring TimePulse (not yet time-synced)` |
| `SlaveSyncController.DrainModeSwitchEvents` (while unsynced) | `[TC3][Slave#{0}] Ignoring SwitchTimeModeEvent (not yet time-synced)` |
| `SlaveSyncController.DrainTimeSyncResponses` (per accepted response) | `[TC3][Slave#{0}] RTT={1:F3}ms, Offset={2} ticks ({3:F3}ms). New SyncedWallTicks base updated.` |
| `SlaveSyncController.DrainTimeSyncResponses` (per rejected) | `[TC3][Slave#{0}] Discarded sync response: RTT={1:F3}ms exceeds max={2:F3}ms` |
| `SlaveSyncController.UpdateBarrierPending` (on barrier trigger) | `[TC3][Slave#{0}] BARRIER HIT. SyncedWallTicks={1}, BarrierWallTicks={2}. Entering Stepping.` |
| `SlaveSyncController.UpdateStepping` (per consumed intent) | `[TC3][Slave#{0}] STEP #{1}. SnappedSimTime={2:hh:mm:ss.fff}, Delta={3:F4}s. Sending ACK.` |
| `SlaveSyncController.OnTimePulseReceived` | `[TC3][Slave#{0}] PULSE. MasterWallTicks={1}, SyncedNow={2}, timeSince={3:F3}ms, simError={4:F3}ms, correction={5:F4}` |

All log calls use `FdpLog<T>.Debug(...)` so they can be disabled in release builds.

---

## 3. Feature A — NTP Message Types & TimeConfig Additions

### 3.1 New DDS Structs

Add two new structs to `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs`:

- **`TimeSyncRequest`** — published by each slave node on startup and periodically (~1 Hz) to the `"TimeSyncRequest"` DDS topic.  Carries the slave's local tick at send time.
- **`TimeSyncResponse`** — published by the master node, echoing back the slave's send ticks plus the master's receive and transmit ticks.  Addressed by `ClientNodeId`.

Both structs are also registered as `[EventId]` events so they can flow through `FdpEventBus`.

Event IDs to assign: 108 (`TimeSyncRequest`), 109 (`TimeSyncResponse`).

### 3.2 TimeConfig Additions

Add to `TimeConfig`:

```csharp
/// Maximum acceptable Round-Trip Time for a TimeSyncResponse.
/// Responses whose RTT exceeds this value are discarded (spike rejection).
/// Default: 200 ms expressed in Stopwatch ticks.
public long MaxRttTicks { get; set; } = (long)(0.2 * Stopwatch.Frequency);

/// How often (in ticks) the slave re-sends a TimeSyncRequest to correct hardware clock skew.
/// Default: 1 second.
public long SyncRefreshIntervalTicks { get; set; } = Stopwatch.Frequency;

/// Weight applied to incremental sync offset updates (0.0 – 1.0).
/// 1.0 = hard-snap every time; 0.1 = gentle steering (default).
public double SyncCorrectionWeight { get; set; } = 0.1;
```

---

## 4. Feature B — MasterSyncController Bug Fixes

### 4.1 Constructor Fix (_totalWallTicks = now)

Current code leaves `_totalWallTicks` at its default value of `0`.  This causes the
continuous-mode wall-clock accumulation (`_totalWallTicks += elapsedTicks` in `UpdateContinuous`)
to start from zero rather than from the current physical time, which in turn means the
`SwitchToDeterministic` call on any pre-existing version that uses `_totalWallTicks` as the
barrier base would issue a near-zero barrier.

**Fix:** Add `_totalWallTicks = now;` at the end of the constructor, immediately after
`_lastPulseTicks = now` and `_lastTickSample = now`.

**Important scope note:** This fix corrects continuous-mode accumulation fidelity only.  The
barrier issuance itself is fixed separately in §4.4 — it now calls `_getTick()` directly and
is *independent* of `_totalWallTicks`.

### 4.2 Step() Fix (TargetSimTime = _totalTime)

Current code passes `TargetSimTime = 0` in the `AdvanceFrameIntent`, causing each slave to
accumulate its own locally-drifted `_totalTime += delta` instead of snapping to the master's
authoritative time.

**Fix:** Change the intent to `TargetSimTime = _totalTime` after the master increments it
inside `Step()`.

### 4.3 Debug Logging

Add `FdpLog<MasterSyncController>.Debug(...)` calls at all the points listed in §2.5.

### 4.4 Physical Clock Barrier Fix (Critical)

The existing `SwitchToDeterministic` computes the barrier as:

```csharp
long barrierWallTicks = _totalWallTicks + _config.LookaheadWallTicks;
```

During lockstep stepping, `_totalWallTicks += (long)(fixedDelta * Stopwatch.Frequency)` runs
synthetically and may run much faster or slower than wall time.  After step→resume→pause, the
cumulative `_totalWallTicks` permanently diverges from `_getTick()`.  Because the slave evaluates
the barrier against `SyncedWallTicks` (which is anchored to the *physical* OS clock), using a
synthetic `_totalWallTicks` as the barrier base causes the second (and every subsequent) pause to
fire at the wrong physical moment.

**Fix A — `SwitchToDeterministic`:**

```csharp
// Always base the barrier on the physical hardware clock, never on synthetic _totalWallTicks
long barrierWallTicks = _getTick() + _config.LookaheadWallTicks;
```

**Fix B — `UpdateBarrierPending` (master side):**

The master itself must also evaluate the barrier against the physical clock:

```csharp
// FIX: evaluate against physical clock, not synthetic _totalWallTicks
if (_getTick() >= _pendingBarrierWallTicks)
{
    _mode        = MasterMode.Stepping;
    _pendingAcks = new HashSet<int>();
}
```

With these two changes, `BarrierWallTicks` is always an absolute physical-clock timestamp, and
both master and slave evaluate it against their respective physical clocks (`_getTick()` on
master, `SyncedWallTicks = _getTick() + offset` on slave).

---

## 5. Feature C — SlaveSyncController NTP Handshake

### 5.1 New Fields

```
_masterWallClockOffset : long   — offset to add to raw ticks to enter master's domain
_lastSyncRequestTicks  : long   — OS tick when the last TimeSyncRequest was sent
_isTimeSynced          : bool   — true once the first valid TimeSyncResponse has been processed;
                                  suppresses all pulse and mode-switch processing until set
```

### 5.2 SyncedWallTicks Property

```csharp
public long SyncedWallTicks => _getTick() + _masterWallClockOffset;
```

This is the slave's view of the master's current wall clock.

### 5.3 Constructor Changes

1. Register **both** `TimeSyncRequest` (EventId 108) and `TimeSyncResponse` (EventId 109) with
   the event bus.
   - `TimeSyncResponse` must be registered so the controller can `Consume<TimeSyncResponse>()`.
   - `TimeSyncRequest` must be registered so the `SlaveTimeSyncTranslator` can
     `Consume<TimeSyncRequest>()` from the same bus and forward it over DDS.
2. Compute `_maxRttTicks` from `_config.MaxRttTicks`.
3. Initialise `_isTimeSynced = false`.
4. Call `SendTimeSyncRequest()` immediately upon construction so the first baseline is established
   as early as possible.

### 5.4 Update() Changes

1. Call `DrainTimeSyncResponses()` at the very top of `Update()`, before everything else.
2. Issue a new `TimeSyncRequest` if `_getTick() - _lastSyncRequestTicks > _config.SyncRefreshIntervalTicks`.
3. **Late-joiner drain — `UpdateContinuous` and `UpdateBarrierPending`:** A slave that boots
   while the master is already in Stepping mode will miss the original `SwitchTimeModeEvent` and
   will therefore stay in `Continuous` mode.  The master continues broadcasting
   `FrameOrderDescriptor` DDS messages; the `SlaveLockstepTranslator` ingresses these and places
   `AdvanceFrameIntent` objects onto the bus.  Because neither `UpdateContinuous` nor
   `UpdateBarrierPending` consumes managed events, these intents accumulate indefinitely.

   Fix: drain (and silently discard) `AdvanceFrameIntent` objects at the top of both methods:

   ```csharp
   private GlobalTime UpdateContinuous()
   {
       // Drain stray step orders — prevents memory leak when slave misses the Pause command
       _eventBus.ConsumeManaged<AdvanceFrameIntent>();

       ProcessTimePulses();
       // ... rest unchanged ...
   }
   ```

   Apply the same one-liner drain at the top of `UpdateBarrierPending()`.

4. **Pre-sync guard — `ProcessTimePulses`:** discard all `TimePulseDescriptor` events while
   `_isTimeSynced == false`.  If a pulse arrives before the baseline is established, the
   `timeSincePulse` calculation (`SyncedWallTicks - pulse.MasterWallTicks`) will still use an
   offset of zero, yielding a large garbage error that the `JitterFilter` will treat as a valid
   large drift and hard-snap `_totalTime` to a corrupted value.

   ```csharp
   private void ProcessTimePulses()
   {
       var pulses = _eventBus.Consume<TimePulseDescriptor>();
       if (!_isTimeSynced)
       {
           FdpLog<SlaveSyncController>.Debug(
               "[TC3][Slave#{0}] Ignoring TimePulse (not yet time-synced)", _localNodeId);
           return;
       }
       foreach (var pulse in pulses) OnTimePulseReceived(pulse);
   }
   ```

4. **Pre-sync guard — `DrainModeSwitchEvents`:** similarly discard `SwitchTimeModeEvent` while
   `_isTimeSynced == false`.  A pause command received before the NTP baseline is established
   would evaluate the barrier against an unsynchronised `SyncedWallTicks`, potentially triggering
   an immediate (or permanently-delayed) barrier transition.

   ```csharp
   private void DrainModeSwitchEvents()
   {
       var events = _eventBus.Consume<SwitchTimeModeEvent>();
       if (!_isTimeSynced)
       {
           FdpLog<SlaveSyncController>.Debug(
               "[TC3][Slave#{0}] Ignoring SwitchTimeModeEvent (not yet time-synced)", _localNodeId);
           return;
       }
       // ... existing processing ...
   }
   ```

### 5.5 DrainTimeSyncResponses()

For each `TimeSyncResponse` addressed to `_localNodeId`:

```
t4        = _getTick()
rtt       = (t4 - response.ClientSendTicks) - (response.MasterTransmitTicks - response.MasterReceiveTicks)
if rtt > _config.MaxRttTicks → discard (log warning)
newOffset = ((response.MasterReceiveTicks - response.ClientSendTicks) +
             (response.MasterTransmitTicks - t4)) / 2
if _masterWallClockOffset == 0 (first sync) OR |newOffset - _masterWallClockOffset| > Stopwatch.Frequency
    _masterWallClockOffset = newOffset          (hard-snap)
else
    _masterWallClockOffset += (long)((newOffset - _masterWallClockOffset) * _config.SyncCorrectionWeight)   (gentle steer)

_isTimeSynced = true    ← set on every accepted response (first valid sync unlocks pulse/event processing)
```

### 5.6 UpdateBarrierPending() Fix

Replace `_virtualWallTicks >= _pendingBarrierWallTicks` with `SyncedWallTicks >= _pendingBarrierWallTicks`.

### 5.7 OnTimePulseReceived() Fix

Replace the line:

```csharp
long currentAbsTicks = _getTick();
```

with:

```csharp
long currentAbsTicks = SyncedWallTicks;
```

This ensures the `timeSincePulse` calculation (`currentAbsTicks - pulse.MasterWallTicks`) is
performed entirely within the master's time domain, giving a clean, network-transit-time-only
delta rather than a garbage inter-machine clock difference.

### 5.8 Debug Logging

Add `FdpLog<SlaveSyncController>.Debug(...)` at all points listed in §2.5.

---

## 6. Feature D — Translators & Network Module

### 6.1 MasterTimeSyncTranslator

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/MasterTimeSyncTranslator.cs`

A new `IDescriptorTranslator` that:
- Subscribes to the `"TimeSyncRequest"` DDS topic.
- On each ingress `TimeSyncRequest`, immediately records `masterReceiveTicks = _getTick()`,
  constructs a `TimeSyncResponse`, records `masterTransmitTicks = _getTick()`, and writes it
  back over DDS — all within the same poll call to minimise master-side processing latency.
- Does NOT route through `FdpEventBus`; the round-trip latency must be as small as possible.

### 6.2 SlaveTimeSyncTranslator

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/SlaveTimeSyncTranslator.cs`

A new `IDescriptorTranslator` that:
- On `ScanAndPublish`: drains `TimeSyncRequest` events from the local `FdpEventBus` and writes
  them to the `"TimeSyncRequest"` DDS topic.
- On `PollIngress`: subscribes to `"TimeSyncResponse"` DDS topic; for samples whose
  `ClientNodeId == _localNodeId`, publishes the response onto the local `FdpEventBus`.
- Filters responses for the wrong node ID silently (enables multiple slaves on the same DDS domain).

### 6.3 TimeNetworkModule Additions

Add to `TimeNetworkModule.cs`:

```csharp
public static IDescriptorTranslator CreateMasterTimeSyncTranslator(DdsParticipant? participant);
public static IDescriptorTranslator CreateSlaveTimeSyncTranslator(
    DdsParticipant? participant, FdpEventBus eventBus, int localNodeId);
```

> **Note to integrators:** these translators must be added to the `customTranslators` list of the
> master node (SimHost / Orchestrator) and each slave node respectively.  A follow-up integration
> task (TC3-P6) verifies the wiring in `OrchestratorSubsystem`, `IgApplication`, and
> `ExConSubsystem`.  The application-layer changes are deliberately kept out of scope for Phase 1–5
> to allow the toolkit to be fully tested in isolation first.

---

## 7. Feature E — Autonomous Multi-Computer Unit Tests

This is the largest and most important phase.  All tests live in
`FDP/Toolkits/FDP.Toolkit.Time.Tests/` and use **only** injected tick sources — no DDS, no
threads, no `Thread.Sleep`.  The injected tick source pattern (already established in
`UnifiedControllerE2ETests`) is extended to simulate separate OS-clock domains with configurable
offsets and different tick rates, proving the correctness of the new sync logic.

Test classes:

| Class | ID range | Focus |
|-------|----------|-------|
| `TimeSyncOffsetTests` | TC3-P5-T01 | RTT formula, offset calculation, spike rejection, gentle steering |
| `PauseBarrierSyncTests` | TC3-P5-T02 | Barrier fires at same simtime on master + 2 slaves with simulated inter-machine offset |
| `LockstepSimTimeAccuracyTests` | TC3-P5-T03 | TotalTime is bit-identical on master + slaves after each step |
| `FullCycleMultiComputerSim` | TC3-P5-T04 | Full continuous→pause→step×5→resume with simulated multi-machine clock offsets |
| `ClockSkewDriftTests` | TC3-P5-T05 | Periodic re-sync keeps offset error bounded when slave ticks at slightly wrong rate |

See §11 and [TASK-DETAIL.md](./TASK-DETAIL.md) for exact test method names and success conditions.

---

## 8. Feature F — Application Integration Validation

### 8.1 API Compatibility

The following public API surface must remain unchanged:

- `ITimeController`: `Update()`, `GetCurrentState()`, `GetMode()`, `GetTimeScale()`, `SetTimeScale()`, `SeedState()`, `Dispose()`
- `ISteppableTimeController`: extends `ITimeController` + `Step(float fixedDelta)`, `SwitchToDeterministic(HashSet<int>)`, `SwitchToContinuous(float)`
- `MasterSyncController` constructor: `(FdpEventBus, HashSet<int>?, TimeConfig?, Func<long>?)`
- `SlaveSyncController` constructor: `(FdpEventBus, int, TimeConfig?, Func<long>?)`

> The new `SlaveSyncController` constructor **does not add new required parameters**.  The
> `localNodeId` parameter already existed.  All new behaviour is opt-in through `TimeConfig`.

### 8.2 Integration Test Regression

The existing integration test `TimeControlIntegrationTests` must continue to pass without
modification.  Any failing test indicates an unintended regression in the application-layer wiring.

---

## 9. Implementation Phases and Dependencies

```
Phase 1  (TC3-P1)  Messages + TimeConfig          ← no dependencies
    │
    ▼
Phase 2  (TC3-P2)  MasterSyncController fixes      ← depends on Phase 1 (TimeSyncRequest event ID)
    │
    ▼
Phase 3  (TC3-P3)  SlaveSyncController NTP         ← depends on Phase 1 + 2
    │
    ▼
Phase 4  (TC3-P4)  Translators + NetworkModule     ← depends on Phase 1 + 3
    │
    ▼
Phase 5  (TC3-P5)  Multi-computer unit tests       ← depends on Phase 2 + 3
    │
    ▼
Phase 6  (TC3-P6)  Application integration check   ← depends on Phase 2 + 3 + 4
```

---

## 10. Files Affected

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs` | Add `TimeSyncRequest`, `TimeSyncResponse` |
| `FDP/Toolkits/FDP.Toolkit.Time/Controllers/TimeConfig.cs` | Add `MaxRttTicks`, `SyncRefreshIntervalTicks`, `SyncCorrectionWeight` |
| `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs` | Constructor fix, `Step()` fix, debug logging |
| `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs` | NTP handshake fields, `SyncedWallTicks`, `DrainTimeSyncResponses`, barrier fix, PLL fix, debug logging |
| `FDP/Toolkits/FDP.Toolkit.Time/Translators/MasterTimeSyncTranslator.cs` | **New file** |
| `FDP/Toolkits/FDP.Toolkit.Time/Translators/SlaveTimeSyncTranslator.cs` | **New file** |
| `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs` | Add two factory methods |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeSyncOffsetTests.cs` | **New test file** |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/PauseBarrierSyncTests.cs` | **New test file** |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/LockstepSimTimeAccuracyTests.cs` | **New test file** |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/FullCycleMultiComputerSim.cs` | **New test file** |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/ClockSkewDriftTests.cs` | **New test file** |
| `Hrot.ClusterRunner.Integration.Tests/TimeControlIntegrationTests.cs` | Regression guard (no changes needed unless a test currently fails) |

---

## 11. Test Architecture Notes

### 11.1 Simulating Multi-Computer Clock Offset

Use separate `long` counters as tick sources.  To simulate two machines whose OS clocks are offset
by a large amount, initialise the slave's counter at a different starting value:

```csharp
long masterTick = 0L;
long slaveTick  = 500_000_000L;  // slave OS started long before master in this sim
long ticksPerFrame = Stopwatch.Frequency / 60;

Func<long> masterClock = () => masterTick;
Func<long> slaveClock  = () => slaveTick;

// Advance both independently each frame:
void Tick() { masterTick += ticksPerFrame; slaveTick += ticksPerFrame; }
```

After the NTP handshake, `slaveClock() + slave._masterWallClockOffset` should equal
`masterClock()` to within ±1 tick.

### 11.2 Simulating Network Latency

To simulate one-way network latency, delay injection of the `TimeSyncResponse` into the slave's
bus by a measured number of ticks before calling the slave's `Update()`:

```csharp
// After master generates the response, advance clocks by latency ticks
// before delivering the response to the slave bus
slaveTick += latencyTicks;
masterTick += latencyTicks;
slaveBus.Publish(response);
```

### 11.3 Simulating Clock Skew (Different Tick Rates)

Advance the slave ticks at a slightly different rate:

```csharp
masterTick += 1000L;
slaveTick  += 1001L;  // slave runs 0.1% fast
```

Run for many frames and verify that periodic re-sync keeps the `_masterWallClockOffset` correcting the drift.
