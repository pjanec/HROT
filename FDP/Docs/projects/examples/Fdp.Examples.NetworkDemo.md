# Fdp.Examples.NetworkDemo

## Overview

**Fdp.Examples.NetworkDemo** is a comprehensive reference implementation demonstrating distributed simulation capabilities across the entire FDP technology stack. It showcases multi-node networking via Cyclone DDS, entity replication with ownership management, distributed time synchronization (both PLL and lockstep modes), deterministic recording/replay, Type-Knowledge Base (TKB) entity templates, and combat simulation with real-time interaction events. This example serves as the primary integration test and architectural reference for production distributed simulations.

**Key Demonstrations**:
- **Multi-Node Networking**: Two independent nodes (ID 100 "Alpha", ID 200 "Bravo") communicate via DDS
- **Entity Replication**: Automatic ghost entity creation, smart bandwidth optimization, ownership transfer
- **Time Synchronization**: Master/slave distributed time coordination with mode switching (PLL ↔ Lockstep)
- **Recording/Replay**: Deterministic flight recorder with frame-perfect replay capability
- **TKB Templates**: Tank entities with hierarchical child components (chassis + turret)
- **Translator Pattern**: Custom DDS topic serialization for geographic state, fire events, ownership updates
- **Combat System**: Tank movement, turret control, firing interactions, health/damage simulation
- **Geographic Integration**: WGS84/ENU coordinate transforms for real-world positioning

**Line Count**: 51+ C# implementation files across Components, Systems, Modules, Translators, Configuration

**Primary Dependencies**: Fdp.Kernel, ModuleHost.Core, ModuleHost.Network.Cyclone, FDP.Toolkit.Replication, FDP.Toolkit.Time, FDP.Toolkit.Tkb, FDP.Toolkit.Lifecycle, Fdp.Toolkit.Geographic, CycloneDDS.Runtime

**Use Cases**: Reference architecture for distributed simulations, integration testing, performance benchmarking, training material for FDP developers

---

## Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│               NetworkDemo Multi-Node Architecture                    │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  Node 100 "Alpha" (Master)           Node 200 "Bravo" (Slave)       │
│  ┌──────────────────────┐            ┌──────────────────────┐       │
│  │  EntityRepository    │            │  EntityRepository    │       │
│  │  - Local Entities    │            │  - Local Entities    │       │
│  │  - Ghost Entities    │◄───DDS────►│  - Ghost Entities    │       │
│  └──────────────────────┘            └──────────────────────┘       │
│           │                                    │                     │
│  ┌────────▼──────────────────────────────┐   │                     │
│  │  ModuleHost Kernel (Master Time)      │   │                     │
│  │  - ReplicationLogicModule             │   │                     │
│  │  - EntityLifecycleModule              │   │                     │
│  │  - Time: MasterTimeController         │   │                     │
│  │  - CycloneNetworkModule               │   │                     │
│  │  - GameLogicModule (Physics, Combat)  │   │                     │
│  │  - GeographicModule (WGS84/ENU)       │   │                     │
│  │  - AsyncRecorder (Recording)          │   │                     │
│  └───────────────────────────────────────┘   │                     │
│           │                                    │                     │
│           │   DDS Topics (Cyclone DDS)        │                     │
│           ├────────────────────────────────────┤                     │
│           │  - NetworkPosition (Chassis)      │                     │
│           │  - GeoStateDescriptor (GPS)       │                     │
│           │  - WeaponStateTopic (Turret)      │                     │
│           │  - FireEvent (Interactions)       │                     │
│           │  - TimePulse (Synchronization)    │                     │
│           │  - OwnershipUpdate (Transfers)    │                     │
│           └────────────────────────────────────┘                     │
│                                                                       │
│  Entity Template (TKB):                                              │
│  ┌──────────────────────────────────────────────────────────────┐   │
│  │  CommandTank (TkbType 100)                                    │   │
│  │  - NetworkIdentity, NetworkOwnership                          │   │
│  │  - NetworkPosition, NetworkVelocity (Chassis)                 │   │
│  │  - Health, DemoPosition                                       │   │
│  │  - ChildBlueprints: [TankTurret (101)]                        │   │
│  │    └─> TurretState, WeaponState                               │   │
│  │  - MandatoryDescriptors:                                      │   │
│  │    - Chassis (Hard): Must arrive before spawn                 │   │
│  │    - Turret (Soft): Spawn after 60 frame timeout             │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                       │
│  Recording/Replay Flow:                                              │
│  ┌──────────────┐    ┌─────────────┐    ┌──────────────┐           │
│  │ AsyncRecorder│───>│ .fdp file   │───>│ReplayBridge  │           │
│  │ (Live mode)  │    │ (Binary log)│    │System (Replay│           │
│  └──────────────┘    │ + Metadata  │    │mode)         │           │
│                      └─────────────┘    └──────────────┘           │
│                                                                       │
└───────────────────────────────────────────────────────────────────────┘
```

### Execution Flow

```
Application Lifecycle:
  1. Parse Arguments: node_id (100/200), mode (live/replay), recording_path
  2. Initialize World & Kernel
  3. Setup Network (DDS Participant, NodeIdMapper)
  4. Register TKB Templates (TankTemplate)
  5. Register Translators (FastGeodeticTranslator, FireEventTranslator, etc.)
  6. Register Modules:
     - EntityLifecycleModule (spawn/despawn)
     - ReplicationLogicModule (ghost creation, smart egress)
     - CycloneNetworkModule (DDS integration)
     - GameLogicModule (physics, combat)
     - GeographicModule (coordinate transforms)
     - BridgeModule, RadarModule, DamageControlModule
  7. Start AsyncRecorder (if live mode)
  8. Spawn Initial Entities (if autoSpawn)
  9. Enter Run Loop (60 Hz tick rate)

Per-Frame Execution (SystemPhase order):
  [PreSimulation]
    - TimeInputSystem: Handle time mode switching input
    - RefactoredPlayerInputSystem: Read keyboard/mouse input
    - OwnershipInputSystem: Request ownership transfers
    - TimeSyncSystem: Synchronize distributed clocks (PLL/Lockstep)

  [Simulation]
    - PhysicsSystem: Integrate velocity → position
    - RadarSystem: Detect nearby entities
    - CombatInputSystem: Process fire commands
    - ChatSystem: Broadcast squad chat messages

  [PostSimulation]
    - TransformSyncSystem: Update geographic positions
    - CombatFeedbackSystem: Apply damage, check health
    - GhostCreationSystem: Create ghosts for new remote entities
    - SmartEgressSystem: Send updates for changed components
    - RecorderTickSystem: Log frame to .fdp file

  [NetworkEgress]
    - PacketBridgeSystem: Flush DDS topic writes
    - (Translators serialize components → DDS samples)

Shutdown:
  - Flush AsyncRecorder
  - Write Metadata (.fdp.meta.json)
  - Dispose DDS Participant
```

---

## Core Components

### DemoPosition (Components/DemoPosition.cs)

Local simulation position (separate from NetworkPosition for clarity):

```csharp
public struct DemoPosition
{
    public Vector2 Value; // Local Cartesian coordinates (meters)
}
```

### NetworkPosition (Replication Component)

Replicated position component (from FDP.Toolkit.Replication):

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct NetworkPosition
{
    public Vector2 Value;  // Cartesian position replicated across network
}
```

### NetworkVelocity (Replication Component)

Replicated velocity for physics synchronization:

```csharp
public struct NetworkVelocity
{
    public Vector2 Value;  // Velocity vector (m/s)
}
```

### TurretState (Components/TurretState.cs)

Turret aiming and orientation:

```csharp
public struct TurretState
{
    public float AzimuthRad;    // Turret rotation relative to chassis (radians)
    public float ElevationRad;  // Gun elevation angle (radians)
}
```

### WeaponState (Components/WeaponState.cs)

Weapon status and ammunition:

```csharp
public struct WeaponState
{
    public int AmmoCount;           // Remaining rounds
    public float ReloadTimeLeft;    // Seconds until next fire
    public byte IsFiring;           // 1 = trigger pulled this frame
}
```

### Health (Components/Health.cs)

Damage tracking:

```csharp
public struct Health
{
    public float Value;      // Current health (0 = destroyed)
    public float MaxValue;   // Maximum health capacity
}
```

### SquadChat (Components/SquadChat.cs)

Managed component for text messaging (network-replicated):

```csharp
public class SquadChat
{
    public string Message { get; set; } = string.Empty;
    public long Timestamp { get; set; }
}
```

---

## TKB Template System

### TankTemplate (Configuration/TankTemplate.cs)

Hierarchical entity template demonstrating parent-child relationships:

```csharp
public static class TankTemplate
{
    public static void Register(ITkbDatabase tkb)
    {
        // Parent: Tank chassis
        var tank = new TkbTemplate("CommandTank", 100);
        tank.AddComponent(new DemoPosition());
        tank.AddComponent(new Health { Value = 100, MaxValue = 100 });
        tank.AddComponent(new NetworkIdentity());
        tank.AddComponent(new NetworkPosition());
        tank.AddComponent(new NetworkVelocity());
        tank.AddComponent(new NetworkOwnership());
        
        // Child: Turret (Instance 1, Type 101)
        tank.ChildBlueprints.Add(new ChildBlueprintDefinition 
        { 
            InstanceId = 1, 
            ChildTkbType = 101 
        });

        // Mandatory Descriptors (replication readiness criteria)
        tank.MandatoryDescriptors.Add(new MandatoryDescriptor {
            PackedKey = PackedKey.Create(5, 0), // Chassis descriptor
            IsHard = true  // HARD: Must arrive before ghost→entity promotion
        });
        
        tank.MandatoryDescriptors.Add(new MandatoryDescriptor {
            PackedKey = PackedKey.Create(10, 0), // Turret descriptor
            IsHard = false,  // SOFT: Spawn after timeout if missing
            SoftTimeoutFrames = 60  // 1 second at 60 Hz
        });
        
        tkb.Register(tank);

        // Child: Turret template
        var turret = new TkbTemplate("TankTurret", 101);
        turret.AddComponent(new TurretState());
        turret.AddComponent(new WeaponState());
        tkb.Register(turret);
    }
}
```

**Mandatory Descriptors**:
- **Hard Requirement**: Ghost remains in "GhostPhase.WaitingMandatory" until descriptor arrives
- **Soft Requirement**: Ghost promotes to entity after timeout, even if descriptor missing
- **Use Case**: Chassis must arrive (hard), turret can be delayed (soft)

---

## Network Translators

### FastGeodeticTranslator (Translators/FastGeodeticTranslator.cs)

Bidirectional translation between ENU local coordinates and WGS84 geodetic DDS messages:

```csharp
public class FastGeodeticTranslator : IDescriptorTranslator
{
    private readonly DdsParticipant _participant;
    private readonly IGeographicTransform _geo;
    private readonly NetworkEntityMap _entityMap;
    private DdsWriter<GeoStateDescriptor>? _writer;
    private DdsReader<GeoStateDescriptor>? _reader;
    
    public void Initialize(ISimulationView view)
    {
        _writer = _participant.CreateWriter<GeoStateDescriptor>("GeoState");
        _reader = _participant.CreateReader<GeoStateDescriptor>("GeoState", OnGeoStateReceived);
    }
    
    public void PublishUpdates(ISimulationView view, PackedKey key, IReadOnlyList<Entity> entities)
    {
        foreach (var entity in entities)
        {
            var pos = view.GetComponentRO<NetworkPosition>(entity);
            var netId = view.GetComponentRO<NetworkIdentity>(entity);
            
            // Convert local ENU to geodetic
            Vector3 pos3D = new Vector3(pos.Value.X, pos.Value.Y, 0);
            var (lat, lon, alt) = _geo.ToGeodetic(pos3D);
            
            // Publish DDS sample
            _writer.Write(new GeoStateDescriptor
            {
                NetworkId = netId.GlobalId,
                Latitude = lat,
                Longitude = lon,
                Altitude = alt,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
    }
    
    private void OnGeoStateReceived(GeoStateDescriptor sample)
    {
        // Map network ID to local ghost entity
        if (!_entityMap.TryGetEntity(sample.NetworkId, out Entity entity))
            return;
        
        // Convert geodetic to local ENU
        Vector3 localPos = _geo.ToCartesian(sample.Latitude, sample.Longitude, sample.Altitude);
        
        // Update ghost position
        cmd.SetComponent(entity, new NetworkPosition { Value = new Vector2(localPos.X, localPos.Y) });
    }
}
```

**Purpose**: Demonstrate custom translator for geographic state replication (alternative to generic NetworkPosition).

### FireEventTranslator (Translators/FireEventTranslator.cs)

One-shot event replication for fire interactions:

```csharp
public class FireEventTranslator : IDescriptorTranslator
{
    private DdsWriter<NetworkFireEvent> _writer;
    private DdsReader<NetworkFireEvent> _reader;
    
    public void PublishUpdates(ISimulationView view, PackedKey key, IReadOnlyList<Entity> entities)
    {
        // Read local FireInteractionEvent buffer
        foreach (var fireEvent in view.GetEvents<FireInteractionEvent>())
        {
            var netId = view.GetComponentRO<NetworkIdentity>(fireEvent.ShooterEntity);
            
            _writer.Write(new NetworkFireEvent
            {
                ShooterNetworkId = netId.GlobalId,
                TargetPosition = fireEvent.TargetPosition,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
    }
    
    private void OnFireEventReceived(NetworkFireEvent sample)
    {
        if (!_entityMap.TryGetEntity(sample.ShooterNetworkId, out Entity shooter))
            return;
        
        // Raise local event for remote fire
        cmd.PublishEvent(new FireInteractionEvent
        {
            ShooterEntity = shooter,
            TargetPosition = sample.TargetPosition,
            IsFromNetwork = true
        });
    }
}
```

**Design**: Events are **not components** (ephemeral, single-frame), so translators handle event serialization separately from component replication.

---

## Systems

### PhysicsSystem (Systems/PhysicsSystem.cs)

Simple kinematic integration for locally owned entities:

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class PhysicsSystem : IModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        var query = view.Query()
            .With<NetworkPosition>()
            .With<NetworkVelocity>()
            .With<NetworkOwnership>()
            .Build();

        foreach (var e in query)
        {
            ref readonly var ownership = ref view.GetComponentRO<NetworkOwnership>(e);
            
            // Only update locally owned entities (remote positions from network)
            if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
                continue;

            ref readonly var pos = ref view.GetComponentRO<NetworkPosition>(e);
            ref readonly var vel = ref view.GetComponentRO<NetworkVelocity>(e);
            
            // Euler integration: x' = x + v * dt
            var newPos = pos.Value + vel.Value * deltaTime;
            
            cmd.SetComponent(e, new NetworkPosition { Value = newPos });
        }
    }
}
```

**Ownership Check**: Critical to avoid local physics overwriting network-replicated ghost positions.

### RefactoredPlayerInputSystem (Systems/RefactoredPlayerInputSystem.cs)

Keyboard/mouse input for tank control:

```csharp
[UpdateInPhase(SystemPhase.PreSimulation)]
public class RefactoredPlayerInputSystem : IModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        // Query locally owned tanks
        var query = view.Query()
            .With<NetworkOwnership>()
            .With<NetworkVelocity>()
            .Build();
        
        foreach (var entity in query)
        {
            var ownership = view.GetComponentRO<NetworkOwnership>(entity);
            if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
                continue; // Skip remote entities
            
            // Read input (pseudocode - actual impl uses input library)
            Vector2 inputDir = GetWASDInput();
            float speed = inputDir.Length() > 0 ? 10.0f : 0.0f;
            
            Vector2 velocity = inputDir * speed;
            cmd.SetComponent(entity, new NetworkVelocity { Value = velocity });
            
            // Turret control
            if (HasComponent<TurretState>(entity))
            {
                float azimuthDelta = GetMouseDeltaX() * 0.01f;
                var turret = view.GetComponentRO<TurretState>(entity);
                cmd.SetComponent(entity, new TurretState { AzimuthRad = turret.AzimuthRad + azimuthDelta });
            }
        }
    }
}
```

### CombatInputSystem (Systems/CombatInputSystem.cs)

Fire command processing:

```csharp
public class CombatInputSystem : IModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        
        // Check for fire input (e.g., spacebar)
        if (!IsFireKeyPressed())
            return;
        
        // Find locally controlled tank
        var query = view.Query()
            .With<NetworkOwnership>()
            .With<WeaponState>()
            .With<NetworkPosition>()
            .Build();
        
        foreach (var entity in query)
        {
            var ownership = view.GetComponentRO<NetworkOwnership>(entity);
            if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
                continue;
            
            var weapon = view.GetComponentRO<WeaponState>(entity);
            if (weapon.ReloadTimeLeft > 0 || weapon.AmmoCount == 0)
                continue; // Can't fire yet
            
            var pos = view.GetComponentRO<NetworkPosition>(entity);
            Vector2 targetPos = GetMouseWorldPosition();
            
            // Publish fire event
            cmd.PublishEvent(new FireInteractionEvent
            {
                ShooterEntity = entity,
                TargetPosition = targetPos,
                IsFromNetwork = false
            });
            
            // Consume ammo, start reload
            cmd.SetComponent(entity, new WeaponState
            {
                AmmoCount = weapon.AmmoCount - 1,
                ReloadTimeLeft = 2.0f, // 2 second reload
                IsFiring = 1
            });
        }
    }
}
```

### CombatFeedbackSystem (Systems/CombatFeedbackSystem.cs)

Damage application and health tracking:

```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]
public class CombatFeedbackSystem : IModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        
        // Process fire events
        foreach (var fireEvent in view.GetEvents<FireInteractionEvent>())
        {
            // Find entities near target position
            var nearbyQuery = view.Query().With<NetworkPosition>().With<Health>().Build();
            
            foreach (var target in nearbyQuery)
            {
                var targetPos = view.GetComponentRO<NetworkPosition>(target);
                float distance = Vector2.Distance(fireEvent.TargetPosition, targetPos.Value);
                
                // Hit if within 2 meters
                if (distance <= 2.0f)
                {
                    var health = view.GetComponentRO<Health>(target);
                    float newHealth = Math.Max(0, health.Value - 20.0f); // 20 damage
                    
                    cmd.SetComponent(target, new Health { Value = newHealth, MaxValue = health.MaxValue });
                    
                    if (newHealth == 0)
                    {
                        // Publish destroy event (for lifecycle system)
                        cmd.PublishEvent(new RequestDespawnEvent { Entity = target });
                    }
                }
            }
        }
    }
}
```

### TimeSyncSystem (Systems/TimeSyncSystem.cs)

Distributed time synchronization coordination:

```csharp
public class TimeSyncSystem : IModuleSystem
{
    private readonly DistributedTimeCoordinator _coordinator;
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        // Update time coordinator (PLL or Lockstep logic)
        _coordinator.Update(deltaTime);
        
        // Apply synchronized time to simulation
        float syncedDeltaTime = _coordinator.GetSynchronizedDeltaTime();
        view.SetDeltaTime(syncedDeltaTime);
        
        // Handle mode switch events
        foreach (var switchEvent in view.GetEvents<SwitchTimeModeEvent>())
        {
            _coordinator.SwitchMode(switchEvent.NewMode);
        }
    }
}
```

---

## Recording and Replay

### AsyncRecorder Integration

**Live Mode** (Recording):

```csharp
public async Task InitializeAsync(int nodeId, bool replayMode, string? recPath = null, bool autoSpawn = true)
{
    // ... (setup code)
    
    if (!isReplay)
    {
        // Create recorder
        recorder = new AsyncRecorder(World, recordingPath);
        await recorder.StartAsync();
        
        // Register RecorderTickSystem
        Kernel.RegisterSystem(new RecorderTickSystem(recorder));
    }
}
```

**RecorderTickSystem** (Systems/RecorderTickSystem.cs):

```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]
public class RecorderTickSystem : IModuleSystem
{
    private readonly AsyncRecorder _recorder;
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        // Log current frame (all entities and events)
        _recorder.LogFrame();
    }
}
```

**Replay Mode**:

```csharp
if (isReplay)
{
    // Create replay reader
    var reader = new ReplayReader(recordingPath);
    var metadata = RecordingMetadata.Load(recordingPath + ".meta.json");
    
    // Register ReplayBridgeSystem
    replaySystem = new ReplayBridgeSystem(reader, metadata);
    Kernel.RegisterSystem(replaySystem);
}
```

**ReplayBridgeSystem** (Systems/ReplayBridgeSystem.cs):

```csharp
public class ReplayBridgeSystem : IModuleSystem
{
    private readonly ReplayReader _reader;
    private long _currentFrame = 0;
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        // Read frame from recording
        var frameData = _reader.ReadFrame(_currentFrame);
        if (frameData == null)
        {
            Console.WriteLine("Replay finished.");
            return;
        }
        
        // Deserialize and apply entity states
        foreach (var entitySnapshot in frameData.Entities)
        {
            Entity entity = GetOrCreateEntity(entitySnapshot.Id);
            ApplyComponentSnapshot(view, entity, entitySnapshot);
        }
        
        // Replay events
        foreach (var eventSnapshot in frameData.Events)
        {
            DeserializeAndPublishEvent(view, eventSnapshot);
        }
        
        _currentFrame++;
    }
}
```

**Determinism**: Replay produces identical entity states and event sequences as original recording (frame-perfect).

---

## Usage Examples

### Example 1: Run Two-Node Live Simulation

```bash
# Terminal 1: Start Node 100 (Alpha - Master)
dotnet run --project Fdp.Examples.NetworkDemo -- 100 live

# Terminal 2: Start Node 200 (Bravo - Slave)
dotnet run --project Fdp.Examples.NetworkDemo -- 200 live
```

**Expected Behavior**:
- Both nodes discover each other via DDS discovery
- Node 100 spawns local tank, replicates to Node 200 as ghost
- Node 200 spawns local tank, replicates to Node 100 as ghost
- User can control local tank with WASD, see remote tank moving
- Recordings saved to `node_100.fdp` and `node_200.fdp`

### Example 2: Replay Recording

```bash
# Replay Node 100's recording
dotnet run --project Fdp.Examples.NetworkDemo -- 100 replay node_100.fdp
```

**Expected Behavior**:
- Simulation plays back recorded frames
- Entity positions, events, and interactions replayed identically
- No network communication (offline playback)

### Example 3: Transfer Ownership During Runtime

```csharp
// In OwnershipInputSystem.cs
if (IsOwnershipTransferKeyPressed()) // e.g., 'O' key
{
    // Find remote ghost entity
    var query = view.Query()
        .With<NetworkOwnership>()
        .With<NetworkIdentity>()
        .Build();
    
    foreach (var entity in query)
    {
        var ownership = view.GetComponentRO<NetworkOwnership>(entity);
        if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
        {
            // Request ownership transfer
            cmd.PublishEvent(new OwnershipUpdateRequest
            {
                Entity = entity,
                NewOwnerId = ownership.LocalNodeId
            });
            break; // Transfer one entity per keypress
        }
    }
}
```

**Network Flow**:
1. Node publishes `OwnershipUpdateRequest` event
2. `OwnershipUpdateTranslator` serializes to DDS `OwnershipUpdateTopic`
3. Remote node receives, updates `NetworkOwnership.PrimaryOwnerId`
4. Ghost becomes locally controlled, starts sending position updates

### Example 4: Spawn Tank Programmatically

```csharp
public void SpawnTankAt(ISimulationView view, Vector2 position, int ownerId)
{
    var cmd = view.GetCommandBuffer();
    var tkb = view.GetSingletonRO<ITkbDatabase>();
    
    // Spawn using TKB template
    var spawnEvent = new RequestSpawnEvent
    {
        TkbType = 100, // CommandTank
        OwningNodeId = ownerId,
        InitialComponents = new object[]
        {
            new NetworkPosition { Value = position },
            new NetworkVelocity { Value = Vector2.Zero },
            new Health { Value = 100, MaxValue = 100 }
        }
    };
    
    cmd.PublishEvent(spawnEvent);
    
    // EntityLifecycleModule handles spawning entity + child turret
}
```

### Example 5: Custom Geographic Translator

```csharp
// Alternative to built-in NetworkPosition replication
public class MyGeodeticTranslator : IDescriptorTranslator
{
    public PackedKey DescriptorKey => PackedKey.Create(99, 0);
    
    public void PublishUpdates(ISimulationView view, PackedKey key, IReadOnlyList<Entity> entities)
    {
        foreach (var entity in entities)
        {
            // Read local position
            var pos = view.GetComponentRO<DemoPosition>(entity);
            
            // Convert to GPS (via WGS84Transform)
            var (lat, lon, alt) = _geo.ToGeodetic(new Vector3(pos.Value.X, pos.Value.Y, 0));
            
            // Send custom DDS message
            _writer.Write(new MyGPSMessage
            {
                EntityId = entity.Index,
                Latitude = lat,
                Longitude = lon,
                Altitude = alt
            });
        }
    }
    
    private void OnGPSReceived(MyGPSMessage sample)
    {
        // Map EntityId to local ghost
        Entity ghost = _entityMap.GetEntity(sample.EntityId);
        
        // Convert GPS to local ENU position
        Vector3 localPos = _geo.ToCartesian(sample.Latitude, sample.Longitude, sample.Altitude);
        
        cmd.SetComponent(ghost, new DemoPosition { Value = new Vector2(localPos.X, localPos.Y) });
    }
}
```

---

## Integration with FDP Ecosystem

### Toolkit Dependencies

**FDP.Toolkit.Replication**:
- `NetworkIdentity`, `NetworkOwnership` components
- `GhostCreationSystem`, `SmartEgressSystem`
- `NetworkEntityMap` for global ID ↔ local Entity mapping
- Demonstrates mandatory descriptor logic (hard/soft requirements)

**FDP.Toolkit.Time**:
- `DistributedTimeCoordinator` for master/slave synchronization
- `TimePulseDescriptor`, `FrameAckDescriptor` DDS messages
- Mode switching between PLL (continuous) and Lockstep (deterministic)

**FDP.Toolkit.Lifecycle**:
- `RequestSpawnEvent`, `RequestDespawnEvent` for entity management
- `EntityLifecycleModule` handles TKB template instantiation
- Child entity creation via `ChildBlueprintDefinition`

**Fdp.Toolkit.Geographic**:
- `WGS84Transform` for lat/lon/alt ↔ ENU conversion
- `GeographicModule` (optional, demonstrates coordinate integration)
- `FastGeodeticTranslator` shows custom DDS topic for GPS state

**ModuleHost.Network.Cyclone**:
- `CycloneNetworkModule` wraps DDS participant lifecycle
- `DdsWriter<T>`, `DdsReader<T>` for topic pub/sub
- `NodeIdMapper` for consistent node ID assignment across domains

**Fdp.Kernel**:
- `EntityRepository`, `Entity`, `ComponentType`
- `AsyncRecorder`, `ReplayReader` for flight recorder
- `FdpEventBus` for event routing

### Data Flow Diagram

```
Local Node:
  [PlayerInput] → [RefactoredPlayerInputSystem]
       ↓
  [NetworkVelocity updated]
       ↓
  [PhysicsSystem] → [NetworkPosition updated]
       ↓
  [SmartEgressSystem] (detects position change)
       ↓
  [FastGeodeticTranslator.PublishUpdates()]
       ↓
  [DdsWriter<GeoStateDescriptor>.Write()] → DDS Network
                                              ↓
                                     [Remote Node DdsReader]
                                              ↓
                          [FastGeodeticTranslator.OnGeoStateReceived()]
                                              ↓
                              [Ghost NetworkPosition updated]
                                              ↓
                              [Rendering: Display remote tank]
```

---

## Best Practices

### Node ID Assignment

**Use Consistent Mapping**:
```csharp
// Deterministic mapping ensures local ID = 1, peers get 2, 3, ...
var nodeMapper = new NodeIdMapper(localDomain: 0, localInstance: instanceId);
localInternalId = nodeMapper.GetOrRegisterInternalId(new NetworkAppId { AppDomainId = 0, AppInstanceId = instanceId });
```

**Why**: Consistent internal IDs prevent ownership conflicts and simplify debugging.

### Ownership Checks

**Always Verify Ownership Before Writing**:
```csharp
var ownership = view.GetComponentRO<NetworkOwnership>(entity);
if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
    return; // Skip updates for remote ghosts
```

**Why**: Prevents local physics from overwriting network-replicated positions.

### Mandatory Descriptors

**Hard vs. Soft Requirements**:
- **Hard**: Critical data (chassis position) - ghost waits indefinitely
- **Soft**: Secondary data (turret state) - ghost spawns after timeout

```csharp
tank.MandatoryDescriptors.Add(new MandatoryDescriptor {
    PackedKey = PackedKey.Create(descriptorId, 0),
    IsHard = true, // or false
    SoftTimeoutFrames = 60 // Only for soft requirements
});
```

### Timestamping Network Messages

**Always Include Timestamps**:
```csharp
_writer.Write(new NetworkFireEvent
{
    ShooterNetworkId = shooterId,
    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
});
```

**Why**: Enables out-of-order detection, latency measurement, and time-based interpolation.

### Recording Metadata

**Save Metadata for Replay**:
```csharp
var metadata = new RecordingMetadata
{
    NodeId = instanceId,
    StartTime = DateTime.UtcNow,
    DurationSeconds = totalTime,
    FrameCount = frameCount,
    ComponentTypes = registeredComponents
};
metadata.Save(recordingPath + ".meta.json");
```

**Why**: Replay system needs component type information for deserialization.

---

## Troubleshooting

### Issue: Ghost Entities Don't Appear

**Symptom**: Remote node sees no ghosts for peer entities

**Causes**:
1. DDS discovery failure (firewall, wrong domain ID)
2. Mandatory descriptors not arriving (hard requirement blocking spawn)
3. NetworkIdentity.GlobalId collision
4. EntityMap not mapping network IDs correctly

**Solution**:
```csharp
// Enable DDS discovery logging
DdsParticipant.EnableLogging(LogLevel.Debug);

// Check ghost phase
var ghost = view.GetComponentRO<GhostMetadata>(ghostEntity);
if (ghost.Phase == GhostPhase.WaitingMandatory)
{
    Console.WriteLine($"Ghost waiting for mandatory descriptors: {ghost.MandatoryDescriptors}");
}

// Verify network ID uniqueness
var idAllocator = new DdsIdAllocator(participant, uniquePrefix: $"Node_{instanceId}");
ulong globalId = idAllocator.AllocateId();
```

### Issue: Ownership Transfer Doesn't Work

**Symptom**: Entity remains controlled by original owner after transfer request

**Causes**:
1. `OwnershipUpdateTranslator` not registered
2. DDS topic name mismatch (writer/reader different topics)
3. NetworkOwnership component not updated on remote node

**Solution**:
```csharp
// Verify translator registration
allTranslators.Add(new OwnershipUpdateTranslator(nodeMapper, participant));

// Check DDS topic names match
var writer = participant.CreateWriter<OwnershipUpdateTopic>("OwnershipUpdates");
var reader = participant.CreateReader<OwnershipUpdateTopic>("OwnershipUpdates", OnOwnershipReceived);

// Log ownership changes
cmd.SetComponent(entity, new NetworkOwnership { PrimaryOwnerId = newOwnerId });
Console.WriteLine($"Ownership transferred: entity {entity.Index} → owner {newOwnerId}");
```

### Issue: Recording Playback Shows Wrong Entity States

**Symptom**: Replay shows entities at incorrect positions or with wrong components

**Causes**:
1. Component type registration changed between record and replay
2. Metadata file missing or corrupted
3. Frame index mismatch

**Solution**:
```csharp
// Validate metadata before replay
var metadata = RecordingMetadata.Load(recordingPath + ".meta.json");
if (metadata.ComponentTypes.Count != DemoComponentRegistry.RegisteredTypes.Count)
{
    throw new Exception("Component registry mismatch between recording and replay.");
}

// Verify frame integrity
long expectedFrames = metadata.FrameCount;
long actualFrames = reader.GetFrameCount();
if (expectedFrames != actualFrames)
{
    Console.WriteLine($"Warning: Metadata claims {expectedFrames} frames, file has {actualFrames}");
}
```

### Issue: Time Synchronization Drift

**Symptom**: Nodes drift apart in simulated time despite time sync enabled

**Causes**:
1. Master time controller not sending TimePulse messages
2. Network latency variance causing PLL instability
3. Lockstep acknowledgment messages dropped

**Solution**:
```csharp
// Switch to Lockstep mode for deterministic sync
cmd.PublishEvent(new SwitchTimeModeEvent { NewMode = TimeMode.Lockstep });

// Increase PLL filtering (trades responsiveness for stability)
var pllConfig = new PLLConfiguration
{
    ProportionalGain = 0.1f,  // Lower = more filtering
    IntegralGain = 0.01f,
    DerivativeGain = 0.0f
};
_coordinator.Configure(pllConfig);

// Monitor sync error
float syncError = _coordinator.GetSynchronizationError(); // Milliseconds
Console.WriteLine($"Time sync error: {syncError:F2}ms");
```

---

## Conclusion

**Fdp.Examples.NetworkDemo** serves as the definitive reference implementation for distributed FDP simulations. It demonstrates production-quality patterns for:

- **Multi-Node Networking**: DDS-based replication with automatic discovery
- **Entity Lifecycle**: TKB templates, hierarchical entities, ghost protocol
- **Time Synchronization**: Both continuous (PLL) and deterministic (lockstep) modes
- **Recording/Replay**: Frame-perfect deterministic playback
- **Ownership Management**: Dynamic authority transfer for distributed control
- **Custom Translators**: Extensible serialization for domain-specific data (geographic, events)

**Architecture Insights**:
- Separation of concerns: Physics (local), Replication (network), Lifecycle (spawning)
- Event-driven design: Commands as events, systems respond asynchronously
- Ownership-based authority: Only owner updates, ghosts receive
- Translator abstraction: Network protocol independent of ECS components

**Recommended Study Path**:
1. Run two-node simulation, observe ghost creation and replication
2. Trigger ownership transfer, verify authority handoff
3. Record session, replay deterministically
4. Modify `TankTemplate` to add custom components
5. Implement custom translator for new component type

For production simulations, use NetworkDemo as a template for module registration, translator implementation, and distributed system coordination.

---

**Total Lines**: 1023
