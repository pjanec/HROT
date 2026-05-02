# ORBAT Context Menu — Task Detail

**Design Reference:** [OC1-DESIGN.md](./OC1-DESIGN.md)  
**Tracker:** [OC1-TASK-TRACKER.md](./OC1-TASK-TRACKER.md)

---

## OC1-CORRECTIVE-01 — Fix BATCH-01 Edge Cases

**Design Reference:** [Bug Reports]

### Scope
1. **IOS Entity Inspector Selection Drift:** `DerEntityInspectorPanel` maintains its own `_selectedEntityId` which ignores `IosLogic`'s deselection. Reset it to `NoSelection` if the entity no longer exists in `repo.GetEntity()`.
2. **Route zero-points mapping:** `DescriptorMapper.MapToComponents` ignores `dtMapRoute`. Add a mapping case that converts waypoints via `geoTransform` and emits a `RoutePlan`.
3. **Tactical style loss on edit:** `ActivateAreaEditingTool` replaces the whole `MapVisualOverlay` destroying styles because it defaults the `StyleOverrideJson`. Save the existing style and inject it.

### Tests
- Ensure `DerEntityInspectorPanel` returns `NoSelection` or correctly reflects deselection when the entity is unmapped.
- Ensure `RoutePlan` is correctly populated with the points during entity spawn.
- Ensure `MapVisualOverlay` does not lose fields when generating the request.

---

## OC1-CORRECTIVE-02 — Fix BATCH-02 Route Regressions

**Design Reference:** [Bug Reports]

### Scope
1. **Canvas Y to Z conversion:** In `IgApplication.cs` under `ActivateRouteAuthoringTool` and `ActivateAreaAuthoringTool`, coordinates from the canvas `(points[i].X, points[i].Y)` are incorrectly mapped to the 3D entity X (default East) and Y (default Altitude). Instead, since the canvas is flat mapping XZ, it should map 2D `Y` to 3D `Vector3.Z`, and hardcode altitude (`Vector3.Y`) to 0. Similarly, in `ActivateAreaEditingTool`, the edited altitude must be preserved while modifying `X` and `Z`.
2. **Missing `PickEntity` for Routes:** Route entities cannot be interacted with via mouse on the map because `RouteRenderLayer.PickEntity` currently returns `null`. Implement a simple segment distance check inside `PickEntity` (similar to ray-cast but checking distance to `RoutePlan` waypoints).
3. **IG Router Deletion fails to update Network state:** Currently the "Delete entity" context menu item on IG sends a local `DestroyEntityCommand`, which deletes the ghost but doesn't tell SimHost. Introduce `DdsWriter<DeleteEntityRequest>` inside `IgApplication.InitializeNetwork`, and dispatch `DeleteEntityRequest` when deleting an entity via the Context Menu.

### Tests
- Ensure clicking on a route on IG now successfully populates `_fdpInspectorState.SelectedEntity`.
- Ensure drawing a shape records `Vector3.Z = Canvas.Y`, resulting in proper rendering after recreation.
- Execute the Delete item and assert a `DeleteEntityRequest` is dispatched.

---

## OC1-B001 — IOS Draw Route: No Entity Created

**Design Reference:** [Phase 0.1 — IOS Draw Route — No Entity Created](./OC1-DESIGN.md#01-ios-draw-route--no-entity-created--oc1-b001)

### Scope
1. **Confirm or refute Hypothesis A** (layer invisibility): check whether drawing a route does successfully create an entity in the DerRepo and shows it in the ORBAT tree (regardless of layer state); if yes, the issue is purely display-layer (see OC1-B002).
2. **Confirm or refute Hypothesis B** (creation pipeline failure): trace the path from `ActivateRouteAuthoringTool` → `_mapCommandController.OnAreaEntityCreated()` → DDS `CreateEntityRequest` topic → `CreateEntityRequestSystem` in SimHost → `CreateUpdateDeleteEntityAck` back to the IG.  Identify any broken link.
3. Fix whatever is found (broken DDS writer, missing subscription, uninitialised `MapCommandController` field, or missing topic registration in runner wiring).

**Not in scope:** Changes to any route authoring UI or workflow logic; changes unrelated to the creation-pipeline failure.

### Constraints
- Fix must be verified with `Hrot.ClusterRunner -m all` (full multi-process mode), not just unit tests, since Hypothesis B is a runtime wiring issue.
- If the only issue is layer visibility (Hypothesis A confirmed, B ruled out), this task is resolved by OC1-B002; mark as a duplicate and close.

### Success Conditions
| # | Scenario | Action | Assertion |
|---|----------|--------|-----------|
| 1 | Route entity appears in ORBAT after draw | Draw a route via IOS SpawnerPanel in full runner mode | Entity visible in IOS ORBAT tree with correct name / TkbType after tool completion |
| 2 | Route entity visible on IG map when layer enabled | Route created, `road_graphs` layer on | Route polyline visible on IG canvas |
| 3 | `CreateUpdateDeleteEntityAck` received by IG | Draw and confirm route | IG logs or test shows ACK arriving with `StatusCode == 0` |

---

## OC1-B002 — No Map Layer for Routes in IOS

**Design Reference:** [Phase 0.2 — No Map Layer for Routes in IOS Layer Panel](./OC1-DESIGN.md#02-no-map-layer-for-routes-in-ios-layer-panel--oc1-b002)

### Scope
1. Determine whether the existing `road_graphs` layer in `MapLayerRegistry` and `ConfigPanel` covers tactical-route entities (`TkbType == 8802`) correctly end-to-end (predicate evaluation, toggle JSON key, layer bit assignment).
2. Determine whether any non-route entities currently use the `road_graphs` layer (e.g., actual road-network graph data with a different TkbType).
3. Apply either Fix A (rename/clarify the checkbox label in `ConfigPanel`) or Fix B (add a dedicated `routes` layer in `MapLayerRegistry` and a new checkbox in `ConfigPanel`) based on findings.
4. Ensure the layer is **enabled by default** so newly authored routes are visible without operator configuration.

**Not in scope:** Changes to how entities are assigned to layers; changes to the IG rendering pipeline.

### Constraints
- MapLayerRegistry layer key names must match exactly between the IOS ConfigPanel JSON patch, the IG `MapLayerRegistry` registration, and any IG-side layer-toggle processing.
- If Fix B (separate layer) is chosen, the `road_graphs` layer must remain functionally unchanged to avoid regressing existing behaviour.

### Success Conditions
| # | Scenario | Action | Assertion |
|---|----------|--------|-----------|
| 1 | Layer toggle visible with clear label | Open IOS ConfigPanel layers section | A clearly-labelled "Routes" (or equivalent) checkbox is present |
| 2 | Toggle hides/shows route entities | Draw a route, then uncheck layer, then re-check | Route entity disappears from IG map when unchecked; reappears when checked |
| 3 | Routes visible by default | Start fresh runner session, draw a route | Route is visible on IG without any manual layer configuration |

---

## OC1-B003 — Tactical Shape Authoring: Shape Position Wrong

**Design Reference:** [Phase 0.3 — Tactical Shape Authoring — Shape Position Wrong](./OC1-DESIGN.md#03-tactical-shape-authoring--shape-position-wrong--oc1-b003)

### Scope
1. Trace the complete coordinate chain for area-shape authoring: canvas world-space coordinates → `_geoTransform.ToGeodetic` → centroid computation → `MapOverlayOutline` vertex storage → DDS descriptor serialisation → IOS ingress deserialization → IOS renderer positioning.
2. Identify whether vertices in `MapOverlayOutline` are stored as **absolute geodetic positions** or as **offsets relative to `WorldPos.Pos`** (the centroid), and confirm that both the writer side (IG `ActivateAreaAuthoringTool`) and the reader side (IOS renderer) agree on this contract.
3. Fix whichever side violates the agreed contract, or fix the contract choice itself if it is fundamentally ambiguous.
4. If a component-arrival timing issue is discovered (WorldPos arriving after the outline), address it with an appropriate guard in the renderer (do not render until both components are present).

**Not in scope:** Changes to the route authoring path (routes use vertex[0] as anchor, not centroid, and are unaffected).  Changes to `PointSequenceTool` point collection logic.

### Constraints
- The fix must preserve the ability to move an area shape after creation (the centroid-as-anchor approach is valid as long as the vertex-as-offset contract is honoured consistently).
- No changes to `FDP.Toolkit.Vis2D.Tools.PointSequenceTool` itself.

### Success Conditions
| # | Scenario | Action | Assertion |
|---|----------|--------|-----------|
| 1 | Shape renders at drawn location | Draw a 3-vertex area shape via IOS area-authoring tool | Shape outline on IG map matches the vertices clicked exactly, with no positional offset |
| 2 | Shape renders at drawn location on IOS side | Same | IOS inspector / map shows shape at the same location as the IG |
| 3 | Repeated authoring consistent | Draw 5 shapes at different map positions | All 5 shapes appear at the correct drawn positions |

---

## OC1-B004 — Entity Deletion Not Reflected in IOS Inspector

**Design Reference:** [Phase 0.4 — Entity Deletion Not Reflected in IOS Inspector](./OC1-DESIGN.md#04-entity-deletion-not-reflected-in-ios-inspector--oc1-b004)

### Scope
1. Confirm that when the IG's entity inspector context menu deletes an entity, the `DerRepo.EntityDeleted` event is fired in the IOS process (via `MasterIngressHandler<EntityMaster>` catching `NotAliveDisposed`).
2. Identify which state — `SelectedEntityId` in `IosLogic`, `EntityInspectorState`, or a cached descriptor in another panel — is not cleared when the event fires.
3. Fix the identified stale-state holder: either subscribe to `DerRepo.EntityDeleted` and clear the state, or change the inspector to always read live data from `DerRepo` (returning empty/null when the entity is absent).

**Not in scope:** Changes to how deletion is initiated from the IG (the context menu action itself is not changed); changes to `DeleteEntityRequest`-initiated deletion flows (which have their own ACK-based clearing mechanism, covered in OC1-I004).

### Constraints
- The fix must not clear the inspector when a different, unrelated entity is deleted — only when the currently-selected entity is deleted.
- The fix must handle the case where the IOS is not currently showing any entity (graceful no-op).

### Success Conditions

**Test class:** `Hrot.ExCon.Tests/EntityInspectorStateTests.cs` (extend existing) or `Hrot.ExCon.Tests/IosLogicEntityDeletionTests.cs`

| # | Scenario | Setup | Action | Assertion |
|---|----------|-------|--------|-----------|
| 1 | Inspector clears on entity deletion | Entity 42 selected; inspect shows populated data | Inject `DerRepo.EntityDeleted(42)` | `logic.SelectedEntityId == 0` (or null equivalent); inspector renders empty state |
| 2 | Inspector unaffected for other entity | Entity 42 selected | Inject `DerRepo.EntityDeleted(7)` | `logic.SelectedEntityId` still `42`; inspector still populated |
| 3 | No crash on empty selection | No entity selected | Inject `DerRepo.EntityDeleted(42)` | No exception |

---

## OC1-C001 — Add `CMD_DRAW_PERSONAL_ROUTE` to `CommandType`

**Design Reference:** [Phase 1.1 — New Command Type](./OC1-DESIGN.md#11-new-command-type--cmd_draw_personal_route--oc1-c001)

### Scope
Add one enum entry `CMD_DRAW_PERSONAL_ROUTE` to the `CommandType` enum in `Hrot.NED/MapMessages.cs`.  
Add a doc-comment that documents the expected `CommandArgsJson` shape: `{ "contextId": "<guid>", "entityId": 12345 }`.

**Not in scope:** Any implementation on the IG or IOS side (covered by later tasks).

### Constraints
- The new enum value must be appended *after* all existing values to avoid breaking the serialised ordinal used by existing DDS messages.
- The doc-comment must match the exact JSON property names expected by OC1-G003 exactly.

### Success Conditions
This task is compile-only.  Success is verified by:
- `Hrot.NED` project builds without warnings.
- `Hrot.NED.Tests` all pass (no regression).

---

## OC1-S001 — SimHost FollowRoute: Translate Network ID at Ingress Boundary

**Design Reference:** [Phase 2.1 — FollowRoute Mission: Translate Network ID at Ingress Boundary](./OC1-DESIGN.md#21-followroute-mission-translate-network-id-at-ingress-boundary--oc1-s001)

### Scope
Modify `Hrot.SimHost/Systems/MissionControlRequestSystem.cs` in `BuildQueue()` (or the closest equivalent method that processes individual `MissionTask` entries before writing to `MissionPlanQueue`):

1. For each task where `BehaviorId == "FollowRoute"`:
   a. Parse `routeEntityId` (long) from `BehaviorParams` JSON.
   b. Query the ECS world for an entity with a matching `NetworkIdentity.Value` that also has a `RouteTrajectoryCache` component.
   c. If the entity is not found yet, place the task back into the existing 10-frame retry queue (no new retry infrastructure needed).
   d. If the entity is found but `RouteTrajectoryCache.TrajectoryId == 0` (route not yet compiled), also retry.
   e. If the entity is found and `TrajectoryId > 0`, rewrite `BehaviorParams` to `{"trajectoryId": <localId>, "Speed": <speed>, "Loop": <loop>}` and proceed with normal queue insertion.

**Not in scope:** Changes to `SimHostNodes.FollowRouteParams` struct; changes to `ParseFollowRouteParams`; changes to `Action_WriteFollowRouteChannel`; any FDP toolkit files; `TrajectoryPoolManager`.

### Constraints
- `SimHostNodes.FollowRouteParams` struct remains `int TrajectoryId` — do NOT change it.  The BTree blackboard must receive a resolved local `trajectoryId` in the JSON, exactly as `ParseFollowRouteParams` expects today.
- The JSON field name written by the translation step must be `"trajectoryId"` (camelCase, matching the existing `JsonSerializer` field binding in `ParseFollowRouteParams`).
- Speed and Loop values from the original `routeEntityId` JSON must be preserved in the rewritten JSON.  Do not default them.
- The ECS query in `BuildQueue()` is on the game-simulation thread; no `async`/`Task` patterns should be introduced there.

### Success Conditions

**Test class:** `Hrot.SimHost.Tests/Systems/MissionControlRequestSystemFollowRouteTests.cs`

| # | Scenario | Setup | Action | Assertion |
|---|----------|-------|--------|-----------|
| 1 | Translation rewrites params | ECS world with route entity `NetworkIdentity.Value=99` and `RouteTrajectoryCache.TrajectoryId=5`; incoming task `BehaviorParams = {"routeEntityId":99,"Speed":12,"Loop":true}` | Process `CMD_REPLACE_MISSION` via `BuildQueue` | `MissionPlanQueue` task contains `BehaviorParams = {"trajectoryId":5,"Speed":12,"Loop":true}` (field names as strings; values preserved) |
| 2 | Unknown route → retry queue | ECS world with no entity id 99 | Process same request | Task not yet written to `MissionPlanQueue`; task enqueued in retry queue; retried on next frames |
| 3 | Route found but TrajectoryId==0 → retry | Route entity present, `RouteTrajectoryCache.TrajectoryId=0` | Process request | Task in retry queue; not written to `MissionPlanQueue` |
| 4 | Route compiles between retries | Start with `TrajectoryId=0`, then on retry frame set `TrajectoryId=7` | Two process cycles | `MissionPlanQueue` receives task with `trajectoryId=7` on the second cycle |
| 5 | Non-FollowRoute tasks unaffected | Task with `BehaviorId="Wander"` | Process normally | `BehaviorParams` is stored verbatim, no ECS query issued |
| 6 | ParseFollowRouteParams roundtrip | Use rewritten JSON `{"trajectoryId":5,"Speed":12,"Loop":true}` | Call `SimHostNodes.ParseFollowRouteParams(json, ptr)` | `FollowRouteParams.TrajectoryId==5`, `Speed==12f`, `Loop==true` (existing struct unchanged) |

---

## OC1-G001 — IG Handles `CMD_SET_SELECTION`

**Design Reference:** [Phase 3.1 — Handle CMD\_SET\_SELECTION](./OC1-DESIGN.md#31-handle-cmd_set_selection--oc1-g001)

### Scope
In `Hrot.IG/IgApplication.cs`:
1. Add `case CommandType.CMD_SET_SELECTION:` to the `MapCommandRequest` dispatch switch in `Update()`.
2. Add `ParseCommandAndSetSelection(string argsJson)`:
   - Parse `entityId` (long) from JSON.
   - Look up the entity via `_entityMap.TryGetEntity(entityId, out Entity entity)`.
   - If found: call `SelectEntityOnMap(entity)`.
   - If not found: log a warning (`FdpLog<IgApplication>.Warn(...)`), do nothing.

**Not in scope:** Changes to `SelectionChangedEvent` emission, `SelectEntityOnMap` itself, or any DDS topic subscriptions.

### Constraints
- `ParseCommandAndSetSelection` must **not** publish a `SelectionChangedEvent`.  That event is only ever emitted on physical mouse clicks to prevent echo loops (see design section on Optimistic UI).
- Null or empty `argsJson` must be silently ignored (no exceptions).

### Success Conditions

**Test class:** `Hrot.IG.Tests/CommandHandling/SetSelectionCommandTests.cs`

| # | Scenario | Setup | Action | Assertion |
|---|----------|-------|--------|-----------|
| 1 | Known entity is selected | IG world with entity having `NetworkIdentity.Value = 42` | Deliver `MapCommandRequest(CMD_SET_SELECTION, {"entityId":42})` | Entity's `SelectionState.IsSelected == true`, `IsPrimarySelection == true` |
| 2 | Unknown entity → warning, no crash | IG world with no entity matching id 999 | Deliver `MapCommandRequest(CMD_SET_SELECTION, {"entityId":999})` | No exception thrown; no `SelectionState` components mutated |
| 3 | Empty JSON → ignored | Any IG world | Deliver `MapCommandRequest(CMD_SET_SELECTION, "")` | No exception; no state change |
| 4 | Only one entity remains selected | World with entity A already selected; entity B also present with id 55 | Deliver `CMD_SET_SELECTION` for entity B | Entity A: `IsSelected == false`; Entity B: `IsSelected == true` |

---

## OC1-G002 — IG Handles `CMD_SET_VIEW` (Entity-Centric)

**Design Reference:** [Phase 3.2 — Handle CMD\_SET\_VIEW (Entity-Centric)](./OC1-DESIGN.md#32-handle-cmd_set_view-entity-centric--oc1-g002)

### Scope
In `Hrot.IG/IgApplication.cs`:
1. Add `case CommandType.CMD_SET_VIEW:` to the dispatch switch.
2. Add `ParseCommandAndSetView(string argsJson)`:
   - Parse `entityId` (long) from JSON.
   - Resolve via `_entityMap.TryGetEntity`.
   - If found: call `CenterCameraOn(entity)`.
   - If not found: log a warning.

**Not in scope:** The alternate raw lat/lon branch mentioned in the design talk is explicitly deferred.  Only the `entityId` path is needed now.

### Constraints
- Must handle missing `entityId` field gracefully (JSON present but no `entityId` key → log and return, no exception).
- `CenterCameraOn` already exists; must not duplicate it.

### Success Conditions

**Test class:** `Hrot.IG.Tests/CommandHandling/SetViewCommandTests.cs`

| # | Scenario | Setup | Action | Assertion |
|---|----------|-------|--------|-----------|
| 1 | Camera centers on entity | World with entity having `NetworkIdentity.Value = 10` and `SimTransform.Position = (100, 200, 0)` | Deliver `MapCommandRequest(CMD_SET_VIEW, {"entityId":10})` | `_camera.FocusOn` called with target `(100, 200)` OR `_keyboardPanTarget == (100, 200)` |
| 2 | Unknown entity → no crash | World with no entity id 7 | Deliver `MapCommandRequest(CMD_SET_VIEW, {"entityId":7})` | No exception; camera unchanged |
| 3 | Empty JSON → ignored | Any world | Deliver `MapCommandRequest(CMD_SET_VIEW, "")` | No exception; no camera change |

---

## OC1-G003 — IG Orchestrates `CMD_DRAW_PERSONAL_ROUTE`

**Design Reference:** [Phase 3.3 — Handle CMD\_DRAW\_PERSONAL\_ROUTE — IG Orchestration](./OC1-DESIGN.md#33-handle-cmd_draw_personal_route--ig-orchestration--oc1-g003)

### Scope
In `Hrot.IG/IgApplication.cs`:
1. Add `case CommandType.CMD_DRAW_PERSONAL_ROUTE:` to the dispatch switch, calling `ParseCommandAndActivatePersonalRoute(cmd.RequestId, cmd.CommandArgsJson)`.
2. Add `ParseCommandAndActivatePersonalRoute(Guid requestId, string argsJson)`:
   - Parse `entityId` (int/long) from JSON.
   - If an existing `PointSequenceTool` is active, pop it first.
   - Create and push a new `PointSequenceTool` requiring ≥ 2 points.
   - On tool completion callback: if < 2 points, send `MapCommandAck(requestId, StatusCancelled)` and return; otherwise fire-and-forget `OrchestratePersonalRouteAsync(requestId, vehicleId, points)`.
3. Add `private async Task OrchestratePersonalRouteAsync(Guid requestId, int vehicleId, Vector2[] canvasPoints)`:
   - Convert canvas points to geodetic waypoints using `_geoTransform`.
   - Build `CreateEntityRequest` with descriptors: `EntityMaster(TkbType=TacGraphic_Route)`, `MapRoute(waypoints)`, `EntityInfo(CommanderId=vehicleId)`.
   - `await _commandGateway.CreateEntityAsync(createReq)` — if `StatusCode > 1`, send failure `MapCommandAck` and return.
   - Build `MissionControlRequest(CMD_REPLACE_MISSION)` with a single `FollowRoute` task, `BehaviorParams = {"routeEntityId": <new id>}`.
   - `await _commandGateway.SendMissionControlRequestAsync(missionReq)`.
   - Send `MapCommandAck(requestId, StatusFinished)`.

**Not in scope:** Exposing `OrchestratePersonalRouteAsync` as public or injectable; changes to `PointSequenceTool` itself; any IOS-side orchestration.

### Constraints
- `OrchestratePersonalRouteAsync` must be fire-and-forget (`_ = OrchestratePersonalRouteAsync(...)`)  from the synchronous tool callback to avoid blocking the ImGui render loop.
- If `_commandGateway` or `_geoTransform` is null (e.g., in tests where they are not injected), the method must return early without throwing.
- The `MissionControlRequest` payload must use `CMD_REPLACE_MISSION` (not `CMD_APPEND_TASK`) so that any existing mission is fully replaced.
- `BehaviorParams` JSON `routeEntityId` must be lowercase (matching the `ParseFollowRouteParams` deserialiser fixed in OC1-S001).

### Success Conditions

**Test class:** `Hrot.IG.Tests/CommandHandling/DrawPersonalRouteCommandTests.cs`

| # | Scenario | Setup | Action | Assertion |
|---|----------|-------|--------|-----------|
| 1 | Tool activates on command | IgApplication with mock gateway | Deliver `CMD_DRAW_PERSONAL_ROUTE({"entityId":5})` | `_canvas.ActiveTool` is `PointSequenceTool` |
| 2 | Cancel: < 2 points → ack cancelled | Tool active; operator submits 0 points | Invoke tool completion callback with empty array | `MapCommandAck.StatusCode == StatusCancelled` written to writer; `_commandGateway.CreateEntityAsync` NOT called |
| 3 | Success: gateway called with correct descriptors | Mock gateway returning success ACK with EntityId=77 | Tool completion with 3 valid points | `CreateEntityRequest` contains `dtEntityMaster(TkbType=TacGraphic_Route)`, `dtMapRoute`, `dtEntityInfo(CommanderId=vehicleId)`; `MissionControlRequest.Payload._d == CMD_REPLACE_MISSION`; task `BehaviorId == "FollowRoute"`; `BehaviorParams` contains `routeEntityId: 77` |
| 4 | Route creation failure → no mission sent | Mock gateway returning StatusCode=2 | Tool completion with valid points | `SendMissionControlRequestAsync` NOT called; failure `MapCommandAck` sent |
| 5 | Success: final ack sent | Mock gateway: both calls succeed | Full flow | `MapCommandAck.StatusCode == StatusFinished` written after both gateway calls complete |

---

## OC1-I001 — OrbatPanel Context Menu Infrastructure

**Design Reference:** [Phase 4.1 — OrbatPanel Context Menu Infrastructure](./OC1-DESIGN.md#41-orbatpanel-context-menu-infrastructure--oc1-i001)

### Scope
In `Hrot.ExCon/Panels/OrbatPanel.cs`:
1. After each entity `ImGui.Selectable` row, detect `ImGui.IsItemClicked(ImGuiMouseButton.Right)` and open a `BeginPopupContextItem` popup.
2. Inside the popup:
   - Always show: "Select entity", "Center on entity", "Delete".
   - Show only when `IsSimulatedEntity(node.EntityId, repo)` returns `true`: "Edit Route", "Abort Mission".
3. Add `private static bool IsSimulatedEntity(int entityId, IDerRepo repo)`:
   - Reads `EntityMaster` descriptor from `repo.TryGetDescriptor<EntityMaster>(entityId, out var em)`.
   - Returns `em.TkbType < 8000`.
   - Returns `false` if descriptor not found.

**Not in scope:** The actual action implementations (those are in OC1-I002 through OC1-I006).  This task only establishes the popup structure and `IsSimulatedEntity` helper.  The menu items can call `ImGui.MenuItem` but leave the callback bodies empty or with `// TODO` until the subsequent tasks fill them in.

### Constraints
- The context menu popup must be opened with a per-node unique ID (e.g., `$"##ctx_{node.EntityId}"`) to avoid ImGui ID collisions when multiple rows are visible.
- The `IsSimulatedEntity` threshold `< 8000` is a **placeholder** — if a dedicated `TkbCategory` flag or `IsMapGraphic()` helper already exists in `Hrot.Map.Common` or `Hrot.Map.Definitions`, use that instead.

### Success Conditions

**Test class:** `Hrot.ExCon.Tests/Panels/OrbatPanelContextMenuTests.cs`  
Tests use `ImGuiTestEngine` or a stub render harness consistent with other panel tests in the project.

| # | Scenario | Setup | Action | Assertion |
|---|----------|-------|--------|-----------|
| 1 | `IsSimulatedEntity` returns true for non-graphic | DerRepo with entity having `EntityMaster.TkbType = 1001` | Call `IsSimulatedEntity(id, repo)` | Returns `true` |
| 2 | `IsSimulatedEntity` returns false for route | DerRepo with entity having `EntityMaster.TkbType = 8802` | Call `IsSimulatedEntity(id, repo)` | Returns `false` |
| 3 | `IsSimulatedEntity` returns false for missing entity | Empty DerRepo | Call `IsSimulatedEntity(99, repo)` | Returns `false` |
| 4 | All three base items always rendered | Render ORBAT with any entity row | Right-click entity | "Select entity", "Center on entity", "Delete" items are present in context menu |
| 5 | Physical-only items shown for simulated entity | Entity with TkbType=1001 | Right-click entity | "Edit Route" and "Abort Mission" items are present |
| 6 | Physical-only items hidden for map graphic | Entity with TkbType=8802 | Right-click entity | "Edit Route" and "Abort Mission" items are NOT present |

---

## OC1-I002 — Select Entity Action

**Design Reference:** [Phase 4.2 — Select Entity](./OC1-DESIGN.md#42-select-entity--oc1-i002)

### Scope
1. In `Hrot.ExCon/IosLogic.cs`: Add `SendSetSelection(int entityId)` method:
   - Calls `SelectEntity(entityId)` locally (optimistic UI).
   - Publishes `MapCommandRequest(CMD_SET_SELECTION, {"entityId": id})` via `_commandWriter`.
2. Expose `SendSetSelection` on `IIosLogic` interface in `Hrot.ExCon/Abstractions/IIosLogic.cs`.
3. Wire the "Select entity" menu item in `OrbatPanel.Draw()` to call `logic.SendSetSelection(node.EntityId)`.

**Not in scope:** Changes to `SelectEntity` itself; changes to how `SelectionChangedEvent` is processed.

### Constraints
- `_commandWriter` may be null in unit tests.  Guard with `_commandWriter?.Write(...)`.
- JSON must use `{"entityId": id}` with integer (not string) value.

### Success Conditions

**Test class:** `Hrot.ExCon.Tests/Panels/OrbatPanelContextMenuTests.cs` (extend) or `Hrot.ExCon.Tests/IosLogicSelectTests.cs`

| # | Scenario | Setup | Action | Assertion |
|---|----------|-------|--------|-----------|
| 1 | Local selection applied immediately | `IosLogic` with mock writer | Call `SendSetSelection(42)` | `logic.SelectedEntityId == 42` |
| 2 | DDS command published | `IosLogic` with captured writer | Call `SendSetSelection(42)` | Writer received `MapCommandRequest` with `Type == CMD_SET_SELECTION`, `CommandArgsJson` contains `"entityId":42` |
| 3 | No crash if writer null | `IosLogic` constructed without `_commandWriter` | Call `SendSetSelection(7)` | No exception; `SelectedEntityId == 7` |

---

## OC1-I003 — Center on Entity Action

**Design Reference:** [Phase 4.3 — Center on Entity](./OC1-DESIGN.md#43-center-on-entity--oc1-i003)

### Scope
1. In `IosLogic.cs`: Add `CenterOnEntity(int entityId)`:
   - Publishes `MapCommandRequest(CMD_SET_VIEW, {"entityId": id})`.
2. Expose on `IIosLogic`.
3. Wire the "Center on entity" menu item in `OrbatPanel`.

**Not in scope:** Reading coordinates from `IDerRepo`; any local camera state.

### Constraints
- IOS must **not** read entity coordinates.  Only the entity ID is transmitted.
- Guard `_commandWriter?.Write(...)`.

### Success Conditions

**Test class:** `Hrot.ExCon.Tests/Panels/OrbatPanelContextMenuTests.cs`

| # | Scenario | Setup | Action | Assertion |
|---|----------|-------|--------|-----------|
| 1 | DDS command published | `IosLogic` with captured writer | Call `CenterOnEntity(15)` | Writer received `MapCommandRequest` with `Type == CMD_SET_VIEW`, `CommandArgsJson` contains `"entityId":15` |
| 2 | No coordinates in payload | Same setup | Call `CenterOnEntity(15)` | `CommandArgsJson` does NOT contain `"lat"` or `"lon"` keys |
| 3 | No crash if writer null | Writer null | Call `CenterOnEntity(15)` | No exception |

---

## OC1-I004 — Delete Action

**Design Reference:** [Phase 4.4 — Delete](./OC1-DESIGN.md#44-delete--oc1-i004)

### Scope
1. In `IosLogic.cs`:
   - Add `HashSet<int> _pendingDeleteEntityIds`.
   - Add `DeleteEntity(int entityId)` method: publishes `DeleteEntityRequest(RequestId=Guid.NewGuid(), EntityId=entityId)`, adds `entityId` to `_pendingDeleteEntityIds`.
   - In `ProcessEntityCreationAcks` (which already drains `CreateUpdateDeleteEntityAck`): when a Success/Failure ACK arrives for a request that corresponds to a pending delete (detect via entity ID match), remove from `_pendingDeleteEntityIds`; on Failure, set a `GlobalAlert`.
2. In `IIosLogic`: add `bool IsEntityPendingDelete(int entityId)`.
3. In `OrbatPanel.Draw()`: disable the row (using `ImGui.BeginDisabled/EndDisabled`) when `logic.IsEntityPendingDelete(node.EntityId)`.
4. Wire the "Delete" menu item to call `logic.DeleteEntity(node.EntityId)`.

**Not in scope:** Changes to the `DeleteEntityRequest` DDS message itself (assumed already available from the two-ack workstream); changes to `GlobalAlert` display (assumed already wired in `IosMock.cs`).

### Constraints
- The ACK correlation for deletes must be by **entity ID** (not request GUID), because the `CreateUpdateDeleteEntityAck` carries `EntityId` in its payload.  Track which entity IDs are pending delete; match ACKs by entity ID.
- The `ProcessEntityCreationAcks` method already handles creation ACKs.  Extend it to also handle delete ACKs without breaking creation ACK processing.  Use the ACK's operational context (e.g., a `DeleteEntityRequest` vs `CreateEntityRequest` flag, or the absence of a matching pending-creation entry) to distinguish the two cases.

### Success Conditions

**Test class:** `Hrot.ExCon.Tests/IosLogicDeleteTests.cs`

| # | Scenario | Setup | Action | Assertion |
|---|----------|-------|--------|-----------|
| 1 | Delete publishes request | `IosLogic` with captured DDS writer | Call `DeleteEntity(5)` | `DeleteEntityRequest` published with `EntityId == 5` |
| 2 | Entity marked pending | Same | Call `DeleteEntity(5)` | `logic.IsEntityPendingDelete(5) == true` |
| 3 | Success ACK clears pending | After `DeleteEntity(5)`, inject success `CreateUpdateDeleteEntityAck(EntityId=5, StatusCode=Success)` | Process ack | `logic.IsEntityPendingDelete(5) == false`; no `GlobalAlert` |
| 4 | Failure ACK clears pending and sets alert | After `DeleteEntity(5)`, inject failure ACK | Process ack | `logic.IsEntityPendingDelete(5) == false`; `logic.GlobalAlert != null` |
| 5 | Non-pending entity ACK ignored | No prior `DeleteEntity` call | Inject ACK for entity 99 | No state change; no alert |

---

## OC1-I005 — Edit Route Action (Physical Entities Only)

**Design Reference:** [Phase 4.5 — Edit Route](./OC1-DESIGN.md#45-edit-route--oc1-i005)

### Scope
1. In `IosLogic.cs`: Add `StartPersonalRouteAuthoring(int vehicleEntityId)`:
   - Generates `ActiveContextId = Guid.NewGuid()`.
   - Publishes `MapCommandRequest(CMD_DRAW_PERSONAL_ROUTE, {"contextId": ActiveContextId.ToString("N"), "entityId": vehicleEntityId})`.
2. Expose on `IIosLogic`.
3. Wire the "Edit Route" menu item in `OrbatPanel` (only visible when `IsSimulatedEntity`, implemented in OC1-I001) to call `logic.StartPersonalRouteAuthoring(node.EntityId)`.

**Not in scope:** IOS-side ACK monitoring for this command (the `MapCommandAck` pipeline for authoring commands already exists in `ProcessMapCommandAcks`; no changes needed there).

### Constraints
- `contextId` in the JSON must use `ToString("N")` (no dashes) to match the format used by other authoring commands in the codebase.
- This action should be callable even if the entity currently has an active mission—the IG orchestration sends `CMD_REPLACE_MISSION`, which replaces any existing behavior.
- Guard `_commandWriter?.Write(...)`.

### Success Conditions

**Test class:** `Hrot.ExCon.Tests/Panels/OrbatPanelContextMenuTests.cs`

| # | Scenario | Setup | Action | Assertion |
|---|----------|-------|--------|-----------|
| 1 | Correct command type published | `IosLogic` with captured writer | Call `StartPersonalRouteAuthoring(10)` | Writer received `MapCommandRequest` with `Type == CMD_DRAW_PERSONAL_ROUTE` |
| 2 | Payload contains entity ID | Same | Call `StartPersonalRouteAuthoring(10)` | `CommandArgsJson` contains `"entityId":10` |
| 3 | Payload contains context ID | Same | Call `StartPersonalRouteAuthoring(10)` | `CommandArgsJson` contains a non-empty `"contextId"` key |
| 4 | ActiveContextId updated | Same | Call `StartPersonalRouteAuthoring(10)` | `logic.ActiveContextId != Guid.Empty` |
| 5 | Not shown for map graphic in panel | ORBAT with entity TkbType=8802 | Render context menu | "Edit Route" menu item not rendered |

---

## OC1-I006 — Abort Mission Action (Physical Entities Only)

**Design Reference:** [Phase 4.6 — Abort Mission](./OC1-DESIGN.md#46-abort-mission--oc1-i006)

### Scope
1. Wire the "Abort Mission" context menu item in `OrbatPanel.Draw()` (only visible when `IsSimulatedEntity`, from OC1-I001) to call the existing `logic.MissionEditorService.SendControlCommandAsync(node.EntityId, eMissionCommandType.CMD_ABORT_ALL, Guid.Empty)`.
2. No new methods on `IIosLogic` are required (the `MissionEditorService` is already accessible there).

**Not in scope:** Propagating the abort to subordinate entities; any UI feedback beyond what the existing mission panel already shows when a mission is aborted.

### Constraints
- `SendControlCommandAsync` is fire-and-forget from the UI; it is an `async void`-compatible call the panel already uses for other mission actions.
- This action must never appear for map graphic entities (TkbType ≥ 8000).  Gated by `IsSimulatedEntity` added in OC1-I001.
- There is **no** subordinate propagation.  The action only targets `node.EntityId` directly.

### Success Conditions

**Test class:** `Hrot.ExCon.Tests/Panels/OrbatPanelContextMenuTests.cs`

| # | Scenario | Setup | Action | Assertion |
|---|----------|-------|--------|-----------|
| 1 | `SendControlCommandAsync` called with correct args | `IosLogic` with mock `IMissionEditorService` | Invoke "Abort Mission" menu item for entity 7 | `missionEditorService.SendControlCommandAsync(7, CMD_ABORT_ALL, Guid.Empty)` was called |
| 2 | Not shown for map graphic | ORBAT with entity TkbType=8802 | Render context menu | "Abort Mission" item not rendered |
| 3 | Not called for subordinates | ORBAT with parent entity 5 having child entity 6 | Invoke "Abort Mission" for entity 5 | Only one `SendControlCommandAsync` call (for entity 5); no call for entity 6 |
