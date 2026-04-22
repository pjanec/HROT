### Architectural Directive: Modernizing the Network Demo

#### 1. Background and Architectural Debt

The legacy `Fdp.Examples.NetworkDemo` project has devolved into an architectural anti-pattern. Currently residing in the foundational Fast Data Plane (FDP) engine layer, it illegally duplicates application-tier routing and schema definitions. It relies on bespoke DDS messages (e.g., `DemoWeaponMsg`, `GeoStateDescriptor`) and redundant components (e.g., `PositionGeodetic`, `SquadChat`, custom `Health`).

Maintaining these parallel definitions violates the DRY principle, fragments the network schema, and fails to represent our production baseline. Furthermore, if we attempt to use the standard Network Exchange Description (NED) messages inside the current project location, we force the FDP engine layer to depend upward on the Hrot application layer, breaking our clean dependency flow.

#### 2. The Goal

The objective is to rebuild this project from scratch as the definitive SDK Onboarding QuickStart. It must serve as a clean, compilable blueprint for a developer embedding the FDP engine into a standalone executable. It will act as an integration proof of our distributed CQRS architecture, demonstrating how cognitive intent (Brain) and physical simulation (Muscle) operate across a network boundary using purely standard toolkits.

#### 3. Core Requirements

**A. Dependency Inversion and Relocation** To resolve the circular dependency boundary and standardize on the NED data model, the project must be relocated from `FDP/Examples/Fdp.Examples.NetworkDemo` to `Hrot/Examples/Hrot.Examples.NetworkDemo`. This elevates the demo to the application tier, allowing it to natively reference `Hrot.Network.NED` without corrupting the engine foundation.

**B. Strict Schema Standardization** We must purge all custom schemas and legacy components.

- Remove all `Demo*` network messages and bespoke components like `TimeModeComponent` and `SquadChat`.
- Rebase the data model strictly on standard NED topics (`EntityMaster`, `WorldPos`, `NavigationIntent`, `NavigationStatus`, `EntityDamage`) and Tier 1 unmanaged FDP components (`SimTransform`, `Health`, `NavState`).

**C. Hexagonal Architecture (Ports and Adapters)** The network demo must be structured purely as a Composition Root. The core simulation logic (the Ports) must remain completely ignorant of network topology or DDS primitives. The network layer (`CycloneNetworkModule` and `NedReplicationModule`) must be injected strictly as a plugin (the Adapter) operating at the system boundaries.

**D. Split-Authority Demonstration** The legacy demo completely bypassed the cognitive tier, relying on hardcoded input hacks. The new demo must explicitly demonstrate the Brain/Muscle split-authority pattern. The composition root must initialize with `NodeRole.Brain | NodeRole.MuscleGround`, wiring both the `CgfLogicPack` (for Behavior Trees/HSMs) and the `SimHostCoreLogicPack` (for physics/kinematics). This proves to new engineers how cognitive decision-making securely drives physical execution over unmanaged memory.



---



The SDK Onboarding QuickStart must serve as an integration proof of our distributed CQRS architecture, demonstrating the complete data flow from cognitive decision-making to physical execution and back without legacy coupling. To achieve this, the demo should run a minimal "Patrol and Engage" scenario spanning two discrete node roles: a Brain (Node 100) and a Muscle (Node 200).

Here is the exact breakdown of the parts, behaviors, and consequences the demo must showcase to properly educate integrating developers.

### 1. The Parts (Node Topology)

The demo must initialize two separate node contexts:

- **The Brain Node (`NodeRole.Brain`):** Hosts the `CgfLogicPack`, `CognitiveRuntimeModule`, and `MissionControlModule`. It owns cognitive state (`DoctrineState`, `TargetMemory`, `Health`) but lacks physical kinematics.
- **The Muscle Node (`NodeRole.MuscleGround | NodeRole.Perception`):** Hosts the `SimHostCoreLogicPack`, `GroundKinematicsModule`, and the background `AutonomousPerceptionModule`. It has no AI, acting purely as a physics and geometry solver.

### 2. Actions & Behaviors (The Scenario Flow)

**Phase 1: Split-Authority Spawning (The Handover)**

- **Action:** The Brain node spawns a patrol vehicle and an enemy target using standard TKB blueprints (e.g., `Tank_M1Abrams` and `Insurgent`).
- **Behavior:** The Brain delegates the `WorldPos` and `NavigationStatus` descriptors to the Muscle node using the `DeferredTakeOwnership` protocol.
- **Consequence:** The Brain retains absolute authority over the entity's identity and AI, but the Muscle node takes over the `SimTransform` authority. This proves the split-authority data model.

**Phase 2: Cognitive Intent to Kinematic Execution (Navigation)**

- **Action:** The Brain's behavior tree evaluates a patrol doctrine and writes a `NavigationIntent` component (e.g., `MoveTo`).
- **Behavior:** The intent crosses the DDS boundary to the Muscle node. The `NavigationIntentBridgeSystem` translates it into a native `NavState`, and the `CarKinematicsSystem` begins integrating physical movement.
- **Consequence:** The Muscle node computes the physical trajectory and returns a `NavigationStatus` (e.g., `InProgress` or `Arrived`) back to the Brain, satisfying the CQRS navigation contract without the Brain ever running kinematics.

**Phase 3: Asynchronous Perception to Cognitive Reaction**

- **Action:** As the patrol vehicle moves, it enters the geometric Line-of-Sight (LOS) of the enemy.
- **Behavior:** The Muscle's `AutonomousPerceptionModule` executes a background spatial hash broadphase and a narrow-phase physics raycast. Upon detecting the enemy, it publishes the result, which the Brain receives as an update to its unmanaged `TargetMemory` component.
- **Consequence:** The Brain's BTree reacts to `Condition_HasTarget` (evaluating `TargetMemory.Count > 0`). The AI cleanly transitions from patrolling to engaging, writing a `WeaponFireIntent` to its `WeaponChannel`.

**Phase 4: Physical Intersection to Authoritative Damage**

- **Action:** The Brain's `WeaponFireIntent` is dispatched to the Muscle node, which spawns a hyper-velocity ballistic projectile.
- **Behavior:** The Muscle's Continuous Collision Detection (CCD) `BallisticsSystem` and `RaycastSolverSystem` detect the projectile intersecting the enemy's collider. The Muscle unconditionally emits a geometric `DetonationNotification` over the network.
- **Consequence:** The Brain ingests the detonation, calculates the hit point loss via `DamageCalculationSystem`, and applies it to the authoritative `Health` component.

**Phase 5: Capability Loss and HSM Fallback (The Climax)**

- **Action:** The enemy's health drops to zero.
- **Behavior:** The Brain's `HealthApplicationSystem` instantly strips the `ActorCapabilities.CanMove` and `ActorCapabilities.CanShoot` bit flags.
- **Consequence:** The `HsmDamageBridgeSystem` detects the missing `CanMove` flag and injects a `MobilityLost` event into the enemy's HSM queue. The enemy's HSM transitions to a `Disabled` state, ceasing all combat logic natively.

By executing this specific sequence, the demo provides a compilable, irrefutable proof of the engine's clean architecture. It shows developers exactly how to compose toolkits where AI never touches physics, physics never modifies health, and the network topology remains entirely hidden from the simulation logic.



----





Here is the technical blueprint for implementing the modern SDK Onboarding QuickStart. This focuses strictly on how to compose the standard engine toolkits (Ports) and inject the NED DDS network layer (Adapters) using our established CQRS and Hexagonal Architecture patterns.

### 1. The Composition Root (Program.cs)

The composition root must remain ignorant of game logic. Its sole responsibility is wiring infrastructure, injecting the network adapter, and starting the kernel loop. To prove the distributed architecture, the builder parses a CLI argument to act as either the Brain or the Muscle.

``` csharp
using System;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Core;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Map.Common;
using Hrot.Network.Infrastructure;
using Hrot.Network.NED.Factory;
using Hrot.SimHost;
using Hrot.CGF;

namespace Hrot.Examples.NetworkDemo
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // E.g., args: "100 Brain" or "200 MuscleGround"
            int nodeId = args.Length > 0 ? int.Parse(args) : 100;
            NodeRole role = args.Length > 1 && args == "MuscleGround"
                ? NodeRole.MuscleGround | NodeRole.Perception
                : NodeRole.Brain;

            int domainId = 0; // Loopback domain for testing

            // 1. External Network Adapter
            using var participant = HrotEnvironment.CreateParticipant(domainId);
            var networkFactory = new NedNetworkFactory(
                participant,
                new Fdp.Toolkit.Replication.Services.NetworkEntityMap(),
                HrotEnvironment.CreateGeoTransform(),
                new FdpEventBus(),
                nodeId,
                role);

            // 2. Infrastructure Boundary
            var config = new HrotNodeConfig { DomainId = domainId, NodeId = nodeId, Headless = true, ExternalParticipant = participant };
            var context = new HrotNodeBuilder(config)
                .WithRole("NetworkDemo", role)
                .WithNetworkFactory(networkFactory)
                .WithReplication(role) // Injects NedReplicationModule automatically
                .Build();

            // 3. Domain Logic Registration (The Ports)
            HrotSharedComponentRegistry.RegisterAll(context.World);
            if (role.HasFlag(NodeRole.MuscleGround)) SimHostComponentRegistry.RegisterAll(context.World);
            if (role.HasFlag(NodeRole.Brain)) CgfComponentRegistry.RegisterAll(context.World);

            // 4. Toolkit Module Wiring
            var doctrineRegistry = new Fdp.Toolkit.Behavior.DoctrineRegistry();
            DemoScenarioSetup.RegisterDoctrines(doctrineRegistry);

            if (role.HasFlag(NodeRole.Brain))
            {
                context.Kernel.RegisterModule(new CgfLogicPack(doctrineRegistry, context.EntityMap));
            }
            if (role.HasFlag(NodeRole.MuscleGround))
            {
                context.Kernel.RegisterModule(new SimHostCoreLogicPack(context.EntityMap));
            }

            foreach (var baseModule in context.BaseModules) context.Kernel.RegisterModule(baseModule);
            context.Kernel.RegisterModule(context.NedReplication!);

            // 5. Scenario Execution
            context.Kernel.Initialize();
            if (role.HasFlag(NodeRole.Brain)) DemoScenarioSetup.SpawnEntities(context);

            while (true)
            {
                context.SlaveTranslator?.Tick();
                context.ClusterSlave.Tick();
                context.Kernel.Update(0.016f);
                context.EventBus.SwapBuffers();
                Thread.Sleep(16);
            }
        }
    }
}
```

### 2. Custom Behavior Definition (DemoScenarioSetup.cs)

Behaviors must be defined strictly using data (JSON) and stateless delegates, adhering to the FastBTree unmanaged memory constraints. We define a "Patrol and Engage" BTree that evaluates `TargetMemory` and falls back to a navigation intent.

``` csharp
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.CGF.Brains;

namespace Hrot.Examples.NetworkDemo
{
    public static class DemoScenarioSetup
    {
        // Stateless BTree structure. Condition_HasTarget evaluates TargetMemory.
        private const string PatrolAndEngageJson = """
        {
          "TreeName": "PatrolAndEngage_BT",
          "Root": {
            "Type": "Selector",
            "Children": [
              {
                "Type": "Sequence",
                "Children": [
                  { "Type": "Condition", "Action": "Condition_HasTarget" },
                  { "Type": "Action", "Action": "Action_AimAndFire" }
                ]
              },
              { "Type": "Action", "Action": "Action_Wander" }
            ]
          }
        }
        """;

        public static void RegisterDoctrines(DoctrineRegistry registry)
        {
            var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
            actionReg.Register("Condition_HasTarget", CgfNodes.Condition_HasTarget); //
            actionReg.Register("Action_AimAndFire", CgfNodes.Action_AimAndFire); //
            actionReg.Register("Action_Wander", CgfNodes.Action_Wander); //

            var blob = TreeCompiler.CompileFromJson(PatrolAndEngageJson);

            registry.Register(1001, "PatrolAndEngage", new DoctrineDefinition
            {
                Name = "PatrolAndEngage",
                BrainTier = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, actionReg)
            });
        }

        public static void SpawnEntities(HrotNodeContext context)
        {
            // Brain node spawns the entity.
            // The NedReplicationModule routes WorldPos authority to MuscleGround.
            var cmd = new Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand
            {
                NetworkId = context.IdAllocator!.AllocateId(),
                TkbType = 2001, // MilitaryAPC
                OwnerNodeId = context.NodeId,
                InitType = Fdp.Toolkit.Replication.ReliableInitType.AllPeers
            };
            context.EventBus.PublishManaged(cmd);

            // Assign Doctrine via MissionControl logic
            // ...
        }
    }
}
```

### 3. Hexagonal Composition Diagram

This diagram maps exactly how the FDP engine decouples the domain (Muscle/Brain toolkits) from the network topology (CycloneDDS/NED).

``` mermaid

flowchart TD
    subgraph CompositionRoot [Composition Root: Program.cs]
        Builder[HrotNodeBuilder]
        Config[NodeRole Setup]
    end

    subgraph Adapters [Plugin Adapters]
        DDS[CycloneDDS Participant]
        Factory[NedNetworkFactory]
        RepMod[NedReplicationModule]
        Ingress[CycloneNetworkIngressSystem]
        Egress[CycloneEgressSystem]
    end

    subgraph Ports_Brain [Brain Tier Ports]
        Cgf[CgfLogicPack]
        BTree[BTreeTickSystem]
        Doc[(DoctrineState)]
        NavIntent[(NavigationIntent)]
    end

    subgraph Ports_Muscle [Muscle Tier Ports]
        Sim[SimHostCoreLogicPack]
        Kinem[CarKinematicsSystem]
        NavState[(NavState)]
        TF[(SimTransform)]
    end

    %% Dependency flow
    Config --> Builder
    Builder -. Injects DDS .-> Factory
    Factory -. Wires Translators .-> RepMod
    RepMod --> Ingress & Egress

    Builder -. Registers .-> Cgf
    Builder -. Registers .-> Sim

    %% CQRS Data flow
    BTree -- Writes --> NavIntent
    NavIntent -- "NavigationIntentEgressTranslator" --> Egress
    Egress == "DDS Network Boundary" ==> Ingress
    Ingress -- "NavigationIntentIngressTranslator" --> NavIntent

    %% Bridge flow
    NavIntent -- "NavigationIntentBridgeSystem" --> NavState
    NavState -- Drives --> Kinem
    Kinem -- Mutates --> TF

    classDef root fill:#2d3436,stroke:#dfe6e9,stroke-width:2px,color:#fff
    classDef adapter fill:#e17055,stroke:#fff,stroke-width:2px,color:#fff
    classDef brain fill:#0984e3,stroke:#fff,stroke-width:2px,color:#fff
    classDef muscle fill:#d63031,stroke:#fff,stroke-width:2px,color:#fff

    class CompositionRoot,Builder,Config root
    class Adapters,DDS,Factory,RepMod,Ingress,Egress adapter
    class Ports_Brain,Cgf,BTree,Doc,NavIntent brain
    class Ports_Muscle,Sim,Kinem,NavState,TF muscle
```

### Architectural Critique

By following this structure, the developer immediately understands that:

1. `NedReplicationModule` is just a plugin. If they swap it for `BdcReplicationModule`, the tactical data model changes but `CarKinematicsSystem` and `BTreeTickSystem` remain completely un-modified.
2. The network boundary forces the CQRS pattern. `BTreeTickSystem` cannot touch `SimTransform` directly. It must write a `NavigationIntent`, which the network adapters transport, and the Muscle node translates into `NavState` for the physics solver.

----



### Acceptance Criteria

To definitively prove the modernization of the `NetworkDemo` is complete and architecturally sound, the pull request must satisfy the following strict conditions:

1. **Structural Decoupling:** The project must physically reside at `Hrot/Examples/Hrot.Examples.NetworkDemo`. It must compile successfully without introducing any circular dependencies from the FDP engine layer to the Hrot application layer.
2. **Schema Eradication:** A static analysis of the project must confirm zero usage of legacy bespoke components (`SquadChat`, `TimeModeComponent`, custom `Health`) and zero custom DDS wire messages (`DemoSpawnMsg`, `DemoTransformMsg`).
3. **Standardized Composition:** `Program.cs` must act purely as a Hexagonal Architecture composition root. It must use `HrotNodeBuilder` to inject `NedNetworkFactory`, configure `NedReplicationModule`, and register standard logic packs (`CgfLogicPack`, `SimHostCoreLogicPack`) without custom network translators.
4. **CQRS Split-Authority Execution:** The demo must successfully execute the "Patrol and Engage" scenario across two isolated roles (`NodeRole.Brain` and `NodeRole.MuscleGround`). The Brain must never directly mutate `SimTransform`, and the Muscle must never evaluate a `BTreeContext`.

### Automated Integration Test Specification

To guarantee the demo does not suffer from bit-rot, you must introduce a headless integration test in the `Hrot.ClusterRunner.Integration.Tests` suite. This test will execute the exact composition root logic of the demo using the `HrotRunnerHarness` over a CycloneDDS loopback domain.

Add the following test specification: **`NetworkDemo_PatrolAndEngage_ExecutesDistributedCqrsFlow`**

**Phase 1: Boot and Split-Authority Verification**

- Initialize a shared loopback domain. Boot `NodeRole.Brain` (CGF) and `NodeRole.MuscleGround` (SimHost) using the standard harnesses.
- Spawn the patrol entity from the Brain node.
- **Assert:** The Muscle node claims authority over `SimTransform` and `NavigationStatus`. The Brain retains authority over `Health`, `TargetMemory`, and `NavigationIntent`.

**Phase 2: Cognitive Intent to Kinematic Execution**

- **Assert:** The Brain's `BTreeTickSystem` evaluates the patrol doctrine and writes a `NavigationIntent`.
- Pump the network frames.
- **Assert:** The Muscle node receives the `NavigationIntent` via DDS, translates it to `NavState`, and the `CarKinematicsSystem` begins mutating `SimTransform` (Velocity > 0).

**Phase 3: Perception and Combat Arbitration**

- Inject a mock enemy entity into the Muscle node's `SpatialHashGrid`.
- Pump frames to allow the `AutonomousPerceptionModule` to perform the broadphase/LOS check and route the `SensorTargets` to the Brain.
- **Assert:** The Brain's `TargetMemory` populates. The BTree transitions from patrol to `Action_AimAndFire`, publishing a `WeaponFireIntent` over DDS.

**Phase 4: Physical Hit to Authoritative Damage**

- Inject a `DetonationNotification` directly onto the Muscle's event bus, simulating the ballistic impact.
- Pump frames to allow the `MunitionDetonationEgressTranslator` to route the geometric hit to the Brain.
- **Assert:** The Brain's `HealthApplicationSystem` deducts hit points from the authoritative `Health` component and strips the `ActorCapabilities.CanMove` flag.
- **Assert:** The Muscle node's `SimVelocity` drops to zero, proving the physical simulation respects the cognitive capability loss without local health arbitration.

Implementing this test proves the demo's scenario works end-to-end and definitively locks the network boundaries against future regressions.

