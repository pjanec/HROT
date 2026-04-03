using System;
using CarKinem.Road;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Examples.UrbanCombat.Brains;
using Fdp.Examples.UrbanCombat.Setup;
using Fdp.Examples.UrbanCombat.Systems;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Toolkit.Tkb;
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Executors;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Perception.Systems;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Systems;
using FDP.Toolkit.CarKinem.Systems;

namespace Fdp.Examples.UrbanCombat
{
    /// <summary>
    /// Orchestrator for the headless "Urban Ambush" demo simulation.
    /// <para>
    /// Call <see cref="Initialize"/> once to register all components, build the doctrine
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
        /// The doctrine registry: maps doctrine IDs to their definitions (BrainTier, HSM blob, BTree interpreter).
        /// Populated during <see cref="Initialize"/>; read-only thereafter.
        /// </summary>
        public DoctrineRegistry DoctrineRegistry => _doctrineRegistry;

        /// <summary>
        /// The road network blob for the city intersection.
        /// Caller must not dispose; <see cref="Dispose"/> handles it.
        /// </summary>
        public RoadNetworkBlob Road { get; private set; }

        private readonly TkbDatabase        _tkb              = new TkbDatabase();
        private readonly DoctrineRegistry   _doctrineRegistry = new DoctrineRegistry();
        private readonly NetworkEntityMap   _entityMap        = new NetworkEntityMap();
        private TrajectoryPoolManager?      _trajectoryPool;

        /// <summary>
        /// The shared <see cref="NetworkEntityMap"/> used by the combat CQRS chain.
        /// Pass this to <see cref="ScenarioDirector"/> so spawned entities are registered
        /// and <see cref="FDP.Toolkit.Combat.Events.WeaponFireIntent"/> IDs resolve correctly.
        /// </summary>
        public NetworkEntityMap EntityMap => _entityMap;

        // System groups — created in RegisterSystems(), disposed in Dispose().
        private InputSystemGroup?           _inputGroup;
        private SimulationSystemGroup?      _simGroup;
        private PostSimulationSystemGroup?  _postSimGroup;
        private ExportSystemGroup?          _exportGroup;

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
        /// Registers all ECS component types, builds the doctrine registry, allocates
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

            // 5. Register all doctrines.
            RegisterDoctrines();

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

                _inputGroup!.Run();
                _simGroup!.Run();
                _postSimGroup!.Run();
                _exportGroup!.Run();

                World.Tick();
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────────

        private void RegisterComponents()
        {
            // Fdp.Kernel universal spatial primitives
            World.RegisterComponent<SimTransform>();
            World.RegisterComponent<SimVelocity>();
            World.RegisterComponent<Health>();

            // FDP.Toolkit.Behavior
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.DoctrineState>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.SimTier>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainBlackboard>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainBTreeState>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainHsm128>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainHsm64>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.ActorCapabilityState>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.PreviousCapabilities>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.LocomotionChannel>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.WeaponChannel>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.InteractionChannel>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.PassengerBuffer>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.IsEmbarkedTag>();
            World.RegisterComponent<FDP.Toolkit.Behavior.Components.MissionPlanQueue>();

            // FDP.Toolkit.Perception
            World.RegisterComponent<FDP.Toolkit.Perception.Components.Faction>();
            World.RegisterComponent<FDP.Toolkit.Perception.Components.PerceptionReceptor>();
            World.RegisterComponent<FDP.Toolkit.Perception.Components.TargetMemory>();

            // FDP.Toolkit.Physics
            World.RegisterComponent<FDP.Toolkit.Physics.Components.PhysicsCollider>();

            // FDP.Toolkit.Combat
            World.RegisterComponent<FDP.Toolkit.Combat.Components.WeaponState>();
            World.RegisterComponent<FDP.Toolkit.Combat.Components.Health>();
            World.RegisterComponent<FDP.Toolkit.Combat.Components.BallisticProjectile>();

            // FDP.Toolkit.CarKinem
            World.RegisterComponent<CarKinem.Core.VehicleState>();
            World.RegisterComponent<CarKinem.Core.VehicleParams>();
            World.RegisterComponent<CarKinem.Core.NavState>();
        }

        private void RegisterDoctrines()
        {
            // ── Civilian (no BTree / HSM needed — TrafficBrainSystem handles tier-1) ──
            _doctrineRegistry.Register(DoctrineIds.WanderCivil, "WanderCivil",
                new DoctrineDefinition { Name = "WanderCivil", BrainTier = 0 });

            _doctrineRegistry.Register(DoctrineIds.PanicFlee, "PanicFlee",
                new DoctrineDefinition { Name = "PanicFlee", BrainTier = 0 });

            // ── APC: HSM ConvoyEscort ─────────────────────────────────────────────────
            _doctrineRegistry.Register(DoctrineIds.ConvoyEscort, "ConvoyEscort",
                new DoctrineDefinition
                {
                    Name          = "ConvoyEscort",
                    BrainTier     = BehaviorConstants.BrainTierHsm,
                    HsmDefinition = ApcHsmSetup.Build(),
                });

            // ── InfantrySoldier: minimal hold-position BTree ──────────────────────────
            var holdReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
            holdReg.Register("HoldPosition", InsurgentNodes.Action_HoldPosition);
            var holdBlob = TreeCompiler.CompileFromJson(InfantryCombatJson);
            _doctrineRegistry.Register(DoctrineIds.InfantryCombat, "InfantryCombat",
                new DoctrineDefinition
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
            var ambushBlob = TreeCompiler.CompileFromJson(AmbushJson);
            _doctrineRegistry.Register(DoctrineIds.Ambush, "Ambush",
                new DoctrineDefinition
                {
                    Name             = "Ambush",
                    BrainTier        = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(ambushBlob, ambushReg),
                });
        }

        private void RegisterSystems()
        {
            _trajectoryPool = new TrajectoryPoolManager();

            // ── Input group ─────────────────────────────────────────────────────────
            _inputGroup = new InputSystemGroup();
            _inputGroup.Create(World);
            _inputGroup.AddSystem(new DoctrineIngressSystem(_doctrineRegistry));
            _inputGroup.AddSystem(new FireProcessingSystem(_entityMap));
            _inputGroup.AddSystem(new RaycastSolverSystem());
            _inputGroup.AddSystem(new HitResolutionSystem());

            // ── Simulation group ────────────────────────────────────────────────────
            _simGroup = new SimulationSystemGroup();
            _simGroup.Create(World);
            _simGroup.AddSystem(new MissionDirectorSystem());
            _simGroup.AddSystem(new TrafficBrainSystem());
            _simGroup.AddSystem(new ChannelArbitrationSystem());
            _simGroup.AddSystem(new BTreeTickSystem(_doctrineRegistry));
            _simGroup.AddSystem(new HsmTickSystem<BrainHsm128>(_doctrineRegistry));
            _simGroup.AddSystem(new DamageSystem());
            _simGroup.AddSystem(new HsmDamageBridgeSystem());
            _simGroup.AddSystem(new AudioPerceptionSystem());

            var weaponSys = new WeaponDispatcherSystem();
            weaponSys.RegisterExecutor(CombatConstants.ActionIdAimAndFire, new AimAndFireExecutor(_entityMap));
            _simGroup.AddSystem(weaponSys);

            var interactSys = new InteractionDispatcherSystem();
            interactSys.RegisterExecutor(3, new EjectPassengersExecutor());
            _simGroup.AddSystem(interactSys);

            _simGroup.AddSystem(new LocomotionDispatcherSystem());

            // SpatialHashSystem and CarKinematicsSystem are both [UpdateInGroup(SimulationSystemGroup)].
            // CarKinematicsSystem is [UpdateAfter(SpatialHashSystem)] — topological sort handles order.
            _simGroup.AddSystem(new SpatialHashSystem());
            _simGroup.AddSystem(new CarKinematicsSystem(Road, _trajectoryPool));

            // ── PostSimulation group ─────────────────────────────────────────────────
            _postSimGroup = new PostSimulationSystemGroup();
            _postSimGroup.Create(World);
            _postSimGroup.AddSystem(new LinearKinematicsSystem());
            _postSimGroup.AddSystem(new BallisticsSystem());

            // ── Export group ─────────────────────────────────────────────────────────
            _exportGroup = new ExportSystemGroup();
            _exportGroup.Create(World);
            _exportGroup.AddSystem(new TelemetryReporterSystem());
        }

        // ── IDisposable ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (!_disposed)
            {
                // Dispose system groups (each group disposes its member systems).
                _exportGroup?.Dispose();
                _postSimGroup?.Dispose();
                _simGroup?.Dispose();
                _inputGroup?.Dispose();

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
