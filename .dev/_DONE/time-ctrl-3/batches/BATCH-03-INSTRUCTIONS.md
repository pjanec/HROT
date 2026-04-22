# BATCH-03: Corrective-01 + Translators + NetworkModule Factory

**Batch Number:** BATCH-03  
**Tasks:** Corrective-01 (P2 bug fix), TC3-P4-T01, TC3-P4-T02, TC3-P4-T03  
**Phase:** Phase 4 — Translators & Network Module (plus P2 corrective)  
**Previous Review:** `.dev/time-ctrl-3/reviews/BATCH-02-REVIEW.md`

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch has two concerns:

**Part A — Corrective-01 (mandatory, implement first):** Fix a P2 bug in
`SlaveSyncController.OnTimePulseReceived` where the hard-snap path incorrectly sets
`_lastUpdateRawTicks` to the master-domain clock value instead of the local raw tick.

**Part B — Phase 4 translators (TC3-P4-T01 through TC3-P4-T03):** Create two new DDS
translator files (`MasterTimeSyncTranslator.cs`, `SlaveTimeSyncTranslator.cs`) and wire
them through factory methods in `TimeNetworkModule`.  These translators carry
`TimeSyncRequest` / `TimeSyncResponse` across the DDS network, completing the NTP
infrastructure started in Phase 3.

### Required Reading (IN ORDER)

1. **Developer Workflow:** `.github/skills/developer/SKILL.md`
2. **Design Document:** `.dev/time-ctrl-3/DESIGN.md` — §6 (Feature D: Translators)
3. **Task Definitions:** `.dev/time-ctrl-3/TASK-DETAIL.md` — TC3-P4-T01 through TC3-P4-T03
4. **Previous Review:** `.dev/time-ctrl-3/reviews/BATCH-02-REVIEW.md` — understand Corrective-01
5. **Reference pattern:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/MasterLockstepTranslator.cs` — existing translator to model

### Source Code Location

- **Corrective-01 fix:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`
- **New files:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/MasterTimeSyncTranslator.cs`
- **New files:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/SlaveTimeSyncTranslator.cs`
- **Modified:** `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs`
- **Test file (modify existing):** `FDP/Toolkits/FDP.Toolkit.Time.Tests/LockstepTranslatorTests.cs`
  — or create `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeSyncTranslatorTests.cs`

### Build/Test Command

```
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet test Toolkits\FDP.Toolkit.Time.Tests\FDP.Toolkit.Time.Tests.csproj --verbosity minimal
```

All pre-existing 109 tests must remain green.

### Report Submission

`.dev/time-ctrl-3/reports/BATCH-03-REPORT.md`

---

## ⚠️ Part A — Corrective-01: Fix _lastUpdateRawTicks in Hard-Snap Path

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`

**Bug:** In `OnTimePulseReceived`, after TC3-P3-T04, `currentAbsTicks = SyncedWallTicks`.
The hard-snap branch then does:
```csharp
_lastUpdateRawTicks = currentAbsTicks;  // WRONG: SyncedWallTicks = _getTick() + offset
```
`UpdateContinuous` and `UpdateBarrierPending` compute `rawDelta = _getTick() - _lastUpdateRawTicks`.
With offset ≠ 0, the next frame's `rawDelta` is off by `−offset`, producing a corrupted time
delta.

**Fix:** Change that one line to use the raw tick instead:

```csharp
_lastUpdateRawTicks = _getTick();   // CORRECT: raw local clock baseline
```

**Success condition (add 1 test to `SlaveSyncControllerTests.cs`):**

- **Corrective-01-SC1** `SlaveSyncController_HardSnap_DoesNotCorruptRawDelta`  
  Setup: tick source starts at `T0 = 0`. Construct slave with a large offset
  (`_masterWallClockOffset` set via a sync response targeting offset = `500_000_000`).  
  Trigger a hard snap: publish a `TimePulseDescriptor` with `MasterWallTicks = 0` and
  `SimTimeSnapshot` that is `SnapThresholdMs * 2 / 1000` seconds ahead of current TotalTime
  (e.g. `SimTimeSnapshot = 2.0` when TotalTime is near 0 —  gives ~2000ms error > 500ms
  threshold).  Call `Update()`.  
  Advance tick source by exactly `TicksFromSeconds(0.016)`.  Call `Update()` again.  
  Assert `DeltaTime` of the second call is within ±20% of `0.016f` (it must NOT be near
  `0.016 + offset/Frequency` which would be grossly large for a 500M-tick offset).

  Test method: `SlaveSyncController_HardSnap_DoesNotCorruptRawDelta`

---

## 📝 Part B — Phase 4: Translators

### Context: Existing Translator Pattern

Look at `MasterLockstepTranslator.cs` and `SlaveLockstepTranslator.cs` before implementing.
Key patterns:
- `DdsWriter<T>?` / `DdsReader<T>?` — nullable; null when `participant == null`.
- All DDS operations guarded by `if (_xxxWriter is null) continue;` or `if (_xxxReader is null) return;`.
- `ScanAndPublish(ISimulationView view)` — egress: drain bus, write to DDS.
- `PollIngress(FdpEventBus bus, ISimulationView view)` — ingress: read from DDS, publish to bus.
- `ApplyToEntity` / `IDisposable.Dispose` — both no-ops in lockstep translators.

### Imports needed for new translator files

```csharp
using System;
using System.Diagnostics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Abstractions;
```

---

### TC3-P4-T01 — Implement MasterTimeSyncTranslator

**File to create:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/MasterTimeSyncTranslator.cs`

```csharp
using System;
using System.Diagnostics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Time.Translators
{
    /// <summary>
    /// Master-side NTP clock-sync translator.
    /// <para>
    /// <b>Ingress only:</b> reads <see cref="TimeSyncRequest"/> samples from DDS, records the
    /// master receive timestamp, constructs a <see cref="TimeSyncResponse"/>, records the
    /// transmit timestamp, and writes the response back to DDS — all without touching the
    /// event bus.
    /// </para>
    /// <para>
    /// <b>Egress:</b> no-op.  The master does not send requests.
    /// </para>
    /// <para>
    /// Pass <see langword="null"/> for <paramref name="participant"/> in unit-test environments;
    /// all DDS operations become safe no-ops.
    /// </para>
    /// </summary>
    public sealed class MasterTimeSyncTranslator : IDescriptorTranslator
    {
        private const string TopicNameValue = "TimeSyncRequest";
        private const long   OrdinalValue   = 205L;

        private readonly DdsReader<TimeSyncRequest>?  _requestReader;
        private readonly DdsWriter<TimeSyncResponse>? _responseWriter;
        private readonly Func<long>                   _getTick;

        public string TopicName         => TopicNameValue;
        public long   DescriptorOrdinal => OrdinalValue;

        /// <summary>Creates the translator.</summary>
        /// <param name="participant">DDS domain participant. Pass <see langword="null"/> for unit tests.</param>
        /// <param name="tickSource">
        /// Optional tick source override (<c>Stopwatch.GetTimestamp</c> by default).
        /// Inject a controlled counter in unit tests.
        /// </param>
        public MasterTimeSyncTranslator(DdsParticipant? participant, Func<long>? tickSource = null)
        {
            _getTick = tickSource ?? Stopwatch.GetTimestamp;

            if (participant is not null)
            {
                _requestReader  = new DdsReader<TimeSyncRequest>(participant);
                _responseWriter = new DdsWriter<TimeSyncResponse>(participant);
            }
        }

        /// <summary>No-op — master does not send sync requests.</summary>
        public void ScanAndPublish(ISimulationView view) { }

        /// <summary>
        /// Reads all pending <see cref="TimeSyncRequest"/> samples; for each, builds and writes
        /// a <see cref="TimeSyncResponse"/> with master-side receive and transmit timestamps.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_requestReader is null || _responseWriter is null) return;

            using var samples = _requestReader.Take();
            foreach (var sample in samples)
            {
                if (!sample.IsValid) continue;
                var request = sample.Data;

                long masterReceiveTicks  = _getTick();

                var response = new TimeSyncResponse
                {
                    ClientNodeId        = request.ClientNodeId,
                    ClientSendTicks     = request.ClientSendTicks,
                    MasterReceiveTicks  = masterReceiveTicks,
                    MasterTransmitTicks = 0, // filled in after
                };

                long masterTransmitTicks = _getTick();
                response.MasterTransmitTicks = masterTransmitTicks;

                _responseWriter.Write(response);

                FDP.Kernel.Logging.FdpLog<MasterTimeSyncTranslator>.Debug(
                    "[TC3][Master] SyncResponse sent. Node={0}, RTT_approx={1} ticks",
                    request.ClientNodeId,
                    masterTransmitTicks - request.ClientSendTicks);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
```

**Success conditions (add to `TimeSyncTranslatorTests.cs` or `LockstepTranslatorTests.cs`):**

- **TC3-P4-T01-SC1** `MasterTimeSyncTranslator_NullParticipant_PollIngress_IsNoOp`  
  ```csharp
  var t = new MasterTimeSyncTranslator(participant: null);
  t.ScanAndPublish(null!);
  t.PollIngress(null!, null!);    // must not throw
  ```

- **TC3-P4-T01-SC2** `MasterTimeSyncTranslator_DescriptorOrdinalAndTopicName_AreCorrect`  
  Assert `DescriptorOrdinal == 205L` and `TopicName == "TimeSyncRequest"`.

---

### TC3-P4-T02 — Implement SlaveTimeSyncTranslator

**File to create:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/SlaveTimeSyncTranslator.cs`

```csharp
using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Time.Messages;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Time.Translators
{
    /// <summary>
    /// Slave-side NTP clock-sync translator.
    /// <para>
    /// <b>Egress:</b> drains <see cref="TimeSyncRequest"/> from the <see cref="FdpEventBus"/>
    /// and writes each to the <c>TimeSyncRequest</c> DDS topic.
    /// </para>
    /// <para>
    /// <b>Ingress:</b> reads <see cref="TimeSyncResponse"/> samples from DDS; for samples
    /// addressed to <c>_localNodeId</c>, publishes onto the <see cref="FdpEventBus"/> so
    /// <see cref="Controllers.SlaveSyncController.DrainTimeSyncResponses"/> can consume them.
    /// Responses addressed to other nodes are silently discarded.
    /// </para>
    /// <para>
    /// Pass <see langword="null"/> for <paramref name="participant"/> in unit-test environments;
    /// all DDS operations become safe no-ops.
    /// </para>
    /// </summary>
    public sealed class SlaveTimeSyncTranslator : IDescriptorTranslator
    {
        private const string TopicNameValue = "TimeSyncResponse";
        private const long   OrdinalValue   = 206L;

        private readonly DdsWriter<TimeSyncRequest>?  _requestWriter;
        private readonly DdsReader<TimeSyncResponse>? _responseReader;
        private readonly FdpEventBus _eventBus;
        private readonly int         _localNodeId;

        public string TopicName         => TopicNameValue;
        public long   DescriptorOrdinal => OrdinalValue;

        /// <summary>Creates the translator.</summary>
        /// <param name="participant">DDS domain participant. Pass <see langword="null"/> for unit tests.</param>
        /// <param name="eventBus">Event bus shared with <see cref="Controllers.SlaveSyncController"/>.</param>
        /// <param name="localNodeId">This slave's node ID — used to filter incoming responses.</param>
        public SlaveTimeSyncTranslator(DdsParticipant? participant, FdpEventBus eventBus, int localNodeId)
        {
            _eventBus    = eventBus    ?? throw new ArgumentNullException(nameof(eventBus));
            _localNodeId = localNodeId;

            if (participant is not null)
            {
                _requestWriter  = new DdsWriter<TimeSyncRequest>(participant);
                _responseReader = new DdsReader<TimeSyncResponse>(participant);
            }
        }

        /// <summary>
        /// Drains <see cref="TimeSyncRequest"/> from the bus and writes each to DDS.
        /// Called every frame on the slave node.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var requests = _eventBus.Consume<TimeSyncRequest>();
            foreach (var request in requests)
            {
                if (_requestWriter is null) continue;
                _requestWriter.Write(request);
            }
        }

        /// <summary>
        /// Reads <see cref="TimeSyncResponse"/> samples from DDS; publishes those addressed
        /// to this node onto the bus.  Discards responses for other nodes.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_responseReader is null) return;

            using var samples = _responseReader.Take();
            foreach (var sample in samples)
            {
                if (!sample.IsValid) continue;
                var response = sample.Data;
                if (response.ClientNodeId != _localNodeId) continue;
                _eventBus.Publish(response);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
```

**Success conditions:**

- **TC3-P4-T02-SC1** `SlaveTimeSyncTranslator_NullParticipant_IsNoOp`  
  Construct with null participant, call `ScanAndPublish(null!)` and `PollIngress(null!, null!)`.
  Must not throw.

- **TC3-P4-T02-SC2** `SlaveTimeSyncTranslator_ScanAndPublish_DrainsRequestsFromBus`  
  Construct with null participant.  Publish a `TimeSyncRequest` onto the bus.  Swap bus.  Call
  `ScanAndPublish(null!)`.  Swap bus again.  Assert `bus.Consume<TimeSyncRequest>()` is empty
  (the translator drained it, even though _requestWriter is null).

- **TC3-P4-T02-SC3** `SlaveTimeSyncTranslator_DescriptorOrdinalAndTopicName_AreCorrect`  
  Assert `DescriptorOrdinal == 206L` and `TopicName == "TimeSyncResponse"`.

---

### TC3-P4-T03 — Add factory methods to TimeNetworkModule

**File:** `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs`

Add the two factory methods at the end of the `TimeNetworkModule` class, before the closing
brace.  Follow the exact XML doc style of the existing methods:

```csharp
/// <summary>
/// Creates a <see cref="Translators.MasterTimeSyncTranslator"/> that handles the
/// NTP-style two-way clock sync handshake for the master/orchestrator node.
/// <para>
/// Add the returned translator to the <c>customTranslators</c> list of the master
/// node's <c>CycloneNetworkModule</c> during application startup.
/// </para>
/// </summary>
/// <param name="participant">
/// DDS domain participant.  Pass <see langword="null"/> for test-only hosts —
/// all DDS operations become safe no-ops.
/// </param>
/// <param name="tickSource">
/// Optional tick source override (<c>Stopwatch.GetTimestamp</c> by default).
/// Inject a controlled counter in unit tests.
/// </param>
public static IDescriptorTranslator CreateMasterTimeSyncTranslator(
    DdsParticipant? participant,
    Func<long>?     tickSource = null)
{
    return new Translators.MasterTimeSyncTranslator(participant, tickSource);
}

/// <summary>
/// Creates a <see cref="Translators.SlaveTimeSyncTranslator"/> for slave nodes
/// (IG, ExCon, SimHost-slave).
/// <para>
/// Add the returned translator to the <c>customTranslators</c> list of the slave
/// node's <c>CycloneNetworkModule</c> during application startup.
/// </para>
/// </summary>
/// <param name="participant">
/// DDS domain participant.  Pass <see langword="null"/> for test-only hosts —
/// all DDS operations become safe no-ops.
/// </param>
/// <param name="eventBus">
/// The event bus shared with the local <see cref="Controllers.SlaveSyncController"/>.
/// Must not be null.
/// </param>
/// <param name="localNodeId">
/// This node's ID — used to filter incoming <see cref="Messages.TimeSyncResponse"/>
/// samples to those addressed to this specific slave.
/// </param>
public static IDescriptorTranslator CreateSlaveTimeSyncTranslator(
    DdsParticipant? participant,
    FdpEventBus     eventBus,
    int             localNodeId)
{
    if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
    return new Translators.SlaveTimeSyncTranslator(participant, eventBus, localNodeId);
}
```

**Success conditions (add to test file):**

- **TC3-P4-T03-SC1** `TimeNetworkModule_CreateMasterTimeSyncTranslator_NullParticipant_ReturnsInstance`  
  Assert `TimeNetworkModule.CreateMasterTimeSyncTranslator(null)` is not null and does not throw.

- **TC3-P4-T03-SC2** `TimeNetworkModule_CreateSlaveTimeSyncTranslator_NullParticipant_ReturnsInstance`  
  Assert `TimeNetworkModule.CreateSlaveTimeSyncTranslator(null, new FdpEventBus(), 5)` is not null.

- **TC3-P4-T03-SC3** `TimeNetworkModule_CreateSlaveTimeSyncTranslator_NullBus_Throws`  
  Assert `TimeNetworkModule.CreateSlaveTimeSyncTranslator(null, null!, 5)` throws
  `ArgumentNullException`.

---

## ✅ Acceptance Criteria

| Task | New Tests | Fixed Bugs |
|------|-----------|------------|
| Corrective-01 | 1 (SC1) | 1-line fix in `OnTimePulseReceived` |
| TC3-P4-T01 | 2 | — |
| TC3-P4-T02 | 3 | — |
| TC3-P4-T03 | 3 | — |
| **Total** | **9 new tests** | |

Target: 109 existing + 9 new = **118 tests**, all green.

---

## 📁 Files to Change

| File | Type |
|------|------|
| `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs` | Modify (1 line fix) |
| `FDP/Toolkits/FDP.Toolkit.Time/Translators/MasterTimeSyncTranslator.cs` | Create |
| `FDP/Toolkits/FDP.Toolkit.Time/Translators/SlaveTimeSyncTranslator.cs` | Create |
| `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs` | Modify (add 2 factory methods) |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeSyncTranslatorTests.cs` (or `LockstepTranslatorTests.cs`) | Create or modify |
| `FDP/Toolkits/FDP.Toolkit.Time.Tests/SlaveSyncControllerTests.cs` | Modify (add Corrective-01 test) |

---

## ⚠️ Implementation Notes

1. **DdsReader.TakeAll() vs Consume():** Check how existing translators read from DDS.
   `MasterLockstepTranslator` uses `_ackReader.TakeAll()` (see `PollIngress`).  Use the same
   pattern for both new translators.

2. **FDP.Kernel.Logging.FdpLog<T>.Debug:** Use the fully-qualified form (as used in
   `SlaveSyncController.cs` at line ~380) to avoid ambiguity if `FDP.Kernel.Logging` is not in
   the file-scope usings.  Alternatively, add `using FDP.Kernel.Logging;` at the top of the
   new file.

3. **IEntityView:** The `ApplyToEntity` method signature uses `Fdp.Interfaces.IEntityView`.
   Check existing translators for the exact type and interface the method references.

4. **`ScanAndPublish` drains bus even when DDS is null:** `SlaveTimeSyncTranslator.ScanAndPublish`
   should always drain the `TimeSyncRequest` items from the bus with `_eventBus.Consume<TimeSyncRequest>()`,
   and only skip the DDS write when `_requestWriter is null`.  This prevents bus buildup when
   running without DDS.
