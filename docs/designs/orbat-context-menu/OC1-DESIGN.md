# ORBAT Context Menu — Design

**Prefix:** `OC1-`  
**Workstream:** ORBAT Panel Context Menu  

---

## Overview

We are adding a right-click context menu to entity rows in the IOS ORBAT panel. The menu gives operators fast access to five spatial and command actions directly from the order-of-battle tree, without switching panels or clicking the map.

The five menu actions are:

| Action | Target entities | Mechanism |
|--------|----------------|-----------|
| Select entity | All entities | `MapCommandRequest(CMD_SET_SELECTION)` + optimistic local select |
| Center on entity | All entities | `MapCommandRequest(CMD_SET_VIEW, {entityId})` |
| Delete | All entities | `DeleteEntityRequest` → monitor `CreateUpdateDeleteEntityAck` |
| Edit Route | Physical (simulated) entities only | `MapCommandRequest(CMD_DRAW_PERSONAL_ROUTE)` — IG orchestrates |
| Abort Mission | Physical (simulated) entities only | `IMissionEditorService.SendControlCommandAsync(CMD_ABORT_ALL)` |

---

## Architectural Principles

### Thin-Client IOS
The IOS is a stateless intent-dispatcher.  It issues a single, semantically named DDS command and then waits for acknowledgment where required.  It does **not** orchestrate multi-step workflows involving the map tooling layer.

### IG as Smart Orchestrator
The IG owns the drawing canvas, spatial transforms, and async command gateway.  For compound operations (personal route authoring), the IG receives a single intent command and chains all the necessary DDS calls internally using `BdcCommandGateway`.

### Optimistic UI for Selection
Selection is a non-destructive, transient UI state.  The IOS applies selection locally (same frame) and dispatches the network command concurrently.  This avoids perceptible input lag without introducing a feedback loop, because the IG only emits `SelectionChangedEvent` from physical mouse clicks — never in response to programmatic `CMD_SET_SELECTION` commands.

### Physical-Entity Gating
"Edit Route" and "Abort Mission" concern simulation-side state (locomotion, behavior) that map graphic entities do not have.  Showing these menu items for route or area graphics would confuse operators and produce no-op or error results.  A local helper, `IsSimulatedEntity(int entityId)`, gates these items based on the entity's `TkbType` from the local `IDerRepo`.

### No Propagation to Subordinates
Mission operations (Abort, Edit Route) are issued only to the directly selected entity.  Subordinate propagation is not supported in the current codebase and is explicitly **out of scope** for this workstream.

---

## Phase 0: Bug Fixes (Priority)

These bugs affect existing authoring and lifecycle flows and must be resolved before or alongside the feature work.

### 0.1 IOS Draw Route — No Entity Created  *(OC1-B001)*

When the operator clicks "Draw Route" in the IOS SpawnerPanel, the `PointSequenceTool` activates on the IG correctly.  However, after the operator confirms the route (right-click to finish), no route entity appears — not in the ORBAT tree, not on the IG map, and not in the IOS inspector.

**Hypothesis A (layer visibility):** The route entity IS successfully created and written to the DerRepo, but it falls under the `road_graphs` layer (see OC1-B002) which may be disabled by default, making it invisible on the map.  The ORBAT tree should still show it regardless of layer state.

**Hypothesis B (creation pipeline failure):** The IG's `ActivateRouteAuthoringTool` emits `CreateEntityRequest` via `_mapCommandController.OnAreaEntityCreated()` or the fallback `_createEntityDdsWriter`.  In full runner mode (`Hrot.ClusterRunner -m all`) there may be a path where this request is not reaching the SimHost — either a DDS topic connectivity issue, a writer not being initialised in the `MapCommandController`, or a missing subscription on the SimHost side.

The investigation must determine which hypothesis is correct (or if both apply) and fix accordingly.

### 0.2 No Map Layer for Routes in IOS Layer Panel  *(OC1-B002)*

The IOS `ConfigPanel` exposes a layer toggle labelled **"Road Graphs"** (`view.layers.road_graphs`).  The IG `MapLayerRegistry` maps the `road_graphs` layer identifier to entities with `TkbType == TacGraphic_Route (8802)`.  So the toggle effectively controls route visibility — but the label is misleading and the user cannot find it.

**Fix A (label rename):** If "road graphs" and "tactical routes" truly share the same layer slot, rename the IOS checkbox to make it clear that tactical routes (authored by the Draw Route tool) are controlled here.

**Fix B (separate layer):** If road-network graph entities and tactical route entities serve different purposes and should be toggled independently, introduce a dedicated `routes` layer entry in `MapLayerRegistry` with its own predicate and a corresponding IOS ConfigPanel checkbox.

Investigate whether any currently-spawned entities use the `road_graphs` layer for non-route purposes before deciding between Fix A and Fix B.

### 0.3 Tactical Shape Authoring — Shape Position Wrong  *(OC1-B003)*

When the operator uses the area-authoring tool (tactical shapes), the shape displayed after confirmation appears at a **different position** than the polygon that was drawn.

In `IgApplication.ActivateAreaAuthoringTool`, the `WorldPos.Pos` anchor is computed as the **arithmetic mean of all vertex positions** (centroid), and the polygon outline vertices may be stored as either absolute coordinates or offsets relative to this centroid.  Two known failure modes:

- **Offset interpretation mismatch:** If vertices are stored as absolute geodetic coordinates but the renderer interprets them as offsets added to `WorldPos.Pos`, the displayed shape is translated by the centroid amount.
- **Timing / component race:** If the `MapOverlayOutline` component arrives on the IOS before the `WorldPos` component is processed, the shape may be rendered at a default or zero position on the first frame and snap later, giving the appearance of being placed at the wrong location.

Investigate the full chain: canvas screen-coords → `_geoTransform.ToGeodetic` → centroid calculation → descriptor serialisation → IOS-side rendering.  Verify the absolute-vs-relative coordinate contract between `ActivateAreaAuthoringTool` and the shape renderer, and fix whichever side violates the contract.

### 0.4 Entity Deletion Not Reflected in IOS Inspector  *(OC1-B004)*

When an entity is deleted using the **IG entity inspector context menu** ("Delete entity"), the entity vanishes from the IG map correctly.  The IOS entity inspector, however, continues to display the deleted entity's data without clearing.

**Root cause:** The IG's delete action publishes `DestroyEntityCommand` to the internal event bus.  `NetworkSpawningSystem` removes the entity from ECS, which causes the SimHost's DDS `EntityMaster` writer to dispose the instance (`NotAliveDisposed`).  The IOS's `MasterIngressHandler<EntityMaster>` catches this lifecycle event and calls `DerRepo.DeleteEntity(id)`, firing `DerRepo.EntityDeleted` — **but the `EntityInspectorState` (or equivalent inspector cache) does not react to this event**, leaving the inspector showing stale entity data.

**Fix:** Wherever `DerRepo.EntityDeleted` is observed in `IosLogic`, clear `SelectedEntityId` if it matches the deleted entity, and reset the inspector state accordingly.  Verify that `EntityInspectorState` either subscribes to `DerRepo.EntityDeleted` or rebuilds itself from live DerRepo data each frame (which would naturally clear stale data once the entity ID is gone).

---

## Phase 1: Shared Contracts

### 1.1 New Command Type — `CMD_DRAW_PERSONAL_ROUTE`  *(OC1-C001)*

The `CommandType` enum in `Hrot.NED/MapMessages.cs` needs one new entry:

```csharp
/// <summary>
/// Activates route-authoring on the IG and automatically assigns the
/// resulting route as a personal navigation mission for the target entity.
/// Args JSON: { "contextId": "<guid>", "entityId": 12345 }
/// </summary>
CMD_DRAW_PERSONAL_ROUTE
```

This is a dedicated, semantically named command rather than re-using `CMD_START_AUTHORING` with a fabricated JSON flag, which would turn the authoring path into an undocumented convention.

The IOS JSON payload carries:
- `contextId` — unique GUID per authoring session (for IG to echo back in the `MapCommandAck`).
- `entityId` — network ID of the target vehicle.

---

## Phase 2: SimHost Route-Assignment Fix

### Background: Two Separate `FollowRouteParams` Structs

The codebase contains two deliberately distinct structs with the same conceptual purpose but different scopes:

| Struct | Namespace | Role |
|--------|-----------|------|
| `FDP.Toolkit.Navigation.FollowRouteParams` | FDP engine | Written directly into `LocomotionChannel.Params`; consumed by the locomotion engine. Uses local `int TrajectoryId`. |
| `Hrot.SimHost.Brains.SimHostNodes.FollowRouteParams` | Application-layer BTree | Written via `Unsafe.Write` into `BrainBlackboard.Memory`; read by `Action_WriteFollowRouteChannel`. Also uses local `int TrajectoryId`. |

Both structs intentionally use a local `int TrajectoryId` — an ephemeral `TrajectoryPoolManager` memory index that is **never replicated over the network**.  The FDP engine (`FDP.Toolkit.*`) is strictly decoupled from network and replication layers and must remain so.  **Neither struct is changed by this task.**

### 2.1 FollowRoute Mission: Translate Network ID at Ingress Boundary  *(OC1-S001)*

**The bug:** When the IOS (or the OC1-G003 IG orchestration) sends a `MissionControlRequest` with a `FollowRoute` task, the `BehaviorParams` JSON contains `{"routeEntityId": 123}` — a network entity ID.  `MissionControlRequestSystem.BuildQueue()` stores this raw JSON string directly into `MissionPlanQueue.Tasks[n].BehaviorParams` without any translation.

Later, when the BTree engine spawns the `FollowRoute` node, `SimHostNodes.ParseFollowRouteParams` deserialises `BehaviorParams` looking for a field named `trajectoryId`.  The JSON contains `routeEntityId` instead, so the deserialiser writes `TrajectoryId = 0` into the blackboard.  `Action_WriteFollowRouteChannel` silently forwards a zero trajectory index into the FDP navigation struct, and the vehicle never moves.

**The fix: translate at the network ingress boundary.**

The correct place to resolve network ID → local ID is in `MissionControlRequestSystem.BuildQueue()` — the outermost network boundary — before any BTree or engine code ever sees the params.  The system already has full access to the ECS world.

For any task whose `BehaviorId == "FollowRoute"`:
1. Parse `routeEntityId` from `BehaviorParams` JSON.
2. Query the ECS world for the entity with `NetworkIdentity.Value == routeEntityId` that also has a `RouteTrajectoryCache` component.
3. Read `RouteTrajectoryCache.TrajectoryId` (the compiled local index).
4. Rewrite `BehaviorParams` as `{"trajectoryId": <localId>, "Speed": <speed>, "Loop": <loop>}` before storing into `MissionPlanQueue`.

If the route entity is not found or its `RouteTrajectoryCache.TrajectoryId == 0` (not yet compiled), the task is held in the existing **10-frame retry queue** until the route is ready.

**What is NOT changed by this task:**
- `SimHostNodes.FollowRouteParams` struct — stays with `int TrajectoryId`.
- `SimHostNodes.ParseFollowRouteParams` — stays unchanged.
- `Action_WriteFollowRouteChannel` — stays unchanged.
- `FDP.Toolkit.Navigation.FollowRouteParams` — not touched.
- `TrajectoryPoolManager` — not touched.

By doing the translation at the ingress boundary, the FDP engine and the BTree layer remain 100% network-agnostic.

---

## Phase 3: IG — Command Handling Extensions

### 3.1 Handle `CMD_SET_SELECTION`  *(OC1-G001)*

The `CMD_SET_SELECTION` entry exists in the `CommandType` enum but is not wired into IgApplication's command polling loop.

The IG adds a case to `IgApplication.Update`:
```csharp
case CommandType.CMD_SET_SELECTION:
    ParseCommandAndSetSelection(cmd.CommandArgsJson);
    break;
```

`ParseCommandAndSetSelection` extracts `entityId` from the JSON args and calls the existing `SelectEntityOnMap(entity)` method (which already handles clearing prior selection, writing ECS `SelectionState` components, and syncing inspector state).  If the entity ID is not found in the local `NetworkEntityMap`, the command is silently ignored with a log warning.

### 3.2 Handle `CMD_SET_VIEW` (Entity-Centric)  *(OC1-G002)*

Similarly, `CMD_SET_VIEW` exists in the enum but is not handled in the switch statement.

The IG adds:
```csharp
case CommandType.CMD_SET_VIEW:
    ParseCommandAndSetView(cmd.CommandArgsJson);
    break;
```

`ParseCommandAndSetView` parses `entityId` from the JSON args, resolves the entity via `_entityMap.TryGetEntity`, and calls the existing `CenterCameraOn(entity)` method.

**Why IG resolves the position (not IOS):**  If the IOS were to read `WorldPos` coordinates from its local `DerRepo` and send explicit lat/lon, those coordinates would be several frames stale by the time the IG receives the command (the entity may have moved).  The IG queries `SimTransform` at the exact frame the camera moves, guaranteeing stutter-free centering with no race condition.

### 3.3 Handle `CMD_DRAW_PERSONAL_ROUTE` — IG Orchestration  *(OC1-G003)*

This is the most complex IG addition.  The IG:

1. **Parses** `entityId` and `contextId` from the JSON args.
2. **Activates** a `PointSequenceTool` (same tool used by `CMD_START_AUTHORING` for routes).
3. **On completion** (operator right-clicks to finish), fires an `async` orchestration chain via `BdcCommandGateway`:
   a. `CreateEntityAsync` — creates a `TacGraphic_Route` entity with `MapRoute` waypoints and `EntityInfo.CommanderId = vehicleId` (places the route under the vehicle in the ORBAT tree).
   b. `SendMissionControlRequestAsync` — assigns a `FollowRoute` mission task to the vehicle, referencing the newly-created route by its network entity ID.
4. **Acks the IOS** via `MapCommandAck` (StatusCode = 0 for success, 2 for cancelled/failure).

If the operator cancels drawing (fewer than 2 points), the tool pops and a cancellation `MapCommandAck` is sent.  If route creation fails (ACK StatusCode > 1), the mission assignment is skipped.

The mission task BehaviorParams JSON contains `{"routeEntityId": <id>}`, which is the format consumed by `ParseFollowRouteParams` after the Phase 2 fix (OC1-S001).

**Why IG orchestrates instead of IOS:**
- IOS would need to maintain an in-memory pending-route dictionary keyed on `contextId`, subscribe to `CreateUpdateDeleteEntityAck`, and respond to the matching ACK by sending a second DDS request.  This introduces client-side state management and a fragile correlation pattern across asynchronous messages.
- `BdcCommandGateway` already wraps exactly this pattern as a clean `async/await` API.  Placing the orchestration in the IG keeps the network contract minimal (one command in, one ACK out) and the IOS genuinely stateless.

---

## Phase 4: IOS — ORBAT Context Menu

### 4.1 OrbatPanel Context Menu Infrastructure  *(OC1-I001)*

`OrbatPanel.cs` currently renders a simple tree.  Row-level right-click menus are added using ImGui's `IsItemClicked(ImGuiMouseButton.Right)` / `BeginPopupContextItem` pattern.

A helper method `IsSimulatedEntity(int entityId, IDerRepo repo)` is added to determine whether menu items specific to simulation entities (Edit Route, Abort Mission) should appear:
- Returns `true` when the entity's `EntityMaster.TkbType` is **not** in the map-graphic range (values ≥ 8000).
- Returns `false` if the entity has no `EntityMaster` descriptor (defensive fallback).

The context menu is built inline in `Draw()` immediately after `ImGui.Selectable` for each node row.

### 4.2 Select Entity  *(OC1-I002)*

Clicking "Select entity" in the context menu:

1. Calls `logic.SelectEntity(entityId)` **immediately** (optimistic UI — highlights the row and opens the inspector without waiting for a network round-trip).
2. Publishes `MapCommandRequest(CMD_SET_SELECTION, {"entityId": id})` so the IG map view synchronises its own selection state.

The optimistic approach is safe here because selection is non-destructive and the IG does not echo `SelectionChangedEvent` back in response to programmatic selection commands (it only emits that event on physical mouse clicks), eliminating any risk of an echo loop.

### 4.3 Center on Entity  *(OC1-I003)*

Clicking "Center on entity" publishes `MapCommandRequest(CMD_SET_VIEW, {"entityId": id})`.  The IOS does not read or transmit coordinates.  The IG resolves the entity's current spatial position at command-receive time (see Section 3.2).

### 4.4 Delete  *(OC1-I004)*

Clicking "Delete" publishes `DeleteEntityRequest` to the DDS bus.  The IOS then monitors `CreateUpdateDeleteEntityAck` (already processed in `ProcessEntityCreationAcks` for creation flows — the same topic covers deletion ACKs):

- **InProgress ACK:** the entity row is disabled/locked in the ORBAT tree.
- **Success ACK:** the entity disappears from the ORBAT tree naturally as the DerRepo ingress removes it.
- **Failure ACK:** the lock is released; a `GlobalAlert` message is shown.

`IosLogic` tracks in-flight delete requests in a `HashSet<int> _pendingDeleteEntityIds` keyed on entity network ID (not request GUID, since the operator identifies entities, not requests).

### 4.5 Edit Route  *(OC1-I005)*

**Only shown for simulated entities** (gated by `IsSimulatedEntity`).  Clicking "Edit Route" generates a new `ActiveContextId` and publishes `MapCommandRequest(CMD_DRAW_PERSONAL_ROUTE, {"contextId": guid, "entityId": id})`.

The IOS then monitors `MapCommandAck` (already processed in `ProcessMapCommandAcks`):
- Cancellation or failure ack: no UI change needed (the authoring tool simply closes on the IG side).

**Note — no subordinate propagation:**  This action is issued only to the directly selected entity.  In the current architecture there is no mechanism to propagate route assignments to child entities.  This is intentional and explicitly out of scope.

### 4.6 Abort Mission  *(OC1-I006)*

**Only shown for simulated entities** (gated by `IsSimulatedEntity`).  Clicking "Abort Mission" calls the existing `IMissionEditorService.SendControlCommandAsync(entityId, eMissionCommandType.CMD_ABORT_ALL, Guid.Empty)`.

This is the same call used by mission-panel controls today.  No new infrastructure is needed on the IOS side.

**Note — no subordinate propagation:**  Mission abort is issued only to the directly selected entity.

---

## Interaction Flow Diagrams

### Edit Route (Personal Route) — End-to-End

```
IOS                         IG                          SimHost
 |                           |                              |
 |--CMD_DRAW_PERSONAL_ROUTE->|                              |
 |                           |--[activate PointSequenceTool]|
 |                           |  (operator draws route)      |
 |                           |--CreateEntityRequest-------->|
 |                           |<-CreateUpdateDeleteEntityAck-|  (StatusCode InProgress)
 |                           |<-CreateUpdateDeleteEntityAck-|  (StatusCode Success, EntityId=routeId)
 |                           |--MissionControlRequest------>|  (FollowRoute, routeEntityId=routeId)
 |                           |<-MissionControlAck-----------| 
 |<-------MapCommandAck------|  (StatusCode 0 = finished)  |
```

### Select Entity — End-to-End

```
IOS (local)                 IOS → DDS                   IG
 |                           |                            |
 |--logic.SelectEntity(id)-->|                            |
 |   (optimistic UI update)  |--CMD_SET_SELECTION-------->|
 |                           |                            |--SelectEntityOnMap(id)
 |                           |                            |   (ECS selection update)
```

---

## Files Changed

| File | Change |
|------|--------|
| `Hrot.NED/MapMessages.cs` | Add `CMD_DRAW_PERSONAL_ROUTE` to `CommandType` enum |
| `Hrot.SimHost/Brains/SimHostNodes.cs` | Replace `TrajectoryId` with `RouteNetworkId`; update parser and action delegate |
| `Hrot.IG/IgApplication.cs` | Handle `CMD_SET_SELECTION`, `CMD_SET_VIEW`, `CMD_DRAW_PERSONAL_ROUTE` in command loop; add orchestration method |
| `Hrot.ExCon/Panels/OrbatPanel.cs` | Add right-click context menu; add `IsSimulatedEntity` helper |
| `Hrot.ExCon/IosLogic.cs` | Add `SendSetSelection`, `CenterOnEntity`, `DeleteEntity`, `StartPersonalRouteAuthoring`; track `_pendingDeleteEntityIds` |
| `Hrot.ExCon/Abstractions/IIosLogic.cs` | Extend interface with the new methods above |
| `Hrot.SimHost.Tests/Brains/SimHostNodesTests.cs` | New test class for OC1-S001 |
| `Hrot.IG.Tests/` | New test class(es) for OC1-G001, OC1-G002, OC1-G003 |
| `Hrot.ExCon.Tests/` | New test class for OC1-I001 through OC1-I006 |
