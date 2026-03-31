
drag & drop of entities is now using ghost target position and the geospatil descriptor update request is sent only after a drop the ghost. How to add an option to send update during the dragging to test various latencies? sometime this hapend unintentionally - instead of dragging a ghost the whole entity gets dragged immediately.

The reason you sometimes see the whole entity dragged immediately instead of a ghost is due to a difference between how the IG (Image Generator) and the SimHost handle interactions.

If you accidentally perform the drag-and-drop inside the **SimHost** debug window, the `SimHostVisualization` directly mutates the authoritative `SimTransform` component every single frame during the drag. Because SimHost is the authority, its `WorldPosEgressTranslator` instantly detects this mutation and broadcasts the new position over DDS. This forces all connected IGs to update in real-time, completely bypassing the ghost-preview mechanism.

To intentionally enable this "Continuous Backbone" drag mode on the IG to test latencies, you need to bridge the `OnEntityMoved` event to your DDS gateway, but you must be careful.

Here is the pragmatic way to implement this toggle without breaking the architecture.

1\. Add the Configuration Toggle

First, add a toggle to `MapUserConfig` so you can switch this behavior on and off via the Debug Panel.

```
// In Hrot.IG.Components.MapUserConfig
public bool ContinuousDragUpdates { get; set; }
```

2\. Refactor the Egress Logic

Currently, `IgApplication.cs` bakes the DDS request creation directly into `OnEntityDragEnded`. Extract that into a reusable helper method so both the drag-update and drag-end events can use it.

```
// In IgApplication.cs
private void SendWorldPosUpdate(Entity entity, Vector2 worldPos)
{
    if (!_networkEnabled || _commandGateway == null || _geoTransform == null) return;
    
    var view = (ISimulationView)_world;
    if (!view.HasComponent<NetworkIdentity>(entity)) return;

    long netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
    var (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(worldPos.X, worldPos.Y, 0f));

    var request = new UpdateEntityDescriptorRequest
    {
        RequestId      = Guid.NewGuid(),
        EntityId       = (int)netId,
        DescriptorType = EDescriptorType.dtWorldPos,
        Payload        = new EntityDescriptorUnion
        {
            _d         = EDescriptorType.dtWorldPos,
            WorldPos = new WorldPos
            {
                EntityId = (int)netId,
                Time     = DateTime.UtcNow,
                Pos      = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt },
                Rot      = new EulerOri() // Assuming orientation remains unchanged
            }
        }
    };

    _commandGateway.SendUpdateDescriptor(request);
}
```

3\. Wire the Continuous Update with Throttling

In `IgApplication.cs`, update the `interactionTool.OnEntityMoved` subscription.

**Architectural Warning:** `UpdateEntityDescriptorRequest` uses `Reliable` DDS QoS. If you blindly broadcast this at 60 Hz during a mouse drag, you will flood the reliable queue, cause TCP-like backpressure, and artificially induce massive latency. You must pragmatically throttle it (e.g., to 10 Hz) during the drag.

```
// In IgApplication.cs, add a throttle tracker
private float _continuousDragTimer = 0f;

// Replace the existing OnEntityMoved subscription:
interactionTool.OnEntityMoved += (entity, worldPos) => 
{
    _lastDragWorldPos = worldPos;
    
    if (_userConfig.ContinuousDragUpdates)
    {
        _continuousDragTimer += Raylib.GetFrameTime();
        if (_continuousDragTimer > 0.1f) // Throttle to 10 Hz
        {
            SendWorldPosUpdate(entity, worldPos);
            _continuousDragTimer = 0f;
        }
    }
};

// Simplify OnEntityDragEnded to use the new helper
private void OnEntityDragEnded(Entity entity)
{
    if (_lastDragWorldPos != default)
    {
        SendWorldPosUpdate(entity, _lastDragWorldPos);
        _lastDragWorldPos = default;
        _continuousDragTimer = 0f;
    }
}
```

This properly establishes the "Continuous Backbone" mode documented in your architecture. When enabled, the IG will send throttled descriptor updates during the drag, allowing you to observe network latency as the SimHost processes the request, mutates the authoritative ECS state, and egresses the `WorldPos` confirmation back to the IGs.

the IOS should generate the entity delete item to the context menu item for any clicked entity - how?

To expose the "Delete" action in the context menu for any clicked entity, you must modify the menu generation pipeline in `Hrot.ExCon/Logic/ContextMenuLogic.cs`.

Currently, the IOS architecture uses a `MenuStrategy` pattern to populate context menus, and the delete action (`ContextMenuActions.Delete`, mapping to ID `10`) is strictly isolated to the `MenuStrategy.Admin` role.

To pragmatically make this action available regardless of the active strategy, you should append it universally to the constructed menu items. In the `BuildEntityMenu` method, just after the strategy-specific switch statement and the dynamic "Edit Drawing" addition, insert the delete item before returning:

```
if (entity != null)
{
    items.Add(new ContextMenuItem
    {
        Id    = ContextMenuActions.Delete,
        Label = "Delete",
        Icon  = "delete",
        Style = "destructive"
    });
}
```

By adding it here, the IOS will serialize this definition into the `ContextActionsUpdate.MenuDefinitionJson` payload and push it to the IG whenever the selection changes. The IG caches this JSON and renders the zero-latency popup natively when the operator right-clicks.

Architecturally, when the operator clicks "Delete", the IG will broadcast a `ContextActionInvoked` DDS message back to the IOS containing `ActionId = 10`. Your IOS application shell must then consume this event via the `IContextMenuLogic.ActionInvoked` event and issue the corresponding network teardown command. In the BDC SST architecture, this means either publishing a local destruction command or disposing of the entity's `EntityMaster` descriptor over DDS, as `EntityMaster` holds absolute authority over the entity's existence across the network.

for an update entity descriptor request i am getting 2 identical update entity descriptor acks . I am running IOS, IOS, SimHost standalone. Shouldn't the ack come just from the owner

{ "RequestId": "2d6674b4-7cb4-4464-8a76-d4bf4e25f86d", "EntityId": 1, "DescriptorType": "dtMapVisualOverlay", "PartId": 0, "CurrentVersion": 0, "Payload": { "\_d": "dtMapVisualOverlay", "MapVisualOverlay": { "EntityId": 1, "PersistenceMode": "MODE\_PERSISTENT", "BirthTimestamp": 0, "AutoDeleteTimeoutSeconds": 0, "StylePresetName": "", "StyleOverrideJson": "", "Points": \[ { "Latitude": -0.0008516148318236105, "Longitude": -0.002600111281875428, "Altitude": 0.011377780698239803 }, { "Latitude": 0.0014759804583590608, "Longitude": -0.0014071724400217533, "Altitude": 0.011788895353674889 }, { "Latitude": 0.0015390105218173744, "Longitude": 0.0012594773067622356, "Altitude": 0.0015206513926386833 }, { "Latitude": 0.00010121133353635514, "Longitude": 0.0031011694227238706, "Altitude": -0.0084560327231884 } \], "IsPartialUpdate": false, "ChangedIndices": \[\], "IsEditable": true, "IsClickable": true } } }

\-------ack ---------

{ "RequestId": "2d6674b4-7cb4-4464-8a76-d4bf4e25f86d", "EntityId": 1, "ErrorCode": 0 }

Yes, you are absolutely right. The acknowledgment must only come from the authoritative owner.

Currently, `UpdateEntityDescriptorRequestSystem` has an architectural flaw. It violates the "Silent Bystander" rule that we correctly established in `UpdateEntityAttributeRequestSystem`. Its class documentation explicitly (and incorrectly) dictates that an ACK is written "regardless of outcome". This forces non-authoritative nodes to spam the network with `NotOwner` or `EntityNotFound` responses.

However, the payload you provided shows `ErrorCode: 0` (Success). If you are receiving two identical `Success` ACKs, it means two separate processes received the request and **both** evaluated `HasAuthority == true`. Because you are testing in standalone mode, you almost certainly have a hidden/zombie `SimHost` process still running in the background. Since all SimHost instances currently hardcode their identity to `SimHostNetworkConstants.LocalNodeId = 1`, both running processes believe they are the primary owner, apply the modification to their local ECS, and broadcast a `Success` ACK.

To fix the pipeline, first kill your zombie SimHost process. Then, you must refactor `UpdateEntityDescriptorRequestSystem.cs` to strictly enforce the Silent Bystander rule by removing the failure ACKs.

1\. Fix `ProcessRequest`

Do not ACK if the node has not yet discovered the entity, or if the descriptor type is unsupported.

```
private void ProcessRequest(UpdateEntityDescriptorRequest req)
{
    if (!_entityMap.TryGetEntity(req.EntityId, out var entity))
    {
        // SILENT BYSTANDER: Drop quietly.
        // REMOVE: WriteAck(req.RequestId, req.EntityId, SstErrorCode.EntityNotFound); [3]
        return;
    }

    switch (req.DescriptorType)
    {
        // ... (existing cases)
        default:
            FdpLog<UpdateEntityDescriptorRequestSystem>.Debug(
                "[UpdDescReq] Ignoring unsupported DescriptorType {0} for Entity {1}.",
                req.DescriptorType, req.EntityId);
            // SILENT BYSTANDER: Drop quietly.
            // REMOVE: WriteAck(req.RequestId, req.EntityId, SstErrorCode.NotSupported); [4]
            return;
    }
}
```

2\. Fix `ProcessMapVisualOverlayUpdate`

Do not ACK if the node is tracking the entity but does not have network authority over the specific descriptor.

```
private void ProcessMapVisualOverlayUpdate(UpdateEntityDescriptorRequest req, Entity entity)
{
    var view = (ISimulationView)World;

    if (!view.HasAuthority(entity, MapVisualOverlayOrdinal))
    {
        FdpLog<UpdateEntityDescriptorRequestSystem>.Debug(
            "[UpdDescReq] Not authoritative for MapVisualOverlay on Entity {0}. Ignoring.",
            req.EntityId);
        
        // SILENT BYSTANDER: Drop quietly.
        // REMOVE: WriteAck(req.RequestId, req.EntityId, SstErrorCode.NotOwner); [5, 6]
        return;
    }
    
    // ... (existing logic)
    
    WriteAck(req.RequestId, req.EntityId, SstErrorCode.Success);
}
```

3\. Fix `ProcessWorldPosUpdate`

Apply the exact same silent bypass for positional updates.

```
private void ProcessWorldPosUpdate(UpdateEntityDescriptorRequest req, Entity entity)
{
    var view = (ISimulationView)World;
    if (!view.HasAuthority(entity, WorldPosOrdinal))
    {
        FdpLog<UpdateEntityDescriptorRequestSystem>.Debug(
            "[UpdDescReq] Not authoritative for WorldPos on Entity {0}. Ignoring.",
            req.EntityId);
            
        // SILENT BYSTANDER: Drop quietly.
        // REMOVE: WriteAck(req.RequestId, req.EntityId, SstErrorCode.NotOwner); [4, 7]
        return;
    }

    // ... (existing logic)
    
    WriteAck(req.RequestId, req.EntityId, SstErrorCode.Success);
}
```

By removing these anti-pattern ACKs, the system correctly delegates authority. Only the true owner of the descriptor will emit the confirmation, and the IOS `RequestTransactionManager` will receive exactly one correlation reply.

i need to be able in hrot.runner to specify the node id for each subsystem - how? some command option?

Currently, the architecture hardcodes the node identifiers using static constants like `SimHostNetworkConstants.LocalNodeId = 1` and `IgNetworkConstants.InstanceId = 300`. This is an anti-pattern for a truly scalable federated simulation where you might want to run multiple instances of the same subsystem type.

To pragmatically expose this as a command-line option in `Hrot.ClusterRunner`, you must thread a new `--node-id` argument through the FDP Runner framework down to the subsystem initialization. Because `Hrot.ClusterRunner` can host multiple subsystems in a single process (`-m all`), you should treat the CLI argument as a "base" ID and apply deterministic offsets per subsystem type to prevent DDS participant/instance collisions.

Here is the architectural path to implement this:

1\. Extend the Runner Configuration Pipeline

First, add the CLI argument to the base configuration and options classes.

In **FDP.Framework.Runner/RunnerConfiguration.cs**:

```
[Option('n', "node-id", Default = 0, HelpText = "Base Node ID for the subsystems")]
public int NodeId { get; set; }
```

In **FDP.Framework.Runner/RunnerOptions.cs**:

```
public int NodeId { get; set; }
```

In **Hrot.ClusterRunner/Program.cs**: When building the `RunnerOptions`, pass the parsed value:

```
var runnerOptions = new RunnerOptions
{
    Headless          = headless,
    DomainId          = opts.DomainId,
    NodeId            = opts.NodeId, // Inject the parsed CLI value
    Deterministic     = deterministic,
    FixedDeltaSeconds = opts.FixedDeltaSeconds
};
```

In **FDP.Framework.Runner/SubsystemConfig.cs**: Add the property so individual subsystems can consume it during initialization:

```
public int NodeId { get; set; }
```

2\. Apply Offsets in the Orchestrator

When running `-m all`, you cannot assign the same Node ID to SimHost, IG, and IOS, as they require distinct identities in the `NodeIdMapper`.

Update **FDP.Framework.Runner/SubsystemOrchestrator.cs** to assign distinct IDs when bootstrapping the subsystems:

```
// Inside Initialize()
foreach (var subsystem in _subsystems)
{
    // Apply standard offsets to the base NodeId to ensure uniqueness
    int resolvedNodeId = _options.NodeId;
    if (resolvedNodeId == 0)
    {
        // Fallback to legacy defaults if not specified
        resolvedNodeId = subsystem.Name switch {
            "SimHost" => 1,
            "IG" => 300,
            "IOS" => 500,
            _ => 1000
        };
    }
    else
    {
        // Apply deterministic offsets based on the base ID
        resolvedNodeId += subsystem.Name switch {
            "SimHost" => 0,
            "IG" => 100,
            "IOS" => 200,
            _ => 300
        };
    }

    var cfg = new SubsystemConfig
    {
        DomainId          = _domainId,
        NodeId            = resolvedNodeId, // Pass the resolved ID
        Headless          = _headless,
        OwnWindow         = false,
        SubsystemName     = subsystem.Name,
        Deterministic     = _deterministic,
        FixedDeltaSeconds = _fixedDeltaSeconds
    };
    subsystem.Initialize(cfg);
}
```

3\. Consume the ID in the Subsystems

Finally, you must refactor the concrete applications to accept this injected ID instead of relying on their static constants.

For **SimHost**, update `SimHostSubsystem.cs` to pass the ID into `SimHostApp`:

```
public void Initialize(SubsystemConfig config)
{
    _headless = config.Headless;
    int? domainOverride = config.DomainId > 0 ? config.DomainId : (int?)null;
    
    // Modify SimHostApp constructor to accept the nodeId
    _app = new SimHostApp(domainOverride, nodeIdOverride: config.NodeId); 
    _app.InitializeEmbedded(headless: config.Headless, domainIdOverride: domainOverride);
}
```

Inside `SimHostApp.OnLoad`, replace usages of `SimHostNetworkConstants.LocalNodeId` with the injected `_nodeIdOverride`.

Apply the exact same pattern to `IgSubsystem.cs` and `IgApplication.cs`, replacing `IgNetworkConstants.InstanceId` with the dynamically provided value. Ensure you pass the dynamic ID into the `NodeIdMapper` so local DDS network identities resolve correctly.

does this handle situation when I start multiple IGs or IOSes?

Yes, but only if you launch them as separate, standalone processes.

Here is the objective breakdown of how the architecture currently handles this:

1\. Single-Process Limitation

You cannot start multiple IGs or IOSes within the _same_ `Hrot.ClusterRunner` process (e.g., `-m ig,ig`). The engine's `RunMode` is strictly implemented as a `[Flags]` enum, and the CLI parsing logic uses a bitwise-OR operation to resolve the string.

If you pass `-m ig,ig`, it mathematically evaluates to a single `RunMode.IG` flag. Consequently, the runner will only instantiate one `IgSubsystem` and one `MapCanvas` for that process.

2\. Multi-Process Execution (The Solution)

To run multiple IGs or IOSes, you must launch multiple instances of the runner executable in parallel and explicitly provide a unique `--node-id` to each.

For example, to spin up two Image Generators, you execute:

-   `Hrot.ClusterRunner.exe -m ig --node-id 300`-   `Hrot.ClusterRunner.exe -m ig --node-id 301`

Because of the CLI pass-through we established, the `SubsystemOrchestrator` will take that base ID and apply its deterministic offset (e.g., `+100` for IG). The first IG instance resolves to an internal Node ID of `400`, and the second resolves to `401`.

This effectively isolates them on the network. The dynamically injected Node ID flows directly into the subsystem's `NodeIdMapper` and `DdsIdAllocator` during initialization, preventing DDS participant collisions and ensuring both IGs can subscribe to and publish independent `MapCommandRequest` or `MapClickEvent` topics safely.

whan i run the 3 separate apps using bat script

set DOMAIN=0

## SET RUNNER=Hrot.ClusterRunner\\bin\\Debug\\net8.0\\Hrot.ClusterRunner.exe -d %DOMAIN%

start "SimHost" %RUNNER% -m simhost --no-wait start "IG" %RUNNER% -m ig --no-wait start "IOS" %RUNNER% -m ios --no-wait

* * *

all runs but the SimHost NEVER receives dds messages.

## If i run simhost via scriopt

set DOMAIN=0

SET RUNNER=Hrot.ClusterRunner\\bin\\Debug\\net8.0\\Hrot.ClusterRunner.exe -d %DOMAIN%

## start "SimHost" %RUNNER% -m simhost --no-wait

is also does not receive messages

## but if i run SimHost from visual studio using launch config

```
"SimHost": {
  "commandName": "Project",
  "commandLineArgs": "-m simhost --no-wait"
},
```

* * *

then simhost received dds messages

how comes???

This happens due to a combination of an invalid zero-check on the DDS domain ID and a working directory mismatch. Your batch-started `SimHost` is silently isolating itself on DDS Domain 42, while your `IG` and `IOS` are correctly starting on Domain 0.

Here is the objective breakdown of the failure chain:

1\. The Domain Override Bug

In `Hrot.ClusterRunner/Services/SimHostSubsystem.cs`, you are extracting the CLI domain argument with this logic:

```
int? domainOverride = config.DomainId > 0 ? config.DomainId : (int?)null;
```

Because your batch script explicitly sets `DOMAIN=0`, the expression `0 > 0` evaluates to `false`. The `domainOverride` becomes `null`, forcing `SimHostApp` to fall back to its local `config.json`.

2\. The Working Directory Mismatch

When `SimHostApp` attempts to load `config.json`, it looks in the Current Working Directory (CWD).

-   **When running via Visual Studio (****commandName: Project****):** The CWD is automatically set to the project output directory (`Hrot.ClusterRunner/bin/Debug/net8.0`). It finds `config.json`, reads `"DdsDomainId": 0`, and successfully joins Domain 0.-   **When running via the batch script:** The CWD is your solution root. It fails to find `config.json` there and falls back to the hardcoded defaults in `NodeConfiguration.cs`, where `DdsDomainId` defaults to `42`.

Meanwhile, `IG` and `IOS` do not have this `> 0` guard logic. They accept `DomainId = 0` directly from the runner config and join Domain 0.

The Fix

**Fix 1: Correct the architectural bug in SimHostSubsystem** Zero is a perfectly valid (and in your case, default) DDS Domain ID. You should not discard it. Update `SimHostSubsystem.cs` to pass the domain directly:

```
public void Initialize(SubsystemConfig config)
{
    _headless = config.Headless;
    
    // Pass the domain ID directly. If you need a sentinel for "unspecified", 
    // RunnerConfiguration should use a nullable int or a negative default.
    int? domainOverride = config.DomainId; 
    
    _app = new SimHostApp(domainOverride);
    _app.InitializeEmbedded(headless: config.Headless, domainIdOverride: domainOverride);
}
```

**Fix 2: Set the Working Directory in the Batch Script** To ensure all subsystems reliably find their assets (like `config.json` and `sample_road.json`), your batch script should execute from the output directory, similar to how the engine's demo scripts do it:

```
set DOMAIN=0

:: Change working directory to the compiled output folder
cd /d "%~dp0Hrot.ClusterRunner\bin\Debug\net8.0"
SET RUNNER=Hrot.ClusterRunner.exe

start "SimHost" %RUNNER% -d %DOMAIN% -m simhost --no-wait
start "IG"      %RUNNER% -d %DOMAIN% -m ig --no-wait
start "IOS"     %RUNNER% -d %DOMAIN% -m ios --no-wait
```


When entity is deleted, just the master descriptor DDS topic sample is sent as disposed. The other descriptors remains active. How to delete also the others? Utilizing some kind od entity lifecycle management infrastructure?

According to the BDC SST architectural principles, you actually do not need to explicitly dispose of the other descriptor topics over the network. The existence of an entity is strictly dictated by the `EntityMaster` descriptor. The architectural contract explicitly states that once the `EntityMaster` is removed, the entity instance ceases to exist "no matter if its descriptors still exist".

However, to ensure that the _local_ simulation nodes cleanly tear down the entity's memory, dependencies, and ECS state, the engine utilizes the **Entity Lifecycle Module (ELM)**. Here is how that teardown infrastructure works:

-   **Trigger:** When the `EntityMaster` disposal is received from the network, the `EntityMasterIngressTranslator` detects the `NotAliveDisposed` instance state and publishes a `DestroyEntityCommand` to the local FDP event bus.-   **Teardown Initiation:** The `NetworkSpawningSystem` consumes this command, changes the ECS entity's state to `EntityLifecycle.TearDown`, and invokes `_elm.BeginDestruction()`.-   **Handshake:** ELM broadcasts a `DestructionOrder` event. Any registered sub-modules or systems that hold complex state (like physics constraints, UI representations, or parent-child hierarchy maps) can observe this order, clean up their internal resources, and reply with a `DestructionAck`.-   **Final Deletion:** Once all expected ACKs are collected, ELM safely invokes `cmd.DestroyEntity()` to wipe the entity from the ECS completely.

If you have a strict, pragmatic requirement to forcefully broadcast DDS disposal samples for _all_ individual descriptors (for example, to purge DDS middleware caches aggressively on late-joining nodes), you can achieve this by extending the `CycloneNetworkCleanupSystem`.

Currently, `CycloneNetworkCleanupSystem` is typically injected with only a single `IDescriptorTranslator` (the `EntityMasterEgressTranslator`) and calls its `Dispose(netId)` method when it detects that a locally-owned entity has died. You can refactor this system to accept an array of all registered egress translators:

```
// Inside CycloneNetworkCleanupSystem
foreach (var translator in _allEgressTranslators)
{
    try
    {
        translator.Dispose(netId);
    }
    catch (Exception ex)
    {
        FdpLog<CycloneNetworkCleanupSystem>.Error("Failed to dispose {0}: {1}", netId, ex.Message);
    }
}
```

Since all high-performance translators in the engine (such as `WorldPosEgressTranslator`, `AutoCycloneTranslator`, and `MultiInstanceCycloneTranslator`) already implement `Dispose(long networkEntityId)` to correctly patch the key and call `Writer.DisposeInstance`, iterating through them in your cleanup system will successfully broadcast the DDS disposal for every active topic.

I know that BDC does not need that, but i want to delete them to avoid sending thousands of samples of INVALID entities to late joiners who would treat those as ghost descriptors and will wait for a while for the master descriptor.

Your reasoning is completely justified. While the theoretical BDC architecture dictates that existence is tied strictly to `EntityMaster`, the reality of DDS middleware is that `TransientLocal` durability queues will aggressively cache those orphaned descriptor samples. If you do not actively dispose of them, a late-joining IG will download thousands of stale `WorldPos` or `EntityInfo` samples, reconstruct partial ghosts, and needlessly burn CPU/memory waiting for a master descriptor that will never arrive.

To purge the DDS middleware caches pragmatically, you must refactor `CycloneNetworkCleanupSystem` to fan out the disposal command to every registered egress translator.

Fortunately, the core architecture is already prepared for this. All of your egress translators (like `EntityInfoEgressTranslator`, `MapVisualOverlayEgressTranslator`, and the base `CycloneTranslator`) already correctly implement the `Dispose(long networkEntityId)` contract by calling `_writer.DisposeInstance()`.

Here is how you implement the fan-out.

1\. Refactor the Cleanup System

Modify `ModuleHost.Network.Cyclone.Systems.CycloneNetworkCleanupSystem` to accept a collection of translators rather than a single master translator.

```
using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Fdp.Interfaces;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;

namespace ModuleHost.Network.Cyclone.Systems
{
    [UpdateInPhase(SystemPhase.Export)]
    public class CycloneNetworkCleanupSystem : IModuleSystem
    {
        private readonly IDescriptorTranslator[] _translators;
        private readonly Dictionary<long, Entity> _trackedEntities = new();

        public CycloneNetworkCleanupSystem(IEnumerable<IDescriptorTranslator> translators)
        {
            _translators = translators.ToArray();
        }

        public void Execute(ISimulationView view, float dt)
        {
            // 1. Scan for new entities to track...
            // [Keep your existing tracking logic]
            var query = view.Query()
                .WithLifecycle(EntityLifecycle.All)
                .With<NetworkIdentity>()
                .With<NetworkOwnership>()
                .Build();

            foreach (var entity in query)
            {
                ref readonly var ownership = ref view.GetComponentRO<NetworkOwnership>(entity);
                if (!ownership.HasAuthority) continue;

                ref readonly var identity = ref view.GetComponentRO<NetworkIdentity>(entity);
                if (!_trackedEntities.ContainsKey(identity.Value))
                    _trackedEntities[identity.Value] = entity;
            }

            // 2. Scan tracked entities for deleted ones
            List<long>? toRemove = null;
            foreach (var kvp in _trackedEntities)
            {
                if (!view.IsAlive(kvp.Value))
                {
                    toRemove ??= new List<long>();
                    toRemove.Add(kvp.Key);
                }
            }

            if (toRemove != null)
            {
                foreach (var netId in toRemove)
                {
                    FdpLog<CycloneNetworkCleanupSystem>.Info(
                        "Detected entity destruction {0}, broadcasting dispose to all topics.", netId);

                    // FIX: Fan out the disposal to every registered topic
                    foreach (var translator in _translators)
                    {
                        try
                        {
                            translator.Dispose(netId);
                        }
                        catch (Exception ex)
                        {
                            FdpLog<CycloneNetworkCleanupSystem>.Error(
                                "Failed to dispose entity {0} on topic {1}: {2}",
                                netId, translator.TopicName, ex.Message);
                        }
                    }
                    
                    _trackedEntities.Remove(netId);
                }
            }
        }
    }
}
```

2\. Update the Registration

You must now inject the full array of active translators into the cleanup system when bootstrapping the node.

If you are using the `NetworkCleanupModule` wrapper, update its constructor:

```
public class NetworkCleanupModule : IModule
{
    public string Name => "NetworkCleanup";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly CycloneNetworkCleanupSystem _system;

    // Accept the full list instead of just masterEgressTranslator
    public NetworkCleanupModule(IEnumerable<IDescriptorTranslator> allTranslators)
    {
        _system = new CycloneNetworkCleanupSystem(allTranslators);
    }

    public void RegisterSystems(ISystemRegistry registry)
        => registry.RegisterSystem(_system);

    public void Tick(ISimulationView view, float dt) { }
}
```

Finally, locate your kernel registration (e.g., in your `NodeBootstrapper` or `SimHostApp`) where you currently instantiate the cleanup module, and pass it the aggregated `translators` list you built for the network module.

By pushing a DDS `DisposeInstance` sample for every descriptor, you forcefully tombstone the specific entity keys across all `TransientLocal` queues. When a late-joining IG connects, CycloneDDS will immediately reconcile those tombstoned instances and drop the stale memory entirely, keeping your replica networks clean.

for selected vehicle entity, in IOS mission panel i add a MoveToLocation task and then one more (2 in total, each with different target point) and then click on Commit button. The IG shows 2-waypoint path. The entity starts moving. When reaching the first waypoint it slows down and stops right before the first waypoint, never continuing to the next one (defined by next task) .snapshot of the ECS compoennts when just stopped before the first waypoint: see the "ECS json" source

Your ECS snapshot reveals exactly why this is happening. The entity physically completed the movement perfectly, but the mission logic is trapped in an infinite wait state because the UI omitted the phase advancement triggers.

If you look at the `NavState` and `LocomotionChannel` in your JSON dump, the kinematic execution was flawless. The entity successfully reached the destination (`"HasArrived": 1`) and the executor reported success (`"Status": "Success"`).

However, the `MissionPlanQueue` reveals why it refuses to advance to the next task:

```
"CurrentPhase": 0,
"Phases": [
  {
    "DoctrineId": 3001,
    "Trigger": "TimerElapsed",
    "TriggerParam": 3.4028235E+38
  },
```

The trigger to advance to phase 1 is set to `TimerElapsed` waiting for `float.MaxValue` seconds.

This originates from an incomplete UI implementation in the IOS `MissionPanel`. When you click "Add Task", the `HandleAddTask()` method initializes the new task with an empty `Triggers` list. Because the IOS UI currently lacks a panel for the operator to explicitly edit these triggers, the empty list is committed and sent to the SimHost.

On the SimHost side, when `MissionControlRequestSystem.ResolveTrigger()` receives a task with no triggers, it intentionally assigns a fallback "hold indefinitely" state: `(EcsMissionTrigger.TimerElapsed, float.MaxValue)`. Consequently, `MissionDirectorSystem` just waits forever.

The Pragmatic Fix

Since the IOS does not yet have a dedicated trigger-authoring UI, the pragmatic solution is to automatically assign a sensible default trigger when the operator selects a behavior.

Update the `HandleEditBehaviorId` method in `Hrot.ExCon/Panels/MissionPanel.cs` to automatically inject the `ReachedDestination` trigger for spatial behaviors:

```
public void HandleEditBehaviorId(int index, string behaviorId)
{
    if (!TryGetDraftTasks(out var tasks)) return;
    if (index < 0 || index >= tasks.Count) return;

    var task = tasks[index];
    task.BehaviorId = behaviorId ?? string.Empty;

    // FIX: Auto-assign the appropriate trigger so the phase advances naturally
    if (behaviorId == BehaviorNameMoveToLocation || 
        behaviorId == BehaviorNameFollowRoute || 
        behaviorId == BehaviorNameJoinFormation)
    {
        task.Triggers = new List<MissionTrigger> 
        { 
            new MissionTrigger { Type = "ReachedDestination" } 
        };
    }
    else
    {
        task.Triggers = new List<MissionTrigger>();
    }

    tasks[index] = task;
}
```

This change strictly mirrors how your headless `ScenarioDirector` programmatically builds its plans. With this in place, selecting `MoveToLocation` will automatically attach the `ReachedDestination` trigger to the payload. When the `NavState` registers arrival, the `MissionDirectorSystem` will instantly bump the `CurrentPhase` index and the vehicle will seamlessly continue to your second waypoint.

instead of I would like to use (or create if not yet existing) the DoctrineFinished trigger as the default trigger, intuitively sequencing the tasks

Using `DoctrineFinished` as the universal default is a much better architectural choice than hardcoding spatial triggers like `ReachedDestination`. It intuitively sequences the tasks by waiting for the behavior tree (or HSM) to naturally report a terminal state (`Success` or `Failure`), fully decoupling the mission planner from the specific locomotion mechanics of the task.

The good news is that you do not need to create this trigger; it already exists in the BDC SST data model and the FDP engine.

Here is how the backend pipeline already supports it:

-   When a behavior tree finishes, the `BTreeTickSystem` publishes a `DoctrineFinishedEvent`.-   The `MissionDirectorSystem` catches this event and natively evaluates the `MissionTrigger.DoctrineFinished` condition, advancing the `MissionPlanQueue` to the next phase.-   The `MissionControlRequestSystem` is already wired to parse the string `"DoctrineFinished"` from the DDS payload into the ECS `EcsMissionTrigger.DoctrineFinished` enum.

To implement this pragmatically, you just need to update the IOS UI to use `"DoctrineFinished"` as the default trigger when building the DDS payload.

First, if you implemented the previous workaround in `HandleEditBehaviorId`, remove it so it stops overwriting triggers. Then, update the `HandleAddTask` method in `Hrot.ExCon/Panels/MissionPanel.cs` to inject the trigger at the moment of task creation:

```
public void HandleAddTask()
{
    if (!EnsureDraftForEdit()) return;

    var tasks = GetDraftTasks();
    tasks.Add(new MissionTask
    {
        TaskId         = Guid.NewGuid(),
        ExecutingEngine = string.Empty,
        BehaviorId     = string.Empty,
        BehaviorParams = string.Empty,
        
        // FIX: Inject DoctrineFinished as the intuitive sequential default
        Triggers       = new List<MissionTrigger> 
        { 
            new MissionTrigger { Type = "DoctrineFinished" } 
        },
        
        State          = eTaskState.TASK_PLANNED
    });
}
```

By assigning this default, any new task added by the operator will automatically wait for its assigned behavior to fully conclude its logic tree before the `MissionDirectorSystem` bumps the queue to the next phase.

## when i click "ABORT" and then i click Commit to send the tasks again, i got VERSION CONFLICT although I am sending the commands from the same IOS

21:23:35.2924 | INFO | MissionPanel | \[IOS\] Commit triggered: entityId=1 taskCount=2 baseVersion=1 21:23:35.2924 | INFO | MissionEditorService | \[IOS\] CommitMissionAsync sent: entityId=1 requestId=8346b9a7-7874-4d7e-b5b5-e01cdfba7160 taskCount=2 baseVersion=1 21:23:35.3082 | INFO | MissionEditorService | \[IOS\] MissionControlAck received: requestId=8346b9a7-7874-4d7e-b5b5-e01cdfba7160 success=False errorCode=7 newVersion=0 21:23:35.3082 | WARN | MissionPanel | \[IOS\] Commit failed: entityId=1 errorCode=7 error=ERR\_VERSION\_CONFLICT

This happens because of an architectural disconnect between how control commands are dispatched and how Optimistic Concurrency Control (OCC) is tracked on the client.

When you click "ABORT", the IOS sends a `MissionControlRequest` with `CMD_ABORT_ALL`. On the SimHost, `MissionControlRequestSystem` processes this, correctly clears the mission, and increments the authoritative version number (e.g., from 1 to 2). It then broadcasts a `MissionControlAck` containing this `NewVersion`.

However, the `MissionEditorService` on the IOS currently treats `SendControlCommand` as a fire-and-forget operation. It does not track the request ID, so when the ACK arrives with the new version, the service drops it. Because the IOS UI never receives the updated version, `_draftBaseVersion` remains stale at `1`. When you subsequently click "Commit", the UI sends `BaseVersion = 1`, and the server correctly rejects it with `ERR_VERSION_CONFLICT` because the authoritative version is now `2`.

To fix this properly, you must upgrade `SendControlCommand` to an asynchronous, tracked transaction so the UI can ingest the resulting version bump.

1\. Update the Service Contract

In `Hrot.ExCon/Services/IMissionEditorService.cs`, change the signature to return a `Task<MissionCommitResult>`:

```
/// <summary>
/// Sends an imperative control command (Jump, Abort, etc.) and awaits
/// the acknowledgment to capture the updated optimistic lock version.
/// </summary>
Task<MissionCommitResult> SendControlCommandAsync(
    long entityId, eMissionCommandType type, Guid taskId);
```

2\. Implement the Tracked Command

In `Hrot.ExCon/Services/MissionEditorService.cs`, replace the existing `SendControlCommand` with the async version. This mirrors your `CommitMissionAsync` implementation, guaranteeing the ACK is captured:

```
public async Task<MissionCommitResult> SendControlCommandAsync(long entityId, eMissionCommandType type, Guid taskId)
{
    var requestId = Guid.NewGuid();
    var tcs = new TaskCompletionSource<MissionCommitResult>(
        TaskCreationOptions.RunContinuationsAsynchronously);

    lock (_pendingLock)
    {
        _pendingCommits[requestId] = tcs;
    }

    _requestWriter.Write(new MissionControlRequest
    {
        RequestId      = requestId,
        TargetEntityId = entityId,
        BaseVersion    = 0,   // Control commands don't perform version checks.
        Payload = new MissionCommandUnion
        {
            _d           = type,
            TargetTaskId = taskId
        }
    });

    using var cts = new CancellationTokenSource(_commitTimeoutMs);
    try
    {
        return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        FdpLog<MissionEditorService>.Warn("[IOS] Control command timed out: entityId={0} requestId={1}", entityId, requestId);
        return new MissionCommitResult
        {
            Success      = false,
            ErrorMessage = "Timeout"
        };
    }
}
```

3\. Route Through the UI State Machine

In `Hrot.ExCon/Panels/MissionPanel.cs`, update your button handlers to assign the returned Task to `_pendingCommit`.

Because you already have `PollCommitCompletion()` written to automatically extract `result.NewVersion` and assign it to `_draftBaseVersion`, routing the control commands through this same pipeline fixes the desync entirely.

```
public void HandleJump(IIosLogic logic)
{
    ArgumentNullException.ThrowIfNull(logic);
    if (_selectedEntityId == 0 || _commitInFlight) return;

    _pendingCommit = logic.MissionEditorService.SendControlCommandAsync(
        _selectedEntityId,
        eMissionCommandType.CMD_JUMP_TO_TASK,
        Guid.Empty);
    _commitInFlight = true;
}

public void HandleAbort(IIosLogic logic)
{
    ArgumentNullException.ThrowIfNull(logic);
    if (_selectedEntityId == 0 || _commitInFlight) return;

    _pendingCommit = logic.MissionEditorService.SendControlCommandAsync(
        _selectedEntityId,
        eMissionCommandType.CMD_ABORT_ALL,
        Guid.Empty);
    _commitInFlight = true;
}
```

Now, clicking "ABORT" locks the UI buttons, waits the few milliseconds for the SimHost to reply, and transparently updates your local `_draftBaseVersion`. A subsequent "Commit" will execute cleanly.

When in SimHost perspective, I used to see a small road network rendered. Now I see none. Why?

The road network is missing because the application is failing to locate the `Assets/sample_road.json` file on disk and is silently swallowing the resulting exception.

In `SimHostApp.cs`, the initialization pipeline attempts to load the road network data using a relative path. However, the `try-catch` block around `RoadNetworkLoader.LoadFromJson` is explicitly designed to fail silently with the comment `/* run fine without roads */`. If the file cannot be resolved, it simply leaves the `RoadNetworkBlob` empty.

Consequently, when the `SimHostRoadLayer` attempts to render the graph during the `Draw` pass, it evaluates `if (!_network.Nodes.IsCreated || !_network.Segments.IsCreated)` and immediately returns without rendering any geometry.

This is a direct symptom of the working directory mismatch we discussed earlier. If you launch the executables from a batch script located at the solution root, the relative path `Assets/sample_road.json` does not exist in that context. The `RoadNetworkLoader` throws a `FileNotFoundException`, the catch block hides it, and the application continues without roads.

To fix this, you must ensure your batch script changes the current working directory to the compiled output folder (e.g., `cd /d "%~dp0Hrot.ClusterRunner\bin\Debug\net8.0"`) before starting the processes. This will allow the engine to resolve the relative `Assets/` path correctly and populate the spatial grid for rendering.

