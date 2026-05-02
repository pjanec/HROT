How do we now store and use predefined vehicle trajectories?

Predefined vehicle trajectories are now centrally stored and managed using a global **TrajectoryPoolManager** singleton rather than keeping path data directly on individual entities.

When a trajectory is created via `RegisterTrajectory`, the manager takes an array of waypoints, precomputes the arc lengths, tangents, and speeds, and stores the result in a **CustomTrajectory** struct backed by a memory-efficient `NativeArray<TrajectoryWaypoint>`. The trajectory can be configured to loop or use different interpolation modes like Linear, Catmull-Rom (for smooth, organic curves), or explicit Hermite splines. The pool manager then stores the trajectory in a dictionary and returns a unique integer ID for the path.

To use the trajectory, a vehicle is commanded to follow the path (e.g., via a `CmdFollowTrajectory` event). The vehicle's **NavState** ECS component is updated to set its `Mode` to `KinematicsMode.CustomTrajectory`. Instead of holding the path itself, the `NavState` simply stores the associated **TrajectoryId** and a `ProgressS` value, which tracks the vehicle's current arc-length progress along the path in meters.

During the simulation phase, the **CarKinematicsSystem** executes the movement by reading the `NavState` and querying the `TrajectoryPoolManager`. It uses `SampleTrajectory`, which evaluates the vehicle's `ProgressS` distance against the path data to return the exact interpolated target position, tangent (heading), and desired speed. As the vehicle drives, its `ProgressS` is incremented by its speed over the timestep, allowing it to smoothly follow the shared trajectory data.

I need to be able to show those trajectories (lets call them 'routes') on IG 2d map and be able to edit them and author new ones. I want to be able to show them all at once (as a special layer) as well as show only concrete trajectories in case the entity is currently following it and the entity is selected. New trajectory should be creatable similarly to how ios now triggers the authoring of tactical drawings. But routes are (now) not a separate entities. Maybe the user created trajectories could be route entities that just reference the trajectory stored in the singleton? And make sure a new trajectory in singleton is created when new route entity is created and similarly when route entity is deleted the teajectory is removed as well? Is this idea of parallelism of routlte-trajectory good idea or shall we rather approach the teajectory authoring in different way?

Representing user-created routes as standard entities is absolutely the right architectural approach. It aligns perfectly with the Entity-Component-System (ECS) paradigm and our DDS network replication, preventing the need to build a parallel state machine just for trajectory data.

Here is how we can implement this cleanly, ensuring the ECS remains the single source of truth while still leveraging the `TrajectoryPoolManager` as a high-performance acceleration structure.

1\. Authoring and Entity Representation

You are spot-on about reusing the tactical drawing workflow. We already have a specific TKB template for this: `TkbEntityTypes.TacGraphic_Route`.

To author a route, the IOS can issue a `CMD_START_AUTHORING` command, which pushes the `PointSequenceTool` onto the IG map canvas. When the operator finishes drawing, this tool emits a `CreateEntityRequest` containing a `dtMapVisualOverlay` descriptor (or the `dtMapRoute` descriptor) with the drawn points.

On the simulation side, this creates an entity carrying an `EditablePolyline` managed component. Because it uses standard components, operators can immediately right-click the route and use the existing `EditTool` to drag individual vertices around, which automatically updates the `EditablePolyline`.

2\. Bridging ECS to the Trajectory Singleton (The Architecture Fix)

While the idea of parallelism is correct, we must be careful about state ownership. The `TrajectoryPoolManager` should be treated strictly as a transient acceleration structure (like the `SpatialHashGrid`), not as authoritative state. ECS must own the data.

Instead of manually pushing data to the pool from the UI or authoring tools, we should introduce a reactive ECS system (e.g., `RouteTrajectorySyncSystem`). This system would:

-   Query for all entities with a `TkbIdentity` of `TacGraphic_Route` and an `EditablePolyline`.-   Detect when a route entity is newly spawned or modified (e.g., by checking the `EditablePolyline.Version` counter incremented by the edit tool).-   Read the vertices from the polyline and call `RegisterTrajectory` on the injected `TrajectoryPoolManager`, caching the returned Trajectory ID on the entity.-   Listen for entity destruction and call `RemoveTrajectory` to keep the pool clean.

This completely decouples the UI from the physics layer. The IG creates a drawing, it replicates over DDS, the reactive system sees it, and the trajectory pool is silently updated for the kinematics system to use.

3\. Rendering and Visibility Layers

Your display requirements perfectly match the existing layer system:

**Showing all routes at once:** By assigning these route entities the `TacGraphic_Route` TKB type, they are automatically caught by the existing `"road_graphs"` map layer predicate (Bit 4). Toggling this layer on the IOS or IG will instantly show or hide all global route entities.

**Showing only the active trajectory for a selected vehicle:** We already have the foundation for this via the `SimHostTrajectoryLayer`. This layer operates as an "always-on" overlay (LayerBitIndex -1) and actively queries the `IInspectorContext` to see if an entity is currently selected. If the selected entity has a `NavState` where `Mode == KinematicsMode.CustomTrajectory`, the layer reads the `TrajectoryId`, samples the exact curve from the `TrajectoryPoolManager`, and draws it on the screen, including a highlight at the vehicle's current `ProgressS`.

By adopting this architecture, routes become first-class citizens in the simulation: they are network-replicated, selectable, editable, and neatly bridge the gap between human-readable map drawings and high-performance kinematic splines.

What about the storage duality? Why to sync routes to pool manager? Cant we use just route entities? What is the role of the trajectory manager, when it can be beneficial to keep it?

This storage duality is actually a textbook architectural pattern: separating _authoritative network state_ from _read-optimized acceleration structures_.

Route entities (such as `MapRoute` or those carrying an `EditablePolyline` component) serve as the single source of truth. They are designed for network replication, persistence, and allowing an operator to dynamically author and edit vertices on the map.

However, evaluating cubic splines—like Catmull-Rom or explicit Hermite curves—is computationally expensive. To follow a path smoothly, vehicles must be sampled along the curve using constant-speed arc lengths rather than raw parametric time. If our high-frequency `CarKinematicsSystem` had to read raw vertices directly from ECS components and perform trapezoidal integration or finite difference math on every single tick for thousands of vehicles, it would completely decimate our frame budget.

This is the exact role of the `TrajectoryPoolManager`. It acts as a thread-safe, read-only cache for the simulation layer. When an ECS route is synced to the pool, the manager performs the heavy mathematical lifting exactly once: it calculates the cumulative distance (arc length) for each waypoint and precomputes the required tangents based on the interpolation mode. This compiled geometry is stored in a `CustomTrajectory` struct backed by a cache-friendly, zero-allocation `NativeArray`.

During the physics loop, the `CarKinematicsSystem` avoids all of that heavy setup. It simply calls `SampleTrajectory` with the vehicle's current distance progress. The pool manager instantly locates the correct segment using the precomputed distances and evaluates the spline, returning the exact interpolated position, forward tangent, and desired speed.

By maintaining this separation, we guarantee that our ECS components remain lightweight and strictly focused on data definition and replication, while the `TrajectoryPoolManager` cleanly isolates the heavy trigonometric caching required for high-performance kinematic execution.

Is this trajectory pool something different from the road network?

Yes, they represent two entirely different architectural concepts: a static topological graph versus a dynamic spline cache.

The **Road Network (****RoadNetworkBlob****)** is your static, global infrastructure definition. Built once at map load, it holds nodes (intersections) and segments (roads) along with metadata like speed limits, lane widths, and a spatial lookup grid. It's a heavy, read-only structure used for structural queries and autonomous roaming where vehicles evaluate their environment dynamically using `KinematicsMode.RoadGraph`.

The **Trajectory Pool (****TrajectoryPoolManager****)** is a lightweight, dynamic cache of specific, isolated paths. A `CustomTrajectory` in this pool knows absolutely nothing about the map, roads, or intersections. It is strictly a compiled sequence of waypoints, precomputed arc lengths, and tangents optimized for high-frequency kinematic sampling via `KinematicsMode.CustomTrajectory`. This is the ideal structure for storing the arbitrary, user-drawn tactical routes we discussed earlier.

The beauty of this separation of concerns is how they interact during dynamic pathfinding. When a vehicle needs to navigate from point A to point B through the city, the `PathfindingSolverSystem` runs a Dijkstra search over the `RoadNetworkBlob` to find the shortest topological path. Once the sequence of road nodes is resolved, the system extracts the geometry, converts the nodes into a raw array of 2D waypoints, and registers them directly into the `TrajectoryPoolManager`. It then returns the newly generated Trajectory ID (the `RouteHandle`) for the vehicle's kinematics system to follow blindly.

This architecture keeps the heavy graph-traversal and spatial-awareness logic completely decoupled from the tight, performance-critical inner loop of the vehicle physics system.

How is the trajectory authoring using shift right click handled now? If we introduce route entities, does it make sense to keep 2 separate mechanisms? Do we need route entities if already can author the pooled trajectories? I need tge route/trjectory to he shareable by multiple vehicle entities.

Currently, when you Shift+Right-Click on the map, the `StandardInteractionTool` captures the input and invokes `ScenarioManager.AddWaypoint` for each currently selected entity. The `ScenarioManager` maintains a hidden, out-of-band dictionary (`_waypointQueues`) to store these points per-entity. It then constructs a path array by prepending the vehicle's _current 2D position_ to the queued waypoints, compiles this sequence into a new trajectory in the `TrajectoryPoolManager`, and issues a `CmdFollowTrajectory` event to the vehicle.

This current mechanism fundamentally prevents sharing. Because the trajectory is dynamically compiled using the specific vehicle's current position as the first waypoint, the resulting trajectory geometry is mathematically bound to that single vehicle. If ten vehicles shift-clicked the same destination, the pool would allocate ten completely different trajectories.

From a clean architecture perspective, keeping two separate mechanisms (this hidden UI state vs. formal route entities) is an anti-pattern. We must deprecate the `_waypointQueues` state machine in the UI layer entirely and unify around ECS route entities.

Here is why we absolutely still need route entities even though the trajectory pool exists:

-   **Single Source of Truth & Network Replication:** The `TrajectoryPoolManager` is strictly an unmanaged, transient acceleration structure designed for fast trigonometric sampling during the physics step. It cannot be serialized, it cannot be edited across the network, and it has no concept of ECS ownership. Route entities (carrying components like `EditablePolyline`) are standard ECS citizens that replicate seamlessly over CycloneDDS.-   **True Shareability:** By making a route an explicit entity, we divorce the path geometry from the vehicle. An operator draws a route once (creating the entity), and a reactive synchronization system compiles its `EditablePolyline` vertices into the `TrajectoryPoolManager` exactly once, obtaining a single Trajectory ID.-   **Many-to-One Mapping:** Multiple vehicles can now share this single route simply by having their `NavState.TrajectoryId` set to the shared ID. The kinematics system will blindly sample that shared mathematical curve using each vehicle's individual `ProgressS` (arc-length progress), saving massive amounts of memory and CPU cycles.

To implement your shareable route requirement correctly, we should route the Shift+Right-Click input (or a dedicated route-drawing tool) to spawn an ECS entity carrying an `EditablePolyline`. Once committed, any selected vehicle commanded to follow that route simply updates its `NavState` to reference the resulting pool ID. This keeps the UI completely stateless, leverages our DDS replication, and maximizes the performance of the kinematics solver.

Ok. At the same time as authoring vehicle independent shared routes, i stil want to be able to be able to define vehicle entity specific route belonging to just one sinle vehicle entiry. If courae i do not want to duplicate code, rather i like to reuse the newly introduced route infrastructure. But keep using the same quick shift right click based authoring of this route paired with concrete vehicle. The route needs to live and die with the vehicle. Pls evaluate this proposal, suggest improvements.

This is an excellent architectural proposal. Unifying the concept of a "route" into a single ECS representation—whether it is a shared global asset or a vehicle-specific path—is exactly the kind of consolidation that leads to robust, maintainable systems. It eliminates parallel state machines, reduces edge cases, and automatically grants vehicle-specific routes all the benefits of the new infrastructure (network replication, IG rendering, and dynamic editing).

Here is an evaluation of how we can cleanly implement this vehicle-specific route paradigm using your new infrastructure, leveraging existing FDP hierarchy and lifecycle systems to ensure we write minimal new code.

1\. Lifecycle Management: The Parent-Child Pattern

To guarantee that the route "lives and dies" with the vehicle, we should utilize the existing ECS composition mechanics rather than writing custom lifecycle hooks.

By attaching a `PartMetadata` component to the newly spawned route entity, we declare the vehicle as its parent. We already have a `SubEntityCleanupSystem` running in the `PostSimulation` phase that monitors entities with `PartMetadata`. If it detects that the `ParentEntity` is no longer alive, it automatically issues a `DestroyEntity` command for the child. This perfectly fulfills your lifecycle requirement with zero new code.

2\. The Authoring Workflow (Shift + Right-Click)

We must completely rip out the legacy `_waypointQueues` dictionary from the `ScenarioManager`. UI classes should not hold simulation state.

Instead, the `StandardInteractionTool` will capture the Shift+Right-Click and emit a standard ECS command, such as `CmdAppendPersonalWaypoint`.

A dedicated simulation system (e.g., `PersonalRouteAuthoringSystem`) will consume this command and apply the following logic:

-   **If the vehicle does not have a personal route:** The system spawns a new entity. It adds the `PartMetadata` (linking it to the vehicle) and an `EditablePolyline` component. It seeds the polyline with two points: the vehicle's current `SimTransform.Position` (converted to the relative coordinate space) and the newly clicked destination.-   **If the vehicle already has a personal route:** The system retrieves the child route entity, appends the newly clicked destination to the `EditablePolyline.Points` list, and increments the `EditablePolyline.Version` counter.

To track this link efficiently, the vehicle entity can simply carry a lightweight component like `PersonalRouteRef { public Entity RouteEntity; }`.

3\. Synchronizing with the Trajectory Pool

Because you are updating an `EditablePolyline`, the exact same reactive synchronization system we discussed for _shared_ routes will naturally pick up the changes.

When the `PersonalRouteAuthoringSystem` increments the polyline's `Version`, the sync system detects the mutation, reads the points, and re-compiles the spline inside the `TrajectoryPoolManager`. It then ensures the vehicle's `NavState.TrajectoryId` points to the updated pool ID. The high-frequency `CarKinematicsSystem` remains blissfully unaware of whether it is following a shared tactical route or a personal waypoint queue; it just samples the math curve.

4\. Added Value: Editing and Network Replication

By making the vehicle's personal path a standard `EditablePolyline` entity, you get two massive features for free:

-   **DDS Replication:** The vehicle's queued path will automatically replicate to the IOS and all other IG nodes. Commanders can see exactly where a specific unit is planning to go.-   **Live Editing:** Because the personal route uses the standard `EditablePolyline`, an operator can use the existing `EditTool` to grab a waypoint out of a vehicle's queue and drag it to a new location on the map. The edit commits, the polyline updates, the trajectory pool recompiles, and the vehicle smoothly adjusts its course—all without interrupting the simulation.

Summary of the Architecture

This approach achieves pure data-driven ECS purity:

-   **Input:** Shift+Right-Click → `CmdAppendPersonalWaypoint`.-   **Authoring:** Command → Mutates/Spawns child `EditablePolyline` entity.-   **Compilation:** Polyline Mutation → `TrajectoryPoolManager` re-evaluates math.-   **Execution:** `CarKinematicsSystem` reads pool and drives.-   **Cleanup:** Vehicle dies → `SubEntityCleanupSystem` reaps the polyline.

This completely unifies the codebase, cleanly separating the operator's UI intent from the mathematical spline execution.

Todays tactical drawing uses simTransform as base point and a list of relative coordinate vertices. For routes this is not much usefull, absolute coorfinates would fit better. Shall we revamp the tactical drawings to absolute coordinates for consistency?

I strongly advise against revamping `EditablePolyline` to use absolute coordinates. While absolute coordinates might seem more intuitive for route authoring at first glance, the current relative-coordinate architecture exists for two critical software engineering reasons that apply just as much to routes as they do to tactical area drawings.

First is floating-point precision. `EditablePolyline` stores its vertices as single-precision `Vector2` values. If we store absolute Cartesian coordinates over a massive world map, we will inevitably hit floating-point precision limits, leading to jitter and degraded mathematical accuracy. By storing vertices as relative Cartesian offsets from the entity's `SimTransform.Position`, we keep the coordinate values small and maintain high precision. We already employ this exact pattern in our navigation solver with `RelativeVector3` specifically to limit floating-point error over large maps.

Second is object cohesion and translation. Because the vertices are relative offsets, moving an entire drawing—or an entire route—is a fast O(1) operation. You simply update the single `SimTransform.Position` and the entire polyline implicitly moves. If we used absolute coordinates, translating a route (e.g., shifting a patrol path slightly to the west) would require an O(N) operation to iterate and mutate every single vertex in the `EditablePolyline`.

Furthermore, this relative approach is already deeply baked into our network replication. The `MapVisualOverlay` DDS descriptor transmits points as relative geographic offsets measured from the entity's centroid reference point.

Instead of breaking this clean architecture, the route synchronization system you build should simply add the `SimTransform.Position` origin to the relative points when compiling them into the `TrajectoryPoolManager`. This is exactly how the `MapOverlayRenderLayer` resolves absolute screen positions for rendering and how the `EditTool` handles vertex dragging. This gives the kinematics system the absolute world coordinates it needs for math, while keeping the ECS data storage compact, precise, and highly modular.

Is it better to kerp a standalone route entitu as a childe of the vehicle or does it make sense to create new ecs component holding vehicle specific trajevtory points (navigation plan?) I want to be able to specify some extra info to individual waypoijts, like desired vehicle speed or other infomation, for example some data for the AI to affect the behavior when entity reaches the wsypoint. Route entity based solely on efitable polyline would not suffice. How to approach this?

You should not bloat the purely geometric `EditablePolyline` or `CustomTrajectory` structures with AI domain knowledge, as they are heavily optimized for fast rendering and kinematic sampling. Instead, our architecture already provides two highly structured mechanisms to solve this exact problem: `MapRoute` entities for shareable semantic paths, and `EntityMission` components for vehicle-specific execution sequences.

For authoring reusable paths that contain semantic metadata, you should instantiate an entity carrying the `MapRoute` descriptor. This descriptor holds a list of `Waypoint` structs, which natively support a `SpeedMetersPerSec` field and an `ExtensionJson` payload specifically designed for mission logic like "Hold for 5 mins" or "Deploy sensors".

However, for vehicle-specific sequential navigation where the AI must react or change behavior at specific points, you should map the waypoints directly into the vehicle's existing `EntityMission` state rather than managing a separate child route entity. The simulation kernel already translates this network state into a `MissionPlanQueue` ECS component directly on the vehicle.

Using this pipeline, you construct a `MissionPlan` consisting of sequential `MissionTask` elements. Each task invokes a specific `BehaviorId` (such as "MoveToLocation") and carries a `BehaviorParams` JSON string where you can explicitly define the target coordinates, arrival radius, and desired travel speed.

To execute AI logic upon reaching a waypoint, you sequence a movement task with a `ReachedDestination` trigger. During the simulation loop, the `MissionDirectorSystem` evaluates this trigger; once the vehicle's navigation state reports that it has arrived, the system automatically advances the `MissionPlanQueue` and assigns the next AI behavior task.

This approach preserves excellent architectural hygiene through a strict separation of concerns. The `TrajectoryPoolManager` remains a pure, high-performance mathematical cache for rapid cubic spline interpolation during the physics step. Meanwhile, the `MissionPlanQueue` on the vehicle entity acts as the authoritative state machine for orchestrating travel speeds, destinations, and subsequent AI behaviors.

You are mixing maproute descriptor (which is dds descriptor) with ecs component concept. We do not have ecs component with a list of waypoints (nav plan component), do we? But we could ad it and translate to maproute descriptor on dds. If we do that, should we keep the child route concept? Or somow unify these two?

You caught me—you are absolutely right. I conflated the `MapRoute` DDS wire descriptor with our internal ECS component structures. We currently have the `EditablePolyline` component for raw geometric shapes, but we do not have an ECS component that encapsulates a semantic list of waypoints containing speeds and AI extensions.

From a clean architecture perspective, your proposal to add a dedicated ECS component and unify these concepts is exactly the right move. Here is how we should architect this to maximize code reuse, keep the ECS pure, and satisfy both personal and shared route requirements.

1\. Introduce the `RoutePlan` ECS Component

We should introduce a new managed ECS component, let's call it `RoutePlan`. This component will serve as the engine-side counterpart to the `MapRoute` DDS descriptor.

Instead of raw `Vector2` points (like `EditablePolyline`), this component will hold a list of structured `RouteWaypoint` objects. These will contain the relative Cartesian coordinates for the `SimTransform`, plus the semantic metadata mirrored from the DDS layer: `SpeedMetersPerSec` and `ExtensionJson` for AI triggers.

2\. Unify the Route Concepts (Everything is an Entity)

We should absolutely **unify** the concepts, but we do this by fully leaning into the "Route as an Entity" paradigm. A route is a complex spatial and semantic data structure; it deserves to be a first-class citizen in our ECS world rather than being buried inside a vehicle's component.

Here is the unified approach:

-   **Shared Routes:** Spawned as standalone, root-level entities carrying the `RoutePlan` component and a `SimTransform`.-   **Personal (Child) Routes:** Spawned carrying the _exact same_ `RoutePlan` component, but with a `PartMetadata` component attached. The `PartMetadata.ParentEntity` field links it to the specific vehicle, automatically tying its lifecycle to the vehicle via our existing sub-entity cleanup systems.

3\. The Synchronization and Translation Pipeline

By unifying both shared and personal routes under the same ECS component (`RoutePlan`), we eliminate duplicate code across the board:

-   **DDS Egress/Ingress:** We write exactly one `MapRouteEgressTranslator` and one `MapRouteIngressTranslator`. If an entity has a `RoutePlan` component, the egress system converts the relative Cartesian waypoints to geodetic coordinates and publishes the `MapRoute` DDS topic. It doesn't matter if it's a shared tactical route or a vehicle's personal child route; it hits the network the same way.-   **Live Editing:** We adapt the IG's `EditTool` to recognize the `RoutePlan` component. Because personal and shared routes use the same component, an operator can right-click and edit a vehicle's personal queued route just as easily as a global shared route.

4\. Execution: Bridging Semantics and Kinematics

When a vehicle is commanded to follow a route, its `MissionPlanQueue` is assigned a `FollowRoute` task containing the Entity ID of the Route.

When the vehicle begins execution:

-   **Spline Compilation:** A reactive system extracts the geometric points from the `RoutePlan` component, compiles the cubic spline math, and registers it with the `TrajectoryPoolManager`. It hands the resulting Trajectory ID to the vehicle's `NavState`. The `CarKinematicsSystem` blindly follows this highly-optimized mathematical curve.-   **Semantic Execution:** Meanwhile, the `MissionDirectorSystem` evaluates the vehicle's progress against the `RoutePlan` waypoints. When the vehicle reaches a waypoint, the mission director extracts the specific `ExtensionJson` or `SpeedMetersPerSec` for that node and triggers the appropriate AI state changes.

Summary

By introducing a unified `RoutePlan` ECS component attached to standalone or child entities, we achieve strict separation of concerns. The DDS layer handles replication, the `TrajectoryPoolManager` handles high-frequency physics math, and the ECS Entity handles identity, composition, and semantic AI metadata. This is textbook ECS design.

What if a route navpoints are very far from each other, maybe hundred of km in case of aircraft route. Small Relative coordinates are not ideal fit here. RoutePlan should use high precision global coordinates.

You are absolutely right, and this is a critical architectural distinction. I was applying the logic of tactical graphics (which are localized shapes) to strategic navigation, which is flawed.

If a flight path spans hundreds of kilometers, projecting it into a single flat Cartesian coordinate system completely breaks down. Our `WGS84Transform` uses an East-North-Up (ENU) tangent plane that is explicitly documented as being accurate only for distances less than 100km from the origin. Furthermore, single-precision floats (`Vector2` / `Vector3`) will suffer catastrophic precision loss at those magnitudes.

Because of this, **Shape Data** and **Navigational Data** must be treated as two fundamentally different domain concepts in our ECS.

Here is the corrected architectural approach to handle high-precision global routes:

1\. `RoutePlan` Must Use Geodetic Doubles

Unlike `EditablePolyline` which uses relative `Vector2` floats for fast canvas rendering, your new `RoutePlan` component must store double-precision geodetic coordinates. This aligns perfectly with the DDS `MapRoute` and `Waypoint` descriptors, which already use the double-precision `GeoPoint` struct.

```
public struct RouteWaypoint
{
    public double Latitude;
    public double Longitude;
    public double Altitude;
    public float SpeedMetersPerSec;
    public string ExtensionJson; 
}

[ComponentId(...)]
public class RoutePlan
{
    public List<RouteWaypoint> Waypoints = new();
    public bool IsLoop;
}
```

This guarantees that an aircraft's route from Berlin to London retains perfect mathematical precision in the ECS, regardless of where the local simulation origin is currently anchored.

2\. Just-In-Time (JIT) Trajectory Compilation

The `TrajectoryPoolManager` and `CarKinematicsSystem` are heavily optimized to run on single-precision Cartesian floats to meet our tight frame budgets. We cannot feed a 500km route into the trajectory pool at once.

To bridge the high-precision `RoutePlan` to the fast `float` physics pool, we use a **Sliding Window** or JIT compilation approach:

-   When a vehicle is commanded to follow a `RoutePlan`, the reactive sync system does _not_ compile the entire route.-   Instead, it looks at the vehicle's current progress and extracts only the current and next few waypoints.-   It passes these specific `double` coordinates through the `WGS84Transform.ToCartesian` projection to get local Cartesian floats relative to the _current_ engine origin.-   It registers this small, highly-accurate local chunk into the `TrajectoryPoolManager` and gives the ID to the vehicle's `NavState`.

3\. Continuous Origin Rebasing (Floating Origin Support)

As the aircraft flies along this route, if the engine shifts the `WGS84Transform` origin to keep the vehicle near `(0,0,0)` (preventing float precision loss in physics), the local Cartesian coordinates of the trajectory pool become instantly invalid.

Because the `RoutePlan` safely holds the absolute, uncorrupted double-precision truth, the translation system can simply discard the active `CustomTrajectory` from the pool, re-project the upcoming `RouteWaypoint`s against the new geodetic origin, and feed a fresh local spline back into the `TrajectoryPoolManager` without skipping a beat.

By maintaining this strict separation—`RoutePlan` for double-precision global truth and `TrajectoryPoolManager` for single-precision local execution—you perfectly solve the long-distance precision problem while preserving the high-performance kinematics pipeline.

Pls summarize the ideas we agreed on so far.

Here is the architectural blueprint we have aligned on to cleanly overhaul the routing infrastructure, ensuring maximum performance, precision, and ECS purity:

**1\. Strict Separation of State and Execution** We are decoupling the authoritative network state from the high-frequency physics math. ECS entities will serve as the absolute single source of truth for routing data, ensuring seamless CycloneDDS network replication and IG visualization. The `TrajectoryPoolManager` is relegated to its proper role: a transient, read-optimized mathematical cache that precomputes arc lengths and cubic splines (like Catmull-Rom or Hermite) specifically for the `CarKinematicsSystem` to sample during the physics step.

**2\. Unified** **RoutePlan** **Component** Instead of juggling parallel state machines or reusing the purely geometric `EditablePolyline`, we will introduce a dedicated `RoutePlan` managed component. This component unifies the concept of a route across the engine, acting as the ECS counterpart to the DDS `MapRoute` descriptor. It will be shareable among multiple vehicles by simply assigning the resulting trajectory ID to their `NavState`.

**3\. Double-Precision Geodetic Truth** To support strategic, long-distance navigation (like aircraft routes) without catastrophic floating-point degradation, the `RoutePlan` will store waypoints using double-precision global geodetic coordinates (Latitude, Longitude, Altitude). This moves us away from the localized relative Cartesian `Vector2` floats used by tactical drawings.

**4\. JIT Spline Compilation & Floating Origin Support** Because the `TrajectoryPoolManager` requires single-precision local Cartesian floats for maximum performance, we will implement a Sliding Window / Just-In-Time (JIT) compilation system. A reactive system will extract a small chunk of upcoming double-precision waypoints, project them against the current `WGS84Transform` origin to get high-precision local offsets, and feed them into the physics cache. This perfectly supports continuous floating-origin rebasing without losing global accuracy.

**5\. Parent-Child Lifecycle for Personal Routes** We satisfy the need for both shared and vehicle-specific routes without duplicating logic by treating everything as an entity. A shared route is a standalone root entity. A personal vehicle route uses the exact same `RoutePlan` component but is spawned as a child entity linked to the vehicle via a `PartMetadata.ParentEntity` reference. If the parent vehicle is destroyed, existing sub-entity cleanup systems will automatically reap the personal route. Shift+Right-Click authoring will simply append points to this child entity.

**6\. Decoupling Physics from AI Semantics** We will keep AI domain logic entirely out of the mathematical trajectory pool. Waypoints in the `RoutePlan` will carry metadata like `SpeedMetersPerSec` and `ExtensionJson`. While the `CarKinematicsSystem` blindly follows the geometric spline, the `MissionDirectorSystem` will evaluate the vehicle's progress and trigger AI state changes or mission tasks when specific semantic nodes are reached.

The extra json in a waypoint was meant as something that could set some entity property or some state in the blackboard rather than directly activating some behavior (which is what the mission system is there for). More some kinf 'soft advice' to the entity rather than a hard imperative. Gor example it can advice the entity to be more csutios as that psrt of the route is expected more dangerous.

That is a brilliant architectural distinction. You are completely right, and I appreciate you calling out that mixing imperative behavioral commands into spatial waypoints violates the Single Responsibility Principle.

The mission system (`MissionPlanQueue`, `MissionDirectorSystem`) must remain the strict authority on _what_ the entity is doing (the hard imperatives), while the route and its waypoints provide the spatial context—the _where_ and the _conditions_ of the environment. Treating `ExtensionJson` as "soft advice" (contextual metadata) perfectly aligns with clean ECS design.

Here is how we can cleanly architect this "soft advice" pipeline so that it influences the AI's behavior without hijacking the mission state machine:

1\. The Route Context System

As the vehicle travels along its mathematical spline in the `TrajectoryPoolManager`, its `NavState.ProgressS` tracks its exact arc-length progress. We can introduce a lightweight, low-frequency system (e.g., `RouteContextSystem`) that runs in the simulation phase.

This system's only job is to compare the vehicle's `ProgressS` against the precomputed cumulative distances of the `RoutePlan` waypoints. When the vehicle crosses into a new route segment, the system reads the `ExtensionJson` from that specific `RouteWaypoint`.

2\. Injecting Advice into the BrainBlackboard

Instead of altering the `BehaviorState` or `MissionPlanQueue`, the `RouteContextSystem` parses this JSON and writes the data directly into the vehicle's `BrainBlackboard`.

Our `BrainBlackboard` uses a fixed inline byte array (`Memory`) designed exactly for this kind of transient, contextual state. For example, the JSON `{"dangerLevel": 2}` could be parsed, and the value `2` written to a designated `ExpectedThreatLevel` byte offset in the blackboard's memory.

3\. BTree Evaluation (The "Soft" Reaction)

Because the data is now safely in the blackboard, the entity's active behavior tree (e.g., `FollowRoute_BT`) naturally reacts to it during its normal `BTreeTickSystem` evaluation.

The BTree remains in control. You simply add a condition node like `Condition_CheckDangerLevel` that reads the blackboard. If the danger level is high, the BTree can branch to:

-   Lower the `TargetSpeed` parameter in the `LocomotionChannel`.-   Increase the `VisionRange` on the `PerceptionReceptor` component to scan further.-   Alter its Rules of Engagement (RoE).

Why this is Best Practice

This approach gives us a beautifully decoupled architecture:

-   **The Trajectory Pool** remains purely mathematical and blazing fast.-   **The Mission System** remains purely sequential and imperative.-   **The Route** acts as a rich semantic layer, painting the map with context.-   **The AI (BTree)** retains its autonomy, evaluating the "soft advice" from the blackboard against its own internal state (like health or ammo) to decide _how_ to execute its current mission task.

What the authoring/editing/ddeleting flow from ios to ig and simhost might look like? It should follow similar flow as for the tactical drawings.

Here is the exact data-driven workflow for authoring, editing, and deleting routes, following the established tactical graphics pipeline and adhering to strict ECS and CQRS principles.

1\. Authoring Flow (Creation)

The creation of a new route is driven by the IOS but executed interactively on the IG map canvas, culminating in an authoritative entity spawn on the SimHost.

-   **IOS Intent:** The operator selects "Draw Route" on the IOS. The IOS publishes a `MapCommandRequest` over DDS with `Type = CommandType.CMD_START_AUTHORING`, supplying a specific `ContextId`.-   **IG Tool Activation:** The IG's `IgApplication` receives the command and routes it to the `MapCommandController`. This controller pushes the `PointSequenceTool` onto the `MapCanvas`.-   **Operator Input:** The operator left-clicks to drop waypoints (drawn as an elastic line) and right-clicks to commit the sequence.-   **Translation & Request:** The IG translates the drawn screen points into high-precision absolute geodetic coordinates (Latitude, Longitude, Altitude) to avoid floating-point loss over large distances. It then publishes a `CreateEntityRequest` to the SimHost containing:
    -   `dtEntityMaster` (TKB type for a route)-   `dtWorldPos` (The centroid/reference point of the route)-   `dtMapRoute` (The sequence of high-precision geodetic waypoints).-   **SimHost Authority:** The SimHost's `CreateEntityRequestSystem` receives the request, allocates a network ID, and dispatches a `SpawnEntityCommand`. The `NetworkSpawningSystem` creates the authoritative ECS entity, attaches the `RoutePlan` component, and publishes the `EntityMaster`, `WorldPos`, and route descriptors back over DDS.-   **Pool Registration:** A reactive system on the SimHost (and IG) detects the new `RoutePlan` component, compiles the cubic spline math, and registers it into the `TrajectoryPoolManager` for kinematic sampling.

2\. Editing Flow (Mutation)

Editing reuses the standard `EditTool`, updating the existing route geometry without tearing down the entity.

-   **IOS Intent:** The operator right-clicks the route and selects "Edit" from the context menu. The IOS publishes a `MapCommandRequest` with `Type = CommandType.CMD_START_EDITING` and the route's `EntityId`.-   **IG Tool Activation:** The IG pops any active tools and pushes the `EditTool`, seeding it with the route's current vertices converted to local absolute world space.-   **Operator Input:** The operator drags individual vertex handles to new positions. A right-click commits the changes.-   **Translation & Request:** The IG translates the updated vertices back to relative geodetic offsets (or absolute geodetic doubles) and publishes an `UpdateEntityDescriptorRequest` targeted at the route's `EntityId`.-   **SimHost Authority:** The SimHost's `UpdateEntityDescriptorRequestSystem` receives the patch, verifies authority, and mutates the `RoutePlan` ECS component. It immediately calls `SmartEgressUtil.MarkDirty()`.-   **Cache Recompilation:** The local reactive system detects the `RoutePlan` mutation (e.g., via a version bump), invalidates the old spline in the `TrajectoryPoolManager`, and JIT-compiles the new geometry. Vehicles following this route instantly adjust to the new curve. The SimHost egress translator simultaneously broadcasts the updated descriptor to all nodes.

3\. Deletion Flow (Teardown)

Deletion removes the entity globally, cleaning up both the ECS representation and the mathematical cache.

-   **IOS Intent:** The operator selects "Delete" from the context menu. The IOS publishes a control command (or a `MapCommandRequest` mapped to deletion).-   **SimHost Authority:** The SimHost receives the request and issues a `DestroyEntityCommand` via the event bus.-   **Entity Lifecycle Teardown:** The `NetworkSpawningSystem` flags the entity with `EntityLifecycle.TearDown`. The `EntityLifecycleModule` (ELM) broadcasts a `DestructionOrder` to all subsystems.-   **DDS Disposal:** The `CycloneNetworkCleanupSystem` detects the destruction and invokes `DisposeInstance` on the `EntityMaster` DDS writer.-   **Network Synchronization & Cache Cleanup:** The IG and IOS receive the `DdsInstanceState.NotAliveDisposed` state. They destroy their local ghost entities. Finally, an ECS cleanup system detects that the `RoutePlan` entity no longer exists and calls `RemoveTrajectory` on the `TrajectoryPoolManager` to free the allocated spline memory.

This flow guarantees that the IOS and IG handle only UI intent and descriptor formulation, while the SimHost strictly manages the authoritative ECS state and network replication.

Centroid does not make sense for map route as all waypoints are already absolute world coordinates.

You are completely right. Applying the tactical graphic centroid logic to strategic routes is an architectural mismatch. Because the `MapRoute` DDS descriptor and our new `RoutePlan` ECS component store waypoints as absolute double-precision `GeoPoint`s, computing a centroid or treating `WorldPos` as a mathematical origin is totally redundant.

Here is how we adapt the architecture to cleanly support absolute world coordinates for routes, while keeping the rest of the ECS machinery happy:

**1\. Skip the Centroid Math** When the operator finishes drawing a route using the `PointSequenceTool`, we do not calculate an arithmetic mean or subtract offsets. We simply take the absolute geodetic coordinates (Latitude, Longitude, Altitude) of every clicked point and pack them directly into the `dtMapRoute` descriptor.

**2\. Use the First Waypoint as an "Anchor"** While the route's geometry does not need a `dtWorldPos` descriptor, our ECS framework expects physical entities to have a `SimTransform` for general spatial tooling. For example, the `MapCullingSystem`, the "Center on Entity" context menu action, and basic mouse picking all rely on an entity having a spatial position.

Instead of a centroid, we simply copy the **first waypoint** of the route into the `dtWorldPos` descriptor. This acts purely as a spatial anchor so the camera knows where to jump when an operator double-clicks the route in the ORBAT.

**3\. Ignore the Anchor for Execution** When your JIT compilation system feeds the route into the `TrajectoryPoolManager`, or when the IG renders the route line on the map, it strictly ignores the entity's `SimTransform`. It reads the absolute `RouteWaypoint`s from the `RoutePlan` component, converts them to local Cartesian space via `IGeographicTransform.ToCartesian`, and builds the spline.

**4\. Editing an Absolute Route** Because we are dropping relative coordinates for routes, the `EditTool` implementation for routes will be slightly different than it is for `EditablePolyline`. When an operator moves a vertex, the tool translates the new screen position directly to an absolute `GeoPoint`. When the edit is committed, we send an `UpdateEntityDescriptorRequest` containing the fully updated `dtMapRoute` array, and if the first waypoint was moved, we also send a `dtWorldPos` update to keep the camera anchor in sync.

This keeps the route definition perfectly precise over global distances while satisfying the engine's baseline requirement that every physical entity has a transform.

When editing a shape or route, i neef to be able to insert new points and delete existing. For map waypoints i need to be able to edit parameters of the currently selected waypoints (in a special imgui panel showing params of currently selected waypoint)

To cleanly support vertex insertion, deletion, and semantic metadata editing without violating the Entity-Component-System (ECS) or UI architecture, we must build a dedicated `RouteEditTool` and a corresponding ImGui panel.

The existing `EditTool` is strictly designed for geometric `EditablePolyline` components using local Cartesian `Vector2` arrays. Because our new `RoutePlan` concept uses high-precision geodetic doubles and carries semantic payload data (`SpeedMetersPerSec`, `ExtensionJson`), it requires a specialized tool.

Here is the architectural blueprint for implementing this cleanly:

1\. The Interaction Layer: `RouteEditTool`

We implement `RouteEditTool` inheriting from `IMapTool`. Like the standard `EditTool`, it will load the route's waypoints into an isolated, in-memory ghost list (`List<RouteWaypoint> _ghostPoints`) during `OnEnter`.

-   **Deleting Points:** We hook into `HandleKeyPressed`. If the operator presses `KeyboardKey.Delete` and we have a valid `_selectedVertexIndex`, we remove that index from `_ghostPoints` and clamp the selection.-   **Inserting Points:** In `HandleClick` (Left Mouse Button), we first attempt to select an existing vertex via `FindNearestVertex`. If no vertex is within the pick radius, we perform a point-to-line-segment distance check against all segments in `_ghostPoints`. If the click falls on a segment between index `i` and `i+1`, we `Insert` a new `RouteWaypoint` at `i+1`. To ensure UX consistency, the newly inserted waypoint should automatically inherit the `SpeedMetersPerSec` and `ExtensionJson` from vertex `i`.-   **Moving Points:** Handled exactly as it is now in `HandleDrag`: mutating the geodetic coordinates of `_ghostPoints[_selectedVertexIndex]`.

2\. Separation of Concerns: The `WaypointEditorPanel`

As a senior engineer, I strongly advise against mixing ImGui rendering logic inside an `IMapTool`. The tool's responsibility is spatial canvas interaction; UI panels must remain decoupled.

We will create a new ImGui view, `WaypointEditorPanel`, which acts as an observer of the tool stack. During the `DrawUI` phase (inside `rlImGui.Begin()`), the panel checks the active tool:

```
if (_canvas.ActiveTool is RouteEditTool routeTool && routeTool.SelectedVertexIndex >= 0)
{
    // Retrieve by reference so ImGui can mutate the ghost state directly
    ref RouteWaypoint wp = ref routeTool.GetSelectedWaypointRef();

    ImGui.InputDouble("Latitude", ref wp.Latitude);
    ImGui.InputDouble("Longitude", ref wp.Longitude);
    
    float speed = wp.SpeedMetersPerSec;
    if (ImGui.InputFloat("Target Speed (m/s)", ref speed))
        wp.SpeedMetersPerSec = speed;

    string json = wp.ExtensionJson ?? string.Empty;
    if (ImGui.InputTextMultiline("AI Advice (JSON)", ref json, 2048, ...))
        wp.ExtensionJson = json;
}
```

This guarantees strict separation. The canvas tool handles the spatial drag-and-drop, while the ImGui panel binds directly to the active selection's metadata, giving the operator real-time editing of the "soft advice" JSON and speed parameters we discussed previously.

3\. Committing the State (CQRS & DDS Egress)

When the operator finishes editing and right-clicks, the `RouteEditTool` intercepts the input in `HandleClick` and commits the changes.

Just as the current `EditTool` fires `OnPolylineCommitted` to issue an `UpdateEntityDescriptorRequest` with a `dtMapVisualOverlay` payload, our `RouteEditTool` will invoke a callback to fire an `UpdateEntityDescriptorRequest` using the `dtMapRoute` descriptor.

The SimHost receives this request, overwrites the authoritative `RoutePlan` ECS component, increments its version, and triggers the `TrajectoryPoolManager` to safely JIT-recompile the mathematical spline with the newly inserted, deleted, or relocated waypoints. Because the new metadata is baked into the component, the `RouteContextSystem` will immediately feed the updated JSON advice to the AI's blackboard as vehicles traverse the modified segments.

The ecs route component should use cartesian absolute coordinate and should be translated to dds descriptor geo lat lon alt on ingress and egress

While I appreciate the desire to standardize the ECS on Cartesian coordinates to match `SimTransform`, storing long-distance routes natively in absolute Cartesian space is a critical architectural flaw for a simulation that supports global scales.

The core issue is the mathematical limitation of our coordinate projection. Our `WGS84Transform` implementation projects the spherical globe onto a flat East-North-Up (ENU) tangent plane. This projection is mathematically valid and accurate only for distances less than 100 kilometers from the origin.

If you translate a 500-kilometer aircraft route into absolute Cartesian coordinates at network ingress and store it that way in the ECS, you are assuming a flat Earth. A straight line drawn between two Cartesian points hundreds of kilometers apart will literally intersect the curvature of the ellipsoid (going underground) or drift out of the atmosphere, destroying altitude accuracy. Furthermore, storing these massive values as single-precision Cartesian floats (which our standard vector types use) will result in catastrophic precision loss and spatial jitter,.

To maintain perfect global fidelity, the ECS route component must act as the absolute source of truth and store its waypoints in double-precision geodetic coordinates (Latitude, Longitude, Altitude), mirroring the `MapRoute` DDS descriptor,,. We already leverage this exact pattern in the engine with the `PositionGeodetic` managed component, which stores double-precision coordinates specifically to guarantee network and spatial interoperability.

The translation to Cartesian should _not_ happen blindly at the network boundary for the entire route. Instead, translation should happen _Just-In-Time (JIT)_ during the simulation loop. When a vehicle is actively navigating, a localized system should extract only the upcoming geodetic waypoints, convert them to local Cartesian floats using the current `WGS84Transform` origin, and feed that short, highly accurate segment into the `TrajectoryPoolManager`,.

This design preserves clean architecture by strictly separating the globally accurate strategic path (geodetic doubles in ECS) from the high-frequency local physics execution (Cartesian floats in the transient trajectory pool).

It is an imperative. My worlds are sized up to around 100km so the precision is sufficient.

Understood. If your simulation domain guarantees a hard upper limit of 100km, the East-North-Up (ENU) tangent plane used by our `WGS84Transform` remains mathematically valid without curving into the ellipsoid. By accepting this constraint, we can simplify the architecture and standardise the ECS entirely on Cartesian coordinates to match `SimTransform`.

Here is how we will implement this imperative cleanly across the pipeline:

1\. The ECS Component: `RoutePlan`

We will define the internal `RoutePlan` component using absolute Cartesian `Vector3` coordinates. This keeps it perfectly aligned with the rest of the local physics engine.

```
public struct RouteWaypointCartesian
{
    public Vector3 Position; 
    public float TargetSpeed;
    public string ExtensionJson;
}

[ComponentId(...)]
public class RoutePlan
{
    public List<RouteWaypointCartesian> Waypoints = new();
    public bool IsLoop;
}
```

2\. Network Boundary: Ingress and Egress Translators

To maintain global interoperability, the DDS wire protocol will remain strictly geodetic (Latitude, Longitude, Altitude) via the `MapRoute` and `Waypoint` descriptors. We isolate the coordinate projection entirely at the network edge using standard translators.

-   **Ingress (****MapRouteIngressTranslator****):** When a `MapRoute` message arrives from the network, the translator iterates over the `GeoPoint` waypoints. It passes them through `IGeographicTransform.ToCartesian` and writes the absolute `Vector3` coordinates into the `RoutePlan` component.-   **Egress (****MapRouteEgressTranslator****):** When an operator or AI modifies a `RoutePlan`, the egress system detects the mutation (e.g. via `SmartEgressUtil`). It iterates over the Cartesian waypoints, converts them back to global coordinates using `IGeographicTransform.ToGeodetic`, and broadcasts the `MapRoute` DDS topic.

3\. The Performance Win: Zero-Cost Trajectory Compilation

Because you have mandated absolute Cartesian coordinates for the `RoutePlan`, you get a massive performance benefit on the execution side.

Previously, we discussed using a Just-In-Time (JIT) compiler to translate geodetic chunks into local physics space. With this new Cartesian imperative, that overhead completely vanishes. When a route is assigned, the reactive system can immediately dump the `RoutePlan`'s `Vector3` array directly into the `TrajectoryPoolManager`.

The physics layer (`CarKinematicsSystem`) now has a direct, 1-to-1 mapping with the precomputed cubic splines without a single geodetic math operation executing on the hot path.

This architecture satisfies your imperative perfectly: it gives you absolute Cartesian precision in the ECS for local physics execution while transparently fulfilling the DDS global geodetic contract at the network boundary.

