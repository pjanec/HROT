using System;
using System.Collections.Generic;
using Raylib_cs;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Transforms;
using FDP.Framework.Raylib;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Time.Controllers;
using Fdp.Toolkit.Tkb;
using ModuleHost.Core;
using ModuleHost.Core.Network;
using ModuleHost.Core.Time;
using ModuleHost.Network.Cyclone.Modules;
using ModuleHost.Network.Cyclone.Services;
using ModuleHost.Network.Cyclone.Translators;
using Bagira.BDC.SSTD;
using Bagira.Map.Common;
using Bagira.Map.Definitions.Tkb;
using Bagira.SimHost.Components;
using Bagira.SimHost.Configuration;
using Bagira.SimHost.Modules;
using Bagira.SimHost.Utilities;
using CarKinem.Commands;
using CarKinem.Road;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;

namespace Bagira.SimHost
{
    /// <summary>
    /// Graphical entry-point for the standalone <c>Bagira.SimHost</c> executable.
    ///
    /// <para>Opens a resizable 1280×720 Raylib window with the same 2-D visualization
    /// as the CarKinem demo (road graph, vehicle entities, ImGui control panels) while
    /// keeping the simulation kernel fully network-distributed via CycloneDDS.</para>
    ///
    /// <para>Lifecycle (<see cref="FdpApplication"/>):
    /// <list type="number">
    ///   <item><see cref="OnLoad"/> — bootstrap DDS, kernel, modules, visualization.</item>
    ///   <item><see cref="OnUpdate"/> — tick kernel + simulation group + visualization input.</item>
    ///   <item><see cref="OnDrawWorld"/> — 2-D map canvas.</item>
    ///   <item><see cref="OnDrawUI"/> — ImGui panels.</item>
    ///   <item><see cref="OnUnload"/> — dispose all resources.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class SimHostApp : FdpApplication
    {
        // ── Kernel infrastructure ─────────────────────────────────────────────
        private EntityRepository?    _world;
        private ModuleHostKernel?    _kernel;
        private SystemGroup?         _kernelGroup;
        private DdsIdAllocator?      _idAllocator;

        // ── Visualization ─────────────────────────────────────────────────────
        private SimHostVisualization? _vis;

        // ── SimLogic ─────────────────────────────────────────────────────────
        private SimulationLogicModule? _simLogicModule;

        // ── Headless/test support ────────────────────────────────────────────
        private bool _headless;
        private int? _domainOverride;

        public new EntityRepository World => base.World
            ?? throw new InvalidOperationException("SimHostApp is not initialized.");

        public new ModuleHostKernel Kernel => base.Kernel
            ?? throw new InvalidOperationException("SimHostApp is not initialized.");

        // ── Constructor ───────────────────────────────────────────────────────

        public SimHostApp() : base(new ApplicationConfig
        {
            Width       = 1280,
            Height      = 720,
            WindowTitle = "Bagira SimHost",
            TargetFPS   = 60,
            Flags       = ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint
        })
        {
        }

        // ── FdpApplication lifecycle ──────────────────────────────────────────

        protected override void OnLoad()
        {
            Console.Title = "Bagira.SimHost";
            Logger.Info("[SimHost] Starting graphical application...");

            // ── 1. Load configuration ─────────────────────────────────────────
            var config = SimHostConfig.Load("config.json");
            var domainId = _domainOverride ?? config.DomainId;
            Logger.Info($"[SimHost] Domain ID:       {domainId}");
            Logger.Info($"[SimHost] Simulation Rate: {config.SimulationRateHz} Hz");

            // ── 2. ECS world ──────────────────────────────────────────────────
            _world = new EntityRepository();
            RegisterSimComponents(_world);

            var eventAccumulator = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, eventAccumulator);
            base.World = _world;
            base.Kernel = _kernel;

            // ── 3. Time controller ────────────────────────────────────────────
            var eventBus   = new FdpEventBus();
            var timeConfig = new TimeControllerConfig { Mode = TimeMode.Continuous, Role = TimeRole.Master };
            var timeCtrl   = TimeControllerFactory.Create(eventBus, timeConfig);
            timeCtrl.SetTimeScale(1.0f);
            _kernel.SetTimeController(timeCtrl);

            // ── 4. Data services ──────────────────────────────────────────────
            var ddsParticipant = BagiraEnvironment.CreateParticipant(domainId);
            var tkbDb          = BagiraEnvironment.CreateTkb(BdcTkbCatalog.RegisterAll);
            var entityMap      = new NetworkEntityMap();
            _idAllocator       = new DdsIdAllocator(ddsParticipant, "SimHostAllocator");

            // ── 5. Geodetic configuration ─────────────────────────────────────
            var wgs84 = BagiraEnvironment.CreateGeoTransform();

            // ── 6. Doctrine registry ──────────────────────────────────────────
            var doctrineRegistry = new DoctrineRegistry();
            doctrineRegistry.Register(SimHostDoctrineIds.MoveTo_BT, "MoveToLocation",
                new DoctrineDefinition { Name = "MoveToLocation", BrainTier = BehaviorConstants.BrainTierBTree });
            doctrineRegistry.Register(SimHostDoctrineIds.FollowRoute_BT, "FollowRoute",
                new DoctrineDefinition { Name = "FollowRoute",   BrainTier = BehaviorConstants.BrainTierBTree });
            doctrineRegistry.Register(SimHostDoctrineIds.JoinFormation_BT, "JoinFormation",
                new DoctrineDefinition { Name = "JoinFormation", BrainTier = BehaviorConstants.BrainTierBTree });
            doctrineRegistry.Register(SimHostDoctrineIds.Idle_HSM, "Idle",
                new DoctrineDefinition { Name = "Idle",          BrainTier = BehaviorConstants.BrainTierHsm });

            // ── 7. Road network ───────────────────────────────────────────────
            var roadNetwork = new RoadNetworkBlob();
            try { roadNetwork = RoadNetworkLoader.LoadFromJson("Assets/sample_road.json"); }
            catch { /* run fine without roads */ }

            // ── 8. SimulationLogicModule ──────────────────────────────────────
            _simLogicModule = new SimulationLogicModule(
                doctrineRegistry,
                entityMap,
                vehicleAPI:  null,
                roadNetwork: roadNetwork);

            _kernelGroup = new SystemGroup();
            _kernelGroup.Create(_world);
            _simLogicModule.RegisterSystems(_kernelGroup);

            // Seed GlobalTime singleton.
            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 1.0f / config.SimulationRateHz,
                TimeScale = 1.0f
            });

            // ── 9. Toolkit modules ────────────────────────────────────────────
            var geoModule = new GeographicModule(wgs84);
            _kernel.RegisterModule(geoModule);

            var elm = new EntityLifecycleModule(tkbDb, new List<int>());
            _kernel.RegisterModule(elm);

            var spawningSystem = new NetworkSpawningSystem(
                tkbDb,
                elm,
                entityMap,
                _idAllocator,
                SimHostNetworkConstants.LocalNodeId,
                disTypeExtractor: null,
                onEntitySpawned: (world, entity, isLocalAuthority) =>
                {
                    if (isLocalAuthority && world.HasComponent<EntityMaster>(entity))
                    {
                        world.SetAuthority<EntityMaster>(entity, true);
                    }
                });

            var simHostMod = new SimHostModule(
                ddsParticipant, tkbDb, _idAllocator, SimHostNetworkConstants.LocalNodeId,
                spawningSystem, entityMap, wgs84);
            _kernel.RegisterModule(simHostMod);

            // ── 10. Network module ──────────────────────────────────────────
            var translators = new List<IDescriptorTranslator>();
            if (simHostMod.GeoEgressTranslator != null)
                translators.Add(simHostMod.GeoEgressTranslator);
            translators.Add(simHostMod.MissionIngressTranslator);
            translators.Add(simHostMod.MissionEgressTranslator);
            translators.Add(new AutoCycloneTranslator<EntityMaster>(ddsParticipant, "EntityMaster", 0, entityMap));

            var localNodeId = SimHostNetworkConstants.LocalNodeId;
            var nodeMapper  = new NodeIdMapper(domainId, localNodeId);
            var topology    = new StaticNetworkTopology(localNodeId, new[] { localNodeId });

            var cycloneModule = new CycloneNetworkModule(
                ddsParticipant, nodeMapper, _idAllocator, topology, elm,
                customTranslators: translators,
                sharedEntityMap:   entityMap);
            _kernel.RegisterModule(cycloneModule);

            // ── 11. Kernel init ───────────────────────────────────────────────
            _kernel.Initialize();
            Logger.Info("[SimHost] Kernel initialized.");

            // ── 12. Visualization ─────────────────────────────────────────────
            if (!_headless)
            {
                _vis = new SimHostVisualization();
                _vis.Initialize(
                    _world,
                    _kernel,
                    _simLogicModule.RoadNetwork,
                    _simLogicModule.TrajectoryPool,
                    _simLogicModule.FormationTemplates);

                Logger.Info("[SimHost] Visualization ready. Window open.");
            }
        }

        protected override void OnUpdate(float dt)
        {
            _vis?.Update(dt);
            _kernel?.Update();
            _kernelGroup?.Run();
        }

        protected override void OnDrawWorld()
        {
            _vis?.DrawWorld();
        }

        protected override void OnDrawUI()
        {
            _vis?.DrawUI();
        }

        protected override void OnUnload()
        {
            _vis?.Dispose();
            _vis = null;
            _idAllocator?.Dispose();
            _kernelGroup?.Dispose();
            Logger.Info("[SimHost] Shutdown complete.");
            base.OnUnload();
        }

        /// <summary>
        /// Initializes the SimHost application without creating a Raylib window.
        /// Intended for integration tests and headless runners.
        /// </summary>
        public void InitializeHeadless(int? domainIdOverride = null)
        {
            _headless = true;
            _domainOverride = domainIdOverride;
            OnLoad();
        }

        /// <summary>
        /// Advances the SimHost kernel by one tick.
        /// </summary>
        public void Tick(float dt) => OnUpdate(dt);

        // ── Component registration ────────────────────────────────────────────

        /// <summary>
        /// Pre-registers all ECS component types and events required by the SimHost
        /// simulation (CarKinem physics, formations, networking, lifecycle).
        /// Must be called immediately after <see cref="EntityRepository"/> construction.
        /// </summary>
        private static void RegisterSimComponents(EntityRepository world)
        {
            // Network replication
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<NetworkOwnership>();
            world.RegisterComponent<NetworkAuthority>();
            world.RegisterComponent<NetworkSpawnRequest>();
            world.RegisterComponent<PendingNetworkAck>();

            // DDS descriptor
            world.RegisterComponent<EntityMaster>();

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

            // CarKinem / navigation
            world.RegisterComponent<CarKinem.Core.VehicleState>();
            world.RegisterComponent<CarKinem.Core.VehicleParams>();
            world.RegisterComponent<CarKinem.Core.NavState>();
            world.RegisterComponent<CarKinem.Formation.FormationMember>();
            world.RegisterComponent<CarKinem.Formation.FormationRoster>();
            world.RegisterComponent<CarKinem.Formation.FormationTarget>();

            // Managed
            world.RegisterManagedComponent<EntityMissionHolder>();
            world.RegisterManagedComponent<IgVisualDef>();
            world.RegisterManagedComponent<SimVehicleDef>();
            world.RegisterManagedComponent<SimCombatDef>();
            world.RegisterManagedComponent<TkbCompositionDef>();

            // Lifecycle events
            world.RegisterEvent<ConstructionOrder>();
            world.RegisterEvent<ConstructionAck>();
            world.RegisterEvent<DestructionOrder>();
            world.RegisterEvent<DestructionAck>();

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
    }
}
