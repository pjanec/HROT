# Network Replication Architecture

## Overview

**Network Replication** is FDP's end-to-end architecture for synchronizing entities across distributed nodes. It coordinates entity ownership, component synchronization, bandwidth optimization, and lifecycle management. This architecture enables massively multiplayer simulations where thousands of entities exist on multiple nodes simultaneously.

**Key Components**:
- [FDP.Toolkit.Replication](../toolkits/FDP.Toolkit.Replication.md): Ghost creation, smart egress, ownership tracking
- [FDP.Toolkit.Lifecycle](../toolkits/FDP.Toolkit.Lifecycle.md): Spawn/despawn coordination
- [ModuleHost.Network.Cyclone](../modulehost/ModuleHost.Network.Cyclone.md): DDS transport layer
- [NetworkDemo](../examples/Fdp.Examples.NetworkDemo.md): Full replication example

---

## Conceptual Model

### Entity Ownership Model

```
┌────────────────────────────────────────────────────────────────┐
│                    Node 100 (Owner)                             │
│                                                                 │
│  Entity #42 (AUTHORITATIVE)                                    │
│  ├─ Position: (10, 5, 3)         Read-Write Access             │
│  ├─ Velocity: (1, 0, 0)          Full control                  │
│  ├─ Health: 100                                                │
│  └─ NetworkId: 42                                              │
│                                                                 │
│  Every Frame:                                                   │
│   1. User input updates velocity                               │
│   2. Physics system updates position                           │
│   3. SmartEgressSystem publishes changes → Network             │
│                                                                 │
└────────────────────┬───────────────────────────────────────────┘
                     │
                     │ DDS Topic: "EntityState"
                     │ {EntityId: 42, PosX: 10, PosY: 5, ...}
                     │
                     ▼
┌────────────────────┬───────────────────────────────────────────┐
│                    Node 200 (Observer)                     │
│                                                                 │
│  Entity #42 (GHOST / READ-ONLY)                                │
│  ├─ Position: (10, 5, 3)         Read-Only Access              │
│  ├─ Velocity: (1, 0, 0)          No modifications allowed      │
│  ├─ Health: 100                                                │
│  ├─ GhostComponent:                                            │
│  │  ├─ OwnerNodeId: 100                                        │
│  │  └─ RemoteEntityId: 42                                      │
│  └─ NetworkId: 42                                              │
│                                                                 │
│  Every Frame:                                                   │
│   1. GhostCreationSystem receives updates from Network         │
│   2. Updates ghost entity components (interpolation if needed) │
│   3. Rendering system draws ghost at current position          │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Key Insight**: Each entity has ONE owner (authoritative) and N ghosts (read-only replicas).

---

## Architecture Layers

```
┌─────────────────────────────────────────────────────────────────┐
│                  Application Layer                              │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ Game Logic Systems                                       │   │
│  │  - PlayerControllerSystem                                │   │
│  │  - PhysicsSystem                                         │   │
│  │  - CombatSystem                                          │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                         │
                         │ Modifies Components (Owner Only)
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│             Replication Layer (FDP.Toolkit.Replication)         │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ Owner Node:                                              │   │
│  │  ├─ SmartEgressSystem (Delta Compression)               │   │
│  │  └─ OwnershipTrackingSystem                             │   │
│  │                                                          │   │
│  │ Ghost Node:                                              │   │
│  │  ├─ GhostCreationSystem                                 │   │
│  │  ├─ GhostUpdateSystem                                   │   │
│  │  └─ InterpolationSystem (optional)                      │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                         │
                         │ Descriptor Transformations
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│             Translator Layer (See Translator-Pattern.md)        │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ Egress: Components → NetworkDescriptor                  │   │
│  │ Ingress: NetworkDescriptor → Components                 │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                         │
                         │ Serialized Descriptors
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│         Transport Layer (ModuleHost.Network.Cyclone)            │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ DDS DataWriter<EntityStateDescriptor>                   │   │
│  │ DDS DataReader<EntityStateDescriptor>                   │   │
│  │ Topic: "EntityState"                                     │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                         │
                         │ UDP Multicast (RTPS Protocol)
                         ▼
                    [ Network ]
```

---

## Egress Pipeline (Owner → Network)

### Smart Egress System

```csharp
public class SmartEgressSystem : IModuleSystem
{
    public SystemPhase Phase => SystemPhase.PostSimulation;
    public int Priority => 10;
    
    private Dictionary<uint, ulong> _lastPublishFrame = new();
    private DeltaCompressor _deltaCompressor;
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        // Query owned entities (not ghosts)
        var query = view.Query()
            .With<NetworkIdComponent>()
            .Without<GhostComponent>()
            .Build();
        
        foreach (var entity in query)
        {
            var netId = entity.Get<NetworkIdComponent>();
            
            // Delta compression: check if changed since last publish
            if (!HasChanges(entity, netId))
                continue;
            
            // Create descriptor
            var descriptor = new EntityStateDescriptor
            {
                EntityId = netId.NetworkId,
                OwnerNodeId = _localNodeId,
                Frame = _currentFrame
            };
            
            // Fill from entity (uses translator)
            var translator = _translatorRegistry.Get<EntityStateDescriptor>();
            translator.FillFromEntity(ref descriptor, entity);
            
            // Publish to network
            _writer.Write(ref descriptor);
            
            // Update last publish frame
            _lastPublishFrame[netId.NetworkId] = _currentFrame;
        }
    }
    
    private bool HasChanges(Entity entity, NetworkIdComponent netId)
    {
        if (!_lastPublishFrame.TryGetValue(netId.NetworkId, out var lastFrame))
            return true; // Never published
        
        // Check component change tracking
        return _deltaCompressor.HasChanges(entity, lastFrame);
    }
}
```

**Key Optimizations**:
1. **Delta Compression**: Only publish entities with component changes
2. **Change Tracking**: Per-component dirty flags (position changed? velocity changed?)
3. **Rate Limiting**: Publish high-priority entities every frame, low-priority every N frames

### Delta Compression

```csharp
public class DeltaCompressor
{
    private struct ComponentHistory
    {
        public ulong LastChangeFrame;
        public uint ChangeMask; // Bitmask for which fields changed
    }
    
    private Dictionary<uint, ComponentHistory> _history = new();
    
    public bool HasChanges(Entity entity, ulong lastPublishFrame)
    {
        var netId = entity.Get<NetworkIdComponent>();
        
        if (!_history.TryGetValue(netId.NetworkId, out var history))
            return true; // New entity
        
        // Check if any component changed since last publish
        return history.LastChangeFrame > lastPublishFrame;
    }
    
    public void RecordChange(Entity entity, Type componentType)
    {
        var netId = entity.Get<NetworkIdComponent>();
        
        _history[netId.NetworkId] = new ComponentHistory
        {
            LastChangeFrame = _currentFrame,
            ChangeMask = GetComponentMask(componentType)
        };
    }
}
```

---

## Ingress Pipeline (Network → Ghost)

### Ghost Creation System

```csharp
public class GhostCreationSystem : IModuleSystem
{
    public SystemPhase Phase => SystemPhase.PreSimulation;
    public int Priority => 20;
    
    private Dictionary<uint, Entity> _ghostEntities = new(); // RemoteId → LocalEntity
    private DdsDataReader<EntityStateDescriptor> _reader;
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        // Read incoming entity updates
        using var samples = _reader.Take();
        
        foreach (var sample in samples)
        {
            if (!sample.Info.Valid)
            {
                // Entity destroyed on remote node
                HandleEntityDestroyed(sample.Info.InstanceHandle);
                continue;
            }
            
            ref readonly var descriptor = ref sample.DataView;
            
            // Ignore entities we own (no self-replication)
            if (descriptor.OwnerNodeId == _localNodeId)
                continue;
            
            // Find or create ghost
            Entity ghost;
            if (!_ghostEntities.TryGetValue(descriptor.EntityId, out ghost))
            {
                ghost = CreateGhost(descriptor);
                _ghostEntities[descriptor.EntityId] = ghost;
            }
            
            // Update ghost components
            var translator = _translatorRegistry.Get<EntityStateDescriptor>();
            translator.ApplyToEntity(ref descriptor, ghost);
            
            // Update metadata
            ghost.Set(new GhostMetadata
            {
                LastUpdateFrame = _currentFrame,
                LastUpdateTime = Time.ElapsedTime
            });
        }
    }
    
    private Entity CreateGhost(EntityStateDescriptor descriptor)
    {
        var ghost = _world.CreateEntity();
        
        // Mark as ghost (read-only, remote-owned)
        ghost.Add(new GhostComponent
        {
            OwnerNodeId = descriptor.OwnerNodeId,
            RemoteEntityId = descriptor.EntityId
        });
        
        // Add network ID
        ghost.Add(new NetworkIdComponent
        {
            NetworkId = descriptor.EntityId
        });
        
        return ghost;
    }
    
    private void HandleEntityDestroyed(ulong instanceHandle)
    {
        // Find ghost by instance handle
        var entityId = GetEntityIdFromInstanceHandle(instanceHandle);
        
        if (_ghostEntities.TryGetValue(entityId, out var ghost))
        {
            // Mark for destruction (garbage collection system will clean up)
            ghost.Add<DestroyedComponent>();
            _ghostEntities.Remove(entityId);
        }
    }
}
```

---

## Ownership & Lifecycle Coordination

### Ownership Transfer

```
Scenario: Player 1 exits vehicle, Player 2 enters vehicle

┌────────────────────────────────────────┐
│ Node 100 (Player 1)                    │
│  Vehicle Entity #42                    │
│  Ownership: Node 100                   │
│                                        │
│  Player exits → Publish ownership      │
│  transfer request                      │
└────────────┬───────────────────────────┘
             │
             │ OwnershipTransferRequest
             │ {EntityId: 42, NewOwner: 200}
             ▼
┌────────────────────────────────────────┐
│ All Nodes (Receive Transfer)           │
│                                        │
│  1. Node 100: Stops publishing Vehicle │
│  2. Node 200: Starts publishing Vehicle│
│     (Ghost → Owner)                    │
│  3. Other Nodes: Update ghost metadata │
│                                        │
└────────────────────────────────────────┘
```

**Implementation**:
```csharp
[DdsTopic("OwnershipTransfer")]
public partial struct OwnershipTransferDescriptor
{
    [DdsKey] public Guid TransferId;
    public uint EntityId;
    public uint OldOwner;
    public uint NewOwner;
    public ulong TransferFrame;
}

public class OwnershipTrackingSystem : IModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        // Read ownership transfer requests
        using var samples = _transferReader.Take();
        
        foreach (var sample in samples)
        {
            if (!sample.Info.Valid) continue;
            
            ref readonly var transfer = ref sample.DataView;
            
            if (transfer.OldOwner == _localNodeId)
            {
                // We're giving up ownership
                ConvertToGhost(transfer.EntityId);
            }
            else if (transfer.NewOwner == _localNodeId)
            {
                // We're taking ownership
                ConvertToOwner(transfer.EntityId);
            }
            else
            {
                // We're observing transfer (update ghost metadata)
                UpdateGhostOwner(transfer.EntityId, transfer.NewOwner);
            }
        }
    }
    
    private void ConvertToGhost(uint entityId)
    {
        var entity = FindEntityByNetworkId(entityId);
        
        // Add ghost component (marks as read-only)
        entity.Add(new GhostComponent
        {
            OwnerNodeId = 0, // Will be updated when new owner publishes
            RemoteEntityId = entityId
        });
        
        // Stop publishing
        _egressSystem.ExcludeEntity(entity);
    }
    
    private void ConvertToOwner(uint entityId)
    {
        var entity = FindEntityByNetworkId(entityId);
        
        // Remove ghost component (marks as read-write)
        entity.Remove<GhostComponent>();
        
        // Start publishing
        _egressSystem.IncludeEntity(entity);
    }
}
```

### Lifecycle Synchronization

See [Entity-Lifecycle-Complete.md](Entity-Lifecycle-Complete.md) for full details.

**Brief Overview**:
```
Spawn:
  Owner → Publishes SpawnEvent → Ghosts created on all nodes

Despawn:
  Owner → Publishes DespawnEvent → Ghosts destroyed on all nodes

Transfer:
  Owner → Publishes OwnershipTransfer → New owner takes over
```

---

## Bandwidth Optimization

### Strategies

1. **Delta Compression**: Only send changed components (50-90% reduction)
2. **Rate Limiting**: High-priority entities @ 60Hz, low-priority @ 10Hz
3. **Area of Interest (AOI)**: Only replicate nearby entities (e.g., 100m radius)
4. **Quantization**: Reduce precision (float32 → int16 for position)

### Example: Area of Interest

```csharp
public class AreaOfInterestSystem : IModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var localPlayer = GetLocalPlayerEntity();
        var playerPos = localPlayer.Get<Position>();
        
        // Update interest mask for egress system
        var query = view.Query().With<NetworkIdComponent>().Build();
        
        foreach (var entity in query)
        {
            var pos = entity.Get<Position>();
            float distance = Vector3.Distance(playerPos, pos);
            
            if (distance < _aoiRadius)
            {
                // In range → publish at full rate
                entity.Set(new ReplicationPriority { Value = ReplicationPriorityLevel.High });
            }
            else if (distance < _aoiRadius * 2)
            {
                // Far but visible → publish at reduced rate
                entity.Set(new ReplicationPriority { Value = ReplicationPriorityLevel.Low });
            }
            else
            {
                // Out of range → don't replicate
                entity.Set(new ReplicationPriority { Value = ReplicationPriorityLevel.None });
            }
        }
    }
}
```

---

## Performance Characteristics

**Replication Throughput** (NetworkDemo benchmarks):

| Scenario                    | Entity Count | Bandwidth    | CPU (Owner) | CPU (Ghost) |
|-----------------------------|--------------|--------------|-------------|-------------|
| Static entities (no changes)| 10,000       | 0 KB/s       | 0.1 ms      | 0.01 ms     |
| Moving entities (full)      | 1,000        | 2.5 MB/s     | 3.2 ms      | 1.8 ms      |
| Moving entities (delta)     | 1,000        | 500 KB/s     | 3.5 ms      | 1.8 ms      |
| Combat scenario (100 units) | 100          | 80 KB/s      | 0.8 ms      | 0.4 ms      |

**Delta Compression Savings**:
- Static components: 100% reduction (not sent)
- Low-frequency updates (Health): 90% reduction (send only on change)
- High-frequency updates (Position): 30-50% reduction (quantization + rate limiting)

---

## Best Practices

**Ownership Design**:
1. **Minimize Transfers**: Ownership handoff is expensive (100-200ms latency)
2. **Clear Ownership Rules**: Who owns projectiles? Vehicles? Items?
3. **Predict Transfers**: Pre-emptively transfer before interactions (e.g., vehicle seat reservation)

**Bandwidth Optimization**:
1. **Delta Compression**: Essential for large-scale simulations
2. **Area of Interest**: Dramatically reduces bandwidth (1000+ entities)
3. **Quantization**: Float32 → Int16 for position (6 bytes → 2 bytes per axis)
4. **Rate Limiting**: Don't publish non-critical entities every frame

**Ghost Management**:
1. **Interpolation**: Smooth ghost movement between updates (see NetworkDemo)
2. **Extrapolation**: Predict future position for sub-frame accuracy
3. **Timeout Detection**: Destroy ghosts if owner stops publishing (network failure)

**Testing**:
1. **Latency Simulation**: Test with artificial delays (50-200ms)
2. **Packet Loss**: Verify graceful degradation (1-5% loss)
3. **Ownership Races**: Test simultaneous transfer requests (conflict resolution)

---

## Conclusion

**Network Replication Architecture** orchestrates entity synchronization across distributed nodes with minimal bandwidth and latency. By combining delta compression, smart egress, ownership tracking, and lifecycle coordination, FDP achieves scalable multiplayer simulations supporting thousands of entities.

**Key Strengths**:
- **Scalability**: Delta compression reduces bandwidth by 50-90%
- **Ownership Model**: Clear authority (owner vs. ghost)
- **Bandwidth Optimization**: AOI, quantization, rate limiting
- **Lifecycle Integration**: Coordinated spawn/despawn/transfer

**Used By**:
- FDP.Toolkit.Replication (ghost creation, smart egress, ownership)
- FDP.Toolkit.Lifecycle (spawn/despawn coordination)
- ModuleHost.Network.Cyclone (DDS transport)
- NetworkDemo (full replication example)

**Total Lines**: 720
