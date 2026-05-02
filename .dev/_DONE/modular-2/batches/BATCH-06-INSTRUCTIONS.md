# BATCH-06: Create Hrot.Network.NED (Merge Hrot.NED + Hrot.Network + NED translators)

**Batch Number:** BATCH-06
**Tasks:** TASK-P3-002 (Hrot.Network.NED)
**Phase:** Phase 3 — NED Network Adapter
**Dependencies:** BATCH-05 (INetworkFactory exists in Hrot.Core)

---

## Overview

This batch creates `Hrot.Network.NED` by merging:
1. `Hrot.NED` — DDS schema types (except orchestration types)
2. `Hrot.Network` — NedReplicationModule, translators, routing
3. `Hrot.Map.Common` stub NED translators (Replication/, Commands/, Translators/, NED-referencing Systems/)
4. New: `NedNetworkFactory : INetworkFactory`

It also:
- Moves `Hrot.NED/Orchestration/OrchestrationMessages.cs` into `Hrot.Network.Orchestration`
  so that `Hrot.Network.Orchestration` no longer depends on `Hrot.NED`.
- Empties `Hrot.Map.Common` (all files moved) and removes it from the solution.
- Creates `Hrot.Network.NED.Tests` from `Hrot.NED.Tests`.
- Updates all callers.

---

## TASK A: Relocate Orchestration Types

### A1 — Read current `Hrot.NED/Orchestration/OrchestrationMessages.cs`

Read the file. All the types (ClusterState, ClusterOpType, NodeOpType, SystemStateTopic,
NodeOpCommand, NodeOpStatus, NodeHeartbeat, NodeHeartbeatAck, ClusterOpRequest, etc.)
should be moved into `Hrot.Network.Orchestration/Orchestration/`.

**But check first:** Does `Hrot.Network.Orchestration/Orchestration/` already exist
and have an OrchestrationMessages.cs? If yes (from BATCH-04 copy), the file was removed
but the directory might exist. Just create the file fresh.

### A2 — Create `Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs`

Copy the EXACT content from `Hrot.NED/Orchestration/OrchestrationMessages.cs`.
Keep namespace `Hrot.NED.Descriptors.Orchestration` unchanged.

### A3 — Delete `Hrot.NED/Orchestration/OrchestrationMessages.cs`

Remove the file from Hrot.NED (use `Remove-Item`).

### A4 — Update `Hrot.Network.Orchestration/Hrot.Network.Orchestration.csproj`

Remove the `<ProjectReference Include="..\Hrot.NED\Hrot.NED.csproj" />` line.
The orchestration types are now directly in Hrot.Network.Orchestration, so the reference
to Hrot.NED is no longer needed.

NOTE: The `<Import Project="..\FDP\ExtDeps\FastCycloneDds\tools\CycloneDDS.CodeGen\CycloneDDS.targets" />`
is needed for the DDS code generation of the orchestration schema types. Make sure it remains.

Build and verify: `dotnet build Hrot.Network.Orchestration/Hrot.Network.Orchestration.csproj -v q`

---

## TASK B: Create Hrot.Network.NED Project Structure

### B1 — Create `Hrot.Network.NED/Hrot.Network.NED.csproj`

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
      <_Parameter1>Hrot.Network.NED.Tests</_Parameter1>
    </AssemblyAttribute>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.IG.Tests</_Parameter1>
    </AssemblyAttribute>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.SimHost.Tests</_Parameter1>
    </AssemblyAttribute>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.ClusterRunner.Integration.Tests</_Parameter1>
    </AssemblyAttribute>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.Map.Common.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
  <ItemGroup>
    <!-- Domain model and neutral interfaces -->
    <ProjectReference Include="..\Hrot.Core\Hrot.Core.csproj" />
    <!-- Hrot.Common stub: HrotNodeBuilder and related -->
    <ProjectReference Include="..\Hrot.Common\Hrot.Common.csproj" />
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

## TASK C: Move Source Files to Hrot.Network.NED

### C1 — Move Hrot.NED files (excluding Orchestration/ which was moved in Task A)

Move ALL .cs files from `Hrot.NED/` (except `Orchestration/`) to `Hrot.Network.NED/`:
- `AllDescriptors.cs` → `Hrot.Network.NED/AllDescriptors.cs`
- `Common.cs` → `Hrot.Network.NED/Common.cs`
- `EntityPropertyPatch.cs` → `Hrot.Network.NED/EntityPropertyPatch.cs`
- `FireInteractionMessages.cs` → `Hrot.Network.NED/FireInteractionMessages.cs`
- `GenericDescriptors.cs` → `Hrot.Network.NED/GenericDescriptors.cs`
- `GenericMessages.cs` → `Hrot.Network.NED/GenericMessages.cs`
- `GenericPrimitives.cs` → `Hrot.Network.NED/GenericPrimitives.cs`
- `MapDescriptors.cs` → `Hrot.Network.NED/MapDescriptors.cs`
- `MapMessages.cs` → `Hrot.Network.NED/MapMessages.cs`
- `Messages/DeferredTakeOwnership.cs` → `Hrot.Network.NED/Messages/DeferredTakeOwnership.cs`
- `MissionDescriptors.cs` → `Hrot.Network.NED/MissionDescriptors.cs`
- `MissionMessages.cs` → `Hrot.Network.NED/MissionMessages.cs`
- `Runner/SubsystemStatusAnnounce.cs` → `Hrot.Network.NED/Runner/SubsystemStatusAnnounce.cs`
- `SimDescriptors.cs` → `Hrot.Network.NED/SimDescriptors.cs`

All namespaces remain unchanged (`Hrot.NED.*`, `Hrot.DDS.DataModel.Runner`).

### C2 — Move Hrot.Network files

Move ALL .cs files from `Hrot.Network/` to `Hrot.Network.NED/`:
- `Infrastructure/HrotNodeBuilderReplicationExtensions.cs` → `Hrot.Network.NED/Infrastructure/`
- `Replication/NedReplicationModule.cs` → `Hrot.Network.NED/Replication/`
- `Routing/BrainMuscleOwnershipStrategy.cs` → `Hrot.Network.NED/Routing/`
- `Routing/IClusterStateCache.cs` → `Hrot.Network.NED/Routing/`
- `Routing/SimpleClusterStateCache.cs` → `Hrot.Network.NED/Routing/`
- `Systems/DeferredTakeoverSystem.cs` → `Hrot.Network.NED/Systems/`
- `Translators/CognitiveTranslatorPack.cs` → `Hrot.Network.NED/Translators/`
- `Translators/DeferredTakeOwnershipEgressTranslator.cs` → `Hrot.Network.NED/Translators/`
- `Translators/DeferredTakeOwnershipIngressTranslator.cs` → `Hrot.Network.NED/Translators/`

All namespaces remain unchanged (`Hrot.Network.Replication`, `Hrot.Network.Infrastructure`, etc.).

### C3 — Move NED-referencing Hrot.Map.Common files

Move the remaining files from `Hrot.Map.Common/` (the NED-referencing stubs) to `Hrot.Network.NED/`:
- `Commands/NedCommandGateway.cs` → `Hrot.Network.NED/Commands/NedCommandGateway.cs`
- `Helpers/MissionTriggerHelper.cs` → `Hrot.Network.NED/Helpers/MissionTriggerHelper.cs`
- All files in `Replication/` → `Hrot.Network.NED/Replication/Map/` (to avoid naming conflicts with the NedReplicationModule files)
  - `FireInteractionEventTranslator.cs` → `Hrot.Network.NED/Replication/Map/`
  - `NedAttributeRecordEmitter.cs` → `Hrot.Network.NED/Replication/Map/`
  - `OwnershipUpdateTranslator.cs` → `Hrot.Network.NED/Replication/Map/`
  - `Egress/` (all files) → `Hrot.Network.NED/Replication/Map/Egress/`
  - `Ingress/` (all files) → `Hrot.Network.NED/Replication/Map/Ingress/`
  - `Utils/DescriptorMapper.cs` → `Hrot.Network.NED/Replication/Map/Utils/`
- `Systems/IUpdateEntityAttributeAckSink.cs` → `Hrot.Network.NED/Systems/`
- `Systems/IUpdateEntityAttributeRequestSource.cs` → `Hrot.Network.NED/Systems/`
- `Systems/UpdateEntityAttributeRequestSystem.cs` → `Hrot.Network.NED/Systems/`
- `Translators/SharedTranslatorPack.cs` → `Hrot.Network.NED/Translators/Map/`
- `Translators/KinematicTranslatorPack.cs` → `Hrot.Network.NED/Translators/Map/`
- `Translators/EntityStatesIngressPack.cs` → `Hrot.Network.NED/Translators/Map/`

All namespaces remain unchanged (`Hrot.Map.Common.Replication`, `Hrot.Map.Common.Systems`, etc.).

### C4 — Update NedReplicationModule using directives

`NedReplicationModule.cs` currently references `Hrot.Map.Common.Translators` for the
translator packs. After those packs move to `Hrot.Network.NED`, the using directives
still work (same namespace) but you need to ensure the project references are correct
in `Hrot.Network.NED.csproj`.

Also: `NedReplicationModule` currently implements `INedReplicationModule`.
Update it to also implement `IReplicationModule`:
```csharp
public sealed class NedReplicationModule : INedReplicationModule, IReplicationModule
```
Actually, since `INedReplicationModule : IReplicationModule`, implementing `INedReplicationModule`
automatically satisfies `IReplicationModule`. No code change is needed — just verify.

### C5 — Create NedNetworkFactory

Create `Hrot.Network.NED/Factory/NedNetworkFactory.cs`:

```csharp
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Lifecycle;
using Hrot.Common;
using Hrot.Common.Abstractions;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Network.Replication;
using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

namespace Hrot.Network.NED.Factory;

/// <summary>
/// Implements <see cref="INetworkFactory"/> using NED (Network Exchange Description)
/// DDS protocols for simulation data exchange.
/// </summary>
public sealed class NedNetworkFactory : INetworkFactory
{
    private readonly DdsParticipant?      _participant;
    private readonly NetworkEntityMap     _entityMap;
    private readonly IGeographicTransform _geoTransform;
    private readonly FdpEventBus          _eventBus;
    private readonly int                  _localNodeId;
    private readonly NodeRole             _role;
    private readonly ITkbDatabase?        _tkbDb;
    private readonly EntityLifecycleModule? _lifecycleModule;
    private readonly BehaviorRegistry?    _behaviorRegistry;

    public NedNetworkFactory(
        DdsParticipant?       participant,
        NetworkEntityMap      entityMap,
        IGeographicTransform  geoTransform,
        FdpEventBus           eventBus,
        int                   localNodeId,
        NodeRole              role,
        ITkbDatabase?         tkbDb            = null,
        EntityLifecycleModule? lifecycleModule  = null,
        BehaviorRegistry?     behaviorRegistry = null)
    {
        _participant      = participant;
        _entityMap        = entityMap;
        _geoTransform     = geoTransform;
        _eventBus         = eventBus;
        _localNodeId      = localNodeId;
        _role             = role;
        _tkbDb            = tkbDb;
        _lifecycleModule  = lifecycleModule;
        _behaviorRegistry = behaviorRegistry;
    }

    /// <inheritdoc/>
    public IReplicationModule CreateReplicationModule()
        => new NedReplicationModule(
               participant:       _participant,
               role:              _role,
               entityMap:         _entityMap,
               geoTransform:      _geoTransform,
               eventBus:          _eventBus,
               localNodeId:       _localNodeId,
               domainId:          0,
               tkbDb:             _tkbDb,
               lifecycleModule:   _lifecycleModule,
               behaviorRegistry:  _behaviorRegistry);

    /// <inheritdoc/>
    public ICommandGateway CreateCommandGateway()
    {
        // NedCommandGateway moved from Hrot.Map.Common to Hrot.Network.NED
        // Return null implementation until full wiring is done in TASK-P4-001
        return new NullCommandGateway();
    }

    /// <inheritdoc/>
    public IExConEgressWriters CreateExConEgressWriters()
    {
        // Return null implementation until full wiring is done in TASK-P4-001
        return new NullExConEgressWriters();
    }
}

/// <summary>No-op stub for ICommandGateway until TASK-P4-001 wires the real implementation.</summary>
internal sealed class NullCommandGateway : ICommandGateway
{
    public Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default)
        => Task.FromResult(0);
    public Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default)
        => Task.CompletedTask;
    public void Dispose() { }
}

/// <summary>No-op stub for IExConEgressWriters until TASK-P4-001 wires the real implementation.</summary>
internal sealed class NullExConEgressWriters : IExConEgressWriters
{
    public void WriteMapConfig(MapConfigDto config) { }
    public void WriteDeleteEntity(int entityId) { }
    public void WriteCreateEntity(CreateEntityCommand cmd) { }
    public void WriteMapCommand(MapCommandDto cmd) { }
    public void Dispose() { }
}
```

---

## TASK D: Update Existing Projects

### D1 — Update Hrot.Map.Common.csproj

Hrot.Map.Common is now EMPTY (all files moved). Update to empty stub:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <!-- All types moved to Hrot.Core (non-NED) and Hrot.Network.NED (NED-specific) -->
    <ProjectReference Include="..\Hrot.Core\Hrot.Core.csproj" />
  </ItemGroup>
</Project>
```

### D2 — Update Hrot.Map.Common.Tests.csproj

Hrot.Map.Common.Tests still has 3 NED-referencing test files. Update to reference Hrot.Network.NED:
```xml
<ItemGroup>
  <ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />
  <ProjectReference Include="..\Hrot.Core\Hrot.Core.csproj" />
  <ProjectReference Include="..\FDP\Kernel\Fdp.Core\Fdp.Core.csproj" />
  <ProjectReference Include="..\Hrot.NED.Tests-temp-not-needed\..." /> <!-- skip -->
</ItemGroup>
```

Actually read the current Hrot.Map.Common.Tests.csproj first. It has 3 NED-using test files:
- DescriptorMapperAreaShapeTests.cs
- EntityMissionIngressTranslatorTests.cs
- MapRouteTranslatorTests.cs

Update csproj to reference Hrot.Network.NED instead of Hrot.Map.Common (which is now empty stub):
```xml
<ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />
<ProjectReference Include="..\Hrot.Core\Hrot.Core.csproj" />
<ProjectReference Include="..\FDP\Kernel\Fdp.Core\Fdp.Core.csproj" />
```
Remove: `<ProjectReference Include="..\Hrot.Map.Common\Hrot.Map.Common.csproj" />`
Remove: `<ProjectReference Include="..\Hrot.Map.Definitions\..." />` (now in Hrot.Core)

### D3 — Update all caller project files

For each project that currently references `Hrot.NED.csproj` OR `Hrot.Network.csproj`:
Replace those references with `Hrot.Network.NED.csproj`.

Projects needing update:
- `Hrot.CGF/Hrot.CGF.csproj` (references Hrot.NED + possibly Hrot.Network)
- `Hrot.ClusterRunner.Integration.Tests/...csproj` (references Hrot.NED)
- `Hrot.ClusterRunner.Tests/....csproj` (references Hrot.Network)
- `Hrot.ClusterRunner/Hrot.ClusterRunner.csproj` (references Hrot.NED + Hrot.Network)
- `Hrot.Common/Hrot.Common.csproj` (references Hrot.NED - for MissionControlCqrsEvents)
- `Hrot.ExCon/Hrot.ExCon.csproj` (references Hrot.NED)
- `Hrot.IG/Hrot.IG.csproj` (references Hrot.NED + Hrot.Network)
- `Hrot.Orchestrator/Hrot.Orchestrator.csproj` (references Hrot.NED)
- `Hrot.SimHost.Integration.Tests/...csproj` (references Hrot.NED)
- `Hrot.SimHost/Hrot.SimHost.csproj` (references Hrot.NED + Hrot.Network)

For EACH project, READ the csproj first, then find and replace the NED/Network references
with Hrot.Network.NED.

IMPORTANT: Some projects reference BOTH Hrot.NED AND Hrot.Network. They should end up
with a single `<ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />`.

Use the relative path from each project directory to `Hrot.Network.NED/Hrot.Network.NED.csproj`.

### D4 — Update Hrot.Common.csproj

Hrot.Common still has `MissionControlCqrsEvents.cs` and `MissionControlExecutionSystem.cs` which
reference NED types. Update Hrot.Common.csproj:
- Replace `<ProjectReference Include="..\Hrot.NED\Hrot.NED.csproj" />` with
  `<ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />`

---

## TASK E: Create Hrot.Network.NED.Tests

### E1 — Create `Hrot.Network.NED.Tests/Hrot.Network.NED.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />
    <ProjectReference Include="..\FDP\ExtDeps\FastCycloneDds\src\CycloneDDS.Runtime\CycloneDDS.Runtime.csproj" />
    <ProjectReference Include="..\FDP\ExtDeps\FastCycloneDds\src\CycloneDDS.Schema\CycloneDDS.Schema.csproj" />
    <ProjectReference Include="..\FDP\ExtDeps\FastCycloneDds\src\CycloneDDS.Core\CycloneDDS.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.7" />
  </ItemGroup>
  <Import Project="..\FDP\ExtDeps\FastCycloneDds\tools\CycloneDDS.CodeGen\CycloneDDS.targets" />
</Project>
```

### E2 — Move test files

Move all .cs files from `Hrot.NED.Tests/` to `Hrot.Network.NED.Tests/`:
- AttributeRecordTests.cs
- DdsIntegrationTests.cs
- FireInteractionMessageTests.cs
- GenericMessageFieldTests.cs
- MissionControlMarshalRoundTripTests.cs
- OrchestrationSchemaTests.cs (NOTE: OrchestrationMessages types now in Hrot.Network.Orchestration)
- PerceptionPathfindingDescriptorTests.cs
- SubsystemStatusAnnounceTests.cs

For `OrchestrationSchemaTests.cs`: it tests orchestration types. After moving OrchestrationMessages
to Hrot.Network.Orchestration, this test needs to also reference Hrot.Network.Orchestration.
Add `<ProjectReference Include="..\Hrot.Network.Orchestration\Hrot.Network.Orchestration.csproj" />`
to Hrot.Network.NED.Tests.csproj if OrchestrationSchemaTests.cs is included.

---

## TASK F: Update Solution File

In `IOS-IG-SimHost.sln`:
1. ADD `Hrot.Network.NED` (new GUID: `{F6A7B8C9-D0E1-2345-F012-345678901205}`)
2. ADD `Hrot.Network.NED.Tests` (new GUID: `{A7B8C9D0-E1F2-3456-0123-456789012306}`)
3. REMOVE `Hrot.NED` entry (project absorbed)
4. REMOVE `Hrot.Network` entry (project absorbed)
5. REMOVE `Hrot.NED.Tests` entry (replaced by Hrot.Network.NED.Tests)
6. KEEP `Hrot.Map.Common` (empty stub — remove it too if you prefer)
7. KEEP `Hrot.Map.Common.Tests` (still has 3 test files)

---

## TASK G: Verification

After all changes:
```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln -v q 2>&1 | Select-String "error" | Select-Object -First 20
```

Run tests:
```
dotnet test Hrot.Network.NED.Tests/Hrot.Network.NED.Tests.csproj
dotnet test Hrot.Map.Common.Tests/Hrot.Map.Common.Tests.csproj
dotnet test Hrot.Core.Tests/Hrot.Core.Tests.csproj
```

Verify Hrot.Network.NED has no reference to old Hrot.NED project:
```
dotnet list Hrot.Network.NED/Hrot.Network.NED.csproj reference
```

---

## Critical Notes

1. The DDS CodeGen `<Import>` line is required for both `Hrot.Network.NED` and
   `Hrot.Network.NED.Tests` to generate the C# code from IDL/DDS schema definitions.

2. When moving NED schema files (.cs files that contain `[DdsStruct]` etc.), the
   CodeGen operates on these during build. The IDL file names (like `"hrot-generic-desc"`)
   must match the IDL files in the `obj/Generated/` output. Since CodeGen reads the tags
   from the .cs source files, the filenames don't actually need to match — the files
   are just moved.

3. The `NedNetworkFactory.CreateReplicationModule()` needs to pass the correct arguments
   to `NedReplicationModule`. Read the FULL constructor signature of `NedReplicationModule`
   by reading `Hrot.Network.NED/Replication/NedReplicationModule.cs` (after moving it)
   and ensure the factory provides all required params.

4. If `NedReplicationModule` constructor uses `HrotEnvironment.CreateGeoTransform()`,
   that method is now in `Hrot.Core` (it was moved from `Hrot.Map.Common`). The using
   directive already works since it's in `Hrot.Map.Common` namespace (preserved).

5. Hrot.Common's `HrotNodeBuilder.cs` references `Hrot.Map.Common` types.
   After Hrot.Map.Common becomes empty, the types it used to provide are now in Hrot.Core
   (for non-NED types) and Hrot.Network.NED (for NED types). Check if HrotNodeBuilder.cs
   compiles correctly.

6. `using Hrot.Map.Common.Translators;` in NedReplicationModule.cs references
   `SharedTranslatorPack`, `KinematicTranslatorPack`, `EntityStatesIngressPack` which 
   moved to `Hrot.Network.NED/Translators/Map/`. The namespace is UNCHANGED so the
   using directive still works — just make sure the files are in the project.

---

## Report

Submit to: `.dev/modular-2/reports/BATCH-06-REPORT.md`
