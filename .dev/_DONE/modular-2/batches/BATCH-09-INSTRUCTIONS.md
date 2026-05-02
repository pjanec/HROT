# BATCH-09: Decouple SimHost from NED (TASK-P4-002) — Revised

**Batch Number:** BATCH-09
**Tasks:** TASK-P4-002 (full)
**Phase:** Phase 4
**Estimated Effort:** 10-14 hours
**Priority:** HIGH -- build is currently BROKEN (3 missing interface implementations in NedNetworkFactory / BdcNetworkFactory for ISimHostNetworkAdapter which must be DELETED, not implemented)
**Dependencies:** BATCH-08 committed (ExCon decoupled, INetworkFactory wired)

---

## Architecture Correction (Read First!)

The PREVIOUS version of these instructions contained a CRITICAL architectural error:
it introduced `ISimHostNetworkAdapter` with `ProcessCreationRequests()` and
`ProcessDeleteRequests()` for SimHost. **This is wrong.**

**SimHost is the muscle role.** It must NEVER handle `CreateEntityRequest` or
`DeleteEntityRequest`. Those are brain (CGF) responsibilities.

**CGF is the brain.** It already handles entity lifecycle in
`CgfSubsystem.Initialize()` (see [CgfSubsystem.cs](../../Hrot.ClusterRunner/Services/CgfSubsystem.cs)).

The correct information flow:
1. ExCon sends `CreateEntityRequest` over DDS.
2. **CGF** receives it, allocates a network ID, publishes `SpawnEntityCommand` (ECS bus).
3. **SimHost** receives `SpawnEntityCommand` via NedReplicationModule and materializes
   the entity locally. SimHost never reads `CreateEntityRequest` from DDS.

**Consequence:** `ISimHostNetworkAdapter` must be deleted, not implemented.
The broken build is fixed by deleting the interface method, not by implementing it.

---

## Onboarding

1. `.dev/modular-2/DESIGN.md` -- Architecture overview, Phase 4.
2. `.dev/modular-2/TASK-DETAIL.md` -- TASK-P4-002.
3. `Hrot.Network.NED/ExCon/` -- reference for how ExCon was decoupled in BATCH-08.
4. `Hrot.ClusterRunner/Services/CgfSubsystem.cs` -- already correctly wires
   `CreateEntityRequestSystem` with `isDefaultProcessor: true`. Do NOT break this.
5. `Hrot.SimHost/SimHostApp.cs` -- remove entity lifecycle wiring from here.

---

## Current State (what exists, what is broken)

### Already exists (created preparatorily, partially correct):
- `Hrot.Core/Network/ISimHostNetworkAdapter.cs` -- **DELETE THIS** (wrong abstraction)
- `Hrot.Core/Network/ISimHostMissionSender.cs` -- KEEP (legitimate: visualization sends missions)
- `Hrot.Core/Network/ISimHostAuxiliaryTranslators.cs` -- KEEP (legitimate: combat/perception translators)
- `INetworkFactory.cs` has `CreateSimHostNetworkAdapter()`, `CreateSimHostMissionSender()`,
  `CreateSimHostAuxiliaryTranslators()` -- remove `CreateSimHostNetworkAdapter()` from the interface

### Broken (build fails):
- `NedNetworkFactory` does NOT implement `CreateSimHostNetworkAdapter()`,
  `CreateSimHostMissionSender()`, `CreateSimHostAuxiliaryTranslators()` -- the first one
  must never be implemented (delete the interface method); implement the other two.
- `BdcNetworkFactory` -- same situation.
- `SimHostApp.cs` still creates `DdsCreateEntityRequestSource`, `DdsCreateUpdateDeleteEntityAckSink`,
  `DdsDeleteEntityRequestSource` and wires `CreateEntityRequestSystem` and `DeleteEntityRequestSystem`.
  These must be REMOVED entirely from SimHostApp.cs.
- Translator files (combat, mission-control) still live in `Hrot.SimHost/Network/`.
- `SimHostVisualization.cs` still takes `DdsWriter<MissionControlRequest>` directly.
- `Hrot.SimHost.csproj` still has `<ProjectReference Include="../Hrot.NED/...">` (or
  the merged NED reference).

---

## Mandatory Workflow

Build check after each phase before moving on:

```
dotnet build IOS-IG-SimHost.sln -v quiet 2>&1 | Select-String "error"
```

---

## Phase 1: Fix the Build -- Delete Wrong Abstraction, Remove from Factory

### 1a: Delete ISimHostNetworkAdapter.cs

Delete `Hrot.Core/Network/ISimHostNetworkAdapter.cs` entirely.
This file contains `ISimHostNetworkAdapter`, `SimHostCreationRequest`, and `SimHostDeleteRequest`.
All three are removed.

### 1b: Remove CreateSimHostNetworkAdapter() from INetworkFactory

In `Hrot.Core/Network/INetworkFactory.cs`, remove the method declaration:
```csharp
ISimHostNetworkAdapter CreateSimHostNetworkAdapter();
```
And remove its XML doc comment block above it.

Also remove the `using` of `ISimHostNetworkAdapter` if the type is no longer referenced
anywhere in INetworkFactory.cs.

### 1c: Verify NedNetworkFactory and BdcNetworkFactory no longer fail to compile

After steps 1a and 1b the "missing interface member" errors for `CreateSimHostNetworkAdapter()`
disappear. Confirm with a build. There will still be errors for `CreateSimHostMissionSender()`
and `CreateSimHostAuxiliaryTranslators()` -- those are addressed in Phase 3.

**After Phase 1: `dotnet build` error count should drop from 3 to 2.**

---

## Phase 2: Add Neutral Entity Lifecycle Types to Hrot.Core.Network

The entity lifecycle request/response types need to be NED-free so the systems that handle
them (`CreateEntityRequestSystem`, `DeleteEntityRequestSystem`, `NedRequestFinalizationSystem`)
can live in `Hrot.CGF` without carrying a NED dependency.

### 2a: Create EntityLifecycleInterfaces.cs in Hrot.Core/Network/

Create `Hrot.Core/Network/EntityLifecycleInterfaces.cs`:

```csharp
using System;

namespace Hrot.Core.Network;

/// <summary>
/// Neutral status codes for entity lifecycle ACKs.
/// Integer values are intentionally identical to <c>NedStatusCode</c> so that
/// the NED adapter can cast directly without a lookup table.
/// </summary>
public enum EntityOperationStatus : int
{
    /// <summary>Operation completed successfully.</summary>
    Success = 0,
    /// <summary>Request accepted; final ACK will follow after ECS confirms.</summary>
    InProgress = 1,
    /// <summary>Requested entity type not found in TKB.</summary>
    UnknownDescriptorType = 2,
    /// <summary>Entity ID not found in the network map.</summary>
    EntityNotFound = 3,
}

/// <summary>
/// Pre-parsed entity-creation request received from the network.
/// Simple primitive fields only -- no ECS components, no descriptor unions.
/// </summary>
public sealed class EntityCreationRequest
{
    /// <summary>Unique request identifier used for two-phase ACK tracking.</summary>
    public Guid RequestId { get; init; }

    /// <summary>AppInstanceId of the requesting node (0 = broadcast).</summary>
    public int OwnerAppInstanceId { get; init; }

    /// <summary>TKB entity type code extracted from the EntityMaster descriptor.</summary>
    public long TkbType { get; init; }

    /// <summary>Packed DIS entity type discriminator.</summary>
    public ulong DisType { get; init; }

    /// <summary>
    /// JSON attribute overrides forwarded verbatim from the wire message.
    /// Processed by <c>JsonAttributeCompiler</c> inside <c>CreateEntityRequestSystem</c>.
    /// </summary>
    public string? InitialAttributesJson { get; init; }
}

/// <summary>
/// Pre-parsed entity-deletion request received from the network.
/// </summary>
public sealed class EntityDeletionRequest
{
    /// <summary>Unique request identifier.</summary>
    public Guid RequestId { get; init; }

    /// <summary>Network entity ID to delete.</summary>
    public long EntityId { get; init; }
}

/// <summary>
/// Source of incoming entity-creation requests.
/// Implemented by NED/BDC adapters; tested via stubs.
/// </summary>
public interface IEntityCreationRequestSource
{
    /// <summary>
    /// Drains all pending requests and invokes <paramref name="handler"/> for each.
    /// Callback-based to avoid per-frame List allocations.
    /// </summary>
    void ProcessRequests(Action<EntityCreationRequest> handler);
}

/// <summary>
/// Source of incoming entity-deletion requests.
/// </summary>
public interface IEntityDeletionRequestSource
{
    /// <summary>Drains all pending requests and invokes <paramref name="handler"/> for each.</summary>
    void ProcessRequests(Action<EntityDeletionRequest> handler);
}

/// <summary>
/// Sink for entity lifecycle ACK messages (creation and deletion).
/// Single neutral method covers both create and delete ACKs.
/// </summary>
public interface IEntityAckSink
{
    /// <summary>Publishes a lifecycle ACK back to the original requester.</summary>
    void WriteAck(Guid requestId, long entityId, EntityOperationStatus status);
}
```

---

## Phase 3: Create NED Adapters for CGF in Hrot.Network.NED/CGF/

These are the DDS-backed implementations of the neutral interfaces.
They replace `DdsCreateEntityRequestSource`, `DdsDeleteEntityRequestSource`,
`DdsCreateUpdateDeleteEntityAckSink` currently in `Hrot.SimHost/Network/SimHostNetworkAdapters.cs`.

Create directory `Hrot.Network.NED/CGF/` and create
`Hrot.Network.NED/CGF/NedCgfEntityLifecycleAdapters.cs`:

```csharp
using System;
using CycloneDDS.Runtime;
using Hrot.Core.Network;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.CGF;

/// <summary>
/// DDS-backed source of <c>CreateEntityRequest</c> messages.
/// Converts NED wire messages to the neutral <see cref="EntityCreationRequest"/> DTO.
///
/// Design: simple extraction. Only TkbType and DisType are extracted from the
/// EntityMaster descriptor. InitialAttributesJson is passed through unchanged.
/// No descriptor-to-component translation is performed here.
/// </summary>
internal sealed class NedEntityCreationRequestSource : IEntityCreationRequestSource
{
    private readonly DdsReader<CreateEntityRequest> _reader;

    public NedEntityCreationRequestSource(DdsParticipant participant)
        => _reader = new DdsReader<CreateEntityRequest>(participant);

    public void ProcessRequests(Action<EntityCreationRequest> handler)
    {
        using var loan = _reader.Take();
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var msg = sample.Data;

            // Extract TkbType and DisType from EntityMaster descriptor only.
            long  tkbType = 0;
            ulong disType = 0;

            if (msg.InitialDescriptors != null)
            {
                foreach (var desc in msg.InitialDescriptors)
                {
                    if (desc._d == EDescriptorType.dtEntityMaster)
                    {
                        tkbType = desc.EntityMaster.TkbType;
                        var d = desc.EntityMaster.DisType;
                        disType = ((ulong)d.Kind        << 56)
                                | ((ulong)d.Domain      << 48)
                                | ((ulong)d.Country     << 32)
                                | ((ulong)d.Category    << 24)
                                | ((ulong)d.Subcategory << 16)
                                | ((ulong)d.Specific    <<  8)
                                |  (ulong)d.Extra;
                        break;
                    }
                }
            }

            handler(new EntityCreationRequest
            {
                RequestId             = msg.RequestId,
                OwnerAppInstanceId    = msg.Owner.AppInstanceId,
                TkbType               = tkbType,
                DisType               = disType,
                InitialAttributesJson = msg.InitialAttributesJson,
            });
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// DDS-backed source of <c>DeleteEntityRequest</c> messages.
/// </summary>
internal sealed class NedEntityDeletionRequestSource : IEntityDeletionRequestSource
{
    private readonly DdsReader<DeleteEntityRequest> _reader;

    public NedEntityDeletionRequestSource(DdsParticipant participant)
        => _reader = new DdsReader<DeleteEntityRequest>(participant);

    public void ProcessRequests(Action<EntityDeletionRequest> handler)
    {
        using var loan = _reader.Take();
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            handler(new EntityDeletionRequest
            {
                RequestId = sample.Data.RequestId,
                EntityId  = sample.Data.EntityId,
            });
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// DDS-backed ACK sink for entity lifecycle operations.
/// Writes <c>CreateUpdateDeleteEntityAck</c> for both creation and deletion ACKs.
/// </summary>
internal sealed class NedEntityAckSink : IEntityAckSink
{
    private readonly DdsWriter<CreateUpdateDeleteEntityAck> _writer;

    public NedEntityAckSink(DdsParticipant participant)
        => _writer = new DdsWriter<CreateUpdateDeleteEntityAck>(participant);

    public void WriteAck(Guid requestId, long entityId, EntityOperationStatus status)
        => _writer.Write(new CreateUpdateDeleteEntityAck
        {
            RequestId  = requestId,
            EntityId   = (int)entityId,
            StatusCode = (int)status,
        });

    public void Dispose() => _writer.Dispose();
}
```

**Important:** verify that `CreateUpdateDeleteEntityAck` has a `StatusCode` (int) field, not
`ErrorCode`. Check `Hrot.Network.NED/GenericMessages.cs`.
If the field is named `ErrorCode`, adjust accordingly.

---

## Phase 4: Move Brain-Role Systems to Hrot.CGF/Systems/

These systems belong to the brain (CGF), not the muscle (SimHost).
Move and refactor the following files.

### 4a: Move CreateEntityRequestSystem.cs

Move `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs` to `Hrot.CGF/Systems/CreateEntityRequestSystem.cs`.

In the file:
- Change namespace: `Hrot.SimHost.Systems` -> `Hrot.CGF.Systems`
- Replace `ICreateEntityRequestSource` with `IEntityCreationRequestSource` (from `Hrot.Core.Network`)
- Replace `ICreateUpdateDeleteEntityAckSink` with `IEntityAckSink` (from `Hrot.Core.Network`)
- Replace `CreateEntityRequest request` parameter type with `EntityCreationRequest request`
- Replace `NedStatusCode.xxx` with `(int)EntityOperationStatus.xxx`
- Replace calls to `_ackSink.WriteAck(new CreateUpdateDeleteEntityAck { ... })` with
  `_ackSink.WriteAck(request.RequestId, entityId, EntityOperationStatus.xxx)`
- Remove `using Hrot.NED.Messages;` and `using Hrot.NED.Descriptors;` and
  `using Hrot.NED.Common;`
- Keep all other logic unchanged

The constructor signature changes from:
```csharp
public CreateEntityRequestSystem(
    ICreateEntityRequestSource          requestSource,
    ICreateUpdateDeleteEntityAckSink    ackSink,
    ...
```
to:
```csharp
public CreateEntityRequestSystem(
    IEntityCreationRequestSource        requestSource,
    IEntityAckSink                      ackSink,
    ...
```

The internal `ProcessIncomingRequest(CreateEntityRequest request)` method changes to
`ProcessIncomingRequest(EntityCreationRequest request)`.
The `PendingRequest` struct now holds `EntityCreationRequest Request` instead of
`CreateEntityRequest Request`.

### 4b: Move DeleteEntityRequestSystem.cs

Move `Hrot.SimHost/Systems/DeleteEntityRequestSystem.cs` to `Hrot.CGF/Systems/DeleteEntityRequestSystem.cs`.

In the file:
- Change namespace: `Hrot.SimHost.Systems` -> `Hrot.CGF.Systems`
- Replace field `IDeleteEntityRequestSource _requestSource` with `IEntityDeletionRequestSource _requestSource`
- Replace field `ICreateUpdateDeleteEntityAckSink _ackSink` with `IEntityAckSink _ackSink`
- Replace `DeleteEntityRequest request` parameter in `ProcessRequest()` with `EntityDeletionRequest request`
- Replace `NedStatusCode.xxx` with `EntityOperationStatus.xxx`
- Replace `_ackSink.WriteAck(new CreateUpdateDeleteEntityAck { ... })` with
  `_ackSink.WriteAck(request.RequestId, request.EntityId, EntityOperationStatus.xxx)`
- Remove `using Hrot.NED.Messages;`

### 4c: Move SstRequestFinalizationSystem.cs (NedRequestFinalizationSystem)

Move `Hrot.SimHost/Systems/SstRequestFinalizationSystem.cs` to
`Hrot.CGF/Systems/NedRequestFinalizationSystem.cs`.

In the file:
- Change namespace: `Hrot.SimHost.Systems` -> `Hrot.CGF.Systems`
- Replace field `ICreateUpdateDeleteEntityAckSink _ackSink` with `IEntityAckSink _ackSink`
- Replace constructor parameter `ICreateUpdateDeleteEntityAckSink ackSink` with `IEntityAckSink ackSink`
- Replace all calls:
  `_ackSink.WriteAck(new CreateUpdateDeleteEntityAck { RequestId = ..., EntityId = ..., StatusCode = (int)NedStatusCode.xxx })`
  with:
  `_ackSink.WriteAck(networkId, networkId, EntityOperationStatus.xxx)` (use the actual requestId from PendingRequest)
  -- look up the exact request ID from the tracking dict entry and use:
  `_ackSink.WriteAck(pending.RequestId, networkId, finalStatus)`
- Replace `NedStatusCode.xxx` with `EntityOperationStatus.xxx`
- Remove `using Hrot.NED.Messages;`

### 4d: Delete the old interface files from Hrot.SimHost/Systems/

Delete:
- `Hrot.SimHost/Systems/ICreateEntityRequestSource.cs`
- `Hrot.SimHost/Systems/ICreateEntityAckSink.cs`
- `Hrot.SimHost/Systems/IDeleteEntityRequestSource.cs`

### 4e: Add needed references to Hrot.CGF.csproj

The moved systems use:
- `FDP.Toolkit.NetworkSpawning.Events.SpawnEntityCommand` -- via Fdp.Engine (already transitive via Hrot.Network.NED)
- `FDP.Toolkit.Replication.Services.NetworkEntityMap` -- same
- `FDP.Toolkit.Replication.Patching.JsonAttributeCompiler` -- same
- `FDP.Toolkit.Replication.Patching.BinaryInterpreter<T>` -- same
- `Fdp.Modules.Geographic.IGeographicTransform` -- same
- `ModuleHost.Core.Abstractions.IEcsModuleSystem` -- via Fdp.Core (already referenced)
- `FDP.Toolkit.Tkb.ITkbDatabase` -- via Fdp.Engine
- `Hrot.Core.Network` -- via Hrot.Core (already referenced)

`Hrot.CGF.csproj` ALREADY references `Hrot.Network.NED` which brings in `Fdp.Engine`.
No new project references should be needed. If the build fails with missing types,
add `<ProjectReference Include="../FDP/Toolkits/Fdp.Engine/Fdp.Engine.csproj" />` explicitly.

---

## Phase 5: Update SimHostModule to Remove Brain-Specific Systems

`SimHostModule` is in `Hrot.SimHost.Modules`. It currently holds optional
`CreateEntityRequestSystem`, `DeleteEntityRequestSystem`, `NedRequestFinalizationSystem`
fields. These are brain concerns -- remove them.

In `Hrot.SimHost/Modules/SimHostModule.cs`:
1. Remove fields: `_requestSystem`, `_deleteSystem`, `_finalizationSystem`
2. Remove constructor parameters for those fields (they are optional, so remove the
   optional params and any null-guard logic)
3. Remove `using Hrot.SimHost.Systems;` if no longer needed after the removal
   (verify: the module still references `NetworkSpawningSystem` from there? No,
   `NetworkSpawningSystem` is in `FDP.Toolkit.NetworkSpawning.Systems`)
4. In `RegisterSystems()`, remove the three conditional registrations for the removed systems.

The simplified constructor becomes:
```csharp
public SimHostModule(
    NetworkSpawningSystem             spawnSystem,
    GeoSpatialEgressTranslator?       geoEgressTranslator        = null,
    MapVisualOverlayEgressTranslator? mapOverlayEgressTranslator = null,
    MapRouteEgressTranslator?         mapRouteEgressTranslator   = null,
    EntityMissionIngressTranslator?   missionIngressTranslator   = null,
    EntityMissionEgressTranslator?    missionEgressTranslator    = null)
```

---

## Phase 6: Update CgfSubsystem.cs to Use New Types

In `Hrot.ClusterRunner/Services/CgfSubsystem.cs`:

1. Add using:
   ```csharp
   using Hrot.CGF.Systems;
   using Hrot.Core.Network;
   using Hrot.Network.NED.CGF;
   ```

2. Replace the DDS adapter instantiation block:
   ```csharp
   // OLD:
   var requestSource = new DdsCreateEntityRequestSource(_context.Participant);
   var ackSink       = new DdsCreateUpdateDeleteEntityAckSink(_context.Participant);
   
   // NEW:
   var requestSource = new NedEntityCreationRequestSource(_context.Participant);
   var deleteSource  = new NedEntityDeletionRequestSource(_context.Participant);
   var ackSink       = new NedEntityAckSink(_context.Participant);
   ```

3. Update `NedRequestFinalizationSystem` constructor call:
   ```csharp
   // OLD: new NedRequestFinalizationSystem(ackSink, _entityMap!)
   // NEW: same -- but ackSink type is now IEntityAckSink (NedEntityAckSink implements it)
   var finalizationSystem = new NedRequestFinalizationSystem(ackSink, _entityMap!);
   ```

4. Update `CreateEntityRequestSystem` constructor call to use new parameter types:
   ```csharp
   var requestSystem = new CreateEntityRequestSystem(
       requestSource:        requestSource,   // IEntityCreationRequestSource
       ackSink:              ackSink,          // IEntityAckSink
       tkbDb:                tkbDb,
       idAllocator:          idAllocator,
       localNodeId:          _context.NodeId,
       geoTransform:         geoTransform,
       jsonAttributeCompiler: jsonCompiler,
       binaryInterpreter:    binaryInterpreter,
       finalizationSystem:   finalizationSystem,
       isDefaultProcessor:   true,
       ownershipStrategy:    ownershipStrategy);
   ```

5. Add `DeleteEntityRequestSystem` (it was missing from CgfSubsystem!):
   ```csharp
   var deleteSystem = new DeleteEntityRequestSystem(
       deleteSource,
       ackSink,
       _entityMap!,
       finalizationSystem,
       _context.NodeId);
   ```

6. Update `SimHostModule` construction to remove brain-specific params:
   ```csharp
   _context.Kernel.RegisterModule(new SimHostModule(
       spawnSystem:        spawnSystem));
   ```

7. Register brain-specific systems on the simGroup directly:
   ```csharp
   simGroup.AddSystem(requestSystem);
   simGroup.AddSystem(deleteSystem);
   simGroup.AddSystem(finalizationSystem);
   ```

8. Remove the `using Hrot.SimHost.Network;` import if it was only used for the old
   `DdsCre...` adapters.

9. Remove `using Hrot.SimHost.Systems;` and replace with `using Hrot.CGF.Systems;`.

---

## Phase 7: Remove Entity Lifecycle Wiring from SimHostApp.cs

In `Hrot.SimHost/SimHostApp.cs`, find the section around line 441 that creates:
```csharp
var requestSource      = new DdsCreateEntityRequestSource(ddsParticipant!);
var ackSink            = new DdsCreateUpdateDeleteEntityAckSink(ddsParticipant!);
var deleteSource       = new DdsDeleteEntityRequestSource(ddsParticipant!);
var finalizationSystem = new NedRequestFinalizationSystem(ackSink, entityMap);
var requestSystem      = new CreateEntityRequestSystem(...);
var deleteSystem       = new DeleteEntityRequestSystem(...);
```

Delete all of those lines. Update the `SimHostModule` construction to just:
```csharp
var simHostMod = new SimHostModule(
    spawnSystem:        spawningSystem);
_kernel.RegisterModule(simHostMod);
```

Remove any `using` statements that were only needed for those deleted classes.

---

## Phase 8: Delete Old DDS Adapter File from SimHost

`Hrot.SimHost/Network/SimHostNetworkAdapters.cs` contains
`DdsCreateEntityRequestSource`, `DdsCreateUpdateDeleteEntityAckSink`, `DdsDeleteEntityRequestSource`.
These have been replaced by `NedEntityCreationRequestSource` etc. in `Hrot.Network.NED/CGF/`.

Delete `Hrot.SimHost/Network/SimHostNetworkAdapters.cs`.

---

## Phase 9: Implement ISimHostMissionSender in Hrot.Network.NED

### 9a: Create NedSimHostMissionSender.cs

Create `Hrot.Network.NED/SimHost/NedSimHostMissionSender.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using CycloneDDS.Runtime;
using Hrot.Core.Network;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.SimHost;

/// <summary>
/// NED implementation of <see cref="ISimHostMissionSender"/>.
/// Sends a MoveToLocation behavior mission via DDS <c>MissionControlRequest</c>.
/// </summary>
internal sealed class NedSimHostMissionSender : ISimHostMissionSender
{
    private readonly DdsWriter<MissionControlRequest> _writer;

    public NedSimHostMissionSender(DdsParticipant participant)
        => _writer = new DdsWriter<MissionControlRequest>(participant);

    public void SendNavigateToPoint(long entityNetworkId, Vector2 destination, float speed, float arrivalRadius)
    {
        var paramsJson = string.Format(
            CultureInfo.InvariantCulture,
            "{{\"X\":{0},\"Y\":{1},\"Speed\":{2},\"ArrivalRadius\":{3}}}",
            destination.X, destination.Y, speed, arrivalRadius);

        var taskId = Guid.NewGuid();

        _writer.Write(new MissionControlRequest
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = entityNetworkId,
            BaseVersion    = 0,
            Payload        = new MissionCommandUnion
            {
                _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = new MissionPlan
                {
                    ActiveTaskId = taskId,
                    Tasks        = new List<MissionTask>
                    {
                        new MissionTask
                        {
                            TaskId          = taskId,
                            ExecutingEngine  = "CGFX",
                            BehaviorId      = "MoveToLocation",
                            BehaviorParams  = paramsJson,
                            Triggers        = new List<MissionTrigger>
                            {
                                new MissionTrigger { Type = "BehaviorFinished" },
                            },
                            State = eTaskState.TASK_PLANNED,
                        }
                    },
                },
            },
        });
    }

    public void Dispose() => _writer.Dispose();
}
```

Verify namespaces by checking `Hrot.Network.NED/MissionMessages.cs`. Types needed:
`MissionControlRequest`, `MissionCommandUnion`, `eMissionCommandType`, `MissionPlan`,
`MissionTask`, `MissionTrigger`, `eTaskState`. If any of these aren't in `Hrot.NED.Messages`,
check `Hrot.NED.Descriptors` or other NED message files.

---

## Phase 10: Move Auxiliary Translator Files to Hrot.Network.NED/SimHost/

### 10a: Move ingress translator files

Move from `Hrot.SimHost/Network/Ingress/` to `Hrot.Network.NED/SimHost/`:
- `EntityHitDamageIngressTranslator.cs`
- `MissionControlIngressTranslator.cs`
- `MunitionDetonationIngressTranslator.cs`
- `WeaponFireRequestIngressTranslator.cs`

Change namespace in each file from `Hrot.SimHost.Network.Ingress` to `Hrot.Network.NED.SimHost`.

### 10b: Move egress translator files

Move from `Hrot.SimHost/Network/Egress/` to `Hrot.Network.NED/SimHost/`:
- `AudioTargetDetectedEgressTranslator.cs`
- `DamageAssessedEgressTranslator.cs`
- `MissionControlAckEgressTranslator.cs`
- `MunitionDetonationEgressTranslator.cs`
- `WeaponFireIntentEgressTranslator.cs`
- `WeaponFireNotificationEgressTranslator.cs`

Change namespace in each file from `Hrot.SimHost.Network.Egress` to `Hrot.Network.NED.SimHost`.

### 10c: Check and move translator pack files

Inspect these files in `Hrot.SimHost/Network/`:
- `BrainPathfindingTranslatorPack.cs`
- `BrainPerceptionTranslatorPack.cs`
- `PathfindingTranslators.cs`
- `PerceptionTranslators.cs`
- `SimHostAuxiliaryTranslatorPack.cs`
- `SimPathfindingTranslatorPack.cs`
- `SimPerceptionTranslatorPack.cs`

For EACH file: if it uses `CycloneDDS.Runtime`, `Hrot.NED.*` or `Hrot.Network.NED.*`
imports, move it to `Hrot.Network.NED/SimHost/` and update its namespace to
`Hrot.Network.NED.SimHost`. Files that use only FDP/ECS imports with no DDS types can
optionally stay if needed, but most will have DDS references.

After moving, `SimHostAuxiliaryTranslatorPack.Create(...)` is now in the new namespace.

### 10d: Create NedSimHostAuxiliaryTranslators.cs

Create `Hrot.Network.NED/SimHost/NedSimHostAuxiliaryTranslators.cs`:

```csharp
using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using Hrot.Common;
using Hrot.Core.Network;
using ModuleHost.Core;
using ModuleHost.Network.Cyclone.Systems;

namespace Hrot.Network.NED.SimHost;

/// <summary>
/// NED implementation of <see cref="ISimHostAuxiliaryTranslators"/>.
/// Wraps <see cref="SimHostAuxiliaryTranslatorPack"/> and registers all
/// DDS translator systems on the given kernel.
/// </summary>
internal sealed class NedSimHostAuxiliaryTranslators : ISimHostAuxiliaryTranslators
{
    private readonly List<IDescriptorTranslator> _translators;

    public NedSimHostAuxiliaryTranslators(
        DdsParticipant   participant,
        NetworkEntityMap entityMap,
        FdpEventBus      eventBus,
        int              localNodeId,
        NodeRole         role)
    {
        _translators = SimHostAuxiliaryTranslatorPack.Create(
            participant, entityMap, eventBus, localNodeId, role);
    }

    public void RegisterOn(ModuleHostKernel kernel)
    {
        kernel.RegisterGlobalSystem(new CycloneNetworkIngressSystem(_translators.ToArray()));
        kernel.RegisterGlobalSystem(new CycloneEgressSystem(_translators.ToArray()));
        kernel.RegisterGlobalSystem(new CycloneNetworkCleanupSystem(_translators));
    }

    public void Dispose() { }
}
```

Verify import namespaces -- `CycloneNetworkIngressSystem` etc. are in
`ModuleHost.Network.Cyclone.Systems` or similar. Check `Hrot.SimHost/SimHostApp.cs`
for the currently-used namespace of those types.

---

## Phase 11: Implement Factory Methods in NedNetworkFactory and BdcNetworkFactory

### 11a: Update NedNetworkFactory

In `Hrot.Network.NED/Factory/NedNetworkFactory.cs`:

1. Verify what constructor parameters `NedNetworkFactory` currently accepts.
   It should have `_participant`, `_entityMap`, `_eventBus`, `_localNodeId`, `_role`.
   If any are missing, check the factory class.

2. Implement:
```csharp
/// <inheritdoc/>
public ISimHostMissionSender CreateSimHostMissionSender()
{
    if (_participant == null) return new NullSimHostMissionSender();
    return new NedSimHostMissionSender(_participant);
}

/// <inheritdoc/>
public ISimHostAuxiliaryTranslators CreateSimHostAuxiliaryTranslators()
{
    if (_participant == null) return new NullSimHostAuxiliaryTranslators();
    return new NedSimHostAuxiliaryTranslators(
        _participant, _entityMap, _eventBus, _localNodeId, _role);
}
```

3. Add null stubs (next to existing null stubs in the file):
```csharp
private sealed class NullSimHostMissionSender : ISimHostMissionSender
{
    public void SendNavigateToPoint(long id, Vector2 dest, float speed, float radius) { }
    public void Dispose() { }
}

private sealed class NullSimHostAuxiliaryTranslators : ISimHostAuxiliaryTranslators
{
    public void RegisterOn(ModuleHostKernel kernel) { }
    public void Dispose() { }
}
```

### 11b: Update BdcNetworkFactory

In `Hrot.Network.BDC/Factory/BdcNetworkFactory.cs`:

Implement both methods with null/stub implementations (same pattern as existing BDC stubs):
```csharp
public ISimHostMissionSender CreateSimHostMissionSender()
    => new BdcNullSimHostMissionSender();

public ISimHostAuxiliaryTranslators CreateSimHostAuxiliaryTranslators()
    => new BdcNullSimHostAuxiliaryTranslators();
```

Add the two null stub classes implementing the interfaces with empty bodies.

**After Phase 11: `dotnet build IOS-IG-SimHost.sln -v quiet` must show 0 errors.**

---

## Phase 12: Update SimHostApp.cs to Use Factory

### 12a: Create NedNetworkFactory after HrotNodeContext is built

In `SimHostApp.cs` OnLoad, after the HrotNodeContext build block (step 5), add:
```csharp
// ── 5b. Network factory ──────────────────────────────────────────────────
var networkFactory = new NedNetworkFactory(
    participant:      ddsParticipant,
    entityMap:        entityMap,
    geoTransform:     wgs84,
    eventBus:         _eventBus,
    localNodeId:      localNodeId,
    role:             _role,
    tkbDb:            tkbDb,
    lifecycleModule:  elm,
    behaviorRegistry: behaviorRegistry);
```

Verify the exact constructor signature of `NedNetworkFactory` before writing the code.
Add `using Hrot.Network.NED.Factory;` if needed.

### 12b: Replace SimHostAuxiliaryTranslatorPack.Create with factory call

Replace the block (currently around line 472):
```csharp
// OLD:
var auxTranslators = SimHostAuxiliaryTranslatorPack.Create(...);
_kernel.RegisterGlobalSystem(new CycloneNetworkIngressSystem(auxTranslators.ToArray()));
_kernel.RegisterGlobalSystem(new CycloneEgressSystem(auxTranslators.ToArray()));
_kernel.RegisterGlobalSystem(new CycloneNetworkCleanupSystem(auxTranslators));

// NEW:
networkFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(_kernel);
```

### 12c: Replace DdsWriter<MissionControlRequest> with factory mission sender

In visualization initialization (currently around SimHostApp.cs line 491):
```csharp
// OLD:
new DdsWriter<MissionControlRequest>(ddsParticipant!),

// NEW:
networkFactory.CreateSimHostMissionSender(),
```

---

## Phase 13: Update SimHostVisualization.cs

`SimHostVisualization.Initialize()` currently takes a `DdsWriter<MissionControlRequest>`
parameter. Change this to `ISimHostMissionSender`.

Find the usages of the writer inside `SimHostVisualization.cs` -- specifically in
`HandleRightClickForEntity()` (or equivalent method) where it constructs a
`MissionControlRequest` and calls `_missionWriter.Write(...)`.

Replace the manual `MissionControlRequest` construction with a call to:
```csharp
_missionSender.SendNavigateToPoint(entityNetworkId, destination, speed, arrivalRadius);
```

Extract the appropriate values from the existing call for `entityNetworkId`, `destination`,
`speed`, and `arrivalRadius`. The default arrival radius currently in the code is `3.0f`.

Remove all `DdsWriter<MissionControlRequest>` references from `SimHostVisualization.cs`.
Remove the `using` for NED mission types if they are no longer referenced.

---

## Phase 14: Remove NED Reference from Hrot.SimHost.csproj

Run a grep to confirm no remaining NED usages in Hrot.SimHost source:
```
Get-ChildItem -Path "Hrot.SimHost" -Recurse -Include "*.cs" |
  Select-String "Hrot\.NED\.|CycloneDDS|DdsReader|DdsWriter|DdsParticipant"
```

If grep is clean (zero hits), remove the NED project reference from
`Hrot.SimHost/Hrot.SimHost.csproj`:
```xml
<!-- Remove this line: -->
<ProjectReference Include="..\Hrot.NED\Hrot.NED.csproj" />
<!-- or the merged NED reference, whichever is currently there -->
```

Run build to confirm clean.

---

## Phase 15: Update Tests

### 15a: Update CreateEntityRequestSystemTests.cs

In `Hrot.SimHost.Tests/CreateEntityRequestSystemTests.cs`:

1. Change namespace using:
   ```csharp
   // OLD: using Hrot.SimHost.Systems;
   // NEW: using Hrot.CGF.Systems;
   using Hrot.Core.Network;   // for IEntityCreationRequestSource, EntityCreationRequest, IEntityAckSink
   ```

2. Replace `StubRequestSource : ICreateEntityRequestSource` stub class with:
   ```csharp
   internal sealed class StubEntityCreationRequestSource : IEntityCreationRequestSource
   {
       private readonly List<EntityCreationRequest> _queue = new();
       public void Enqueue(EntityCreationRequest r) => _queue.Add(r);
       public void ProcessRequests(Action<EntityCreationRequest> handler)
       {
           foreach (var r in _queue) handler(r);
           _queue.Clear();
       }
   }
   ```

3. Replace `StubAckSink : ICreateUpdateDeleteEntityAckSink` with:
   ```csharp
   internal sealed class StubEntityAckSink : IEntityAckSink
   {
       public List<(Guid RequestId, long EntityId, EntityOperationStatus Status)> Written { get; } = new();
       public void WriteAck(Guid requestId, long entityId, EntityOperationStatus status)
           => Written.Add((requestId, entityId, status));
   }
   ```

4. Update `MakeValidRequest()` to return `EntityCreationRequest` (neutral DTO):
   ```csharp
   private static EntityCreationRequest MakeValidRequest(long tkbType = ValidTkbType) =>
       new EntityCreationRequest
       {
           RequestId          = Guid.NewGuid(),
           OwnerAppInstanceId = LocalNodeId,
           TkbType            = tkbType,
           DisType            = ValidDisType,
       };
   ```

5. Update `BuildSystem()` to use new stub types and updated constructor:
   ```csharp
   private static (CreateEntityRequestSystem, StubEntityAckSink, StubIdAllocator)
       BuildSystem(ITkbDatabase tkb, StubEntityCreationRequestSource source)
   {
       var idAlloc = new StubIdAllocator(startId: 100);
       var ackSink = new StubEntityAckSink();
       var system  = new CreateEntityRequestSystem(
           source, ackSink, tkb, idAlloc, LocalNodeId,
           jsonAttributeCompiler: null);
       return (system, ackSink, idAlloc);
   }
   ```

6. Remove `using Hrot.NED.Messages;`, `using Hrot.NED.Descriptors;`,
   `using Hrot.NED.Common;` if no longer needed.

7. Update assertions that check `ackSink.WrittenAcks[0].StatusCode` to use
   `ackSink.Written[0].Status` (an `EntityOperationStatus` enum value).

### 15b: Fix DeleteEntityRequestSystemTests (if it exists)

Apply the same pattern: replace NED-typed stubs with neutral-typed stubs.

### 15c: Fix SstRequestFinalizationSystemTests (if it exists)

Update `NedRequestFinalizationSystem` constructor calls to use `IEntityAckSink` stub.
Update assertions to use `EntityOperationStatus` values.

### 15d: Fix SimHostVisualizationTests (if they test HandleRightClickForEntity)

If the method signature changed, update test calls and create a `StubMissionSender`:
```csharp
internal sealed class StubMissionSender : ISimHostMissionSender
{
    public record Invocation(long EntityId, Vector2 Dest, float Speed, float ArrivalRadius);
    public List<Invocation> Calls { get; } = new();
    public void SendNavigateToPoint(long id, Vector2 dest, float speed, float radius)
        => Calls.Add(new Invocation(id, dest, speed, radius));
    public void Dispose() { }
}
```

---

## Phase 16: Final Build and Test Pass

```powershell
# Build
dotnet build IOS-IG-SimHost.sln -v quiet

# Test SimHost
dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build -v quiet

# Test CGF (if tests exist)
# dotnet test Hrot.CGF.Tests/...

# Integration tests
dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-build -v quiet
```

Zero build errors and zero test failures are the acceptance criteria.

---

## Key Notes

- **Do NOT implement `CreateSimHostNetworkAdapter()`** anywhere. The method no longer
  exists in `INetworkFactory` after Phase 1.
- **Do NOT add entity lifecycle handling to SimHost** -- SimHostApp.cs must not create
  `CreateEntityRequestSource`, `DeleteEntityRequestSource`, or `AckSink` instances.
- **`CgfSubsystem.Initialize()` is the correct location** for entity lifecycle handling,
  with `isDefaultProcessor: true`. Do not break this.
- **Simple JSON approach**: the `NedEntityCreationRequestSource` extracts only TkbType +
  DisType from EntityMaster descriptor and passes `InitialAttributesJson` through unchanged.
  No deep descriptor translation is needed.
- `SimHostModule` after Phase 5 only handles spawning + optional egress translators.
  It has no creation/deletion request fields.
- If a file has both muscle-role translators (perception, kinematics) AND NED imports,
  move only the NED-dependent translators; leave pure FDP/ECS translators in SimHost.
