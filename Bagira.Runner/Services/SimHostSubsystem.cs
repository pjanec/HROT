using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Bagira.Map.Common;
using Bagira.Map.Definitions.Tkb;
using Bagira.Runner.Abstractions;
using Bagira.Runner.Models;
using Bagira.SimHost;
using Bagira.SimHost.Configuration;
using Bagira.SimHost.Modules;
using Bagira.SimHost.Systems;
using Bagira.Map.Common.Replication.Egress;
using Bagira.Map.Common.Replication.Ingress;
using Bagira.Map.Common.Replication;
using Bagira.SimHost.Utilities;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
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
using Bagira.SimHost.Components;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;
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
        private SystemGroup?            _kernelGroup;
        private DdsIdAllocator?         _idAllocator;
        private DdsIdAllocatorServer?   _idAllocatorServer;
        private FdpEventBus?            _eventBus;
        private NetworkEntityMap?       _entityMap;
        private IGeographicTransform?   _geoTransform;
        private bool                    _headless;
        private bool                    _initialized;

        // ── Visualization (non-headless only) ─────────────────────────────────
        private SimHostVisualization?   _vis;
        private SimulationLogicModule?  _simLogicModule;

        // ── Background loop (standalone mode) ────────────────────────────────

        private CancellationTokenSource? _cts;
        private Thread?                  _loopThread;

        // ── Public ECS access ─────────────────────────────────────────────────

        /// <summary>
        /// Provides access to the ECS <see cref="EntityRepository"/> after
        /// <see cref="Initialize"/> has been called.  Returns <see langword="null"/>
        /// when the subsystem has not yet been initialised.
        /// </summary>
        public EntityRepository? World => _world;

        /// <summary>
        /// TestHook: exposes the NetworkEntityMap for integration test assertions.
        /// </summary>
        internal NetworkEntityMap TestHook_EntityMap => _entityMap
            ?? throw new InvalidOperationException("SimHostSubsystem is not initialized.");

        /// <summary>
        /// TestHook: spawns an entity via the network spawning pipeline and returns its network ID.
        /// </summary>
        internal long TestHook_SpawnEntity(long tkbType, GeoPosition position)
        {
            if (_world == null || _idAllocator == null || _entityMap == null)
                throw new InvalidOperationException("SimHostSubsystem is not initialized.");

            long networkId = _idAllocator.AllocateId();

            var initialComponents = new List<object>();
            if (_geoTransform != null)
            {
                var cart = _geoTransform.ToCartesian(position.Latitude, position.Longitude, position.Altitude);
                var cartPos = new Vector3((float)cart.X, (float)cart.Y, (float)cart.Z);
                initialComponents.Add(new SimTransform
                {
                    Position = cartPos,
                    Rotation = Quaternion.Identity
                });
            }

            initialComponents.Add(new GeoTransform
            {
                Latitude = position.Latitude,
                Longitude = position.Longitude,
                Altitude = (float)position.Altitude,
                HeadingDeg = 0f,
                PitchDeg = 0f,
                RollDeg = 0f
            });

            _world.Bus.PublishManaged(new SpawnEntityCommand
            {
                NetworkId = networkId,
                TkbType = tkbType,
                DisType = 0,
                OwnerNodeId = 1,
                InitType = ReliableInitType.AllPeers,
                InitialComponents = initialComponents,
                RequestId = Guid.Empty
            });

            return networkId;
        }

        /// <summary>
        /// TestHook: returns child entities that reference the given parent via <see cref="PartMetadata"/>.
        /// </summary>
        internal List<Entity> TestHook_GetChildEntities(Entity parentEntity)
        {
            if (_world == null)
                throw new InvalidOperationException("SimHostSubsystem is not initialized.");

            var children = new List<Entity>();
            var query = _world.Query().With<PartMetadata>().Build();
            foreach (var entity in query)
            {
                var meta = _world.GetComponent<PartMetadata>(entity);
                if (meta.ParentEntity == parentEntity)
                    children.Add(entity);
            }

            return children;
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
            var tkbDb          = BagiraEnvironment.CreateTkb();
            var entityMap      = new NetworkEntityMap();
            _entityMap = entityMap;
            _idAllocator       = new DdsIdAllocator(ddsParticipant, "SimHostAllocator");
            _idAllocatorServer = new DdsIdAllocatorServer(ddsParticipant);

            // ── 3. Geodetic configuration ─────────────────────────────────────
            var wgs84 = new WGS84Transform();
            wgs84.SetOrigin(
                simConfig.GeodeticOrigin.Latitude,
                simConfig.GeodeticOrigin.Longitude,
                simConfig.GeodeticOrigin.Altitude);
            _geoTransform = wgs84;

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

            _kernelGroup = new SystemGroup();
            _kernelGroup.Create(_world);
            _kernelGroup.AddSystem(new MissionControlRequestSystem(ddsParticipant, entityMap, doctrineRegistry));
            _simLogicModule.RegisterSystems(_kernelGroup, _kernelGroup, _kernelGroup);

            // Seed GlobalTime singleton.
            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 1.0f / simConfig.SimulationRateHz,
                TimeScale = 1.0f
            });

            // ── 6. Toolkit modules ────────────────────────────────────────────
            var geoModule = new GeographicModule(wgs84);
            _kernel.RegisterModule(geoModule);

            var elm = new EntityLifecycleModule(tkbDb, new List<int>());
            _kernel.RegisterModule(elm);

            var spawningSystem = new NetworkSpawningSystem(
                tkbDb, elm, entityMap, _idAllocator, localNodeId: 1);

            var simHostMod = new SimHostModule(
                ddsParticipant, tkbDb, _idAllocator, 1,
                spawningSystem, entityMap, doctrineRegistry, wgs84);
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

            // ── 8. Kernel init ────────────────────────────────────────────────
            _kernel.Initialize();

            // ── 9. Visualization (skipped in headless mode) ───────────────────
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
            _idAllocatorServer?.ProcessRequests();
            _vis?.Update(deltaTime);
            _kernel!.Update();
            _kernelGroup!.Run();
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
            _vis?.Dispose();
            _vis = null;
            _idAllocatorServer?.Dispose();
            _idAllocatorServer = null;
            _idAllocator?.Dispose();
            _kernelGroup?.Dispose();
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
            world.RegisterComponent<MissionPlanQueue>();

            // Combat + perception
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<HealthData>();
            world.RegisterComponent<BallisticProjectile>();
            world.RegisterComponent<Faction>();
            world.RegisterComponent<PhysicsCollider>();

            // CarKinem / navigation
            world.RegisterComponent<CarKinem.Core.VehicleState>();
            world.RegisterComponent<CarKinem.Core.VehicleParams>();
            world.RegisterComponent<CarKinem.Core.NavState>();
            world.RegisterComponent<CarKinem.Formation.FormationMember>();
            world.RegisterComponent<CarKinem.Formation.FormationRoster>();
            world.RegisterComponent<CarKinem.Formation.FormationTarget>();
            world.RegisterComponent<VisualData>();

            // Managed
            world.RegisterManagedComponent<SimCombatDef>();
            world.RegisterManagedComponent<TkbCompositionDef>();
            world.RegisterManagedComponent<EntityMissionHolder>();

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
