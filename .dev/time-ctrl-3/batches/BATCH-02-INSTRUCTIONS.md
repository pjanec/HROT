# BATCH-02: SlaveSyncController NTP Handshake

**Batch Number:** BATCH-02  
**Tasks:** TC3-P3-T01, TC3-P3-T02, TC3-P3-T03, TC3-P3-T04, TC3-P3-T05, TC3-P3-T06  
**Phase:** Phase 3 — SlaveSyncController NTP Handshake  
**Previous Review:** `.dev/time-ctrl-3/reviews/BATCH-01-REVIEW.md`

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch equips `SlaveSyncController` with an NTP-style two-way clock-sync handshake.
After this batch, the slave will:
- Publish a `TimeSyncRequest` on construction (and periodically thereafter).
- Receive a `TimeSyncResponse` from the master and compute `_masterWallClockOffset` via the
  NTP RTT formula.
- Expose `SyncedWallTicks` (= `_getTick() + _masterWallClockOffset`) — the slave's best
  estimate of the master's current OS tick.
- Gate all pulse and mode-switch processing behind `_isTimeSynced == true`.
- Use `SyncedWallTicks` instead of raw `_getTick()` / `_virtualWallTicks` when comparing
  against master-domain timestamps (barrier, PLL transit time).
- Drain stray `AdvanceFrameIntent` managed-bus items in Continuous and BarrierPending modes.

### Required Reading (IN ORDER)

1. **Developer Workflow:** `.github/skills/developer/SKILL.md` — how to work with batches
2. **Design Document:** `.dev/time-ctrl-3/DESIGN.md` — full architecture (especially §5)
3. **Task Definitions:** `.dev/time-ctrl-3/TASK-DETAIL.md` — TC3-P3-T01 through TC3-P3-T06
4. **Previous Review:** `.dev/time-ctrl-3/reviews/BATCH-01-REVIEW.md` — lessons from BATCH-01

### Source Code Location

- **Primary Work Area:**
  `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`
- **Test Project (existing file to extend):**
  `FDP/Toolkits/FDP.Toolkit.Time.Tests/SlaveSyncControllerTests.cs`
- **Supporting messages (already implemented in BATCH-01):**
  `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs`
- **Supporting config (already implemented in BATCH-01):**
  `FDP/Toolkits/FDP.Toolkit.Time/Controllers/TimeConfig.cs`

### Report Submission

**When done, submit your report to:**  
`.dev/time-ctrl-3/reports/BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev/time-ctrl-3/questions/BATCH-02-QUESTIONS.md`

---

## 🔍 Context: Current SlaveSyncController State

Below is the full current source of `SlaveSyncController.cs` (abbreviated at private helpers
you will not touch).  Read it carefully before making changes.

### Current fields region

```csharp
// ── State machine ─────────────────────────────────────────────────────────
private enum SlaveMode { Continuous, BarrierPending, Stepping }
private SlaveMode _mode = SlaveMode.Continuous;
private long _pendingBarrierWallTicks = -1;

// ── Identity ──────────────────────────────────────────────────────────────
private readonly int _localNodeId;

// ── PLL state (NEVER destroyed across transitions) ────────────────────────
private readonly JitterFilter _errorFilter;
private long   _virtualWallTicks;
private long   _lastUpdateRawTicks;
private double _currentError;

// ── Time state ────────────────────────────────────────────────────────────
private double _totalTime;
private double _unscaledTotalTime;
private long   _frameNumber;
private float  _timeScale = 1.0f;

// ── Stepping state ────────────────────────────────────────────────────────
private readonly Queue<AdvanceFrameIntent> _pendingIntents = new();
private long _lastAcceptedStepFrameId = -1L;

// ── Infrastructure ────────────────────────────────────────────────────────
private readonly FdpEventBus _eventBus;
private readonly TimeConfig  _config;
private readonly Func<long>  _getTick;
```

### Current constructor

```csharp
public SlaveSyncController(
    FdpEventBus  eventBus,
    int          localNodeId,
    TimeConfig?  config     = null,
    Func<long>?  tickSource = null)
{
    _eventBus    = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    _localNodeId = localNodeId;
    _config      = config ?? TimeConfig.Default;
    _getTick     = tickSource ?? Stopwatch.GetTimestamp;
    _errorFilter = new JitterFilter(_config.JitterWindowSize);

    long now            = _getTick();
    _virtualWallTicks   = now;
    _lastUpdateRawTicks = now;

    // Register bus types that carry [EventId] — bus needs them pre-registered.
    _eventBus.Register<TimePulseDescriptor>();
    _eventBus.Register<SwitchTimeModeEvent>();
    // AdvanceFrameIntent and FrameStepCompletedEvent are domain types
    // (no [EventId]) — they use PublishManaged / ConsumeManaged, no registration needed.
}
```

### Current Update()

```csharp
public GlobalTime Update()
{
    // ── 1. Drain SwitchTimeModeEvent first ─────────────────────────────
    DrainModeSwitchEvents();

    // ── 2. Run the current mode ────────────────────────────────────────
    return _mode switch
    {
        SlaveMode.Continuous     => UpdateContinuous(),
        SlaveMode.BarrierPending => UpdateBarrierPending(),
        SlaveMode.Stepping       => UpdateStepping(),
        _                        => GetCurrentState(),
    };
}
```

### Current DrainModeSwitchEvents

```csharp
private void DrainModeSwitchEvents()
{
    var events = _eventBus.Consume<SwitchTimeModeEvent>();
    foreach (var evt in events)
    {
        if (evt.TargetMode == TimeMode.Deterministic)
        {
            if (_mode != SlaveMode.Stepping)
            {
                _pendingBarrierWallTicks = evt.BarrierWallTicks;
                _mode = SlaveMode.BarrierPending;
                _pendingIntents.Clear();
                _lastAcceptedStepFrameId = -1L;
            }
        }
        else // Continuous / Resume
        {
            ApplyResume(evt);
        }
    }
}
```

### Current UpdateContinuous

```csharp
private GlobalTime UpdateContinuous()
{
    ProcessTimePulses();

    long nowTicks    = _getTick();
    long rawDelta    = nowTicks - _lastUpdateRawTicks;
    _lastUpdateRawTicks = nowTicks;

    return AdvanceContinuousTime(rawDelta);
}
```

### Current UpdateBarrierPending

```csharp
private GlobalTime UpdateBarrierPending()
{
    ProcessTimePulses();

    long nowTicks    = _getTick();
    long rawDelta    = nowTicks - _lastUpdateRawTicks;
    _lastUpdateRawTicks = nowTicks;

    var result = AdvanceContinuousTime(rawDelta);

    // Check if virtual wall clock has reached the barrier.
    if (_pendingBarrierWallTicks >= 0 && _virtualWallTicks >= _pendingBarrierWallTicks)
    {
        _mode = SlaveMode.Stepping;
        _pendingIntents.Clear();
        _lastAcceptedStepFrameId = -1L;
    }

    return result;
}
```

### Current ProcessTimePulses and OnTimePulseReceived

```csharp
private void ProcessTimePulses()
{
    var pulses = _eventBus.Consume<TimePulseDescriptor>();
    foreach (var pulse in pulses)
        OnTimePulseReceived(pulse);
}

private void OnTimePulseReceived(TimePulseDescriptor pulse)
{
    long currentAbsTicks  = _getTick();
    long timeSincePulse   = currentAbsTicks - pulse.MasterWallTicks;
    double timeSinceSec   = timeSincePulse / (double)Stopwatch.Frequency;

    double expectedSimTime = pulse.SimTimeSnapshot + timeSinceSec * pulse.TimeScale;
    double simTimeError    = expectedSimTime - _totalTime;
    long   errorTicks      = (long)(simTimeError * Stopwatch.Frequency);
    _errorFilter.AddSample(errorTicks);

    _timeScale = pulse.TimeScale;

    double errorMs = Math.Abs(simTimeError) * 1000.0;
    if (errorMs > _config.SnapThresholdMs)
    {
        _totalTime          = expectedSimTime;
        _lastUpdateRawTicks = currentAbsTicks;
        _errorFilter.Reset();
        _currentError = 0.0;
    }
}
```

---

## 📝 Task Specifications

### TC3-P3-T01 — Add NTP fields, SyncedWallTicks, and initial SendTimeSyncRequest

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**Step 1 — New fields:** Add a `// ── NEW: NTP Real-Time Baseline` region after the
`// ── Stepping state` block with these three fields and one property:

```csharp
// ── NEW: NTP Real-Time Baseline ───────────────────────────────────────────
private long  _masterWallClockOffset = 0;   // Master ticks - local ticks
private long  _lastSyncRequestTicks  = 0;   // Physical tick when last request was sent
private bool  _isTimeSynced          = false; // Unlocked once first valid response arrives

/// <summary>The slave's best estimate of the master node's current OS tick.</summary>
public long SyncedWallTicks => _getTick() + _masterWallClockOffset;
```

**Step 2 — Update constructor:** After `_lastUpdateRawTicks = now`, append:

```csharp
// Register NTP message types on the bus.
// TimeSyncResponse: so DrainTimeSyncResponses can Consume<> the master's reply.
// TimeSyncRequest:  so SlaveTimeSyncTranslator can Consume<> and forward our outbound req.
_eventBus.Register<TimeSyncResponse>();
_eventBus.Register<TimeSyncRequest>();

// Send the initial handshake request.
SendTimeSyncRequest();

FdpLog<SlaveSyncController>.Debug(
    "[TC3][Slave#{0}] Initialized. _virtualWallTicks={1}", _localNodeId, _virtualWallTicks);
```

**Step 3 — Add `SendTimeSyncRequest()` private method** (add near the bottom of the class,
after the `AdvanceContinuousTime` helper):

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

**Note:** `TimeSyncRequest` and `TimeSyncResponse` are in namespace
`FDP.Toolkit.Time.Messages`.  The using directive is already present.

**Success conditions (add to `SlaveSyncControllerTests.cs`):**

- **TC3-P3-T01-SC1** `SlaveSyncController_Constructor_PublishesInitialTimeSyncRequest`  
  Construct a `SlaveSyncController` (tick source frozen at `1_000_000L`).  After construction
  (before any swap/update), call `bus.SwapBuffers()` then `bus.Consume<TimeSyncRequest>()`.
  Assert exactly one request is in the list, with `ClientNodeId == NodeId` and
  `ClientSendTicks == 1_000_000L`.

- **TC3-P3-T01-SC2** `SlaveSyncController_SyncedWallTicks_IsRawTickWhenOffsetIsZero`  
  Construct a controller.  Advance the tick source to `2_000_000L`.  Assert
  `ctrl.SyncedWallTicks == 2_000_000L` (offset is 0 before first sync response).

- **TC3-P3-T01-SC3** `SlaveSyncController_Constructor_IsTimeSynced_IsFalse`  
  Construct a controller.  Without calling `Update()`, assert `_isTimeSynced == false`.  
  (Access via reflection: `typeof(SlaveSyncController).GetField("_isTimeSynced",
  BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ctrl)`)

---

### TC3-P3-T02 — Implement DrainTimeSyncResponses

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**Step 1 — Add `DrainTimeSyncResponses()`** (add after `SendTimeSyncRequest()`):

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

**Step 2 — Update `Update()`:** Insert at the very beginning of `Update()`, before the
`DrainModeSwitchEvents()` call:

```csharp
// Drain NTP responses and (if due) send a fresh request.
DrainTimeSyncResponses();
if (_getTick() - _lastSyncRequestTicks > _config.SyncRefreshIntervalTicks)
    SendTimeSyncRequest();
```

The full `Update()` should now look like:

```csharp
public GlobalTime Update()
{
    DrainTimeSyncResponses();
    if (_getTick() - _lastSyncRequestTicks > _config.SyncRefreshIntervalTicks)
        SendTimeSyncRequest();

    DrainModeSwitchEvents();

    return _mode switch
    {
        SlaveMode.Continuous     => UpdateContinuous(),
        SlaveMode.BarrierPending => UpdateBarrierPending(),
        SlaveMode.Stepping       => UpdateStepping(),
        _                        => GetCurrentState(),
    };
}
```

**Success conditions (add to `SlaveSyncControllerTests.cs`):**

For these tests, use a mutable `long ticks` captured by a lambda (`Func<long> tickSource =
() => ticks`) so you can advance the tick source between operations without `Thread.Sleep`.
Use `bus.SwapBuffers()` before each `Consume<>` call (or after each `Publish<>`) to
make items visible.

- **TC3-P3-T02-SC1** `SlaveSyncController_DrainTimeSyncResponses_CalculatesCorrectOffset`  
  Freeze ticks at `0L`.  Construct slave (sends request with `ClientSendTicks = 0`).  
  Build response: `ClientNodeId = NodeId`, `ClientSendTicks = 0`,
  `MasterReceiveTicks = 500`, `MasterTransmitTicks = 501`.  
  Advance ticks to `1L` (slave's `t4`).  
  Publish response, swap, call `Update()`.  
  Via reflection, assert `_masterWallClockOffset == 500`
  (formula: `((500-0)+(501-1))/2 = (500+500)/2 = 500`).
  Also assert `ctrl.SyncedWallTicks == 1L + 500 == 501L`.

- **TC3-P3-T02-SC2** `SlaveSyncController_DrainTimeSyncResponses_DiscardsHighRttSpikes`  
  Use a config with `MaxRttTicks = 100`.  Build a response where `t4 - ClientSendTicks = 200`
  (RTT > 100 with zero master processing time).  
  Publish, swap, call `Update()`.  
  Assert `_masterWallClockOffset` remains `0`.

- **TC3-P3-T02-SC3** `SlaveSyncController_DrainTimeSyncResponses_HardSnapsOnFirstSync`  
  `_masterWallClockOffset` starts at `0`.  Inject one valid response that computes
  `newOffset = 300_000` (choose timestamps accordingly).  
  Assert `_masterWallClockOffset == 300_000` (hard-snap, not `0.1 * 300_000 = 30_000`).

- **TC3-P3-T02-SC4** `SlaveSyncController_DrainTimeSyncResponses_GentleSteersAfterBaseline`  
  After hard-snap to `300_000`, inject a second response that computes `newOffset = 310_000`.  
  Assert `_masterWallClockOffset == 300_000 + (long)((310_000 - 300_000) * 0.1) == 301_000`.

- **TC3-P3-T02-SC5** `SlaveSyncController_Update_SendsPeriodicResync`  
  Drain the initial request from the bus.  Advance ticks past `SyncRefreshIntervalTicks + 1`.  
  Call `Update()`.  Swap.  Consume `TimeSyncRequest` from bus.  Assert one new request is present.

- **TC3-P3-T02-SC6** `SlaveSyncController_DrainTimeSyncResponses_SetsIsTimeSynced`  
  Before: assert `_isTimeSynced == false`.  Inject a valid response.  Call `Update()`.  
  After: assert `_isTimeSynced == true`.

- **TC3-P3-T02-SC7** `SlaveSyncController_DrainTimeSyncResponses_SpikeRejected_IsTimeSyncedRemainsfalse`  
  Inject only a spike-rejected response.  Call `Update()`.  Assert `_isTimeSynced == false`.

---

### TC3-P3-T05 — Add pre-sync guards to ProcessTimePulses and DrainModeSwitchEvents

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**Replace `ProcessTimePulses()` with:**

```csharp
private void ProcessTimePulses()
{
    var pulses = _eventBus.Consume<TimePulseDescriptor>();
    if (!_isTimeSynced)
    {
        // Discard: offset == 0 → SyncedWallTicks == raw ticks ≠ master domain.
        // A pulse received now would produce a garbage timeSincePulse and hard-snap
        // _totalTime via the SnapThresholdMs fallback.
        FdpLog<SlaveSyncController>.Debug(
            "[TC3][Slave#{0}] Ignoring TimePulse (not yet time-synced)", _localNodeId);
        return;
    }
    foreach (var pulse in pulses)
        OnTimePulseReceived(pulse);
}
```

**Replace `DrainModeSwitchEvents()` with** (same logic as before except for the added guard):

```csharp
private void DrainModeSwitchEvents()
{
    var events = _eventBus.Consume<SwitchTimeModeEvent>();
    if (!_isTimeSynced)
    {
        // Discard: barrier evaluation uses SyncedWallTicks which is not yet calibrated.
        FdpLog<SlaveSyncController>.Debug(
            "[TC3][Slave#{0}] Ignoring SwitchTimeModeEvent (not yet time-synced)", _localNodeId);
        return;
    }
    foreach (var evt in events)
    {
        if (evt.TargetMode == TimeMode.Deterministic)
        {
            if (_mode != SlaveMode.Stepping)
            {
                _pendingBarrierWallTicks = evt.BarrierWallTicks;
                _mode = SlaveMode.BarrierPending;
                _pendingIntents.Clear();
                _lastAcceptedStepFrameId = -1L;
            }
        }
        else // Continuous / Resume
        {
            ApplyResume(evt);
        }
    }
}
```

**Success conditions (add to `SlaveSyncControllerTests.cs`):**

- **TC3-P3-T05-SC1** `SlaveSyncController_ProcessTimePulses_DiscardsBeforeSync`  
  Construct slave (not synced).  Publish a `TimePulseDescriptor` to bus.  Swap.  Call `Update()`.  
  Assert `ctrl.GetCurrentState().TotalTime == 0.0` and `GetMode() == TimeMode.Continuous`.

- **TC3-P3-T05-SC2** `SlaveSyncController_DrainModeSwitchEvents_DiscardsBeforeSync`  
  Construct slave (not synced).  Publish a `SwitchTimeModeEvent` (Deterministic) to bus.
  Swap.  Call `Update()`.  Assert `GetMode() == TimeMode.Continuous`.

- **TC3-P3-T05-SC3** `SlaveSyncController_ProcessTimePulses_AcceptsAfterSync`  
  Inject a valid `TimeSyncResponse` (so `_isTimeSynced = true`).  Swap.  Call `Update()`.  
  Then publish a `TimePulseDescriptor` (`SimTimeSnapshot = 1.0, TimeScale = 1f,
  MasterWallTicks = SyncedWallTicks`).  Swap.  Call `Update()` again.  
  Assert via reflection that `_errorFilter` has at least one sample
  (get `_errorFilter`'s internal `_window` field and verify it has a non-zero entry), OR
  simply assert that `_totalTime` is close to `1.0` (the pulse time snapshot).

- **TC3-P3-T05-SC4** `SlaveSyncController_DrainModeSwitchEvents_AcceptsAfterSync`  
  Inject a valid `TimeSyncResponse`.  Swap.  Call `Update()` to apply sync.  
  Publish a `SwitchTimeModeEvent` (Deterministic, `BarrierWallTicks = 0`).
  Advance ticks past `0`.  Swap.  Call `Update()`.  Assert `GetMode() == TimeMode.Deterministic`.

**Important:** After adding TC3-P3-T05, the 12 existing tests that use the slave in barrier /
stepping / pulse scenarios WILL NEED UPDATING to inject a sync response first (so
`_isTimeSynced = true`).  Without that, those tests will now find the slave ignoring all
`SwitchTimeModeEvent` and `TimePulseDescriptor` messages.

For each existing test that publishes a `SwitchTimeModeEvent` or `TimePulseDescriptor`,
add this preamble:

```csharp
// Pre-sync: inject a valid TimeSyncResponse so _isTimeSynced = true
InjectValidTimeSyncResponse(bus, ctrl, tickSource: ref ticks);
```

Add a shared helper to `SlaveSyncControllerTests`:

```csharp
/// <summary>
/// Injects a minimal valid TimeSyncResponse for the given NodeId so that
/// _isTimeSynced transitions to true on the next Update() call.
/// The response is crafted for zero network latency (offset = 0).
/// </summary>
private static void InjectSyncResponse(FdpEventBus bus, long currentTick, int nodeId = NodeId)
{
    bus.Publish(new TimeSyncResponse
    {
        ClientNodeId       = nodeId,
        ClientSendTicks    = currentTick,
        MasterReceiveTicks = currentTick,     // same tick = zero latency → offset 0
        MasterTransmitTicks = currentTick,
    });
    bus.SwapBuffers();
}
```

Then, for each existing test that now needs the guard bypassed, call:
```csharp
InjectSyncResponse(bus, ticks);
ctrl.Update(); // consume the response — _isTimeSynced = true now
```

**Note on existing tests that don't use a tick source:** Some existing tests use a fixed tick
source.  You can pass `ticks = Stopwatch.GetTimestamp()` or any fixed value for `currentTick`
in `InjectSyncResponse`.

---

### TC3-P3-T03 — Fix UpdateBarrierPending to use SyncedWallTicks

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**In `UpdateBarrierPending()`**, replace:

```csharp
if (_pendingBarrierWallTicks >= 0 && _virtualWallTicks >= _pendingBarrierWallTicks)
{
    _mode = SlaveMode.Stepping;
    _pendingIntents.Clear();
    _lastAcceptedStepFrameId = -1L;
}
```

with:

```csharp
if (_pendingBarrierWallTicks >= 0 && SyncedWallTicks >= _pendingBarrierWallTicks)
{
    FdpLog<SlaveSyncController>.Debug(
        "[TC3][Slave#{0}] BARRIER HIT. SyncedWallTicks={1}, BarrierWallTicks={2}. Entering Stepping.",
        _localNodeId, SyncedWallTicks, _pendingBarrierWallTicks);
    _mode = SlaveMode.Stepping;
    _pendingIntents.Clear();
    _lastAcceptedStepFrameId = -1L;
}
```

**Success conditions (add to `SlaveSyncControllerTests.cs`):**

- **TC3-P3-T03-SC1** `SlaveSyncController_BarrierPending_UsesSyncedWallTicks`  
  Setup:
  - Master tick source: `masterTicks` starting at `0L`.
  - Slave tick source: `slaveTicks` starting at `500_000_000L` (half-a-second OS offset).
  - Construct slave with slave tick source.  Inject `TimeSyncResponse` crafted so
    `_masterWallClockOffset = -500_000_000L` (slave's SyncedWallTicks = slaveTicks - 500_000_000
    = master domain).
  - How to build the response for offset = -500_000_000:  
    `ClientSendTicks = 500_000_000`, `MasterReceiveTicks = 0`, `MasterTransmitTicks = 0`,
    and the slave records `t4 = 500_000_000` when consuming.  
    Formula: `((0 - 500_000_000) + (0 - 500_000_000)) / 2 = -500_000_000`. ✓
  - Swap, call `Update()` to apply sync.
  - Publish `SwitchTimeModeEvent` (Deterministic, `BarrierWallTicks = 100_000`).  Swap.
  - Advance `slaveTicks` to `500_000_050L` (SyncedWallTicks = 50L < 100_000 barrier).
    Call `Update()`.  Assert `GetMode() == TimeMode.Continuous` (still barrier-pending).
  - Advance `slaveTicks` to `500_110_000L` (SyncedWallTicks = 110_000L > 100_000 barrier).
    Call `Update()`.  Assert `GetMode() == TimeMode.Deterministic`.

---

### TC3-P3-T04 — Fix OnTimePulseReceived to use SyncedWallTicks

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**In `OnTimePulseReceived()`**, replace:

```csharp
long currentAbsTicks  = _getTick();
```

with:

```csharp
long currentAbsTicks  = SyncedWallTicks;
```

Then compute `correctionFactor` inside the method so the debug log can reference it.
The existing code already computes `_errorFilter.GetFilteredValue()` in `AdvanceContinuousTime`.
For the log, read the filter _after_ calling `AddSample`:

Add this debug log after the `if (errorMs > _config.SnapThresholdMs)` block (i.e. at the end
of the method):

```csharp
FdpLog<SlaveSyncController>.Debug(
    "[TC3][Slave#{0}] PULSE. MasterWallTicks={1}, SyncedNow={2}, timeSince={3:F3}ms, simError={4:F3}ms",
    _localNodeId,
    pulse.MasterWallTicks,
    currentAbsTicks,
    timeSinceSec * 1000.0,
    simTimeError * 1000.0);
```

**Success conditions (add to `SlaveSyncControllerTests.cs`):**

- **TC3-P3-T04-SC1** `SlaveSyncController_PLL_UsesOffsetClock_ForTimeSincePulse`  
  Setup slave with tick source frozen at `S = 500_000_000L`.  
  Inject a sync response that gives `_masterWallClockOffset = -500_000_000L` (master domain ticks
  are 500M behind slave's raw ticks, i.e. master at `M0 = 0`, slave at `S0 = 500_000_000`).  
  Swap.  Call `Update()` to apply.  
  Now publish a `TimePulseDescriptor` with `MasterWallTicks = 0` (fresh pulse from master
  at tick `0`).  Swap.  Call `Update()`.  
  The key assertion: `timeSincePulse` inside `OnTimePulseReceived` must be near zero
  (because `SyncedWallTicks = S + (-500_000_000) = 0 ≈ MasterWallTicks`), NOT `S - 0 = 
  500_000_000`.  
  Verify indirectly: assert `_totalTime` was NOT snapped (i.e. `_totalTime < 1.0`) because
  a near-zero `timeSinceSec` gives `expectedSimTime ≈ 0`, and `simTimeError < SnapThresholdMs`.
  (If `_getTick()` were used instead of `SyncedWallTicks`, `timeSinceSec` ≈ 500_000_000 /
  Frequency ≈ 500 s, `simTimeError` would be huge, and `_totalTime` would be hard-snapped to
  a nonsense value — the test would detect this as `_totalTime >> 1.0`.)

---

### TC3-P3-T06 — Drain stray AdvanceFrameIntent in Continuous and BarrierPending modes

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**In `UpdateContinuous()`**, insert at the very top (before `ProcessTimePulses()`):

```csharp
// Prevent memory leak: drain any AdvanceFrameIntent that arrived while in Continuous mode
// (e.g. if this slave joined late while the master is already stepping).
_eventBus.ConsumeManaged<AdvanceFrameIntent>();
```

**In `UpdateBarrierPending()`**, insert at the very top (before `ProcessTimePulses()`):

```csharp
// Same drain as Continuous: stray step intents must not pile up during the barrier wait.
_eventBus.ConsumeManaged<AdvanceFrameIntent>();
```

**Success conditions (add to `SlaveSyncControllerTests.cs`):**

- **TC3-P3-T06-SC1** `SlaveSyncController_ContinuousMode_DrainsStrayStepIntents`  
  Construct slave.  Inject and process a sync response (so `_isTimeSynced = true`).  
  Publish 10 `AdvanceFrameIntent` via `bus.PublishManaged(...)`.  Call `Update()` (slave stays
  in Continuous because no `SwitchTimeModeEvent` was published).  
  Then call `bus.ConsumeManaged<AdvanceFrameIntent>()` and assert the result is empty.

- **TC3-P3-T06-SC2** `SlaveSyncController_BarrierPendingMode_DrainsStrayStepIntents`  
  Construct slave; inject sync response.  Publish a `SwitchTimeModeEvent` (Deterministic,
  `BarrierWallTicks = long.MaxValue` so the barrier is never crossed).  Swap.  Call `Update()`.  
  Slave is now in BarrierPending.  Publish 5 `AdvanceFrameIntent` via `PublishManaged`.  
  Call `Update()`.  Assert `bus.ConsumeManaged<AdvanceFrameIntent>()` returns empty.

- **TC3-P3-T06-SC3** `SlaveSyncController_SteppingMode_ProcessesIntentNotDrains`  
  Construct slave; inject sync response.  Transition to Stepping (barrier = 0).  
  Publish one `AdvanceFrameIntent` (`FrameID = 1, FixedDelta = 0.016f, TargetSimTime = 0.016`).  
  Call `Update()`.  
  Assert `ctrl.GetCurrentState().TotalTime ≈ 0.016` and that a `FrameStepCompletedEvent`
  was published (swap then `ConsumeManaged<FrameStepCompletedEvent>()` returns one event).

---

## ✅ Acceptance Criteria

All pre-existing tests must remain green.  New test count:

| Task | New Tests | Method Count |
|------|-----------|-------------|
| TC3-P3-T01 | 3 | SC1, SC2, SC3 |
| TC3-P3-T02 | 7 | SC1–SC7 |
| TC3-P3-T05 | 4 | SC1–SC4 |
| TC3-P3-T03 | 1 | SC1 |
| TC3-P3-T04 | 1 | SC1 |
| TC3-P3-T06 | 3 | SC1–SC3 |
| **Total** | **19** | |

Existing tests `SlaveSyncController_TransitionsToStepping_WhenBarrierCrossed`,
`SlaveSyncController_Stepping_*`, `SlaveSyncController_Resume_*`, and
`SlaveSyncController_BarrierPending_PLLContinuesDuringWait` all require the sync-response
preamble because they rely on events that TC3-P3-T05 will now suppress until synced.
Update them using the `InjectSyncResponse` helper described above.

**Required final test command:**
```
dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj --verbosity minimal
```
All tests must pass (target: existing 90 + 19 new = 109 total, not counting any updates to
existing tests which are not new tests).

---

## ⚠️ Implementation Order

Implement tasks in this order to keep the build green at each step:

1. **TC3-P3-T01** — Add fields, property, registrations, initial send.
   *(Build should be green after this; 3 new tests pass.)*

2. **TC3-P3-T02** — Add `DrainTimeSyncResponses()` and update `Update()`.
   *(7 new tests; all should pass.)*

3. **TC3-P3-T06** — Add stray-drain in `UpdateContinuous` and `UpdateBarrierPending`.
   *(3 new tests; all should pass.  No behaviour change yet to existing tests.)*

4. **TC3-P3-T05** — Add pre-sync guards and update existing tests with `InjectSyncResponse`.
   *(4 new tests; up to 8 existing tests require the preamble update.)*

5. **TC3-P3-T03** — Replace barrier check.
   *(1 new test; existing barrier test should still pass because single-machine offset = 0.)*

6. **TC3-P3-T04** — Replace `_getTick()` with `SyncedWallTicks` in PLL.
   *(1 new test; existing PLL tests should still pass because offset = 0 in those tests.)*

---

## 📁 Files to Change

| File | Type |
|------|------|
| `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs` | Modify |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/SlaveSyncControllerTests.cs` | Modify (add ~19 tests + update ~8 existing) |
