you are working on implementing .dev\modular-2\DESIGN.md hecagonal architecture refactor

you are almost done!

### The Final Polish (The Last 1%)
To make this architecture mathematically flawless, there are two tiny cleanups remaining. One is a strict lifecycle rule enforcement, and the other is a DRY (Don't Repeat Yourself) violation introduced to break the lateral coupling.

#### 1. Shift the `DdsParticipant` Fallback Out of the Inner Ring
You correctly removed the fallback `DdsParticipant` instantiations from the subsystem applications, but one still lingers in the shared inner-ring infrastructure: `HrotNodeBuilder.cs`.

Currently, `Hrot.Common/Infrastructure/HrotNodeBuilder.cs` does this:
```csharp
participant = _config.ExternalParticipant ?? HrotEnvironment.CreateParticipant(_config.DomainId);
```
**Why this is an issue:** `Hrot.Common` is the inner domain ring. It has no business spinning up network sockets if the caller forgot to provide one. Rule 3 dictates that ONLY the Application Shell / Composition Root may instantiate `DdsParticipant`. 

**The Fix:**
1. In `HrotNodeBuilder.cs`, remove the fallback. It should simply be:
   `participant = _config.ExternalParticipant;`
2. If a standalone Application Shell like `SimHostApp.cs` or the test harnesses need a participant when running outside the `ClusterRunner`, *they* should instantiate it and pass it to the builder:
   ```csharp
   // In SimHostApp.OnLoad()
   var participant = _networkFactory?.Participant ?? (!nodeConfig.Headless ? HrotEnvironment.CreateParticipant(domainId) : null);
   
   var hrotConfig = new HrotNodeConfig {
       ExternalParticipant = participant,
       // ...
   };
   ```
By pushing the `new DdsParticipant()` call strictly out to the outermost executable shells (`Program.cs`, `SimHostApp.cs`, `HrotRunnerHarness.cs`), the domain builder remains pure and side-effect free.

#### 2. Resolve the `CgfNodes` vs `SimHostNodes` Duplication

You have identified one of the most stubborn remnants of the old monolithic design: Cognitive execution logic and definitions bleeding across the physical boundaries into the Muscle and Core layers.

In a strict CQRS and Ports-and-Adapters architecture, `Hrot.SimHost` (the Muscle) must act as a blind actuator. It should only consume primitive commands like `NavigationIntent` and output `NavigationStatus`. Behavior Trees (`Fbt`), `BrainBlackboard` execution, and Doctrine definitions are 100% the responsibility of `Hrot.CGF` (the Brain).

Here is the exact surgical plan to eradicate this nonsense and perfectly enforce the Brain/Muscle boundary:

**1. Delete `SimHostNodes.cs`**
The file `Hrot.SimHost/Brains/SimHostNodes.cs` is a blatant architectural violation. It contains concrete FastBTree action nodes like `Action_Wander` and `Action_WriteMoveToChannel`. The Muscle node has absolutely no business knowing how to traverse a behavior tree. 
*   **Action:** Physically delete this file. `Hrot.CGF/Brains/CgfNodes.cs` already exists and correctly owns this execution logic for the Brain node.

**2. Relocate and Rename `SimHostDoctrineIds`**
Currently, `SimHostDoctrineIds` lives inside the shared application domain at `Hrot.Common/DoctrineIds.cs`. The shared inner domain should be agnostic to specific AI implementation hashes like `WanderMilitary_BT`.
*   **Action:** Move this file into `Hrot.CGF/Configuration/` and rename the class to `CgfDoctrineIds`. Only the CGF node (and the offline Editor which natively references CGF logic packs) needs to know about these specific doctrine assignments.

**3. Purge Brain Registration from `SimHostApp.cs`**
Because `SimHostApp` used to be a monolith, `SimHostApp.OnLoad()` statically compiles and registers the BTree interpreters for `MoveToLocation`, `FollowRoute`, and `WanderMilitary` into the `DoctrineRegistry`. This forces the Muscle executable to load and wire Brain logic.
*   **Action:** Delete the entire `doctrineRegistry.Register(...)` block from `SimHostApp.cs`. The Muscle shell should initialize its infrastructure with an empty `DoctrineRegistry`.

**4. Eradicate Brain Test Hooks in SimHost**
`SimHostApp.cs` contains test hooks like `TestHook_AssignWanderMission` and `TestHook_ForceDoctrineActive` which manually inject the `WanderMilitary_BT` hash and increment the doctrine instance ID to force movement during tests.
*   **Action:** Delete these methods. Muscle integration tests should not trigger movement by acting like a brain. To test kinematics on the Muscle node, integration tests should directly attach a `NavigationIntent` component to the entity, simulating exactly what arrives over the network from the CGF.

**5. Relocate `BlackboardOffsets.cs`**
`Hrot.SimHost/Brains/BlackboardOffsets.cs` defines the memory layout for threat evaluation inside the `BrainBlackboard`. 
*   **Action:** Move this file to `Hrot.CGF/Brains/BlackboardOffsets.cs`.

**A Note on ECS Memory Allocation:**
You might wonder why `CognitiveComponentRegistry.RegisterAll` still needs to be called by the Muscle node. This is because the shared `NedTkbBuilder` attaches empty `BrainBTreeState`, `BrainBlackboard`, and `DoctrineState` components to vehicle blueprints at spawn time. 



While we caught the execution nodes (`SimHostNodes`) and definitions (`SimHostDoctrineIds`) above, a deep dive into your latest `HROT_51.txt` reveals that **Cognitive-tier logic is still bleeding into the Muscle-tier's simulation and UI systems.**

Here are the remaining toxic leaks you must purge from `Hrot.SimHost` to achieve a mathematically perfect Brain/Muscle isolation:

### 1. The Most Severe Leak: `RouteContextSystem.cs`
In `Hrot.SimHost/Systems/Routing/RouteContextSystem.cs`, a system residing in the Muscle node is reading waypoint advice and writing it directly into the `BrainBlackboard`. Specifically, it writes to `blackboard.Memory[BlackboardOffsets.ExpectedThreatLevel]`. 
*   **Why it’s toxic:** The Muscle node (kinematics/routing) is directly manipulating the unmanaged memory layout of the Cognitive tier. A pure Muscle node should not even know what a `BrainBlackboard` is, let alone how to write to its specific byte offsets.
*   **The Fix:** Move `RouteContextSystem.cs` and `BlackboardOffsets.cs` entirely out of `Hrot.SimHost` and into `Hrot.CGF/Systems/Routing/`. This is 100% Brain-tier logic.

### 2. The UI Leak: `SimHostScenarioManager.cs`
In `Hrot.SimHost/UI/SimHostScenarioManager.cs`, when the user clicks to spawn "Roamers" or "Road Users", the Muscle UI explicitly news up a `DoctrineState` (hardcoding `SimHostDoctrineIds.WanderMilitary_BT`) and a `BrainBlackboard`.
*   **Why it’s toxic:** A UI panel running inside a pure Muscle node is acting like a Brain by manually configuring AI execution states.
*   **The Fix:** Strip out the `DoctrineState` and `BrainBlackboard` instantiations from `SimHostScenarioManager`. It should simply publish a `SpawnEntityCommand` for the TKB type. If the entities need to wander, the UI should send a neutral `MissionControlRequest` via the `ICommandGateway`, allowing the CGF node to assign the doctrine.

### 3. The "God Class" Leak: `SimulationLogicModule` & `NodeBootstrapper`
`Hrot.SimHost` still contains `Modules/SimulationLogicModule.cs` and `NodeBootstrapper.cs`. Both of these files explicitly `using` and instantiate `MissionControlModule`, `CognitiveRuntimeModule`, and `ActionDispatchModule`.
*   **Why it’s toxic:** Because these files live in `Hrot.SimHost.csproj`, they force the Muscle assembly to maintain physical package dependencies on the Brain-tier FDP toolkits, destroying your assembly isolation.
*   **The Fix:** 
    1. **Delete** `SimulationLogicModule.cs` entirely. You already built `SimHostCoreLogicPack`, which perfectly supersedes it for the Muscle role.
    2. **Move** `NodeBootstrapper.cs` completely out of `Hrot.SimHost` and up into `Hrot.ClusterRunner` (or a shared `Hrot.Common.Infrastructure` assembly). Bootstrapping multiple node roles is a Composition Root concern, not a SimHost domain concern.

### 4. The Purge Checklist (from our previous analysis)
Just to ensure nothing is left behind from our previous sweep, make sure you also execute these:
*   **Delete** `Hrot.SimHost/Brains/SimHostNodes.cs`. The Muscle node cannot traverse a behavior tree.
*   **Move** `Hrot.Common/DoctrineIds.cs` (specifically the `SimHostDoctrineIds` class) to `Hrot.CGF/Configuration/CgfDoctrineIds.cs`. 
*   **Strip** `SimHostApp.cs` of all `doctrineRegistry.Register(...)` calls. The Muscle shell must boot with an empty `DoctrineRegistry`.

### A Note on `CognitiveComponentRegistry.cs`
You might notice `Hrot.SimHost/CognitiveComponentRegistry.cs` still registers `BrainBTreeState`, `BrainBlackboard`, etc.. **This is actually perfectly fine and required.** 
Because ECS replication (ghosts) requires both nodes to agree on the exact memory layout of an entity, the Muscle node must register the component schemas. The rule is: *The Muscle node must allocate the memory to satisfy the network, but it must never execute logic against it.* 

Execute these surgical extractions, and your `Hrot.SimHost` will finally be a 100% blind, pure-physics Muscle node!


well, simHost should not be blind! simhost should have the perception role!

You are completely correct: the `SimHost` is **not** blind. In fact, looking at `SimHostApp.cs`, the default deployment explicitly combines both the Muscle and Perception roles: `NodeRole role = NodeRole.MuscleGround | NodeRole.Perception`.

Here is the exact architectural boundary we are enforcing between **Perception** (which belongs on the SimHost) and **Cognition** (which belongs on the CGF):

### 1. The Perception Role (Belongs on SimHost)
The `SimHostCoreLogicPack` correctly hosts the `AutonomousPerceptionModule` and perception systems inside the `CombatModule`. 
This means the SimHost is fully responsible for the heavy mathematical lifting of sensors:
*   Running the `VisionBroadphaseSystem` and `LosRequestBatchingSystem` (Line of Sight raycasts).
*   Running the `AudioPerceptionSystem` to detect sounds.
*   Writing the results into the unmanaged `TargetMemory` component (the threat table).

To the SimHost, Perception is just another form of physics. It calculates *what* can be seen or heard based on geometry and ranges, and updates the state.

### 2. The Cognitive Role (Belongs on CGF)
While the SimHost has *eyes* (Perception) and *muscles* (Kinematics), it does not have a *brain*. 

The `Hrot.CGF` node reads the `TargetMemory` that the SimHost populated and evaluates it using Behavior Trees or HSMs. For example, the CGF runs a `Condition_HasTarget` node, decides to attack, and outputs a `WeaponFireIntent` or `NavigationIntent`.

### The Verdict
Your architectural intuition is spot on. We must ruthlessly purge Behavior Trees, HSMs, and Doctrines from `Hrot.SimHost` because those are Cognitive-tier decision-makers. However, we must absolutely preserve `AutonomousPerceptionModule`, `TargetMemory`, and `PerceptionReceptor` inside the SimHost because calculating line-of-sight and sensor ranges is a core Muscle/Physics-tier responsibility!

---



before finishing, make sure the tests are passing and the solution compiles.
