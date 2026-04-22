# Task Detail: Time Controller Unification

**Reference Design:** [DESIGN.md](./DESIGN.md)

---

## Phase 1: CQRS Message Layer — Foundation

---

### TCU-M001 — Fix Network Wire DTOs

**Design Reference:** [§4.1 CQRS Message Contracts — Network Wire DTOs](./DESIGN.md#41-cqrs-message-contracts)

**Scope**

Convert all network wire DTOs in `FDP.Toolkit.Time.Messages` (`TimeMessages.cs`) from C#
properties (`{ get; set; }`) to plain public fields and add missing fields. Update the corresponding
generated partial structs and any `ToWire`/`ToEvent` mapping helpers.

Out of scope: any controller logic, any translator logic, new domain message types.

**Files touched:**
- `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/obj/Generated/` — regenerated automatically by the DDS codegen
  source generator on next build

**Constraints**
- `[DdsId(N)]` ordinals must be preserved exactly to maintain backwards compatibility with
  existing flight recordings and interop with older cluster nodes.
- `[DdsTopic]` attribute must remain on `TimePulseDescriptor` and `SwitchTimeModeWireDto`.
- `[MessagePackObject]` / `[Key(N)]` must remain on all local structs (`FrameOrderDescriptor`,
  `FrameAckDescriptor`, `SwitchTimeModeEvent`) so MessagePack serialisation (scenario save/load)
  continues to work.
- `SwitchTimeModeWireDto.ToWire()` / `ToEvent()` helpers must be updated to map the new fields.
- The DDS codegen source generator picks up changes automatically; verify the generated
  `obj/Generated/*.g.cs` files regenerate cleanly (`dotnet build`).

**Changes required:**

| Struct | Change |
|---|---|
| `FrameOrderDescriptor` | Properties → plain fields; add `double TargetSimTime` at `[Key(3)]` / `[DdsId(3)]` |
| `FrameAckDescriptor` | Properties → plain fields |
| `TimePulseDescriptor` | Properties → plain fields |
| `SwitchTimeModeWireDto` | Properties → plain fields; add `double SimTimeSnapshot` at `[DdsId(3)]`, `float TimeScale` at `[DdsId(4)]`; update `ToWire`/`ToEvent` |
| `SwitchTimeModeEvent` | Properties → plain fields; add `double SimTimeSnapshot`, `float TimeScale` (already computed by coordinator but missing from struct definition) |

**Success Conditions**

1. `dotnet build FDP/FDP.sln` produces zero errors and zero warnings related to the changed types.
2. All existing tests in `FDP.Toolkit.Time.Tests` pass without modification (`dotnet test`).
3. `SwitchTimeModeWireDto.ToWire(evt).SimTimeSnapshot` returns `evt.SimTimeSnapshot`.
4. `SwitchTimeModeWireDto.ToWire(evt).TimeScale` returns `evt.TimeScale`.
5. Unit test: `SwitchTimeModeWireDto_RoundTrip` — create a `SwitchTimeModeEvent` with all fields
   set to non-zero values; call `ToWire().ToEvent()`; assert every field equals the original.
6. Unit test: `FrameOrderDescriptor_HasTargetSimTime` — construct a `FrameOrderDescriptor` with
   `TargetSimTime = 42.5`; assert the field is readable.

---

### TCU-M002 — Introduce Local Domain Message Types

**Design Reference:** [§4.1 CQRS Message Contracts — Local Domain Messages](./DESIGN.md#41-cqrs-message-contracts)

**Scope**

Add two new plain-field structs to a new file
`FDP/Toolkits/FDP.Toolkit.Time/Domain/TimeLocalEvents.cs`. Register them in the FdpEventBus
test helpers where needed.

Out of scope: publishing or consuming these structs — translators and controllers come in later tasks.

**Files touched:**
- `FDP/Toolkits/FDP.Toolkit.Time/Domain/TimeLocalEvents.cs` (new file)

**Structs to create:**

```csharp
namespace FDP.Toolkit.Time.Domain
{
    public struct AdvanceFrameIntent
    {
        public long   FrameID;
        public float  FixedDelta;
        public double TargetSimTime;   // 0 = use FixedDelta; >0 = snap sim time to this value
    }

    public struct FrameStepCompletedEvent
    {
        public long FrameID;
        public int  NodeID;
    }
}
```

**Constraints**
- No `[DdsTopic]`, `[EventId]`, `[MessagePackObject]`, or any serialisation attributes — these
  types are purely in-process.
- Plain fields only (no properties).

**Success Conditions**

1. `dotnet build` succeeds.
2. Unit test: `AdvanceFrameIntent_CanBePublishedAndConsumed` — create a `FdpEventBus`; register
   and publish an `AdvanceFrameIntent`; swap buffers; call `Consume<AdvanceFrameIntent>()`; assert
   `FrameID`, `FixedDelta`, and `TargetSimTime` match the published values.
3. Unit test: `FrameStepCompletedEvent_CanBePublishedAndConsumed` — same pattern for
   `FrameStepCompletedEvent`; assert `FrameID` and `NodeID` match.

---

## Phase 2: Unified Master Controller

---

### TCU-MC001 — MasterSyncController

**Design Reference:** [§4.2 Unified Master Controller](./DESIGN.md#42-unified-master-controller--mastersynccotroller)

**Scope**

Create `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs`.

This single class subsumes `MasterTimeController`, `SteppedMasterController`, and
`DistributedTimeCoordinator`. It implements `ISteppableTimeController`.

Out of scope: wiring into Orchestrator (Phase 5), deleting old classes (Phase 5).

**Files touched:**
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs` (new file)

**Internal state:**

```csharp
private enum MasterMode { Continuous, BarrierPending, Stepping }
private MasterMode _mode = MasterMode.Continuous;
private long   _pendingBarrierWallTicks = -1;
private readonly HashSet<int> _expectedSlaves;
private HashSet<int> _pendingAcks;       // cleared as FrameStepCompletedEvent arrive
private long   _frameNumber;
private double _totalTime;
private double _unscaledTotalTime;
private float  _timeScale;
private long   _totalWallTicks;
private readonly Stopwatch _wallClock;
private readonly FdpEventBus _eventBus;
private readonly TimeConfig  _config;
// TimePulse rate-limiting
private long _lastPulseTicks;
```

**Public API:**

```csharp
public class MasterSyncController : ISteppableTimeController
{
    // ctor: eventBus, slaveNodeIds (may be empty), config
    public void SwitchToDeterministic(HashSet<int> slaveNodeIds);
    public void SwitchToContinuous(float resumeTimeScale = 0f);
    public GlobalTime Update();
    public GlobalTime Step(float fixedDelta);
    public void SetTimeScale(float scale);
    public float GetTimeScale();
    public TimeMode GetMode();
    public GlobalTime GetCurrentState();
    public void SeedState(GlobalTime state);
    public void Dispose();
}
```

**Behaviour rules:**

- `Update()` in `Continuous`: measure wall-clock delta; accumulate `_totalWallTicks`,
  `_totalTime`, `_unscaledTotalTime`; publish `TimePulseDescriptor` at ~1 Hz (when
  `Stopwatch.GetTimestamp() - _lastPulseTicks > Stopwatch.Frequency`); return `GlobalTime`.
- `Update()` in `BarrierPending`: same as Continuous; additionally check if
  `_totalWallTicks >= _pendingBarrierWallTicks` → if yes transition to `Stepping`, reset
  `_pendingAcks = new HashSet<int>(_expectedSlaves)`.
- `Update()` in `Stepping`: drain `FrameStepCompletedEvent` from bus; remove reporting node IDs
  from `_pendingAcks`; return current `GlobalTime` with `DeltaTime=0`.
- `Step(delta)` callable only in `Stepping` mode; ignored (returns current state) if
  `_pendingAcks` is non-empty (previous step not yet ACK'd by all slaves); otherwise:
  increment `_frameNumber`; accumulate `_totalTime += delta * _timeScale`;
  advance `_totalWallTicks += (long)(delta * Stopwatch.Frequency)`;
  publish `AdvanceFrameIntent { FrameID=_frameNumber, FixedDelta=delta, TargetSimTime=0 }`;
  set `_pendingAcks = new HashSet<int>(_expectedSlaves)`.
- `SwitchToDeterministic`: compute `BarrierWallTicks = _totalWallTicks + _config.LookaheadWallTicks`;
  store as `_pendingBarrierWallTicks`; set `_mode = BarrierPending`; publish
  `SwitchTimeModeEvent { TargetMode=Deterministic, BarrierWallTicks, FixedDelta, TimeScale }`.
- `SwitchToContinuous(scale)`: idempotent (no-op if already Continuous and no pending barrier);
  cancel `_pendingBarrierWallTicks = -1`; capture `SimTimeSnapshot = _totalTime`; transition
  `_mode = Continuous`; publish `SwitchTimeModeEvent { TargetMode=Continuous, BarrierWallTicks=0,
  SimTimeSnapshot, TimeScale }`.

**Constraints**
- Must NOT couple to DDS directly. All DDS traffic goes through bus + translators.
- Bus must be pre-registered for `FrameStepCompletedEvent`, `AdvanceFrameIntent`,
  `SwitchTimeModeEvent`, `TimePulseDescriptor`.
- `GetMode()` returns `TimeMode.Continuous` whenever `_mode` is `Continuous` or `BarrierPending`
  (the pending state is a detail of the barrier protocol, not exposed via the public mode API).
  Returns `TimeMode.Deterministic` when `_mode == Stepping`.

**Success Conditions**

1. `MasterSyncController_ContinuousMode_AdvancesTime` — construct with empty slave set; call
   `Update()` twice with artificial wall-clock ticks (use internal tick-source override or rely on
   real Stopwatch with small sleep); assert `TotalTime > 0` and `FrameNumber == 2`.
2. `MasterSyncController_SwitchToDeterministic_PublishesBarrierEvent` — call
   `SwitchToDeterministic`; swap bus; assert one `SwitchTimeModeEvent` with
   `TargetMode==Deterministic` and `BarrierWallTicks > currentWallTicks`.
3. `MasterSyncController_BarrierPending_TransitionsToStepping` — set a very-near barrier
   (`LookaheadWallTicks = 0`); call `Update()` after the barrier has passed; assert
   `GetMode() == TimeMode.Deterministic`.
4. `MasterSyncController_Step_PublishesAdvanceFrameIntent` — transition to Stepping; call
   `Step(0.016f)`; swap bus; `Consume<AdvanceFrameIntent>()`; assert `FrameID == 1`,
   `FixedDelta ≈ 0.016f`.
5. `MasterSyncController_Step_BlocksUntilAllAcksReceived` — two slaves; call `Step(0.016f)`;
   verify second `Step()` returns same frame (no advance); publish one `FrameStepCompletedEvent`
   for slave A; verify still blocked; publish for slave B; call `Update()`; verify `Step()` now
   advances to frame 2.
6. `MasterSyncController_SwitchToContinuous_PublishesSnapshotEvent` — while in Stepping,
   seed `_totalTime = 42.0`; call `SwitchToContinuous()`; swap bus; assert
   `SwitchTimeModeEvent.TargetMode == Continuous` and `SimTimeSnapshot ≈ 42.0`.
7. `MasterSyncController_SwitchToContinuous_IdempotentWhenAlreadyContinuous` — call
   `SwitchToContinuous()` twice from Continuous mode; assert only zero events published on second
   call.
8. `MasterSyncController_SeedState_RestoresTotalTime` — call `SeedState(new GlobalTime
   { TotalTime=99.0, FrameNumber=500 })`; assert `GetCurrentState().TotalTime ≈ 99.0`.
9. `MasterSyncController_PublishesTimePulse_OncePerSecond` — run ~65 frames each advancing
   ~16 ms (simulated via tick source); assert exactly one `TimePulseDescriptor` published.

---

## Phase 3: Unified Slave Controller

---

### TCU-SC001 — SlaveSyncController

**Design Reference:** [§4.3 Unified Slave Controller](./DESIGN.md#43-unified-slave-controller--slavesynccontroller)
, [§4.6 TimePulse Source — Orchestrator Only](./DESIGN.md#46-timepulse-source--orchestrator-only)

**Scope**

Create `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs`.

This single class subsumes `SlaveTimeController`, `SteppedSlaveController`, and
`SlaveTimeModeListener`. It implements `ITimeController`. It never publishes
`TimePulseDescriptor`.

Out of scope: wiring in application hosts (Phase 5), deleting old classes (Phase 5).

**Files touched:**
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs` (new file)

**Internal state:**

```csharp
private enum SlaveMode { Continuous, BarrierPending, Stepping }
private SlaveMode _mode = SlaveMode.Continuous;
private long   _pendingBarrierWallTicks = -1;
private SwitchTimeModeEvent? _pendingModeSwitch;
private readonly int _localNodeId;
// PLL state (never destroyed across transitions)
private readonly JitterFilter _errorFilter;
private long   _virtualWallTicks;
private long   _lastUpdateRawTicks;
private double _currentError;
// Time state
private double _totalTime;
private double _unscaledTotalTime;
private long   _frameNumber;
private float  _timeScale;
// Stepping state
private readonly Queue<AdvanceFrameIntent> _pendingIntents = new();
private readonly FdpEventBus _eventBus;
private readonly TimeConfig  _config;
private readonly Func<long>? _tickSource;   // test seam
```

**Behaviour rules:**

- Constructor: register `TimePulseDescriptor`, `SwitchTimeModeEvent`, `AdvanceFrameIntent` on bus.
- `Update()` always starts by draining `SwitchTimeModeEvent` to update `_pendingModeSwitch` or
  apply an immediate Continuous transition.
- Then checks `_mode == BarrierPending` and whether barrier crossed → transitions to `Stepping`.
- In `Continuous`: compute raw delta from tick source; apply PLL slew from latest
  `TimePulseDescriptor`; accumulate time; return `GlobalTime`.
- In `BarrierPending`: same as Continuous (PLL keeps running), then barrier check.
- In `Stepping`: drain `AdvanceFrameIntent` queue; for each intent advance `_totalTime` (snap to
  `TargetSimTime` if non-zero, else `+= FixedDelta * _timeScale`); increment `_frameNumber`;
  publish `FrameStepCompletedEvent { FrameID, NodeID=_localNodeId }`; advance
  `_virtualWallTicks += (long)(FixedDelta * Stopwatch.Frequency)`. If no intents: return current
  state with `DeltaTime=0`.
- Transition to `Continuous` via incoming `SwitchTimeModeEvent(Continuous)`:
  - Apply `SimTimeSnapshot` if `> 0` (snap `_totalTime` to prevent UI jump-back).
  - Apply `TimeScale` if carried.
  - Set `_mode = Continuous`. PLL state unchanged — warm restart.
- Transition to `Stepping` via barrier: set `_mode = Stepping`; clear `_pendingIntents`.

**Constraints**
- Must NOT publish `TimePulseDescriptor` under any condition or in any mode.
- PLL (`JitterFilter`, `_virtualWallTicks`, `_currentError`) must survive all mode transitions.
- `GetMode()` returns `Continuous` when `_mode` is `Continuous` or `BarrierPending`;
  returns `Deterministic` when `Stepping`.

**Success Conditions**

1. `SlaveSyncController_ContinuousMode_PLLTracksTimePulse` — publish a `TimePulseDescriptor`
   with `MasterWallTicks = N`; advance local ticks by 100 ms; call `Update()`;
   assert `TotalTime` has advanced (non-zero).
2. `SlaveSyncController_NoTimePulseEmitted` — run 200 frames; swap bus each frame; collect all
   published events; assert `zero` `TimePulseDescriptor` events on the bus at any point.
3. `SlaveSyncController_BarrierPending_PLLContinuesDuringWait` — send
   `SwitchTimeModeEvent(Deterministic, BarrierWallTicks=veryFar)`;  advance several frames;
   assert `GetMode() == Continuous` (barrier not yet crossed) and `TotalTime` is still advancing.
4. `SlaveSyncController_TransitionsToStepping_WhenBarrierCrossed` — set
   `BarrierWallTicks = currentVirtualWallTicks`; call `Update()`; assert
   `GetMode() == Deterministic`.
5. `SlaveSyncController_Stepping_AdvancesOnAdvanceFrameIntent` — transition to Stepping;
   publish `AdvanceFrameIntent { FrameID=1, FixedDelta=0.016f }`; swap; call `Update()`;
   assert `FrameNumber==1`, `DeltaTime≈0.016f`, `TotalTime≈0.016f`.
6. `SlaveSyncController_Stepping_WaitsWithDeltaZeroWhenNoIntent` — in Stepping, call `Update()`
   without any intent; assert `DeltaTime==0` and `FrameNumber` unchanged.
7. `SlaveSyncController_Stepping_PublishesFrameStepCompletedEvent` — advance one intent;
   swap; drain `FrameStepCompletedEvent`; assert `FrameID==1` and `NodeID==_localNodeId`.
8. `SlaveSyncController_Resume_SnapsToMasterSimTime` — in Stepping with `_totalTime=3.0`;
   send `SwitchTimeModeEvent(Continuous, SimTimeSnapshot=4.5)`; call `Update()`; assert
   `GetCurrentState().TotalTime ≈ 4.5`.
9. `SlaveSyncController_Resume_PLLIsWarm_NoJitterReset` — run 50 Continuous frames to warm PLL;
   transition to Stepping; transition back to Continuous; assert PLL error is still near zero
   (not cold-started) by checking that the first Continuous `Update()` post-resume has a
   `DeltaTime` within ±5% of the pre-pause delta.
10. `SlaveSyncController_Stepping_SnapsToTargetSimTime_WhenProvided` — publish
    `AdvanceFrameIntent { FrameID=5, FixedDelta=0.016f, TargetSimTime=10.0 }`; advance once;
    assert `TotalTime ≈ 10.0` (not `prevTime + 0.016f`).

---

## Phase 4: Role-Split Lockstep Translators

---

### TCU-TR001 — MasterLockstepTranslator

**Design Reference:** [§4.4 Role-Split Lockstep Translators](./DESIGN.md#44-role-split-lockstep-translators)

**Scope**

Create `FDP/Toolkits/FDP.Toolkit.Time/Translators/MasterLockstepTranslator.cs`.

Wires only the master-side DDS resources:
- Egress: `AdvanceFrameIntent` → `DdsWriter<FrameOrderDescriptor>`
- Ingress: `DdsReader<FrameAckDescriptor>` → `FrameStepCompletedEvent`

Must NOT create `DdsReader<FrameOrderDescriptor>` or `DdsWriter<FrameAckDescriptor>`.

Out of scope: wiring into Orchestrator (Phase 5).

**Constraints**
- No echo-prevention tracking state (`_lastSentOrderFrameId` etc.).
- `participant == null` → both sides are safe no-ops (test environments).
- Implement `IDescriptorTranslator`; `TopicName = "FrameOrder"`; `DescriptorOrdinal = 202`.

**Success Conditions**

1. `MasterLockstepTranslator_NullParticipant_DoesNotThrow` — construct with `null`; call
   `ScanAndPublish` and `PollIngress`; assert no exception.
2. `MasterLockstepTranslator_Egress_PublishesFrameOrderFromAdvanceFrameIntent` — use null-DDS
   construction; publish `AdvanceFrameIntent { FrameID=7, FixedDelta=0.016f }` to bus; swap;
   call `ScanAndPublish`; swap; assert no stray events remain on bus (event was drained). *(Full
   DDS round-trip verified in integration tests — TCU-T003.)*
3. `MasterLockstepTranslator_Ingress_PublishesFrameStepCompletedEvent` — with null DDS the
   ingress is a no-op; test documents this contract.

---

### TCU-TR002 — SlaveLockstepTranslator

**Design Reference:** [§4.4 Role-Split Lockstep Translators](./DESIGN.md#44-role-split-lockstep-translators)

**Scope**

Create `FDP/Toolkits/FDP.Toolkit.Time/Translators/SlaveLockstepTranslator.cs`.

Wires only the slave-side DDS resources:
- Ingress: `DdsReader<FrameOrderDescriptor>` → `AdvanceFrameIntent`
- Egress: `FrameStepCompletedEvent` → `DdsWriter<FrameAckDescriptor>`

Must NOT create `DdsWriter<FrameOrderDescriptor>` or `DdsReader<FrameAckDescriptor>`.

Out of scope: wiring into SimHost, IG, CGF (Phase 5).

**Constraints**
- No echo-prevention tracking state.
- `participant == null` → both sides are safe no-ops.
- Implement `IDescriptorTranslator`; `TopicName = "FrameOrder"`; `DescriptorOrdinal = 203`
  (distinct ordinal from master translator to avoid collision if both are in the same list).
- `_localNodeId` passed at construction; used when creating `FrameAckDescriptor.NodeID`.

**Success Conditions**

1. `SlaveLockstepTranslator_NullParticipant_DoesNotThrow`.
2. `SlaveLockstepTranslator_Ingress_PublishesAdvanceFrameIntent` — with null DDS; no-op test.
3. `SlaveLockstepTranslator_Egress_DrainFrameStepCompletedEvent` — publish
   `FrameStepCompletedEvent { FrameID=3, NodeID=10 }` to bus; call `ScanAndPublish`; swap;
   assert `FrameStepCompletedEvent` was drained from bus.

---

### TCU-TR003 — Update TimeNetworkModule Factory Methods

**Design Reference:** [§4.4 Role-Split Lockstep Translators](./DESIGN.md#44-role-split-lockstep-translators)

**Scope**

Update `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs`:

1. Add `CreateMasterLockstepTranslator(participant, eventBus)` → returns `MasterLockstepTranslator`.
2. Add `CreateSlaveLockstepTranslator(participant, eventBus, localNodeId)` → returns
   `SlaveLockstepTranslator`.
3. Mark existing `CreateLockstepTranslator(participant, eventBus, localNodeId)` as `[Obsolete]`
   with migration message pointing to the role-specific methods.

Out of scope: removing the old method or updating call sites (Phase 5).

**Constraints**
- Existing `CreateLockstepTranslator` must remain so that current application code compiles
  without error until Phase 5 migrates call sites.

**Success Conditions**

1. `dotnet build` succeeds with no errors.
2. `TimeNetworkModule_CreateMasterLockstepTranslator_ReturnsMasterType` — assert returned
   object is `MasterLockstepTranslator`.
3. `TimeNetworkModule_CreateSlaveLockstepTranslator_ReturnsSlaveLockstepType`.

---

## Phase 5: Application Wiring

---

### TCU-W001 — Wire MasterSyncController in Orchestrator

**Design Reference:** [§3 Cluster Role Topology](./DESIGN.md#3-cluster-role-topology),
[§4.2](./DESIGN.md#42-unified-master-controller--mastersynccotroller)

**Scope**

Update `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs`:

1. Replace `MasterTimeController` + `SteppedMasterController` + `DistributedTimeCoordinator`
   fields/construction with a single `MasterSyncController`.
2. Remove `_timeCoordinator` field; replace calls to `_timeCoordinator.SwitchToDeterministic` /
   `SwitchToContinuous` with `_masterSync.SwitchToDeterministic` / `SwitchToContinuous`.
3. Remove `_timeModeTranslator` (handled by new translator composed below).
4. Wire `TimePulseEgressTranslator` via `TimeNetworkModule`; add to the update loop.

**Constraints**
- `OrchestratorSubsystem.Update()` must still call `_masterSync.Update()` then
  `_eventBus.SwapBuffers()` each frame in the same order.
- The slave-ID set passed to `SwitchToDeterministic` is still gathered from
  `_clusterMaster.NodeRoster.ActiveNodes.Keys` at the time of the command.

**Success Conditions**

1. Orchestrator builds without errors (`dotnet build`).
2. Integration test: `OrchestratorSubsystem_PausePublishesSwitchTimeModeEvent` — init subsystem
   with null DDS; trigger `PauseTime` op; assert `SwitchTimeModeEvent(Deterministic)` on bus.
3. Integration test: `OrchestratorSubsystem_ResumePublishesContinuousEvent`.

---

### TCU-W002 — Wire SlaveSyncController in SimHost

**Design Reference:** [§4.3](./DESIGN.md#43-unified-slave-controller--slavesynccontroller),
[§4.6 TimePulse Source — Orchestrator Only](./DESIGN.md#46-timepulse-source--orchestrator-only)

**Scope**

Update `Hrot.SimHost/SimHostApp.cs`:

1. Replace `SlaveTimeController` construction with `SlaveSyncController`.
2. Remove `SlaveTimeModeListener` field and its `Update()` call.
3. Remove `continuousControllerFactory` lambda (no longer needed).
4. Remove `TimePulseEgressTranslator` from `egressTranslators` list (Orchestrator is the sole
   pulse source).
5. Replace `CreateLockstepTranslator(...)` call with `CreateSlaveLockstepTranslator(...)`.

**Constraints**
- `SlaveSyncController` must be constructed with the same `_eventBus` that the existing network
  module uses.
- `_slaveTimeModeListener?.Update()` call site must be removed; the unified controller handles
  the barrier internally on its own `Update()`.

**Success Conditions**

1. SimHost builds without errors.
2. Existing SimHost integration tests pass.
3. No `TimePulseDescriptor` appears on the SimHost event bus in any test (verify by asserting
   zero `TimePulseDescriptor` published in a 100-frame run test).

---

### TCU-W003 — Wire SlaveSyncController in CGF

**Design Reference:** [§4.3](./DESIGN.md#43-unified-slave-controller--slavesynccontroller)

**Scope**

Update `Hrot.CGF/CgfApplication.cs`:

1. Replace `SlaveTimeController` construction with `SlaveSyncController`.
2. Remove `SlaveTimeModeListener` field and its `Update()` call.
3. Replace `CreateLockstepTranslator(...)` with `CreateSlaveLockstepTranslator(...)`.

**Constraints**
- Remove `_slaveTimeModeListener` field and all references.

**Success Conditions**

1. CGF builds without errors.
2. `CgfApplication_Tick_DoesNotThrow` — construct `CgfApplication` with null domain participant
   (test constructor path); call `Tick()` 10 times; assert no exception.

---

### TCU-W004 — Wire SlaveSyncController in IG

**Design Reference:** [§4.3](./DESIGN.md#43-unified-slave-controller--slavesynccontroller)

**Scope**

Update `Hrot.IG/IgApplication.cs`:

1. Find the `SlaveTimeController` (or existing time controller) construction.
2. Replace with `SlaveSyncController`.
3. Replace `CreateLockstepTranslator(...)` with `CreateSlaveLockstepTranslator(...)`.
4. Remove any `SlaveTimeModeListener` usage.

**Success Conditions**

1. IG builds without errors.
2. Existing IG integration tests pass.

---

### TCU-W005 — Update TimeControllerFactory

**Design Reference:** [§7 Implementation Phases](./DESIGN.md#7-implementation-phases)

**Scope**

Update `FDP/Toolkits/FDP.Toolkit.Time/Controllers/TimeControllerFactory.cs`:

1. `TimeRole.Master` + `TimeMode.Continuous` → returns `MasterSyncController`.
2. `TimeRole.Slave` + either mode → returns `SlaveSyncController`.
3. `TimeRole.Standalone` → unchanged (returns `MasterTimeController` driving a private bus,
   no DDS, unchanged behaviour for single-node tools).

**Constraints**
- The Standalone path must remain unchanged because single-node tools and many unit tests rely on
  it and have no cluster semantics.

**Success Conditions**

1. `TimeControllerFactory_Master_Continuous_ReturnsMasterSyncController`.
2. `TimeControllerFactory_Slave_Continuous_ReturnsSlaveSyncController`.
3. `TimeControllerFactory_Slave_Deterministic_ReturnsSlaveSyncController`.
4. `TimeControllerFactory_Standalone_ReturnsUnchangedType`.

---

### TCU-W006 — Delete Obsolete Classes

**Design Reference:** [§6 What Gets Deleted](./DESIGN.md#6-what-gets-deleted)

**Scope**

Delete the following files:

- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterTimeController.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SteppedMasterController.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SteppedSlaveController.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveTimeController.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SwitchableTimeController.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/DistributedTimeCoordinator.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveTimeModeListener.cs`
- `FDP/Toolkits/FDP.Toolkit.Time/FrameLockstepDescriptorTranslator.cs`

Also mark `TimeNetworkModule.CreateLockstepTranslator` as deleted (remove the obsolete method
after all call-sites have been migrated in phases TCU-W001 through TCU-W004).

**Constraints**
- Must only be done after ALL of TCU-W001 through TCU-W005 are complete and `dotnet build` is
  error-free.
- Do NOT delete `SteppingTimeController` — it is still used.

**Success Conditions**

1. `dotnet build FDP/FDP.sln` produces zero errors.
2. `dotnet build IOS-IG-SimHost.sln` produces zero errors.
3. `grep -r "SlaveTimeController\|SteppedMasterController\|DistributedTimeCoordinator\|SlaveTimeModeListener\|FrameLockstepDescriptorTranslator" --include="*.cs"` returns no matches
   outside of test files that were explicitly deleted or migrated.

---

## Phase 6: Test Coverage

---

### TCU-T001 — Unit Tests: MasterSyncController

**Design Reference:** [§4.2](./DESIGN.md#42-unified-master-controller--mastersynccotroller)

**Scope**

Create `FDP/Toolkits/FDP.Toolkit.Time.Tests/MasterSyncControllerTests.cs` covering all nine
success conditions listed under TCU-MC001 plus any additional edge cases discovered during
implementation.

**Additional edge-case tests:**
- Step while in Continuous mode is a no-op.
- ACK from an unrecognised node ID is silently ignored (does not unblock the next step).
- Transition Continuous → BarrierPending → Stepping → Continuous → Stepping works correctly for a
  second pause cycle.

---

### TCU-T002 — Unit Tests: SlaveSyncController

**Design Reference:** [§4.3](./DESIGN.md#43-unified-slave-controller--slavesynccontroller)

**Scope**

Create `FDP/Toolkits/FDP.Toolkit.Time.Tests/SlaveSyncControllerTests.cs` covering all ten success
conditions listed under TCU-SC001 plus:

- Two consecutive pause/resume cycles without any PLL re-initialisation.
- Out-of-order `AdvanceFrameIntent` (frame ID less-than current): must be ignored (with a log
  warning); controller must not advance to the old frame ID.

---

### TCU-T003 — Unit Tests: Lockstep Translators

**Scope**

Create `FDP/Toolkits/FDP.Toolkit.Time.Tests/LockstepTranslatorTests.cs` covering:

- All success conditions for TCU-TR001 and TCU-TR002.
- `MasterLockstepTranslator_TopicName_IsFrameOrder`.
- `SlaveLockstepTranslator_DescriptorOrdinal_Is203` (different from master's 202).

---

### TCU-T004 — Unit Tests: TimeControllerFactory (updated)

**Scope**

Update `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeControllerFactoryTests.cs` adding the four
success conditions from TCU-W005. The existing tests for Standalone mode must continue passing.

---

### TCU-T005 — Unit Tests: DTO Round-Trip and Domain Events

**Scope**

Create `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeMessagesTests.cs` covering:

- All success conditions for TCU-M001 and TCU-M002.
- `SwitchTimeModeWireDto_ToWire_PreservesAllFields` (including new `SimTimeSnapshot`, `TimeScale`).
- `FrameOrderDescriptor_PlainFields_NoCsharpProperties` (reflection-based check: assert that
  public members are fields, not properties).

---

### TCU-T006 — Integration Test: Full Pause/Step/Resume Cycle (In-Process)

**Design Reference:** [§5 Distributed Flow Diagrams](./DESIGN.md#5-distributed-flow-diagrams)

**Scope**

Add to `FDP/Toolkits/FDP.Toolkit.Time.Tests/DistributedPauseTests.cs` (or a new
`UnifiedControllerE2ETests.cs`):

An in-process end-to-end test wiring one `MasterSyncController` and two `SlaveSyncController`
instances on **shared `FdpEventBus`** instances bridged by in-process implementations of
`MasterLockstepTranslator` and `SlaveLockstepTranslator` (null DDS participants, relaying via
bus-to-bus calls).

Test scenario: `FullCycle_Pause_Step_Resume_NoPllLoss`

Setup:
- One master bus, two slave buses.
- Bridge: master egress `AdvanceFrameIntent` → both slave buses (manual copy for in-process);
  slave egress `FrameStepCompletedEvent` → master bus.

Steps:
1. Run 20 Continuous frames; record `slave.TotalTime` after frame 20.
2. `master.SwitchToDeterministic()` with both slave IDs.
3. Drive frames until both slaves' `_virtualWallTicks >= barrier`; assert both transition to
   Stepping.
4. `master.Step(0.016f)` × 5; relay `AdvanceFrameIntent` to slaves each time; relay
   `FrameStepCompletedEvent` back to master; assert master's `TotalTime` advances by
   `5 × 0.016f`.
5. `master.SwitchToContinuous()`; relay event to slaves.
6. Run 20 more Continuous frames.

Assertions:
- `slave.GetMode() == Continuous` after step 6.
- `slave.TotalTime` is within 5% of `master.TotalTime` after step 6 (PLL re-converges).
- No `TimePulseDescriptor` was ever published by either slave (assert zero).
- Slave `TotalTime` after resume ≈ `master.SimTimeSnapshot` from the resume event (not the stale
  pre-pause slave value).
