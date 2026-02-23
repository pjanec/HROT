# SimHost Mock Design

**Version:** 1.0 (Infrastructure Audit Complete)  
**Date:** 2026-02-13  
**Status:** Ready for Implementation

**⚠️ INFRASTRUCTURE AUDIT:** This document reflects comprehensive audit of existing FDP infrastructure. Components marked ✅ EXIST, components marked ❌ require NEW implementation.

**Parent Document**: [Overall Design](./DESIGN-OVERALL.md)

## Table of Contents

1. [Infrastructure Status Matrix](#1-infrastructure-status-matrix)
2. [Overview](#2-overview)
3. [Existing FDP Infrastructure (Reuse)](#3-existing-fdp-infrastructure-reuse)
4. [New Components (Implement)](#4-new-components-implement)
5. [System Architecture](#5-system-architecture)
6. [Implementation Plan](#6-implementation-plan)

---

## 1. Infrastructure Status Matrix

| Component | Status | Location | Purpose |
|-----------|--------|----------|---------|
| **Vehicle Physics** | ✅ EXISTS | `FDP.Toolkit.CarKinem.CarKinematicsSystem` | Bicycle model kinematics, steering, speed control |
| **Vehicle Commands** | ✅ EXISTS | `FDP.Toolkit.CarKinem.VehicleAPI` | High-level API (SpawnVehicle, NavigateTo, Formation) |
| **Formation System** | ✅ EXISTS | `FDP.Toolkit.CarKinem.FormationTargetSystem` | Leader-follower formations |
| **Trajectory Planning** | ✅ EXISTS | `FDP.Toolkit.CarKinem.Trajectory.*` | Waypoint interpolation, path following |
| **Spatial Hashing** | ✅ EXISTS | `FDP.Toolkit.CarKinem.SpatialHashSystem` | Collision/proximity lookups |
| **ID Allocation** | ✅ EXISTS | `ModuleHost.Network.Cyclone.DdsIdAllocator` | Server-side entity ID allocation |
| **Entity Lifecycle** | ✅ EXISTS | `FDP.Toolkit.Lifecycle.EntityLifecycleModule` | Constructing→Active→TearDown |
| **Network Egress** | ✅ EXISTS | `FDP.Toolkit.Replication.SmartEgressSystem`<br>`ModuleHost.Network.Cyclone.CycloneEgressSystem` | DDS publishing with delta tracking |
| **Network Entity Map** | ✅ EXISTS | `FDP.Toolkit.Replication.NetworkEntityMap` | Network ID ↔ Local Entity mapping |
| **TKB System** | ✅ EXISTS | `FDP.Toolkit.Tkb.TkbDatabase` | Entity templates |
| **Time Controller** | ✅ EXISTS | `FDP.Toolkit.Time.MasterTimeController` | Simulation time authority |
| **Geographic Transform** | ✅ EXISTS | `Fdp.Toolkit.Geographic.WGS84Transform` | WGS84 ↔ Cartesian projection |
| **Network Spawning** | ❌ NEW (shared) | `FDP.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem` | Unified entity creation: ID, TKB, network infra, ELM — see [DESIGN-NetworkSpawning.md](./DESIGN-NetworkSpawning.md) |
| **Descriptor Mapper** | ❌ NEW | `Bagira.SimHost.Util.DescriptorMapper` | Converts DDS `EntityDescriptorUnion` list → `List<object>` for SpawnEntityCommand |
| **CreateEntity Handler** | ❌ NEW | `Bagira.SimHost.Systems.CreateEntityRequestSystem` | Translates DDS CreateEntityRequest → SpawnEntityCommand (thin, no direct ELM/TKB calls) |
| **Mission Execution** | ❌ NEW | `Bagira.SimHost.Systems.MissionExecutionSystem` | Executes MissionPlan tasks |
| **GeoSpatial Bridge** | ❌ NEW | `Bagira.SimHost.Components.GeoSpatialBridge` | Sync GeoPosition ↔ VehicleState |
| **SimHost Application** | ❌ NEW | `Bagira.SimHost.Program` | Main application shell, initialization |

**Key Insight**: Vehicle physics, networking, and ECS infrastructure **FULLY EXISTS**. Focus on request handlers, mission execution, and application shell.

---

## 2. Overview

### 2.1 Purpose

SimHost Mock is the **"truth" authority** for the simulation. It:
- **Owns Entity Lifecycle**: Creates, updates, and destroys all simulation entities
- **Runs Physics**: Executes vehicle kinematics using CarKinem toolkit
- **Executes Missions**: Interprets MissionPlan and drives vehicle behavior
- **Publishes State**: Sends EntityMaster, GeoSpatial, EntityInfo to DDS
- **Responds to Requests**: Handles CreateEntityRequest from IOS/IG

### 2.2 Design Principles

1. **Reuse Existing Toolkits**: CarKinem for physics, Replication for networking, Lifecycle for entity management
2. **Master Time Authority**: Uses MasterTimeController to publish simulation time
3. **ECS-First Design**: All logic in ECS systems, no external state
4. **Minimal Custom Code**: Only implement request handlers, mission execution, and bridges

### 2.3 Technology Stack

- **ECS**: FDP Kernel (Flecs-based)
- **Physics**: FDP.Toolkit.CarKinem (bicycle kinematics)
- **Networking**: ModuleHost.Network.Cyclone (CycloneDDS)
- **Language**: C# (.NET 8)
- **UI**: None (headless)

---

## 3. Existing FDP Infrastructure (Reuse)

### 3.1 Vehicle Physics (CarKinem Toolkit)

**✅ VERIFIED EXISTS** - Production-ready vehicle simulation

**Components:**
- `FDP.Toolkit.CarKinem.CarKinematicsSystem` - Main physics loop
- `FDP.Toolkit.CarKinem.VehicleState` - Speed, steering (⚠️ Phase 0: Position and heading fields removed — now in `SimPosition` + `SimRotation` from `Fdp.Kernel`)
- `FDP.Toolkit.CarKinem.VehicleParams` - Mass, dimensions, acceleration limits
- `FDP.Toolkit.CarKinem.NavState` - Navigation target, arrival radius

**Key Systems:**
```csharp
// CarKinematicsSystem - Main physics (bicycle model)
public class CarKinematicsSystem : ComponentSystem
{
    // Runs in parallel for all vehicles
    // Updates position, heading, speed based on steering input
    // Integrates with spatial hashing for collision avoidance
}

// VehicleCommandSystem - Command handling
// FormationTargetSystem - Formation logic
// SpatialHashSystem - Proximity queries
```

**VehicleState Structure:**

> ⚠️ **Phase 0 Adaptation (VehicleState shrink):** `VehicleState.Position`, `Forward`, `Pitch`, and `Roll` have been **removed** from the struct (completed in BCS-P0-T2). The trimmed struct contains only: `Speed`, `SteerAngle`, `Accel`, `CurrentLaneIndex`. Vehicle world position is now stored in `SimPosition { Vector3 Value }` and heading in `SimRotation { Quaternion Value }` from `Fdp.Kernel`. All code that reads `vehicleState.Position` must instead read `world.GetComponent<SimPosition>(entity).Value` (using `.X`/`.Y` for 2-D operations). All code that reads `vehicleState.Forward` must instead derive the forward vector from `SimRotation`.

```csharp
public struct VehicleState
{
    public Vector2 Position;    // World position (meters)
    public Vector2 Forward;     // Normalized heading vector
    public float Speed;         // Forward speed (m/s)
    public float SteerAngle;    // Wheel angle (radians)
    public float Accel;         // Acceleration (m/s²)
    public float Pitch, Roll;   // Visual presentation
    public int CurrentLaneIndex; // Lane-aware logic
}
```

**VehicleAPI (High-Level Commands):**
```csharp
public class VehicleAPI
{
    // Spawn vehicle at position
    public void SpawnVehicle(Entity entity, Vector2 position, Vector2 heading, 
        VehicleClass vehicleClass = VehicleClass.PersonalCar);
    
    // Navigate to point
    public void NavigateToPoint(Entity entity, Vector2 destination, 
        float arrivalRadius = 2.0f, float speed = 10.0f);
    
    // Create formation
    public void CreateFormation(Entity leaderEntity, FormationType type, 
        FormationParams? parameters = null);
}
```

**Usage Pattern:**
```csharp
// Register systems
world.AddSystem<CarKinematicsSystem>();
world.AddSystem<VehicleCommandSystem>();
world.AddSystem<FormationTargetSystem>();
world.AddSystem<SpatialHashSystem>();

// Spawn vehicle
var vehicleAPI = new VehicleAPI(world);
var entity = world.NewEntity();
vehicleAPI.SpawnVehicle(entity, new Vector2(100, 200), Vector2.UnitY, VehicleClass.Truck);

// Navigate
vehicleAPI.NavigateToPoint(entity, new Vector2(500, 600), arrivalRadius: 5.0f, speed: 15.0f);
```

---

### 3.2 Network & DDS Integration

**✅ VERIFIED EXISTS** - Full DDS stack with CycloneDDS

**ID Allocation:**
```csharp
// Server-side (SimHost)
var idAllocator = new DdsIdAllocator(participant, "IdAllocatorService");
idAllocator.Start(); // Listens to allocation requests

// Allocate ID
int newEntityId = await idAllocator.AllocateAsync();
```

**Network Entity Mapping:**
```csharp
var entityMap = new NetworkEntityMap(graveyardDurationFrames: 60);

// Register entity with network ID
entityMap.Register(networkId: 12345, entity: entity);

// Lookup
if (entityMap.TryGetEntity(12345, out var entity))
{
    // Use entity
}

// Unregister (moves to graveyard for ~1 second to handle late packets)
entityMap.Unregister(networkId: 12345, currentFrame: frameCounter);
```

**DDS Egress (Publishing):**

> ⚠️ **Architecture note:** Do NOT manually add `SmartEgressSystem` or `CycloneEgressSystem` to the world/kernel. Both are **internal implementation details** of `CycloneNetworkModule`. When `CycloneNetworkModule` is registered via `kernel.RegisterModule(networkModule)`, it automatically installs all required ingress, egress, and gateway systems. Only the **Translators** (which describe what to publish and how) are supplied from outside.

```csharp
// CORRECT: Pass translators into CycloneNetworkModule — it owns egress internally.
var allTranslators = new List<ITranslator>();
allTranslators.Add(new EntityMasterTranslator(participant, entityMap));
allTranslators.Add(new GeoSpatialTranslator(participant, entityMap));
// (plus auto-translators from ReplicationBootstrap)

var networkModule = new CycloneNetworkModule(
    participant, nodeMapper, idAllocator, topology, elm,
    serializationRegistry, allTranslators, entityMap
);
kernel.RegisterModule(networkModule); // CycloneEgressSystem + SmartEgress registered here

// INCORRECT (do NOT do this — causes double execution / conflicts):
// world.AddSystem(new SmartEgressSystem());
// world.AddSystem(new CycloneEgressSystem<EntityMaster>(...));
```

---

### 3.3 Entity Lifecycle Management

**✅ VERIFIED EXISTS** - State machine with ELM

**Component:** `FDP.Toolkit.Lifecycle.EntityLifecycleModule`

**State Flow:**
```
Constructing → Active → TearDown → Disposed
```

**Usage:**

> ⚠️ **Architecture note:** Do NOT manually add `ConstructingTag` to an entity. The `EntityLifecycleModule` (ELM) manages all lifecycle state components internally. Simply setting a tag will **not** trigger the `ConstructionOrder` event that the `NetworkGatewaySystem` requires to register the entity with peer nodes. Always call `elm.BeginConstruction(...)` explicitly, and add `PendingNetworkAck` **before** calling it when reliable distributed initialisation is required.

```csharp
// Register module
var elm = new EntityLifecycleModule(tkb, Array.Empty<int>());
kernel.RegisterModule(elm);

// Entity creation — CORRECT pattern
var entity = world.CreateEntity();
template.ApplyTo(world, entity); // Apply TKB defaults first

// Add PendingNetworkAck to block ACK until all peers confirm
world.AddComponent(entity, new PendingNetworkAck { ExpectedType = ReliableInitType.AllPeers });

// BeginConstruction fires ConstructionOrder → NetworkGatewaySystem → DDS
elm.BeginConstruction(entity, tkbType, world.GlobalVersion, cmdBuffer);
// NOTE: Do NOT call entity.Set(new ConstructingTag()) — ELM adds state components itself.

// Entity deletion (graceful)
elm.BeginDestruction(entity, cmdBuffer);
// ELM transitions to TearDown, publishes DestructionOrder, cleans up network mapping.
```

---

### 3.4 Time Authority

**✅ VERIFIED EXISTS** - Master/Slave time sync

**Component:** `FDP.Toolkit.Time.MasterTimeController`

**Usage:**
```csharp
// SimHost is the master
var timeController = new MasterTimeController();
world.AddModule(timeController);

// Publishes time via DDS
// IOS/IG use SlaveTimeController to sync
```

---

### 3.5 Geographic Transforms

**✅ VERIFIED EXISTS** - WGS84 projection

**Component:** `Fdp.Toolkit.Geographic.WGS84Transform`

**Usage:**
```csharp
var origin = new GeoPosition { Latitude = 50.0755, Longitude = 14.4378, Altitude = 200.0 };
var geoTransform = new WGS84Transform(origin);

// CarKinem uses flat Vector2 coordinates
var vehiclePos = new Vector2(1000, 500); // meters from origin

// Convert to GeoPosition for DDS publishing
var geoPos = geoTransform.ToGeodetic(new CartesianCoordinate 
{ 
    X = vehiclePos.X, 
    Y = vehiclePos.Y, 
    Z = 0 
});

// geoPos.Latitude, geoPos.Longitude → publish to GeoSpatial topic
```

---

## 4. Module & System Designs

> **ARCHITECTURE NOTE:** SimHost follows the **NetworkDemo pattern** using `ModuleHostKernel` with `IModule`-based components (NOT ComponentSystem).

### 4.1 Module Pattern Overview

SimHost uses the **FDP ModuleHost pattern** demonstrated in `Fdp.Examples.NetworkDemo`:

```csharp
// IModule interface (from ModuleHost.Core.Abstractions)
public interface IModule
{
    string Name { get; }
    ExecutionPolicy Policy { get; }  // Synchronous(), SlowBackground(hz), FastBackground()
    void RegisterSystems(ISystemRegistry registry);
    void Tick(ISimulationView view, float dt);
}
```

**Key Concepts:**
- **Modules** encapsulate related systems (physics, networking, missions)
- **Systems** perform ECS queries and component updates
- **ExecutionPolicy** controls threading: `Synchronous()`, `SlowBackground(5)`, etc.
- **ModuleHostKernel** owns modules and drives update loop
- **EventBus** for inter-module communication (like NetworkDemo)

**NetworkDemo Reference Modules:**
- `GameLogicModule`: PhysicsSystem, RefactoredPlayerInputSystem, CombatFeedbackSystem
- `BridgeModule`: PacketBridgeSystem, TimeSyncSystem, ReplayBridgeSystem
- `GameInputModule`: TimeInputSystem, OwnershipInputSystem, CombatInputSystem
- `RecordingModule`: TransformSyncSystem, RecorderTickSystem
- `RadarModule`: RadarSystem (SlowBackground policy)

---

### 4.2 EntityCreationModule

**Purpose:** Handle CreateEntityRequest events from IOS, create entities via TKB, send ACK.

**Architecture:**

```csharp
namespace Bagira.SimHost.Modules
{
    using ModuleHost.Core.Abstractions;
    using Bagira.SimHost.Systems;
    
    /// <summary>
    /// Module for handling entity creation requests.
    /// Pattern: Like NetworkDemo's GameInputModule (event-driven).
    /// </summary>
    public class EntityCreationModule : IModule
    {
        public string Name => "EntityCreation";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
        
        private readonly FdpEventBus _eventBus;
        private readonly int _localNodeId;
        private readonly DdsIdAllocator _idAllocator;
        private readonly NetworkEntityMap _entityMap;
        private readonly TkbDatabase _tkbDatabase;
        
        public EntityCreationModule(
            FdpEventBus eventBus,
            int localNodeId,
            DdsIdAllocator idAllocator,
            NetworkEntityMap entityMap,
            TkbDatabase tkbDatabase)
        {
            _eventBus = eventBus;
            _localNodeId = localNodeId;
            _idAllocator = idAllocator;
            _entityMap = entityMap;
            _tkbDatabase = tkbDatabase;
        }
        
        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new CreateEntityRequestSystem(
                _eventBus, _localNodeId, _idAllocator, _entityMap, _tkbDatabase));
        }
        
        public void Tick(ISimulationView view, float dt) { }
    }
}
```

**CreateEntityRequestSystem** (updated for `FDP.Toolkit.NetworkSpawning` — thin translator only):

> **Toolkit Integration:** `CreateEntityRequestSystem` now only translates `CreateEntityRequest` into `SpawnEntityCommand`. All entity creation mechanics (TKB, network infra, ELM) are handled by `NetworkSpawningSystem` from `FDP.Toolkit.NetworkSpawning`. See [DESIGN-NetworkSpawning.md §8.1](./DESIGN-NetworkSpawning.md#81-simhost-authority-node).

```csharp
namespace Bagira.SimHost.Systems
{
    using Bagira.BDC.SSTM;
    using Bagira.SimHost.Util;
    using FDP.Toolkit.NetworkSpawning.Events;
    using Fdp.Kernel;
    using ModuleHost.Core.Network.Interfaces;

    /// <summary>
    /// Thin system: DDS CreateEntityRequest → SpawnEntityCommand (EventBus).
    /// Does NOT create entities directly. NetworkSpawningSystem handles creation.
    /// Pattern: Like NetworkDemo's CombatInputSystem (event ingress only).
    /// </summary>
    public class CreateEntityRequestSystem
    {
        private readonly FdpEventBus _eventBus;
        private readonly int _localNodeId;
        private readonly DdsIdAllocator _idAllocator;
        private readonly WGS84Transform _geoTransform;
        
        public CreateEntityRequestSystem(
            FdpEventBus eventBus,
            int localNodeId,
            DdsIdAllocator idAllocator,
            WGS84Transform geoTransform)
        {
            _eventBus     = eventBus;
            _localNodeId  = localNodeId;
            _idAllocator  = idAllocator;
            _geoTransform = geoTransform;
        }

        public void Update(EntityRepository _)
        {
            var requests = _eventBus.ConsumeEvents<CreateEntityRequestEvent>();
            foreach (var evt in requests)
                ProcessRequest(evt.Request);
        }

        private async void ProcessRequest(CreateEntityRequest request)
        {
            try
            {
                // 1. Allocate network ID (SimHost is the ID authority)
                int newEntityId = await _idAllocator.AllocateAsync();

                // 2. Convert DDS descriptors → component list via DescriptorMapper
                var initialComponents =
                    DescriptorMapper.MapToComponents(request.InitialDescriptors, _geoTransform);
                long tkbType = DescriptorMapper.ExtractTkbType(request.InitialDescriptors);

                if (tkbType == 0)
                {
                    SendErrorAck(request.RequestId, newEntityId: 0, errorCode: 400);
                    return;
                }

                // 3. Publish SpawnEntityCommand — NetworkSpawningSystem does the rest
                //    (TKB lookup, network infra, PendingNetworkAck, BeginConstruction)
                _eventBus.Publish(new SpawnEntityCommand
                {
                    NetworkId         = newEntityId,
                    TkbType           = tkbType,
                    OwnerNodeId       = _localNodeId,   // SimHost takes authority
                    InitType          = ReliableInitType.AllPeers,
                    InitialComponents = initialComponents,
                    RequestId         = request.RequestId
                });

                // 4. Send ACK immediately — entity appears in ECS on next frame
                _eventBus.Publish(new CreateEntityAckEvent
                {
                    Ack = new CreateEntityAck
                    {
                        RequestId   = request.RequestId,
                        NewEntityId = newEntityId,
                        ErrorCode   = 0
                    }
                });

                FdpLog.Info(
                    $"[SimHost] CreateEntityRequest → SpawnEntityCommand: ID={newEntityId} TkbType={tkbType}");
            }
            catch (Exception ex)
            {
                FdpLog.Error($"[SimHost] CreateEntityRequest failed: {ex.Message}");
                SendErrorAck(request.RequestId, newEntityId: 0, errorCode: 500);
            }
        }

        private void SendErrorAck(Guid requestId, int newEntityId, int errorCode)
        {
            _eventBus.Publish(new CreateEntityAckEvent
            {
                Ack = new CreateEntityAck
                {
                    RequestId   = requestId,
                    NewEntityId = newEntityId,
                    ErrorCode   = errorCode
                }
            });
        }
    }
}
```

**DescriptorMapper** (`Bagira.SimHost.Util.DescriptorMapper`) — converts DDS `EntityDescriptorUnion` list to ECS component overrides for `SpawnEntityCommand.InitialComponents`:

```csharp
namespace Bagira.SimHost.Util
{
    public static class DescriptorMapper
    {
        public static long ExtractTkbType(List<EntityDescriptorUnion> descriptors)
        {
            foreach (var d in descriptors)
                if (d._d == EDescriptorType.dtEntityMaster) return d.EntityMaster.TkbType;
            return 0;
        }

        public static List<object> MapToComponents(
            List<EntityDescriptorUnion> descriptors, WGS84Transform geo)
        {
            var result = new List<object>();
            foreach (var d in descriptors)
            {
                switch (d._d)
                {
                    case EDescriptorType.dtEntityMaster:
                        result.Add(d.EntityMaster);  // Use DDS type directly — no wrapper
                        break;
                    case EDescriptorType.dtEntityInfo:
                        result.Add(d.EntityInfo);
                        break;
                    case EDescriptorType.dtGeoSpatial:
                        result.Add(d.GeoSpatial);    // Replicated via AutoCycloneTranslator
                        var cart = geo.ToCartesian(d.GeoSpatial.Pos);
                        // ⚠️ Phase 0: VehicleState.Position and .Forward removed. Instead, add SimPosition + SimRotation components to the entity.
                        result.Add(new VehicleState
                        {
                            Position   = new Vector2((float)cart.X, (float)cart.Y), // ⚠️ Phase 0: → world.AddComponent(entity, new SimPosition { Value = new Vector3((float)cart.X, (float)cart.Y, 0) })
                            Forward    = HeadingToVector(d.GeoSpatial.Rot.Heading),  // ⚠️ Phase 0: → world.AddComponent(entity, new SimRotation { Value = Quaternion.CreateFromYawPitchRoll(headingRad, 0, 0) })
                            Speed      = 0, SteerAngle = 0
                        });
                        break;
                    default:
                        FdpLog.Warn($"[DescriptorMapper] Unhandled descriptor: {d._d}");
                        break;
                }
            }
            return result;
        }

        private static Vector2 HeadingToVector(float deg)
        {
            float rad = deg * (MathF.PI / 180f);
            return new Vector2(MathF.Sin(rad), MathF.Cos(rad));
        }
    }
}
```

**DDS Translator Setup:**

SimHost needs translators to bridge DDS ↔ EventBus (like NetworkDemo's FireEventTranslator):

```csharp
// CreateEntityRequestTranslator: DDS → EventBus
public class CreateEntityRequestTranslator : IIngressTranslator
{
    private readonly DataReader<CreateEntityRequest> _reader;
    private readonly FdpEventBus _eventBus;
    
    public CreateEntityRequestTranslator(DomainParticipant participant, FdpEventBus eventBus)
    {
        var subscriber = participant.CreateSubscriber();
        _reader = subscriber.CreateDataReader<CreateEntityRequest>("CreateEntityRequest");
        _eventBus = eventBus;
    }
    
    public void ReadAndPublish()
    {
        var samples = _reader.Take();
        foreach (var sample in samples)
        {
            if (sample.Info.ValidData)
            {
                _eventBus.Publish(new CreateEntityRequestEvent { Request = sample.Data });
            }
        }
    }
}

// CreateEntityAckTranslator: EventBus → DDS
public class CreateEntityAckTranslator : IEgressTranslator
{
    private readonly DataWriter<CreateEntityAck> _writer;
    private readonly FdpEventBus _eventBus;
    
    public CreateEntityAckTranslator(DomainParticipant participant, FdpEventBus eventBus)
    {
        var publisher = participant.CreatePublisher();
        _writer = publisher.CreateDataWriter<CreateEntityAck>("CreateEntityAck");
        _eventBus = eventBus;
    }
    
    public void ConsumeAndWrite()
    {
        var events = _eventBus.ConsumeEvents<CreateEntityAckEvent>();
        foreach (var evt in events)
        {
            _writer.Write(evt.Ack);
        }
    }
}
```

---

### 4.3 SimulationLogicModule

**Purpose:** Drive physics and mission execution (CarKinem + mission behaviors).

**Architecture:**

```csharp
namespace Bagira.SimHost.Modules
{
    using ModuleHost.Core.Abstractions;
    using Bagira.SimHost.Systems;
    using CarKinem.Systems;
    
    /// <summary>
    /// Module for simulation logic (physics + AI behavior).
    /// Pattern: Like NetworkDemo's GameLogicModule.
    /// </summary>
    public class SimulationLogicModule : IModule
    {
        public string Name => "SimLogic";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
        
        private readonly VehicleAPI _vehicleAPI;
        private readonly RoadNetworkBlob _roadNetwork;
        private readonly TrajectoryPoolManager _trajectoryPool;
        
        public SimulationLogicModule(
            VehicleAPI vehicleAPI,
            RoadNetworkBlob roadNetwork,
            TrajectoryPoolManager trajectoryPool)
        {
            _vehicleAPI = vehicleAPI;
            _roadNetwork = roadNetwork;
            _trajectoryPool = trajectoryPool;
        }
        
        public void RegisterSystems(ISystemRegistry registry)
        {
            // CarKinem systems (physics)
            registry.RegisterSystem(new SpatialHashSystem());
            registry.RegisterSystem(new FormationTargetSystem());
            registry.RegisterSystem(new VehicleCommandSystem());
            registry.RegisterSystem(new CarKinematicsSystem(_roadNetwork, _trajectoryPool));
            
            // Mission execution
            registry.RegisterSystem(new MissionExecutionSystem(_vehicleAPI));
        }
        
        public void Tick(ISimulationView view, float dt) { }
    }
}
```

---

### 4.4 MissionExecutionSystem

**Purpose:** Read EntityMission component, execute MissionTasks, drive vehicle behavior.

**Architecture:**

```csharp
namespace Bagira.SimHost.Systems
{
    using Bagira.DDS.DataModel;
    using CarKinem.Commands;
    using Fdp.Kernel;
    
    /// <summary>
    /// Executes mission plans for entities.
    /// Reads EntityMission component, drives VehicleAPI commands.
    /// Pattern: Similar to NetworkDemo's RefactoredPlayerInputSystem.
    /// </summary>
    public class MissionExecutionSystem
    {
        private readonly VehicleAPI _vehicleAPI;
        
        public MissionExecutionSystem(VehicleAPI vehicleAPI)
        {
            _vehicleAPI = vehicleAPI;
        }
        
        public void Update(EntityRepository world)
        {
            var query = world.Query()
                .With<EntityMissionComponent>()
                .With<VehicleState>()
                .Build();
            
            foreach (var entity in query)
            {
                var mission = world.GetComponent<EntityMissionComponent>(entity);
                
                // Find active task
                var activeTask = FindTaskById(mission.Plan.Tasks, mission.Plan.ActiveTaskId);
                if (activeTask == null) continue;
                
                // Execute based on behavior type
                switch (activeTask.BehaviorId)
                {
                    case "MoveToLocation":
                        ExecuteMoveToLocation(world, entity, activeTask);
                        break;
                    
                    case "FollowRoute":
                        ExecuteFollowRoute(world, entity, activeTask);
                        break;
                    
                    case "JoinFormation":
                        ExecuteJoinFormation(world, entity, activeTask);
                        break;
                    
                    case "Idle":
                        // Do nothing
                        break;
                    
                    default:
                        FdpLog.Warn($"[SimHost] Unknown behavior: {activeTask.BehaviorId}");
                        MarkTaskFailed(world, entity, activeTask.TaskId);
                        break;
                }
                
                // Check task completion
                if (IsTaskComplete(world, entity, activeTask))
                {
                    AdvanceToNextTask(world, entity);
                }
            }
        }
        
        private void ExecuteMoveToLocation(EntityRepository world, int entity, MissionTask task)
        {
            var params = JsonSerializer.Deserialize<MoveToLocationParams>(task.BehaviorParams);
            var vehicleState = world.GetComponent<VehicleState>(entity);
            var destination = new Vector2(params.X, params.Y);
            float distance = Vector2.Distance(vehicleState.Position, destination); // ⚠️ Phase 0: vehicleState.Position removed — use world.GetComponent<SimPosition>(entity).Value.XY()
            {
                _vehicleAPI.NavigateToPoint(entity, destination, params.ArrivalRadius, params.Speed);
            }
        }
        
        private void ExecuteFollowRoute(EntityRepository world, int entity, MissionTask task)
        {
            var params = JsonSerializer.Deserialize<FollowRouteParams>(task.BehaviorParams);
            
            if (!world.HasComponent<RouteProgressComponent>(entity))
            {
                world.AddComponent(entity, new RouteProgressComponent { CurrentWaypointIndex = 0 });
            }
            
            var progress = world.GetComponent<RouteProgressComponent>(entity);
            
            if (progress.CurrentWaypointIndex < params.Waypoints.Count)
            {
                var waypoint = params.Waypoints[progress.CurrentWaypointIndex];
                var destination = new Vector2(waypoint.X, waypoint.Y);
                var vehicleState = world.GetComponent<VehicleState>(entity);
                float distance = Vector2.Distance(vehicleState.Position, destination); // ⚠️ Phase 0: vehicleState.Position removed — use world.GetComponent<SimPosition>(entity).Value.XY()
                
                if (distance < waypoint.ArrivalRadius)
                {
                    progress.CurrentWaypointIndex++;
                    world.SetComponent(entity, progress);
                }
                else
                {
                    _vehicleAPI.NavigateToPoint(entity, destination, waypoint.ArrivalRadius, waypoint.Speed);
                }
            }
        }
        
        private void ExecuteJoinFormation(EntityRepository world, int entity, MissionTask task)
        {
            // Implementation details...
        }
        
        private bool IsTaskComplete(EntityRepository world, int entity, MissionTask task)
        {
            // Implementation details...
            return false;
        }
        
        private void AdvanceToNextTask(EntityRepository world, int entity)
        {
            // Implementation details...
        }
        
        private MissionTask? FindTaskById(List<MissionTask> tasks, Guid taskId)
        {
            return tasks.FirstOrDefault(t => t.TaskId == taskId);
        }
        
        private void MarkTaskFailed(EntityRepository world, int entity, Guid taskId)
        {
            // Implementation details...
        }
    }
    
    // Behavior parameter structures
    public class MoveToLocationParams
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Speed { get; set; } = 15.0f;
        public float ArrivalRadius { get; set; } = 5.0f;
    }
    
    public class FollowRouteParams
    {
        public List<RouteWaypoint> Waypoints { get; set; } = new();
    }
    
    public class RouteWaypoint
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Speed { get; set; } = 15.0f;
        public float ArrivalRadius { get; set; } = 5.0f;
    }
    
    public struct RouteProgressComponent
    {
        public int CurrentWaypointIndex;
    }
}
```

---

### 4.5 GeoSpatialBridgeModule

**Purpose:** Sync VehicleState (local coordinates) ↔ GeoSpatialComponent (WGS84) for DDS.

**Architecture:**

```csharp
namespace Bagira.SimHost.Modules
{
    using ModuleHost.Core.Abstractions;
    using Bagira.SimHost.Systems;
    
    public class GeoSpatialBridgeModule : IModule
    {
        public string Name => "GeoBridge";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
        
        private readonly WGS84Transform _geoTransform;
        
        public GeoSpatialBridgeModule(WGS84Transform geoTransform)
        {
            _geoTransform = geoTransform;
        }
        
        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new GeoSpatialBridgeSystem(_geoTransform));
        }
        
        public void Tick(ISimulationView view, float dt) { }
    }
}
```

**GeoSpatialBridgeSystem:**

```csharp
namespace Bagira.SimHost.Systems
{
    using Fdp.Toolkit.Geographic;
    using Fdp.Kernel;
    
    /// <summary>
    /// Bridges VehicleState (local coordinates) to GeoSpatialComponent (WGS84).
    /// Runs after physics, before network egress.
    /// Pattern: Like NetworkDemo's TransformSyncSystem.
    /// </summary>
    public class GeoSpatialBridgeSystem
    {
        private readonly WGS84Transform _geoTransform;
        
        public GeoSpatialBridgeSystem(WGS84Transform geoTransform)
        {
            _geoTransform = geoTransform;
        }
        
        public void Update(EntityRepository world)
        {
            var query = world.Query()
                .With<VehicleState>()
                .With<NetworkIdentity>()
                .Build();
            
            foreach (var entity in query)
            {
                var vehicleState = world.GetComponent<VehicleState>(entity);
                var netId = world.GetComponent<NetworkIdentity>(entity);
                
                // Convert local position to geodetic
                // ⚠️ CRITICAL: Preserve altitude from GeoSpatialComponent if entity exists
                var existingGeo = world.TryGetComponent<GeoSpatialComponent>(entity);
                float altitude = existingGeo?.Pos.Altitude ?? 0.0f;
                
                var cartesian = new CartesianCoordinate
                {
                    X = vehicleState.Position.X,  // ⚠️ Phase 0: read world.GetComponent<SimPosition>(entity).Value.X instead
                    Y = vehicleState.Position.Y,  // ⚠️ Phase 0: read world.GetComponent<SimPosition>(entity).Value.Y instead
                    Z = altitude  // Preserve altitude, CarKinem only updates X/Y
                };
                
                var geoPos = _geoTransform.ToGeodetic(cartesian);
                
                // Convert forward vector to heading
                float headingDeg = MathF.Atan2(vehicleState.Forward.X, vehicleState.Forward.Y) * (180.0f / MathF.PI); // ⚠️ Phase 0: vehicleState.Forward removed — derive from SimRotation quaternion
                if (headingDeg < 0) headingDeg += 360.0f;
                
                // Update GeoSpatial component (will be egressed by translators)
                world.SetComponent(entity, new GeoSpatialComponent
                {
                    EntityId = netId.Value,
                    Time = DateTime.UtcNow,
                    Pos = new GeoPosition
                    {
                        Latitude = geoPos.Latitude,
                        Longitude = geoPos.Longitude,
                        Altitude = geoPos.Altitude
                    },
                    Rot = new OrientationHPR
                    {
                        Heading = headingDeg,
                        Pitch = 0,
                        Roll = 0
                    }
                });
            }
        }
    }
}
```

---

### 4.6 SimHost Main Application

**Purpose:** Entry point following NetworkDemo initialization pattern.

**Architecture:**

```csharp
namespace Bagira.SimHost
{
    using Fdp.Kernel;
    using FDP.Toolkit.Lifecycle;
    using FDP.Toolkit.Replication;
    using FDP.Toolkit.Tkb;
    using FDP.Toolkit.Time.Controllers;
    using ModuleHost.Network.Cyclone;
    using ModuleHost.Network.Cyclone.Services;
    using Bagira.SimHost.Modules;
    using CarKinem;
    
    /// <summary>
    /// SimHost main application.
    /// Pattern: Follows NetworkDemoApp.cs initialization sequence.
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            FdpLog.Info("[SimHost] Starting...");
            
            // --- 1. Core Infrastructure ---
            
            var world = new EntityRepository();
            var eventBus = new FdpEventBus();
            var accumulator = new FixedTimeAccumulator(dt: 1.0f / 60.0f);
            var kernel = new ModuleHostKernel(world, accumulator);
            
            int instanceId = 100; // SimHost instance
            int localInternalId = 0; // Will be assigned by NodeIdMapper
            
            // --- 2. Network Setup (Like NetworkDemo) ---
            
            var participant = new DdsParticipant(domainId: 0);
            var nodeMapper = new NodeIdMapper(localDomain: 0, localInstance: instanceId);
            localInternalId = nodeMapper.LocalInternalId;
            
            var entityMap = new NetworkEntityMap();
            var serializationRegistry = new SerializationRegistry();
            
            // ID Allocator (SimHost runs server)
            var topology = new NetworkTopology { IsServer = true };
            var idAllocator = new DdsIdAllocator(participant, isServer: true);
            
            FdpLog.Info($"[SimHost] NodeId: {localInternalId}");
            
            // --- 3. TKB Setup ---
            
            var tkb = new TkbDatabase();
            BdcTkbCatalog.RegisterAll(tkb);
            FdpLog.Info("[SimHost] TKB templates registered");
            
            // --- 4. Geographic Transform ---
            
            var geoOrigin = new GeoPosition { Latitude = 50.0755, Longitude = 14.4378, Altitude = 200.0 };
            var geoTransform = new WGS84Transform(geoOrigin);
            
            // --- 5. Register Core Modules (Like NetworkDemo) ---
            
            // A. Lifecycle
            var elm = new EntityLifecycleModule(tkb, Array.Empty<int>());
            kernel.RegisterModule(elm);
            
            // B. Replication
            kernel.RegisterModule(new ReplicationLogicModule());
            
            // C. Network (with translators)
            var allTranslators = new List<ITranslator>();
            
            // 1. Manual translators (like NetworkDemo's FastGeodeticTranslator)
            allTranslators.Add(new CreateEntityRequestTranslator(participant, eventBus));
            allTranslators.Add(new CreateEntityAckTranslator(participant, eventBus));
            allTranslators.Add(new EntityMasterTranslator(participant, entityMap));
            allTranslators.Add(new GeoSpatialTranslator(participant, entityMap));
            
            // 2. Auto-generated translators
            var (autoTranslators, _) = ReplicationBootstrap.CreateAutoTranslators(
                participant,
                typeof(Program).Assembly,
                entityMap
            );
            allTranslators.AddRange(autoTranslators);
            
            var networkModule = new CycloneNetworkModule(
                participant, nodeMapper, idAllocator, topology, elm,
                serializationRegistry, allTranslators, entityMap
            );
            kernel.RegisterModule(networkModule);
            
            // --- 6. Application Modules ---
            
            // A. Entity Creation
            kernel.RegisterModule(new EntityCreationModule(
                eventBus, localInternalId, idAllocator, entityMap, tkb));
            
            // B. Simulation Logic (Physics + Missions)
            var roadNetwork = new RoadNetworkBlob();
            var trajectoryPool = new TrajectoryPoolManager();
            var vehicleAPI = new VehicleAPI(world);
            
            kernel.RegisterModule(new SimulationLogicModule(
                vehicleAPI, roadNetwork, trajectoryPool));
            
            // C. GeoSpatial Bridge
            kernel.RegisterModule(new GeoSpatialBridgeModule(geoTransform));
            
            // --- 7. Time Controller ---
            
            var timeController = new MasterTimeController(eventBus, null);
            kernel.SetTimeController(timeController);
            
            // --- 8. Initialize ---
            
            kernel.Initialize();
            FdpLog.Info("[SimHost] Kernel initialized");
            
            // --- 9. Main Loop (60 Hz) ---
            
            FdpLog.Info("[SimHost] Entering simulation loop...");
            int frameCount = 0;
            var cancellationToken = CancellationToken.None; // Add Ctrl+C handling
            
            while (!cancellationToken.IsCancellationRequested)
            {
                kernel.Update(); // TimeController drives dt
                eventBus.SwapBuffers();
                
                if (frameCount % 60 == 0)
                {
                    PrintStatus(world, localInternalId);
                }
                
                await Task.Delay(16); // ~60 Hz
                frameCount++;
            }
            
            // --- 10. Cleanup ---
            
            participant.Dispose();
            FdpLog.Info("[SimHost] Shutdown complete");
        }
        
        static void PrintStatus(EntityRepository world, int localNodeId)
        {
            var query = world.Query().With<NetworkIdentity>().Build();
            int localCount = 0, remoteCount = 0;
            
            foreach (var entity in query)
            {
                var auth = world.GetComponent<NetworkAuthority>(entity);
                if (auth.PrimaryOwnerId == localNodeId) localCount++;
                else remoteCount++;
            }
            
            FdpLog.Info($"[STATUS] Entities: Local={localCount}, Remote={remoteCount}");
        }
    }
}
```

---

## 5. System Architecture

### 5.1 Module Update Order

```
ModuleHostKernel Update Sequence:
  1. EntityLifecycleModule (state transitions)
  2. ReplicationLogicModule (ingress + ownership)
  3. CycloneNetworkModule (DDS read/write)
  4. EntityCreationModule (CreateEntityRequestSystem)
  5. SimulationLogicModule:
       - MissionExecutionSystem
       - VehicleCommandSystem  
       - CarKinematicsSystem (physics)
  6. GeoSpatialBridgeModule (coordinate conversion)
  7. NetworkModule Egress (component → DDS)
```

### 5.2 Component Flow

```
CreateEntityRequest (DDS)
  ↓ CreateEntityRequestTranslator
EventBus
  ↓ CreateEntityRequestSystem
Entity + VehicleState + EntityMissionComponent
  ↓ MissionExecutionSystem
VehicleAPI.NavigateToPoint()
  ↓ CarKinematicsSystem
VehicleState.Position updated
  ↓ GeoSpatialBridgeSystem
GeoSpatialComponent (WGS84)
  ↓ GeoSpatialTranslator
DDS GeoSpatial topic → IOS/IG
```

### 5.3 Data Flow Diagram

```
┌──────────────────────────────────────────────┐
│           SimHost (ModuleHostKernel)         │
├──────────────────────────────────────────────┤
│ CreateEntityRequestTranslator → EventBus     │
│   ↓                                          │
│ CreateEntityRequestSystem (create entity)    │
│   ↓                                          │
│ MissionExecutionSystem → VehicleAPI          │
│   ↓                                          │
│ CarKinematicsSystem (physics)                │
│   ↓                                          │
│ GeoSpatialBridgeSystem (Vector2→WGS84)       │
│   ↓                                          │
│ GeoSpatialTranslator → DDS                   │
└──────────────────┬──────────────┬────────────┘
                   ↓              ↓
             EntityMaster    GeoSpatial (DDS)
                   ↓              ↓
               IOS Mock        IG Mock
```

---

## 6. Critical Edge Cases & Mitigations

### 6.1 Terrain Height Preservation

**Issue:** CarKinem physics operates in 2D (Vector2), but entities have 3D positions with altitude. If SimHost sets Z=0 unconditionally, entities on mountains will snap to sea level on IG.

**Solution:**
- `GeoSpatialBridgeSystem` MUST preserve existing altitude when updating position
- Read current `GeoSpatialComponent.Pos.Altitude` before overwriting
- Only modify X/Y via physics, maintain Z from creation or terrain service

**Code Pattern:**
```csharp
var existingGeo = world.TryGetComponent<GeoSpatialComponent>(entity);
float altitude = existingGeo?.Pos.Altitude ?? 0.0f;
var cartesian = new CartesianCoordinate { X = pos.X, Y = pos.Y, Z = altitude };
```

**Future Enhancement:** Integrate with `ITerrainService` for dynamic height lookup.

### 6.2 Physics Initialization Jitter

**Issue:** First frame after entity creation might have dt=0 or uninitialized velocities, causing teleportation or velocity spikes.

**Solution:**
- Initialize `VehicleState` with zero velocity: `Speed=0`, `Accel=0`
- In `CarKinematicsSystem.Update()`, skip integration if `dt <= 0`
- Use `FirstFrameFlag` component to defer physics for 1 frame after creation

**Code Pattern:**
```csharp
public void Update(float dt)
{
    if (dt <= 0 || dt > 0.1f) return;  // Guard against invalid dt
    
    foreach (var entity in query)
    {
        if (world.HasComponent<FirstFrameFlag>(entity))
        {
            world.RemoveComponent<FirstFrameFlag>(entity);
            continue;  // Skip physics on first frame
        }
        // ... normal physics
    }
}
```

### 6.3 Mission Command Re-Entrancy

**Issue:** If IOS sends `CMD_JUMP_TO_TASK` to the currently executing task, it might reset task state (e.g., "Wait 30s" timer resets to 0).

**Solution:**
- In `MissionExecutionSystem.HandleJumpCommand()`, check if target task is already active
- Only reset state if `ForceRestart=true` flag is set in command

**Code Pattern:**
```csharp
private void HandleJumpCommand(int entityId, int targetTaskIndex, bool forceRestart)
{
    var mission = GetMissionComponent(entityId);
    
    if (mission.ActiveTaskIndex == targetTaskIndex && !forceRestart)
    {
        _logger.LogWarning($"Task {targetTaskIndex} already active, ignoring jump command");
        return;
    }
    
    // Reset task state only if different task or forced
    mission.ActiveTaskIndex = targetTaskIndex;
    mission.TaskStartTime = DateTime.UtcNow;
}
```

### 6.4 ID Allocation Race Condition

**Issue:** Multiple CreateEntityRequests arriving simultaneously might request IDs before previous entities are fully constructed.

**Solution:**
- `DdsIdAllocator` uses `Interlocked.Increment` for thread safety
- `CreateEntityRequestSystem` processes requests sequentially in single-threaded ECS update
- No additional mitigation needed

---

## 7. Implementation Plan

### Phase S1: Project Setup (1 day)

**Goal:** Create SimHost project structure and configure dependencies.

**Tasks:**
1. Create `Bagira.SimHost` C# console project (.NET 8)
2. Add project references:
   - `Bagira.DDS.DataModel` (generated from IDLs)
   - `Bagira.Map.Common`
   - `Bagira.Map.Definitions`
   - `Fdp.Kernel` (EntityRepository, ECS primitives)
   - `FDP.Toolkit.CarKinem` (vehicle physics)
   - `FDP.Toolkit.Lifecycle` (EntityLifecycleModule)
   - `FDP.Toolkit.Replication` (ReplicationLogicModule, NetworkEntityMap)
   - `FDP.Toolkit.Tkb` (TkbDatabase)
   - `FDP.Toolkit.Time` (MasterTimeController)
   - `Fdp.Toolkit.Geographic` (WGS84Transform)
   - `ModuleHost.Core` (IModule, ModuleHostKernel)
   - `ModuleHost.Network.Cyclone` (CycloneNetworkModule, DdsParticipant)
3. Create folder structure:
   ```
   Bagira.SimHost/
     Program.cs
     Modules/
       EntityCreationModule.cs
       SimulationLogicModule.cs
       GeoSpatialBridgeModule.cs
     Systems/
       CreateEntityRequestSystem.cs
       MissionExecutionSystem.cs
       GeoSpatialBridgeSystem.cs
     Translators/
       CreateEntityRequestTranslator.cs
       CreateEntityAckTranslator.cs
     Configuration/
   ```

**Estimated Effort:** 1 day

---

### Phase S2: EntityCreationModule (3 days)

**Goal:** Implement module + system + translators for entity creation.

**Tasks:**
1. Create `EntityCreationModule.cs` (IModule implementation)
2. Create `CreateEntityRequestSystem.cs`:
   - EventBus subscription
   - TKB template lookup
   - Entity creation
   - Initial descriptor application
   - NetworkEntityMap registration
3. Create translators:
   - `CreateEntityRequestTranslator` (DDS → EventBus)
   - `CreateEntityAckTranslator` (EventBus → DDS)
4. Write unit tests:
   - Request with valid TKB template
   - Request with invalid template
   - Multiple concurrent requests
5. Integration test with mock IOS client

**Estimated Effort:** 3 days

---

### Phase S3: GeoSpatialBridgeModule (2 days)

**Goal:** Implement coordinate conversion module.

**Tasks:**
1. Create `GeoSpatialBridgeModule.cs`
2. Create `GeoSpatialBridgeSystem.cs`:
   - VehicleState → GeoPosition conversion
   - Heading calculation (Vector2 → degrees)
   - GeoSpatialDR velocity calculation
3. Write unit tests:
   - Conversion accuracy
   - Heading correctness
4. Performance test (1000 vehicles)

**Estimated Effort:** 2 days

---

### Phase S4: SimulationLogicModule (5 days)

**Goal:** Implement mission execution and physics integration.

**Tasks:**
1. Create `SimulationLogicModule.cs`
2. Create `MissionExecutionSystem.cs`:
   - Behavior handlers: MoveToLocation, FollowRoute, JoinFormation, Idle
   - Task completion detection
   - Task state transitions (PLANNED→ACTIVE→DONE)
   - JSON parameter parsing
3. Integrate CarKinem systems:
   - SpatialHashSystem
   - FormationTargetSystem
   - VehicleCommandSystem
   - CarKinematicsSystem
4. Write unit tests:
   - Each behavior type
   - Task state transitions
   - Route progress tracking
5. Integration test with physics

**Estimated Effort:** 5 days

---

### Phase S5: Main Application Shell (3 days)

**Goal:** Implement Program.cs following NetworkDemo pattern.

**Tasks:**
1. Create `Program.cs` Main() method
2. Implement initialization sequence:
   - EntityRepository + ModuleHostKernel
   - DdsParticipant + NodeIdMapper
   - NetworkEntityMap + TkbDatabase
   - EntityLifecycleModule + ReplicationLogicModule
   - CycloneNetworkModule (with all translators)
   - Application modules (EntityCreation, SimLogic, GeoBridge)
3. Time controller setup (MasterTimeController)
4. Main loop (kernel.Update() + eventBus.SwapBuffers())
5. Configuration file support (JSON)
6. Logging/diagnostics
7. Graceful shutdown (Ctrl+C)

**Estimated Effort:** 3 days

---

### Phase S6: Integration Testing (3 days)

**Goal:** End-to-end testing with IOS and IG mocks.

**Tasks:**
1. Test entity creation flow:
   - IOS sends CreateEntityRequest
   - SimHost creates entity
   - IOS receives CreateEntityAck
   - IOS ingests EntityMaster
2. Test physics simulation:
   - Entity spawns at location
   - GeoSpatial published correctly
   - IG receives and renders
3. Test mission execution:
   - IOS publishes EntityMission
   - SimHost navigates vehicle
   - GeoSpatial updates reflect movement
4. Test formation behavior:
   - Create platoon of 4 tanks
   - Verify formation maintained
5. Performance testing:
   - 100 entities with missions
   - 60 Hz sustained frame rate
6. Create integration test report

**Estimated Effort:** 3 days

---

### Phase S7: Documentation (1 day)

**Goal:** Create user guide and API documentation.

**Deliverables:**
- `docs/SimHost-User-Guide.md`
- `docs/SimHost-Configuration.md`
- Code XML documentation
- README.md

**Estimated Effort:** 1 day

---

### Summary

**Total Effort:** ~18 developer-days (~3.5 weeks for 1 developer)

**Critical Path:**
```
S1 (1d) → S2 (3d) → S3 (2d) → S4 (5d) → S5 (3d) → S6 (3d) → S7 (1d)
```

**Parallelization Opportunities:**
- S3 (GeoBridge) and S4 (SimLogic) can run in parallel after S2
- S6 (Integration Testing) requires all phases complete

**Optimized Timeline (2 developers):**
- Week 1: S1+S2 (Dev1), Start S3 (Dev2)
- Week 2: S3+S4 (parallel)
- Week 3: S5 (both developers)
- Week 4: S6+S7

**Dependencies:**
- Requires Shared Components Phase 1-6 complete (data model, TKB extensions, commands)
- Requires CarKinem toolkit (already exists in FDP)
- Requires ModuleHost infrastructure (already exists in FDP)
- NetworkDemo serves as reference implementation

---

## 7. Embeddability Architecture

### 7.1 Overview

SimHost is designed to run in **two deployment modes**:
1. **Standalone Application** - Independent executable (`Bagira.SimHost.Standalone.exe`)
2. **Embedded Subsystem** - Library embedded in aggregated runner (`Bagira.Runner.exe`)

This dual-mode design enables:
- Independent testing and development
- Integration into combined dashboard view
- Headless automated testing
- Flexible deployment scenarios

**Reference:** See [DESIGN-RUNNER.md](./DESIGN-RUNNER.md) for full aggregated application architecture.

### 7.2 ISubsystem Interface Implementation

**Interface:** `ISubsystem` (defined in `Bagira.Runner.Models.ISubsystem.cs`)

SimHost implements the standard subsystem interface:

```csharp
public class SimHostSubsystem : SubsystemBase
{
    private FdpWorld? _world;
    private DdsParticipant? _participant;
    private SimHostConfiguration _config;
    private SubsystemStatusPublisher? _statusPublisher;
    
    public override string Name => "simhost";
    
    // Lifecycle Methods
    public override void Initialize(object config)
    {
        _config = (SimHostConfiguration)config;
        
        // Create FDP World
        _world = new FdpWorld();
        
        // Add all ECS modules (but don't connect DDS yet)
        _world.AddModule<CarKinemModule>();
        _world.AddModule<MissionExecutionModule>();
        _world.AddModule<EntityLifecycleModule>();
        
        Status = SubsystemStatus.Ready;
    }
    
    public override void ConnectToDomain(int domainId)
    {
        // Create DDS participant
        _participant = new DdsParticipant(domainId);
        
        // Start ID allocator server
        var idAllocator  = new DdsIdAllocator(_participant, isServer: true);
        idAllocator.Start();
        
        // Build and register CycloneNetworkModule with all required arguments.
        // See Section 4.6 and Task S5.1 for the complete translator/topology setup.
        var nodeMapper    = new NodeIdMapper(localDomain: domainId, localInstance: _config.NodeId);
        var topology      = new NetworkTopology { IsServer = true };
        var serialisation = new SerializationRegistry();
        var elm           = _world.GetModule<EntityLifecycleModule>();
        
        var translators = BuildTranslators(_participant, _entityMap, elm, _geoTransform, _eventBus);
        var networkModule = new CycloneNetworkModule(
            _participant, nodeMapper, idAllocator, topology, elm,
            serialisation, translators, _entityMap
        );
        kernel.RegisterModule(networkModule); // Registers all egress/ingress/gateway systems internally
        
        // Set MasterTimeController AFTER network module
        kernel.SetTimeController(new MasterTimeController(_eventBus, null));
        
        // Announce presence for waiting room
        _statusPublisher = new SubsystemStatusPublisher(_participant, _config.NodeId, "simhost");
        _statusPublisher.UpdateStatus(SubsystemStatus.Ready);
    }
    
    public override void Start()
    {
        Status = SubsystemStatus.Running;
        // SimHost runs its own update loop in background thread
    }
    
    public override void Update(float deltaTime)
    {
        // Called by orchestrator if not using background thread
        _world?.Update(deltaTime);
        
        // Draw ImGui panels if not headless
        if (!_config.Headless)
        {
            DrawSimHostPanels();
        }
    }
    
    // ... other ISubsystem methods
}
```

### 7.3 Refactoring Strategy

**Current Structure:**
```
Bagira.SimHost/
├── Program.cs (main entry point)
├── Systems/
│   ├── CreateEntityRequestHandler.cs
│   ├── MissionExecutionSystem.cs
│   └── ...
└── Components/
    └── ...
```

**Refactored Structure:**
```
Bagira.SimHost/ (Library)
├── SimHostSubsystem.cs          ← NEW: ISubsystem implementation
├── SimHostConfiguration.cs       ← NEW: Configuration model
├── Systems/                      ← UNCHANGED: All systems
│   ├── CreateEntityRequestHandler.cs
│   ├── MissionExecutionSystem.cs
│   └── ...
└── Components/                   ← UNCHANGED: All components
    └── ...

Bagira.SimHost.Standalone/ (Executable)
└── Program.cs                    ← NEW: Thin wrapper using SimHostSubsystem
```

**Key Changes:**
1. **Extract Logic**: Move `Program.cs` initialization logic → `SimHostSubsystem.Initialize()`
2. **Separate DDS**: Move DDS connection → `SimHostSubsystem.ConnectToDomain()`
3. **Configuration Model**: Extract CLI arguments → `SimHostConfiguration` class
4. **Systems Unchanged**: All ECS systems remain as-is

### 7.4 Configuration Model

```csharp
public class SimHostConfiguration
{
    public int NodeId { get; set; } = 1;
    public bool Headless { get; set; }
    public float TimeScale { get; set; } = 1.0f;
    public bool AutoSpawn { get; set; }
    public int AutoSpawnCount { get; set; } = 10;
    public long AutoSpawnType { get; set; } = 100;  // TKB type ID
}
```

### 7.5 Headless Mode Support

When `Headless = true`:
- **Skip ImGui**: No window creation, no panel rendering
- **Background Loop**: Run physics in fixed timestep loop
- **Metrics Only**: Expose performance counters via properties
- **No Graphics**: Skip all visualization logic

```csharp
public override void Update(float deltaTime)
{
    _world?.Update(deltaTime);
    
    if (!_config.Headless)
    {
        // Only render ImGui panels when NOT headless
        ImGui.Begin("SimHost Control");
        // ... panel code
        ImGui.End();
    }
}
```

### 7.6 Waiting Room Integration

**Protocol:** SimHost announces its presence via DDS topic `SubsystemStatusAnnounce`

```csharp
public class SubsystemStatusAnnounce
{
    [DdsKey]
    public int NodeId { get; set; }                // Unique node ID
    public string SubsystemName { get; set; }       // "simhost"
    public byte Status { get; set; }                // SubsystemStatus enum
    public long TimestampMs { get; set; }
}
```

**Status Transitions:**
- `Uninitialized` → `Initializing` (during Initialize())
- `Initializing` → `Ready` (after Initialize() complete)
- `Ready` → `Running` (after Start())
- `Running` → `Stopped` (after Stop())

**Heartbeat:** Publisher sends status every 1 second to maintain presence

### 7.7 Standalone Executable

Thin wrapper that uses `SimHostSubsystem`:

```csharp
// Bagira.SimHost.Standalone/Program.cs
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Parse CLI arguments
        var opts = ParseArguments(args);
        
        var config = new SimHostConfiguration
        {
            NodeId = opts.NodeId,
            Headless = opts.Headless,
            TimeScale = opts.TimeScale
        };
        
        var simHost = new SimHostSubsystem();
        
        try
        {
            simHost.Initialize(config);
            simHost.ConnectToDomain(opts.DomainId);
            simHost.Start();
            
            Console.WriteLine("SimHost running. Press Ctrl+C to exit.");
            WaitForCtrlC();
            
            simHost.Stop();
            simHost.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
```

### 7.8 Deployment Modes

**Mode 1: Standalone SimHost**
```bash
Bagira.SimHost.Standalone.exe --domain 0 --node-id 1 --time-scale 1.0
```

**Mode 2: Embedded in Runner (Combined View)**
```bash
Bagira.Runner.exe --mode all --domain 0
# SimHost runs alongside IG and IOS in one process
```

**Mode 3: Embedded in Runner (Headless Testing)**
```bash
Bagira.Runner.exe --mode simhost --domain 0 --headless --script test.json
# SimHost runs without UI for automated testing
```

### 7.9 Implementation Tasks

See [TASK-DETAILS-RUNNER.md](./TASK-DETAILS-RUNNER.md) Phase R2:
- **R2.1**: Refactor SimHost to SimHostSubsystem Library (1.0d)
- **R2.2**: Create SimHost Standalone Program.cs (0.25d)
- **R2.3**: Test SimHost Embeddability (0.5d)

**Dependencies:**
- Runner Phase R1 complete (ISubsystem interface defined)
- SimHost Phases S1-S7 complete (all functionality implemented)

### 7.10 Testing Strategy

**Unit Tests:**
- `Test_SimHost_Initialize`: Verify Initialize() creates FDP world
- `Test_SimHost_ConnectDomain`: Verify DDS connection
- `Test_SimHost_Headless`: Verify headless mode skips UI

**Integration Tests:**
- `Test_SimHost_Standalone`: Run standalone executable
- `Test_SimHost_Embedded`: Run via orchestrator
- `Test_SimHost_WaitingRoom`: Verify status announcements

**Verification:**
- No functionality lost from refactoring
- Standalone mode works identically to embedded mode
- Headless mode performs equivalently to UI mode

---

## Navigation

- **[⬆ Back to Overall Design](./DESIGN-OVERALL.md)**
- **[➜ Task Details](./TASK-DETAILS-SIMHOST.md)**
- **[➜ Task Tracker (Combined)](./TASK-TRACKER.md)**
- **[⬅ Shared Components Design](./DESIGN-SHARED.md)**
