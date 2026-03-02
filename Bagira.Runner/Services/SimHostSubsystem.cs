using System;
using System.Collections.Generic;
using System.Threading;
using Bagira.Runner.Abstractions;
using Bagira.Runner.Models;
using Bagira.SimHost;
using Bagira.SimHost.Configuration;
using Bagira.SimHost.Modules;
using Bagira.SimHost.Systems;
using Bagira.SimHost.Translators;
using Bagira.SimHost.Util;
using Bagira.SimHost.Utilities;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Time.Controllers;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Tkb;
using ModuleHost.Core;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;
using ModuleHost.Core.Time;
using ModuleHost.Network.Cyclone.Modules;
using ModuleHost.Network.Cyclone.Services;
using ModuleHost.Network.Cyclone.Systems;
using ModuleHost.Network.Cyclone.Translators;
using CycloneDDS.Runtime;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using Bagira.Map.Common;
using Bagira.SimHost.Components;
using Bagira.Map.Definitions.Tkb;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.Replication.Components;
using Fdp.Modules.Geographic.Components;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;

namespace Bagira.Runner.Services
{
    /// <summary>
    /// <see cref="ISubsystem"/> implementation that embeds the SimHost simulation kernel.
    ///
    /// <para>Lifecycle:
    /// <list type="number">
    ///   <item><see cref="Initialize"/> — creates ECS world, kernel, modules, DDS participant.</item>
    ///   <item><see cref="Update"/> — ticks kernel + simulation-logic group (no rendering).</item>
    ///   <item><see cref="DrawWorld"/> — no-op (SimHost has no 3-D world visuals).</item>
    ///   <item><see cref="DrawUI"/> — renders ImGui control panels when not headless.</item>
    ///   <item><see cref="Shutdown"/> — disposes all managed resources.</item>
    /// </list>
    /// </para>
    /// <para>
    /// For standalone use outside the orchestrator, call <see cref="Start"/> after
    /// <see cref="Initialize"/> to spin up a background simulation thread, then
    /// <see cref="Stop"/> to gracefully shut it down.
    /// </para>
    /// </summary>
    public sealed class SimHostSubsystem : ISubsystem
    {
        // ── Subsystem identity ────────────────────────────────────────────────

        /// <inheritdoc/>
        public string Name => "SimHost";

        // ── Runtime objects ───────────────────────────────────────────────────

        private EntityRepository?       _world;
        private ModuleHostKernel?       _kernel;
        private SystemGroup?            _inputGroup;
        private SystemGroup?            _simulationGroup;
        private SystemGroup?            _postSimulationGroup;
        private DdsIdAllocator?         _idAllocator;
        private FdpEventBus?            _eventBus;
        private WGS84Transform?         _geoTransform;
        private bool                    _headless;
        private bool                    _initialized;

        // Background server for network-distributed ID allocation
        private DdsIdAllocatorServer?   _idAllocatorServer;
        private CancellationTokenSource? _allocatorCts;
        private System.Threading.Tasks.Task? _allocatorTask;

        // ── Visualization (non-headless only) ─────────────────────────────────
        private SimHostVisualization?   _vis;
        private SimulationLogicModule?  _simLogicModule;

        // ── Background loop (standalone mode) ────────────────────────────────

        private CancellationTokenSource? _cts;
        private Thread?                  _loopThread;

        private static long _testNetworkId;

        // ── Public ECS access ─────────────────────────────────────────────────

        /// <summary>
        /// Internal test hook for integration tests.
        /// </summary>
        internal EntityRepository World => _world ?? throw new InvalidOperationException("Not initialized");

        /// <summary>
        /// Internal test hook for integration tests.
        /// </summary>
        internal ModuleHostKernel Kernel => _kernel ?? throw new InvalidOperationException("Not initialized");

        /// <summary>
        /// Internal test hook to spawn an entity directly in the SimHost world.
        /// </summary>
        internal long TestHook_SpawnEntity(long tkbType, GeoPosition position)
        {
            if (_world == null || _geoTransform == null)
                throw new InvalidOperationException("Not initialized");

            var descriptors = new List<EntityDescriptorUnion>
            {
                new EntityDescriptorUnion
                {
                    _d = EDescriptorType.dtEntityMaster,
                    EntityMaster = new EntityMaster { TkbType = tkbType }
                },
                new EntityDescriptorUnion
                {
                    _d = EDescriptorType.dtGeoSpatial,
                    GeoSpatial = new GeoSpatial { Pos = position }
                }
            };

            long networkId = Interlocked.Increment(ref _testNetworkId);
            var initialComponents = DescriptorMapper.MapToComponents(descriptors, _geoTransform);

            _world.Bus.PublishManaged(new SpawnEntityCommand
            {
                NetworkId = networkId,
                TkbType = tkbType,
                DisType = 0,
                OwnerNodeId = 1,
                InitType = ReliableInitType.AllPeers,
                InitialComponents = initialComponents,
                RequestId = Guid.NewGuid()
            });

            return networkId;
        }

        // ── ISubsystem ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the ECS world, registers all SimHost modules, and connects to DDS.
        /// Mirrors the initialisation sequence from <c>Bagira.SimHost/Program.cs</c>.
        /// </summary>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;

            Logger.Info("[SimHost] Initializing...");

            // Load JSON config (generates defaults if missing).
            var simConfig = SimHostConfig.Load("config.json");

            var domainId = config.DomainId > 0 ? config.DomainId : simConfig.DomainId;
            Logger.Info($"[SimHost] Domain ID:       {domainId}");
            Logger.Info($"[SimHost] Simulation Rate: {simConfig.SimulationRateHz} Hz");

            // ── 1. Kernel ─────────────────────────────────────────────────────
            _world = new EntityRepository();
            RegisterSimComponents(_world);
            var eventAccumulator = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, eventAccumulator);

            _eventBus    = new FdpEventBus();
            var timeConfig  = new TimeControllerConfig { Mode = TimeMode.Continuous, Role = TimeRole.Master };
            var timeCtrl    = TimeControllerFactory.Create(_eventBus, timeConfig);
            timeCtrl.SetTimeScale(1.0f);
            _kernel.SetTimeController(timeCtrl);
            _eventBus.SwapBuffers();

            // ── 2. Data services ──────────────────────────────────────────────
            var ddsParticipant = new DdsParticipant((uint)domainId);

            // Spin up the ID Allocator Server as a non-blocking background task
            _idAllocatorServer = new DdsIdAllocatorServer(ddsParticipant);
            _allocatorCts = new CancellationTokenSource();
            _allocatorTask = System.Threading.Tasks.Task.Run(() =>
            {
                while (!_allocatorCts.Token.IsCancellationRequested)
                {
                    try { _idAllocatorServer.ProcessRequests(); } catch { }
                    Thread.Sleep(5);
                }
            });

            var tkbDb          = BagiraEnvironment.CreateTkb();
            var entityMap      = new NetworkEntityMap();
            _idAllocator       = new DdsIdAllocator(ddsParticipant, "SimHostAllocator");

            // ── 3. Geodetic configuration ─────────────────────────────────────
            _geoTransform = new WGS84Transform();
            _geoTransform.SetOrigin(
                simConfig.GeodeticOrigin.Latitude,
                simConfig.GeodeticOrigin.Longitude,
                simConfig.GeodeticOrigin.Altitude);

            // ── 4. Doctrine registry ──────────────────────────────────────────
            var doctrineRegistry = new DoctrineRegistry();
            doctrineRegistry.Register(SimHostDoctrineIds.MoveTo_BT, "MoveToLocation",
                new DoctrineDefinition { Name = "MoveToLocation", BrainTier = BehaviorConstants.BrainTierBTree });
            doctrineRegistry.Register(SimHostDoctrineIds.FollowRoute_BT, "FollowRoute",
                new DoctrineDefinition { Name = "FollowRoute",   BrainTier = BehaviorConstants.BrainTierBTree });
            doctrineRegistry.Register(SimHostDoctrineIds.JoinFormation_BT, "JoinFormation",
                new DoctrineDefinition { Name = "JoinFormation", BrainTier = BehaviorConstants.BrainTierBTree });
            doctrineRegistry.Register(SimHostDoctrineIds.Idle_HSM, "Idle",
                new DoctrineDefinition { Name = "Idle",          BrainTier = BehaviorConstants.BrainTierHsm });

            // ── 5. SimulationLogicModule ──────────────────────────────────────
            // Load road network from file so the visualizer can show roads.
            var roadNetwork = new RoadNetworkBlob();
            try { roadNetwork = RoadNetworkLoader.LoadFromJson("Assets/sample_road.json"); }
            catch { /* run fine without roads */ }

            _simLogicModule = new SimulationLogicModule(
                doctrineRegistry,
                entityMap,
                vehicleAPI:  null,
                roadNetwork: roadNetwork);

            _inputGroup = new SystemGroup();
            _inputGroup.Create(_world);
            _simulationGroup = new SystemGroup();
            _simulationGroup.Create(_world);
            _postSimulationGroup = new SystemGroup();
            _postSimulationGroup.Create(_world);

            _inputGroup.AddSystem(new MissionControlRequestSystem(ddsParticipant, entityMap, doctrineRegistry));
            _simLogicModule.RegisterSystems(_inputGroup, _simulationGroup, _postSimulationGroup);

            // Seed GlobalTime singleton.
            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 1.0f / simConfig.SimulationRateHz,
                TimeScale = 1.0f
            });

            // ── 6. Toolkit modules ────────────────────────────────────────────
            var geoModule = new GeographicModule(_geoTransform);
            _kernel.RegisterModule(geoModule);

            var elm = new EntityLifecycleModule(tkbDb, new List<int>());
            _kernel.RegisterModule(elm);

            _kernel.RegisterModule(new ReplicationLogicModule(entityMap, tkbDb));

            var spawningSystem = new NetworkSpawningSystem(
                tkbDb, elm, entityMap, _idAllocator, localNodeId: 1);

            var simHostMod = new SimHostModule(
                ddsParticipant, tkbDb, _idAllocator, 1,
                spawningSystem, entityMap, doctrineRegistry, _geoTransform);
            _kernel.RegisterModule(simHostMod);

            // ── 7. Network module ─────────────────────────────────────────────
            var localNodeId = 1;
            var translators = new List<IDescriptorTranslator>();
            if (simHostMod.GeoEgressTranslator != null)
                translators.Add(simHostMod.GeoEgressTranslator);
            translators.Add(simHostMod.MissionIngressTranslator);
            translators.Add(simHostMod.MissionEgressTranslator);
            var entityMasterEgressTranslator = new EntityMasterEgressTranslator(ddsParticipant, entityMap, localNodeId);
            translators.Add(entityMasterEgressTranslator);
            translators.Add(new FireInteractionEventTranslator(ddsParticipant, entityMap));
            translators.Add(new TimePulseEgressTranslator(ddsParticipant, _eventBus));

            _kernel.RegisterGlobalSystem(
                new CycloneNetworkCleanupSystem(entityMasterEgressTranslator));
            var nodeMapper  = new NodeIdMapper(domainId, localNodeId);
            var topology    = new StaticNetworkTopology(localNodeId, new[] { localNodeId });

            var cycloneModule = new CycloneNetworkModule(
                ddsParticipant, nodeMapper, _idAllocator, topology, elm,
                customTranslators: translators,
                sharedEntityMap:   entityMap);
            _kernel.RegisterModule(cycloneModule);

            // ── 8. Physics toolkit init ───────────────────────────────────────
            // Allocates RaycastBatchData singleton required by raycast systems.
            var physicsModule = new PhysicsToolkitModule();
            physicsModule.Initialize(_world);

            // ── 9. Kernel init ────────────────────────────────────────────────
            _kernel.Initialize();

            // ── 10. Visualization (skipped in headless mode) ──────────────────
            if (!_headless)
            {
                _vis = new SimHostVisualization();
                _vis.Initialize(
                    _world,
                    _kernel,
                    _simLogicModule.RoadNetwork,
                    _simLogicModule.TrajectoryPool,
                    _simLogicModule.FormationTemplates);
                Logger.Info("[SimHost] Visualization initialized.");
            }

            _initialized = true;
            Logger.Info("[SimHost] Initialized.");
        }

        /// <summary>
        /// Ticks the kernel and simulation-logic group by <paramref name="deltaTime"/> seconds.
        /// Called each frame by the orchestrator (or each loop iteration in standalone mode).
        /// </summary>
        public void Update(float deltaTime)
        {
            if (!_initialized) return;
            _vis?.Update(deltaTime);
            _kernel!.Update();
            _inputGroup!.Run();
            _simulationGroup!.Run();
            _postSimulationGroup!.Run();
            _eventBus?.SwapBuffers();
        }

        /// <summary>Renders the 2-D map canvas (road graph + vehicle entities).</summary>
        public void DrawWorld()
        {
            _vis?.DrawWorld();
        }

        /// <summary>Renders ImGui control panels (spawn, simulation controls, inspector).</summary>
        public void DrawUI()
        {
            _vis?.DrawUI();
        }

        /// <summary>Disposes all kernel resources.</summary>
        public void Shutdown()
        {
            Stop();
            if (_world != null && _world.HasSingleton<RaycastBatchData>())
            {
                ref var batch = ref _world.GetSingleton<RaycastBatchData>();
                if (batch.Requests.IsCreated) batch.Requests.Dispose();
                if (batch.Hits.IsCreated) batch.Hits.Dispose();
            }
            // Clean up the background allocator server
            _allocatorCts?.Cancel();
            try { _allocatorTask?.Wait(1000); } catch { }
            _allocatorCts?.Dispose();
            _idAllocatorServer?.Dispose();

            _vis?.Dispose();
            _vis = null;
            _idAllocator?.Dispose();
            _postSimulationGroup?.Dispose();
            _simulationGroup?.Dispose();
            _inputGroup?.Dispose();
            _initialized = false;
            Logger.Info("[SimHost] Shutdown complete.");
        }

        // ── Standalone helpers ────────────────────────────────────────────────

        /// <summary>
        /// Starts a background simulation thread (~60 Hz).
        /// Use this when running SimHost standalone (outside the orchestrator update loop).
        /// The orchestrator calls <see cref="Update"/> directly and does not use this method.
        /// </summary>
        public void Start()
        {
            if (_cts != null) return; // already running
            _cts        = new CancellationTokenSource();
            _loopThread = new Thread(() => RunLoop(_cts.Token))
            {
                IsBackground = true,
                Name         = "SimHost-Loop"
            };
            _loopThread.Start();
            Logger.Info("[SimHost] Background loop started.");
        }

        /// <summary>
        /// Signals the background simulation thread to stop and waits for it to exit.
        /// Safe to call even when <see cref="Start"/> was never called.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _loopThread?.Join(TimeSpan.FromSeconds(3));
            _cts?.Dispose();
            _cts        = null;
            _loopThread = null;
        }

        // ── Private ───────────────────────────────────────────────────────────

        /// <summary>
        /// Registers all ECS component types and events used by the SimHost simulation,
        /// CarKinem physics/navigation, formations, and visualization.  Must be called
        /// immediately after <see cref="EntityRepository"/> construction, before any
        /// module or system is initialised.
        /// </summary>
        private static void RegisterSimComponents(EntityRepository world)
        {
            // Network replication
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<NetworkOwnership>();
            world.RegisterComponent<NetworkAuthority>();
            world.RegisterComponent<NetworkSpawnRequest>();
            world.RegisterComponent<PendingNetworkAck>();

            // Geographic / physics
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<GeoTransform>();
            world.RegisterComponent<GeoVelocity>();

            // Behavior toolkit
            world.RegisterComponent<DoctrineState>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<InteractionChannel>();
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<BrainBTreeState>();
            world.RegisterComponent<BrainBlackboard>();

            // HSM brain tiers (for APC-style HSM doctrines)
            world.RegisterComponent<BrainHsm64>();
            world.RegisterComponent<BrainHsm128>();
            world.RegisterComponent<PreviousCapabilities>();
            world.RegisterComponent<PassengerBuffer>();
            world.RegisterComponent<IsEmbarkedTag>();

            // Perception
            world.RegisterComponent<Faction>();
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();

            // Combat & Physics
            world.RegisterComponent<PhysicsCollider>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<BallisticProjectile>();
            world.RegisterComponent<HealthData>();

            // CarKinem / navigation
            world.RegisterComponent<CarKinem.Core.VehicleState>();
            world.RegisterComponent<CarKinem.Core.VehicleParams>();
            world.RegisterComponent<CarKinem.Core.NavState>();
            world.RegisterComponent<CarKinem.Formation.FormationMember>();
            world.RegisterComponent<CarKinem.Formation.FormationRoster>();
            world.RegisterComponent<CarKinem.Formation.FormationTarget>();

            // Managed
            world.RegisterComponent<MissionPlanQueue>();
            world.RegisterManagedComponent<IgVisualDef>();
            world.RegisterManagedComponent<SimVehicleDef>();
            world.RegisterManagedComponent<SimCombatDef>();
            world.RegisterManagedComponent<TkbCompositionDef>();

            // Lifecycle events
            world.RegisterEvent<ConstructionOrder>();
            world.RegisterEvent<ConstructionAck>();
            world.RegisterEvent<DestructionOrder>();
            world.RegisterEvent<DestructionAck>();
            world.RegisterEvent<Bagira.Map.Common.Events.FireInteractionEvent>();

            // CarKinem command events
            world.RegisterEvent<CmdSpawnVehicle>();
            world.RegisterEvent<CmdCreateFormation>();
            world.RegisterEvent<CmdNavigateToPoint>();
            world.RegisterEvent<CmdFollowTrajectory>();
            world.RegisterEvent<CmdNavigateViaRoad>();
            world.RegisterEvent<CmdJoinFormation>();
            world.RegisterEvent<CmdLeaveFormation>();
            world.RegisterEvent<CmdStop>();
            world.RegisterEvent<CmdSetSpeed>();
        }

        private void RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                Update(0f); // dt managed internally by time controller
                Thread.Sleep(1); // ~1 ms yield; time controller manages dt
            }
            Logger.Info("[SimHost] Background loop exited.");
        }
    }
}
