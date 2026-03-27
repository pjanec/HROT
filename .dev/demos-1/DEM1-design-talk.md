Note: This document is referencing and supersedes the original "FDP-demos-all.md" documents which is stored next to this document. 

* * *

in the recent refactoring we created lots of new modules and moved lots of stuff to generic toolkits. now we need to show how to use these toolkits via a set of demo applications or subsystems of bagora runner. These should be all purely headless to enable smooth autonomous development and testing.

I basically want to mimic the stuff demonstrated by current apps/subsystem from Bagira.\* domain as well as examples like the network demo and carkinem demo and urban combat, just re-organized to allow for both showcasing some feature/toolkit as well as providing environment for autonomous testing. The goal of these new style demos is to show and prove that the toolkits/fetures work well together.

The new demos should be strictyl using just the stuff located inside the FDP domain, nothing from top level apps like "Bagira.\*". Top level apps might be taken for inspiration but if needed, their stuff nedds to be duplicated to inside FDP and put in Fdp.Examples namespace in FDP/Examples folder.

The examples will need their own network definitions and corresponding ingress/egress translators. The DDS messages should not resemble the Bagira ones too much. They should not use the concept of entity master. No need for using geographical coordinates, no geo-cartesian conversions needed.

The stuff shared by different demo apps/subytems (like components, dds topics, constants...) must be centralized in "Fdp.Examples.Common" project. DDS topic definitions required to be added to "Fdp.Examples.DDS" namespace.

The new demos must demonstrate ALL the features and concepts present in/supported by the FDP & toolkits, like for example  - planning trajectory (path planner)  - following trajectory (car kinem)  - detecting enemies (perception)  - ground clamping  - car avoidance

To fully demonstrate these, the following nodes (subsystems) might to be needed  - IG (ground clamping, smoothing, perception - visibility raycasts, smart sensors-enemy list)  - Sim (car kinematics with avoidance - owner of position descriptor, road network, path planner, physics, ballistics, hit detection, damage inflicting to vehicles... )  - Brain (missions, doctrines, BTree, HSM, blackboard, channels, combat AI etc...)

\- some subsystems that could show partial ownership

I am not sure if one single big all-in one demo showing every feature/toolkit is the best way - such a system might be difficult to understand and test, but on the other hand it would should all integrated like in a very real use case. Maybe we need both approaches - partial demos and one big full demo showing all together, reusing the parts from the partial demo to minimize the code duplication.

* * *

This revised vision significantly clarifies the goal. By completely decoupling from the `Bagira.*` domain, dropping heavy concepts like the Entity Master, and ignoring Geodetic/WGS84 conversions in favor of pure Cartesian math, we can create a much leaner, more focused set of CI-friendly testbeds.

Your suggestion to use a **hybrid approach**—creating several small, isolated "Partial Demos" to prove specific toolkits, culminating in a single "Grand Integration Demo" that reuses those components—is the absolute best path forward. It provides both granular CI unit testing and macro-level system validation.

Here is how we can structure the `Fdp.Examples` suite to perfectly align with your new constraints and the current FDP codebase.

1\. Project Organization & Networking Foundation

As requested, all demo code will be isolated from production apps, relying strictly on generic FDP toolkits.

-   **Fdp.Examples.DDS****:** Will contain lightweight IDL definitions for Cartesian-only networking. Because we are dropping the `EntityMaster` concept, we can use a simple `SimObjectSpawnMsg` (EntityId, TemplateId) to handle remote instantiation, bypassing the complex handshake of the current `EntityMasterEgressTranslator`. Position updates will be pure `Vector3` payloads representing `SimTransform`, rather than the heavy `GeoStateDescriptor` used in the current Network demo.-   **Fdp.Examples.Common****:** Will house the shared `NodeBootstrapper` or runner wrappers, scenario constants, and the network translators (Ingress/Egress) that map the lightweight DDS messages to the ECS `SimTransform`, `LocomotionChannel`, and `TargetMemory` components.

2\. Node (Subsystem) Definitions

To demonstrate distributed authority and partial ownership, we will define strict CQRS boundaries for the nodes using the existing toolkits.

-   **Brain Node:** Registers `FDP.Toolkit.Behavior`. Owns the cognitive pipeline. Reads `TargetMemory` and `BlackboardComponent`, executes BTree/HSM logic, and has **write authority** over `LocomotionChannel` and `WeaponChannel`. It receives physical positions as read-only network ghosts.-   **Sim Node (Muscle):** Registers `FDP.Toolkit.CarKinem`, `FDP.Toolkit.Navigation`, and `FDP.Toolkit.Combat`. Owns physical reality. It reads the ghosted `LocomotionChannel`, calculates pathfinding via `PathfindingSolverSystem`, handles RVO avoidance, and has **write authority** over `SimTransform`. It also processes `FireRequestEvent`s into ballistics and resolves hits.-   **IG / Environment Node:** Registers `FDP.Toolkit.Perception`, `FDP.Toolkit.Geographic` (for localized clamping), and `FDP.Toolkit.Physics` (for broadphase). Owns situational awareness. It queries the `SpatialHashGrid`, performs raycasts for line-of-sight, smooths Z-heights for visual terrain clamping, and has **write authority** over the `TargetMemory` component, broadcasting enemy contacts back to the Brain.

3\. Proposed Demo Suite

Here is how we can break down the examples into partial and integrated scenarios to prove the toolkits work together autonomously:

Partial Demo 1: Kinematics & Avoidance (Sim Node Isolation)

-   **Goal:** Prove `FDP.Toolkit.Navigation` and `FDP.Toolkit.CarKinem` work together.-   **Setup:** A headless Sim Node loaded with a `RoadNetworkBlob`.-   **Scenario:** Inject direct ECS commands via `VehicleAPI` to bypass the Brain entirely. Command two vehicles to follow a trajectory that intersects.-   **Validation:** Assert that `RVOAvoidance` successfully deviates their `SimTransform.Position` laterally to avoid collision, and that they recover to the path to achieve a precision stop at their destinations.

Partial Demo 2: Distributed Cognitive Combat (Brain + Sim)

-   **Goal:** Prove `FDP.Toolkit.Behavior` drives `FDP.Toolkit.Combat` over the network.-   **Setup:** Process A (Brain Node) and Process B (Sim Node) communicating via loopback.-   **Scenario:** The Brain's BTree detects a mocked threat in the blackboard and writes `ActionIdAimAndFire` to the `WeaponChannel`. This intention replicates to the Sim node.-   **Validation:** Assert that the Sim node's `WeaponDispatcherSystem` triggers the executor, publishes a `FireRequestEvent`, and the `BallisticsSystem` correctly spawns a `BallisticProjectile` and computes a hit on a target without any logic running on the Brain node.

Partial Demo 3: Perception & Clamping (IG Node Isolation)

-   **Goal:** Prove `FDP.Toolkit.Perception` and `FDP.Toolkit.Geographic` terrain queries.-   **Setup:** A headless IG node loaded with a mock terrain provider.-   **Scenario:** A target entity is moved deterministically behind a static physics collider (a wall).-   **Validation 1 (Perception):** Assert that the `VisionBroadphaseSystem` queues a raycast, detects the occlusion, and removes the target from the observer's `TargetMemory`.-   **Validation 2 (Clamping):** Assert that `TerrainQuerySubmitSystem` uses the vehicle's velocity to look ahead, and `TerrainQueryResolutionSystem` successfully modifies the `GroundClampingState.TargetZOffset` to smoothly pitch the vehicle _before_ it hits an incline.

The Grand Demo: Urban Autonomous Patrol (All Nodes)

-   **Goal:** The ultimate "Host of Hosts" CI test proving all toolkits integrate.-   **Setup:** Brain, Sim, and IG nodes running concurrently.-   **Scenario:**
    -   Brain commands a patrol along a road network (Brain -> Sim).-   Sim executes the pathfinding and moves the vehicle (Sim -> Network).-   IG node observes the movement, runs LOS checks against an enemy, and publishes a sensor contact (IG -> Brain).-   Brain's HSM/BTree evaluates the contact, interrupts the patrol, and commands an engagement (Brain -> Sim).-   Sim resolves the ballistics, registers a hit, and destroys the enemy entity.-   **Validation:** By mathematically asserting the final ECS state (Enemy is dead, vehicle resumed patrol), you prove the entire engine pipeline is stable, thread-safe, and properly replicated without requiring visual observation.

By stripping out the Bagira-specific networking overhead (like geodetic translators and the heavy Entity Master), these headless tests will be incredibly fast to run and will serve as perfect autonomous regression tests for the generic FDP toolkits.

i need the demo runner as described in the demo document, allowing to run in headless mode or for AI development and testing, optionally with 2d map for human observation.

To implement the Demo Runner exactly as specified in the framework document while strictly adhering to the current `FDP.Framework.Runner` codebase, we will map the specification's conceptual `NodeBootstrapper` directly into the engine's existing `ISubsystem` and `SubsystemOrchestrator` architecture.

Because the `SubsystemOrchestrator` natively bypasses all Raylib and ImGui rendering calls when `RunnerOptions.Headless` is true, it perfectly satisfies the requirement for dual-purpose execution: lighting-fast CI validation via the command line, and visual debugging via `FDP.Toolkit.Vis2D`.

Here is the implementation of the Demo Runner and the Subsystem Wrapper.

1\. The CLI Entry Point (`Program.cs`)

This acts as the `fdp.framework.runner`. It parses the command line arguments using the existing `CommandLineParser` and spins up the `SubsystemOrchestrator`.

```
using System;
using System.Threading.Tasks;
using CommandLine;
using FDP.Framework.Runner;
using Fdp.Examples.Common;

namespace Fdp.Examples.Runner
{
    public class DemoRunnerOptions
    {
        [Option("scenario", Required = true, HelpText = "The scenario to execute (e.g., ChannelDispatch, AutoDrive).")]
        public string Scenario { get; set; } = string.Empty;

        [Option("max-ticks", Default = 500, HelpText = "Timeout threshold for the scenario.")]
        public int MaxTicks { get; set; }

        [Option("attach-vis2d", Default = false, HelpText = "If true, overrides headless mode to render the 2D map.")]
        public bool AttachVis2D { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<DemoRunnerOptions>(args)
                .WithParsed(options => 
                {
                    // If we aren't explicitly attaching Vis2D, run strictly headless for CI [10, 11]
                    var runnerOptions = new RunnerOptions 
                    { 
                        Headless = !options.AttachVis2D,
                        TargetFps = options.AttachVis2D ? 60 : 0 // Run unthrottled in CI
                    };

                    // Instantiate the requested scenario
                    IScenario scenarioLogic = ScenarioRegistry.GetScenario(options.Scenario);

                    // Wrap the scenario in an ISubsystem so the Orchestrator can manage it
                    var scenarioSubsystem = new ScenarioSubsystem(options.Scenario, scenarioLogic, options.MaxTicks);

                    // The Orchestrator handles the loop, and safely skips rendering if Headless [6, 8]
                    var orchestrator = new SubsystemOrchestrator(new[] { scenarioSubsystem }, runnerOptions);
                    
                    orchestrator.Initialize();
                    orchestrator.Run();
                });
        }
    }
}
```

2\. The Scenario Subsystem (`ScenarioSubsystem.cs`)

This replaces the fictional `NodeBootstrapper`. It implements `ISubsystem`, owns the `ModuleHostKernel`, and conditionally spins up the `MapCanvas` if the user requested visual observation.

```
using System.Numerics;
using Fdp.Kernel;
using ModuleHost.Core;
using FDP.Framework.Runner;
using FDP.Toolkit.Vis2D;

namespace Fdp.Examples.Common
{
    public class ScenarioSubsystem : ISubsystem, IMapCameraProvider
    {
        public string Name { get; }
        public Vector4 TitleBarColor => new Vector4(0.2f, 0.6f, 0.8f, 1f); [15]

        private readonly IScenario _scenario;
        private readonly int _maxTicks;
        
        public EntityRepository World { get; private set; }
        public ModuleHostKernel Kernel { get; private set; }
        private MapCanvas? _mapCanvas;
        
        private bool _isHeadless;

        public ScenarioSubsystem(string name, IScenario scenario, int maxTicks)
        {
            Name = name;
            _scenario = scenario;
            _maxTicks = maxTicks;
        }

        public void Initialize(SubsystemConfig config)
        {
            _isHeadless = config.Headless; [16]
            
            World = new EntityRepository();
            var accumulator = new EventAccumulator();
            Kernel = new ModuleHostKernel(World, accumulator); [17]

            // 1. Let the scenario register its specific toolkits (CQRS isolation) [4]
            _scenario.Configure(World, Kernel);

            Kernel.Initialize();

            // 2. Conditionally setup Vis2D if requested for human observation [1]
            if (!_isHeadless)
            {
                _mapCanvas = new MapCanvas(); [13]
                _scenario.ConfigureVisuals(_mapCanvas, World);
            }
        }

        public void Update(float deltaTime)
        {
            // Abort if we exceed the CI timeout [4, 10]
            if (World.GlobalVersion > _maxTicks)
            {
                System.Console.WriteLine($"[CI FAILED] Scenario {Name} timed out after {_maxTicks} ticks.");
                System.Environment.Exit(1);
            }

            // Step 1: Inject synthetic scenario events / assertions [4]
            if (_scenario.EvaluateTick(World.GlobalVersion))
            {
                System.Console.WriteLine($"[CI SUCCESS] Scenario {Name} completed successfully at tick {World.GlobalVersion}.");
                System.Environment.Exit(0);
            }

            // Step 2: Tick the actual engine
            Kernel.Update(); [18]
            
            // Step 3: Update camera if observing
            _mapCanvas?.Update(deltaTime); [19]
        }

        public void DrawWorld()
        {
            // Safely bypassed by SubsystemOrchestrator if headless [8, 20]
            _mapCanvas?.Draw();
        }

        public void DrawUI()
        {
            // Render specific test telemetry or debug panels here
        }

        public void Shutdown()
        {
            Kernel?.Dispose();
            World?.Dispose();
        }

        public MapCamera? GetMapCamera() => _mapCanvas?.Camera; [21]
    }
}
```

3\. Example Scenario Implementation (`IScenario`)

Here is how an individual demo (like the `ChannelDispatch` scenario) plugs into this runner, strictly using the actual unmanaged C# components.

```
using Fdp.Kernel;
using ModuleHost.Core;

namespace Fdp.Examples.Common
{
    public interface IScenario
    {
        void Configure(EntityRepository world, ModuleHostKernel kernel);
        void ConfigureVisuals(MapCanvas canvas, EntityRepository world);
        
        /// Returns true if the CI completion condition is met.
        bool EvaluateTick(uint currentTick);
    }
}
```

**Why this architecture succeeds:**

-   **Zero Thread Blocking/Sleeps:** The scenario operates purely on `World.GlobalVersion` evaluations inside `EvaluateTick`, adhering to the engine's strict phase-based iteration.-   **True Headless Performance:** When `--attach-vis2d` is omitted, `SubsystemConfig.Headless` is true. The orchestrator loop (`while (_running)`) skips `Render()` entirely, meaning the kernel processes ticks as fast as the CPU allows without waiting for VSync or GPU buffers.-   **Pristine Domain Isolation:** Because the runner wraps `IScenario`, tests can compose their nodes using exactly the toolkits they want to evaluate (e.g., `Kernel.RegisterModule(new ActionDispatchModule(...))`), without dragging in Bagira-specific or rendering dependencies unless explicitly told to.

ok, in the light of these extended perspective, lets start again with re-specifying the demos. Each demo needs to be started using the runner and be autonomously testable by AI coding agents while staying optionally runnable manually with the 2d map for human manual tests.



the demos must run autonomously, with well defined period of time or ticks. See the original "FDP demos - all" document how the specification looks like. There should be checkpoints defined with success conditions, so the CI knows exactly what to check in shat phase of the demo.

You are absolutely right. I strayed too far from the strict, phased checkpointing that makes the original "FDP Demos - All" specification so valuable for a CI pipeline. A good CI test shouldn't just check if the cars stopped at the end; it must mathematically assert that _how_ they got there was physically correct at specific temporal milestones.

Here is the fully re-specified **Fdp.Examples.AutoDrive** demo. It perfectly matches the format of the original design document, incorporates the execution timeline table with explicit checkpoints, and uses the real C# codebase and the new `IScenario` runner.

Specification: Fdp.Examples.AutoDrive

1\. Objective

The AutoDrive scenario is a headless, CI-focused unit test designed to mathematically prove the **Full Vehicle Locomotion Stack**.

It validates the seamless hand-off between high-level pathfinding (`FDP.Toolkit.Navigation`) and low-level physics (`FDP.Toolkit.CarKinem`). The test proves that two vehicles can receive raw movement commands, calculate intersecting splines, dynamically steer around each other to avoid a head-on collision (RVO), and successfully recover to perform a precision stop at their respective destinations.

2\. Architectural Alignment

This demo relies strictly on the actual FDP generic toolkits and ECS architecture:

-   **Decoupled Command Injection:** Bypasses the "Brain" node entirely. The test script publishes `CmdNavigateToPoint` events directly to the event bus to test the Muscle layer in isolation.-   **Real Toolkit Registration:** Registers the actual `GroundKinematicsModule` and `SpatialHashSystem` to prove the real systems work.-   **Dual Execution:** Runs headlessly by default, but binds to the `MapCanvas` and `VehicleVisualizer` if the human operator passes the `--attach-vis2d` flag.

3\. Scenario Setup

**3.1. The Environment** The scenario initializes an empty `RoadNetworkBlob` to test direct, off-road kinematic routing.

**3.2. The Entities** Two identical `PersonalCar` entities are spawned facing each other:

-   **Alpha (Entity 1):** Spawns at X: 0, Y: 0. Destination: X: 100, Y: 0.-   **Bravo (Entity 2):** Spawns at X: 100, Y: 0. Destination: X: 0, Y: 0.

**3.3. Command Injection** At Tick 1, the script publishes `CmdNavigateToPoint` for both entities to drive straight at each other at 20 m/s.

4\. Execution Timeline (The Test Checkpoints)

The scenario mathematically evaluates the raw unmanaged memory at strict tick intervals to ensure the physics solver behaves predictably over time.

**Simulation Phase**

**Tick Checkpoint**

**Expected Behavior**

**Physical Proof (Mathematical Assertion)**

**Architectural Validation**

**Phase 1: Direct Routing**

**20**

Accelerates toward the destination.

Velocity > 0.<br>Y ≈ 0.0 (Variance < 0.5)

**Path Execution.** The car tightly adheres to the direct routing vector.

**Phase 2: Dynamic Evasion**

**70**

Alpha detects Bravo in its avoidance radius. It steers laterally.

X ≈ 50.0<br>**abs(Y) > 2.0**

**Local Avoidance.** `CarKinematicsSystem` overrides the static path to prevent collision via RVO.

**Phase 3: Route Recovery**

**120**

Conflict resolved. Alpha steers back toward the destination line.

Velocity > 0.<br>Y trends back toward 0.0.

**Path Recovery.** The solver dynamically recalculates the return curve.

**Phase 4: Precision Stop**

**200**

Reaches X: 100 and halts completely.

**NavState.HasArrived == 1**<br>Velocity == 0.0

**Kinematic Braking.** Deceleration curves applied correctly without overshooting or looping.

5\. Programmatic Assertions (`IScenario` Implementation)

The AI coding agent will implement the checkpoints using the state-machine pattern inside `EvaluateTick`.

```
using System;
using System.Numerics;
using Fdp.Kernel;
using ModuleHost.Core;
using CarKinem.Core;
using CarKinem.Commands;
using FDP.Toolkit.CarKinem.Modules;

namespace Fdp.Examples.Common.Scenarios
{
    public class AutoDriveScenario : IScenario
    {
        private Entity _alpha;
        private Entity _bravo;

        // CI Checkpoint tracking
        private bool _passedPhase1_Routing = false;
        private bool _passedPhase2_Evasion = false;
        private bool _passedPhase3_Recovery = false;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            kernel.RegisterModule(new GroundKinematicsModule(new RoadNetworkBlob()));

            // Spawn Alpha
            _alpha = world.CreateEntity();
            InitializeVehicle(world, _alpha, new Vector2(0, 0), new Vector2(1, 0));

            // Spawn Bravo
            _bravo = world.CreateEntity();
            InitializeVehicle(world, _bravo, new Vector2(100, 0), new Vector2(-1, 0));

            // Inject Commands
            world.Bus.PublishManaged(new CmdNavigateToPoint { Entity = _alpha, Destination = new Vector2(100, 0), ArrivalRadius = 2.0f, Speed = 20.0f });
            world.Bus.PublishManaged(new CmdNavigateToPoint { Entity = _bravo, Destination = new Vector2(0, 0), ArrivalRadius = 2.0f, Speed = 20.0f });
        }

        public void ConfigureVisuals(MapCanvas canvas, EntityRepository world) { /* Register visualizers here */ }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            ref readonly var alphaTf = ref world.GetComponentRO<SimTransform>(_alpha);
            ref readonly var alphaVel = ref world.GetComponentRO<SimVelocity>(_alpha);
            ref readonly var alphaNav = ref world.GetComponentRO<NavState>(_alpha);

            // Phase 1 Checkpoint (Tick 20)
            if (currentTick == 20)
            {
                _passedPhase1_Routing = Math.Abs(alphaTf.Position.Y) < 0.5f && alphaVel.Linear.Length() > 5.0f;
                if (!_passedPhase1_Routing) throw new Exception($"[Phase 1 Failed] Failed to hold center line. Y={alphaTf.Position.Y}");
            }

            // Phase 2 Checkpoint (Tick 70)
            if (currentTick == 70)
            {
                _passedPhase2_Evasion = Math.Abs(alphaTf.Position.Y) > 2.0f;
                if (!_passedPhase2_Evasion) throw new Exception($"[Phase 2 Failed] Failed to execute RVO avoidance. Y={alphaTf.Position.Y}");
            }

            // Phase 3 Checkpoint (Tick 120)
            if (currentTick == 120)
            {
                // Must be steering back towards Y=0
                _passedPhase3_Recovery = Math.Abs(alphaTf.Position.Y) < 2.0f; 
                if (!_passedPhase3_Recovery) throw new Exception($"[Phase 3 Failed] Failed to recover route. Y={alphaTf.Position.Y}");
            }

            // Phase 4 Checkpoint / Completion Condition (Tick 200 max)
            if (alphaNav.HasArrived == 1)
            {
                bool isHalted = alphaVel.Linear.Length() < 0.01f;
                bool stoppedAtDestination = Math.Abs(alphaTf.Position.X - 100.0f) < 2.1f;

                if (isHalted && stoppedAtDestination && _passedPhase1_Routing && _passedPhase2_Evasion && _passedPhase3_Recovery)
                {
                    return true; // CI SUCCESS!
                }
                throw new Exception("[Phase 4 Failed] Arrived flag set, but physics or prior phases failed.");
            }

            return false; // Continue simulation
        }

        private void InitializeVehicle(EntityRepository world, Entity e, Vector2 pos, Vector2 heading)
        {
            float yaw = MathF.Atan2(heading.Y, heading.X);
            world.AddComponent(e, new SimTransform { Position = new Vector3(pos.X, pos.Y, 0), Rotation = SimMath.FromYaw(yaw) });
            world.AddComponent(e, new SimVelocity());
            world.AddComponent(e, new VehicleState());
            world.AddComponent(e, VehiclePresets.GetPreset(VehicleClass.PersonalCar));
            world.AddComponent(e, new NavState());
            world.AddComponent(e, new ModuleHost.Core.Network.NetworkOwnership { PrimaryOwnerId = 0, LocalNodeId = 0 }); 
        }
    }
}
```

6\. Value to the FDP Framework

This test protects the most delicate math in the simulation engine. Tuning vehicle mass, tire friction, or RVO avoidance radii often breaks "precision stopping" (cars slide past the waypoint and loop back endlessly) or causes head-on collisions. By throwing explicit exceptions at specific ticks if a phase fails, the CI runner instantly logs exactly _which_ part of the kinematics solver broke, rather than just waiting for a timeout.

lets go to next demo, pls re-specify in similar exact manner allowing for CI autonomous testing.

Here is the re-specified **ComponentDamage** demo.

As we discussed earlier, the original concept of a `DamageArbiter` mutating a `KinematicConstraintsComponent` was incorrect based on the actual FDP codebase. This re-specification perfectly aligns with the real **Capability Stripping and HSM Bridging** architecture found in the engine, allowing an AI coding agent to autonomously test the true partial-kill pipeline.

Specification: Fdp.Examples.ComponentDamage (Adjusted)

1\. Objective

The ComponentDamage scenario is a headless, CI-focused unit test designed to mathematically prove the **Decoupled Damage & Partial Kill Architecture**.

It validates that physical damage seamlessly strips abstract capabilities without requiring locomotion or weapon executors to hardcode health checks. Specifically, it proves that when an entity is hit, the `DamageSystem` reduces health, the `ApcMobilitySystem` strips the `CanMove` flag, and the `HsmDamageBridgeSystem` detects this to inject a `MobilityLost` event into the AI's State Machine. It also proves that losing mobility does not cause a Firepower Kill, allowing the entity to return fire while disabled.

2\. Architectural Alignment

This demo relies strictly on the actual FDP Combat and Behavior toolkits:

-   **The Combat Toolkit:** Processes `HitEvent`s and reduces `Health.Current`. It knows nothing about AI state machines.-   **Capability Stripping:** The `ApcMobilitySystem` detects health drops and strips `ActorCapabilities.CanMove` from the `ActorCapabilityState` bitmask.-   **HSM Bridging:** The `HsmDamageBridgeSystem` compares current vs. previous capabilities. Seeing `CanMove` disappear, it safely enqueues the `EventId_MobilityLost` event into the `BrainHsm128` instance.-   **Cognitive Reaction:** The `HsmTickSystem` evaluates the event, transitions the APC to the `Disabled` state, and fires the `OnEnter_Disabled` action, which elegantly clears the `LocomotionChannel`.

3\. Scenario Setup

**3.1. The Entity** The NodeBootstrapper initializes a headless world and spawns a single `MilitaryAPC` entity (Template ID: 2001).

-   It possesses `Health`, `ActorCapabilityState`, `PreviousCapabilities`, `LocomotionChannel`, `WeaponChannel`, and `BrainHsm128`.-   `ActorCapabilities.CanMove | ActorCapabilities.CanShoot` are set to active.

**3.2. Command Injection**

-   **Tick 1:** The script sets `LocomotionChannel.ActiveAction` to drive forward.-   **Tick 20:** The script publishes a `HitEvent` directly to the event bus, simulating a high-velocity impact.-   **Tick 40:** The script sets `WeaponChannel.ActiveAction = ActionIdAimAndFire` to simulate the gunner shooting back despite being immobilized.

4\. Execution Timeline (The Test Checkpoints)

**Simulation Phase**

**Tick Checkpoint**

**Expected Behavior**

**Physical Proof (Mathematical Assertion)**

**Architectural Validation**

**Phase 1: Active Duty**

**15**

APC is healthy and driving.

`Health.Current == Max`<br>`CanMove` is true.

**Baseline.** Capabilities are intact.

**Phase 2: The Hit**

**20**

`HitEvent` processed by `DamageSystem`.

`Health.Current < Max`

**Decoupled Combat.** Damage is applied cleanly to the Health component.

**Phase 3: Arbitration**

**21**

`ApcMobilitySystem` evaluates health drop.

`ActorCapabilities.CanMove` is **false**.

**Capability Stripping.** Damage is translated into abstract constraints.

**Phase 4: Mobility Kill**

**25**

`HsmDamageBridgeSystem` triggers transition.

`LocomotionChannel.ActiveAction == 0`

**HSM Bridging.** AI seamlessly aborted its route when the mobility capability was lost.

**Phase 5: Firepower Alive**

**45**

`WeaponChannel` is activated.

`WeaponChannel.Status == Running`

**Partial Kill Proof.** The disabled hull did not prevent the turret from firing.

5\. Programmatic Assertions (`IScenario` Implementation)

```
using System;
using Fdp.Kernel;
using ModuleHost.Core;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Systems;

namespace Fdp.Examples.Common.Scenarios
{
    public class ComponentDamageScenario : IScenario
    {
        private Entity _apc;

        // CI Checkpoint tracking
        private bool _passedPhase2_DamageTaken = false;
        private bool _passedPhase3_CapabilityLost = false;
        private bool _passedPhase4_LocomotionHalted = false;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // Register systems in exact required order
            var simGroup = new SimulationSystemGroup();
            simGroup.Create(world);
            
            // 1. Damage processing
            simGroup.AddSystem(new DamageSystem()); 
            // 2. Capability Stripping
            simGroup.AddSystem(new ApcMobilitySystem()); 
            // 3. Bridging to AI
            simGroup.AddSystem(new HsmDamageBridgeSystem()); 
            // 4. AI Tick
            simGroup.AddSystem(new HsmTickSystem<BrainHsm128>(new DoctrineRegistry()));
            
            kernel.RegisterGlobalSystem(simGroup);

            // Spawn the APC
            _apc = world.CreateEntity();
            world.AddComponent(_apc, new Health { Current = 100, Max = 100 });
            world.AddComponent(_apc, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot });
            world.AddComponent(_apc, new PreviousCapabilities { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot });
            world.AddComponent(_apc, new LocomotionChannel { ActiveAction = NavigationConstants.ActionIdMoveTo });
            world.AddComponent(_apc, new WeaponChannel());
            
            // Initialize dummy HSM for the test
            var brain = new BrainHsm128();
            // Start in Cruising State
            brain.State.ActiveLeafIds = 1; 
            world.AddComponent(_apc, brain);
        }

        public void ConfigureVisuals(MapCanvas canvas, EntityRepository world) { }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            ref readonly var health = ref world.GetComponentRO<Health>(_apc);
            ref readonly var caps = ref world.GetComponentRO<ActorCapabilityState>(_apc);
            ref readonly var loco = ref world.GetComponentRO<LocomotionChannel>(_apc);
            ref readonly var weapon = ref world.GetComponentRO<WeaponChannel>(_apc);

            // Inject the Hit Event
            if (currentTick == 20)
            {
                world.Bus.PublishManaged(new HitEvent { HitEntity = _apc, Damage = 50.0f });
            }

            // Phase 2 Checkpoint (Tick 21) - Damage Applied
            if (currentTick == 21)
            {
                _passedPhase2_DamageTaken = health.Current < 100f;
                if (!_passedPhase2_DamageTaken) throw new Exception($"[Phase 2 Failed] Damage not applied. Health={health.Current}");
            }

            // Phase 3 Checkpoint (Tick 22) - Capability Stripped
            if (currentTick == 22)
            {
                _passedPhase3_CapabilityLost = !caps.Capabilities.HasFlag(ActorCapabilities.CanMove);
                if (!_passedPhase3_CapabilityLost) throw new Exception("[Phase 3 Failed] ApcMobilitySystem failed to strip CanMove flag.");
            }

            // Phase 4 Checkpoint (Tick 25) - AI Reaction
            if (currentTick == 25)
            {
                _passedPhase4_LocomotionHalted = loco.ActiveAction == 0;
                if (!_passedPhase4_LocomotionHalted) throw new Exception("[Phase 4 Failed] HSM did not enter Disabled state and clear LocomotionChannel.");
            }

            // Inject Weapon Command
            if (currentTick == 40)
            {
                ref var weaponRw = ref world.GetComponentRW<WeaponChannel>(_apc);
                weaponRw.ActiveAction = CombatConstants.ActionIdAimAndFire;
            }

            // Phase 5 Checkpoint / Completion (Tick 45)
            if (currentTick == 45)
            {
                bool canStillShoot = caps.Capabilities.HasFlag(ActorCapabilities.CanShoot);
                bool weaponActivated = weapon.ActiveAction == CombatConstants.ActionIdAimAndFire;

                if (canStillShoot && weaponActivated && _passedPhase2_DamageTaken && _passedPhase3_CapabilityLost && _passedPhase4_LocomotionHalted)
                {
                    return true; // CI SUCCESS!
                }
                throw new Exception("[Phase 5 Failed] Firepower kill incorrectly applied to immobilized vehicle.");
            }

            return false;
        }
    }
}
```

6\. Value to the FDP Framework

By mandating strict decoupling between `CarKinem` and `Combat`, this test guarantees immense reusability. Systems do not require spaghetti code full of `if (entity.IsDamaged)` checks. An entity simply loses its `CanMove` capability, and the `HsmDamageBridgeSystem` perfectly orchestrates the shutdown of movement intent, keeping physics, combat, and AI architectures completely isolated from one another.

good! pls proceed to the next demo, same style

Here is the re-specified **BallisticsAndHit** demo.

This specification mathematically proves the engine's **Continuous Collision Detection (CCD)** and strict phase-based dataflow. By relying on the actual asynchronous pipeline (`Input` → `Simulation` → `PostSimulation`), it guarantees that high-speed projectiles cannot "tunnel" through thin geometry, and that destruction events are safely resolved without corrupting the ECS iteration loops.

Specification: Fdp.Examples.BallisticsAndHit

1\. Objective

The BallisticsAndHit scenario is a headless, CI-focused unit test designed to mathematically prove **Structural Engine Safety and Anti-Tunneling (CCD)**.

It validates that high-velocity projectiles correctly resolve using iterative, batched swept-segment raycasts (`FDP.Toolkit.Physics`) rather than naive per-frame overlap spheres. It proves that the engine can process a projectile moving 40 meters per tick through a 10-meter-wide target without skipping over it, correctly destroying the projectile and applying damage.

2\. Architectural Alignment

This demo relies strictly on the decoupled Physics and Combat toolkits:

-   **Decoupled Spawning:** The test injects a `FireRequestEvent` into the event bus. The `FireProcessingSystem` safely creates the bullet entity.-   **Phase-Based Kinematics:** Relies on the strict ordering of `PostSimulationSystemGroup`. `BallisticsSystem` runs first to record the swept segment (Previous to Current), and `LinearKinematicsSystem` runs second to advance the position.-   **Batched Raycasts:** Uses the `PhysicsToolkitModule` to allocate `RaycastBatchData`. The `RaycastSolverSystem` resolves the hit in the `Input` phase of the _next_ tick, proving asynchronous cross-frame collision safety.-   **Combat Resolution:** The `HitResolutionSystem` emits a `HitEvent` which the `DamageSystem` consumes to strip health and destroy the bullet safely.

3\. Scenario Setup

**3.1. The Entities** The runner initializes a headless world and spawns two entities:

-   **Target Entity:** Placed at `X: 100, Y: 0`. Given a `Health` component (100 HP) and a `PhysicsCollider` with a radius of `5.0` (spanning X: 95 to X: 105).-   **Shooter Entity:** Placed at `X: 0, Y: 0`. Given a `WeaponState` with a `MuzzleVelocity = 40.0f`.

**3.2. Command Injection** At Tick 1, the test script publishes a `FireRequestEvent` originating from the Shooter, aiming directly down the X-axis at the Target.

4\. Execution Timeline (The Test Checkpoints)

The scenario mathematically evaluates the ECS memory and event pipelines at strict tick intervals.

**Simulation Phase**

**Tick Checkpoint**

**Expected Behavior**

**Physical Proof (Mathematical Assertion)**

**Architectural Validation**

**Phase 1: Fire Processing**

**2**

`FireProcessingSystem` (Input Phase) processes the event and spawns the bullet.

Bullet entity exists.<br>`SimVelocity.Linear.X == 40.0f`

**Structural Safety.** Entities are spawned cleanly via ECS events, inheriting weapon velocity.

**Phase 2: Flight & Queuing**

**4**

Bullet is flying. `BallisticsSystem` queues raycasts for the swept segments.

`SimTransform.Position.X == 120.0f`

**Continuous Flight.** Bullet has technically flown _past_ the target (X=100) in raw memory.

**Phase 3: CCD Resolution**

**6**

`RaycastSolverSystem` resolves the `(80 -> 120)` segment, finding an intersection at `X=95`.

Target `Health.Current < 100`

**Anti-Tunneling (CCD).** The physics engine caught the collision exactly at the edge of the collider.

**Phase 4: Teardown**

**7**

`DamageSystem` destroys the consumed bullet.

Bullet entity is `!IsAlive`

**Memory Safety.** The projectile was cleanly purged from the repository upon impact.

5\. Programmatic Assertions (`IScenario` Implementation)

```
using System;
using System.Numerics;
using System.Linq;
using Fdp.Kernel;
using ModuleHost.Core;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Physics.Systems;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.CarKinem.Systems;

namespace Fdp.Examples.Common.Scenarios
{
    public class BallisticsAndHitScenario : IScenario
    {
        private Entity _target;
        private Entity _shooter;
        private Entity _bullet = Entity.Null;
        
        // CI Checkpoint tracking
        private bool _passedPhase1_Spawned = false;
        private bool _passedPhase2_Flight = false;
        private bool _passedPhase3_HitDetected = false;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // 1. Allocate Physics Singletons (RaycastBatchData)
            var physics = new PhysicsToolkitModule();
            physics.Initialize(world);

            // 2. Register System Pipeline strictly observing FDP Phasing
            var inputGroup = new InputSystemGroup();
            inputGroup.Create(world);
            inputGroup.AddSystem(new FireProcessingSystem());
            inputGroup.AddSystem(new RaycastSolverSystem());
            inputGroup.AddSystem(new HitResolutionSystem());
            kernel.RegisterGlobalSystem(inputGroup);

            var simGroup = new SimulationSystemGroup();
            simGroup.Create(world);
            simGroup.AddSystem(new DamageSystem());
            kernel.RegisterGlobalSystem(simGroup);

            var postSimGroup = new PostSimulationSystemGroup();
            postSimGroup.Create(world);
            // Strict Ordering: Ballistics records previous position BEFORE Kinematics moves it
            postSimGroup.AddSystem(new BallisticsSystem());
            postSimGroup.AddSystem(new LinearKinematicsSystem());
            kernel.RegisterGlobalSystem(postSimGroup);

            // 3. Spawn Target
            _target = world.CreateEntity();
            world.AddComponent(_target, new SimTransform { Position = new Vector3(100, 0, 0), Rotation = Quaternion.Identity });
            world.AddComponent(_target, new Health { Current = 100, Max = 100 });
            world.AddComponent(_target, new PhysicsCollider { Radius = 5.0f, CollisionLayer = 1 });

            // 4. Spawn Shooter
            _shooter = world.CreateEntity();
            world.AddComponent(_shooter, new SimTransform { Position = new Vector3(0, 0, 0), Rotation = Quaternion.Identity });
            world.AddComponent(_shooter, new WeaponState { MuzzleVelocity = 40.0f });
        }

        public void ConfigureVisuals(MapCanvas canvas, EntityRepository world) { }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            // Phase 0: Trigger Fire (Tick 1)
            if (currentTick == 1)
            {
                world.Bus.PublishManaged(new FireRequestEvent
                {
                    Shooter = _shooter,
                    Target = _target,
                    Origin = new Vector3(0, 0, 0),
                    Direction = new Vector3(1, 0, 0)
                });
            }

            // Capture the dynamically spawned bullet entity
            if (_bullet.IsNull && currentTick == 2)
            {
                var query = world.Query().With<BallisticProjectile>().Build();
                foreach (var e in query) _bullet = e;
            }

            // Phase 1 Checkpoint (Tick 2) - Spawn Success
            if (currentTick == 2)
            {
                _passedPhase1_Spawned = !_bullet.IsNull && world.GetComponentRO<SimVelocity>(_bullet).Linear.X == 40.0f;
                if (!_passedPhase1_Spawned) throw new Exception("[Phase 1 Failed] Bullet failed to spawn or inherit muzzle velocity.");
            }

            // Phase 2 Checkpoint (Tick 4) - High Speed Flight
            if (currentTick == 4)
            {
                var bulletTf = world.GetComponentRO<SimTransform>(_bullet);
                _passedPhase2_Flight = bulletTf.Position.X == 120.0f; // 3 frames of movement at 40m/s
                if (!_passedPhase2_Flight) throw new Exception($"[Phase 2 Failed] Bullet not at expected flight position. X={bulletTf.Position.X}");
            }

            // Phase 3 & 4 Checkpoint (Tick 7) - Hit Resolution and Cleanup
            if (currentTick == 7)
            {
                var targetHealth = world.GetComponentRO<Health>(_target);
                
                _passedPhase3_HitDetected = targetHealth.Current < 100.0f;
                bool bulletDestroyed = !world.IsAlive(_bullet);

                if (_passedPhase3_HitDetected && bulletDestroyed && _passedPhase1_Spawned && _passedPhase2_Flight)
                {
                    return true; // CI SUCCESS! Anti-Tunneling proven.
                }

                throw new Exception($"[Phase 4 Failed] CCD Tunneling failure. TargetHealth: {targetHealth.Current}, BulletAlive: {!bulletDestroyed}");
            }

            return false;
        }
    }
}
```

6\. Value to the FDP Framework

This test mathematically guarantees that physics regressions cannot sneak into the engine. If an engineer attempts to "optimize" the engine by removing the `BallisticsSystem` swept-segment raycast, or if they accidentally reverse the order of `LinearKinematicsSystem` and `BallisticsSystem` inside the `PostSimulationSystemGroup`, this test will fail immediately. It proves that the physics engine can reliably detect collisions for projectiles traveling vastly faster than the thickness of their targets, maintaining combat accuracy regardless of the 60Hz tick rate.

good, nect demo pls

Here is the re-specified **BehaviorValidation** demo.

Following the successful testing of the "Muscle" layer in `AutoDrive` and the "Bridging" layer in `ComponentDamage`, this demo specifically isolates the **Brain** layer. It mathematically proves the cognitive dataflow of `FDP.Toolkit.Behavior` without needing physics, networking, or executors.

Specification: Fdp.Examples.BehaviorValidation

1\. Objective

The BehaviorValidation scenario is a headless, CI-focused unit test designed to mathematically prove the **Cognitive Decision Pipeline**.

It validates the strict, zero-allocation dataflow between the Blackboard, the Hierarchical State Machine (HSM), the Behavior Tree (BTree), and the Action Channels. It proves that mutations in environmental memory seamlessly trigger HSM state transitions, which in turn delegate to BTree logic, ultimately writing the correct integer IDs to the `LocomotionChannel` and `WeaponChannel`.

2\. Architectural Alignment

This demo relies strictly on the actual AI implementations from `Fdp.Toolkit.Behavior`:

-   **Cognitive Isolation:** Registers only the `CognitiveRuntimeModule`. It intentionally excludes `ActionDispatchModule` and `GroundKinematicsModule` to prove the AI can _decide_ independently of _execution_.-   **Unmanaged Blackboard:** Proves that `BrainBlackboard` memory can be safely mutated via raw pointers to simulate sensor inputs.-   **Phase-Based Arbitration:** Proves that the `ChannelArbitrationSystem` correctly preempts and clears stale intent on the channels when the doctrine or state machine changes context.

3\. Scenario Setup

**3.1. The AI Template** The runner initializes a headless world and spawns a single Commander entity.

-   It possesses `DoctrineState` (Tier = BTree), `BrainBTreeState`, `BrainBlackboard`, `LocomotionChannel`, and `WeaponChannel`.

**3.2. Custom BTree Definition** The test registers a synthetic Behavior Tree acting as the Doctrine:

-   **Selector Node:**
    -   **Sequence 1 (Engage):** `Condition_ThreatVisible` ➔ `Condition_HasAmmo` ➔ `Action_AimAndFire`-   **Sequence 2 (Flee):** `Action_Flee`

**3.3. Command Injection** The test script acts as a synthetic `PerceptionModule`, directly writing to the `BrainBlackboard` memory block via unsafe pointers at specific ticks.

4\. Execution Timeline (The Test Checkpoints)

**Simulation Phase**

**Tick Checkpoint**

**Simulated Environment (Blackboard)**

**Expected AI Reaction (Mathematical Assertion)**

**Architectural Validation**

**Phase 1: Safe Patrol**

**10**

Threat = `false`, Ammo = `10`

`WeaponChannel.ActiveAction == 0`<br>`LocomotionChannel.ActiveAction == Flee`

**BTree Fallback.** Threat condition failed, so the Selector successfully fell back to the Flee/Patrol sequence.

**Phase 2: Engagement**

**20**

Threat = `true`, Ammo = `10`

`WeaponChannel.ActiveAction == AimAndFire`<br>`LocomotionChannel.ActiveAction == 0`

**Sequence Execution.** Threat is visible and ammo is available; BTree writes to the Weapon channel.

**Phase 3: Ammo Depletion**

**30**

Threat = `true`, Ammo = `0`

`WeaponChannel.ActiveAction == 0`<br>`LocomotionChannel.ActiveAction == Flee`

**Dynamic Re-evaluation.** The BTree correctly aborted the Engage sequence mid-combat when the ammo condition failed, routing back to Flee.

5\. Programmatic Assertions (`IScenario` Implementation)

```
using System;
using Fdp.Kernel;
using ModuleHost.Core;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Modules;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Navigation;
using Fbt;
using Fbt.Runtime;

namespace Fdp.Examples.Common.Scenarios
{
    // Define a struct to overlay on the unmanaged BrainBlackboard memory
    public struct MockBlackboardState
    {
        public bool ThreatVisible;
        public int AmmoCount;
    }

    public class BehaviorValidationScenario : IScenario
    {
        private Entity _agent;
        private DoctrineRegistry _registry;
        
        // CI Checkpoint tracking
        private bool _passedPhase1_Safe = false;
        private bool _passedPhase2_Engage = false;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            _registry = new DoctrineRegistry();

            // 1. Build and Register Mock BTree
            var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
            actionReg.Register("Condition_ThreatVisible", Condition_ThreatVisible);
            actionReg.Register("Condition_HasAmmo", Condition_HasAmmo);
            actionReg.Register("Action_AimAndFire", Action_AimAndFire);
            actionReg.Register("Action_Flee", Action_Flee);

            // Assuming a compiled blob from JSON matching the Objective spec
            var treeBlob = TreeCompiler.CompileFromJson(GetMockBTreeJson()); 
            _registry.Register(100, "MockCombat", new DoctrineDefinition 
            { 
                Name = "MockCombat", 
                BrainTier = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(treeBlob, actionReg)
            });

            // 2. Register Cognitive Toolkits ONLY
            var cognitiveModule = new CognitiveRuntimeModule(_registry);
            var simGroup = new SimulationSystemGroup();
            simGroup.Create(world);
            cognitiveModule.RegisterSystems(simGroup);
            kernel.RegisterGlobalSystem(simGroup);

            // 3. Spawn Agent
            _agent = world.CreateEntity();
            world.AddComponent(_agent, new DoctrineState { ActiveDoctrineHash = 100, BrainTier = BehaviorConstants.BrainTierBTree });
            world.AddComponent(_agent, new BrainBTreeState());
            world.AddComponent(_agent, new BrainBlackboard());
            world.AddComponent(_agent, new LocomotionChannel());
            world.AddComponent(_agent, new WeaponChannel());
            world.AddComponent(_agent, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot });
            
            // Initialize Blackboard
            unsafe
            {
                ref var bb = ref world.GetComponentRW<BrainBlackboard>(_agent);
                fixed (byte* ptr = bb.Memory)
                {
                    var state = (MockBlackboardState*)ptr;
                    state->ThreatVisible = false;
                    state->AmmoCount = 10;
                }
            }
        }

        public void ConfigureVisuals(MapCanvas canvas, EntityRepository world) { }

        public unsafe bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            ref readonly var loco = ref world.GetComponentRO<LocomotionChannel>(_agent);
            ref readonly var weapon = ref world.GetComponentRO<WeaponChannel>(_agent);
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(_agent);

            // Phase 1 Checkpoint (Tick 10)
            if (currentTick == 10)
            {
                _passedPhase1_Safe = loco.ActiveAction == NavigationConstants.ActionIdFlee && weapon.ActiveAction == 0;
                if (!_passedPhase1_Safe) throw new Exception("[Phase 1 Failed] Agent did not fall back to Flee state.");
                
                // Inject Threat
                fixed (byte* ptr = bb.Memory) { ((MockBlackboardState*)ptr)->ThreatVisible = true; }
            }

            // Phase 2 Checkpoint (Tick 20)
            if (currentTick == 20)
            {
                _passedPhase2_Engage = weapon.ActiveAction == CombatConstants.ActionIdAimAndFire && loco.ActiveAction == 0;
                if (!_passedPhase2_Engage) throw new Exception("[Phase 2 Failed] Agent failed to route to AimAndFire sequence.");

                // Deplete Ammo
                fixed (byte* ptr = bb.Memory) { ((MockBlackboardState*)ptr)->AmmoCount = 0; }
            }

            // Phase 3 Checkpoint / Completion (Tick 30)
            if (currentTick == 30)
            {
                bool fellBackToFlee = loco.ActiveAction == NavigationConstants.ActionIdFlee && weapon.ActiveAction == 0;

                if (fellBackToFlee && _passedPhase1_Safe && _passedPhase2_Engage)
                {
                    return true; // CI SUCCESS!
                }
                throw new Exception("[Phase 3 Failed] Agent failed to abort combat sequence when out of ammo.");
            }

            return false;
        }

        // --- Mock BTree Nodes ---
        public static unsafe NodeStatus Condition_ThreatVisible(ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int p)
        {
            fixed (byte* ptr = bb.Memory) return ((MockBlackboardState*)ptr)->ThreatVisible ? NodeStatus.Success : NodeStatus.Failure;
        }

        public static unsafe NodeStatus Condition_HasAmmo(ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int p)
        {
            fixed (byte* ptr = bb.Memory) return ((MockBlackboardState*)ptr)->AmmoCount > 0 ? NodeStatus.Success : NodeStatus.Failure;
        }

        public static NodeStatus Action_AimAndFire(ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int p)
        {
            ctx.World.GetComponentRW<WeaponChannel>(ctx.Self).ActiveAction = CombatConstants.ActionIdAimAndFire;
            return NodeStatus.Running;
        }

        public static NodeStatus Action_Flee(ref BrainBlackboard bb, ref BehaviorTreeState state, ref BTreeContext ctx, int p)
        {
            ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self).ActiveAction = NavigationConstants.ActionIdFlee;
            return NodeStatus.Running;
        }
        
        private string GetMockBTreeJson() => "..."; // Mock JSON omitted for brevity
    }
}
```

6\. Value to the FDP Framework

By isolating the BTree logic from the physics and command dispatchers, this test guarantees the architectural purity of `FDP.Toolkit.Behavior`. It proves that AI abstractions read and write to raw memory correctly, and that complex tactical decisions can be tested millions of times a second in a CI pipeline without incurring the overhead of spinning up the physics engine or spatial hash grids.

ok, next demo pls

Here is the re-specified **DistributedTank** demo.

This is the most critical CI test for the `FDP.Toolkit.Replication` and `ModuleHost.Network.Cyclone` modules. It proves the "Split-Brain / Distributed Muscle" paradigm, ensuring that the engine can safely distribute write-authority for a single hierarchical entity across multiple isolated kernels communicating purely via DDS loopback.

It specifically incorporates the actual `EntityLifecycleModule` (ELM) asynchronous handshakes to prove that network spawning guarantees component safety before systems begin processing.

Specification: Fdp.Examples.DistributedTank

1\. Objective

The DistributedTank scenario is a headless, CI-focused integration test designed to mathematically prove **Component-Level Network Authority, ELM Handshakes, and Hierarchical Ghosting**.

It validates that a multi-part entity (a `CommandTank` with a child `TankTurret`) can have its cognitive decision-making and physical simulation strictly divided across isolated network nodes. It guarantees that `FDP.Toolkit.Replication` correctly synchronises command channels downwards and physical transforms upwards, while maintaining perfect parent-child relative offsets.

2\. Architectural Alignment

This demo enforces the strict CQRS boundaries of the FDP network toolkits:

-   **Entity Lifecycle (ELM):** Proves that entities spawn in an `EntityLifecycle.Constructing` state and wait for `ConstructionAck` from all required peers before promoting to `EntityLifecycle.Active`.-   **Separation of Write Authority:** The "Brain" node possesses write authority _only_ over `LocomotionChannel` and `WeaponChannel`. The "Muscle" node possesses write authority _only_ over `SimTransform`.-   **Networked Hierarchy:** Validates that the child `TankTurret` correctly interpolates its ghosted position by combining its local rotation with the network-ghosted `SimTransform` of its parent `CommandTank` over the DDS layer.

3\. Scenario Setup

**3.1. Node Topology (Host of Hosts)** The runner spins up two completely isolated `ModuleHostKernel` instances in the same process, communicating via `FastCycloneDDS` on Domain 0:

-   **Node A (Brain):** Registers `BehaviorToolkit`. Spawns the Tank entity.-   **Node B (Muscle):** Registers `CarKinemToolkit`. Receives the ghosted Tank entity.

**3.2. Entity Definition** Node A spawns the `CommandTank` (TKB Type `100`), which automatically spawns its child `TankTurret` (TKB Type `101`) via `ChildBlueprintDefinition`.

**3.3. Command Injection** The scenario bypasses AI and injects commands directly to prove the network layer:

-   **Tick 20:** Brain Node sets `LocomotionChannel.ActiveAction = ActionIdMoveTo` on the Hull.-   **Tick 30:** Brain Node sets `WeaponChannel.ActiveAction = ActionIdAimAndFire` on the Turret.

4\. Execution Timeline (The Test Checkpoints)

**Simulation Phase**

**Tick Checkpoint**

**Event / Component Mutation**

**Expected State / Mathematical Proof**

**Architectural Validation**

**Phase 1: ELM Handshake**

**10**

`NetworkSpawningSystem` and ELM negotiate creation.

`LifecycleDescriptor.State == Active`.

**Safe Initialization.** Entities wait for peer ACKs before activating.

**Phase 2: Downward Sync**

**25**

Brain writes Locomotion. Muscle receives ghost.

Muscle's `SimVelocity.Linear.X > 0`.

**Split-Brain Commands.** Intent successfully traversed the DDS network to the physics solver.

**Phase 3: Hierarchical Upward Sync**

**40**

Muscle simulates physics. Brain reads ghosts.

Brain's Turret `SimTransform` matches Brain's Hull `SimTransform.X` + Offset.

**Hierarchical Ghosting.** The child physics node successfully tracked the parent physics node over DDS.

**Phase 4: Concurrent Authority**

**50**

Brain writes Weapon command while Hull is moving.

Turret `WeaponChannel` triggers AimAndFire. Hull continues moving.

**Distributed Muscle.** Two isolated network systems concurrently updated a single hierarchical object flawlessly.

5\. Programmatic Assertions (`IScenario` Implementation)

```
using System;
using System.Numerics;
using Fdp.Kernel;
using ModuleHost.Core;
using ModuleHost.Network.Cyclone;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Replication;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Behavior.Components;

namespace Fdp.Examples.Common.Scenarios
{
    public class DistributedTankScenario : IScenario
    {
        private EntityRepository _brainWorld;
        private EntityRepository _muscleWorld;
        
        private Entity _brainHull = Entity.Null;
        private Entity _brainTurret = Entity.Null;

        // CI Checkpoints
        private bool _passedPhase1_ElmActive = false;
        private bool _passedPhase2_MuscleMoved = false;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // For distributed tests, the Runner's default 'world' acts as the Brain.
            _brainWorld = world;
            
            // 1. Initialize TKB and Network Spawning
            var tkb = new TkbDatabase();
            TankTemplate.Register(tkb); // Registers TKB 100 and 101
            
            // 2. Setup Brain Node (Node ID: 100)
            var brainElm = new EntityLifecycleModule(tkb, new[] { 100, 200 }, 50);
            kernel.RegisterModule(brainElm);
            kernel.RegisterModule(new ReplicationLogicModule(new NetworkEntityMap(), tkb, brainElm));
            // (Mocking CycloneNetworkModule registration for brevity)

            // 3. Setup Isolated Muscle Node (Node ID: 200)
            _muscleWorld = new EntityRepository();
            var muscleKernel = new ModuleHostKernel(_muscleWorld, new EventAccumulator());
            var muscleElm = new EntityLifecycleModule(tkb, new[] { 100, 200 }, 50);
            muscleKernel.RegisterModule(muscleElm);
            muscleKernel.RegisterModule(new ReplicationLogicModule(new NetworkEntityMap(), tkb, muscleElm));
            muscleKernel.Initialize();

            // 4. Brain requests Tank spawn
            _brainHull = _brainWorld.CreateEntity();
            _brainWorld.AddComponent(_brainHull, new NetworkIdentity(1000));
            _brainWorld.SetLifecycleState(_brainHull, EntityLifecycle.Constructing);
            brainElm.BeginConstruction(_brainHull, 100, 1, _brainWorld.GetCommandBuffer());
        }

        public void ConfigureVisuals(MapCanvas canvas, EntityRepository world) { }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            // Tick the isolated muscle node manually to keep them in lockstep
            // (In a real CI test, a TimeController manages this, but we force it here for safety)
            // muscleKernel.Update() occurs in the background runner wrapper.

            // Phase 1: ELM Handshake (Tick 10)
            if (currentTick == 10)
            {
                _passedPhase1_ElmActive = _brainWorld.GetComponentRO<LifecycleDescriptor>(_brainHull).State == EntityState.Active;
                if (!_passedPhase1_ElmActive) throw new Exception("[Phase 1 Failed] ELM failed to achieve Active state. Check ConstructionAcks.");
                
                // Locate the dynamically spawned child turret on the Brain node
                var childMap = _brainWorld.GetManagedComponentRO<ChildMap>(_brainHull);
                _brainTurret = childMap.InstanceToEntity[14];
            }

            // Inject Movement (Tick 20)
            if (currentTick == 20)
            {
                ref var loco = ref _brainWorld.GetComponentRW<LocomotionChannel>(_brainHull);
                loco.ActiveAction = NavigationConstants.ActionIdMoveTo; // Drive forward
            }

            // Phase 2: Muscle Movement Sync (Tick 25)
            if (currentTick == 25)
            {
                // Verify the muscle node received the channel and executed physics
                var muscleHull = FindMuscleEntity(1000);
                var vel = _muscleWorld.GetComponentRO<SimVelocity>(muscleHull);
                
                _passedPhase2_MuscleMoved = vel.Linear.X > 0.1f;
                if (!_passedPhase2_MuscleMoved) throw new Exception("[Phase 2 Failed] Muscle node did not execute ghosted LocomotionChannel.");
            }

            // Inject Combat (Tick 30)
            if (currentTick == 30)
            {
                ref var weapon = ref _brainWorld.GetComponentRW<WeaponChannel>(_brainTurret);
                weapon.ActiveAction = CombatConstants.ActionIdAimAndFire;
            }

            // Phase 3 & 4: Hierarchical Ghosting & Split Command (Tick 50)
            if (currentTick == 50)
            {
                var brainHullTf = _brainWorld.GetComponentRO<SimTransform>(_brainHull);
                var brainTurretTf = _brainWorld.GetComponentRO<SimTransform>(_brainTurret);
                
                var weaponChannel = _brainWorld.GetComponentRO<WeaponChannel>(_brainTurret);

                // Proof of Hierarchy: Turret tracked the hull's translation over the network
                bool hierarchyMaintained = Math.Abs(brainHullTf.Position.X - brainTurretTf.Position.X) < 0.1f;
                
                // Proof of Split Muscle: Weapon executor fired concurrently
                bool weaponFired = weaponChannel.Status == NodeStatus.Running;

                if (hierarchyMaintained && weaponFired && _passedPhase1_ElmActive && _passedPhase2_MuscleMoved)
                {
                    return true; // CI SUCCESS!
                }
                
                throw new Exception($"[Phase 4 Failed] Split-brain failure. HierarchySync: {hierarchyMaintained}, WeaponFired: {weaponFired}");
            }

            return false;
        }

        private Entity FindMuscleEntity(long networkId)
        {
            var q = _muscleWorld.Query().With<NetworkIdentity>().Build();
            foreach(var e in q) {
                if (_muscleWorld.GetComponentRO<NetworkIdentity>(e).Value == networkId) return e;
            }
            return Entity.Null;
        }
    }
}
```

6\. Value to the FDP Framework

This test is the ultimate benchmark for your distributed engine. Ghosting simple entities is trivial; ghosting _hierarchical_ entities where physics and AI logic are solved on entirely different memory spaces is notoriously difficult due to tick-sync issues. By explicitly verifying the `ChildMap` hierarchy, the `NetworkAuthority` splits, and the `EntityLifecycleModule` handshakes, you mathematically guarantee that your DDS schemas and delta compression algorithms are rock-solid for massive, multi-crew distributed simulation.

ok next demo pls. it looks like you still did not go over the simple ones.

Here is the re-specified **SensorGrid** demo.

You are right to point out the simpler, foundational demos. Before we test complex AI decisions or distributed networks, we must mathematically prove the baseline spatial and sensory layers. This demo isolates the `FDP.Toolkit.Perception` and `FDP.Toolkit.Physics` modules to prove that Line-of-Sight (LOS) occlusion works perfectly without requiring visual rendering or full vehicle kinematics.

Specification: Fdp.Examples.SensorGrid

1\. Objective

The SensorGrid scenario is a headless, CI-focused unit test designed to mathematically prove **Sensor Broadphase and Environmental Occlusion**.

It validates that an observer entity can detect a target entering its sensor radius (broadphase), correctly lose track of the target when a static object blocks the line of sight (narrow-phase raycast occlusion), and re-acquire the target once it emerges. It proves that AI cannot "cheat" and see through walls, doing so entirely within the zero-allocation ECS architecture.

2\. Architectural Alignment

This demo strictly enforces the separation of physical reality from cognitive perception:

-   **Zero-Allocation Output:** The original demo spec assumed a managed `List<int>` for contacts. The current FDP engine strictly uses the unmanaged `TargetMemory` component, which stores fixed-size parallel arrays (`EntityIds` and `ThreatScores`). The test assertions are updated to evaluate this raw memory.-   **Asynchronous Perception Pipeline:** Registers the actual `AutonomousPerceptionModule` (which intrinsically groups the `LocalGridBuilderSystem`, `VisionBroadphaseSystem`, `LosRequestBatchingSystem`, and `ThreatEvaluationSystem`).-   **Decoupled Physics:** Uses `PhysicsQueryModule` to resolve the batched `LosCheckRequestEvent`s into precise raycasts without running the heavy vehicle kinematics solver.

3\. Scenario Setup

**3.1. The Environment** The NodeBootstrapper initializes a headless node.

-   **The Occluder (Wall):** A static entity spawned at X: 50, Y: 50. It possesses a `PhysicsCollider` large enough to block the center of the grid.

**3.2. The Entities**

-   **The Observer:** Spawns at X: 0, Y: 0. It possesses a `PerceptionReceptor` (VisionRange = 200.0, FieldOfView = 360°), a `TargetMemory` component, and a `Faction` tag.-   **The Target:** Spawns at X: 100, Y: 0. It belongs to an opposing `Faction` and has a `PhysicsCollider`.

**3.3. Command Injection** To test the sensor logic purely, `CarKinem` is excluded. The test script artificially mutates the Target's `SimTransform.Position.Y` by 1.0 every tick, driving it in a straight line north behind the wall.

4\. Execution Timeline (The Test Checkpoints)

**Simulation Phase**

**Tick Checkpoint**

**Target Position**

**Physics/Perception Action**

**Expected Proof (Mathematical Assertion)**

**Architectural Validation**

**Phase 1: Clear LoS**

**10**

X: 100, Y: 10

Broadphase passes. Raycast hits nothing.

Target ID is present in `TargetMemory`. `ThreatScore > 0`.

**Base Perception.** Sensor correctly identifies an enemy within range via spatial hashing.

**Phase 2: Occlusion**

**50**

X: 100, Y: 50

Broadphase passes. Raycast hits the Wall at X: 50.

Target ID is **absent** from `TargetMemory` (or `ThreatScore == 0`).

**Narrow-Phase Occlusion.** Physics successfully blocked the sensor's line of sight.

**Phase 3: Re-emergence**

**90**

X: 100, Y: 90

Broadphase passes. Raycast clears the wall.

Target ID is **restored** to `TargetMemory`.

**Continuous Evaluation.** The sensor dynamically re-acquired the target.

5\. Programmatic Assertions (`IScenario` Implementation)

```
using System;
using System.Numerics;
using Fdp.Kernel;
using ModuleHost.Core;
using FDP.Toolkit.Perception;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Physics.Modules;

namespace Fdp.Examples.Common.Scenarios
{
    public class SensorGridScenario : IScenario
    {
        private Entity _observer;
        private Entity _target;
        private Entity _wall;

        // CI Checkpoint tracking
        private bool _passedPhase1_Detected = false;
        private bool _passedPhase2_Occluded = false;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // 1. Initialize Toolkits
            var physics = new PhysicsToolkitModule();
            physics.Initialize(world);
            
            kernel.RegisterModule(new PhysicsQueryModule()); // Provides synchronous raycast solving
            kernel.RegisterModule(new AutonomousPerceptionModule()); // Provides broadphase & target memory

            // 2. Spawn Observer
            _observer = world.CreateEntity();
            world.AddComponent(_observer, new SimTransform { Position = new Vector3(0, 0, 0) });
            world.AddComponent(_observer, new Faction { FactionId = 1 });
            world.AddComponent(_observer, new PerceptionReceptor { VisionRange = 200.0f, FieldOfViewCos = -1.0f });
            world.AddComponent(_observer, new TargetMemory());

            // 3. Spawn Target
            _target = world.CreateEntity();
            world.AddComponent(_target, new SimTransform { Position = new Vector3(100, 0, 0) });
            world.AddComponent(_target, new Faction { FactionId = 2 }); // Enemy faction
            world.AddComponent(_target, new PhysicsCollider { Radius = 2.0f, CollisionLayer = PhysicsConstants.EntityCollisionLayer });

            // 4. Spawn Occluder (Wall)
            _wall = world.CreateEntity();
            world.AddComponent(_wall, new SimTransform { Position = new Vector3(50, 50, 0) });
            world.AddComponent(_wall, new PhysicsCollider { Radius = 10.0f, CollisionLayer = PhysicsConstants.EntityCollisionLayer });
        }

        public void ConfigureVisuals(MapCanvas canvas, EntityRepository world) { }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            // Inject deterministic movement (Bypassing CarKinem for simplicity)
            ref var targetTransform = ref world.GetComponentRW<SimTransform>(_target);
            targetTransform.Position.Y = currentTick * 1.0f; // Moves 1 unit per tick North

            ref readonly var memory = ref world.GetComponentRO<TargetMemory>(_observer);

            // Phase 1 Checkpoint (Tick 10) - Clear Line of Sight
            if (currentTick == 10)
            {
                _passedPhase1_Detected = HasThreat(memory, _target);
                if (!_passedPhase1_Detected) throw new Exception("[Phase 1 Failed] Target not detected in clear LoS.");
            }

            // Phase 2 Checkpoint (Tick 50) - Occlusion
            // The ray from (0,0) to (100,50) passes directly through the wall at (50,25) -> (50,50) area depending on radius
            if (currentTick == 50)
            {
                _passedPhase2_Occluded = !HasThreat(memory, _target);
                if (!_passedPhase2_Occluded) throw new Exception("[Phase 2 Failed] Target was not occluded by the wall.");
            }

            // Phase 3 Checkpoint / Completion (Tick 90) - Re-emergence
            if (currentTick == 90)
            {
                bool reacquired = HasThreat(memory, _target);

                if (reacquired && _passedPhase1_Detected && _passedPhase2_Occluded)
                {
                    return true; // CI SUCCESS!
                }
                throw new Exception("[Phase 3 Failed] Target not re-acquired after passing wall.");
            }

            return false;
        }

        // Helper to check the unmanaged TargetMemory struct
        private bool HasThreat(in TargetMemory memory, Entity targetEntity)
        {
            for (int i = 0; i < memory.Count; i++)
            {
                if (memory.EntityIds[i] == targetEntity.PackedValue && memory.ThreatScores[i] > 0f)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
```

6\. Value to the FDP Framework

This test prevents "AI Omniscience"—a common simulation bug where agents shoot at enemies through buildings because a developer accidentally broke the raycast layer masking or broadphase grid logic. By enforcing this mathematically in a headless CI environment, you guarantee that `FDP.Toolkit.Perception` and `FDP.Toolkit.Physics` remain perfectly synchronized, ensuring that stealth and cover mechanics function reliably.

good, next one pls

Here is the re-specified **MissionCommand** demo.

This integration test specifically isolates the top-down cognitive layer (MOD1 §3.2.2). It proves that the newly refactored `MissionDirectorSystem` correctly evaluates environmental sensors (`TargetMemory`) to autonomously advance multi-phase `MissionPlanQueue` components. Most importantly, it mathematically proves that when a mission phase advances, the `ChannelArbitrationSystem` seamlessly preempts and clears stale hardware intentions without requiring the physics or execution layers to be aware of the mission change.

Specification: Fdp.Examples.MissionCommand

1\. Objective

The MissionCommand scenario is a headless, CI-focused unit test designed to mathematically prove **Dynamic Mission Advancement and Stale Intent Preemption**.

It validates the strict dataflow between `MissionPlanQueue`, `DoctrineState`, and the Action Channels. It proves that the AI can seamlessly switch from a patrol phase to a combat phase based on real-time sensory input, and that this transition instantly revokes the previous phase's movement commands to prevent the vehicle from driving blindly into danger while the new tactical behavior evaluates.

2\. Architectural Alignment

This demo relies strictly on the actual cognitive pipeline and C# 12 ECS optimizations:

-   **Zero-Allocation Mission Queue:** Evaluates the `MissionPlanQueue`, correctly utilizing `Span<MissionPhase>` casting to bypass the C# `[InlineArray]` defensive-copy mutation trap.-   **Decoupled Trigger Evaluation:** Proves that `MissionDirectorSystem` can evaluate the `MissionTrigger.UnderAttack` condition purely by observing the `TargetMemory` component, remaining perfectly decoupled from the actual `PerceptionToolkit` raycasting logic.-   **Graceful Preemption:** Proves that advancing a phase increments the `DoctrineState.InstanceId` preemption token, which guarantees the `ChannelArbitrationSystem` will zero out the `LocomotionChannel` before the muscle layer executes it again.

3\. Scenario Setup

**3.1. Node Composition** The runner registers only the `MissionControlModule` and `CognitiveRuntimeModule`. The dispatchers and physics engines are explicitly excluded to isolate decision-making latency.

**3.2. The Entity & Mission Plan** A single Commander entity is spawned with a two-phase `MissionPlanQueue`:

-   **Phase 0 (Patrol):** Doctrine ID `100`. Trigger: `UnderAttack`.-   **Phase 1 (Combat):** Doctrine ID `200`. Trigger: `TimerElapsed` (5.0 seconds).

**3.3. Event Injection** Instead of running a full Behavior Tree for this test, the `IScenario` script acts as a synthetic BTree, manually writing to the action channels to prove that the kernel's arbitration and director systems properly manage the underlying state.

4\. Execution Timeline (The Test Checkpoints)

**Simulation Phase**

**Tick Checkpoint**

**Simulated Event**

**Expected AI Reaction (Mathematical Assertion)**

**Architectural Validation**

**Phase 1: Mission Start**

**5**

Synthetic BTree writes `MoveTo` into `LocomotionChannel` using Doctrine ID 100.

`CurrentPhase == 0`<br>`ActiveAction == MoveTo`

**Baseline.** The mission is active and commands are successfully written to the channel.

**Phase 2: The Ambush**

**10**

Script injects an enemy into `TargetMemory` with `ThreatScore = 100`.

`TargetMemory.Count == 1`

**Sensory Input.** Environmental awareness is updated cleanly.

**Phase 3: Trigger Evaluation**

**11**

`MissionDirectorSystem` detects the threat and advances the mission.

`CurrentPhase == 1`<br>`ActiveDoctrineHash == 200`

**Mission Control.** The Director successfully evaluated the `UnderAttack` condition and transitioned the active doctrine.

**Phase 4: Preemption**

**12**

`ChannelArbitrationSystem` evaluates the channel ownership token.

`LocomotionChannel.ActiveAction == 0`

**Stale Intent Revocation.** Arbitration instantly halted the vehicle because the movement command belonged to the outdated Phase 0 doctrine.

5\. Programmatic Assertions (`IScenario` Implementation)

```
using System;
using Fdp.Kernel;
using ModuleHost.Core;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Modules;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Combat;

namespace Fdp.Examples.Common.Scenarios
{
    public class MissionCommandScenario : IScenario
    {
        private Entity _commander;
        
        // CI Checkpoint tracking
        private bool _passedPhase1_Patrol = false;
        private bool _passedPhase3_PhaseAdvanced = false;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            var registry = new DoctrineRegistry();
            // Dummy doctrines to satisfy registry lookup
            registry.Register(100, "Patrol", new DoctrineDefinition { Name = "Patrol", BrainTier = BehaviorConstants.BrainTierBTree });
            registry.Register(200, "Combat", new DoctrineDefinition { Name = "Combat", BrainTier = BehaviorConstants.BrainTierBTree });

            // Register Cognitive Pipeline
            var missionModule = new MissionControlModule(registry);
            var cognitiveModule = new CognitiveRuntimeModule(registry);
            
            var simGroup = new SimulationSystemGroup();
            simGroup.Create(world);
            missionModule.RegisterSystems(simGroup);
            cognitiveModule.RegisterSystems(simGroup);
            kernel.RegisterGlobalSystem(simGroup);

            // Spawn Entity
            _commander = world.CreateEntity();
            world.AddComponent(_commander, new DoctrineState { ActiveDoctrineHash = 100, InstanceId = 1 });
            world.AddComponent(_commander, new LocomotionChannel());
            world.AddComponent(_commander, new WeaponChannel());
            world.AddComponent(_commander, new TargetMemory());

            // Initialize 2-Phase Mission Plan
            var queue = new MissionPlanQueue { PhaseCount = 2, CurrentPhase = 0, PhaseElapsedSeconds = 0f };
            
            // CRITICAL: Safely cast the [InlineArray] to Span to avoid defensive-copy mutation trap
            Span<MissionPhase> phases = queue.Phases;
            phases = new MissionPhase { DoctrineId = 100, Trigger = MissionTrigger.UnderAttack };
            phases[1] = new MissionPhase { DoctrineId = 200, Trigger = MissionTrigger.TimerElapsed, TriggerParam = 5.0f };
            
            world.AddComponent(_commander, queue);
        }

        public void ConfigureVisuals(MapCanvas canvas, EntityRepository world) { }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            ref readonly var queue = ref world.GetComponentRO<MissionPlanQueue>(_commander);
            ref readonly var doctrine = ref world.GetComponentRO<DoctrineState>(_commander);
            ref readonly var loco = ref world.GetComponentRO<LocomotionChannel>(_commander);

            // Phase 1: Synthetic BTree output (Tick 5)
            if (currentTick == 5)
            {
                ref var rwLoco = ref world.GetComponentRW<LocomotionChannel>(_commander);
                rwLoco.ActiveAction = NavigationConstants.ActionIdMoveTo;
                rwLoco.DoctrineInstanceId = doctrine.InstanceId; // Bind to Phase 0
                _passedPhase1_Patrol = true;
            }

            // Phase 2: Inject Ambush (Tick 10)
            if (currentTick == 10)
            {
                ref var mem = ref world.GetComponentRW<TargetMemory>(_commander);
                // Inject threat to satisfy MissionTrigger.UnderAttack
                TargetMemory.AddOrUpdateTarget(ref mem, entityId: 9999, posX: 50f, posY: 50f, scoreBoost: 100f, tick: currentTick);
            }

            // Phase 3 Checkpoint (Tick 11) - Director Advanced Phase
            if (currentTick == 11)
            {
                _passedPhase3_PhaseAdvanced = queue.CurrentPhase == 1 && doctrine.ActiveDoctrineHash == 200;
                if (!_passedPhase3_PhaseAdvanced) throw new Exception($"[Phase 3 Failed] MissionDirectorSystem failed to advance phase. CurrentPhase={queue.CurrentPhase}");
            }

            // Phase 4 Checkpoint / Completion (Tick 12) - Preemption
            if (currentTick == 12)
            {
                // Locomotion MUST be zeroed out because its DoctrineInstanceId (1) no longer matches the DoctrineState (2)
                bool wasPreempted = loco.ActiveAction == 0;

                if (wasPreempted && _passedPhase1_Patrol && _passedPhase3_PhaseAdvanced)
                {
                    return true; // CI SUCCESS!
                }
                throw new Exception("[Phase 4 Failed] ChannelArbitrationSystem failed to preempt stale LocomotionChannel.");
            }

            return false;
        }
    }
}
```

6\. Value to the FDP Framework

This CI scenario directly protects against the `[InlineArray]` C# 12 mutation bugs highlighted in the documentation. By mathematically verifying that the `MissionPlanQueue` safely advances and triggers the `ChannelArbitrationSystem` without needing physics execution, this test ensures your autonomous entities will never get permanently "stuck" executing a peacetime movement order while taking hostile fire.

good pls next demo

Here is the re-specified **TerrainClamping** (formerly PredictiveClamping) demo.

This test isolates the **Geographic** and **Physics** toolkits. It mathematically proves that an Image Generator (IG) or observer node can safely adapt vehicle heights to a 3D terrain mesh _asynchronously_, while actively rejecting bad raycasts (like bridge overpasses or tunnel roofs) to prevent violent visual snapping.

Specification: Fdp.Examples.TerrainClamping

1\. Objective

The TerrainClamping scenario is a headless, CI-focused unit test designed to mathematically prove **Z-Height Smoothing, Asynchronous Terrain Querying, and Jump-Rejection**.

It validates the strict dataflow of `FDP.Toolkit.Geographic`. It proves that terrain queries are correctly batched in the `Input` phase, resolved asynchronously by a provider in the `Simulation` phase, and applied smoothly in the `PostSimulation` phase. Crucially, it proves the **Jump-Rejection Filter**: if a terrain raycast suddenly returns an altitude 50 meters higher than the last frame (e.g., hitting a bridge ceiling instead of the road), the engine will mathematically discard the hit and maintain a smooth trajectory.

2\. Architectural Alignment

This demo relies strictly on the asynchronous `TerrainQueryBatchData` pipeline and C# 12 ECS optimizations:

-   **Zero-Allocation Batching:** Proves that `TerrainQuerySubmitSystem` successfully writes requests into the pre-allocated `NativeArray` buffers without generating garbage.-   **Decoupled Terrain Provider:** Bypasses visual rendering completely. Registers a headless `MockTerrainProvider` implementing `ITerrainProvider` to feed deterministic altitudes into the solver.-   **Phase-Based Clamping:** Relies on the strict ordering of `TerrainQueryResolutionSystem` writing to `TargetZOffset`, followed by `TransformSyncSystem` calculating the lerp into `CurrentZOffset` before the frame completes.

3\. Scenario Setup

**3.1. Node Composition** The runner registers the specific Geographic pipeline across the three scheduling phases. The `CarKinem` module is explicitly excluded to isolate Z-axis logic; we will drive the entity purely via `SimVelocity`.

**3.2. The Entity** A single Vehicle entity is spawned at `X: 0, Y: 0, Z: 0` with a constant velocity of 10 m/s along the X-axis.

-   It possesses `GroundClampingConfig` (Active = true), `GroundClampingState`, and `SimTransform`.-   It is given `NetworkAuthority` to satisfy the `TransformSyncSystem` requirements.

**3.3. The Mock Terrain** The `MockTerrainProvider` feeds specific deterministic heights based on the `QueryX` parameter:

-   **0 to 20m:** Flat ground (Z = 0).-   **20m to 60m:** A smooth ramp upwards.-   **At 40m (The Spike):** A sudden, artificial cliff returning `Z = 100` to simulate a bad raycast hitting a bridge overpass.

4\. Execution Timeline (The Test Checkpoints)

**Simulation Phase**

**Tick Checkpoint**

**Mock Terrain State**

**Expected AI Reaction (Mathematical Assertion)**

**Architectural Validation**

**Phase 1: Bootstrap**

**10**

Flat (Z = 0)

`CurrentZOffset == 0`<br>`LastValidIgAltitude == 0`

**Baseline.** Clamping is active but ground is flat.

**Phase 2: The Climb**

**150**

Ramp (Z = ~2.5)

`TargetZOffset > 0`<br>`CurrentZOffset < TargetZOffset`

**Asynchronous Smoothing.** The vehicle is adapting to the slope, interpolating smoothly rather than snapping instantly.

**Phase 3: Jump Rejection**

**240**

Spike (Z = 100)

`LastValidIgAltitude < 10`

**Seam Skipping.** The provider returned Z=100, but `TerrainQueryResolutionSystem` correctly discarded it because it exceeded the 5-meter jump threshold.

**Phase 4: Recovery**

**300**

Ramp (Z = ~5.0)

`TargetZOffset ≈ 5.0`

**Resilience.** The system seamlessly reacquired the valid ground mesh after passing the anomaly.

5\. Programmatic Assertions (`IScenario` Implementation)

```
using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using ModuleHost.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Systems;
using FDP.Toolkit.Replication.Components;
using Fdp.Examples.NetworkDemo.Systems; // For TransformSyncSystem

namespace Fdp.Examples.Common.Scenarios
{
    // 1. The Headless Terrain Provider
    public class MockTerrainProvider : ITerrainProvider
    {
        public void QueryBatch(NativeArray<TerrainQueryRequest> requests, int count, NativeArray<TerrainQueryResult> results)
        {
            for (int i = 0; i < count; i++)
            {
                float qx = requests[i].QueryX;
                float z = 0f;
                
                if (qx >= 39.5f && qx <= 40.5f) 
                    z = 100f; // Artificial Spike / Bad Raycast
                else if (qx > 20f) 
                    z = (qx - 20f) * 0.2f; // Smooth ramp up
                
                results[i] = new TerrainQueryResult { HasHit = true, HitZ = z };
            }
        }
    }

    public class TerrainClampingScenario : IScenario
    {
        private Entity _vehicle;
        
        // CI Checkpoint tracking
        private bool _passedPhase2_Smoothing = false;
        private bool _passedPhase3_JumpRejection = false;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // 2. Register Phase-Strict Pipeline
            var provider = new MockTerrainProvider();

            var inputGroup = new InputSystemGroup();
            inputGroup.Create(world);
            inputGroup.AddSystem(new TerrainQueryInitializationSystem());
            inputGroup.AddSystem(new TerrainQuerySubmitSystem());
            kernel.RegisterGlobalSystem(inputGroup);

            var simGroup = new SimulationSystemGroup();
            simGroup.Create(world);
            simGroup.AddSystem(new TerrainQuerySolverSystem(provider));
            kernel.RegisterGlobalSystem(simGroup);

            var postGroup = new PostSimulationSystemGroup();
            postGroup.Create(world);
            postGroup.AddSystem(new TerrainQueryResolutionSystem());
            // DriveFromNetwork=true forces the sync system to apply smoothing to local entities in tests
            postGroup.AddSystem(new TransformSyncSystem(driveFromNetwork: true)); 
            kernel.RegisterGlobalSystem(postGroup);

            // 3. Spawn Test Entity
            _vehicle = world.CreateEntity();
            world.AddComponent(_vehicle, new SimTransform { Position = new Vector3(0, 0, 0), Rotation = Quaternion.Identity });
            world.AddComponent(_vehicle, new SimVelocity { Linear = new Vector3(10f, 0, 0) }); // 10 m/s
            world.AddComponent(_vehicle, new GroundClampingConfig { IsClampingActive = true });
            world.AddComponent(_vehicle, new GroundClampingState { LastValidIgAltitude = 0f });
            world.AddComponent(_vehicle, new NetworkTransform());
            world.AddComponent(_vehicle, new NetworkAuthority { LocalNodeId = 0, PrimaryOwnerId = 0 });
        }

        public void ConfigureVisuals(MapCanvas canvas, EntityRepository world) { }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            // Advance physics manually for the test (bypassing CarKinem)
            ref var tf = ref world.GetComponentRW<SimTransform>(_vehicle);
            tf.Position.X += 10f * (1f / 60f);

            ref readonly var clamp = ref world.GetComponentRO<GroundClampingState>(_vehicle);

            // Phase 1 Checkpoint (Tick 10, X ≈ 1.6m) - Flat Ground
            if (currentTick == 10)
            {
                if (clamp.CurrentZOffset > 0.01f) throw new Exception("[Phase 1 Failed] Clamping applied altitude on flat ground.");
            }

            // Phase 2 Checkpoint (Tick 150, X ≈ 25m) - Smoothing the Ramp
            if (currentTick == 150)
            {
                // Target should be > 0, but Current should be smoothly chasing it (Current < Target)
                _passedPhase2_Smoothing = clamp.TargetZOffset > 0.5f && clamp.CurrentZOffset < clamp.TargetZOffset;
                if (!_passedPhase2_Smoothing) throw new Exception($"[Phase 2 Failed] Smoothing failed. Target={clamp.TargetZOffset}, Current={clamp.CurrentZOffset}");
            }

            // Phase 3 Checkpoint (Tick 240, X ≈ 40m) - The Spike (Z=100)
            if (currentTick == 240)
            {
                // The JumpRejectionThresholdMeters is 5.0f. The jump from ~4.0 to 100.0 should be dropped.
                _passedPhase3_JumpRejection = clamp.LastValidIgAltitude < 10.0f;
                if (!_passedPhase3_JumpRejection) throw new Exception($"[Phase 3 Failed] Jump rejection failed! Vehicle snapped to Z={clamp.LastValidIgAltitude}");
            }

            // Phase 4 Checkpoint / Completion (Tick 300, X ≈ 50m)
            if (currentTick == 300)
            {
                bool recovered = Math.Abs(clamp.TargetZOffset - 6.0f) < 1.0f;

                if (recovered && _passedPhase2_Smoothing && _passedPhase3_JumpRejection)
                {
                    return true; // CI SUCCESS!
                }
                throw new Exception("[Phase 4 Failed] Failed to recover clamping after anomaly.");
            }

            return false;
        }
    }
}
```

6\. Value to the FDP Framework

This test is critical for the visual and physics fidelity of your engine's observer nodes (Image Generators / Vis2D). Real-world elevation meshes contain anomalies, holes, and sudden vertical spikes at bridges. By mathematically isolating and asserting the `JumpRejectionThresholdMeters` and lerp calculations without needing a GPU or a human QA tester to "watch for jitter", you guarantee that network replication and terrain sampling never cause visual popping during a live military exercise.

ony other demo left? pls next one

Yes, we have three demos left from the original specification document: **ParallelStories**, **HumanOverride**, and the grand integration demo **UrbanCombat**.

Let's move on to **ParallelStories**. As we discussed in the very first evaluation, the original specification assumed a "Replay Pump" that manually mutated individual components tick-by-tick. The actual FDP engine uses a much more powerful, zero-allocation memory-chunk restoration system (`AsyncRecorder` and `PlaybackSystem` via LZ4 compression).

Here is the re-specified demo, perfectly aligned with the actual flight recorder codebase and the headless CI pipeline.

Specification: Fdp.Examples.ParallelStories

1\. Objective

The ParallelStories scenario is a headless, CI-focused unit test designed to mathematically prove **Deterministic After Action Review (AAR) and Raw Memory Restoration**.

It validates that the `Fdp.Kernel.FlightRecorder` namespace can capture a live stream of raw ECS component chunks (the "Live" run) and serialize them to an LZ4-compressed `.fdprec` log. It then proves that a "Naked" kernel (the "Replay" run)—stripped of all physics, kinematics, and AI systems—can perfectly reconstruct the memory space byte-for-byte, allowing an observer to scrub through the timeline without executing a single line of simulation logic.

2\. Architectural Alignment

This demo rigorously tests the boundary between Simulation and Data Representation:

-   **The Recording Phase:** Uses the actual `RecordingModule` and `AsyncRecorder` to hook into the `PostSimulation` phase. It writes raw `NativeChunkTable` memory arrays directly to disk.-   **The Replay Phase:** Purges all toolkits (`CarKinem`, `Behavior`, `Physics`). Registers ONLY the `ReplayModule` and `PlaybackTickSystem`.-   **No Re-Simulation:** Proves that an entity can move along a trajectory during replay _without_ `CarKinematicsSystem` or `LinearKinematicsSystem` existing in the execution topology, because `PlaybackSystem.ApplyFrame` forces the memory state directly via `RestoreChunkFromBuffer`.

3\. Scenario Setup

Because this test proves perfect determinism between two isolated timelines, the `IScenario` script actually manages two distinct `ModuleHostKernel` executions.

**3.1. Phase A: The Live Run (Synchronous Setup)**

-   During `Configure`, the script spins up a temporary Kernel loaded with `GroundKinematicsModule` and `RecordingModule`.-   It spawns a vehicle, commands it to drive forward, and manually ticks the kernel 50 times, flushing an `AsyncRecorder` to a temporary `test_parallel_stories.fdprec` file.

**3.2. Phase B: The Replay Run (Main Test Loop)**

-   The script then configures the primary CI Runner's Kernel with ONLY the `ReplayModule`.-   The primary CI runner loop ticks the Replay node, which steps through the recording.

4\. Execution Timeline (The Replay Phase)

**Simulation Phase**

**Tick Checkpoint**

**Recorded Event in Log**

**Replay Pump Action**

**Architectural Validation**

**Phase 1: Keyframe Load**

**1**

Full chunk state saved as Keyframe (Type 1).

`PlaybackSystem` clears the world and blasts raw chunk data into `EntityRepository`.

**Baseline Restoration.** Entities exist with correct data without calling `World.CreateEntity()`.

**Phase 2: Delta Application**

**10**

Delta frame containing only changed chunks (Type 0).

`PlaybackSystem` applies chunk diffs over the existing memory.

**Data Compression.** Delta frames correctly patch memory without rebuilding the whole world.

**Phase 3: Physics Bypass**

**25**

Vehicle is mid-movement (X=25.0).

Replay forces `SimTransform.X = 25.0`.

**No Physics Loop.** The entity moved perfectly without `CarKinem` calculating velocity, proving the "Naked Node" architecture.

5\. Programmatic Assertions (`IScenario` Implementation)

```
using System;
using System.IO;
using System.Numerics;
using System.Collections.Generic;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using ModuleHost.Core;
using CarKinem.Core;
using CarKinem.Commands;
using FDP.Toolkit.CarKinem.Modules;
using FDP.Toolkit.Replay;

namespace Fdp.Examples.Common.Scenarios
{
    public class ParallelStoriesScenario : IScenario
    {
        private const string RecFile = "test_parallel_stories.fdprec";
        private Entity _testVehicle;
        
        // Stored history from the Live Run to compare against
        private readonly Dictionary<uint, Vector3> _liveTrajectory = new();

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // =================================================================
            // PHASE A: The Live Run (Executed synchronously during setup)
            // =================================================================
            using var liveWorld = new EntityRepository();
            using var liveKernel = new ModuleHostKernel(liveWorld, new EventAccumulator());
            
            // 1. Full toolkits
            liveKernel.RegisterModule(new GroundKinematicsModule(new RoadNetworkBlob()));
            
            // 2. Setup Recording
            if (File.Exists(RecFile)) File.Delete(RecFile);
            var recorder = new AsyncRecorder(RecFile);
            liveKernel.RegisterModule(new RecordingModule(new RecordingConfiguration { FilePath = RecFile, DrillId = Guid.NewGuid() }));
            
            liveKernel.Initialize();

            _testVehicle = liveWorld.CreateEntity();
            liveWorld.AddComponent(_testVehicle, new SimTransform { Position = Vector3.Zero });
            liveWorld.AddComponent(_testVehicle, new SimVelocity());
            liveWorld.AddComponent(_testVehicle, new VehicleState());
            liveWorld.AddComponent(_testVehicle, new NavState());

            // Command movement
            liveWorld.Bus.PublishManaged(new CmdNavigateToPoint { Entity = _testVehicle, Destination = new Vector2(100, 0), Speed = 10f });

            // Run Live Simulation for 50 ticks
            for (uint i = 1; i <= 50; i++)
            {
                liveKernel.Update();
                // Store absolute truth
                _liveTrajectory[i] = liveWorld.GetComponentRO<SimTransform>(_testVehicle).Position;
            }
            
            // Ensure recorder flushes LZ4 buffers to disk
            liveKernel.Dispose(); 

            // =================================================================
            // PHASE B: The Replay Run (Configured for the CI Runner to tick)
            // =================================================================
            // Notice: NO GroundKinematicsModule registered in the CI kernel.
            kernel.RegisterModule(new ReplayModule(RecFile, world));
        }

        public void ConfigureVisuals(MapCanvas canvas, EntityRepository world) { }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            // If the replay system hasn't loaded the entity yet, wait
            if (!world.IsAlive(_testVehicle)) return false;

            ref readonly var replayTf = ref world.GetComponentRO<SimTransform>(_testVehicle);

            // Phase 3 Checkpoint (Tick 25) - Physics Bypass Determinism
            if (currentTick == 25)
            {
                var livePos = _liveTrajectory[70];
                float distanceError = Vector3.Distance(livePos, replayTf.Position);
                
                // Must match perfectly down to the float precision.
                if (distanceError > 0.0001f) 
                    throw new Exception($"[Phase 3 Failed] Replay divergence at Tick 25. Live: {livePos.X}, Replay: {replayTf.Position.X}");
            }

            // Phase 4 Checkpoint / Completion (Tick 50)
            if (currentTick == 50)
            {
                var livePos = _liveTrajectory[71];
                float distanceError = Vector3.Distance(livePos, replayTf.Position);
                
                if (distanceError < 0.0001f)
                {
                    // Clean up test file
                    if (File.Exists(RecFile)) File.Delete(RecFile);
                    return true; // CI SUCCESS!
                }
                
                throw new Exception($"[Phase 4 Failed] Final state mismatch.");
            }

            return false;
        }
    }
}
```

6\. Value to the FDP Framework

The ability to deterministically replay scenarios is the holy grail of military simulation (AAR) and engine debugging.

This test proves two critical architectural tenets:

-   **Network & Serialization Integrity:** It proves the engine's `IUnmanagedComponentTable` raw-memory copying and LZ4 compression (`K4os.Compression.LZ4`) perfectly serializes physics state without dropping float precision.-   **Execution Decoupling:** By successfully moving an entity without the `CarKinematicsSystem` existing in the running topology, it proves that an Observer node (Vis2D Map / Image Generator) can scrub through replay files seamlessly without incurring the heavy CPU overhead of simulating the vehicles.

good pls the urban combat now - this one needs to select success conditions with caution as it combines so many stuff that thing mihgt end up differently every time - unless we use deterministic time stepping (we should, in all the demos) and even that the floating point errors might lead to slightly different results.

You make an excellent point. In a massive integration test involving physics, pathfinding, and AI, relying on exact "Tick 53" assertions is a recipe for flaky CI tests due to floating-point math differences across CPU architectures.

To solve this, the FDP codebase already contains a strict, deterministic time-stepping mechanism. In the `UrbanCombat` codebase, the `HeadlessDemoApp` manually injects a synthetic `GlobalTime` with a mathematically locked `DeltaTime = 1f / 60f` into the ECS world every single iteration, completely isolating the simulation from real-world wall-clock variations.

Furthermore, to handle slight variances in _when_ an event occurs, our `IScenario.EvaluateTick` will use **Sequential State Latches** rather than strict tick-matching. It will verify that event B happened _after_ event A within a generous overall frame budget.

Here is the re-specified **UrbanCombat** Grand Integration Demo.

Specification: Fdp.Examples.UrbanCombat

1\. Objective

The UrbanCombat scenario is the **Grand Integration Demo** of the FDP Framework.

It mathematically proves that **every toolkit in the engine** (`Behavior`, `CarKinem`, `Navigation`, `Perception`, `Physics`, and `Combat`) works together flawlessly in a deterministic, headless environment. It proves the full cascading chain of events: Pathfinding ➔ Kinematic Movement ➔ Perception ➔ BTree Re-evaluation ➔ Combat Dispatch ➔ Swept-Segment Physics Raycasting ➔ Entity Death ➔ Mission Resumption.

2\. Architectural Alignment & Determinism

-   **Locked Deterministic Time Slicing:** Bypasses the `TimeController` entirely. The runner forces a strict `1.0f / 60.0f` delta step into the `GlobalTime` singleton each iteration to guarantee physics and ballistic trajectories calculate identically across all CI hardware.-   **Pre-Seeded Sensory Data:** To eliminate variance in _when_ the ambush starts due to vision cone intersection framing, the Insurgent's `TargetMemory` is deterministically pre-seeded with the APC's entity handle at Tick 0.-   **Host of Hosts (Topology-Ready):** All systems are registered into their strict phase groups (`Input`, `Simulation`, `PostSimulation`) mirroring a fully distributed environment, proving the CQRS boundaries function without data race conditions.

3\. Scenario Setup

**3.1. The Environment** A mathematical 4-way intersection road graph (`RoadNetworkBlob`) is generated using `DemoEnvironmentSetup.CreateCityIntersection()`.

**3.2. The Entities** The `ScenarioDirector` spawns 14 entities using the real `TkbDatabase`:

-   **Bluefor:** 1 Military APC (TKB 2001) driving North on the `ConvoyEscort` HSM. It carries 4 `InfantrySoldier` passengers in its `PassengerBuffer`.-   **Opfor:** 1 Insurgent (TKB 2003) hiding at X: 60, Y: 20 running the `Ambush` BTree.-   **Neutral:** 5 `CivilianPedestrian` and 3 `CivilianCar` entities acting as background clutter, driven by the `TrafficBrainSystem`.

4\. Execution Timeline (Sequential Latches)

Instead of checking exact ticks, the CI runner validates that these milestones occur sequentially within a 600-tick (10-second) budget.

**Simulation Phase**

**Event Window**

**Expected State / Mathematical Proof**

**Architectural Validation**

**Phase 1: The Ambush**

Ticks 1 - 100

Insurgent `WeaponChannel` fires. A `FireRequestEvent` is observed.

**Cognitive to Combat.** BTree reacted to seeded TargetMemory and dispatched a weapon action.

**Phase 2: Reaction**

Ticks 50 - 150

APC `LocomotionChannel` transitions from `MoveTo` ➔ `0`.

**Dynamic Re-evaluation.** Bluefor seamlessly interrupted its pathfinding to engage the threat.

**Phase 3: Retaliation**

Ticks 100 - 300

APC or Soldiers fire back. `HitEvent`s registered against Insurgent.

**Physics Integration.** Ballistics accurately raycasted hits against the humanoid collider.

**Phase 4: Resolution**

Ticks 200 - 400

Insurgent `Health.Current == 0`. Entity destroyed via ELM.

**Lifecycle.** Combat lethality correctly triggers ELM teardown sequences.

**Phase 5: Resumption**

Ticks 300 - 600

APC `LocomotionChannel` transitions back to `MoveTo`.

**State Recovery.** The HSM correctly dropped out of Combat state when the threat was purged.

5\. Programmatic Assertions (`IScenario` Implementation)

```
using System;
using System.Linq;
using CarKinem.Road;
using Fdp.Kernel;
using ModuleHost.Core;
using Fdp.Examples.UrbanCombat.Setup;
using Fdp.Examples.UrbanCombat.Systems;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.Navigation;

namespace Fdp.Examples.Common.Scenarios
{
    public class UrbanCombatScenario : IScenario
    {
        private RoadNetworkBlob _road;
        private Entity _apc;
        private Entity _insurgent;

        // Sequential Latches to handle floating-point timing variance
        private bool _latchAmbushFired = false;
        private bool _latchApcHalted = false;
        private bool _latchInsurgentHit = false;
        private bool _latchInsurgentKilled = false;

        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // 1. Initialize TKB and Environment
            var tkb = new Fdp.Toolkit.Tkb.TkbDatabase();
            DemoTkbSetup.RegisterAll(tkb);
            _road = DemoEnvironmentSetup.CreateCityIntersection();

            // 2. Register all Toolkits exactly as HeadlessDemoApp does
            var app = new Fdp.Examples.UrbanCombat.HeadlessDemoApp();
            app.Initialize(); // Registers the massive block of systems across all phases

            // 3. Spawn the Cast
            var director = new ScenarioDirector(world, tkb, _road, app.DoctrineRegistry);
            director.SetupAmbushScenario();

            // Capture specific entities for tracking
            _apc = FindEntityByType(world, 2001); // MilitaryAPC
            _insurgent = FindEntityByType(world, 2003); // Insurgent
        }

        public void ConfigureVisuals(MapCanvas canvas, EntityRepository world) { }

        public bool EvaluateTick(uint currentTick, EntityRepository world)
        {
            // Force strict deterministic time-slicing
            world.SetSingleton(new GlobalTime { DeltaTime = 1f / 60f, TimeScale = 1f, FrameNumber = currentTick });

            // Latch 1: Detect Insurgent Firing
            if (!_latchAmbushFired)
            {
                var fireEvents = world.Bus.Consume<FireRequestEvent>();
                foreach (ref readonly var evt in fireEvents)
                {
                    if (evt.Shooter == _insurgent) _latchAmbushFired = true;
                }
            }

            // Latch 2: Detect APC Halt (Reaction)
            if (_latchAmbushFired && !_latchApcHalted && world.IsAlive(_apc))
            {
                var loco = world.GetComponentRO<LocomotionChannel>(_apc);
                if (loco.ActiveAction == 0) _latchApcHalted = true;
            }

            // Latch 3: Detect Insurgent taking damage
            if (_latchApcHalted && !_latchInsurgentHit)
            {
                var hitEvents = world.Bus.Consume<HitEvent>();
                foreach (ref readonly var hit in hitEvents)
                {
                    if (hit.HitEntity == _insurgent) _latchInsurgentHit = true;
                }
            }

            // Latch 4 & 5: Detect Kill and Route Resumption
            if (_latchInsurgentHit && !_latchInsurgentKilled)
            {
                bool insurgentDead = !world.IsAlive(_insurgent);
                
                if (insurgentDead)
                {
                    _latchInsurgentKilled = true;
                    
                    // Check if APC resumed its mission
                    var loco = world.GetComponentRO<LocomotionChannel>(_apc);
                    if (loco.ActiveAction == NavigationConstants.ActionIdMoveTo || 
                        loco.ActiveAction == NavigationConstants.ActionIdFollowRoute)
                    {
                        return true; // CI SUCCESS! The entire cascade completed.
                    }
                }
            }

            return false; // Keep simulating
        }

        private Entity FindEntityByType(EntityRepository world, int tkbTypeId)
        {
            var query = world.Query().With<Fdp.Toolkit.Replication.Components.TkbIdentity>().Build();
            foreach (var e in query)
            {
                if (world.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(e).TkbType == tkbTypeId) return e;
            }
            return Entity.Null;
        }
    }
}
```

6\. Value to the FDP Framework

The UrbanCombat scenario acts as the ultimate **Regression Safety Net**. By tracking sequential, logical latches (e.g., "Did the APC stop moving after the RPG fired?") rather than asserting raw floating-point coordinates at specific ticks, this integration test proves that the entire FDP engine acts as a cohesive whole. If a change to the `CarKinem` toolkit accidentally breaks the `ActionDispatch` module's ability to interrupt movement during combat, this single CI test will catch the failure reliably, regardless of the underlying CPU architecture.

Let's pls detail the concept of the demo runner. It needs to support running headless, with optional 2d visualization etc. (see the FDP demos source) Newly is needs to enforce deterministic time stepping for CI tests.

The FDP Demo Runner is built around a highly flexible, dual-purpose architecture that ensures engine stability through **headless CI validation** while simultaneously supporting **real-time visual debugging**.

Here is a detailed breakdown of the runner's core concepts and how it enforces determinism for your CI pipelines:

**1\. The Subsystem Orchestrator and Headless Mode** At the heart of the runner is the `SubsystemOrchestrator`, which manages the lifecycle of all registered subsystems (like the simulation host or observer nodes).

-   **Headless Execution:** By default, or when triggered via the CLI without visual flags, the orchestrator operates in strict headless mode. It completely skips all `Raylib.BeginDrawing()` and `rlImGui.Begin()` calls. This allows the ECS kernel to process simulation ticks as fast as the CPU can handle, executing millions of calculations for pathfinding and combat without waiting on VSync or GPU buffers.

**2\. Optional 2D Visualization (--attach-vis2d)** When a developer needs to debug a failing test or visually inspect behavior, they can pass the `--attach-vis2d` command-line flag.

-   This spins up a secondary observer node within the runner that registers the `FDP.Toolkit.Vis2D` toolkit and opens a Raylib/ImGui window.-   Because the FDP engine relies on a strict data-oriented replication architecture, the visualizer merely reads ghosted ECS components (like `SimTransform` and `LocomotionChannel`). **The presence of the renderer cannot slow down or alter the physics or AI determinism of the scenario**. What you see on the screen is mathematically identical to what the CI pipeline evaluates headlessly.

**3\. Deterministic Time Stepping for CI Tests** To guarantee that complex integrations (like the Urban Combat or AutoDrive scenarios) do not yield flaky results due to floating-point drift or varying CPU speeds across different CI runners, the framework completely isolates the simulation from the real-world wall clock.

-   **No Wall-Clock Logic:** Scenarios are strictly forbidden from using `Thread.Sleep()` or wall-clock timers.-   **The Stepping Time Controller:** Instead of a continuous clock, CI tests utilize the `SteppingTimeController`. This controller explicitly does not measure the wall clock; it only advances the simulation time when manually stepped by a fixed delta.-   **The GlobalTime Singleton:** On every tick, the runner forces a mathematically locked delta step (such as `1.0f / 60.0f` for 60Hz) into the `GlobalTime` ECS singleton. This ensures that every kinematic integration (pos+\=vel×dt), avoidance calculation, and ballistic trajectory resolves identically down to the exact float precision, regardless of how long the physical CPU took to compute the frame.

**4\. Mathematical Assertions and Timeout Control** The runner evaluates success programmatically rather than visually.

-   Each scenario implements an `IScenario` interface that acts as the test script, injecting commands into the ECS at specific deterministic ticks.-   The runner loop continuously evaluates mathematical assertions against the raw memory (e.g., checking if `SimVelocity == 0.0` or if a bullet triggered a hit event).-   The CLI limits execution via a `--max-ticks` argument (defaulting to 500). If the mathematical completion conditions are met before the tick limit, the runner exits with a success code (Code 0). If the physics fail to resolve or the AI gets stuck, the runner hits the tick ceiling and exits with a failure code (Code 1), instantly flagging the regression in the CI pipeline.

pls let's go deeper how the runnes should be implemented (class skeletons, interfaces etc.)

To implement the FDP Demo Runner with strict support for headless CI execution, optional 2D visualization, and deterministic time stepping, we must rely on the core runner interfaces found in the `FDP.Framework.Runner` namespace.

The architecture is built around a composition of **Subsystems** managed by an **Orchestrator**, allowing the execution logic to be completely decoupled from the rendering pipeline.

Here is a deep dive into the specific class skeletons, interfaces, and implementations required to build the runner.

1\. The Core Subsystem Contract (`ISubsystem.cs`)

Every scenario or node (e.g., a "Brain" node or a "Muscle" node) must be wrapped in a class that implements `ISubsystem`. The orchestrator calls these lifecycle methods in a strict, deterministic order.

**Crucially, subsystems must NEVER call window initialization or UI setup directly** unless explicitly configured to do so.

```
using System.Numerics;

namespace FDP.Framework.Runner
{
    public interface ISubsystem
    {
        /// Human-readable subsystem name shown in diagnostics and UI.
        string Name { get; }

        /// RGBA colour used to theme the subsystem's ImGui title bars [2].
        Vector4 TitleBarColor { get; }

        /// Performs one-time initialisation. Passed a config dictating headless state [2].
        void Initialize(SubsystemConfig config);

        /// Advances simulation logic. MUST NOT contain rendering calls [3].
        void Update(float deltaTime);

        /// Renders the 3-D / world layer. No-op in headless mode [3].
        void DrawWorld();

        /// Renders the ImGui UI layer. No-op in headless mode [3].
        void DrawUI();

        /// Releases all subsystem resources [3].
        void Shutdown();
    }
}
```

2\. The Configuration Payloads

When the CLI parser evaluates arguments like `--headless` or `--attach-vis2d`, it translates them into configuration objects that dictate how the runner and its subsystems behave.

```
namespace FDP.Framework.Runner
{
    public class RunnerOptions
    {
        /// When true, the orchestrator skips all Raylib and ImGui calls [5].
        public bool Headless { get; set; }
        public int DomainId { get; set; }
        public int TargetFps { get; set; } = 60;
    }

    public class SubsystemConfig
    {
        public int DomainId { get; set; }
        /// When true the subsystem must skip all Raylib and ImGui calls [6].
        public bool Headless { get; set; }
        /// When false, the orchestrator owns the window and the subsystem must NOT call Raylib.InitWindow() [6, 7].
        public bool OwnWindow { get; set; }
        public string SubsystemName { get; set; } = string.Empty;
    }
}
```

3\. The Orchestrator (`SubsystemOrchestrator.cs`)

This is the heart of the runner. It takes a collection of `ISubsystem` instances and manages their execution loop. The magic of the dual-purpose design happens inside the `Run()` method, where rendering is conditionally bypassed.

```
namespace FDP.Framework.Runner
{
    public class SubsystemOrchestrator
    {
        private readonly List<ISubsystem> _subsystems;
        private readonly bool _headless;
        private volatile bool _running = true;

        public SubsystemOrchestrator(IEnumerable<ISubsystem> subsystems, RunnerOptions? options = null)
        {
            _headless = options?.Headless ?? false;
            _subsystems = new List<ISubsystem>(subsystems);
        }

        public void Initialize()
        {
            // Only initialize the window if we are actively visualizing [9].
            if (!_headless)
            {
                Raylib.InitWindow(...);
                rlImGui.Setup(true);
            }

            foreach (var subsystem in _subsystems)
            {
                subsystem.Initialize(new SubsystemConfig { Headless = _headless });
            }
        }

        public void Run()
        {
            // If headless, we do not wait for Raylib.WindowShouldClose() [8].
            while (_running && (_headless || !Raylib.WindowShouldClose()))
            {
                // Force dt to 0 in headless mode to rely purely on stepping [8].
                float dt = _headless ? 0f : Raylib.GetFrameTime();
                
                // 1. Tick Simulation Logic
                for (int i = 0; i < _subsystems.Count; i++)
                    _subsystems[i].Update(dt);

                // 2. Conditionally Tick Visuals
                if (!_headless)
                {
                    Raylib.BeginDrawing();
                    foreach (var s in _subsystems) s.DrawWorld(); // e.g., MapCanvas [10].
                    
                    rlImGui.Begin();
                    foreach (var s in _subsystems) s.DrawUI(); // e.g., Inspectors [1].
                    rlImGui.End();
                    
                    Raylib.EndDrawing();
                }
            }
        }

        /// Used by the headless test executor to rapidly process ticks without rendering [11].
        public void RunFrames(int frames)
        {
            for (int i = 0; i < frames; i++) Update(0f);
        }
    }
}
```

4\. Deterministic Time Stepping for CI (`SteppingTimeController.cs`)

For CI integration tests to pass reliably across different hardware, the simulation cannot rely on the system's wall clock. The runner enforces determinism by injecting the `SteppingTimeController` into the `ModuleHostKernel`.

```
namespace FDP.Toolkit.Time.Controllers
{
    /// Time controller for manual stepping only. 
    /// Does not measure wall clock - advances only when Step() is called [13].
    public class SteppingTimeController : ISteppableTimeController
    {
        private double _totalTime;
        private long _frameNumber;

        public SteppingTimeController(GlobalTime seedState)
        {
            SeedState(seedState);
        }

        public GlobalTime Step(float deltaTime)
        {
            _frameNumber++;
            _totalTime += deltaTime;

            return new GlobalTime
            {
                FrameNumber = _frameNumber,
                DeltaTime = deltaTime, // Forced deterministic delta (e.g., 1.0f / 60.0f)
                TotalTime = _totalTime,
                TimeScale = 1.0f
            };
        }
    }
}
```

5\. The CI Test Executor (`HeadlessTestExecutor.cs`)

To drive the orchestrator autonomously, the framework provides a `HeadlessTestExecutor`. This class reads a JSON `TestScript` containing timed steps and executes them against the running `SubsystemOrchestrator`.

Instead of waiting for real time to pass, it calls `_orchestrator.RunFrames(frames)` to instantly calculate physics and AI.

```
namespace FDP.Framework.Runner.Testing
{
    public class HeadlessTestExecutor
    {
        private readonly SubsystemOrchestrator _orchestrator;
        private readonly TestScript _script;

        public async Task<int> RunAsync()
        {
            _orchestrator.Initialize();

            // Run the orchestrator update loop in a background thread [16].
            var loopTask = Task.Run(() => _orchestrator.Run());

            // Process script actions...
            foreach (var step in _script.Steps)
            {
                // e.g., The "tick" action handler directly commands the orchestrator 
                // to process N frames instantly.
                if (step.Action == "tick")
                {
                    int framesToTick = (int)step.Args["frames"];
                    _orchestrator.RunFrames(framesToTick); // Blazes through ECS math [11, 15].
                }
            }

            _orchestrator.Stop();
            return 0; // Success
        }
    }
}
```

**How it all fits together:** When a developer runs `fdp.framework.runner.exe --scenario AutoDrive --max-ticks 500`, the program initializes an `EntityRepository`, registers `GroundKinematicsModule`, and wraps it in a `ScenarioSubsystem`. Because `--attach-vis2d` is missing, `SubsystemConfig.Headless` is true. The `SubsystemOrchestrator` loops instantly, calling `Update()` while completely skipping all `Raylib.BeginDrawing()` calls. The `SteppingTimeController` mathematically advances physics by exactly `0.01667` seconds per iteration. If the mathematical assertion succeeds before frame 500, CI passes. If the developer wants to see _why_ a test failed, they add `--attach-vis2d`, and the exact same ECS memory is rendered to the screen.

I think the deterministic time stepping should become the optional feature of the already existing generic runner infrastructure which might be missing such a feature now but is absolutely necessary for CI testing, what do you think? The deterministic time stepping will be usefull also later what using the generic runner to run fully fledged simulation nodes, not just CI tests.

I think this is an excellent architectural insight and absolutely the right direction to take. Moving deterministic time stepping out of individual app implementations and into the generic `FDP.Framework.Runner` infrastructure makes the framework much more robust and reusable.

Here is why your idea perfectly aligns with the engine's architecture and how it benefits both CI testing and production nodes:

**1\. Centralizing What is Currently Fragmented** Right now, the codebase forces individual applications or scenarios to manually manage deterministic time. For instance, the `NetworkDemoApp` manually checks if it is in replay mode to instantiate a `SteppingTimeController`, and the `CarKinemApp` juggles a `SwitchableTimeController` to toggle between continuous and stepping modes when paused.

By pushing this up into `RunnerConfiguration` and `RunnerOptions`, you can introduce standard CLI flags like `--deterministic` and `--fixed-step=0.01667`. The generic runner can then automatically configure the `TimeControllerFactory` using `TimeMode.Deterministic`, completely freeing the individual `IScenario` implementations from having to write boilerplate time-management code.

**2\. Absolute Necessity for CI** As you noted, relying on wall-clock time in CI is a recipe for flaky tests. The `HeadlessTestExecutor` is already designed to process script actions (like `tick`) by calling `_orchestrator.RunFrames(frames)`. If the generic runner natively enforces a deterministic time step, `RunFrames()` will reliably inject the exact same `DeltaTime` into the `GlobalTime` singleton on every single hardware configuration, guaranteeing that complex physics math (like RVO avoidance and ballistic raycasts) resolves identically every time.

**3\. immense Value for Fully Fledged Nodes** Your foresight regarding production simulation nodes is spot on. The `FDP.Toolkit.Time` module already contains the heavy lifting for distributed deterministic simulations:

-   **Distributed Lockstep:** It includes `SteppedMasterController` and `SteppedSlaveController` which advance simulation time strictly via network `FrameOrder` and `FrameAck` descriptors rather than the local CPU clock.-   **AAR / Replay Mode:** The `ReplayModule` relies heavily on deterministic time stepping to scrub through recorded `.fdprec` files and restore unmanaged memory chunks without letting the physics engine run wild.

If the generic runner natively supports deterministic execution as a first-class feature, launching a fully fledged distributed lockstep node or an After Action Review (AAR) observer becomes as simple as passing `--mode=deterministic --role=Muscle_Hull` to the CLI, rather than having to write a custom application wrapper for every deployment configuration.

**Proposed Implementation Update:** To implement your idea, you would simply extend `RunnerOptions` to include:

```
public bool Deterministic { get; set; }
public float FixedDeltaSeconds { get; set; } = 1.0f / 60.0f;
```

Then, during the `SubsystemOrchestrator.Initialize` phase, the runner can inject a `TimeControllerConfig` with `Mode = TimeMode.Deterministic` and `SyncConfig.FixedDeltaSeconds = options.FixedDeltaSeconds` down into the registered subsystems and the `ModuleHostKernel`.

good. now pls provide a list all new demo-specific ECS components and FDP event bus events and DDS messages to be created for the purpose of the demo logic - pls re-spicify the existing list from the "FDP demos" document.

This is one of the biggest architectural wins of the new framework. Because we are now testing the actual FDP toolkits via the CI Runner instead of building a fake "demo domain", **we can completely delete 90% of the mocked components and events originally requested in the demo document****.**

By relying on the actual engine toolkits, the "demo-specific" data model shrinks down to just the lightweight Cartesian networking structs and a few CI tracking tools.

Here is the re-specified list of the data model required for the `Fdp.Examples` suite, mapped directly to the current codebase:

1\. Obsolete Components (What we DELETE from the original spec)

The original FDP Demos specification requested several mock components that are now natively supported by the generic zero-allocation toolkits. We will **not** create these for the demos:

-   **ParentEntityComponent****:** Obsolete. The demos will use the real `ChildMap` and `PartMetadata` components from `FDP.Toolkit.Replication`.-   **HealthComponent** **&** **KinematicConstraintsComponent****:** Obsolete. The demos will use the real `Health` component and `ActorCapabilityState` bitmasks.-   **VisionSensorComponent** **&** **PerceptionComponent****:** Obsolete. The demos will use the unmanaged `PerceptionReceptor`, `VisualReceptor`, and `TargetMemory` arrays.-   **TerrainClampComponent****:** Obsolete. The demos will use `GroundClampingConfig` and `GroundClampingState`.

\--------------------------------------------------------------------------------

2\. New DDS Messages (`Fdp.Examples.DDS`)

To support the `DistributedTank` demo and "Host of Hosts" topology without dragging in the heavy Geodetic/WGS84 logic of Bagira or the legacy EntityMaster, we need to define ultra-fast, Cartesian-only IDL structs for local loopback testing.

-   **DemoSpawnMsg** _(Replaces_ _SimObjectLifecycleMsg_ _/_ _EntityMasterTopic__)_
    -   **Purpose:** Commands headless nodes to spawn an entity without waiting for the full ELM handshake if running in fast-mock mode.-   **Fields:** `NetworkId` (long), `TkbType` (long), `OwnerNodeId` (int), `IsDestroyed` (bool).-   **DemoTransformMsg** _(Replaces_ _Transform3DMsg_ _/_ _GeoStateDescriptor__)_
    -   **Purpose:** Replicates `SimTransform` purely in flat Cartesian space.-   **Fields:** `NetworkId` (long), `PosX`, `PosY`, `PosZ` (float), `RotX`, `RotY`, `RotZ`, `RotW` (float).-   **DemoLocomotionMsg** _(Replaces_ _LocomotionIntentMsg__)_
    -   **Purpose:** Replicates the AI's `LocomotionChannel` downwards to the CarKinem physics node.-   **Fields:** `NetworkId` (long), `ActiveAction` (ushort), `DoctrineInstanceId` (uint), `ActionInstanceId` (uint).-   **DemoWeaponMsg** _(Replaces_ _WeaponIntentMsg__)_
    -   **Purpose:** Replicates the AI's `WeaponChannel` downwards to the Turret physics node.-   **Fields:** `NetworkId` (long), `ActiveAction` (ushort), `DoctrineInstanceId` (uint), `ActionInstanceId` (uint).-   **DemoCombatInteractionMsg** _(Replaces_ _WeaponFireEvent_ _/_ _HitNotificationEvent_ _over DDS)_
    -   **Purpose:** Notifies cross-process nodes that a shot was fired or a hit was resolved by `RaycastSolverSystem`.-   **Fields:** `ShooterNetId` (long), `TargetNetId` (long), `IsHit` (bool), `Damage` (float).

\--------------------------------------------------------------------------------

3\. New FDP Event Bus Events (`Fdp.Examples.Common.Events`)

Most inter-system communication will use the actual engine events (`FireRequestEvent`, `HitEvent`, `CmdNavigateToPoint`, `ConstructionOrder`). We only need to add events designed to artificially trigger CI scenario milestones.

-   **DemoTestLogEvent**
    -   **Purpose:** A synthetic event used by the headless AI tester to log test phase transitions without using `Console.WriteLine`, ensuring thread-safe test reporting.-   **Fields:** `ScenarioName` (FixedString32), `PhaseId` (int), `IsSuccess` (bool).-   **DemoScenarioTriggerEvent**
    -   **Purpose:** Allows the `IScenario` script to inject an artificial world-state change (like an HQ override) dynamically into the `EventAccumulator` across all running modules.-   **Fields:** `TriggerType` (byte: e.g., 1 = ForceHoldFire, 2 = SpawnAmbush), `TargetEntityIndex` (int).

\--------------------------------------------------------------------------------

4\. New ECS Components (`Fdp.Examples.Common.Components`)

Because we rely on the `TkbDatabase` to provide actual combat and locomotion components, our demo-specific components exist purely to help the CI Runner track test states.

-   **MockBlackboardState** **(Struct)**
    -   **Purpose:** A test-specific struct overlay for the `BrainBlackboard.Memory` byte array.-   **Fields:** `ThreatVisible` (bool), `AmmoCount` (int), `CurrentRoE` (byte).-   **Usage:** Used in `BehaviorValidation` to directly poke the unmanaged AI memory without spinning up the Perception raycast systems.-   **DemoScenarioTracker** **(Component)**
    -   **Purpose:** Attached to the "Scenario Master" entity in the ECS world. It holds the latches and tick-timers for the current CI test, preventing the test logic from living entirely in C# variables outside the ECS kernel.-   **Fields:** `CurrentPhase` (int), `TicksInPhase` (uint), `Latches` (BitMask256).

By strictly limiting the `Fdp.Examples` namespace to these lightweight networking definitions and CI trackers, you mathematically prove that your generic toolkits (`FDP.Toolkit.Behavior`, `FDP.Toolkit.CarKinem`, `FDP.Toolkit.Combat`) are fully capable of executing a complex, distributed 3D simulation without any application-level "glue" components.

OK pls re-specify also the folder layout chapter from the original document (take into consideration the exiting examples and make sure the new layout does not collide with them - we need to keep them until we completely switch to the new demos)

Here is the re-specified **Folder Layout & Organization** chapter.

To ensure we do not collide with the existing legacy applications currently in the `FDP.sln` (such as `Fdp.Examples.NetworkDemo`, `Fdp.Examples.CarKinem`, and the legacy `Fdp.Examples.UrbanCombat`), we will not put the new demos inside a single monolithic project.

Instead, we will add a suite of **new, cleanly separated projects** into the existing `Examples/` folder. This guarantees the legacy apps remain fully functional until they are gracefully deprecated, while strictly enforcing the new modular CI architecture.

Specification: FDP Demo Framework Folder Layout

1\. Objective

To maintain a pristine architecture, the new CI-focused demo suite is split into strictly segregated projects. It separates network definitions, shared ECS state, scenario logic, and the executable runner.

2\. Solution Directory Structure

The new projects will be added to the `Examples\` folder alongside the legacy projects.

```
Examples/
├── Fdp.Examples.CarKinem/           # [LEGACY] Do Not Touch
├── Fdp.Examples.NetworkDemo/        # [LEGACY] Do Not Touch
├── Fdp.Examples.UrbanCombat/        # [LEGACY] Do Not Touch
│
├── Fdp.Examples.DDS/                # [NEW] Pure Cartesian Network Schemas
│   ├── Fdp.Examples.DDS.csproj
│   ├── DemoSpawnMsg.cs
│   ├── DemoTransformMsg.cs
│   └── DemoLocomotionMsg.cs
│
├── Fdp.Examples.Common/             # [NEW] Shared State & Tooling
│   ├── Fdp.Examples.Common.csproj
│   ├── Components/                  # CI Trackers & Unmanaged Blackboard overlays
│   │   ├── DemoScenarioTracker.cs
│   │   └── MockBlackboardState.cs
│   ├── Events/                      # Synthetic injection events
│   ├── Translators/                 # Lightweight Ingress/Egress DDS mappers
│   ├── Configuration/               # Reusable Mock Terrain / Road Graphs
│   └── Constants/                   # Strict enforcement of "No Magic Strings"
│       ├── ScenarioNames.cs
│       ├── DemoTemplateIds.cs
│       └── DemoDoctrineIds.cs
│
├── Fdp.Examples.Scenarios/          # [NEW] The CI Test Scripts (IScenario)
│   ├── Fdp.Examples.Scenarios.csproj
│   ├── Cognitive/             
│   │   ├── BehaviorValidationScenario.cs
│   │   └── MissionCommandScenario.cs
│   ├── Kinematics/
│   │   └── AutoDriveScenario.cs
│   ├── Perception/
│   │   └── SensorGridScenario.cs
│   ├── Physics/
│   │   └── BallisticsAndHitScenario.cs
│   ├── Network/
│   │   └── DistributedTankScenario.cs
│   ├── Replay/
│   │   └── ParallelStoriesScenario.cs
│   └── Integrated/
│       └── UrbanCombatScenario.cs   # The Grand CI Demo
│
└── Fdp.Examples.Runner/             # [NEW] The CLI Executable
    ├── Fdp.Examples.Runner.csproj
    ├── Program.cs                   # CLI argument parser
    └── ScenarioSubsystem.cs         # Wraps the kernel & manages headless/Vis2D
```

3\. Project Responsibilities & Architectural Standards

**Fdp.Examples.DDS**

-   **Rule:** Must contain _only_ DDS struct definitions (`[DdsTopic]`).-   **Restriction:** Cannot reference `Fdp.Kernel` or any toolkits. It must remain pure, serializable data representing flat, Cartesian-only messaging (ignoring Bagira WGS84 logic).

**Fdp.Examples.Common**

-   **Rule:** Houses the connective tissue used by the runner and the scenarios.-   **Constants:** Magic strings and hardcoded integer IDs are strictly forbidden in scenario scripts. They must reference `DemoTemplateIds.CommandTank` or `DemoDoctrineIds.Ambush`.-   **Translators:** Holds the specific `IDescriptorTranslator` classes that map the lightweight `Fdp.Examples.DDS` messages to generic ECS components (e.g., mapping `DemoTransformMsg` to `SimTransform`).

**Fdp.Examples.Scenarios**

-   **Rule:** Contains only classes implementing the `IScenario` interface.-   **Restriction:** Scenarios must _never_ contain rendering logic, `Raylib` references, or wall-clock timers (`Thread.Sleep`). All logic must evaluate raw ECS memory deterministically via the `EvaluateTick(uint currentTick, EntityRepository world)` method.

**Fdp.Examples.Runner**

-   **Rule:** This is the only executable project (`OutputType = Exe`).-   **Execution:** It parses the CLI arguments (`--scenario`, `--attach-vis2d`, `--max-ticks`), loads the requested `IScenario` via reflection or a registry, and injects it into the `SubsystemOrchestrator`. It is the sole owner of the `ModuleHostKernel`, the `EntityRepository`, and the deterministic `SteppingTimeController`.

