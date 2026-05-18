# FDP.Interfaces

**Project Path**: `Common/FDP.Interfaces/FDP.Interfaces.csproj`  
**Created**: February 10, 2026  
**Last Verified**: February 10, 2026  
**README Status**: No README

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
  - [Abstraction Layer Design](#abstraction-layer-design)
  - [Network Integration Model](#network-integration-model)
  - [Template Knowledge Base (TKB) Pattern](#template-knowledge-base-tkb-pattern)
- [Core Abstractions](#core-abstractions)
  - [IDescriptorTranslator](#idescriptortranslator)
  - [ITkbDatabase](#itkbdatabase)
  - [INetworkTopology](#inetworktopology)
  - [INetworkMaster](#inetworkmaster)
  - [ISerializationProvider](#iserializationprovider)
- [Supporting Types](#supporting-types)
  - [TkbTemplate](#tkbtemplate)
  - [MandatoryDescriptor](#mandatorydescriptor)
  - [ChildBlueprintDefinition](#childblueprintdefinition)
  - [PackedKey](#packedkey)
  - [FdpDescriptorAttribute](#fdpdescriptorattribute)
- [ASCII Diagrams](#ascii-diagrams)
- [Source Code Analysis](#source-code-analysis)
- [Dependencies](#dependencies)
- [Usage Examples](#usage-examples)
- [Best Practices](#best-practices)
- [Design Principles](#design-principles)
- [Relationships to Other Projects](#relationships-to-other-projects)
- [API Reference](#api-reference)
- [Known Issues & Limitations](#known-issues--limitations)
- [References](#references)

---

## Overview

**FDP.Interfaces** is a lightweight abstraction layer that defines core contracts for network replication, entity blueprinting, and cross-module integration in the Fast Data Plane (FDP) ecosystem.

### Purpose

FDP.Interfaces serves as the **integration contract layer** between:
- The kernel ECS engine (Fdp.Kernel)
- The module host orchestration layer (ModuleHost.Core)
- Network replication implementations (ModuleHost.Network.Cyclone)
- Domain-specific toolkits (FDP.Toolkit.*)

It provides **zero-dependency abstractions** that enable:
- Decoupled network replication strategies
- Pluggable entity blueprint systems
- Network topology configuration
- Protocol-agnostic descriptor translation

### Key Features

- **IDescriptorTranslator**: Bidirectional network↔ECS component translation
- **ITkbDatabase**: Template Knowledge Base for entity blueprints
- **INetworkTopology**: Distributed node configuration
- **TkbTemplate**: Composable entity templates with mandatory descriptor tracking
- **PackedKey**: Efficient descriptor identification (Ordinal + InstanceId)
- **FdpDescriptorAttribute**: Code-generation marker for automatic translators

### Target Use Cases

- Distributed simulations with DDS (Data Distribution Service)
- Multi-node networked environments
- Entity replication across network boundaries
- Blueprint-based entity spawning
- Protocol-agnostic network abstraction

---

## Architecture

### Abstraction Layer Design

FDP.Interfaces sits at the boundary between the kernel and the application layers:

```
┌─────────────────────────────────────────────────────────┐
│         Application / Examples Layer                    │
│  (Fdp.Examples.NetworkDemo, BattleRoyale, etc.)        │
└─────────────────────┬───────────────────────────────────┘
                      │ Uses
                      ▼
┌─────────────────────────────────────────────────────────┐
│           Toolkit Layer (FDP.Toolkit.*)                 │
│  (Lifecycle, Replication, Time, Geographic, etc.)       │
└─────────────────────┬───────────────────────────────────┘
                      │ Implements
                      ▼
┌─────────────────────────────────────────────────────────┐
│         ModuleHost.Core / ModuleHost.Network.*          │
│  (Module orchestration, DDS networking, etc.)           │
└─────────────────────┬───────────────────────────────────┘
                      │ Depends on
                      ▼
┌═════════════════════════════════════════════════════════┐
║              FDP.Interfaces (THIS LAYER)                ║
║  Pure abstractions, no implementations                  ║
╚═════════════════════┬═══════════════════════════════════╝
                      │ Uses
                      ▼
┌─────────────────────────────────────────────────────────┐
│              Fdp.Kernel (ECS Foundation)                │
│  (EntityRepository, ComponentSystem, EventBus)          │
└─────────────────────────────────────────────────────────┘
```

**Design Goals**:
- **Minimal dependencies**: Only depends on Fdp.Kernel
- **Protocol-agnostic**: No DDS-specific types exposed
- **Testable**: Easy to mock for unit testing
- **Stable**: Interface changes break downstream, so keep minimal

### Network Integration Model

FDP.Interfaces defines a **descriptor-based replication model**:

```
Network Layer (DDS Topics)
         ↕
   IDescriptorTranslator (Bidirectional)
         ↕
   ECS Components (Fdp.Kernel)
```

**Key Concepts**:

1. **Descriptor**: A network-serializable data structure representing entity state
2. **Translator**: Converts between network descriptors and ECS components
3. **Ordinal**: Unique identifier for descriptor type (used for routing)
4. **Instance ID**: Distinguishes multiple instances of the same descriptor type

**Translation Flow**:

```
INGRESS (Network → ECS):
  DDS Topic → Reader → Translator.PollIngress() → EntityCommandBuffer → Components

EGRESS (ECS → Network):
  Components → Query → Translator.ScanAndPublish() → Writer → DDS Topic
```

### Template Knowledge Base (TKB) Pattern

TKB is a **blueprint system** for entity spawning with network-aware construction:

```
TkbTemplate (Blueprint)
  ├─ TkbType (Unique ID)
  ├─ Name (String identifier)
  ├─ Component Applicators (List<Action>)
  ├─ Mandatory Descriptors (Network requirements)
  └─ Child Blueprints (Hierarchical entities)

Spawning Flow:
  1. Create entity (ghost state)
  2. Wait for mandatory descriptors from network
  3. Apply template components
  4. Promote to active entity
```

**Mandatory Descriptor Logic**:
- **Hard requirements**: Entity cannot activate without this descriptor
- **Soft requirements**: Entity activates after timeout if descriptor missing
- **Timeout mechanism**: Prevents indefinite waiting for unreliable networks

---

## Core Abstractions

### IDescriptorTranslator

**Purpose**: Translates between network descriptors and ECS components in both directions.

**Interface**:
```csharp
public interface IDescriptorTranslator
{
    long DescriptorOrdinal { get; }
    string TopicName { get; }
    
    void PollIngress(IEntityCommandBuffer cmd, ISimulationView view);
    void ScanAndPublish(ISimulationView view);
    void ApplyToEntity(Entity entity, object data, EntityRepository repo);
    void Dispose(long networkEntityId);
}
```

**Responsibilities**:

1. **PollIngress**: Process incoming network data
   - Read samples from DDS reader
   - Map network entity ID to local entity
   - Update/create components via command buffer
   - Handle lifecycle (entity creation, updates, deletion)

2. **ScanAndPublish**: Publish local entity state to network
   - Query entities with authority
   - Read component data
   - Write to DDS writer
   - Respect ownership/authority masks

3. **ApplyToEntity**: Direct component application
   - Used during ghost promotion
   - Bypasses network read path
   - Applies cached descriptor data

4. **Dispose**: Cleanup network resources
   - Dispose DDS instance for entity
   - Signal entity deletion to network

**Key Design Points**:
- Protocol-agnostic (no DDS types in interface)
- Bidirectional (ingress + egress)
- Command-buffer based (deferred structural changes)
- Authority-aware (respects ownership)

### ITkbDatabase

**Purpose**: Registry for entity templates (blueprints) with dual-key lookup.

**Interface**:
```csharp
public interface ITkbDatabase
{
    void Register(TkbTemplate template);
    
    TkbTemplate GetByType(long tkbType);
    bool TryGetByType(long tkbType, out TkbTemplate template);
    
    TkbTemplate GetByName(string name);
    bool TryGetByName(string name, out TkbTemplate template);
    
    IEnumerable<TkbTemplate> GetAll();
}
```

**Lookup Mechanisms**:
- **By TkbType (long)**: Primary key, used in network messages
- **By Name (string)**: Secondary key, used in configuration/scripting

**Typical Usage**:
```csharp
// Registration
tkbDb.Register(new TkbTemplate("Tank", tkbType: 1001));

// Network spawn (by ID)
long tkbType = masterDescriptor.TkbType;
var template = tkbDb.GetByType(tkbType);
template.ApplyTo(repo, entity);

// Configuration spawn (by name)
var template = tkbDb.GetByName("Tank");
Entity tank = repo.CreateEntity();
template.ApplyTo(repo, tank);
```

### INetworkTopology

**Purpose**: Defines network peer configuration for distributed entity lifecycle management.

**Interface**:
```csharp
public interface INetworkTopology
{
    int LocalNodeId { get; }
    
    IEnumerable<int> GetExpectedPeers(long tkbType);
    IEnumerable<int> GetAllNodes();
}
```

**Usage in Entity Lifecycle Management**:

When creating a networked entity, the topology determines:
1. Which peers must acknowledge (ACK) construction
2. Timeout for waiting for ACKs
3. Authority distribution

**Example**:
```csharp
// Node 100 creates tank entity (TkbType 1001)
var peers = topology.GetExpectedPeers(tkbType: 1001);
// Returns: [200, 300] (other tank nodes)

// System creates entity in "Constructing" state
// Waits for ACKs from nodes 200 and 300
// After ACKs received (or timeout), promotes to "Active"
```

### INetworkMaster

**Purpose**: Contract for entity master descriptors (entity identity on network).

**Interface**:
```csharp
public interface INetworkMaster
{
    long EntityId { get; }   // Global unique entity ID
    long TkbType { get; }    // Blueprint type ID
}
```

**Key Design Decision**:
- **No OwnerId field**: Ownership is implicit from DDS writer registration
- **Minimal interface**: Only identity and type

**Typical Implementation**:
```csharp
[DdsTopic("SST_EntityMaster")]
public struct EntityMasterTopic : INetworkMaster
{
    [DdsKey] public long EntityId { get; set; }
    public long TkbType { get; set; }
}
```

### ISerializationProvider

**Purpose**: Binary serialization for descriptor types (used in ghost stashing).

**Interface**:
```csharp
public interface ISerializationProvider
{
    int GetSize(object descriptor);
    void Encode(object descriptor, Span<byte> buffer);
    void Apply(Entity entity, ReadOnlySpan<byte> buffer, IEntityCommandBuffer cmd);
}

public interface ISerializationRegistry
{
    void Register(long descriptorOrdinal, ISerializationProvider provider);
    ISerializationProvider Get(long descriptorOrdinal);
    bool TryGet(long descriptorOrdinal, out ISerializationProvider provider);
}
```

**Use Case**: Ghost Stashing

When a descriptor arrives before entity master:
1. Calculate size: `provider.GetSize(descriptor)`
2. Allocate buffer
3. Serialize: `provider.Encode(descriptor, buffer)`
4. Store in ghost stash
5. Later: `provider.Apply(entity, buffer, cmd)` when entity created

---

## Supporting Types

### TkbTemplate

**Purpose**: Blueprint for spawning entities with components and network requirements.

**Key Members**:
```csharp
public class TkbTemplate
{
    public long TkbType { get; }
    public string Name { get; }
    
    public List<MandatoryDescriptor> MandatoryDescriptors { get; }
    public List<ChildBlueprintDefinition> ChildBlueprints { get; }
    
    public void AddComponent<T>(T component) where T : unmanaged;
    public void AddManagedComponent<T>(Func<T> factory) where T : class;
    
    public void ApplyTo(EntityRepository repo, Entity entity, bool preserveExisting = false);
    
    public bool AreHardRequirementsMet(IReadOnlyCollection<long> availableKeys);
    public bool AreAllRequirementsMet(IReadOnlyCollection<long> availableKeys, 
                                      uint currentFrame, uint identifiedAtFrame);
}
```

**Component Application**:
```csharp
var template = new TkbTemplate("Tank", tkbType: 1001);

// Add unmanaged components (value copied)
template.AddComponent(new Position { X = 0, Y = 0, Z = 0 });
template.AddComponent(new Health { Current = 100, Max = 100 });

// Add managed components (factory function)
template.AddManagedComponent(() => new AIBehaviorTree 
{ 
    TreeDef = "PatrolTree" 
});

// Apply to entity
Entity tank = repo.CreateEntity();
template.ApplyTo(repo, tank, preserveExisting: false);
```

**Mandatory Descriptor Tracking**:
```csharp
template.MandatoryDescriptors.Add(new MandatoryDescriptor
{
    PackedKey = PackedKey.Create(ordinal: 5, instanceId: 0), // GeoState descriptor
    IsHard = true,
    SoftTimeoutFrames = 0
});

// Check if requirements met
var availableKeys = new HashSet<long> { geoStateKey, weaponStateKey };
bool canActivate = template.AreHardRequirementsMet(availableKeys);
```

### MandatoryDescriptor

**Purpose**: Defines network descriptor requirements for entity activation.

**Structure**:
```csharp
public struct MandatoryDescriptor
{
    public long PackedKey;              // (Ordinal << 32) | InstanceId
    public bool IsHard;                 // Must have, or can timeout?
    public uint SoftTimeoutFrames;      // Timeout for soft requirements
}
```

**Hard vs Soft Requirements**:

| Type | Behavior | Use Case |
|------|----------|----------|
| **Hard** | Entity cannot activate without this descriptor | Critical data (position, master) |
| **Soft** | Entity activates after timeout if missing | Optional data (visual effects, audio) |

**Example**:
```csharp
// Hard requirement: EntityMaster (must have)
new MandatoryDescriptor
{
    PackedKey = PackedKey.Create(ordinal: -1, instanceId: 0),
    IsHard = true
}

// Soft requirement: WeaponState (timeout after 60 frames = 1 sec @ 60fps)
new MandatoryDescriptor
{
    PackedKey = PackedKey.Create(ordinal: 6, instanceId: 0),
    IsHard = false,
    SoftTimeoutFrames = 60
}
```

### ChildBlueprintDefinition

**Purpose**: Defines hierarchical entity relationships (parent-child).

**Structure**:
```csharp
public struct ChildBlueprintDefinition
{
    public int InstanceId { get; set; }      // Part ID within parent
    public long ChildTkbType { get; set; }   // Blueprint type for child
}
```

**Use Case**: Multi-Part Entities

```csharp
var tankTemplate = new TkbTemplate("Tank", tkbType: 1001);

// Define child entities (turrets, wheels, etc.)
tankTemplate.ChildBlueprints.Add(new ChildBlueprintDefinition
{
    InstanceId = 1,
    ChildTkbType = 2001  // Turret blueprint
});

tankTemplate.ChildBlueprints.Add(new ChildBlueprintDefinition
{
    InstanceId = 2,
    ChildTkbType = 2002  // Gun blueprint
});

// When tank spawns, children auto-spawn with:
// - Parent relationship
// - InstanceId for multi-instance descriptor routing
```

### PackedKey

**Purpose**: Efficient encoding of descriptor type + instance ID into single long.

**Layout**:
```
┌──────────────────────────┬──────────────────────────┐
│  High 32 bits: Ordinal   │ Low 32 bits: InstanceId  │
└──────────────────────────┴──────────────────────────┘
```

**API**:
```csharp
public static class PackedKey
{
    public static long Create(int ordinal, int instanceId);
    public static int GetOrdinal(long packedKey);
    public static int GetInstanceId(long packedKey);
    public static string ToString(long packedKey);
}
```

**Usage**:
```csharp
// Create packed key
long key = PackedKey.Create(ordinal: 5, instanceId: 0);
// Result: 0x0000000500000000

// Unpack
int ordinal = PackedKey.GetOrdinal(key);     // 5
int instanceId = PackedKey.GetInstanceId(key); // 0

// Debug string
string debug = PackedKey.ToString(key);  // "(Ord:5, Inst:0)"
```

**Benefits**:
- Single long comparison (fast lookups)
- Hashable (use as dictionary key)
- Sortable (natural ordering by ordinal, then instance)

### FdpDescriptorAttribute

**Purpose**: Marks types for automatic translator code generation.

**Definition**:
```csharp
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
public sealed class FdpDescriptorAttribute : Attribute
{
    public int Ordinal { get; }
    public string TopicName { get; }
    public bool IsMandatory { get; set; } = false;
}
```

**Usage**:
```csharp
[FdpDescriptor(ordinal: 5, topicName: "Tank_GeoState", IsMandatory = true)]
public struct GeoStateDescriptor
{
    [DdsKey] public long EntityId;
    public double Latitude;
    public double Longitude;
    public float Heading;
}

// Code generator creates:
// - IDescriptorTranslator implementation
// - DDS topic registration
// - Component mapping logic
```

---

## ASCII Diagrams

### Descriptor Translation Architecture

```
╔═══════════════════════════════════════════════════════════════╗
║                Network Layer (DDS Topics)                      ║
║  EntityMaster, GeoState, WeaponState, EntityState, ...         ║
╚═══════════════════════════════════════════════════════════════╝
                              ↕
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
        ▼                     ▼                     ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐
│ EntityMaster     │ │ GeoState         │ │ WeaponState      │
│ Translator       │ │ Translator       │ │ Translator       │
│  Ord: -1         │ │  Ord: 5          │ │  Ord: 6          │
│  Topic: Master   │ │  Topic: GeoState │ │  Topic: Weapon   │
└────────┬─────────┘ └────────┬─────────┘ └────────┬─────────┘
         │                    │                    │
         │ Implements         │ Implements         │ Implements
         │ IDescriptor        │ IDescriptor        │ IDescriptor
         │ Translator         │ Translator         │ Translator
         │                    │                    │
         └────────────────────┴────────────────────┘
                              │
                              ▼
         ┌────────────────────────────────────────┐
         │   Translator Registry (Map)            │
         │   Ordinal → IDescriptorTranslator      │
         │     -1 → EntityMasterTranslator        │
         │      5 → GeoStateTranslator            │
         │      6 → WeaponStateTranslator         │
         └────────────────┬───────────────────────┘
                          │
                          ▼
         ┌────────────────────────────────────────┐
         │       Network Ingress System           │
         │   foreach translator:                  │
         │     translator.PollIngress(cmd, view)  │
         └────────────────┬───────────────────────┘
                          │
                          ▼
         ┌────────────────────────────────────────┐
         │     EntityCommandBuffer (Deferred)     │
         │   - CreateEntity()                     │
         │   - AddComponent()                     │
         │   - RemoveComponent()                  │
         └────────────────┬───────────────────────┘
                          │ FlushCommands()
                          ▼
╔═══════════════════════════════════════════════════════════════╗
║           EntityRepository (ECS Components)                    ║
║  NetworkIdentity, Position, Velocity, Health, ...             ║
╚═══════════════════════════════════════════════════════════════╝
```

### TKB Template Application Flow

```
Network receives EntityMaster descriptor
              │
              ▼
┌─────────────────────────────────────────┐
│ 1. Lookup TkbTemplate by TkbType        │
│    ITkbDatabase.GetByType(tkbType)      │
└─────────────────┬───────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────┐
│ 2. Create entity in Constructing state  │
│    repo.CreateStagedEntity()            │
│    State: Constructing                  │
└─────────────────┬───────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────┐
│ 3. Wait for mandatory descriptors       │
│    Check: template.AreHardRequirementsMet│
│    - Hard: Must arrive                  │
│    - Soft: Timeout after N frames       │
└─────────────────┬───────────────────────┘
                  │
                  ▼
        ┌─────────┴─────────┐
        │ All met?          │
        └─────┬─────────┬───┘
              │ No      │ Yes
              │         │
              │         ▼
              │   ┌─────────────────────────────┐
              │   │ 4. Apply template components│
              │   │    template.ApplyTo(entity) │
              │   │    - Position               │
              │   │    - Velocity               │
              │   │    - Health                 │
              │   │    - AIBehaviorTree         │
              │   └─────────┬───────────────────┘
              │             │
              │             ▼
              │   ┌─────────────────────────────┐
              │   │ 5. Promote to Active        │
              │   │    repo.SetLifecycleState(  │
              │   │      entity, Active)        │
              │   └─────────────────────────────┘
              │
              └─→ Wait (check next frame)
```

### PackedKey Structure

```
64-bit PackedKey Layout:
┌────────────────────────────────┬────────────────────────────────┐
│   Bits 63-32 (High 32 bits)    │   Bits 31-0 (Low 32 bits)      │
│      Descriptor Ordinal        │        Instance ID             │
│         (int32)                │          (int32)               │
└────────────────────────────────┴────────────────────────────────┘

Example: GeoState descriptor, instance 0
Ordinal: 5
InstanceId: 0

Binary:
┌────────────────────────────────┬────────────────────────────────┐
│ 00000000 00000000 00000000 00000101 │ 00000000 00000000 00000000 00000000 │
└────────────────────────────────┴────────────────────────────────┘
                 5                                  0

Hexadecimal: 0x0000000500000000
Decimal: 21474836480

Usage in HashSet/Dictionary:
- Fast equality check (single long comparison)
- Natural sorting (Ordinal first, then InstanceId)
- Efficient hashing (64-bit hash code)
```

### Entity Lifecycle with Mandatory Descriptors

```
NETWORK PERSPECTIVE                     LOCAL ECS PERSPECTIVE
─────────────────────                   ─────────────────────

EntityMaster arrives                    
  (TkbType=1001)                        
         │                              
         ▼                              
┌────────────────────┐                  ┌──────────────────┐
│ DDS Topic:         │       ───────▶   │ Entity Created   │
│ SST_EntityMaster   │       Ingress    │ State: Ghost     │
└────────────────────┘                  │ (waiting)        │
                                        └────────┬─────────┘
GeoState arrives                                 │
  (Ord=5, Inst=0)                                │ Check requirements
         │                                       ▼
         ▼                              ┌──────────────────┐
┌────────────────────┐                  │ Hard Requirements│
│ DDS Topic:         │       ───────▶   │  ✅ EntityMaster │
│ Tank_GeoState      │       Ingress    │  ✅ GeoState     │
└────────────────────┘                  │  ⏳ WeaponState  │
                                        │  (soft, timeout) │
                                        └────────┬─────────┘
                                                 │ Wait...
WeaponState arrives                              │
  (Ord=6, Inst=0)                                │
         │                                       ▼
         ▼                              ┌──────────────────┐
┌────────────────────┐                  │ All Requirements │
│ DDS Topic:         │       ───────▶   │  ✅ Met!         │
│ SST_WeaponState    │       Ingress    └────────┬─────────┘
└────────────────────┘                           │
                                                 ▼
                                        ┌──────────────────┐
                                        │ Apply Template   │
                                        │  - Components    │
                                        │  - Children      │
                                        └────────┬─────────┘
                                                 │
         ┌───────────────────────────────────────┘
         │
         ▼
┌────────────────────┐                  ┌──────────────────┐
│ Entity Active on   │       ◀───────   │ Entity Active    │
│ Network            │       Egress     │ State: Active    │
└────────────────────┘                  │ Simulating...    │
                                        └──────────────────┘
```

---

## Source Code Analysis

### Key Files and Their Purposes

#### Abstractions (Interfaces)
- **IDescriptorTranslator.cs** (36 lines): Network ↔ ECS translation contract
- **ITkbDatabase.cs** (14 lines): Template registry interface
- **INetworkTopology.cs** (19 lines): Distributed node configuration
- **INetworkMaster.cs** (14 lines): Entity master descriptor contract
- **ISerializationProvider.cs** (33 lines): Binary serialization for ghost stashing

#### Supporting Types
- **TkbTemplate.cs** (155 lines): Entity blueprint implementation
- **MandatoryDescriptor.cs** (30 lines): Descriptor requirement definition
- **ChildBlueprintDefinition.cs** (21 lines): Hierarchical entity definition
- **PackedKey.cs** (27 lines): Descriptor key encoding utilities
- **FdpDescriptorAttribute.cs** (20 lines): Code generation marker

### Namespace Structure

```
FDP.Interfaces
└── Abstractions/
    ├── IDescriptorTranslator.cs
    ├── ITkbDatabase.cs
    ├── INetworkTopology.cs
    ├── INetworkMaster.cs
    ├── ISerializationProvider.cs
    ├── TkbTemplate.cs
    ├── MandatoryDescriptor.cs
    ├── ChildBlueprintDefinition.cs
    ├── PackedKey.cs
    └── FdpDescriptorAttribute.cs
```

**Total Source Lines**: ~369 lines (excluding comments/whitespace)

---

## Dependencies

### Project References

1. **Fdp.Kernel** (`../../Kernel/Fdp.Kernel/Fdp.Kernel.csproj`)
   - **Purpose**: ECS foundation (Entity, EntityRepository, ComponentSystem)
   - **Usage**: 
     - Entity type in IDescriptorTranslator.ApplyToEntity
     - TkbTemplate.ApplyTo uses EntityRepository
     - All abstractions reference kernel types

### NuGet Packages

**None** - FDP.Interfaces is dependency-minimal by design.

### Target Framework

- **.NET 8.0**
- **No unsafe code** (pure managed abstractions)

---

## Usage Examples

### Example 1: Implementing a Custom Translator

```csharp
using Fdp.Interfaces;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

public class HealthTranslator : IDescriptorTranslator
{
    private readonly DdsReader<HealthDescriptor> _reader;
    private readonly DdsWriter<HealthDescriptor> _writer;
    private readonly NetworkEntityMap _entityMap;
    
    public long DescriptorOrdinal => 10;
    public string TopicName => "Entity_Health";
    
    public HealthTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
    {
        _reader = new DdsReader<HealthDescriptor>(participant, TopicName);
        _writer = new DdsWriter<HealthDescriptor>(participant, TopicName);
        _entityMap = entityMap;
    }
    
    // INGRESS: Network → ECS
    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
    {
        foreach (var sample in _reader.Take())
        {
            long netId = sample.EntityId;
            
            // Map network ID to local entity
            if (!_entityMap.TryGetLocalEntity(netId, out Entity entity))
                continue; // Entity not created yet
            
            // Update component
            cmd.SetComponent(entity, new Health
            {
                Current = sample.CurrentHp,
                Max = sample.MaxHp
            });
        }
    }
    
    // EGRESS: ECS → Network
    public void ScanAndPublish(ISimulationView view)
    {
        var query = view.Query()
            .With<Health>()
            .With<NetworkIdentity>()
            .Build();
        
        foreach (var entity in query)
        {
            // Check authority
            if (!view.HasAuthority(entity, DescriptorOrdinal))
                continue;
            
            ref readonly var health = ref view.GetComponentRO<Health>(entity);
            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            
            _writer.Write(new HealthDescriptor
            {
                EntityId = netId.NetworkEntityId,
                CurrentHp = health.Current,
                MaxHp = health.Max
            });
        }
    }
    
    public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
    {
        var desc = (HealthDescriptor)data;
        repo.AddComponent(entity, new Health 
        { 
            Current = desc.CurrentHp, 
            Max = desc.MaxHp 
        });
    }
    
    public void Dispose(long networkEntityId)
    {
        _writer.DisposeInstance(new HealthDescriptor { EntityId = networkEntityId });
    }
}
```

### Example 2: Creating Entity Templates

```csharp
using Fdp.Interfaces;
using Fdp.Kernel;

public class TemplateFactory
{
    public static TkbTemplate CreateTankTemplate()
    {
        var template = new TkbTemplate("Tank", tkbType: 1001);
        
        // Add components
        template.AddComponent(new Position { X = 0, Y = 0, Z = 0 });
        template.AddComponent(new Velocity { X = 0, Y = 0, Z = 0 });
        template.AddComponent(new Health { Current = 100, Max = 100 });
        template.AddComponent(new Armor { Value = 50 });
        
        // Add managed component (behavior tree)
        template.AddManagedComponent(() => new AIBehaviorTree 
        { 
            TreeDefinition = "TankAI",
            Blackboard = new Dictionary<string, object>
            {
                ["AggressionLevel"] = 0.7f,
                ["PatrolRadius"] = 100f
            }
        });
        
        // Define mandatory descriptors
        template.MandatoryDescriptors.Add(new MandatoryDescriptor
        {
            PackedKey = PackedKey.Create(ordinal: 5, instanceId: 0), // GeoState
            IsHard = true,
            SoftTimeoutFrames = 0
        });
        
        template.MandatoryDescriptors.Add(new MandatoryDescriptor
        {
            PackedKey = PackedKey.Create(ordinal: 6, instanceId: 0), // WeaponState
            IsHard = false,
            SoftTimeoutFrames = 60  // 1 second @ 60fps
        });
        
        // Define child entities
        template.ChildBlueprints.Add(new ChildBlueprintDefinition
        {
            InstanceId = 1,
            ChildTkbType = 2001  // Turret
        });
        
        template.ChildBlueprints.Add(new ChildBlueprintDefinition
        {
            InstanceId = 2,
            ChildTkbType = 2002  // Gun
        });
        
        return template;
    }
}

// Usage
var template = TemplateFactory.CreateTankTemplate();
tkbDatabase.Register(template);

// Spawn entity from template
Entity tank = repo.CreateEntity();
template.ApplyTo(repo, tank, preserveExisting: false);
```

### Example 3: Implementing ITkbDatabase

```csharp
using System.Collections.Generic;
using System.Linq;
using Fdp.Interfaces;

public class InMemoryTkbDatabase : ITkbDatabase
{
    private readonly Dictionary<long, TkbTemplate> _byType = new();
    private readonly Dictionary<string, TkbTemplate> _byName = new();
    
    public void Register(TkbTemplate template)
    {
        if (_byType.ContainsKey(template.TkbType))
            throw new InvalidOperationException($"Template {template.TkbType} already registered");
        
        if (_byName.ContainsKey(template.Name))
            throw new InvalidOperationException($"Template '{template.Name}' already registered");
        
        _byType[template.TkbType] = template;
        _byName[template.Name] = template;
    }
    
    public TkbTemplate GetByType(long tkbType) => _byType[tkbType];
    
    public bool TryGetByType(long tkbType, out TkbTemplate template) 
        => _byType.TryGetValue(tkbType, out template);
    
    public TkbTemplate GetByName(string name) => _byName[name];
    
    public bool TryGetByName(string name, out TkbTemplate template) 
        => _byName.TryGetValue(name, out template);
    
    public IEnumerable<TkbTemplate> GetAll() => _byType.Values;
}
```

### Example 4: Network Topology Configuration

```csharp
using System.Collections.Generic;
using System.Linq;
using Fdp.Interfaces;

public class StaticTopology : INetworkTopology
{
    private readonly int _localNodeId;
    private readonly Dictionary<long, List<int>> _peerConfig;
    
    public int LocalNodeId => _localNodeId;
    
    public StaticTopology(int localNodeId)
    {
        _localNodeId = localNodeId;
        _peerConfig = new Dictionary<long, List<int>>();
    }
    
    public void ConfigurePeers(long tkbType, params int[] peerIds)
    {
        _peerConfig[tkbType] = new List<int>(peerIds);
    }
    
    public IEnumerable<int> GetExpectedPeers(long tkbType)
    {
        if (_peerConfig.TryGetValue(tkbType, out var peers))
            return peers.Where(id => id != _localNodeId); // Exclude self
        
        return Enumerable.Empty<int>();
    }
    
    public IEnumerable<int> GetAllNodes()
    {
        var allNodes = new HashSet<int> { _localNodeId };
        foreach (var peers in _peerConfig.Values)
            allNodes.UnionWith(peers);
        return allNodes;
    }
}

// Usage
var topology = new StaticTopology(localNodeId: 100);

// Tank entities require ACK from combat nodes
topology.ConfigurePeers(tkbType: 1001, peerIds: 100, 200, 300);

// Building entities only on construction nodes
topology.ConfigurePeers(tkbType: 2001, peerIds: 100, 150);

var peers = topology.GetExpectedPeers(tkbType: 1001);
// Node 100 sees: [200, 300] (excluding self)
```

### Example 5: Using PackedKey

```csharp
using Fdp.Interfaces;
using System.Collections.Generic;

public class DescriptorCache
{
    private readonly Dictionary<long, object> _descriptorCache = new();
    
    public void StoreDescriptor(int ordinal, int instanceId, object descriptor)
    {
        long key = PackedKey.Create(ordinal, instanceId);
        _descriptorCache[key] = descriptor;
    }
    
    public object GetDescriptor(int ordinal, int instanceId)
    {
        long key = PackedKey.Create(ordinal, instanceId);
        return _descriptorCache[key];
    }
    
    public bool HasDescriptor(long packedKey)
    {
        return _descriptorCache.ContainsKey(packedKey);
    }
    
    public void LogDescriptors()
    {
        foreach (var kvp in _descriptorCache)
        {
            Console.WriteLine($"Descriptor: {PackedKey.ToString(kvp.Key)}");
        }
    }
}

// Usage
var cache = new DescriptorCache();
cache.StoreDescriptor(ordinal: 5, instanceId: 0, new GeoStateDescriptor());
cache.StoreDescriptor(ordinal: 6, instanceId: 1, new WeaponStateDescriptor());

var geo = cache.GetDescriptor(ordinal: 5, instanceId: 0);
```

---

## Best Practices

### Interface Implementation

**✅ DO:**
- Keep translators stateless where possible
- Use dependency injection for services (entity map, topology)
- Implement proper disposal (DDS readers/writers)
- Batch network operations (don't write per-entity in tight loop)

**❌ DON'T:**
- Store entity references across frames (entities can be destroyed)
- Perform I/O in PollIngress/ScanAndPublish (breaks determinism)
- Throw exceptions for missing entities (gracefully skip)
- Allocate in hot path (use pooling or value types)

### Template Design

**✅ DO:**
- Use factories for managed components (fresh instance per spawn)
- Mark critical descriptors as hard requirements
- Set realistic soft timeouts (network latency + margin)
- Test template application with `preserveExisting = true`

**❌ DON'T:**
- Store mutable state in template components (use factories)
- Create circular child blueprints (infinite recursion)
- Skip mandatory descriptor validation
- Hardcode node IDs in templates (use topology)

### Network Topology

**✅ DO:**
- Load topology from configuration files (not hardcoded)
- Support dynamic peer addition/removal if needed
- Validate peer lists at startup
- Log topology configuration for debugging

**❌ DON'T:**
- Include local node in expected peers (causes deadlock)
- Return null from GetExpectedPeers (return empty collection)
- Modify topology during runtime without synchronization
- Assume symmetric topology (A expects B ≠ B expects A)

---

## Design Principles

### 1. Dependency Minimalism

**Principle**: Keep this layer dependency-free except for Fdp.Kernel.

**Rationale**:
- Interfaces should be stable (breaking changes cascade)
- Enables testability (easy to mock)
- Allows alternative implementations (not locked to DDS)

**Implementation**:
- No NuGet packages
- No platform-specific types
- No concrete implementations (only abstractions)

### 2. Protocol Agnosticism

**Principle**: Abstractions should not expose protocol-specific details.

**Rationale**:
- DDS is one implementation, not the only one
- Future: gRPC, MQTT, custom UDP, etc.
- Testing with in-memory fake implementations

**Implementation**:
- IDescriptorTranslator doesn't mention DDS
- Network types are `object` (not `DdsTopic`)
- Topology is abstract (not "DDS Domain")

### 3. Separation of Concerns

**Principle**: Each interface addresses a single responsibility.

**Rationale**:
- Single Responsibility Principle
- Composability (mix and match implementations)
- Testability (mock one concern at a time)

**Implementation**:
- IDescriptorTranslator: Network ↔ ECS translation only
- ITkbDatabase: Template storage only
- INetworkTopology: Peer configuration only

### 4. Explicit Over Implicit

**Principle**: Make network requirements visible and trackable.

**Rationale**:
- Debugging distributed systems is hard
- Explicit requirements enable validation
- Timeouts prevent indefinite waits

**Implementation**:
- MandatoryDescriptor explicitly lists requirements
- Hard vs soft distinction (clear semantics)
- PackedKey makes descriptor identity explicit

---

## Relationships to Other Projects

### Implementers (Projects that implement these interfaces)

1. **ModuleHost.Core** (`ModuleHost/ModuleHost.Core`)
   - Implements ITkbDatabase (in-memory database)
   - Uses IDescriptorTranslator for network module
   - Provides INetworkTopology configuration

2. **ModuleHost.Network.Cyclone** (`ModuleHost/ModuleHost.Network.Cyclone`)
   - **Key implementer of IDescriptorTranslator**:
     - EntityMasterTranslator
     - EntityStateTranslator
     - AutoCycloneTranslator<T>
     - MultiInstanceCycloneTranslator<T>
     - CycloneNativeEventTranslator<T>
   - Implements ISerializationProvider for ghost stashing

3. **FDP.Toolkit.Lifecycle** (`Toolkits/FDP.Toolkit.Lifecycle`)
   - Uses ITkbDatabase for entity construction
   - Validates MandatoryDescriptor requirements

4. **FDP.Toolkit.Replication** (`Toolkits/FDP.Toolkit.Replication`)
   - Uses INetworkTopology for peer coordination
   - Maintains NetworkEntityMap (network ID ↔ local entity)

5. **Fdp.Examples.NetworkDemo** (`Examples/Fdp.Examples.NetworkDemo`)
   - Custom translators (FastGeodeticTranslator, WeaponStateTranslator)
   - Template definitions for tanks, buildings
   - Topology configuration for multi-node scenario

### Consumers (Projects that use these abstractions)

All projects in the FDP ecosystem that integrate networking consume FDP.Interfaces:
- **All Toolkits**: Reference for network-aware features
- **All Examples**: Use for networking and blueprints

### Key Integration Patterns

#### Pattern 1: Translator Pattern
- **Description**: All IDescriptorTranslator implementations follow consistent structure
- **Seen in**: ModuleHost.Network.Cyclone, Examples
- **Document needed**: `relationships/Translator-Pattern.md`
- **Key aspects**:
  - Bidirectional translation (ingress + egress)
  - Authority-based filtering
  - PackedKey usage for multi-instance

#### Pattern 2: Entity Lifecycle Management
- **Description**: TkbTemplate + MandatoryDescriptor enable phased entity construction
- **Seen in**: FDP.Toolkit.Lifecycle, ModuleHost.Network.Cyclone
- **Document needed**: `relationships/Entity-Lifecycle-Complete.md`
- **Key aspects**:
  - Ghost → Constructing → Active states
  - Hard vs soft requirements
  - Timeout mechanism

#### Pattern 3: Hierarchical Entity Construction
- **Description**: ChildBlueprintDefinition enables parent-child relationships
- **Seen in**: TkbTemplate, ModuleHost.Network.Cyclone
- **Key aspects**:
  - Auto-spawning of children
  - InstanceId routing for multi-instance descriptors

---

## API Reference

### IDescriptorTranslator

```csharp
namespace Fdp.Interfaces
{
    public interface IDescriptorTranslator
    {
        long DescriptorOrdinal { get; }
        string TopicName { get; }
        
        void PollIngress(IEntityCommandBuffer cmd, ISimulationView view);
        void ScanAndPublish(ISimulationView view);
        void ApplyToEntity(Entity entity, object data, EntityRepository repo);
        void Dispose(long networkEntityId);
    }
}
```

### ITkbDatabase

```csharp
namespace Fdp.Interfaces
{
    public interface ITkbDatabase
    {
        void Register(TkbTemplate template);
        
        TkbTemplate GetByType(long tkbType);
        bool TryGetByType(long tkbType, out TkbTemplate template);
        
        TkbTemplate GetByName(string name);
        bool TryGetByName(string name, out TkbTemplate template);
        
        IEnumerable<TkbTemplate> GetAll();
    }
}
```

### INetworkTopology

```csharp
namespace Fdp.Interfaces
{
    public interface INetworkTopology
    {
        int LocalNodeId { get; }
        IEnumerable<int> GetExpectedPeers(long tkbType);
        IEnumerable<int> GetAllNodes();
    }
}
```

### TkbTemplate

```csharp
namespace Fdp.Interfaces
{
    public class TkbTemplate
    {
        public TkbTemplate(string name, long tkbType);
        
        public long TkbType { get; }
        public string Name { get; }
        public List<MandatoryDescriptor> MandatoryDescriptors { get; }
        public List<ChildBlueprintDefinition> ChildBlueprints { get; }
        
        public void AddComponent<T>(T component) where T : unmanaged;
        public void AddManagedComponent<T>(Func<T> factory) where T : class;
        public void ApplyTo(EntityRepository repo, Entity entity, bool preserveExisting = false);
        
        public bool AreHardRequirementsMet(IReadOnlyCollection<long> availableKeys);
        public bool AreAllRequirementsMet(IReadOnlyCollection<long> availableKeys, 
                                          uint currentFrame, uint identifiedAtFrame);
    }
}
```

### PackedKey

```csharp
namespace Fdp.Interfaces
{
    public static class PackedKey
    {
        public static long Create(int ordinal, int instanceId);
        public static int GetOrdinal(long packedKey);
        public static int GetInstanceId(long packedKey);
        public static string ToString(long packedKey);
    }
}
```

---

## Known Issues & Limitations

### Current Limitations

1. **No Multi-Level Hierarchy**
   - ChildBlueprints support one level (parent → child)
   - No grandchildren support (child → grandchild)
   - **Workaround**: Flatten hierarchy or use recursive template application

2. **No Dynamic Descriptor Registration**
   - Descriptor ordinals must be compile-time constants
   - Cannot add translators at runtime (affects hot reload)
   - **Rationale**: Performance (no dictionary lookups), determinism

3. **Object-Typed Descriptor Data**
   - IDescriptorTranslator.ApplyToEntity uses `object data`
   - Requires runtime casting (boxing for value types)
   - **Rationale**: Type erasure for interface generality

4. **No Descriptor Dependency Graph**
   - MandatoryDescriptors are flat list
   - Cannot express "Descriptor B requires Descriptor A first"
   - **Workaround**: Application logic handles ordering

5. **Hard Requirement Deadlock Risk**
   - If hard descriptor never arrives, entity stuck in Constructing
   - No automatic timeout for hard requirements
   - **Mitigation**: Use soft requirements with generous timeouts

### Known Bugs

**None documented** as of February 10, 2026.

### Future Enhancement Areas

1. **Generic IDescriptorTranslator<T>**: Avoid object casting
2. **Descriptor Dependency Graph**: Express ordering constraints
3. **Dynamic Translator Registration**: Runtime descriptor addition
4. **Multi-Level Hierarchies**: Recursive child blueprints
5. **Template Versioning**: Schema evolution for blueprints

---

## References

### Internal Documentation

- [Fdp.Kernel](../core/Fdp.Kernel.md)
- [ModuleHost.Core](../modulehost/ModuleHost.Core.md) (To be created)
- [ModuleHost.Network.Cyclone](../modulehost/ModuleHost.Network.Cyclone.md) (To be created)

### Related Project Documents

- [FDP.Toolkit.Lifecycle](../toolkits/FDP.Toolkit.Lifecycle.md) (To be created)
- [FDP.Toolkit.Replication](../toolkits/FDP.Toolkit.Replication.md) (To be created)
- [Fdp.Examples.NetworkDemo](../examples/Fdp.Examples.NetworkDemo.md) (To be created)

### Relationship Documents

- [Translator Pattern Architecture](../relationships/Translator-Pattern.md) (To be created)
- [Entity Lifecycle Complete](../relationships/Entity-Lifecycle-Complete.md) (To be created)
- [Network Replication Architecture](../relationships/Network-Replication.md) (To be created)

### Architecture Documents

- [ModuleHost Network ELM Design](../../ModuleHost/docs/ModuleHost-network-ELM-design-talk.md)
- [NetworkDemo Tank Design](../../Examples/Fdp.Examples.NetworkDemo/docs/TANK-DESIGN.md)

---

**Document Version**: 1.0  
**Lines**: 1368  
**Maintainer**: FDP Documentation Team  
**Next Review**: March 2026
