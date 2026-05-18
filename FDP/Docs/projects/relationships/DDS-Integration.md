# DDS Integration Pattern

## Overview

**DDS Integration** describes how FDP wraps Eclipse Cyclone DDS to provide a high-performance, zero-allocation networking layer. This integration bridges DDS (a publish-subscribe middleware designed for real-time systems) with FDP's Entity Component System, enabling distributed simulations with minimal latency and bandwidth.

**Key Components**:
- [FastCycloneDds](../extdeps/ExtDeps.FastCycloneDds.md): C# bindings for Cyclone DDS
- [ModuleHost.Network.Cyclone](../modulehost/ModuleHost.Network.Cyclone.md): FDP's DDS integration module
- [NetworkDemo](../examples/Fdp.Examples.NetworkDemo.md): Practical DDS usage examples

---

## DDS Fundamentals

### Core Concepts

**Domain**: Isolated universe of DDS communication (Domain 0, Domain 1, etc.)
- Participants in different domains cannot communicate
- Use different domains for: production vs. test, separate simulations

**Topic**: Named, typed channel (e.g., "EntityState" of type `EntityStateDescriptor`)
- Publishers write to topics
- Subscribers read from topics
- Topic = (Name, Type, QoS)

**Quality of Service (QoS)**: Delivery guarantees
- **Reliability**: Reliable (TCP-like) vs. BestEffort (UDP-like)
- **Durability**: Volatile (ephemeral) vs. TransientLocal (late-joiners receive history)
- **History**: KeepAll vs. KeepLast(N)

### DDS Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        DDS Domain 0                              │
│                                                                  │
│  ┌──────────────────────┐       ┌──────────────────────┐        │
│  │ Node 100             │       │ Node 200             │        │
│  │                      │       │                      │        │
│  │ DomainParticipant    │       │ DomainParticipant    │        │
│  │  ├─ Publisher        │       │  ├─ Subscriber       │        │
│  │  │  └─ Writer<T>     │       │  │  └─ Reader<T>     │        │
│  │  │     Topic: "X"    │◄─────►│  │     Topic: "X"    │        │
│  │  │                   │       │  │                   │        │
│  │  └─ Subscriber       │       │  └─ Publisher        │        │
│  │     └─ Reader<T>     │       │     └─ Writer<T>     │        │
│  │        Topic: "Y"    │◄─────►│        Topic: "Y"    │        │
│  └──────────────────────┘       └──────────────────────┘        │
│                                                                  │
│             Discovery: UDP Multicast (239.255.0.1)               │
│             Data: UDP Unicast (P2P after discovery)              │
└─────────────────────────────────────────────────────────────────┘
```

---

## FDP Integration Architecture

### Layer Mapping

```
┌─────────────────────────────────────────────────────────────────┐
│                     FDP Layer                                    │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ ECS World                                                  │ │
│  │  - Entities                                                │ │
│  │  - Components (Position, Velocity, Health, ...)           │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                          │
                          │ Translators (see Translator-Pattern.md)
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│                  DDS Integration Layer                           │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ NetworkModule (ModuleHost.Network.Cyclone)                 │ │
│  │  ├─ TopicRegistry (Name → Writer/Reader)                  │ │
│  │  ├─ TranslatorRegistry (Type → IDescriptorTranslator)     │ │
│  │  ├─ SystemRegistry (Ingress/Egress systems)               │ │
│  │  └─ Configuration (Domain ID, QoS profiles)               │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                          │
                          │ P/Invoke Bindings (FastCycloneDds)
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│                  Cyclone DDS Native Runtime                      │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ C Runtime (libddsc.so / ddsc.dll)                          │ │
│  │  ├─ RTPS Protocol (Real-Time Publish-Subscribe)           │ │
│  │  ├─ Discovery (SPDP/SEDP)                                 │ │
│  │  ├─ Transport (UDP/TCP)                                   │ │
│  │  └─ Serialization (CDR encoding)                          │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                          │
                          │ UDP/TCP Sockets
                          ▼
                    [ Network ]
```

---

## Topic Schema Generation

### Code-First Schema

FDP uses **code-first** DDS topics: define data types in C#, auto-generate IDL.

**C# Descriptor**:
```csharp
using CycloneDDS.Schema;

[DdsTopic("EntityState")]
public partial struct EntityStateDescriptor
{
    [DdsKey, DdsId(0)]
    public uint EntityId;
    
    [DdsId(1)]
    public uint OwnerNodeId;
    
    [DdsId(2)]
    public float PosX;
    
    [DdsId(3)]
    public float PosY;
    
    [DdsId(4)]
    public float PosZ;
    
    [DdsId(5)]
    public float VelX;
    
    [DdsId(6)]
    public float VelY;
    
    [DdsId(7)]
    public float VelZ;
}
```

**Generated IDL** (automatic):
```idl
module FDP {
  struct EntityStateDescriptor {
    @key unsigned long EntityId;
    unsigned long OwnerNodeId;
    float PosX;
    float PosY;
    float PosZ;
    float VelX;
    float VelY;
    float VelZ;
  };
};

#pragma keylist EntityStateDescriptor EntityId
```

**Why `[DdsId(N)]`?**
- Schema evolution: Add/remove fields without breaking wire compatibility
- Explicit field ordering (avoid C# struct layout surprises)
- Enables forward/backward compatibility (old clients can read new messages)

---

## NetworkModule Configuration

### Initialization

```csharp
public class NetworkModule : IModule
{
    private DomainParticipant _participant;
    private Dictionary<string, object> _writers = new();
    private Dictionary<string, object> _readers = new();
    
    public void Initialize(IModuleContext context)
    {
        var config = context.GetConfiguration("Network");
        
        // Create domain participant
        _participant = new DomainParticipant(
            domainId: config.GetInt("DomainId", 0)
        );
        
        // Register topics
        RegisterTopic<EntityStateDescriptor>("EntityState", ReliableQoS);
        RegisterTopic<LifecycleEventDescriptor>("LifecycleEvent", ReliableQoS);
        RegisterTopic<TimeUpdateDescriptor>("TimeUpdate", ReliableQoS);
    }
    
    private void RegisterTopic<T>(string topicName, DataWriterQos writerQoS)
        where T : struct
    {
        // Create writer
        var writer = _participant.CreateWriter<T>(writerQoS);
        _writers[topicName] = writer;
        
        // Create reader
        var reader = _participant.CreateReader<T>();
        _readers[topicName] = reader;
    }
    
    public DataWriter<T> GetWriter<T>(string topicName) where T : struct
    {
        return (DataWriter<T>)_writers[topicName];
    }
    
    public DataReader<T> GetReader<T>(string topicName) where T : struct
    {
        return (DataReader<T>)_readers[topicName];
    }
}
```

---

## QoS Configuration

### Common Profiles

**Reliable, Transient Local** (State synchronization):
```csharp
var stateQoS = new DataWriterQos
{
    Reliability = ReliabilityKind.Reliable,
    Durability = DurabilityKind.TransientLocal,
    History = new HistoryQos
    {
        Kind = HistoryKind.KeepLast,
        Depth = 10 // Keep last 10 samples per instance
    }
};

// Use for: Entity state, ownership, lifecycle events
var writer = participant.CreateWriter<EntityStateDescriptor>(stateQoS);
```

**Best-Effort, Volatile** (High-frequency telemetry):
```csharp
var telemetryQoS = new DataWriterQos
{
    Reliability = ReliabilityKind.BestEffort,
    Durability = DurabilityKind.Volatile,
    History = new HistoryQos
    {
        Kind = HistoryKind.KeepLast,
        Depth = 1 // Only keep latest sample
    }
};

// Use for: 60Hz position updates, sensor data
var writer = participant.CreateWriter<PositionUpdateDescriptor>(telemetryQoS);
```

**Reliable, Persistent** (Configuration/Metadata):
```csharp
var configQoS = new DataWriterQos
{
    Reliability = ReliabilityKind.Reliable,
    Durability = DurabilityKind.Persistent, // Survives process restart
    History = new HistoryQos
    {
        Kind = HistoryKind.KeepAll // Keep all samples
    }
};

// Use for: Server configuration, metadata
// Requires DDS persistence service running
```

---

## Reader/Writer Lifecycle

### Publishing Data (Egress)

```csharp
public class SmartEgressSystem : IModuleSystem
{
    private DataWriter<EntityStateDescriptor> _writer;
    
    public void Initialize(NetworkModule networkModule)
    {
        _writer = networkModule.GetWriter<EntityStateDescriptor>("EntityState");
    }
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        var query = view.Query()
            .With<NetworkIdComponent>()
            .Without<GhostComponent>()
            .Build();
        
        foreach (var entity in query)
        {
            var descriptor = new EntityStateDescriptor();
            
            // Fill from entity (translator)
            _translator.FillFromEntity(ref descriptor, entity);
            
            // Publish (zero-allocation write)
            _writer.Write(ref descriptor);
        }
    }
}
```

### Receiving Data (Ingress)

```csharp
public class GhostCreationSystem : IModuleSystem
{
    private DataReader<EntityStateDescriptor> _reader;
    
    public void Initialize(NetworkModule networkModule)
    {
        _reader = networkModule.GetReader<EntityStateDescriptor>("EntityState");
    }
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        // Zero-copy read (ref struct)
        using var samples = _reader.Take();
        
        foreach (var sample in samples)
        {
            if (!sample.Info.Valid)
            {
                // Entity disposed (owner destroyed it)
                HandleDispose(sample.Info.InstanceHandle);
                continue;
            }
            
            // Zero-copy access to data
            ref readonly var descriptor = ref sample.DataView;
            
            // Find or create ghost
            var ghost = GetOrCreateGhost(descriptor.EntityId);
            
            // Apply ingress translation
            _translator.ApplyToEntity(ref descriptor, ghost);
        }
    }
}
```

---

## Instance Management (Keyed Topics)

DDS supports **keyed topics** - multiple instances per topic (one per key value).

**Example: EntityState with key**
```csharp
[DdsTopic("EntityState")]
public partial struct EntityStateDescriptor
{
    [DdsKey] public uint EntityId; // Key field
    public float PosX, PosY, PosZ;
}
```

**Benefits**:
- **O(1) Lookup**: Reader can retrieve specific entity's data by key
- **Instance Lifecycle**: DDS tracks per-instance state (alive, disposed, no-writers)
- **History Per Instance**: `KeepLast(10)` keeps 10 samples PER EntityId

**Usage**:
```csharp
// Reader: Get latest sample for specific entity
var sample = reader.ReadInstance(entityId: 42);
if (sample.Info.Valid)
{
    ref readonly var data = ref sample.DataView;
    Console.WriteLine($"Entity 42 position: ({data.PosX}, {data.PosY}, {data.PosZ})");
}

// Writer: Dispose instance (signals entity destroyed)
writer.Dispose(entityId: 42); // Readers will see Info.Valid = false
```

---

## Discovery & Connectivity

### Automatic Discovery

DDS uses **multicast** for participant discovery:

```
┌──────────────────────────────────────────────────────────────┐
│ Node 100 Startup                                              │
│  1. Create DomainParticipant(Domain 0)                       │
│  2. Send SPDP announcement → 239.255.0.1:7400                │
│     "I am Node 100 at 192.168.1.100:7410"                    │
└──────────────────────────────────────────────────────────────┘
                          │
                          │ Multicast
                          ▼
┌──────────────────────────────────────────────────────────────┐
│ Node 200 (Already Running)                                   │
│  1. Receive SPDP announcement                                │
│  2. Reply with own announcement → Unicast 192.168.1.100:7410 │
│  3. Send SEDP (endpoint discovery)                           │
│     "I have Reader<EntityState>, Writer<TimeUpdate>"         │
└──────────────────────────────────────────────────────────────┘
                          │
                          │ Unicast P2P
                          ▼
┌──────────────────────────────────────────────────────────────┐
│ Node 100                                                      │
│  1. Receive Node 200's SEDP                                  │
│  2. Match readers/writers (topic name + type match)          │
│  3. Establish direct P2P connection for data                 │
│  4. Start exchanging EntityState samples                     │
└──────────────────────────────────────────────────────────────┘
```

**No Central Server**: Fully peer-to-peer, self-healing (nodes can join/leave dynamically).

### Connectivity Troubleshooting

**Problem: Nodes not discovering each other**
- Check firewall (UDP multicast 239.255.0.1:7400, unicast ports 7410+)
- Verify same DomainId (different domains = isolated)
- Check network interfaces (DDS may bind to wrong NIC)

**Problem: Discovery works, but no data**
- Topic mismatch (name or type differs)
- QoS incompatibility (e.g., Reliable writer + BestEffort reader)
- Check DDS logs: `CYCLONEDDS_URI=file://cyclonedds.xml`

---

## Performance Characteristics

**Zero-Allocation Benchmarks** (FastCycloneDds):

| Operation | Throughput | Latency (Localhost) | GC Allocations |
|-----------|------------|---------------------|----------------|
| Write (32B descriptor) | 19M msg/s | 25 µs | 0 bytes |
| Write (1KB descriptor) | 5.5M msg/s | 35 µs | 0 bytes |
| Read (zero-copy) | 33M ops/s | N/A | 0 bytes |
| Read (.Data copy) | 8M ops/s | N/A | 32-1KB per sample |

**Network Latency** (1Gbps LAN):

| Payload Size | Min Latency | Avg Latency | P99 Latency |
|--------------|-------------|-------------|-------------|
| 32 bytes | 45 µs | 60 µs | 120 µs |
| 256 bytes | 50 µs | 70 µs | 150 µs |
| 1 KB | 65 µs | 90 µs | 200 µs |
| 64 KB | 800 µs | 1.2 ms | 3 ms |

---

## Best Practices

**Topic Design**:
1. **One Topic Per Logical Stream**: EntityState, LifecycleEvent, TimeUpdate (separate topics)
2. **Use Keys for Entities**: `[DdsKey] EntityId` enables instance management
3. **Explicit DdsId**: Always use `[DdsId(N)]` for schema evolution
4. **Keep Payloads Small**: < 1KB for low latency, < 64KB to avoid fragmentation

**QoS Selection**:
1. **Reliable for State**: Entity state, ownership, lifecycle (must not be lost)
2. **BestEffort for Telemetry**: High-frequency updates where loss is acceptable
3. **TransientLocal for Late-Joiners**: New nodes receive recent history
4. **KeepLast(N) for History**: Balance memory vs. completeness

**Performance**:
1. **Zero-Copy Reads**: Use `.DataView` instead of `.Data`
2. **Batch Writes**: Group multiple entities into single descriptor (reduce syscalls)
3. **Preallocate Readers/Writers**: Create at startup, not per-frame
4. **Profile with DDS Tools**: Use `ddsperf`, Wireshark RTPS dissector

**Debugging**:
1. **Enable DDS Tracing**: `CYCLONEDDS_URI=file://cyclonedds.xml` (trace discovery, data flow)
2. **Use ddsi2.builtin.discovery Logs**: See participant/endpoint matching
3. **Wireshark RTPS Filter**: Capture DDS traffic (filter: `rtps`)

---

## Comparison with Other Network Layers

| Feature | DDS (FDP) | Custom TCP | gRPC | ZeroMQ |
|---------|-----------|------------|------|--------|
| Discovery | ✅ Automatic | ❌ Manual | ⚠️ via DNS | ⚠️ Manual |
| QoS Policies | ✅ Rich (Reliability, Durability, etc.) | ❌ TCP only | ⚠️ Limited | ⚠️ Limited |
| Typed Topics | ✅ Schema validation | ❌ Raw bytes | ✅ Protobuf | ❌ Raw bytes |
| Zero-Copy Reads | ✅ (FastCycloneDds) | ❌ | ❌ | ❌ |
| Late-Joiner Support | ✅ TransientLocal | ❌ | ❌ | ❌ |
| Multicast | ✅ Native | ❌ | ❌ | ⚠️ PGM |

---

## Conclusion

**DDS Integration** provides FDP with production-quality networking via Eclipse Cyclone DDS. By wrapping DDS with zero-allocation bindings, code-first schemas, and translator patterns, FDP achieves high-performance distributed simulations with automatic discovery, flexible QoS, and wire-level interoperability.

**Key Strengths**:
- **Zero-Allocation**: 19M msg/s writes, 33M ops/s reads
- **Automatic Discovery**: No central server, self-healing
- **Flexible QoS**: Reliable/BestEffort, Transient/Volatile, KeepLast(N)
- **Schema Evolution**: `[DdsId(N)]` enables forward/backward compatibility

**Used By**:
- ModuleHost.Network.Cyclone (NetworkModule orchestration)
- FDP.Toolkit.Replication (ghost creation, smart egress)
- NetworkDemo (multi-node simulation example)

**Total Lines**: 680
