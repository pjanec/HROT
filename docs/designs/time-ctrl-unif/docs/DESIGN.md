<!--STATUS
state: HISTORICAL
updated: 2026-08-21
current-answer: §4 is the IMPLEMENTED intent behind MasterSyncController / SlaveSyncController and
  the role-split lockstep translators. It is WHY the code is shaped as it is.
stale-below: the header's "Status: Planning" is stale — this shipped.
known-rot: none found. §4.4's roster "SlaveLockstepTranslator (SimHost, IG, CGF)" was measured
  UNMET for CGF on 2026-08-21 and restored by TM-002 — the DESIGN was right, the wiring had
  regressed in the CgfApplication -> CgfSubsystem migration.
superseded-by: nothing wholesale. For the CURRENT target shape of the time APIs see
  ../../../blueprints/DESIGN_Time_Architecture.md §9/§9a; this document remains the authority on
  WHY the master/slave split and the barrier protocol exist.
known-conflict: none.
-->
# Design: Time Controller Unification

**Workstream:** `time-ctrl-unif`
**Status:** Planning

---

## Table of Contents

1. [Background & Motivation](#1-background--motivation)
2. [Current Architecture — Problems](#2-current-architecture--problems)
3. [Cluster Role Topology](#3-cluster-role-topology)
4. [Target Architecture](#4-target-architecture)
   - 4.1 [CQRS Message Contracts](#41-cqrs-message-contracts)
   - 4.2 [Unified Master Controller — MasterSyncController](#42-unified-master-controller--mastersynccotroller)
   - 4.3 [Unified Slave Controller — SlaveSyncController](#43-unified-slave-controller--slavesynccontroller)
   - 4.4 [Role-Split Lockstep Translators](#44-role-split-lockstep-translators)
   - 4.5 [Future Barrier Preserved](#45-future-barrier-preserved)
   - 4.6 [TimePulse Relay — SimHost → IG/CGF](#46-timepulse-relay--simhost--igcgf)
5. [Distributed Flow Diagrams](#5-distributed-flow-diagrams)
6. [What Gets Deleted](#6-what-gets-deleted)
7. [Implementation Phases](#7-implementation-phases)

---

## 1. Background & Motivation

`FDP.Toolkit.Time` provides distributed time synchronisation for the multi-node simulation cluster.
The current architecture separates continuous and deterministic (lockstep) operation into distinct
controller classes that are hot-swapped at runtime when the Orchestrator issues a pause/resume
command.

While individually correct, the hot-swap design has accumulated significant brittleness over time.
Bug fixes for the pause/resume glitches were patched in without addressing the root cause, leaving
the code in a state where the next maintenance developer will struggle to understand what the correct
steady-state behaviour is.

This workstream replaces the hot-swap strategy with unified, self-contained state-machine
controllers that handle both modes internally, and introduces a proper CQRS message split that
structurally eliminates the echo-prevention hacks currently needed in the lockstep translator.

---

## 2. Current Architecture — Problems

### 2.1 Controller Hot-Swapping (State Tearing)

When the cluster pauses, `DistributedTimeCoordinator` calls `SwitchableTimeController.SwitchTo()`
to physically replace `MasterTimeController` with `SteppedMasterController`. On every slave
`SlaveTimeModeListener.ExecuteSwapToDeterministic()` replaces `SlaveTimeController` with
`SteppedSlaveController`.

This causes **state tearing**:

| Object destroyed | Value lost |
|---|---|
| `SlaveTimeController` | `JitterFilter` (PLL warm-up state) |
| `MasterTimeController` | Accumulated `_totalWallTicks` reference |

Consequences observed in production:
- PLL cold-restart on every Resume → clock slews sharply on the first few frames after unpausing.
- Time "jump backward" on Resume because the new slave controller seeds from its own paused time
  rather than from the master's authoritative sim time. Fixed with a `SimTimeSnapshot` field
  embedded in `SwitchTimeModeEvent` and special-cased `SeedState()` calls — accidental complexity.
- `SimHost`'s `SlaveTimeModeListener` requires a `continuousControllerFactory` lambda to produce a
  `MasterTimeController` on resume (SimHost is a slave of the Orchestrator's commands but must
  still publish `TimePulse` to IG). This factory parameter exists solely because of the hot-swap.

### 2.2 Echo-Prevention Hacks in FrameLockstepDescriptorTranslator

`FrameLockstepDescriptorTranslator` is symmetric: it wires both `FrameOrder` ingress/egress and
`FrameAck` ingress/egress **on every node**. On the master node this means the master's own
outgoing `FrameOrder` loops back via DDS and is re-read by the master's ingress side, which then
publishes it back to the local bus. Conversely, when a slave's `FrameAck` arrives at the master,
the master's egress accidentally re-broadcasts it.

To prevent infinite echo storms the translator carries two stateful tracking variables:

```csharp
private long _lastSentOrderFrameId = -1;  // echo prevention
private long _lastSentAckFrameId   = -1;  // echo prevention
```

These hacks break the principle that infrastructure adapters should be **stateless pipes**. They
are also fragile — any ordering or buffering change in the DDS reader can reintroduce echoes.

### 2.3 CQRS Semantic Confusion — SwitchTimeModeEvent

`SwitchTimeModeEvent` is named as a domain "event" (past tense — something that has already
happened) but is used as a **command** (future intent — "please switch at wall-tick X"). This
confusion is visible in `DistributedTimeCoordinator.HandleModeSwitch`, which must distinguish
between "this is a local application request" (`BarrierWallTicks == 0`) and "this is a relayed
event with a definite barrier" (`BarrierWallTicks > 0`).

### 2.4 Missing Fields on DTOs

`SwitchTimeModeEvent` is missing `SimTimeSnapshot` and `TimeScale` in the message definition even
though `DistributedTimeCoordinator.SwitchToContinuous` sets them. `FrameOrderDescriptor` is
missing `TargetSimTime` even though `SteppedMasterController` needs to broadcast the authoritative
sim-time on resume to prevent drift.

Network DTOs use C# properties (`{ get; set; }`) rather than plain fields, which wastes IL overhead
and can interfere with raw-memory serialisation assumptions in the FDP blitting layer.

---

## 3. Cluster Role Topology

```
┌─────────────────────────────┐
│       Orchestrator          │  ◄── ONLY time master and TimePulse source
│   MasterSyncController      │      Issues: SwitchTimeModeEvent, FrameOrder, TimePulse
│   MasterLockstepTranslator  │      Receives: FrameAck
└──────────────┬──────────────┘
               │  CycloneDDS  (TimePulse, SwitchTimeModeEvent, FrameOrder/Ack)
    ┌──────────┼──────────────────────────────────┐
    │          │                                  │
    ▼          ▼                                  ▼
┌───────┐  ┌─────────┐                       ┌───────┐
│  IG   │  │ SimHost │                       │ CGF   │
│ Slave │  │ Slave   │                       │ Slave │
└───────┘  └─────────┘                       └───────┘
   SlaveSyncController (PLL)
   SlaveLockstepTranslator
```

**Key rule:** The Orchestrator is the sole time master and the **only publisher of
`TimePulseDescriptor`**. IG, SimHost, and CGF are pure time slaves — they PLL-synchronise
directly to the Orchestrator's pulses and never emit pulses of their own.

---

## 4. Target Architecture

### 4.1 CQRS Message Contracts

Two message domains are strictly separated.

#### Network Wire DTOs — CycloneDDS only

These types cross the network. They are defined in `FDP.Toolkit.Time.Messages` and use **plain
fields** (no properties). Their `[DdsId]` attributes are preserved for backwards compatibility with
existing flight recordings.

| Struct | DDS Topic | Direction | Fields to add/fix |
|---|---|---|---|
| `FrameOrderDescriptor` | `FrameOrder` | Master → Slaves | Add `TargetSimTime` (double); convert to plain fields |
| `FrameAckDescriptor` | `FrameAck` | Slave → Master | Convert to plain fields |
| `TimePulseDescriptor` | `TimePulse` | SimHost → IG/CGF | Convert to plain fields |
| `SwitchTimeModeWireDto` | `SwitchTimeModeEvent` | Master → Slaves | Add `SimTimeSnapshot`, `TimeScale`; convert to plain fields |

#### Local Domain Messages — FdpEventBus only

These types never leave the process. They express CQRS intent (commands) and results (events).
Defined in `FDP.Toolkit.Time.Domain` namespace, plain fields, no DDS attributes.

| Struct | Bus role | Published by | Consumed by |
|---|---|---|---|
| `AdvanceFrameIntent` | Command | `MasterSyncController.Step()` → `MasterLockstepTranslator` (egress of wire DTO back to domain) | `SlaveSyncController` |
| `FrameStepCompletedEvent` | Result | `SlaveSyncController` → `SlaveLockstepTranslator` (egress of wire DTO back to domain) | `MasterSyncController` |
| `SwitchTimeModeEvent` | Command | `MasterSyncController.SwitchToDeterministic/Continuous` → `SwitchTimeModeDescriptorTranslator` (egress) | `SlaveSyncController` (via `SwitchTimeModeDescriptorTranslator` ingress) |

> **Note:** `SwitchTimeModeEvent` retains its existing name (it is already registered in tests and
> app code). It is treated as a **command** semantically in this design. A future rename to
> `ScheduleTimeModeIntent` is desirable but out of scope to avoid excessive churn.

**Domain message field definitions:**

```csharp
// FDP.Toolkit.Time.Domain — local bus only, no DDS attributes

public struct AdvanceFrameIntent
{
    public long   FrameID;
    public float  FixedDelta;
    public double TargetSimTime;   // 0 = use accumulated delta; >0 = snap to this value
}

public struct FrameStepCompletedEvent
{
    public long FrameID;
    public int  NodeID;
}
```

---

### 4.2 Unified Master Controller — MasterSyncController

Replaces: `MasterTimeController` + `SteppedMasterController` + `DistributedTimeCoordinator`

**Responsibility:** Maintain the master node's authoritative `GlobalTime` singleton across all
operating modes without destroying or recreating any internal state.

**Internal state machine:**

```
Continuous ──SwitchToDeterministic()──► BarrierPending ──barrier crossed──► Stepping
Stepping   ──SwitchToContinuous()──────────────────────────────────────────► Continuous
BarrierPending ──SwitchToContinuous()──────────────────────────────────────► Continuous
```

**Behaviour per mode:**

| Mode | Update() behaviour | Step() behaviour |
|---|---|---|
| `Continuous` | Advances wall clock, publishes `TimePulseDescriptor` ~1 Hz | No-op |
| `BarrierPending` | Advances wall clock; transitions to `Stepping` when `TotalWallTicks ≥ BarrierWallTicks` | Buffered; executes after transition |
| `Stepping` | Returns `DeltaTime=0` until Step() is called; processes incoming `FrameStepCompletedEvent` to clear pending ACKs | Publishes `AdvanceFrameIntent`; blocks next call until all ACKs received |

**Public API:**

```csharp
public class MasterSyncController : ISteppableTimeController
{
    // Initiates a cluster-wide pause with a future barrier.
    // Computes BarrierWallTicks = currentWallTicks + LookaheadWallTicks.
    // Publishes SwitchTimeModeEvent(Deterministic) to bus.
    public void SwitchToDeterministic(HashSet<int> slaveNodeIds);

    // Initiates immediate cluster-wide resume.
    // Publishes SwitchTimeModeEvent(Continuous, SimTimeSnapshot=currentTotalTime) to bus.
    public void SwitchToContinuous(float resumeTimeScale = 0f);

    // Issues one deterministic step (only valid in Stepping mode).
    // Publishes AdvanceFrameIntent. Blocks next Step() until all ACKs received.
    public GlobalTime Step(float fixedDelta);
}
```

**ACK tracking:** `MasterSyncController` holds `HashSet<int> _pendingAcks` and `HashSet<int>
_expectedAcks`. It consumes `FrameStepCompletedEvent` from the bus to remove node IDs from
`_pendingAcks`. A step is accepted only when `_pendingAcks` is empty. The set of expected slaves
is passed once at construction and never changes (unlike `DistributedTimeCoordinator` which
accepted the slave IDs dynamically per `SwitchToDeterministic` call — a source of bugs).

---

### 4.3 Unified Slave Controller — SlaveSyncController

Replaces: `SlaveTimeController` + `SteppedSlaveController` + `SlaveTimeModeListener`
Also removes: `continuousControllerFactory` lambda in `SimHostApp`

**Responsibility:** Keep the slave node's `GlobalTime` synchronised to the master clock,
smoothly and without PLL loss across mode transitions.

**Internal state machine:**

```
Continuous ──ScheduleTimeModeIntent(Deterministic)──► BarrierPending ──barrier crossed──► Stepping
Stepping   ──ScheduleTimeModeIntent(Continuous)───────────────────────────────────────────► Continuous
BarrierPending ──ScheduleTimeModeIntent(Continuous)───────────────────────────────────────► Continuous
```

**Behaviour per mode:**

| Mode | Update() behaviour |
|---|---|
| `Continuous` | PLL slews virtual wall clock from `TimePulseDescriptor`; if `emitTimePulse=true` publishes `TimePulseDescriptor` periodically (used by SimHost to feed IG/CGF) |
| `BarrierPending` | Continues PLL; transitions to `Stepping` when own `TotalWallTicks ≥ BarrierWallTicks` |
| `Stepping` | Returns `DeltaTime=0` until `AdvanceFrameIntent` arrives; on receipt: advances time by `FixedDelta` (or snaps to `TargetSimTime` when non-zero), increments `FrameNumber`, publishes `FrameStepCompletedEvent` |

**PLL continuity guarantee:** The `JitterFilter` and `_virtualWallTicks` accumulator are **never
reset** across transitions. When transitioning from Stepping back to Continuous the controller
continues with the same PLL state. The only time values change is when `SimTimeSnapshot > 0` in
the incoming `SwitchTimeModeEvent(Continuous)`, in which case `_totalTime` is snapped to the
authoritative master value to prevent UI jump-back.

**No TimePulse emission on slaves.** `SlaveSyncController` never publishes `TimePulseDescriptor`.
All PLL inputs for IG, SimHost, and CGF come exclusively from the Orchestrator via DDS. This
eliminates the `continuousControllerFactory` → `MasterTimeController` workaround that SimHost
currently uses to keep publishing pulses after a resume.

---

### 4.4 Role-Split Lockstep Translators

Replaces: `FrameLockstepDescriptorTranslator` (symmetric, echo-prone)

Two strictly asymmetric translators. Each has access to exactly the DDS topics it needs for its
role, making echo structurally impossible — no tracking state required.

#### MasterLockstepTranslator (Orchestrator only)

```
FdpEventBus ──AdvanceFrameIntent──► [Egress] ──FrameOrderDescriptor──► DDS FrameOrder topic
DDS FrameAck topic ──FrameAckDescriptor──► [Ingress] ──FrameStepCompletedEvent──► FdpEventBus
```

- DDS resources created: `DdsWriter<FrameOrderDescriptor>`, `DdsReader<FrameAckDescriptor>`
- DDS resources NOT created: `DdsReader<FrameOrderDescriptor>`, `DdsWriter<FrameAckDescriptor>`
- No `_lastSentOrderFrameId` or any tracking state

#### SlaveLockstepTranslator (SimHost, IG, CGF)

```
DDS FrameOrder topic ──FrameOrderDescriptor──► [Ingress] ──AdvanceFrameIntent──► FdpEventBus
FdpEventBus ──FrameStepCompletedEvent──► [Egress] ──FrameAckDescriptor──► DDS FrameAck topic
```

- DDS resources created: `DdsReader<FrameOrderDescriptor>`, `DdsWriter<FrameAckDescriptor>`
- DDS resources NOT created: `DdsWriter<FrameOrderDescriptor>`, `DdsReader<FrameAckDescriptor>`
- No tracking state

The `_localNodeId` used in the ACK is passed at construction. The ACK does not need additional
node-ID filtering because the translator is physically incapable of reading back its own ACKs.

---

### 4.5 Future Barrier Preserved

The future barrier mechanism (BarrierWallTicks = currentWallTicks + LookaheadWallTicks) is
**retained** — it is essential for avoiding time tearing in a distributed cluster with variable
network latency. The change is that the barrier logic is internalized into both `MasterSyncController`
and `SlaveSyncController` rather than living in the separate `DistributedTimeCoordinator` and
`SlaveTimeModeListener` classes.

The lookahead is configured via `TimeConfig.LookaheadWallTicks` (unchanged, defaults to 200 ms).

---

### 4.6 TimePulse Source — Orchestrator Only

The Orchestrator's `MasterSyncController` is the **sole publisher of `TimePulseDescriptor`**.
IG, SimHost, and CGF all PLL-synchronise directly to the Orchestrator's pulses over DDS.

The current arrangement where SimHost re-publishes `TimePulseDescriptor` (via a
`continuousControllerFactory` that creates a `MasterTimeController` on resume) is **removed**.
SimHost's `SlaveTimeModeListener` and the `continuousControllerFactory` parameter are deleted
as part of the migration to `SlaveSyncController`.

The `TimePulseEgressTranslator` already wired in the Orchestrator (`OrchestratorSubsystem`) is
retained as-is. The equivalent translator in `SimHostApp.cs` is **removed**.

---

## 5. Distributed Flow Diagrams

### Pause Flow

```
Orchestrator                    CycloneDDS                 SlaveNode (SimHost / IG / CGF)
     │                              │                              │
     │ masterSync.SwitchToDeterministic()                         │
     │ ► compute BarrierWT = wallTicks + lookahead                │
     │ ► publish SwitchTimeModeEvent(Det, BarrierWT) to bus       │
     │ ► store _pendingBarrierWT                                  │
     │                              │                              │
     │──SwitchTimeModeWireDto──────►│──SwitchTimeModeWireDto─────►│
     │                              │                              │ ingress → publish SwitchTimeModeEvent
     │                              │                              │ ► store _pendingBarrierWT
     │                              │                              │
     │  (each frame, Update())      │               (each frame, Update())
     │  TotalWallTicks < BarrierWT → keep advancing               │ TotalWallTicks < BarrierWT → keep PLL
     │  TotalWallTicks ≥ BarrierWT → transition to Stepping       │ TotalWallTicks ≥ BarrierWT → transition Stepping
```

### Step Flow

```
Orchestrator                    CycloneDDS              SlaveNode
     │                              │                       │
     │ masterSync.Step(delta)        │                       │
     │ ► publish AdvanceFrameIntent  │                       │
     │                              │                       │
     │──FrameOrderDescriptor────────►│──FrameOrderDescriptor►│
     │                              │                       │ ingress → publish AdvanceFrameIntent
     │                              │                       │ slaveSyncCtrl.Update:
     │                              │                       │  advance time by FixedDelta
     │                              │                       │  publish FrameStepCompletedEvent
     │                              │                       │
     │◄─FrameAckDescriptor──────────◄│◄──FrameAckDescriptor──│
     │ ingress → publish             │                       │
     │     FrameStepCompletedEvent   │                       │
     │ masterSync.Update: collect ACK│                       │
     │ (all ACKs received → ready    │                       │
     │  for next Step)               │                       │
```

### Resume Flow

```
Orchestrator                    CycloneDDS              SlaveNode
     │                              │                       │
     │ masterSync.SwitchToContinuous()                       │
     │ ► capture SimTimeSnapshot=TotalTime                   │
     │ ► transition self to Continuous                       │
     │ ► publish SwitchTimeModeEvent(Cont, snapshot, scale)  │
     │                              │                       │
     │──SwitchTimeModeWireDto──────►│──SwitchTimeModeWireDto►│
     │                              │                       │ ingress → publish SwitchTimeModeEvent
     │                              │                       │ slaveSyncCtrl transits Continuous
     │                              │                       │ _totalTime = SimTimeSnapshot
     │                              │                       │ PLL resumes (warm, no cold-start)
```

---

## 6. What Gets Deleted

After all phases complete and pass tests, the following classes are deleted:

| Class | Location | Replaced by |
|---|---|---|
| `MasterTimeController` | `Controllers/` | `MasterSyncController` |
| `SteppedMasterController` | `Controllers/` | `MasterSyncController` |
| `SwitchableTimeController` | `Controllers/` | Unnecessary — no more hot-swap |
| `SlaveTimeController` | `Controllers/` | `SlaveSyncController` |
| `SteppedSlaveController` | `Controllers/` | `SlaveSyncController` |
| `DistributedTimeCoordinator` | `Controllers/` | Logic in `MasterSyncController` |
| `SlaveTimeModeListener` | `Controllers/` | Logic in `SlaveSyncController` |
| `FrameLockstepDescriptorTranslator` | `FDP.Toolkit.Time/` | `MasterLockstepTranslator` + `SlaveLockstepTranslator` |

`SteppingTimeController` is **NOT deleted** — it is a pure manual stepping controller used in
standalone tools and tests. It has no network role.

`SwitchTimeModeDescriptorTranslator` is **NOT deleted** — it remains the bridge for
`SwitchTimeModeEvent` between the local bus and DDS.

---

## 7. Implementation Phases

### Phase 1: CQRS Message Layer — Foundation

**Goal:** Establish clean, plain-field message contracts before touching any controller logic.
All existing tests must remain passing after this phase.

Tasks: `TCU-M001`, `TCU-M002`

### Phase 2: Unified Master Controller

**Goal:** Replace the Orchestrator's three-class time management (MasterTimeController +
SteppedMasterController + DistributedTimeCoordinator) with a single self-contained
MasterSyncController. Old classes remain but are unused in the Orchestrator after this phase.

Tasks: `TCU-MC001`

### Phase 3: Unified Slave Controller

**Goal:** Replace the slave-side four-component arrangement (SlaveTimeController +
SteppedSlaveController + SlaveTimeModeListener + continuousControllerFactory) with a single
SlaveSyncController. Old classes remain but unused on slaves after this phase.

Tasks: `TCU-SC001`

### Phase 4: Role-Split Lockstep Translators

**Goal:** Introduce MasterLockstepTranslator and SlaveLockstepTranslator; delete echoprevention
state from the old symmetric translator; update TimeNetworkModule.

Tasks: `TCU-TR001`, `TCU-TR002`, `TCU-TR003`

### Phase 5: Application Wiring

**Goal:** Wire the new controllers and translators in all four applications
(Orchestrator, SimHost, CGF, IG); update TimeControllerFactory; delete obsolete classes.

Tasks: `TCU-W001`, `TCU-W002`, `TCU-W003`, `TCU-W004`, `TCU-W005`, `TCU-W006`

### Phase 6: Test Coverage

**Goal:** Full unit and integration test suite for the unified design; migrate or delete tests
that covered removed classes.

Tasks: `TCU-T001`, `TCU-T002`, `TCU-T003`, `TCU-T004`, `TCU-T005`, `TCU-T006`
