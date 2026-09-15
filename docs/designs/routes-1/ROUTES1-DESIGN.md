# Routes-1 Design — Predefined Vehicle Trajectories as Route Entities

**Workstream:** ROUTES1  
**Status:** Design Phase  
**Reference Talk:** [design-talk.md](./design-talk.md)

---

## Table of Contents

1. [Problem Statement & Goals](#1-problem-statement--goals)
2. [Current State (Legacy System)](#2-current-state-legacy-system)
3. [Architectural Principles](#3-architectural-principles)
4. [Core Data Model — RoutePlan ECS Component](#4-core-data-model--routeplan-ecs-component)
5. [Shared vs. Personal Routes](#5-shared-vs-personal-routes)
6. [DDS Network Replication](#6-dds-network-replication)
7. [Trajectory Pool Integration](#7-trajectory-pool-integration)
8. [Rendering on the IG 2D Map](#8-rendering-on-the-ig-2d-map)
9. [Authoring Flow — Shared Routes](#9-authoring-flow--shared-routes)
10. [Authoring Flow — Personal (Vehicle-Owned) Routes](#10-authoring-flow--personal-vehicle-owned-routes)
11. [Editing Flow](#11-editing-flow)
12. [Deletion Flow](#12-deletion-flow)
13. [AI Soft Advice Pipeline](#13-ai-soft-advice-pipeline)
14. [TKB Blueprint for TacGraphic_Route](#14-tkb-blueprint-for-tacgraphic_route)
15. [Deprecation of Legacy Waypoint Queue](#15-deprecation-of-legacy-waypoint-queue)
16. [Implementation Phases](#16-implementation-phases)

---

## 1. Problem Statement & Goals

The current `TrajectoryPoolManager` stores all vehicle path data as transient, in-memory `CustomTrajectory` objects keyed by an auto-incremented integer. There is no ECS representation, no network replication, no IG visualisation, and no operator authoring flow for user-defined trajectories.

The primary goals of this workstream are:

- **Visualise routes** on the IG 2D map, both as a "show all routes" layer and as a "selected vehicle trajectory" overlay.
- **Author and edit routes** interactively from the IOS/IG map canvas using the same interaction patterns as tactical drawings.
- **Share a single route across multiple vehicles** by decoupling path geometry from individual vehicle state.
- **Create vehicle-specific personal routes** paired to a single vehicle via Shift+Right-Click, matching the existing ad-hoc waypoint workflow.
- **Carry metadata per waypoint** (target speed, AI "soft advice" JSON) accessible to the vehicle's behavior system.
- **Replicate routes over DDS** so that all nodes (IOS, IG, SimHost) stay synchronised.
- **Deprecate** the legacy `_waypointQueues` per-entity dictionary held in `ScenarioManager`, replacing it with the new ECS route infrastructure.

---

## 2. Current State (Legacy System)

### 2.1 TrajectoryPoolManager

`TrajectoryPoolManager` (`FDP/Toolkits/FDP.Toolkit.CarKinem/Trajectory/TrajectoryPoolManager.cs`) is a plain singleton managed object (not an ECS component). It stores `CustomTrajectory` structs in `Dictionary<int, CustomTrajectory>`, each backed by a `NativeArray<TrajectoryWaypoint>` with precomputed arc-lengths and tangents. Supports `Linear`, `CatmullRom`, and `HermiteExplicit` interpolation.

### 2.2 NavState

`NavState` (`FDP/Toolkits/FDP.Toolkit.CarKinem/Core/NavState.cs`) is the ECS blittable struct that governs vehicle movement. The fields relevant to this workstream:

```csharp
public int   TrajectoryId;   // index into TrajectoryPoolManager
public float ProgressS;      // arc-length progress in metres
public KinematicsMode Mode;  // CustomTrajectory, RoadGraph, Formation, Direct, None
```

### 2.3 Legacy Waypoint Queue

`SimHostScenarioManager` (and the example `ScenarioManager`) keep a `Dictionary<Entity, List<Vector2>> _waypointQueues`. On Shift+Right-Click, `AddWaypoint` appends the clicked position using the vehicle's current world position as the starting waypoint, compiles the full sequence into the trajectory pool, and issues `CmdFollowTrajectory`. This approach:

- Ties the trajectory geometry to the vehicle's position at the moment of authoring.
- Prevents sharing a trajectory across multiple vehicles.
- Holds simulation state inside a UI class, violating CQRS/ECS principles.
- Produces no ECS entity, so the route is invisible to the network and the IG map.

### 2.4 CarKinematicsSystem

`CarKinematicsSystem` (`FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs`) handles `KinematicsMode.CustomTrajectory` by calling `TrajectoryPoolManager.SampleTrajectory(nav.TrajectoryId, nav.ProgressS)` each physics tick. No changes to this system are required by this workstream — routes will be compiled into the same pool and the vehicle's `NavState.TrajectoryId` will reference the compiled entry.

### 2.5 SimHostTrajectoryLayer

`SimHostTrajectoryLayer` (`Hrot.SimHost/Visualization/SimHostTrajectoryLayer.cs`) is an always-visible overlay that reads the selected vehicle's `NavState`, fetches its `CustomTrajectory` from the pool, and draws the path as grey line segments with an orange circle at the current `ProgressS`. This layer will need to be extended to also handle route entities.

### 2.6 Existing Map Layer — road_graphs

The `MapLayerRegistry` defines a `road_graphs` layer (bit 4) whose predicate matches entities with `TkbIdentity.TkbType == TkbEntityTypes.TacGraphic_Route (8802)`. Layer visibility is toggled from the IOS Config panel and filters the ORBAT mission panel. The new route entities will automatically appear in this layer once the `TkbIdentity` component is set correctly, but a **route-specific rendering layer** is still needed to draw the actual polyline geometry.

---

## 3. Architectural Principles

The design is built on three pillars:

1. **ECS as Authoritative State.** The `RoutePlan` managed component, attached to a dedicated route entity, is the single source of truth for all route data. The `TrajectoryPoolManager` is treated strictly as a transient, read-optimised performance cache — analogous to `SpatialHashGrid`.

2. **Network Boundary Isolation.** Coordinate conversion (Cartesian ↔ Geodetic) happens exclusively at the DDS ingress/egress translators. The internal ECS state stores absolute Cartesian `Vector3` positions, which are valid and precise for our ≤ 100 km world domains. The DDS `MapRoute` wire format retains double-precision geodetic coordinates for interoperability.

3. **Single Unified Abstraction.** Both shared (multi-vehicle) routes and personal (single-vehicle) routes use the same `RoutePlan` component on the same kind of entity. A shared route is a root-level ECS entity; a personal route carries an additional `PartMetadata` component linking it to its owning vehicle. All systems downstream (rendering, sync, editing) operate on the same component — no branching by route type.

---

## 4. Core Data Model — RoutePlan ECS Component

### 4.1 RouteWaypoint

```csharp
public struct RouteWaypoint
{
    /// <summary>
    /// Absolute Cartesian position in local world space (ENU, metres from map origin).
    /// Valid for world domains ≤ 100 km — coordinate conversion to/from geodetic 
    /// happens only at DDS boundary.
    /// </summary>
    public Vector3 Position;

    /// <summary>
    /// Desired vehicle speed on this segment in m/s. 0 = use vehicle default.
    /// </summary>
    public float TargetSpeed;

    /// <summary>
    /// Optional JSON object with "soft advice" hints for the vehicle's behavior tree.
    /// Example: {"dangerLevel": 2, "tacticalStance": "cautious"}
    /// Written to the vehicle's BrainBlackboard by RouteContextSystem as the vehicle
    /// enters the segment; does NOT interact with MissionPlanQueue.
    /// </summary>
    public string ExtensionJson;
}
```

### 4.2 RoutePlan Managed Component

```csharp
[ComponentId(GlobalComponentIds.RoutePlan)]
public class RoutePlan
{
    public List<RouteWaypoint> Waypoints = new();

    /// <summary>
    /// When true, the vehicle loops back to waypoint 0 upon reaching the last waypoint.
    /// </summary>
    public bool IsLoop;

    /// <summary>
    /// Monotonically-incremented version stamp. Reactive systems (RouteTrajectorySyncSystem)
    /// compare against their cached version to detect mutations without polling every field.
    /// </summary>
    public int Version;
}
```

`ComponentId` must be registered in `GlobalComponentIds` and wired into the `RegisterManagedComponent<RoutePlan>()` call in both `NetworkSpawningSystem` registrations (SimHost and IG).

### 4.3 PersonalRouteRef Component

Placed on the **vehicle** entity to provide an O(1) lookup from vehicle → its personal route entity:

```csharp
[ComponentId(GlobalComponentIds.PersonalRouteRef)]
public struct PersonalRouteRef
{
    public Entity RouteEntity;
}
```

### 4.4 CmdAppendPersonalWaypoint Event

Blittable ECS event published to the event bus by the IG input layer when the operator Shift+Right-Clicks on the map while a vehicle is selected:

```csharp
public struct CmdAppendPersonalWaypoint
{
    public Entity VehicleEntity;
    public Vector3 WorldPosition; // absolute Cartesian, already converted from screen
}
```

---

## 5. Shared vs. Personal Routes

| Aspect | Shared Route | Personal (Vehicle) Route |
|---|---|---|
| Entity parent | Root entity (no `PartMetadata`) | Child entity with `PartMetadata { ParentEntity = vehicleEntity }` |
| Lifecycle | Exists until operator deletes it | Automatically destroyed by `SubEntityCleanupSystem` when the vehicle entity is destroyed |
| Authoring | CMD_START_AUTHORING → PointSequenceTool on IG canvas → CreateEntityRequest | Shift+Right-Click → `CmdAppendPersonalWaypoint` → `PersonalRouteAuthoringSystem` creates/mutates child entity |
| Sharing | Multiple vehicles can point `NavState.TrajectoryId` to the same compiled entry | One vehicle only (`PersonalRouteRef` on the vehicle) |
| DDS replication | Full — entity announced via `EntityMaster` + `dtWorldPos` + `dtMapRoute` | Full — same sequence; the child entity is announced as a separate network entity with its parent's NetworkId encoded in descriptors |
| Editing | RouteEditTool accessible via context menu | RouteEditTool accessible via context menu on the child entity |
| Component | `RoutePlan` only | `RoutePlan` + `PartMetadata` |

### 5.1 Directing a Vehicle to Follow a Route

When an operator commands a vehicle to follow a shared route:

1. The commanding system reads the route entity's compiled `TrajectoryId` from a lightweight cache component (e.g., `RouteTrajectoryCache { public int TrajectoryId; public int CompiledVersion; }`) attached to the route entity by `RouteTrajectorySyncSystem`.
2. It publishes a `CmdFollowTrajectory { Entity, int TrajectoryId }` event.
3. The vehicle's `NavState.TrajectoryId` and `ProgressS = 0` are set; `Mode = KinematicsMode.CustomTrajectory`.

For personal routes, step 1 is handled automatically by `PersonalRouteAuthoringSystem` which both mutates the route entity and issues `CmdFollowTrajectory` each time a waypoint is appended.

---

## 6. DDS Network Replication

The DDS wire format remains geodetic (the pre-existing `MapRoute` IDL descriptor) to preserve global interoperability. Coordinate conversion is isolated to two new translators.

### 6.1 MapRouteEgressTranslator (SimHost + IG)

- Queries entities with `NetworkIdentity` + `SimTransform` + `RoutePlan` (and `NetworkAuthority` on SimHost).
- On dirty flag (`SmartEgressUtil.IsDirty`), iterates `RoutePlan.Waypoints`.
- Converts each `RouteWaypoint.Position` (`Vector3` Cartesian) → `GeoPoint` (lat/lon/alt) via `IGeographicTransform.ToGeodetic`.
- Emits `MapRoute { EntityId, Points: [Waypoint(GeoPoint, SpeedMetersPerSec, ExtensionJson)], IsLoop }`.
- Also emits `WorldPos` update if waypoint[0] changed (spatial anchor for "center on entity").

### 6.2 MapRouteIngressTranslator (SimHost + IG)

- Subscribes to the `dtMapRoute` DDS topic.
- On receipt, looks up the route entity by `EntityId` in `NetworkEntityMap`.
- Converts each `Waypoint.Position` (`GeoPoint`) → absolute Cartesian `Vector3` via `IGeographicTransform.ToCartesian`.
- Writes updated `RoutePlan.Waypoints` and increments `RoutePlan.Version`.
- Defers if the entity's `SimTransform` is not yet available (same pattern as `MapVisualOverlayIngressTranslator`).

### 6.3 CreateEntityRequest Payload for New Shared Routes

When a new route is being created, the IG issues a `CreateEntityRequest` containing:

| Descriptor | Content |
|---|---|
| `dtEntityMaster` | `TkbType = TkbEntityTypes.TacGraphic_Route` |
| `dtWorldPos` | First waypoint converted to geodetic — used as spatial anchor |
| `dtMapRoute` | All waypoints in geodetic doubles |

The SimHost `CreateEntityRequestSystem` allocates a network ID and dispatches `SpawnEntityCommand`. `NetworkSpawningSystem` creates the entity, applies the `TacGraphic_Route` TKB blueprint, and the `MapRouteIngressTranslator` populates the `RoutePlan` component from the initial `dtMapRoute` descriptor.

---

## 7. Trajectory Pool Integration

### 7.1 RouteTrajectorySyncSystem

A new reactive ECS system in `SystemPhase.BeforeSync` (after translators, before kinematics):

- Queries all entities with `RoutePlan` (optional `RouteTrajectoryCache`).
- For each entity: compares `RoutePlan.Version` against `RouteTrajectoryCache.CompiledVersion`.
- If version differs (new route or mutation):
  - If a previous trajectory was compiled (`RouteTrajectoryCache.TrajectoryId > 0`), calls `TrajectoryPoolManager.RemoveTrajectory`.
  - Converts `RoutePlan.Waypoints` to a `Vector2[]` (XZ plane, matching `TrajectoryWaypoint.Position` convention) with per-waypoint speeds.
  - Calls `TrajectoryPoolManager.RegisterTrajectory(waypoints, interpolation, isLoop)` → gets `int id`.
  - Writes `id` and current version into `RouteTrajectoryCache`.
- On entity destruction, an `IEntityLifecycleListener` implementation calls `RemoveTrajectory`.

### 7.2 RouteTrajectoryCache Component

Lightweight blittable struct attached by `RouteTrajectorySyncSystem`:

```csharp
public struct RouteTrajectoryCache
{
    public int TrajectoryId;
    public int CompiledVersion;
}
```

Not replicated over DDS — purely local performance state.

---

## 8. Rendering on the IG 2D Map

### 8.1 RouteRenderLayer (IG)

A new `IMapLayer` implementation in `Hrot.IG` — analogous to `MapOverlayRenderLayer` which renders `EditablePolyline` entities.

- **Layer bit:** Reuses `RoadGraphsBit` (bit 4, "road_graphs") so the existing IOS layer toggle controls route visibility.
- **Query:** Iterates all entities whose `TkbIdentity.TkbType == TkbEntityTypes.TacGraphic_Route` AND the `road_graphs` layer bit is set.
- **Render:** For each entity with a `RoutePlan`, draws line segments between consecutive waypoints (converted to screen space via the map canvas transform). Draws a small circle at each waypoint vertex. Uses a distinct colour (e.g. blue) to differentiate from tactical overlay drawings.
- **Selected highlight:** When the entity is selected (`IInspectorContext.SelectedEntity == routeEntity`), draws the route in a highlighted colour with vertex drag handles visible.

### 8.2 SimHostTrajectoryLayer Extension

The existing `SimHostTrajectoryLayer` is extended to also show the route geometry for selected vehicles that follow a route entity:

- If selected vehicle has `PersonalRouteRef`, look up the child route entity's `RoutePlan` and draw it in the trajectory overlay colour.
- If the vehicle follows a shared route (`NavState.TrajectoryId` matches a `RouteTrajectoryCache`), draw the shared route entity's `RoutePlan` waypoints.

---

## 9. Authoring Flow — Shared Routes

The IOS triggers authoring using the same `CMD_START_AUTHORING` command as for tactical drawings.

```
IOS                     IG                          SimHost
 │                       │                              │
 │─ MapCommandRequest ──►│                              │
 │  (CMD_START_AUTHORING,│                              │
 │   TkbType=TacGraphic  │                              │
 │   _Route)             │                              │
 │                       │─ push PointSequenceTool      │
 │                       │  (configured for routes)     │
 │                       │                              │
 │                   [operator left-clicks N waypoints] │
 │                   [operator right-clicks to commit]  │
 │                       │                              │
 │                       │─ CreateEntityRequest ───────►│
 │                       │  dtEntityMaster(Route)       │
 │                       │  dtWorldPos(wp[0])         │
 │                       │  dtMapRoute(all waypoints)   │
 │                       │                              │
 │                       │◄────── CreateEntityAck ──────│
 │                       │                              │
 │                       │◄── EntityMaster (broadcast)  │
 │                       │◄── WorldPos (broadcast)    │
 │                       │◄── MapRoute (broadcast) ─────│
 │                       │                              │
 │                       │  [MapRouteIngressTranslator  │
 │                       │   populates RoutePlan ECS]   │
 │                       │  [RouteTrajectorySyncSystem  │
 │                       │   compiles into pool]        │
```

The `PointSequenceTool` configured for route authoring translates screen clicks directly to absolute geodetic coordinates (via the map canvas's `IGeographicTransform`) and emits the `CreateEntityRequest`.

---

## 10. Authoring Flow — Personal (Vehicle-Owned) Routes

Kept as a fast, single-gesture interaction: Shift+Right-Click on the map while a vehicle is selected.

```
[User: Shift+Right-Click on map, vehicle selected]
        │
        ▼
IgApplication.OnWorldClick (shift=true)
        │
        ▼
Publishes CmdAppendPersonalWaypoint { VehicleEntity, WorldPosition }
        │
        ▼ (ECS event bus, SystemPhase.Input)
PersonalRouteAuthoringSystem
        │
        ├─ [No PersonalRouteRef on vehicle]
        │       │
        │       ▼
        │  SpawnChildRouteEntity:
        │    · Creates ECS entity with RoutePlan, PartMetadata { ParentEntity=vehicle },
        │      TkbIdentity(TacGraphic_Route), SimTransform (at WorldPosition)
        │    · Seeds RoutePlan.Waypoints with:
        │        [0] = vehicle.SimTransform.Position (current pos)
        │        [1] = WorldPosition
        │    · Attaches PersonalRouteRef { RouteEntity } to vehicle
        │
        └─ [PersonalRouteRef exists]
                │
                ▼
           Appends WorldPosition to route entity's RoutePlan.Waypoints
           Increments RoutePlan.Version
           │
           ▼
   (RouteTrajectorySyncSystem detects version bump)
   Recompiles trajectory in pool, updates RouteTrajectoryCache.TrajectoryId
           │
           ▼
   PersonalRouteAuthoringSystem issues CmdFollowTrajectory { vehicle, newTrajectoryId }
```

This flow does **not** go through `ScenarioManager`. The UI layer (`IgApplication`) emits only a typed event; all simulation state mutation happens in ECS systems.

---

## 11. Editing Flow

### 11.1 RouteEditTool (IG)

A new `IMapTool` dedicated to `RoutePlan` editing — separate from the existing `EditTool` which is specific to `EditablePolyline` relative `Vector2` points.

**Capabilities:**

| Action | Interaction |
|---|---|
| Select vertex | Left-click near a waypoint handle |
| Move vertex | Left-click-drag on selected handle |
| Insert vertex | Left-click on a line segment (between two waypoints) |
| Delete vertex | Select vertex, press `Delete` key |
| Edit speed/JSON | Reflected live in `WaypointEditorPanel` |
| Commit changes | Right-click |
| Cancel | `Escape` key |

On `OnEnter`, the tool copies `RoutePlan.Waypoints` into an in-memory ghost list (`List<RouteWaypoint> _ghost`). All in-tool edits mutate the ghost. On commit (right-click), the tool fires an `UpdateEntityDescriptorRequest` containing the updated `dtMapRoute` descriptor. The SimHost's `UpdateEntityDescriptorRequestSystem` overwrites the `RoutePlan` component and increments its version, triggering recompilation in the trajectory pool.

**Vertex insertion logic:** When the user left-clicks and no existing vertex is within the pick radius (15 world units), the tool does a point-to-segment distance check for all segments in `_ghost`. If the click is within the pick radius of a segment `[i, i+1]`, a new `RouteWaypoint` is inserted at index `i+1`, inheriting `TargetSpeed` and `ExtensionJson` from waypoint `i`.

### 11.2 WaypointEditorPanel (IG ImGui)

An ImGui panel rendered during the `DrawUI` phase. It observes the `MapCanvas`'s active tool:

```csharp
if (_canvas.ActiveTool is RouteEditTool routeTool && routeTool.SelectedVertexIndex >= 0)
{
    ref RouteWaypoint wp = ref routeTool.GetSelectedWaypointRef();
    ImGui.InputFloat("Target Speed (m/s)", ref wp.TargetSpeed);
    string json = wp.ExtensionJson ?? "";
    ImGui.InputTextMultiline("AI Advice (JSON)", ref json, ...);
    wp.ExtensionJson = json;
}
```

The panel provides real-time, in-tool editing of per-waypoint semantic metadata. It does **not** submit changes itself; the `RouteEditTool` owns the ghost state and emits the update request on commit.

### 11.3 Triggering Edit Mode

Editing is initiated via the IOS context menu: the operator right-clicks a route entity and selects "Edit". The IOS publishes `MapCommandRequest { Type = CMD_START_EDITING, EntityId }`. The IG's `MapCommandController` pushes the `RouteEditTool` seeded with the target entity's `RoutePlan`.

---

## 12. Deletion Flow

Deletion follows the established ECS lifecycle pipeline:

1. Operator selects "Delete" from context menu.
2. IOS/IG issues a `DestroyEntityCommand` for the route entity.
3. `NetworkSpawningSystem` sets `EntityLifecycle.TearDown` on the entity.
4. `EntityLifecycleModule` broadcasts a `DestructionOrder`.
5. `CycloneNetworkCleanupSystem` disposes the DDS writers (`DisposeInstance` on `MapRoute`, `EntityMaster`, `WorldPos` topics).
6. Remote nodes receive `DdsInstanceState.NotAliveDisposed` and destroy their ghost entities.
7. An `IEntityLifecycleListener` on `RouteTrajectorySyncSystem` (or a dedicated cleanup system) calls `TrajectoryPoolManager.RemoveTrajectory` for the compiled entry.

For **personal routes**: if the parent vehicle is destroyed first, `SubEntityCleanupSystem` (running in `PostSimulation`) detects `PartMetadata.ParentEntity` is dead and issues `DestroyEntity` for the child route entity — the above pipeline then proceeds normally.

---

## 13. AI Soft Advice Pipeline

### 13.1 RouteContextSystem

A low-frequency simulation phase system that periodically evaluates the vehicle's position along its active `RoutePlan`:

1. Queries all entities with `NavState` (mode `CustomTrajectory`) + `PersonalRouteRef` **or** a reference to a shared route.
2. Determines the current route entity from `NavState.TrajectoryId` ↔ `RouteTrajectoryCache`.
3. Using `NavState.ProgressS` and the precomputed cumulative arc-lengths, identifies which segment the vehicle is currently on (via the `RouteTrajectoryCache` or by checking the `RoutePlan` waypoints in order).
4. Reads `ExtensionJson` from the current `RouteWaypoint`.
5. Parses the JSON and writes values directly to the vehicle's `BrainBlackboard` at designated byte-offset slots (e.g., `blackboard.Memory[BlackboardOffsets.ExpectedThreatLevel] = jsonValue`).

### 13.2 BTree Reaction

The vehicle's active behavior tree reads the blackboard values during its normal `BTreeTickSystem` evaluation. Condition nodes such as `Condition_CheckDangerLevel` branch to sub-trees that adjust `TargetSpeed` (via `LocomotionChannel`), expand `VisionRange` (via `PerceptionReceptor` mutation), or alter Rules of Engagement flags — all without touching the `MissionPlanQueue`. This keeps the route's "soft advice" strictly advisory; the behavior tree remains in full control.

---

## 14. TKB Blueprint for TacGraphic_Route

A new TKB blueprint entry is required for `TkbEntityTypes.TacGraphic_Route (8802)`. The blueprint must:

- Register `RoutePlan` as a managed component requirement.
- Register `TkbIdentity` with `TkbType = 8802`.
- Register `SimTransform` (for spatial anchor, "center on entity", and map culling).
- Register `NetworkIdentity` and `NetworkAuthority` (for DDS replication).
- **Not** register `EditablePolyline`, `NavState`, or any movement component (routes are not moving entities).

The TKB entry is located in the TKB definition database (see `Hrot.Map.Definitions`). The blueprint ensures that both `NetworkSpawningSystem` and `IG`'s entity factory system attach the correct components when a route is spawned via `CreateEntityRequest`.

---

## 15. Deprecation of Legacy Waypoint Queue

`SimHostScenarioManager._waypointQueues` and the `AddWaypoint` / `SetDestination` methods that operate on it must be deprecated and ultimately removed. The replacement flow is:

- `AddWaypoint` → `CmdAppendPersonalWaypoint` → `PersonalRouteAuthoringSystem` (new system).
- The `ScenarioManager` retains only its `SetDestination` (direct movement, no trajectory) helper in the interim.

Removal should happen at the end of the workstream, once the personal route authoring system is fully tested.

---

## 16. Implementation Phases

The implementation is split into nine phases, each decomposed into independently testable tasks. Tasks are identified by the prefix `ROUTES1-T`.

### Phase 1 — Core ECS Data Layer

> **Goal:** Introduce the new ECS components and events so all downstream phases have a stable foundation.

- **ROUTES1-T001** — `RoutePlan` managed component + `RouteWaypoint` struct
- **ROUTES1-T002** — `PersonalRouteRef` + `RouteTrajectoryCache` blittable components + `CmdAppendPersonalWaypoint` event
- **ROUTES1-T003** — `GlobalComponentIds` registration + TKB blueprint for `TacGraphic_Route`

### Phase 2 — DDS Replication

> **Goal:** Route entities replicate seamlessly over DDS using the pre-existing `MapRoute` descriptor.

- **ROUTES1-T004** — `MapRouteEgressTranslator` (SimHost + IG)
- **ROUTES1-T005** — `MapRouteIngressTranslator` (SimHost + IG)

### Phase 3 — Trajectory Pool Integration

> **Goal:** Route entity mutations automatically propagate to the `TrajectoryPoolManager`.

- **ROUTES1-T006** — `RouteTrajectorySyncSystem`

### Phase 4 — Shared Route Authoring

> **Goal:** Operators can author new shared routes from the IOS/IG map canvas.

- **ROUTES1-T007** — `CMD_START_AUTHORING` → `PointSequenceTool` → `CreateEntityRequest` flow for routes

### Phase 5 — Personal Route Authoring

> **Goal:** Shift+Right-Click authors a vehicle-specific child route.

- **ROUTES1-T008** — `PersonalRouteAuthoringSystem` (ECS system)
- **ROUTES1-T009** — IG input wiring: `IgApplication` shift+right-click → `CmdAppendPersonalWaypoint`

### Phase 6 — Rendering

> **Goal:** Routes are visible on the IG 2D map.

- **ROUTES1-T010** — `RouteRenderLayer` (IG map layer)
- **ROUTES1-T011** — `SimHostTrajectoryLayer` extension for route entities

### Phase 7 — Editing

> **Goal:** Operators can modify routes (move, insert, delete waypoints; set per-waypoint metadata).

- **ROUTES1-T012** — `RouteEditTool` (IG map tool)
- **ROUTES1-T013** — `WaypointEditorPanel` (IG ImGui panel)

### Phase 8 — AI Soft Advice

> **Goal:** Per-waypoint `ExtensionJson` influences vehicle behavior trees via BrainBlackboard.

- **ROUTES1-T014** — `RouteContextSystem`

### Phase 9 — Legacy Deprecation

> **Goal:** Remove the legacy waypoint queue mechanism.

- **ROUTES1-T015** — Remove `_waypointQueues` from `ScenarioManager`; wire Shift+Right-Click through the new personal route system end-to-end
