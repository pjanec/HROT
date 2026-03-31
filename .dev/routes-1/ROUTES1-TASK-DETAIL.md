# Routes-1 Task Details

**Workstream:** ROUTES1  
**Design Reference:** [ROUTES1-DESIGN.md](./ROUTES1-DESIGN.md)  
**Tracker:** [ROUTES1-TASK-TRACKER.md](./ROUTES1-TASK-TRACKER.md)

> ⚠️ This document provides the **complete task specification** for every implementation task.
> Task descriptions reference chapters in the design document to avoid redundancy.
> Together, the two documents provide everything a developer needs to implement and verify their work.

---

## Phase 1 — Core ECS Data Layer

---

### ROUTES1-T001 — RoutePlan Managed Component

**Design ref:** [§4 Core Data Model](./ROUTES1-DESIGN.md#4-core-data-model--routeplan-ecs-component)

**Scope:** `Hrot.Map.Common` project (to avoid circular dependencies, parallel to `EditablePolyline.cs`).

**What to implement:**

1. Define `RouteWaypoint` struct with fields:
   - `Vector3 Position` — absolute Cartesian world-space position
   - `float TargetSpeed` — desired speed in m/s (0 = use entity default)
   - `string ExtensionJson` — nullable; AI soft advice JSON blob

2. Define `RoutePlan` managed class:
   - `List<RouteWaypoint> Waypoints` (initialised to `new()`)
   - `bool IsLoop`
   - `int Version` (incremented on each mutation; reactive systems compare this)
   - Decorated with `[ComponentId(GlobalComponentIds.RoutePlan)]`

3. Reserve a new entry in `GlobalComponentIds` (e.g. `RoutePlan = 220` — choose next available id after inspecting the file).

4. Register `RoutePlan` as a managed component inside both:
   - `Hrot.SimHost` component registration bootstrap
   - `Hrot.IG` component registration bootstrap

**Success conditions / tests (`Hrot.Map.Common.Tests` or `Hrot.SimHost.Tests`):**

- `RoutePlan` can be created with `new RoutePlan()` — `Waypoints` is not null, `IsLoop` defaults to `false`, `Version` defaults to `0`.
- Adding a waypoint and incrementing `Version` produces observable mutation without exceptions.
- `RouteWaypoint` is a value type (struct); confirm no heap allocation per waypoint when stored in a `List<RouteWaypoint>`.
- Component round-trip: create an ECS world, spawn an entity, `SetManagedComponent<RoutePlan>(entity, plan)`, retrieve with `GetManagedComponent<RoutePlan>(entity)`, verify equality of all waypoint fields including `ExtensionJson`.

---

### ROUTES1-T002 — Supporting Components and Events

**Design ref:** [§4.3 PersonalRouteRef](./ROUTES1-DESIGN.md#43-personalrouteref-component), [§4.4 CmdAppendPersonalWaypoint](./ROUTES1-DESIGN.md#44-cmdappendpersonalwaypoint-event), [§7.2 RouteTrajectoryCache](./ROUTES1-DESIGN.md#72-routetrajectorycache-component)

**Scope:** `Hrot.Map.Common` (components) / appropriate shared assembly (events).

**What to implement:**

1. `PersonalRouteRef` blittable struct:
   - `Entity RouteEntity`
   - `[ComponentId(GlobalComponentIds.PersonalRouteRef)]`
   - Reserve new `GlobalComponentIds` entry.

2. `RouteTrajectoryCache` blittable struct:
   - `int TrajectoryId`
   - `int CompiledVersion`
   - `[ComponentId(GlobalComponentIds.RouteTrajectoryCache)]`
   - Reserve new `GlobalComponentIds` entry.

3. `CmdAppendPersonalWaypoint` blittable event struct in the command/event layer:
   - `Entity VehicleEntity`
   - `Vector3 WorldPosition`

**Success conditions / tests:**

- Each component struct is blittable (confirmed by `IsBlittable<T>()` helper or `UnsafeUtility.IsBlittable`).
- `PersonalRouteRef.RouteEntity` defaults to `Entity.Null`.
- `RouteTrajectoryCache.TrajectoryId` defaults to `0` (i.e. uncompiled).
- `CmdAppendPersonalWaypoint` can be enqueued and dequeued on an ECS event bus without data corruption (field values survive round-trip through the buffer).

---

### ROUTES1-T003 — TKB Blueprint for TacGraphic_Route

**Design ref:** [§14 TKB Blueprint for TacGraphic_Route](./ROUTES1-DESIGN.md#14-tkb-blueprint-for-tacgraphic_route)

**Scope:** TKB definition database wiring in `Hrot.Map.Definitions` and the SimHost / IG entity factory bootstraps.

**What to implement:**

1. Locate `TkbEntityTypes.TacGraphic_Route = 8802` (already defined in `Hrot.Map.Definitions/TkbEntityTypes.cs`).
2. Create (or extend) a TKB blueprint entry for type `8802` that attaches:
   - `TkbIdentity { TkbType = TacGraphic_Route }`
   - `SimTransform` (default identity)
   - `NetworkIdentity`
   - `NetworkAuthority` (server-side authoritative)
   - `RoutePlan` managed component
   - **Does not** include `EditablePolyline`, `NavState`, or any locomotion components.

3. Verify the `road_graphs` map layer predicate in `MapLayerRegistry` already matches this TKB type (it should — the predicate `TkbType == 8802` already exists). No code change needed there.

**Success conditions / tests (`Hrot.SimHost.Tests` or integration):**

- A `SpawnEntityCommand` with `TkbType = TacGraphic_Route` results in an ECS entity that has `RoutePlan`, `TkbIdentity`, `SimTransform`, `NetworkIdentity`, and `NetworkAuthority`.
- The spawned entity does **not** have `EditablePolyline` or `NavState`.
- The entity's `TkbIdentity.TkbType == 8802`.
- The spawned entity is visible through the `road_graphs` layer predicate without any additional setup.

---

## Phase 2 — DDS Replication

---

### ROUTES1-T004 — MapRouteEgressTranslator

**Design ref:** [§6.1 MapRouteEgressTranslator](./ROUTES1-DESIGN.md#61-maprouteegresstranslator-simhost--ig)

**Scope:** `Hrot.Map.Common` (parallel to `MapVisualOverlayEgressTranslator.cs`).

**What to implement:**

Create `MapRouteEgressTranslator` class. It runs in `SystemPhase.Egress`:

1. Queries entities with `NetworkIdentity` + `SimTransform` + `RoutePlan` (+ `NetworkAuthority` flag when on SimHost).
2. Uses `SmartEgressUtil.IsDirty(entity, routePlan.Version)` to skip unchanged entities.
3. For each dirty entity:
   a. Iterate `RoutePlan.Waypoints`.
   b. Convert each `RouteWaypoint.Position` (Cartesian `Vector3`) → `GeoPoint` via `IGeographicTransform.ToGeodetic`.
   c. Build `MapRoute { EntityId = netId, Points = [Waypoint(GeoPoint, Name="", SpeedMetersPerSec, ExtensionJson)], IsLoop }`.
   d. Publish via DDS writer.
   e. If `waypoints[0]` position changed, also publish a `WorldPos` update (first waypoint = spatial anchor).
4. Mark entity as clean via `SmartEgressUtil.MarkClean`.

**Success conditions / tests (`Hrot.Map.Common.Tests`):**

- Given an entity with a `RoutePlan` containing 3 waypoints, the translator emits exactly one `MapRoute` publish call with `Points.Count == 3`.
- `GeoPoint` values round-trip through `ToGeodetic` within acceptable floating-point tolerance (≤ 1 mm error for positions within 100 km of origin).
- `IsLoop`, `SpeedMetersPerSec`, and `ExtensionJson` are faithfully propagated.
- If `RoutePlan.Version` has not changed since last emit, no publish is made (dirty-flag test).
- An entity without `RoutePlan` is not processed.

---

### ROUTES1-T005 — MapRouteIngressTranslator

**Design ref:** [§6.2 MapRouteIngressTranslator](./ROUTES1-DESIGN.md#62-maprouteingresstranslator-simhost--ig)

**Scope:** `Hrot.Map.Common` (parallel to `MapVisualOverlayIngressTranslator.cs`).

**What to implement:**

Create `MapRouteIngressTranslator` class. It runs in `SystemPhase.Ingress`:

1. Drains incoming `MapRoute` DDS samples.
2. For each sample, looks up the entity by `EntityId` in `NetworkEntityMap`.
3. If entity not yet found (not yet spawned), defers the sample to a retry queue (same pattern as `MapVisualOverlayIngressTranslator`).
4. Gets the `RoutePlan` managed component from the entity.
5. Clears `RoutePlan.Waypoints`, then for each incoming `Waypoint`:
   a. Converts `GeoPoint` → Cartesian `Vector3` via `IGeographicTransform.ToCartesian`.
   b. Appends `RouteWaypoint { Position, TargetSpeed = (float)SpeedMetersPerSec, ExtensionJson }`.
6. Sets `RoutePlan.IsLoop` from the descriptor.
7. Increments `RoutePlan.Version`.

**Success conditions / tests (`Hrot.Map.Common.Tests`):**

- A `MapRoute` sample with 5 waypoints results in a `RoutePlan` with exactly 5 `RouteWaypoint` entries.
- `ToCartesian` values are within 1 mm of the original positions used to create the message via `ToGeodetic` (round-trip precision test using `IGeographicTransform` with a fixed origin).
- `IsLoop`, `TargetSpeed`, and `ExtensionJson` are faithfully propagated.
- `RoutePlan.Version` is incremented on each processed sample.
- Receiving a sample for an unknown `EntityId` does not throw; it is deferred and processed once the entity becomes available.

---

## Phase 3 — Trajectory Pool Integration

---

### ROUTES1-T006 — RouteTrajectorySyncSystem

**Design ref:** [§7.1 RouteTrajectorySyncSystem](./ROUTES1-DESIGN.md#71-routetrajectorysyncystem), [§7.2 RouteTrajectoryCache](./ROUTES1-DESIGN.md#72-routetrajectorycache-component)

**Scope:** `Hrot.SimHost` (and optionally `Hrot.IG` if local trajectory sampling is needed there too).

**What to implement:**

1. Create `RouteTrajectorySyncSystem` in `SystemPhase.BeforeSync` (after ingress translators; before kinematics).

2. Query entities with `RoutePlan` and optionally `RouteTrajectoryCache`.

3. For each entity:
   - If `RouteTrajectoryCache` is absent, add it with default values.
   - If `routePlan.Version != cache.CompiledVersion`:
     - If `cache.TrajectoryId > 0`, call `TrajectoryPoolManager.RemoveTrajectory(cache.TrajectoryId)`.
     - Convert `RoutePlan.Waypoints` to a `Vector2[]` (use `XZ` coordinates — matching the pool's 2D XZ convention used by `CarKinematicsSystem`). Include per-waypoint target speeds.
     - Call `TrajectoryPoolManager.RegisterTrajectory(points, speeds, interpolation: CatmullRom, isLoop)` → `int newId`.
     - Set `cache.TrajectoryId = newId`, `cache.CompiledVersion = routePlan.Version`.
     - Write `cache` back via `SetComponent`.

4. Register lifecycle cleanup: when a route entity is destroyed, call `TrajectoryPoolManager.RemoveTrajectory(cache.TrajectoryId)` to free the native array. Implement via an `IEntityLifecycleListener` or a `PostSimulation` cleanup query that scans recently-destroyed caches.

**Success conditions / tests (`Hrot.SimHost.Tests`):**

- Given a route entity with a `RoutePlan` of 4 waypoints (version=1), the system registers a trajectory and populates `RouteTrajectoryCache.TrajectoryId` with a positive integer.
- `RouteTrajectoryCache.CompiledVersion` equals `RoutePlan.Version` after sync.
- Mutating `RoutePlan` (appending a waypoint, incrementing version to 2) causes the old trajectory to be removed and a new one registered.
- Destroying the route entity triggers `RemoveTrajectory` — the trajectory id is no longer present in the pool after the next simulation tick.
- An entity with 0 waypoints does not cause an exception (graceful no-op or registers a degenerate trajectory).

---

## Phase 4 — Shared Route Authoring

---

### ROUTES1-T007 — Shared Route Authoring via CMD_START_AUTHORING

**Design ref:** [§9 Authoring Flow — Shared Routes](./ROUTES1-DESIGN.md#9-authoring-flow--shared-routes)

**Scope:** `Hrot.IG` — `MapCommandController`, `IgApplication` route context, `PointSequenceTool` configuration.

**What to implement:**

1. In `IgApplication` / `MapCommandController`, handle `CommandType.CMD_START_AUTHORING` with a `CommandArgsJson` that specifies `TkbType = TacGraphic_Route`.

2. Push a `PointSequenceTool` configured for route authoring:
   - `_onFinish` callback receives the array of world-space `Vector2` points.
   - Convert each point to an absolute geodetic `GeoPoint` via `IGeographicTransform.ToGeodetic(new Vector3(pt.x, 0, pt.y))`.
   - Build a `CreateEntityRequest`:
     - `dtEntityMaster { TkbType = TkbEntityTypes.TacGraphic_Route }`
     - `dtWorldPos { GeoPoint = points[0] }` (first waypoint as spatial anchor)
     - `dtMapRoute { Points = [all waypoints], IsLoop = false }`
   - Publish the request via the DDS command channel.

3. After publishing, switch back to the default `StandardInteractionTool`.

4. Ensure the `CMD_START_EDITING` path for route entities pushes the `RouteEditTool` (see T012) instead of the generic `EditTool`.

**Success conditions / tests (`Hrot.IG.Tests`):**

- Invoking the `CMD_START_AUTHORING` handler with route args pushes a `PointSequenceTool` onto the map canvas.
- Simulating the finish callback with 3 points results in a `CreateEntityRequest` being emitted that contains all three descriptors (`dtEntityMaster`, `dtWorldPos`, `dtMapRoute`).
- `dtEntityMaster.TkbType == TkbEntityTypes.TacGraphic_Route`.
- `dtMapRoute.Points.Count == 3`.
- `dtWorldPos` position matches the geodetic conversion of `points[0]`.
- After the finish callback, the active tool reverts to `StandardInteractionTool`.

---

## Phase 5 — Personal Route Authoring

---

### ROUTES1-T008 — PersonalRouteAuthoringSystem

**Design ref:** [§10 Authoring Flow — Personal Routes](./ROUTES1-DESIGN.md#10-authoring-flow--personal-vehicle-owned-routes)

**Scope:** `Hrot.SimHost` (ECS system, `SystemPhase.Input`).

**What to implement:**

Create `PersonalRouteAuthoringSystem`:

1. Drain `CmdAppendPersonalWaypoint` events from the event bus.

2. For each event:
   a. Look up the vehicle entity. If not alive, skip.
   b. Check if `PersonalRouteRef` exists on the vehicle.
   
   **Case A — No existing personal route:**
   - Spawn a new child route entity via `EntityCommandBuffer`:
     - Components: `RoutePlan` (managed), `PartMetadata { ParentEntity = vehicleEntity }`, `TkbIdentity { TkbType = TacGraphic_Route }`, `SimTransform` (positioned at vehicle's current position).
     - Seed `RoutePlan.Waypoints` with:
       - `[0]` = vehicle `SimTransform.Position` (current absolute world pos)
       - `[1]` = `CmdAppendPersonalWaypoint.WorldPosition`
     - Set `RoutePlan.Version = 1`.
   - Attach `PersonalRouteRef { RouteEntity = newChildEntity }` to the vehicle.
   
   **Case B — Personal route already exists:**
   - Retrieve the child route entity via `PersonalRouteRef`.
   - Append `WorldPosition` to `RoutePlan.Waypoints`.
   - Increment `RoutePlan.Version`.
   
   c. In both cases, after the entity command buffer is flushed and `RouteTrajectorySyncSystem` has run:
   - Issue `CmdFollowTrajectory { VehicleEntity, TrajectoryId = routeCache.TrajectoryId }` to the vehicle.

> **Note:** `CmdFollowTrajectory` must be issued in the next frame after trajectory compilation, or the system must ensure `RouteTrajectorySyncSystem` runs first in the same frame. Design the ordering accordingly.

**Success conditions / tests (`Hrot.SimHost.Tests`):**

- Dispatching `CmdAppendPersonalWaypoint` for a vehicle with no existing personal route spawns exactly one new child entity with `RoutePlan` (2 waypoints: vehicle pos + clicked pos) and `PartMetadata.ParentEntity == vehicleEntity`.
- `PersonalRouteRef` is attached to the vehicle after the first command.
- Dispatching a second `CmdAppendPersonalWaypoint` for the same vehicle appends one more waypoint to the existing route entity (total 3 waypoints); no new entity is spawned.
- `RoutePlan.Version` increments on each append.
- Destroying the parent vehicle entity causes the child route entity to be destroyed by `SubEntityCleanupSystem` within one simulation tick.
- Events for dead/unknown vehicle entities are silently ignored.

---

### ROUTES1-T009 — IG Input Wiring for Shift+Right-Click

**Design ref:** [§10](./ROUTES1-DESIGN.md#10-authoring-flow--personal-vehicle-owned-routes)

**Scope:** `Hrot.IG` — `IgApplication` `OnWorldClick` handler (or `StandardInteractionTool` wrapper).

**What to implement:**

1. In `IgApplication`'s `OnWorldClick` subscription:
   - When `shift == true` AND `button == RightMouseButton` AND one or more vehicle entities are selected (`IInspectorContext.SelectedEntities`):
     - For each selected vehicle entity:
       - Convert the 2D `worldPos` (canvas / map space) to a 3D absolute Cartesian `Vector3` (set Y/altitude from entity's current `SimTransform.Position.Y` or 0).
       - Publish `CmdAppendPersonalWaypoint { VehicleEntity = entity, WorldPosition = pos }` via the DDS command channel.
   - The existing non-shift right-click path (`SetDestination`) must remain unaffected.

2. Remove (or guard) the old `ScenarioManager.AddWaypoint` call from this path.

**Success conditions / tests (`Hrot.IG.Tests`):**

- A simulated Shift+Right-Click with one vehicle entity selected emits exactly one `CmdAppendPersonalWaypoint` with the correct `VehicleEntity` and `WorldPosition`.
- Two selected vehicle entities produce two `CmdAppendPersonalWaypoint` events.
- A plain (non-shift) right-click does **not** emit `CmdAppendPersonalWaypoint`.
- A shift right-click with no vehicle selected does not emit a command and does not throw.

---

## Phase 6 — Rendering

---

### ROUTES1-T010 — RouteRenderLayer

**Design ref:** [§8.1 RouteRenderLayer](./ROUTES1-DESIGN.md#81-routerenderlayer-ig)

**Scope:** `Hrot.IG` — new `IMapLayer` registered in `MapLayerRegistry`.

**What to implement:**

Create `RouteRenderLayer : IMapLayer`:

1. `LayerBitIndex` = (reuse `RoadGraphsBit = 4`); the layer predicate in `MapLayerRegistry` already filters by `TkbType == TacGraphic_Route`.
2. In `Draw(IEcsReadView view, MapCanvas canvas, IInspectorContext inspector)`:
   - Query entities visible in the viewport whose `TkbIdentity.TkbType == TacGraphic_Route`.
   - For each entity: retrieve `RoutePlan` managed component.
   - Convert each `RouteWaypoint.Position` (`Vector3`) → 2D screen `Vector2` via `canvas.WorldToScreen`.
   - Draw line segments between consecutive screen positions (use `DrawList.AddLine` or equivalent).
   - Draw a small filled circle at each waypoint handle (radius 5 px).
   - **Normal colour:** medium blue (e.g. `#4488FF`).
   - **Selected colour:** bright yellow (e.g. `#FFD700`) when `inspector.SelectedEntity == routeEntity`.
   - **IsLoop:** if `RoutePlan.IsLoop == true`, draw an additional closing segment from last waypoint back to first.
3. Register the layer in `MapLayerRegistry` at the appropriate position (after road graph layer).

**Success conditions / tests (`Hrot.IG.Tests`):**

- Given a route entity with 4 waypoints and the `road_graphs` layer enabled, `Draw` invokes at least 3 `AddLine` calls (n-1 segments) and 4 circle draw calls.
- For a looping route, exactly 4 `AddLine` calls are made (n segments including wrap-around).
- When the route entity is selected, the draw calls use the highlight colour.
- When the `road_graphs` layer bit is off, no draw calls are made for route entities.
- No exceptions when processing an entity with 0 or 1 waypoints.

---

### ROUTES1-T011 — SimHostTrajectoryLayer Extension

**Design ref:** [§8.2 SimHostTrajectoryLayer Extension](./ROUTES1-DESIGN.md#82-simhosttrajectorylay-extension)

**Scope:** `Hrot.SimHost` — `SimHostTrajectoryLayer.cs`.

**What to implement:**

Extend `SimHostTrajectoryLayer.Draw`:

1. Existing path (unchanged): if selected entity has `NavState.Mode == CustomTrajectory` and a trajectory in the pool, draw the raw trajectory segments + progress circle.

2. New path: additionally check if the selected entity has `PersonalRouteRef`:
   - If yes, retrieve the child route entity's `RoutePlan` and draw its waypoints as an overlay (e.g. in orange) with a distinct colour for the personal route context.

3. New path: if selected entity's `NavState.TrajectoryId` matches `RouteTrajectoryCache.TrajectoryId` on any known route entity, draw the shared route entity's `RoutePlan` waypoints in a highlight colour.

**Success conditions / tests (`Hrot.SimHost.Tests`):**

- A vehicle with `NavState.Mode == CustomTrajectory` and a corresponding personal child route causes the trajectory layer to make draw calls for the route waypoints.
- A vehicle following a shared route causes draw calls for the shared route's waypoints.
- A vehicle with `NavState.Mode == None` (not following a route) causes no draw calls.
- Existing unit tests for `SimHostTrajectoryLayer` still pass (non-regression).

---

## Phase 7 — Editing

---

### ROUTES1-T012 — RouteEditTool

**Design ref:** [§11.1 RouteEditTool](./ROUTES1-DESIGN.md#111-routeediittool-ig)

**Scope:** `Hrot.IG/Tools/RouteEditTool.cs` — new `IMapTool` implementation.

**What to implement:**

1. `RouteEditTool(Entity routeEntity, RoutePlan plan, IMapCanvas canvas, Action<Entity, List<RouteWaypoint>> onCommit)`.
2. `OnEnter`: snapshot `plan.Waypoints` into `List<RouteWaypoint> _ghost`. Convert positions to screen using `canvas.WorldToScreen`. Set `_selectedVertexIndex = -1`.
3. `HandleHover(Vector2 screen)`: highlight nearest vertex if within pick radius (15 world units). Highlight nearest segment midpoint (insert affordance).
4. `HandleClick(Vector2 world, MouseButton btn, bool shift)`:
   - Left: attempt vertex select (`FindNearestVertex`). If no vertex in range, check segment (point-to-segment distance). If on segment `[i, i+1]`, insert new waypoint at `i+1` (inheriting `TargetSpeed` and `ExtensionJson` from waypoint `i`).
   - Right: commit — call `onCommit(routeEntity, _ghost)` and signal tool should be popped.
5. `HandleDrag(Vector2 worldDelta)`: translate `_ghost[_selectedVertexIndex].Position` by the drag delta (converted from screen to world space).
6. `HandleKeyPressed(KeyboardKey key)`:
   - `Delete`: remove `_ghost[_selectedVertexIndex]`, clamp index.
   - `Escape`: cancel, pop tool without committing.
7. `GetSelectedWaypointRef() ref RouteWaypoint`: returns a `ref` to `_ghost[_selectedVertexIndex]` for use by `WaypointEditorPanel`.
8. `Draw(MapCanvas)`: render ghost waypoints + line segments + vertex handles; highlight selected vertex.

**Success conditions / tests (`Hrot.IG.Tests`):**

- `OnEnter` with a 3-waypoint route results in `_ghost.Count == 3`.
- Left-clicking near waypoint index 1 (within 14 world units) sets `SelectedVertexIndex == 1`.
- Left-clicking on the midpoint of segment [0→1] (outside vertex radius) inserts a new point at index 1; `_ghost.Count == 4`.
- Pressing `Delete` with `SelectedVertexIndex == 1` removes the waypoint; `_ghost.Count == 2` (was 3).
- `HandleDrag` with a delta of `(10, 0, 5)` translates the selected waypoint's `Position` by that delta.
- Right-click fires `onCommit` with the current ghost state.
- `Escape` does **not** fire `onCommit`.

---

### ROUTES1-T013 — WaypointEditorPanel

**Design ref:** [§11.2 WaypointEditorPanel](./ROUTES1-DESIGN.md#112-waypointeditorpanel-ig-imgui)

**Scope:** `Hrot.IG` — new ImGui panel class referenced by the IG main UI draw loop.

**What to implement:**

1. Create `WaypointEditorPanel` class with a reference to `IMapCanvas _canvas`.
2. In the `Draw()` method (called within `rlImGui.Begin()`):
   - Check `_canvas.ActiveTool is RouteEditTool routeTool && routeTool.SelectedVertexIndex >= 0`.
   - If true: render an `ImGui.Begin("Waypoint Editor")` window containing:
     - Read-only position display: `ImGui.LabelText("Position", wp.Position.ToString())`.
     - `ImGui.InputFloat("Target Speed (m/s)", ref speed)` — updates `wp.TargetSpeed`.
     - `ImGui.InputTextMultiline("AI Advice (JSON)", ref json, 2048, ...)` — updates `wp.ExtensionJson`.
   - If no vertex selected: render grayed-out placeholder text "Select a waypoint to edit its properties."
3. Register panel in IG's UI render loop, analogous to existing panels.

**Success conditions / tests (`Hrot.IG.Tests`):**

- When `RouteEditTool.SelectedVertexIndex == -1`, the panel renders the placeholder text (no crash).
- When `SelectedVertexIndex == 0`, the panel renders input controls; mutating the speed field updates `routeTool.GetSelectedWaypointRef().TargetSpeed` immediately (same reference).
- Calling `Draw()` with no active `RouteEditTool` does not throw.

---

## Phase 8 — AI Soft Advice

---

### ROUTES1-T014 — RouteContextSystem

**Design ref:** [§13 AI Soft Advice Pipeline](./ROUTES1-DESIGN.md#13-ai-soft-advice-pipeline)

**Scope:** `Hrot.SimHost` — new ECS system, `SystemPhase.Simulation` (low-frequency, e.g. every 0.5 s).

**What to implement:**

1. Create `RouteContextSystem`. Run at reduced frequency (configurable fixed interval, default 0.5 s).

2. Query entities with `NavState` (mode `CustomTrajectory`) + `BrainBlackboard` + either `PersonalRouteRef` or a resolvable `RouteTrajectoryCache` match.

3. For each such vehicle entity:
   a. Resolve the `RoutePlan` (from personal route or by scanning shared route entities whose `RouteTrajectoryCache.TrajectoryId == navState.TrajectoryId`).
   b. Determine the `currentSegmentIndex` from `navState.ProgressS` by walking the `RoutePlan.Waypoints` and comparing cumulative distances (approximated as Euclidean segment lengths).
   c. Read `RoutePlan.Waypoints[currentSegmentIndex].ExtensionJson`. If null or empty, skip.
   d. Parse the JSON (using `System.Text.Json.JsonDocument` or a lightweight parser).
   e. For each recognised key, write the value to the vehicle's `BrainBlackboard.Memory` at a designated offset defined in `BlackboardOffsets` constants:
      - `"dangerLevel"` → `BlackboardOffsets.ExpectedThreatLevel` (byte)
      - Additional keys can be added incrementally.

4. Define a `BlackboardOffsets` static class (in the appropriate behavior constants file) that lists the byte offset constants.

**Success conditions / tests (`Hrot.SimHost.Tests`):**

- Given a vehicle with `NavState.ProgressS` placing it at segment index 1 of a 3-waypoint route where waypoint[1].`ExtensionJson = '{"dangerLevel":2}'`, the system writes `(byte)2` to `blackboard.Memory[BlackboardOffsets.ExpectedThreatLevel]`.
- A vehicle not on a route (or with an empty `ExtensionJson`) leaves the blackboard unchanged.
- Malformed JSON does not throw — the system logs a warning and skips the entry.
- System executes at most once every `TickIntervalSeconds` (frequency throttle test: two consecutive simulation ticks within the interval result in only one blackboard write).

---

## Phase 9 — Legacy Deprecation

---

### ROUTES1-T015 — Remove Legacy Waypoint Queue

**Design ref:** [§15 Deprecation of Legacy Waypoint Queue](./ROUTES1-DESIGN.md#15-deprecation-of-legacy-waypoint-queue)

**Scope:** `Hrot.SimHost/UI/SimHostScenarioManager.cs` and `Hrot.IG`'s `IgApplication` shift-right-click handler.

**Prerequisite:** Tasks T008 and T009 must be complete and verified. The personal route authoring flow must be fully operational end-to-end.

**What to implement:**

1. In `SimHostScenarioManager`, **remove**:
   - `_waypointQueues` field declaration.
   - `AddWaypoint(entity, pos, interp)` method body that populates `_waypointQueues` and calls `RegisterTrajectory` directly.
   - Any remaining direct calls to `TrajectoryPoolManager.RegisterTrajectory` from UI/manager classes.

2. In `IgApplication` (or `StandardInteractionTool` wrapper), **remove** any remaining call to `ScenarioManager.AddWaypoint` in the shift-right-click path (should have been removed in T009, but confirm and clean up).

3. If `SetDestination` still uses `_waypointQueues` incidentally, refactor that path to be clean.

4. Ensure that all existing `Hrot.SimHost.Tests`, `Hrot.IG.Tests`, and integration tests still compile and pass — no test should rely on `_waypointQueues` as a test double.

**Success conditions / tests:**

- `SimHostScenarioManager._waypointQueues` field does not exist in the final codebase.
- `SimHostScenarioManager.AddWaypoint` either no longer exists or is empty / redirects to the new system.
- All existing tests in `Hrot.SimHost.Tests` and `Hrot.IG.Tests` pass without modification.
- A Shift+Right-Click end-to-end integration test (added in T009) still passes, now exercising only the new `PersonalRouteAuthoringSystem` path, confirming the deprecation is complete.
