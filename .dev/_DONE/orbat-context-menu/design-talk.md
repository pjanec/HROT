
In the IOS orbat panel i would like to add a context menu to the entities shown there.

-   Select entity ... makes the entity selected on the IG by sending proper map command to IG
-   Center on entity ... centers the map on the entity by sending proper map command to the IG
-   Delete ... should send entity deletion request (DeleteEntityRequest) to the owner
-   Edit Route ... should tell the IG to open route authoring tool, and set the route created as the personal route of the entity
-   Abort mission ... should send mission abort request to the entity

what means exist to make it happen? (what comamnds/mechanism the IOS should use?) for example i think the IG now does not have any possibility to open route editor for a personal route, does it?

Based on the provided codebase, you have existing mechanisms to support almost all of these context menu actions out of the box. You are also entirely correct in your suspicion regarding the "Edit Route" functionality for personal routes.

Here is exactly how the IOS can implement each of these actions, and how to bridge the gap for personal routes:

1\. Select Entity

**Mechanism:** `MapCommandRequest` using `CommandType.CMD_SET_SELECTION`. The IOS can publish a `MapCommandRequest` to the IG's `MapId`. You will need to construct a JSON argument payload containing the `entityId` to be selected. The IG will intercept this and update its local `SelectionState` components.

2\. Center on Entity

**Mechanism:** `MapCommandRequest` using `CommandType.CMD_SET_VIEW`. Similar to selection, the IOS can publish a `MapCommandRequest`. You would read the entity's current `WorldPos` coordinates from the IOS `DerRepo` and pass them in the `CommandArgsJson` (e.g., `{ "lat": 45.0, "lon": 12.0, "zoom": 1000 }` as documented in the struct). The IG will consume this and move its `MapCamera`.

3\. Delete

**Mechanism:** Your newly implemented `DeleteEntityRequest`. The IOS publishes the `DeleteEntityRequest` to the DDS bus. The SimHost intercepts it, initiates the ELM teardown, and acknowledges it using the unified `CreateUpdateDeleteEntityAck`.

4\. Abort Mission

**Mechanism:** `MissionControlRequest` via `IMissionEditorService`. The IOS already has this fully wired up. When the user clicks "Abort Mission", you simply call `logic.MissionEditorService.SendControlCommandAsync(entityId, eMissionCommandType.CMD_ABORT_ALL, Guid.Empty)`. This publishes the `MissionControlRequest` with the `CMD_ABORT_ALL` payload, which the SimHost catches to clear the entity's doctrine.

5\. Edit Route (The Personal Route Challenge)

Your intuition is spot on: **the IG currently does not have a mechanism to remotely open a route authoring tool that explicitly creates a "personal route" for a specific vehicle.**

Currently, the IG can do two things:

-   **Edit an existing route:** using `CMD_START_EDITING` with an `entityId`, which opens the `RouteEditTool`.-   **Create a standalone route:** using `CMD_START_AUTHORING` with a `tkbType: 8802`, which opens the `PointSequenceTool` and eventually fires a `CreateEntityRequest` for a standard map graphic.

Meanwhile, _Personal_ routes are currently authored via a local IG shortcut (Shift+Right-Click), which fires `CmdAppendPersonalWaypoint`. The `PersonalRouteAuthoringSystem` intercepts this and locally creates a child entity with a `PersonalRouteRef` and `PartMetadata`.

**How to make it work from the IOS Context Menu:** To allow the IOS to initiate this, you will need to extend the `CMD_START_AUTHORING` pipeline on the IG:

-   **Extend the Command Args:** The IOS context menu action should send a `MapCommandRequest` with `CMD_START_AUTHORING`, but include the vehicle's network ID in the JSON: `{"contextId": "...", "tkbType": 8802, "parentEntityId": 12345}`-   **Update the IG Parser:** Modify `ParseCommandAndActivateAreaTool` in `IgApplication.cs` to extract the `parentEntityId`.-   **Pass the Parent ID to the Tool:** Update `ActivateRouteAuthoringTool` to accept the `parentEntityId`.-   **Link the Route on Creation:** When the operator finishes drawing and the `PointSequenceTool` emits the `CreateEntityRequest`, add a `PartMetadata` descriptor or a custom attribute to the `InitialDescriptors` list indicating it belongs to `parentEntityId`.-   **SimHost Linkage:** When the SimHost processes this creation request, your `CreateEntityRequestSystem` will see the parent ID and can attach the `PersonalRouteRef` component to the vehicle entity, successfully linking the new route to the vehicle's navigation brain.

shoudl CreateEntityRequestSystem know aboout how to handle routes to add personal route def? This does not sound like a good separation of concerns. is there a cleaner way for the whole route authoring?

You are absolutely right to call this out. Modifying the generic `CreateEntityRequestSystem` to understand domain-specific concepts like "personal routes" is a violation of the Single Responsibility Principle. That system is a foundational SST network plumbing layer; it should only translate DDS requests into ELM creation events, agnostic to what the entities actually mean.

The cleanest, architecturally sound way to handle this is to use the **Orchestrator Pattern on the IOS**. Since the IOS already acts as the command-and-control client, it can chain standard, decoupled network requests to achieve the desired domain state without modifying the core SimHost or IG spawning pipelines.

However, to make this work, we must first fix a latent bug in the SimHost codebase regarding how routes are followed.

1\. The SimHost Bug: `RouteEntityId` vs `TrajectoryId`

Currently, when the IOS assigns a route via the `MissionPanel`, it sends JSON like `{"routeEntityId": 123}`. However, the SimHost's `FollowRouteParams` struct expects a `TrajectoryId`.

The `TrajectoryId` is a purely local SimHost memory index generated by the `TrajectoryPoolManager`. It is not replicated over the network, so the IOS cannot possibly know it. For a decoupled flow to work, the SimHost must accept the network ID and look up the local trajectory ID internally.

**Fix** **Hrot.SimHost/Brains/SimHostNodes.cs****:**

```
// 1. Update the parameter struct to store the network ID
[StructLayout(LayoutKind.Sequential)]
public struct FollowRouteParams
{
    public long  RouteNetworkId; // Changed from TrajectoryId
    public float Speed;
    public bool  Loop;
}

// 2. Add a DTO for JSON parsing
private class FollowRouteParamsJsonDto
{
    public long routeEntityId { get; set; }
    public float Speed { get; set; }
    public bool Loop { get; set; }
}

// 3. Update the parser
public static unsafe void ParseFollowRouteParams(string json, byte* ptr)
{
    if (string.IsNullOrWhiteSpace(json))
    {
        Unsafe.Write(ptr, default(FollowRouteParams));
        return;
    }

    var dto = JsonSerializer.Deserialize<FollowRouteParamsJsonDto>(json, JsonOptions);
    var p = new FollowRouteParams
    {
        RouteNetworkId = dto?.routeEntityId ?? 0,
        Speed          = dto?.Speed ?? 15f,
        Loop           = dto?.Loop ?? false
    };
    
    Unsafe.Write(ptr, p);
}

// 4. Update the Action Delegate to resolve the local TrajectoryId
public static unsafe NodeStatus Action_WriteFollowRouteChannel(
    ref BrainBlackboard blackboard,
    ref BehaviorTreeState state,
    ref BTreeContext ctx,
    int paramIndex)
{
    if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
        return NodeStatus.Failure;

    FollowRouteParams p;
    fixed (byte* src = blackboard.Memory)
        p = Unsafe.Read<FollowRouteParams>(src);

    // Resolve RouteNetworkId -> ECS Entity -> RouteTrajectoryCache -> TrajectoryId
    int targetTrajectoryId = -1;
    var query = ctx.World.Query().With<NetworkIdentity>().With<RouteTrajectoryCache>().Build();
    foreach (var routeEntity in query)
    {
        if (ctx.World.GetComponentRO<NetworkIdentity>(routeEntity).Value == p.RouteNetworkId)
        {
            targetTrajectoryId = ctx.World.GetComponentRO<RouteTrajectoryCache>(routeEntity).TrajectoryId;
            break;
        }
    }

    // Fail if the route doesn't exist or hasn't compiled a trajectory yet
    if (targetTrajectoryId <= 0) return NodeStatus.Failure;

    ref var channel = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
    if (ctx.World.HasComponent<DoctrineState>(ctx.Self))
    {
        var doctrine = ctx.World.GetComponent<DoctrineState>(ctx.Self);
        channel.DoctrineInstanceId = doctrine.InstanceId;
    }

    bool needsActivation = channel.ActiveAction != NavigationConstants.ActionIdFollowRoute
        || channel.Status == NodeStatus.Failure;
    if (needsActivation)
        unchecked { channel.ActionInstanceId++; }

    channel.ActiveAction = NavigationConstants.ActionIdFollowRoute;

    // Write the resolved TrajectoryId into the core navigation struct
    var route = new FDP.Toolkit.Navigation.FollowRouteParams
    {
        TrajectoryId = targetTrajectoryId,
        IsLooped     = (byte)(p.Loop ? 1 : 0)
    };
    
    fixed (byte* dst = channel.Params)
        Unsafe.Write(dst, route);

    return NodeStatus.Running;
}
```

2\. The Clean Workflow: IOS Orchestration

With the SimHost now correctly accepting network IDs, we can implement "Personal Routes" purely via the IOS application layer using standard SST contracts.

Instead of hacking `CreateEntityRequestSystem`, the IOS acts as the workflow orchestrator:

-   **The Request:** The user clicks "Draw Route" for Vehicle A. The IOS generates a `ContextId`, saves it to an internal dictionary (`_pendingPersonalRoutes[ContextId] = VehicleId`), and sends a standard `CMD_START_AUTHORING` command to the IG for a route.-   **The Execution:** The IG operator draws the route normally. The IG publishes a generic `CreateEntityRequest`. The SimHost spawns the route and broadcasts a `CreateUpdateDeleteEntityAck`. Both are completely unaware of the "personal" context.-   **The Link (IOS):** The IOS receives the ACK. It checks if the `ContextId` matches an entry in `_pendingPersonalRoutes`. Finding it, the IOS now has the newly minted `RouteEntityId`.-   **The Finalization:** The IOS immediately fires two standard requests:
    -   An `UpdateEntityDescriptorRequest` (EntityInfo) on the Route to set `CommanderId = Vehicle A`. This makes it formally a child of the vehicle in the ORBAT tree.-   A `MissionControlRequest` targeting Vehicle A, assigning a `FollowRoute` mission task pointing to the new `RouteEntityId`.

**Implementing this in** **IosLogic.cs****:**

```
// 1. Track the intent
private readonly Dictionary<Guid, int> _pendingPersonalRoutes = new();

public void StartPersonalRouteAuthoring(int vehicleEntityId)
{
    ActiveContextId = Guid.NewGuid();
    _pendingPersonalRoutes[ActiveContextId] = vehicleEntityId;
    
    // Tell IG to draw a route using the standard tool
    string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new {
        contextId = ActiveContextId.ToString("N"),
        tkbType = Hrot.Map.Common.TkbEntityTypes.TacGraphic_Route 
    });

    _commandWriter.Write(new MapCommandRequest {
        RequestId = Guid.NewGuid(),
        MapId = _targetMapId,
        Type = CommandType.CMD_START_AUTHORING,
        CommandArgsJson = argsJson,
    });
}

// 2. Intercept the creation ACK
private void ProcessEntityCreationAcks()
{
    while (_createEntityAckQueue.TryDequeue(out var ack))
    {
        // ... normal transaction completion ...

        // Check if this was a personal route
        if (ack.StatusCode == 0 && _pendingPersonalRoutes.TryGetValue(ActiveContextId, out int vehicleId))
        {
            _pendingPersonalRoutes.Remove(ActiveContextId);
            
            // Link 1: Assign it to the vehicle's mission
            AssignRouteToVehicle(vehicleId, ack.EntityId);
            
            // Link 2: Update ORBAT hierarchy
            UpdateRouteHierarchy(ack.EntityId, vehicleId);
        }
    }
}
```

This design keeps the core generic, treats routes as first-class standard entities, and isolates the complexity of multi-step workflows to the UI/orchestration layer where it belongs.

i do not want the IOS do do the orchestration. IOS should stay 'dumb' and just request simple well framed operation. The IG needs to perform this irchestration on its own.

You are absolutely right. Pushing complex orchestration into the IOS violates the thin-client principle. The IOS should remain a stateless "dumb" terminal that simply declares user intent, while the IG—which already possesses the spatial context, the tool execution loop, and the asynchronous command gateway—handles the heavy lifting.

This is a much cleaner architecture. In fact, your IG already contains the perfect utility for this: **BdcCommandGateway**. We can use its `async/await` methods (`CreateEntityAsync` and `SendMissionControlRequestAsync`) to effortlessly chain the requests on the IG side, exactly as you already do in `MiniIosPanelState.SubmitWithWanderMissionViaGateway`.

Here is the clean, IG-orchestrated implementation.

1\. Extend the Map Command Contract

First, give the IOS a precise, well-framed command so it doesn't have to piggyback on generic authoring. In `Hrot.NED/MapMessages.cs`, add a new command type:

```
public enum CommandType
{
    CMD_SET_VIEW,
    CMD_SET_SELECTION,
    CMD_START_EDITING,
    CMD_PLACE_ENTITY,
    CMD_START_AUTHORING,
    CMD_PICK_LOCATION,
    CMD_PICK_ENTITY,
    
    /// <summary>
    /// Activates route authoring and automatically assigns it to the target entity.
    /// Args JSON: { "contextId": "<guid>", "entityId": 12345 }
    /// </summary>
    CMD_DRAW_PERSONAL_ROUTE 
}
```

2\. The "Dumb" IOS Implementation

The IOS does no tracking, no queueing, and no multi-step logic. It simply fires the command when the context menu item is clicked.

In `IosLogic.cs`:

```
public void StartPersonalRouteAuthoring(int vehicleEntityId)
{
    ActiveContextId = Guid.NewGuid();
    
    string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new {
        contextId = ActiveContextId.ToString("N"),
        entityId = vehicleEntityId
    });

    _commandWriter.Write(new MapCommandRequest {
        RequestId = Guid.NewGuid(),
        MapId = _targetMapId,
        Type = CommandType.CMD_DRAW_PERSONAL_ROUTE,
        CommandArgsJson = argsJson,
    });
}
```

3\. The IG Orchestration (The Smart Client)

On the IG, we catch the command, activate the standard `PointSequenceTool`, and use the `BdcCommandGateway` to seamlessly `await` the route creation before assigning the mission.

In **Hrot.IG/IgApplication.cs**:

**Step A: Catch the command** In your `_commandReader` polling loop (`Update` method), route the new command:

```
case CommandType.CMD_DRAW_PERSONAL_ROUTE:
    ParseCommandAndActivatePersonalRoute(cmd.RequestId, cmd.CommandArgsJson);
    break;
```

**Step B: The Orchestration Logic** Add the handler that extracts the target vehicle ID, pops the tool, and orchestrates the two-step network operation using C# async/await:

```
private void ParseCommandAndActivatePersonalRoute(Guid requestId, string argsJson)
{
    if (string.IsNullOrWhiteSpace(argsJson)) return;

    using var doc = JsonDocument.Parse(argsJson);
    if (!doc.RootElement.TryGetProperty("entityId", out var idEl)) return;
    int vehicleId = idEl.GetInt32();

    if (_canvas.ActiveTool is PointSequenceTool)
        _canvas.PopTool();

    // Spin up the tool. When the operator right-clicks, the callback fires.
    var tool = new PointSequenceTool(points => 
    {
        _canvas.PopTool();
        
        if (points.Length < 2) 
        {
            // Cancelled or invalid: Ack the IOS so it closes the transaction
            _mapCommandAckWriter?.Write(new MapCommandAck {
                RequestId = requestId,
                StatusCode = MapCommandController.StatusCancelled
            });
            return;
        }

        // Fire-and-forget the async orchestration
        _ = OrchestratePersonalRouteAsync(requestId, vehicleId, points);
    });

    _canvas.PushTool(tool);
}

private async Task OrchestratePersonalRouteAsync(Guid requestId, int vehicleId, Vector2[] canvasPoints)
{
    if (_commandGateway == null || _geoTransform == null) return;

    // 1. Convert drawn points to WorldPos
    var waypoints = new List<Waypoint>();
    foreach (var p in canvasPoints)
    {
        var (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(p.X, p.Y, 0f));
        waypoints.Add(new Waypoint { Position = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt } });
    }

    // 2. Create the Route Entity
    var createReq = new CreateEntityRequest
    {
        RequestId = Guid.NewGuid(),
        InitialDescriptors = new List<EntityDescriptorUnion>
        {
            new() { _d = EDescriptorType.dtEntityMaster, EntityMaster = new EntityMaster { TkbType = TkbEntityTypes.TacGraphic_Route } },
            new() { _d = EDescriptorType.dtMapRoute, MapRoute = new MapRoute { Points = waypoints, IsLoop = false } },
            // Make it a child of the vehicle in the ORBAT
            new() { _d = EDescriptorType.dtEntityInfo, EntityInfo = new EntityInfo { CommanderId = vehicleId } }
        }
    };

    var createAck = await _commandGateway.CreateEntityAsync(createReq);
    if (createAck.StatusCode > 1) return; // Creation failed

    // 3. Assign FollowRoute Mission to the Vehicle
    var missionReq = new MissionControlRequest
    {
        RequestId = Guid.NewGuid(),
        TargetEntityId = vehicleId,
        BaseVersion = 0,
        Payload = new MissionCommandUnion
        {
            _d = eMissionCommandType.CMD_REPLACE_MISSION,
            FullMissionData = new MissionPlan
            {
                ActiveTaskId = Guid.NewGuid(),
                Tasks = new List<MissionTask>
                {
                    new MissionTask
                    {
                        TaskId = Guid.NewGuid(),
                        ExecutingEngine = "CGFX",
                        BehaviorId = "FollowRoute",
                        BehaviorParams = $"{{\"routeEntityId\": {createAck.EntityId}}}",
                        State = eTaskState.TASK_PLANNED
                    }
                }
            }
        }
    };

    await _commandGateway.SendMissionControlRequestAsync(missionReq);

    // 4. Finally, Ack the IOS that the tool session finished successfully
    _mapCommandAckWriter?.Write(new MapCommandAck {
        RequestId = requestId,
        StatusCode = MapCommandController.StatusFinished
    });
}
```

Why this is the best architectural approach

-   **Single Responsibility:** The IOS strictly manages the UI and issues domain-level intents. It doesn't maintain state machines for things it doesn't own.-   **Asynchronous Linearity:** By wrapping the DDS request/response cycles in `Task` via your `BdcCommandGateway`, the IG handles the multi-step `[Draw -> Create Route -> Wait for ID -> Send Mission -> Finish]` flow in a single, readable method without blocking the 60Hz render loop.-   **No SimHost Hacks:** The `CreateEntityRequestSystem` on the backend stays completely generic. It just receives standard SST requests, exactly as designed.

pls describe again what the IOS needs to do to make the orbat entity context menu work - what it should do for each of the menu items

To make the ORBAT context menu fully functional while adhering to our thin-client and clean architecture principles, the IOS should act strictly as an intent-dispatcher. It will rely entirely on the existing DDS request contracts to command the IG and SimHost.

Here is exactly what the IOS needs to do for each context menu action:

**1\. Select Entity** To synchronize the IG's selection with the operator's ORBAT choice, the IOS must publish a `MapCommandRequest` to the DDS bus with `Type = CommandType.CMD_SET_SELECTION`. The `CommandArgsJson` payload should contain the target entity ID (e.g., `{"entityId": 12345}`). Locally, the IOS should also call its own `SelectEntity` interface method so the local Inspector and Mission panels immediately reflect the choice.

**2\. Center on Entity** The IOS must retrieve the entity's current physical location and command the IG camera to move there. It does this by querying the local `IDerRepo` for the entity's `WorldPos` descriptor. Once it has the `GeoPoint`, the IOS publishes a `MapCommandRequest` with `Type = CommandType.CMD_SET_VIEW`. The `CommandArgsJson` will contain the extracted coordinates (e.g., `{"lat": 45.0, "lon": 12.0, "zoom": 1000}`).

**3\. Delete** _(Note: As established in our recent conversation history, this relies on the newly proposed messages not yet present in the provided source files)._ The IOS will publish a `DeleteEntityRequest` to the DDS bus containing a new `RequestId` and the target `EntityId`. Following our two-ACK design, the IOS will then monitor the `CreateUpdateDeleteEntityAck` topic. It will receive an `InProgress` status code to temporarily lock the UI, followed by a `Success` status code once the SimHost's Entity Lifecycle Module fully tears down the entity across the network.

**4\. Edit Route (Create Personal Route)** _(Note: This also relies on the orchestrator pattern we designed in our conversation history, which is outside the provided sources)._ To keep the IOS "dumb" and strictly separated from map-authoring state machines, the IOS does no orchestration here. It simply generates a unique `ContextId` and publishes a `MapCommandRequest` with the newly proposed `CommandType.CMD_DRAW_PERSONAL_ROUTE`. The `CommandArgsJson` payload must include both the `contextId` and the vehicle's `entityId`. The IG intercepts this command, activates the map drawing tools, creates the route entity, and sends the mission assignment to the SimHost automatically.

**5\. Abort Mission** The IOS is already perfectly equipped for this out of the box. The ORBAT menu simply needs to invoke the existing `IMissionEditorService` by calling `SendControlCommandAsync` and passing the entity ID along with `eMissionCommandType.CMD_ABORT_ALL`. This natively publishes a `MissionControlRequest` to the SimHost, which safely intercepts it, clears the active doctrine, and halts the vehicle in the simulation.

CMD\_SET\_VIEW seems wrong. IG must support centering the map on concrete entity ID, IOS should not orchestrate

You are absolutely spot on. Fetching the coordinates on the IOS just to send them back to the IG as a lat/lon pair is a blatant violation of the thin-client principle. The IOS should express the _intent_ ("center on this unit"), and the IG, which actually owns the spatial transforms, camera state, and physics representations, should handle the resolution.

Fortunately, we don't need to invent a new command for this. The existing `CommandType.CMD_SET_VIEW` is perfect for this, but if you look closely at the IG's command polling loop (`IgApplication.Update`), `CMD_SET_VIEW` is currently missing from the `switch` statement entirely.

We can wire it up using polymorphic JSON arguments so that `CMD_SET_VIEW` cleanly accepts an `entityId` instead of forcing explicit coordinates.

Here is how to implement this clean, decoupled approach:

1\. The "Dumb" IOS Implementation

The IOS simply fires the `CMD_SET_VIEW` command carrying the network ID.

```
// In IosLogic.cs
public void CenterOnEntity(int networkEntityId)
{
    string argsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new {
        entityId = networkEntityId
    });

    _commandWriter.Write(new MapCommandRequest {
        RequestId = Guid.NewGuid(),
        MapId = _targetMapId,
        Type = CommandType.CMD_SET_VIEW,
        CommandArgsJson = argsJson,
    });
}
```

2\. Wire the Command in the IG

In **Hrot.IG/IgApplication.cs**, add the missing `case` to the command polling loop inside the `Update(float dt)` method:

```
case CommandType.CMD_SET_VIEW:
    ParseCommandAndSetView(cmd.CommandArgsJson);
    break;
```

3\. Let the IG Handle Spatial Resolution

Add the parser method to `IgApplication.cs`. The IG will extract the `entityId`, look up the local ECS ghost via its `NetworkEntityMap`, and then reuse the exact same `CenterCameraOn(Entity)` method it already uses for local context menu actions:

```
private void ParseCommandAndSetView(string argsJson)
{
    if (string.IsNullOrWhiteSpace(argsJson)) return;

    try
    {
        using var doc = JsonDocument.Parse(argsJson);
        var root = doc.RootElement;

        // If the IOS provides an entity ID, let the IG resolve its current spatial position
        if (root.TryGetProperty("entityId", out var eidEl))
        {
            long networkEntityId = eidEl.GetInt64();
            
            if (_entityMap.TryGetEntity(networkEntityId, out var entity))
            {
                CenterCameraOn(entity);
            }
            else
            {
                FdpLog<IgApplication>.Warn("[IG] CMD_SET_VIEW failed: Entity {0} not found in map.", networkEntityId);
            }
        }
        // NOTE: You can also handle raw "lat"/"lon" here in the future if the IOS ever needs to pan to empty coordinates.
    }
    catch (Exception ex)
    {
        FdpLog<IgApplication>.Warn("[IG] ParseCommandAndSetView failed: {0}", ex.Message);
    }
}
```

Why this is the superior architecture

By letting the IG handle the lookup, we avoid a subtle race condition: if the entity is a fast-moving aircraft, the coordinates the IOS reads from its local `DerRepo` might be several frames out of date by the time the IG receives the command. By pushing the `entityId` to the IG, the IG queries its `SimTransform` component on the exact frame the camera moves, guaranteeing perfect, stutter-free centering.

what was the necessary fix in simhost and why?

The necessary fix in the SimHost codebase was modifying the `FollowRouteParams` payload and its parsing logic in `SimHostNodes.cs` to accept a replicated network `RouteNetworkId` rather than a local `TrajectoryId`.

Why was this critical? It fundamentally comes down to maintaining clean API boundaries and preventing the leakage of internal application state. A `TrajectoryId` is a purely local, ephemeral memory index managed exclusively by the SimHost's internal `TrajectoryPoolManager`. Because this memory index is never replicated across the DDS network, external thin clients like the IOS or IG cannot possibly know it.

For a decoupled orchestrator flow to work, the client must be able to express its intent using the only shared identifier the network understands: the entity's network ID. The fix required the SimHost application layer to accept this network ID from the `MissionControlRequest`, query the local ECS world to find the route's ghost entity, and safely resolve the internal `TrajectoryId` via the `RouteTrajectoryCache` component.

By pushing this translation step down into the SimHost's `Action_WriteFollowRouteChannel` behavior tree node, we encapsulate local memory management completely within the engine. This keeps the network contracts pristine, ensures proper separation of concerns, and allows the IOS to remain a "dumb" intent-dispatcher that doesn't need to track backend memory states.

summarize all the stuff needed for the orbat context menu

To implement a clean, decoupled ORBAT context menu in the IOS, we must strictly adhere to the thin-client principle. The IOS should act purely as an intent-dispatcher, relying on standardized DDS contracts to command the IG and SimHost, which own the actual spatial state, tooling orchestration, and ECS mutations.

Here is the architectural breakdown of the mechanisms required for each menu action, incorporating the new patterns we established in our recent discussions (which are not yet present in the provided source files):

**1\. Select Entity** The IOS publishes a `MapCommandRequest` with the `CMD_SET_SELECTION` command type, passing a JSON payload that contains the target `entityId`. Locally, the IOS also calls its internal `SelectEntity` method so the Inspector and Mission panels immediately reflect the operator's choice without waiting for a network round-trip.

**2\. Center on Entity** The IOS dispatches a `MapCommandRequest` using `CMD_SET_VIEW` and includes the `entityId` in the JSON payload. As we designed previously, the IG intercepts this command, queries its local ECS world for the entity's `SimTransform` component, and safely centers the map camera. This completely encapsulates spatial resolution within the IG and prevents race conditions that would occur if the IOS tried to read and transmit raw coordinates.

**3\. Delete** Relying on the two-phase acknowledgment pattern from our prior design session, the IOS publishes the newly proposed `DeleteEntityRequest` to the authoritative owner. The IOS then monitors the unified `CreateUpdateDeleteEntityAck` topic. It temporarily locks the UI upon receiving an `InProgress` status code, and safely unlocks or clears the UI when the final `Success` or `Failure` ACK arrives once the Entity Lifecycle Module completes its distributed teardown.

**4\. Edit Route (Create Personal Route)** To guarantee the IOS remains a "dumb" terminal, it avoids managing multi-step drawing state machines. Instead, it generates a unique `ContextId` and fires the newly proposed `CMD_DRAW_PERSONAL_ROUTE` map command containing both the context and the vehicle's `entityId`. The IG takes over as the smart orchestrator, activating the drawing tools, creating the route entity, and assigning the mission via its internal `BdcCommandGateway`.

**5\. Abort Mission** The IOS utilizes its existing `IMissionEditorService` abstraction, which provides a clean API for mission mutations. The panel simply invokes `SendControlCommandAsync`, targeting the vehicle's ID and passing the `CMD_ABORT_ALL` command type. This natively publishes a `MissionControlRequest` to the DDS bus, which the SimHost safely processes to halt the entity and clear its active doctrine.

ad "IOS also calls its internal SelectEntity " - shouldn't the IOS wait for selection changed notification from IOS to stay in sync with IG using same single mechanism always, not taking local shortcuts?

You are bringing up a classic architectural debate: strict Unidirectional Data Flow (Single Source of Truth) versus Optimistic UI.

Your instinct to rely on a single mechanism—waiting for the IG to broadcast a `SelectionChangedEvent` back to the IOS—is architecturally pure. It ensures the IOS never displays a state that hasn't been confirmed by the backend. However, in the context of this specific real-time simulation, doing the local update is not a dirty shortcut; it is an intentional **Optimistic UI** pattern designed to solve two specific problems: UI latency and network feedback loops.

Here is why the architecture favors applying the selection locally rather than waiting for the round-trip:

1\. The Network Echo Loop

If you look closely at how the IG handles selection, it only publishes a `SelectionChangedEvent` in direct response to physical hardware input. Specifically, the `_selectionWriter` is only invoked inside `OnCanvasClicked` when the operator clicks the map with their mouse.

The IG does _not_ have an ECS system that watches for programmatic changes to `SelectionState` and broadcasts them. If it did, we would introduce a distributed feedback loop:

-   IOS sends `CMD_SET_SELECTION`.-   IG applies it to the ECS.-   IG's watcher sees the change and broadcasts `SelectionChangedEvent`.-   IOS receives the event and updates its UI... and has to carefully avoid re-transmitting another `CMD_SET_SELECTION`.

By only emitting `SelectionChangedEvent` on physical mouse clicks, the IG acts as the source of truth for _map clicks_, while the IOS acts as the source of truth for _ORBAT clicks_. They cross-pollinate each other using command messages, avoiding infinite echo loops.

2\. UI Responsiveness (Input Lag)

Selection is a transient, non-destructive UI state. When a user clicks a unit in a hierarchical tree (like the ORBAT panel), they expect the row to highlight instantly (on the same frame).

If the IOS strictly waited for the IG's `SelectionChangedEvent`, every click in the IOS UI would suffer a network round-trip delay. In a 60Hz simulation with network overhead, this introduces a perceived input lag of anywhere from 16ms to 50ms+. If the network degrades, the UI feels completely unresponsive.

3\. The Pattern is Already Established

This pattern of optimistic local application is already standard practice in your codebase for transient UI interactions. For example, when the operator clicks a node in the ORBAT tree, `OrbatPanel.HandleEntityClick` directly calls `logic.SelectEntity(entityId)`, which sets `SelectedEntityId` instantly without waiting. The system then safely absorbs incoming `SelectionChangedEvent` messages from the network to keep the two views synchronized.

Summary

If this were a highly destructive or authoritative operation (like firing a weapon or deleting a unit), you absolutely must wait for the network ACK. But for purely visual state like selection, applying the intent locally and syncing it outbound (Optimistic UI) provides a vastly superior user experience while keeping the network topology free of complex echo-cancellation logic.

