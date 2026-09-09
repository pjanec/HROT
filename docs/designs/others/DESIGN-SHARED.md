# Shared Components Design

**Version:** 2.0 (Infrastructure Audit Complete)  
**Date:** 2026-02-13  
**Status:** Ready for Implementation

**⚠️ INFRASTRUCTURE AUDIT:** This document reflects comprehensive audit of existing FDP infrastructure. Components marked ✅ EXIST, components marked ❌ require NEW implementation.

**Parent Document**: [Overall Design](./DESIGN-OVERALL.md)

## Table of Contents

1. [Infrastructure Status Matrix](#1-infrastructure-status-matrix)
2. [Overview](#2-overview)
3. [Existing FDP Infrastructure (Reuse)](#3-existing-fdp-infrastructure-reuse)
4. [New Components (Implement)](#4-new-components-implement)
5. [Hrot.NED](#5-hrotddsdata model)
6. [Hrot.Map.Common](#6-hrotmap common)
7. [Implementation Plan](#7-implementation-plan)

---

## 1. Infrastructure Status Matrix

| Component | Status | Location | Purpose |
|-----------|--------|----------|---------|
| **ID Allocation** | ✅ EXISTS | `FDP.Toolkit.Replication.BlockIdManager`<br>`ModuleHost.Network.Cyclone.DdsIdAllocator` | Client-side ID buffering<br>Server-side ID allocation |
| **TKB Database** | ✅ EXISTS | `FDP.Toolkit.Tkb.TkbDatabase`<br>`FDP.Interfaces.TkbTemplate` | Template storage<br>Blueprint with descriptor requirements |
| **Geographic Transforms** | ✅ EXISTS | `Fdp.Toolkit.Geographic.WGS84Transform` | WGS84 Lat/Lon ↔ Flat Cartesian |
| **Time Sync** | ✅ EXISTS | `FDP.Toolkit.Time.*` | Master/Slave/Stepped controllers |
| **Entity Lifecycle** | ✅ EXISTS | `FDP.Toolkit.Lifecycle.EntityLifecycleModule` | Constructing→Active→Tear Down states |
| **Network Replication** | ✅ EXISTS | `FDP.Toolkit.Replication.NetworkEntityMap`<br>`SmartEgressSystem` | Network ID↔Entity mapping<br>DDS egress with delta tracking |
| **DDS Egress** | ✅ EXISTS | `ModuleHost.Network.Cyclone.CycloneEgressSystem` | Direct CycloneDDS publisher |
| **DER (IOS)** | ❌ NEW | `FDP.Toolkit.DER.*` | Non-ECS entity access for IOS |
| **Commands Toolkit** | ❌ NEW | `FDP.Toolkit.Commands.*` | RPC-over-DDS with correlation |
| **TKB Extensions (BDC)** | ❌ NEW | `Hrot.Map.Definitions.*` | Domain-specific descriptors |

**Key Insight**: Most infrastructure EXISTS. Focus implementation on DER, Commands, and domain TKB extensions.

---

## 2. Overview

---

## 2. Overview

### 2.1 Purpose

This document describes the **shared infrastructure** for IOS-IG-SimHost mocks. After comprehensive infrastructure audit, most required components **already exist** in FDP. Priority is implementing three NEW components:

1. **FDP.Toolkit.DER**: Dynamic Entity Repository for IOS (non-ECS entity access)
2. **FDP.Toolkit.Commands**: RPC-over-DDS toolkit (request/ack correlation)
3. **Hrot.Map.Definitions**: Domain-specific TKB descriptors (visual, physics, combat)

### 2.2 Design Principles

1. **Reuse Existing Infrastructure**: BlockIdManager, TkbDatabase, WGS84Transform, TimeControllers, EntityLifecycleModule
2. **Minimal New Code**: Only implement DER, Commands, and domain TKB extensions
3. **Type Safety**: Leverage existing FDP patterns and IDL→C# codegen
4. **Incremental Integration**: Test existing components first, then add new ones

### 2.3 Critical Dependencies

### 2.3 Critical Dependencies

**Build Order:**
1. Data Model (Hrot.NED) - FIRST
2. Existing Infrastructure Validation - SECOND
3. New Components (DER, Commands, TKB Extensions) - THIRD
4. Subsystem Mocks (IOS, IG, SimHost) - FOURTH

---

## 3. Existing FDP Infrastructure (Reuse)

### 3.1 ID Allocation

**✅ VERIFIED EXISTS** - No implementation needed

**Components:**
- `FDP.Toolkit.Replication.BlockIdManager` (client-side buffering)
- `ModuleHost.Network.Cyclone.DdsIdAllocator` (server-side allocation)

**Usage Pattern:**
```csharp
// Client-side (for CreateEntityRequest)
var idManager = fdpWorld.GetModule<BlockIdManager>();
int newId = await idManager.AllocateIdAsync();

// Server-side (SimHost/IG)
var allocator = new DdsIdAllocator(participant, "IdAllocator");
allocator.Start();
```

**Tests:** `IdAllocationTests.cs`, `DdsIdAllocatorTests.cs`

### 3.2 TKB System

**✅ VERIFIED EXISTS** - Domain extensions needed (see Section 4.3)

**Components:**
- `FDP.Interfaces.TkbTemplate` (blueprint model)
- `FDP.Toolkit.Tkb.TkbDatabase` (template storage)

**Existing Template Structure:**
```csharp
public class TkbTemplate
{
    public long TkbType { get; set; }
    public string Name { get; set; }
    public List<Type> MandatoryDescriptors { get; set; }
    public List<TkbBlueprintChild> ChildBlueprints { get; set; }
    
    public bool AreHardRequirementsMet(IEntity entity);
    public bool AreAllRequirementsMet(IEntity entity);
}
```

**Usage:**
```csharp
var tkbDb = fdpWorld.GetModule<TkbDatabase>();
var template = tkbDb.GetTemplate(TkbEntityTypes.Tank_M1Abrams);
var entity = world.NewEntity(template);
```

### 3.3 Geographic Transforms

**✅ VERIFIED EXISTS** - Production-ready

**Component:** `Fdp.Toolkit.Geographic.WGS84Transform`

**Features:**
- WGS84 ellipsoid calculations (a=6378137m, e²=0.00669438)
- Lat/Lon/Alt ↔ Flat Cartesian projection
- Configurable origin point

**Usage:**
```csharp
using Fdp.Toolkit.Geographic;

var origin = new GeoPoint { Latitude = 50.0755, Longitude = 14.4378, Altitude = 200.0 };
var transform = new WGS84Transform(origin);

// DDS GeoPoint → ECS Cartesian
var geo = new GeoPoint { Latitude = 50.08, Longitude = 14.45, Altitude = 250 };
var cart = transform.ToCartesian(geo);

// ECS Cartesian → DDS GeoPoint
var geoOut = transform.ToGeodetic(cart);
```

### 3.4 Time Synchronization

**✅ VERIFIED EXISTS** - Multiple modes supported

**Components:** `FDP.Toolkit.Time.*`
- `MasterTimeController` (authority, publishes time)
- `SlaveTimeController` (follows master via DDS)
- `SteppedTimeController` (pause/step for debugging)

**Usage:**
```csharp
// SimHost (master)
var timeController = new MasterTimeController();
world.AddModule(timeController);

// IOS/IG (slaves)
var timeController = new SlaveTimeController(ddsParticipant);
world.AddModule(timeController);
```

### 3.5 Entity Lifecycle

**✅ VERIFIED EXISTS** - State machine built-in

**Component:** `FDP.Toolkit.Lifecycle.EntityLifecycleModule`

**State Flow:**
```
Constructing → Active → TearDown → Disposed
```

**Usage:**
```csharp
world.AddModule<EntityLifecycleModule>();

// Entity creation
var entity = world.NewEntity();
entity.Set(new ConstructingTag()); // Automatic → Active transition

// Entity deletion
entity.Set(new TearDownTag()); // Graceful cleanup
```

### 3.6 Network Replication

**✅ VERIFIED EXISTS** - Full DDS integration

**Components:**
- `FDP.Toolkit.Replication.NetworkEntityMap` (ID ↔ Entity mapping)
- `FDP.Toolkit.Replication.SmartEgressSystem` (delta tracking)
- `ModuleHost.Network.Cyclone.CycloneEgressSystem` (DDS publisher)

**Features:**
- Network ID (int) ↔ Local Entity mapping
- Graveyard cleanup (60-frame default TTL)
- Ownership tracking via DDS sample metadata
- Delta compression for bandwidth optimization

**Usage:**
```csharp
// Setup network mapping
var entityMap = new NetworkEntityMap(graveyardDurationFrames: 60);
world.AddModule(entityMap);

// DDS ingress (EntityMaster received)
int networkId = entityMasterSample.EntityId;
var entity = entityMap.GetOrCreateEntity(networkId);

// DDS egress (publish EntityMaster)
var egressSystem = new CycloneEgressSystem<EntityMaster>(participant, "EntityMaster");
world.AddSystem(egressSystem);
```

---

## 4. New Components (Implement)

### 4.1 FDP.Toolkit.DER (Dynamic Entity Repository)

**Purpose:** Provide non-ECS entity access for IOS Mock (no Flecs dependency).

**Why Needed:** IOS Mock uses pure ImGui panels with DDS translators. No ECS world, so needs alternative entity storage.

**Architecture:**

```csharp
namespace FDP.Toolkit.DER
{
    /// <summary>
    /// Non-ECS entity repository for IOS Mock.
    /// Thread-safe dictionary-based storage.
    /// </summary>
    public interface IDerRepo
    {
        IDerEntity? GetEntity(long entityId);
        IEnumerable<IDerEntity> GetAllEntities();
        IDerEntity CreateEntity(long entityId, long tkbType);
        void DeleteEntity(long entityId);
        
        event Action<IDerEntity> EntityCreated;
        event Action<IDerEntity> EntityDeleted;
    }
    
    /// <summary>
    /// DER entity with descriptor storage.
    /// </summary>
    public interface IDerEntity
    {
        long EntityId { get; }
        long TkbType { get; }
        
        T? GetDescriptor<T>() where T : class, IDerDescriptor;
        void SetDescriptor<T>(T descriptor) where T : class, IDerDescriptor;
        bool HasDescriptor<T>() where T : class, IDerDescriptor;
        IEnumerable<Type> GetAllDescriptorTypes();
    }
    
    /// <summary>
    /// Marker interface for DER descriptors.
    /// </summary>
    public interface IDerDescriptor
    {
        long EntityId { get; set; }
        int Version { get; set; }
    }
}
```

**Implementation:****

```csharp
public class DerRepo : IDerRepo
{
    private readonly ConcurrentDictionary<long, DerEntity> _entities = new();
    
    public event Action<IDerEntity> EntityCreated;
    public event Action<IDerEntity> EntityDeleted;
    
    public IDerEntity? GetEntity(long entityId)
    {
        return _entities.TryGetValue(entityId, out var entity) ? entity : null;
    }
    
    public IEnumerable<IDerEntity> GetAllEntities()
    {
        return _entities.Values;
    }
    
    public IDerEntity CreateEntity(long entityId, long tkbType)
    {
        var entity = new DerEntity(entityId, tkbType);
        if (!_entities.TryAdd(entityId, entity))
            throw new InvalidOperationException($"Entity {entityId} already exists");
        
        EntityCreated?.Invoke(entity);
        return entity;
    }
    
    public void DeleteEntity(long entityId)
    {
        if (_entities.TryRemove(entityId, out var entity))
        {
            EntityDeleted?.Invoke(entity);
        }
    }
}

public class DerEntity : IDerEntity
{
    private readonly ConcurrentDictionary<Type, IDerDescriptor> _descriptors = new();
    
    public long EntityId { get; }
    public long TkbType { get; }
    
    public DerEntity(long entityId, long tkbType)
    {
        EntityId = entityId;
        TkbType = tkbType;
    }
    
    public T? GetDescriptor<T>() where T : class, IDerDescriptor
    {
        return _descriptors.TryGetValue(typeof(T), out var desc) ? desc as T : null;
    }
    
    public void SetDescriptor<T>(T descriptor) where T : class, IDerDescriptor
    {
        descriptor.EntityId = EntityId;
        _descriptors[typeof(T)] = descriptor;
    }
    
    public bool HasDescriptor<T>() where T : class, IDerDescriptor
    {
        return _descriptors.ContainsKey(typeof(T));
    }
    
    public IEnumerable<Type> GetAllDescriptorTypes()
    {
        return _descriptors.Keys;
    }
}
```

**DDS Integration (IOS):**

```csharp
// EntityMaster ingress → DER
public class EntityMasterIngressTranslator
{
    private readonly IDerRepo _repo;
    
    public void OnEntityMasterReceived(EntityMaster sample, SampleInfo info)
    {
        if (info.InstanceState == InstanceState.Disposed)
        {
            _repo.DeleteEntity(sample.EntityId);
        }
        else
        {
            var entity = _repo.GetEntity(sample.EntityId) 
                         ?? _repo.CreateEntity(sample.EntityId, sample.TkbType);
            
            entity.SetDescriptor(new DerEntityMaster
            {
                TkbType = sample.TkbType,
                DisType = sample.DisType,
                Flags = sample.Flags,
                OwnerId = info.PublicationHandle // From DDS metadata
            });
        }
    }
}
```

**Assembly Output:**
- **Namespace**: `FDP.Toolkit.DER`
- **Assembly**: `FDP.Toolkit.DER.dll`
- **Dependencies**: None (pure C#, no Flecs)

---

### 4.2 FDP.Toolkit.Commands (RPC-over-DDS)

**Purpose:** Provide async/await RPC pattern over DDS request/ack topics.

**Why Needed:** CreateEntityRequest/Ack, MissionControlRequest/Ack patterns need correlation and timeout handling.

**Architecture:**

```csharp
namespace FDP.Toolkit.Commands
{
    /// <summary>
    /// Generic DDS command client with correlation.
    /// </summary>
    public class DdsCommandClient<TRequest, TAck> where TRequest : struct where TAck : struct
    {
        private readonly DomainParticipant _participant;
        private readonly DataWriter<TRequest> _requestWriter;
        private readonly DataReader<TAck> _ackReader;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<TAck>> _pending = new();
        
        public DdsCommandClient(DomainParticipant participant, string requestTopic, string ackTopic)
        {
            _participant = participant;
            _requestWriter = CreateWriter<TRequest>(requestTopic);
            _ackReader = CreateReader<TAck>(ackTopic);
            
            // Start ACK listener
            Task.Run(AckListenerLoop);
        }
        
        public async Task<TAck> SendAsync(TRequest request, int timeoutMs = 5000)
        {
            var correlationId = ExtractCorrelationId(request);
            var tcs = new TaskCompletionSource<TAck>();
            _pending[correlationId] = tcs;
            
            _requestWriter.Write(request);
            
            using var cts = new CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => tcs.TrySetCanceled());
            
            try
            {
                return await tcs.Task;
            }
            finally
            {
                _pending.TryRemove(correlationId, out _);
            }
        }
        
        private async Task AckListenerLoop()
        {
            while (true)
            {
                var samples = await _ackReader.TakeAsync();
                foreach (var sample in samples)
                {
                    if (sample.Info.ValidData)
                    {
                        var correlationId = ExtractCorrelationId(sample.Data);
                        if (_pending.TryRemove(correlationId, out var tcs))
                        {
                            tcs.SetResult(sample.Data);
                        }
                    }
                }
            }
        }
        
        // Reflection-based correlation ID extraction
        private Guid ExtractCorrelationId(object message)
        {
            var prop = message.GetType().GetProperty("RequestId");
            return (Guid)prop?.GetValue(message)!;
        }
    }
}
```

**BDC-Specific Gateway:**

```csharp
namespace Hrot.Map.Common.Commands
{
    /// <summary>
    /// Convenience gateway for BDC SST commands.
    /// </summary>
    public class BdcCommandGateway
    {
        private readonly DdsCommandClient<CreateEntityRequest, CreateEntityAck> _createEntity;
        private readonly DdsCommandClient<UpdateEntityDescriptorRequest, UpdateEntityDescriptorAck> _updateDescriptor;
        private readonly DdsCommandClient<MissionControlRequest, MissionControlAck> _missionControl;
        
        public BdcCommandGateway(DomainParticipant participant)
        {
            _createEntity = new(participant, "CreateEntityRequest", "CreateEntityAck");
            _updateDescriptor = new(participant, "UpdateEntityDescriptorRequest", "UpdateEntityDescriptorAck");
            _missionControl = new(participant, "MissionControlRequest", "MissionControlAck");
        }
        
        public async Task<CreateEntityAck> CreateEntityAsync(CreateEntityRequest request, int timeoutMs = 5000)
        {
            return await _createEntity.SendAsync(request, timeoutMs);
        }
        
        public async Task<UpdateEntityDescriptorAck> UpdateDescriptorAsync(UpdateEntityDescriptorRequest request)
        {
            return await _updateDescriptor.SendAsync(request);
        }
        
        public async Task<MissionControlAck> SendMissionCommandAsync(MissionControlRequest request)
        {
            return await _missionControl.SendAsync(request);
        }
    }
}
```

**Usage Example (IOS Mock):**

```csharp
var gateway = new BdcCommandGateway(_participant);

var request = new CreateEntityRequest
{
    RequestId = Guid.NewGuid(),
    Owner = new NodeId { AppDomainId = 1, AppInstanceId = 1 },
    Flags = 0,
    InitialDescriptors = new List<EntityDescriptorUnion>
    {
        new() { /* EntityMaster */ },
        new() { /* EntityInfo */ }
    }
};

try
{
    var ack = await gateway.CreateEntityAsync(request, timeoutMs: 3000);
    if (ack.ErrorCode == 0)
    {
        Console.WriteLine($"Entity created: ID={ack.NewEntityId}");
    }
}
catch (TaskCanceledException)
{
    Console.WriteLine("Request timed out");
}
```

**Assembly Output:**
- **Namespace**: `FDP.Toolkit.Commands`, `Hrot.Map.Common.Commands`
- **Assembly**: `FDP.Toolkit.Commands.dll`, `Hrot.Map.Common.dll`
- **Dependencies**: `CycloneDDS`, `Hrot.NED`

---

### 4.3 Hrot.Map.Definitions (TKB Extensions)

**Purpose:** Domain-specific TKB descriptor classes for BDC SST.

**Why Needed:** TKB system is generic. Need descriptors for visual (IG), physics (SimHost), and combat (future) properties.

**Architecture:**

```csharp
namespace Hrot.Map.Definitions.Tkb
{
    /// <summary>
    /// IG visual properties (color, symbol, 3D model).
    /// </summary>
    public class IgVisualDef : IManagedComponent
    {
        public string SymbolCode { get; set; } = "SFGPUCIZ-------"; // MIL-STD-2525
        public string ModelPath { get; set; } = "models/default.obj";
        public string ColorHex { get; set; } = "#FFFFFF";
        public float Scale { get; set; } = 1.0f;
        public bool ShowLabel { get; set; } = true;
    }
    
    /// <summary>
    /// SimHost physics properties (mass, dimensions, mobility).
    /// </summary>
    public class SimVehicleDef : IManagedComponent
    {
        public float Mass { get; set; } // kg
        public float Length { get; set; } // meters
        public float Width { get; set; } // meters
        public float Height { get; set; } // meters
        public float MaxSpeed { get; set; } // m/s
        public float Acceleration { get; set; } // m/s²
        public float TurnRate { get; set; } // deg/s
        public TerrainMobility Mobility { get; set; }
    }
    
    public enum TerrainMobility
    {
        Tracked, Wheeled, Infantry, Air, Naval
    }
    
    /// <summary>
    /// Combat properties (weapons, armor, sensors).
    /// </summary>
    public class SimCombatDef : IManagedComponent
    {
        public float ArmorFront { get; set; } // mm RHA equivalent
        public float ArmorSide { get; set; }
        public float ArmorRear { get; set; }
        public List<WeaponMount> Weapons { get; set; } = new();
        public float SensorRange { get; set; } // meters
    }
    
    public struct WeaponMount
    {
        public string WeaponType { get; set; } // "120mm_APFSDS", "7.62mm_MG"
        public int Ammunition { get; set; }
        public float Range { get; set; } // meters
        public float RateOfFire { get; set; } // rounds/min
    }
    
    /// <summary>
    /// Composite unit (ORBAT) definition with subordinates.
    /// </summary>
    public class TkbCompositionDef : IManagedComponent
    {
        public List<TkbChildSlot> Subordinates { get; set; } = new();
    }
    
    public struct TkbChildSlot
    {
        public long TkbType { get; set; } // Required child type
        public int Count { get; set; } // How many (e.g., 4 tanks)
        public string RoleTag { get; set; } // "Tank", "Infantry", "Artillery"
    }
}
```

**TKB Builder Fluent API:**

```csharp
namespace Hrot.Map.Definitions.Tkb
{
    public class BdcTkbBuilder
    {
        private readonly TkbDatabase _db;
        
        public BdcTkbBuilder(TkbDatabase db)
        {
            _db = db;
        }
        
        public BdcTkbBuilder DefineVehicle(long tkbId, string name)
        {
            var template = new TkbTemplate
            {
                TkbType = tkbId,
                Name = name,
                MandatoryDescriptors = new List<Type>
                {
                    typeof(EntityMasterComponent),
                    typeof(EntityInfoComponent),
                    typeof(WorldPosComponent)
                }
            };
            
            _db.RegisterTemplate(template);
            return this;
        }
        
        public BdcTkbBuilder WithVisual(long tkbId, Action<IgVisualDef> configure)
        {
            var template = _db.GetTemplate(tkbId);
            var visualDef = new IgVisualDef();
            configure(visualDef);
            template.AddManagedComponent(visualDef);
            return this;
        }
        
        public BdcTkbBuilder WithPhysics(long tkbId, Action<SimVehicleDef> configure)
        {
            var template = _db.GetTemplate(tkbId);
            var physicsDef = new SimVehicleDef();
            configure(physicsDef);
            template.AddManagedComponent(physicsDef);
            return this;
        }
        
        public BdcTkbBuilder WithCombat(long tkbId, Action<SimCombatDef> configure)
        {
            var template = _db.GetTemplate(tkbId);
            var combatDef = new SimCombatDef();
            configure(combatDef);
            template.AddManagedComponent(combatDef);
            return this;
        }
    }
}
```

**TKB Registration Example:**

```csharp
var tkbDb = world.GetModule<TkbDatabase>();
var builder = new BdcTkbBuilder(tkbDb);

builder
    .DefineVehicle(TkbEntityTypes.Tank_M1Abrams, "M1 Abrams")
    .WithVisual(TkbEntityTypes.Tank_M1Abrams, v =>
    {
        v.SymbolCode = "SFGPUCIZ-------";
        v.ModelPath = "models/m1_abrams.obj";
        v.ColorHex = "#2E4057";
        v.Scale = 1.2f;
    })
    .WithPhysics(TkbEntityTypes.Tank_M1Abrams, p =>
    {
        p.Mass = 61_000; // kg
        p.Length = 7.93f; // meters
        p.Width = 3.66f;
        p.Height = 2.44f;
        p.MaxSpeed = 20.0f; // m/s (~45 mph)
        p.Acceleration = 2.5f;
        p.TurnRate = 15.0f;
        p.Mobility = TerrainMobility.Tracked;
    })
    .WithCombat(TkbEntityTypes.Tank_M1Abrams, c =>
    {
        c.ArmorFront = 600; // mm RHA equivalent
        c.ArmorSide = 350;
        c.ArmorRear = 200;
        c.Weapons.Add(new WeaponMount
        {
            WeaponType = "120mm_M256",
            Ammunition = 42,
            Range = 3000,
            RateOfFire = 6
        });
        c.SensorRange = 8000; // meters
    });
```

**Assembly Output:**
- **Namespace**: `Hrot.Map.Definitions.Tkb`
- **Assembly**: `Hrot.Map.Definitions.dll`
- **Dependencies**: `FDP.Interfaces`, `FDP.Toolkit.Tkb`

---

## 5. Hrot.NED

### 2.1 Purpose

Provides the compiled C# types for all BDC SST descriptors and topics.

### 2.2 Source

The data model is derived from:
- `docs/bdc-sst-dm.txt` (IDL specification)
- `docs/FcdCsharp/` (existing C# DSL examples)

### 2.3 Implementation Approach

**Option A**: Manual C# Translation
- Cons: Labor-intensive, error-prone
- Pros: Full control, immediate availability

**Option B**: Use FDP IDL Code Generator
- Cons: Requires understanding FDP codegen toolchain
- Pros: Automated, consistent with FDP patterns

**Recommended**: Start with **Option A** for core types, migrate to **Option B** as needed.

### 2.4 Critical Types

**Source:** `docs/FcdCsharp/*.cs` (AUTHORITATIVE - DO NOT MODIFY)

**Note:** These types are already defined in `docs/FcdCsharp`. This project will use them directly by referencing those files.

#### Core Entity Descriptors (from GenericDescriptors.cs)

```csharp
using Hrot.NED.Common;  // Common types
using Hrot.NED.Descriptors; // Descriptors

// Core identity - determines entity existence
[DdsTopic("EntityMaster")]
public partial struct EntityMaster
{
    [DdsKey] public int EntityId;          // NOT long!
    public long TkbType;                    // TKB database ID
    public ulong DisType;                   // SISO DIS type
    public ulong Flags;                     // Entity-specific flags
    // NOTE: OwnerId comes from DDS sample metadata, NOT a field!
}

// Metadata and ORBAT hierarchy
[DdsTopic("EntityInfo")]
public partial struct EntityInfo
{
    [DdsKey] public int EntityId;
    public string Name;
    public eForceIdentifier ForceIdentifier;  // Note: 'e' prefix!
    public int CommanderId;                   // ORBAT parent (0 = root)
}
```

#### Geospatial Descriptors (from SimDescriptors.cs)

```csharp
// Position/Orientation WITHOUT dead reckoning
[DdsTopic("WorldPos")]  // NOT "NetworkPosition"!
public partial struct WorldPos
{
    [DdsKey] public int EntityId;
    public DateTime Time;           // Exercise timestamp
    public GeoPoint Pos;         // Lat/Lon/Alt (see Common.cs)
    public EulerOri Rot;      // Heading/Pitch/Roll in degrees
}

// Velocity/Acceleration WITH dead reckoning
[DdsTopic("WorldPos")]  // NOT "NetworkVelocity"!
public partial struct WorldPos
{
    [DdsKey] public int EntityId;
    public DateTime Time;
    public AngularVector Vel;                // Direction-Angle-Length velocity
    public AngularVector Acc;                // Acceleration
    public EulerOri RotVel;   // Angular velocity
}
```

#### Mission Descriptors (from MissionDescriptors.cs)

```csharp
[DdsTopic("EntityMission")]
public partial struct EntityMission
{
    [DdsKey] public long EntityId;  // Note: long for missions
    public MissionPlan Plan;
}

public partial struct MissionPlan
{
    public Guid ActiveTaskId;       // GUID, not "CorrelationId"
    public List<MissionTask> Tasks; // List, not array
}

public partial struct MissionTask
{
    public Guid TaskId;
    public string ExecutingEngine;     // "CGFX", "SimHost", etc.
    public string BehaviorId;          // "MoveToLocation", etc.
    public string BehaviorParams;      // JSON payload
    public List<MissionTrigger> Triggers;
    public eTaskState State;           // Note: 'e' prefix!
}

public enum eTaskState  // Note: 'e' prefix!
{
    TASK_PLANNED, TASK_ACTIVE, TASK_DONE, TASK_FAILED, TASK_SKIPPED
}
```

#### Map Descriptors (from MapDescriptors.cs)

```csharp
// Visual override for specific map group
[DdsTopic("MapEntitySymbol")]
public partial struct MapEntitySymbol
{
    [DdsKey] public int EntityId;
    [DdsKey] public int MapGroupId;      // 0 = global, >0 = scoped
    public string StyleSetId;
    public string StyleParamsJson;       // NOT "StyleJsonOverride"!
}

// Tactical graphics (lines, areas, etc.)
[DdsTopic("MapVisualOverlay")]
public partial struct MapVisualOverlay
{
    [DdsKey] public int EntityId;
    public PersistenceMode PersistenceMode;
    public long BirthTimestamp;
    public float AutoDeleteTimeoutSeconds;   // NOT "LifetimeMs"!
    public string StylePresetName;
    public string StyleOverrideJson;         // NOT "StyleJsonOverride"!
    public List<GeoPoint> Points;         // NOT "MapGeometry"!
    public List<int> ChangedIndices;
    public bool IsEditable;
    public bool IsClickable;
}

// Navigation route
[DdsTopic("MapRoute")]
public partial struct MapRoute
{
    [DdsKey] public int EntityId;
    public List<Waypoint> Points;
    public bool IsLoop;
    public string ExtensionJson;
}

public partial struct Waypoint
{
    public GeoPoint Position;
    public string Name;
    public double SpeedMetersPerSec;
    public string ExtensionJson;
}
```

#### Map Configuration (from MapDescriptors.cs)

```csharp
// IOS → IG configuration
[DdsTopic("MapInteractionConfig")]
public partial struct MapInteractionConfig
{
    [DdsKey] public int MapGroupId;
    public Guid ActiveContextId;           // NOT "ContextId"!
    public int JsonSchemaVersion;
    public string ConfigurationJson;       // NOT "ConfigJson"!
}

// ⚠️ CRITICAL: ConfigurationJson Structure
// Must use Dictionary<string, bool> for layers, NOT List<string>
// RFC 7396 JSON Merge Patch treats arrays as REPLACE, not APPEND
// Example correct structure:
// {
//   "view": {
//     "layers": {"Terrain": true, "Units": true, "Overlays": false}
//   },
//   "tool": "Selection"
// }

// IG → IOS status feedback
[DdsTopic("MapConfigStatus")]
public partial struct MapConfigStatus
{
    [DdsKey] public int MapId;             // Instance, not group!
    public string PresetName;
    public string CurrentSettingsJson;
}
```

#### Interaction Events (from MapMessages.cs)

```csharp
using Hrot.NED.Messages;  // Messages namespace

// User clicks map
[DdsTopic("MapClickEvent")]
public partial struct MapClickEvent
{
    public int MapId;
    public GeoPoint Position;           // NOT GeodeticCoordinate!
    public List<MapObjectRef> HitStack;    // NOT long[]!
    public Guid InteractionContextId;      // NOT "ContextId"!
}

// User drags entity
[DdsTopic("DragEvent")]
public partial struct DragEvent
{
    public int MapId;
    public DragState State;               // START/UPDATE/END/CANCEL
    public int EntityId;
    public GeoPoint CurrentPosition;
    public Guid InteractionContextId;
}

// Selection changes
[DdsTopic("SelectionChangedEvent")]
public partial struct SelectionChangedEvent
{
    public int MapId;
    public List<int> SelectedEntityIds;   // List, not array!
}

// Context menu
[DdsTopic("ContextActionsUpdate")]
public partial struct ContextActionsUpdate
{
    public int MapGroupId;
    public List<int> ForSelection;
    public string MenuDefinitionJson;     // NOT "MenuJson"!
}
```

#### Lifecycle Messages (from GenericMessages.cs)

```csharp
// Request entity creation
[DdsTopic("CreateEntityRequest")]
public partial struct CreateEntityRequest
{
    public Guid RequestId;
    public NodeId Owner;                          // NOT long!
    public long Flags;
    public List<EntityDescriptorUnion> InitialDescriptors;  // Union type!
}

// ACK for creation
[DdsTopic("CreateEntityAck")]
public partial struct CreateEntityAck
{
    public Guid RequestId;
    public int NewEntityId;                       // NOT "AllocatedEntityId"!
    public int ErrorCode;                         // 0 = success
}

// Update descriptor request
[DdsTopic("UpdateEntityDescriptorRequest")]
public partial struct UpdateEntityDescriptorRequest
{
    public Guid RequestId;
    public int EntityId;
    public EDescriptorType DescriptorType;        // Enum discriminator
    public int PartId;
    public int CurrentVersion;                    // Optimistic lock
    public EntityDescriptorUnion Payload;
}

// Simplified attribute update
[DdsTopic("UpdateEntityAttributeRequest")]
public partial struct UpdateEntityAttributeRequest
{
    public Guid RequestId;
    public int EntityId;
    public EntityAttribute AttributeId;           // eaName, eaGeoPoint
    public EntityAttributePayload Payload;        // Union
}
```

#### Mission Control (from MissionMessages.cs)

```csharp
[DdsTopic("MissionControlRequest")]
public partial struct MissionControlRequest
{
    public Guid RequestId;
    public long TargetEntityId;
    public MissionCommandUnion Payload;           // NOT "MissionCommandPayload"!
}

public partial struct MissionCommandUnion         // DDS Union type
{
    [DdsDiscriminator]
    public eMissionCommandType _d;

    [DdsCase(eMissionCommandType.CMD_JUMP_TO_TASK)]
    public Guid TargetTaskId;

    [DdsCase(eMissionCommandType.CMD_APPEND_TASK)]
    public MissionTask NewTaskData;

    [DdsCase(eMissionCommandType.CMD_REPLACE_MISSION)]
    public MissionPlan FullMissionData;

    [DdsCase(eMissionCommandType.CMD_ABORT_ALL)]
    public bool UnusedPlaceholder;
}
```

#### Common Types (from Common.cs)

```csharp
namespace Hrot.NED.Common  // Common namespace
{
    // Geodetic position (NOT "GeodeticCoordinate")
    public partial struct GeoPoint
    {
        public double Latitude;   // degrees
        public double Longitude;  // degrees
        public double Altitude;   // meters above WGS84
    }

    // Orientation (Heading/Pitch/Roll)
    public partial struct EulerOri
    {
        public float Heading;    // degrees
        public float Pitch;      // degrees
        public float Roll;       // degrees
    }

    // Direction-Angle-Length vector
    public partial struct AngularVector
    {
        float Azimuth;           // degrees
        float Elevation;         // degrees
        float Length;            // meters (or m/s for velocity)
    }

    // Node identifier
    public partial struct NodeId
    {
        public int AppDomainId;
        public int AppInstanceId;
    }
}
```

### 2.5 Assembly Output

- **Namespace**: `Hrot.NED`
- **Assembly**: `Hrot.NED.dll`
- **Dependencies**: `CycloneDDS.dll`, `System.Numerics`

---

## 3. Hrot.Map.Common

### 3.1 Purpose

Provides shared constants, TKB mocks, and utilities used by all three mocks.

### 3.2 TKB Mock System

Since the full TKB infrastructure is unavailable, we implement a **minimal TKB substitute** for testing.

#### 3.2.1 Entity Type Registry

```csharp
namespace Hrot.Map.Common.Tkb
{
    /// <summary>
    /// Simple TKB entity type catalog for mock purposes.
    /// </summary>
    public static class TkbEntityTypes
    {
        // Ground Platforms
        public const long Tank_M1Abrams = 100;
        public const long IFV_Bradley = 101;
        public const long Truck_HMMWV = 102;

        // Lifeforms
        public const long Infantry_Rifleman = 200;
        public const long Infantry_Officer = 201;

        // Tactical Graphics
        public const long TacGraphic_FireLine = 8801;
        public const long TacGraphic_Route = 8802;
        public const long TacGraphic_Area = 8803;
        public const long TacGraphic_Annotation = 8804;

        // Composite Units (ORBAT)
        public const long Unit_InfantrySquad = 300;
        public const long Unit_TankPlatoon = 301;
    }

    /// <summary>
    /// TKB metadata for entity types.
    /// </summary>
    public class TkbEntityMeta
    {
        public long TkbId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public MapSymbolStyle DefaultStyle { get; set; }
        public long[] Subordinates { get; set; } // For composite units
    }

    /// <summary>
    /// In-memory TKB database (mock).
    /// </summary>
    public static class TkbDatabase
    {
        private static readonly Dictionary<long, TkbEntityMeta> _entities = new();

        static TkbDatabase()
        {
            Register(100, "M1 Abrams", "Platform.Ground.Tank", new MapSymbolStyle
            {
                SymbolCode = "SFGPUCIZ-------",
                Color = "#0000FF",
                Size = 32
            });

            Register(8801, "Fire Line", "TacticalGraphic.Line", new MapSymbolStyle
            {
                LineStyle = LineStyle.Dashed,
                Color = "#FF4500",
                LineWidth = 3
            });

            Register(301, "Tank Platoon", "Unit.Platoon", new MapSymbolStyle
            {
                SymbolCode = "SFGPUCIZ--H----",
                Color = "#0000FF",
                Size = 48
            }, subordinates: new[] { 100L, 100L, 100L, 100L }); // 4 tanks
        }

        private static void Register(long id, string name, string category, 
            MapSymbolStyle style, long[] subordinates = null)
        {
            _entities[id] = new TkbEntityMeta
            {
                TkbId = id,
                Name = name,
                Category = category,
                DefaultStyle = style,
                Subordinates = subordinates ?? Array.Empty<long>()
            };
        }

        public static TkbEntityMeta Get(long tkbId) => _entities.GetValueOrDefault(tkbId);
        public static IEnumerable<TkbEntityMeta> GetAll() => _entities.Values;
    }
}
```

#### 3.2.2 Style System

```csharp
namespace Hrot.Map.Common.Tkb
{
    public enum LineStyle
    {
        Solid, Dashed, Dotted, DashDot
    }

    public class MapSymbolStyle
    {
        // For units/platforms
        public string SymbolCode { get; set; } // MIL-STD-2525 (or simplified)
        public int Size { get; set; } = 32;

        // For graphics
        public string Color { get; set; } = "#FFFFFF";
        public LineStyle LineStyle { get; set; } = LineStyle.Solid;
        public int LineWidth { get; set; } = 2;
        public float Opacity { get; set; } = 1.0f;
    }

    /// <summary>
    /// Style presets (affiliation-based).
    /// </summary>
    public static class StylePresets
    {
        public static readonly Dictionary<string, MapSymbolStyle> Presets = new()
        {
            ["Friendly"] = new() { Color = "#0000FF" }, // Blue
            ["Hostile"] = new() { Color = "#FF0000" },  // Red
            ["Neutral"] = new() { Color = "#00FF00" },  // Green
            ["Unknown"] = new() { Color = "#FFFF00" }   // Yellow
        };
    }
}
```

### 3.3 Coordinate Utilities

**Purpose:** Convert between WGS84 geodetic coordinates (used by DDS) and flat Cartesian coordinates (used internally in ECS).

```csharp
namespace Hrot.Map.Common.Geo
{
    using Hrot.NED.Common;  // Import GeoPoint

    /// <summary>
    /// Flat Cartesian coordinate (simulation space).
    /// NOTE: GeoPoint from Hrot.NED.Common is used for WGS84 coordinates.
    /// </summary>
    public struct CartesianCoordinate
    {
        public double X; // meters East
        public double Y; // meters North
        public double Z; // meters Up
    }

    /// <summary>
    /// Origin point for Cartesian ↔ Geodetic conversions.
    /// </summary>
    public static class GeodeticOrigin
    {
        // Example: Prague (can be configurable)
        public static GeoPoint Origin = new()  // NOT GeodeticCoordinate!
        {
            Latitude = 50.0755,
            Longitude = 14.4378,
            Altitude = 200.0
        };
    }

    /// <summary>
    /// Simple flat-earth projection (good for ~10km radius).
    /// For production, use proper UTM or custom tangent plane.
    /// </summary>
    public static class CoordinateConverter
    {
        private const double EarthRadiusMeters = 6371000.0;

        /// <summary>
        /// Convert GeoPoint (WGS84) to flat Cartesian coordinates.
        /// </summary>
        public static CartesianCoordinate ToCartesian(GeoPoint geo)
        {
            var origin = GeodeticOrigin.Origin;
            double dLat = (geo.Latitude - origin.Latitude) * (Math.PI / 180.0);
            double dLon = (geo.Longitude - origin.Longitude) * (Math.PI / 180.0);

            return new CartesianCoordinate
            {
                X = dLon * EarthRadiusMeters * Math.Cos(origin.Latitude * Math.PI / 180.0),
                Y = dLat * EarthRadiusMeters,
                Z = geo.Altitude - origin.Altitude
            };
        }

        /// <summary>
        /// Convert flat Cartesian coordinates to GeoPoint (WGS84).
        /// </summary>
        public static GeoPoint ToGeodetic(CartesianCoordinate cart)
        {
            var origin = GeodeticOrigin.Origin;
            double dLat = cart.Y / EarthRadiusMeters;
            double dLon = cart.X / (EarthRadiusMeters * Math.Cos(origin.Latitude * Math.PI / 180.0));

            return new GeoPoint
            {
                Latitude = origin.Latitude + dLat * (180.0 / Math.PI),
                Longitude = origin.Longitude + dLon * (180.0 / Math.PI),
                Altitude = origin.Altitude + cart.Z
            };
        }
    }
}
```

### 3.4 Map Constants

```csharp
namespace Hrot.Map.Common
{
    public static class MapLayers
    {
        public const string Background = "background";
        public const string TacticalGraphics = "tactical_graphics";
        public const string UnitsGround = "units_ground";
        public const string UnitsAir = "units_air";
        public const string Routes = "routes";
        public const string Labels = "labels";
    }

    public static class ContextKeys
    {
        public const string PlaceTank = "place_tank";
        public const string DrawRoute = "draw_route";
        public const string DrawFireLine = "draw_fire_line";
        public const string Measure = "measure";
    }
}
```

### 3.5 Assembly Output

- **Namespace**: `Hrot.Map.Common`
- **Assembly**: `Hrot.Map.Common.dll`
- **Dependencies**: `Hrot.NED`, `System.Numerics`

---

## 4. Hrot.Map.Toolkit

### 4.1 Purpose

Provides reusable, higher-level map logic **independent of specific mocks**. This becomes the nucleus of a production map library.

### 4.2 Planned Components

#### 4.2.1 Style Resolution System

```csharp
namespace Hrot.Map.Toolkit
{
    /// <summary>
    /// Resolves final style from 3 layers: JSON override → Preset → TKB default.
    /// </summary>
    public class StyleResolver
    {
        public MapSymbolStyle Resolve(long tkbTypeId, string presetName, string jsonOverride)
        {
            // 1. Start with TKB default
            var style = TkbDatabase.Get(tkbTypeId)?.DefaultStyle?.Clone() 
                        ?? new MapSymbolStyle();

            // 2. Apply preset
            if (!string.IsNullOrEmpty(presetName) && StylePresets.Presets.TryGetValue(presetName, out var preset))
            {
                style.MergeWith(preset);
            }

            // 3. Apply JSON override
            if (!string.IsNullOrEmpty(jsonOverride))
            {
                var overrideStyle = JsonSerializer.Deserialize<MapSymbolStyle>(jsonOverride);
                style.MergeWith(overrideStyle);
            }

            return style;
        }
    }
}
```

#### 4.2.2 ORBAT Tree Reconstructor

```csharp
namespace Hrot.Map.Toolkit.Orbat
{
    /// <summary>
    /// Reconstructs hierarchical tree from flat EntityInfo list (CommanderId pointers).
    /// </summary>
    public class OrbatTreeBuilder
    {
        public OrbatNode BuildTree(IEnumerable<EntityInfo> entities)
        {
            var lookup = entities.ToDictionary(e => e.EntityId);
            var roots = new List<OrbatNode>();
            var orphans = new List<OrbatNode>();

            foreach (var entity in entities)
            {
                var node = new OrbatNode { Entity = entity };

                if (entity.CommanderId == 0)
                {
                    roots.Add(node);
                }
                else if (lookup.TryGetValue(entity.CommanderId, out var parent))
                {
                    // TODO: Add to parent's children
                }
                else
                {
                    orphans.Add(node); // Parent not yet received
                }
            }

            return new OrbatNode { Children = roots };
        }
    }

    public class OrbatNode
    {
        public EntityInfo Entity { get; set; }
        public List<OrbatNode> Children { get; set; } = new();
    }
}
```

### 4.3 Assembly Output

- **Namespace**: `Hrot.Map.Toolkit`
- **Assembly**: `Hrot.Map.Toolkit.dll`
- **Dependencies**: `Hrot.Map.Common`

---

## 5. Hrot.NED

### 5.1 Purpose

Provides compiled C# types for all BDC SST descriptors and messages.

### 5.2 Source

**AUTHORITATIVE SOURCE:** `docs/FcdCsharp/*.cs`

These files are already generated and corrected. Reference them directly:
- `Common.cs` - Core types (GeoPoint, EulerOri, AngularVector, NodeId)
- `GenericDescriptors.cs` - EntityMaster, EntityInfo
- `SimDescriptors.cs` - WorldPos, WorldPos
- `MapDescriptors.cs` - MapEntitySymbol, MapVisualOverlay, MapRoute, MapInteractionConfig
- `MissionDescriptors.cs` - EntityMission, MissionPlan, MissionTask
- `GenericMessages.cs` - CreateEntityRequest/Ack, UpdateEntityDescriptorRequest/Ack
- `MissionMessages.cs` - MissionControlRequest/Ack
- `MapMessages.cs` - MapClickEvent, DragEvent, SelectionChangedEvent

### 5.3 Critical Type Examples

```csharp
// EntityMaster - Core identity (from GenericDescriptors.cs)
[DdsTopic("EntityMaster")]
public partial struct EntityMaster
{
    [DdsKey] public int EntityId; // NOT long!
    public long TkbType;
    public ulong DisType;
    public ulong Flags;
}

// WorldPos - Position/Orientation (from SimDescriptors.cs)
[DdsTopic("WorldPos")]
public partial struct WorldPos
{
    [DdsKey] public int EntityId;
    public DateTime Time;
    public GeoPoint Pos; // Lat/Lon/Alt
    public EulerOri Rot; // Heading/Pitch/Roll
}

// GeoPoint - Common type (from Common.cs)
public partial struct GeoPoint
{
    public double Latitude; // degrees
    public double Longitude; // degrees
    public double Altitude; // meters above WGS84
}
```

### 5.4 Assembly Output

- **Namespace**: `Hrot.NED` (or reuse `Hrot.NED.Common`, `Hrot.NED.Descriptors`, `Hrot.NED.Messages`)
- **Assembly**: `Hrot.NED.dll`
- **Dependencies**: `CycloneDDS.dll`

**See:** [DATA-MODEL-REFERENCE.md](./DATA-MODEL-REFERENCE.md) for complete type catalog.

---

## 6. Hrot.Map.Common

### 6.1 Purpose

Provides shared constants, TKB entity registry, and command gateway.

### 6.2 Components

#### 6.2.1 TKB Entity Types Registry

```csharp
namespace Hrot.Map.Common
{
    public static class TkbEntityTypes
    {
        // Ground Platforms
        public const long Tank_M1Abrams = 100;
        public const long IFV_Bradley = 101;
        public const long Truck_HMMWV = 102;
        
        // Lifeforms
        public const long Infantry_Rifleman = 200;
        
        // Tactical Graphics
        public const long TacGraphic_FireLine = 8801;
        public const long TacGraphic_Route = 8802;
        public const long TacGraphic_Area = 8803;
        
        // Composite Units
        public const long Unit_InfantrySquad = 300;
        public const long Unit_TankPlatoon = 301;
    }
}
```

#### 6.2.2 Command Gateway

Instantiate `BdcCommandGateway` (see Section 4.2) for all request/ack operations.

#### 6.2.3 Constants

```csharp
namespace Hrot.Map.Common
{
    public static class MapConfig
    {
        public const int DefaultMapGroupId = 0;
        public const int DefaultMapId = 1;
    }
    
    public static class ContextKeys
    {
        public const string PlaceTank = "place_tank";
        public const string DrawRoute = "draw_route";
        public const string Measure = "measure";
    }
}
```

### 6.3 Assembly Output

- **Namespace**: `Hrot.Map.Common`
- **Assembly**: `Hrot.Map.Common.dll`
- **Dependencies**: `Hrot.NED`, `FDP.Toolkit.Commands`

---

## 7. Implementation Plan

### Phase 1: Infrastructure Validation (2 days)

**Goal:** Verify all existing FDP components compile and pass tests.

**Tasks:**
1. ✅ Build FDP.sln successfully
2. ✅ Run tests for:
   - `IdAllocationTests` (BlockIdManager, DdsIdAllocator)
   - `TkbDatabaseTests` (template storage)
   - `WGS84TransformTests` (geographic conversion)
   - `NetworkEntityMapTests` (ID mapping)
   - `EntityLifecycleTests` (Constructing→Active→TearDown)
3. ✅ Document API patterns for each component
4. ✅ Create minimal integration examples

**Success Criteria:**
- All tests green
- Example code runs without errors
- API documentation complete

---

### Phase 2: Data Model Assembly (3 days)

**Goal:** Create usable `Hrot.NED.dll` from FcdCsharp files.

**Tasks:**
1. Create `Hrot.NED` C# project (.NET 8)
2. Copy/reference `docs/FcdCsharp/*.cs` files
3. Add CycloneDDS NuGet package
4. Compile and resolve any type errors
5. Create simple DDS publisher/subscriber test
6. Document namespace structure

**Success Criteria:**
- Assembly compiles without errors
- Can publish/subscribe EntityMaster topic
- All types accessible from external projects

**Estimated Effort:** 3 developer-days

---

### Phase 3: FDP.Toolkit.DER Implementation (5 days)

**Goal:** Non-ECS entity repository for IOS Mock.

**Tasks:**
1. Create `FDP.Toolkit.DER` project
2. Implement `IDerRepo` interface with `ConcurrentDictionary<long, DerEntity>`
3. Implement `IDerEntity` with descriptor storage
4. Add thread-safety tests (concurrent read/write)
5. Create DDS translator example (EntityMaster → DER)
6. Write unit tests:
   - Entity creation/deletion
   - Descriptor get/set
   - Event notifications
   - Concurrent access

**Success Criteria:**
- 100% test coverage for core APIs
- Thread-safe under concurrent access
- Sample DDS translator working

**Estimated Effort:** 5 developer-days

---

### Phase 4: FDP.Toolkit.Commands Implementation (4 days)

**Goal:** Generic RPC-over-DDS with async/await pattern.

**Tasks:**
1. Create `FDP.Toolkit.Commands` project
2. Implement `DdsCommandClient<TReq, TAck>` with TaskCompletionSource
3. Implement correlation ID extraction via reflection
4. Add timeout handling with CancellationTokenSource
5. Create `BdcCommandGateway` in `Hrot.Map.Common`
6. Write unit tests:
   - Successful request/ack roundtrip
   - Timeout handling
   - Concurrent requests
   - Correlation mismatch scenarios
7. Integration test with real DDS

**Success Criteria:**
- CreateEntityRequest → CreateEntityAck working end-to-end
- Timeout triggers after specified duration
- Multiple concurrent requests handled correctly

**Estimated Effort:** 4 developer-days

---

### Phase 5: Hrot.Map.Definitions (TKB Extensions) (6 days)

**Goal:** Domain-specific TKB descriptors for visual, physics, combat.

**Tasks:**
1. Create `Hrot.Map.Definitions` project
2. Implement descriptor classes:
   - `IgVisualDef` (symbol, model, color)
   - `SimVehicleDef` (mass, dimensions, mobility)
   - `SimCombatDef` (armor, weapons, sensors)
   - `TkbCompositionDef` (subordinate slots)
3. Implement `BdcTkbBuilder` fluent API
4. Integrate with `FDP.Toolkit.Tkb.TkbDatabase`
5. Register 10-15 representative entity types:
   - M1 Abrams, Bradley IFV, HMMWV
   - Infantry Rifleman, Officer
   - Fire Line, Route, Area graphics
   - Infantry Squad, Tank Platoon (composites)
6. Write unit tests:
   - Template registration
   - Descriptor retrieval
   - Composite unit subordinate validation

**Success Criteria:**
- All 10-15 entity types registered
- TkbTemplate.AreHardRequirementsMet() works correctly
- Entity creation applies descriptors automatically

**Estimated Effort:** 6 developer-days

---

### Phase 6: Hrot.Map.Common Assembly (2 days)

**Goal:** Consolidate shared constants and utilities.

**Tasks:**
1. Create `Hrot.Map.Common` project
2. Add TkbEntityTypes constants
3. Add MapConfig and ContextKeys constants
4. Reference BdcCommandGateway (from Phase 4)
5. Create README with usage examples

**Success Criteria:**
- Clean compilation
- No circular dependencies
- Examples runnable

**Estimated Effort:** 2 developer-days

---

### Phase 7: Integration Testing (3 days)

**Goal:** Verify all shared components work together.

**Tasks:**
1. Create `Hrot.Map.Integration.Tests` project
2. Write end-to-end test scenarios:
   - IOS creates entity via BdcCommandGateway
   - SimHost receives CreateEntityRequest, allocates ID
   - SimHost publishes EntityMaster
   - IOS ingests EntityMaster into DER
   - TKB template applied with descriptors
3. Performance testing:
   - 1000 entities in DER (memory/lookup speed)
   - 100 concurrent command requests (latency)
4. Document integration patterns

**Success Criteria:**
- All integration tests pass
- Performance within acceptable bounds (<10ms lookup, <100ms command RTT)
- Integration guide complete

**Estimated Effort:** 3 developer-days

---

### Summary

**Total Effort:** ~25 developer-days (~5 weeks for 1 developer)

**Critical Path:**
```
Phase 1 (2d) → Phase 2 (3d) → Phase 3 (5d) → Phase 4 (4d) → Phase 5 (6d) → Phase 6 (2d) → Phase 7 (3d)
```

**Parallelization Opportunities:**
- Phase 3 (DER) and Phase 4 (Commands) can run in parallel after Phase 2
- Phase 5 (TKB Extensions) requires Phase 1 (TKB validation)

**Optimized Timeline (2 developers):**
- Week 1: Phases 1+2
- Week 2-3: Phases 3+4 (parallel)
- Week 4: Phase 5
- Week 5: Phases 6+7

**Deliverables:**
- `Hrot.NED.dll` (types)
- `FDP.Toolkit.DER.dll` (non-ECS entity storage)
- `FDP.Toolkit.Commands.dll` (RPC framework)
- `Hrot.Map.Definitions.dll` (TKB descriptors)
- `Hrot.Map.Common.dll` (constants, gateway)
- Integration test suite
- API documentation

---

## Navigation

- **[⬆ Back to Overall Design](./DESIGN-OVERALL.md)**
- **[➜ Task Details](./TASK-DETAILS-SHARED.md)**
- **[➜ Task Tracker](./TASK-TRACKER.md)**
- **[➜ SimHost Design](./DESIGN-SIMHOST.md)**
- **[➜ IG Design](./DESIGN-IG.md)**
- **[➜ IOS Design](./DESIGN-IOS.md)**
