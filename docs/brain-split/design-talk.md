How does the mission mechanism work? from dds network down to doctrine assignment and cancelling? what is the role of MissionAdapterSystem?

The mission mechanism is designed around a clean separation between network ingress, unmanaged execution, and cognitive state assignment, ensuring high-frequency ticks remain allocation-free while supporting complex JSON-parameterised behaviours.

Here is the exact architectural flow from the network layer down to execution and cancellation:

1\. Network Ingress and Locking

The pipeline begins when a `MissionControlRequest` arrives over DDS. The `MissionControlRequestSystem` intercepts it and validates the `BaseVersion` to enforce optimistic concurrency control, rejecting stale requests.

If the command is `CMD_REPLACE_MISSION`, the system unpacks the `MissionPlan` into two distinct ECS components:

-   **MissionPlanQueue**: A high-performance, unmanaged component that stores up to 8 `MissionPhase` structs inline. It contains only the numeric doctrine ID and trigger thresholds.-   **EntityMissionHolder**: A managed component that retains the full original payload, including the heap-allocated JSON parameter strings for each task. The system then returns a `MissionControlAck` over DDS to close the transaction.

2\. Phase Progression

The `MissionDirectorSystem` runs in the simulation phase and continuously evaluates the active phase's trigger condition for every entity with a `MissionPlanQueue`. Triggers can be temporal (`TimerElapsed`), spatial (`ReachedDestination`), health-based (`HealthCritical`), or threat-based (`UnderAttack`).

When a trigger condition is met, the system increments `CurrentPhase` and resets the elapsed timers.

3\. The Role of MissionAdapterSystem

Because `MissionPlanQueue` is strictly unmanaged for CPU cache efficiency, it cannot store the JSON parameters needed to configure the AI behaviours.

The `MissionAdapterSystem` acts as the critical bridge between the managed mission data and the unmanaged cognitive tier. When it detects that the `CurrentPhase` has advanced, it does two things:

-   **Atomic Blackboard Injection:** It pulls the JSON `BehaviorParams` from the managed `EntityMissionHolder` and calls `ParseParams` to write the data directly into the unmanaged `BrainBlackboard`. This happens on the exact same frame, completely bypassing event-bus lag.-   **Event Publication:** It publishes an `AssignDoctrineEvent` to signal the rest of the cognitive tier that a new behaviour needs to be spun up.

4\. Doctrine Assignment

The `DoctrineIngressSystem` consumes the `AssignDoctrineEvent`. It acts as the sole authoritative mutator for cognitive state. When applying the new doctrine, it:

-   Updates the `ActiveDoctrineHash` and sets the appropriate `BrainTier` (BTree or HSM).-   Resets the execution pointer (`BrainBTreeState.State = default`) so the new behaviour starts cleanly from the root.-   Increments the `InstanceId` (using an `unchecked` wrap). This acts as a monotonic preemption token.

5\. Cancelling and Teardown

If the operator issues a `CMD_ABORT_ALL` command, the `MissionControlRequestSystem` empties the `MissionPlanQueue` (`PhaseCount = 0`), removes the `EntityMissionHolder`, and publishes a `ClearDoctrineEvent`.

The `ClearDoctrineEvent` forces a top-down abort:

-   `DoctrineIngressSystem` catches the event and forces the entity into a "brain-dead" state, setting the doctrine to `DoctrineIds.None`, clearing the brain tier, and bumping the `InstanceId` preemption token.-   On the next tick, the `ChannelArbitrationSystem` evaluates the locomotion, weapon, and interaction channels. It detects that the channels' `DoctrineInstanceId` no longer matches the entity's `DoctrineState.InstanceId`.-   The arbitration system immediately zeros out the channels' active actions. This cleanly halts all physical movement and firing at the muscle layer without the executors needing to handle the abort logic themselves.

MissionTrigger.ReachedDestination trigger checks NavState. Who updates the NavState? What if the navigation runs on different node than the brain?

In the current architecture, `MissionTrigger.ReachedDestination` directly polls the `NavState.HasArrived` field in the ECS.

If your navigation (Muscle tier) and brain (Cognitive tier) run on the same node (e.g., `NodeRole.AllInOne`), `NavState` is updated natively by the `CarKinematicsSystem` during the physics update.

However, if the navigation runs on a different node, **this trigger will fail and hang the mission indefinitely.**

Here is why this happens, and why it is an architectural flaw:

-   **Lack of Replication:** `NavState` is an internal kinematic component. It is never replicated over the DDS network.-   **CQRS Violation:** The engine uses a strict CQRS contract for distributed movement. The Brain node publishes a `NavigationIntent` command, and the Muscle node (running `NavigationExecutionSystem`) executes the physics and replies with a `NavigationStatus`.-   Because `NavState` is never updated on the Brain node by the network ingress layer, `MissionDirectorSystem` will never see `HasArrived == 1`. It is a leaky abstraction that couples the cognitive tier directly to a local kinematic component.

**The Correct Architectural Approach** To fix this in a distributed topology, you must stop using `MissionTrigger.ReachedDestination` and instead use **MissionTrigger.DoctrineFinished**.

This fully respects the network boundary and leverages the CQRS pipeline correctly:

-   The remote Muscle node determines that the vehicle is within the arrival radius and broadcasts `NavigationStatus.Result = Arrived` over DDS.-   The Brain node receives this via the `NavigationStatusIngressTranslator`.-   The `MoveToExecutor` (running on the Brain) reads the replicated `NavigationStatus` and returns `NodeStatus.Success`.-   The `BTreeTickSystem` observes that the doctrine's root node has reached a terminal state and publishes a `DoctrineFinishedEvent`.-   `MissionDirectorSystem` consumes the event and cleanly advances the mission phase without ever needing to touch `NavState`.

MissionTrigger.HealthCritical checks the HealthData ECS component. Is the HealthData calculated on the brain node? If on muscle node, is it updated over network?

Unlike the isolated `NavState`, `HealthData` does not suffer from distributed execution flaws because it is calculated locally directly on the Brain node.

Architecturally, the engine's `NodeBootstrapper` registers the `CombatModule` on all active simulation roles, meaning both the Brain and Muscle nodes independently instantiate and run the combat resolution pipelines.

During the simulation phase on the Brain node, the local `DamageSystem` consumes `HitEvent`s and deducts the appropriate damage from the entity's `Health.Current` component. In that exact same tick, the system immediately updates the `HealthData` mirror component.

Consequently, the `MissionDirectorSystem` evaluates the `HealthCritical` trigger using this locally computed data. It does not rely on the DDS network to replicate the health state from the Muscle node, keeping the cognitive evaluation strictly synchronous and entirely immune to network replication latency.

what is the role of the CombatModule? how does it interact with perception, physics, brain, muscles?

The `CombatModule` serves as the central orchestrator for combat resolution, ballistic physics, and perception processing across all simulation node roles. Architecturally, it is a cohesive grouping of systems scheduled across the `Input`, `Simulation`, and `PostSimulation` phases, designed to cleanly decouple the mechanics of shooting, damage application, and sensing from the cognitive and kinematic tiers.

Here is how it interacts across the engine's boundaries:

**1\. Interaction with Physics (The CCD Pipeline)** The module relies heavily on the `PhysicsToolkitModule`'s raycast batching to perform Continuous Collision Detection (CCD) and prevent high-velocity projectiles from tunneling through targets. The `BallisticsSystem` runs in the `PostSimulation` phase _before_ the `LinearKinematicsSystem` actually advances the bullet's position. This ensures Combat can record the bullet's `PreviousPosition` and submit a swept-segment raycast that perfectly covers the distance travelled in that tick. In the following `Input` phase, the `RaycastSolverSystem` processes the batch, and the `HitResolutionSystem` filters out the bullet rays to emit `HitEvent`s.

**2\. Interaction with Perception** The module encapsulates perception routines, including the `PerceptionBroadphaseSystem` and `ThreatEvaluationAdapterSystem`. Because the physics raycast pipeline handles both bullets and Line-of-Sight (LOS) checks, the `HitResolutionSystem` acts as a multiplexer: if a resolved ray is flagged as an LOS check, it emits a `TargetVisibleEvent` instead of a `HitEvent`. The `ThreatEvaluationAdapterSystem` then consumes these visibility events to boost threat scores inside the entity's `TargetMemory`.

**3\. Interaction with the Brain (Cognitive Tier)** The interaction follows a strict CQRS and event-driven contract to maintain boundary purity:

-   **Top-Down (Command):** The Brain evaluates BTree or HSM doctrines and writes intent (like `ActionIdAimAndFire`) into the `WeaponChannel`. The associated `AimAndFireExecutor` evaluates cooldowns and ammo, then publishes a `FireRequestEvent`. The `FireProcessingSystem` within the `CombatModule` consumes this event to spawn the bullet entity and configure its ballistics.-   **Bottom-Up (Feedback):** When the `DamageSystem` applies hits, it updates a `HealthData` mirror component. This allows cognitive systems (like the `MissionDirectorSystem` evaluating `HealthCritical` triggers) to react to damage states without creating a circular assembly dependency. Furthermore, if damage causes a mobility kill, the `HsmDamageBridgeSystem` injects a hardware-level `MobilityLost` event directly into the Brain's HSM queue.

**4\. Interaction with Muscles (Kinematics Tier)** The Combat module enforces physical consequences on the kinematic tier through capability bitmasks. When an entity takes damage, systems like `ApcMobilitySystem` or `MobilityKillSystem` evaluate the health drop and strip the `ActorCapabilities.CanMove` flag. By clearing this capability flag, the combat pipeline immediately forces the locomotion channels to fail and halts physical movement at the kinematic layer, effectively simulating a mobility kill while leaving the weapon channels active.

bullet simulation is a 'muscle' as well. all 'muscles' live on a node where physics is instantiated - a single central node because physics is not distributed. Brain can run on different node, not having direct access to muscles (all needs to go via intents). Also the health calculation can be made on different nodes based on entity types, it should not run everywhere, the healthData should be updated over network to reach the brain. I need th brain to be separated, doing just high level logic like sensor data evaluation and behavior control.

This is exactly the right architectural direction. It completes the strict CQRS (Command Query Responsibility Segregation) pattern we already established for locomotion (`NavigationIntent` and `NavigationStatus`).

Right now, the codebase violates this separation for combat. The `CombatModule` (which includes ballistics, hit resolution, and damage application) is registered indiscriminately on all simulation roles in `NodeBootstrapper.cs`. This forces the Brain to compute physics locally.

To physically separate the Brain into a pure cognitive tier and isolate bullets, ballistics, and health to a central Physics/Muscle node, we need to implement the following architectural refactoring:

1\. Reconfigure the Node Bootstrapper

We must strip physical and combat execution out of the `Brain` role so it only handles cognition, while ensuring the Physics node (Muscle) handles all instantiation.

In `NodeBootstrapper.cs`, update the role assignments:

-   **Brain Node:** Only registers `MissionControlModule`, `CognitiveRuntimeModule`, and `ActionDispatchModule`. It evaluates sensors and writes intents.-   **Muscle Node (Central Physics):** Registers `GroundKinematicsModule` and `CombatModule`. This node becomes the sole authority for physics colliders, ballistics, and damage resolution.

2\. Network Replication of HealthData (Muscle → Brain)

Since the Brain node will no longer run the `DamageSystem`, its local `HealthData` component will never change unless the Muscle node tells it to.

Currently, the engine has an `EntityDamage` DDS topic, but its ingress translator (`EntityDamageIngressTranslator`) is hardcoded to write `IgHealthState` for the Image Generator.

**The Fix:**

-   **Egress:** Create an `EntityDamageEgressTranslator` on the Muscle node. It will query entities with `Health` components and publish the `EntityDamage` topic whenever the value changes.-   **Ingress:** Create a `SimHealthIngressTranslator` for the Brain node. When it receives the `EntityDamage` packet, it writes directly to the `HealthData` and `Health` ECS components on the Brain's ghost entities.-   **Result:** The `MissionDirectorSystem` running on the Brain can continue to evaluate the `HealthCritical` mission trigger against its local `HealthData` mirror, completely decoupled from the physics node calculating the damage.

3\. Firing via Intent (Brain → Muscle)

Currently, the BTree's `AimAndFireExecutor` publishes a `FireRequestEvent` which is immediately consumed by the local `FireProcessingSystem` to spawn a bullet. Because we removed the `CombatModule` from the Brain, that event will just drop into the void.

We must bridge this across the network, treating weapons exactly like locomotion intents:

-   **Brain Egress:** Create a `WeaponIntentEgressTranslator` that listens to the `WeaponChannel` (or consumes the `FireRequestEvent` directly) and publishes a `WeaponStateTopic` or a dedicated `FireIntent` DDS message.-   **Muscle Ingress:** The Muscle node receives the intent and publishes a local `FireRequestEvent` to its own ECS bus.-   **Execution:** The `FireProcessingSystem` on the Muscle node consumes the event, spawns the `BallisticProjectile`, and runs the Continuous Collision Detection (CCD) pipeline entirely in isolation.

4\. Sensor Data Evaluation

You mentioned the Brain should just do sensor data evaluation. The architecture actually already supports isolating this even further.

We have an `AutonomousPerceptionModule` designed to run on a dedicated `NodeRole.Perception`. The Brain node publishes a `SensorConfig` (defining FOV and range), the Perception node builds a local spatial hash and does the heavy Line-of-Sight raycasting, and then publishes `SensorTargets` back to the Brain.

By pushing combat to the Muscle node, and perception to the Perception node, the Brain node is stripped down to pure decision-making: consuming `SensorTargets`, stepping the `BrainBTreeState`, and outputting `NavigationIntent` and `WeaponIntent`.

Yes i need the brain to emit WeaponFireIntent internal FDP event to be translated to DDS WeaponFireRequest (see the BDC.SST.Msg.idl source for illustration of the what the WeaponFireRequest migth look like in the final state, for the time being we should keep the topic name but use something much simplier as its attributes - we are building a proof of concept). The notification that a shot has been fired should be sent as another internal event WeaponFireNotification which is translated to DDS WeaponFire message (again see the BDC.SST.Msg.idl source for illustration ). When the ammo hits a target, there should be internal FDP event for hit notification which is translated to MunitionDetonation DDS event (again could be simplified for our POC). There should be an entity damage calculator module somewhere (not necessarily where physics is, but also not where the brain is - should be decoupled!) listening to hit notification and producing internal event for entity damage that is translated to DDS EntityHitDamage message. The hit notification should be applied to the EntityHealth component by some other module (not sure where this one shoudl live, probably different entity types can have their own specific damage apply module??. )

This is a highly robust, authoritative architecture. Separating the damage assessment into a dedicated module perfectly aligns with standard distributed simulation patterns (like DIS and HLA) and prevents client-side or brain-node manipulation of combat outcomes.

By mapping to the `WeaponFireRequest`, `WeaponFire`, `MunitionDetonation`, and `EntityHitDamage` network contract, we establish a strict Command-Query Responsibility Segregation (CQRS) pipeline across four distinct functional boundaries.

Here is the pragmatic, proof-of-concept (POC) design implementing this decoupled pipeline.

1\. The Core Event & Message Contracts

For the POC, we strip down the extensive IDL definitions into tight, unmanaged ECS structs and their corresponding DDS payloads.

```
// --- 1. Brain Intent ---
public struct WeaponFireIntent // ECS
{
    public long TargetEntityId;
    public int WeaponIndex;
}
// Translated to DDS: WeaponFireRequest [1, 5]

// --- 2. Muscle Notification (Shot Fired) ---
public struct WeaponFireNotification // ECS
{
    public long ShooterEntityId;
    public long TargetEntityId;
    public int WeaponIndex;
}
// Translated to DDS: WeaponFire (Tells IG to draw muzzle flash) [2, 6]

// --- 3. Muscle Notification (Impact) ---
public struct DetonationNotification // ECS
{
    public long ShooterEntityId;
    public long TargetEntityId;
    public float HitX, HitY, HitZ;
}
// Translated to DDS: MunitionDetonation (Tells IG to draw explosion, triggers damage calc) [3, 7]

// --- 4. Damage Arbiter (Assessment) ---
public struct DamageAssessedEvent // ECS
{
    public long HitEntityId;
    public float TotalDamage;
}
// Translated to DDS: EntityHitDamage [4]
```

2\. Node Responsibilities and System Flow

To implement this, we distribute the systems across the nodes as follows:

Node A: The Brain (Cognitive Tier)

The Brain is strictly responsible for decision-making.

-   **Execution:** The BTree `AimAndFireExecutor` evaluates line-of-sight and ROE. Instead of spawning a bullet, it publishes a `WeaponFireIntent` event locally.-   **Egress:** A `WeaponFireIntentEgressTranslator` consumes this event and broadcasts the `WeaponFireRequest` over DDS.

Node B: The Muscle (Physics Tier)

The Muscle node handles kinematics and continuous collision detection (CCD).

-   **Ingress:** A `WeaponFireRequestIngressTranslator` reads the DDS request and publishes a local `WeaponFireIntent`.-   **Execution (Fire):** The `FireProcessingSystem` consumes the intent. It spawns the `BallisticProjectile` entity into the physics world and simultaneously publishes a `WeaponFireNotification` event to trigger the muzzle flash egress.-   **Execution (Hit):** The `BallisticsSystem` and `HitResolutionSystem` process the swept-segment CCD. When a bullet strikes a collider, the system destroys the bullet and publishes a `DetonationNotification` event.-   **Egress:** A `MunitionDetonationEgressTranslator` broadcasts the `MunitionDetonation` DDS message.

Node C: The Combat Server (Damage Assessment Tier)

This node is entirely decoupled from physics and cognition. It acts as the impartial arbiter of combat math.

-   **Ingress:** A `MunitionDetonationIngressTranslator` listens for impacts and publishes `DetonationNotification` locally.-   **Execution:** A `DamageCalculationSystem` consumes the notification. It reads the armor thickness, calculates impact angles, applies penetration curves, and computes the final HP loss. It then publishes a `DamageAssessedEvent`.-   **Egress:** A `DamageAssessedEgressTranslator` broadcasts the `EntityHitDamage` DDS message.

Node D: The Authority (State Application Tier)

_Note: In many topologies, Node D and Node A (or B) are the same physical machine, but logically, the system acts only on entities it owns._

-   **Ingress:** An `EntityHitDamageIngressTranslator` receives the assessed damage and publishes `DamageAssessedEvent` locally.-   **Execution:** You noted that different entity types might need specific damage application modules. In ECS, this is trivial. Instead of one monolithic health system, you build highly targeted application systems that filter by component:-   These systems deduct the hit points from `Health.Current` and immediately update the `HealthData` mirror.-   **Egress:** Finally, the standard `EntityDamageEgressTranslator` detects the modified health and broadcasts the generic `EntityDamage` topic so the IG updates the health bars.

Architectural Benefits

-   **Security & Authority:** A compromised Brain node can spam `WeaponFireRequest` messages, but it cannot dictate that it successfully destroyed a target. The Combat Server retains absolute authority over the `EntityHitDamage`.-   **Performance:** Heavy raycasting and CCD are isolated to the Physics node, while complex armor-penetration math is isolated to the Combat node.-   **IG Decoupling:** The Image Generator (IG) simply listens to `WeaponFire` and `MunitionDetonation` to play particle effects natively, completely detached from the underlying ECS hit resolution.





does the following still apply?

\[BUG\] MissionTrigger.ReachedDestination trigger (brain) checks NavState which lives in another node (muscle) and will never be set on the brain node

In the current architecture, `MissionTrigger.ReachedDestination` directly polls the `NavState.HasArrived` field in the ECS.

If your navigation (Muscle tier) and brain (Cognitive tier) run on the same node (e.g., `NodeRole.AllInOne`), `NavState` is updated natively by the `CarKinematicsSystem` during the physics update.

However, if the navigation runs on a different node, **this trigger will fail and hang the mission indefinitely.**

Here is why this happens, and why it is an architectural flaw:

1.  **Lack of Replication:** `NavState` is an internal kinematic component. It is never replicated over the DDS network.
2.  **CQRS Violation:** The engine uses a strict CQRS contract for distributed movement. The Brain node publishes a `NavigationIntent` command, and the Muscle node (running `NavigationExecutionSystem`) executes the physics and replies with a `NavigationStatus`.
3.  Because `NavState` is never updated on the Brain node by the network ingress layer, `MissionDirectorSystem` will never see `HasArrived == 1`. It is a leaky abstraction that couples the cognitive tier directly to a local kinematic component.

**The Correct Architectural Approach** To fix this in a distributed topology, you must stop using `MissionTrigger.ReachedDestination` and instead use **`MissionTrigger.DoctrineFinished`**.

This fully respects the network boundary and leverages the CQRS pipeline correctly:

1.  The remote Muscle node determines that the vehicle is within the arrival radius and broadcasts `NavigationStatus.Result = Arrived` over DDS.
2.  The Brain node receives this via the `NavigationStatusIngressTranslator`.
3.  The `MoveToExecutor` (running on the Brain) reads the replicated `NavigationStatus` and returns `NodeStatus.Success`.
4.  The `BTreeTickSystem` observes that the doctrine's root node has reached a terminal state and publishes a `DoctrineFinishedEvent`.
5.  `MissionDirectorSystem` consumes the event and cleanly advances the mission phase without ever needing to touch `NavState`.

**Yes, this bug and architectural flaw still fully apply to the current codebase.**

The engine currently suffers from this exact leaky abstraction, and it will break distributed Brain/Muscle topologies as you described. Here is what the source code confirms:

-   **MissionDirectorSystem** **still polls** **NavState****:** Inside `MissionDirectorSystem.cs`, the `MissionTrigger.ReachedDestination` case still explicitly looks for the local `NavState` component and checks if `nav.HasArrived == 1`.-   **NavState** **is not replicated:** Looking at the network translators, `NavState` is completely absent from both the `KinematicTranslatorPack` (Muscle egress) and the `CognitiveTranslatorPack` (Brain ingress). Because it is never sent over DDS, the Brain node's `MissionDirectorSystem` will never observe `HasArrived == 1`.-   **The UI hardcodes the flawed trigger:** When a user right-clicks to send a vehicle to a location (`HandleRightClickForEntity`), the system generates a `MoveToLocation` task and hardcodes the `ReachedDestination` trigger into the mission plan. As a result, these generated missions will hang indefinitely if the vehicle's kinematics run on a separate node.

**The CQRS Pipeline is already built to support your fix:** Your proposed architectural approach using `MissionTrigger.DoctrineFinished` is exactly how the distributed command pipeline was intended to be used:

-   The Muscle node broadcasts `NavigationStatus` via the `NavigationStatusEgressTranslator`.-   The Brain node receives the status via `NavigationStatusIngressTranslator`.-   The `MoveToExecutor` correctly implements the CQRS contract: it observes `NavigationStatus.Result == Arrived` and translates it to `NodeStatus.Success`.-   The `BTreeTickSystem` detects this terminal success and cleanly publishes the `DoctrineFinishedEvent`.-   `MissionDirectorSystem` already has a working `MissionTrigger.DoctrineFinished` case that listens for this exact event to advance the phase.

**Additional Note (A second manifestation of the bug):** If you are fixing this, you will also need to address the `FollowRouteExecutor`. It currently runs on the Brain tier but directly polls `NavState.HasArrived` to determine if a route loop has finished. Because of the same lack of `NavState` replication, `FollowRouteExecutor` will also fail to advance in a distributed topology and needs to be migrated to read the CQRS `NavigationStatus` component instead.

what about the following bug?

\[BUG\] Entity health now calculated on all systems from hit events. Entity damage might be complex and should be calculated dedicated node(s), not in parallel on each node indepedently. Different entity types might need different damage calculators located at different nodes. In our current situation, the SimHost should be calulating the hit damage for all entitities. But the infrastructure should allow distributing this per entity type to multiple nodes. The resulting health update should be published over network.

**Yes, this bug is completely valid and still present in the current architecture.**

The combat pipeline currently violates the single-source-of-truth principle. Instead of calculating damage on a dedicated authoritative node and replicating the resulting health, the engine distributes the _event_ and relies on every node to calculate the damage independently.

Here is exactly how this architectural flaw manifests in the current codebase:

**1\.** **DamageSystem** **lacks an Authority Check** In the core toolkit (`FDP.Toolkit.Combat.Systems.DamageSystem`), the system simply consumes `HitEvent`s and deducts damage directly from the target's `Health` component: `health.Current -= damage;`. There is no `HasAuthority(evt.HitEntity)` check anywhere in this system. As a result, any node that simulates the bullet and generates a `HitEvent` will independently mutate the entity's health.

**2\. Events are replicated instead of State** Rather than replicating the health state, SimHost uses `FireInteractionEventTranslator` to bidirectionally replicate transient fire events (`FireInteractionEvent`) across the DDS network. Because the event is broadcasted, all nodes run their own local ballistics and hit resolution, leading to parallel damage calculations.

**3\. The** **EntityDamage** **Egress is Missing** The infrastructure to publish health updates over the network is half-finished. The `EntityDamage` DDS topic is defined, and the Image Generator (IG) actively runs an `EntityDamageIngressTranslator` to receive it and update the visual `IgHealthState`. However, **SimHost never publishes it**. Looking at the egress pipeline setup in `SimHostApp.cs`, `EntityMaster`, `EntityInfo`, `GeoSpatial`, and `EntityMission` are all registered for egress, but `EntityDamage` is completely absent.

**A Note on Regression:** Interestingly, older test code in the `NetworkDemo` handled this correctly. The `CombatFeedbackSystem` explicitly checked `if (own.PrimaryOwnerId == _localNodeId)` before applying damage to a tank. This critical ownership check was lost when the damage logic was extracted into the generalized `FDP.Toolkit.Combat.Systems.DamageSystem`.

**The Correct Architectural Approach to Fix This:** To distribute damage calculations as you suggested, combat must be refactored to mimic the CQRS pattern used by the Navigation tier:

-   **Enforce Authority:** `DamageSystem` must be updated to check `view.HasAuthority(evt.HitEntity)` before applying any damage.-   **Create the Egress Translator:** An `EntityDamageEgressTranslator` needs to be authored and registered in `SimHostApp.cs`. It should track changes to the `Health` component and publish them to the `EntityDamage` DDS topic.-   **Stop Replicating Fire Events to SimNodes:** `FireInteractionEvent` should only be sent to presentation nodes (like the IG for drawing tracers and explosions), rather than being used to trigger parallel physics calculations on peer simulation nodes.

ad point #3 stop replicating to SimNodes. We are using dds so we publish to whoever is listening. SimNodes WILL receive the event but only the damage system on the primary owner should be processing it, correct?

**Yes, you are exactly correct.** Your understanding perfectly aligns with how the engine's distributed authority model is designed to interact with the DDS publish-subscribe bus.

Because DDS broadcasts to the domain, any node that has instantiated a `DdsReader` for a topic will receive the sample. The correct way to handle distributed logic in this architecture is to let the event arrive on the node, but use the ECS authority mask to gate the actual state mutation.

Here is how your logic correctly applies to the combat pipeline:

**1\. The Network Reception (DDS)** When a combat event is published over DDS, every SimNode listening to that topic will receive it via their ingress translator (e.g., `PollIngress` decoding the DDS sample and publishing it to the local ECS `FdpEventBus`).

**2\. The Authority Guard (ECS)** Once the event is on the local event bus, the `DamageSystem` will consume it. This is where your point applies: the system must check if the local node is the authoritative owner of the target before doing any math. By wrapping the damage logic in an authority check—using `view.HasAuthority(evt.HitEntity)`—you ensure that while _every_ node sees the event, **only the primary owner of the target actually executes the damage calculation** and deducts from the `Health` component.

**3\. The State Egress** Because the primary owner was the only one to mutate the `Health` component, it is now the only node with dirty state. Its egress translator will pick up this change and publish the resulting `EntityDamage` state (or `EntityInfo` for death) back to the DDS bus. The other SimNodes and IGs will receive this state update and apply it, keeping everyone perfectly synchronized.

**One Minor Optimization Note:** While you are entirely correct that DDS publishes to whoever is listening, you _can_ prevent SimNodes from receiving the event at the network layer if you choose to. In this engine, a node only receives a DDS topic if you register an ingress translator for it in the `NodeBootstrapper` or `SimHostApp`.

-   If a fire event is **purely visual** (like `Bagira.BDC.SSTM.FireInteractionEvent` which just contains X/Y coordinates for drawing tracers), you can optimize by only adding the ingress translator to the Image Generator (IG) nodes.-   If the event contains **logical hit data** that the simulation needs to process, then your approach is the exact intended architecture: let the SimNodes receive it, but enforce `HasAuthority` in the system before applying damage.

Are there any other similar violations of Brain/Muscle topologies like with the navstate?

**Yes, the** **NavState** **leaky abstraction is actually pervasive throughout the Cognitive (Brain) tier.**

While the `MoveToExecutor` correctly implements the CQRS pattern by writing to `NavigationIntent` and reading from `NavigationStatus`, almost all other navigation executors and some BTree nodes completely bypass the network contract and directly mutate or poll the local `NavState` component.

Because `NavState` is a local kinematic component that is not replicated over the network, **these behaviors will silently fail in a distributed topology** (the Brain node will write to its local `NavState`, but the Muscle node running the actual physics will never see the update).

Here are the specific violations found in the codebase:

**1\.** **FleeExecutor**

-   **The Violation:** Instead of writing a `NavigationIntent`, `ComputeAndWriteFleeDestination` directly fetches the `NavState` component and overwrites its fields: `nav.Mode = KinematicsMode.Direct; nav.FinalDestination = ...; nav.TargetSpeed = p.Speed;`.-   **The Exit Bug:** In `OnExit`, it also directly wipes the state: `nav.TargetSpeed = 0f; nav.Mode = KinematicsMode.None;`.

**2\.** **FollowRoadGraphExecutor**

-   **The Violation:** In `OnEnter`, it directly mutates `NavState` to set up road navigation: `nav.Mode = KinematicsMode.RoadGraph; nav.RoadPhase = RoadGraphPhase.Approaching; nav.CurrentSegmentId = p.TargetNodeId;`.-   **The Poll Bug:** In `Execute`, it directly polls `nav.HasArrived != 0` instead of reading the CQRS `NavigationStatus.Result`.-   **The Exit Bug:** `OnExit` forces `nav.Mode = KinematicsMode.None;`.

**3\.** **FollowRouteExecutor**

-   **The Violation:** In `OnEnter`, it directly injects the trajectory into `NavState`: `nav.Mode = KinematicsMode.CustomTrajectory; nav.TrajectoryId = p.TrajectoryId;`.-   **The Poll Bug:** In `Execute`, it checks `nav.HasArrived == 0` to see if the route is finished.-   **The Exit Bug:** `OnExit` forces `nav.Mode = KinematicsMode.None;`.

**4\.** **SimHostNodes.Action\_Wander** **(BTree Node)**

-   **The Violation:** The BTree action itself directly polls `NavState` to figure out if it needs to pick a new random destination. It checks: `if (!needsNewTarget && ctx.World.HasComponent<NavState>(ctx.Self)) { var nav = ctx.World.GetComponent<NavState>(ctx.Self); if (nav.HasArrived != 0) needsNewTarget = true; }`. BTree nodes live exclusively on the Brain tier and should never poll Muscle-tier components directly.

**How to Fix This:** The infrastructure to fix this already exists. The `NavigationMode` enum used by `NavigationIntent` already has definitions for `FollowRoute`, `DirectPoint`, and `JoinFormation`.

To repair the distributed architecture, all of these executors must be rewritten to mimic `MoveToExecutor`. They need to translate their parameters into a `NavigationIntent` component, increment the `IntentId`, and let the `NavigationIntentBridgeSystem` on the Muscle node handle the translation into `NavState`.

what about the smart-sensors/perception mechanism from the prespective of brain/muscle separation?

Unlike the navigation mechanism that currently suffers from the leaky `NavState` abstraction, the **Smart Sensors and Perception mechanism is properly designed for distributed Brain/Muscle separation and strictly follows the CQRS network contract.**

Instead of the Brain computing line-of-sight locally, the architecture offloads this heavy lifting to a dedicated perception solver node using a clean request/response loop over DDS.

Here is how the perception mechanism correctly handles the distributed topology:

**1\. The Command / Configuration (Brain -> Solver)** The Brain node (which runs the AI doctrines) owns the configuration of the entity's sensors but does not run the spatial queries.

-   It uses the `BrainPerceptionTranslatorPack` to broadcast `SensorConfig` descriptors over DDS.-   This descriptor contains the parameters needed for sensing, such as `VisionRange`, `HearingRange`, and `FovDegrees`.-   The Brain can also publish explicit `RaycastRequestBatch` queries for specific line-of-sight checks.

**2\. The Execution (Perception Node)** Instead of a "Muscle" node handling physics, perception uses a dedicated node running `NodeRole.Perception`.

-   This node runs the `AutonomousPerceptionModule` on a background thread at 10 Hz.-   It receives the Brain's configurations via the `SimPerceptionTranslatorPack`.-   It executes the heavy spatial math using four dedicated systems: `LocalGridBuilderSystem` (building a spatial hash grid), `VisionBroadphaseSystem` (filtering by faction and FOV), `LosRequestBatchingSystem` (performing physics-accurate raycasts), and `ThreatEvaluationSystem` (scoring and decaying threats).

**3\. The Status / Result (Solver -> Brain)** Once the perception node finishes evaluating what an entity can see or hear, it acts as the authoritative source of truth for the environment.

-   It publishes `SensorTargets` over DDS (which contains a list of targets with their `ThreatScore`, `Distance`, and `BearingDegrees`).-   It also publishes `RaycastResponseBatch` messages for any explicit line-of-sight requests.

**4\. The Ingestion and AI Decision (Brain)** The Brain node receives the `SensorTargets` messages via its `SensorTargetsIngressTranslator` and uses them to update the local `TargetMemory` ECS component attached to the observer entity.

-   Behavior trees running on the Brain (like the Insurgent's `Ambush_BT` doctrine) simply use nodes like `Condition_HasTarget` to check if `TargetMemory.Count > 0`.

**Summary:** Because the Brain tier never directly polls kinematic or spatial components to "see" targets, it completely avoids the distributed hang-ups seen in the navigation tier. It writes to `SensorConfig`, waits for `SensorTargets` to update its `TargetMemory`, and reacts accordingly—perfectly respecting the network boundary.

what about vehicle navigation? Imagine the brain wants to tell the vehicle to go to given target point but using navmesh-based navigation mechanism. This meas the navigation infrastructure needs to find a path and then let the vehicle to follow this path; and this path migh be dynamically updated as the vehicle moves because the situation in the world can change (new obstacles etc.) So the navigated vehicle movements needs to be autonomous from the brain's point of view and the brain needs to know only if the vehicle successfully reached the destination or if it is still moving or if something failed (no part exists, vehicle got stuck etc.)

The engine's CQRS (Command Query Responsibility Segregation) architecture is specifically designed to handle exactly this kind of autonomous, abstracted vehicle navigation. It completely decouples the "Brain" (which decides _where_ to go) from the "Muscle" (which figures out _how_ to get there and execute the physics).

Here is how the distributed infrastructure currently handles autonomous navigation and status reporting:

**1\. The Command (Brain -> Muscle/Solver)** When an AI doctrine on the Brain node wants a vehicle to move, it does not micromanage the steering or local pathing. Instead, it publishes a **NavigationIntent** component over the DDS network.

-   This intent simply specifies the `FinalDestination` (in geographic coordinates), the `TargetSpeed`, the `ArrivalRadius`, and the `Mode` (e.g., `NAV_DIRECT_POINT` or `NAV_FOLLOW_ROUTE`).-   If a complex path needs to be found around obstacles, the Brain can offload this to a dedicated **Navigation Solver node** by publishing a `PathRequestBatch` containing the start, end, and mobility profile (wheeled, tracked, infantry). The solver computes the route across the road graph and returns a registered `RouteHandle` for the vehicle to follow.

**2\. Autonomous Execution (Muscle)** Once the Muscle node receives the `NavigationIntent` (or the computed route trajectory), its local kinematics systems take full autonomous control:

-   The `NavigationIntentBridgeSystem` translates the Brain's command into a local kinematic `NavState`.-   The **CarKinematicsSystem** handles the actual frame-by-frame movement physics, steering (via a Pure Pursuit controller), and following the trajectory.-   **Dynamic Updates & Obstacles:** The Muscle node autonomously handles dynamic situational changes using **RVO (Reciprocal Velocity Obstacles)**. It continuously queries a spatial hash grid for nearby neighbors and calculates avoidance forces to dynamically steer around unexpected obstacles while still trying to maintain its preferred path to the destination.

**3\. Status and Feedback (Muscle -> Brain)** Just as you described, the Brain only needs to know the high-level outcome. The Muscle tier runs the **NavigationExecutionSystem**, which continuously monitors the vehicle's progress and publishes a **NavigationStatus** component back to the Brain. This CQRS status perfectly maps to the states you mentioned:

-   **Still Moving:** It reports **RES\_IN\_PROGRESS** while the vehicle is actively driving toward the destination.-   **Successfully Reached:** Once the vehicle is within the requested `ArrivalRadius`, it reports **RES\_ARRIVED**.-   **Failed / Stuck:** To detect if a vehicle gets stuck (e.g., blocked by debris), the system uses a "frustration guard." If the vehicle's speed drops below a `FrustrationSpeedThreshold` (e.g., 0.2 m/s) for too many consecutive ticks, it autonomously reports **RES\_FAILED\_BLOCKED**. It can also report **RES\_FAILED\_UNREACHABLE** if the pathfinding solver determines no valid route exists.

Back on the Brain node, the `MoveToExecutor` reads this status from the network and simply translates it into standard Behavior Tree signals (`Running`, `Success`, or `Failure`), allowing the AI to react cleanly without ever knowing the vehicle was actively dodging obstacles along the way.
