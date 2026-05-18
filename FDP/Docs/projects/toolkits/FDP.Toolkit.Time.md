# FDP.Toolkit.Time

**Project Path**: `Toolkits/FDP.Toolkit.Time/FDP.Toolkit.Time.csproj`  
**Created**: February 10, 2026  
**Last Verified**: February 10, 2026  
**README Status**: No README

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Core Concepts](#core-concepts)
4. [Time Controllers](#time-controllers)
5. [Synchronization Mechanisms](#synchronization-mechanisms)
6. [Mode Switching](#mode-switching)
7. [Configuration](#configuration)
8. [Data Flow Diagrams](#data-flow-diagrams)
9. [Dependencies](#dependencies)
10. [Usage Examples](#usage-examples)
11. [Best Practices](#best-practices)
12. [Design Principles](#design-principles)
13. [Relationships to Other Projects](#relationships-to-other-projects)
14. [API Reference](#api-reference)
15. [Testing](#testing)
16. [Performance Considerations](#performance-considerations)
17. [Known Issues & Limitations](#known-issues--limitations)
18. [References](#references)

---

## Overview

**FDP.Toolkit.Time** provides sophisticated distributed time synchronization for multi-node FDP simulations. It enables seamless coordination between networked peers, supporting both real-time continuous operation and deterministic lockstep execution with jitter-free mode switching.

### Purpose

The toolkit solves the fundamental challenge of **distributed time coherence**:
- Multiple simulation nodes must maintain synchronized clocks
- Network latency and jitter create timing uncertainty
- Some scenarios require deterministic lockstep, others need real-time responsiveness
- Mode switches (pause/unpause) must be atomic across all nodes without visual jitter

### Key Features

- **Phase-Locked Loop (PLL) Synchronization**: Smooth, jitter-resistant clock tracking for continuous mode
- **Deterministic Lockstep**: Frame-perfect synchronization for replay and distributed testing
- **Jitter-Free Mode Switching**: Future barrier pattern prevents visual glitches
- **Master/Slave Architecture**: Centralized time authority for simplified coordination
- **Configurable Parameters**: Tunable PLL gain, snap thresholds, and lockstep timeouts
- **Manual Stepping Controller**: Frame-by-frame debugging support
- **Network Messages**: TimePulse, FrameOrder, and FrameAck for coordination

### Target Use Cases

1. **Multi-Node Real-Time Training Simulations**: Aircraft simulators with synchronized displays
2. **Distributed Deterministic Replay**: Synchronized playback across multiple visualization stations
3. **Pause/Resume Coordination**: Training scenarios requiring synchronized pause across federation
4. **Interactive Debugging**: Manual frame stepping for diagnosing distributed issues
5. **Hybrid Scenarios**: Switch between real-time operation and deterministic analysis

### Position in Solution Architecture

```
┌─────────────────────────────────────────┐
│    Application (NetworkDemo, CarKinem)  │  ← Uses TimeControllers
└────────────────┬────────────────────────┘
                 │
┌────────────────▼────────────────────────┐
│       ModuleHost.Core                   │  ← ITimeController interface
│       (Kernel manages TimeController)   │
└────────────────┬────────────────────────┘
                 │
       ┌─────────▼──────────┐
       │ Toolkit.Time       │  ◄── YOU ARE HERE
       │ (Time Coordination)│
       └─────────┬──────────┘
                 │
┌────────────────▼────────────────────────┐
│ Fdp.Kernel (EventBus, GlobalTime)      │  ← Foundation
└─────────────────────────────────────────┘
```

**Layer Role**: Implements time synchronization algorithms while remaining transport-agnostic. Network messages published via EventBus, transported by application-chosen mechanism (DDS, TCP, etc.).

---

## Architecture

### High-Level Design

FDP.Toolkit.Time implements a **Master/Slave time synchronization architecture** with two operational modes:

1. **Continuous Mode** (Real-Time)
   - Master publishes TimePulse (wall clock + simulation time)
   - Slaves use Phase-Locked Loop (PLL) to track Master clock
   - Gradual error correction avoids visual jitter
   - Supports time scaling (slow-mo, fast-forward)

2. **Deterministic Mode** (Lockstep)
   - Master publishes FrameOrder for each frame
   - Slaves wait for FrameOrder before advancing
   - Slaves send FrameAck after processing frame
   - Master waits for all ACKs before next frame
   - Ensures frame-perfect synchronization

### Component Breakdown

```
FDP.Toolkit.Time/
│
├── Controllers/
│   ├── MasterTimeController.cs           # Continuous mode Master
│   ├── SlaveTimeController.cs            # Continuous mode Slave (PLL)
│   ├── SteppedMasterController.cs        # Deterministic mode Master
│   ├── SteppedSlaveController.cs         # Deterministic mode Slave
│   ├── SteppingTimeController.cs         # Manual stepping (debug)
│   ├── DistributedTimeCoordinator.cs     # Master mode switching logic
│   ├── SlaveTimeModeListener.cs          # Slave mode switching logic
│   ├── TimeControllerFactory.cs          # Factory pattern for instantiation
│   ├── TimeConfig.cs                     # PLL and sync parameters
│   └── TimeControllerConfig.cs           # Role/mode configuration
│
├── Messages/
│   └── TimeMessages.cs                   # Network messages
│       ├── TimePulseDescriptor           # Continuous sync (1 Hz)
│       ├── FrameOrderDescriptor          # Lockstep order
│       ├── FrameAckDescriptor            # Lockstep acknowledgment
│       └── SwitchTimeModeEvent           # Mode change broadcast
│
└── AssemblyInfo.cs
```

### Design Patterns Employed

1. **Phase-Locked Loop (PLL)**: Gradual frequency adjustment for smooth synchronization
2. **Future Barrier**: Mode switches planned N frames ahead for jitter-free transition
3. **Master/Slave**: Single authoritative time source simplifies consensus
4. **Strategy Pattern**: ITimeController interface with multiple implementations
5. **Factory Pattern**: TimeControllerFactory encapsulates creation logic
6. **Event-Driven**: Network messages via FdpEventBus for transport independence
7. **Median Filter**: JitterFilter rejects outliers for stable PLL

### Technical Constraints

- **Network Latency Compensation**: PLL uses configurable latency estimate
- **Determinism**: Lockstep guarantees frame-perfect synchronization (required for replay)
- **Visual Smoothness**: Continuous mode avoids hard snaps (<500ms error threshold)
- **Scalability**: Lockstep ACK pattern limits to ~10 nodes (N² message complexity)
- **Transport Agnostic**: Uses EventBus abstraction, works with DDS/TCP/UDP

---

## Core Concepts

### 1. Time Roles

**Three roles define a node's behavior**:

```csharp
public enum TimeRole
{
    Standalone,  // No network sync, local wall clock only
    Master,      // Authoritative time source, publishes pulses/orders
    Slave        // Follows Master, consumes pulses/orders
}
```

**Role Responsibilities**:

| Role | Continuous Mode | Deterministic Mode |
|------|----------------|-------------------|
| **Standalone** | Local wall clock | Not supported (no distributed sync) |
| **Master** | Publishes TimePulse @1Hz | Publishes FrameOrder, waits for ACKs |
| **Slave** | Consumes TimePulse, PLL sync | Consumes FrameOrder, sends ACK |

**Typical Deployment**:
```
┌─────────────┐        TimePulse/FrameOrder         ┌─────────────┐
│   Master    │──────────────────────────────────────▶│   Slave 1   │
│   Node 100  │                                       │   Node 200  │
└─────────────┘        FrameAck (lockstep only)      └─────────────┘
       │                                                      │
       │                                                      │
       ▼                                                      ▼
┌─────────────┐                                       ┌─────────────┐
│   Slave 2   │◀───────────────────────────────────────│   Slave 3   │
│   Node 300  │        Peer-to-peer messages          │   Node 400  │
└─────────────┘        (application-specific)         └─────────────┘
```

---

### 2. Time Modes

**Two operational modes**:

```csharp
public enum TimeMode
{
    Continuous,     // Real-time, variable frame rate
    Deterministic   // Lockstep, fixed frame rate
}
```

**Mode Characteristics**:

| Aspect | Continuous Mode | Deterministic Mode |
|--------|----------------|-------------------|
| **Frame Rate** | Variable (wallclock-driven) | Fixed (configured, e.g., 60Hz) |
| **Synchronization** | Soft (PLL, gradual) | Hard (lockstep, ACKs) |
| **Latency Tolerance** | High (smoothed by PLL) | Low (waits for all nodes) |
| **Use Case** | Real-time training | Replay, debugging, verification |
| **Pause Support** | Instant (local only) | Coordinated (all nodes sync) |

---

### 3. Phase-Locked Loop (PLL)

**Problem**: Network latency and jitter cause Slave clocks to drift from Master.

**Naive Solution**: Hard snap to Master time on every pulse → Visual jitter.

**PLL Solution**: Gradual frequency adjustment to converge smoothly.

**Algorithm**:
```
1. Measure Error: 
   error = (MasterTime + Latency) - LocalVirtualTime

2. Filter Jitter:
   filteredError = Median(error samples over last N frames)

3. Compute Correction:
   correction = filteredError * PLLGain
   correction = Clamp(correction, -MaxSlew, +MaxSlew)

4. Adjust Frame Delta:
   adjustedDelta = rawDelta * (1.0 + correction)
   virtualTime += adjustedDelta

5. Update Simulation:
   totalTime += adjustedDelta * timeScale
```

**Example**:
```
Master Time: 10.500s
Slave Time:  10.450s (50ms behind)
Latency:     2ms

Error = (10.500 + 0.002) - 10.450 = 0.052s = 52ms
FilteredError = Median(52ms, 48ms, 55ms, 50ms, 51ms) = 51ms
Correction = 51ms * 0.1 (PLLGain) = 5.1ms speedup

Next frame (16.67ms @ 60Hz):
AdjustedDelta = 16.67ms * (1 + 0.0051) = 16.75ms
Slave advances 0.08ms more than wall clock → Catches up gradually
```

**Benefits**:
- Smooth convergence (no visual jitter)
- Robust to transient network spikes (median filter)
- Configurable gain (fast vs. smooth tradeoff)

---

### 4. Lockstep Synchronization

**Deterministic Mode ensures frame-perfect execution**:

**Frame Protocol**:
```
Master (Frame N):
  1. Advance simulation (Step)
  2. Publish FrameOrder(FrameID=N, FixedDelta=16.67ms)
  3. Wait for FrameAck from all Slaves

Slave (Frame N):
  1. Wait for FrameOrder(FrameID=N)
  2. Advance simulation (Update)
  3. Publish FrameAck(FrameID=N, NodeID=myID)

Master:
  4. Receive FrameAck from all Slaves
  5. Proceed to Frame N+1
```

**State Machine**:
```
Master:
  READY ──Step()──▶ SENT_ORDER ──All ACKs Received──▶ READY

Slave:
  WAITING ──Order Received──▶ PROCESSING ──Send ACK──▶ WAITING
```

**Guarantees**:
- All nodes execute Frame N before any node proceeds to Frame N+1
- Deterministic replay: Same inputs → Same frame sequence
- State synchronization: All nodes at same simulation time

**Performance**:
- **Throughput**: Limited by slowest node + network RTT
- **Scalability**: O(N) messages per frame (N Slaves × 2 messages)
- **Typical Framerate**: 30-60fps for local network, 10-30fps for WAN

---

### 5. Jitter-Free Mode Switching

**Problem**: Switching from Continuous to Deterministic (pause) mid-frame causes visual jitter.

**Naive Approach**:
```
Master: "Pause now!" → Broadcast SwitchMode
Slaves: Receive at different frames due to latency → Desync!
```

**Future Barrier Solution**:
```
T=1000: Master decides to pause
T=1000: Master broadcasts SwitchMode(TargetMode=Deterministic, BarrierFrame=1010)
T=1001-1009: All nodes continue in Continuous mode
T=1010: All nodes swap to SteppedController simultaneously
Result: Jitter-free transition, no visual pop
```

**Parameters**:
- **Lookahead** (`PauseBarrierFrames`): Default 10 frames (~167ms @ 60Hz)
- **Trade-off**: Higher = safer for high-latency networks, Lower = faster response

**Implementation**:
```csharp
// Master
long barrierFrame = currentFrame + config.PauseBarrierFrames;
Publish(SwitchMode(TargetMode=Deterministic, BarrierFrame=barrierFrame));
// Wait for barrier frame to arrive
if (currentFrame >= barrierFrame)
    Swap to SteppedMasterController

// Slave
OnSwitchModeReceived(event)
{
    _pendingBarrierFrame = event.BarrierFrame;
}

OnUpdate()
{
    if (currentFrame >= _pendingBarrierFrame)
        Swap to SteppedSlaveController
}
```

---

## Time Controllers

### MasterTimeController (Continuous Mode)

**Purpose**: Authoritative time source for real-time operation.

**Algorithm**:
```
1. Measure wall clock delta (Stopwatch)
2. Advance frame number
3. Accumulate totalTime (scaled by timeScale)
4. Publish TimePulse @1Hz (includes wall ticks, sim time, timeScale)
5. Return GlobalTime struct
```

**API**:
```csharp
public class MasterTimeController : ITimeController
{
    public GlobalTime Update();
    public void SetTimeScale(float scale);
    public void SeedState(GlobalTime state);
    public GlobalTime GetCurrentState();
}
```

**Key Features**:
- **Time Scaling**: Supports slow-mo (scale < 1.0) and fast-forward (scale > 1.0)
- **Pulse Rate**: Publishes TimePulse every 1 second (configurable via PulseIntervalTicks)
- **Immediate Publish**: TimeScale changes trigger immediate pulse

**Usage**:
```csharp
var master = new MasterTimeController(eventBus, config);
master.SetTimeScale(0.5f); // Slow motion

while (running)
{
    GlobalTime time = master.Update();
    kernel.Tick(time.DeltaTime);
}
```

---

### SlaveTimeController (Continuous Mode)

**Purpose**: Follows Master clock using PLL for smooth synchronization.

**Algorithm**:
```
1. Consume TimePulse messages
2. Calculate clock error (MasterTime + Latency - VirtualTime)
3. Filter error using median filter (jitter resistance)
4. Compute PLL correction factor (error × PLLGain)
5. Clamp correction (prevent overshoot)
6. Adjust frame delta: adjustedDelta = rawDelta × (1 + correction)
7. Advance virtualTime and totalTime
```

**Internal State**:
```csharp
private long _virtualWallTicks;      // PLL-adjusted clock
private double _currentError;        // Tracking error
private JitterFilter _errorFilter;   // Median filter for robustness
```

**PLL Parameters** (from TimeConfig):
- **PLLGain**: Correction strength (0.1 = 10% per second)
- **MaxSlew**: Maximum frequency deviation (0.05 = ±5%)
- **SnapThresholdMs**: Hard snap if error > 500ms
- **AverageLatencyTicks**: Estimated network latency (2ms default)
- **JitterWindowSize**: Median filter window (5 samples)

**Snap Logic**:
```csharp
if (Math.Abs(errorMs) > config.SnapThresholdMs)
{
    // Error too large → Hard snap
    _virtualWallTicks = targetWallTicks;
    _totalTime = pulse.SimTimeSnapshot + timeSincePulse;
    _errorFilter.Reset();
}
```

**Usage**:
```csharp
var slave = new SlaveTimeController(eventBus, config);

while (running)
{
    GlobalTime time = slave.Update(); // Consumes pulses, applies PLL
    kernel.Tick(time.DeltaTime);
}
```

---

### SteppedMasterController (Deterministic Mode)

**Purpose**: Coordinates lockstep frame execution.

**State Machine**:
```
READY ──Step()──▶ SENT_ORDER ──All ACKs Received──▶ READY
```

**Frame Execution**:
```csharp
public GlobalTime Step(float fixedDeltaTime)
{
    _frameNumber++;
    _totalTime += fixedDeltaTime * _timeScale;
    
    // Send order to all slaves
    Publish(FrameOrder(FrameID=_frameNumber, FixedDelta=fixedDeltaTime));
    
    // Mark waiting
    _pendingAcks = new HashSet(_slaveNodeIds);
    _waitingForAcks = true;
    
    return GetCurrentTime();
}

private void OnAckReceived(FrameAck ack)
{
    if (ack.FrameID == _frameNumber)
    {
        _pendingAcks.Remove(ack.NodeID);
        if (_pendingAcks.Count == 0)
            _waitingForAcks = false; // Ready for next frame
    }
}
```

**Auto-Advance**:
```csharp
public GlobalTime Update()
{
    // Process incoming ACKs
    foreach (var ack in ConsumeEvents<FrameAck>()) OnAckReceived(ack);
    
    // If not waiting, automatically advance
    if (!_waitingForAcks)
        return Step(config.FixedDeltaSeconds);
    
    return GetCurrentTime(); // Frozen
}
```

**Usage**:
```csharp
var master = new SteppedMasterController(eventBus, slaveIds, config);

// Manual stepping
GlobalTime time = master.Step(0.016667f); // 60Hz
kernel.Tick(time.DeltaTime);
```

---

### SteppedSlaveController (Deterministic Mode)

**Purpose**: Executes frames in lockstep with Master.

**Algorithm**:
```
1. Buffer incoming FrameOrders
2. When FrameOrder received for Frame N:
   a. Execute frame with FixedDelta
   b. Advance frameNumber, totalTime
   c. Send FrameAck(FrameID=N, NodeID=myID)
3. If no order available, return frozen time (DeltaTime=0)
```

**Frame Queue**:
```csharp
private readonly Queue<FrameOrderDescriptor> _pendingOrders = new();

public GlobalTime Update()
{
    // Refill buffer
    foreach (var order in ConsumeEvents<FrameOrder>())
    {
        if (order.FrameID > _frameNumber)
            _pendingOrders.Enqueue(order);
    }
    
    // Process next order
    if (_pendingOrders.Count > 0)
    {
        var order = _pendingOrders.Dequeue();
        ExecuteFrame(order);
        SendAck(order.FrameID);
        return GetCurrentTime(order.FixedDelta);
    }
    
    return GetCurrentTime(0); // Frozen
}
```

**Usage**:
```csharp
var slave = new SteppedSlaveController(eventBus, localNodeId, fixedDelta);

while (running)
{
    GlobalTime time = slave.Update(); // Waits for FrameOrder
    kernel.Tick(time.DeltaTime);
}
```

---

### SteppingTimeController (Manual Stepping)

**Purpose**: Manual frame-by-frame control for debugging.

**Characteristics**:
- No wall clock measurement
- Advances only when `Step()` called
- `Update()` returns frozen time (DeltaTime=0)

**Usage**:
```csharp
var controller = new SteppingTimeController(seedState);

// Advance one frame
GlobalTime time = controller.Step(0.016667f);
kernel.Tick(time.DeltaTime);

// Frozen (Update does nothing)
GlobalTime frozen = controller.Update(); // DeltaTime=0
```

**Use Cases**:
- Interactive debugging (step button in UI)
- Reproducible test cases
- Manual timeline scrubbing

---

### DistributedTimeCoordinator (Master Mode Switching)

**Purpose**: Orchestrates mode switches on Master node.

**API**:
```csharp
public void SwitchToDeterministic(HashSet<int> slaveNodeIds);
public void SwitchToContinuous();
public void Update(); // Poll for barrier frame
```

**Pause Flow** (Continuous → Deterministic):
```csharp
public void SwitchToDeterministic(HashSet<int> slaveNodeIds)
{
    long barrierFrame = currentFrame + config.PauseBarrierFrames;
    
    Publish(SwitchMode(
        TargetMode = Deterministic,
        BarrierFrame = barrierFrame,
        FixedDeltaSeconds = config.FixedDeltaSeconds
    ));
    
    _pendingBarrierFrame = barrierFrame;
}

public void Update()
{
    if (currentFrame >= _pendingBarrierFrame)
    {
        SwapController(new SteppedMasterController(...));
        _pendingBarrierFrame = -1;
    }
}
```

**Unpause Flow** (Deterministic → Continuous):
```csharp
public void SwitchToContinuous()
{
    Publish(SwitchMode(TargetMode = Continuous, BarrierFrame = 0)); // Immediate
    
    SwapController(new MasterTimeController(...));
}
```

---

### SlaveTimeModeListener (Slave Mode Switching)

**Purpose**: Handles mode switch events on Slave nodes.

**Algorithm**:
```csharp
OnSwitchModeReceived(event)
{
    if (event.TargetMode == Deterministic)
    {
        _pendingBarrierFrame = event.BarrierFrame;
        _pendingEvent = event;
    }
    else if (event.TargetMode == Continuous)
    {
        SwapToContinuous(event); // Immediate
    }
}

Update()
{
    if (currentFrame >= _pendingBarrierFrame)
    {
        SwapToDeterministic(_pendingEvent);
        _pendingBarrierFrame = -1;
    }
}
```

**Safety Check** (Late Event Handling):
```csharp
if (currentFrame >= event.BarrierFrame)
{
    // Already past barrier (high latency!)
    Log.Warn("Swapping immediately - network latency exceeded lookahead");
    SwapToDeterministic(event); // Emergency swap
}
```

---

## Synchronization Mechanisms

### Continuous Mode Synchronization

**Message**: TimePulseDescriptor

```csharp
struct TimePulseDescriptor
{
    long MasterWallTicks;     // Master's Stopwatch ticks
    double SimTimeSnapshot;   // Master's simulation time
    float TimeScale;          // Current time scale
    long SequenceId;          // Frame number
}
```

**Frequency**: 1 Hz (or on TimeScale change)

**Flow**:
```
Master (every 1 second):
  CurrentTicks = Stopwatch.GetTimestamp();
  Publish(TimePulse(MasterTicks=CurrentTicks, SimTime=_totalTime, Scale=_timeScale));

Slave (on receive):
  LocalTicks = Stopwatch.GetTimestamp();
  TimeSincePulse = LocalTicks - pulse.MasterWallTicks;
  TargetTicks = pulse.MasterWallTicks + AverageLatency + TimeSincePulse;
  
  Error = TargetTicks - _virtualWallTicks;
  FilteredError = Median(Error, ...);
  
  Correction = FilteredError × PLLGain;
  AdjustedDelta = RawDelta × (1 + Correction);
  _virtualWallTicks += AdjustedDelta;
```

**Diagram**:
```
Master                                    Slave
─────────────────────────────────────────────────────────────
T=0.000   Tick=0                          
T=1.000   Tick=10M  ──TimePulse────▶     Receive @T=1.002 (2ms latency)
                     (Ticks=10M,           LocalTicks=10.02M
                      SimTime=1.0)         Error = (10M + 2K + 20K) - 10M = 22K ticks
                                           Correction = 22K × 0.1 = 2.2K ticks/sec
                                           Next 60 frames: Slightly faster delta
T=2.000   Tick=20M  ──TimePulse────▶     Error reduced to ~10K ticks
T=3.000   Tick=30M  ──TimePulse────▶     Error reduced to ~5K ticks
                                           → Convergence achieved
```

---

### Deterministic Mode Synchronization

**Messages**: FrameOrderDescriptor, FrameAckDescriptor

```csharp
struct FrameOrderDescriptor
{
    long FrameID;        // Sequential frame number
    float FixedDelta;    // Delta time for this frame
    long SequenceID;     // For duplicate detection
}

struct FrameAckDescriptor
{
    long FrameID;        // Which frame was processed
    int NodeID;          // Slave ID
    int Checksum;        // Optional state hash
}
```

**Flow**:
```
Master:
  1. Step(fixedDelta)
  2. Publish FrameOrder(FrameID=N, FixedDelta=0.016667)
  3. _pendingAcks = {Slave1, Slave2, Slave3}
  4. Wait...

Slave1:
  1. Receive FrameOrder(FrameID=N)
  2. Execute frame: Tick(fixedDelta)
  3. Publish FrameAck(FrameID=N, NodeID=1)

Slave2:
  1. Receive FrameOrder(FrameID=N)
  2. Execute frame: Tick(fixedDelta)
  3. Publish FrameAck(FrameID=N, NodeID=2)

Slave3:
  1. Receive FrameOrder(FrameID=N)
  2. Execute frame: Tick(fixedDelta)
  3. Publish FrameAck(FrameID=N, NodeID=3)

Master:
  4. Receive FrameAck from Slave1 → _pendingAcks = {Slave2, Slave3}
  5. Receive FrameAck from Slave2 → _pendingAcks = {Slave3}
  6. Receive FrameAck from Slave3 → _pendingAcks = {}
  7. All ACKs received! → Step(fixedDelta) for Frame N+1
```

**Diagram**:
```
Frame Execution Timeline:

Master    Slave1    Slave2    Slave3
──────    ──────    ──────    ──────
Step ────▶ │         │         │       (FrameOrder N)
Wait       │         │         │
  │      Execute   Execute   Execute   (Parallel execution)
  │        │         │         │
  │◀─────ACK      ACK       ACK         (FrameAck N)
  │        │         │         │
Step ────▶ │         │         │       (FrameOrder N+1)
```

---

## Mode Switching

### Future Barrier Pattern

**Problem**: Distributed mode switches have race conditions due to network latency.

**Solution**: Plan switch at future frame, broadcast to all nodes.

**Implementation**:
```
T=1000: User presses "Pause" button (on Master or sent to Master)

Master:
  BarrierFrame = 1000 + 10 = 1010
  Publish SwitchMode(Target=Deterministic, Barrier=1010, FixedDelta=0.016667)

Slave (receives @T=1002 due to latency):
  _pendingBarrierFrame = 1010
  "I'll switch at Frame 1010"

All nodes continue running in Continuous mode...

T=1010:
  Master: currentFrame=1010 >= 1010 → Swap to SteppedMaster
  Slave:  currentFrame=1010 >= 1010 → Swap to SteppedSlave
  
Result: Simultaneous swap, no jitter!
```

**Sequence Diagram**:
```
User        Master                   Slave1                  Slave2
 │            │                        │                       │
 │──Pause───▶│                        │                       │
 │            │                        │                       │
 │            │─────SwitchMode────────▶│                       │
 │            │     (Barrier=1010)      │─────SwitchMode──────▶│
 │            │                        │     (Barrier=1010)    │
 │            │                        │                       │
 │       [Frame 1001-1009: All running Continuous]             │
 │            │                        │                       │
 │       [Frame 1010 reached]          │                       │
 │            │                        │                       │
 │            ├─Swap─▶SteppedMaster    ├─Swap─▶SteppedSlave   ├─Swap─▶SteppedSlave
 │            │                        │                       │
 │            │◀──────────────────────────── All synchronized ─┘
```

**Parameters**:
- **PauseBarrierFrames**: Lookahead window (default 10 frames)
- **Trade-off**:
  - Too small: Network latency exceeds lookahead → Emergency snap
  - Too large: Pause feels sluggish (167ms @ 10 frames @ 60Hz)

---

### Unpause (Deterministic → Continuous)

**Simpler**: No barrier needed, unpause is immediate.

**Rationale**: Visual jitter less noticeable when resuming motion.

**Implementation**:
```
Master:
  Publish SwitchMode(Target=Continuous, Barrier=0) // Immediate
  Swap to MasterTimeController

Slave:
  On receive → Swap to SlaveTimeController immediately
```

---

## Configuration

### TimeConfig (Synchronization Parameters)

```csharp
public class TimeConfig
{
    // PLL Parameters
    public double PLLGain { get; set; } = 0.1;           // 10% correction strength
    public double MaxSlew { get; set; } = 0.05;          // ±5% frequency limit
    public double SnapThresholdMs { get; set; } = 500.0; // Hard snap if >500ms error
    public int JitterWindowSize { get; set; } = 5;       // Median filter window
    
    // Network
    public long AverageLatencyTicks { get; set; } = 2ms; // Estimated network RTT/2
    
    // Lockstep
    public float FixedDeltaSeconds { get; set; } = 1/60f; // 60 FPS
    public double LockstepTimeoutMs { get; set; } = 1000; // ACK timeout warning
    
    // Mode Switching
    public int PauseBarrierFrames { get; set; } = 10;    // Lookahead for pause
}
```

**Tuning Guide**:

| Parameter | Low Value | High Value | Recommendation |
|-----------|-----------|------------|----------------|
| **PLLGain** | Slow convergence (smooth) | Fast convergence (responsive) | 0.1 for training, 0.05 for visuals |
| **MaxSlew** | Prevents overshoot | Faster correction | 0.05 (±5%) max to avoid physics issues |
| **SnapThresholdMs** | More gradual adjustments | Tolerates larger errors | 500ms typical, 1000ms for WAN |
| **AverageLatencyTicks** | Must match network RTT | Over-estimate → Slave runs ahead | Measure via ping, halve for one-way |
| **PauseBarrierFrames** | Fast pause response | Safer for high jitter | 10 for LAN, 30 for WAN |

---

### TimeControllerConfig (Role & Mode)

```csharp
public class TimeControllerConfig
{
    public TimeRole Role { get; set; }       // Standalone, Master, Slave
    public TimeMode Mode { get; set; }       // Continuous, Deterministic
    public TimeConfig SyncConfig { get; set; } = TimeConfig.Default;
    
    // For Master in Deterministic mode
    public HashSet<int>? AllNodeIds { get; set; }
    
    // For Slave in any mode
    public int LocalNodeId { get; set; } = 0;
    
    // Initial state
    public float InitialTimeScale { get; set; } = 1.0f;
    
    // Testing only
    internal Func<long>? TickProvider { get; set; }
}
```

**Factory Instantiation**:
```csharp
var config = new TimeControllerConfig
{
    Role = TimeRole.Master,
    Mode = TimeMode.Continuous,
    SyncConfig = new TimeConfig { PLLGain = 0.1 },
    InitialTimeScale = 1.0f
};

ITimeController controller = TimeControllerFactory.Create(eventBus, config);
```

---

## Data Flow Diagrams

### Continuous Mode Data Flow

```
┌────────────────────────────────────────────────────────────┐
│                         MASTER NODE                         │
├────────────────────────────────────────────────────────────┤
│                                                             │
│  1. MasterTimeController.Update()                          │
│     ├─ Measure wall clock delta (Stopwatch)                │
│     ├─ Advance _frameNumber                                │
│     ├─ Accumulate _totalTime += delta × _timeScale         │
│     └─ Every 1 second:                                      │
│         └─ Publish TimePulseDescriptor                      │
│             {                                               │
│               MasterWallTicks = Stopwatch.GetTimestamp()    │
│               SimTimeSnapshot = _totalTime                  │
│               TimeScale = _timeScale                        │
│               SequenceId = _frameNumber                     │
│             }                                               │
│                                                             │
└────────────────────────┬───────────────────────────────────┘
                         │
                         │ Network (EventBus → DDS/TCP)
                         │
                         ▼
┌────────────────────────────────────────────────────────────┐
│                         SLAVE NODE                          │
├────────────────────────────────────────────────────────────┤
│                                                             │
│  1. SlaveTimeController.Update()                           │
│     ├─ Consume TimePulseDescriptor messages                │
│     │                                                       │
│     ├─ OnTimePulseReceived(pulse):                         │
│     │   ├─ CurrentTicks = Stopwatch.GetTimestamp()         │
│     │   ├─ TimeSincePulse = CurrentTicks - pulse.MasterTicks│
│     │   ├─ TargetTicks = pulse.MasterTicks + Latency + TimeSincePulse│
│     │   ├─ ErrorTicks = TargetTicks - _virtualWallTicks    │
│     │   ├─ JitterFilter.AddSample(ErrorTicks)              │
│     │   ├─ Update _timeScale = pulse.TimeScale             │
│     │   │                                                   │
│     │   └─ If |Error| > SnapThreshold:                     │
│     │       ├─ HARD SNAP: _virtualWallTicks = TargetTicks  │
│     │       ├─ _totalTime = pulse.SimTime + elapsed        │
│     │       └─ JitterFilter.Reset()                        │
│     │                                                       │
│     ├─ PLL Calculation:                                    │
│     │   ├─ FilteredError = JitterFilter.GetMedian()        │
│     │   ├─ Correction = FilteredError × PLLGain            │
│     │   ├─ Correction = Clamp(Correction, -MaxSlew, +MaxSlew)│
│     │   │                                                   │
│     │   ├─ RawDelta = Stopwatch.ElapsedTicks               │
│     │   ├─ AdjustedDelta = RawDelta × (1 + Correction)     │
│     │   └─ _virtualWallTicks += AdjustedDelta              │
│     │                                                       │
│     └─ Accumulate:                                         │
│         ├─ DeltaSec = AdjustedDelta / Frequency            │
│         └─ _totalTime += DeltaSec × _timeScale             │
│                                                             │
└────────────────────────────────────────────────────────────┘
```

### Deterministic Mode Data Flow

```
┌────────────────────────────────────────────────────────────┐
│                     MASTER NODE (Frame N)                   │
├────────────────────────────────────────────────────────────┤
│                                                             │
│  1. SteppedMasterController.Step(fixedDelta)               │
│     ├─ _frameNumber++                                      │
│     ├─ _totalTime += fixedDelta × _timeScale               │
│     ├─ Execute simulation frame                            │
│     │                                                       │
│     └─ Publish FrameOrderDescriptor                        │
│         {                                                   │
│           FrameID = N                                       │
│           FixedDelta = 0.016667 (60Hz)                      │
│           SequenceID = N                                    │
│         }                                                   │
│                                                             │
│  2. Set _waitingForAcks = true                             │
│     _pendingAcks = {Slave1, Slave2, Slave3}                │
│                                                             │
└────────────────────────┬───────────────────────────────────┘
                         │
                         │ Broadcast to all Slaves
                         │
         ┌───────────────┼───────────────┐
         │               │               │
         ▼               ▼               ▼
    ┌────────┐     ┌────────┐     ┌────────┐
    │ Slave1 │     │ Slave2 │     │ Slave3 │
    └────┬───┘     └────┬───┘     └────┬───┘
         │               │               │
         │  3. SteppedSlaveController.Update()
         │     ├─ Consume FrameOrder(N)
         │     ├─ _frameNumber = N
         │     ├─ _totalTime += fixedDelta
         │     └─ Execute simulation frame
         │
         │  4. Publish FrameAckDescriptor
         │     {
         │       FrameID = N
         │       NodeID = myID
         │       Checksum = stateHash
         │     }
         │               │               │
         └───────────────┼───────────────┘
                         │
                         │ Send ACKs back to Master
                         │
                         ▼
┌────────────────────────────────────────────────────────────┐
│                     MASTER NODE (Waiting)                   │
├────────────────────────────────────────────────────────────┤
│                                                             │
│  5. OnAckReceived(FrameAck):                               │
│     ├─ If ack.FrameID == currentFrame:                     │
│     │   ├─ _pendingAcks.Remove(ack.NodeID)                 │
│     │   └─ If _pendingAcks.Count == 0:                     │
│     │       └─ _waitingForAcks = false                     │
│     │           → Ready for Frame N+1                       │
│                                                             │
└────────────────────────────────────────────────────────────┘
```

### Mode Switch Flow (Pause)

```
┌────────────────────────────────────────────────────────────┐
│                     USER ACTION (Frame 1000)                │
│  User presses "Pause" button on Master UI                  │
└────────────────────────┬───────────────────────────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────────┐
│            MASTER: DistributedTimeCoordinator               │
├────────────────────────────────────────────────────────────┤
│  SwitchToDeterministic(slaveNodeIds):                      │
│    ├─ BarrierFrame = 1000 + 10 = 1010                      │
│    ├─ _pendingBarrierFrame = 1010                          │
│    └─ Publish SwitchTimeModeEvent                          │
│        {                                                    │
│          TargetMode = Deterministic                         │
│          BarrierFrame = 1010                                │
│          FixedDeltaSeconds = 0.016667                       │
│        }                                                    │
└────────────────────────┬───────────────────────────────────┘
                         │
                         │ Broadcast
                         │
         ┌───────────────┼───────────────┐
         │               │               │
         ▼               ▼               ▼
    ┌────────┐     ┌────────┐     ┌────────┐
    │ Slave1 │     │ Slave2 │     │ Slave3 │
    └────┬───┘     └────┬───┘     └────┬───┘
         │               │               │
         │  SlaveTimeModeListener.OnSwitchModeReceived():
         │    ├─ _pendingBarrierFrame = 1010
         │    └─ Continue running in Continuous mode...
         │               │               │
         │         [Frames 1001-1009]    │
         │               │               │
         │         [Frame 1010 Reached]  │
         │               │               │
         │  SlaveTimeModeListener.Update():
         │    ├─ currentFrame >= 1010 → TRUE
         │    ├─ Swap to SteppedSlaveController
         │    └─ _pendingBarrierFrame = -1
         │               │               │
         └───────────────┴───────────────┘
                         │
                         ▼
┌────────────────────────────────────────────────────────────┐
│           MASTER: DistributedTimeCoordinator               │
│  Update():                                                  │
│    ├─ currentFrame >= 1010 → TRUE                          │
│    ├─ Swap to SteppedMasterController                      │
│    └─ _pendingBarrierFrame = -1                            │
│                                                             │
│  [All nodes now in Deterministic mode @ Frame 1010]        │
└────────────────────────────────────────────────────────────┘

Result: Jitter-free synchronized pause!
```

---

## Dependencies

### Project References

```xml
<ItemGroup>
  <ProjectReference Include="..\..\ModuleHost\ModuleHost.Core\ModuleHost.Core.csproj" />
  <ProjectReference Include="..\..\Kernel\Fdp.Kernel\Fdp.Kernel.csproj" />
  <ProjectReference Include="..\..\Common\FDP.Interfaces\FDP.Interfaces.csproj" />
</ItemGroup>
```

### NuGet Packages

None (pure C# implementation)

### Dependency Justification

| Dependency | Reason |
|------------|--------|
| **ModuleHost.Core** | ITimeController interface, TimeMode/TimeRole enums, ISteppableTimeController |
| **Fdp.Kernel** | FdpEventBus (message passing), GlobalTime struct, EventId attribute |
| **FDP.Interfaces** | (Minimal, mostly for compatibility) |

**Transport Independence**: Time coordination uses EventBus abstraction. Actual network transport (DDS, TCP, etc.) provided by application layer.

---

## Usage Examples

### Example 1: Standalone Time Controller

```csharp
using FDP.Toolkit.Time.Controllers;
using Fdp.Kernel;

// Standalone mode: No network sync, local wall clock only
var config = new TimeControllerConfig
{
    Role = TimeRole.Standalone,
    Mode = TimeMode.Continuous,
    SyncConfig = TimeConfig.Default
};

var eventBus = new FdpEventBus(); // Dummy bus
ITimeController controller = TimeControllerFactory.Create(eventBus, config);

// Main loop
while (running)
{
    GlobalTime time = controller.Update();
    kernel.Tick(time.DeltaTime);
    
    Console.WriteLine($"Frame {time.FrameNumber}, Time={time.TotalTime:F3}s");
}
```

**Output**:
```
Frame 1, Time=0.016s
Frame 2, Time=0.033s
Frame 3, Time=0.049s
...
```

---

### Example 2: Master/Slave Continuous Mode

**Master Node**:
```csharp
var eventBus = new FdpEventBus();

var masterConfig = new TimeControllerConfig
{
    Role = TimeRole.Master,
    Mode = TimeMode.Continuous,
    SyncConfig = new TimeConfig
    {
        PLLGain = 0.1,
        FixedDeltaSeconds = 1.0f / 60.0f
    }
};

var master = TimeControllerFactory.Create(eventBus, masterConfig);

// Main loop
while (running)
{
    GlobalTime time = master.Update(); // Publishes TimePulse @1Hz
    kernel.Tick(time.DeltaTime);
}
```

**Slave Node**:
```csharp
var eventBus = new FdpEventBus(); // Shared or network-backed

var slaveConfig = new TimeControllerConfig
{
    Role = TimeRole.Slave,
    Mode = TimeMode.Continuous,
    SyncConfig = new TimeConfig
    {
        PLLGain = 0.1,
        AverageLatencyTicks = Stopwatch.Frequency * 2 / 1000, // 2ms
        SnapThresholdMs = 500.0
    }
};

var slave = TimeControllerFactory.Create(eventBus, slaveConfig);

// Main loop
while (running)
{
    GlobalTime time = slave.Update(); // Consumes TimePulse, applies PLL
    kernel.Tick(time.DeltaTime);
}
```

**Network Integration** (Application Layer):
```csharp
// Publish TimePulse to DDS
eventBus.Subscribe<TimePulseDescriptor>(pulse =>
{
    ddsWriter.Write(pulse); // Send to network
});

// Consume TimePulse from DDS
ddsReader.OnDataAvailable += data =>
{
    eventBus.Publish(data as TimePulseDescriptor); // Inject to local bus
};
```

---

### Example 3: Deterministic Lockstep Mode

**Master Node**:
```csharp
var slaveIds = new HashSet<int> { 200, 300 }; // Node IDs of Slaves

var masterConfig = new TimeControllerConfig
{
    Role = TimeRole.Master,
    Mode = TimeMode.Deterministic,
    AllNodeIds = slaveIds,
    SyncConfig = new TimeConfig
    {
        FixedDeltaSeconds = 1.0f / 60.0f,
        LockstepTimeoutMs = 1000.0
    }
};

var master = (SteppedMasterController)TimeControllerFactory.Create(eventBus, masterConfig);

// Manual stepping
while (running)
{
    GlobalTime time = master.Step(1.0f / 60.0f); // Sends FrameOrder, waits for ACKs
    kernel.Tick(time.DeltaTime);
    
    // Or use auto-advance: master.Update()
}
```

**Slave Node (ID=200)**:
```csharp
var slaveConfig = new TimeControllerConfig
{
    Role = TimeRole.Slave,
    Mode = TimeMode.Deterministic,
    LocalNodeId = 200,
    SyncConfig = new TimeConfig
    {
        FixedDeltaSeconds = 1.0f / 60.0f
    }
};

var slave = TimeControllerFactory.Create(eventBus, slaveConfig);

// Main loop
while (running)
{
    GlobalTime time = slave.Update(); // Waits for FrameOrder, sends ACK
    kernel.Tick(time.DeltaTime);
}
```

---

### Example 4: Mode Switching (Pause/Unpause)

**Master Node** (with DistributedTimeCoordinator):
```csharp
var slaveIds = new HashSet<int> { 200, 300 };

var kernel = new ModuleHostKernel(world, eventAccumulator);
var timeConfig = new TimeControllerConfig
{
    Role = TimeRole.Master,
    Mode = TimeMode.Continuous,
    AllNodeIds = slaveIds,
    SyncConfig = new TimeConfig
    {
        PauseBarrierFrames = 10,
        FixedDeltaSeconds = 1.0f / 60.0f
    }
};

var controller = TimeControllerFactory.Create(eventBus, timeConfig);
kernel.SetTimeController(controller);

var coordinator = new DistributedTimeCoordinator(eventBus, kernel, timeConfig, slaveIds);

// Main loop
while (running)
{
    coordinator.Update(); // Check for barrier frame

    if (userPressedPause)
    {
        coordinator.SwitchToDeterministic(slaveIds); // Plan pause 10 frames ahead
    }
    
    if (userPressedUnpause)
    {
        coordinator.SwitchToContinuous(); // Immediate unpause
    }
    
    kernel.Tick();
}
```

**Slave Node** (with SlaveTimeModeListener):
```csharp
var kernel = new ModuleHostKernel(world, eventAccumulator);
var timeConfig = new TimeControllerConfig
{
    Role = TimeRole.Slave,
    Mode = TimeMode.Continuous,
    LocalNodeId = 200,
    SyncConfig = TimeConfig.Default
};

var controller = TimeControllerFactory.Create(eventBus, timeConfig);
kernel.SetTimeController(controller);

var listener = new SlaveTimeModeListener(eventBus, kernel, timeConfig);

// Main loop
while (running)
{
    listener.Update(); // Check for mode switch events
    kernel.Tick();
}
```

---

### Example 5: Manual Stepping for Debugging

```csharp
var seedState = new GlobalTime
{
    FrameNumber = 0,
    TotalTime = 0.0,
    TimeScale = 1.0f,
    UnscaledTotalTime = 0.0
};

var controller = new SteppingTimeController(seedState);

// Frame-by-frame stepping (e.g., triggered by UI button)
void OnStepButtonPressed()
{
    GlobalTime time = controller.Step(1.0f / 60.0f);
    kernel.Tick(time.DeltaTime);
    
    Console.WriteLine($"Stepped to Frame {time.FrameNumber}");
}

// Update() returns frozen time (DeltaTime=0)
GlobalTime frozenTime = controller.Update(); // No-op
```

---

## Best Practices

### 1. Match Configuration to Deployment

```csharp
// ❌ BAD: Using Continuous config for deterministic replay
var config = new TimeControllerConfig
{
    Role = TimeRole.Slave,
    Mode = TimeMode.Continuous // Wrong! Replay needs Deterministic
};

// ✅ GOOD: Use Deterministic mode for frame-perfect replay
var replayConfig = new TimeControllerConfig
{
    Role = TimeRole.Slave,
    Mode = TimeMode.Deterministic,
    LocalNodeId = 200,
    SyncConfig = new TimeConfig { FixedDeltaSeconds = 1.0f / 60.0f }
};
```

---

### 2. Tune PLL for Your Network

```csharp
// ❌ BAD: Default 2ms latency assumption for WAN deployment
var config = new TimeConfig
{
    AverageLatencyTicks = Stopwatch.Frequency * 2 / 1000 // 2ms (LAN)
};

// ✅ GOOD: Measure and configure for actual network latency
// Measure: ping target-host, RTT=50ms → Latency ~25ms one-way
var wanConfig = new TimeConfig
{
    AverageLatencyTicks = Stopwatch.Frequency * 25 / 1000, // 25ms WAN
    PLLGain = 0.05,                                        // Lower gain for stability
    SnapThresholdMs = 1000.0,                              // Higher snap threshold
    PauseBarrierFrames = 30                                // Longer lookahead
};
```

---

### 3. Handle Mode Switching Gracefully

```csharp
// ✅ GOOD: Always use DistributedTimeCoordinator for Master
var coordinator = new DistributedTimeCoordinator(eventBus, kernel, config, slaveIds);

coordinator.Update(); // Poll every frame

// Avoid direct SwapTimeController calls - use coordinator methods
coordinator.SwitchToDeterministic(slaveIds); // Handles barrier logic
coordinator.SwitchToContinuous();             // Handles immediate swap
```

---

### 4. Validate Lockstep Configuration

```csharp
// ❌ BAD: Missing AllNodeIds for Master
var masterConfig = new TimeControllerConfig
{
    Role = TimeRole.Master,
    Mode = TimeMode.Deterministic
    // Missing AllNodeIds! Factory will throw exception
};

// ✅ GOOD: Provide Slave IDs for lockstep coordination
var validConfig = new TimeControllerConfig
{
    Role = TimeRole.Master,
    Mode = TimeMode.Deterministic,
    AllNodeIds = new HashSet<int> { 200, 300, 400 }
};
```

---

### 5. Use Manual Stepping for Reproducible Tests

```csharp
// ✅ GOOD: Manual stepping ensures deterministic test execution
[Test]
public void TestPhysicsCollision()
{
    var controller = new SteppingTimeController(GlobalTime.Zero);
    
    // Step exactly 10 frames
    for (int i = 0; i < 10; i++)
    {
        GlobalTime time = controller.Step(1.0f / 60.0f);
        kernel.Tick(time.DeltaTime);
    }
    
    Assert.AreEqual(10, controller.GetCurrentState().FrameNumber);
    Assert.AreEqual(0.16667f, controller.GetCurrentState().TotalTime, 0.001f);
}
```

---

### 6. Monitor Synchronization Health

```csharp
// ✅ GOOD: Log sync errors for diagnostics
var slave = new SlaveTimeController(eventBus, config);

// Monitor error in OnTimePulseReceived (add logging):
// if (Math.Abs(errorMs) > 100.0)
//     Log.Warn($"High sync error: {errorMs:F2}ms");

// Count hard snaps (indicates network issues):
// if (didSnap)
//     _snapCount++;
```

---

## Design Principles

### 1. Transport Independence

**Principle**: Time coordination logic decoupled from network transport.

**Implementation**:
- Uses FdpEventBus abstraction for message passing
- Application layer provides DDS/TCP/UDP transport
- Same code works for local simulation, networked training, and replay

**Benefit**: Can swap DDS for WebSockets without changing time controllers.

---

### 2. Phase-Locked Loop for Smooth Sync

**Principle**: Gradual error correction avoids visual jitter.

**Rationale**:
- Hard snapping to Master time causes visual "pops"
- Network jitter creates transient errors
- PLL smooths out corrections over multiple frames

**Implementation**: Median filter + proportional correction + slew limiting.

---

### 3. Future Barrier for Mode Switching

**Principle**: Plan distributed state changes at future time.

**Rationale**:
- Network latency varies between nodes
- Immediate switch causes desynchronization
- Future barrier ensures atomic transition

**Implementation**: Broadcast BarrierFrame, all nodes wait, simultaneous swap.

---

### 4. Deterministic Lockstep

**Principle**: Frame-perfect synchronization for replay.

**Rationale**:
- Replay correctness requires identical frame sequence
- Variable frame rates cause divergence
- Lockstep guarantees all nodes execute Frame N before any proceeds to N+1

**Implementation**: FrameOrder/FrameAck protocol with explicit waiting.

---

### 5. Configurable Synchronization

**Principle**: Expose tuning parameters for different scenarios.

**Rationale**:
- LAN vs WAN have different latencies
- Training vs replay have different requirements
- Visual smoothness vs responsiveness trade-off

**Implementation**: TimeConfig with PLLGain, SnapThreshold, BarrierFrames, etc.

---

### 6. Separation of Concerns

**Principle**: Time controllers focus on time, coordinators handle mode switching.

**Rationale**:
- MasterTimeController: Time measurement + pulse publishing
- DistributedTimeCoordinator: Mode switching logic
- Clear responsibilities → maintainability

**Implementation**: ITimeController interface + separate coordinator classes.

---

## Relationships to Other Projects

### Depends On

| Project | Relationship | Integration Points |
|---------|--------------|-------------------|
| **Fdp.Kernel** | Foundation | FdpEventBus for messaging, GlobalTime struct, EventId attribute |
| **ModuleHost.Core** | Module System | ITimeController interface, TimeMode/TimeRole enums, ModuleHostKernel |
| **FDP.Interfaces** | Compatibility | Minimal (mostly unused) |

### Depended Upon By

| Project | Usage | Integration Points |
|---------|-------|-------------------|
| **ModuleHost.Network.Cyclone** | Transport | Serializes/deserializes time messages for DDS |
| **Fdp.Examples.NetworkDemo** | Demo App | Uses DistributedTimeCoordinator for pause/unpause |
| **Fdp.Examples.CarKinem** | Demo App | Uses SlaveTimeController for multi-node rendering |

### Collaboration Patterns

#### With ModuleHost.Core

**Division of Responsibility**:
```
FDP.Toolkit.Time (Algorithms)
  - PLL implementation
  - Lockstep protocol
  - Mode switching logic
  - Message definitions

ModuleHost.Core (Infrastructure)
  - ITimeController interface
  - ModuleHostKernel.SwapTimeController()
  - TimeMode/TimeRole enums
  - GlobalTime struct definition
```

**Data Flow**:
```
Toolkit → Creates ITimeController implementations
Kernel → Invokes ITimeController.Update()
Kernel → Stores GlobalTime in CurrentTime property
Modules → Read Kernel.CurrentTime for scheduling
```

---

## API Reference

### Core Interfaces

```csharp
// ModuleHost.Core.Time.ITimeController
public interface ITimeController
{
    GlobalTime Update();                  // Advance time, return current state
    void SetTimeScale(float scale);       // Change time dilation (0.0 = pause, 1.0 = normal)
    float GetTimeScale();                 // Get current time scale
    TimeMode GetMode();                   // Get current mode (Continuous/Deterministic)
    GlobalTime GetCurrentState();         // Query state without advancing
    void SeedState(GlobalTime state);     // Initialize from saved state
    void Dispose();                       // Cleanup resources
}

// ModuleHost.Core.Time.ISteppableTimeController
public interface ISteppableTimeController : ITimeController
{
    GlobalTime Step(float fixedDeltaTime); // Manual frame advance
}
```

### Time Controllers

```csharp
// Continuous Mode Master
public class MasterTimeController : ITimeController
{
    public MasterTimeController(FdpEventBus eventBus, TimeConfig? config = null);
}

// Continuous Mode Slave
public class SlaveTimeController : ITimeController
{
    public SlaveTimeController(FdpEventBus eventBus, TimeConfig? config = null);
    internal SlaveTimeController(FdpEventBus eventBus, TimeConfig? config, Func<long>? tickSource);
}

// Deterministic Mode Master
public class SteppedMasterController : ISteppableTimeController
{
    public SteppedMasterController(FdpEventBus eventBus, HashSet<int> nodeIds, TimeConfig config);
    public SteppedMasterController(FdpEventBus eventBus, HashSet<int> nodeIds, TimeControllerConfig configWrapper);
}

// Deterministic Mode Slave
public class SteppedSlaveController : ITimeController
{
    public SteppedSlaveController(FdpEventBus eventBus, int localNodeId, float fixedDeltaSeconds);
}

// Manual Stepping
public class SteppingTimeController : ISteppableTimeController
{
    public SteppingTimeController(GlobalTime seedState);
}
```

### Coordinators

```csharp
// Master mode switching
public class DistributedTimeCoordinator
{
    public DistributedTimeCoordinator(FdpEventBus eventBus, ModuleHostKernel kernel, 
                                      TimeControllerConfig config, HashSet<int> slaveNodeIds);
    public void SwitchToDeterministic(HashSet<int> slaveNodeIds);
    public void SwitchToContinuous();
    public void Update(); // Poll for barrier frame
}

// Slave mode switching
public class SlaveTimeModeListener
{
    public SlaveTimeModeListener(FdpEventBus eventBus, ModuleHostKernel kernel, 
                                 TimeControllerConfig config);
    public void Update(); // Poll for switch events
}
```

### Factory

```csharp
public static class TimeControllerFactory
{
    public static ITimeController Create(FdpEventBus eventBus, TimeControllerConfig config);
}
```

### Messages

```csharp
[EventId(100)]
public struct TimePulseDescriptor
{
    public long MasterWallTicks;
    public double SimTimeSnapshot;
    public float TimeScale;
    public long SequenceId;
}

[EventId(101)]
public struct FrameOrderDescriptor
{
    public long FrameID;
    public float FixedDelta;
    public long SequenceID;
}

[EventId(102)]
public struct FrameAckDescriptor
{
    public long FrameID;
    public int NodeID;
    public int Checksum;
}

[EventId(103)]
public struct SwitchTimeModeEvent
{
    public TimeMode TargetMode;
    public long FrameNumber;
    public double TotalTime;
    public float FixedDeltaSeconds;
    public long BarrierFrame; // 0 = immediate
}
```

---

## Testing

### Test Scenarios

1. **PLL Convergence Tests**
   - Verify slave converges to master time within N frames
   - Validate jitter filter robustness to outliers
   - Test snap threshold triggers correctly

2. **Lockstep Synchronization Tests**
   - Verify all slaves execute Frame N before master proceeds
   - Test ACK timeout handling
   - Validate frame sequence determinism

3. **Mode Switching Tests**
   - Jitter-free pause (future barrier)
   - Immediate unpause
   - Emergency snap when latency exceeds lookahead

4. **Time Scale Tests**
   - Slow-motion (scale=0.5)
   - Fast-forward (scale=2.0)
   - Pause (scale=0.0)

### Test Harness

```csharp
// Inject custom tick source for deterministic testing
var tickSource = new MockTickSource();

var slave = new SlaveTimeController(
    eventBus,
    config,
    tickSource: () => tickSource.CurrentTicks
);

// Simulate time advancement
tickSource.Advance(Stopwatch.Frequency / 60); // 16.67ms
GlobalTime time = slave.Update();

Assert.AreEqual(expected, time.TotalTime, 0.001);
```

---

## Performance Considerations

### Continuous Mode Performance

- **TimePulse Rate**: 1 Hz → minimal network traffic
- **PLL Overhead**: ~10 μs per frame (median filter + arithmetic)
- **Memory**: ~200 bytes per slave controller (circular buffer)

### Deterministic Mode Performance

- **Locked Framerate**: Determined by slowest node + network RTT
- **Message Rate**: 2N messages per frame (N Orders + N ACKs)
- **Typical Latency**: 1-2ms LAN, 50-100ms WAN
- **Scalability**: Practical limit ~10 nodes (ACK storm)

### Optimization Recommendations

1. **Batch ACKs**: Aggregate multiple ACKs into single message (future enhancement)
2. **Adaptive Timeout**: Increase LockstepTimeoutMs for slow nodes
3. **PLL Tuning**: Lower PLLGain if visual smoothness more important than responsiveness
4. **BarrierFrame Tuning**: Increase for WAN, decrease for LAN

---

## Known Issues & Limitations

### 1. Lockstep Scalability

**Issue**: O(N) ACK messages per frame limits scalability.

**Impact**: >10 nodes → network congestion, increased latency.

**Workaround**: Use hierarchical lockstep (regional masters).

**Status**: Design limitation, requires protocol redesign.

---

### 2. PLL Does Not Compensate for Clock Drift

**Issue**: Hardware clock drift (ppm) not measured or corrected.

**Impact**: Long-running simulations (>1 hour) may accumulate error.

**Workaround**: Periodic hard snap via SnapThreshold.

**Status**: Acceptable for typical training scenarios (<2 hours).

---

### 3. No Dynamic Node Join/Leave

**Issue**: AllNodeIds fixed at startup, no runtime add/remove.

**Impact**: Cannot add/remove nodes during lockstep simulation.

**Workaround**: Restart simulation with new configuration.

**Status**: Known limitation, requires membership protocol.

---

### 4. Hard-Coded TimePulse Rate

**Issue**: 1 Hz pulse rate not configurable.

**Impact**: Cannot tune for very high latency networks (>1s).

**Workaround**: Modify PulseIntervalTicks constant.

**Status**: Low priority (1 Hz sufficient for typical scenarios).

---

## References

### Related Documentation

- [ModuleHost.Core.md](../modulehost/ModuleHost.Core.md) - ITimeController interface
- [Fdp.Kernel.md](../core/Fdp.Kernel.md) - FdpEventBus and GlobalTime
- [Fdp.Examples.NetworkDemo.md](../examples/Fdp.Examples.NetworkDemo.md) - Usage in multi-node demo

### External Resources

- **Phase-Locked Loop**: Wikipedia - PLL Theory
- **Lockstep Protocol**: Gaffer On Games - Deterministic Lockstep
- **Network Time Protocol**: RFC 5905 (inspiration for PLL)

### Design Documents

- `Docs/architecture/time-synchronization.md` (if exists)
- `ModuleHost/docs/time-coordination.md` (if exists)

### Code Examples

- `Examples/Fdp.Examples.NetworkDemo/` - Multi-node time sync
- `Examples/Fdp.Examples.CarKinem/` - Master/Slave with mode switching

---

## Conclusion

FDP.Toolkit.Time provides robust, flexible distributed time synchronization for multi-node FDP simulations. Its dual-mode architecture supports both real-time continuous operation (using Phase-Locked Loop) and deterministic lockstep execution (using frame-order protocol), with jitter-free mode switching via future barriers.

**Key Strengths**:
- Smooth, jitter-resistant synchronization for visual applications
- Frame-perfect determinism for replay and verification
- Transport-agnostic design (works with DDS, TCP, UDP)
- Configurable tuning for LAN/WAN deployments
- Separation of concerns (time measurement vs. mode switching)

**Recommended Use Cases**:
- Multi-node training simulators (aircraft, vehicles)
- Distributed deterministic replay
- Federated simulations with pause/resume coordination
- Interactive debugging with manual stepping

**Next Steps**:
1. Configure TimeControllerConfig for your deployment (Master/Slave, LAN/WAN)
2. Integrate with network transport (DDS, TCP) via EventBus
3. Tune PLL parameters based on measured network latency
4. Use DistributedTimeCoordinator for coordinated mode switching
5. Monitor synchronization health in production

**Total Lines**: 1843
