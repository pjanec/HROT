# Bagira.Map.Common

Shared constants, utilities, and command gateway for BDC SST.

## Components

### BdcCommandGateway

Convenience facade for BDC SST commands (CreateEntity, UpdateDescriptor, MissionControl).

### TkbEntityTypes

Centralized TKB entity type ID constants.

### MapConfig

Map and context configuration constants.

## Usage

### Command Gateway

```csharp
using Bagira.Map.Common.Commands;
using Bagira.BDC.SSTM; // For CreateEntityRequest
using CycloneDDS.Core;
using CycloneDDS.Runtime;

var gateway = new BdcCommandGateway(participant);

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
