
how well is the entity "brain" (the part executing the behaviors) separated from the "muscles" (like the kinematic motion model of a vehicle)?

The architecture demonstrates an exceptionally clean, data-oriented separation between the decision-making "brains" and the physical "muscles", communicating entirely through decoupled, zero-allocation ECS components.

Here is how the pipeline enforces this strict separation of concerns:

**1\. The Brain (Decision Layer)** The cognitive layer, driven by FastBTree (`BrainBTreeState`) or FastHSM (`BrainHsm128` / `BrainHsm64`), has absolutely no knowledge of vehicle physics, wheelbases, or steering angles. When an AI behavior makes a decision (e.g., a wander behavior deciding to move to a random point), the action node simply writes a high-level intent—an Action ID and a small unmanaged parameter struct like `MoveToParams`—into the entity's `LocomotionChannel` component.

**2\. The Nervous System (Translation Layer)** The bridge between mind and muscle is handled by the `LocomotionDispatcherSystem` and a suite of stateless `IActionExecutor` implementations. The dispatcher monitors the `LocomotionChannel` and routes the active action to the appropriate executor, such as the `MoveToExecutor` or `FollowRouteExecutor`. These executors act as translators: they parse the channel's parameters and map them into concrete navigation targets by mutating the entity's `NavState` component, setting the `FinalDestination`, `TargetSpeed`, and `NavigationMode`.

**3\. The Muscles (Kinematic Layer)** At the bottom of the pipeline, the `CarKinematicsSystem` acts as the physics engine and operates entirely ignorant of behavior trees, HSMs, or AI behaviors. It strictly reads the intended targets from the `NavState` alongside the vehicle's physical limits defined in `VehicleParams` and `VehicleState`. It then performs the heavy mathematical lifting: calculating local collision avoidance via RVO, computing steering angles using a Pure Pursuit controller, and integrating the kinematic bicycle model. Finally, it writes the resulting spatial updates directly into the `SimTransform` and `SimVelocity` components.

**Architectural Wins** This design is a textbook application of Data-Oriented Design (DOD) and the Dependency Inversion Principle. Because the layers communicate exclusively by mutating sequential, unmanaged structs (`LocomotionChannel` → `NavState` → `SimTransform`), the hot path generates zero garbage collection (GC) pressure and maintains high CPU cache locality. Furthermore, this decoupling allows the engine to route different entities to entirely different physics backends—for example, using `LinearKinematicsSystem` instead of `CarKinematicsSystem` for non-wheeled entities—without requiring a single change to the upstream AI logic.

I would like to achive dstributed architecture where one node (a new one, lets call it Brain) plays the brain and the other (like current SimHost or a new "Sim") plays the muscles. How can it be done? Do i need to sync the intention ECS compoennt via network from Brain to the Sim? What the sim would be doing? How would it signal back to the brain that the intention was fullfilled?

To achieve this distributed split-brain architecture, you will leverage the engine's existing split-authority model using the `DescriptorOwnership` managed component, which is explicitly designed for scenarios where different nodes simulate different parts of the same entity.

Because the architecture already strictly enforces Data-Oriented Design (DOD) by decoupling the cognitive layers from the kinematic layers, splitting this across a network boundary is highly elegant.

Here is exactly how to architect the split:

1\. The Division of Authority

You will configure the system so that authority over the entity is split at the descriptor level:

-   **The Brain Node (Authority over Intellect):** Executes `MissionDirectorSystem`, `BTreeTickSystem`, and `LocomotionDispatcherSystem`. It maintains authority over the cognitive descriptors (e.g., `EntityMission`).-   **The Sim Node (Authority over Physics):** Executes the heavy `SpatialHashSystem`, local collision avoidance, and `CarKinematicsSystem`,. It maintains authority over the physical descriptors (e.g., `dtWorldPos`).

2\. Syncing the Intention

Yes, you absolutely need to sync the intention component over the network. In the current monolithic pipeline, the Brain writes its intent into the `LocomotionChannel`, which local executors translate into a `NavState`.

To distribute this, you must introduce a new DDS descriptor (e.g., `NavigationIntentTopic`) that serializes the `NavState` component.

-   **Brain Node (Egress):** Acts as the authoritative owner of the `NavigationIntent` descriptor. When the BTree updates the desired destination or speed, an `IDescriptorTranslator` packages the `NavState` and publishes it to the DDS bus.-   **Sim Node (Ingress):** Subscribes to the `NavigationIntent` topic. Its ingress translator decodes the payload and applies the `NavState` values directly to the local ECS entity.

3\. What the Sim Node Does

The Sim node acts entirely as the "muscles" and remains completely ignorant of behavior trees or AI behaviors. Each tick, the `CarKinematicsSystem` runs in parallel across all vehicles,. It reads the replicated `NavState` (containing the `FinalDestination` and `TargetSpeed`), calculates local collision avoidance (RVO) against the spatial hash grid, computes the required steering angle via a Pure Pursuit controller, and applies the kinematic bicycle model,,. Finally, it writes the resulting state directly into the `SimTransform` and `SimVelocity` components.

4\. Signaling Fulfillment (The Feedback Loop)

In a clean, decoupled ECS architecture, you do not use RPC-style callbacks to signal fulfillment. Instead, you rely on implicit state observation via spatial replication.

Here is the feedback loop:

-   As the Sim node executes the kinematics, its `WorldPosEgressTranslator` continually compares the live `SimTransform` against a shadow state. When the entity moves, it publishes `WorldPos` and `WorldPos` updates back to the network,.-   The Brain node's `WorldPosIngressTranslator` receives these updates and overwrites the local ghost entity's `SimTransform`,.-   The Brain node's `MoveToExecutor` (which is still ticking the `LocomotionChannel` locally) simply observes the replicated `SimTransform` and `SimVelocity` each frame. It calculates `Vector2.Distance(pos, destination)` to detect if the physical entity has successfully arrived.-   If the entity's replicated velocity drops below a threshold and it hasn't reached the target, the Brain's frustration guard detects that the "muscles" are stuck and transitions the node to `Failure`, prompting the BTree to replan.

By syncing the **Intention** (`NavState`) in one direction and replicating the **Reality** (`SimTransform`/`SimVelocity`) in the other, both nodes remain fully decoupled, perfectly adhering to the single-responsibility principle.

The brains' MoveToExecutor must handle situation when the entity fails to arrive to destination. Checking if entity position reaches the target points is not good enough. I need the sim to autonomously plan the path and control all the movement and deal with errors and be fully responsible for motion tasks execution. Only the Sim can tell if the action is finished or failed. The Brains executor should be fully dependent on Sim's authonomous dealing with the motion "sub-problem" and should not try to decide when the task is finished.

As a senior software developer passionate about clean architecture, I couldn't agree with you more. You have identified a classic violation of the **Single Responsibility Principle (SRP)** and a leaky abstraction in the current design.

Right now, the cognitive layer (`MoveToExecutor`) is doing physics math—fetching `SimTransform`, calculating `Vector2.Distance`, reading `SimVelocity`, and maintaining a stateful `_stuckTicks` dictionary to guess if the vehicle is frustrated. This tightly couples the "Brain" to the kinematic reality and breaks the distributed ownership model, because the Brain shouldn't care _how_ or _if_ a path is traversable; it should only care about the result.

To achieve a truly autonomous Sim node and a cleanly decoupled Brain node, we must implement a **Command / Feedback separation** (a form of CQRS adapted for ECS and network authority).

Here is how we refactor the architecture to make the Sim fully responsible for the motion sub-problem:

1\. Split the God-Component (`NavState`)

Currently, `NavState` mixes fields the Brain writes (`FinalDestination`, `TargetSpeed`) with fields the Sim writes (`HasArrived`, `IsBlocked`). In a distributed split-authority model, two nodes cannot easily share write-authority over the same ECS component. We must split it into two strict, one-way unmanaged components:

**Owned by the Brain Node (Egress to Sim):**

```
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.NavigationIntent)]
public struct NavigationIntent
{
    public NavigationMode Mode;
    public Vector2 FinalDestination;
    public float TargetSpeed;
    public float ArrivalRadius;
    public uint IntentId; // Incremented on new command
}
```

**Owned by the Sim Node (Egress to Brain):**

```
public enum NavigationResult : byte
{
    InProgress = 0,
    Arrived = 1,
    Failed_Blocked = 2,
    Failed_Unreachable = 3
}

[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.NavigationStatus)]
public struct NavigationStatus
{
    public uint IntentId; // Correlates to the Intent being executed
    public NavigationResult Result;
}
```

2\. Purify the Brain (`MoveToExecutor`)

We completely strip the `MoveToExecutor` of all physical awareness, distance checks, and its `_stuckTicks` dictionary. The Brain becomes a pure observer of the Sim's autonomous execution.

```
public sealed class MoveToExecutor : IActionExecutor<LocomotionChannel>
{
    public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
    {
        MoveToParams p;
        fixed (byte* src = channel.Params) p = *(MoveToParams*)src;

        // Write the command. The Brain node is authoritative over this.
        ref var intent = ref world.GetComponentRW<NavigationIntent>(entity);
        intent.FinalDestination = p.Destination;
        intent.TargetSpeed = p.Speed;
        intent.ArrivalRadius = p.ArrivalRadius;
        intent.Mode = NavigationMode.Direct;
        unchecked { intent.IntentId++; } 
    
        channel.Status = Fbt.NodeStatus.Running;
    }
    
    public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
    {
        // 100% reliant on the Sim node's autonomous processing.
        if (!world.HasComponent<NavigationStatus>(entity)) return;
    
        var status = world.GetComponent<NavigationStatus>(entity);
        var intent = world.GetComponent<NavigationIntent>(entity);
    
        // Ensure the Sim is responding to our CURRENT intent, not an old one
        if (status.IntentId != intent.IntentId) return;
    
        if (status.Result == NavigationResult.Arrived)
        {
            channel.Status = Fbt.NodeStatus.Success;
        }
        else if (status.Result == NavigationResult.Failed_Blocked || 
                 status.Result == NavigationResult.Failed_Unreachable)
        {
            // The Sim determined the path is impossible. The BTree will now naturally replan.
            channel.Status = Fbt.NodeStatus.Failure;
        }
    }
    
    public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
    {
        // Clear intent
        ref var intent = ref world.GetComponentRW<NavigationIntent>(entity);
        intent.Mode = NavigationMode.None;
        intent.TargetSpeed = 0f;
    }
}
```

3\. Empower the Sim (`CarKinematicsSystem`)

The "muscle" logic, including the frustration guard that we just deleted from the Brain, is moved into the Sim node (e.g., inside `CarKinematicsSystem` or a dedicated `NavigationExecutionSystem`).

The Sim node is now the absolute authority on motion. It reads `NavigationIntent`. It handles local RVO collision avoidance, pathfinding, and Pure Pursuit.

-   If its internal pathfinder cannot find a route, it writes `NavigationResult.Failed_Unreachable`.-   If its internal physics detect that `vel.Linear.Length() < FrustrationSpeedThreshold` for too long despite applying acceleration, it writes `NavigationResult.Failed_Blocked`.-   If it successfully navigates to the coordinate, it writes `NavigationResult.Arrived`.

Architectural Wins

By structuring it this way, you adhere to the **Dependency Inversion Principle**. The cognitive layer no longer depends on kinematic implementations.

Furthermore, over the network, the Brain securely multicasts `NavigationIntent` to the DDS bus, and the Sim securely multicasts `NavigationStatus` back. Because the authority is perfectly segregated into distinct ECS components, the FDP replication layer (`SmartEgressUtil`) will synchronize this bidirectional handshake seamlessly, with absolutely zero race conditions or ownership conflicts.

I want to have differnet nodes playing the Sim's role, each takin care of different kind of entitits (like aircraft flight model Sim, ground vehicle Sim, human motion Sim etc.) Actually, i want these to be separated on a ModuleHost's Module level. I want to be able to instantiate a module handling aircraft motion intents that handles just aircraft. Similarly for ground vehicles. Similarly for humans. And have the freedom to instantiate all these in a single Sim or each one in its dedicated Sim.

This is a textbook use case for the FDP engine’s Data-Oriented Design (DOD) and the `ModuleHost` architecture. Because the system strictly decouples data (ECS components) from logic (Systems) and packages those systems into composable `IModule`s, achieving this level of deployment flexibility is highly elegant and requires zero changes to the core kernel.

Here is the clean architecture approach to separating your "Muscle" domains (Ground, Air, Human) into distinct, deployable modules that naturally scale from a single monolithic process out to a distributed cluster.

1\. Distinct Component Signatures (The Data Contract)

To ensure systems only process their respective entity types, you must define strictly disjoint component signatures for your physical state. Do not use generic "MovementState" for everything.

-   **Ground Vehicles:** Retain the existing `VehicleState` and `VehicleParams`.-   **Aircraft:** Create new components like `FlightDynamicsState` and `AerodynamicParams`.-   **Humans:** Create `PedestrianState` and `HumanoidLocomotionParams`.

When the TKB Database instantiates a blueprint (e.g., an M1 Abrams vs. an Apache Helicopter), it attaches only the specific state component.

2\. Module & System Segregation (The Logic)

You will break up the monolithic `SimulationLogicModule` into highly cohesive, domain-specific modules. Each module registers only the systems that understand its domain.

```
// Ground Vehicle Module
public class GroundKinematicsModule : IModule
{
    public string Name => "GroundKinematics";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public void RegisterSystems(ISystemRegistry registry)
    {
        // Only cares about wheeled/tracked entities
        registry.RegisterSystem(new CarKinematicsSystem(_roadNetwork, _trajectoryPool));
    }
    public void Tick(ISimulationView view, float dt) { }
}

// Aircraft Module
public class AirKinematicsModule : IModule
{
    public string Name => "AirKinematics";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public void RegisterSystems(ISystemRegistry registry)
    {
        // A new system that integrates 6-DOF flight models
        registry.RegisterSystem(new FlightDynamicsSystem());
    }
    public void Tick(ISimulationView view, float dt) { }
}
```

Inside your `FlightDynamicsSystem`, the `EntityQuery` guarantees strict isolation without branching logic:

```
// Inside FlightDynamicsSystem.cs
var query = World.Query()
    .With<FlightDynamicsState>() // Automatically excludes ground vehicles & humans
    .With<SimTransform>()
    .With<SimVelocity>()
    .With<NetworkOwnership>() 
    .Build();
```

3\. The Authority Guard (Distributed Execution)

When you deploy these modules across different physical nodes, **all nodes will receive all entities** via CycloneDDS replication (because they all subscribe to the `EntityMaster` and `WorldPos` topics).

To prevent a scenario where multiple nodes try to integrate physics for the same entity, you must rely on the network authority guard inside every kinematic system. As seen in the existing `PhysicsSystem` and `RefactoredPlayerInputSystem`, you must check the `NetworkOwnership` or `NetworkAuthority` component:

```
foreach (var entity in query)
{
    ref readonly var ownership = ref view.GetComponentRO<NetworkOwnership>(entity);
    
    // Clean exit: If this node is not the authoritative owner of this entity, 
    // it acts purely as a passive ghost. Do not run physics!
    if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
        continue; 
    
    // ... execute flight physics and mutate SimTransform ...
}
```

4\. Flexible Orchestration (Single vs. Distributed Deployment)

Because the logic is bounded within `IModule` implementations, your application shell (`SimHostApp` or `SimHostSubsystem`) becomes a pure orchestrator. You can dynamically register these modules based on runtime configuration or CLI flags.

**Scenario A: The Monolithic SimHost (All-in-One)** If you launch the process with `--role all_physics`:

```
_kernel.RegisterModule(new GroundKinematicsModule());
_kernel.RegisterModule(new AirKinematicsModule());
_kernel.RegisterModule(new HumanKinematicsModule());
```

The kernel gracefully schedules all three modules side-by-side. They run in parallel, operating on disjoint subsets of the ECS world without cache contention.

**Scenario B: The Distributed Split (Dedicated Nodes)** If you launch three separate processes across your cluster (`--role air_sim`, `--role ground_sim`, `--role human_sim`):

```
if (config.Role == "air_sim") {
    _kernel.RegisterModule(new AirKinematicsModule());
}
```

In this scenario, the Air Sim node literally does not possess the `CarKinematicsSystem` in memory. Ground vehicles still exist in its ECS world as ghosts (replicated via DDS), and can be observed by aircraft sensors (via the `PerceptionModule`), but they consume absolutely zero physics CPU cycles on the Air Sim node.

Summary

By pairing strict `EntityQuery` filtering (Data-Oriented Design) with `IModule` segregation and `NetworkOwnership` checks, you completely decouple the physics logic from the deployment topology. You write the systems once, and you can scale them from a single laptop up to a multi-server distributed cluster simply by changing which modules the host instantiates at startup.

Regarding the authority guard - do we need to directly query the network ownership? can't we rely on ECS component authority (HasAuthority)? Isn;t the component level authority automatically set from the network authority so that kinematic systems does NOT need to know anything about network?

You have just hit on one of the most critical principles of clean Data-Oriented Design. You are absolutely right—coupling a pure kinematic system to network-layer components like `NetworkOwnership` or `NetworkAuthority` is a leaky abstraction and a violation of the Single Responsibility Principle.

The physics systems should operate entirely in a vacuum, completely ignorant of whether the simulation is running as a standalone single-player game, a split-authority distributed cluster, or a replay viewer.

Here is the architectural reality of how the FDP kernel natively solves this, and how you should structure your systems.

1\. The Native ECS Authority Mask

Deep inside the kernel, every entity's 96-byte `EntityHeader` contains a `BitMask256 AuthorityMask`. This tracks ownership explicitly at the _component level_ (e.g., we own this entity's `SimTransform`, but we do not own its `WeaponState`).

It is the strict responsibility of the network ingress layer (specifically systems like `OwnershipIngressSystem`) to translate incoming DDS network topologies into these native ECS bits by calling `EntityRepository.SetAuthority<T>()`. The physics systems should never look at the DDS-driven components.

2\. The Clean Query (`WithOwned<T>`)

Instead of fetching a networking component and writing an `if` statement, your kinematic systems should simply declare their authority requirements directly in the `EntityQuery` using the `.WithOwned<T>()` fluent builder.

Here is what your cleanly decoupled `FlightDynamicsSystem` or `CarKinematicsSystem` should look like:

```
var query = World.Query()
    .With<FlightDynamicsState>()
    .With<SimVelocity>()
    .WithOwned<SimTransform>() // Natively filters via SIMD bitmask
    .Build();

foreach (var entity in query)
{
    // 100% guaranteed that we have authority to mutate this component.
    // Zero network awareness required.
    ref var tf = ref World.GetComponentRW<SimTransform>(entity);
    
    // ... integrate physics ...
}
```

By using `.WithOwned<SimTransform>()`, the query evaluator performs an O(1) bitwise check against the `AuthorityMask` and silently skips ghost entities, preserving your CPU cache and keeping the hot path branch-free.

3\. The Kernel's Phase Guard

Relying on this native ECS authority is not just a suggestion; the kernel actively enforces it.

If you look at `PhaseConfig.Default`, the `Simulation` phase (where kinematics run) is strictly locked to `PhasePermission.OwnedOnly`. If a system ever attempts to call `GetComponentRW<SimTransform>` on a ghost entity, the kernel's `ValidateWriteAccess<T>` method intercepts it and instantly throws an `InvalidOperationException` to prevent distributed state corruption. Filtering via `.WithOwned<T>()` cleanly sidesteps this by ensuring your system only iterates over the exact subset of entities it is legally allowed to mutate.

A Note on Legacy Code

If you look around the codebase, you will see older systems (like `PhysicsSystem` or `RefactoredPlayerInputSystem`) that manually query `.With<ModuleHost.Core.Network.NetworkOwnership>()` and evaluate `if (ownership.PrimaryOwnerId != ownership.LocalNodeId) continue;`.

**Treat those as technical debt.** They are artifacts from an older architecture before the granular `AuthorityMask` and `.WithOwned<T>()` query extensions were fully matured. When building your new domain-specific modules, rely entirely on the native ECS component authority. Let the network layer manage the network, and let the muscles manage the motion.

These 'older' systems need to be fixed to use the clean principles as much as possible. Checking of network authority and also relying on primary owner id (nor counting with partial ownership) is more a severe bug that a tech debt. Which places needs fixing?

You are absolutely right to treat this as a severe bug rather than just technical debt. Relying on `PrimaryOwnerId` completely breaks the distributed split-authority model. If Node A owns the chassis (`SimTransform`) and Node B owns the turret (`WeaponState`), any system checking `PrimaryOwnerId == LocalNodeId` will fail to simulate the partially-owned components, leading to massive state desynchronization across the cluster.

To implement clean Data-Oriented Design (DOD), these systems must be completely decoupled from networking concepts like `NetworkOwnership` or `NetworkAuthority`. They should rely exclusively on the ECS kernel's native `.WithOwned<T>()` and `.WithoutOwned<T>()` query filters, or `HasAuthority<T>()` for event-driven logic.

Based on the source dump, here are the exact systems that contain this bug and how they must be fixed:

1\. Kinematics & Physics Systems

These systems iterate over entities and integrate movement. Currently, they fetch networking components and manually check if the primary owner matches the local node.

**Fdp.Examples.NetworkDemo.Systems.PhysicsSystem**

-   **The Bug:** Queries `.With<ModuleHost.Core.Network.NetworkOwnership>()` and skips entities if `ownership.PrimaryOwnerId != ownership.LocalNodeId`.-   **The Clean Fix:** Remove the network component from the query. Use `.WithOwned<SimTransform>()` and `.WithOwned<SimVelocity>()`. The ECS kernel will automatically filter out ghost entities using the SIMD-optimized `AuthorityMask`.

**Fdp.Modules.Geographic.Systems.CoordinateTransformSystem**,

-   **The Bug:** Queries `.With<NetworkOwnership>()` and skips if `PrimaryOwnerId != LocalNodeId`.-   **The Clean Fix:** Remove the explicit ownership check. Change the query to include `.WithOwned<PositionGeodetic>()`.

**Fdp.Modules.Geographic.Systems.GeodeticSmoothingSystem**,

-   **The Bug:** This system is meant to process _ghosts_ (remote entities), so it checks `if (ownership.PrimaryOwnerId == ownership.LocalNodeId) continue;`.-   **The Clean Fix:** Remove `NetworkOwnership`. Chain `.WithoutOwned<Position>()` into the query builder so it natively iterates only over entities where we lack write authority.

2\. Input & Control Systems

These systems take human/AI input and apply it to components. They currently suffer from the same split-authority blind spots.

**Fdp.Examples.NetworkDemo.Systems.RefactoredPlayerInputSystem**,

-   **The Bug:** Queries `.With<ModuleHost.Core.Network.NetworkOwnership>()` and performs a manual primary owner check before modifying `SimVelocity`.-   **The Clean Fix:** Remove the networking component. Use `.WithOwned<SimVelocity>()`.

**Fdp.Examples.NetworkDemo.Systems.CombatInputSystem**,

-   **The Bug:** Queries `.With<NetworkAuthority>()` and explicitly verifies `auth.LocalNodeId == auth.PrimaryOwnerId` before mutating the transform.-   **The Clean Fix:** Strip the `NetworkAuthority` dependency entirely and use `.WithOwned<SimTransform>()`.

3\. Event-Driven Systems (Combat)

When processing events, we cannot use `EntityQuery` filtering, so we must check component-level authority directly against the ECS repository.

**Fdp.Examples.NetworkDemo.Systems.CombatFeedbackSystem**,,

-   **The Bug:** When a `FireInteractionEvent` arrives, it fetches `NetworkOwnership` from the `TargetRoot` entity and checks `own.PrimaryOwnerId == _localNodeId` before subtracting from the `Health` component. If a node owns the health but isn't the primary owner, the damage is silently dropped.-   **The Clean Fix:** Remove the `NetworkOwnership` block completely. Since `ISimulationView` now safely proxies the repository, use:

4\. Network Synchronization Bridges

Systems that sync ECS component state to/from network shadow states are heavily infected with this bug.

**Fdp.Examples.NetworkDemo.Systems.TransformSyncSystem**,,,,

-   **The Bug:** In `SyncOwnedEntities`, it fetches `NetworkAuthority` to check if it's the primary owner. In `SyncRemoteEntities`, it fetches it again to check if it's _not_ the primary owner.-   **The Clean Fix:** For `SyncOwnedEntities`, use `.WithOwned<SimTransform>()` and `.WithOwned<NetworkTransform>()`. For `SyncRemoteEntities`, use `.WithoutOwned<SimTransform>()` and `.With<NetworkTransform>()`. This removes all branching from the hot path and fully respects split authority.

Summary of the Architectural Rule

By making these fixes, you enforce the core tenet of the FDP engine: **Simulation systems must not know they are on a network.** They must simply request components they are allowed to write to (via `WithOwned<T>`), and the Kernel's `ValidateWriteAccess<T>` and bitmask filters will perfectly enforce authority down to the specific byte, regardless of who the "Primary" owner is.

Different nodes can be implemented using different underlying engines (not just FDP, but for example Unreal or Unity). The DDS descriptors needs to be engine agnostic and generic enough to be handled by all these. All the existing DDS messages and descriptors strictly follow this mind set. What would the new descriptors for expressing the motion intent and its result look like?

To achieve a truly engine-agnostic distributed architecture where a "Brain" node (written in C# FDP) can command a "Muscle" node (written in Unreal, Unity, or VBS), the network boundary must be completely scrubbed of any engine-specific implementation details.

In clean distributed systems design, this means our DDS descriptors must act as a **strictly defined domain language**. We cannot transmit FDP's internal Cartesian `Vector2`/`Vector3` coordinates (which are flat-earth, X-East/Y-North), because Unreal uses Z-up/Y-Right centimeters, and Unity uses Y-up/Z-Forward meters.

Instead, we must rely exclusively on universal WGS-84 geodetic coordinates, exactly as the existing `WorldPos` descriptor does using the `GeoPoint` struct.

Here is the clean, engine-agnostic DDS descriptor design that aligns perfectly with the `Hrot.NED.Descriptors` data model and guarantees cross-engine interoperability:

1\. The Intent Descriptor (Brain → Sim)

This descriptor represents the cognitive node's authoritative command. It is defined as a `TransientLocal` topic so that if an Unreal Sim node crashes and restarts, it instantly receives the current movement orders upon reconnecting.

```
using CycloneDDS.Schema;
using Hrot.NED.Common;

namespace Hrot.NED.Descriptors
{
    public enum ENavigationMode : byte
    {
        NAV_NONE = 0,
        NAV_DIRECT_POINT = 1,
        NAV_FOLLOW_ROUTE = 2,
        NAV_JOIN_FORMATION = 3
    }

    [DdsTopic("NavigationIntent")]
    [DdsIdlFile("bdc-sst-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct NavigationIntent
    {
        // 1. Target Entity
        [DdsKey]
        public int EntityId;
    
        // 2. Correlation ID (Idempotency Key)
        // Incremented by the Brain whenever a NEW order is issued.
        public uint IntentId;
    
        // 3. The requested behavior
        public ENavigationMode Mode;
    
        // 4. Engine-Agnostic Spatial Data
        // Uses Lat/Lon/Alt. The Sim node (Unreal/Unity) is responsible for 
        // converting this into its local Cartesian space for pathfinding.
        public GeoPoint FinalDestination;
    
        public float TargetSpeed;   // m/s
        public float ArrivalRadius; // meters
    }
}
```

2\. The Status Descriptor (Sim → Brain)

This descriptor provides the feedback loop. The Sim node is the absolute authority over the physical reality of the world. It publishes this state back to the network.

```
namespace Hrot.NED.Descriptors
{
    public enum ENavigationResult : byte
    {
        RES_IN_PROGRESS = 0,
        RES_ARRIVED = 1,
        RES_FAILED_BLOCKED = 2,
        RES_FAILED_UNREACHABLE = 3
    }

    [DdsTopic("NavigationStatus")]
    [DdsIdlFile("bdc-sst-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct NavigationStatus
    {
        // 1. Target Entity
        [DdsKey]
        public int EntityId;
    
        // 2. Acknowledged Correlation ID
        // The Sim node echoes the IntentId it is currently executing.
        // This prevents the Brain from reading a stale "ARRIVED" status 
        // from a previous waypoint before the Sim has begun the new order.
        public uint IntentId;
    
        // 3. Autonomous Execution Result
        public ENavigationResult Result;
    }
}
```

Architectural Translation Flow

By structuring the data model this way, you establish a perfectly decoupled **Command / Query Responsibility Segregation (CQRS)** pipeline across heterogeneous engines:

-   **Egress Translation (FDP Brain Node):** The FDP `MoveToExecutor` writes to the local, unmanaged `NavState` component. An egress translator reads this Cartesian state, converts it to WGS-84 using the `IGeographicTransform`, and publishes the `NavigationIntent` over DDS.-   **Ingress Translation (Unreal Sim Node):** The Unreal engine receives the `NavigationIntent`. Its ingress layer unpacks the WGS-84 `GeoPoint`, converts it to Unreal's local coordinate system, and hands it off to Unreal's internal NavMesh and AI Controller.-   **Execution & Feedback (Unreal Sim Node):** Unreal's AI Controller handles the actual obstacle avoidance and pathfinding. Once the Unreal Pawn reaches the target (or fails to find a path), Unreal publishes the `NavigationStatus` over DDS.-   **Ingress Translation (FDP Brain Node):** The FDP Brain's ingress translator maps the DDS `NavigationStatus` back into the local ECS. The `MoveToExecutor` reads the status, sees `RES_ARRIVED`, and advances the Behavior Tree.

This design strictly isolates coordinate math and physics logic inside the respective engines. The DDS bus acts only as the universal, strongly-typed state synchronizer, adhering flawlessly to clean distributed systems architecture.

What the elements the brain logically consits of (components, systems...)? I would like to "modularize" it, i.e. to instantiate the "brain capabilities" by installing a module host module or modules. Is (could be, should be?) the brain common for all types of entities, meaning the behavior execution machinery inclusing behavior trees and hiearachical state machines? What/how many modules can we (should we) break it into?

This is an excellent architectural initiative. Breaking apart monolithic logic into highly cohesive, decoupled modules is exactly how we scale an ECS engine gracefully.

In this architecture, the "Brain" is an outstanding example of Data-Oriented Design (DOD). It is strictly separated into cognitive state (memory), cognitive execution (systems), and intent output (channels).

Here is the breakdown of the logical elements, proof of their universality, and the blueprint for how we should modularize them.

1\. What the Brain Logically Consists Of

The brain is entirely decoupled from the "muscles" (physics, kinematics). It reads perceptions, evaluates a state machine or behavior tree, and writes an _intent_ to an output channel.

**The Data (ECS Components):**

-   **Identity & Tier:** `SimTier` (e.g., civilian vs. tactical) and `BehaviorState` (which specific behavior hash is active, and the preemption `InstanceId`).-   **Cognitive Memory:** `BrainBlackboard` (a 128-byte unmanaged fixed buffer for zero-allocation parameter storage).-   **Execution State:** `BrainBTreeState` (for FastBTree), `BrainHsm64`, and `BrainHsm128` (for FastHSM).-   **Output Channels (The Intents):** `LocomotionChannel`, `WeaponChannel`, and `InteractionChannel`. These hold the chosen action ID, status (`Running`, `Success`, `Failure`), and inline parameter/state byte buffers.

**The Logic (ECS Systems):**

-   **Mission & Command:** `BehaviorIngressSystem` (assigns behaviors and parses JSON params into the blackboard) and `MissionDirectorSystem` (advances multi-phase plans based on triggers like time or health).-   **Arbitration:** `ChannelArbitrationSystem` (detects when a behavior changes and instantly preempts/clears stale output channels).-   **The Engines:** `BTreeTickSystem` and `HsmTickSystem<T>` (the generic evaluators that step the logic forward each frame).

2\. Is the Brain Common for All Entities?

**Yes, absolutely.** The cognitive machinery is 100% agnostic to the entity type.

A civilian pedestrian, an insurgent, and an M1 Abrams tank all share the exact same `BrainBTreeState` component and are ticked by the exact same `BTreeTickSystem`.

The architecture achieves this generic universality by treating behaviors as data:

-   The `BTreeTickSystem` simply queries for any entity matching `BehaviorState` where `BrainTier == BrainTierBTree`.-   It looks up the active behavior by its hash in the `BehaviorRegistry`.-   It evaluates the generic interpreter, which writes standard intents (like `ActionIdMoveTo`) into the `LocomotionChannel`.

Because the "Brain" only outputs abstract intents, it doesn't care if the entity has wheels, tracks, or legs.

3\. How We Should Modularize It

Currently, in the legacy example code, these systems are lumped together inside a monolithic `SimulationLogicModule`. To adhere to the Single Responsibility Principle, we should break the brain capabilities into **three distinct** **IModule** **implementations**.

This allows host applications to install only the layers of cognition they actually need.

Module 1: `MissionControlModule` (The Higher Brain)

**Responsibility:** Handling top-down commands, multi-phase mission plans, and behavior assignment. **Execution Policy:** Synchronous (Main Thread) or Fast Replica. **Registered Systems:**

-   `BehaviorIngressSystem` (runs in `InputSystemGroup` to apply new behaviors immediately).-   `MissionDirectorSystem` (runs in `SimulationSystemGroup` to evaluate phase triggers).

Module 2: `CognitiveRuntimeModule` (The Core Brain)

**Responsibility:** Executing the localized AI frame-by-frame. This is the raw evaluation engine. **Execution Policy:** Slow Background (e.g., `ExecutionPolicy.SlowBackground(10)`). BTree and HSM ticks are highly parallelizable and usually don't need to run at a full 60Hz. **Registered Systems:**

-   `ChannelArbitrationSystem` (runs first to clear stale intents).-   `BTreeTickSystem` (evaluates Behavior Trees).-   `HsmTickSystem<BrainHsm64>` / `HsmTickSystem<BrainHsm128>` (evaluates Hierarchical State Machines).-   _Note: You would inject the shared_ _BehaviorRegistry_ _into this module's constructor._

Module 3: `ActionDispatchModule` (The Nervous System)

**Responsibility:** Bridging the generic intents outputted by the brain to the concrete domain executors. **Execution Policy:** Synchronous (runs at 60Hz to provide responsive tracking of the physical state). **Registered Systems:**

-   `LocomotionDispatcherSystem`.-   `WeaponDispatcherSystem`.-   `InteractionDispatcherSystem`.

Architectural Win

By breaking it into these three modules, you achieve perfect composability. If you want to spin up a lightweight "Sim Node" that only does physics (muscles), you install zero brain modules. If you want a dedicated "Commander AI Node", you install the `MissionControlModule` and `CognitiveRuntimeModule`, letting it process the BTree/HSMs and publish the resulting `LocomotionChannel` updates over the DDS network to be executed by the physics nodes.

I need to achieve high flexibility by being able to build a node by putting together relevant modules, instantiating just those necessary for node's role. Like the 'Brain', 'Sim', 'IG' etc. And also an all-in-one node combining everything in a single app.

This should include module(s) also for IG 2d map (end user oriented, polished) and Sim's debug 2d map (developer oriented, with many more debug features) and be able to use them both (switching the 'perspective' - IG or Sim - similarly like now in the Hrot.ClusterRunner if run with multiple subsystems).

how to properly modularize the 2d map related presentation and logic and how to separate (or configure) this for different perspective and how to allow switching the perspectived dynamically in one single application?

To achieve a highly flexible, composable architecture where nodes can run dedicated roles or act as an all-in-one application, we must strictly decouple the simulation state (the ECS data) from the presentation layer. The 2D map is not a monolithic application; it is simply a read-only projection of the ECS world.

Because both the IG and the SimHost rely on the same fundamental ECS primitives (`SimTransform`, `SimVelocity`), we can construct distinct presentation modules that share the same data but render it completely differently.

Here is the clean architecture approach to modularizing and switching 2D map perspectives dynamically.

1\. Separate Presentation into Distinct Modules

Instead of embedding the `MapCanvas` and rendering logic deeply into application shells like `IgApplication` or `SimHostVisualization`, you should wrap them in formal `IModule` implementations that register systems in the `PresentationSystemGroup`.

You will create two independent modules:

-   **IgPresentationModule**: Configured for the end-user. It instantiates a `MapCanvas` equipped with the `SstVisualizerAdapter`, which resolves MIL-STD-2525 symbols using `ResolvedStyle` and applies performance gates via `CullingState`. Its tool stack is populated with operational tools like `CreationTool` and `MeasureTool`.-   **SimPresentationModule**: Configured for the developer. It instantiates its own `MapCanvas` equipped with the `SimHostVehicleVisualizer`, which bypasses LOD culling to draw raw bounding boxes color-coded by internal AI states like `NavState` or `FormationRoster`. It registers diagnostic layers like `SimHostRoadLayer` and `SimHostTrajectoryLayer`.

2\. Encapsulate Rendering in ECS Systems

Currently, map drawing is invoked imperatively by the application shell calling `DrawWorld()`. To make this modular, wrap the canvas update and draw calls in ECS systems.

```
[UpdateInGroup(typeof(PresentationSystemGroup))]
public class IgMapRenderSystem : ComponentSystem
{
    private readonly MapCanvas _canvas;
    
    public IgMapRenderSystem(MapCanvas canvas) => _canvas = canvas;
    
    protected override void OnUpdate()
    {
        // Check a global perspective singleton to see if we should render
        if (!World.GetSingleton<ActivePerspective>().IsIgActive) return;
    
        _canvas.Update(DeltaTime);
        _canvas.Draw();
    }
}
```

By placing this in the `PresentationSystemGroup`, the engine guarantees that all physics integration (`PostSimulationSystemGroup`) is fully resolved before any rendering occurs.

3\. Dynamic Perspective Switching

When you build an "all-in-one" node, you simply register both the `IgPresentationModule` and the `SimPresentationModule` into the same `ModuleHostKernel`.

To allow dynamic switching without visual jumping, you must synchronise the `MapCamera` state and ensure only one canvas consumes input and issues draw calls at a time. The FDP architecture already provides the exact mechanism for this via `MapCamera.SnapTo()`.

**The Implementation:** Introduce an unmanaged singleton component to control the active view:

```
[ComponentId(GlobalComponentIds.ActivePerspective)]
public struct ActivePerspective
{
    public PerspectiveType Current; // e.g., Enum: IG, Sim
}
```

Create a higher-level `PerspectiveCoordinatorSystem` (running before the render systems) that listens for a UI toggle (like an ImGui button or a keyboard shortcut). When a perspective switch is requested:

-   It updates the `ActivePerspective` singleton so the outgoing map system stops rendering and the incoming one starts.-   It fetches the `MapCamera` from both modules.-   It calls `incomingCamera.SnapTo(outgoingCamera)`.

Because `SnapTo` copies the `Zoom`, `Target`, and `Offset` values, the incoming perspective will perfectly match the exact world region and zoom level the user was just looking at, eliminating any jarring camera resets.

Architectural Summary

By treating the `MapCanvas`, layers, and input tools as internal state of an `IModule`, you adhere to the Single Responsibility Principle.

-   If you deploy a headless "Sim" node, you install zero presentation modules.-   If you deploy a dedicated "IG" node, you install `IgPresentationModule`.-   If you deploy a local developer environment, you install both, and the `ActivePerspective` singleton acts as an immediate-mode multiplexer, determining which module's canvas gets to draw its interpretation of the shared ECS reality to the screen.

For example the layer concept is very usefull in both the IG perspective and Sim perspective. Similarly the many map tools might be very usefull. So the perspectives themselves need to be flexibly configurable, the tools shareable, and stuff like layer filtering needs to be generic enough to be used in both perspectives but very likely slightly differently. For example the IG will use different kind of layers and different kinds of renderable layer-filterable elements than Sim, but the foltering concept and infrastucture shoudl be shared to follow DRY principles.

This is exactly the right architectural mindset. You are looking for the **Dependency Inversion Principle (DIP)** applied to an ECS rendering pipeline.

The good news is that the codebase already contains a dedicated toolkit designed precisely for this: **FDP.Toolkit.Vis2D**. It provides the generic map, tool, and layer abstractions without knowing anything about your domain logic (tanks, aircraft, IG, or SimHost).

Here is how you leverage this shared infrastructure to build highly configurable, distinct perspectives (IG and Sim) while strictly following DRY principles.

1\. The Shared Presentation Infrastructure (`Vis2D`)

The `Vis2D` toolkit owns the `MapCanvas`, the camera, and the contract interfaces (`IMapLayer`, `IMapTool`, `IVisualizerAdapter`). It provides generic implementations that both the IG and the SimHost share.

**The Shared Filtering Concept:** Layer filtering is unified through the `MapDisplayComponent` (a 32-bit bitmask). The generic `EntityRenderLayer` iterates over entities and performs an ultra-fast bitwise `&` against the canvas's `VisibleLayersMask`. Because this component is an unmanaged ECS struct, the filtering logic is shared, highly cache-friendly, and completely agnostic to what the entity actually represents.

2\. Configuring the Perspectives (Dependency Injection)

To create the IG and Sim perspectives, you instantiate two separate `MapCanvas` objects and inject different concrete adapters into the shared `EntityRenderLayer`.

**The IG Perspective (End-User):**

-   **Visualizer:** You inject the `SstVisualizerAdapter`, which resolves complex TKB styles, handles LOD culling, and draws polished MIL-STD-2525 symbol textures.-   **Layers:** You map bits to tactical concepts via `MapLayerRegistry` (e.g., bit 0 = "units\_ground"). You add the `MapOverlayRenderLayer` to draw tactical graphics.

**The Sim Perspective (Developer/Debug):**

-   **Visualizer:** You inject the `SimHostVehicleVisualizer`, which bypasses LOD, draws raw bounding boxes, and color-codes entities based on their AI `NavState` or formation role.-   **Layers:** You inject debug-specific layers like `SimHostRoadLayer` (draws the navigation graph) and `SimHostTrajectoryLayer` (draws the Pure Pursuit paths).

Both perspectives use the exact same `EntityRenderLayer` from `Vis2D`, completely eliminating code duplication for the rendering loop and filtering mechanics.

3\. Sharing and Customising Tools

Map tools also live in `Vis2D` and rely on interfaces to stay generic.

Take the `EntityPickerTool` as an example. It needs to filter clicks (e.g., "only let the user click road graphs"). Instead of hardcoding IG layer logic into the tool, the tool requires an `IEntityFilterFactory`.

-   In the IG node, you inject `HrotEntityFilterFactory`, which translates strings like `"road_graphs"` into a bitmask check against the `MapDisplayComponent`.-   If the SimHost needs an entity picker, it can inject a completely different factory (e.g., one that filters by AI state) without changing a single line of the tool's code.

For interactions, the IG wraps the generic `FDP.Toolkit.Vis2D.Tools.StandardInteractionTool` inside its own `Hrot.IG.Tools.StandardInteractionTool`. This allows the IG to reuse 100% of the drag/drop/box-select math, while simply subscribing to the tool's C# events to route network DDS messages (like `MapClickEvent` or `DragEvent`) back to the IOS.

4\. Dynamic Switching in an "All-In-One" Node

To build a single application that can switch between these perspectives dynamically (like a developer workstation), you compose them side-by-side using the `SubsystemOrchestrator`.

Both subsystems (`IgSubsystem` and `SimHostSubsystem`) expose their configured map views via the `IMapCameraProvider` interface.

When the user clicks the toggle button to switch from the IG view to the Sim view, the orchestrator executes a seamless transition:

-   It updates the `_activeMapOwner` flag, determining which subsystem's `DrawWorld()` method is called this frame.-   It fetches both cameras and calls `incomingCamera.SnapTo(outgoingCamera)`. This instantly copies the zoom, target, and offset.

Because both perspectives are querying the exact same underlying ECS `EntityRepository`, and the orchestrator perfectly syncs the cameras, the user experiences a flawless, instant visual transition from polished military symbols to raw debug collision boxes, with absolutely zero state reconstruction.

How about modularizing the network layers? There are different translators and other infrastructure needed for Sim, different for Brain, different for IG etc.

Also different roles need different componens and systems registered, many time these all needs overlap. How to organize the registration of these to be higly shareable for overlapping (shared) stuff with just the very specific one being present just in the specific modules?

This is exactly where we separate a good ECS engine from a truly enterprise-grade distributed system. You are hitting on the core of scalable Data-Oriented Design (DOD) and the **Dependency Inversion Principle (DIP)**.

Right now, your application shells (like `SimHostApp` and `IgApplication`) are acting as "God Classes". They manually register every component across all domains, and they manually instantiate massive lists of `IDescriptorTranslator` objects before feeding them into the `CycloneNetworkModule`. This tightly couples the orchestrator to the internal details of every feature.

To achieve true modularity where you can snap together a "Brain Node", a "Muscle Node", or an "All-in-One Node" with zero code duplication, we must organize the registration into **Domain Packs**.

Here is the clean architecture approach to highly shareable, role-based registration.

1\. The Data Layer: Component Registry Slicing

Currently, `SimHostComponentRegistry` registers everything: AI brains, CarKinem physics, combat, and perception. We need to split this into strict domain boundaries.

We already have `HrotSharedComponentRegistry` for the baseline network and lifecycle components. We will build upon this by creating domain-specific static registries.

```
// The Muscle components
public static class KinematicComponentRegistry 
{
    public static void RegisterAll(EntityRepository world) 
    {
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<VehicleState>();
        // ...
    }
}

// The Brain components
public static class CognitiveComponentRegistry 
{
    public static void RegisterAll(EntityRepository world) 
    {
        world.RegisterComponent<BrainBTreeState>();
        world.RegisterComponent<BrainBlackboard>();
        world.RegisterComponent<MissionPlanQueue>();
        world.RegisterComponent<NavigationIntent>(); // From our CQRS design
        // ...
    }
}
```

2\. The I/O Layer: Translator Packs

The `CycloneNetworkModule` accepts a list of custom `IDescriptorTranslator` instances at initialization. Instead of the App Shell knowing exactly which translators are needed for an aircraft versus a brain, we encapsulate translator creation into **Translator Packs**.

These packs strictly define the ingress/egress boundary for a specific domain.

```
public static class KinematicTranslatorPack
{
    public static IEnumerable<IDescriptorTranslator> Create(
        DdsParticipant participant, 
        NetworkEntityMap entityMap,
        IGeographicTransform geoTransform)
    {
        // Muscles output reality
        yield return new WorldPosEgressTranslator(participant, entityMap, geoTransform);
        yield return new NavigationStatusEgressTranslator(participant, entityMap);
        
        // Muscles receive intent
        yield return new NavigationIntentIngressTranslator(participant, entityMap);
    }
}

public static class CognitiveTranslatorPack
{
    public static IEnumerable<IDescriptorTranslator> Create(
        DdsParticipant participant, 
        NetworkEntityMap entityMap)
    {
        // Brains output intent
        yield return new NavigationIntentEgressTranslator(participant, entityMap);
        yield return new EntityMissionEgressTranslator(participant, entityMap);

        // Brains receive reality
        yield return new WorldPosIngressTranslator(participant, entityMap);
        yield return new NavigationStatusIngressTranslator(participant, entityMap);
    }
}
```

3\. The Logic Layer: Module Composition

We continue the existing practice of grouping systems into `IModule` implementations, but we ensure they strictly adhere to their domain. For instance, `GroundKinematicsModule` only registers `CarKinematicsSystem`, and `CognitiveRuntimeModule` only registers `BTreeTickSystem` and `HsmTickSystem`.

4\. The Composition Root (The Node Builder)

Finally, we create a highly cohesive `NodeBootstrapper` or `RoleBuilder` inside the application shell. This completely replaces the monolithic `Initialize()` methods in your applications.

By defining the `NodeRole` at startup, the bootstrapper aggregates the correct components, modules, and translator packs cleanly.

```
public enum NodeRole { Brain, MuscleGround, ImageGenerator, AllInOne }

public class NodeBootstrapper
{
    public void Bootstrap(NodeRole role, EntityRepository world, ModuleHostKernel kernel, DdsParticipant dds)
    {
        var translators = new List<IDescriptorTranslator>();

        // 1. Shared Foundation (Always required)
        HrotSharedComponentRegistry.RegisterAll(world);
        translators.Add(new EntityMasterEgressTranslator(dds, ...));
        translators.Add(new EntityMasterIngressTranslator(dds, ...));
    
        // 2. Compose Role-Specific Capabilities
        if (role == NodeRole.MuscleGround || role == NodeRole.AllInOne)
        {
            KinematicComponentRegistry.RegisterAll(world);
            kernel.RegisterModule(new GroundKinematicsModule());
            translators.AddRange(KinematicTranslatorPack.Create(dds, _entityMap, _geo));
        }
    
        if (role == NodeRole.Brain || role == NodeRole.AllInOne)
        {
            CognitiveComponentRegistry.RegisterAll(world);
            kernel.RegisterModule(new MissionControlModule());
            kernel.RegisterModule(new CognitiveRuntimeModule());
            translators.AddRange(CognitiveTranslatorPack.Create(dds, _entityMap));
        }
    
        if (role == NodeRole.ImageGenerator || role == NodeRole.AllInOne)
        {
            VisualComponentRegistry.RegisterAll(world);
            kernel.RegisterModule(new IgPresentationModule());
            translators.AddRange(VisualTranslatorPack.Create(dds, _entityMap));
        }
    
        // 3. Finalize Network Module
        var networkModule = new CycloneNetworkModule(
            dds, nodeMapper, idAllocator, topology, elm,
            customTranslators: translators,
            sharedEntityMap: _entityMap);
            
        kernel.RegisterModule(networkModule);
    }
}
```

Architectural Wins

By adopting this pattern:

-   **DRY Principle:** You never write `world.RegisterComponent<SimTransform>()` or `new WorldPosEgressTranslator(...)` more than once in the codebase.-   **Open/Closed Principle:** If you add an "Aircraft Muscle" node later, you simply create an `AirKinematicTranslatorPack` and add one `if` statement to the bootstrapper. You don't have to touch the Brain, the IG, or the core network module.-   **Deployment Agility:** You can compile a single executable and deploy it across a cluster. Server 1 runs with `--role brain`, Server 2 with `--role muscleground`. Their memory footprints and CPU cache utilization will be perfectly optimized for their specific tasks, carrying zero dead weight from the other domains.

There are for sure some low level components that need to be registered (almost) always as they are used by (almost) any role. Like SimTransform and similar. They can hardly be treated as belonging strictrly to the same group as vehicle kinematics as there mught me bany more entities having nothing to do with kinematics (like static waypoint or whatever).

I believe the component registration is idempotent so there is no issue if we register same component multiple times, is that correct? Allowing us to register same components from multiple different domain packs without the need for extremly strict "single-registration" rule. But this probably does not apply to ECS systems as easily. As long as the systems are single-instance only the idempotency is possible, but some systems might be configurable and instantiated in multiple instances and here the "single-registration of each configured instance" matters. Unless we somehow identify the configured instaces, but strict separation into non-overlapping domain packs is probably cleaner and safer.

You have hit the nail on the head. Your intuition about the asymmetry between data (components) and logic (systems) is perfectly aligned with the realities of building scalable ECS architectures.

Here is the architectural breakdown of how the FDP engine handles this, and why your instinct about strict domain separation for systems is exactly right.

1\. Component Registration is Strictly Idempotent

You are completely correct. In the FDP kernel, component registration is strictly idempotent. When you call `world.RegisterComponent<T>()`, the underlying `ComponentTypeRegistry` checks if the type is already mapped to an ID and returns early if it is. Likewise, the `EntityRepository` checks if a memory table for that component already exists and silently skips re-allocation.

Because of this, you _could_ technically have every single domain pack redundantly register `SimTransform`. However, to keep the architecture clean and DRY, the codebase already implements a "Foundation Pack" pattern specifically for these ubiquitous low-level primitives.

**The** **HrotSharedComponentRegistry** **Foundation:** The architecture provides `HrotSharedComponentRegistry.RegisterAll(world)` as the single source of truth for base-level ECS primitives. It registers the foundational layer that _every_ networked role requires, regardless of whether it is a Brain, a Muscle, or an IG. This includes:

-   **Geographic / Spatial:** `SimTransform`, `SimVelocity`.-   **Network Identity:** `NetworkIdentity`, `NetworkOwnership`, `NetworkAuthority`.-   **Lifecycle:** `ConstructionOrder`, `DestructionAck`, etc..

In your bootstrapper, you simply call the Foundation pack first, and then the specific Domain packs. The Domain packs (like `KinematicComponentRegistry`) only need to register their highly specific components (e.g., `VehicleState`, `VehicleParams`).

2\. System Registration is NOT Idempotent (And Shouldn't Be)

Your suspicion about ECS systems is spot on. While component definitions are pure data structures, systems are active processing loops.

If you look at the kernel's `SystemScheduler` and `SystemGroup` implementations, they simply append the provided system instance to an internal `List<IModuleSystem>`. There is no deduplication by type. If two overlapping domain packs both try to register a `CarKinematicsSystem`, the kernel will happily run the physics integration twice per frame, causing your vehicles to move at double speed and completely corrupting the simulation. Furthermore, as you noted, a system might be instantiated multiple times with different configurations (e.g., passing a different `RoadNetworkBlob` or `TrajectoryPoolManager`), making type-based deduplication impossible.

The Clean Architecture Conclusion

Because systems represent the _behavior_ of the node and are non-idempotent, we must strictly enforce non-overlapping **Domain Packs** (packaged as `IModule` implementations).

-   **The Data Layer (Components):** Can tolerate overlap thanks to idempotency, but is best organized into a `SharedFoundationRegistry` + strict `DomainRegistries`.-   **The Logic Layer (Systems):** Must be strictly separated into mutually exclusive `IModule` implementations (e.g., `GroundKinematicsModule`, `CognitiveRuntimeModule`).

By keeping the system boundaries strict, the orchestrator acts as a clean composition root. You assemble the exact personality of a node by installing the right system modules, completely eliminating the risk of double-execution or conflicting logic loops.

The component ids of all the entities are now enumerated centrally inside the generic FDP sources. This is undesired as the basic ones (used by the FDP engine) are stored in same file like the project-specific ones. This is good and bad for maintenance - we do not want to touch the FDP generic sources if we introduce project-specific new component. We need multiple centralized registration points for component Id constants, each with dedicate id ranges or something.

You are absolutely right to flag this. Centralising every component ID across both the framework and all end-user projects into a single `GlobalComponentIds` file is a severe violation of the **Open/Closed Principle**. Every time a new project-specific component is introduced, it forces a modification to the core FDP engine's source code, creating unnecessary coupling and merge conflicts.

From a clean architecture perspective, the FDP kernel does not actually care where the constants are defined; it only cares that the resulting IDs are deterministic and collision-free.

Here is how we can cleanly decouple the registration points while respecting the engine's hard constraints.

The Architectural Constraint

At the lowest level, the FDP engine relies on a 256-bit SIMD-optimized mask (`BitMask256`) for O(1) query filtering, which strictly limits the entire system to 256 unique component types. Because the `[ComponentId]` attribute simply accepts a `byte`, it does not strictly require a single centralized class.

The Solution: Distributed Registries with Managed Ranges

Instead of one monolithic file, we should distribute the ID constants into domain-specific static classes within their respective projects, adhering to a documented block-allocation strategy.

Currently, `GlobalComponentIds` already documents a block allocation strategy (e.g., `0-19` for Kernel, `110-139` for Hrot.IG, `200-255` for Custom/Examples). We simply need to extract these blocks into their own bounded contexts.

**1\. The Framework Registry (Inside FDP Core)** Leave only the engine and toolkit primitives in the FDP core layer.

```
namespace Fdp.Kernel
{
    public static class FdpCoreComponentIds
    {
        // 0-19: Core
        public const byte SimTransform = 1;
        public const byte SimVelocity = 2;
        
        // 20-109: FDP Toolkits
        public const byte BehaviorState = 22;
    }
}
```

**2\. The Project Registry (Inside Hrot.IG)** The IG project defines its own registry, strictly staying within its allocated `110-139` block.

```
namespace Hrot.IG.Components
{
    public static class IgComponentIds
    {
        // 110-139: Hrot.IG block
        public const byte ResolvedStyle = 110;
        public const byte CullingState = 111;
        public const byte SelectionState = 112;
    }
}
```

The IG components then reference their local registry:

```
[ComponentId(IgComponentIds.ResolvedStyle)]
public struct ResolvedStyle { ... }
```

**3\. The Application/Example Registry (Inside specific apps)**

```
namespace Fdp.Examples.UrbanCombat
{
    public static class UrbanCombatComponentIds
    {
        // 200-255: Reserved block
        public const byte CivilianMetadata = 200;
    }
}
```

Why this is safe

You might worry that distributing the constants removes the safety of having them all in one file where developers can easily spot duplicates.

However, the architecture already has an automated safeguard against this. The `ComponentTypeRegistry` actively tracks explicitly assigned IDs. If two different projects accidentally claim the same byte ID, the system's runtime collision detection will instantly fail fast during startup, throwing an `InvalidOperationException` that clearly states which two components caused the collision.

By moving to this distributed constant pattern, you achieve perfect strict modularity: FDP engineers maintain the `0-109` blocks, Hrot developers maintain the `110-199` blocks, and downstream integrators can safely build entirely new components in the `200-255` range without ever touching the FDP repository.

Dnes • 9:54

--------------------------









The brain needs sensors to feel/see the "world". The perception mechanism should be separated from the brain, possibly implemented in another node. The perception mechanism should be able to provide both low level information based on dumb direct queries (like a batch of raycasts should produce a bunch of results till the next frame) as well as "smart sensors", higher-level aggregated information as a result of continuous and autonomous operation of the perception mechanisms (for example low frequency scanning the entities around an ownship entity, providing the list of potential targets or threats, sorted by threat level or whatever according to the dynamically updatable sensor parameters controllable from outside). In the latter case, the brain would just read (for example) the list of threats sorted by priority, supporting further decisions, and "magically" updated automatically (by perception writing those to ECS compoennts being transferred over network to the brain. ). Similarly the direct queries would be written to a query ECS component and handed over to the perception mechanism for processing, producing results into anothe ECS component (being transferred back to brain if on another node). The direct queries expected to produce the corresponding results need to be marked with some version number/correlatonid to detect whether the results have arrived. The dumb direct queries need to be very fast (best till next frame) while the "smart sensor" output might be updated at lower rate. The sizes of the dumb query batches is variable and might be pretty large (thousands of reaycsts). The size fo the smart-sensor data is typically small (list of sensoric results - few most important records like the ones with highest threat level) but the records are typicalyl complex. In both cases these would probably be best implemented as managed components. pls suggest how to approach this.

This is an excellent architectural question. Separating the "Brain" (decision making) from "Perception" (sensory evaluation) is a textbook application of the Single Responsibility Principle and Command Query Responsibility Segregation (CQRS).

However, as a developer passionate about clean Data-Oriented Design (DOD), I must strongly push back against your suggestion to use managed components (heap-allocated classes or lists) for these data pipelines. In a high-performance ECS, thousands of raycasts or dynamically sized lists of threats allocated every frame will instantly melt your Garbage Collector and destroy your CPU cache locality.

The FDP engine already anticipates this exact distributed perception architecture and solves it using **pure unmanaged memory and native arrays**.

Here is how you should architect both the "Smart Sensors" and "Dumb Queries" pipelines across the network.

1\. Smart Sensors (The Autonomous Slow Path)

Smart sensors should run continuously and autonomously, completely decoupled from the Brain's tick rate. The FDP engine implements this via the `PerceptionModule`, which runs asynchronously on a background thread at a lower frequency (e.g., 10 Hz).

**The Data Contract:** Instead of a managed list of complex objects, the perception output is written into a strictly bounded, unmanaged struct called `TargetMemory`. It uses `fixed` arrays (e.g., `fixed long EntityIds`, `fixed float ThreatScores`) to store the top prioritized threats. Because it is unmanaged, it fits directly inside the ECS chunk memory and generates zero GC pressure.

**The Distributed Flow:**

-   **Perception Node (Execution):** Evaluates vision cones (`VisionBroadphaseSystem`) and calculates threat scores (`ThreatEvaluationSystem`). It automatically drops decaying threats and sorts the array descending by threat level.-   **Network Replication:** The Perception Node is authoritative over the `TargetMemory` descriptor. It publishes this unmanaged struct over DDS.-   **Brain Node (Consumption):** The Brain node receives the DDS updates and overwrites its local shadow `TargetMemory`. The AI behaviors (like `Ambush_BT`) never loop or calculate distances; they simply query `TargetMemory.Count > 0` in O(1) time to instantly know if a threat is present.

2\. Dumb Direct Queries (The Ultra-Fast Path)

For "dumb" queries like massive raycast batches, you need results within a single frame.

**The Data Contract:** Do not use an ECS component per query. Storing thousands of individual query components fragments memory. Instead, the architecture uses a pre-allocated unmanaged singleton called `RaycastBatchData`. It contains `NativeArray<RaycastRequest>` and `NativeArray<RaycastHit>`.

To satisfy your requirement for correlation IDs, the `RaycastRequest` struct includes a `long RayId` field, which the solver perfectly mirrors into the corresponding `RaycastHit.RayId`.

**The Execution Flow:**

-   **Submission:** Systems write directly into the native request array.-   **Execution:** The `RaycastSolverSystem` runs in the `InputSystemGroup` and processes the entire batch using `Parallel.For` across all CPU cores.-   **Resolution:** Next frame, the requesting system reads the parallel `Hits` array, matching its original `RayId`.

3\. The Network Boundary Reality (A Warning)

While you _can_ put the Smart Sensors on a separate node (because 10 Hz DDS replication of `TargetMemory` is cheap), **you should almost never put the Dumb Queries (Raycasts) on a remote node.**

If the Brain node generates 10,000 raycast requests and sends them over DDS to a Perception node, the serialization, network transit, and deserialization will absolutely violate your "next frame" requirement. It will introduce severe multi-frame latency and saturate your network bandwidth.

**The Clean Architecture Solution:** Instead of sending the _queries_ over the network, you replicate the _spatial reality_.

-   The Physics node streams `WorldPos` updates (the `SimTransform` of all entities) over DDS.-   The Brain node receives these positions.-   The Brain node runs its own local `SpatialHashSystem` to rebuild the spatial grid in memory.-   The Brain node submits its 10,000 raycasts to its _local_ `RaycastBatchData` singleton and solves them locally via its own `RaycastSolverSystem`.

By doing this, the Brain achieves zero-latency sensory queries against a perfectly synchronized spatial shadow of the world, keeping network traffic strictly limited to entity state changes rather than raw computational RPCs.

by the 'managed components' i meant the possibility of using variable sized batches, not a size limited fixed size components. Singleton component will do. The dumb raycasy queries as well as results should be of course added into a pre-allocated native arrays with no GC overhas whatsoever. The latency is something the whole system must be adapted to. If not networked and running on a single node, the results might be available very soon, like next frame. In case of a remote perception code, it can take more frames. Brain can not always have its own dedicated local perception-supporting mechanisms. For simple queries like entity proximity, local spatial hash is a good solution for broad phase filtering. For high resolution line of sight queries, the "world" is often calculated in high details on different dedicated node (or nodes for distributing the load) running full physics and/or full visual scene graph. So the sensors must run there (remotely from brain, but locally close to the physics), communicating the request/responses over the network.

You are absolutely spot on. Architecturally, offloading high-resolution geometric line-of-sight (LOS) or physics queries to a dedicated remote node (or cluster of nodes) is exactly how enterprise-grade simulations scale. The "Brain" shouldn't be burdened with a multi-gigabyte NavMesh or 3D collision scene graph in memory.

This requires us to embrace **Eventual Consistency** and true **Asynchronous CQRS** across the network boundary, accepting that a request might take N frames to resolve.

Here is how we cleanly bridge your zero-allocation, fixed-capacity native arrays in the ECS kernel with variable-sized, latency-tolerant batches over the DDS network.

1\. The Network Boundary (DDS Contracts)

We accept managed allocations (like `List<T>`) at the DDS network boundary because network serialization inherently requires variable-sized buffers. However, we strictly isolate this from the ECS hot path using translators.

We define two new DDS descriptors:

```
[DdsTopic("RaycastRequestBatch")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
public partial class RaycastRequestBatch
{
    [DdsKey] public int SourceNodeId; // The Brain node requesting the LOS
    public uint BatchCorrelationId;
    public List<DdsRaycastRequest> Requests; // Variable-sized
}

[DdsTopic("RaycastResponseBatch")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
public partial class RaycastResponseBatch
{
    [DdsKey] public int TargetNodeId; // Routing back to the specific Brain node
    public uint BatchCorrelationId;
    public List<DdsRaycastHit> Hits; // Variable-sized
}
```

2\. The Distributed Pipeline

We take the existing monolithic raycast pipeline and "cut" it in half, inserting the network translators in the middle.

On the Brain Node (Egressing Requests)

Your AI behaviors (e.g., `VisionBroadphaseSystem`) continue to write requests into the **local, unmanaged** `RaycastBatchData` singleton using fast native arrays.

Instead of a local physics solver reading this, you introduce a `RaycastBatchEgressTranslator`. At the end of the `Input` phase, this translator reads the populated native array, copies the data into the variable-sized `RaycastRequestBatch` DDS class, publishes it, and instantly zeroes out the local `RaycastBatchData.Count`.

On the Sim/Physics Node (The Solver)

The remote physics node has a `RaycastBatchIngressTranslator`.

-   It receives the DDS batch and copies the requests into its _own_ local, unmanaged `RaycastBatchData.Requests` native array.-   The `RaycastSolverSystem` runs in parallel (using `Parallel.For`), evaluating the math against the high-resolution collision graph. It writes the results into the local `Hits` native array.-   The physics node's `RaycastBatchEgressTranslator` scoops up the `Hits` native array, packages it into a `RaycastResponseBatch` DDS message, and fires it back to the Brain.

Back on the Brain Node (Ingressing Results)

The Brain's `RaycastBatchIngressTranslator` receives the response. It copies the hits back into the Brain's local `RaycastBatchData.Hits` array. Finally, the `HitResolutionSystem` runs, iterating the hits and publishing `TargetVisibleEvent` onto the local event bus for the AI to consume.

3\. Dealing with Latency in the Brain

Because the response takes multiple frames, the AI logic must not stall. The architecture handles this elegantly through **stateful decoupling**.

**For Smart Sensors (Autonomous Scanning):** If the broadphase queues a bunch of LOS checks, the system simply continues. When the `TargetVisibleEvent` finally arrives N frames later, the `ThreatEvaluationSystem` consumes it and boosts the target's score in the `TargetMemory` component. The BTree naturally reacts to the updated `TargetMemory` on its next tick. The latency just means the AI's reaction time is organically delayed by the network round-trip.

**For Dumb Queries (Behavior Tree specific checks):** If a Behavior Tree explicitly requests a single raycast (e.g., an `Action_CheckLineOfFire` node), it writes the request to the channel and returns `NodeStatus.Running`. The node caches the `RayId` in its blackboard. On subsequent frames, it checks a local response queue. Until the response matching the `RayId` arrives, the node yields `Running`, effectively suspending that branch of the tree while the rest of the simulation continues uninterrupted.

Architectural Win

By isolating the variable-sized batching strictly to the DDS Translator layer, both the Brain and the Sim nodes get to keep their internal loops perfectly Data-Oriented (DOD). They iterate tightly packed Native Arrays with zero Garbage Collection, while the network seamlessly handles the variable workloads and multi-frame latency asynchronously.

what channel the BTree is writing the reaycast requests into? Do we have it already or does it need to be added?

Currently, the BTree does _not_ write raycast requests into an ECS Channel, and from a clean Data-Oriented Design (DOD) perspective, we absolutely should not add one.

In this architecture, channels (like `LocomotionChannel`, `WeaponChannel`, and `InteractionChannel`) are designed to hold **continuous, stateful intent**. They act as the "Command" side of our CQRS pattern, persisting an action until an executor resolves it. A raycast, however, is a **discrete, fire-and-forget query**, which means it belongs in a completely different data pipeline.

What We Have Now

The engine already provides the optimal, zero-allocation destination for these queries: the **RaycastBatchData** **singleton**. This component holds a pre-allocated `NativeArray<RaycastRequest>` and a parallel `NativeArray<RaycastHit>`.

Currently, autonomous "smart sensors" use an event-driven bridge to reach this batch: the `VisionBroadphaseSystem` emits a `LosCheckRequestEvent`, which the `LosRequestBatchingSystem` consumes and translates into the `RaycastBatchData`.

However, if you look at the BTree's direct integration in `BTreeContext.cs`, you will see that explicit raycast queries are currently stubbed out:

```
// Raycast / pathfinding batching is not implemented in this adapter.
// Nodes that require these services must be integrated via a separate
// async request-response system (Phase 3+).
int IAIContext.RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance) => -1;
```

What Needs to Be Added

You do not need to add a new component. You just need to wire the existing `BTreeContext` directly to the `RaycastBatchData` singleton so the BTree can write to the native array.

Here is the clean, high-performance way to implement it:

**1\. Inject the Batch into the Context** Modify `BTreeTickSystem.OnUpdate` to fetch the global raycast batch and pass it into the `BTreeContext` struct (as a `ref` or unsafe pointer to avoid copying).

**2\. Implement** **RequestRaycast** Inside `BTreeContext`, implement the interface method to append directly to the native array:

```
int IAIContext.RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance)
{
    // Write directly into unmanaged memory. Zero GC overhead.
    int index = _raycastBatch->Count++;
    long rayId = GenerateCorrelationId(Self.Index); 
    
    _raycastBatch->Requests[index] = new RaycastRequest
    {
        Start = origin,
        End = origin + (direction * maxDistance),
        RayId = rayId,
        IgnoreEntity = Self 
    };
    
    return (int)rayId;
}
```

**3\. Reading the Result in the BTree Node** When your custom BTree node (e.g., `Action_CheckLineOfFire`) calls `RequestRaycast`, it receives the `rayId` and saves it to the `BrainBlackboard` (the unmanaged memory block for BTree state). On the next frame, the node calls `IAIContext.GetRaycastResult(rayId)`. The context simply loops through the `RaycastBatchData.Hits` native array, matches the `RayId`, and returns the result.

By using the global `RaycastBatchData` singleton, we batch all raycasts from all entities (AI nodes, ballistics, vision sensors) into a single contiguous memory block. This guarantees that the `RaycastSolverSystem` can rip through them using `Parallel.For` with perfect CPU cache locality and zero heap allocations.

how do we handle the smart sensor dynamic parametrizing and results?

In this architecture, both the dynamic parametrisation of sensors and the aggregation of their results are strictly handled through unmanaged ECS value types. This ensures we adhere to Data-Oriented Design (DOD) principles, maintaining high CPU cache locality and zero Garbage Collection (GC) overhead on the hot path.

Here is how the pipeline is cleanly decoupled into parameters, processing, and results.

1\. Dynamic Parametrizing (`PerceptionReceptor`)

The configuration of an entity's sensors is stored in the `PerceptionReceptor` component. This is an unmanaged struct that defines the physical limits of the entity's perception:

-   `VisionRange`: The maximum distance in meters the entity can see.-   `HearingRange`: The maximum distance for audio stimuli.-   `FieldOfViewCos`: The precomputed cosine of the half-FOV angle.

**Clean Architecture Win:** Storing `FieldOfViewCos` instead of the raw angle is a classic DOD optimisation. It prevents the `VisionBroadphaseSystem` from evaluating expensive trigonometric functions (`MathF.Cos`) for every entity pair every frame. Instead, the system simply performs a fast vector dot-product and compares it directly to this cached cosine value.

To dynamically change a sensor's parameters (e.g., a "narrow focus" command or sensor damage), an external system simply updates this component via `World.GetComponentRW<PerceptionReceptor>(entity)`. The perception systems read this struct every tick, so parameter changes take effect instantly.

2\. Processing the Sensors

The `PerceptionModule` runs asynchronously on a background thread using a Snapshot-on-Demand (SoD) policy.

-   **Vision:** The `VisionBroadphaseSystem` iterates over entities with a `PerceptionReceptor`, queries a spatial hash grid using the `VisionRange`, filters targets using the `FieldOfViewCos`, and emits `LosCheckRequestEvent`s for any targets that pass the broadphase.-   **Audio:** The `AudioPerceptionSystem` listens for `AudioStimulusEvent`s and checks them against the listener's `HearingRange`.

3\. The Results (`TargetMemory`)

The final, aggregated output of all sensors is written into the `TargetMemory` component. Instead of allocating a variable-sized managed `List<T>` of threats—which would destroy the heap—the architecture uses a strictly bounded, unmanaged struct with `fixed` arrays.

-   It holds up to `PerceptionConstants.MaxTrackedTargets` (currently 4).-   It tracks the `EntityIds`, `PositionsX/Y`, `ThreatScores`, and the `LastSeenTick` for each perceived target.

The `ThreatEvaluationSystem` acts as the sensory aggregator. Every frame, it applies a decay factor to all existing `ThreatScores` (so old threats are forgotten if not re-acquired) and adds score boosts for targets confirmed visible by the raycast solver.

When `TargetMemory.AddOrUpdateTarget()` is called, the unmanaged array automatically handles insertion, eviction of the lowest-threat targets if the buffer is full, and sorts the arrays descending by threat score.

The Brain's Consumption

Because `TargetMemory` is self-sorting and unmanaged, the "Brain" layer is completely shielded from perception complexity. A Behavior Tree node (like the Insurgent's `Condition_HasTarget`) doesn't need to loop through entities or calculate distances; it just does an O(1) check of `TargetMemory.Count > 0`. If true, it inherently knows that index `0` contains the highest-priority threat currently detected by the combined sensor suite.

what the DDS messages /descriptorsand the translators might look like for the dumb raycats queries and smart sensor parameters and results? Not that we still require engine agnosticism here and we should keep the network performance in mind (sending for example small-range coordinates as floats relative to some point like current entity position and not always absolute geo position in doubles etc - whatever makes sense)

As a senior software engineer, I love this constraint. When designing distributed systems that bridge heterogeneous engines (like FDP, Unreal, and Unity), bandwidth and coordinate-system agnosticism are usually the first things that break a clean architecture.

If we send 10,000 raycasts using absolute `double` WGS-84 coordinates (Latitude, Longitude, Altitude) for every Start and End point, we will saturate the network and incur massive floating-point serialization overhead. Instead, we must use a **Local Tangent Plane (ENU - East, North, Up)** pattern. We send one absolute WGS-84 anchor (using `double` precision), and all batch elements use lightweight 32-bit `float` offsets relative to that anchor.

Here is the clean, engine-agnostic DDS data model and the translator architecture for both pipelines.

1\. Dumb Raycasts (The Batch CQRS Pipeline)

For "dumb" queries, we rely on the CQRS pattern. The Brain node asks a remote Sim node (which holds the heavy NavMesh/Collision graph) to perform the math. We compress the batch by grouping requests around a single geographical origin.

**The DDS Descriptors (Engine Agnostic):**

```
namespace Hrot.NED.Descriptors
{
    // The lightweight ENU float vector (Engine Agnostic: East, North, Up)
    [DdsStruct]
    public partial struct RelativeVector3
    {
        public float East;
        public float North;
        public float Up;
    }

    [DdsStruct]
    public partial struct DdsRaycastRequest
    {
        public long RayId;             // Correlation ID
        public RelativeVector3 Start;  // Float offset from BatchOrigin
        public RelativeVector3 End;    // Float offset from BatchOrigin
        public int LayerMask;          // E.g., EntityCollisionLayer
        public long IgnoreEntityId;    // Don't hit the shooter
    }

    [DdsTopic("RaycastRequestBatch")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct RaycastRequestBatch
    {
        [DdsKey] public int SourceNodeId; 
        public uint BatchCorrelationId;
        
        // The single absolute double-precision anchor for the whole batch
        public GeoPoint BatchOrigin; 
        
        [DdsManaged]
        public List<DdsRaycastRequest> Requests;
    }

    [DdsStruct]
    public partial struct DdsRaycastHit
    {
        public long RayId;
        public bool HasHit;
        public long HitEntityId;
        public float HitT; // Normalized distance [1] along the ray
    }

    [DdsTopic("RaycastResponseBatch")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct RaycastResponseBatch
    {
        [DdsKey] public int TargetNodeId; // Routes back to the requesting Brain
        public uint BatchCorrelationId;
        
        [DdsManaged]
        public List<DdsRaycastHit> Hits;
    }
}
```

**The Egress Translator (Brain Node):** Instead of dumping the unmanaged `RaycastBatchData` straight to the network, the `RaycastBatchEgressTranslator` compresses it. It reads the local `SimTransform` of the querying entity to establish the `BatchOrigin`, then subtracts that origin from every ray's Cartesian `Start` and `End` to produce the `RelativeVector3` floats. This cuts the spatial payload size in half and removes all engine-specific coordinate axes (like Unity's Z-forward vs FDP's X-forward) from the wire.

2\. Smart Sensors (The Replicated State Pipeline)

Smart sensors run autonomously on the remote Physics/Vision node. The Brain node configures them by owning the `SensorConfig` descriptor, and the Physics node runs the `VisionBroadphaseSystem` and publishes back the `SensorTargets`.

**The DDS Descriptors:**

```
namespace Hrot.NED.Descriptors
{
    // Brain -> Sim (Configuration)
    [DdsTopic("SensorConfig")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal)]
    public partial struct SensorConfig
    {
        [DdsKey] public long EntityId;
        public float VisionRange;
        public float HearingRange;
        public float FovDegrees; 
    }

    [DdsStruct]
    public partial struct DdsTrackedTarget
    {
        public long TargetEntityId;
        public float ThreatScore;
        // Relative to the observing entity's position
        public float Distance;
        public float BearingDegrees; // 0 = North, 90 = East
    }

    // Sim -> Brain (Results)
    [DdsTopic("SensorTargets")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.Volatile)]
    public partial struct SensorTargets
    {
        [DdsKey] public long ObserverEntityId;
        public uint Tick; 
        
        [DdsManaged]
        public List<DdsTrackedTarget> Targets;
    }
}
```

**The Architecture Translation:**

-   **Config Ingress (Sim Node):** The Sim node receives `SensorConfig` and writes it to the unmanaged `PerceptionReceptor` component. Note that the translator eagerly converts `FovDegrees` into `FieldOfViewCos` during ingress. This is a classic DOD pattern—precompute math at the network boundary so the hot-path `VisionBroadphaseSystem` does a cheap dot-product instead of calling `MathF.Cos()` per entity.-   **Execution (Sim Node):** The Sim node maintains the `TargetMemory` component. The `ThreatEvaluationSystem` autonomously decays scores and boosts them when entities are visible.-   **Targets Egress (Sim Node):** A `SensorTargetsEgressTranslator` reads the `TargetMemory` fixed arrays (`EntityIds`, `ThreatScores`, `PositionsX`, `PositionsY`). It computes the relative `Distance` and `BearingDegrees` to the targets, packages them into the `SensorTargets` list, and fires it over DDS.-   **Targets Ingress (Brain Node):** The Brain node receives the list, decodes it back into its local unmanaged `TargetMemory` component, maintaining perfect cache locality for the Behavior Tree. The BTree's `Condition_HasTarget` node simply checks `TargetMemory.Count > 0` in O(1) time without doing any spatial math.

The Architectural Win

By establishing a strict **Domain Language** (WGS-84 absolute anchors + ENU relative floats) at the DDS boundary, your systems remain completely ignorant of other engines' architectures. Unreal can receive the Raycast batch, map the `BatchOrigin` to its local Z-up world origin, apply the relative float offsets, query its internal PhysX/Chaos NavMesh, and return the hit `T` value without ever knowing it was commanded by a C# FDP instance.

How about modularizing these perception mechanisms? What modules to maintain? Some might handle the network part (separately the brain part and the solver part as they differ), other the implementation part (if the implementation is FDP based)...

This is the perfect application of the **Single Responsibility Principle (SRP)** and **Command Query Responsibility Segregation (CQRS)** at the module level. Because the FDP engine strictly separates data (ECS components) from logic (Systems) and packages systems into composable `IModule` implementations, we can cleanly slice the perception pipeline into distinct, deployable boundaries.

Here is how you should architect the modularization of the perception mechanisms, separating the "Brain" (request/consumption) from the "Solver" (execution/physics) and decoupling the network I/O.

1\. The Implementation Modules (The "FDP Solvers")

When a node is acting as the "Sim" (or when running an All-In-One node), it must actually execute the spatial math. We separate this into two distinct modules based on their execution cadence and domain.

**A.** **AutonomousPerceptionModule** **(Smart Sensors)** This module is responsible for the continuous, autonomous scanning of the environment.

-   **Execution Policy:** `ExecutionPolicy.SlowBackground(10)`. Running at 10 Hz on a background thread via Snapshot-on-Demand (SoD) prevents sensor sweeps from stalling the main physics loop.-   **Registered Systems:** `LocalGridBuilderSystem` (builds the spatial index), `VisionBroadphaseSystem` (evaluates FOV and faction filters), and `ThreatEvaluationSystem` (decays old scores and boosts visible targets).-   **Data Contract:** Reads `PerceptionReceptor` and `SimTransform`, mutates `TargetMemory`.

**B.** **PhysicsQueryModule** **(Dumb Raycasts)** This module handles discrete, high-resolution geometry queries that require single-frame precision.

-   **Execution Policy:** `ExecutionPolicy.Synchronous` (Runs on the main thread at 60 Hz).-   **Registered Systems:** `RaycastSolverSystem` (processes the batch in parallel against the physics colliders) and `HitResolutionSystem` (dispatches the results).-   **Data Contract:** Consumes requests from the `RaycastBatchData` singleton and writes back to its `Hits` array.

2\. The Network Modules (The I/O Boundaries)

Because a node might be written in Unreal/Unity or simply distributed across a cluster, the network translators must not be hardcoded into the logic modules. We organize them into **Translator Packs** (as discussed previously), which are injected into the `CycloneNetworkModule` during node bootstrap.

**A.** **SimPerceptionTranslatorPack** **(The Solver's Network I/O)** Installed on the node running the physics/perception execution.

-   **Ingress:** `SensorConfigIngressTranslator` (writes to `PerceptionReceptor`), `RaycastBatchIngressTranslator` (writes to `RaycastBatchData.Requests`).-   **Egress:** `SensorTargetsEgressTranslator` (reads `TargetMemory` and publishes to DDS), `RaycastBatchEgressTranslator` (reads `RaycastBatchData.Hits` and publishes to DDS).

**B.** **BrainPerceptionTranslatorPack** **(The Brain's Network I/O)** Installed on the cognitive node.

-   **Egress:** Publishes the `SensorConfig` to tune the remote sensors, and publishes the `RaycastRequestBatch` whenever the BTree writes a dumb query.-   **Ingress:** Receives the `SensorTargets` (overwriting the local `TargetMemory` component) and receives the `RaycastResponseBatch` (writing back to the local `RaycastBatchData.Hits` native array).

3\. The Cognitive Layer (The Brain's Perspective)

What logic module does the Brain node need to install for perception? **Absolutely nothing.**

This is the beauty of Data-Oriented Design (DOD). Because the network translators decode the DDS payloads directly into the local unmanaged ECS components (`TargetMemory` and `RaycastBatchData`), the Brain node does not need any perception systems installed. Your `BTreeTickSystem` simply queries `TargetMemory.Count > 0` or looks up its `RayId` in the local batch. The Brain node remains perfectly lightweight, spending 100% of its CPU budget evaluating AI behaviors.

4\. Node Composition Example (The Bootstrapper)

By structuring the architecture this way, your host application can flexibly build any topology via composition:

```
public void Bootstrap(NodeRole role, ModuleHostKernel kernel, EntityRepository world, DdsParticipant dds)
{
    var translators = new List<IDescriptorTranslator>();

    // 1. The Brain Node
    if (role == NodeRole.Brain || role == NodeRole.AllInOne)
    {
        // Brain just needs the components registered, no logic modules for perception!
        world.RegisterComponent<TargetMemory>();
        world.RegisterSingleton<RaycastBatchData>(new RaycastBatchData(...));
        
        translators.AddRange(BrainPerceptionTranslatorPack.Create(dds));
    }

    // 2. The Sim / Execution Node
    if (role == NodeRole.Sim || role == NodeRole.AllInOne)
    {
        world.RegisterComponent<PerceptionReceptor>();
        
        // Install the heavy solvers
        kernel.RegisterModule(new AutonomousPerceptionModule());
        kernel.RegisterModule(new PhysicsQueryModule());
        
        translators.AddRange(SimPerceptionTranslatorPack.Create(dds));
    }

    // 3. Finalize Network
    kernel.RegisterModule(new CycloneNetworkModule(dds, ..., customTranslators: translators));
}
```

**Architectural Win:** If your Sim node is implemented in Unreal Engine instead of FDP, you still use the exact same `BrainPerceptionTranslatorPack` on your C# Brain node. The Brain node doesn't care _who_ is running the `PhysicsQueryModule` or _how_ it's implemented, as long as the DDS contracts (`RaycastRequestBatch` → `RaycastResponseBatch`) are fulfilled.

I will certainly need different type of smart sensors calculated by different solver nodes. Also the dumb queries might be very different (visual, radar, thermal audio, whetever...) Probably each type od of sensor and each type of dumb query will require their specific data structures describing the request/response/parameters. And corresponding stuff for network and solvers.

This is exactly the kind of architectural foresight I love to see. When scaling a distributed simulation to support N heterogeneous sensor modalities (Visual, Radar, Thermal, Acoustic) spread across M dedicated solver nodes, a naive architecture will quickly collapse under boilerplate, massive network payloads, and ECS component exhaustion.

To maintain a clean, Data-Oriented Design (DOD) that respects the engine's strict 256-component limit while achieving perfect modularity, we must leverage **Polymorphic Data Pipelines** and **Topic-Based Network Routing**.

Here is the blueprint for architecting this at scale.

1\. Smart Sensors: Decoupled Receptors, Unified Memory

For continuous, autonomous sensors, the "Brain" does not care _how_ the physics node calculated a radar cross-section or a thermal signature; it only cares about the fused tactical result (e.g., "There is a tank at these coordinates, and I know this via Radar and Visual").

**The ECS Components (Data Contract):** Instead of stuffing every possible parameter into a single god-component like `PerceptionReceptor`, we define strict, highly cohesive unmanaged structs for each modality:

```
[ComponentId(GlobalComponentIds.VisualReceptor)]
public struct VisualReceptor { public float VisionRange; public float FovCos; }

[ComponentId(GlobalComponentIds.RadarReceptor)]
public struct RadarReceptor { public float MaxRange; public float EmissionPower; public int TargetMask; }
```

**The Fused Result (****TargetMemory****):** All remote solver nodes must funnel their results back into the **same** generic `TargetMemory` component on the Brain node. To allow the AI Behavior Tree to differentiate the sensory source, we simply add a parallel `fixed byte` array to the unmanaged struct to act as a modality bitmask:

```
[Flags] public enum SensorModality : byte { Visual = 1, Radar = 2, Thermal = 4, Acoustic = 8 }

[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.TargetMemory)]
public unsafe struct TargetMemory
{
    public int Count;
    public fixed long EntityIds[PerceptionConstants.MaxTrackedTargets];
    public fixed float PositionsX[PerceptionConstants.MaxTrackedTargets];
    public fixed float PositionsY[PerceptionConstants.MaxTrackedTargets];
    public fixed float ThreatScores[PerceptionConstants.MaxTrackedTargets];
    
    // NEW: The AI can inspect this O(1) bitmask to know HOW it perceives the target
    public fixed byte Modalities[PerceptionConstants.MaxTrackedTargets]; 
}
```

_Architectural Win:_ The Behavior Tree logic remains universally decoupled. The `Condition_HasTarget` node evaluates `TargetMemory.Count > 0`, and can optionally check `(Modalities & SensorModality.Radar) != 0` without knowing anything about the underlying radar math.

2\. Dumb Queries: Generic Batch Singletons

For discrete, instantaneous queries (like a laser rangefinder or a directed radar ping), we cannot afford to create new ECS component types per query, as this fragments memory.

Instead, we use a generic batching pattern per modality. We define unmanaged singletons for each domain:

```
[ComponentId(GlobalComponentIds.RadarBatchData)]
public struct RadarBatchData
{
    public int Count;
    public NativeArray<RadarRequest> Requests;
    public NativeArray<RadarHit> Hits;
}
```

When the Brain's Behavior Tree explicitly requests a radar sweep, it writes directly into `RadarBatchData.Requests`. Because it is a contiguous `NativeArray`, the local egress translator can scoop it up with zero Garbage Collection (GC) overhead.

3\. The Network Boundary: Routing via Pub/Sub

The most elegant way to route different queries to different solver nodes is to completely avoid application-level routing logic and rely on the DDS middleware's native Publish/Subscribe architecture.

We define distinct DDS topics for each modality:

-   **Visual Pipeline:** `VisualSensorConfig`, `VisualTargets`, `LosRequestBatch`, `LosResponseBatch`.-   **Radar Pipeline:** `RadarSensorConfig`, `RadarTargets`, `RadarRequestBatch`, `RadarResponseBatch`.

This is standard CQRS (Command Query Responsibility Segregation).

4\. Node Composition (The Orchestrator)

Because we have segregated the data into distinct unmanaged components and DDS topics, assembling the nodes becomes incredibly clean. You build specific `IModule` implementations for each solver and package their I/O into Translator Packs.

**Node A: The Radar Solver** You launch a node with `--role radar_solver`. It registers the `RadarSolverModule`. It subscribes _only_ to `RadarSensorConfig` and `RadarRequestBatch`. It completely ignores Visual or Audio topics, saving massive amounts of network bandwidth and CPU cycles.

**Node B: The Visual Solver** You launch another node with `--role visual_solver` (perhaps backed by Unreal Engine for line-of-sight). It subscribes _only_ to `VisualSensorConfig` and `LosRequestBatch`.

**Node C: The Brain Node** The Brain node registers zero solver modules. It simply registers the `EntityMission` and the unified `TargetMemory` component. Its `SensorFusionIngressTranslator` listens to _both_ `RadarTargets` and `VisualTargets` DDS topics. As the results stream in asynchronously from Node A and Node B, the translator applies a bitwise `OR` to the `TargetMemory.Modalities` array, dynamically fusing the distributed sensor data directly into the entity's cognitive memory.

By adhering to these strict Data-Oriented Design boundaries, you achieve infinite horizontal scalability. You can add a Thermal Solver node tomorrow without changing a single line of code in the Brain's core AI loop or the existing Radar systems.

What about the path finding queries? The movement "musclele" migth use it internally when performing a MoveToLocation task but the brain login might also request an independent path query (parametrized) to check if there is path at all and optinally to use it as parameter for the upcoming movement task (like FollowNavigationPath or something). How to approach this? What messages and descriptors and translators and modules other elements?

This is a brilliant architectural challenge. You are touching on a classic problem in distributed AI: the **Pathfinding Bottleneck**.

If we naively send a pathfinding request over the network, and the "Muscle/Sim" node sends back an array of 5,000 waypoints to the "Brain", and the Brain then immediately sends those exact same 5,000 waypoints _back_ to the Sim node as a movement intent, we have utterly destroyed our network bandwidth and violated Data-Oriented Design (DOD) by moving massive amounts of geometric data into the cognitive layer.

To maintain our pristine, zero-allocation architecture, we must implement the **"Route Handle" Pattern** via asynchronous CQRS. The Brain asks for a path; the Sim calculates it, caches the heavy geometry in its local memory, and returns a lightweight "Handle" (an integer ID) along with tactical metadata (length, status) to the Brain. The Brain evaluates the metadata, and if it decides to proceed, it simply commands the Sim to execute that Handle.

Here is how we architect this cleanly, leveraging the engine's existing `TrajectoryPoolManager` and `FollowRouteParams`.

1\. The DDS Boundary (Engine-Agnostic CQRS)

We define a request/response batching protocol exactly like we did for raycasts, using the WGS-84 WGS-84 WGS-84 origin + ENU relative floats compression to save bandwidth.

```
namespace Hrot.NED.Descriptors
{
    [DdsStruct]
    public partial struct DdsPathRequest
    {
        public long RequestId;             // Correlation ID
        public RelativeVector3 Start;      // Float offset from BatchOrigin
        public RelativeVector3 End;        // Float offset from BatchOrigin
        public byte MobilityProfile;       // e.g., 0=Wheeled, 1=Tracked, 2=Infantry
    }

    [DdsTopic("PathRequestBatch")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct PathRequestBatch
    {
        [DdsKey] public int SourceNodeId; 
        public GeoPoint BatchOrigin; 
        
        [DdsManaged] public List<DdsPathRequest> Requests;
    }

    [DdsStruct]
    public partial struct DdsPathResult
    {
        public long RequestId;
        public bool IsReachable;
        public float TotalDistanceMeters;
        
        // THE ARCHITECTURAL WIN: 
        // We do not send 5,000 waypoints back to the Brain. 
        // We send the ID of the path cached on the Sim node.
        public int RouteHandle; 
        
        // Optional: A highly decimated/coarse list of waypoints (e.g., just major intersections)
        // if the Brain's Behavior Tree actually needs to analyze the route's geometry for danger zones.
        [DdsManaged] public List<RelativeVector3> CoarseWaypoints;
    }

    [DdsTopic("PathResponseBatch")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct PathResponseBatch
    {
        [DdsKey] public int TargetNodeId; 
        [DdsManaged] public List<DdsPathResult> Results;
    }
}
```

2\. The Local ECS Singletons (Zero-Allocation Hot Path)

On the Brain node, we implement the interface methods that are currently stubbed out in `BTreeContext.cs` (line 1201): `RequestPath` and `GetPathResult`.

We define an unmanaged singleton `PathfindingBatchData` that mirrors the raycast pattern:

```
[ComponentId(GlobalComponentIds.PathfindingBatchData)]
public struct PathfindingBatchData
{
    public int Count;
    public NativeArray<PathRequest> Requests;
    public NativeArray<PathResult> Results;
}
```

When a BTree action node (e.g., `Action_PlanFlankRoute`) calls `ctx.RequestPath(from, to)`, it writes directly into this `NativeArray`. The BTree node saves the `RequestId` to the `BrainBlackboard` and returns `NodeStatus.Running` to suspend execution while waiting for the network.

3\. The Translators and Execution Modules

We cleanly slice this across our deployment topology:

**A. On the Brain Node:**

-   **Module:** None required. The Brain only reads/writes the `PathfindingBatchData` singleton.-   **Translators:**
    -   `PathRequestEgressTranslator`: Reads the unmanaged requests, applies the `BatchOrigin` compression, and publishes `PathRequestBatch` to DDS.-   `PathResponseIngressTranslator`: Receives `PathResponseBatch` and writes the results back into the unmanaged `PathfindingBatchData.Results` array.

**B. On the Sim Node (The Pathfinding Solver):**

-   **Module:** `NavigationSolverModule` (Runs synchronously or in a slow-background thread depending on NavMesh density).-   **Translators:**
    -   `PathRequestIngressTranslator`: Decodes the DDS batch into a local work queue.-   `PathResponseEgressTranslator`: Publishes the results.-   **Execution System:** `PathfindingSolverSystem`.
    -   It reads the requests and queries the `RoadNetworkBlob` or Unreal NavMesh.-   If a path is found, it calls `_trajectoryPool.RegisterTrajectory(waypoints)` to cache the full, high-resolution curve in memory.-   It packages the resulting `TrajectoryId` into the `RouteHandle` field of the `DdsPathResult`.

4\. Closing the Loop: Executing the Intent

Once the `PathResponseBatch` arrives back at the Brain node, the suspended BTree node (`Action_PlanFlankRoute`) wakes up. It reads the `PathResult`.

If `IsReachable` is true, the Brain decides to execute the movement. Because we already have a strictly separated intent pipeline, the Brain **does not** use `ActionIdMoveTo`. Instead, it uses the existing `NavigationConstants.ActionIdFollowRoute`.

The BTree node writes the intent:

```
var routeParams = new FollowRouteParams
{
    TrajectoryId = result.RouteHandle, // Pass the token back to the Sim!
    IsLooped = 0,
    Speed = tacticalSpeed
};
// Write routeParams into LocomotionChannel.Params and set ActiveAction = ActionIdFollowRoute
```

The Brain's `NavigationIntentEgressTranslator` publishes this intent over DDS. The Sim node receives it, and its local `FollowRouteExecutor` perfectly maps the `TrajectoryId` back to the high-resolution spline stored in its `TrajectoryPoolManager`, initiating pure-pursuit steering along the path.

Architectural Summary

By adopting this **Token-Based Pathfinding (Route Handle)** approach:

-   **Network traffic is negligible:** We send 2 floats for the request and 1 integer (the Handle) for the response, rather than serializing megabytes of path vertices.-   **CPU Cache is preserved:** The Brain node's Behavior Tree continues to operate entirely within unmanaged 128-byte blackboards.-   **Engine Agnosticism is absolute:** An FDP Brain can ask an Unreal Engine Sim node for a path. Unreal does the heavy A\* math against its proprietary geometry, stores the path in its own proprietary spline system, and hands the FDP Brain a generic integer ID. When the Brain says "Follow ID 42", Unreal knows exactly what to do.

-------------



There is one more feature i want to address. IG node often shows the world/terrain using different level of details (LOD) based on distance from camera, requirements for high fidelity etc. Different LOD often results in slightly different height of the terrain at the same geo coordinates. When Sim calculates the ground vehicle altitude, it uses its own high fidelity perception provider, which produces different altitude than where the altitude of the terrain surface is displayed by the IG. Without correction this would result in cars displayed by the IG a burried under ground or flying in the air. IG therefore needs to apply so called ground clamping for all incoming ghost entities. Meaning that for each entity which requires ground clamping (the default can be usually forced on/off in some entity descriptor) the IG queries its internal terrain representation for terrain surface altitude - multiple queries per vehicle (one for vehicle contact point - wheels or tracks etc.) This forms a big batch which needs to be processed on the background and be available the next frame so the IG can render the vehicles at proper altitude on the terrain. The queries contain the original requested altitude from the inbound GeoPoint of the entity. And usually looks for terrain surface just in close vicinity of the original altitude. This allows for placing the entity to correct floor in a multi-store building or in bridge/tunnel situations. Sometimes this also includes some heuristics utilizing the previously calculated ground-clamped IG altitude to prevent suddens altitude jumps when vehicle just gets under the bridge. The whole ground clamping also means the incoming entity position is not applied immediately, but needs to wait for ground clamping correction, basically either introducing a one frame delay (if we need a pixel perfect results) or that it is aplied the next frame when the entity position of a moving vehicle is already elsewhere. PLs suggest how to approach all this.

This is the classic "Heterogeneous Terrain Correlation" problem in distributed simulations, and as an architect, I love this challenge. If handled poorly, you end up with stuttering vehicles, decoupled shadows, or blocking network pipelines.

To solve this cleanly, we must strictly decouple the **Kinematic Truth** (the SimHost's authoritative X/Y/Z) from the **Visual Presentation** (the IG's LOD-dependent Z). The IG should _never_ delay or block the application of the incoming horizontal movement while waiting for a terrain query.

Instead, we will build an asynchronous, zero-allocation **Z-Offset Pipeline** using the same Data-Oriented Design (DOD) batching pattern we use for physics raycasts. We will calculate an altitude offset and inject it smoothly during the interpolation phase.

Here is the clean architecture blueprint for IG Ground Clamping:

1\. The Data Model (Zero-Allocation ECS)

We need a discrete component to store the clamping state, and a pre-allocated unmanaged singleton for the batched terrain queries.

**The Entity Component:** This stores the heuristic history and the calculated visual offset.

```
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.GroundClampingState)]
public struct GroundClampingState
{
    public float TargetZOffset; // The desired difference between Sim Z and IG Z
    public float CurrentZOffset; // The smoothed, currently applied offset
    public float LastValidIgAltitude;
    public byte  RequiresClamping; // Driven by network descriptor
}
```

**The Batch Singleton:** Pre-allocated natively, preventing Garbage Collection (GC) spikes when querying thousands of wheels/tracks per frame.

```
public struct TerrainQueryRequest
{
    public Entity Entity;
    public float QueryX;
    public float QueryY;
    public float ReferenceSimZ; // The SimHost's altitude
}

public struct TerrainQueryResult
{
    public float HitZ;
    public bool  HasHit;
}

[ComponentId(GlobalComponentIds.TerrainQueryBatchData)]
public struct TerrainQueryBatchData
{
    public int Count;
    public NativeArray<TerrainQueryRequest> Requests;
    public NativeArray<TerrainQueryResult> Results;
}
```

2\. The Execution Pipeline (3-Phase Asynchronous)

We split the logic into three distinct systems to handle the one-frame delay elegantly and safely extrapolate for moving vehicles.

A. The Submission System (`InputSystemGroup`)

We do not query the terrain at the vehicle's _current_ position. Because the result arrives next frame, we query the terrain where the vehicle _will be_ next frame.

```
[UpdateInGroup(typeof(InputSystemGroup))]
public class TerrainQuerySubmitSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        ref var batch = ref World.GetSingleton<RaycastBatchData>();
        
        var query = World.Query()
            .With<SimTransform>()
            .With<SimVelocity>()
            .With<GroundClampingState>()
            .Build();

        foreach (var entity in query)
        {
            var state = World.GetComponent<GroundClampingState>(entity);
            if (state.RequiresClamping == 0) continue;

            var tf  = World.GetComponent<SimTransform>(entity);
            var vel = World.GetComponent<SimVelocity>(entity);

            // Forward-predict the position by 1 frame (e.g., 16.6ms)
            var predictedPos = tf.Position + (vel.Linear * DeltaTime);

            batch.Requests[batch.Count++] = new TerrainQueryRequest
            {
                Entity        = entity,
                QueryX        = predictedPos.X,
                QueryY        = predictedPos.Y,
                ReferenceSimZ = predictedPos.Z
            };
        }
    }
}
```

B. The Solver (The IG Terrain Engine)

Between the Input and PostSimulation phases (or fully async on a background thread), the IG executes the queries against its LOD-based geometry.

Crucially, the query ray does not cast from the sky down to `-Infinity`. It casts from `ReferenceSimZ + 3.0m` down to `ReferenceSimZ - 3.0m`. By strictly bounding the search vicinity around the SimHost's authoritative altitude, you perfectly solve the bridge/tunnel problem—the IG will find the bridge deck if the Sim is on the bridge, or the road below if the Sim is in the tunnel, ignoring the multi-story meshes above or below the target bracket.

C. The Resolution & Heuristics System (`PostSimulationSystemGroup`)

Once the batch is solved, we apply heuristics to prevent jarring visual snaps.

```
[UpdateInGroup(typeof(PostSimulationSystemGroup))]
[UpdateBefore(typeof(TransformSyncSystem))]
public class TerrainQueryResolutionSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        ref var batch = ref World.GetSingleton<TerrainQueryBatchData>();
        var cmd = World.GetCommandBuffer();

        for (int i = 0; i < batch.Count; i++)
        {
            var req = batch.Requests[i];
            var res = batch.Results[i];
            if (!res.HasHit || !World.IsAlive(req.Entity)) continue;

            var state = World.GetComponent<GroundClampingState>(req.Entity);

            // Heuristic: If the terrain suddenly jumped by 5 meters (e.g., driving 
            // under a bridge and the IG's low-LOD mesh hasn't opened the tunnel hole yet),
            // we reject the clamp to prevent the tank from snapping to the roof.
            if (MathF.Abs(res.HitZ - state.LastValidIgAltitude) > 5.0f)
            {
                // Reject drastic jumps; rely on previous offset
                continue; 
            }

            state.LastValidIgAltitude = res.HitZ;
            state.TargetZOffset       = res.HitZ - req.ReferenceSimZ; 
            
            cmd.SetComponent(req.Entity, state);
        }
        batch.Count = 0;
    }
}
```

3\. Applying the Visual Offset

We do not overwrite the raw `SimTransform.Position.Z` with the IG's altitude, because `SimTransform` is our shadow of the authoritative network state. If we overwrite it, we corrupt our dead-reckoning baseline.

Instead, we modify the existing `TransformSyncSystem.SyncRemoteEntities`, which is already responsible for smoothing the visual positions of remote ghosts.

```
// Inside TransformSyncSystem.cs
if (isRemote)
{
    var netTf = view.GetComponentRO<NetworkTransform>(entity);
    var currentTf = view.GetComponentRO<SimTransform>(entity);

    // 1. Standard horizontal dead-reckoning
    var smoothed = Vector3.Lerp(currentTf.Position, netTf.LastPosition, deltaTime * SMOOTHING_RATE);

    // 2. Apply ground clamping visually
    if (view.HasComponent<GroundClampingState>(entity))
    {
        ref var clamp = ref view.GetComponentRW<GroundClampingState>(entity);
        
        // Smoothly lerp the offset itself to prevent pop-in when LODs swap
        clamp.CurrentZOffset = MathF.Lerp(clamp.CurrentZOffset, clamp.TargetZOffset, deltaTime * 5f);
        
        // Apply the offset to the final visual Z
        smoothed.Z = netTf.LastPosition.Z + clamp.CurrentZOffset;
    }

    cmd.SetComponent(entity, new SimTransform {
        Position = smoothed,
        Rotation = currentTf.Rotation
    });
}
```

Architectural Wins

By adopting this approach:

-   **Zero Latency:** The vehicle's X/Y movement never waits for a terrain raycast. It remains fluid and strictly tied to the network baseline.-   **No Stuttering:** By forward-predicting the raycast position via `SimVelocity` and lerping the `ZOffset`, moving vehicles perfectly hug the undulating terrain without micro-stutters caused by 1-frame delays.-   **Bridge/Tunnel Safe:** Relying on `ReferenceSimZ` guarantees the IG picks the correct topological layer (overpass vs underpass) matching the SimHost's intent.

how to modularize this or make it configurable? Not every IG needs it - some IG is using just plain 2d map where 3d altitude correction is not necessary. Some IG renders in 3d where the ground clamping correction is paramount. Also this needs to be controlled per entity and per user request - when placing entities visually in pause editing mode, we might want the cars to be temporarily 'flying' (ground camping forced off via a field in some ground clamping setting descriptor) until we drop them to their final location (ground clamping not forces on/off, left for default). Some entities like aricraft also need dynamic switching mid-simulation (landing/take-off). Pls suggest the descriptors as well as the ECS implementation

To achieve perfect modularity where purely 2D Image Generators (IGs) carry zero overhead while 3D IGs perform high-fidelity terrain clamping, we must extract the clamping logic into an independent `IModule` and define a strict network contract via CycloneDDS descriptors.

By making the clamping behaviour data-driven, both the IOS (during editing) and the SimHost (for aircraft landing) can dynamically manipulate the entity's visual altitude without coupling to the IG's internal rendering engine.

Here is the clean, Data-Oriented Design (DOD) approach to building this modular pipeline.

1\. The Network Contract (DDS Descriptors)

We introduce a new transient-local descriptor to explicitly control clamping state over the network. By using a ternary mode, we allow entities to rely on their default engine heuristics (like aircraft automatically clamping when gear is down) while giving operators the power to force the state.

```
namespace Hrot.NED.Descriptors
{
    public enum EClampingMode : byte
    {
        CLAMP_DEFAULT = 0,   // Engine decides (e.g. grounded vehicles ON, flying aircraft OFF)
        CLAMP_FORCE_ON = 1,  // Snapped to terrain (e.g. forced landing/taxiing)
        CLAMP_FORCE_OFF = 2  // Ignored by terrain solver (e.g. editor dragging, mid-air)
    }

    [DdsTopic("GroundClampingOverride")]
    [DdsIdlFile("bdc-sst-map-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct GroundClampingOverride
    {
        [DdsKey]
        public int EntityId;
        public EClampingMode Mode;
    }
}
```

2\. The Data Layer (ECS Components)

On the IG side, we translate the incoming DDS descriptor into an unmanaged ECS component. We also maintain the internal state component for interpolation heuristics (to prevent snapping under bridges).

```
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.GroundClampingConfig)]
public struct GroundClampingConfig
{
    public EClampingMode Mode;
    public byte BaseRequiresClamping; // Seeded from TKB (e.g., 1 for tanks, 0 for jets)
    
    // Fast O(1) evaluation property
    public bool IsClampingActive => Mode == EClampingMode.CLAMP_FORCE_ON || 
                                   (Mode == EClampingMode.CLAMP_DEFAULT && BaseRequiresClamping == 1);
}

[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.GroundClampingState)]
public struct GroundClampingState
{
    public float TargetZOffset;
    public float CurrentZOffset;
    public float LastValidIgAltitude;
}
```

An ingress translator (`GroundClampingOverrideTranslator`) will inherit from `AutoCycloneTranslator<GroundClampingOverride>` to automatically deserialize network updates into the `GroundClampingConfig` component.

3\. The Logic Layer (Module Encapsulation)

We package the terrain query logic into a dedicated `IgGroundClampingModule`. This satisfies your requirement for node-level configuration.

```
public class IgGroundClampingModule : IModule
{
    public string Name => "GroundClamping";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly ITerrainProvider _terrainProvider;

    public IgGroundClampingModule(ITerrainProvider terrainProvider)
    {
        _terrainProvider = terrainProvider;
    }

    public void RegisterSystems(ISystemRegistry registry)
    {
        // Allocates the unmanaged batch singleton
        registry.RegisterSystem(new TerrainQueryInitializationSystem()); 
        
        // Input Phase: Queries where the entity WILL be [6]
        registry.RegisterSystem(new TerrainQuerySubmitSystem());
        
        // Background thread: Solves the queries against the 3D mesh
        registry.RegisterSystem(new TerrainQuerySolverSystem(_terrainProvider));
        
        // PostSimulation Phase: Applies heuristic Z-offsets to the visual transform [7]
        registry.RegisterSystem(new TerrainQueryResolutionSystem());
    }

    public void Tick(ISimulationView view, float dt) { }
}
```

4\. Node Configuration (The Bootstrapper)

Because the logic is bounded within `IgGroundClampingModule`, configuring your nodes becomes trivial. If an IG is purely 2D (like a tactical map), it simply does not register the module.

```
// Inside your IG Bootstrapper
if (igConfig.Requires3DClamping)
{
    // The 3D IG registers the module and allocates the heavy terrain mesh provider.
    _kernel.RegisterModule(new IgGroundClampingModule(new UnrealTerrainProvider()));
}
// Pure 2D IGs skip this entirely. They receive the exact same ECS and DDS network 
// traffic, but without the module installed, the entities render at the raw 
// SimTransform.Position.Z altitude.
```

5\. Dynamic Switching & Editor Mode

By leveraging the network descriptor, the system organically supports both manual editing and autonomous simulation changes without tightly coupling the components.

**A. Editor Drag & Drop (IOS / IG)** When the operator grabs an entity using the `EntityDragTool`, the UI sends an `UpdateEntityDescriptorRequest` to the SimHost:

-   **Drag Start:** Publish `GroundClampingOverride { Mode = CLAMP_FORCE_OFF }`.-   **During Drag:** The entity is ignored by `TerrainQuerySubmitSystem`. It floats smoothly at the operator's cursor altitude without jittering violently over terrain features.-   **Drag End / Drop:** Publish `GroundClampingOverride { Mode = CLAMP_DEFAULT }`. The clamping system resumes and snaps the entity to the correct multi-story floor or terrain surface.

**B. Dynamic Aircraft Behaviour (SimHost)** The flight dynamics module (running on a dedicated muscle node) natively controls its own clamping state based on physics:

-   **Take-off:** As `SimVelocity.Z` turns positive and the landing gear retracts, the `FlightDynamicsSystem` calls `SmartEgressUtil.MarkDirty()` and publishes `GroundClampingOverride { Mode = CLAMP_FORCE_OFF }`. All 3D IGs instantly stop clamping the aircraft, allowing it to fly smoothly.-   **Landing:** When the aircraft's radar altimeter detects touchdown, the flight system publishes `GroundClampingOverride { Mode = CLAMP_DEFAULT }` (or `CLAMP_FORCE_ON`). The 3D IGs immediately resume querying the runway terrain mesh, ensuring the aircraft's wheels perfectly hug the tarmac regardless of LOD-induced height variations.

if ground clamping is in a separate module, how do we achieve the synergy with the extrapolation/smoothing? Is the smooting a different module?

In a clean Data-Oriented Design (DOD), smoothing and ground clamping absolutely belong in completely separate modules. They achieve perfect synergy not by calling each other's methods, but by acting as sequential filters in the ECS data pipeline, orchestrated by the kernel's dependency graph.

Here is how the architecture naturally decouples them while maintaining flawless visual results.

1\. The Smoothing Logic is a Separate System

In the current architecture, smoothing is already decoupled into its own dedicated logic via the `DeadReckoningSyncSystem`. This system is registered independently into the kernel during the IG's initialization.

Its sole responsibility is to read the raw `NetworkTransform` and `NetworkVelocity`, project the target position forward in time, and apply a smooth `Vector3.Lerp` to the entity's `SimTransform`. It knows nothing about terrain, LODs, or visual clamping; it is purely a mathematical interpolator.

2\. Achieving Synergy via Execution Ordering

To make the separate `IgGroundClampingModule` work in synergy with the smoothing system, we rely entirely on the engine's declarative scheduling attributes.

Both systems will run in the `PostSimulation` phase, but the clamping resolution system will explicitly declare a dependency on the smoothing system using the kernel's native sorting attributes:

```
[UpdateInPhase(SystemPhase.PostSimulation)]
[UpdateAfter(typeof(DeadReckoningSyncSystem))] // Guarantees execution order
public class TerrainQueryResolutionSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        // ... applies Z-offset ...
    }
}
```

3\. The Data Pipeline (Step-by-Step)

Because the `SystemScheduler` resolves the `[UpdateAfter]` attribute to build a deterministic execution graph, the synergy happens automatically through the `SimTransform` component:

-   **Step 1 (The Smoothing):** `DeadReckoningSyncSystem` runs first. It calculates the perfectly smooth `X` and `Y` interpolation for the frame and writes the complete `Vector3` directly into `SimTransform.Position`.-   **Step 2 (The Clamping):** `TerrainQueryResolutionSystem` runs immediately after. It reads the `SimTransform` that was just smoothed by the previous system. Because the `X` and `Y` are already perfectly interpolated, the clamping system's terrain lookup perfectly matches the sub-frame position of the vehicle.-   **Step 3 (The Write):** The clamping system calculates the final visual altitude and overwrites _only_ the `Z` axis of the `SimTransform.Position` in the command buffer, leaving the smoothed `X` and `Y` completely intact.

The Architectural Win

By separating them, you strictly adhere to the Open/Closed Principle.

If you deploy a lightweight 2D tactical map node, you install the smoothing module but omit the `IgGroundClampingModule`. The `DeadReckoningSyncSystem` runs, updates the `SimTransform`, and the 2D UI renders the smooth movement perfectly.

If you deploy a high-fidelity 3D Image Generator, you register both modules. The kernel's topological sorter detects the `[UpdateAfter]` attribute and chains them together automatically. You achieve complex, terrain-aware interpolated movement without adding a single `if (is3D)` statement into your core network smoothing logic.

