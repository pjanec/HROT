# Time Control Phase 3 — Task Detail Document

**Reference:** See [DESIGN.md](./DESIGN.md) for architecture context and rationale.  
**Tracker:** See [TASK-TRACKER.md](./TASK-TRACKER.md) for progress status.

---

## Phase 1 — Message Types & Configuration

Design reference: [DESIGN.md §3](./DESIGN.md#3-feature-a--ntp-message-types--timeconfig-additions)

---

### TC3-P1-T01 — Add TimeSyncRequest and TimeSyncResponse DDS structs

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs`

**What to do:**

Add the following two structs to the namespace `FDP.Toolkit.Time.Messages`, at the end of the
existing file (after the last existing struct):

```csharp
[MessagePackObject]
[DdsTopic("TimeSyncRequest")]
[EventId(108)]
public partial struct TimeSyncRequest
{
    /// <summary>Node ID of the slave initiating the handshake.</summary>
    [Key(0)] [DdsId(0), DdsKey]
    public int ClientNodeId;

    /// <summary>Raw OS tick (<c>Stopwatch.GetTimestamp()</c>) recorded just before publish.</summary>
    [Key(1)] [DdsId(1)]
    public long ClientSendTicks;
}

[MessagePackObject]
[DdsTopic("TimeSyncResponse")]
[EventId(109)]
public partial struct TimeSyncResponse
{
    /// <summary>Echoed back from the request — identifies the slave this reply is addressed to.</summary>
    [Key(0)] [DdsId(0), DdsKey]
    public int ClientNodeId;

    /// <summary>Echoed back from the request.</summary>
    [Key(1)] [DdsId(1)]
    public long ClientSendTicks;

    /// <summary>Master OS tick recorded immediately upon receiving the request.</summary>
    [Key(2)] [DdsId(2)]
    public long MasterReceiveTicks;

    /// <summary>Master OS tick recorded immediately before writing the response to DDS.</summary>
    [Key(3)] [DdsId(3)]
    public long MasterTransmitTicks;
}
```

**Note on EventId:** do not reuse any existing IDs.  Check `TimeMessages.cs` — IDs 100–107 are
taken.  108 and 109 are free.

**Success conditions (unit tests in `TimeMessagesTests.cs`):**

- **TC3-P1-T01-SC1**: Round-trip MessagePack serialisation of `TimeSyncRequest` preserves
  `ClientNodeId` and `ClientSendTicks` exactly.
  Test method: `TimeSyncRequest_RoundTrip_PreservesAllFields`

- **TC3-P1-T01-SC2**: Round-trip MessagePack serialisation of `TimeSyncResponse` preserves
  all four fields exactly.
  Test method: `TimeSyncResponse_RoundTrip_PreservesAllFields`

- **TC3-P1-T01-SC3**: Publish a `TimeSyncRequest` onto a fresh `FdpEventBus` (registered with
  `EventId(108)`), swap buffers, consume — the same struct is returned.
  Test method: `TimeSyncRequest_FdpEventBus_PublishConsume_RoundTrip`

- **TC3-P1-T01-SC4**: Same as SC3 but for `TimeSyncResponse`.
  Test method: `TimeSyncResponse_FdpEventBus_PublishConsume_RoundTrip`

**Test class:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeMessagesTests.cs`  
(Add the new test methods to the existing file.)

---

### TC3-P1-T02 — Add TimeConfig properties for NTP sync

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/TimeConfig.cs`

**What to do:**

Add the following three properties to the `TimeConfig` class, with XML doc comments:

```csharp
/// <summary>
/// Maximum acceptable Round-Trip Time for a <see cref="Messages.TimeSyncResponse"/>.
/// Responses whose RTT exceeds this value are discarded (spike rejection).
/// Default: 200 ms expressed as Stopwatch ticks.
/// </summary>
public long MaxRttTicks { get; set; } = (long)(0.2 * Stopwatch.Frequency);

/// <summary>
/// How often (in Stopwatch ticks) the slave re-sends a <see cref="Messages.TimeSyncRequest"/>
/// to correct hardware clock skew across long simulation sessions.
/// Default: 1 second.
/// </summary>
public long SyncRefreshIntervalTicks { get; set; } = Stopwatch.Frequency;

/// <summary>
/// Weight applied to incremental sync offset updates (range 0.0–1.0).
/// 1.0 = hard-snap every response; 0.1 (default) = gentle steering.
/// </summary>
public double SyncCorrectionWeight { get; set; } = 0.1;
```

**Success conditions:**

- **TC3-P1-T02-SC1**: `TimeConfig.Default.MaxRttTicks` equals `(long)(0.2 * Stopwatch.Frequency)`
  (approximately 2,000,000 ticks on a 10 MHz Stopwatch).
  Test method: `TimeConfig_Default_MaxRttTicks_IsApproximately200ms`

- **TC3-P1-T02-SC2**: `TimeConfig.Default.SyncRefreshIntervalTicks` equals `Stopwatch.Frequency`.
  Test method: `TimeConfig_Default_SyncRefreshIntervalTicks_Is1Second`

- **TC3-P1-T02-SC3**: `TimeConfig.Default.SyncCorrectionWeight` equals `0.1`.
  Test method: `TimeConfig_Default_SyncCorrectionWeight_IsPointOne`

**Test class:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeControllerFactoryTests.cs`  
(Or add a dedicated `TimeConfigTests.cs` if that is cleaner.)

---

## Phase 2 — MasterSyncController Bug Fixes

Design reference: [DESIGN.md §4](./DESIGN.md#4-feature-b--mastersynccollroller-bug-fixes)

---

### TC3-P2-T01 — Fix MasterSyncController constructor: initialise _totalWallTicks

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs`

**What to do:**

Locate the constructor.  At the point where `now` is captured (i.e. `long now = _getTick();`),
also assign:

```csharp
_totalWallTicks = now;
```

Add this line immediately after `_lastTickSample = now` and before the closing brace.

Also add the constructor debug log line:

```csharp
FdpLog<MasterSyncController>.Debug(
    "[TC3][Master] Initialized. _totalWallTicks={0}, Stopwatch.Frequency={1}",
    _totalWallTicks, Stopwatch.Frequency);
```

**Why:** Without this initialisation, `_totalWallTicks` defaults to `0`.  Continuous-mode
accumulation (`_totalWallTicks += elapsedTicks`) therefore starts from zero, causing the
cumulative wall-clock value to be meaningless relative to physical time.  This also matters for
the `SeedState` path (which reads `_totalWallTicks`) and for any diagnostic / UI code that
exposes `TotalWallTicks`.

> **Note:** This fix does **not** change how the barrier is issued.  The barrier calculation is
> fixed separately in TC3-P2-T04, which replaces the `_totalWallTicks`-based barrier with a
> direct `_getTick()` call.  The two fixes are independent and both required.

**Success conditions (unit tests in `MasterSyncControllerTests.cs`):**

- **TC3-P2-T01-SC1**: Construct a `MasterSyncController` with a controlled tick source starting
  at `1_000_000L`.  Immediately call `GetCurrentState()`.  Verify that `TotalWallTicks` equals
  `1_000_000L` (not `0`).
  Test method: `MasterSyncController_Constructor_TotalWallTicks_InitialisedToNow`

- **TC3-P2-T01-SC2**: With tick source starting at `1_000_000L` and `LookaheadWallTicks = 500_000L`,
  call `SwitchToDeterministic(empty_set)`.  Inspect the `SwitchTimeModeEvent` published to the bus.
  Assert `BarrierWallTicks >= 1_500_000L` (i.e. `>= now + lookahead`).
  Test method: `MasterSyncController_SwitchToDeterministic_BarrierIsAbsoluteNowPlusLookahead`

- **TC3-P2-T01-SC3** (regression): Construct a `SlaveSyncController` with tick source also
  starting at `1_000_000L`.  Feed the `SwitchTimeModeEvent` from the master bus into the slave bus.
  Advance ticks by `499_000L` and call slave `Update()`.  Slave must still be in `BarrierPending`
  mode (not yet crossed barrier).  Advance by another `1_100L`; call `Update()`.  Slave must now
  be in `Stepping` mode.  This proves the barrier is absolute and that single-machine loopback
  still works after the fix.
  Test method: `MasterSyncController_BarrierFix_SlaveEntersStepping_AfterLookahead`

---

### TC3-P2-T04 — Fix SwitchToDeterministic and UpdateBarrierPending to use the physical clock

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs`

**What to do:**

**Part 1 — `SwitchToDeterministic`:**

Locate the line that computes the barrier:

```csharp
long barrierWallTicks = _totalWallTicks + _config.LookaheadWallTicks;
```

Replace it with:

```csharp
// Always use the physical clock for the barrier; _totalWallTicks may have drifted
// synthetically during previous lockstep sessions.
long barrierWallTicks = _getTick() + _config.LookaheadWallTicks;
```

**Part 2 — `UpdateBarrierPending`:**

Locate the barrier-check condition inside `UpdateBarrierPending`:

```csharp
if (_totalWallTicks >= _pendingBarrierWallTicks)
```

Replace it with:

```csharp
// Evaluate against the physical clock.  _totalWallTicks may be synthetic after stepping.
if (_getTick() >= _pendingBarrierWallTicks)
```

**Why this is a separate fix from TC3-P2-T01:**  
`_totalWallTicks` is incremented both by real elapsed ticks (in continuous mode) and by the
synthetic `(long)(fixedDelta * Stopwatch.Frequency)` in `Step()`.  When stepping executes
faster or slower than real time, `_totalWallTicks` permanently decouples from the physical OS
clock.  After step→resume→pause, the barrier would be based on a synthetic timestamp while
the slave evaluates it against `SyncedWallTicks` (rooted to physical time), breaking the second
pause.  Using `_getTick()` ensures the barrier is always an absolute physical OS timestamp,
regardless of how many stepping sessions have occurred.

**Success conditions (unit tests in `MasterSyncControllerTests.cs`):**

- **TC3-P2-T04-SC1** (physical barrier on first pause):  
  Construct master with tick source at `T0`.  Call `SwitchToDeterministic()`.  Capture the
  `SwitchTimeModeEvent.BarrierWallTicks`.  Assert it equals `getTick() + LookaheadWallTicks`
  (i.e. very close to `T0 + lookahead`), NOT `0 + lookahead`.
  Test method: `MasterSyncController_SwitchToDeterministic_BarrierBasedOnPhysicalClock`

- **TC3-P2-T04-SC2** (physical barrier survives stepping):  
  Transition to Stepping.  Call `Step(1.0f)` 10 times (simulating very-fast stepping).  Call
  `SwitchToContinuous()`.  Wait 0 ticks.  Call `SwitchToDeterministic()` again.  Capture the
  second `SwitchTimeModeEvent.BarrierWallTicks`.  Assert it equals `getTick() + LookaheadWallTicks`
  at the time of the second pause call, proving the barrier is not corrupted by the synthetic
  wall-tick increments from the 10 steps.
  Test method: `MasterSyncController_SwitchToDeterministic_BarrierCorrectAfterStepping`

- **TC3-P2-T04-SC3** (master barrier pending uses physical clock):  
  After calling `SwitchToDeterministic()`, drive `Update()` calls with a tick source that
  advances manually.  Assert the master stays in `BarrierPending` while `getTick() < barrierTicks`
  and transitions to `Stepping` exactly when `getTick() >= barrierTicks`.
  Test method: `MasterSyncController_UpdateBarrierPending_UsesPhysicalClock`

---

### TC3-P2-T02 — Fix MasterSyncController.Step: populate TargetSimTime

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs`

**What to do:**

Locate the `Step(float fixedDelta)` method.  Change the line:

```csharp
TargetSimTime = 0,
```

to:

```csharp
TargetSimTime = _totalTime,
```

The fix must appear **after** `_totalTime += scaledDelta` so the published value is the
post-increment authoritative time.

**Success conditions (unit tests in `MasterSyncControllerTests.cs`):**

- **TC3-P2-T02-SC1**: Transition master to Stepping mode (zero lookahead).
  Call `Step(0.016f)`.  Consume the `AdvanceFrameIntent` from the master bus.
  Assert `intent.TargetSimTime > 0.0` (specifically equals `0.016f * timeScale`).
  Test method: `MasterSyncController_Step_TargetSimTime_IsPopulated`

- **TC3-P2-T02-SC2**: After two consecutive steps (`Step(0.016f)` × 2), the second intent's
  `TargetSimTime` must equal `0.032f * timeScale` (accumulative, not reset).
  Test method: `MasterSyncController_Step_TargetSimTime_Accumulates`

- **TC3-P2-T02-SC3** (cross-controller): Wire master + slave with shared tick source.  Call
  `Step(0.016f)` once.  Relay the `AdvanceFrameIntent` to the slave bus.  Slave calls `Update()`.
  Assert `slave.GetCurrentState().TotalTime == master.GetCurrentState().TotalTime` to within
  `double.Epsilon * 2`.
  Test method: `MasterSyncController_Step_SlaveSnapsToMasterSimTime`

---

### TC3-P2-T03 — Add debug logging to MasterSyncController

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs`

**What to do:**

Add `FdpLog<MasterSyncController>.Debug(...)` calls at the following points (see the exact format
strings in [DESIGN.md §2.5](./DESIGN.md#25-debug-logging-requirements)):

1. End of constructor (after `_totalWallTicks = now`).
2. Start of `SwitchToDeterministic`, after barrier is computed.
3. Inside `Step()`, after `_pendingAcks = new HashSet<int>(_expectedSlaves)`.
4. Inside `UpdateStepping()`, when the ACK count changes (`wasWaiting && _pendingAcks.Count == 0`
   already logs at `Info`; add a `Debug` log on each individual ACK removal too).

**Success conditions:**

- **TC3-P2-T03-SC1**: The existing `MasterSyncController` unit tests continue to pass after
  adding log calls (no behaviour change).
- **TC3-P2-T03-SC2**: A test with a captured `FdpLog` sink verifies that after `Step()` a
  message containing `"[TC3][Master] STEP"` was emitted at Debug level.
  Test method: `MasterSyncController_Step_EmitsDebugLog`

---

## Phase 3 — SlaveSyncController NTP Handshake

Design reference: [DESIGN.md §5](./DESIGN.md#5-feature-c--slavesynccontroller-ntp-handshake)

---

### TC3-P3-T01 — Add NTP fields, SyncedWallTicks, and initial SendTimeSyncRequest

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**What to do:**

1. In the `// ── NEW: Real-Time Baseline` region, add:
   ```csharp
   private long   _masterWallClockOffset = 0;
   private long   _lastSyncRequestTicks  = 0;
   private bool   _isTimeSynced          = false;
   
   public long SyncedWallTicks => _getTick() + _masterWallClockOffset;
   ```

2. In the constructor, after `_lastUpdateRawTicks = now`:
   - Register **both** message types with the event bus:
     ```csharp
     _eventBus.Register<TimeSyncResponse>(); // so we can Consume<> the master's reply
     _eventBus.Register<TimeSyncRequest>();  // so SlaveTimeSyncTranslator can Consume<> our outbound requests
     ```
   - Call `SendTimeSyncRequest()`.
   - Add the constructor debug log.

3. Add the `SendTimeSyncRequest()` helper method:
   ```csharp
   private void SendTimeSyncRequest()
   {
       _lastSyncRequestTicks = _getTick();
       _eventBus.Publish(new TimeSyncRequest
       {
           ClientNodeId    = _localNodeId,
           ClientSendTicks = _lastSyncRequestTicks,
       });
       FdpLog<SlaveSyncController>.Debug(
           "[TC3][Slave#{0}] TimeSyncRequest sent. ClientSendTicks={1}",
           _localNodeId, _lastSyncRequestTicks);
   }
   ```

**Success conditions (unit tests in `SlaveSyncControllerTests.cs`):**

- **TC3-P3-T01-SC1**: Construct a fresh `SlaveSyncController`.  Without advancing time or
  calling `Update()`, consume `TimeSyncRequest` events from the slave bus.  Assert exactly one
  request is pending with the correct `ClientNodeId`.
  Test method: `SlaveSyncController_Constructor_PublishesInitialTimeSyncRequest`

- **TC3-P3-T01-SC2**: Before any sync response, `SyncedWallTicks` must equal `_getTick()` (i.e.
  offset is zero).
  Test method: `SlaveSyncController_SyncedWallTicks_IsRawTickWhenOffsetIsZero`

- **TC3-P3-T01-SC3**: Before any sync response, `_isTimeSynced` must be `false`.
  Test method: `SlaveSyncController_Constructor_IsTimeSynced_IsFalse`

---

### TC3-P3-T02 — Implement DrainTimeSyncResponses with RTT calculation and offset update

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**What to do:**

Add the `DrainTimeSyncResponses()` private method with the NTP formula described in
[DESIGN.md §5.5](./DESIGN.md#55-draintimesyncresponses):

```csharp
private void DrainTimeSyncResponses()
{
    var responses = _eventBus.Consume<TimeSyncResponse>();
    foreach (var response in responses)
    {
        if (response.ClientNodeId != _localNodeId) continue;

        long t4  = _getTick();
        long rtt = (t4 - response.ClientSendTicks)
                 - (response.MasterTransmitTicks - response.MasterReceiveTicks);

        double rttMs = rtt * 1000.0 / Stopwatch.Frequency;

        if (rtt > _config.MaxRttTicks)
        {
            FdpLog<SlaveSyncController>.Debug(
                "[TC3][Slave#{0}] Discarded sync response: RTT={1:F3}ms exceeds max={2:F3}ms",
                _localNodeId, rttMs, _config.MaxRttTicks * 1000.0 / Stopwatch.Frequency);
            continue;
        }

        long newOffset = ((response.MasterReceiveTicks - response.ClientSendTicks)
                        + (response.MasterTransmitTicks - t4)) / 2;

        bool hardSnap = _masterWallClockOffset == 0
                     || Math.Abs(newOffset - _masterWallClockOffset) > Stopwatch.Frequency;

        if (hardSnap)
            _masterWallClockOffset = newOffset;
        else
            _masterWallClockOffset += (long)((newOffset - _masterWallClockOffset)
                                             * _config.SyncCorrectionWeight);

        FdpLog<SlaveSyncController>.Debug(
            "[TC3][Slave#{0}] RTT={1:F3}ms, Offset={2} ticks ({3:F3}ms). {4}",
            _localNodeId, rttMs,
            _masterWallClockOffset,
            _masterWallClockOffset * 1000.0 / Stopwatch.Frequency,
            hardSnap ? "HARD-SNAP" : "gentle-steer");

        _isTimeSynced = true; // Unlock pulse and mode-switch processing
    }
}
```

Call `DrainTimeSyncResponses()` at the very top of `Update()`, before `DrainModeSwitchEvents()`.

Also add the periodic re-sync trigger in `Update()`:

```csharp
if (_getTick() - _lastSyncRequestTicks > _config.SyncRefreshIntervalTicks)
    SendTimeSyncRequest();
```

Add this after `DrainTimeSyncResponses()` and before `DrainModeSwitchEvents()`.

**Success conditions (unit tests in `SlaveSyncControllerTests.cs`):**

- **TC3-P3-T02-SC1** (basic offset): Simulate a perfect zero-latency handshake by creating a
  `TimeSyncResponse` where `ClientSendTicks = 100`, `MasterReceiveTicks = 600`,
  `MasterTransmitTicks = 601`, and the slave records `t4 = 601`.  (The master has an OS offset of
  +500 ticks relative to the slave.)  Publish the response, call `Update()`.  Assert
  `slave._masterWallClockOffset == 500` (actual field value via reflection or a test-only property)
  and `slave.SyncedWallTicks == slave raw tick + 500`.
  Test method: `SlaveSyncController_DrainTimeSyncResponses_CalculatesCorrectOffset`

- **TC3-P3-T02-SC2** (spike rejection): Provide a response with a very high RTT
  (e.g. `t4 - ClientSendTicks = 10 * Stopwatch.Frequency` and `MasterTransmitTicks - MasterReceiveTicks = 0`).
  RTT will far exceed `MaxRttTicks = 0.2 * Frequency`.  Assert `_masterWallClockOffset` remains `0`.
  Test method: `SlaveSyncController_DrainTimeSyncResponses_DiscardsHighRttSpikes`

- **TC3-P3-T02-SC3** (hard-snap on first sync): `_masterWallClockOffset` starts at `0`.  First
  valid response computes `newOffset = 300_000`.  Assert `_masterWallClockOffset == 300_000`
  (not weighted: `0 + (300_000 - 0) * 0.1 = 30_000`).
  Test method: `SlaveSyncController_DrainTimeSyncResponses_HardSnapsOnFirstSync`

- **TC3-P3-T02-SC4** (gentle steering on subsequent syncs): After hard-snap to `300_000`, a
  second response computes `newOffset = 310_000`.  Assert `_masterWallClockOffset` is in the range
  `(300_000, 301_000)` — specifically `300_000 + (long)((310_000 - 300_000) * 0.1) = 301_000`.
  Test method: `SlaveSyncController_DrainTimeSyncResponses_GentleSteersAfterBaseline`

- **TC3-P3-T02-SC5** (periodic resync): Construct a slave.  Drain the initial request.  Advance
  ticks by `SyncRefreshIntervalTicks + 1`.  Call `Update()`.  Consume requests from the bus.
  Assert a second `TimeSyncRequest` is present.
  Test method: `SlaveSyncController_Update_SendsPeriodicResync`

- **TC3-P3-T02-SC6** (`_isTimeSynced` becomes true on first valid response):  
  Construct a slave; assert `_isTimeSynced == false`.  Inject a valid `TimeSyncResponse`.  Call
  `Update()`.  Assert `_isTimeSynced == true`.
  Test method: `SlaveSyncController_DrainTimeSyncResponses_SetsIsTimeSynced`

- **TC3-P3-T02-SC7** (`_isTimeSynced` stays false on rejected response):  
  Inject a response with RTT > `MaxRttTicks`.  Call `Update()`.  Assert `_isTimeSynced` is still
  `false`.
  Test method: `SlaveSyncController_DrainTimeSyncResponses_SpikeRejected_IsTimeSyncedRemainsfalse`

---

### TC3-P3-T05 — Add pre-sync guards to ProcessTimePulses and DrainModeSwitchEvents

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**What to do:**

For both `ProcessTimePulses()` and `DrainModeSwitchEvents()`, add an early-return guard that
drains (discards) the events from the bus while `_isTimeSynced == false`.

**`ProcessTimePulses` — add guard at the top:**

```csharp
private void ProcessTimePulses()
{
    var pulses = _eventBus.Consume<TimePulseDescriptor>();
    if (!_isTimeSynced)
    {
        // Discard: with offset==0, SyncedWallTicks == raw ticks != master domain.
        // A pulse received now would compute a huge garbage timeSincePulse and hard-snap
        // _totalTime to a corrupted value via the SnapThresholdMs fallback.
        FdpLog<SlaveSyncController>.Debug(
            "[TC3][Slave#{0}] Ignoring TimePulse (not yet time-synced)", _localNodeId);
        return;
    }
    foreach (var pulse in pulses) OnTimePulseReceived(pulse);
}
```

**`DrainModeSwitchEvents` — add guard at the top:**

```csharp
private void DrainModeSwitchEvents()
{
    var events = _eventBus.Consume<SwitchTimeModeEvent>();
    if (!_isTimeSynced)
    {
        // Discard: barrier evaluation uses SyncedWallTicks which is not yet calibrated.
        // Accepting a pause command now would either trigger immediately or never trigger.
        FdpLog<SlaveSyncController>.Debug(
            "[TC3][Slave#{0}] Ignoring SwitchTimeModeEvent (not yet time-synced)", _localNodeId);
        return;
    }
    foreach (var evt in events)
    {
        // ... existing processing (unchanged) ...
    }
}
```

**Why:** Network delivery is asynchronous.  A `TimePulseDescriptor` or `SwitchTimeModeEvent` may
arrive before the first `TimeSyncResponse` has been processed.  While `_isTimeSynced == false`,
`_masterWallClockOffset` is `0` and `SyncedWallTicks` equals raw local ticks, which are in a
completely different domain from the master's timestamps.  Processing either event in this state
will produce garbage corrections or incorrect barrier transitions.

**Success conditions (unit tests in `SlaveSyncControllerTests.cs`):**

- **TC3-P3-T05-SC1** (pulses discarded before sync):  
  Construct a slave (offset = 0, `_isTimeSynced = false`).  Publish a `TimePulseDescriptor` to
  the slave bus.  Call `Update()`.  Assert `_totalTime == 0.0` (no snap occurred) and
  `GetMode() == TimeMode.Continuous`.
  Test method: `SlaveSyncController_ProcessTimePulses_DiscardsBeforeSync`

- **TC3-P3-T05-SC2** (mode switch discarded before sync):  
  Construct a slave.  Publish a `SwitchTimeModeEvent` (Deterministic) to the slave bus.  Call
  `Update()`.  Assert `GetMode() == TimeMode.Continuous` (not `Deterministic`).
  Test method: `SlaveSyncController_DrainModeSwitchEvents_DiscardsBeforeSync`

- **TC3-P3-T05-SC3** (pulses accepted after sync):  
  Inject a valid `TimeSyncResponse` (so `_isTimeSynced = true`).  Call `Update()`.  Then publish
  a `TimePulseDescriptor`.  Call `Update()` again.  Assert the pulse was processed (e.g.
  `_errorFilter` has at least one sample, verifiable via reflection or a helper).
  Test method: `SlaveSyncController_ProcessTimePulses_AcceptsAfterSync`

- **TC3-P3-T05-SC4** (mode switch accepted after sync):  
  Inject a valid `TimeSyncResponse`.  Call `Update()`.  Publish a `SwitchTimeModeEvent`
  (Deterministic, with `BarrierWallTicks = 0` so it triggers instantly on the next tick past 0).
  Advance ticks past the barrier.  Call `Update()`.  Assert `GetMode() == TimeMode.Deterministic`.
  Test method: `SlaveSyncController_DrainModeSwitchEvents_AcceptsAfterSync`

---

### TC3-P3-T03 — Fix UpdateBarrierPending to use SyncedWallTicks

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**What to do:**

In `UpdateBarrierPending()`, locate the barrier check:

```csharp
if (_pendingBarrierWallTicks >= 0 && _virtualWallTicks >= _pendingBarrierWallTicks)
```

Replace `_virtualWallTicks` with `SyncedWallTicks`:

```csharp
if (_pendingBarrierWallTicks >= 0 && SyncedWallTicks >= _pendingBarrierWallTicks)
```

Add the debug log line on barrier trigger:

```csharp
FdpLog<SlaveSyncController>.Debug(
    "[TC3][Slave#{0}] BARRIER HIT. SyncedWallTicks={1}, BarrierWallTicks={2}. Entering Stepping.",
    _localNodeId, SyncedWallTicks, _pendingBarrierWallTicks);
```

Insert this line immediately before `_mode = SlaveMode.Stepping`.

**Success conditions (unit tests in `SlaveSyncControllerTests.cs`):**

- **TC3-P3-T03-SC1** (cross-machine barrier): Construct master with tick source starting at `M0`.
  Construct slave with tick source starting at `S0 = M0 + 500_000_000` (large OS offset).
  Perform a handshake so `slave._masterWallClockOffset = -500_000_000` (slave sees master's domain).
  Master calls `SwitchToDeterministic()` with `LookaheadWallTicks = 100_000`.
  BarrierWallTicks emitted = approx `M0 + 100_000`.
  Feed event to slave bus.  Advance both clocks by `50_000`.  Slave calls `Update()` — must still
  be in `BarrierPending`.  Advance both by `60_000` more.  Slave calls `Update()` — must now be
  `Stepping`.
  Test method: `SlaveSyncController_BarrierPending_UsesSyncedWallTicks`

- **TC3-P3-T03-SC2** (without sync, regression): Without any handshake (offset remains 0), the
  existing single-machine barrier test scenario from the original `SlaveSyncControllerTests`
  continues to pass.
  Test method: (existing test, does not need modification — just must stay green)

---

### TC3-P3-T04 — Fix OnTimePulseReceived to use SyncedWallTicks

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**What to do:**

In `OnTimePulseReceived(TimePulseDescriptor pulse)`, replace:

```csharp
long currentAbsTicks  = _getTick();
```

with:

```csharp
long currentAbsTicks  = SyncedWallTicks;
```

Add the PLL debug log line:

```csharp
FdpLog<SlaveSyncController>.Debug(
    "[TC3][Slave#{0}] PULSE. MasterWallTicks={1}, SyncedNow={2}, timeSince={3:F3}ms, simError={4:F3}ms, correction={5:F4}",
    _localNodeId,
    pulse.MasterWallTicks,
    currentAbsTicks,
    timeSinceSec * 1000.0,
    simTimeError * 1000.0,
    correctionFactor);
```

The variable `correctionFactor` is computed later in `AdvanceContinuousTime`; add the log after
computing `simTimeError` (capture the filtered error from `_errorFilter.GetFilteredValue()` for
the purpose of this log if `correctionFactor` is not yet in scope, or restructure to surface it).

**Success conditions (unit tests in `PLLSynchronizationTests.cs` or `SlaveSyncControllerTests.cs`):**

- **TC3-P3-T04-SC1**: With a non-zero `_masterWallClockOffset`, publish a `TimePulseDescriptor`
  where `MasterWallTicks = masterNow`.  In `OnTimePulseReceived`, `currentAbsTicks` must equal
  `_getTick() + offset` (i.e. `SyncedWallTicks`), NOT raw `_getTick()`.  Confirm via a test that
  checks `timeSincePulse` is near-zero when the pulse was freshly generated in master domain.
  Test method: `SlaveSyncController_PLL_UsesOffsetClock_ForTimeSincePulse`

- **TC3-P3-T04-SC2** (regression): Existing PLL convergence tests in `PLLSynchronizationTests.cs`
  must pass unchanged (zero offset case is identical behaviour).

---

### TC3-P3-T06 — Drain stray AdvanceFrameIntent in Continuous and BarrierPending modes

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**What to do:**

At the very top of `UpdateContinuous()`, before the `ProcessTimePulses()` call, add:

```csharp
// Prevent memory leak: if this slave missed the Pause command and joined late while the
// master is already stepping, FrameOrderDescriptors will arrive over DDS and be translated
// into AdvanceFrameIntent objects on the managed bus.  Drain and discard them here so they
// cannot pile up across frames.
_eventBus.ConsumeManaged<AdvanceFrameIntent>();
```

Apply the identical drain call at the top of `UpdateBarrierPending()` for the same reason
(a slave in barrier-pending state also does not consume managed intents).

**Why:** The `SlaveLockstepTranslator` pushes every incoming `FrameOrderDescriptor` onto the
bus as an `AdvanceFrameIntent` regardless of the slave's current mode.  When the slave is in
`Continuous` or `BarrierPending` mode, `UpdateStepping` never runs and nothing drains the queue.
Over time (especially in debug sessions with many step clicks) this becomes an unbounded managed
memory allocation.

**Success conditions (unit tests in `SlaveSyncControllerTests.cs`):**

- **TC3-P3-T06-SC1** (continuous drain):  
  Construct a `SlaveSyncController`.  Publish 10 `AdvanceFrameIntent` objects via
  `_eventBus.PublishManaged(...)`.  Call `Update()` (slave remains in Continuous mode because no
  `SwitchTimeModeEvent` was published).  Immediately call
  `_eventBus.ConsumeManaged<AdvanceFrameIntent>()` and assert the result is empty — all 10
  intents were drained by `UpdateContinuous`.
  Test method: `SlaveSyncController_ContinuousMode_DrainsStrayStepIntents`

- **TC3-P3-T06-SC2** (barrier-pending drain):  
  Transition the slave to `BarrierPending` (inject a `SwitchTimeModeEvent` with a future barrier
  that will not be crossed yet).  Publish 5 `AdvanceFrameIntent` objects.  Call `Update()`.  Assert
  the managed bus is empty after the call.
  Test method: `SlaveSyncController_BarrierPendingMode_DrainsStrayStepIntents`

- **TC3-P3-T06-SC3** (intents not drained in Stepping mode — negative test):  
  Transition the slave to Stepping mode.  Publish one `AdvanceFrameIntent`.  Call `Update()`.
  Assert the intent was **consumed and processed** by `UpdateStepping` (i.e. `TotalTime` advanced
  and an ACK was published), confirming the drain is mode-specific.
  Test method: `SlaveSyncController_SteppingMode_ProcessesIntentNotDrains`

---

## Phase 4 — Translators & Network Module

Design reference: [DESIGN.md §6](./DESIGN.md#6-feature-d--translators--network-module)

---

### TC3-P4-T01 — Implement MasterTimeSyncTranslator

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/MasterTimeSyncTranslator.cs`  *(new file)*

**What to do:**

Create the file implementing `IDescriptorTranslator`.  See the full class outline in the design
talk (Part 1) — reproduced here for clarity:

Key requirements:
- `DescriptorOrdinal` = `205L`.
- `TopicName` = `"TimeSyncRequest"`.
- `PollIngress`: read all valid `TimeSyncRequest` samples; for each, immediately record
  `masterReceiveTicks = _getTick()`, construct `TimeSyncResponse`, record
  `masterTransmitTicks = _getTick()` (re-read to capture post-serialisation latency), write
  response over DDS.
- Do NOT publish to `FdpEventBus` (no bus dependency on the master translator).
- `ScanAndPublish`:  no-op (master does not send requests).
- `ApplyToEntity` / `Dispose`:  no-op.
- Accept `DdsParticipant? participant` — if null, all DDS ops become no-ops (safe for unit tests).
- Accept optional `Func<long>? tickSource` (test seam).
- Add per-request debug log line as specified in [DESIGN.md §2.5](./DESIGN.md#25-debug-logging-requirements).

**Success conditions (unit tests in `LockstepTranslatorTests.cs` or a new `TimeSyncTranslatorTests.cs`):**

- **TC3-P4-T01-SC1**: Construct with `participant = null`.  Call `PollIngress(...)`.  No exception
  is thrown.
  Test method: `MasterTimeSyncTranslator_NullParticipant_PollIngress_IsNoOp`

- **TC3-P4-T01-SC2** (contract): Instantiate with a live DDS participant (integration scenario).
  Publish a `TimeSyncRequest` from a test writer.  Call `PollIngress`.  Read back the
  `TimeSyncResponse` from a test reader.  Assert:
  - `response.ClientNodeId == request.ClientNodeId`
  - `response.ClientSendTicks == request.ClientSendTicks`
  - `response.MasterReceiveTicks <= response.MasterTransmitTicks`
  - `response.MasterReceiveTicks > 0`
  Test method: `MasterTimeSyncTranslator_Dds_RespondsToRequest`  
  *(Mark `[Fact(Skip = "Requires DDS loopback")]` if DDS not available in test environment.)*

---

### TC3-P4-T02 — Implement SlaveTimeSyncTranslator

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/SlaveTimeSyncTranslator.cs`  *(new file)*

**What to do:**

Create the file implementing `IDescriptorTranslator`.  Key requirements:

- `DescriptorOrdinal` = `206L`.
- `TopicName` = `"TimeSyncResponse"`.
- `ScanAndPublish`: drain `TimeSyncRequest` from the `FdpEventBus`, write each to DDS.
- `PollIngress`: read `TimeSyncResponse` samples; for samples whose `ClientNodeId == _localNodeId`,
  publish onto `FdpEventBus`.  Silently ignore responses addressed to other nodes.
- Constructor requires `FdpEventBus eventBus` and `int localNodeId`; accepts optional
  `DdsParticipant? participant`.
- `ApplyToEntity` / `Dispose`:  no-op.

**Success conditions:**

- **TC3-P4-T02-SC1**: Construct with `participant = null`.  Call `PollIngress(...)` and
  `ScanAndPublish(...)`.  No exception thrown.
  Test method: `SlaveTimeSyncTranslator_NullParticipant_IsNoOp`

- **TC3-P4-T02-SC2**: Publish a `TimeSyncRequest` onto the slave's `FdpEventBus`.  Swap the bus.
  Call `ScanAndPublish()`.  With a live DDS reader on `"TimeSyncRequest"`, verify one sample
  is received with the correct `ClientNodeId`.
  Test method: `SlaveTimeSyncTranslator_Dds_ForwardsRequestFromBus`  
  *(Mark skip if DDS not available.)*

- **TC3-P4-T02-SC3**: Write two `TimeSyncResponse` samples to a test DDS writer: one with
  `ClientNodeId = 3` (this slave's ID) and one with `ClientNodeId = 99` (another slave).
  Call `PollIngress`.  Consume `TimeSyncResponse` from the slave bus.  Assert exactly one event
  is consumed and it has `ClientNodeId == 3`.
  Test method: `SlaveTimeSyncTranslator_PollIngress_FiltersResponsesByNodeId`  
  *(Mark skip if DDS not available.)*

---

### TC3-P4-T03 — Add factory methods to TimeNetworkModule

**File:** `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs`

**What to do:**

Add these two public static methods at the end of the `TimeNetworkModule` class:

```csharp
/// <summary>
/// Creates a <see cref="MasterTimeSyncTranslator"/> that handles the two-way NTP-style
/// clock sync handshake for the master/orchestrator node.
/// Add to the <c>customTranslators</c> list of the master node's
/// <c>CycloneNetworkModule</c> during application startup.
/// </summary>
public static IDescriptorTranslator CreateMasterTimeSyncTranslator(
    DdsParticipant? participant,
    Func<long>? tickSource = null)
{
    return new Translators.MasterTimeSyncTranslator(participant, tickSource);
}

/// <summary>
/// Creates a <see cref="SlaveTimeSyncTranslator"/> for slave nodes (IG, ExCon, SimHost-slave).
/// Add to the <c>customTranslators</c> list of the slave node's
/// <c>CycloneNetworkModule</c> during application startup.
/// </summary>
public static IDescriptorTranslator CreateSlaveTimeSyncTranslator(
    DdsParticipant?  participant,
    FdpEventBus      eventBus,
    int              localNodeId)
{
    if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
    return new Translators.SlaveTimeSyncTranslator(participant, eventBus, localNodeId);
}
```

**Success conditions:**

- **TC3-P4-T03-SC1**: `CreateMasterTimeSyncTranslator(null)` returns a non-null `IDescriptorTranslator`
  instance and does not throw.
  Test method: `TimeNetworkModule_CreateMasterTimeSyncTranslator_NullParticipant_ReturnsInstance`

- **TC3-P4-T03-SC2**: `CreateSlaveTimeSyncTranslator(null, bus, 5)` returns a non-null
  `IDescriptorTranslator` and does not throw.
  Test method: `TimeNetworkModule_CreateSlaveTimeSyncTranslator_NullParticipant_ReturnsInstance`

- **TC3-P4-T03-SC3**: `CreateSlaveTimeSyncTranslator(null, null, 5)` throws
  `ArgumentNullException`.
  Test method: `TimeNetworkModule_CreateSlaveTimeSyncTranslator_NullBus_Throws`

**Test class:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/SwitchTimeModeTranslatorTests.cs`
(or create `TimeSyncTranslatorTests.cs` as a dedicated file).

---

## Phase 5 — Autonomous Multi-Computer Unit Tests

Design reference: [DESIGN.md §7](./DESIGN.md#7-feature-e--autonomous-multi-computer-unit-tests)  
and [DESIGN.md §11](./DESIGN.md#11-test-architecture-notes)

These tests are the primary evidence that the new synchronisation system works correctly
in multi-process / multi-computer scenarios.  **All tests must be deterministic, fast
(< 100 ms wall time each), and require no DDS, no threads, and no external processes.**
They simulate remote nodes through separate injected tick sources and in-process bus relays.

---

### TC3-P5-T01 — TimeSyncOffsetTests: RTT formula and offset corner cases

**File:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeSyncOffsetTests.cs`  *(new file)*

**Tests to implement:**

- **TC3-P5-T01-SC1** `Offset_ZeroLatency_ExactlyCapturesMasterDomain`  
  Simulate a zero-latency sync: `slaveTick = 0`, `masterTick = 5_000_000` (large machine offset).
  Construct `TimeSyncResponse` as if master received the request at tick `5_000_000` and
  transmitted back at tick `5_000_001`.  Slave receives at `slaveTick = 1`.
  Assert `slave._masterWallClockOffset ≈ 5_000_000` (within ±2 ticks).

- **TC3-P5-T01-SC2** `Offset_SymmetricLatency_CancelsOut`  
  Both sides advance by the same latency amount during transit.  Verify offset calculation
  still converges to the true inter-machine clock difference.

- **TC3-P5-T01-SC3** `Offset_AsymmetricLatency_IsWithinHalfRTT`  
  Uplink = 1 ms, downlink = 3 ms (total RTT = 4 ms).  The NTP formula will introduce a
  systematic error of at most half the RTT = 2 ms.  Assert `|computed offset - true offset| ≤ RTT / 2`.

- **TC3-P5-T01-SC4** `SpikeRejection_HighRTT_OffsetUnchanged`  
  Supply a response whose RTT > `MaxRttTicks`.  Assert offset was not modified.

- **TC3-P5-T01-SC5** `HardSnap_FirstSync_IgnoresWeight`  
  Verify that `_masterWallClockOffset == 0` before and is set to exact `newOffset` (not `newOffset * 0.1`) after first valid sync.

- **TC3-P5-T01-SC6** `GentleSteering_SubsequentSync_WeightApplied`  
  After establishing a baseline, supply a second response with a different offset.
  Verify the new offset is `old + (new - old) * SyncCorrectionWeight`.

---

### TC3-P5-T02 — PauseBarrierSyncTests: barrier fires at the same simtime

**File:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/PauseBarrierSyncTests.cs`  *(new file)*

Simulate a two-machine scenario: master ticks start at `0`; slave ticks start at `500_000_000`.
Run a complete NTP handshake (inject response, let slave compute offset).  Then drive both through
the pause barrier sequence.

**Tests to implement:**

- **TC3-P5-T02-SC1** `BarrierFires_SameSimTime_WithLargeClockOffset`  
  After NTP sync, master pauses.  Relay `SwitchTimeModeEvent` to slave.  Advance both clocks
  until the master transitions to `Stepping`.  Assert that at the transition frame the slave
  also transitions to `Stepping` within the same frame (±1).

- **TC3-P5-T02-SC2** `BarrierFires_Before_NTPSync_Slave_DoesNotEnterStepping_Early`  
  Without a NTP sync, confirm that if the master's `BarrierWallTicks` is below the slave's
  raw large absolute tick, the slave would instantly enter Stepping.  This test documents
  the known-broken pre-fix behaviour and serves as a regression guard — it should FAIL if run
  against the old code, and PASS against the new code (the test uses the new code).

- **TC3-P5-T02-SC3** `TwoSlaves_WithDifferentOffsets_BothEnterStepping_WithinOneFrame`  
  Slave1 ticks start at `500_000_000`; Slave2 ticks start at `300_000_000`.  Both perform NTP
  sync.  Master pauses.  Both should enter Stepping within 1 frame of each other (measure by
  frame count at transition).

- **TC3-P5-T02-SC4** `SimTime_OnBarrierTransition_IsIdenticalAcrossNodes`  
  At the exact frame all three controllers (master + 2 slaves) enter Stepping, compare their
  `GetCurrentState().TotalTime`.  Assert they are within `fixedDelta / 2` of each other.

---

### TC3-P5-T03 — LockstepSimTimeAccuracyTests: step simtime is bit-identical

**File:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/LockstepSimTimeAccuracyTests.cs`  *(new file)*

**Tests to implement:**

- **TC3-P5-T03-SC1** `FirstStep_SlaveSimTime_EqualsMasterSimTime`  
  Wire master + slave.  NTP sync.  Pause.  Drive barrier.  Step once.  Assert
  `slave.GetCurrentState().TotalTime == master.GetCurrentState().TotalTime`.

- **TC3-P5-T03-SC2** `TenSteps_SlaveSimTime_EqualsMasterSimTimeAfterEachStep`  
  Repeat: for each of 10 steps, assert that after each step the TotalTime on slave exactly
  equals the master.  Stale drift must not accumulate.

- **TC3-P5-T03-SC3** `TwoSlaves_BothSnapToMasterSimTime_PerStep`  
  Wire master + slave1 + slave2.  After each of 5 steps, assert
  `slave1.TotalTime == master.TotalTime` and `slave2.TotalTime == master.TotalTime`.

- **TC3-P5-T03-SC4** `Resume_AfterLockstep_SlaveContinuesFromMasterSimTime`  
  After 3 steps, call `SwitchToContinuous`.  Relay the event.  Assert both nodes resume from
  the same `TotalTime`, and that over the next 10 continuous frames the slave's sim time stays
  within 1 ms of the master's (PLL operates correctly on the already-synced time base).

---

### TC3-P5-T04 — FullCycleMultiComputerSim: end-to-end with clock offsets

**File:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/FullCycleMultiComputerSim.cs`  *(new file)*

Combines all of the above into a single scenario test, mirroring `UnifiedControllerE2ETests`
but with simulated multi-computer clock offsets and explicit NTP handshake steps.

**Tests to implement:**

- **TC3-P5-T04-SC1** `FullCycle_OneSlaveOffset_PauseStepResume_SimTimesConverge`  
  Setup:
  - Master tick starts at `0`, slave tick starts at `500_000_000L`.
  - `LookaheadWallTicks = ticksPerFrame * 3` (3-frame barrier window).
  - Phase 0: NTP handshake (inject response, verify offset).
  - Phase 1: 20 continuous frames; at frame 20 assert `|slave.TotalTime - master.TotalTime| < 2ms`.
  - Phase 2: Pause.  Drive barrier.
  - Phase 3: 5 steps; after each step assert `slave.TotalTime == master.TotalTime` exactly.
  - Phase 4: Resume.  20 more continuous frames; assert sim times converge.
  End assertions:
  - No `TimePulseDescriptor` was ever published by the slave bus.
  - Master's `FrameNumber` > 0 throughout.

- **TC3-P5-T04-SC2** `FullCycle_TwoSlavesLargeOffsets_AllSimTimesMatch`  
  Same as SC1 but with two slaves.  After each of 5 steps all three TotalTime values must match.

---

### TC3-P5-T05 — ClockSkewDriftTests: periodic re-sync keeps drift bounded

**File:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/ClockSkewDriftTests.cs`  *(new file)*

**Tests to implement:**

- **TC3-P5-T05-SC1** `ClockSkew_WithPeriodicResync_OffsetStaysWithin2ms`  
  Slave ticks advance at `1001` per master's `1000` (slave runs 0.1% fast).  Run for 10 seconds
  of simulated time (at 60 Hz = 600 frames).  After every `SyncRefreshIntervalTicks` frames,
  inject a new NTP handshake response (auto-generated from the current tick state).  After 600
  frames, assert `|slave.SyncedWallTicks - masterTick| < 2ms_in_ticks`.

- **TC3-P5-T05-SC2** `ClockSkew_WithoutResync_DriftAccumulates`  
  Same setup but skip periodic re-sync.  After 600 frames, assert drift IS larger than 2 ms.
  (This is a documentation-of-bug test that proves resync is necessary.)

---

## Phase 6 — Application Integration Validation

Design reference: [DESIGN.md §8](./DESIGN.md#8-feature-f--application-integration-validation)

---

### TC3-P6-T01 — API compatibility verification

**File:** `Hrot.ClusterRunner.Integration.Tests/TimeControlIntegrationTests.cs`

**What to do:**

No source code changes required to the application layer.  Verify:

1. Build `Hrot.ClusterRunner` and `Hrot.ClusterRunner.Integration.Tests` without errors after the
   toolkit changes (Phase 1–4).
2. Run `TimeControlIntegrationTests`.  All existing tests must pass.

**Success conditions:**

- **TC3-P6-T01-SC1**: `dotnet build Hrot.ClusterRunner/Hrot.ClusterRunner.csproj` exits cleanly.
  Test method: *(build check, not a unit test)*

- **TC3-P6-T01-SC2**: All existing `TimeControlIntegrationTests` methods pass without
  modification.
  Test method: *(existing test suite, unchanged)*

---

### TC3-P6-T02 — Wire the new translators in application startup (Integration guide task)

> **Scope note:** This task documents the wiring instructions for the application layer.
> The actual code changes to `OrchestratorSubsystem`, `IgApplication`, and `ExConSubsystem`
> are deliberately deferred to a follow-on workstream — the toolkit must pass Phase 5 tests
> first.

**Files to update in the follow-on workstream:**

| File | Change |
|------|--------|
| `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` | Add `TimeNetworkModule.CreateMasterTimeSyncTranslator(participant)` to `customTranslators` list |
| `Hrot.IG/IgApplication.cs` | Add `TimeNetworkModule.CreateSlaveTimeSyncTranslator(participant, eventBus, nodeId)` **and** `TimeNetworkModule.CreateSlaveLockstepTranslator(participant, eventBus)` to `customTranslators` (second translator ensures IG sends `FrameAck` DDS messages on each lockstep step so the master's `_pendingAcks` is cleared) |
| `Hrot.ClusterRunner/Services/ExConSubsystem.cs` | Same NTP translator as IG |
| `Hrot.SimHost/SimHostApplication.cs` | Add master or slave translator depending on role |

**Success conditions for the follow-on:**

- **TC3-P6-T02-SC1**: Running the full cluster (SimHost + IG + ExCon) shows no console errors
  about unhandled `TimeSyncRequest` topics.
- **TC3-P6-T02-SC2**: `[TC3][Master]` and `[TC3][Slave#N]` debug log lines appear in the debug
  output, confirming the translators are active.
- **TC3-P6-T02-SC3**: Pausing and stepping confirm <1 ms sim-time difference across nodes
  (observable in the debug panel introduced in the previous workstream).
