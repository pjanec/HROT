# **FDP Demo Framework Spec**

WARNING: OBSOLETE, for reference only from new documents, superseded by new ideas!


\# Specification: FDP Demo Framework & Runner

\#\# 1\. Objective  
The FDP Demo Framework serves a dual purpose:  
1\.  **\*\*Headless CI Validation:\*\*** It provides a lightning-fast, headless environment to mathematically assert engine stability, component replication, and toolkit decoupling via the command line.  
2\.  **\*\*Visual Debugging:\*\*** It allows engineers to seamlessly attach the \`FDP.Toolkit.Vis2D\` Map Viewer to any running headless scenario to visually inspect ECS ghosting, AI trajectories, and combat raycasts in real-time.

\#\# 2\. Folder Layout & Organization  
To maintain a pristine architecture, the demo project (\`Fdp.Examples\`) must strictly separate scenario logic from data definitions. 

Fdp.Examples/  
├── Fdp.Examples.csproj  
├── Scenarios/                 \# The actual test execution logic  
│   ├── Cognitive/               
│   │   ├── BehaviorValidationScenario.cs  
│   │   └── ChannelDispatchScenario.cs  
│   ├── Kinematics/  
│   │   └── AutoDriveScenario.cs  
│   └── Network/  
│       └── DistributedTankScenario.cs  
├── Configuration/             \# Scenario-specific mock data  
│   ├── MockRoadGraphs.cs  
│   └── MockElevationData.cs  
└── Constants/                 \# Strict enforcement of "No Magic Strings"  
    ├── ScenarioNames.cs  
    ├── TemplateIds.cs  
    └── DoctrineKeys.cs

## **3\. Naming Rules & Architectural Standards**

The FDP engine relies on integer IDs and structured ECS components. **Magic strings are strictly forbidden.**

* **Action Dispatching:** Never use string identifiers for actions. Always reference the toolkit constants (e.g., NavigationConstants.ActionIdMoveTo, CombatConstants.ActionIdAimAndFire).  
* **Template IDs:** Entity templates must be referenced via a centralized static class (TemplateIds.CommanderBrain, TemplateIds.KinematicCar).  
* **Scenario Registration:** Scenarios must be registered via ScenarioNames.cs to ensure the CLI runner can parse arguments safely without typos.  
* **Tick-Based Logic:** Scenarios must *never* use Thread.Sleep() or wall-clock timers. All temporal logic must be bound to node.OnTick \+= (currentTick) \=\> { ... }.

## **4\. The NodeBootstrapper**

The NodeBootstrapper is the core wrapper around the Fdp.Kernel. Every scenario must implement the IScenario interface, which is injected with the Bootstrapper.

The Bootstrapper handles three distinct phases:

1. **Composition:** Registering only the specific toolkits required for the NodeRole (e.g., node.RegisterToolkit\<BehaviorToolkit\>()).  
2. **Event Injection:** Hooking into node.OnTick to mutate ECS components or write to Fdp.Kernel Command Buffers at specific deterministic frames.  
3. **Mathematical Assertion:** Using node.SetCompletionCondition(Func\<bool\> condition) to evaluate the EntityObserverHistory. If this returns true, the runner exits with Code 0 (Success). If it never returns true before \--max-ticks is reached, it exits with Code 1 (Failure).

## **5\. The CLI Runner (fdp.framework.runner)**

The fdp.framework.runner executable is the entry point for both CI pipelines and local developer testing. It dynamically instantiates the requested IScenario and spins up the required NodeBootstrapper instances.

### **5.1 Command Line Arguments**

| Argument | Type | Description |
| :---- | :---- | :---- |
| \--scenario | String | (Required) The name of the scenario to execute (e.g., AutoDrive). |
| \--role | String | The specific NodeRole to assume. If omitted, the runner assumes the role of a monolithic "Host" running all toolkits in a single process. |
| \--network-mode | Enum | Determines DDS behavior. None (default for single-node tests), StrictLoopback (for Host-of-Hosts multi-node testing on localhost), or Multicast (for actual LAN testing). |
| \--max-ticks | Integer | (Default: 500\) The timeout threshold. If the scenario's completion condition isn't met by this tick, the test fails. |
| \--attach-vis2d | Flag | If present, overrides headless mode and boots a secondary ECS observer to render the visual map. |

### **5.2 Example CLI Executions**

**1\. Running a fast, single-process CI unit test:**

fdp.framework.runner.exe \--scenario ChannelDispatch \--max-ticks 100

**2\. Running a distributed test spanning multiple processes:**

*(Process A)*: fdp.framework.runner.exe \--scenario DistributedTank \--role Brain\_Driver \--network-mode StrictLoopback

*(Process B)*: fdp.framework.runner.exe \--scenario DistributedTank \--role Muscle\_Hull \--network-mode StrictLoopback

## **6\. Attaching the Vis2D Map Viewer**

The \--attach-vis2d flag is a powerful debugging tool. Because the FDP engine uses FDP.Toolkit.Replication, attaching a visualizer does **not** change the execution logic of the headless test.

When \--attach-vis2d is passed:

1. The Runner spins up the headless NodeBootstrapper as normal.  
2. It spins up a *secondary* NodeBootstrapper internally with NodeRole.PassiveObserver.  
3. This observer node registers the FDP.Toolkit.Vis2D toolkit and the Replication toolkit.  
4. It opens a Raylib/ImGui window.  
5. As the headless test mutates Transform3D and LocomotionChannel components, the Replication toolkit seamlessly ghosts them over to the observer node.  
6. The Vis2D render layers automatically query the local ghost components and draw the cars, splines, and combat raycasts on the screen.

**Why this matters:** The presence of the renderer cannot slow down or alter the physics/AI determinism of the headless scenario, guaranteeing that what you see visually is exactly what the CI pipeline evaluated mathematically.

# **FDP Examples: Data Model & Component Registry**

## **1\. Objective and Architectural Rules**

Fdp.Examples suite defines its own lightweight, Cartesian-only data model.

* **Fdp.Examples.DDS**: Contains the struct definitions (IDLs) for all network messages. These traverse the loopback network.  
* **Fdp.Examples.Common**: Contains the demo-specific ECS Components. These live in the local Fdp.Kernel memory space and are used by the demo-specific Systems (like the Damage Arbiter) or are mapped to generic Toolkit components via Translators.

## **2\. DDS Messages (Fdp.Examples.DDS)**

These structs represent the serialized data crossing the network boundaries between isolated Nodes (e.g., Brain to MuscleGround). They use simple primitive types to ensure fast serialization.

### **2.1. Kinematics & Locomotion**

* **Transform3DMsg**  
  * *Purpose:* Replicates physical state from Muscle to Observers/Brains.  
  * *Fields:* EntityId (int), X, Y, Z (float), Pitch, Yaw, Roll (float), Velocity (float).  
* **LocomotionIntentMsg**  
  * *Purpose:* Replicates the Brain's LocomotionChannel downwards to the Muscle node.  
  * *Fields:* EntityId (int), ActionId (int), InstanceId (int), TargetX, TargetY (float).  
* **WeaponIntentMsg**  
  * *Purpose:* Replicates the Brain's WeaponChannel downwards to the Muscle node.  
  * *Fields:* EntityId (int), ActionId (int), InstanceId (int), TargetX, TargetY (float), TargetEntityId (int).

### **2.2. Combat & Lifecycle**

* **WeaponFireEvent**  
  * *Purpose:* Fire-and-forget event when an executor spawns a projectile.  
  * *Fields:* ShooterId (int), WeaponType (int), OriginX, OriginY, OriginZ (float), DirX, DirY, DirZ (float), MuzzleVelocity (float).  
* **HitNotificationEvent**  
  * *Purpose:* Fired by the Physics broadphase when a batched raycast intersects a collider.  
  * *Fields:* TargetId (int), ShooterId (int), ImpactX, ImpactY, ImpactZ (float), HitBoxName (string/hash).  
* **SimObjectLifecycleMsg**  
  * *Purpose:* Commands all nodes to spawn, despawn, or visually destroy an entity.  
  * *Fields:* EntityId (int), TemplateId (int), State (Enum: Spawned, MobilityKill, Destroyed).

### **2.3. Perception & Environment**

* **PerceptionContactMsg**  
  * *Purpose:* Fired by the Environment\_Sensors node when LoS is achieved.  
  * *Fields:* ObserverId (int), TargetId (int), Modality (Enum: Visual, Acoustic, Radar), EstimatedX, EstimatedY (float).  
* **EnvironmentUpdateEvent**  
  * *Purpose:* Alters global simulation weather/conditions across all nodes.  
  * *Fields:* FogDensity (float), TimeOfDay (float).

### **2.4. High-Level Command (HQ)**

* **MissionCommandEvent**  
  * *Purpose:* Simulates an HQ operator overriding an AI's active waypoint.  
  * *Fields:* EntityId (int), CommandType (Enum: SetWaypoint, ClearWaypoint), TargetX, TargetY (float).  
* **DoctrineUpdateEvent**  
  * *Purpose:* Updates the AI's Rules of Engagement in the Blackboard.  
  * *Fields:* EntityId (int), Key (int/hash), Value (int \- e.g., WeaponsFree, HoldFire).

## **3\. ECS Components (Fdp.Examples.Common)**

While the FDP Toolkits (Behavior, Navigation, CarKinem) provide core components like BTreeComponent and LocomotionChannel, the validation suite requires specific mocked components to stitch the logic together without bringing in Bagira domain code.

### **3.1. Entity Definition & Hierarchy**

* **FactionTag**  
  * *Purpose:* Identifies friend/foe allegiance for BTree and Perception evaluation.  
  * *Fields:* FactionId (int: 1=Bluefor, 2=Opfor).  
* **ParentEntityComponent**  
  * *Purpose:* Used by Muscle\_Turret to locate its Hull over the network.  
  * *Fields:* ParentId (int), OffsetX, OffsetY, OffsetZ (float).

### **3.2. Health & Damage Arbitration**

* **HealthComponent**  
  * *Purpose:* Tracked by the Combat toolkit; mutated by HitNotificationEvent.  
  * *Fields:* TotalHealth (float), EngineHealth (float), TurretHealth (float).  
* **KinematicConstraintsComponent**  
  * *Purpose:* The decoupled parameter block read by CarKinem. Mutated by the DamageArbiterSystem when EngineHealth drops to 0\.  
  * *Fields:* MaxSpeed (float), MaxAcceleration (float), CanSteer (bool).

### **3.3. Sensor Config & Output**

* **VisionSensorComponent** / **AcousticSensorComponent**  
  * *Purpose:* Holds tuning parameters for the PerceptionToolkit broadphase.  
  * *Fields:* BaseRange (float), FieldOfViewDegrees (float).  
* **PerceptionComponent**  
  * *Purpose:* The aggregated output written by the sensor systems.  
  * *Fields:* VisualContacts (List\<int\>), AcousticContacts (List\<int\>).  
* **EnvironmentStateComponent** (Singleton)  
  * *Purpose:* Holds local weather data affecting sensor ranges.  
  * *Fields:* FogDensity (float).

### **3.4. Geographic Interpolation**

* **TerrainClampComponent**  
  * *Purpose:* Tuning parameters for the PredictiveClamping scenario.  
  * *Fields:* LookAheadDistance (float), InterpolationRate (float).

## **4\. Translators (The ECS-to-DDS Bridge)**

To enforce the CQRS separation, the Fdp.Examples.Common assembly must include lightweight Systems (Translators) that run at the beginning and end of the Fdp.Kernel tick:

1. **LocomotionEgressTranslator (Brain Node):** Reads the LocomotionChannel component. If ActionInstanceId changed, serializes it into a LocomotionIntentMsg and publishes to DDS.  
2. **LocomotionIngressTranslator (Muscle Node):** Subscribes to LocomotionIntentMsg. Writes the values directly into the local LocomotionChannel component, waking up the ActionDispatchModule.  
3. **TransformEgressTranslator (Muscle Node):** Reads Transform3D managed by CarKinem. Publishes Transform3DMsg to DDS.  
4. **TransformIngressTranslator (Brain / IG Nodes):** Subscribes to Transform3DMsg. Updates the local ghost Transform3D components so sensors and AI can read them.

# 

# **Specification: Fdp.Examples.ChannelDispatch**

## **1\. Objective**

The ChannelDispatch scenario is a headless, CI-focused unit test designed to mathematically prove **Concurrent Action Execution** within the FDP Engine's cognitive layer.

Instead of relying on generic message passing, this test validates the actual Fdp.Kernel ECS architecture and the ActionDispatchModule. It proves that the engine can independently execute, interrupt, and maintain continuous actions across multiple hardware-like "channels" (Locomotion and Weapons) on a single entity within the exact same simulation tick, without thread blocking or state corruption.

## **2\. Architectural Alignment**

This demo relies strictly on the existing FDP codebase:

* **ECS Components:** Uses the actual LocomotionChannel and WeaponChannel components to hold action state (ActiveAction, ActionInstanceId).  
* **Action Dispatch Module:** Uses the actual ActionDispatchModule to map action constants (e.g., NavigationConstants.ActionIdMoveTo) to specific IActionExecutor implementations.  
* **Decoupled Execution:** Bypasses the Behavior Tree entirely to directly mutate the channel components, proving that the Dispatcher systems (LocomotionDispatcherSystem, WeaponDispatcherSystem) react correctly to raw ECS memory changes.

## **3\. Scenario Setup**

### **3.1. Mock Executors**

To prove the dispatchers are firing without requiring the heavy physics or combat toolkits, the test registers lightweight Mock Executors.

* **MockMoveExecutor**: Implements IActionExecutor. Records the exact Node.CurrentTick whenever its Execute() or OnExit() methods are invoked by the Dispatcher.  
* **MockWeaponExecutor**: Implements IActionExecutor. Records the exact Node.CurrentTick whenever its Execute() or OnExit() methods are invoked.

### **3.2. Node Bootstrapping**

The NodeBootstrapper initializes a minimal headless world:

1. Registers the ActionDispatchModule, binding NavigationConstants.ActionIdMoveTo to the MockMoveExecutor and CombatConstants.ActionIdAimAndFire to the MockWeaponExecutor.  
2. Creates a single TestEntity via Fdp.Kernel.  
3. Adds the LocomotionChannel and WeaponChannel components to the TestEntity.

## **4\. Execution Timeline (The Test Script)**

The scenario hooks into the Node.OnTick event to artificially manipulate the ECS components, acting as a synthetic Behavior Tree.

| Tick | Simulated Action (ECS Component Mutation) | Expected Dispatcher Behavior | Architectural Proof |
| :---- | :---- | :---- | :---- |
| **10** | LocomotionChannel.ActiveAction \= MoveTo LocomotionChannel.ActionInstanceId++ | MockMoveExecutor.Execute() is invoked. MockWeaponExecutor remains idle. | **Standard Routing.** The Locomotion Dispatcher successfully reads the component and routes to the correct executor. |
| **20** | WeaponChannel.ActiveAction \= AimAndFire WeaponChannel.ActionInstanceId++ | MockMoveExecutor.Execute() is invoked. MockWeaponExecutor.Execute() is invoked. | **Concurrency.** Both dispatchers ran in the same ECS tick and processed their respective channels simultaneously without blocking the main thread. |
| **30** | LocomotionChannel.ActiveAction \= 0 (Idle) | MockMoveExecutor.OnExit() is invoked. MockWeaponExecutor.Execute() is invoked. | **Graceful Interruption.** Clearing the Locomotion channel correctly fired the cleanup logic, while the Weapon channel continued uninterrupted. |

## **5\. Programmatic Assertions**

The test evaluates success at Tick 40 using the recorded history inside the Mock Executors. If any assertion fails, the CI pipeline fails.

node.SetCompletionCondition(() \=\>   
{  
    if (node.CurrentTick \< 40\) return false;

    // 1\. Assert Independent Locomotion (Tick 10-19)  
    // The Move executor must have fired at tick 15, but Weapon executor must be empty.  
    bool startedMoving \= mockMoveExecutor.ExecuteTicks.Contains(15);  
    bool weaponWasIdle \= \!mockWeaponExecutor.ExecuteTicks.Contains(15);

    // 2\. Assert Concurrent Execution (Tick 20-29)  
    // Both executors MUST contain the exact same tick in their history (e.g., Tick 25).  
    bool weaponFiredConcurrently \= mockWeaponExecutor.ExecuteTicks.Contains(25);  
    bool movementContinuedConcurrently \= mockMoveExecutor.ExecuteTicks.Contains(25);

    // 3\. Assert Independent Halting (Tick 30-39)  
    // Move executor must have registered an Exit, and stopped recording Executes.  
    // Weapon executor must continue recording Executes.  
    bool stoppedMoving \= mockMoveExecutor.ExitTicks.Contains(30) && \!mockMoveExecutor.ExecuteTicks.Contains(35);  
    bool weaponKeptFiring \= mockWeaponExecutor.ExecuteTicks.Contains(35);

    return startedMoving && weaponWasIdle &&   
           weaponFiredConcurrently && movementContinuedConcurrently &&   
           stoppedMoving && weaponKeptFiring;  
});

## **6\. Value to the FDP Framework**

By isolating the ActionDispatchModule from the physics and network layers, this test mathematically guarantees that the core ECS channel abstraction is sound. It ensures that future modifications to the Fdp.Kernel iterators or dispatcher systems will never accidentally introduce synchronous blocking behavior that could paralyze AI agents during combat.

# **Specification: Fdp.Examples.ParallelStories**

## **1\. Objective**

The ParallelStories scenario is a headless, CI-focused unit test designed to mathematically prove **Deterministic After Action Review (AAR) and Component Replay**.

It validates that the FDP.Toolkit.Replication module can capture a live stream of ECS component mutations (Transform3D, LocomotionChannel, WeaponChannel) and serialize them to a data log. More importantly, it proves that a "naked" node (one stripped of all Behavior, Combat, and CarKinem toolkits) can use a Replay Pump to inject those recorded component states back into the Fdp.Kernel ECS world. It guarantees that parallel data streams (e.g., a Brain's log and a Muscle's log) can be replayed simultaneously in perfect tick-lockstep, recreating a complex skirmish without executing a single line of simulation logic.

## **2\. Architectural Alignment**

This demo rigorously tests the boundary between Simulation and Data Representation:

* **The Recording Phase:** A fully equipped node runs a short simulation. A RecordingModule hooks into the FDP.Toolkit.Replication outbound queues, saving the serialized Bagira.DDS.DataModel SST descriptors to a memory buffer or disk, timestamped by the exact simulation tick.  
* **The Replay Phase:** The node is completely purged of ActionDispatchModule, BehaviorToolkit, and CarKinemToolkit. A ReplayPumpModule reads the recorded descriptors and directly mutates the ECS components (Transform3D, HealthComponent) at the designated ticks.  
* **No Re-Simulation:** Proves that an entity can move and fire a weapon during a replay *without* triggering the Batched Raycast physics solver or the LocomotionDispatcherSystem, because those systems literally do not exist in the replay node's composition.

## **3\. Scenario Setup**

The test consists of two sequential phases managed by the IScenario runner.

### **3.1. Phase A: The Live Recording**

* **Node Composition:** Full suite (Behavior, CarKinem, Combat, ActionDispatch, Replication, Recording).  
* **The Story:** An entity (ID: 100\) is commanded to drive to X: 50\. At Tick 20, it detects a threat and fires its weapon.  
* **The Output:** A serialized log of ECS component changes (LiveLog).

### **3.2. Phase B: The Parallel Replay**

* **Node Composition:** Minimal suite (Fdp.Kernel, Replication, ReplayPump).  
* **The Action:** The ReplayPump is fed the LiveLog. It automatically steps through the ticks, applying the recorded component states to the ECS world.  
* **The Output:** A second history of ECS component states (ReplayHistory).

## **4\. Execution Timeline (The Replay Phase)**

The execution timeline tracks how the Replay Pump bypasses normal simulation routing.

| Tick | Recorded Event in Log | Replay Pump Action | Architectural Proof |
| :---- | :---- | :---- | :---- |
| **1** | LocomotionChannel mutated to ActionIdMoveTo. | ReplayPump writes to LocomotionChannel. | State accurately restored. No dispatcher wakes up. |
| **5** | Transform3D.X becomes 10.0. | ReplayPump forces Transform3D.X \= 10.0. | **No Physics Loop.** The entity moved perfectly without CarKinem calculating velocity or friction. |
| **20** | WeaponChannel mutated to ActionIdAimAndFire. | ReplayPump writes to WeaponChannel. | State restored. |
| **21** | Projectile Entity Spawned. | ReplayPump queues Spawn command to Lifecycle. | **Replicated Lifecycle.** Structural changes are accurately mirrored without the combat executor. |

## **5\. Programmatic Assertions**

The CI pipeline runs both phases and then mathematically asserts that the memory footprint of the Replay world matches the Live world byte-for-byte at specific keyframes, proving absolute determinism.

public class ParallelStoriesScenario : IScenario  
{  
    public void Configure(NodeBootstrapper node, NodeRole role)  
    {  
        // This test manages two separate Bootstrappers internally to prove replay isolation.

        // \--- PHASE A: LIVE RUN \---  
        var liveNode \= new NodeBootstrapper();  
        liveNode.RegisterToolkit\<BehaviorToolkit\>();  
        liveNode.RegisterToolkit\<CarKinemToolkit\>();  
        liveNode.RegisterToolkit\<ActionDispatchModule\>();  
        liveNode.RegisterToolkit\<RecordingToolkit\>(); // Enables component serialization  
          
        var entity \= liveNode.World.CreateEntity();  
        liveNode.World.AddComponent(entity, new LocomotionChannel());  
        liveNode.World.AddComponent(entity, new Transform3D());

        // Simulate Action  
        liveNode.OnTick \+= (tick) \=\> {  
            if (tick \== 1\) liveNode.World.GetComponentRW\<LocomotionChannel\>(entity).ActiveAction \= NavigationConstants.ActionIdMoveTo;  
        };

        liveNode.RunUntil(maxTicks: 50);  
        var liveLog \= liveNode.GetModule\<RecordingToolkit\>().ExportLog();  
        var liveHistory \= liveNode.GetObserverHistory();

        // \--- PHASE B: REPLAY RUN \---  
        var replayNode \= new NodeBootstrapper();  
        // Notice: NO Behavior, NO CarKinem, NO ActionDispatch.  
        replayNode.RegisterToolkit\<ReplayPumpModule\>(new ReplayConfig { LogData \= liveLog });  
          
        replayNode.RunUntil(maxTicks: 50);  
        var replayHistory \= replayNode.GetObserverHistory();

        // \--- ASSERTIONS \---  
        node.SetCompletionCondition(() \=\>   
        {  
            // 1\. Assert Physics Bypass Determinism  
            // The Transform at Tick 25 must match perfectly, proving the Replay Pump   
            // correctly forced the coordinates without the CarKinem module running.  
            var liveTick25Transform \= liveHistory.GetStateAt(25, entityId: entity.Id).Transform;  
            var replayTick25Transform \= replayHistory.GetStateAt(25, entityId: entity.Id).Transform;  
              
            bool transformsMatch \= Vector3.Distance(liveTick25Transform.Position, replayTick25Transform.Position) \< 0.0001f;

            // 2\. Assert Channel State Restoration  
            // The Locomotion Channel must show it was "active" during replay, even though no executor was reading it.  
            var liveTick10Channel \= liveHistory.GetStateAt(10, entityId: entity.Id).LocomotionChannel;  
            var replayTick10Channel \= replayHistory.GetStateAt(10, entityId: entity.Id).LocomotionChannel;

            bool channelsMatch \= liveTick10Channel.ActiveAction \== replayTick10Channel.ActiveAction;

            return transformsMatch && channelsMatch;  
        });  
    }  
}

## **6\. Value to the FDP Framework**

The ability to deterministically replay scenarios is the holy grail of military simulation (AAR) and engine debugging.

By validating that the ReplayPumpModule interfaces directly with Fdp.Kernel and FDP.Toolkit.Replication while ignoring the actual simulation toolkits, this test ensures:

1. **Debugging Confidence:** If an AI agent does something anomalous in a live test, a developer can replay the exact sequence of ECS states locally without worrying about butterfly-effect physics divergence.  
2. **Bandwidth Efficiency:** It proves that your Bagira.DDS.DataModel SST serialization is complete and robust enough to hold the entire state of the world in a generic format.

# **Specification: Fdp.Examples.BehaviorValidation**

## **1\. Objective**

The BehaviorValidation scenario is a headless, CI-focused unit test designed to validate the **Cognitive Decision Pipeline** of the FDP Engine (FDP.Toolkit.Behavior).

It mathematically proves the handshake between the Hierarchical State Machine (HSM), the Behavior Tree (BTree), the Blackboard, and the ECS Action Channels. The goal is to verify that changes in the environment (represented by Blackboard mutations) correctly trigger HSM state transitions, which in turn evaluate BTree logic, ultimately resulting in the correct assignment of actions to the LocomotionChannel and WeaponChannel.

## **2\. Architectural Alignment**

This demo relies strictly on the actual AI and ECS implementations:

* **BlackboardComponent**: Acts as the shared memory space. The test script artificially mutates this memory (e.g., setting IsThreatVisible \= true) to simulate sensor input.  
* **HsmComponent & BTreeComponent**: The core AI modules. The HSM routes execution based on the Blackboard, and delegates ticks to the BTree.  
* **Component-Based Intent:** The BTree does *not* publish DDS messages or execute physics. Its sole responsibility is to mutate the ActiveAction integers on the entity's LocomotionChannel and WeaponChannel.  
* **Execution Boundary:** This test does *not* load the ActionDispatchModule or any Executors. We are strictly testing the AI's ability to *decide*, stopping exactly at the point where the decision is written to ECS memory.

## **3\. Scenario Setup**

### **3.1. The AI Template**

The NodeBootstrapper initializes a headless world with the BehaviorToolkit and spawns a single CommanderEntity.

* **Blackboard Setup:** Ammo \= 2, IsThreatVisible \= false.  
* **HSM Definition:** \* State Patrol: Writes ActionIdMoveTo to the Locomotion Channel. Transitions to Combat if IsThreatVisible \== true.  
  * State Combat: Delegates tick execution to the CombatBTree.  
* **BTree Definition (CombatBTree):**  
  * *Selector Node:*  
    * *Sequence 1 (Engage):* Condition(Ammo \> 0\) \-\> WriteAction(WeaponChannel, ActionIdAimAndFire) \-\> Action(Decrement Ammo).  
    * *Sequence 2 (Flee):* WriteAction(LocomotionChannel, ActionIdEvade).

### **3.2. Node Bootstrapping**

The Node registers only the BehaviorToolkit (no physics, no dispatchers) to guarantee a lightning-fast execution isolated from simulation latency.

## **4\. Execution Timeline (The Test Script)**

The scenario hooks into the Node.OnTick event to mutate the Blackboard (acting as the "Environment") and observes the resulting channel mutations (acting as the "Dispatcher").

| Tick | Simulated Environment (Blackboard Mutation) | Expected AI Reaction (Channel States) | Architectural Proof |
| :---- | :---- | :---- | :---- |
| **10** | None (IsThreatVisible is false). | LocomotionChannel \= MoveTo WeaponChannel \= Idle | **Base State.** HSM correctly evaluates the Patrol state and writes to Locomotion. |
| **20** | Blackboard.IsThreatVisible \= true | LocomotionChannel \= Idle WeaponChannel \= AimAndFire | **HSM Transition & BTree Execution.** HSM transitioned to Combat, delegated to BTree. BTree evaluated ammo \> 0 and wrote to the Weapon channel. |
| **22** | None (Ammo has reached 0 internally). | LocomotionChannel \= Evade WeaponChannel \= Idle | **BTree Fallback.** The BTree condition (Ammo \> 0\) failed. The Selector successfully fell back to the Flee sequence. |

## **5\. Programmatic Assertions**

The test runs for 30 ticks and evaluates the history of the ECS components.

public class BehaviorValidationScenario : IScenario  
{  
    public void Configure(NodeBootstrapper node, NodeRole role)  
    {  
        // ... (Entity Setup with HSM, BTree, Blackboard, Channels) ...

        node.OnTick \+= (currentTick) \=\>   
        {  
            if (currentTick \== 20\)  
            {  
                // Simulate an external sensor detecting a threat  
                ref var bb \= ref node.World.GetComponentRW\<BlackboardComponent\>(entity);  
                bb.SetBool("IsThreatVisible", true);  
            }  
        };

        node.SetCompletionCondition(() \=\>   
        {  
            if (node.CurrentTick \< 30\) return false;

            // Retrieve the captured state histories from the test observer  
            var history \= node.GetObserverHistory(entity);

            // 1\. Assert Patrol State (Tick 15\)  
            var tick15 \= history.GetStateAt(15);  
            bool wasPatrolling \= tick15.LocomotionChannel.ActiveAction \== NavigationConstants.ActionIdMoveTo &&  
                                 tick15.WeaponChannel.ActiveAction \== 0;

            // 2\. Assert Combat State & BTree Sequence (Tick 21\)  
            var tick21 \= history.GetStateAt(21);  
            bool wasFighting \= tick21.LocomotionChannel.ActiveAction \== 0 &&  
                               tick21.WeaponChannel.ActiveAction \== CombatConstants.ActionIdAimAndFire;

            // 3\. Assert BTree Selector Fallback (Tick 25\)  
            // By Tick 25, the BTree should have decremented ammo to 0 and hit the fallback Evade action  
            var tick25 \= history.GetStateAt(25);  
            bool wasEvading \= tick25.LocomotionChannel.ActiveAction \== NavigationConstants.ActionIdEvade &&  
                              tick25.WeaponChannel.ActiveAction \== 0;

            return wasPatrolling && wasFighting && wasEvading;  
        });  
    }  
}

## **6\. Value to the FDP Framework**

This unit test provides absolute confidence in the FDP.Toolkit.Behavior module. By decoupling the AI's cognitive output from actual physical execution, we can test thousands of complex tactical decision trees in milliseconds. It guarantees that the core AI abstractions (State Machines and Behavior Trees) correctly interface with the Fdp.Kernel ECS components required by the rest of the engine.

# **Specification: Fdp.Examples.MissionCommand**

## **1\. Objective**

The MissionCommand scenario is a headless, CI-focused unit test designed to mathematically prove that **Dynamic Mission and Doctrine updates** correctly override an AI's active behavior.

It validates that when an external commander (HQ) or a replicated network command mutates the entity's BlackboardComponent, the active Behavior Tree instantly re-evaluates its state. It proves the AI can seamlessly switch from an aggressive posture to a defensive one (Rules of Engagement) by halting writes to the WeaponChannel and initiating new commands on the LocomotionChannel.

## **2\. Architectural Alignment**

This demo relies strictly on the actual cognitive pipeline and ECS implementation:

* **BlackboardComponent**: Acts as the authoritative source of truth for both *Internal State* (e.g., ammo, sensors) and *External Doctrine* (e.g., Rules of Engagement, current mission waypoint).  
* **Command Ingress Mocking**: In a full distributed environment, HQ commands are ghosted via FDP.Toolkit.Replication. For this isolated unit test, the IScenario script acts as the network ingress, directly mutating the Blackboard to simulate the arrival of an HQ command.  
* **Dynamic BTree Evaluation**: Proves that the Behavior Tree does not cache stale condition states. It actively checks Doctrine.RoE on every tick.  
* **Execution Boundary**: Like the other cognitive tests, this strictly monitors the ECS LocomotionChannel and WeaponChannel output. It does not load the Dispatchers or Physics toolkits.

## **3\. Scenario Setup**

### **3.1. The AI Template**

The NodeBootstrapper initializes a headless world with the BehaviorToolkit and spawns a single CommanderEntity.

* **Blackboard Setup:** \* Doctrine.RoE \= WeaponsFree (Default)  
  * Mission.TargetX \= 0.0  
  * Env.ThreatVisible \= false  
* **BTree Definition:**  
  * *Parallel Node* (Evaluating both Movement and Combat):  
    * *Movement Branch:* Condition(TargetX \!= 0\) \-\> WriteAction(LocomotionChannel, ActionIdMoveTo(TargetX))  
    * *Combat Branch (Selector):*  
      * *Sequence 1 (Engage):* Condition(ThreatVisible) AND Condition(RoE \== WeaponsFree) \-\> WriteAction(WeaponChannel, ActionIdAimAndFire)  
      * *Sequence 2 (Evade):* Condition(ThreatVisible) AND Condition(RoE \== HoldFire) \-\> WriteAction(LocomotionChannel, ActionIdEvade) \-\> ClearAction(WeaponChannel)

### **3.2. Node Bootstrapping**

Registers the BehaviorToolkit and the core Fdp.Kernel ECS. Dispatchers and Executors are explicitly excluded.

## **4\. Execution Timeline (The Test Script)**

The scenario hooks into the Node.OnTick event to inject HQ commands and sensor data into the Blackboard.

| Tick | Simulated Event (Blackboard Mutation) | Expected AI Reaction (Channel States) | Architectural Proof |
| :---- | :---- | :---- | :---- |
| **0** | Initialization (RoE \= WeaponsFree) | Channels Idle. | Baseline stability. |
| **10** | HQ: Mission.TargetX \= 100.0 | LocomotionChannel \= MoveTo WeaponChannel \= Idle | **Mission Control.** AI accepts the external waypoint and acts on it. |
| **20** | Sensor: Env.ThreatVisible \= true | LocomotionChannel \= MoveTo WeaponChannel \= AimAndFire | **Baseline Combat.** AI engages the threat because default RoE allows it. |
| **30** | HQ: Doctrine.RoE \= HoldFire | LocomotionChannel \= Evade WeaponChannel \= Idle | **Doctrine Override\!** The BTree re-evaluates the active threat under new RoE. It instantly clears the weapon channel and overrides the locomotion channel to evade. |
| **40** | Sensor: Env.ThreatVisible \= true | LocomotionChannel \= Evade WeaponChannel \= Idle | **Sustained Discipline.** A new threat appears, but the AI strictly respects the HoldFire doctrine. |

## **5\. Programmatic Assertions**

The test evaluates the ECS history at Tick 50 to prove the command chain succeeded.

public class MissionCommandScenario : IScenario  
{  
    public void Configure(NodeBootstrapper node, NodeRole role)  
    {  
        // ... (Entity Setup) ...

        node.OnTick \+= (currentTick) \=\>   
        {  
            ref var bb \= ref node.World.GetComponentRW\<BlackboardComponent\>(entity);

            if (currentTick \== 10\)   
                bb.SetFloat("Mission.TargetX", 100.0f);  
              
            else if (currentTick \== 20 || currentTick \== 40\)   
                bb.SetBool("Env.ThreatVisible", true);  
              
            else if (currentTick \== 30\)   
                bb.SetInt("Doctrine.RoE", (int)RulesOfEngagement.HoldFire);  
        };

        node.SetCompletionCondition(() \=\>   
        {  
            if (node.CurrentTick \< 50\) return false;  
            var history \= node.GetObserverHistory(entity);

            // 1\. Assert Mission Accepted (Tick 15\)  
            var tick15 \= history.GetStateAt(15);  
            bool missionAccepted \= tick15.LocomotionChannel.ActiveAction \== NavigationConstants.ActionIdMoveTo;

            // 2\. Assert Weapons Free Engagement (Tick 25\)  
            var tick25 \= history.GetStateAt(25);  
            bool engagedThreat \= tick25.WeaponChannel.ActiveAction \== CombatConstants.ActionIdAimAndFire;

            // 3\. Assert Doctrine Change / Hold Fire (Tick 35 & 45\)  
            var tick35 \= history.GetStateAt(35);  
            var tick45 \= history.GetStateAt(45);  
              
            bool heldFire \= tick35.WeaponChannel.ActiveAction \== 0 &&   
                            tick45.WeaponChannel.ActiveAction \== 0;  
              
            bool evadedUnderDoctrine \= tick35.LocomotionChannel.ActiveAction \== NavigationConstants.ActionIdEvade;

            return missionAccepted && engagedThreat && heldFire && evadedUnderDoctrine;  
        });  
    }  
}

## **6\. Value to the FDP Framework**

This test mathematically guarantees that High-Level Command logic functions perfectly within the FDP.Toolkit.Behavior module. It proves that military doctrines are not hardcoded into the BTree topology, but are dynamically evaluated variables. If an engineer accidentally caches a condition state or writes a BTree sequence that ignores the Blackboard's RoE updates, this CI test will fail immediately.

# **Specification: Fdp.Examples.AutoDrive**

## **1\. Objective**

The AutoDrive scenario is a headless, CI-focused unit test designed to mathematically prove the **Full Vehicle Locomotion Stack**.

It validates the seamless hand-off between high-level pathfinding (FDP.Toolkit.Navigation) and low-level physics (FDP.Toolkit.CarKinem). The test specifically proves that two vehicles can receive ghosted movement commands, calculate intersecting splines on a shared road network, dynamically steer around each other to avoid a head-on collision, and successfully recover to perform a precision stop at their respective destinations.

## **2\. Architectural Alignment**

This demo relies strictly on the actual FDP ECS and Replication architecture:

* **LocomotionChannel Ghosting**: The test script acts as the "Brain" by mutating the LocomotionChannel components. FDP.Toolkit.Replication synchronizes this channel to the "Muscle" node.  
* **Graph-Based Navigation**: Proves that the NavigationToolkit evaluates a mathematical node-and-edge road network (not a navmesh) to generate a static driving spline.  
* **Decoupled Avoidance**: Proves that the CarKinemToolkit uses the generated spline only as a baseline. It calculates localized dynamic avoidance (Y-axis deviation) using physical bounding boxes without needing a new route from the pathfinder.  
* **Precision Kinematics**: Asserts that braking friction correctly halts the vehicle exactly at the destination coordinate without infinitely looping or overshooting.

## **3\. Scenario Setup**

### **3.1. The Environment**

The Node initializes the NavigationToolkit with a simple mathematical mock road network: a single straight road segment from X: 0, Y: 0 to X: 100, Y: 0\.

### **3.2. The Entities**

The NodeBootstrapper creates two identical KinematicCar entities on the Muscle node.

* **Alpha (Entity 1):** Spawns at X: 0, Y: 0\. Destination: X: 100, Y: 0\.  
* **Bravo (Entity 2):** Spawns at X: 100, Y: 0\. Destination: X: 0, Y: 0\.

### **3.3. Command Injection**

At Tick 1, the test script bypasses the Brain completely and writes directly to the ECS memory:

* LocomotionChannel (Alpha): ActiveAction \= ActionIdMoveTo, Target \= (100, 0, 0\)  
* LocomotionChannel (Bravo): ActiveAction \= ActionIdMoveTo, Target \= (0, 0, 0\)

## **4\. Execution Timeline (Alpha Vehicle Focus)**

The test observes the Transform3D component updates generated by the CarKinemToolkit over time.

| Simulation Phase | Expected Behavior | Physical Proof (Mathematical Assertion) | Architectural Validation |
| :---- | :---- | :---- | :---- |
| **Phase 1: Spline Following** | Accelerates toward the center point along the straight road. | Velocity \> 0\. Y ≈ 0.0 (Variance \< 0.1) | **Path Execution.** The car tightly adheres to the generated navigational spline. |
| **Phase 2: Dynamic Evasion** | Alpha detects Bravo in its avoidance radius. It steers right. | X ≈ 50.0 **abs(Y) \> 2.0** | **Local Avoidance.** CarKinem overrides the static spline to prevent collision, utilizing lateral steering parameters. |
| **Phase 3: Route Recovery** | Conflict resolved. Alpha steers back toward the destination line. | Velocity \> 0\. Y trends back to 0.0. | **Path Recovery.** The solver dynamically recalculates the return curve to the original spline. |
| **Phase 4: Precision Stop** | Reaches X: 100 and halts completely. | **Velocity \== 0.0** X \== 100.0, Y ≈ 0.0 | **Kinematic Braking.** Deceleration curves applied correctly without overshooting the objective. |

## **5\. Programmatic Assertions**

The CI pipeline evaluates the history of the Transform3D components to guarantee no physics regressions.

public class AutoDriveScenario : IScenario  
{  
    public void Configure(NodeBootstrapper node, NodeRole role)  
    {  
        // ... (Environment and Entity Setup) ...

        // Trigger the movement at Tick 1  
        node.OnTick \+= (tick) \=\>   
        {  
            if (tick \== 1\)  
            {  
                ref var alphaChannel \= ref node.World.GetComponentRW\<LocomotionChannel\>(entityAlpha);  
                alphaChannel.ActiveAction \= NavigationConstants.ActionIdMoveTo;  
                alphaChannel.TargetX \= 100f;

                ref var bravoChannel \= ref node.World.GetComponentRW\<LocomotionChannel\>(entityBravo);  
                bravoChannel.ActiveAction \= NavigationConstants.ActionIdMoveTo;  
                bravoChannel.TargetX \= 0f;  
            }  
        };

        // Mathematical Assertions of the Physics Output  
        node.SetCompletionCondition(() \=\>   
        {  
            if (node.CurrentTick \< 150\) return false;

            var alphaHistory \= node.GetObserverHistory(entityAlpha).Transforms;  
              
            // 1\. Assert Spline Following (Early Phase)  
            var earlyPos \= alphaHistory\[20\];  
            bool followedSpline \= Math.Abs(earlyPos.Y) \< 0.5f;

            // 2\. Assert Dynamic Avoidance (Mid Phase / Collision Zone)  
            // At least one frame must have significant Y-deviation to prove steering avoidance  
            bool executedAvoidance \= alphaHistory.Any(pos \=\> pos.X \> 40f && pos.X \< 60f && Math.Abs(pos.Y) \> 2.0f);

            // 3\. Assert Precision Stop (Final Phase)  
            var finalPos \= alphaHistory.Last();  
            bool stoppedAtDestination \= Math.Abs(finalPos.X \- 100.0f) \< 0.1f && Math.Abs(finalPos.Y) \< 0.5f;  
              
            // Validate that the entity is no longer moving  
            var previousPos \= alphaHistory\[alphaHistory.Count \- 5\];  
            bool isHalted \= Math.Abs(finalPos.X \- previousPos.X) \< 0.01f;

            return followedSpline && executedAvoidance && stoppedAtDestination && isHalted;  
        });  
    }  
}

## **6\. Value to the FDP Framework**

This test protects the most delicate math in the simulation engine. Tuning vehicle mass, tire friction, or avoidance radii often breaks "precision stopping" (cars slide past the waypoint and loop back endlessly) or causes head-on collisions. This test guarantees that global graph navigation and local collision avoidance remain perfectly coupled in CI without needing human observation.

# **Specification: Fdp.Examples.ComponentDamage**

## **1\. Objective**

The ComponentDamage scenario is a headless, CI-focused unit test designed to mathematically prove the **Decoupled Damage & Partial Kill Architecture** of the FDP Engine.

It validates that physical locomotion systems (FDP.Toolkit.CarKinem) remain completely ignorant of "health" or "combat damage." Instead, it proves that an intermediary "Damage Arbiter" can safely translate a specific structural failure (e.g., an Engine block hit evaluated by FDP.Toolkit.Combat) into generic physical constraints (e.g., MaxSpeed \= 0.0). Furthermore, it proves that a Mobility Kill (Hull) does not implicitly cause a Firepower Kill (Turret) within a distributed, component-driven entity.

## **2\. Architectural Alignment**

This demo relies strictly on the FDP framework's ECS and Replication patterns to avoid domain leakage between toolkits:

* **The Combat Toolkit:** Owns the HealthComponent and sub-component bounding boxes (Engine, Tracks). It processes batched raycast hits and decrements health. It knows nothing about physics parameters.  
* **The Damage Arbiter (Translator System):** A specialized ECS system that observes HealthComponent. If EngineHealth \== 0, it mutates a generic KinematicConstraintsComponent on the entity, setting MaxSpeed \= 0.0.  
* **The CarKinem Toolkit:** Owns locomotion calculation. It reads the KinematicConstraintsComponent and obeys it. It does not know *why* the max speed changed; it simply calculates the required braking friction.  
* **Component-Level Ghosting (FDP.Toolkit.Replication):** Proves that constraints modified on the Combat node replicate seamlessly to the Muscle node, and that the WeaponChannel continues to function independently of the LocomotionChannel.

## **3\. Scenario Setup**

### **3.1. The Entities (Distributed Tank)**

The scenario leverages a multi-part vehicle structure:

* **The Hull:** Has LocomotionChannel, Transform3D (Locomotion), HealthComponent, and KinematicConstraintsComponent.  
* **The Turret:** Has WeaponChannel, Transform3D (Rotation, parented to Hull).

### **3.2. Command Injection**

The test script simulates both the "Brain" and the "Enemy":

* **Tick 1:** Mutates the Hull's LocomotionChannel to ActionIdMoveTo (driving North).  
* **Tick 20:** Simulates an incoming high-velocity projectile using Fdp.Kernel command buffers, specifically intersecting the Hull's "Engine" bounding box.  
* **Tick 40:** Mutates the Turret's WeaponChannel to ActionIdAimAndFire (simulating the gunner engaging a threat).

## **4\. Execution Timeline**

The test observes the state of the ECS components as they interact across the separated toolkits.

| Simulation Phase | Tick | Event / Component Mutation | Expected Behavior | Architectural Proof |
| :---- | :---- | :---- | :---- | :---- |
| **Phase 1: Locomotion** | 10 | LocomotionChannel is active. | Transform3D.Y increases. Velocity ≈ 20.0. | Base kinematics functioning. |
| **Phase 2: The Hit** | 20 | Projectile hits Engine bounding box. | HealthComponent.EngineHealth drops to 0\. | Combat sub-component hit detection works. |
| **Phase 3: Arbitration** | 21 | Arbiter evaluates HealthComponent. | KinematicConstraints.MaxSpeed is set to 0.0. | **Decoupling Proof.** Specific damage translated into a generic constraint. |
| **Phase 4: Mobility Kill** | 25 | CarKinem evaluates constraints. | CarKinem applies braking. Velocity reaches 0.0. | CarKinem reacts to external parameters without knowing it was "damaged." |
| **Phase 5: Firepower Alive** | 40 | WeaponChannel is activated. | Turret rotation continues; ActionDispatchModule fires the weapon executor. | **Partial Kill Proof.** Distributed channels remain strictly isolated. The dead Hull did not block the live Turret. |

## **5\. Programmatic Assertions**

The CI pipeline evaluates the history of the ECS components at the end of the run to mathematically guarantee the architectural boundaries held up.

public class ComponentDamageScenario : IScenario  
{  
    public void Configure(NodeBootstrapper node, NodeRole role)  
    {  
        // ... (Environment and Entity Setup for Hull and Turret) ...

        // Inject the simulated events  
        node.OnTick \+= (currentTick) \=\>   
        {  
            if (currentTick \== 1\) // Start driving  
            {  
                ref var loco \= ref node.World.GetComponentRW\<LocomotionChannel\>(hullEntity);  
                loco.ActiveAction \= NavigationConstants.ActionIdMoveTo;  
            }  
            if (currentTick \== 20\) // Sniper hits the engine block specifically  
            {  
                InjectMockProjectileHit(target: hullEntity, hitBox: "Engine");  
            }  
            if (currentTick \== 40\) // Gunner decides to shoot back  
            {  
                ref var weapon \= ref node.World.GetComponentRW\<WeaponChannel\>(turretEntity);  
                weapon.ActiveAction \= CombatConstants.ActionIdAimAndFire;  
            }  
        };

        // Assertions  
        node.SetCompletionCondition(() \=\>   
        {  
            if (node.CurrentTick \< 50\) return false;

            var hullHistory \= node.GetObserverHistory(hullEntity);  
            var turretHistory \= node.GetObserverHistory(turretEntity);

            // 1\. Assert Arbitration: The Damage system successfully updated the generic constraint  
            var tick22Hull \= hullHistory.GetStateAt(22);  
            bool constraintsUpdated \= tick22Hull.KinematicConstraints.MaxSpeed \== 0.0f;

            // 2\. Assert Mobility Kill: CarKinem obeyed the constraint and braked smoothly  
            var tick15Velocity \= CalculateVelocity(hullHistory, 14, 15);  
            var tick35Velocity \= CalculateVelocity(hullHistory, 34, 35);  
            bool wasMoving \= tick15Velocity \> 0.0f;  
            bool stoppedMoving \= tick35Velocity \< 0.01f;

            // 3\. Assert Firepower Isolation: Turret executor fired despite Hull being disabled  
            var tick45Turret \= turretHistory.GetStateAt(45);  
            bool turretFired \= tick45Turret.WeaponChannel.ActionInstanceId \> 0; // Proves executor advanced

            return constraintsUpdated && wasMoving && stoppedMoving && turretFired;  
        });  
    }

    private float CalculateVelocity(EntityObserverHistory history, int tickA, int tickB)  
    {  
        var posA \= history.GetStateAt(tickA).Transform;  
        var posB \= history.GetStateAt(tickB).Transform;  
        return Vector3.Distance(posA.Position, posB.Position);  
    }  
}

## **6\. Value to the FDP Framework**

By mandating a strict decoupling between CarKinem and Combat, this architecture ensures immense reusability. The KinematicConstraintsComponent updated by the Damage Arbiter is the exact same component that the Brain can update to enforce a "Cautious Driving" doctrine, or that the Environment node can update to simulate driving through deep mud.

This test guarantees that CarKinem never devolves into spaghetti code filled with if (entity.IsDamaged) statements, maintaining a pristine, single-responsibility physics solver.

# **Specification: Fdp.Examples.BallisticsAndHit**

## **1\. Objective**

The BallisticsAndHit scenario is a headless, CI-focused unit test designed to mathematically prove **Structural Engine Safety and Continuous Collision Detection (CCD)**.

It validates that high-speed projectiles are correctly resolved using iterative batched raycasts (FDP.Toolkit.Physics) rather than naive overlap spheres, preventing "tunneling" (where a projectile skips entirely over a target in a single frame). Furthermore, it proves that the engine strictly utilizes Fdp.Kernel Command Buffers via FDP.Toolkit.Lifecycle to safely spawn and destroy entities without corrupting the active ECS iteration loop.

## **2\. Architectural Alignment**

This demo strictly enforces the use of the actual physics and lifecycle plumbing:

* **The Dispatcher (ActionDispatchModule):** Reads the WeaponChannel and triggers the combat executor.  
* **Structural Safety (Fdp.Kernel Command Buffers):** The executor does not instantly spawn the projectile. It queues a spawn command. FDP.Toolkit.Lifecycle processes this command at the end of the frame to prevent ECS structural mutations during iteration.  
* **Iterative Batched Raycasts (FDP.Toolkit.Physics):** Calculates the projectile's PreviousFramePosition to CurrentFramePosition and queues a line-segment raycast to the physics broadphase, ensuring collisions are caught *between* ticks.  
* **Lifecycle Despawn:** Upon a successful hit response, the projectile is not instantly deleted. A destruction command is queued to the Command Buffer, ensuring clean memory management.

## **3\. Scenario Setup**

### **3.1. The Environment & Target**

The NodeBootstrapper initializes a single headless node with Physics, Combat, and Lifecycle toolkits.

* **Target Entity:** Spawns at X: 100, Y: 0, Z: 0\. It has a HealthComponent and a PhysicsCollider with a radius of 5.0 (spanning X:95 to X:105).

### **3.2. Command Injection**

At Tick 1, the test script artificially mutates a "Shooter" entity's WeaponChannel to ActionIdAimAndFire, aimed directly along the X-axis toward the target.

### **3.3. The Tunneling Threat**

The projectile is configured with a muzzle velocity of **40.0 units per tick**. Because the target is only 10 units wide, a standard position check at Tick 3 (X=80) and Tick 4 (X=120) would completely miss the target. The physics engine *must* raycast the 80 \-\> 120 segment to detect the hit at X: 95\.

## **4\. Execution Timeline**

The test observes the state of the ECS, the Command Buffers, and the resulting transforms.

| Simulation Phase | Tick | Physics Engine Action | Expected State / Output | Architectural Proof |
| :---- | :---- | :---- | :---- | :---- |
| **Phase 1: Firing** | 1 | WeaponChannel read. Executor writes Spawn command to Fdp.Kernel buffer. | Command Buffer size \> 0\. Projectile does not exist yet. | **Structural Safety.** Entities are not spawned mid-iteration. |
| **Phase 2: Flight** | 2 | Lifecycle spawned projectile. Moves 0 \-\> 40\. Queues raycast. | Transform3D published at X: 40\. No hit. | Iterative trajectory math. |
| **Phase 3: Flight** | 3 | Moves 40 \-\> 80\. Queues raycast 40 \-\> 80\. | Transform3D published at X: 80\. No hit. | Continuous flight. |
| **Phase 4: Impact** | 4 | Calculates X: 120\. Queues raycast 80 \-\> 120\. Raycast returns intersection at X: 95\. | Target Health decrements. Destroy command written to buffer. | **Batched Raycast CCD.** Tunneling was successfully prevented by the physics solver. |
| **Phase 5: Resolution** | 5 | Lifecycle flushes buffer, destroying the projectile. | Projectile entity no longer exists. No Transform3D at X: 120\. | **Memory Safety.** The projectile was cleanly destroyed. |

## **5\. Programmatic Assertions**

The CI pipeline evaluates the history of the ECS components and Lifecycle events to guarantee engine integrity.

public class BallisticsAndHitScenario : IScenario  
{  
    public void Configure(NodeBootstrapper node, NodeRole role)  
    {  
        // 1\. Spawn Target  
        var targetEntity \= node.World.CreateEntity(TemplateBuilder.Get(  
            TemplateIds.CombatTarget, startX: 100f, startY: 0f, startZ: 0f, colliderRadius: 5.0f));

        var shooterEntity \= node.World.CreateEntity();  
        node.World.AddComponent(shooterEntity, new WeaponChannel());

        // 2\. Inject Firing Event  
        node.OnTick \+= (currentTick) \=\>   
        {  
            if (currentTick \== 1\)  
            {  
                ref var weapon \= ref node.World.GetComponentRW\<WeaponChannel\>(shooterEntity);  
                weapon.ActiveAction \= CombatConstants.ActionIdAimAndFire;  
                // Assume weapon template configures 40 units/tick velocity  
            }  
        };

        // 3\. Mathematical Assertions  
        node.SetCompletionCondition(() \=\>   
        {  
            if (node.CurrentTick \< 10\) return false;

            var history \= node.GetObserverHistory();  
            var lifecycleEvents \= history.GetLifecycleEvents();

            // Find the dynamically spawned projectile ID  
            var projSpawn \= lifecycleEvents.FirstOrDefault(l \=\> l.State \== LifecycleState.Spawned && l.Type \== EntityTypes.Projectile);  
            if (projSpawn \== null) return false;  
              
            int projId \= projSpawn.EntityId;  
            var projTransforms \= history.GetTransformsForEntity(projId);  
            var hitEvents \= history.GetHitNotifications();

            // ASSERTION 1: Command Buffer Delayed Spawn  
            // Proves it wasn't spawned instantly on Tick 1  
            bool spawnedOnTick2 \= projSpawn.Tick \== 2;

            // ASSERTION 2: Batched Raycast CCD (Anti-Tunneling)  
            // The hit MUST occur. It should hit the edge of the collider at X: 95\.  
            bool hitDetectedCorrectly \= hitEvents.Any(h \=\> h.TargetId \== targetEntity.Id && h.ImpactLocationX \== 95.0f);

            // ASSERTION 3: Projectile Trajectory Truncation  
            // The projectile must not publish any transforms past the impact point (X: 120 should never exist)  
            bool tunnelingPrevented \= \!projTransforms.Any(p \=\> p.Position.X \>= 100.0f);

            // ASSERTION 4: Lifecycle Despawn  
            bool destroyEventPublished \= lifecycleEvents.Any(l \=\> l.EntityId \== projId && l.State \== LifecycleState.Destroyed);

            return spawnedOnTick2 && hitDetectedCorrectly && tunnelingPrevented && destroyEventPublished;  
        });  
    }  
}

## **6\. Value to the FDP Framework**

This test is essential for engine stability. If an engineer attempts to "optimize" the physics engine by removing the continuous raycast logic or bypassing the Fdp.Kernel command buffers to instantiate entities synchronously, this test will fail immediately. It mathematically guarantees that high-speed projectiles will reliably hit thin targets, maintaining combat accuracy regardless of the simulation's tick rate.

# **Specification: Fdp.Examples.DistributedTank**

## **1\. Objective**

The DistributedTank scenario is a headless, CI-focused unit test designed to mathematically prove **Component-Level Network Authority and Hierarchical Ghosting**.

It validates the "Split Brain / Distributed Muscle" paradigm. It proves that a multi-part entity (a Tank composed of a Hull and a Turret) can have its cognitive decision-making and physical simulation strictly divided across four separate network nodes. It guarantees that FDP.Toolkit.Replication correctly synchronizes Channel components downwards and Transform3D components upwards, maintaining perfect physical cohesion of the parent-child relationship over the DDS network.

## **2\. Architectural Alignment**

This demo strictly enforces the CQRS (Command Query Responsibility Segregation) boundaries of the FDP toolkits via the Replication system:

* **Separation of Write Authority:** No single node "owns" the tank. Authority is fragmented by ECS component.  
* **Brain Nodes (Brain\_Driver, Brain\_Gunner):** Possess write authority *only* over the LocomotionChannel and WeaponChannel. They possess read-only ghost copies of Transform3D.  
* **Muscle Nodes (Muscle\_Hull, Muscle\_Turret):** Possess write authority *only* over Transform3D. They possess read-only ghost copies of the Channels.  
* **Networked Hierarchy:** The Turret entity has a ParentEntity component pointing to the Hull. Muscle\_Turret must read the ghosted Hull Transform3D over the network to know where its base is located before calculating its own relative rotation and world position.

## **3\. Scenario Setup**

### **3.1. Node Topology (Host of Hosts)**

The NodeBootstrapper spins up four completely isolated ECS worlds communicating strictly via DDS loopback:

1. **Brain\_Driver**: Spawns Hull Entity (owns LocomotionChannel).  
2. **Brain\_Gunner**: Spawns Turret Entity (owns WeaponChannel, parented to Hull).  
3. **Muscle\_Hull**: Loads CarKinem. Receives ghosted Hull.  
4. **Muscle\_Turret**: Loads rotation logic. Receives ghosted Hull and Turret.

### **3.2. Command Injection (The "Fire on the Move" Test)**

To prove the nodes don't block each other, the test forces them to act simultaneously:

* **Tick 1 (Brain\_Driver):** Mutates Hull's LocomotionChannel to drive North.  
* **Tick 20 (Brain\_Gunner):** Mutates Turret's WeaponChannel to aim East and fire.

## **4\. Execution Timeline**

The test monitors the DDS replication streams to ensure the disconnected systems act as a unified vehicle.

| Simulation Phase | Tick | Event | Expected State / Output | Architectural Proof |
| :---- | :---- | :---- | :---- | :---- |
| **Phase 1: Movement** | 1 | Driver writes ActionIdMoveTo. | Muscle\_Hull receives ghost channel, triggers executor, Transform3D.Y increases. | **Downward Replication.** Intent successfully traversed the network to the physics solver. |
| **Phase 2: Hierarchy Sync** | 10 | Hull moves. | Muscle\_Turret reads ghosted Hull Transform3D. Updates Turret Transform3D.Y to match. | **Parent-Child Ghosting.** The child physics node successfully tracked the parent physics node over DDS. |
| **Phase 3: Split Brain** | 20 | Gunner writes ActionIdAimAndFire. | Muscle\_Turret receives ghost channel, begins rotating Turret Yaw to 90 degrees. | **Independent Dispatch.** The Gunner's network traffic did not interrupt the Driver's network traffic. |
| **Phase 4: Cohesive Action** | 40 | Rotation complete. | Hull is still moving North (Yaw=0). Turret has rotated East (Yaw=90) and fires. | **Distributed Muscle.** Two isolated physics solvers concurrently updated a single hierarchical object flawlessly. |

## **5\. Programmatic Assertions**

The CI pipeline evaluates the history of the ECS components by looking at the resulting ghost states on a purely passive Observer node (or evaluating the local history on the Muscles).

public class DistributedTankScenario : IScenario  
{  
    public void Configure(NodeBootstrapper node, NodeRole role)  
    {  
        // 1\. Distribute Entity Authority based on Role  
        if (role \== NodeRole.Brain\_Driver)  
            node.World.CreateEntity(TemplateBuilder.Get(TemplateIds.DriverBrain, hullId: 100));  
        else if (role \== NodeRole.Brain\_Gunner)  
            node.World.CreateEntity(TemplateBuilder.Get(TemplateIds.GunnerBrain, turretId: 101, parentId: 100));  
        else if (role \== NodeRole.Muscle\_Hull)  
            node.RegisterToolkit\<CarKinemToolkit\>();  
        else if (role \== NodeRole.Muscle\_Turret)  
            node.RegisterToolkit\<TurretKinemToolkit\>();

        // 2\. Inject independent triggers  
        node.OnTick \+= (currentTick) \=\>   
        {  
            if (role \== NodeRole.Brain\_Driver && currentTick \== 1\)  
            {  
                ref var loco \= ref node.World.GetComponentRW\<LocomotionChannel\>(100);  
                loco.ActiveAction \= NavigationConstants.ActionIdMoveTo;  
                loco.TargetY \= 500f;  
            }  
                  
            if (role \== NodeRole.Brain\_Gunner && currentTick \== 20\)  
            {  
                ref var weapon \= ref node.World.GetComponentRW\<WeaponChannel\>(101);  
                weapon.ActiveAction \= CombatConstants.ActionIdAimAndFire;  
                weapon.TargetX \= 500f; // Aim East  
            }  
        };

        // 3\. Mathematical Proof of Distributed Cohesion  
        node.SetCompletionCondition(() \=\>   
        {  
            if (node.CurrentTick \< 50\) return false;

            var history \= node.GetObserverHistory();  
            var tick40Hull \= history.GetStateAt(40, entityId: 100).Transform;  
            var tick40Turret \= history.GetStateAt(40, entityId: 101).Transform;

            // ASSERTION 1: Networked Hierarchical Attachment  
            // The turret must physically follow the hull's translation exactly (plus vertical Z-offset).  
            // If Replication latency caused the Turret to read a stale Hull position, this will fail.  
            bool attachmentMaintained \= Math.Abs(tick40Hull.Position.X \- tick40Turret.Position.X) \< 0.01f &&   
                                        Math.Abs(tick40Hull.Position.Y \- tick40Turret.Position.Y) \< 0.01f;

            // ASSERTION 2: Distributed Muscle Separation  
            // Hull must still be facing North (Yaw 0), while Turret must be facing East (Yaw 90).  
            bool splitMuscleWorked \= tick40Hull.Rotation.Yaw \== 0.0f && Math.Abs(tick40Turret.Rotation.Yaw \- 90.0f) \< 1.0f;

            // ASSERTION 3: Split Brain Execution  
            // The Gunner successfully triggered the weapon executor on the Turret Muscle node.  
            var tick45Turret \= history.GetStateAt(45, entityId: 101);  
            bool gunnerFired \= tick45Turret.WeaponChannel.ActionInstanceId \> 0;

            return attachmentMaintained && splitMuscleWorked && gunnerFired;  
        });  
    }  
}

## **6\. Value to the FDP Framework**

This test is the ultimate benchmark for the FDP.Toolkit.Replication system. Ghosting single entities is easy; ghosting hierarchical entities whose physics are solved on different machines is incredibly difficult due to tick-sync issues. If this test passes, it guarantees that your SST descriptors, ghost parenting logic, and DDS latency compensation are rock-solid, proving the FDP engine is fully ready for massive, multi-crew distributed simulation.

# **Specification: Fdp.Examples.SensorGrid**

## **1\. Objective**

The SensorGrid scenario is a headless, CI-focused unit test designed to mathematically prove **Sensor Broadphase and Environmental Occlusion (Line of Sight)**.

It validates the FDP.Toolkit.Perception module's ability to efficiently process spatial data using FDP.Toolkit.Physics. It proves that an observer entity can detect a target entering its sensor radius (broadphase), correctly lose track of the target when a static object blocks the line of sight (batched raycast occlusion), and re-acquire the target once it emerges, all without requiring a GPU or active rendering context.

## **2\. Architectural Alignment**

This demo strictly enforces the separation of physical reality from cognitive perception:

* **Ghost Observation (FDP.Toolkit.Replication):** Sensors do not query remote nodes. They run on a local node (like an IG or Environment server) and observe the ghosted Transform3D components of remote entities.  
* **Broadphase Filtering (FDP.Toolkit.Physics):** Instead of checking every entity against every other entity (![][image1]), the sensor first queries the physics broadphase tree (e.g., a spatial hash or BVH) to find entities within its maximum range.  
* **Batched Raycasting:** For entities that pass the broadphase radius check, the sensor queues batched raycasts from the Observer's Transform3D to the Target's Transform3D.  
* **Perception State Output:** The PerceptionToolkit does not make combat decisions. It simply writes the results of the raycasts to the Observer's PerceptionComponent (updating the list of VisibleTargetIds) or mutates the BlackboardComponent.

## **3\. Scenario Setup**

### **3.1. The Environment (The Grid)**

The NodeBootstrapper initializes a headless node with the Physics and Perception toolkits.

* **The Occluder (Wall):** A static physics entity spawned at X: 50, Y: 50 with a large box collider designed to block the middle of the grid.

### **3.2. The Entities**

* **The Observer:** Spawns at X: 0, Y: 0\. It possesses a VisionSensorComponent with a range of 200.0.  
* **The Target:** Spawns at X: 100, Y: 0\. It moves steadily North (along the Y-axis) towards X: 100, Y: 100\.

### **3.3. Command Injection**

To purely test the sensor logic, we completely exclude the CarKinem toolkit. The test script artificially mutates the Target's Transform3D.Y by 1.0 every tick, creating a perfect, deterministic trajectory that will cross behind the Wall.

## **4\. Execution Timeline**

The test monitors the Observer's PerceptionComponent as the Target moves across the grid and behind the occluder.

| Simulation Phase | Tick | Target Position | Physics/Perception Action | Expected State | Architectural Proof |
| :---- | :---- | :---- | :---- | :---- | :---- |
| **Phase 1: Clear LoS** | 10 | X: 100, Y: 10 | Broadphase: Target in range. Raycast: No hit. | Target ID present in VisibleTargetIds. | **Base Perception.** Sensor correctly identifies an entity within range. |
| **Phase 2: Occlusion** | 50 | X: 100, Y: 50 | Broadphase: Target in range. Raycast: Hits the Wall at X: 50\. | Target ID **removed** from VisibleTargetIds. | **Raycast Occlusion.** The physics module successfully blocked the sensor's line of sight. AI cannot cheat. |
| **Phase 3: Re-emergence** | 90 | X: 100, Y: 90 | Broadphase: Target in range. Raycast: No hit. | Target ID **restored** to VisibleTargetIds. | **Continuous Evaluation.** The sensor dynamically re-acquired the target once the raycast cleared the wall collider. |

## **5\. Programmatic Assertions**

The CI pipeline evaluates the history of the Observer's perception state to mathematically guarantee that Line of Sight constraints are respected.

public class SensorGridScenario : IScenario  
{  
    public void Configure(NodeBootstrapper node, NodeRole role)  
    {  
        // 1\. Initialize Toolkits  
        node.RegisterToolkit\<PhysicsToolkit\>();  
        node.RegisterToolkit\<PerceptionToolkit\>();

        // 2\. Spawn Entities  
        var observer \= node.World.CreateEntity(TemplateBuilder.Get(TemplateIds.Observer, startX: 0f, startY: 0f));  
        var target \= node.World.CreateEntity(TemplateBuilder.Get(TemplateIds.KinematicCar, startX: 100f, startY: 0f));  
        var wall \= node.World.CreateEntity(TemplateBuilder.Get(TemplateIds.ConcreteWall, startX: 50f, startY: 50f));

        // 3\. Inject deterministic movement to bypass CarKinem  
        node.OnTick \+= (currentTick) \=\>   
        {  
            ref var targetTransform \= ref node.World.GetComponentRW\<Transform3D\>(target);  
            targetTransform.Position.Y \= currentTick \* 1.0f; // Moves 1 unit per tick North  
        };

        // 4\. Mathematical Assertions  
        node.SetCompletionCondition(() \=\>   
        {  
            if (node.CurrentTick \< 100\) return false;

            var history \= node.GetObserverHistory(observer);

            // Phase 1: Clear Line of Sight (Tick 10\)  
            var tick10Perception \= history.GetStateAt(10).Perception;  
            bool detectedInitially \= tick10Perception.VisibleTargetIds.Contains(target.Id);

            // Phase 2: Complete Occlusion (Tick 50\)  
            // The ray from (0,0) to (100,50) passes directly through the wall at (50,50)  
            var tick50Perception \= history.GetStateAt(50).Perception;  
            bool successfullyOccluded \= \!tick50Perception.VisibleTargetIds.Contains(target.Id);

            // Phase 3: Re-emergence (Tick 90\)  
            // The target is at (100,90), the ray from (0,0) passes behind the wall.  
            var tick90Perception \= history.GetStateAt(90).Perception;  
            bool reacquired \= tick90Perception.VisibleTargetIds.Contains(target.Id);

            return detectedInitially && successfullyOccluded && reacquired;  
        });  
    }  
}

## **6\. Value to the FDP Framework**

This unit test prevents "AI Omniscience"—a common simulation bug where agents shoot at enemies through buildings because a developer accidentally broke the raycast masking logic. By enforcing this mathematically in a headless CI environment, you guarantee that FDP.Toolkit.Perception and FDP.Toolkit.Physics remain perfectly synchronized, ensuring that stealth and cover mechanics function reliably across the distributed network.

# **Specification: Fdp.Examples.PredictiveClamping**

## **1\. Objective**

The PredictiveClamping scenario is a headless, CI-focused unit test designed to mathematically prove **Terrain Adaptation, Z-Height Smoothing, and Look-Ahead Pitch**.

It validates the TerrainClampingSystem within the FDP.Toolkit.Geographic (or Physics) module. When 2D pathfinding and kinematics move a vehicle along the X/Y plane, the terrain clamping system must query the Geographic toolkit for elevation data. To prevent visual stuttering or physics "snapping" when hitting a steep slope, this test proves the system samples the terrain *ahead* of the vehicle using its velocity vector, smoothly interpolating the vehicle's Z-axis (height) and Pitch *before* the center of mass physically reaches the incline.

## **2\. Architectural Alignment**

This demo strictly relies on the decoupled data architecture of the FDP engine:

* **Separation of Kinematics and Elevation:** The CarKinemToolkit solves the X/Y planar movement. It does not know about the 3D terrain mesh.  
* **GeographicToolkit Queries:** The TerrainClampingSystem reads the entity's Transform3D (specifically Position and Velocity), queries the GeographicToolkit's elevation database (e.g., WGS84 DTED or local mock terrain), and writes back to Transform3D.Z, Pitch, and Roll.  
* **The TerrainClampComponent:** An ECS component attached to the entity that holds the tuning parameters: LookAheadDistance (e.g., 5.0 meters) and SmoothingAlpha (e.g., 0.1 for lerp).  
* **Headless Evaluation:** The CI test mathematically defines a mock terrain function and asserts that the resulting Transform history creates a smooth curve rather than a rigid step-function.

## **3\. Scenario Setup**

### **3.1. The Mock Environment (The Ramp)**

The NodeBootstrapper initializes the GeographicToolkit with a mocked synthetic elevation provider:

* **Flat Ground:** From X: 0 to X: 50, Elevation Z \= 0.0.  
* **The Ramp:** From X: 50 to X: 100, Elevation slopes up linearly to Z \= 50.0 (a 45-degree incline).  
* **The Plateau:** From X: 100 onwards, Elevation Z \= 50.0.

### **3.2. The Entity**

* **The Vehicle:** Spawns at X: 0, Y: 0, Z: 0\.  
* **Components:** Possesses Transform3D and TerrainClampComponent (configured with a 5.0 unit look-ahead distance).  
* **Movement:** To isolate the clamping logic, CarKinem is bypassed. The test script forces a constant velocity of 1.0 unit per tick along the positive X-axis.

## **4\. Execution Timeline**

The test observes how the entity's Z-height and Pitch react to the upcoming terrain topology.

| Simulation Phase | Tick | Entity X Pos | Terrain Z at X | Expected Entity Z & Pitch | Architectural Proof |
| :---- | :---- | :---- | :---- | :---- | :---- |
| **Phase 1: Flat Run** | 10 | X: 10 | 0.0 | Z: 0.0, Pitch: 0.0 | Base clamping works. |
| **Phase 2: Prediction** | 46 | X: 46 | 0.0 | Z \> 0.01, Pitch \> 0.0 | **Look-Ahead Works.** Even though the ground under the vehicle is flat, the 5-unit look-ahead ray hit the ramp at X:51. The vehicle starts pitching up pre-emptively. |
| **Phase 3: The Climb** | 75 | X: 75 | 25.0 | Z ≈ 25.0, Pitch ≈ 45.0 | Continuous adaptation. |
| **Phase 4: Smoothing** | 102 | X: 102 | 50.0 | Z ≈ 49.8, Pitch \< 45.0 | **Smoothing Works.** The entity passed the ramp crest at X:100. Instead of instantly snapping flat, the smoothing algorithm gradually levels the suspension out. |

## **5\. Programmatic Assertions**

The CI pipeline evaluates the entity's transform history to guarantee the curve is mathematically smooth and predictive.

public class PredictiveClampingScenario : IScenario  
{  
    public void Configure(NodeBootstrapper node, NodeRole role)  
    {  
        // 1\. Initialize Toolkits with Mock Terrain  
        node.RegisterToolkit\<GeographicToolkit\>(new MockElevationConfig {  
            RampStartX \= 50.0f, RampEndX \= 100.0f, MaxZ \= 50.0f  
        });  
        node.RegisterToolkit\<TerrainClampingToolkit\>();

        // 2\. Spawn Entity  
        var entity \= node.World.CreateEntity();  
        node.World.AddComponent(entity, new Transform3D { Position \= new Vector3(0, 0, 0\) });  
        node.World.AddComponent(entity, new TerrainClampComponent {   
            LookAheadDistance \= 5.0f,   
            InterpolationRate \= 0.2f   
        });

        // 3\. Inject deterministic horizontal movement (1 unit/tick)  
        node.OnTick \+= (currentTick) \=\>   
        {  
            ref var transform \= ref node.World.GetComponentRW\<Transform3D\>(entity);  
            transform.Position.X \= currentTick \* 1.0f; // Force move  
        };

        // 4\. Mathematical Assertions  
        node.SetCompletionCondition(() \=\>   
        {  
            if (node.CurrentTick \< 120\) return false;

            var history \= node.GetObserverHistory(entity);

            // ASSERTION 1: Predictive Pitching (Tick 48, X=48)  
            // The ground at X=48 is completely flat (Z=0).   
            // Because LookAhead \= 5, the sensor sees the ramp at X=53.  
            // The vehicle must have a positive Pitch before it physically hits the ramp.  
            var tick48State \= history.GetStateAt(48).Transform;  
            bool predictedRamp \= tick48State.Position.Z \> 0.001f && tick48State.Rotation.Pitch \> 0.0f;

            // ASSERTION 2: Slope Climbing (Tick 75, X=75)  
            // The vehicle should be smoothly climbing, matching the \~25.0 Z-height.  
            var tick75State \= history.GetStateAt(75).Transform;  
            bool isClimbing \= Math.Abs(tick75State.Position.Z \- 25.0f) \< 1.0f && tick75State.Rotation.Pitch \> 40.0f;

            // ASSERTION 3: Smoothing / Cresting the Hill (Tick 102, X=102)  
            // Ground is flat at Z=50. The vehicle shouldn't snap to Pitch 0 instantly.  
            // It should be interpolating back down to 0\.  
            var tick102State \= history.GetStateAt(102).Transform;  
            bool smoothedCrest \= tick102State.Rotation.Pitch \> 0.0f && tick102State.Rotation.Pitch \< 45.0f;

            return predictedRamp && isClimbing && smoothedCrest;  
        });  
    }  
}

## **6\. Value to the FDP Framework**

This test is critical for the Visual and Physics fidelity of the engine. In a networked environment where ghost updates might arrive at 10Hz, updating a vehicle's Z-height and Pitch without smoothing creates unacceptable jitter that ruins the visual experience in an IG (Image Generator) or Vis2D observer. By mathematically asserting the look-ahead and lerp behaviors in a headless environment, you guarantee that ground-clamping regressions (like vehicles clipping into terrain or snapping violently on network ticks) are caught immediately in the CI pipeline.

# **Specification: Fdp.Examples.HumanOverride**

## **1\. Objective**

The HumanOverride scenario is a headless, CI-focused unit test designed to mathematically prove **Vis2D Tooling, Coordinate Projection, and AI Interruption**.

It validates that "Human-in-the-Loop" interactions—specifically using Vis2D tools like the LocationPickerTool or StandardInteractionTool—can safely translate screen-space pixel coordinates into world-space Cartesian coordinates. Furthermore, it proves that when these tools mutate an entity's BlackboardComponent or LocomotionChannel, the active Behavior Tree gracefully accepts the override, halting its current autonomous task to obey the human operator.

## **2\. Architectural Alignment**

This demo strictly enforces the boundaries between UI, Data, and Execution:

* **Headless UI Testing:** A UI tool does not inherently require a rendered window to be tested. The test instantiates the LocationPickerTool and feeds it a mocked Viewport (camera projection matrix) and mocked mouse click events.  
* **Coordinate Translation:** Proves the math inside Vis2D correctly projects a 2D screen click (e.g., X: 800, Y: 600 on a 1080p monitor) into the correct mathematical World Space (e.g., X: 150.0, Y: 200.0).  
* **Safe State Mutation:** The tool does not directly call CarKinem.DriveTo(). It adheres to the engine's CQRS design by mutating the BlackboardComponent (acting as an HQ command) or writing directly to the LocomotionChannel.  
* **Behavior Tree Reactivity:** Proves that the AI's cognitive layer yields to these external data mutations dynamically.

## **3\. Scenario Setup**

### **3.1. The Environment & Tooling**

The NodeBootstrapper initializes a headless node with the BehaviorToolkit and the Vis2DToolkit.

* **The Viewport:** A mocked Vis2D Camera is set up looking at the origin, with a known zoom level and orthographic projection matrix.  
* **The Tool:** The LocationPickerTool is instantiated and set as the active interaction context.

### **3.2. The Entity**

* **The AI Agent:** A CommanderEntity spawned at X: 0, Y: 0\.  
* It is actively executing a patrol Behavior Tree, currently driving towards X: 50.0, Y: 0.0.

### **3.3. Command Injection (The "Click")**

At Tick 20, the test script bypasses the physical mouse and directly invokes LocationPickerTool.OnMouseClick(screenX: 960, screenY: 540, button: RightClick, selectedEntity: AgentId).

* *Note: With the mocked camera centered on (100, 100\) and specific zoom, this exact pixel maps mathematically to World Space X: 100.0, Y: 100.0.*

## **4\. Execution Timeline**

The test monitors the mathematical translation of the UI input and the resulting ECS channel output.

| Simulation Phase | Tick | Event | Expected State / Output | Architectural Proof |
| :---- | :---- | :---- | :---- | :---- |
| **Phase 1: Autonomous Patrol** | 10 | AI executing standard BTree. | LocomotionChannel.Target is X: 50.0. | Base cognitive state is stable. |
| **Phase 2: Human Interaction** | 20 | Simulated Right-Click at Screen (960, 540). | Tool translates pixel to World (100.0, 100.0). | **Projection Math.** The tool's screen-to-world matrix is mathematically correct. |
| **Phase 3: ECS Mutation** | 21 | Tool writes to ECS. | BlackboardComponent updated with Mission.TargetX \= 100.0, Mission.TargetY \= 100.0. | **Safe UI Routing.** The UI tool wrote to data components, not direct execution methods. |
| **Phase 4: AI Override** | 25 | BTree re-evaluates Blackboard. | LocomotionChannel.Target shifts to X: 100.0, Y: 100.0. | **Human-in-the-Loop.** The autonomous AI successfully aborted its patrol to obey the human command. |

## **5\. Programmatic Assertions**

The CI pipeline evaluates the history of the ECS components to guarantee the UI layer successfully puppeted the AI layer.

public class HumanOverrideScenario : IScenario  
{  
    public void Configure(NodeBootstrapper node, NodeRole role)  
    {  
        node.RegisterToolkit\<BehaviorToolkit\>();  
        node.RegisterToolkit\<Vis2DToolkit\>();

        // Spawn Autonomous Entity  
        var agent \= node.World.CreateEntity(TemplateBuilder.Get(TemplateIds.CommanderBrain));  
          
        // Give it an initial autonomous waypoint  
        ref var bb \= ref node.World.GetComponentRW\<BlackboardComponent\>(agent);  
        bb.SetFloat(DoctrineKeys.MissionTargetX, 50.0f);  
        bb.SetFloat(DoctrineKeys.MissionTargetY, 0.0f);

        // Inject the simulated human interaction  
        node.OnTick \+= (currentTick) \=\>   
        {  
            if (currentTick \== 20\)  
            {  
                // Retrieve the active tool from the Vis2D context  
                var visContext \= node.GetModule\<Vis2DToolkit\>().GetContext();  
                var activeTool \= visContext.GetActiveTool\<LocationPickerTool\>();

                // Mock a camera looking at (100, 100\)  
                var mockCamera \= new MockOrthographicCamera(focusX: 100f, focusY: 100f, zoom: 1.0f);

                // Simulate a human right-clicking exactly in the center of a 1920x1080 screen  
                activeTool.SimulateClick(  
                    screenX: 960,   
                    screenY: 540,   
                    camera: mockCamera,   
                    targetEntityId: agent.Id  
                );  
            }  
        };

        // Mathematical Assertions  
        node.SetCompletionCondition(() \=\>   
        {  
            if (node.CurrentTick \< 30\) return false;

            var history \= node.GetObserverHistory(agent);

            // ASSERTION 1: Phase 1 (Autonomous Patrol)  
            var tick15 \= history.GetStateAt(15).LocomotionChannel;  
            bool wasPatrolling \= tick15.TargetX \== 50.0f;

            // ASSERTION 2: Phase 4 (Human Override)  
            // The AI must have processed the Blackboard mutation and written the new   
            // translated screen coordinates (100.0, 100.0) to its Locomotion Channel.  
            var tick25 \= history.GetStateAt(25).LocomotionChannel;  
            bool obeyedHuman \= tick25.TargetX \== 100.0f && tick25.TargetY \== 100.0f;

            return wasPatrolling && obeyedHuman;  
        });  
    }  
}

## **6\. Value to the FDP Framework**

This test protects the interactive layer of your simulation. GUI tools are notoriously prone to breaking during refactors, especially when camera zoom levels or screen resolutions change. By extracting the mathematical translation logic (ScreenToWorldSpace) into a testable component and simulating clicks headlessly, this CI test guarantees that map interactions will always accurately command the AI, without needing a human QA tester to manually click around the screen after every commit.

# **Specification: Fdp.Examples.UrbanCombat**

## **1\. Objective**

The UrbanCombat scenario is the **Grand Integration Demo** of the FDP Framework.

Unlike the focused unit tests (which isolate specific systems like CarKinem or ActionDispatch), this headless CI test mathematically proves that **every toolkit in the engine works together flawlessly in a fully distributed environment.** It reincarnates the existing UrbanCombat legacy demo, migrating its complex multi-agent ambush logic into the new deterministic "Host of Hosts" architecture.

It proves the full chain of events: AI Intention ![][image2] Pathfinding ![][image2] Kinematic Movement ![][image2] Multi-Modal Perception ![][image2] BTree Re-evaluation ![][image2] Combat Dispatch ![][image2] Physics Raycasting ![][image2] Entity Death.

## **2\. Architectural Alignment & Source Migration**

This scenario heavily reuses the existing UrbanCombat source code (templates, BTrees, and HSMs) but strictly enforces the new FDP Demo Framework rules:

* **Legacy Code Reuse:** We keep the existing Insurgent and Patrol entity templates, including their assignment of LocomotionChannel, WeaponChannel, and InteractionChannel. We reuse the existing Behavior Tree JSON/C\# definitions without changing the AI logic.  
* **Network Isolation:** The original demo likely ran in a single monolithic process. The reincarnation splits the execution across distinct NodeRoles (Brain\_Bluefor, Brain\_Opfor, MuscleGround, Environment\_Sensors) communicating purely via FDP.Toolkit.Replication ghosting.  
* **Deterministic Environment:** We strip out graphical dependencies and use the mathematical mock graphs (MockRoadGraphs and MockElevationData) to guarantee that the ambush happens at the exact same tick on every CI run.

## **3\. Scenario Setup**

### **3.1. Node Topology (Host of Hosts)**

The NodeBootstrapper spins up four distinct ECS worlds:

1. **Brain\_Bluefor**: Spawns the Convoy entities. Owns their Channels.  
2. **Brain\_Opfor**: Spawns the Insurgent entities. Owns their Channels.  
3. **MuscleGround**: Runs Navigation and CarKinem. Receives ghosted channels, solves physics, owns Transform3D.  
4. **Environment\_Sensors**: Runs Perception and Physics broadphase. Evaluates Line of Sight and updates Blackboard states.

### **3.2. The Entities & The Story**

* **Bluefor Convoy (Entity 1 & 2):** Commanded to drive down Main Street (X-axis). Their BTree is set to Patrol with Rules of Engagement: ReturnFireOnly.  
* **Opfor Insurgent (Entity 3):** Hidden behind a mocked occlusion box (X: 100, Y: 20). BTree is set to Ambush with RoE: WeaponsFree.

## **4\. Execution Timeline**

The timeline tracks the cascading interactions across all FDP toolkits and network nodes.

| **Simulation Phase** | **Tick Range** | **Cross-Toolkit Interaction** | **Architectural Proof** |

| **Phase 1: Approach** | 1 \- 50 | Bluefor Brain writes to LocomotionChannel. Muscle executes pathfinding and moves them down the X-axis. Environment checks LoS (blocked by occlusion box). | **Baseline Locomotion.** Distributed movement works. |

| **Phase 2: The Ambush** | 51 \- 55 | Bluefor clears the occlusion box. Environment node detects Line of Sight, updates Insurgent's Blackboard. | **Perception Integration.** Sensors successfully detected the kinematic movement. |

| **Phase 3: Engagement** | 56 \- 60 | Insurgent BTree transitions to Combat. Writes to WeaponChannel. Muscle fires projectile. Physics registers hit on Bluefor 1\. | **Cognitive to Combat.** AI correctly reacted to replicated sensor data and executed action dispatch. |

| **Phase 4: Retaliation**| 61 \- 80 | Bluefor 1 Health drops. Bluefor BTree sees threat, transitions to Combat (Return Fire). Stops driving, returns fire. Insurgent Health drops to 0\. | **Dynamic Reaction.** Bluefor seamlessly interrupted its pathfinding to engage the threat. |

| **Phase 5: Resolution** | 81 \- 100 | Insurgent dies (SimObjectLifecycle updated). Bluefor BTree clears Combat state, resumes Patrol along the road graph. | **Lifecycle & Recovery.** The simulation successfully cleaned up the dead entity and resumed background tasks. |

## **5\. Programmatic Assertions**

Because this is a macro-level integration test, the assertions focus on the major milestones of the skirmish rather than micro-tick timing.

public class UrbanCombatScenario : IScenario  
{  
    public void Configure(NodeBootstrapper node, NodeRole role)  
    {  
        // ... (Register all toolkits based on Role) ...  
        // ... (Spawn Convoy and Insurgents using legacy TemplateIds) ...

        node.SetCompletionCondition(() \=\>   
        {  
            // Give the grand simulation plenty of time to unfold  
            if (node.CurrentTick \< 200\) return false;

            var history \= node.GetObserverHistory();  
            var lifecycleEvents \= history.GetLifecycleEvents();  
            var weapons \= history.GetWeaponFireEvents();

            // ASSERTION 1: The Ambush Triggered  
            // The Insurgent (ID: 3\) must have fired at least one weapon event.  
            bool insurgentFired \= weapons.Any(w \=\> w.ShooterId \== 3);

            // ASSERTION 2: Bluefor Reactive Retaliation  
            // Bluefor (ID: 1\) must have stopped moving (Locomotion interrupted)   
            // and fired back at the Insurgent.  
            bool blueforReturnedFire \= weapons.Any(w \=\> w.ShooterId \== 1);  
              
            var blueforTick50 \= history.GetStateAt(50, entityId: 1).LocomotionChannel;  
            var blueforTick70 \= history.GetStateAt(70, entityId: 1).LocomotionChannel;  
            bool locomotionInterrupted \= blueforTick50.ActiveAction \== NavigationConstants.ActionIdMoveTo &&   
                                         blueforTick70.ActiveAction \== NavigationConstants.ActionIdHalt;

            // ASSERTION 3: Combat Resolution  
            // The Insurgent entity must have reached the 'Destroyed' lifecycle state.  
            bool insurgentDefeated \= lifecycleEvents.Any(l \=\> l.EntityId \== 3 && l.State \== LifecycleState.Destroyed);

            // ASSERTION 4: Mission Resumption  
            // Bluefor must have resumed moving towards its original objective after the fight.  
            var blueforTick150 \= history.GetStateAt(150, entityId: 1).LocomotionChannel;  
            bool missionResumed \= blueforTick150.ActiveAction \== NavigationConstants.ActionIdMoveTo;

            return insurgentFired && blueforReturnedFire &&   
                   locomotionInterrupted && insurgentDefeated &&   
                   missionResumed;  
        });  
    }  
}

## **6\. Value to the FDP Framework**

The UrbanCombat scenario acts as the ultimate **Regression Safety Net**. While unit tests prove that an engine part works in isolation, this integration test proves that the entire FDP engine acts as a cohesive whole. If a change to the Navigation toolkit accidentally breaks the ActionDispatch module's ability to interrupt movement during combat, this single CI test will catch the failure immediately, ensuring the overall simulation remains highly robust.

