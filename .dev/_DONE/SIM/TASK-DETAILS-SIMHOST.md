# SimHost Implementation Tasks

**Version:** 1.0  
**Date:** 2026-02-13  
**Status:** Ready for Development

**Parent Documents**: [DESIGN-SIMHOST.md](./DESIGN-SIMHOST.md) | [TASK-TRACKER.md](./TASK-TRACKER.md)

## Overview

This document provides **detailed task breakdown** for implementing SimHost Mock. Each task includes acceptance criteria, estimated effort, and dependencies.

**Total Effort:** ~18 developer-days (~3.5 weeks for 1 developer)

**Key Insight:** Most infrastructure exists (CarKinem, networking, ECS, Behavior toolkit, Geographic module). Focus on request handlers, mission adapter, and application shell.

---

## Phase S1: Project Setup (1 day)

### Task S1.1: Create SimHost Console Project

**Goal:** Create C# console application for SimHost.

**Steps:**
1. Create new project:
   ```
   dotnet new console -n Hrot.SimHost -f net8.0
   ```
2. Add to IOS-IG-SimHost.sln solution:
   ```
   Location: Hrot.SimHost/
   ```
3. Create folder structure:
   ```
   Hrot.SimHost/
     Program.cs
     DoctrineIds.cs
     Components/
       NetworkIdComponent.cs
     Systems/
       CreateEntityRequestHandler.cs
       MissionAdapterSystem.cs
       JoinFormationExecutor.cs
     Translators/
       CreateEntityRequestTranslator.cs
       CreateEntityAckTranslator.cs
       EntityMissionTranslator.cs
       EntityMissionEgressTranslator.cs
     Configuration/
       SimHostConfig.cs
   ```

**Acceptance Criteria:**
- âś… Project created and compiles
- âś… Folder structure in place
- âś… Added to solution file

**Estimated Effort:** 0.25 days

**Dependencies:** None

---

### Task S1.2: Add Project References

**Goal:** Configure all required dependencies.

**Steps:**
1. Add FDP project references:
   ```xml
   <ProjectReference Include="..\Hrot.NED\Hrot.NED.csproj" />
   <ProjectReference Include="..\Hrot.Map.Common\Hrot.Map.Common.csproj" />
   <ProjectReference Include="..\Hrot.Map.Definitions\Hrot.Map.Definitions.csproj" />
   <ProjectReference Include="..\FDP\Kernel\Fdp.Kernel\Fdp.Kernel.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.CarKinem\FDP.Toolkit.CarKinem.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Lifecycle\FDP.Toolkit.Lifecycle.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Replication\FDP.Toolkit.Replication.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Tkb\FDP.Toolkit.Tkb.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Time\FDP.Toolkit.Time.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\Fdp.Toolkit.Geographic\Fdp.Toolkit.Geographic.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Behavior\FDP.Toolkit.Behavior.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Navigation\FDP.Toolkit.Navigation.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Physics\FDP.Toolkit.Physics.csproj" />
   <ProjectReference Include="..\FDP\ModuleHost\ModuleHost.Network.Cyclone\ModuleHost.Network.Cyclone.csproj" />
   <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.NetworkSpawning\FDP.Toolkit.NetworkSpawning.csproj" />
   ```

2. Add NuGet packages:
   ```xml
   <PackageReference Include="CycloneDDS.NET" Version="*" />
   <PackageReference Include="System.Text.Json" Version="7.0.0" />
   ```

**Acceptance Criteria:**
- âś… All project references resolve
- âś… NuGet packages restore successfully
- âś… Project builds without errors

**Estimated Effort:** 0.25 days

**Dependencies:** S1.1

---

### Task S1.3: Define ECS Components

**Goal:** Create component definitions for SimHost.

> âš ď¸Ź **Architecture note â€” avoid duplicate type definitions:**
> FDP uses the **Shared Data Model** types from `Hrot.NED` directly as ECS components. Types such as `EntityMaster`, `WorldPos`, `WorldPos`, `EntityInfo`, and `EntityMission` are already defined there and are decorated with `[FdpDescriptor]`, which allows the `AutoCycloneTranslator` to replicate them automatically over DDS.
>
> **Do NOT redefine local copies** (`EntityMasterComponent`, `WorldPosComponent`, etc.) that duplicate the fields of these DDS types. This creates:
> - Schema drift (local copy diverges from the wire format)
> - Broken auto-replication (the `AutoCycloneTranslator` cannot find its type)
> - Redundant conversion code in handlers
>
> The only components that should be **newly** defined in `Hrot.SimHost.Components` are ones that have **no corresponding DDS topic**: runtime/local state such as `NetworkIdComponent`.
> If a wrapper is truly necessary (e.g. to carry extra simulation-only state alongside the replicated data), mark it with `[FdpDescriptor]` so `AutoCycloneTranslator` picks it up.

**Implementation:**

Only create `Components/NetworkIdComponent.cs` (a genuinely local, non-replicated component):
```csharp
namespace Hrot.SimHost.Components
{
    /// <summary>
    /// Maps an ECS entity to its allocated network entity ID.
    /// This is a local runtime component â€” not replicated over DDS directly.
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
// DO NOT create EntityMasterComponent, WorldPosComponent, etc.
// Use Hrot.NED types as ECS components directly:
using Hrot.NED;

// Set EntityMaster data on entity (type is already [FdpDescriptor]-tagged in DataModel)
world.AddComponent(entity, new EntityMaster
{
    EntityId  = networkId,
    TkbType   = tkbType,
    DisType   = disType,
    Flags     = 0
});

// AutoCycloneTranslator will replicate EntityMaster over DDS automatically
// because the type carries [FdpDescriptor] â€” no manual translator stub needed.
```

**Folder structure update for S1.1** â€” remove the duplicate component files:
```
Hrot.SimHost/
  Components/
    NetworkIdComponent.cs     âś… Keep (local, non-replicated)
    EntityMasterComponent.cs  âťŚ Delete â€” use Hrot.NED.EntityMaster
    EntityInfoComponent.cs    âťŚ Delete â€” use Hrot.NED.EntityInfo
    WorldPosComponent.cs    âťŚ Delete â€” use Hrot.NED.WorldPos
    WorldPosComponent.cs  âťŚ Delete â€” use Hrot.NED.WorldPos
    EntityMissionComponent.cs âťŚ Delete â€” use Hrot.NED.EntityMission
```

**Acceptance Criteria:**
- âś… `NetworkIdComponent` created
- âś… No local duplicates of `Hrot.NED` types
- âś… ECS systems use `EntityMaster`, `WorldPos`, etc. from the shared data model
- âś… XML documentation complete

**Estimated Effort:** 0.25 days (reduced â€” less boilerplate to write)

**Dependencies:** S1.2


---

### Task S1.4: Create Hrot.SimHost.Tests Project

**Goal:** Setup unit test project.

**Steps:**
1. Create project:
   ```bash
   dotnet new mstest -n Hrot.SimHost.Tests -f net8.0
   ```
2. Location: `Hrot.SimHost.Tests/`
3. Add to solution `IOS-IG-SimHost.sln`.
4. Add reference to `Hrot.SimHost` project.

**Acceptance Criteria:**
- âś… Test project created
- âś… Dependencies resolved

**Estimated Effort:** 0.1 days

**Dependencies:** S1.1

---

### Task S1.3b: Audit TKB Descriptors for SimTransform/SimVelocity

**Goal:** Confirm every physical entity template in `BdcTkbBuilder` includes `SimTransform` and `SimVelocity`, and that `VehicleState` is added **only** to wheeled/tracked ground platforms.

**Steps:**
1. Open `Hrot.Map.Definitions/Tkb/BdcTkbBuilder.cs` and enumerate all `RegisterTemplate` calls.
2. Verify each template's component list contains `SimTransform` and `SimVelocity`.
3. Verify `VehicleState` is present **only** on templates whose `TkbEntityType` corresponds to wheeled/tracked ground vehicles (tanks, APCs, cars, trucks). Infantry, aircraft, naval, and pure-ghost entities must **not** receive `VehicleState`.
4. Add missing `SimTransform`/`SimVelocity` entries where absent. Remove `VehicleState` from non-wheeled templates.

> âš ď¸Ź **VehicleState scope reminder:** `VehicleState` holds only `Speed`, `SteerAngle`, `Accel`, `CurrentLaneIndex` (Phase 0 trim). Any template that previously stored position/heading in `VehicleState` must now use `SimTransform` exclusively.

**Acceptance Criteria:**
- âś… All entity templates have `SimTransform` and `SimVelocity`
- âś… `VehicleState` present only on wheeled/tracked ground platforms
- âś… `LinearKinematicsSystem` will integrate non-wheeled entities correctly (no `VehicleState` filter conflict)
- âś… All existing TKB-related unit tests still pass

**Estimated Effort:** 0.25 days

**Dependencies:** S1.2

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
using Hrot.NED;
using Hrot.SimHost.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Tkb;
using Fdp.Kernel;
using ModuleHost.Network.Cyclone;

namespace Hrot.SimHost.Systems
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
- âś… Class compiles
- âś… DDS reader/writer initialized
- âś… OnUpdate() processes samples
- âś… ProcessRequest() stubbed

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
- âś… ID allocation working
- âś… Error handling implemented
- âś… Logs indicate allocation success

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
- âś… TkbType extraction working
- âś… Template lookup working
- âś… Error handling for invalid template ID

**Estimated Effort:** 0.5 days

**Dependencies:** S2.2

---

### Task S2.4: Publish SpawnEntityCommand

**Goal:** Replace manual 11-step ECS entity creation with a single `SpawnEntityCommand` event, delegating all spawning logic to `FDP.Toolkit.NetworkSpawning.NetworkSpawningSystem`.

> âš ď¸Ź **Architecture note â€” thin translator, not thick creator:**
> `CreateEntityRequestSystem` is a DDS-to-ECS **translator**. It must not call `world.CreateEntity()`, `template.ApplyTo()`, `elm.BeginConstruction()`, or manipulate `NetworkEntityMap` directly. All of that is now owned by `NetworkSpawningSystem`.
>
> The system's only job is: allocate a network ID â†’ convert descriptors â†’ publish `SpawnEntityCommand`. `NetworkSpawningSystem` processes the command and does the rest on the next ECS tick.

**Implementation:**

Replace `ProcessRequest()` body (after ID allocation):

```csharp
// 2. Convert DDS descriptors â†’ ECS component list via DescriptorMapper
var initialComponents =
    DescriptorMapper.MapToComponents(request.InitialDescriptors, _geoTransform);
long tkbType = DescriptorMapper.ExtractTkbType(request.InitialDescriptors);

if (tkbType == 0)
{
    SendErrorAck(request.RequestId, newEntityId: 0, errorCode: 400);
    return;
}

// 3. Delegate all ECS spawning to NetworkSpawningSystem
_eventBus.Publish(new SpawnEntityCommand
{
    NetworkId         = newEntityId,
    TkbType           = tkbType,
    OwnerNodeId       = _localNodeId,
    InitType          = ReliableInitType.AllPeers,
    InitialComponents = initialComponents,
    RequestId         = request.RequestId
});

// 4. ACK immediately â€” entity will be live in ECS on the next frame
_eventBus.Publish(new CreateEntityAckEvent
{
    Ack = new CreateEntityAck
    {
        RequestId   = request.RequestId,
        NewEntityId = newEntityId,
        ErrorCode   = 0
    }
});
```

**Acceptance Criteria:**
- âś… `SpawnEntityCommand` published with correct `NetworkId`, `TkbType`, `OwnerNodeId`, `InitType=AllPeers`
- âś… `SpawnEntityCommand.InitialComponents` contains the mapped component list
- âś… No direct call to `world.CreateEntity()`, `template.ApplyTo()`, or `elm.BeginConstruction()` in this file
- âś… ACK sent immediately after publishing command
- âś… `tkbType == 0` handled as a 400 error ACK

**Estimated Effort:** 0.5 days

**Dependencies:** S2.3, NS1 (FDP.Toolkit.NetworkSpawning complete)

---

### Task S2.5: Implement DescriptorMapper

**Goal:** Create `Hrot.SimHost.Util.DescriptorMapper` â€” the application-side adapter that converts a `List<EntityDescriptorUnion>` from a `CreateEntityRequest` into a `List<object>` suitable for `SpawnEntityCommand.InitialComponents`.

> âš ď¸Ź **Architecture note â€” DescriptorMapper lives in SimHost, not the toolkit:**
> `FDP.Toolkit.NetworkSpawning` is deliberately generic and has no dependency on `Hrot.NED`. The application (SimHost) is responsible for bridging DDS-specific types to generic `object` components via `DescriptorMapper`, keeping the toolkit clean.
>
> `EntityComponentReflector` (inside the toolkit) will call `world.SetComponent(entity, componentType, componentInstance)` for each object in `InitialComponents`, so the objects must be valid ECS component types already registered in the world.

**Implementation:**

Create `Hrot.SimHost/Util/DescriptorMapper.cs`:

```csharp
namespace Hrot.SimHost.Util
{
    using Hrot.NED.Descriptors;
    using Fdp.Toolkit.Geographic;
    using FDP.Kernel;

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
                        // Use DDS model type directly â€” no local wrapper
                        result.Add(d.EntityMaster);
                        break;

                    case EDescriptorType.dtEntityInfo:
                        result.Add(d.EntityInfo);
                        break;

                    case EDescriptorType.dtWorldPos:
                        // Raw DDS component replicated via AutoCycloneTranslator
                        result.Add(d.WorldPos);
                        // Set SimTransform (world position + orientation) â€” used by ALL systems.
                        // This is the ONLY authoritative position source; never use VehicleState for position.
                        var cart = geo.ToCartesian(d.WorldPos.Pos);
                        float headingRad = d.WorldPos.Rot.Heading * (MathF.PI / 180f);
                        result.Add(new SimTransform
                        {
                            Position = new Vector3((float)cart.X, (float)cart.Y, (float)cart.Z),
                            Rotation = Quaternion.CreateFromYawPitchRoll(headingRad, 0f, 0f)
                        });
                        // VehicleState: physics metadata only (speed/steer scalars, no position).
                        // NetworkSpawningSystem applies this only when the TKB template includes VehicleState
                        // (i.e. wheeled/tracked entities); ignored for other entity types.
                        result.Add(new VehicleState { Speed = 0, SteerAngle = 0 });
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

**Acceptance Criteria:**
- âś… `ExtractTkbType` returns `EntityMaster.TkbType` or 0 if not present
- âś… `MapToComponents` returns `EntityMaster`, `EntityInfo` using DDS types directly (no wrappers)
- âś… `WorldPos` produces both `WorldPos` component and `VehicleState` for CarKinem
- âś… `HeadingToVector` converts degrees to unit-direction Vector2 correctly
- âś… Unknown descriptor types produce a warning log (not an exception)
- âś… Unit tests: all 3 descriptor types â†’ expected component list

**Estimated Effort:** 0.5 days

**Dependencies:** S2.4, NS1 (FDP.Toolkit.NetworkSpawning)

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
- âś… ACK published to DDS
- âś… Correct RequestId correlation
- âś… NewEntityId populated

**Estimated Effort:** 0.25 days

**Dependencies:** S2.5

---

### Task S2.7: Write Request Handler Tests

**Goal:** Unit test CreateEntityRequestHandler.

**Test Implementation:**

Create `Hrot.SimHost.Tests/CreateEntityRequestHandlerTests.cs`:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hrot.SimHost.Systems;
using Hrot.SimHost.Components;
using Hrot.NED;
using FDP.Toolkit.Tkb;
using FDP.Toolkit.Replication.Services;

namespace Hrot.SimHost.Tests
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
- âś… Valid request test passes
- âś… Invalid template test passes
- âś… ACK correlation verified
- âś… Entity exists in NetworkEntityMap

**Estimated Effort:** 0.75 days

**Dependencies:** S2.6

---

## Phase S3: Geographic Module Integration (2 days)

### Task S3.1: Register GeographicModule and Verify Egress

**Goal:** Register `GeographicModule` from `Fdp.Toolkit.Geographic` at kernel startup, confirming that `SimTransformBridgeSystem` converts `SimTransform`/`SimVelocity` â†’ `GeoTransform`/`GeoVelocity` for locally-owned entities, and that `WorldPosEgressTranslator` publishes correct DDS topics.

> `WorldPosBridgeModule` and `WorldPosBridgeSystem` are **not created**. The toolkit provides all bridge functionality.

**Steps:**
1. In `Program.cs` startup, register `GeographicModule` with the configured `WGS84Transform` (follow `NetworkDemoApp.cs` pattern). The module registers `SimTransformBridgeSystem` which runs post-physics.
2. Confirm the egress translator list passed to `CycloneNetworkModule` includes the already-implemented `WorldPosEgressTranslator`.
3. Run SimHost, spawn one entity via a `CreateEntityRequest`, let it sit for one second.
4. Assert that the DDS `WorldPos` topic receives a sample with `Pos.Latitude` and `Pos.Longitude` within 1Â° of the configured `GeodeticOrigin`.

**Acceptance Criteria:**
- âś… `GeographicModule` registered at kernel startup with correct `WGS84Transform`
- âś… `SimTransformBridgeSystem` runs post-physics (check system order in debug log)
- âś… `WorldPosEgressTranslator` publishes `WorldPos` DDS sample with plausible coordinates
- âś… No custom bridge code written in `Hrot.SimHost`

**Estimated Effort:** 2 days *(includes smoke-test integration run)*

**Dependencies:** S1.3, S2.5

---

## Phase S4: Behavior Toolkit Integration (5 days)

### Task S4.1: Register Behavior / Navigation / Physics Systems

**Goal:** Wire `FDP.Toolkit.Behavior` and `FDP.Toolkit.Navigation` systems into `SimulationLogicModule`, replacing the old `MissionExecutionSystem` placeholder.

**Steps:**
1. In `SimulationLogicModule.RegisterSystems()`, register in order:
   - `new MissionAdapterSystem(_doctrineRegistry, _entityMap)` â€” runs first each frame
   - `new ChannelArbitrationSystem()` â€” preempts stale channels on doctrine change
   - `new BTreeTickSystem(_doctrineRegistry)` â€” zero-alloc stack context, ticks the BTree
   - `new LocomotionDispatcherSystem()` â€” OnEnter/Execute/OnExit lifecycle for executors
   - `new MoveToExecutor()` â€” existing toolkit executor
   - `new FollowRouteExecutor()` â€” existing toolkit executor
   - `new JoinFormationExecutor(_vehicleAPI, _entityMap)` â€” new (implemented in S4.4)
   - *(existing)* `new SpatialHashSystem()`, `new FormationTargetSystem()`, `new VehicleCommandSystem()`, `new CarKinematicsSystem(...)`
   - `new LinearKinematicsSystem()` â€” for non-wheeled entities; already excludes `VehicleState` entities via its own query filter
2. Update `SimulationLogicModule` constructor to accept `DoctrineRegistry` and `NetworkEntityMap` parameters.
3. Write a minimal unit test verifying all systems register without error on an empty world.

**Acceptance Criteria:**
- âś… All systems registered in correct order
- âś… `LinearKinematicsSystem` does **not** process entities with `VehicleState` (verify via query inspection or test)
- âś… Unit test: empty world initialization passes without exception

**Estimated Effort:** 0.5 days

**Dependencies:** S1.3b

---

### Task S4.2: Implement EntityMissionTranslator and EntityMissionEgressTranslator

**Goal:** Sync the DDS `EntityMission` topic into the ECS (ingress) and publish `EntityMission` updates back to IOS when `ActiveTaskId` advances (egress).

**Implementation:**

Create `Translators/EntityMissionTranslator.cs` (DDS ingress â€” Managed Cyclone pattern):

```csharp
namespace Hrot.SimHost.Translators
{
    /// <summary>
    /// Ingress: subscribes to DDS EntityMission topic.
    /// On each valid sample, sets/updates the EntityMission ECS component on the matching entity.
    /// On NOT_ALIVE_DISPOSED, removes the component.
    /// </summary>
    public class EntityMissionTranslator : IManagedTranslator
    {
        private readonly DataReader<EntityMission> _reader;
        private readonly NetworkEntityMap _entityMap;

        public EntityMissionTranslator(DomainParticipant participant, NetworkEntityMap entityMap)
        {
            var sub = participant.CreateSubscriber();
            _reader = sub.CreateDataReader<EntityMission>("EntityMission");
            _entityMap = entityMap;
        }

        public void ReadAndApply(EntityRepository world)
        {
            var samples = _reader.Take();
            foreach (var s in samples)
            {
                if (!_entityMap.TryGetEntity(s.Data.EntityId, out var entity)) continue;

                if (s.Info.ValidData)
                    world.SetComponent(entity, s.Data);
                else if (s.Info.InstanceState == InstanceState.NotAliveDisposed)
                    world.RemoveComponent<EntityMission>(entity);
            }
        }
    }
}
```

Create `Translators/EntityMissionEgressTranslator.cs` (DDS egress):

```csharp
namespace Hrot.SimHost.Translators
{
    /// <summary>
    /// Egress: publishes EntityMission DDS topic whenever MissionAdapterSystem
    /// advances ActiveTaskId or marks a task failed.
    /// Uses ECS change detection (dirty flag) to avoid unnecessary publishes.
    /// </summary>
    public class EntityMissionEgressTranslator : IEgressTranslator
    {
        private readonly DataWriter<EntityMission> _writer;
        private readonly NetworkEntityMap _entityMap;

        public EntityMissionEgressTranslator(DomainParticipant participant, NetworkEntityMap entityMap)
        {
            var pub = participant.CreatePublisher();
            _writer = pub.CreateDataWriter<EntityMission>("EntityMission");
            _entityMap = entityMap;
        }

        public void WriteChanges(EntityRepository world)
        {
            // Query entities whose EntityMission component has been modified this frame
            var query = world.Query()
                .With<EntityMission>()
                .With<NetworkAuthority>() // locally-owned only
                .Changed<EntityMission>()
                .Build();

            foreach (var entity in query)
            {
                var mission = world.GetComponent<EntityMission>(entity);
                _writer.Write(mission);
            }
        }
    }
}
```

Register both translators in `Program.cs` translator list.

**Acceptance Criteria:**
- âś… `EntityMissionTranslator` sets/removes ECS `EntityMission` component on DDS sample receipt
- âś… `EntityMissionEgressTranslator` publishes DDS `EntityMission` when the component changes
- âś… Both translators registered in the `CycloneNetworkModule` translator list
- âś… Unit test: translator sets component for known entity, skips sample for unknown entity ID

**Estimated Effort:** 0.75 days

**Dependencies:** S4.1

---

### Task S4.3: Implement MissionAdapterSystem

**Goal:** Implement the thin adapter that maps the active `MissionTask.BehaviorId` string to a `DoctrineId`, writes parameters into `BrainBlackboard`, and monitors `LocomotionChannel.Status` to advance `ActiveTaskId`.

**Implementation:**

Create `Systems/MissionAdapterSystem.cs` following the architecture described in [DESIGN-SIMHOST.md Â§4.4](./DESIGN-SIMHOST.md#44-missionadaptersystem).

Key logic summary:
1. Query entities with `EntityMission` + `DoctrineState` + `BrainBlackboard`.
2. `DoctrineRegistry.TryGetId(task.BehaviorId, out int id)` â€” log warning and skip if not found.
3. If `DoctrineState.ActiveDoctrineHash != id`: set hash, call `DoctrineDefinition.ParseParams(task.BehaviorParams, ref blackboard)`.
4. Read `LocomotionChannel.Status`:
   - `NodeStatus.Success` â†’ `AdvanceToNextTask()`
   - `NodeStatus.Failure` â†’ `MarkTaskFailed()`
5. `AdvanceToNextTask`: mark current task `TASK_DONE`, activate next by setting `ActiveTaskId`; if no next task, remove `EntityMission` component.

**Acceptance Criteria:**
- âś… `MissionAdapter_ResolvesDoctrineId()`: given `BehaviorId="MoveToLocation"`, `DoctrineState.ActiveDoctrineHash` is set to `DoctrineIds.MoveTo_BT`
- âś… `MissionAdapter_AdvancesTaskOnSuccess()`: when `LocomotionChannel.Status == Success`, `ActiveTaskId` moves to the next task and previous task state is `TASK_DONE`
- âś… `MissionAdapter_MarksFailedOnChannelFailure()`: when status is `Failure`, current task state is `TASK_FAILED`
- âś… Unknown `BehaviorId` logs a warning and does not throw

**Estimated Effort:** 1.5 days

**Dependencies:** S4.2

---

### Task S4.4: Implement JoinFormationExecutor

**Goal:** Implement `JoinFormationExecutor : IActionExecutor<LocomotionChannel>` to cover the formation-joining behavior using the Behavior toolkit executor pattern.

**Implementation:**

Create `Systems/JoinFormationExecutor.cs`:

```csharp
namespace Hrot.SimHost.Systems
{
    using FDP.Toolkit.Behavior;
    using FDP.Toolkit.CarKinem;
    using FDP.Toolkit.Replication.Services;
    using Fdp.Kernel;

    /// <summary>
    /// Action executor for the JoinFormation behavior.
    /// OnEnter: looks up leader via NetworkEntityMap, calls VehicleAPI.CreateFormation().
    /// Execute: checks InFormationTag presence â†’ reports Success.
    /// Pattern: follows MoveToExecutor / FollowRouteExecutor conventions.
    /// </summary>
    public class JoinFormationExecutor : IActionExecutor<LocomotionChannel>
    {
        private readonly VehicleAPI _vehicleAPI;
        private readonly NetworkEntityMap _entityMap;

        public JoinFormationExecutor(VehicleAPI vehicleAPI, NetworkEntityMap entityMap)
        {
            _vehicleAPI = vehicleAPI;
            _entityMap = entityMap;
        }

        public void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            // Read params from BrainBlackboard (written by MissionAdapterSystem)
            var bb = world.GetComponent<BrainBlackboard>(entity);
            var p  = bb.Read<JoinFormationParams>();

            if (!_entityMap.TryGetEntity(p.LeaderNetworkId, out var leaderEntity))
            {
                FdpLog.Warn($"[JoinFormationExecutor] Leader {p.LeaderNetworkId} not found");
                channel.Status = NodeStatus.Failure;
                return;
            }

            FormationType ft = p.FormationType.ToLowerInvariant() switch
            {
                "column" => FormationType.Column,
                "line"   => FormationType.Line,
                _        => FormationType.Wedge
            };

            _vehicleAPI.CreateFormation(leaderEntity, ft);
            channel.Status = NodeStatus.Running;
        }

        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            // Formation join is one-shot: success once InFormationTag is present
            if (world.HasComponent<InFormationTag>(entity))
                channel.Status = NodeStatus.Success;
        }

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            // No cleanup needed; formation system manages its own state.
        }
    }

    /// <summary>Params struct read from BrainBlackboard for JoinFormation doctrine.</summary>
    public struct JoinFormationParams
    {
        public int    LeaderNetworkId;
        public string FormationType;   // "Wedge" | "Column" | "Line"
    }

    /// <summary>Tag added by VehicleAPI.CreateFormation â€” signals active formation membership.</summary>
    public struct InFormationTag
    {
        public int LeaderEntityId;
    }
}
```

**Acceptance Criteria:**
- âś… `OnEnter` calls `VehicleAPI.CreateFormation()` and sets `Status = Running` when leader found
- âś… `OnEnter` sets `Status = Failure` when leader not found
- âś… `Execute` sets `Status = Success` once `InFormationTag` is present on entity
- âś… Tests: `JoinFormation_LeaderFound_SetsRunning()`, `JoinFormation_LeaderNotFound_SetsFailure()`, `JoinFormation_Execute_SuccessOnFormationTag()`

**Estimated Effort:** 0.75 days

**Dependencies:** S4.3

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
using Hrot.SimHost.Systems;
using Hrot.SimHost.Configuration;
using Hrot.Map.Definitions.Tkb;
using Hrot.NED;
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

namespace Hrot.SimHost
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("===========================================");
            Console.WriteLine("  Hrot SimHost Mock (BDC SST)");
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
            world.AddSystem(new CreateEntityRequestHandler(
                eventBus, config.InstanceId, idAllocator, geoTransform));
            world.AddSystem(new NetworkSpawningSystem(
                tkbDatabase, elm, networkEntityMap, idAllocator, eventBus, config.InstanceId,
                // DisTypeExtractor delegate: decouples Toolkit from Hrot.NED
                (object c, out ulong dis) => {
                    if (c is Hrot.NED.Descriptors.EntityMaster m) { dis = m.DisType; return true; }
                    dis = 0; return false;
                }));
            // SimulationLogicModule: registers DoctrineRegistry + Behavior toolkit + CarKinem + LinearKinematicsSystem (see Task S4.1)
            world.AddModule(new SimulationLogicModule(doctrineRegistry, networkEntityMap));
            // GeographicModule: registers SimTransformBridgeSystem post-physics (see Task S3.1)
            world.AddModule(new GeographicModule(geoTransform));
            Console.WriteLine("  - SimHost modules registered");
            
            // 7. Register CycloneNetworkModule (owns ALL ingress, egress, and gateway systems)
            //
            // âš ď¸Ź  Do NOT call world.AddSystem(new SmartEgressSystem()) or
            //    world.AddSystem(new CycloneEgressSystem(...)). Those are internal to
            //    CycloneNetworkModule and adding them separately causes double execution.
            //
            // Only supply the Translators list â€” the Module registers its own internal
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
            translators.Add(new WorldPosTranslator(participant, networkEntityMap));
            
            // Auto-translators for types tagged [FdpDescriptor] in Hrot.NED
            var (autoTranslators, _) = ReplicationBootstrap.CreateAutoTranslators(
                participant, typeof(Program).Assembly, networkEntityMap);
            translators.AddRange(autoTranslators);
            
            var elm = world.GetModule<EntityLifecycleModule>(); // retrieved after AddModule above
            var networkModule = new CycloneNetworkModule(
                participant, nodeMapper, idAllocator, topology, elm,
                serialisation, translators, networkEntityMap
            );
            kernel.RegisterModule(networkModule); // â† this registers all network systems internally
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
- âś… Main() entry point compiles
- âś… `DoctrineRegistry` compiled and set as kernel singleton before `kernel.Initialize()` (follow `NetworkDemoApp.cs` / `HeadlessDemoApp.cs` pattern); stable `int` constants defined in `DoctrineIds.cs` per DEBT-006 rules; BTree/HSM doctrine definitions registered for all four `BehaviorId` strings: `"MoveToLocation"`, `"FollowRoute"`, `"JoinFormation"`, `"Idle"`
- âś… `EntityMissionTranslator` and `EntityMissionEgressTranslator` included in the translator list passed to `CycloneNetworkModule`
- âś… `GeographicModule` registered at kernel level (not as an app module) with the configured `WGS84Transform`
- âś… `CycloneNetworkModule` constructed with full translator list and registered via `kernel.RegisterModule`
- âś… No standalone `SmartEgressSystem` or `CycloneEgressSystem` added anywhere outside the module
- âś… All application modules initialized
- âś… Main loop runs at target frame rate
- âś… Graceful startup/shutdown

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
using Hrot.NED;

namespace Hrot.SimHost.Configuration
{
    public class SimHostConfig
    {
        public int DomainId { get; set; } = 0;
        public int SimulationRateHz { get; set; } = 60;
        public GeoPoint GeodeticOrigin { get; set; } = new()
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
- âś… Configuration class defined
- âś… JSON loading/saving works
- âś… Default config generated if missing
- âś… config.json file created

**Estimated Effort:** 0.5 days

**Dependencies:** S5.1

---

### Task S5.3: Add Logging and Diagnostics

**Goal:** Comprehensive logging for debugging.

**Implementation:**

Create `Utilities/Logger.cs`:

```csharp
using System;

namespace Hrot.SimHost.Utilities
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
- âś… Logger class implemented
- âś… Log levels working
- âś… Timestamps included
- âś… All systems use Logger

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
- âś… Ctrl+C handler registered
- âś… Graceful shutdown message
- âś… Resources cleaned up
- âś… ID allocator stopped

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

Create `Hrot.SimHost.Integration.Tests/EntityCreationFlowTests.cs`:

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
- âś… ACK received within timeout
- âś… NewEntityId valid
- âś… EntityMaster published
- âś… TkbType correct

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
5. Verify WorldPos updates published
6. Verify task state transitions

**Implementation:**

Create `Hrot.SimHost.Integration.Tests/MissionExecutionFlowTests.cs`:

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
    var finalWorldPos = simHost.ReadWorldPos(createAck.NewEntityId);
    Assert.IsNotNull(finalWorldPos);
    
    // Verify vehicle moved (position should have changed)
    // Convert GeoPoint back to Vector2
    var finalPos = simHost.GeoToCartesian(finalWorldPos.Pos);
    float distance = Vector2.Distance(Vector2.Zero, finalPos);
    
    Assert.IsTrue(distance > 50, "Vehicle should have moved significantly");
    
    // Cleanup
    await simHost.StopAsync();
}
```

**Acceptance Criteria:**
- âś… Vehicle position changes
- âś… WorldPos updates published
- âś… Task state becomes DONE
- âś… Vehicle navigates toward target

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

Create `Hrot.SimHost.Integration.Tests/PerformanceTests.cs`:

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
- âś… 100 entities created
- âś… Average FPS â‰Ą 58
- âś… Min FPS â‰Ą 55
- âś… No crashes or errors

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
cd Hrot.SimHost
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
5. SimHost publishes EntityMaster, WorldPos

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
- âś… User guide created
- âś… All sections complete
- âś… Examples included

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

**Type:** `GeoPoint`

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
- âś… Configuration reference created
- âś… All options documented
- âś… Examples provided

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
- `CreateEntityRequestSystem` - Request handler system
- `MissionAdapterSystem` - Behavior toolkit adapter (maps BehaviorId → DoctrineId)
- `JoinFormationExecutor` - `IActionExecutor<LocomotionChannel>` for formation joining
- `EntityMissionTranslator` / `EntityMissionEgressTranslator` - DDS ↔ ECS mission sync
- All component structs
- All public methods

**Acceptance Criteria:**
- âś… All public APIs documented
- âś… XML file generated
- âś… No documentation warnings

**Estimated Effort:** 0.25 days

**Dependencies:** S7.2

---

## Summary Tables

### Effort by Phase

| Phase | Focus | Days | Dependencies |
|-------|-------|------|--------------|
| S1 | Project Setup | 1 | None |
| S2 | CreateEntityRequestHandler | 3 | S1 |
| S3 | Geographic Module Integration | 2 | S1 |
| S4 | Behavior Toolkit Integration | 5 | S1 |
| S5 | Main Application Shell | 3 | S1, S2, S3, S4 |
| S6 | Integration Testing | 3 | S5 |
| S7 | Documentation | 1 | S6 |
| **TOTAL** | | **18** | |

### Critical Path

```
S1 (1d) â†’ S2 (3d) â†’ S5 (3d) â†’ S6 (3d) â†’ S7 (1d) = 11 days minimum
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

- **[â¬† Back to DESIGN-SIMHOST.md](./DESIGN-SIMHOST.md)**
- **[âžś Task Tracker](./TASK-TRACKER.md)**
- **[â¬… Shared Tasks](./TASK-DETAILS-SHARED.md)**
