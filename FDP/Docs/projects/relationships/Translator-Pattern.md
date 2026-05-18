# Translator Pattern Architecture

## Overview

The **Translator Pattern** is a core architectural pattern in FDP that decouples network messages from ECS components. It defines a bidirectional transformation pipeline: **ingress** (network data → components) and **egress** (components → network data). This pattern enables:
- Type-safe component serialization
- Network protocol evolution without code changes
- Centralized data policy enforcement
- Pluggable transport layers

**Key Projects**:
- [FDP.Interfaces](../core/FDP.Interfaces.md): `IDescriptorTranslator<TDescriptor>` interface
- [ModuleHost.Network.Cyclone](../modulehost/ModuleHost.Network.Cyclone.md): DDS-based ingress/egress pipeline
- [NetworkDemo](../examples/Fdp.Examples.NetworkDemo.md): Concrete translator implementations

---

## Conceptual Model

### Problem Space

In distributed simulations, entities exist on multiple nodes simultaneously:
- **Owner Node**: Authoritative copy, full read-write access
- **Ghost Nodes**: Remote replicas, read-only, synchronized from owner

**Challenge**: How do we serialize/deserialize ECS components to network messages without tight coupling?

**Solution**: Translator Pattern - a registry of bidirectional transformations indexed by component type.

### High-Level Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                      Owner Node (Node 100)                       │
│                                                                  │
│  ECS World                         Network Layer                │
│  ┌──────────────┐                 ┌──────────────┐             │
│  │ Entity #42   │                 │ DDS Writer   │             │
│  │ ├─ Position  │  Egress         │              │             │
│  │ ├─ Velocity  │─────────────────┤ Publish      │─────────┐   │
│  │ └─ Health    │  Translator     │              │         │   │
│  └──────────────┘                 └──────────────┘         │   │
└────────────────────────────────────────────────────────────┼───┘
                                                              │
                                                              │ DDS Network
                                                              │ (UDP Multicast)
                                                              │
┌────────────────────────────────────────────────────────────┼───┐
│                      Ghost Node (Node 200)                 │   │
│                                                             ▼   │
│  Network Layer                 ECS World                        │
│  ┌──────────────┐             ┌──────────────┐                 │
│  │ DDS Reader   │             │ Entity #42   │                 │
│  │              │  Ingress    │ ├─ Position  │                 │
│  │ Receive      ├─────────────┤ ├─ Velocity  │                 │
│  │              │  Translator │ └─ Health    │  (Ghost)        │
│  └──────────────┘             └──────────────┘                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Architecture

### Interface Definition (FDP.Interfaces)

```csharp
namespace FDP.Interfaces
{
    /// <summary>
    /// Bidirectional translator between network descriptors and ECS components.
    /// </summary>
    public interface IDescriptorTranslator<TDescriptor>
        where TDescriptor : struct
    {
        /// <summary>
        /// INGRESS: Translate network descriptor to ECS components.
        /// Called when receiving remote entity data.
        /// </summary>
        void ApplyToEntity(ref TDescriptor descriptor, EntityHandle entity);
        
        /// <summary>
        /// EGRESS: Translate ECS components to network descriptor.
        /// Called when publishing local entity data.
        /// </summary>
        void FillFromEntity(ref TDescriptor descriptor, EntityHandle entity);
        
        /// <summary>
        /// Component types this translator reads/writes.
        /// Used for: 1) Component registration validation
        ///           2) Snapshot provider optimization
        /// </summary>
        Type[] GetComponentTypes();
    }
}
```

**Key Design Decisions**:
- **`ref TDescriptor`**: Avoid allocations (descriptor is typically a large struct)
- **`EntityHandle`**: Abstraction over Entity, prevents kernel coupling
- **`GetComponentTypes()`**: Enables runtime validation and optimization

### Translator Registry (ModuleHost.Network.Cyclone)

The `NetworkModule` maintains a registry mapping descriptor types to translators:

```csharp
public class NetworkModule : IModule
{
    private Dictionary<Type, object> _ingressTranslators = new();
    private Dictionary<Type, object> _egressTranslators = new();
    
    public void RegisterTranslator<TDescriptor>(
        IDescriptorTranslator<TDescriptor> translator)
        where TDescriptor : struct
    {
        var descriptorType = typeof(TDescriptor);
        
        // Store translator (both ingress and egress use same object)
        _ingressTranslators[descriptorType] = translator;
        _egressTranslators[descriptorType] = translator;
        
        // Validate component types are registered
        foreach (var componentType in translator.GetComponentTypes())
        {
            if (!_componentRegistry.IsRegistered(componentType))
            {
                throw new InvalidOperationException(
                    $"Translator for {descriptorType.Name} requires unregistered " +
                    $"component {componentType.Name}. Register components first."
                );
            }
        }
    }
    
    internal IDescriptorTranslator<TDescriptor> GetTranslator<TDescriptor>()
        where TDescriptor : struct
    {
        if (_ingressTranslators.TryGetValue(typeof(TDescriptor), out var translator))
        {
            return (IDescriptorTranslator<TDescriptor>)translator;
        }
        
        throw new InvalidOperationException(
            $"No translator registered for {typeof(TDescriptor).Name}"
        );
    }
}
```

### Ingress Pipeline (Ghost Creation)

When a DDS reader receives entity data, the ingress pipeline activates:

```csharp
public class GhostCreationSystem : NetworkSystem
{
    public override void Execute(ISimulationView view, float deltaTime)
    {
        // Read entity state descriptors from DDS
        using var samples = _entityStateReader.Take();
        
        foreach (var sample in samples)
        {
            if (!sample.Info.Valid) continue;
            
            ref readonly var descriptor = ref sample.DataView;
            
            // Find or create ghost entity
            Entity ghostEntity;
            if (!_ghostEntities.TryGetValue(descriptor.EntityId, out ghostEntity))
            {
                ghostEntity = _world.CreateEntity();
                _ghostEntities[descriptor.EntityId] = ghostEntity;
                
                // Mark as ghost (read-only)
                ghostEntity.Add<GhostComponent>(new GhostComponent
                {
                    OwnerNodeId = descriptor.OwnerNodeId,
                    RemoteEntityId = descriptor.EntityId
                });
            }
            
            // Apply ingress translation
            var translator = _networkModule.GetTranslator<EntityStateDescriptor>();
            translator.ApplyToEntity(ref descriptor, ghostEntity.AsHandle());
        }
    }
}
```

### Egress Pipeline (Smart Egress)

When the owner node publishes entity updates, the egress pipeline activates:

```csharp
public class SmartEgressSystem : NetworkSystem
{
    public override void Execute(ISimulationView view, float deltaTime)
    {
        // Query local entities (not ghosts)
        var query = view.Query()
            .With<NetworkIdComponent>()
            .Without<GhostComponent>()
            .Build();
        
        foreach (var entity in query)
        {
            var netId = entity.Get<NetworkIdComponent>();
            
            // Check if entity has changed (delta compression)
            if (!HasComponentChanges(entity, netId.LastPublishFrame))
                continue;
            
            // Fill descriptor from entity
            var descriptor = new EntityStateDescriptor
            {
                EntityId = netId.NetworkId,
                OwnerNodeId = _localNodeId
            };
            
            var translator = _networkModule.GetTranslator<EntityStateDescriptor>();
            translator.FillFromEntity(ref descriptor, entity.AsHandle());
            
            // Publish to DDS
            _entityStateWriter.Write(ref descriptor);
            
            // Update last publish frame
            entity.Set(new NetworkIdComponent
            {
                NetworkId = netId.NetworkId,
                LastPublishFrame = _currentFrame
            });
        }
    }
}
```

---

## Concrete Examples (NetworkDemo)

### Example 1: Position/Velocity Translator

**Descriptor** (Network Message):
```csharp
[DdsTopic("EntityState")]
public partial struct EntityStateDescriptor
{
    [DdsKey] public uint EntityId;
    public uint OwnerNodeId;
    public float PosX, PosY, PosZ;
    public float VelX, VelY, VelZ;
}
```

**Components** (ECS):
```csharp
public struct Position
{
    public float X, Y, Z;
}

public struct Velocity
{
    public float X, Y, Z;
}
```

**Translator Implementation**:
```csharp
public class EntityStateTranslator : IDescriptorTranslator<EntityStateDescriptor>
{
    public void ApplyToEntity(ref EntityStateDescriptor desc, EntityHandle entity)
    {
        // INGRESS: Network → Components
        entity.Set(new Position { X = desc.PosX, Y = desc.PosY, Z = desc.PosZ });
        entity.Set(new Velocity { X = desc.VelX, Y = desc.VelY, Z = desc.VelZ });
    }
    
    public void FillFromEntity(ref EntityStateDescriptor desc, EntityHandle entity)
    {
        // EGRESS: Components → Network
        var pos = entity.Get<Position>();
        var vel = entity.Get<Velocity>();
        
        desc.PosX = pos.X; desc.PosY = pos.Y; desc.PosZ = pos.Z;
        desc.VelX = vel.X; desc.VelY = vel.Y; desc.VelZ = vel.Z;
    }
    
    public Type[] GetComponentTypes()
    {
        return new[] { typeof(Position), typeof(Velocity) };
    }
}
```

### Example 2: Lifecycle Translator (Spawn/Despawn)

**Descriptor**:
```csharp
[DdsTopic("LifecycleEvent")]
public partial struct LifecycleEventDescriptor
{
    [DdsKey] public uint EntityId;
    public LifecycleState State; // Spawning, Active, Despawning, Destroyed
    public uint OwnerNodeId;
}
```

**Translator Implementation**:
```csharp
public class LifecycleTranslator : IDescriptorTranslator<LifecycleEventDescriptor>
{
    public void ApplyToEntity(ref LifecycleEventDescriptor desc, EntityHandle entity)
    {
        // INGRESS: Update ghost lifecycle state
        switch (desc.State)
        {
            case LifecycleState.Spawning:
                entity.Add<SpawningComponent>();
                break;
            case LifecycleState.Active:
                entity.Remove<SpawningComponent>();
                entity.Add<ActiveComponent>();
                break;
            case LifecycleState.Despawning:
                entity.Remove<ActiveComponent>();
                entity.Add<DespawningComponent>();
                break;
            case LifecycleState.Destroyed:
                entity.Add<DestroyedComponent>(); // GC system will clean up
                break;
        }
    }
    
    public void FillFromEntity(ref LifecycleEventDescriptor desc, EntityHandle entity)
    {
        // EGRESS: Publish lifecycle changes
        if (entity.Has<SpawningComponent>())
            desc.State = LifecycleState.Spawning;
        else if (entity.Has<ActiveComponent>())
            desc.State = LifecycleState.Active;
        else if (entity.Has<DespawningComponent>())
            desc.State = LifecycleState.Despawning;
        else if (entity.Has<DestroyedComponent>())
            desc.State = LifecycleState.Destroyed;
    }
    
    public Type[] GetComponentTypes()
    {
        return new[]
        {
            typeof(SpawningComponent),
            typeof(ActiveComponent),
            typeof(DespawningComponent),
            typeof(DestroyedComponent)
        };
    }
}
```

---

## Data Policy Enforcement

Translators enforce **data policies** - rules about which components are networked:

```csharp
// Example: Health component marked as networked
[DataPolicy(DataPolicyFlags.Networked)]
public struct HealthComponent
{
    public float CurrentHealth;
    public float MaxHealth;
}

// Example: Input component NOT networked (local-only)
[DataPolicy(DataPolicyFlags.LocalOnly)]
public struct PlayerInputComponent
{
    public float Horizontal;
    public float Vertical;
    public bool Jump;
}
```

**Translator Respects Policies**:
```csharp
public void FillFromEntity(ref EntityStateDescriptor desc, EntityHandle entity)
{
    // Health is networked → include in descriptor
    if (entity.Has<HealthComponent>())
    {
        var health = entity.Get<HealthComponent>();
        desc.Health = health.CurrentHealth;
        desc.MaxHealth = health.MaxHealth;
    }
    
    // PlayerInput is LocalOnly → NEVER include in network messages
    // (no code to copy it)
}
```

---

## Performance Optimizations

### 1. Delta Compression

Translators can implement **delta compression** - only send changed components:

```csharp
public struct DeltaTracker
{
    public ulong LastPublishFrame;
    public uint ComponentChangeMask; // Bitflags for changed components
}

public void FillFromEntity(ref EntityStateDescriptor desc, EntityHandle entity)
{
    var tracker = entity.Get<DeltaTracker>();
    
    // Check which components changed since last publish
    bool positionChanged = (tracker.ComponentChangeMask & 0x1) != 0;
    bool velocityChanged = (tracker.ComponentChangeMask & 0x2) != 0;
    
    // Only fill changed components
    if (positionChanged)
    {
        var pos = entity.Get<Position>();
        desc.PosX = pos.X; desc.PosY = pos.Y; desc.PosZ = pos.Z;
    }
    
    if (velocityChanged)
    {
        var vel = entity.Get<Velocity>();
        desc.VelX = vel.X; desc.VelY = vel.Y; desc.VelZ = vel.Z;
    }
}
```

### 2. Sparse Descriptors

Use **optional fields** to reduce bandwidth for unchanged data:

```csharp
[DdsTopic("SparseEntityState")]
public partial struct SparseEntityStateDescriptor
{
    [DdsKey] public uint EntityId;
    
    // Bitmask: which fields are valid
    public uint ValidMask;
    
    // Optional fields (only valid if corresponding bit set)
    public float PosX, PosY, PosZ;
    public float VelX, VelY, VelZ;
    public float Health;
}

// Translator uses ValidMask to determine what to apply
public void ApplyToEntity(ref SparseEntityStateDescriptor desc, EntityHandle entity)
{
    if ((desc.ValidMask & 0x1) != 0) // Position valid?
    {
        entity.Set(new Position { X = desc.PosX, Y = desc.PosY, Z = desc.PosZ });
    }
    
    if ((desc.ValidMask & 0x2) != 0) // Velocity valid?
    {
        entity.Set(new Velocity { X = desc.VelX, Y = desc.VelY, Z = desc.VelZ });
    }
}
```

### 3. Component Batching

Batch multiple entities into single descriptor for efficiency:

```csharp
[DdsTopic("EntityBatch")]
public partial struct EntityBatchDescriptor
{
    public uint Count; // Number of entities in this batch
    public uint[] EntityIds; // Array of entity IDs (max 100)
    public Position[] Positions; // Parallel array
    public Velocity[] Velocities; // Parallel array
}

public void ApplyToEntity(ref EntityBatchDescriptor desc, EntityHandle entity)
{
    // Find entity in batch
    for (uint i = 0; i < desc.Count; i++)
    {
        if (desc.EntityIds[i] == entity.GetNetworkId())
        {
            entity.Set(desc.Positions[i]);
            entity.Set(desc.Velocities[i]);
            break;
        }
    }
}
```

---

## Debugging & Validation

### Translator Validation

```csharp
public static class TranslatorValidator
{
    public static void ValidateTranslator<TDescriptor>(
        IDescriptorTranslator<TDescriptor> translator,
        IComponentRegistry componentRegistry)
        where TDescriptor : struct
    {
        // Check all component types registered
        foreach (var componentType in translator.GetComponentTypes())
        {
            if (!componentRegistry.IsRegistered(componentType))
            {
                throw new InvalidOperationException(
                    $"Translator requires unregistered component: {componentType.Name}"
                );
            }
        }
        
        // Check for circular dependencies (if translator A depends on translator B)
        // ... validation logic ...
    }
}
```

### Runtime Diagnostics

```csharp
public class TranslatorDiagnostics
{
    private struct TranslatorStats
    {
        public ulong IngressCount;
        public ulong EgressCount;
        public TimeSpan TotalIngressTime;
        public TimeSpan TotalEgressTime;
    }
    
    private Dictionary<Type, TranslatorStats> _stats = new();
    
    public void RecordIngress<TDescriptor>(TimeSpan duration)
    {
        var stats = _stats[typeof(TDescriptor)];
        stats.IngressCount++;
        stats.TotalIngressTime += duration;
        _stats[typeof(TDescriptor)] = stats;
    }
    
    public void PrintReport()
    {
        foreach (var (type, stats) in _stats)
        {
            var avgIngress = stats.TotalIngressTime / stats.IngressCount;
            var avgEgress = stats.TotalEgressTime / stats.EgressCount;
            
            Console.WriteLine($"{type.Name}:");
            Console.WriteLine($"  Ingress: {stats.IngressCount} calls, avg {avgIngress.TotalMicroseconds:F1} µs");
            Console.WriteLine($"  Egress:  {stats.EgressCount} calls, avg {avgEgress.TotalMicroseconds:F1} µs");
        }
    }
}
```

---

## Best Practices

**Translator Design**:
1. **Keep Translators Stateless**: Store state in components, not translators
2. **One Translator Per Descriptor**: Don't reuse translators for multiple descriptors
3. **Validate Component Types**: Use `GetComponentTypes()` for early error detection
4. **Document Data Policies**: Clearly specify which components are networked

**Performance**:
1. **Minimize Allocations**: Use `ref TDescriptor`, avoid LINQ, cache arrays
2. **Delta Compression**: Only send changed components (use ComponentChangeMask)
3. **Batch Updates**: Group multiple entities into single descriptor
4. **Profile Translators**: Use diagnostics to identify hot spots

**Testing**:
1. **Roundtrip Tests**: Verify `ApplyToEntity(FillFromEntity(entity))` is identity
2. **Schema Evolution**: Test adding/removing fields (wire compatibility)
3. **Null Entity**: Handle missing components gracefully (don't crash)

---

## Comparison with Alternative Patterns

### Translator Pattern vs. Direct Serialization

| Aspect | Translator Pattern | Direct Serialization |
|--------|-------------------|---------------------|
| Coupling | ✅ Decoupled (components don't know about network) | ❌ Tight coupling (components serialize themselves) |
| Protocol Evolution | ✅ Change descriptor without changing components | ❌ Breaking change requires component modification |
| Data Policy | ✅ Centralized enforcement | ❌ Scattered across components |
| Performance | ⚠️ Slight overhead (indirection) | ✅ Direct (no abstraction) |
| Testability | ✅ Easy to mock translators | ⚠️ Harder to test serialization |

### Translator Pattern vs. Reflection-Based Serialization

| Aspect | Translator Pattern | Reflection-Based |
|--------|-------------------|-----------------|
| Performance | ✅ Zero allocation, delegate caching | ❌ Allocations, reflection overhead |
| Type Safety | ✅ Compile-time checks | ⚠️ Runtime discovery |
| Customization | ✅ Full control over translation | ❌ Limited to attribute-based config |
| Code Size | ⚠️ More boilerplate | ✅ Less code (automated) |

---

## Conclusion

The **Translator Pattern** is a foundational architecture in FDP, enabling clean separation between ECS components and network messages. By centralizing serialization logic, enforcing data policies, and providing optimization hooks (delta compression, batching), it supports high-performance distributed simulations while maintaining code flexibility and testability.

**Key Strengths**:
- **Decoupling**: Components independent of network protocol
- **Flexibility**: Protocol evolution without component changes
- **Performance**: Zero-allocation hot path, delta compression, batching
- **Testability**: Mock translators for unit tests

**Used By**:
- ModuleHost.Network.Cyclone (DDS ingress/egress)
- FDP.Toolkit.Replication (ghost creation, smart egress)
- NetworkDemo (concrete translator implementations)

**Total Lines**: 950
