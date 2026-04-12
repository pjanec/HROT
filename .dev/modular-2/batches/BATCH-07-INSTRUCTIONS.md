# BATCH-07: Create Hrot.Network.BDC

**Tasks covered:** TASK-P3-003

**Prerequisites:** Batches 01-06 committed (Hrot.Network.NED, INetworkFactory contracts all in place).

---

## Overview

Create a minimal BDC (Battlefield Data Channel) network protocol adapter.
BDC is a second simulation protocol that must satisfy the same `INetworkFactory`
contract as `NedNetworkFactory`. This demonstrates protocol-swapability: plugging in
`BdcNetworkFactory` instead of `NedNetworkFactory` requires zero changes to any
subsystem (`Hrot.SimHost`, `Hrot.ExCon`, `Hrot.IG`, `Hrot.CGF`).

Scope is intentionally minimal: entity state replication (EntityMaster + WorldPos)
and mission control commands only. No need for full NED feature parity.

---

## STEP 1 — Create Hrot.Network.BDC project

### File: `Hrot.Network.BDC/Hrot.Network.BDC.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.Network.BDC.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
  <ItemGroup>
    <!-- Domain model and neutral interfaces -->
    <ProjectReference Include="..\Hrot.Core\Hrot.Core.csproj" />
    <!-- Fdp engine: ECS, behavioral, geographic -->
    <ProjectReference Include="..\FDP\Kernel\Fdp.Core\Fdp.Core.csproj" />
    <ProjectReference Include="..\FDP\Toolkits\Fdp.Engine\Fdp.Engine.csproj" />
    <!-- DDS runtime -->
    <ProjectReference Include="..\FDP\ModuleHost\Fdp.Network.Cyclone\Fdp.Network.Cyclone.csproj" />
    <ProjectReference Include="..\FDP\ExtDeps\FastCycloneDds\src\CycloneDDS.Runtime\CycloneDDS.Runtime.csproj" />
    <ProjectReference Include="..\FDP\ExtDeps\FastCycloneDds\src\CycloneDDS.Schema\CycloneDDS.Schema.csproj" />
    <ProjectReference Include="..\FDP\ExtDeps\FastCycloneDds\src\CycloneDDS.Core\CycloneDDS.Core.csproj" />
  </ItemGroup>
  <Import Project="..\FDP\ExtDeps\FastCycloneDds\tools\CycloneDDS.CodeGen\CycloneDDS.targets" />
</Project>
```

---

## STEP 2 — BDC DDS schema types

### File: `Hrot.Network.BDC/BdcCommon.cs`

```csharp
using CycloneDDS.Schema;

namespace Hrot.BDC.Common
{
    // Unique identifier of a BDC participating node
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcNodeId
    {
        public int AppDomainId;
        public int AppInstanceId;
    }

    // Geographic position for BDC — latitude/longitude/altitude in degrees/meters
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcGeoPoint
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
    }

    // Orientation angles in degrees (heading, pitch, roll)
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcEulerOri
    {
        public float Heading;
        public float Pitch;
        public float Roll;
    }

    // Velocity/angular vector: azimuth (deg), elevation (deg), length (m/s)
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcAngularVector
    {
        public float Azimuth;
        public float Elevation;
        public float Length;
    }
}
```

### File: `Hrot.Network.BDC/BdcEntityMessages.cs`

BDC topic names MUST be prefixed with `BDC_` so they do not collide with NED topics
on the same DDS domain ID.

```csharp
using CycloneDDS.Schema;
using Hrot.BDC.Common;

namespace Hrot.BDC.Messages
{
    // Entity lifecycle topic for BDC.
    // When this topic instance is alive the entity exists.
    // When it is disposed the entity is deleted.
    // Topic name BDC_EntityMaster is distinct from NED's EntityMaster.
    [DdsTopic("BDC_EntityMaster")]
    [DdsIdlFile("bdc-entity-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct BdcEntityMaster
    {
        // Entity network ID; 0=invalid
        [DdsKey]
        public int EntityId;

        // TKB type index; 0=invalid
        public long TkbType;

        // SISO DIS entity kind (1=Platform, 2=Munition, etc.)
        public byte Diskind;
    }

    // Merged BDC spatial topic: position, orientation, and velocity.
    // Topic name BDC_WorldPos is distinct from NED's WorldPos.
    [DdsTopic("BDC_WorldPos")]
    [DdsIdlFile("bdc-entity-msgs")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct BdcWorldPos
    {
        [DdsKey]
        public int EntityId;

        public DateTime Time;
        public BdcGeoPoint Pos;
        public BdcEulerOri Ori;
        public BdcAngularVector Vel;
    }
}
```

### File: `Hrot.Network.BDC/BdcMissionMessages.cs`

```csharp
using CycloneDDS.Schema;
using System;

namespace Hrot.BDC.Messages
{
    // BDC mission command types
    public enum BdcMissionCommandType : int
    {
        ReplaceMission = 0,
        AbortAll       = 1,
        JumpToTask     = 2,
    }

    // BDC mission control request sent from ExCon/Editor to CGF.
    // Topic name BDC_MissionControlRequest is distinct from NED's MissionControlRequest.
    [DdsTopic("BDC_MissionControlRequest")]
    [DdsIdlFile("bdc-mission-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile,
            HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct BdcMissionControlRequest
    {
        public Guid RequestId;
        public long TargetEntityId;
        public BdcMissionCommandType CommandType;
        // JSON payload carrying command parameters; empty string for parameterless commands
        public string PayloadJson;
    }

    // BDC acknowledgment sent by CGF/SimHost for a BdcMissionControlRequest.
    [DdsTopic("BDC_MissionControlAck")]
    [DdsIdlFile("bdc-mission-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile,
            HistoryKind = DdsHistoryKind.KeepAll)]
    [DdsManaged]
    public partial struct BdcMissionControlAck
    {
        public Guid RequestId;
        public int ErrorCode;
        public string? ErrorMessage;
    }
}
```

---

## STEP 3 — BDC Translators

### File: `Hrot.Network.BDC/Replication/BdcEntityMasterTranslator.cs`

This translator handles the BDC_EntityMaster topic: announces entity birth (egress)
and creates ghosts for remote entities (ingress).

```csharp
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Hrot.BDC.Messages;
using ModuleHost.Core.Abstractions;

namespace Hrot.BDC.Replication
{
    /// <summary>
    /// BDC entity lifecycle translator.
    /// Egress: writes BDC_EntityMaster for locally-owned entities.
    /// Ingress: creates ghost entities from incoming BDC_EntityMaster samples.
    /// </summary>
    internal sealed class BdcEntityMasterTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<BdcEntityMaster>? _writer;
        private readonly DdsReader<BdcEntityMaster>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;
        private readonly FdpEventBus _eventBus;
        private readonly GhostCreationSystem _ghostCreation;
        private readonly HashSet<long> _publishedNetIds = new();

        public string TopicName => "BDC_EntityMaster";
        public long DescriptorOrdinal => 1000; // BDC ordinal space starts at 1000 to avoid collisions with NED

        private static readonly IReadOnlyList<int> _targetIds =
            new int[] { GlobalComponentIds.NetworkIdentity, GlobalComponentIds.TkbIdentity };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public BdcEntityMasterTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            long localNodeId,
            FdpEventBus eventBus,
            GhostCreationSystem ghostCreation)
        {
            _entityMap      = entityMap;
            _localNodeId    = localNodeId;
            _eventBus       = eventBus;
            _ghostCreation  = ghostCreation;
            _writer         = new DdsWriter<BdcEntityMaster>(participant, "BDC_EntityMaster");
            _reader         = new DdsReader<BdcEntityMaster>(participant, "BDC_EntityMaster");
        }

        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<NetworkIdentity>()
                .With<TkbIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            long packedKey = ModuleHost.Core.Network.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                if (_publishedNetIds.Contains(netId.Value))
                    continue;

                ref readonly var tkb = ref view.GetComponentRO<TkbIdentity>(entity);

                _writer!.Write(new BdcEntityMaster
                {
                    EntityId = (int)netId.Value,
                    TkbType  = tkb.TkbType,
                    Diskind  = 1, // Platform
                });

                _publishedNetIds.Add(netId.Value);
                FdpLog<BdcEntityMasterTranslator>.Debug(
                    "[BDC Node-{0}] Egress: BDC_EntityMaster EntityId={1}", _localNodeId, netId.Value);
            }
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader == null) return;

            foreach (var sample in _reader.TakeSamples())
            {
                var msg = (BdcEntityMaster)sample.Sample;

                if (sample.InstanceState == CycloneDDS.Runtime.DdsInstanceState.Alive)
                {
                    if (_entityMap.TryGetEntityByNetId(msg.EntityId, out _)) continue;
                    if (msg.EntityId == 0) continue;

                    // Create ghost entity for inbound remote entity
                    _ghostCreation.CreateGhost(cmd, msg.EntityId, msg.TkbType, _localNodeId);
                    FdpLog<BdcEntityMasterTranslator>.Debug(
                        "[BDC Node-{0}] Ingress: ghost for EntityId={1}", _localNodeId, msg.EntityId);
                }
                else
                {
                    // Entity was disposed: fire destroy command
                    if (_entityMap.TryGetEntityByNetId(msg.EntityId, out var entity))
                        _eventBus.Publish(new DestroyEntityCommand { Entity = entity });
                }
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId)
        {
            if (_writer != null && _publishedNetIds.Contains(networkEntityId))
            {
                _writer.DisposeInstance(new BdcEntityMaster { EntityId = (int)networkEntityId });
                _publishedNetIds.Remove(networkEntityId);
            }
        }
    }
}
```

**IMPORTANT NOTE**: `DdsReader<T>` in FastCycloneDds has a `TakeSamples()` method that
returns samples. Look at how the NED code reads DDS samples and adapt accordingly. If
`DdsReader<BdcEntityMaster>` does not exist or has a slightly different API than what I
wrote above, look at NED ingress translators (e.g.,
`Hrot.Network.NED/Replication/Map/Ingress/EntityMasterIngressTranslator.cs`) for the
correct pattern and replicate it for BDC.

### File: `Hrot.Network.BDC/Replication/BdcWorldPosTranslator.cs`

```csharp
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Hrot.BDC.Messages;
using ModuleHost.Core.Abstractions;

namespace Hrot.BDC.Replication
{
    /// <summary>
    /// BDC world position translator.
    /// Egress: writes BDC_WorldPos for locally-owned entities.
    /// Ingress: updates SimTransform on ghost entities from incoming BDC_WorldPos samples.
    /// </summary>
    internal sealed class BdcWorldPosTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<BdcWorldPos>? _writer;
        private readonly DdsReader<BdcWorldPos>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform _geoTransform;
        private readonly GhostCreationSystem _ghostCreation;
        private readonly long _localNodeId;

        public string TopicName => "BDC_WorldPos";
        public long DescriptorOrdinal => 1002; // BDC WorldPos ordinal

        // Targets: SimTransform (component ID 2, matching Hrot.NED.Descriptors.EDescriptorType.GeoSpatial mapping)
        private static readonly IReadOnlyList<int> _targetIds =
            new int[] { GlobalComponentIds.SimTransform };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public BdcWorldPosTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            IGeographicTransform geoTransform,
            GhostCreationSystem ghostCreation,
            long localNodeId)
        {
            _entityMap     = entityMap;
            _geoTransform  = geoTransform;
            _ghostCreation = ghostCreation;
            _localNodeId   = localNodeId;
            _writer        = new DdsWriter<BdcWorldPos>(participant, "BDC_WorldPos");
            _reader        = new DdsReader<BdcWorldPos>(participant, "BDC_WorldPos");
        }

        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<NetworkIdentity>()
                .With<SimTransform>()
                .WithLifecycle(EntityLifecycle.Active)
                .Build();

            long packedKey = ModuleHost.Core.Network.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                ref readonly var xf    = ref view.GetComponentRO<SimTransform>(entity);

                var geoPos = _geoTransform.CartesianToGeodetic(xf.Position);

                _writer!.Write(new BdcWorldPos
                {
                    EntityId = (int)netId.Value,
                    Time     = DateTime.UtcNow,
                    Pos      = new BdcGeoPoint
                    {
                        Latitude  = geoPos.Latitude,
                        Longitude = geoPos.Longitude,
                        Altitude  = geoPos.Altitude,
                    },
                    Ori = new BdcEulerOri
                    {
                        Heading = xf.EulerAngles.x,
                        Pitch   = xf.EulerAngles.y,
                        Roll    = xf.EulerAngles.z,
                    },
                    Vel = new BdcAngularVector
                    {
                        Azimuth   = 0,
                        Elevation = 0,
                        Length    = 0,
                    },
                });
            }
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader == null) return;

            foreach (var sample in _reader.TakeSamples())
            {
                if (sample.InstanceState != CycloneDDS.Runtime.DdsInstanceState.Alive)
                    continue;

                var msg = (BdcWorldPos)sample.Sample;
                if (!_entityMap.TryGetEntityByNetId(msg.EntityId, out var entity))
                    continue;

                var cartesian = _geoTransform.GeodeticToCartesian(
                    msg.Pos.Latitude, msg.Pos.Longitude, msg.Pos.Altitude);

                var xf = new SimTransform
                {
                    Position    = cartesian,
                    EulerAngles = new System.Numerics.Vector3(
                        msg.Ori.Heading, msg.Ori.Pitch, msg.Ori.Roll),
                };

                cmd.SetComponent(entity, xf);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not BdcWorldPos msg) return;

            var cartesian = _geoTransform.GeodeticToCartesian(
                msg.Pos.Latitude, msg.Pos.Longitude, msg.Pos.Altitude);

            var xf = new SimTransform
            {
                Position    = cartesian,
                EulerAngles = new System.Numerics.Vector3(
                    msg.Ori.Heading, msg.Ori.Pitch, msg.Ori.Roll),
            };
            repo.SetComponent(entity, xf);
        }

        public void Dispose(long networkEntityId)
        {
            _writer?.DisposeInstance(new BdcWorldPos { EntityId = (int)networkEntityId });
        }
    }
}
```

**IMPORTANT**: Adapt the `ScanAndPublish` and `PollIngress` implementations based on
what APIs `SimTransform` and `IGeographicTransform` actually expose. Study the NED
equivalents `GeoSpatialEgressTranslator.cs` and `GeoSpatialIngressTranslator.cs` in
`Hrot.Network.NED/Replication/Map/` and replicate the same pattern.

Similarly, if `DdsReader<T>` does not have a simple `TakeSamples()` returning a sequence
of objects with `.Sample` and `.InstanceState`, look at how NED ingress translators read
from DDS and adapt accordingly.

---

## STEP 4 — BdcReplicationModule

### File: `Hrot.Network.BDC/Replication/BdcReplicationModule.cs`

```csharp
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Hrot.Common;
using Hrot.Common.Abstractions;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Scheduling;
using ModuleHost.Network.Cyclone.Systems;

namespace Hrot.BDC.Replication
{
    /// <summary>
    /// BDC replication module implementing the protocol-neutral <see cref="IReplicationModule"/>.
    /// Provides entity state synchronisation using BDC DDS topics (BDC_EntityMaster, BDC_WorldPos).
    /// </summary>
    public sealed class BdcReplicationModule : IReplicationModule
    {
        public string Name => "BdcReplication";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly DdsParticipant? _participant;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform _geoTransform;
        private readonly FdpEventBus _eventBus;
        private readonly long _localNodeId;
        private readonly bool _driveFromNetwork;
        private readonly NodeRole _role;

        private BdcEntityMasterTranslator? _masterTranslator;
        private BdcWorldPosTranslator? _worldPosTranslator;

        /// <inheritdoc/>
        public GhostCreationSystem GhostCreationSystem { get; }

        /// <inheritdoc/>
        public bool DriveFromNetwork => _driveFromNetwork;

        public BdcReplicationModule(
            DdsParticipant? participant,
            NodeRole role,
            NetworkEntityMap entityMap,
            IGeographicTransform geoTransform,
            FdpEventBus eventBus,
            long localNodeId)
        {
            _participant     = participant;
            _role            = role;
            _entityMap       = entityMap      ?? throw new ArgumentNullException(nameof(entityMap));
            _geoTransform    = geoTransform   ?? throw new ArgumentNullException(nameof(geoTransform));
            _eventBus        = eventBus       ?? throw new ArgumentNullException(nameof(eventBus));
            _localNodeId     = localNodeId;

            bool roleHasMuscle = role.HasFlag(NodeRole.MuscleGround);
            bool roleHasBrain  = role.HasFlag(NodeRole.Brain);
            _driveFromNetwork  = !roleHasMuscle && !roleHasBrain;

            GhostCreationSystem = new GhostCreationSystem(entityMap);
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(GhostCreationSystem);

            if (_participant != null)
            {
                _masterTranslator = new BdcEntityMasterTranslator(
                    _participant, _entityMap, _localNodeId, _eventBus, GhostCreationSystem);
                _worldPosTranslator = new BdcWorldPosTranslator(
                    _participant, _entityMap, _geoTransform, GhostCreationSystem, _localNodeId);

                var translators = new IDescriptorTranslator[]
                {
                    _masterTranslator,
                    _worldPosTranslator,
                };

                registry.RegisterSystem(new CycloneNetworkIngressSystem(translators));
                registry.RegisterSystem(new CycloneEgressSystem(translators));
                registry.RegisterSystem(new CycloneNetworkCleanupSystem(translators));
            }

            registry.RegisterSystem(new SmartEgressSystem());
            registry.RegisterSystem(new DeadReckoningSyncSystem(_driveFromNetwork));
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
```

---

## STEP 5 — BdcNetworkFactory

### File: `Hrot.Network.BDC/Factory/BdcNetworkFactory.cs`

```csharp
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Replication.Services;
using Hrot.Common;
using Hrot.Core.Network;

namespace Hrot.BDC.Factory
{
    /// <summary>
    /// Implements <see cref="INetworkFactory"/> using BDC (Battlefield Data Channel)
    /// DDS protocols for simulation data exchange.
    /// </summary>
    public sealed class BdcNetworkFactory : INetworkFactory
    {
        private readonly DdsParticipant?      _participant;
        private readonly NetworkEntityMap     _entityMap;
        private readonly IGeographicTransform _geoTransform;
        private readonly FdpEventBus          _eventBus;
        private readonly long                 _localNodeId;
        private readonly NodeRole             _role;

        public BdcNetworkFactory(
            DdsParticipant?      participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform,
            FdpEventBus          eventBus,
            long                 localNodeId,
            NodeRole             role)
        {
            _participant  = participant;
            _entityMap    = entityMap;
            _geoTransform = geoTransform;
            _eventBus     = eventBus;
            _localNodeId  = localNodeId;
            _role         = role;
        }

        /// <inheritdoc/>
        public IReplicationModule CreateReplicationModule()
            => new Hrot.BDC.Replication.BdcReplicationModule(
                _participant, _role, _entityMap, _geoTransform, _eventBus, _localNodeId);

        /// <inheritdoc/>
        public ICommandGateway CreateCommandGateway()
            => new BdcNullCommandGateway();

        /// <inheritdoc/>
        public IExConEgressWriters CreateExConEgressWriters()
            => new BdcNullExConEgressWriters();
    }

    internal sealed class BdcNullCommandGateway : ICommandGateway
    {
        public Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default)
            => Task.CompletedTask;
        public void Dispose() { }
    }

    internal sealed class BdcNullExConEgressWriters : IExConEgressWriters
    {
        public void WriteMapConfig(MapConfigDto config) { }
        public void WriteDeleteEntity(int entityId) { }
        public void WriteCreateEntity(CreateEntityCommand cmd) { }
        public void WriteMapCommand(MapCommandDto cmd) { }
        public void Dispose() { }
    }
}
```

---

## STEP 6 — Add to solution

Add both new projects to `IOS-IG-SimHost.sln` using:
```
dotnet sln IOS-IG-SimHost.sln add Hrot.Network.BDC/Hrot.Network.BDC.csproj
dotnet sln IOS-IG-SimHost.sln add Hrot.Network.BDC.Tests/Hrot.Network.BDC.Tests.csproj
```

---

## STEP 7 — Create test project

### File: `Hrot.Network.BDC.Tests/Hrot.Network.BDC.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="NSubstitute" Version="5.1.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Hrot.Network.BDC\Hrot.Network.BDC.csproj" />
    <ProjectReference Include="..\Hrot.Core\Hrot.Core.csproj" />
    <ProjectReference Include="..\FDP\Kernel\Fdp.Core\Fdp.Core.csproj" />
    <ProjectReference Include="..\FDP\Toolkits\Fdp.Engine\Fdp.Engine.csproj" />
  </ItemGroup>
</Project>
```

### File: `Hrot.Network.BDC.Tests/BdcNetworkFactoryTests.cs`

```csharp
using Hrot.BDC.Factory;
using Hrot.Common;
using Hrot.Core.Network;
using FDP.Toolkit.Replication.Services;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Behavior;
using Fdp.Kernel;
using Xunit;
using NSubstitute;

namespace Hrot.Network.BDC.Tests
{
    public class BdcNetworkFactoryTests
    {
        private static BdcNetworkFactory CreateFactory()
        {
            var entityMap   = new NetworkEntityMap();
            var geoTransform = Substitute.For<IGeographicTransform>();
            var eventBus    = new FdpEventBus();
            return new BdcNetworkFactory(
                participant:  null,      // headless — no real DDS
                entityMap:    entityMap,
                geoTransform: geoTransform,
                eventBus:     eventBus,
                localNodeId:  1,
                role:         NodeRole.AllInOne);
        }

        [Fact]
        public void BdcNetworkFactory_CreatesReplicationModule_WhenParticipantIsNull()
        {
            var factory = CreateFactory();
            var module = factory.CreateReplicationModule();
            Assert.NotNull(module);
            Assert.IsAssignableFrom<IReplicationModule>(module);
        }

        [Fact]
        public void BdcNetworkFactory_ReplicationModuleName_IsBdcReplication()
        {
            var factory = CreateFactory();
            var module = factory.CreateReplicationModule();
            Assert.Equal("BdcReplication", module.Name);
        }

        [Fact]
        public void BdcNetworkFactory_GhostCreationSystem_IsNotNull()
        {
            var factory = CreateFactory();
            var module  = factory.CreateReplicationModule();
            Assert.NotNull(module.GhostCreationSystem);
        }

        [Fact]
        public void BdcNetworkFactory_CreateCommandGateway_ReturnsNonNull()
        {
            var factory  = CreateFactory();
            var gateway  = factory.CreateCommandGateway();
            Assert.NotNull(gateway);
            // No-op gateway must be disposable without throwing
            gateway.Dispose();
        }

        [Fact]
        public void BdcNetworkFactory_CreateExConEgressWriters_ReturnsNonNull()
        {
            var factory  = CreateFactory();
            var writers  = factory.CreateExConEgressWriters();
            Assert.NotNull(writers);
            writers.Dispose();
        }

        [Fact]
        public void BdcNetworkFactory_DriveFromNetwork_TrueForAllInOneRole()
        {
            // AllInOne has Muscle + Brain, so DriveFromNetwork = false
            var factory = CreateFactory(); // role = AllInOne
            var module  = factory.CreateReplicationModule();
            Assert.False(module.DriveFromNetwork);
        }

        [Fact]
        public void BdcNetworkFactory_DriveFromNetwork_TrueForIgOnlyRole()
        {
            var entityMap    = new NetworkEntityMap();
            var geoTransform = Substitute.For<IGeographicTransform>();
            var eventBus     = new FdpEventBus();
            var factory = new BdcNetworkFactory(
                null, entityMap, geoTransform, eventBus, 1, NodeRole.ImageGenerator);
            var module = factory.CreateReplicationModule();
            Assert.True(module.DriveFromNetwork);
        }

        [Fact]
        public void BdcNetworkFactory_SatisfiesINetworkFactoryContract()
        {
            // This test verifies the compile-time contract: assigning the concrete factory
            // to the interface works without referencing Hrot.Network.NED.
            INetworkFactory factory = CreateFactory();
            Assert.NotNull(factory.CreateReplicationModule());
        }
    }
}
```

---

## Implementation Notes

### API Discovery

Before writing the translator bodies, grep the NED codebase for the exact DDS reader/writer API
to avoid guessing:

```powershell
# How does NED write to DDS?
Select-String -Path "Hrot.Network.NED/**/*.cs" -Pattern "DdsWriter|_writer\." | Select-Object -First 20

# How does NED read from DDS?
Select-String -Path "Hrot.Network.NED/**/*.cs" -Pattern "DdsReader|TakeSamples|_reader\." | Select-Object -First 20

# Check SimTransform fields
Select-String -Path "FDP/**/*.cs" -Pattern "struct SimTransform" | Select-Object -First 5
```

Read the following NED files as concrete templates:
- `Hrot.Network.NED/Replication/Map/Egress/EntityMasterEgressTranslator.cs`
- `Hrot.Network.NED/Replication/Map/Ingress/EntityMasterIngressTranslator.cs`
- `Hrot.Network.NED/Replication/Map/Egress/GeoSpatialEgressTranslator.cs`
- `Hrot.Network.NED/Replication/Map/Ingress/GeoSpatialIngressTranslator.cs`

These are the 4 most important files. The BDC translator implementations directly mirror
the NED ones but reference `Hrot.BDC.Messages.*` types instead of `Hrot.NED.*` types, and
use `BDC_EntityMaster`/`BDC_WorldPos` topic names.

### Namespace convention

| NED             | BDC                |
|-----------------|--------------------|
| `Hrot.NED.*`    | `Hrot.BDC.*`       |
| `Hrot.Network.NED.*` | `Hrot.BDC.Factory`, `Hrot.BDC.Replication` |
| Topic `EntityMaster` | Topic `BDC_EntityMaster` |
| Topic `WorldPos`     | Topic `BDC_WorldPos`     |
| Ordinal 0 (EntityMaster) | Ordinal 1000 |
| Ordinal 2 (WorldPos)     | Ordinal 1002 |

### DDS Reader Note

If `DdsReader<T>` in the CycloneDDS.Runtime API has a different method signature than
what I wrote in `PollIngress`, look at `EntityMasterIngressTranslator.cs` in the NED
project for the correct pattern and replicate it for BDC. The exact API shape is
defined there.

### IGeographicTransform Note

If `IGeographicTransform.CartesianToGeodetic` / `GeodeticToCartesian` have different
signatures than what I wrote, look at `GeoSpatialEgressTranslator.cs` and
`GeoSpatialIngressTranslator.cs` in NED for the correct usage.

### SimTransform Note

If `SimTransform.EulerAngles` is not a `Vector3` or `.Position` is not a float/Vector3,
look at the NED GeoSpatial translators to see how they set/read position and orientation
on `SimTransform`, and replicate that pattern.

---

## Build and Test

```powershell
# Build the full solution
dotnet build IOS-IG-SimHost.sln -v q

# Run BDC tests
dotnet test Hrot.Network.BDC.Tests/Hrot.Network.BDC.Tests.csproj -v q

# Run all tests to ensure nothing is broken
dotnet test IOS-IG-SimHost.sln -v q --filter "FullyQualifiedName!~Integration"
```

---

## Success Criteria

- [ ] `Hrot.Network.BDC` and `Hrot.Network.BDC.Tests` added to `IOS-IG-SimHost.sln`
- [ ] Solution builds with zero errors (`0 Error(s)`)
- [ ] All BDC tests pass: `Hrot.Network.BDC.Tests`
- [ ] All pre-existing tests still pass
- [ ] `BdcNetworkFactory` implements `INetworkFactory`
- [ ] `BdcReplicationModule` implements `IReplicationModule`
- [ ] BDC topic names all use `BDC_` prefix (not colliding with NED topics)
- [ ] No file in `Hrot.Network.BDC` references `Hrot.NED`, `Hrot.Network.NED`, or any NED-specific type
- [ ] No file in `Hrot.Network.BDC` references `Hrot.Common` (use only `Hrot.Core`)

---

## Report

After completing the above, write a report to:
`.dev/modular-2/reports/BATCH-07-REPORT.md`

Include:
1. Files created
2. Any deviations from the instructions above (with reasons)
3. Build result (error count)
4. Test results (pass/fail counts for BDC and all other test projects)
5. Any API adaptations made due to actual vs. assumed method signatures
