using Bagira.BDC.SSTD;
using Bagira.IG.Components;
using Bagira.Map.Common;
using Bagira.Map.Common.Events;
using Bagira.Map.Common.Replication;
using Bagira.Map.Common.Replication.Egress;
using Bagira.Map.Common.Replication.Ingress;
using Bagira.Map.Common.Systems;
using Bagira.Map.Definitions.Tkb;
using Bagira.SimHost.Brains;
using Bagira.SimHost.Components;
using Bagira.SimHost.Configuration;
using Bagira.SimHost.Modules;
using Bagira.SimHost.Systems;
using Bagira.SimHost.Utilities;
using CarKinem.Commands;
using CarKinem.Road;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Tkb;
using FDP.Framework.Raylib;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Time.Controllers;
using ModuleHost.Core;
using ModuleHost.Core.Network;
using ModuleHost.Core.Time;
using ModuleHost.Network.Cyclone.Modules;
using ModuleHost.Network.Cyclone.Services;
using ModuleHost.Network.Cyclone.Systems;
using ModuleHost.Network.Cyclone.Translators;
using Raylib_cs;
using System;
using System.Collections.Generic;
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;
using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

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
        private FdpEventBus?         _eventBus;

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

        /// <summary>
        /// Creates a SimHostApp with an optional DDS domain ID override.
        /// </summary>
        /// <param name="domainOverride">
        /// When non-null, takes highest priority over the <c>DomainId</c> value
        /// in <c>config.json</c>.  Pass the value parsed from the <c>--domain</c>
        /// CLI argument; leave <see langword="null"/> to fall back to the JSON config.
        /// </param>
        public SimHostApp(int? domainOverride = null) : base(new ApplicationConfig
        {
            Width       = 1280,
            Height      = 720,
            WindowTitle = "Bagira SimHost",
            TargetFPS   = 60,
            Flags       = ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint
        })
        {
            _domainOverride = domainOverride;
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
            _eventBus   = new FdpEventBus();
            var timeConfig = new TimeControllerConfig { Mode = TimeMode.Continuous, Role = TimeRole.Master };
            var timeCtrl   = TimeControllerFactory.Create(_eventBus, timeConfig);
            timeCtrl.SetTimeScale(1.0f);
            _kernel.SetTimeController(timeCtrl);
            _eventBus.SwapBuffers();

            // ── 4. Data services ──────────────────────────────────────────────
            var ddsParticipant = BagiraEnvironment.CreateParticipant(domainId);
            var tkbDb          = BagiraEnvironment.CreateTkb();
            var entityMap      = new NetworkEntityMap();
            _idAllocator       = new DdsIdAllocator(ddsParticipant, "SimHostAllocator");

            // ── 5. Geodetic configuration ─────────────────────────────────────
            var wgs84 = BagiraEnvironment.CreateGeoTransform();

            // ── 5a. JSON Attribute Compiler (ATTR-S5T1 / ATTR-S5T4) ───────────
            // Builds the zero-allocation JSON attribute compiler with routing delegates
            // for Name, Affiliation, and GeoPosition registered at startup.  The same
            // instance is shared by CreateEntityRequestSystem and UpdateEntityAttributeRequestSystem.
            var jsonAttributeCompiler = AttributeCompilerFactory.Build(wgs84);

            // ── 6. Doctrine registry ──────────────────────────────────────────
            var doctrineRegistry = new DoctrineRegistry();
            unsafe
            {
                doctrineRegistry.Register(SimHostDoctrineIds.MoveTo_BT, "MoveToLocation",
                    new DoctrineDefinition {
                        Name = "MoveToLocation",
                        BrainTier = BehaviorConstants.BrainTierBTree,
                        ParseParams = (json, ptr) => SimHostNodes.ParseMoveToParams(json, ptr, wgs84),
                        BTreeInterpreter = SimHostNodes.BuildMoveToLocationInterpreter()
                    });
                
                doctrineRegistry.Register(SimHostDoctrineIds.FollowRoute_BT, "FollowRoute",
                    new DoctrineDefinition { 
                        Name = "FollowRoute",   
                        BrainTier = BehaviorConstants.BrainTierBTree,
                        ParseParams = (json, ptr) => SimHostNodes.ParseFollowRouteParams(json, ptr),
                        BTreeInterpreter = SimHostNodes.BuildFollowRouteInterpreter()
                    });
            }
            doctrineRegistry.Register(SimHostDoctrineIds.JoinFormation_BT, "JoinFormation",
                new DoctrineDefinition { 
                    Name = "JoinFormation", 
                    BrainTier = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = SimHostNodes.BuildJoinFormationInterpreter() 
                });
            doctrineRegistry.Register(SimHostDoctrineIds.Idle_HSM, "Idle",
                new DoctrineDefinition { Name = "Idle",          BrainTier = BehaviorConstants.BrainTierHsm });
            doctrineRegistry.Register(SimHostDoctrineIds.WanderMilitary_BT, "WanderMilitary",
                new DoctrineDefinition
                {
                    Name             = "WanderMilitary",
                    BrainTier        = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = SimHostNodes.BuildWanderMilitaryInterpreter(),
                });

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
            _kernelGroup.AddSystem(new MissionControlRequestSystem(ddsParticipant, entityMap, doctrineRegistry));
            _kernelGroup.AddSystem(new MissionAdapterSystem(doctrineRegistry, entityMap));
            _kernelGroup.AddSystem(new UpdateEntityDescriptorRequestSystem(ddsParticipant, entityMap, wgs84));
            _kernelGroup.AddSystem(new UpdateEntityAttributeRequestSystem(ddsParticipant, entityMap, wgs84, jsonAttributeCompiler));
            _simLogicModule.RegisterSystems(_kernelGroup, _kernelGroup, _kernelGroup);

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
                SimHostNetworkConstants.LocalNodeId);

            var simHostMod = new SimHostModule(
                ddsParticipant, tkbDb, _idAllocator, SimHostNetworkConstants.LocalNodeId,
                spawningSystem, entityMap, doctrineRegistry,
                new GhostCreationSystem(entityMap), wgs84, jsonAttributeCompiler);
            _kernel.RegisterModule(simHostMod);

            // ── 10. Network module ──────────────────────────────────────────
            var translators = new List<IDescriptorTranslator>();
            if (simHostMod.GeoEgressTranslator != null)
                translators.Add(simHostMod.GeoEgressTranslator);
            if (simHostMod.MapOverlayEgressTranslator != null)
                translators.Add(simHostMod.MapOverlayEgressTranslator);
            translators.Add(simHostMod.MissionIngressTranslator);
            translators.Add(simHostMod.MissionEgressTranslator);
            var entityMasterEgressTranslator = new EntityMasterEgressTranslator(
                ddsParticipant, entityMap, SimHostNetworkConstants.LocalNodeId);
            translators.Add(entityMasterEgressTranslator);
            translators.Add(new FireInteractionEventTranslator(ddsParticipant, entityMap));
            translators.Add(new TimePulseEgressTranslator(ddsParticipant, _eventBus));

            _kernel.RegisterGlobalSystem(
                new CycloneNetworkCleanupSystem(entityMasterEgressTranslator));
            _kernel.RegisterGlobalSystem(
                new DisposalMonitoringSystem(entityMap));

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
            _kernelGroup?.Run();   // process incoming requests first (sets dirty flags)
            _kernel?.Update();     // then run egress scan (picks up dirty → publishes immediately)
            _eventBus?.SwapBuffers();
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
        /// simulation.  Delegates to <see cref="SimHostComponentRegistry.RegisterAll"/>.
        /// </summary>
        private static void RegisterSimComponents(EntityRepository world)
            => SimHostComponentRegistry.RegisterAll(world);
    }
}
