using System;
using CarKinem.Road;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Examples.UrbanCombat.Brains;
using Fdp.Examples.UrbanCombat.Setup;
using Fdp.Examples.UrbanCombat.Systems;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Tkb;
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Systems;
using Fdp.Toolkit.CarKinem.Systems;
using System.Collections.Generic;

namespace Fdp.Examples.UrbanCombat
{
    /// <summary>
    /// Orchestrator for the headless "Urban Ambush" demo simulation.
    /// <para>
    /// Call <see cref="Initialize"/> once to register all components, build the behavior
    /// registry and system pipeline, then call <see cref="RunSimulation"/> to execute
    /// the simulation loop.
    /// </para>
    /// </summary>
    public class HeadlessDemoApp : IDisposable
    {
        // ── Constants ────────────────────────────────────────────────────────────────

        /// <summary>Simulation timestep: 60 Hz → ~16.67 ms per frame.</summary>
        private const float Dt = 1f / 60f;

        /// <summary>Total frames for the 10-second Urban Ambush scenario.</summary>
        private const int TotalFrames = 600;

        // ── Inline BTree JSON ─────────────────────────────────────────────────────────

        private const string AmbushJson = """
            {
                "TreeName": "Ambush_BT",
                "Version": 1,
                "Root": {
                    "Type": "Selector",
                    "Children": [
                        {
                            "Type": "Sequence",
                            "Children": [
                                { "Type": "Condition", "Action": "Condition_HasTarget"  },
                                { "Type": "Action",    "Action": "Action_AimAndFire"    }
                            ]
                        },
                        { "Type": "Action", "Action": "Action_HoldPosition" }
                    ]
                }
            }
            """;

        private const string InfantryCombatJson = """
            {
                "TreeName": "InfantryCombat_BT",
                "Version": 1,
                "Root": {
                    "Type": "Action",
                    "Action": "HoldPosition"
                }
            }
            """;

        // ── State ────────────────────────────────────────────────────────────────────

        /// <summary>The ECS world that owns all entity data and system execution.</summary>
        public EntityRepository World { get; private set; }

        /// <summary>
        /// The Transient Knowledge Base: holds all five entity blueprint templates.
        /// </summary>
        public ITkbDatabase Tkb => _tkb;

        /// <summary>
        /// The behavior registry: maps behavior IDs to their definitions (BrainTier, HSM blob, BTree interpreter).
        /// Populated during <see cref="Initialize"/>; read-only thereafter.
        /// </summary>
        public BehaviorRegistry BehaviorRegistry => _behaviorRegistry;

        /// <summary>
        /// The road network blob for the city intersection.
        /// Caller must not dispose; <see cref="Dispose"/> handles it.
        /// </summary>
        public RoadNetworkBlob Road { get; private set; }

        private readonly TkbDatabase        _tkb              = new TkbDatabase();
        private readonly BehaviorRegistry   _behaviorRegistry = new BehaviorRegistry();
        private readonly NetworkEntityMap   _entityMap        = new NetworkEntityMap();
        private TrajectoryPoolManager?      _trajectoryPool;

        /// <summary>
        /// The shared <see cref="NetworkEntityMap"/> used by the combat CQRS chain.
        /// Pass this to <see cref="ScenarioDirector"/> so spawned entities are registered
        /// and <see cref="Fdp.Toolkit.Combat.Events.WeaponFireIntent"/> IDs resolve correctly.
        /// </summary>
        public NetworkEntityMap EntityMap => _entityMap;

        // IEcsModuleSystem instances executed in phase order each frame.
        private List<IEcsModuleSystem> _inputModuleSystems   = new();
        private List<IEcsModuleSystem> _simModuleSystems     = new();
        private List<IEcsModuleSystem> _postSimModuleSystems = new();
        private List<IEcsModuleSystem> _exportModuleSystems  = new();

        // Physics module — retained for lifetime so native arrays stay valid;
        // Dispose() frees the persistent NativeArrays after the world is torn down.
        private PhysicsToolkitModule?       _physicsModule;

        private bool _initialized;
        private bool _disposed;

        // ── Lifecycle ────────────────────────────────────────────────────────────────

        public HeadlessDemoApp()
        {
            World = new EntityRepository();
        }

        /// <summary>
        /// Registers all ECS component types, builds the behavior registry, allocates
        /// the physics singleton and system pipeline.
        /// Must be called exactly once before any simulation.
        /// </summary>
        public void Initialize()
        {
            if (_initialized)
                throw new InvalidOperationException("HeadlessDemoApp.Initialize() called more than once.");

            // 1. Register all component types used by the demo.
            RegisterComponents();

            // 2. Register all five TKB entity blueprints.
            DemoTkbSetup.RegisterAll(_tkb);

            // 3. Register HSM action delegates so HsmActionDispatcher can invoke them.
            //    Generated by Fhsm.SourceGen from [HsmAction]-annotated methods in this assembly.
            Fdp.Examples.UrbanCombat.Generated.HsmActionRegistrar.RegisterAll();

            // 3. Create the road network blob (disposed in Dispose()).
            Road = DemoEnvironmentSetup.CreateCityIntersection();

            // 4. Allocate RaycastBatchData singleton (NativeArrays — retained until Dispose()).
            // The module is stored as a field so Dispose() can free the native arrays
            // at the correct time. Using `using` here would free them prematurely,
            // leaving the world singleton with a dangling pointer (AV on first tick).
            _physicsModule = new PhysicsToolkitModule();
            _physicsModule.Initialize(World);

            // 5. Register all behaviors.
            RegisterBehaviors();

            // 6. Build the system pipeline.
            RegisterSystems();

            // 7. Seed GlobalTime singleton so DeltaTime is available on frame 0.
            World.SetSingleton(new GlobalTime { DeltaTime = Dt, TimeScale = 1f });

            _initialized = true;
        }

        /// <summary>
        /// Runs the 600-frame Urban Ambush scenario.
        /// Prints a completion message when the simulation finishes.
        /// </summary>
        public void Run()
        {
            if (!_initialized)
                throw new InvalidOperationException("HeadlessDemoApp.Initialize() must be called before Run().");

            RunSimulation(TotalFrames);
            Console.WriteLine("[UrbanAmbush] Simulation complete.");
        }

        /// <summary>
        /// Executes exactly <paramref name="frames"/> simulation frames.
        /// Each frame: GlobalTime update → SwapBuffers → Input → Sim → PostSim → Export → Tick.
        /// </summary>
        /// <param name="frames">Number of frames to simulate.</param>
        public void RunSimulation(int frames)
        {
            if (!_initialized)
                throw new InvalidOperationException("HeadlessDemoApp.Initialize() must be called before RunSimulation().");

            for (int frame = 0; frame < frames; frame++)
            {
                // Update GlobalTime so DeltaTime and FrameNumber are available to all systems.
                World.SetSingleton(new GlobalTime
                {
                    TotalTime   = frame * (double)Dt,
                    DeltaTime   = Dt,
                    TimeScale   = 1f,
                    FrameNumber = frame,
                });
                World.SetSimulationTime(frame * Dt);

                // Swap double-buffered event streams: write → read, read → empty write.
                World.Bus.SwapBuffers();

                foreach (var sys in _inputModuleSystems) sys.Execute(World, Dt);
                foreach (var sys in _simModuleSystems) sys.Execute(World, Dt);
                foreach (var sys in _postSimModuleSystems) sys.Execute(World, Dt);
                foreach (var sys in _exportModuleSystems) sys.Execute(World, Dt);

                // Flush deferred command buffer: events posted via cmd.PublishEvent during
                // system execution are recorded in the per-thread ECB and must be played
                // back so they enter the bus write buffer.  SwapBuffers() at the top of the
                // next frame then makes them visible to all ReadEvents<T>() calls.
                ((EntityCommandBuffer)((ISimulationView)World).GetCommandBuffer()).Playback(World);

                World.Tick();
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────────

        private void RegisterComponents()
        {
            // Fdp.Core universal spatial primitives
            World.RegisterComponent<SimTransform>();
            World.RegisterComponent<SimVelocity>();
            World.RegisterComponent<Health>();

            // FDP.Toolkit.Behavior
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.SimTier>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainBlackboard>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainBTreeState>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainHsm128>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainHsm64>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.ActorCapabilityState>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.PreviousCapabilities>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.WeaponChannel>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.InteractionChannel>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            World.RegisterComponent<Fdp.Toolkit.Behavior.Components.MissionPlanQueue>();

            // FDP.Toolkit.Perception
            World.RegisterComponent<EntityInfo>();
            World.RegisterComponent<Fdp.Toolkit.Perception.Components.PerceptionReceptor>();
            World.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>();

            // FDP.Toolkit.Physics
            World.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();

            // Events routed via EntityCommandBuffer (must be pre-registered so ECB playback
            // can call Bus.PublishRaw without hitting the "not registered" guard).
            World.RegisterEvent<RaycastRequestEvent>();
            World.RegisterEvent<RaycastResultEvent>();

            // FDP.Toolkit.Combat
            World.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponState>();
            World.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            World.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();

            // FDP.Toolkit.CarKinem
            World.RegisterComponent<CarKinem.Core.VehicleState>();
            World.RegisterComponent<CarKinem.Core.VehicleParams>();
            World.RegisterComponent<CarKinem.Core.NavState>();
        }

        private void RegisterBehaviors()
        {
            // ── Civilian (no BTree / HSM needed — TrafficBrainSystem handles tier-1) ──
            _behaviorRegistry.Register(BehaviorIds.WanderCivil, "WanderCivil",
                new BehaviorDefinition { Name = "WanderCivil", BrainTier = 0 });

            _behaviorRegistry.Register(BehaviorIds.PanicFlee, "PanicFlee",
                new BehaviorDefinition { Name = "PanicFlee", BrainTier = 0 });

            // ── APC: HSM ConvoyEscort ─────────────────────────────────────────────────
            _behaviorRegistry.Register(BehaviorIds.ConvoyEscort, "ConvoyEscort",
                new BehaviorDefinition
                {
                    Name          = "ConvoyEscort",
                    BrainTier     = BehaviorConstants.BrainTierHsm,
                    HsmDefinition = ApcHsmSetup.Build(),
                });

            // ── InfantrySoldier: minimal hold-position BTree ──────────────────────────
            var holdReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
            holdReg.Register("HoldPosition", InsurgentNodes.Action_HoldPosition);
            var holdBlob = TreeCompiler.CompileFromJson(InfantryCombatJson);
            _behaviorRegistry.Register(BehaviorIds.InfantryCombat, "InfantryCombat",
                new BehaviorDefinition
                {
                    Name             = "InfantryCombat",
                    BrainTier        = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(holdBlob, holdReg),
                });

            // ── Insurgent: Ambush BTree ───────────────────────────────────────────────
            var ambushReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
            ambushReg.Register("Condition_HasTarget", InsurgentNodes.Condition_HasTarget);
            ambushReg.Register("Action_AimAndFire",   InsurgentNodes.Action_AimAndFire);
            ambushReg.Register("Action_HoldPosition", InsurgentNodes.Action_HoldPosition);
            ambushReg.RegisterDeactivator("Fdp.Examples.UrbanCombat.Brains.InsurgentNodes.Action_AimAndFire", InsurgentNodes.Deactivate_AimAndFire);
            var ambushBlob = TreeCompiler.CompileFromJson(AmbushJson);
            _behaviorRegistry.Register(BehaviorIds.Ambush, "Ambush",
                new BehaviorDefinition
                {
                    Name             = "Ambush",
                    BrainTier        = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(ambushBlob, ambushReg),
                });
        }

        private void RegisterSystems()
        {
            _trajectoryPool = new TrajectoryPoolManager();

            // ── Input phase ────────────────────────────────────────────
            _inputModuleSystems.Add(new BehaviorIngressSystem(_behaviorRegistry));
            _inputModuleSystems.Add(new FireProcessingSystem());
            _inputModuleSystems.Add(new RaycastSolverSystem());
            _inputModuleSystems.Add(new HitResolutionSystem());

            // ── Simulation phase ───────────────────────────────────────────
            _simModuleSystems.Add(new MissionDirectorSystem());
            _simModuleSystems.Add(new TrafficBrainSystem());
            // CognitiveRuntimeModule groups ChannelArbitration, CognitiveInterrupt,
            // BTreeTick, HsmTick, and CognitiveCleanup in the correct order.
            var cognitiveModule = new CognitiveRuntimeModule(_behaviorRegistry);
            foreach (var sys in cognitiveModule.SimulationSystems)
                _simModuleSystems.Add(sys);
            _simModuleSystems.Add(new DamageSystem());
            _simModuleSystems.Add(new AudioPerceptionSystem());

            var weaponSys = new WeaponDispatcherSystem();
            weaponSys.RegisterExecutor(CombatConstants.ActionIdAimAndFire, new AimAndFireExecutor());
            _simModuleSystems.Add(weaponSys);

            var interactSys = new InteractionDispatcherSystem();
            interactSys.RegisterExecutor(BehaviorConstants.ActionIdEjectPassengers, new EjectPassengersExecutor());
            interactSys.RegisterExecutor(BehaviorConstants.ActionIdOpenDoor, new OpenDoorExecutor());
            _simModuleSystems.Add(interactSys);

            _simModuleSystems.Add(new LocomotionDispatcherSystem());

            // SpatialHashSystem and CarKinematicsSystem are both [UpdateInGroup(SimulationSystemGroup)].
            // CarKinematicsSystem is [UpdateAfter(SpatialHashSystem)] — topological sort handles order.
            _simModuleSystems.Add(new SpatialHashSystem());
            _simModuleSystems.Add(new CarKinematicsSystem(_trajectoryPool));

            // ── PostSimulation phase ─────────────────────────────────────────
            _postSimModuleSystems.Add(new LinearKinematicsSystem());
            _postSimModuleSystems.Add(new BallisticsSystem());

            // ── Export phase ────────────────────────────────────────────
            _exportModuleSystems.Add(new TelemetryReporterSystem());
        }

        // ── IDisposable ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (!_disposed)
            {
                // Dispose systems that implement IDisposable.
                foreach (var sys in _inputModuleSystems)   (sys as IDisposable)?.Dispose();
                foreach (var sys in _simModuleSystems)     (sys as IDisposable)?.Dispose();
                foreach (var sys in _postSimModuleSystems) (sys as IDisposable)?.Dispose();
                foreach (var sys in _exportModuleSystems)  (sys as IDisposable)?.Dispose();

                // Dispose the trajectory pool.
                _trajectoryPool?.Dispose();

                // Free the persistent NativeArrays owned by the physics module.
                // This must happen before World.Dispose() to avoid use-after-free,
                // and via the module (not the world's copy) to prevent double-free.
                _physicsModule?.Dispose();

                // Dispose the road network blob.
                if (_initialized) Road.Dispose();

                // Dispose the ECS world.
                World?.Dispose();

                _disposed = true;
            }
        }
    }
}
