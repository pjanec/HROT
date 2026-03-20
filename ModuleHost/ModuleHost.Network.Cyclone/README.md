# ModuleHost.Network.Cyclone

## Overview

`ModuleHost.Network.Cyclone` is a **CycloneDDS-based networking plugin** for ModuleHost.Core. It provides distributed entity synchronization, network identity management, and DDS-based communication.

This is a **Plugin Layer** component - it extends the generic kernel with network-specific functionality.

## Features

- **Network Components**: `NetworkPosition`, `NetworkIdentity`, `NetworkOwnership`, `NetworkSpawnRequest`
- **Entity Lifecycle**: `NetworkGatewayModule` manages entity creation/destruction across nodes
- **DDS Integration**: CycloneDDS topics for entity state and master descriptors
- **ID Allocation**: `DdsIdAllocator` and `DdsIdAllocatorServer` for distributed entity IDs
- **Translators**: `EntityMasterTranslator`, `EntityStateTranslator` for DDS ↔ ECS bridges
- **Network Topology**: `StaticNetworkTopology` for peer discovery

## Architecture

### Components

- **NetworkPosition**: Position synchronized over the network
- **NetworkIdentity**: Unique network entity ID
- **NetworkOwnership**: Tracks which node is authoritative for an entity
- **NetworkSpawnRequest**: Tag component for requesting network registration

### Modules

- **NetworkGatewayModule**: Participates in Entity Lifecycle Management (ELM)
  - Registers entities with network
  - Waits for peer acknowledgments
  - Handles network-aware construction/destruction

### Services

- **DdsIdAllocator**: Local ID allocation service
- **DdsIdAllocatorServer**: Centralized ID server (optional)
- **NetworkEntityMap**: Tracks entity ↔ network ID mappings
- **TypeIdMapper**: Maps component types to DDS topic IDs

### Translators

Translators bridge ECS components and DDS topics:
- **EntityMasterTranslator**: Publishes entity creation/destruction
- **EntityStateTranslator**: Publishes entity state updates

## Usage

### Basic Setup

```csharp
using ModuleHost.Core;
using ModuleHost.Network.Cyclone;
using ModuleHost.Network.Cyclone.Modules;

// Setup Core
var world = new EntityRepository();
var eventAccumulator = new EventAccumulator();
var kernel = new ModuleHostKernel(world, eventAccumulator);

// Setup Network
const int NetworkModuleId = 100;
const int LocalNodeId = 1;

var topology = new StaticNetworkTopology(LocalNodeId, new[] { 1, 2, 3 });
var elm = new EntityLifecycleModule(new[] { NetworkModuleId });
var gateway = new NetworkGatewayModule(NetworkModuleId, LocalNodeId, topology, elm);

kernel.RegisterModule(elm);
kernel.RegisterModule(gateway);
```

### Application-Level Integration

Applications must bridge local and network components. Example:

```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]
public class NetworkSyncSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var query = view.Query()
            .With<NetworkPosition>()
            .With<Position>()
            .With<NetworkOwnership>()
            .Build();

        foreach (var entity in query)
        {
            var ownership = view.GetComponentRO<NetworkOwnership>(entity);
            
            if (ownership.PrimaryOwnerId == ownership.LocalNodeId)
            {
                // EGRESS: Local -> Network
                var localPos = view.GetComponentRO<Position>(entity);
                cmd.SetComponent(entity, new NetworkPosition { Value = localPos.Value });
            }
            else
            {
                // INGRESS: Network -> Local
                var netPos = view.GetComponentRO<NetworkPosition>(entity);
                cmd.SetComponent(entity, new Position { Value = netPos.Value });
            }
        }
    }
}
```

## Dependencies

- **ModuleHost.Core** - Generic ECS kernel
- **CycloneDDS.Runtime** - DDS implementation
- **Fdp.Kernel** - FDP ECS types

## Design Notes

- This plugin does NOT define application components like `Position` or `Velocity`
- Applications must explicitly wire local and network components
- Network components use `Vector3` from `System.Numerics`
- Ownership model: One node is authoritative for each entity
- Uses CycloneDDS

## See Also

- [ModuleHost.Core](../ModuleHost/ModuleHost.Core/README.md) - Generic kernel
- [ARCHITECTURE-NOTES.md](../docs/ARCHITECTURE-NOTES.md) - Overall architecture
- [Fdp.Examples.BattleRoyale](../Fdp.Examples.BattleRoyale/) - Example integration
