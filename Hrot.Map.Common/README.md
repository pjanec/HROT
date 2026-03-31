# Hrot.Map.Common

Shared constants, utilities, and command gateway for NED SST.

## Components

### NedCommandGateway

Convenience facade for NED SST commands (CreateEntity, UpdateDescriptor, MissionControl).

### TkbEntityTypes

Centralized TKB entity type ID constants.

### MapConfig

Map and context configuration constants.

## Usage

### Command Gateway

```csharp
using Hrot.Map.Common.Commands;
using Hrot.NED.Messages; // For CreateEntityRequest
using CycloneDDS.Core;
using CycloneDDS.Runtime;

var gateway = new NedCommandGateway(participant);

var request = new CreateEntityRequest
{
    RequestId = Guid.NewGuid(),
    Owner = new NodeId { AppDomainId = 1, AppInstanceId = 1 }
};

var ack = await gateway.CreateEntityAsync(request);
```

### Constants

```csharp
long tankTkbId = TkbEntityTypes.Tank_M1Abrams;
int mapGroupId = MapConfig.DefaultMapGroupId;
string contextKey = ContextKeys.PlaceTank;
```

## See Also

- [DESIGN-SHARED.md](../docs/design/DESIGN-SHARED.md)
- [FDP.Toolkit.Commands](../FDP/Toolkits/FDP.Toolkit.Commands/README.md)
