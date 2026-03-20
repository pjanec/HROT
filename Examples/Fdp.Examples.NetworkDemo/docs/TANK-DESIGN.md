# FDP Engine Distributed Recording and Playback - Design Document

## 1. Executive Summary

This design extends the FDP Engine framework to support **Distributed Recording and Playback** capabilities, demonstrating the full potential of the engine's architecture through a comprehensive network demo. The system enables multiple nodes to record only their owned entity components, then replay them while simultaneously receiving remote data from other nodes' replays, reconstructing the complete distributed simulation accurately.

### Key Capabilities Demonstrated

1. **Partial Component Ownership**: Single entities can have different components owned by different nodes
2. **Distributed Replay**: Each node replays only its owned data while receiving other nodes' data via network
3. **Geographic Coordinate Translation**: Seamless conversion between network geodetic coordinates (WGS84) and internal flat coordinates
4. **Deterministic Time Control**: Runtime switching between continuous time and lockstep deterministic mode
5. **Variable Playback Speed**: Independent control of replay speed including pause and single-step
6. **Zero Boilerplate Networking**: Attribute-based automatic descriptor registration

---

## 2. Architecture Overview

### 2.1 The "Composite Tank" Scenario

The demo showcases a distributed simulation where control of a single tank entity is split across multiple nodes:

**Node A (Driver):**
- Owns **EntityMaster** (Lifecycle authority)
- Owns **ChassisDescriptor** (Position/Rotation)
- Records to `node_a.fdp`

**Node B (Gunner):**
- Owns **TurretDescriptor** (Yaw/Pitch angles)
- Records to `node_b.fdp`

**Distributed Replay:**
- Node A loads `node_a.fdp`, injects chassis data, which triggers network egress
- Node B loads `node_b.fdp`, injects turret data, which triggers network egress
- Each node receives the other's replay stream via DDS as if it were live
- Result: Complete reconstruction of the original session

### 2.2 Data Flow Architecture

```
[APPLICATION LAYER]
  Input/Physics → DemoPosition (Internal) → [RECORDED]
                      ↓
              TransformSyncSystem
                      ↓
[TOOLKIT LAYER]
              NetworkPosition (Buffer) → [NOT RECORDED]
                      ↓
              GeoTranslator
                      ↓
[NETWORK LAYER]
              GeoStateDescriptor (DDS) → [NETWORK]
```

**Critical Design Principle:**
- We record **Internal Simulation State** (`DemoPosition`), not **Network State** (`NetworkPosition`)
- Replay injects internal state, letting normal engine logic generate network packets
- This proves "Replay is just another Input Source"

---

## 3. Core Components

### 3.1 Entity ID Management

**Problem:** Recording stores entities by their internal memory index. During replay, the same entity might exist at a different index.

**Solution: ID Partitioning with Safety Gap**

```
ID Range 0 - 65,535:     System Entities (Never Recorded)
ID Range 65,536+:        Simulation Entities (Recorded)
```

**Why 65,536?**
- FDP uses 64KB chunks for component storage
- Smallest component (1 byte) fits 65,536 entities per chunk
- This gap ensures System entities (Chunk 0) never overlap with Recorded entities (Chunk 1+)

**Implementation:**
1. After system init: `world.ReserveIdRange(65536)`
2. Recorder configured: `MinRecordableId = 65536`
3. First tank spawns at ID 65,536 (start of Chunk 1)

### 3.2 Component Recording Policy

Components are classified into two categories:

**Recorded (Internal State):**
- `DemoPosition` - The physics/logic position
- `TurretState` - Application-specific turret data
- `Health`, `Velocity`, etc. - All application components

**Not Recorded (Transient Buffers Only):**
- `NetworkPosition` - Buffer for network sync, marked `[DataPolicy(NoRecord)]`
- `NetworkVelocity` - Buffer for network sync
- `NetworkOrientation` - Buffer for network sync

**MUST Be Recorded (Critical for Replay):**
- `NetworkIdentity` - Required to map Shadow → Live entities and resolve network ghosts
- `NetworkAuthority` - Required to determine ownership during recording (fallback authority)
- `DescriptorOwnership` - Required to resolve partial ownership history

### 3.3 Ownership Model (SST Compliance)

**NetworkAuthority** (Per-Entity):
```csharp
struct NetworkAuthority {
    int PrimaryOwnerId;   // Default owner (Lifecycle authority)
    int LocalNodeId;       // This node's ID
}
```

**DescriptorOwnership** (Per-Descriptor):
```csharp
struct DescriptorOwnership {
    Dictionary<long, int> OwnershipMap; // PackedKey(OrdinalID, InstanceID) → NodeID
}
```

**Authority Resolution:**
1. Check `DescriptorOwnership` for specific descriptor
2. If not found, fallback to `PrimaryOwnerId` from `NetworkAuthority`

**Critical for Replay:**
The `ReplayBridgeSystem` must check `HasAuthority(entity, descriptorKey)` in the **Shadow World** before injecting data.

---

## 4. The Shadow World Pattern

### 4.1 Why Shadow World is Mandatory

**Problem with Direct Replay:**
- FDP's `PlaybackSystem` is **destructive** - it overwrites memory chunks completely
- Partial Ownership means one entity has mixed Owned/Remote components
- Direct playback would overwrite valid remote data with stale recorded data

**Example:**
```
Entity 10 (Live):
  Position: [From Replay - Valid]
  Health:   [From Network Node B - Valid]

Direct Playback loads Chunk containing Entity 10:
  Position: [Restored - Good]
  Health:   [Overwritten with old value - DATA LOSS]
```

**Shadow World Solution:**
1. Load recording into isolated `ShadowRepository`
2. `ReplayBridgeSystem` selectively copies components where we had authority
3. Live World maintains remote components from active network ingress
4. Result: Merge operation instead of overwrite

### 4.2 ID Mapping Strategy

**Identical IDs Approach:**
Since we use ID Partitioning with Safety Gap, we can maintain `ShadowID == LiveID` for recorded entities.

**Benefits:**
- No dictionary lookups
- Simpler bridge logic
- Deterministic entity references

**Implementation:**
1. Read `.meta` sidecar file for `MaxEntityId`
2. Call `world.ReserveIdRange(MaxEntityId)`
3. Load recording into Shadow World (IDs 65536-70000)
4. New live ghosts allocate from 70001+
5. Bridge uses direct index mapping

---

## 5. Geographic Coordinate Translation

### 5.1 The Requirement

**Network Protocol:** WGS84 Geodetic (Latitude, Longitude, Altitude in degrees/meters)
**Internal Engine:** Flat Cartesian (Vector3 in meters relative to floating origin)

**Why?**
- Demonstrates real-world interoperability (DIS/HLA standards use geodetic)
- Showcases complex translators vs simple zero-boilerplate translators
- Tests that recording internal state (flat) allows re-translation on replay

### 5.2 The Translation Pipeline

**Descriptors:**
```csharp
// Network representation
[DdsTopic("Tank_GeoState")]
struct GeoStateDescriptor {
    [DdsKey] long EntityId;
    double Latitude;
    double Longitude;
    double Altitude;
    float Heading;
}

// Internal representation
struct DemoPosition {
    Vector3 Value; // Flat meters from origin
}
```

**Translator (Manual):**
```csharp
class GeodeticTranslator : IDescriptorTranslator {
    IGeographicTransform _geoTransform; // WGS84Transform with Berlin origin
    
    // Egress: Flat → Geo
    void ScanAndPublish() {
        var flatPos = view.GetComponentRO<NetworkPosition>(entity);
        var (lat, lon, alt) = _geoTransform.ToGeodetic(flatPos.Value);
        writer.Write(new GeoStateDescriptor { Latitude = lat, ... });
    }
    
    // Ingress: Geo → Flat
    void PollIngress() {
        var geoData = reader.TakeSample();
        var flatPos = _geoTransform.ToCartesian(geoData.Latitude, geoData.Longitude, ...);
        cmd.SetComponent(entity, new NetworkPosition { Value = flatPos });
    }
}
```

**Replay Flow:**
1. Recording contains `DemoPosition` (flat Vector3)
2. Replay injects `DemoPosition` into live world
3. `TransformSyncSystem` copies to `NetworkPosition`
4. `GeodeticTranslator` converts to `GeoStateDescriptor`
5. Network receives correct geodetic coordinates

---

## 6. Time Synchronization

### 6.1 Time Modes

**Continuous Mode (Default):**
- **Master:** Uses wall clock, sends `TimePulse` events
- **Slave:** Uses PLL to track master's time
- Smooth interpolation, tolerates network jitter

**Deterministic Mode (On Demand):**
- **All Nodes:** Use `SteppedTimeController`
- Master sends `FrameOrder` with barrier sequence
- All nodes ACK, then advance exactly one tick
- Perfect synchronization for debugging

### 6.2 Runtime Switching

**The Distributed Barrier Protocol:**
1. Master detects user input: `Press('T')`
2. `DistributedTimeCoordinator.SwitchToDeterministic()` called
3. Master broadcasts `SwitchTimeModeEvent` with target barrier frame
4. All nodes continue until reaching barrier frame
5. All nodes ACK via `BarrierAck` message
6. Master waits for all ACKs
7. All nodes swap `MasterTimeController` → `SteppedMasterController`
8. Simulation continues in lockstep

**Benefits:**
- No simulation pause during transition
- Allows debugging complex multi-node race conditions
- Demonstrates engine's flexibility

### 6.3 Replay Time Control

In replay mode, time is controlled by the **Playback Head**, not the wall clock.

**PlaybackController:**
```csharp
class ReplayBridgeSystem {
    double _accumulator = 0.0;
    float _playbackSpeed = 1.0f;  // User-controllable
    bool _isPaused = false;
    
    void Execute(float dt) {
        _accumulator += dt * _playbackSpeed;
        
        while (_accumulator >= RECORDED_DELTA) {
            _controller.StepForward(_shadowRepo);  // Load next frame
            SyncShadowToLive(liveView);             // Inject owned components
            _accumulator -= RECORDED_DELTA;
        }
    }
}
```

**Features:**
- **Speed Control:** 0.5x, 1x, 2x, etc.
- **Pause:** `_playbackSpeed = 0`
- **Single-Step:** While paused, manually step one frame

---

## 7. Zero Boilerplate Networking

### 7.1 The Attribute System

Instead of writing custom translators for every data type, simple cases can use attributes:

```csharp
[FdpDescriptor(ordinal: 10, topicName: "Tank_Turret")]
[DdsTopic("Tank_Turret")]
struct TurretState {
    [DdsKey] long EntityId;
    float YawAngle;
    float PitchAngle;
    bool IsTargeting;
}
```

### 7.2 Generic Translator

```csharp
class GenericDescriptorTranslator<T> : IDescriptorTranslator where T : unmanaged {
    private readonly ISerializationProvider _serializer;
    
    void PollIngress(IDataReader reader, ...) {
        foreach (var sample in reader.TakeSamples()) {
            if (!_entityMap.TryGetEntity(sample.EntityId, out Entity entity))
                continue;
            
            var data = (T)sample.Data;
            
            // CHECK 1: IS IT A GHOST? (Accumulating)
            if (view.HasManagedComponent<BinaryGhostStore>(entity)) {
                var store = view.GetManagedComponent<BinaryGhostStore>(entity);
                long key = PackedKey.Create(DescriptorOrdinal, sample.InstanceId);
                // STASH DATA (Do not apply yet - waiting for mandatory descriptors)
                store.StashedData[key] = _serializer.Serialize(data);
            }
            // CHECK 2: IS IT ACTIVE? (Simulating)
            else {
                // Apply to entity or sub-entity
                Entity target = ResolveSubEntity(view, entity, sample.InstanceId);
                cmd.SetComponent(target, data);
            }
        }
    }
    
    void ScanAndPublish(ISimulationView view, IDataWriter writer) {
        var query = view.Query().With<T>().With<NetworkIdentity>().Build();
        foreach (var entity in query) {
            if (view.HasAuthority(entity, DescriptorOrdinal)) {
                var data = view.GetComponentRO<T>(entity);
                writer.Write(data);  // 1:1 mapping
            }
        }
    }
}
```

### 7.3 Assembly Scanning

```csharp
class ReplicationBootstrap {
    static List<IDescriptorTranslator> CreateAutoTranslators(Assembly assembly, ...) {
        foreach (var type in assembly.GetTypes()) {
            var attr = type.GetCustomAttribute<FdpDescriptorAttribute>();
            if (attr != null) {
                var translator = Activator.CreateInstance(
                    typeof(GenericDescriptorTranslator<>).MakeGenericType(type),
                    attr.Ordinal, attr.TopicName, entityMap
                );
                translators.Add(translator);
            }
        }
    }
}
```

**Usage:**
```csharp
// In Program.cs
replication.RegisterDescriptorsFromAssembly(typeof(Program).Assembly);
```

---

## 8. Advanced Demo Features

### 8.1 Radar Module (Async/SlowBackground Showcase)

Demonstrates `SlowBackground` execution policy with `Snapshot-on-Demand`.

**Purpose:** Scans for nearby entities at 1Hz instead of 60Hz, proving efficient async processing.

```csharp
[ExecutionPolicy(ExecutionMode.SlowBackground, priority: 1)]
[SnapshotPolicy(SnapshotMode.OnDemand)]
public class RadarModule : IEcsModuleSystem {
    public void Execute(ISimulationView view, float dt) {
        // Request snapshot of world state
        var snapshot = view.CaptureSnapshot();
        
        // Heavy computation in background
        var nearbyEntities = ScanRadar(snapshot);
        
        // Publish results asynchronously
        foreach (var target in nearbyEntities) {
            _eventBus.Publish(new RadarContactEvent { Target = target });
        }
    }
}
```

**Benefit:** Proves the engine can handle expensive operations without blocking main simulation thread.

### 8.2 Damage Control Module (Reactive/Event-Driven Showcase)

Demonstrates `WatchEvents` reactive scheduling.

**Purpose:** Only runs when damage events occur, proving zero overhead when idle.

```csharp
[ExecutionPolicy(ExecutionMode.Synchronous)]
[WatchEvents(typeof(DetonationEvent))]
public class DamageControlModule : IEcsModuleSystem {
    public void Execute(ISimulationView view, float dt) {
        // Only executes when DetonationEvent published
        var events = view.GetEvents<DetonationEvent>();
        
        foreach (var evt in events) {
            ApplyDamage(view, evt.Entity, evt.DamageAmount);
        }
    }
}
```

**Benefit:** Proves event-driven architecture eliminates unnecessary per-frame checks.

### 8.3 Dynamic Ownership Transfer

Demonstrates runtime authority transfer for partial ownership.

**Scenario:** Node B can request control of the Turret from Node A.

```csharp
public class OwnershipInputSystem : IEcsModuleSystem {
    public void Execute(ISimulationView view, float dt) {
        if (Input.GetKeyDown(KeyCode.O)) {
            var tank = FindTank(view);
            
            // Request turret authority if we don't have it
            if (!view.HasAuthority(tank, TURRET_DESCRIPTOR)) {
                var request = new OwnershipUpdateRequest {
                    EntityId = GetNetworkId(tank),
                    DescriptorOrdinal = TURRET_DESCRIPTOR,
                    InstanceId = 0,
                    NewOwner = _localNodeId
                };
                
                _eventBus.Publish(request);
                Console.WriteLine("[Ownership] Requesting turret control...");
            }
        }
    }
}
```

**Network Flow:**
1. Node B publishes `OwnershipUpdateRequest`
2. Master node validates and broadcasts `OwnershipUpdate`
3. Both nodes update `DescriptorOwnership` component
4. Node B starts publishing turret data
5. Node A stops publishing turret data (authority check fails)

---

## 9. Recording Metadata (Sidecar File)

### 8.1 The Metadata Structure

```csharp
[Serializable]
class RecordingMetadata {
    int MaxEntityId;      // Highest ID allocated during session
    DateTime Timestamp;   // Recording start time
    int NodeId;           // Which node recorded this
}
```

### 8.2 Saving (Live Mode)

```csharp
// At session end
recorder.Dispose();  // Flush binary data

var meta = new RecordingMetadata {
    MaxEntityId = world.MaxEntityIndex,
    Timestamp = DateTime.UtcNow,
    NodeId = nodeId
};

File.WriteAllText("node_1.fdp.meta", JsonSerializer.Serialize(meta));
```

### 8.3 Loading (Replay Mode)

```csharp
// At startup
var meta = JsonSerializer.Deserialize<RecordingMetadata>(
    File.ReadAllText("node_1.fdp.meta")
);

world.ReserveIdRange(meta.MaxEntityId);  // Critical!
```

---

## 9. System Architecture

### 9.1 TransformSyncSystem

Bridges Application Logic ↔ Network Toolkit

**Outbound (Owned):**
```csharp
if (view.HasAuthority(entity, chassisDescriptorKey)) {
    var appPos = view.GetComponentRO<DemoPosition>(entity);
    cmd.SetComponent(entity, new NetworkPosition { Value = appPos.Value });
}
```

**Inbound (Remote):**
```csharp
// Smooth interpolation from NetworkPosition → DemoPosition
var netPos = view.GetComponentRO<NetworkPosition>(entity);
var currentPos = view.GetComponentRO<DemoPosition>(entity);
var smoothed = Vector3.Lerp(currentPos.Value, netPos.Value, dt * SmoothingRate);
cmd.SetComponent(entity, new DemoPosition { Value = smoothed });
```

### 9.2 ReplayBridgeSystem

The core of distributed replay. Performs the **Merge Operation**.

**Pseudocode:**
```csharp
void Execute(ISimulationView liveView, float dt) {
    // 1. Advance shadow world
    _controller.StepForward(_shadowRepo);
    
    // 2. Iterate shadow entities
    var shadowQuery = _shadowRepo.Query()
        .With<NetworkIdentity>()
        .Build();
    
    foreach (var shadowEntity in shadowQuery) {
        // 3. Map to live entity (ShadowID == LiveID in our design)
        Entity liveEntity = shadowEntity;
        
        if (!liveView.IsAlive(liveEntity)) continue;
        
        // 4. Granular injection based on authority
        // Check Chassis
        if (_shadowRepo.HasAuthority(shadowEntity, CHASSIS_KEY)) {
            var pos = _shadowRepo.GetComponentRO<DemoPosition>(shadowEntity);
            cmd.SetComponent(liveEntity, pos);
        }
        
        // Check Turret
        if (_shadowRepo.HasAuthority(shadowEntity, TURRET_KEY)) {
            var turret = _shadowRepo.GetComponentRO<TurretState>(shadowEntity);
            cmd.SetComponent(liveEntity, turret);
        }
    }
}
```

### 9.3 TimeInputSystem (Live Only)

```csharp
void Execute(ISimulationView view, float dt) {
    if (Input.GetKeyDown(KeyCode.T)) {
        if (currentMode == TimeMode.Continuous) {
            coordinator.SwitchToDeterministic();
        } else {
            coordinator.SwitchToContinuous();
        }
    }
    
    if (Input.GetKeyDown(KeyCode.UpArrow)) {
        timeController.SetTimeScale(timeScale + 0.5f);
    }
    
    // Manual stepping in deterministic mode
    if (currentMode == Deterministic && timeScale == 0) {
        if (Input.GetKeyDown(KeyCode.RightArrow)) {
            kernel.StepFrame(0.0166f);
        }
    }
}
```

---

## 10. Demo Walkthrough

### 10.1 Live Session

**Node A (Driver):**
1. Spawns Tank at (52.52°N, 13.40°E) - Berlin
2. WASD controls drive the tank
3. Physics updates `DemoPosition` (flat coordinates relative to origin)
4. `TransformSyncSystem` copies to `NetworkPosition`
5. `GeodeticTranslator` converts to Lat/Lon
6. DDS publishes `GeoStateDescriptor` to Node B
7. `RecorderSystem` saves `DemoPosition` to `node_a.fdp`

**Node B (Gunner):**
1. Receives `GeoStateDescriptor` from DDS
2. `GeodeticTranslator` converts to flat `NetworkPosition`
3. `TransformSyncSystem` smooths to `DemoPosition` (remote)
4. Mouse controls aim the turret
5. Updates `TurretState` (owned)
6. `GenericDescriptorTranslator` publishes `TurretState` to Node A
7. `RecorderSystem` saves `TurretState` to `node_b.fdp`

### 10.2 Replay Session

**Node A (Replay Driver):**
1. Reads `node_a.fdp.meta` → Reserves IDs
2. Loads `node_a.fdp` into Shadow World
3. `ReplayBridgeSystem` runs:
   - Reads `DemoPosition` from Shadow
   - Checks authority (YES for Chassis)
   - Injects `DemoPosition` into Live World
   - **Copies `NetworkIdentity`, `NetworkAuthority` from Shadow to Live (first encounter)**
4. **`world.Tick()` called to advance version (critical for change detection)**
5. `TransformSyncSystem` copies to `NetworkPosition`
6. `GeodeticTranslator` publishes to DDS
6. **Node B's replay stream arrives via DDS**
7. `GeodeticTranslator` ingests `TurretState` (remote)
8. Renderer shows complete tank: Chassis from disk, Turret from network

**Node B (Replay Gunner):**
- Mirror process: Injects `TurretState`, receives `GeoStateDescriptor` from Node A

**Result:**
Exact reconstruction of the original session, with each node contributing its owned data via network replay.

---

## 11. Implementation Phases

### Phase 1: Kernel Foundation
- Add `EntityIndex.ReserveIdRange()`
- Add `EntityRepository.HydrateEntity()`
- Add `FdpConfig.SYSTEM_ID_RANGE = 65536`
- Update `RecorderSystem` to respect `MinRecordableId`

### Phase 2: Replication Toolkit
- Add `[DataPolicy(NoRecord)]` to all toolkit components
- Implement `GenericDescriptorTranslator<T>`
- Implement `ReplicationBootstrap.CreateAutoTranslators()`
- Create `FdpDescriptorAttribute`

### Phase 3: Network Demo - Components
- Define `DemoPosition` (Internal, Recorded)
- Define `GeoStateDescriptor` (Network, DDS)
- Define `TurretState` with `[FdpDescriptor]`
- Ensure `NetworkPosition` is buffer only

### Phase 4: Network Demo - Translators
- Implement `GeodeticTranslator` (Manual, Complex)
- Configure `GenericDescriptorTranslator` for Turret (Auto)
- Integrate with `CycloneNetworkModule`

### Phase 5: Network Demo - Systems
- Implement `TransformSyncSystem`
- Implement `ReplayBridgeSystem`
- Implement `TimeInputSystem`
- Implement Metadata save/load in `Program.cs`

### Phase 6: Testing & Polish
- Test Live Session (2 nodes)
- Test Distributed Replay (2 nodes)
- Test Time Mode Switching
- Test Variable Playback Speed
- Test Geographic Translation Accuracy

---

## 12. Technical Constraints

### 12.1 Memory Layout
- FDP uses 64KB chunks
- Component-specific chunk capacity varies by component size
- Safety Gap must be ≥ max possible chunk capacity (65,536)

### 12.2 ID Collision Prevention
- System entities: 0 - 65,535
- Simulation entities: 65,536+
- `RecorderSystem.MinRecordableId = 65536`
- Replay must reserve range before any entity creation

### 12.3 Ownership Verification
- Always check `view.HasAuthority(entity, descriptorKey)` before injection
- Use Shadow World authority, not Live World
- Prevents replaying components we didn't own

### 12.4 Network Identity Mapping
- Network IDs (long) are separate from Internal IDs (int)
- Maintain `NetworkEntityMap` for resolution
- Ghost entities created by network ingress get IDs above recorded range

---

## 13. Success Criteria

The implementation is complete when:

1. **Live Session:**
   - Node A drives Tank, Node B aims Turret
   - Both nodes see synchronized composite tank
   - Each node records only owned components

2. **Replay Session:**
   - Each node loads own recording
   - Replay data triggers network egress
   - Remote node receives replay stream
   - Complete tank reconstructed from distributed sources

3. **Time Control:**
   - Can switch Live → Deterministic at runtime
   - Can pause and single-step in Deterministic mode
   - Replay supports variable speed (0.5x - 4x)

4. **Geographic Translation:**
   - Network protocol uses WGS84
   - Internal engine uses flat coordinates
   - Translation is seamless and accurate

5. **Code Quality:**
   - Manual translator demonstrates complexity handling
   - Auto translator demonstrates zero boilerplate
   - Clean separation: Internal State (Recorded) vs Network State (Buffer)

---

## 14. Future Extensions

### 14.1 Hierarchical Entities
- Sub-entities for multi-instance descriptors (Machine Guns)
- Automatic child spawning via `ChildBlueprintDefinition`
- Partial ownership extends to sub-entities

### 14.2 Replay Analysis
- Divergence detection (compare replay to live)
- Performance profiling during replay
- Network bandwidth analysis

### 14.3 Advanced Time
- Client-side prediction with server reconciliation
- Input delay compensation
- Jitter buffer visualization

---

## Appendix A: Key Data Structures

```csharp
// Kernel
const int SYSTEM_ID_RANGE = 65536;

// Components
[DataPolicy(NoRecord)]
struct NetworkPosition { Vector3 Value; }

struct DemoPosition { Vector3 Value; }  // Recorded

[FdpDescriptor(10, "Tank_Turret")]
struct TurretState { 
    long EntityId; 
    float YawAngle; 
    float PitchAngle; 
}

// Network Descriptors
[DdsTopic("Tank_GeoState")]
struct GeoStateDescriptor {
    [DdsKey] long EntityId;
    double Latitude;
    double Longitude;
    double Altitude;
}

// Metadata
class RecordingMetadata {
    int MaxEntityId;
    DateTime Timestamp;
    int NodeId;
}
```

## Appendix B: Configuration Constants

```csharp
// Recording
const int MIN_RECORDABLE_ID = 65536;
const float RECORDED_DELTA = 1.0f / 60.0f;  // 60Hz

// Network
const int CHASSIS_DESCRIPTOR = 5;
const int TURRET_DESCRIPTOR = 10;
const int WEAPON_DESCRIPTOR = 11;

// Geographic
const double ORIGIN_LAT = 52.5200;  // Berlin
const double ORIGIN_LON = 13.4050;
const double ORIGIN_ALT = 0.0;
```
