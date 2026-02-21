# SimHost Implementation Tasks

**Version:** 1.0  
**Date:** 2026-02-13  
**Status:** Ready for Development

**Parent Documents**: [DESIGN-SIMHOST.md](./DESIGN-SIMHOST.md) | [TASK-TRACKER.md](./TASK-TRACKER.md)

## Overview

This document provides **detailed task breakdown** for implementing SimHost Mock. Each task includes acceptance criteria, estimated effort, and dependencies.

**Total Effort:** ~18 developer-days (~3.5 weeks for 1 developer)

**Key Insight:** Most infrastructure exists (CarKinem, networking, ECS). Focus on request handlers, mission execution, and application shell.

---

## Phase S1: Project Setup (1 day)

### Task S1.1: Create SimHost Console Project

**Goal:** Create C# console application for SimHost.

**Steps:**
1. Create new project:
   ```
   dotnet new console -n Bagira.SimHost -f net8.0
   ```
2. Add to IOS-IG-SimHost.sln solution:
   ```
   Location: Bagira.SimHost/
   ```
3. Create folder structure:
   ```
   Bagira.SimHost/
     Program.cs
     Components/
       NetworkIdComponent.cs
       EntityMasterComponent.cs
       EntityInfoComponent.cs
       GeoSpatialComponent.cs
       GeoSpatialDRComponent.cs
       EntityMissionComponent.cs
     Systems/
       CreateEntityRequestHandler.cs
       GeoSpatialBridgeSystem.cs
       MissionExecutionSystem.cs
     Configuration/
       SimHostConfig.cs
   ```

**Acceptance Criteria:**
- ✅ Project created and compiles
- ✅ Folder structure in place
- ✅ Added to solution file

**Estimated Effort:** 0.25 days

**Dependencies:** None

---

### Task S1.2: Add Project References

**Goal:** Configure all required dependencies.

**Steps:**
1. Add FDP project references:
   ```xml
   <ProjectReference Include="..\Bagira.DDS.DataModel\Bagira.DDS.DataModel.csproj" />
   <ProjectReference Include="..\Bagira.Map.Common\Bagira.Map.Common.csproj" />
   <ProjectReference Include="..\Bagira.Map.Definitions\Bagira.Map.Definitions.csproj" />
   <ProjectReference Include="..\FDP\Kernel\Fdp.Kernel\Fdp.Kernel.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.CarKinem\FDP.Toolkit.CarKinem.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Lifecycle\FDP.Toolkit.Lifecycle.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Replication\FDP.Toolkit.Replication.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Tkb\FDP.Toolkit.Tkb.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Time\FDP.Toolkit.Time.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\Fdp.Toolkit.Geographic\Fdp.Toolkit.Geographic.csproj" />
   <ProjectReference Include="..\FDP\ModuleHost\ModuleHost.Network.Cyclone\ModuleHost.Network.Cyclone.csproj" />
   ```

2. Add NuGet packages:
   ```xml
   <PackageReference Include="CycloneDDS.NET" Version="*" />
   <PackageReference Include="System.Text.Json" Version="7.0.0" />
   ```

**Acceptance Criteria:**
- ✅ All project references resolve
- ✅ NuGet packages restore successfully
- ✅ Project builds without errors

**Estimated Effort:** 0.25 days

**Dependencies:** S1.1

---

### Task S1.3: Define ECS Components

**Goal:** Create component definitions for SimHost.

> ⚠️ **Architecture note — avoid duplicate type definitions:**
> FDP uses the **Shared Data Model** types from `Bagira.DDS.DataModel` directly as ECS components. Types such as `EntityMaster`, `GeoSpatial`, `GeoSpatialDR`, `EntityInfo`, and `EntityMission` are already defined there and are decorated with `[FdpDescriptor]`, which allows the `AutoCycloneTranslator` to replicate them automatically over DDS.
>
> **Do NOT redefine local copies** (`EntityMasterComponent`, `GeoSpatialComponent`, etc.) that duplicate the fields of these DDS types. This creates:
> - Schema drift (local copy diverges from the wire format)
> - Broken auto-replication (the `AutoCycloneTranslator` cannot find its type)
> - Redundant conversion code in handlers
>
> The only components that should be **newly** defined in `Bagira.SimHost.Components` are ones that have **no corresponding DDS topic**: runtime/local state such as `NetworkIdComponent`.
> If a wrapper is truly necessary (e.g. to carry extra simulation-only state alongside the replicated data), mark it with `[FdpDescriptor]` so `AutoCycloneTranslator` picks it up.

**Implementation:**

Only create `Components/NetworkIdComponent.cs` (a genuinely local, non-replicated component):
```csharp
namespace Bagira.SimHost.Components
{
    /// <summary>
    /// Maps an ECS entity to its allocated network entity ID.
    /// This is a local runtime component — not replicated over DDS directly.
    /// The actual replication key is carried by FDP.Kernel's built-in NetworkIdentity.
    /// </summary>
    public struct NetworkIdComponent
    {
        public int NetworkId;
    }
}
```

**For replicated data, use the DDS model types directly:**
```csharp
// DO NOT create EntityMasterComponent, GeoSpatialComponent, etc.
// Use Bagira.DDS.DataModel types as ECS components directly:
using Bagira.DDS.DataModel;

// Set EntityMaster data on entity (type is already [FdpDescriptor]-tagged in DataModel)
world.AddComponent(entity, new EntityMaster
{
    EntityId  = networkId,
    TkbType   = tkbType,
    DisType   = disType,
    Flags     = 0
});

// AutoCycloneTranslator will replicate EntityMaster over DDS automatically
// because the type carries [FdpDescriptor] — no manual translator stub needed.
```

**Folder structure update for S1.1** — remove the duplicate component files:
```
Bagira.SimHost/
  Components/
    NetworkIdComponent.cs     ✅ Keep (local, non-replicated)
    EntityMasterComponent.cs  ❌ Delete — use Bagira.DDS.DataModel.EntityMaster
    EntityInfoComponent.cs    ❌ Delete — use Bagira.DDS.DataModel.EntityInfo
    GeoSpatialComponent.cs    ❌ Delete — use Bagira.DDS.DataModel.GeoSpatial
    GeoSpatialDRComponent.cs  ❌ Delete — use Bagira.DDS.DataModel.GeoSpatialDR
    EntityMissionComponent.cs ❌ Delete — use Bagira.DDS.DataModel.EntityMission
```

**Acceptance Criteria:**
- ✅ `NetworkIdComponent` created
- ✅ No local duplicates of `Bagira.DDS.DataModel` types
- ✅ ECS systems use `EntityMaster`, `GeoSpatial`, etc. from the shared data model
- ✅ XML documentation complete

**Estimated Effort:** 0.25 days (reduced — less boilerplate to write)

**Dependencies:** S1.2


---

### Task S1.4: Create Bagira.SimHost.Tests Project

**Goal:** Setup unit test project.

**Steps:**
1. Create project:
   ```bash
   dotnet new mstest -n Bagira.SimHost.Tests -f net8.0
   ```
2. Location: `Bagira.SimHost.Tests/`
3. Add to solution `IOS-IG-SimHost.sln`.
4. Add reference to `Bagira.SimHost` project.

**Acceptance Criteria:**
- ✅ Test project created
- ✅ Dependencies resolved

**Estimated Effort:** 0.1 days

**Dependencies:** S1.1

---


## Phase S2: CreateEntityRequestHandler (3 days)

### Task S2.1: Implement Request Handler Skeleton

**Goal:** Create system class with DDS reader/writer setup.

**Implementation:**

Create `Systems/CreateEntityRequestHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Bagira.DDS.DataModel;
using Bagira.SimHost.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Tkb;
using Fdp.Kernel;
using ModuleHost.Network.Cyclone;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Handles CreateEntityRequest from IOS/IG.
    /// Creates entities in ECS and sends CreateEntityAck.
    /// </summary>
    [UpdateInGroup(typeof(NetworkIngressSystemGroup))]
    public class CreateEntityRequestHandler : ComponentSystem
    {
        private readonly DataReader<CreateEntityRequest> _requestReader;
        private readonly DataWriter<CreateEntityAck> _ackWriter;
        private readonly DdsIdAllocator _idAllocator;
        private readonly NetworkEntityMap _entityMap;
        private readonly TkbDatabase _tkbDatabase;
        
        public CreateEntityRequestHandler(
            DomainParticipant participant,
            DdsIdAllocator idAllocator,
            NetworkEntityMap entityMap,
            TkbDatabase tkbDatabase)
        {
            var subscriber = participant.CreateSubscriber();
            var publisher = participant.CreatePublisher();
            
            _requestReader = subscriber.CreateDataReader<CreateEntityRequest>("CreateEntityRequest");
            _ackWriter = publisher.CreateDataWriter<CreateEntityAck>("CreateEntityAck");
            
            _idAllocator = idAllocator;
            _entityMap = entityMap;
            _tkbDatabase = tkbDatabase;
        }
        
        protected override void OnUpdate()
        {
            var samples = _requestReader.Take();
            
            foreach (var sample in samples)
            {
                if (sample.Info.ValidData)
                {
                    ProcessRequest(sample.Data);
                }
            }
        }
        
        private async void ProcessRequest(CreateEntityRequest request)
        {
            // TODO: Implement
            Console.WriteLine($"[SimHost] Received CreateEntityRequest: {request.RequestId}");
        }
    }
}
```

**Acceptance Criteria:**
- ✅ Class compiles
- ✅ DDS reader/writer initialized
- ✅ OnUpdate() processes samples
- ✅ ProcessRequest() stubbed

**Estimated Effort:** 0.5 days

**Dependencies:** S1.3

---

### Task S2.2: Implement ID Allocation Logic

**Goal:** Allocate network IDs for new entities.

**Implementation:**

Add to `ProcessRequest()`:

```csharp
private async void ProcessRequest(CreateEntityRequest request)
{
    try
    {
        // 1. Allocate network ID
        Console.WriteLine($"[SimHost] Allocating ID for request {request.RequestId}");
        int newEntityId = await _idAllocator.AllocateAsync();
        Console.WriteLine($"[SimHost] Allocated ID: {newEntityId}");
        
        // TODO: Extract TkbType
        // TODO: Create entity
        // TODO: Send ACK
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[SimHost] CreateEntity failed: {ex.Message}");
        SendErrorAck(request.RequestId, errorCode: 500);
    }
}

private void SendErrorAck(Guid requestId, int errorCode)
{
    var ack = new CreateEntityAck
    {
        RequestId = requestId,
        NewEntityId = 0,
        ErrorCode = errorCode
    };
    
    _ackWriter.Write(ack);
    Console.WriteLine($"[SimHost] Sent error ACK: {requestId} (ErrorCode={errorCode})");
}
```

**Acceptance Criteria:**
- ✅ ID allocation working
- ✅ Error handling implemented
- ✅ Logs indicate allocation success

**Estimated Effort:** 0.5 days

**Dependencies:** S2.1

---

### Task S2.3: Implement TKB Template Lookup

**Goal:** Extract TkbType from request and lookup template.

**Implementation:**

Add helper method:

```csharp
private long ExtractTkbType(List<EntityDescriptorUnion> descriptors)
{
    // Find EntityMaster descriptor
    foreach (var desc in descriptors)
    {
        if (desc._d == EDescriptorType.EntityMaster)
        {
            return desc.EntityMasterPayload.TkbType;
        }
    }
    
    Console.WriteLine("[SimHost] Warning: No EntityMaster descriptor in request");
    return 0; // Default/unknown
}
```

Update `ProcessRequest()`:

```csharp
// 2. Extract TkbType from InitialDescriptors
long tkbType = ExtractTkbType(request.InitialDescriptors);

if (tkbType == 0)
{
    Console.WriteLine("[SimHost] Invalid TkbType");
    SendErrorAck(request.RequestId, errorCode: 400); // Bad request
    return;
}

// 3. Get TKB template
var template = _tkbDatabase.GetTemplate(tkbType);
if (template == null)
{
    Console.WriteLine($"[SimHost] Template not found: {tkbType}");
    SendErrorAck(request.RequestId, errorCode: 404); // Not found
    return;
}

Console.WriteLine($"[SimHost] Using template: {template.Name}");
```

**Acceptance Criteria:**
- ✅ TkbType extraction working
- ✅ Template lookup working
- ✅ Error handling for invalid template ID

**Estimated Effort:** 0.5 days

**Dependencies:** S2.2

---

### Task S2.4: Implement Entity Creation from Template

**Goal:** Create entity in ECS using TKB template with correct distributed lifecycle.

> ⚠️ **Architecture note — use `elm.BeginConstruction`, not `ConstructingTag`:**
> Manually setting `ConstructingTag` on an entity does **not** trigger the distributed initialisation handshake. The `EntityLifecycleModule` (ELM) must be called explicitly via `elm.BeginConstruction(...)` so it can:
> 1. Emit a `ConstructionOrder` event, picked up by `NetworkGatewaySystem`.
> 2. Register the entity with all peer nodes over DDS.
> 3. Transition the entity to `Active` only after all required peers acknowledge.
>
> Additionally, to enforce "Reliable Init" (entity does not go active until peers confirm), add a `PendingNetworkAck` component **before** calling `BeginConstruction`.

**Implementation:**

Update `ProcessRequest()`:

```csharp
// 4. Create entity from template (TKB applies default components)
var entity = world.CreateEntity();
template.ApplyTo(world, entity);

// 5. Register with network entity map
_entityMap.Register(newEntityId, entity);

// 6. Set network ID component
world.AddComponent(entity, new NetworkIdComponent { NetworkId = newEntityId });

// 7. Set ownership (SimHost is authority for all entities it creates)
world.AddComponent(entity, new NetworkAuthority
{
    PrimaryOwnerId = _localNodeId,
    LocalNodeId    = _localNodeId
});

// 8. Enforce distributed reliability: entity stays in Constructing until
//    all peer nodes (IG, IOS) acknowledge via NetworkGateway.
world.AddComponent(entity, new PendingNetworkAck
{
    ExpectedType = ReliableInitType.AllPeers
});

// 9. Begin ELM construction — fires ConstructionOrder → NetworkGatewaySystem → DDS.
//    Do NOT call entity.Set(new ConstructingTag()); ELM manages state components itself.
_elm.BeginConstruction(entity, tkbType, world.GlobalVersion, _cmdBuffer);
```

**Acceptance Criteria:**
- ✅ Entity created and template applied
- ✅ Entity registered with `NetworkEntityMap`
- ✅ `NetworkIdComponent` set
- ✅ `PendingNetworkAck` added before `BeginConstruction`
- ✅ `_elm.BeginConstruction(...)` called (no bare `ConstructingTag`)
- ✅ `ConstructionOrder` event is emitted and processed by `NetworkGatewaySystem`

**Estimated Effort:** 0.5 days

**Dependencies:** S2.3

---

### Task S2.5: Implement Initial Descriptor Application

**Goal:** Apply descriptors from request as overrides on top of TKB template defaults.

> ⚠️ **Architecture note — TKB handles defaults; descriptors are overrides only:**
> By the time `ApplyInitialDescriptors` runs, `template.ApplyTo(world, entity)` has already populated the entity with all default components defined in the TKB (e.g. a `M1A2Tank` template already sets `EntityMaster.TkbType`, default `EntityInfo`, etc.).
>
> The `InitialDescriptors` from the `CreateEntityRequest` represent **caller-provided overrides** (e.g. a specific spawn position, a custom name, a force identifier). Do NOT unconditionally `AddComponent` for each descriptor — the component may already exist from the template. Use `SetComponent` (overwrite) instead.
>
> Also, do NOT construct local copies such as `EntityMasterComponent` — use `Bagira.DDS.DataModel.EntityMaster` etc. directly (see Task S1.3).

**Implementation:**

Update `ProcessRequest()` — call after `template.ApplyTo`:
```csharp
// 10. Apply caller-provided descriptor overrides on top of TKB defaults
ApplyInitialDescriptors(world, entity, request.InitialDescriptors);
```

Add helper method using DDS model types directly:
```csharp
private void ApplyInitialDescriptors(
    EntityRepository world, int entity,
    List<EntityDescriptorUnion> descriptors)
{
    foreach (var desc in descriptors)
    {
        switch (desc._d)
        {
            case EDescriptorType.EntityMaster:
                // Overwrite existing EntityMaster (set by TKB template) with caller values.
                // Use Bagira.DDS.DataModel.EntityMaster — NOT a local EntityMasterComponent.
                world.SetComponent(entity, desc.EntityMasterPayload);
                FdpLog.Debug($"[SimHost] Override EntityMaster TkbType={desc.EntityMasterPayload.TkbType}");
                break;

            case EDescriptorType.EntityInfo:
                // Overwrite existing EntityInfo from template.
                world.SetComponent(entity, desc.EntityInfoPayload);
                FdpLog.Debug($"[SimHost] Override EntityInfo Name={desc.EntityInfoPayload.Name}");
                break;

            case EDescriptorType.GeoSpatial:
                // Convert the requested geodetic position to local Cartesian for CarKinem.
                // Also store the raw GeoSpatial as an ECS component for replication.
                var geoSpatial = desc.GeoSpatialPayload;
                world.SetComponent(entity, geoSpatial); // replicated over DDS via AutoCycloneTranslator

                // Drive initial VehicleState (CarKinem input) from the same data.
                var cartesian  = _geoTransform.ToCartesian(geoSpatial.Pos);
                var vehicleState = new VehicleState
                {
                    Position   = new Vector2((float)cartesian.X, (float)cartesian.Y),
                    Forward    = HeadingToVector(geoSpatial.Rot.Heading),
                    Speed      = 0,
                    SteerAngle = 0,
                    Accel      = 0,
                    Pitch      = (float)geoSpatial.Rot.Pitch,
                    Roll       = (float)geoSpatial.Rot.Roll
                };
                world.SetComponent(entity, vehicleState);
                FdpLog.Debug($"[SimHost] Override GeoSpatial → VehicleState pos={vehicleState.Position}");
                break;

            default:
                FdpLog.Warn($"[SimHost] Unhandled override descriptor type: {desc._d}");
                break;
        }
    }
}

private Vector2 HeadingToVector(float headingDegrees)
{
    float rad = headingDegrees * (MathF.PI / 180.0f);
    return new Vector2(MathF.Sin(rad), MathF.Cos(rad));
}
```

**Acceptance Criteria:**
- ✅ Uses `world.SetComponent` (overwrite), not `AddComponent` (would duplicate)
- ✅ Uses `Bagira.DDS.DataModel` types directly (`EntityMaster`, `EntityInfo`, `GeoSpatial`)
- ✅ `EntityMaster` override applied
- ✅ `EntityInfo` override applied
- ✅ `GeoSpatial` → `VehicleState` + stored `GeoSpatial` component for DDS replication
- ✅ Heading conversion correct

**Estimated Effort:** 0.75 days

**Dependencies:** S2.4

---

### Task S2.6: Implement ACK Response

**Goal:** Send CreateEntityAck back to requester.

**Implementation:**

Update `ProcessRequest()`:

```csharp
// 9. Send ACK
var ack = new CreateEntityAck
{
    RequestId = request.RequestId,
    NewEntityId = newEntityId,
    ErrorCode = 0 // Success
};

_ackWriter.Write(ack);

Console.WriteLine($"[SimHost] Sent ACK: Entity {newEntityId} created successfully");
```

**Acceptance Criteria:**
- ✅ ACK published to DDS
- ✅ Correct RequestId correlation
- ✅ NewEntityId populated

**Estimated Effort:** 0.25 days

**Dependencies:** S2.5

---

### Task S2.7: Write Request Handler Tests

**Goal:** Unit test CreateEntityRequestHandler.

**Test Implementation:**

Create `Bagira.SimHost.Tests/CreateEntityRequestHandlerTests.cs`:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Bagira.SimHost.Systems;
using Bagira.SimHost.Components;
using Bagira.DDS.DataModel;
using FDP.Toolkit.Tkb;
using FDP.Toolkit.Replication.Services;

namespace Bagira.SimHost.Tests
{
    [TestClass]
    public class CreateEntityRequestHandlerTests
    {
        [TestMethod]
        public async Task ProcessRequest_ValidTemplate_CreatesEntity()
        {
            // Arrange
            using var participant = new DomainParticipant();
            var world = new FdpWorld();
            
            var tkbDb = new TkbDatabase();
            RegisterTestTemplate(tkbDb, tkbType: 100, name: "TestVehicle");
            
            var entityMap = new NetworkEntityMap();
            var idAllocator = new DdsIdAllocator(participant, "TestAllocator");
            idAllocator.Start();
            
            var handler = new CreateEntityRequestHandler(participant, idAllocator, entityMap, tkbDb);
            world.AddSystem(handler);
            
            // Act
            var requestPublisher = participant.CreatePublisher();
            var requestWriter = requestPublisher.CreateDataWriter<CreateEntityRequest>("CreateEntityRequest");
            
            var request = new CreateEntityRequest
            {
                RequestId = Guid.NewGuid(),
                Owner = new NodeId { AppDomainId = 1, AppInstanceId = 1 },
                Flags = 0,
                InitialDescriptors = new List<EntityDescriptorUnion>
                {
                    new()
                    {
                        _d = EDescriptorType.EntityMaster,
                        EntityMasterPayload = new EntityMaster
                        {
                            EntityId = 0, // Not yet allocated
                            TkbType = 100,
                            DisType = 0,
                            Flags = 0
                        }
                    }
                }
            };
            
            requestWriter.Write(request);
            
            // Wait for processing
            await Task.Delay(500);
            world.Update(0.016f);
            
            // Assert
            var ackReader = participant.CreateDataReader<CreateEntityAck>("CreateEntityAck");
            await Task.Delay(100);
            var ackSamples = ackReader.Take();
            
            Assert.AreEqual(1, ackSamples.Count);
            var ack = ackSamples[0].Data;
            
            Assert.AreEqual(request.RequestId, ack.RequestId);
            Assert.AreEqual(0, ack.ErrorCode);
            Assert.IsTrue(ack.NewEntityId > 0);
            
            // Verify entity in ECS
            Assert.IsTrue(entityMap.TryGetEntity(ack.NewEntityId, out var entity));
            Assert.IsTrue(entity.IsValid);
        }
        
        [TestMethod]
        public async Task ProcessRequest_InvalidTemplate_SendsErrorAck()
        {
            // Similar test but with tkbType = 999 (not registered)
            // Assert ErrorCode = 404
        }
        
        private void RegisterTestTemplate(TkbDatabase db, long tkbType, string name)
        {
            var template = new TkbTemplate
            {
                TkbType = tkbType,
                Name = name,
                MandatoryDescriptors = new List<Type>()
            };
            
            db.RegisterTemplate(template);
        }
    }
}
```

**Acceptance Criteria:**
- ✅ Valid request test passes
- ✅ Invalid template test passes
- ✅ ACK correlation verified
- ✅ Entity exists in NetworkEntityMap

**Estimated Effort:** 0.75 days

**Dependencies:** S2.6

---

## Phase S3: GeoSpatialBridgeSystem (2 days)

### Task S3.1: Implement Bridge System Skeleton

**Goal:** Create system class that queries vehicles.

**Implementation:**

Create `Systems/GeoSpatialBridgeSystem.cs`:

```csharp
using System;
using System.Numerics;
using Bagira.SimHost.Components;
using CarKinem.Core;
using Fdp.Kernel;
using Fdp.Toolkit.Geographic;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Bridges VehicleState (local coordinates) to GeoSpatialComponent (WGS84).
    /// Runs after physics, before egress.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CarKinematicsSystem))]
    [UpdateBefore(typeof(SmartEgressSystem))]
    public class GeoSpatialBridgeSystem : ComponentSystem
    {
        private readonly WGS84Transform _geoTransform;
        
        public GeoSpatialBridgeSystem(WGS84Transform geoTransform)
        {
            _geoTransform = geoTransform;
        }
        
        protected override void OnUpdate()
        {
            var query = World.Query()
                .With<VehicleState>()
                .With<NetworkIdComponent>()
                .Build();
            
            foreach (var entity in query)
            {
                UpdateGeoSpatial(entity);
            }
        }
        
        private void UpdateGeoSpatial(Entity entity)
        {
            // TODO: Implement conversion
        }
    }
}
```

**Acceptance Criteria:**
- ✅ Class compiles
- ✅ System registered with correct update order
- ✅ Query targets vehicles with NetworkIdComponent

**Estimated Effort:** 0.25 days

**Dependencies:** S1.3

---

### Task S3.2: Implement Position Conversion

**Goal:** Convert Vector2 position to GeoPosition.

**Implementation:**

Add to `UpdateGeoSpatial()`:

```csharp
private void UpdateGeoSpatial(Entity entity)
{
    var vehicleState = entity.Get<VehicleState>();
    var networkId = entity.Get<NetworkIdComponent>();
    
    // Convert local position to geodetic
    var cartesian = new CartesianCoordinate
    {
        X = vehicleState.Position.X,
        Y = vehicleState.Position.Y,
        Z = 0 // Flat terrain for now (can add terrain height later)
    };
    
    var geoPos = _geoTransform.ToGeodetic(cartesian);
    
    Console.WriteLine($"[SimHost] Entity {networkId.NetworkId}: Pos={vehicleState.Position} → Geo=({geoPos.Latitude},{geoPos.Longitude})");
    
    // TODO: Convert heading
    // TODO: Create GeoSpatial component
}
```

**Acceptance Criteria:**
- ✅ Cartesian → Geodetic conversion working
- ✅ Latitude/Longitude values reasonable (near origin)
- ✅ Logs show conversion

**Estimated Effort:** 0.25 days

**Dependencies:** S3.1

---

### Task S3.3: Implement Heading Conversion

**Goal:** Convert Vector2.Forward to heading degrees.

**Implementation:**

Add helper method:

```csharp
private float VectorToHeading(Vector2 forward)
{
    // Convert normalized forward vector to heading (degrees clockwise from North)
    // North = (0, 1), East = (1, 0)
    float radians = MathF.Atan2(forward.X, forward.Y);
    float degrees = radians * (180.0f / MathF.PI);
    
    // Normalize to [0, 360)
    if (degrees < 0) degrees += 360.0f;
    
    return degrees;
}
```

Update `UpdateGeoSpatial()`:

```csharp
// Convert forward vector to heading (degrees)
float headingDeg = VectorToHeading(vehicleState.Forward);
```

**Acceptance Criteria:**
- ✅ North (0,1) → 0°
- ✅ East (1,0) → 90°
- ✅ South (0,-1) → 180°
- ✅ West (-1,0) → 270°

**Estimated Effort:** 0.25 days

**Dependencies:** S3.2

---

### Task S3.4: Create GeoSpatial Component

**Goal:** Set GeoSpatialComponent on entity.

**Implementation:**

Complete `UpdateGeoSpatial()`:

```csharp
// Create GeoSpatial component
var geoSpatial = new GeoSpatialComponent
{
    EntityId = networkId.NetworkId,
    Time = DateTime.UtcNow, // TODO: Use simulation time from MasterTimeController
    Pos = new GeoPosition
    {
        Latitude = geoPos.Latitude,
        Longitude = geoPos.Longitude,
        Altitude = geoPos.Altitude
    },
    Rot = new OrientationHPR
    {
        Heading = headingDeg,
        Pitch = vehicleState.Pitch,
        Roll = vehicleState.Roll
    }
};

entity.Set(geoSpatial);
```

**Acceptance Criteria:**
- ✅ GeoSpatialComponent created
- ✅ All fields populated
- ✅ Component set on entity

**Estimated Effort:** 0.25 days

**Dependencies:** S3.3

---

### Task S3.5: Add GeoSpatialDR (Velocity)

**Goal:** Create GeoSpatialDR component for moving entities.

**Implementation:**

Add to `UpdateGeoSpatial()`:

```csharp
// Optional: GeoSpatialDR (velocity) if vehicle is moving
if (vehicleState.Speed > 0.1f) // Threshold to avoid noise
{
    var geoSpatialDR = new GeoSpatialDRComponent
    {
        EntityId = networkId.NetworkId,
        Time = DateTime.UtcNow,
        Vel = new DAL3
        {
            Azimuth = headingDeg,      // Velocity direction (same as heading)
            Elevation = 0,             // Flat terrain
            Length = vehicleState.Speed // Speed in m/s
        },
        Acc = new DAL3
        {
            Azimuth = headingDeg,
            Elevation = 0,
            Length = vehicleState.Accel
        },
        RotVel = new OrientationHPR
        {
            Heading = 0, // Angular velocity (TODO: calculate from steering)
            Pitch = 0,
            Roll = 0
        }
    };
    
    entity.Set(geoSpatialDR);
}
```

**Acceptance Criteria:**
- ✅ GeoSpatialDR created for moving vehicles
- ✅ Velocity components populated
- ✅ Not created for stationary vehicles (Speed < 0.1)

**Estimated Effort:** 0.5 days

**Dependencies:** S3.4

---

### Task S3.6: Write Bridge System Tests

**Goal:** Unit test coordinate conversions.

**Test Implementation:**

Create `Bagira.SimHost.Tests/GeoSpatialBridgeSystemTests.cs`:

```csharp
[TestClass]
public class GeoSpatialBridgeSystemTests
{
    [TestMethod]
    public void UpdateGeoSpatial_ConvertsPosition()
    {
        // Arrange
        var origin = new GeoPosition { Latitude = 50.0, Longitude = 14.0, Altitude = 200.0 };
        var geoTransform = new WGS84Transform(origin);
        
        var world = new FdpWorld();
        world.AddModule(geoTransform);
        var bridge = new GeoSpatialBridgeSystem(geoTransform);
        world.AddSystem(bridge);
        
        var entity = world.NewEntity();
        entity.Set(new VehicleState { Position = new Vector2(1000, 500), Forward = Vector2.UnitY, Speed = 0 });
        entity.Set(new NetworkIdComponent { NetworkId = 123 });
        
        // Act
        world.Update(0.016f);
        
        // Assert
        var geoSpatial = entity.Get<GeoSpatialComponent>();
        Assert.IsNotNull(geoSpatial);
        Assert.AreEqual(123, geoSpatial.EntityId);
        
        // Verify position is near origin
        Assert.IsTrue(Math.Abs(geoSpatial.Pos.Latitude - 50.0) < 0.01);
        Assert.IsTrue(Math.Abs(geoSpatial.Pos.Longitude - 14.0) < 0.02);
    }
    
    [TestMethod]
    public void VectorToHeading_North_Returns0()
    {
        // Test heading conversion
        // North (0,1) should be 0 degrees
    }
    
    [TestMethod]
    public void VectorToHeading_East_Returns90()
    {
        // East (1,0) should be 90 degrees
    }
    
    [TestMethod]
    public void GeoSpatialDR_NotCreated_WhenStationary()
    {
        // Verify GeoSpatialDR not created when Speed < 0.1
    }
}
```

**Acceptance Criteria:**
- ✅ Position conversion test passes
- ✅ Heading conversion tests pass (4 cardinal directions)
- ✅ Stationary vehicle test passes

**Estimated Effort:** 0.5 days

**Dependencies:** S3.5

---

## Phase S4: MissionExecutionSystem (5 days)

### Task S4.1: Implement Mission System Skeleton

**Goal:** Create system that queries entities with missions.

**Implementation:**

Create `Systems/MissionExecutionSystem.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Numerics;
using Bagira.SimHost.Components;
using Bagira.DDS.DataModel;
using CarKinem.Core;
using CarKinem.Commands;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Executes mission plans for entities.
    /// Reads EntityMission component, drives VehicleAPI commands.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(CarKinematicsSystem))]
    public class MissionExecutionSystem : ComponentSystem
    {
        private readonly VehicleAPI _vehicleAPI;
        private readonly NetworkEntityMap _entityMap;
        
        public MissionExecutionSystem(VehicleAPI vehicleAPI, NetworkEntityMap entityMap)
        {
            _vehicleAPI = vehicleAPI;
            _entityMap = entityMap;
        }
        
        protected override void OnUpdate()
        {
            var query = World.Query()
                .With<EntityMissionComponent>()
                .With<VehicleState>()
                .Build();
            
            foreach (var entity in query)
            {
                ExecuteMission(entity);
            }
        }
        
        private void ExecuteMission(Entity entity)
        {
            // TODO: Implement
        }
    }
}
```

**Acceptance Criteria:**
- ✅ Class compiles
- ✅ Query targets entities with missions and vehicle state
- ✅ ExecuteMission() stubbed

**Estimated Effort:** 0.5 days

**Dependencies:** S1.3

---

### Task S4.2: Implement MoveToLocation Behavior

**Goal:** Drive vehicle to target position.

**Implementation:**

Add behavior parameter classes:

```csharp
public class MoveToLocationParams
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Speed { get; set; } = 15.0f;
    public float ArrivalRadius { get; set; } = 5.0f;
}
```

Implement handler:

```csharp
private void ExecuteMoveToLocation(Entity entity, MissionTask task)
{
    // Parse behavior params (JSON)
    var params_ = JsonSerializer.Deserialize<MoveToLocationParams>(task.BehaviorParams);
    
    if (params_ == null)
    {
        Console.WriteLine($"[SimHost] Invalid MoveToLocation params for entity {entity.Index}");
        return;
    }
    
    var vehicleState = entity.Get<VehicleState>();
    var destination = new Vector2(params_.X, params_.Y);
    float distance = Vector2.Distance(vehicleState.Position, destination);
    
    if (distance > params_.ArrivalRadius)
    {
        // Issue navigation command
        _vehicleAPI.NavigateToPoint(entity, destination, params_.ArrivalRadius, params_.Speed);
        Console.WriteLine($"[SimHost] Entity {entity.Index} navigating to ({params_.X}, {params_.Y})");
    }
    else
    {
        Console.WriteLine($"[SimHost] Entity {entity.Index} arrived at destination");
    }
}
```

**Acceptance Criteria:**
- ✅ JSON parsing working
- ✅ VehicleAPI.NavigateToPoint() called
- ✅ Distance calculation correct
- ✅ Arrival detection working

**Estimated Effort:** 1 day

**Dependencies:** S4.1

---

### Task S4.3: Implement FollowRoute Behavior

**Goal:** Navigate through waypoint sequence.

**Implementation:**

Add parameter classes:

```csharp
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
```

Implement handler:

```csharp
private void ExecuteFollowRoute(Entity entity, MissionTask task)
{
    var params_ = JsonSerializer.Deserialize<FollowRouteParams>(task.BehaviorParams);
    
    if (params_ == null || params_.Waypoints.Count == 0)
    {
        Console.WriteLine($"[SimHost] Invalid FollowRoute params for entity {entity.Index}");
        return;
    }
    
    // Get or create route progress component
    if (!entity.Has<RouteProgressComponent>())
    {
        entity.Set(new RouteProgressComponent { CurrentWaypointIndex = 0 });
        Console.WriteLine($"[SimHost] Entity {entity.Index} starting route (waypoints={params_.Waypoints.Count})");
    }
    
    var progress = entity.Get<RouteProgressComponent>();
    
    // Check if route complete
    if (progress.CurrentWaypointIndex >= params_.Waypoints.Count)
    {
        Console.WriteLine($"[SimHost] Entity {entity.Index} completed route");
        return;
    }
    
    // Get current waypoint
    var waypoint = params_.Waypoints[progress.CurrentWaypointIndex];
    var destination = new Vector2(waypoint.X, waypoint.Y);
    
    var vehicleState = entity.Get<VehicleState>();
    float distance = Vector2.Distance(vehicleState.Position, destination);
    
    if (distance < waypoint.ArrivalRadius)
    {
        // Arrived at waypoint, advance
        progress.CurrentWaypointIndex++;
        entity.Set(progress);
        
        Console.WriteLine($"[SimHost] Entity {entity.Index} reached waypoint {progress.CurrentWaypointIndex - 1}");
    }
    else
    {
        // Navigate to waypoint
        _vehicleAPI.NavigateToPoint(entity, destination, waypoint.ArrivalRadius, waypoint.Speed);
    }
}
```

**Acceptance Criteria:**
- ✅ RouteProgressComponent tracking works
- ✅ Waypoint arrival detection correct
- ✅ Advances through all waypoints
- ✅ Route completion detected

**Estimated Effort:** 1.5 days

**Dependencies:** S4.2

---

### Task S4.4: Implement JoinFormation Behavior

**Goal:** Join vehicle to formation with leader.

**Implementation:**

Add parameter classes:

```csharp
public class JoinFormationParams
{
    public int LeaderNetworkId { get; set; }
    public string FormationType { get; set; } = "Wedge";
}

public struct InFormationTag
{
    public Entity LeaderEntity;
}
```

Implement handler:

```csharp
private void ExecuteJoinFormation(Entity entity, MissionTask task)
{
    var params_ = JsonSerializer.Deserialize<JoinFormationParams>(task.BehaviorParams);
    
    if (params_ == null)
    {
        Console.WriteLine($"[SimHost] Invalid JoinFormation params for entity {entity.Index}");
        return;
    }
    
    // Find leader entity by network ID
    if (!_entityMap.TryGetEntity(params_.LeaderNetworkId, out var leaderEntity))
    {
        Console.WriteLine($"[SimHost] Leader entity {params_.LeaderNetworkId} not found");
        return;
    }
    
    // Issue formation join (one-time command)
    if (!entity.Has<InFormationTag>())
    {
        // Parse formation type
        FormationType formationType = params_.FormationType.ToLower() switch
        {
            "wedge" => FormationType.Wedge,
            "column" => FormationType.Column,
            "line" => FormationType.Line,
            _ => FormationType.Wedge
        };
        
        _vehicleAPI.CreateFormation(leaderEntity, formationType);
        entity.Set(new InFormationTag { LeaderEntity = leaderEntity });
        
        Console.WriteLine($"[SimHost] Entity {entity.Index} joined formation with leader {params_.LeaderNetworkId}");
    }
}
```

**Acceptance Criteria:**
- ✅ Leader lookup working
- ✅ Formation type parsing correct
- ✅ VehicleAPI.CreateFormation() called once
- ✅ InFormationTag prevents duplicate joins

**Estimated Effort:** 0.75 days

**Dependencies:** S4.3

---

### Task S4.5: Implement Task Completion Detection

**Goal:** Determine when tasks are complete.

**Implementation:**

Add helper method:

```csharp
private bool IsTaskComplete(Entity entity, MissionTask task)
{
    switch (task.BehaviorId)
    {
        case "MoveToLocation":
            // Check if arrived
            if (!entity.Has<NavState>()) return false;
            
            var vehicleState = entity.Get<VehicleState>();
            var navState = entity.Get<NavState>();
            float distance = Vector2.Distance(vehicleState.Position, navState.TargetPosition);
            return distance < navState.ArrivalRadius;
        
        case "FollowRoute":
            // Check if all waypoints visited
            if (!entity.Has<RouteProgressComponent>()) return false;
            
            var progress = entity.Get<RouteProgressComponent>();
            var params_ = JsonSerializer.Deserialize<FollowRouteParams>(task.BehaviorParams);
            return params_ != null && progress.CurrentWaypointIndex >= params_.Waypoints.Count;
        
        case "JoinFormation":
            // Formation join is instant, complete immediately
            return entity.Has<InFormationTag>();
        
        case "Idle":
            // Check triggers (stub for now)
            return CheckTriggersComplete(task.Triggers);
        
        default:
            Console.WriteLine($"[SimHost] Unknown behavior for completion check: {task.BehaviorId}");
            return false;
    }
}

private bool CheckTriggersComplete(List<MissionTrigger> triggers)
{
    // Stub: implement trigger evaluation (time-based, condition-based)
    // For now, always return false (manual advancement needed)
    return false;
}
```

**Acceptance Criteria:**
- ✅ MoveToLocation completion detected
- ✅ FollowRoute completion detected
- ✅ JoinFormation completion detected
- ✅ Idle stub implemented

**Estimated Effort:** 0.5 days

**Dependencies:** S4.4

---

### Task S4.6: Implement Task State Transitions

**Goal:** Advance to next task when current task completes.

**Implementation:**

Add helper methods:

```csharp
private void AdvanceToNextTask(Entity entity, EntityMissionComponent mission)
{
    // Find current task index
    int currentIndex = mission.Plan.Tasks.FindIndex(t => t.TaskId == mission.Plan.ActiveTaskId);
    
    if (currentIndex < 0)
    {
        Console.WriteLine($"[SimHost] Active task not found in mission plan");
        return;
    }
    
    // Mark current task as DONE
    mission.Plan.Tasks[currentIndex].State = eTaskState.TASK_DONE;
    
    // Check if there's a next task
    if (currentIndex < mission.Plan.Tasks.Count - 1)
    {
        // Activate next task
        mission.Plan.ActiveTaskId = mission.Plan.Tasks[currentIndex + 1].TaskId;
        mission.Plan.Tasks[currentIndex + 1].State = eTaskState.TASK_ACTIVE;
        
        // Update component (will egress to DDS)
        entity.Set(mission);
        
        Console.WriteLine($"[SimHost] Entity {entity.Index} advanced to task {currentIndex + 1} ({mission.Plan.Tasks[currentIndex + 1].BehaviorId})");
    }
    else
    {
        // Mission complete
        Console.WriteLine($"[SimHost] Entity {entity.Index} completed mission");
        
        // Optional: Remove EntityMissionComponent to stop execution
        entity.Remove<EntityMissionComponent>();
    }
}

private MissionTask? FindTaskById(List<MissionTask> tasks, Guid taskId)
{
    return tasks.FirstOrDefault(t => t.TaskId == taskId);
}

private void MarkTaskFailed(Entity entity, EntityMissionComponent mission, Guid taskId)
{
    var taskIndex = mission.Plan.Tasks.FindIndex(t => t.TaskId == taskId);
    if (taskIndex >= 0)
    {
        mission.Plan.Tasks[taskIndex].State = eTaskState.TASK_FAILED;
        entity.Set(mission);
        Console.WriteLine($"[SimHost] Entity {entity.Index} task {taskIndex} failed");
    }
}
```

Update `ExecuteMission()`:

```csharp
private void ExecuteMission(Entity entity)
{
    var mission = entity.Get<EntityMissionComponent>();
    
    // Find active task
    var activeTask = FindTaskById(mission.Plan.Tasks, mission.Plan.ActiveTaskId);
    if (activeTask == null)
    {
        Console.WriteLine($"[SimHost] No active task for entity {entity.Index}");
        return;
    }
    
    // Execute based on behavior type
    switch (activeTask.BehaviorId)
    {
        case "MoveToLocation":
            ExecuteMoveToLocation(entity, activeTask);
            break;
        
        case "FollowRoute":
            ExecuteFollowRoute(entity, activeTask);
            break;
        
        case "JoinFormation":
            ExecuteJoinFormation(entity, activeTask);
            break;
        
        case "Idle":
            // Do nothing, wait for triggers
            break;
        
        default:
            Console.WriteLine($"[SimHost] Unknown behavior: {activeTask.BehaviorId}");
            MarkTaskFailed(entity, mission, activeTask.TaskId);
            return;
    }
    
    // Check task completion
    if (IsTaskComplete(entity, activeTask))
    {
        AdvanceToNextTask(entity, mission);
    }
}
```

**Acceptance Criteria:**
- ✅ Task state updates (PLANNED→ACTIVE→DONE)
- ✅ Advances to next task automatically
- ✅ Detects mission completion
- ✅ EntityMissionComponent updates egress to DDS

**Estimated Effort:** 0.75 days

**Dependencies:** S4.5

---

### Task S4.7: Write Mission Execution Tests

**Goal:** Unit test mission behaviors.

**Test Implementation:**

Create `Bagira.SimHost.Tests/MissionExecutionSystemTests.cs`:

```csharp
[TestClass]
public class MissionExecutionSystemTests
{
    [TestMethod]
    public void ExecuteMoveToLocation_NavigatesToTarget()
    {
        // Arrange
        var world = new FdpWorld();
        var vehicleAPI = new VehicleAPI(world);
        var entityMap = new NetworkEntityMap();
        var missionSystem = new MissionExecutionSystem(vehicleAPI, entityMap);
        world.AddSystem(missionSystem);
        
        var entity = world.NewEntity();
        entity.Set(new VehicleState { Position = new Vector2(0, 0), Forward = Vector2.UnitY, Speed = 0 });
        
        var mission = new EntityMissionComponent
        {
            EntityId = 1,
            Plan = new MissionPlan
            {
                ActiveTaskId = Guid.NewGuid(),
                Tasks = new List<MissionTask>
                {
                    new MissionTask
                    {
                        TaskId = mission.Plan.ActiveTaskId,
                        BehaviorId = "MoveToLocation",
                        BehaviorParams = JsonSerializer.Serialize(new MoveToLocationParams { X = 100, Y = 200 }),
                        State = eTaskState.TASK_ACTIVE
                    }
                }
            }
        };
        
        entity.Set(mission);
        
        // Act
        world.Update(0.016f);
        
        // Assert
        // Verify NavState set by VehicleAPI
        Assert.IsTrue(entity.Has<NavState>());
        var navState = entity.Get<NavState>();
        Assert.AreEqual(new Vector2(100, 200), navState.TargetPosition);
    }
    
    [TestMethod]
    public void ExecuteFollowRoute_AdvancesThroughWaypoints()
    {
        // Test route with 3 waypoints
        // Verify CurrentWaypointIndex advances
    }
    
    [TestMethod]
    public void AdvanceToNextTask_UpdatesTaskState()
    {
        // Test task state transitions
        // Verify PLANNED → ACTIVE → DONE sequence
    }
}
```

**Acceptance Criteria:**
- ✅ MoveToLocation test passes
- ✅ FollowRoute waypoint test passes
- ✅ Task state transition test passes
- ✅ Mission completion test passes

**Estimated Effort:** 1 day

**Dependencies:** S4.6

---

## Phase S5: Main Application Shell (3 days)

### Task S5.1: Implement Program.cs Entry Point

**Goal:** Create main application with initialization.

**Implementation:**

Update `Program.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bagira.SimHost.Systems;
using Bagira.SimHost.Configuration;
using Bagira.Map.Definitions.Tkb;
using Bagira.DDS.DataModel;
using FDP.Kernel;
using FDP.Toolkit.Time;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Tkb;
using FDP.Toolkit.Replication;
using FDP.Toolkit.Replication.Services;
using CarKinem.Systems;
using CarKinem.Commands;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Toolkit.Geographic;
using ModuleHost.Core;
using ModuleHost.Network.Cyclone;
using ModuleHost.Network.Cyclone.Services;

namespace Bagira.SimHost
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("===========================================");
            Console.WriteLine("  Bagira SimHost Mock (BDC SST)");
            Console.WriteLine("  Version 1.0");
            Console.WriteLine("===========================================");
            Console.WriteLine();
            
            // Load configuration
            var config = SimHostConfig.Load("config.json");
            
            Console.WriteLine($"[SimHost] Domain ID: {config.DomainId}");
            Console.WriteLine($"[SimHost] Simulation Rate: {config.SimulationRateHz} Hz");
            Console.WriteLine($"[SimHost] Origin: {config.GeodeticOrigin.Latitude}, {config.GeodeticOrigin.Longitude}");
            Console.WriteLine();
            
            // 1. Create DDS Participant
            Console.WriteLine("[SimHost] Creating DDS Participant...");
            using var participant = new DomainParticipant(domainId: config.DomainId);
            Console.WriteLine("[SimHost] DDS Participant created");
            
            // 2. Create FDP World (ECS)
            Console.WriteLine("[SimHost] Creating ECS World...");
            using var world = new FdpWorld();
            Console.WriteLine("[SimHost] ECS World created");
            
            // 3. Register Core Modules
            Console.WriteLine("[SimHost] Registering modules...");
            
            world.AddModule(new MasterTimeController());
            Console.WriteLine("  - MasterTimeController (time authority)");
            
            world.AddModule<EntityLifecycleModule>();
            Console.WriteLine("  - EntityLifecycleModule");
            
            var tkbDatabase = new TkbDatabase();
            BdcTkbCatalog.RegisterAll(tkbDatabase);
            world.AddModule(tkbDatabase);
            Console.WriteLine($"  - TkbDatabase ({tkbDatabase.GetAllTemplates().Count()} templates)");
            
            var networkEntityMap = new NetworkEntityMap(graveyardDurationFrames: 60);
            world.AddModule(networkEntityMap);
            Console.WriteLine("  - NetworkEntityMap");
            
            var geoTransform = new WGS84Transform(config.GeodeticOrigin);
            world.AddModule(geoTransform);
            Console.WriteLine("  - WGS84Transform");
            
            // 4. Start ID Allocator (server)
            Console.WriteLine("[SimHost] Starting ID Allocator...");
            var idAllocator = new DdsIdAllocator(participant, "IdAllocatorService");
            idAllocator.Start();
            Console.WriteLine("[SimHost] ID Allocator started");
            
            // 5. Register CarKinem Systems (Physics)
            Console.WriteLine("[SimHost] Registering CarKinem systems...");
            var roadNetwork = new RoadNetworkBlob();
            var trajectoryPool = new TrajectoryPoolManager();
            
            world.AddSystem(new SpatialHashSystem());
            world.AddSystem(new FormationTargetSystem());
            world.AddSystem(new VehicleCommandSystem());
            world.AddSystem(new CarKinematicsSystem(roadNetwork, trajectoryPool));
            Console.WriteLine("  - CarKinem systems registered");
            
            var vehicleAPI = new VehicleAPI(world);
            
            // 6. Register SimHost-Specific Systems
            Console.WriteLine("[SimHost] Registering SimHost systems...");
            world.AddSystem(new CreateEntityRequestHandler(participant, idAllocator, networkEntityMap, tkbDatabase));
            world.AddSystem(new MissionExecutionSystem(vehicleAPI, networkEntityMap));
            world.AddSystem(new GeoSpatialBridgeSystem(geoTransform));
            Console.WriteLine("  - SimHost systems registered");
            
            // 7. Register CycloneNetworkModule (owns ALL ingress, egress, and gateway systems)
            //
            // ⚠️  Do NOT call world.AddSystem(new SmartEgressSystem()) or
            //    world.AddSystem(new CycloneEgressSystem(...)). Those are internal to
            //    CycloneNetworkModule and adding them separately causes double execution.
            //
            // Only supply the Translators list — the Module registers its own internal
            // systems (CycloneIngressSystem, CycloneEgressSystem, NetworkGatewaySystem)
            // automatically when kernel.RegisterModule(networkModule) is called.
            Console.WriteLine("[SimHost] Registering CycloneNetworkModule...");
            
            var nodeMapper    = new NodeIdMapper(localDomain: 0, localInstance: config.InstanceId);
            var topology      = new NetworkTopology { IsServer = true };
            var serialisation = new SerializationRegistry();
            
            // Build translator list
            var translators = new List<ITranslator>();
            translators.Add(new CreateEntityRequestTranslator(participant, eventBus));
            translators.Add(new CreateEntityAckTranslator(participant, eventBus));
            translators.Add(new EntityMasterTranslator(participant, networkEntityMap));
            translators.Add(new GeoSpatialTranslator(participant, networkEntityMap));
            
            // Auto-translators for types tagged [FdpDescriptor] in Bagira.DDS.DataModel
            var (autoTranslators, _) = ReplicationBootstrap.CreateAutoTranslators(
                participant, typeof(Program).Assembly, networkEntityMap);
            translators.AddRange(autoTranslators);
            
            var elm = world.GetModule<EntityLifecycleModule>(); // retrieved after AddModule above
            var networkModule = new CycloneNetworkModule(
                participant, nodeMapper, idAllocator, topology, elm,
                serialisation, translators, networkEntityMap
            );
            kernel.RegisterModule(networkModule); // ← this registers all network systems internally
            Console.WriteLine("  - CycloneNetworkModule registered (egress/ingress/gateway)");
            
            Console.WriteLine();
            Console.WriteLine("[SimHost] Initialization complete");
            Console.WriteLine($"[SimHost] Entering simulation loop ({config.SimulationRateHz} Hz)...");
            Console.WriteLine();
            
            // 8. Initialise kernel and enter main loop
            kernel.Initialize();
            await RunSimulationLoop(kernel, config.SimulationRateHz);
        }
        
        static async Task RunSimulationLoop(ModuleHostKernel kernel, int targetRateHz)
        {
            float targetDeltaTime = 1.0f / targetRateHz;
            var stopwatch = new System.Diagnostics.Stopwatch();
            
            ulong frameCounter = 0;
            
            while (true)
            {
                stopwatch.Restart();
                
                // Update all registered modules and their systems via kernel
                kernel.Update(); // TimeController drives dt internally
                
                frameCounter++;
                
                // Frame rate limiting
                stopwatch.Stop();
                double elapsed = stopwatch.Elapsed.TotalSeconds;
                double sleepTime = targetDeltaTime - elapsed;
                
                if (sleepTime > 0)
                {
                    await Task.Delay((int)(sleepTime * 1000));
                }
                
                // Periodic status
                if (frameCounter % (targetRateHz * 10) == 0) // Every 10 seconds
                {
                    double actualDeltaTime = elapsed + (sleepTime > 0 ? sleepTime : 0);
                    double actualFPS = 1.0 / actualDeltaTime;
                    Console.WriteLine($"[SimHost] Frame {frameCounter}: {actualFPS:F1} FPS");
                }
                
                // Check for exit condition (Ctrl+C handled by OS)
            }
        }
    }
}
```

**Acceptance Criteria:**
- ✅ Main() entry point compiles
- ✅ `CycloneNetworkModule` constructed with full translator list and registered via `kernel.RegisterModule`
- ✅ No standalone `SmartEgressSystem` or `CycloneEgressSystem` added anywhere outside the module
- ✅ All application modules initialized
- ✅ Main loop runs at target frame rate
- ✅ Graceful startup/shutdown

**Estimated Effort:** 1.5 days

**Dependencies:** S1.1, S2.1, S3.1, S4.1

---

### Task S5.2: Create Configuration System

**Goal:** JSON configuration file for settings.

**Implementation:**

Create `Configuration/SimHostConfig.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using Bagira.DDS.DataModel;

namespace Bagira.SimHost.Configuration
{
    public class SimHostConfig
    {
        public int DomainId { get; set; } = 0;
        public int SimulationRateHz { get; set; } = 60;
        public GeoPosition GeodeticOrigin { get; set; } = new()
        {
            Latitude = 50.0755,
            Longitude = 14.4378,
            Altitude = 200.0
        };
        
        public static SimHostConfig Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[SimHost] Config file not found: {filePath}, using defaults");
                var defaultConfig = new SimHostConfig();
                Save(defaultConfig, filePath);
                return defaultConfig;
            }
            
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<SimHostConfig>(json) ?? new SimHostConfig();
        }
        
        public static void Save(SimHostConfig config, string filePath)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(filePath, json);
            Console.WriteLine($"[SimHost] Config saved to: {filePath}");
        }
    }
}
```

Create `config.json`:

```json
{
  "DomainId": 0,
  "SimulationRateHz": 60,
  "GeodeticOrigin": {
    "Latitude": 50.0755,
    "Longitude": 14.4378,
    "Altitude": 200.0
  }
}
```

**Acceptance Criteria:**
- ✅ Configuration class defined
- ✅ JSON loading/saving works
- ✅ Default config generated if missing
- ✅ config.json file created

**Estimated Effort:** 0.5 days

**Dependencies:** S5.1

---

### Task S5.3: Add Logging and Diagnostics

**Goal:** Comprehensive logging for debugging.

**Implementation:**

Create `Utilities/Logger.cs`:

```csharp
using System;

namespace Bagira.SimHost.Utilities
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
    
    public static class Logger
    {
        public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;
        
        public static void Debug(string message)
        {
            Log(LogLevel.Debug, message);
        }
        
        public static void Info(string message)
        {
            Log(LogLevel.Info, message);
        }
        
        public static void Warning(string message)
        {
            Log(LogLevel.Warning, message);
        }
        
        public static void Error(string message)
        {
            Log(LogLevel.Error, message);
        }
        
        private static void Log(LogLevel level, string message)
        {
            if (level < MinimumLevel) return;
            
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var levelStr = level switch
            {
                LogLevel.Debug => "DEBUG",
                LogLevel.Info => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                _ => "     "
            };
            
            Console.WriteLine($"[{timestamp}] [{levelStr}] {message}");
        }
    }
}
```

Update logging throughout code to use Logger instead of Console.WriteLine.

**Acceptance Criteria:**
- ✅ Logger class implemented
- ✅ Log levels working
- ✅ Timestamps included
- ✅ All systems use Logger

**Estimated Effort:** 0.5 days

**Dependencies:** S5.2

---

### Task S5.4: Add Graceful Shutdown

**Goal:** Handle Ctrl+C and cleanup resources.

**Implementation:**

Update `Program.cs`:

```csharp
static async Task Main(string[] args)
{
    // Setup Ctrl+C handler
    var cancellationTokenSource = new CancellationTokenSource();
    Console.CancelKeyPress += (sender, e) =>
    {
        e.Cancel = true; // Prevent immediate termination
        Console.WriteLine();
        Console.WriteLine("[SimHost] Shutdown requested...");
        cancellationTokenSource.Cancel();
    };
    
    // ... initialization ...
    
    // 8. Main Loop with cancellation
    await RunSimulationLoop(world, config.SimulationRateHz, cancellationTokenSource.Token);
    
    // Cleanup
    Console.WriteLine("[SimHost] Shutting down...");
    idAllocator.Stop();
    Console.WriteLine("[SimHost] Shutdown complete");
}

static async Task RunSimulationLoop(FdpWorld world, int targetRateHz, CancellationToken cancellationToken)
{
    // ... existing loop code ...
    
    while (!cancellationToken.IsCancellationRequested)
    {
        // ... update logic ...
    }
    
    Console.WriteLine("[SimHost] Simulation loop terminated");
}
```

**Acceptance Criteria:**
- ✅ Ctrl+C handler registered
- ✅ Graceful shutdown message
- ✅ Resources cleaned up
- ✅ ID allocator stopped

**Estimated Effort:** 0.5 days

**Dependencies:** S5.3

---

## Phase S6: Integration Testing (3 days)

### Task S6.1: Test Entity Creation Flow

**Goal:** End-to-end test with mock IOS client.

**Test Scenario:**
1. IOS Mock publishes CreateEntityRequest
2. SimHost receives request
3. SimHost allocates ID
4. SimHost creates entity
5. SimHost sends CreateEntityAck
6. IOS receives ACK
7. SimHost publishes EntityMaster (egress)
8. IOS ingests EntityMaster

**Implementation:**

Create `Bagira.SimHost.Integration.Tests/EntityCreationFlowTests.cs`:

```csharp
[TestClass]
public class EntityCreationFlowTests
{
    [TestMethod]
    public async Task FullFlow_IOSCreateTank_SimHostCreatesAndPublishes()
    {
        // Arrange
        using var participant = new DomainParticipant();
        
        // Start SimHost (background)
        var simHost = new SimHostInstance();
        await simHost.StartAsync(participant);
        
        // Mock IOS client
        var iosClient = new MockIOSClient(participant);
        
        // Act
        var request = new CreateEntityRequest
        {
            RequestId = Guid.NewGuid(),
            Owner = new NodeId { AppDomainId = 1, AppInstanceId = 1 },
            Flags = 0,
            InitialDescriptors = new List<EntityDescriptorUnion>
            {
                new() { _d = EDescriptorType.EntityMaster, EntityMasterPayload = new EntityMaster 
                {
                    TkbType = TkbEntityTypes.Tank_M1Abrams
                }}
            }
        };
        
        var ackTask = iosClient.WaitForAckAsync(request.RequestId, timeoutMs: 3000);
        iosClient.SendCreateRequest(request);
        
        var ack = await ackTask;
        
        // Assert ACK
        Assert.IsNotNull(ack);
        Assert.AreEqual(0, ack.ErrorCode);
        Assert.IsTrue(ack.NewEntityId > 0);
        
        // Wait for EntityMaster egress
        await Task.Delay(500);
        
        var entityMasterSample = iosClient.ReadEntityMaster(ack.NewEntityId);
        Assert.IsNotNull(entityMasterSample);
        Assert.AreEqual(TkbEntityTypes.Tank_M1Abrams, entityMasterSample.TkbType);
        
        // Cleanup
        await simHost.StopAsync();
    }
}
```

**Acceptance Criteria:**
- ✅ ACK received within timeout
- ✅ NewEntityId valid
- ✅ EntityMaster published
- ✅ TkbType correct

**Estimated Effort:** 1 day

**Dependencies:** S5.4

---

### Task S6.2: Test Mission Execution

**Goal:** Verify vehicle navigates according to mission.

**Test Scenario:**
1. Create vehicle entity
2. Set EntityMission with MoveToLocation task
3. Run simulation for 10 seconds
4. Verify vehicle position changed
5. Verify GeoSpatial updates published
6. Verify task state transitions

**Implementation:**

Create `Bagira.SimHost.Integration.Tests/MissionExecutionFlowTests.cs`:

```csharp
[TestMethod]
public async Task MoveToLocation_VehicleNavigates()
{
    // Arrange
    var simHost = new SimHostInstance();
    await simHost.StartAsync();
    
    // Create vehicle
    var createAck = await simHost.CreateEntityAsync(TkbEntityTypes.Tank_M1Abrams, 
        position: new Vector2(0, 0));
    
    // Set mission
    var mission = new EntityMission
    {
        EntityId = createAck.NewEntityId,
        Plan = new MissionPlan
        {
            ActiveTaskId = Guid.NewGuid(),
            Tasks = new List<MissionTask>
            {
                new()
                {
                    TaskId = ...,
                    BehaviorId = "MoveToLocation",
                    BehaviorParams = JsonSerializer.Serialize(new { X = 1000, Y = 1000 }),
                    State = eTaskState.TASK_ACTIVE
                }
            }
        }
    };
    
    simHost.PublishEntityMission(mission);
    
    // Act - Run simulation
    await simHost.RunForSeconds(10);
    
    // Assert
    var finalGeoSpatial = simHost.ReadGeoSpatial(createAck.NewEntityId);
    Assert.IsNotNull(finalGeoSpatial);
    
    // Verify vehicle moved (position should have changed)
    // Convert GeoPosition back to Vector2
    var finalPos = simHost.GeoToCartesian(finalGeoSpatial.Pos);
    float distance = Vector2.Distance(Vector2.Zero, finalPos);
    
    Assert.IsTrue(distance > 50, "Vehicle should have moved significantly");
    
    // Cleanup
    await simHost.StopAsync();
}
```

**Acceptance Criteria:**
- ✅ Vehicle position changes
- ✅ GeoSpatial updates published
- ✅ Task state becomes DONE
- ✅ Vehicle navigates toward target

**Estimated Effort:** 1 day

**Dependencies:** S6.1

---

### Task S6.3: Performance Testing

**Goal:** Verify 60 Hz sustained with 100 entities.

**Test Scenario:**
1. Create 100 tank entities
2. Assign missions to all
3. Run simulation for 60 seconds
4. Measure frame rate
5. Verify no frame drops below 58 FPS

**Implementation:**

Create `Bagira.SimHost.Integration.Tests/PerformanceTests.cs`:

```csharp
[TestMethod]
public async Task Performance_100Entities_Maintains60Hz()
{
    // Arrange
    var simHost = new SimHostInstance();
    simHost.EnablePerformanceMetrics();
    await simHost.StartAsync();
    
    // Create 100 entities
    var entities = new List<int>();
    for (int i = 0; i < 100; i++)
    {
        var ack = await simHost.CreateEntityAsync(TkbEntityTypes.Tank_M1Abrams,
            position: new Vector2(i * 10, i * 10));
        entities.Add(ack.NewEntityId);
    }
    
    // Assign simple missions
    foreach (var entityId in entities)
    {
        var mission = CreateSimpleMission(entityId);
        simHost.PublishEntityMission(mission);
    }
    
    // Act - Run for 60 seconds
    await simHost.RunForSeconds(60);
    
    // Assert
    var metrics = simHost.GetPerformanceMetrics();
    
    Assert.IsTrue(metrics.AverageFPS >= 58, $"Average FPS too low: {metrics.AverageFPS}");
    Assert.IsTrue(metrics.MinFPS >= 55, $"Min FPS too low: {metrics.MinFPS}");
    
    Console.WriteLine($"Average FPS: {metrics.AverageFPS:F2}");
    Console.WriteLine($"Min FPS: {metrics.MinFPS:F2}");
    Console.WriteLine($"Max FPS: {metrics.MaxFPS:F2}");
    
    // Cleanup
    await simHost.StopAsync();
}
```

**Acceptance Criteria:**
- ✅ 100 entities created
- ✅ Average FPS ≥ 58
- ✅ Min FPS ≥ 55
- ✅ No crashes or errors

**Estimated Effort:** 1 day

**Dependencies:** S6.2

---

## Phase S7: Documentation (1 day)

### Task S7.1: Create User Guide

**Goal:** Document how to run and configure SimHost.

**Deliverables:**

Create `docs/SimHost-User-Guide.md`:

```markdown
# SimHost User Guide

## Overview

SimHost is the "truth" authority for the BDC SST simulation. It runs vehicle physics, executes missions, and publishes entity state to DDS.

## Requirements

- .NET 8.0 SDK
- CycloneDDS runtime
- FDP libraries

## Building

```
cd Bagira.SimHost
dotnet build
```

## Running

```
dotnet run
```

SimHost will:
1. Load `config.json` (or create default)
2. Initialize DDS participant
3. Register TKB entity templates
4. Start ID allocator service
5. Enter simulation loop at 60 Hz

## Configuration

Edit `config.json`:

```json
{
  "DomainId": 0,
  "SimulationRateHz": 60,
  "GeodeticOrigin": {
    "Latitude": 50.0755,
    "Longitude": 14.4378,
    "Altitude": 200.0
  }
}
```

## Creating Entities

Entities are created via CreateEntityRequest DDS topic:

1. IOS/IG publishes CreateEntityRequest
2. SimHost allocates ID
3. SimHost creates entity from TKB template
4. SimHost publishes CreateEntityAck
5. SimHost publishes EntityMaster, GeoSpatial

## Missions

Assign missions via EntityMission DDS topic:

```json
{
  "EntityId": 12345,
  "Plan": {
    "ActiveTaskId": "...",
    "Tasks": [
      {
        "TaskId": "...",
        "BehaviorId": "MoveToLocation",
        "BehaviorParams": "{\"X\": 1000, \"Y\": 1000, \"Speed\": 15}",
        "State": "TASK_ACTIVE"
      }
    ]
  }
}
```

Supported behaviors:
- `MoveToLocation` - Navigate to point
- `FollowRoute` - Waypoint sequence
- `JoinFormation` - Follow leader
- `Idle` - Wait for triggers

## Monitoring

SimHost logs to console:
- Entity creation
- Mission execution
- Task state transitions
- Frame rate (every 10 seconds)

## Shutdown

Press `Ctrl+C` for graceful shutdown.

## See Also

- [DESIGN-SIMHOST.md](../design1_AG/DESIGN-SIMHOST.md)
- [TASK-DETAILS-SIMHOST.md](../design1_AG/TASK-DETAILS-SIMHOST.md)
```

**Acceptance Criteria:**
- ✅ User guide created
- ✅ All sections complete
- ✅ Examples included

**Estimated Effort:** 0.5 days

**Dependencies:** S6.3

---

### Task S7.2: Create Configuration Reference

**Goal:** Document all configuration options.

**Deliverables:**

Create `docs/SimHost-Configuration.md`:

```markdown
# SimHost Configuration Reference

## config.json

### DomainId

**Type:** `int`  
**Default:** `0`

DDS domain ID. Must match IOS and IG for communication.

### SimulationRateHz

**Type:** `int`  
**Default:** `60`

Simulation update rate in Hz. Higher rates increase CPU usage.

Recommended values:
- `30` - Low performance
- `60` - Standard (recommended)
- `120` - High fidelity

### GeodeticOrigin

**Type:** `GeoPosition`

Simulation origin point in WGS84 coordinates.

Properties:
- `Latitude` (double) - Degrees North
- `Longitude` (double) - Degrees East
- `Altitude` (double) - Meters above WGS84 ellipsoid

Example locations:
- Prague: `50.0755, 14.4378`
- New York: `40.7128, -74.0060`
- Tokyo: `35.6762, 139.6503`

## Environment Variables

### CYCLONEDDS_URI

Override CycloneDDS configuration:

```
export CYCLONEDDS_URI=file:///path/to/cyclonedds.xml
```

## See Also

- [SimHost User Guide](./SimHost-User-Guide.md)
```

**Acceptance Criteria:**
- ✅ Configuration reference created
- ✅ All options documented
- ✅ Examples provided

**Estimated Effort:** 0.25 days

**Dependencies:** S7.1

---

### Task S7.3: Add Code Documentation

**Goal:** XML documentation for all public APIs.

**Tasks:**
1. Add `<summary>` tags to all public classes
2. Add `<param>` tags to all public methods
3. Add `<returns>` tags where applicable
4. Generate XML documentation file

**Documentation Coverage:**
- `CreateEntityRequestHandler` - Request handler system
- `MissionExecutionSystem` - Mission execution engine
- `GeoSpatialBridgeSystem` - Coordinate conversion
- All component structs
- All public methods

**Acceptance Criteria:**
- ✅ All public APIs documented
- ✅ XML file generated
- ✅ No documentation warnings

**Estimated Effort:** 0.25 days

**Dependencies:** S7.2

---

## Summary Tables

### Effort by Phase

| Phase | Focus | Days | Dependencies |
|-------|-------|------|--------------|
| S1 | Project Setup | 1 | None |
| S2 | CreateEntityRequestHandler | 3 | S1 |
| S3 | GeoSpatialBridgeSystem | 2 | S1 |
| S4 | MissionExecutionSystem | 5 | S1 |
| S5 | Main Application Shell | 3 | S1, S2, S3, S4 |
| S6 | Integration Testing | 3 | S5 |
| S7 | Documentation | 1 | S6 |
| **TOTAL** | | **18** | |

### Critical Path

```
S1 (1d) → S2 (3d) → S5 (3d) → S6 (3d) → S7 (1d) = 11 days minimum
```

### Parallelization (2 developers)

**Week 1:**
- Dev1: S1 + S2 (4 days)
- Dev2: S1 + S3 (3 days)

**Week 2:**
- Dev1: S2 (complete) + S4 (start, 3 days)
- Dev2: S4 (parallel, 5 days total)

**Week 3:**
- Dev1: S4 (complete) + S5 (3 days)
- Dev2: S4 (complete) + S5 (assist)

**Week 4:**
- Both: S6 (3 days) + S7 (1 day)

**Total: ~3.5 weeks with 2 developers**

---

## Navigation

- **[⬆ Back to DESIGN-SIMHOST.md](./DESIGN-SIMHOST.md)**
- **[➜ Task Tracker](./TASK-TRACKER.md)**
- **[⬅ Shared Tasks](./TASK-DETAILS-SHARED.md)**
