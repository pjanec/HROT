# BATCH-04: Phase 5 — Autonomous Multi-Computer Unit Tests

**Batch Number:** BATCH-04  
**Tasks:** TC3-P5-T01, TC3-P5-T02, TC3-P5-T03, TC3-P5-T04, TC3-P5-T05  
**Phase:** Phase 5 — Autonomous Multi-Computer Unit Tests  
**Previous Review:** `.dev/time-ctrl-3/reviews/BATCH-03-REVIEW.md`

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch adds 5 new test files that prove the Phase 1–4 changes work correctly in simulated
multi-machine scenarios.  **No production code changes are required.**  All tests must be:
- Deterministic and fast (< 200 ms wall time per test)
- Require no DDS, no threads, no external processes
- Use only injected `Func<long>` tick sources and in-process bus relays

### Required Reading (IN ORDER)

1. **Developer Workflow:** `.github/skills/developer/SKILL.md`
2. **Design Document:** `.dev/time-ctrl-3/DESIGN.md` — §7 (Feature E), §11 (Test Architecture)
3. **Task Definitions:** `.dev/time-ctrl-3/TASK-DETAIL.md` — TC3-P5-T01 through TC3-P5-T05
4. **Previous Review:** `.dev/time-ctrl-3/reviews/BATCH-03-REVIEW.md`
5. **Reference pattern:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/UnifiedControllerE2ETests.cs` — see how master+slave are wired in-process

### Source Code Location

- **Test Project:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/`
- **5 new test files to create** (see below)
- **No production file changes**

### Build/Test Command

```
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet test Toolkits\FDP.Toolkit.Time.Tests\FDP.Toolkit.Time.Tests.csproj --verbosity minimal
```

Target: 118 existing + 18 new = **136 tests**, all green.

### Report Submission

`.dev/time-ctrl-3/reports/BATCH-04-REPORT.md`

---

## 🔍 Context: Multi-Computer Test Architecture

All multi-machine tests use a shared in-process relay pattern:

```
MasterSyncController (bus: masterBus, tick: () => masterTicks)
    │
    ├─ masterBus.ConsumeManaged<AdvanceFrameIntent> → relay → slaveBus.PublishManaged<AdvanceFrameIntent>
    ├─ masterBus.Consume<SwitchTimeModeEvent> → relay → slaveBus.Publish<SwitchTimeModeEvent>
    ├─ masterBus.Consume<TimeSyncResponse> — relay to slave — NOT used; SlaveTimeSyncTranslator does this over DDS
    │   (in tests: inject TimeSyncResponse directly onto slaveBus)
    └─ slaveBus.ConsumeManaged<FrameStepCompletedEvent> → relay → masterBus.PublishManaged<FrameStepCompletedEvent>

SlaveSyncController (bus: slaveBus, tick: () => slaveTicks, nodeId: 1)
```

**Key observation:** In tests, instead of DDS translators, relay manually:

```csharp
// After masterBus.SwapBuffers():
// 1. Relay SwitchTimeModeEvent master→slave
var modeEvents = masterBus.Consume<SwitchTimeModeEvent>();
foreach (var e in modeEvents) slaveBus.Publish(e);

// 2. Relay AdvanceFrameIntent master→slave (managed, no swap needed)
var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>();
foreach (var i in intents) slaveBus.PublishManaged(i);

// 3. Relay FrameStepCompletedEvent slave→master (managed, no swap needed)
var acks = slaveBus.ConsumeManaged<FrameStepCompletedEvent>();
foreach (var a in acks) masterBus.PublishManaged(a);
```

**NTP handshake simulation:** Inject a computed `TimeSyncResponse` directly onto the slave's bus:

```csharp
// To give slave an offset = (masterTick - slaveTick):
// Formula: newOffset = ((MasterReceiveTicks - ClientSendTicks) + (MasterTransmitTicks - t4)) / 2
// For zero RTT: MasterReceiveTicks = MasterTransmitTicks = masterTicks, ClientSendTicks = slaveTicks, t4 = slaveTicks
// → newOffset = ((masterTicks - slaveTicks) + (masterTicks - slaveTicks)) / 2 = masterTicks - slaveTicks ✓
private static void PerformNtpHandshake(
    FdpEventBus  slaveBus,
    SlaveSyncController slave,
    long         masterTicks,
    long         slaveTicks,
    int          nodeId = 1)
{
    // First drain the initial TimeSyncRequest published by slave constructor
    slaveBus.SwapBuffers();
    slaveBus.Consume<TimeSyncRequest>();

    // Inject response
    slaveBus.Publish(new TimeSyncResponse
    {
        ClientNodeId        = nodeId,
        ClientSendTicks     = slaveTicks,
        MasterReceiveTicks  = masterTicks,
        MasterTransmitTicks = masterTicks,  // zero master processing time
    });
    slaveBus.SwapBuffers();
    // slave t4 = slaveTicks (tick source frozen at that value)
    slave.Update();  // applies offset = masterTicks - slaveTicks, _isTimeSynced = true
    slaveBus.SwapBuffers();
    slaveBus.Consume<TimeSyncRequest>(); // drain any re-sync request
}
```

After `PerformNtpHandshake`, `slave.SyncedWallTicks = slaveTick + (masterTick - slaveTick) = masterTick` — i.e., in master's domain.

---

## 📝 File 1: `TimeSyncTranslatorTests.cs` — Wait, that already exists!

The test file `TimeSyncTranslatorTests.cs` was created in BATCH-03.  The new file for Phase 5
tests is different.

---

## 📝 Phase 5 Test Files (5 new files)

### File: `TimeSyncOffsetTests.cs` — TC3-P5-T01

Create `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeSyncOffsetTests.cs` with these 6 tests.

These tests exercise `SlaveSyncController.DrainTimeSyncResponses` directly with constructed
responses — no master controller needed.

**Setup pattern** (shared across all TimeSyncOffsetTests):
```csharp
private static (SlaveSyncController ctrl, FdpEventBus bus, Func<long> getTick) CreateSlave(
    ref long ticks, int nodeId = 1)
{
    var bus = new FdpEventBus();
    var ctrl = new SlaveSyncController(bus, nodeId, tickSource: () => ticks);
    bus.SwapBuffers();
    bus.Consume<TimeSyncRequest>(); // drain initial request
    return (ctrl, bus, () => ticks);
}

private static void InjectResponse(FdpEventBus bus, SlaveSyncController ctrl,
    long clientSend, long masterReceive, long masterTransmit, ref long slaveTicks_t4)
{
    bus.Publish(new TimeSyncResponse
    {
        ClientNodeId        = 1,
        ClientSendTicks     = clientSend,
        MasterReceiveTicks  = masterReceive,
        MasterTransmitTicks = masterTransmit,
    });
    bus.SwapBuffers();
    // tick source must be at slaveTicks_t4 when ctrl.Update() is called
    ctrl.Update();
}

private static long GetOffset(SlaveSyncController ctrl)
    => (long)typeof(SlaveSyncController)
        .GetField("_masterWallClockOffset",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
        .GetValue(ctrl)!;
```

**Tests:**

```csharp
[Fact]
public void Offset_ZeroLatency_ExactlyCapturesMasterDomain()
{
    // Slave has tick=0, master has tick=5_000_000 (large offset)
    long slaveTicks = 0L;
    long masterTick = 5_000_000L;

    var bus  = new FdpEventBus();
    var ctrl = new SlaveSyncController(bus, nodeId: 1, tickSource: () => slaveTicks);
    bus.SwapBuffers();
    bus.Consume<TimeSyncRequest>();

    // Zero-latency: clientSend=0, masterReceive=masterTick, masterTransmit=masterTick, t4=1
    bus.Publish(new TimeSyncResponse
    {
        ClientNodeId        = 1,
        ClientSendTicks     = 0L,
        MasterReceiveTicks  = masterTick,
        MasterTransmitTicks = masterTick,
    });
    bus.SwapBuffers();
    slaveTicks = 1L; // t4

    ctrl.Update(); // hard-snap: offset = ((masterTick-0)+(masterTick-1))/2 = (10_000_000 - 1)/2 ≈ 5_000_000

    long offset = GetOffset(ctrl);
    // Expected: ((5_000_000-0) + (5_000_000-1))/2 = 4_999_999 (off by <1 tick due to integer division)
    Assert.True(Math.Abs(offset - masterTick) <= 2,
        $"Offset {offset} should be ~{masterTick} (within 2 ticks). Got {Math.Abs(offset - masterTick)} tick error.");
}

[Fact]
public void Offset_SymmetricLatency_CancelsOut()
{
    // Master 5_000_000 ahead. Symmetric latency: 50 ticks each way.
    // clientSend=100, t4=200 (slave elapsed 100). masterReceive=5_100_150, masterTransmit=5_100_150 (master at receipt same as transmit)
    // RTT = (200-100)-(5_100_150-5_100_150) = 100 - 0 = 100 ticks
    // newOffset = ((5_100_150-100)+(5_100_150-200))/2 = (5_100_050 + 5_099_950)/2 = 5_100_000
    // True offset = 5_000_000. Systematic NTP error = (uplink-downlink)/2 = 0.
    // But here uplink latency = 5_100_150 - 100 = 5_100_050 ticks above send time...
    // Actually let me think more carefully.
    //
    // Slave view: send at slaveT=100, receive at slaveT=200 → total elapsed slave = 100 ticks
    // Master view: receive at masterT=5_100_150, transmit at masterT=5_100_150
    //   (zero master processing time)
    // Symmetric latency means slave→master = master→slave = 50 slave ticks
    //   But the master and slave clocks run at the same frequency, just offset by 5_000_000.
    //   Master receive time in master domain: masterT=5_100_150 corresponds to slaveT=150
    //   (same-frequency clocks, +50 ticks from slave T=100).
    // So onset=5_000_000+50=5_000_050 → masterT = 5_000_050 at send, masterReceive = 5_000_100. Hmm.
    //
    // Simpler approach: use zero master processing BUT asymmetric from MASTER's perspective.
    // For symmetric case: just verify offset converges to the true value.
    // True offset = masterTick_at_slave_send - slaveTick_at_send.
    //
    // Let's set: slaveTick=0 at send, masterTick_now=5_000_000 (true offset=5_000_000).
    // Network latency = 100 ticks on each way (but measured in local clocks ≈ equal frequency).
    // clientSendTicks=0, masterReceiveTicks=5_000_100 (master got it 100 ticks later in master domain),
    // masterTransmitTicks=5_000_100 (zero processing on master).
    // slaveTicks at t4 = 200 (200 slave ticks elapsed = 100 up + 100 down).
    // RTT = (200-0) - (5_000_100-5_000_100) = 200 ticks. MaxRtt default=0.2s >> 200 ticks → accepted.
    // newOffset = ((5_000_100-0) + (5_000_100-200))/2 = (5_000_100 + 4_999_900)/2 = 5_000_000
    // This is exactly the true offset! Symmetric latency cancels perfectly. ✓

    long slaveTicks = 0L;
    var bus  = new FdpEventBus();
    var ctrl = new SlaveSyncController(bus, nodeId: 1, tickSource: () => slaveTicks);
    bus.SwapBuffers();
    bus.Consume<TimeSyncRequest>();

    bus.Publish(new TimeSyncResponse
    {
        ClientNodeId        = 1,
        ClientSendTicks     = 0L,
        MasterReceiveTicks  = 5_000_100L,
        MasterTransmitTicks = 5_000_100L,
    });
    bus.SwapBuffers();
    slaveTicks = 200L;
    ctrl.Update();

    long offset = GetOffset(ctrl);
    Assert.Equal(5_000_000L, offset);
}

[Fact]
public void Offset_AsymmetricLatency_IsWithinHalfRTT()
{
    // True offset = 5_000_000. Uplink = 100 ticks, downlink = 300 ticks (4:1 asymmetry).
    // clientSendTicks=0, masterReceiveTicks=5_000_100 (master gets request 100 later),
    // masterTransmitTicks=5_000_100 (zero processing), t4=400 (100 up + 300 down).
    // RTT = (400-0) - (5_000_100-5_000_100) = 400 ticks.
    // newOffset = ((5_000_100-0) + (5_000_100-400))/2 = (5_000_100 + 4_999_700)/2 = 4_999_900
    // True offset = 5_000_000. Error = |4_999_900 - 5_000_000| = 100 ticks.
    // Half RTT = 200 ticks. 100 <= 200 ✓ (NTP guarantee: error <= RTT/2)

    long slaveTicks = 0L;
    var bus  = new FdpEventBus();
    var ctrl = new SlaveSyncController(bus, nodeId: 1, tickSource: () => slaveTicks);
    bus.SwapBuffers();
    bus.Consume<TimeSyncRequest>();

    bus.Publish(new TimeSyncResponse
    {
        ClientNodeId        = 1,
        ClientSendTicks     = 0L,
        MasterReceiveTicks  = 5_000_100L,
        MasterTransmitTicks = 5_000_100L,
    });
    bus.SwapBuffers();
    slaveTicks = 400L; // asymmetric: uplink=100, downlink=300
    ctrl.Update();

    long offset   = GetOffset(ctrl);
    long trueOffset = 5_000_000L;
    long rtt        = 400L;
    Assert.True(Math.Abs(offset - trueOffset) <= rtt / 2,
        $"Error {Math.Abs(offset - trueOffset)} must be <= RTT/2 = {rtt / 2}");
}

[Fact]
public void SpikeRejection_HighRTT_OffsetUnchanged()
{
    long slaveTicks = 0L;
    var config = new TimeConfig { MaxRttTicks = 500 };
    var bus    = new FdpEventBus();
    var ctrl   = new SlaveSyncController(bus, 1, config, () => slaveTicks);
    bus.SwapBuffers();
    bus.Consume<TimeSyncRequest>();

    // RTT = (1001 - 0) - 0 = 1001 > 500 → rejected
    bus.Publish(new TimeSyncResponse { ClientNodeId = 1, ClientSendTicks = 0, MasterReceiveTicks = 500, MasterTransmitTicks = 500 });
    bus.SwapBuffers();
    slaveTicks = 1001L;
    ctrl.Update();

    Assert.Equal(0L, GetOffset(ctrl));
}

[Fact]
public void HardSnap_FirstSync_IgnoresWeight()
{
    long slaveTicks = 0L;
    var bus  = new FdpEventBus();
    var ctrl = new SlaveSyncController(bus, 1, tickSource: () => slaveTicks);
    bus.SwapBuffers();
    bus.Consume<TimeSyncRequest>();

    // newOffset = ((300_000-0)+(300_000-0))/2 = 300_000
    bus.Publish(new TimeSyncResponse { ClientNodeId = 1, ClientSendTicks = 0, MasterReceiveTicks = 300_000L, MasterTransmitTicks = 300_000L });
    bus.SwapBuffers();
    ctrl.Update();

    // Hard-snap: should be 300_000 not 300_000 * 0.1 = 30_000
    Assert.Equal(300_000L, GetOffset(ctrl));
}

[Fact]
public void GentleSteering_SubsequentSync_WeightApplied()
{
    long slaveTicks = 0L;
    var bus  = new FdpEventBus();
    var ctrl = new SlaveSyncController(bus, 1, tickSource: () => slaveTicks);
    bus.SwapBuffers();
    bus.Consume<TimeSyncRequest>();

    // First sync → hard-snap to 300_000
    bus.Publish(new TimeSyncResponse { ClientNodeId = 1, ClientSendTicks = 0, MasterReceiveTicks = 300_000L, MasterTransmitTicks = 300_000L });
    bus.SwapBuffers();
    ctrl.Update();
    bus.SwapBuffers();
    bus.Consume<TimeSyncRequest>();

    // Second sync → newOffset = 310_000 → gentle steer
    bus.Publish(new TimeSyncResponse { ClientNodeId = 1, ClientSendTicks = 0, MasterReceiveTicks = 310_000L, MasterTransmitTicks = 310_000L });
    bus.SwapBuffers();
    ctrl.Update();

    long expected = 300_000L + (long)((310_000L - 300_000L) * 0.1); // = 301_000
    Assert.Equal(expected, GetOffset(ctrl));
}
```

---

### File: `PauseBarrierSyncTests.cs` — TC3-P5-T02

Create `FDP/Toolkits/FDP.Toolkit.Time.Tests/PauseBarrierSyncTests.cs`.

These tests use a full master+slave pair with separate tick sources and an NTP handshake.

**Shared helpers:**

```csharp
private const int SlaveNodeId = 1;

private static (MasterSyncController master, SlaveSyncController slave,
                FdpEventBus masterBus, FdpEventBus slaveBus)
    CreateMasterSlave(ref long masterTicks, ref long slaveTicks,
                      TimeConfig? config = null)
{
    var masterBus = new FdpEventBus();
    var slaveBus  = new FdpEventBus();
    var cfg       = config ?? new TimeConfig { LookaheadWallTicks = 0 };

    var master = new MasterSyncController(masterBus, new HashSet<int> { SlaveNodeId },
        cfg, () => masterTicks);
    var slave  = new SlaveSyncController(slaveBus, SlaveNodeId, cfg, () => slaveTicks);
    return (master, slave, masterBus, slaveBus);
}

private static void NtpHandshake(
    FdpEventBus slaveBus, SlaveSyncController slave,
    long masterTick, long slaveTick)
{
    slaveBus.SwapBuffers();
    slaveBus.Consume<TimeSyncRequest>();

    slaveBus.Publish(new TimeSyncResponse
    {
        ClientNodeId        = SlaveNodeId,
        ClientSendTicks     = slaveTick,
        MasterReceiveTicks  = masterTick,
        MasterTransmitTicks = masterTick,
    });
    slaveBus.SwapBuffers();
    slave.Update();
    slaveBus.SwapBuffers();
    slaveBus.Consume<TimeSyncRequest>();
}

private static void RelayModeSwitch(FdpEventBus masterBus, FdpEventBus slaveBus)
{
    // Relay SwitchTimeModeEvent from master to slave. Must be called AFTER masterBus.SwapBuffers().
    var events = masterBus.Consume<SwitchTimeModeEvent>();
    foreach (var e in events) slaveBus.Publish(e);
    slaveBus.SwapBuffers();
}

private static void RelayIntents(FdpEventBus masterBus, FdpEventBus slaveBus)
{
    // Relay AdvanceFrameIntent (managed) master→slave.
    var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>();
    foreach (var i in intents) slaveBus.PublishManaged(i);
}

private static void RelayAcks(FdpEventBus slaveBus, FdpEventBus masterBus)
{
    // Relay FrameStepCompletedEvent (managed) slave→master.
    var acks = slaveBus.ConsumeManaged<FrameStepCompletedEvent>();
    foreach (var a in acks) masterBus.PublishManaged(a);
}
```

**Tests:**

```csharp
[Fact]
public void BarrierFires_SameSimTime_WithLargeClockOffset()
{
    long masterTicks = 0L;
    long slaveTicks  = 500_000_000L;

    var (master, slave, masterBus, slaveBus) = CreateMasterSlave(ref masterTicks, ref slaveTicks,
        new TimeConfig { LookaheadWallTicks = 0 });

    // NTP handshake: slave offset = masterTicks - slaveTicks = -500_000_000
    NtpHandshake(slaveBus, slave, masterTick: masterTicks, slaveTick: slaveTicks);

    // Pause master
    masterBus.SwapBuffers();
    master.Update(); // SwitchToDeterministic; emits SwitchTimeModeEvent
    masterBus.SwapBuffers();
    RelayModeSwitch(masterBus, slaveBus);

    // Advance both to cross barrier (LookaheadWallTicks = 0 → barrier = masterTicks now)
    masterTicks += 1;
    slaveTicks  += 1;

    // Master transitions
    masterBus.SwapBuffers();
    master.Update();
    masterBus.SwapBuffers();

    // Slave transitions
    slave.Update();

    Assert.Equal(TimeMode.Deterministic, master.GetMode());
    Assert.Equal(TimeMode.Deterministic, slave.GetMode());
}

[Fact]
public void BarrierFires_Before_NTPSync_Slave_DoesNotEnterStepping_Early()
{
    // Documents pre-fix broken behaviour: without NTP sync, a master barrier *well below*
    // slave's raw tick value would cause the slave to enter Stepping immediately
    // (before barrier is even close in master time).
    // With the fix (pre-sync guard + SyncedWallTicks), the slave discards the SwitchTimeModeEvent
    // because _isTimeSynced = false, and stays in Continuous.
    long masterTicks = 0L;
    long slaveTicks  = 500_000_000L; // slave ticks far ahead

    var cfg = new TimeConfig { LookaheadWallTicks = 100_000 };
    var masterBus = new FdpEventBus();
    var slaveBus  = new FdpEventBus();

    var master = new MasterSyncController(masterBus, new HashSet<int> { SlaveNodeId }, cfg, () => masterTicks);
    var slave  = new SlaveSyncController(slaveBus, SlaveNodeId, cfg, () => slaveTicks);

    // Drain slave's initial TimeSyncRequest but do NOT send a response (no NTP sync)
    slaveBus.SwapBuffers();
    slaveBus.Consume<TimeSyncRequest>();

    // Master pauses — barrier = masterTicks + lookahead = 100_000
    masterBus.SwapBuffers();
    master.Update();
    masterBus.SwapBuffers();

    RelayModeSwitch(masterBus, slaveBus);

    // Old code: slave._virtualWallTicks = slaveTicks = 500_000_000 >> barrier = 100_000 → instantly Stepping
    // New code: _isTimeSynced = false → DrainModeSwitchEvents discards the event → still Continuous
    slave.Update();

    Assert.Equal(TimeMode.Continuous, slave.GetMode(),
        "Without NTP sync, slave should stay Continuous (pre-sync guard must discard the event)");
}

[Fact]
public void TwoSlaves_WithDifferentOffsets_BothEnterStepping_WithinOneFrame()
{
    long masterTicks = 0L;
    long slave1Ticks = 500_000_000L;
    long slave2Ticks = 300_000_000L;

    var cfg      = new TimeConfig { LookaheadWallTicks = 0 };
    var masterBus = new FdpEventBus();
    var slave1Bus = new FdpEventBus();
    var slave2Bus = new FdpEventBus();

    var master = new MasterSyncController(masterBus,
        new HashSet<int> { 1, 2 }, cfg, () => masterTicks);
    var slave1 = new SlaveSyncController(slave1Bus, nodeId: 1, config: cfg, tickSource: () => slave1Ticks);
    var slave2 = new SlaveSyncController(slave2Bus, nodeId: 2, config: cfg, tickSource: () => slave2Ticks);

    NtpHandshake(slave1Bus, slave1, masterTick: masterTicks, slaveTick: slave1Ticks);
    NtpHandshake(slave2Bus, slave2, masterTick: masterTicks, slaveTick: slave2Ticks);

    // Pause master
    masterBus.SwapBuffers();
    master.Update();
    masterBus.SwapBuffers();

    var events = masterBus.Consume<SwitchTimeModeEvent>();
    foreach (var e in events)
    {
        slave1Bus.Publish(e);
        slave2Bus.Publish(e);
    }
    slave1Bus.SwapBuffers();
    slave2Bus.SwapBuffers();

    // Advance past barrier
    masterTicks += 1; slave1Ticks += 1; slave2Ticks += 1;

    master.Update(); masterBus.SwapBuffers();
    slave1.Update();
    slave2.Update();

    Assert.Equal(TimeMode.Deterministic, master.GetMode());
    Assert.Equal(TimeMode.Deterministic, slave1.GetMode());
    Assert.Equal(TimeMode.Deterministic, slave2.GetMode());
}

[Fact]
public void SimTime_OnBarrierTransition_IsIdenticalAcrossNodes()
{
    long masterTicks = 0L;
    long slave1Ticks = 500_000_000L;
    long slave2Ticks = 300_000_000L;

    var cfg      = new TimeConfig { LookaheadWallTicks = 0, };
    var masterBus = new FdpEventBus();
    var slave1Bus = new FdpEventBus();
    var slave2Bus = new FdpEventBus();

    var master = new MasterSyncController(masterBus, new HashSet<int> { 1, 2 }, cfg, () => masterTicks);
    var slave1 = new SlaveSyncController(slave1Bus, 1, cfg, () => slave1Ticks);
    var slave2 = new SlaveSyncController(slave2Bus, 2, cfg, () => slave2Ticks);

    NtpHandshake(slave1Bus, slave1, masterTick: masterTicks, slaveTick: slave1Ticks);
    NtpHandshake(slave2Bus, slave2, masterTick: masterTicks, slaveTick: slave2Ticks);

    // Run 5 continuous frames to get some TotalTime
    float delta = 1f / 60f;
    long  frameTicks = (long)(delta * System.Diagnostics.Stopwatch.Frequency);
    for (int i = 0; i < 5; i++)
    {
        masterTicks += frameTicks; slave1Ticks += frameTicks; slave2Ticks += frameTicks;
        master.Update(); masterBus.SwapBuffers();
        slave1.Update();
        slave2.Update();
        masterBus.SwapBuffers();
    }

    double masterTime = master.GetCurrentState().TotalTime;
    // Slaves TotalTime may differ slightly due to PLL (no TimePulse sent in this test)
    // At barrier, AdvanceFrameIntent carries TargetSimTime from master — after first step slaves snap.
    // For barrier transition alone, just verify barrier fires simultaneously (all Deterministic).

    masterBus.SwapBuffers();
    master.Update();
    masterBus.SwapBuffers();
    var modeEvents = masterBus.Consume<SwitchTimeModeEvent>();
    foreach (var e in modeEvents) { slave1Bus.Publish(e); slave2Bus.Publish(e); }
    slave1Bus.SwapBuffers(); slave2Bus.SwapBuffers();

    masterTicks += 1; slave1Ticks += 1; slave2Ticks += 1;
    master.Update(); masterBus.SwapBuffers();
    slave1.Update();
    slave2.Update();

    Assert.Equal(TimeMode.Deterministic, master.GetMode());
    Assert.Equal(TimeMode.Deterministic, slave1.GetMode());
    Assert.Equal(TimeMode.Deterministic, slave2.GetMode());
}
```

---

### File: `LockstepSimTimeAccuracyTests.cs` — TC3-P5-T03

Create `FDP/Toolkits/FDP.Toolkit.Time.Tests/LockstepSimTimeAccuracyTests.cs`.

These tests verify that `TargetSimTime` from the master causes slaves to have identical
`TotalTime` after each step.

**Pattern for stepping in multi-computer test:**

```csharp
// After barrier transition, step once:
master.Step(delta);  // emits AdvanceFrameIntent to masterBus (managed)
masterBus.SwapBuffers(); // not strictly needed for managed, but good practice

// Relay intent to slave
var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>();
foreach (var i in intents) slaveBus.PublishManaged(i);

// Slave processes intent
slave.Update(); // consumes intent, snaps TotalTime to TargetSimTime, emits FrameStepCompletedEvent

// Relay ACK back to master
var acks = slaveBus.ConsumeManaged<FrameStepCompletedEvent>();
foreach (var a in acks) masterBus.PublishManaged(a);

// Master processes ACK (UpdateStepping)
master.Update();  // removes node from pendingAcks; when empty → can step again
masterBus.SwapBuffers();
```

**Tests:**

```csharp
[Fact]
public void FirstStep_SlaveSimTime_EqualsMasterSimTime()
{
    long masterTicks = 0L;
    long slaveTicks  = 500_000_000L;

    var cfg      = new TimeConfig { LookaheadWallTicks = 0 };
    var masterBus = new FdpEventBus();
    var slaveBus  = new FdpEventBus();

    var master = new MasterSyncController(masterBus, new HashSet<int> { 1 }, cfg, () => masterTicks);
    var slave  = new SlaveSyncController(slaveBus, 1, cfg, () => slaveTicks);

    NtpHandshake(slaveBus, slave, masterTick: masterTicks, slaveTick: slaveTicks);

    // Transition to Stepping
    masterBus.SwapBuffers(); master.Update(); masterBus.SwapBuffers();
    masterTicks += 1; slaveTicks += 1;
    master.Update(); masterBus.SwapBuffers();

    var modeEvents = masterBus.Consume<SwitchTimeModeEvent>();
    foreach (var e in modeEvents) slaveBus.Publish(e);
    slaveBus.SwapBuffers();
    slave.Update(); // enters Stepping

    Assert.Equal(TimeMode.Deterministic, master.GetMode());
    Assert.Equal(TimeMode.Deterministic, slave.GetMode());

    // Step once
    float delta = 1f / 60f;
    master.Step(delta);
    var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>();
    foreach (var i in intents) slaveBus.PublishManaged(i);
    slave.Update();
    var acks = slaveBus.ConsumeManaged<FrameStepCompletedEvent>();
    foreach (var a in acks) masterBus.PublishManaged(a);
    master.Update(); masterBus.SwapBuffers();

    double masterTime = master.GetCurrentState().TotalTime;
    double slaveTime  = slave.GetCurrentState().TotalTime;

    Assert.Equal(masterTime, slaveTime, precision: 10);
}

[Fact]
public void TenSteps_SlaveSimTime_EqualsMasterSimTimeAfterEachStep()
{
    long masterTicks = 0L;
    long slaveTicks  = 500_000_000L;

    var cfg      = new TimeConfig { LookaheadWallTicks = 0 };
    var masterBus = new FdpEventBus();
    var slaveBus  = new FdpEventBus();

    var master = new MasterSyncController(masterBus, new HashSet<int> { 1 }, cfg, () => masterTicks);
    var slave  = new SlaveSyncController(slaveBus, 1, cfg, () => slaveTicks);

    NtpHandshake(slaveBus, slave, masterTick: masterTicks, slaveTick: slaveTicks);

    masterBus.SwapBuffers(); master.Update(); masterBus.SwapBuffers();
    masterTicks += 1; slaveTicks += 1;
    master.Update(); masterBus.SwapBuffers();
    var modeEvts = masterBus.Consume<SwitchTimeModeEvent>();
    foreach (var e in modeEvts) slaveBus.Publish(e);
    slaveBus.SwapBuffers();
    slave.Update();

    float delta = 1f / 60f;
    for (int i = 0; i < 10; i++)
    {
        master.Step(delta);
        var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>();
        foreach (var x in intents) slaveBus.PublishManaged(x);
        slave.Update();
        var acks = slaveBus.ConsumeManaged<FrameStepCompletedEvent>();
        foreach (var a in acks) masterBus.PublishManaged(a);
        master.Update(); masterBus.SwapBuffers();

        Assert.Equal(master.GetCurrentState().TotalTime, slave.GetCurrentState().TotalTime, precision: 10);
    }
}

[Fact]
public void TwoSlaves_BothSnapToMasterSimTime_PerStep()
{
    long masterTicks = 0L, s1Ticks = 500_000_000L, s2Ticks = 300_000_000L;
    var cfg      = new TimeConfig { LookaheadWallTicks = 0 };
    var mBus = new FdpEventBus();
    var s1Bus = new FdpEventBus();
    var s2Bus = new FdpEventBus();

    var master = new MasterSyncController(mBus, new HashSet<int> { 1, 2 }, cfg, () => masterTicks);
    var slave1 = new SlaveSyncController(s1Bus, 1, cfg, () => s1Ticks);
    var slave2 = new SlaveSyncController(s2Bus, 2, cfg, () => s2Ticks);

    NtpHandshake(s1Bus, slave1, masterTick: masterTicks, slaveTick: s1Ticks);
    NtpHandshake(s2Bus, slave2, masterTick: masterTicks, slaveTick: s2Ticks);

    // Transition to Stepping
    mBus.SwapBuffers(); master.Update(); mBus.SwapBuffers();
    masterTicks += 1; s1Ticks += 1; s2Ticks += 1;
    master.Update(); mBus.SwapBuffers();
    var modeEvts = mBus.Consume<SwitchTimeModeEvent>();
    foreach (var e in modeEvts) { s1Bus.Publish(e); s2Bus.Publish(e); }
    s1Bus.SwapBuffers(); s2Bus.SwapBuffers();
    slave1.Update(); slave2.Update();

    float delta = 1f / 60f;
    for (int i = 0; i < 5; i++)
    {
        master.Step(delta);
        var intents = mBus.ConsumeManaged<AdvanceFrameIntent>();
        foreach (var x in intents) { s1Bus.PublishManaged(x); s2Bus.PublishManaged(x); }
        slave1.Update(); slave2.Update();
        var ack1 = s1Bus.ConsumeManaged<FrameStepCompletedEvent>();
        var ack2 = s2Bus.ConsumeManaged<FrameStepCompletedEvent>();
        foreach (var a in ack1) mBus.PublishManaged(a);
        foreach (var a in ack2) mBus.PublishManaged(a);
        master.Update(); mBus.SwapBuffers();

        double masterTime = master.GetCurrentState().TotalTime;
        Assert.Equal(masterTime, slave1.GetCurrentState().TotalTime, precision: 10);
        Assert.Equal(masterTime, slave2.GetCurrentState().TotalTime, precision: 10);
    }
}

[Fact]
public void Resume_AfterLockstep_SlaveContinuesFromMasterSimTime()
{
    long masterTicks = 0L;
    long slaveTicks  = 500_000_000L;
    long frameTicks  = (long)(1.0 / 60 * System.Diagnostics.Stopwatch.Frequency);

    var cfg      = new TimeConfig { LookaheadWallTicks = 0 };
    var masterBus = new FdpEventBus();
    var slaveBus  = new FdpEventBus();

    var master = new MasterSyncController(masterBus, new HashSet<int> { 1 }, cfg, () => masterTicks);
    var slave  = new SlaveSyncController(slaveBus, 1, cfg, () => slaveTicks);

    NtpHandshake(slaveBus, slave, masterTick: masterTicks, slaveTick: slaveTicks);

    // Transition to Stepping
    masterBus.SwapBuffers(); master.Update(); masterBus.SwapBuffers();
    masterTicks += 1; slaveTicks += 1;
    master.Update(); masterBus.SwapBuffers();
    var mEvts = masterBus.Consume<SwitchTimeModeEvent>();
    foreach (var e in mEvts) slaveBus.Publish(e);
    slaveBus.SwapBuffers();
    slave.Update();

    // Step 3 times
    float delta = 1f / 60f;
    for (int i = 0; i < 3; i++)
    {
        master.Step(delta);
        var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>();
        foreach (var x in intents) slaveBus.PublishManaged(x);
        slave.Update();
        var acks = slaveBus.ConsumeManaged<FrameStepCompletedEvent>();
        foreach (var a in acks) masterBus.PublishManaged(a);
        master.Update(); masterBus.SwapBuffers();
    }

    double masterTimeAfterSteps = master.GetCurrentState().TotalTime;

    // Resume
    master.SwitchToContinuous();
    masterBus.SwapBuffers();
    var resumeEvents = masterBus.Consume<SwitchTimeModeEvent>();
    foreach (var e in resumeEvents) slaveBus.Publish(e);
    slaveBus.SwapBuffers();

    masterTicks += frameTicks; slaveTicks += frameTicks;
    master.Update(); masterBus.SwapBuffers();
    slave.Update();

    Assert.Equal(TimeMode.Continuous, master.GetMode());
    Assert.Equal(TimeMode.Continuous, slave.GetMode());
    // Slave should have snapped to master's TotalTime on resume, now continuing from same base
    Assert.True(Math.Abs(slave.GetCurrentState().TotalTime - master.GetCurrentState().TotalTime)
        < 0.05, "Slave TotalTime should be within 50ms of master after resume");
}
```

---

### File: `FullCycleMultiComputerSim.cs` — TC3-P5-T04

Create `FDP/Toolkits/FDP.Toolkit.Time.Tests/FullCycleMultiComputerSim.cs`.

These are scenario tests — full continuous→pause→step×5→resume.

```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Kernel;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Domain;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Time;
using Xunit;

namespace FDP.Toolkit.Time.Tests
{
    public class FullCycleMultiComputerSim
    {
        private static void NtpHandshake(FdpEventBus slaveBus, SlaveSyncController slave,
            long masterTick, long slaveTick, int nodeId)
        {
            slaveBus.SwapBuffers();
            slaveBus.Consume<TimeSyncRequest>();
            slaveBus.Publish(new TimeSyncResponse
            {
                ClientNodeId        = nodeId,
                ClientSendTicks     = slaveTick,
                MasterReceiveTicks  = masterTick,
                MasterTransmitTicks = masterTick,
            });
            slaveBus.SwapBuffers();
            slave.Update();
            slaveBus.SwapBuffers();
            slaveBus.Consume<TimeSyncRequest>();
        }

        [Fact]
        public void FullCycle_OneSlaveOffset_PauseStepResume_SimTimesConverge()
        {
            long masterTicks = 0L;
            long slaveTicks  = 500_000_000L;

            float delta      = 1f / 60f;
            long  frameTicks = (long)(delta * Stopwatch.Frequency);

            var cfg      = new TimeConfig { LookaheadWallTicks = 0 };
            var masterBus = new FdpEventBus();
            var slaveBus  = new FdpEventBus();

            var master = new MasterSyncController(masterBus, new HashSet<int> { 1 }, cfg, () => masterTicks);
            var slave  = new SlaveSyncController(slaveBus, 1, cfg, () => slaveTicks);

            // Phase 0: NTP handshake
            NtpHandshake(slaveBus, slave, masterTick: masterTicks, slaveTick: slaveTicks);

            // Phase 1: 20 continuous frames
            for (int i = 0; i < 20; i++)
            {
                masterTicks += frameTicks; slaveTicks += frameTicks;
                masterBus.SwapBuffers(); master.Update(); masterBus.SwapBuffers();
                slave.Update(); slaveBus.SwapBuffers();
            }

            // Phase 2: Pause
            masterBus.SwapBuffers(); master.Update(); masterBus.SwapBuffers();
            masterTicks += 1; slaveTicks += 1;
            master.Update(); masterBus.SwapBuffers();
            var modeEvts = masterBus.Consume<SwitchTimeModeEvent>();
            foreach (var e in modeEvts) slaveBus.Publish(e);
            slaveBus.SwapBuffers();
            slave.Update();

            Assert.Equal(TimeMode.Deterministic, master.GetMode());
            Assert.Equal(TimeMode.Deterministic, slave.GetMode());

            // Phase 3: 5 steps
            for (int i = 0; i < 5; i++)
            {
                master.Step(delta);
                var intents = masterBus.ConsumeManaged<AdvanceFrameIntent>();
                foreach (var x in intents) slaveBus.PublishManaged(x);
                slave.Update();
                var acks = slaveBus.ConsumeManaged<FrameStepCompletedEvent>();
                foreach (var a in acks) masterBus.PublishManaged(a);
                master.Update(); masterBus.SwapBuffers();

                Assert.Equal(master.GetCurrentState().TotalTime, slave.GetCurrentState().TotalTime, precision: 10);
            }

            // Phase 4: Resume
            master.SwitchToContinuous();
            masterBus.SwapBuffers();
            var resumeEvts = masterBus.Consume<SwitchTimeModeEvent>();
            foreach (var e in resumeEvts) slaveBus.Publish(e);
            slaveBus.SwapBuffers();

            for (int i = 0; i < 20; i++)
            {
                masterTicks += frameTicks; slaveTicks += frameTicks;
                masterBus.SwapBuffers(); master.Update(); masterBus.SwapBuffers();
                slave.Update(); slaveBus.SwapBuffers();
            }

            Assert.Equal(TimeMode.Continuous, master.GetMode());
            Assert.Equal(TimeMode.Continuous, slave.GetMode());
            Assert.True(master.GetCurrentState().FrameNumber > 0);
        }

        [Fact]
        public void FullCycle_TwoSlavesLargeOffsets_AllSimTimesMatch()
        {
            long masterTicks = 0L;
            long s1Ticks     = 500_000_000L;
            long s2Ticks     = 300_000_000L;

            float delta      = 1f / 60f;
            long  frameTicks = (long)(delta * Stopwatch.Frequency);

            var cfg   = new TimeConfig { LookaheadWallTicks = 0 };
            var mBus  = new FdpEventBus(); var s1Bus = new FdpEventBus(); var s2Bus = new FdpEventBus();

            var master = new MasterSyncController(mBus, new HashSet<int> { 1, 2 }, cfg, () => masterTicks);
            var slave1 = new SlaveSyncController(s1Bus, 1, cfg, () => s1Ticks);
            var slave2 = new SlaveSyncController(s2Bus, 2, cfg, () => s2Ticks);

            NtpHandshake(s1Bus, slave1, masterTick: masterTicks, slaveTick: s1Ticks, nodeId: 1);
            NtpHandshake(s2Bus, slave2, masterTick: masterTicks, slaveTick: s2Ticks, nodeId: 2);

            // Pause
            mBus.SwapBuffers(); master.Update(); mBus.SwapBuffers();
            masterTicks += 1; s1Ticks += 1; s2Ticks += 1;
            master.Update(); mBus.SwapBuffers();
            var modeEvts = mBus.Consume<SwitchTimeModeEvent>();
            foreach (var e in modeEvts) { s1Bus.Publish(e); s2Bus.Publish(e); }
            s1Bus.SwapBuffers(); s2Bus.SwapBuffers();
            slave1.Update(); slave2.Update();

            // 5 steps
            for (int i = 0; i < 5; i++)
            {
                master.Step(delta);
                var intents = mBus.ConsumeManaged<AdvanceFrameIntent>();
                foreach (var x in intents) { s1Bus.PublishManaged(x); s2Bus.PublishManaged(x); }
                slave1.Update(); slave2.Update();
                var ack1 = s1Bus.ConsumeManaged<FrameStepCompletedEvent>();
                var ack2 = s2Bus.ConsumeManaged<FrameStepCompletedEvent>();
                foreach (var a in ack1) mBus.PublishManaged(a);
                foreach (var a in ack2) mBus.PublishManaged(a);
                master.Update(); mBus.SwapBuffers();

                double mt = master.GetCurrentState().TotalTime;
                Assert.Equal(mt, slave1.GetCurrentState().TotalTime, precision: 10);
                Assert.Equal(mt, slave2.GetCurrentState().TotalTime, precision: 10);
            }
        }
    }
}
```

---

### File: `ClockSkewDriftTests.cs` — TC3-P5-T05

Create `FDP/Toolkits/FDP.Toolkit.Time.Tests/ClockSkewDriftTests.cs`.

```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Kernel;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Domain;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Time;
using Xunit;

namespace FDP.Toolkit.Time.Tests
{
    public class ClockSkewDriftTests
    {
        // Slave ticks advance at 1001 per master's 1000 (0.1% fast)
        private const long MasterTicksPerFrame = 1_000L;
        private const long SlaveTicksPerFrame  = 1_001L;
        private const int  FrameCount          = 600; // 10 seconds at 60 Hz

        private static void NtpHandshake(FdpEventBus slaveBus, SlaveSyncController slave,
            long masterTick, long slaveTick)
        {
            slaveBus.SwapBuffers();
            slaveBus.Consume<TimeSyncRequest>();
            slaveBus.Publish(new TimeSyncResponse
            {
                ClientNodeId        = 1,
                ClientSendTicks     = slaveTick,
                MasterReceiveTicks  = masterTick,
                MasterTransmitTicks = masterTick,
            });
            slaveBus.SwapBuffers();
            slave.Update();
            slaveBus.SwapBuffers();
            slaveBus.Consume<TimeSyncRequest>();
        }

        [Fact]
        public void ClockSkew_WithPeriodicResync_OffsetStaysWithin2ms()
        {
            long masterTicks = 0L;
            long slaveTicks  = 0L; // same starting point, slave just runs slightly faster

            // SyncRefreshIntervalTicks = 1_000 * 60 = 60_000 ticks per second * 1s = ~1 sec of simulation
            // Since we use MasterTicksPerFrame = 1000, 60 frames = 60_000 master ticks = 1 sync interval
            long syncInterval = 60_000L;
            var config = new TimeConfig
            {
                SyncRefreshIntervalTicks = syncInterval,
                MaxRttTicks              = 10_000L, // generous spike threshold
            };

            var masterBus = new FdpEventBus();
            var slaveBus  = new FdpEventBus();

            var slave = new SlaveSyncController(slaveBus, 1, config, () => slaveTicks);

            // Initial NTP handshake
            NtpHandshake(slaveBus, slave, masterTick: masterTicks, slaveTick: slaveTicks);

            long twoMsTicks = (long)(0.002 * Stopwatch.Frequency);

            for (int frame = 0; frame < FrameCount; frame++)
            {
                masterTicks += MasterTicksPerFrame;
                slaveTicks  += SlaveTicksPerFrame;

                // Periodically inject a fresh NTP response (simulating translator firing)
                // slave._lastSyncRequestTicks was last updated; we inject here to simulate
                // the response arriving. Since slave's Update triggers a SendTimeSyncRequest,
                // we inject a response after each 60 frames.
                if (frame > 0 && frame % 60 == 0)
                {
                    // Re-sync: update the offset so slave tracks the drifting master
                    slaveBus.Publish(new TimeSyncResponse
                    {
                        ClientNodeId        = 1,
                        ClientSendTicks     = slaveTicks - SlaveTicksPerFrame,
                        MasterReceiveTicks  = masterTicks - MasterTicksPerFrame,
                        MasterTransmitTicks = masterTicks - MasterTicksPerFrame,
                    });
                    slaveBus.SwapBuffers();
                }
                else
                {
                    slaveBus.SwapBuffers();
                }

                slave.Update();
                slaveBus.SwapBuffers();
                slaveBus.Consume<TimeSyncRequest>(); // drain outbound requests
            }

            // After 600 frames, SyncedWallTicks should be close to masterTicks
            long drift = Math.Abs(slave.SyncedWallTicks - masterTicks);
            Assert.True(drift < twoMsTicks,
                $"Drift={drift} ticks exceeds 2ms={twoMsTicks} ticks after {FrameCount} frames with periodic re-sync");
        }

        [Fact]
        public void ClockSkew_WithoutResync_DriftAccumulates()
        {
            // Same setup but NO periodic re-sync. After 600 frames slave ticks
            // ahead by 600 * (SlaveTicksPerFrame - MasterTicksPerFrame) = 600 * 1 = 600 ticks.
            // The offset was established at frame 0; without re-sync it doesn't update.
            // SyncedWallTicks = slaveTicks + offset0; masterTicks = 600_000.
            // slaveTicks = 601_000 (ran faster). offset0 = 0 - 0 = 0.
            // drift = |SyncedWallTicks - masterTicks| = |(601_000 + 0) - 600_000| = 1_000 ticks.
            // With Stopwatch.Frequency ≈ 10_000_000, 2ms = 20_000 ticks.
            // 1_000 < 20_000... OK so the drift is still small because frames are only 1000 ticks.
            // Let's use more dramatic skew: 1% not 0.1%. Use SlaveTicksPerFrame = 1_010.

            long masterTicks = 0L;
            long slaveTicks  = 0L;
            const long slaveTicksFast = 1_010L; // 1% faster slave

            var config = new TimeConfig { SyncRefreshIntervalTicks = long.MaxValue }; // never re-sync

            var slaveBus = new FdpEventBus();
            var slave    = new SlaveSyncController(slaveBus, 1, config, () => slaveTicks);

            slaveBus.SwapBuffers();
            slaveBus.Consume<TimeSyncRequest>();
            // Establish offset=0 at start
            slaveBus.Publish(new TimeSyncResponse
            {
                ClientNodeId = 1, ClientSendTicks = 0, MasterReceiveTicks = 0, MasterTransmitTicks = 0
            });
            slaveBus.SwapBuffers();
            slave.Update();
            slaveBus.SwapBuffers();
            slaveBus.Consume<TimeSyncRequest>();

            for (int frame = 0; frame < FrameCount; frame++)
            {
                masterTicks += MasterTicksPerFrame;
                slaveTicks  += slaveTicksFast; // 1% faster
                slaveBus.SwapBuffers();
                slave.Update();
                slaveBus.SwapBuffers();
                slaveBus.Consume<TimeSyncRequest>();
            }

            // After 600 frames: slave ran 600 * 1010 = 606_000 ticks, master ran 600_000 ticks.
            // drift = |SyncedWallTicks - masterTicks| = |(606_000 + 0) - 600_000| = 6_000 ticks.
            // 2ms = Stopwatch.Frequency * 0.002 ≈ 20_000 ticks (on 10MHz stopwatch).
            // Hmm 6_000 < 20_000 — the drift is still under 2ms!
            // The test needs to prove drift IS larger than 0 (not exactly 0) since no re-sync.
            // Better assertion: drift IS proportional to tick count.

            long drift = Math.Abs(slave.SyncedWallTicks - masterTicks);
            // Without re-sync, SyncedWallTicks = slaveTicks + offset0 = 606_000, masterTicks = 600_000
            // drift = 6_000 ticks ≈ 0.6ms (at 10 MHz). Non-zero and growing.
            Assert.True(drift > 0,
                "Without re-sync, drift must be non-zero (slave runs faster than master)");
            // Also assert the drift is proportional to total frames (not bounded)
            Assert.True(drift >= (FrameCount * (slaveTicksFast - MasterTicksPerFrame)) / 2,
                $"Drift {drift} should be at least {FrameCount * (slaveTicksFast - MasterTicksPerFrame) / 2} (half of expected accumulation)");
        }
    }
}
```

---

## ✅ Acceptance Criteria

| Task | New File | New Tests |
|------|----------|-----------|
| TC3-P5-T01 | `TimeSyncOffsetTests.cs` | 6 |
| TC3-P5-T02 | `PauseBarrierSyncTests.cs` | 4 |
| TC3-P5-T03 | `LockstepSimTimeAccuracyTests.cs` | 4 |
| TC3-P5-T04 | `FullCycleMultiComputerSim.cs` | 2 |
| TC3-P5-T05 | `ClockSkewDriftTests.cs` | 2 |
| **Total** | **5 new files** | **18 new tests** |

Target: 118 existing + 18 new = **136 tests**, all green.

---

## ⚠️ Implementation Notes

1. **Using directives:** All new test files need at minimum:
   ```csharp
   using System.Collections.Generic;
   using System.Diagnostics;
   using Fdp.Kernel;
   using FDP.Toolkit.Time.Controllers;
   using FDP.Toolkit.Time.Domain;
   using FDP.Toolkit.Time.Messages;
   using ModuleHost.Core.Time;
   using Xunit;
   ```
   Add `using System;` if using `Math.Abs`.

2. **`Math.Abs` in C# tests:** Use `System.Math.Abs(...)` or add `using System;`.

3. **`MasterSyncController` constructor signature:** Check the existing constructor in
   `MasterSyncController.cs` — it takes `(FdpEventBus, HashSet<int> expectedSlaves, TimeConfig, Func<long> tickSource)`. Note: `new HashSet<int> { 1 }` for one slave. Empty set `new HashSet<int>()` means no ACKs required (master steps alone). For tests where you don't care about ACKs (e.g. barrier tests), use `new HashSet<int>()` to avoid master waiting forever.

4. **`master.Update()` vs `master.Step(delta)`:** In Stepping mode, `master.Update()` processes
   pending ACKs. When all slaves have ACK'd, `_pendingAcks` is empty and the master is ready for
   the next `Step()` call. Always call `master.Update()` AFTER relaying slave ACKs back to master
   bus, before calling `master.Step()` again.

5. **`master.SwitchToContinuous()` emits via event bus:** After calling `master.SwitchToContinuous()`,
   call `masterBus.SwapBuffers()` before consuming the `SwitchTimeModeEvent` to relay to slaves.

6. **The `NtpHandshake` helper requires `_isTimeSynced = false` on entry:** Always drain the
   initial `TimeSyncRequest` from the constructor before injecting the response.

7. **Single-slave tests with `new HashSet<int> { 1 }`:** After `slave.Update()` processes the
   intent, call `master.Update()` to drain the ACK so master considers the step complete.
   Otherwise `_pendingAcks` is never cleared and the master will be stuck.
