using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Events;
using Hrot.Map.Common.Replication;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.Map.Common.Systems;
using Hrot.Map.Definitions.Tkb;
using Hrot.SimHost.Brains;
using Hrot.SimHost.Components;
using Hrot.SimHost.Configuration;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Network;
using Hrot.SimHost.Network.Egress;
using Hrot.SimHost.Network.Ingress;
using Hrot.SimHost.Systems;
using Hrot.SimHost.Utilities;
using CarKinem.Commands;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
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
using FDP.Kernel.Logging;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Components;
using FDP.Toolkit.Vis2D.Defaults;
using FDP.Toolkit.Scenario;
using ModuleHost.Core;
using ModuleHost.Core.Network;
using ModuleHost.Core.Scheduling;
using Fdp.Kernel.Orchestration;
using ModuleHost.Core.Network.Interfaces;
using ModuleHost.Core.Time;
using ModuleHost.Network.Cyclone.Modules;
using ModuleHost.Network.Cyclone.Services;
using ModuleHost.Network.Cyclone.Systems;
using ModuleHost.Network.Cyclone.Translators;
using Raylib_cs;
using rlImGui_cs;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;
using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

namespace Hrot.SimHost
{
    /// <summary>
    /// Graphical entry-point for the standalone <c>Hrot.SimHost</c> executable.
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
        private FdpEventBus?         _eventBus;        // Swaps kernel to SlaveSyncController when a SwitchTimeModeEvent(Deterministic) arrives.
        // (SlaveTimeModeListener has been removed; SlaveSyncController handles mode transitions internally.)
        // ── Data services ─────────────────────────────────────────────────────
        private NetworkEntityMap?       _entityMap;
        private IGeographicTransform?   _geoTransform;

        // ── Visualization ─────────────────────────────────────────────────────
        private SimHostVisualization? _vis;
        private IgPresentationModule? _igPresentationModule;

        /// <summary>
        /// The visualization layer. Valid after <see cref="InitializeEmbedded"/> in non-headless mode.
        /// Exposed for window-manager panel registration in <c>SimHostSubsystem.RegisterWindows</c>.
        /// </summary>
        public SimHostVisualization? Visualization => _vis;

        // ── SimLogic ─────────────────────────────────────────────────────────
        private SimulationLogicModule? _simLogicModule;

        // ── Orchestration (CGF1-S0104 / CMC-S016) ────────────────────────────
        private FDP.Toolkit.Orchestration.ClusterSlave? _clusterSlave;
        private Hrot.Common.Orchestration.NodeOpSlaveTranslator? _slaveTranslator;
        // CheckpointIOWorker owns the background I/O thread; created in OnLoad,
        // passed to BuildOrchestration, and disposed in Shutdown (CGF1-S0303 A.1).
        private CheckpointIOWorker? _checkpointWorker;

        // ── Headless/test support ────────────────────────────────────────────
        private bool _headless;
        private int? _domainOverride;
        private int  _nodeIdOverride;
        private bool _initialized;
        // ── Role-based bootstrap ─────────────────────────────────────────────
        private NodeRole          _role       = NodeRole.AllInOne;
        private NodeConfiguration? _nodeConfig;
        public new EntityRepository World => base.World
            ?? throw new InvalidOperationException("SimHostApp is not initialized.");

        public new ModuleHostKernel Kernel => base.Kernel
            ?? throw new InvalidOperationException("SimHostApp is not initialized.");

        /// <summary>Returns the ECS world, or <c>null</c> before <see cref="InitializeEmbedded"/> / <see cref="OnLoad"/> completes.</summary>
        public EntityRepository? WorldOrNull => _initialized ? _world : null;

        /// <summary>Returns the network entity map after initialization.</summary>
        public NetworkEntityMap EntityMap => _entityMap
            ?? throw new InvalidOperationException("SimHostApp is not initialized.");

        // ── Constructor ───────────────────────────────────────────────────────

        // ── Static CLI helpers ────────────────────────────────────────────

        /// <summary>
        /// Parses a <see cref="NodeRole"/> from a <c>--role &lt;value&gt;</c> argument pair.
        /// Returns <see cref="NodeRole.AllInOne"/> when the flag is absent or unrecognised.
        /// </summary>
        public static NodeRole ParseRole(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--role", StringComparison.OrdinalIgnoreCase))
                {
                    if (Enum.TryParse<NodeRole>(args[i + 1], ignoreCase: true, out var role))
                        return role;
                }
            }
            return NodeRole.AllInOne;
        }

        /// <summary>
        /// Resolves the <see cref="NodeConfiguration"/> from a <c>--config &lt;path&gt;</c>
        /// argument pair, or returns defaults when the flag is absent.
        /// </summary>
        public static NodeConfiguration ParseNodeConfig(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--config", StringComparison.OrdinalIgnoreCase))
                    return NodeConfiguration.LoadFrom(args[i + 1]);
            }
            return new NodeConfiguration();
        }

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates a SimHostApp with an optional DDS domain ID override.
        /// </summary>
        /// <param name="domainOverride">
        /// When non-null, takes highest priority over the <c>DomainId</c> value
        /// in <c>config.json</c>.  Pass the value parsed from the <c>--domain</c>
        /// CLI argument; leave <see langword="null"/> to fall back to the JSON config.
        /// </param>
        /// <param name="role">
        /// Node role controlling which simulation modules are activated.
        /// Defaults to <see cref="NodeRole.AllInOne"/> for backward compatibility.
        /// </param>
        /// <param name="nodeConfig">
        /// Optional <see cref="NodeConfiguration"/>; defaults are used when <c>null</c>.
        /// </param>
        public SimHostApp(
            int?              domainOverride = null,
            NodeRole          role           = NodeRole.AllInOne,
            NodeConfiguration? nodeConfig    = null) : base(new ApplicationConfig
        {
            Width       = 1280,
            Height      = 720,
            WindowTitle = "Hrot SimHost",
            TargetFPS   = 60,
            Flags       = ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint
        })
        {
            _domainOverride = domainOverride;
            _role           = role;
            _nodeConfig     = nodeConfig;
        }

        // ── FdpApplication lifecycle ──────────────────────────────────────────

        protected override void OnLoad()
        {
            Console.Title = "Hrot.SimHost";
            Logger.Info("[SimHost] Starting graphical application...");

            // ── 0. Apply node configuration (sets CYCLONEDDS_URI if needed) ───
            _nodeConfig?.ApplyEnvironment();
            Logger.Info($"[SimHost] Node role: {_role}");

            // ── 1. Load configuration ─────────────────────────────────────────
            // NodeConfiguration is the unified config type (DB-MOD1-09); SimHostConfig was absorbed.
            // When no explicit config is injected (e.g. Runner path), load from config.json on disk —
            // mirroring the old SimHostConfig.Load("config.json") behaviour.  LoadFrom returns
            // defaults if the file is absent, so this is safe in all environments.
            var nodeConfig = _nodeConfig ?? NodeConfiguration.LoadFrom("config.json");
            // Apply environment side-effects (e.g. CYCLONEDDS_URI) using the resolved config.
            // Safe to call even when _nodeConfig?.ApplyEnvironment() already ran above — idempotent.
            if (_nodeConfig == null) nodeConfig.ApplyEnvironment();
            var domainId   = _domainOverride ?? (int)nodeConfig.DdsDomainId;
            var localNodeId = _nodeIdOverride != 0 ? _nodeIdOverride : SimHostNetworkConstants.LocalNodeId;
            Logger.Info($"[SimHost] Domain ID:       {domainId}");
            Logger.Info($"[SimHost] Node ID:         {localNodeId}");
            Logger.Info($"[SimHost] Simulation Rate: {nodeConfig.SimulationRateHz} Hz");

            // ── 2. ECS world ──────────────────────────────────────────────────
            _world = new EntityRepository();
            RegisterSimComponents(_world);

            var eventAccumulator = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, eventAccumulator);
            base.World = _world;
            base.Kernel = _kernel;

            // ── 3. Time controller ────────────────────────────────────────────
            _eventBus   = new FdpEventBus();
            var timeConfig = new TimeControllerConfig
            {
                Mode        = TimeMode.Continuous,
                Role        = TimeRole.Slave,
                LocalNodeId = localNodeId,
                SyncConfig  = FDP.Toolkit.Time.Controllers.TimeConfig.Default,
            };
            var timeCtrl   = TimeControllerFactory.Create(_eventBus, timeConfig);
            timeCtrl.SetTimeScale(1.0f);
            _kernel.SetTimeController(timeCtrl);
            _eventBus.SwapBuffers();
            // SlaveSyncController (returned by TimeControllerFactory for Slave role) handles all
            // mode transitions internally — no SlaveTimeModeListener needed.
            // ── 4. Data services ──────────────────────────────────────────────
            var ddsParticipant = HrotEnvironment.CreateParticipant(domainId);
            ddsParticipant.EnableSenderTracking(new SenderIdentityConfig
            {
                AppDomainId   = domainId,
                AppInstanceId = localNodeId
            });
            var tkbDb          = HrotEnvironment.CreateTkb();
            var entityMap      = new NetworkEntityMap();
            _entityMap         = entityMap;

            // ── ID allocator client (server lives on Hrot.Orchestrator; throws if unavailable) ──
            _idAllocator = new DdsIdAllocator(ddsParticipant, "SimHostAllocator");
            EnsureIdAllocatorRouting(ddsParticipant);

            // ── 5. Geodetic configuration ─────────────────────────────────────
            var wgs84     = HrotEnvironment.CreateGeoTransform();
            _geoTransform = wgs84;

            // ── 5a. JSON Attribute Compiler (ATTR-S5T1 / ATTR-S5T4) ───────────
            // Builds the zero-allocation JSON attribute compiler with routing delegates
            // for Name, Affiliation, and GeoPosition registered at startup.  The same
            // instance is shared by CreateEntityRequestSystem and UpdateEntityAttributeRequestSystem.
            var jsonAttributeCompiler = AttributeCompilerFactory.Build(wgs84);

            // ── 5b. Binary Attribute Interpreter (ATTR2-P5T1) ─────────────────
            // Builds the binary interpreter for decoding InitialAttributeRecords sent
            // by the IG when it compiles JSON properties into binary wire format.
            // Without this, binary attributes (e.g. Affiliation / Hostile) are silently dropped.
            var binaryInterpreter = AttributeCompilerFactory.BuildBinaryInterpreter(wgs84);

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
            var roadNetwork = LoadRoadNetwork(nodeConfig.RoadNetworkBlobPath);

            // ── 8. SimulationLogicModule (role-based via NodeBootstrapper) ─────
            var bootstrapper = new NodeBootstrapper();
            _simLogicModule = bootstrapper.BuildSimulationLogic(
                _role,
                doctrineRegistry,
                entityMap,
                vehicleApi:  null,
                roadNetwork: roadNetwork);

            // ── 8a. ClusterSlave (CGF1-S0104) ───────────────────────────────────
            // Build ScenarioSerializer for production scenario load/save (CGF1-S0307 / CGF1-S0302).
            // Must be built after RegisterSimComponents so the auto-serializer compiles
            // delegates for all registered component types.
            var scenarioSerializer = new ScenarioSerializerBuilder("Hrot.SimHost").Build();

            // CheckpointIOWorker: starts its background I/O thread here; owned by SimHostApp
            // and disposed in Shutdown().
            // Storage directory: derived from NodeConfiguration.LocalTempRoot so that checkpoints
            // are co-located with pre-fetched scenario files under the same root volume
            // (CGF1-S0303 / A.3 config alignment).  Default: C:\FDP_Temp\checkpoints.
            // Override LocalTempRoot in config.json for non-default deployments.
            var checkpointStoragePath = System.IO.Path.Combine(nodeConfig.LocalTempRoot, "checkpoints");
            _checkpointWorker = new CheckpointIOWorker(checkpointStoragePath, localNodeId);

            // ── Two-phase bootstrap: construct replay-control objects before BuildOrchestration ──
            // GhostCreationSystem, SimulationSystemGroup, and NetworkLifecycleSystemGroup must
            // be constructed here — before BuildOrchestration — so NodeBootstrapper can register
            // ReplayLoadClusterOpHandler (which requires all three).  The same ghostCreationSystem
            // instance is forwarded to SimHostModule below so both paths share one object.
            // (CGF1-S0304 Part A.1 — BATCH-17; previously constructed inline inside SimHostModule
            // after BuildOrchestration, which meant all three were null at registration time.)
            var ghostCreationSystem   = new GhostCreationSystem(entityMap);
            var simulationSystemGroup = new SimulationSystemGroup();
            var networkLifecycleGroup = new NetworkLifecycleSystemGroup(ghostCreationSystem);

            // Built here so SimHostApp owns lifetime; Tick() called in OnUpdate.
            _clusterSlave = bootstrapper.BuildOrchestration(
                _role, _kernel, _world, localNodeId,
                participant: ddsParticipant,
                subsystemName: "SimHost",
                eventBus: _eventBus,
                scenarioSerializer: scenarioSerializer,
                localTempRoot: nodeConfig.LocalTempRoot,
                checkpointWorker: _checkpointWorker,
                simGroup: simulationSystemGroup,
                lifecycleGroup: networkLifecycleGroup,
                ghostCreationSystem: ghostCreationSystem);
            // CMC-S016: wire NodeOpSlaveTranslator created by bootstrapper (DDS ↔ bus bridge).
            _slaveTranslator = bootstrapper.SlaveTranslator;

            _kernelGroup = new SystemGroup();
            _kernelGroup.Create(_world);
            _kernelGroup.AddSystem(new MissionControlRequestSystem(ddsParticipant, entityMap, doctrineRegistry));
            _kernelGroup.AddSystem(new MissionAdapterSystem(doctrineRegistry, entityMap));
            _kernelGroup.AddSystem(new UpdateEntityAttributeRequestSystem(ddsParticipant, entityMap, wgs84, jsonAttributeCompiler));
            _simLogicModule.RegisterSystems(_kernelGroup, _kernelGroup, _kernelGroup);

            // Seed GlobalTime singleton.
            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 1.0f / nodeConfig.SimulationRateHz,
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
                localNodeId,
                onEntitySpawned: (world, entity, isLocalAuthority) =>
                {
                    // Mark locally-owned physics components as authoritative so
                    // CarKinematicsSystem (.WithOwned<SimTransform>()) processes this entity.
                    // This mirrors the same callback in SimHostInstance (integration tests).
                    // Without this, vehicles spawned in SimHostApp never move because
                    // CarKinematicsSystem (post MOD1 refactoring) skips entities whose
                    // SimTransform authority flag is not set.
                    if (isLocalAuthority && world.HasComponent<SimTransform>(entity))
                        world.SetAuthority<SimTransform>(entity, true);
                });

            // ── DDS adapters and request-handling systems ────────────────────────
            var requestSource      = new DdsCreateEntityRequestSource(ddsParticipant);
            var ackSink            = new DdsCreateUpdateDeleteEntityAckSink(ddsParticipant);
            var deleteSource       = new DdsDeleteEntityRequestSource(ddsParticipant);
            var finalizationSystem = new NedRequestFinalizationSystem(ackSink, entityMap);
            var requestSystem      = new CreateEntityRequestSystem(
                requestSource, ackSink, tkbDb, _idAllocator, localNodeId,
                wgs84, jsonAttributeCompiler, binaryInterpreter, finalizationSystem);
            var deleteSystem       = new DeleteEntityRequestSystem(
                deleteSource, ackSink, entityMap, finalizationSystem);

            // ── Create translators ────────────────────────────────────────────────
            GeoSpatialEgressTranslator?       geoEgress   = wgs84 != null ? new GeoSpatialEgressTranslator(ddsParticipant, entityMap, wgs84) : null;
            MapVisualOverlayEgressTranslator? mapOverlay  = wgs84 != null ? new MapVisualOverlayEgressTranslator(ddsParticipant, entityMap, wgs84) : null;
            MapRouteEgressTranslator?         mapRoute    = wgs84 != null ? new MapRouteEgressTranslator(ddsParticipant, entityMap, wgs84) : null;
            var missionIngress = new EntityMissionIngressTranslator(ddsParticipant, entityMap, doctrineRegistry, ghostCreationSystem);
            var missionEgress  = new EntityMissionEgressTranslator(ddsParticipant, entityMap);

            var simHostMod = new SimHostModule(
                spawnSystem:        spawningSystem,
                requestSystem:      requestSystem,
                deleteSystem:       deleteSystem,
                finalizationSystem: finalizationSystem,
                geoEgressTranslator:        geoEgress,
                mapOverlayEgressTranslator: mapOverlay,
                mapRouteEgressTranslator:   mapRoute,
                missionIngressTranslator:   missionIngress,
                missionEgressTranslator:    missionEgress);
            _kernel.RegisterModule(simHostMod);

            // Register UpdateEntityDescriptorRequestSystem in the network-boundary section
            // (conditionally alongside the other DDS-coupled spawning systems).
            _kernelGroup.AddSystem(new UpdateEntityDescriptorRequestSystem(ddsParticipant, entityMap, wgs84));

            // ── 10. Network module ──────────────────────────────────────────
            // Egress translators: fan-out disposal on entity death + CycloneNetworkModule publication.
            // Ingress translators: CycloneNetworkModule ingestion only (not disposed on entity death).
            var egressTranslators = new List<IDescriptorTranslator>();
            // EntityMaster must be published before GeoSpatial so receivers can
            // register the entity identity before its first position update.
            var entityMasterEgressTranslator = new EntityMasterEgressTranslator(
                ddsParticipant, entityMap, localNodeId);
            egressTranslators.Add(entityMasterEgressTranslator);
            egressTranslators.Add(new EntityInfoEgressTranslator(ddsParticipant, entityMap));
            if (geoEgress != null)
                egressTranslators.Add(geoEgress);
            if (mapOverlay != null)
                egressTranslators.Add(mapOverlay);
            if (mapRoute != null)
                egressTranslators.Add(mapRoute);
            egressTranslators.Add(missionEgress);
            egressTranslators.Add(new FireInteractionEventTranslator(ddsParticipant, entityMap));
            // CGF1-A.1: Bridge SwitchTimeModeEvent between FdpEventBus and DDS for distributed
            // time-mode switching (SlaveSyncController ingress).
            egressTranslators.Add(FDP.Toolkit.Time.TimeNetworkModule.CreateDescriptorTranslator(ddsParticipant, _eventBus));
            // Bridge FrameOrder/FrameAck for distributed lockstep stepping (slave side).
            egressTranslators.Add(FDP.Toolkit.Time.TimeNetworkModule.CreateSlaveLockstepTranslator(ddsParticipant, _eventBus, localNodeId));
            // BS1-T015: Publish Health changes to the IG/ExCon so health bars update.
            egressTranslators.Add(new EntityDamageEgressTranslator(ddsParticipant, entityMap));

            // BS1-T017: combat CQRS pipeline translators, registered by node role.
            // Brain: emits WeaponFireIntent → DDS WeaponFireRequest.
            if (_role == NodeRole.Brain || _role == NodeRole.AllInOne)
            {
                egressTranslators.Add(new WeaponFireIntentEgressTranslator(ddsParticipant, entityMap));
            }

            // Muscle: emits Notification + Detonation → DDS; Brain-side emits DamageAssessed → DDS.
            if (_role == NodeRole.MuscleGround || _role == NodeRole.AllInOne)
            {
                egressTranslators.Add(new WeaponFireNotificationEgressTranslator(ddsParticipant));
                egressTranslators.Add(new MunitionDetonationEgressTranslator(ddsParticipant));
                egressTranslators.Add(new DamageAssessedEgressTranslator(ddsParticipant));
            }

            // All translators (egress + ingress) passed to CycloneNetworkModule.
            var translators = new List<IDescriptorTranslator>(egressTranslators);
            translators.Add(missionIngress);

            // BS1-T017 (continued): ingress translators added after translators list is created.
            // Muscle: receives WeaponFireRequest and MunitionDetonation from DDS → local events.
            if (_role == NodeRole.MuscleGround || _role == NodeRole.AllInOne)
            {
                translators.Add(new WeaponFireRequestIngressTranslator(ddsParticipant, entityMap));
                translators.Add(new MunitionDetonationIngressTranslator(ddsParticipant, entityMap));
            }

            // Brain (authority node): receives EntityHitDamage → applies health changes.
            if (_role == NodeRole.Brain || _role == NodeRole.AllInOne)
            {
                translators.Add(new EntityHitDamageIngressTranslator(ddsParticipant, entityMap));
            }

            _kernel.RegisterGlobalSystem(
                new CycloneNetworkCleanupSystem(egressTranslators));
            _kernel.RegisterGlobalSystem(
                new DisposalMonitoringSystem(entityMap));

            var localNodeIdForMapper = localNodeId;
            var nodeMapper  = new NodeIdMapper(domainId, localNodeIdForMapper);
            var topology    = new StaticNetworkTopology(localNodeIdForMapper, new[] { localNodeIdForMapper });

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
                    _simLogicModule.TrajectoryPool ?? new TrajectoryPoolManager(),
                    _simLogicModule.FormationTemplates ?? new FormationTemplateManager(),
                    new DdsWriter<MissionControlRequest>(ddsParticipant),
                    idAllocator: _idAllocator);

                // Wire IG presentation module with a real MapCanvas + NedVisualizerAdapter
                // for production rendering (DB-MOD1-12).
                var igCanvas = new MapCanvas(new RaylibInputProvider());
                _igPresentationModule = new IgPresentationModule(canvas: igCanvas);
                Logger.Info("[SimHost] Visualization ready. Window open.");
            }
            else
            {
                // Headless / integration-test path: headless canvas (no Raylib calls).
                _igPresentationModule = new IgPresentationModule(canvas: null);
            }

            _initialized = true;
            Logger.Info("[SimHost] Initialized.");
        }

        protected override void OnUpdate(float dt)
        {
            // CMC-S016: translator tick BEFORE clusterSlave so DDS→bus ingress is processed first.
            _slaveTranslator?.Tick();
            _clusterSlave?.Tick();
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
            Shutdown();
            // base.OnUnload() would call Kernel?.Dispose() and World?.Dispose() again;
            // Shutdown() already handles that so we do not call base here to avoid double-dispose.
        }

        // ── Embedded lifecycle (IgApplication pattern) ────────────────────────

        /// <summary>
        /// Initializes SimHost for use inside an orchestrator or integration test,
        /// without creating a Raylib window.  The caller owns the window lifecycle
        /// (or passes <paramref name="headless"/> = <c>true</c> for windowless use).
        /// </summary>
        public void InitializeEmbedded(bool headless = false, int? domainIdOverride = null, int nodeIdOverride = 0)
        {
            _headless       = headless;
            _domainOverride = domainIdOverride;
            _nodeIdOverride = nodeIdOverride;
            OnLoad();
        }

        /// <summary>
        /// Initializes the SimHost application without creating a Raylib window.
        /// Intended for integration tests and headless runners.
        /// </summary>
        public void InitializeHeadless(int? domainIdOverride = null, int nodeIdOverride = 0)
            => InitializeEmbedded(headless: true, domainIdOverride: domainIdOverride, nodeIdOverride: nodeIdOverride);

        /// <summary>
        /// Disposes all SimHost resources.
        /// Pass <paramref name="ownsWindow"/> = <c>false</c> when the orchestrator
        /// owns the Raylib window (i.e. when used via <see cref="InitializeEmbedded"/>).
        /// </summary>
        public void Shutdown(bool ownsWindow = false)
        {
            if (!_initialized) return;
            _initialized = false;

            // ── Stop ClusterSlave (CGF1-S0104) ──────────────────────────────────
            _clusterSlave?.Dispose();
            _clusterSlave = null;

            // ── Dispose CheckpointIOWorker (lets background I/O finish before kernel teardown)
            _checkpointWorker?.Dispose();
            _checkpointWorker = null;

            // ── Dispose simulation resources ──────────────────────────────────
            _vis?.Dispose();
            _vis = null;
            _idAllocator?.Dispose();
            _kernelGroup?.Dispose();
            _kernel?.Dispose();

            Logger.Info("[SimHost] Shutdown complete.");

            if (ownsWindow)
            {
                rlImGui.Shutdown();
                Raylib.CloseWindow();
            }
        }

        /// <summary>Advances the SimHost kernel by one tick.</summary>
        public void Tick(float dt) => OnUpdate(dt);

        /// <summary>
        /// Renders the 2-D map canvas (delegates to visualization; no-op in headless mode).
        /// Call inside <c>Raylib.BeginDrawing()</c>.
        /// </summary>
        public void DrawWorld() => OnDrawWorld();

        /// <summary>
        /// Renders ImGui control panels (delegates to visualization; no-op in headless mode).
        /// Call inside <c>rlImGui.Begin()</c>.
        /// </summary>
        public void DrawUI() => OnDrawUI();

        /// <summary>Returns the current map camera, or <c>null</c> in headless mode.</summary>
        public MapCamera? GetMapCamera() => _vis?.GetMapCamera();

        // ── TestHooks ────────────────────────────────────────────────────────

        /// <summary>TestHook: exposes the <see cref="NetworkEntityMap"/> after initialization.</summary>
        public NetworkEntityMap TestHook_EntityMap => _entityMap
            ?? throw new InvalidOperationException("SimHostApp is not initialized.");

        /// <summary>TestHook: current kernel simulation time in seconds. Updates every frame.</summary>
        public double TestHook_CurrentSimTime => _kernel?.CurrentTime.TotalTime ?? 0.0;
        /// <summary>
        /// Internal test hook: returns the runtime type of the currently active time controller
        /// in the SimHost kernel.  Tests use this to verify that after Pause/Resume the
        /// <see cref="FDP.Toolkit.Time.Controllers.SlaveSyncController"/> transitions correctly.
        /// </summary>
        public Type? TestHook_TimeControllerType => _kernel?.GetTimeController().GetType();

        /// <summary>
        /// TestHook: returns the current <see cref="ModuleHost.Core.Time.TimeMode"/> of
        /// the kernel's time controller.  Used in integration tests to verify mode transitions
        /// of <see cref="FDP.Toolkit.Time.Controllers.SlaveSyncController"/>.
        /// </summary>
        public ModuleHost.Core.Time.TimeMode? TestHook_TimeControllerMode
            => _kernel?.GetTimeController().GetMode();

        /// <summary>
        /// TestHook: spawns an entity via the network spawning pipeline and returns its network ID.
        /// </summary>
        public long TestHook_SpawnEntity(long tkbType, GeoPoint position)
        {
            if (_world == null || _idAllocator == null || _entityMap == null)
                throw new InvalidOperationException("SimHostApp is not initialized.");

            long networkId = _idAllocator.AllocateId();

            var initialComponents = new List<object>();
            if (_geoTransform != null)
            {
                var cart    = _geoTransform.ToCartesian(position.Latitude, position.Longitude, position.Altitude);
                var cartPos = new Vector3((float)cart.X, (float)cart.Y, (float)cart.Z);
                initialComponents.Add(new SimTransform
                {
                    Position = cartPos,
                    Rotation = Quaternion.Identity
                });
            }

            _world.Bus.PublishManaged(new SpawnEntityCommand
            {
                NetworkId         = networkId,
                TkbType           = tkbType,
                DisType           = 0,
                OwnerNodeId       = 1,
                InitType          = ReliableInitType.AllPeers,
                InitialComponents = initialComponents,
                RequestId         = Guid.Empty
            });

            return networkId;
        }

        /// <summary>
        /// TestHook: teleports the entity to <paramref name="worldPos"/> (simulates an IG drag).
        /// </summary>
        public void TestHook_SimulateDrag(long networkId, Vector2 worldPos)
        {
            if (_world == null || _entityMap == null)
                throw new InvalidOperationException("SimHostApp is not initialized.");

            if (!_entityMap.TryGetEntity(networkId, out var entity))
                throw new InvalidOperationException($"Entity with networkId={networkId} not found in entity map.");

            if (!_world.IsAlive(entity) || !_world.HasComponent<SimTransform>(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive or has no SimTransform.");

            ref var tf = ref _world.GetComponentRW<SimTransform>(entity);
            tf.Position = new Vector3(worldPos.X, worldPos.Y, 0f);
        }

        /// <summary>
        /// TestHook: directly assigns the WanderMilitary BTree doctrine to an entity,
        /// bypassing the DDS <c>MissionControlRequest</c> round-trip.
        /// </summary>
        public void TestHook_AssignWanderMission(long networkId)
        {
            if (_world == null || _entityMap == null)
                throw new InvalidOperationException("SimHostApp is not initialized.");

            if (!_entityMap.TryGetEntity(networkId, out var entity))
                throw new InvalidOperationException($"Entity with networkId={networkId} not found in entity map.");

            if (!_world.IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive.");

            var newPhase = new MissionPhase
            {
                DoctrineId   = SimHostDoctrineIds.WanderMilitary_BT,
                Trigger      = FDP.Toolkit.Behavior.Components.MissionTrigger.TimerElapsed,
                TriggerParam = float.MaxValue,
            };

            if (_world.HasComponent<MissionPlanQueue>(entity))
            {
                var queue = _world.GetComponent<MissionPlanQueue>(entity);
                queue.CurrentPhase        = 0;
                queue.PhaseElapsedSeconds = 0f;
                queue.PhaseCount          = 1;
                Span<MissionPhase> phases = queue.Phases;
                phases[0] = newPhase;
                _world.SetComponent(entity, queue);
            }
            else
            {
                var queue = new MissionPlanQueue
                {
                    CurrentPhase        = 0,
                    PhaseElapsedSeconds = 0f,
                    PhaseCount          = 1,
                };
                Span<MissionPhase> phases = queue.Phases;
                phases[0] = newPhase;
                _world.AddComponent(entity, queue);
            }

            if (_world.HasComponent<DoctrineState>(entity))
            {
                ref var doctrine = ref _world.GetComponentRW<DoctrineState>(entity);
                unchecked { doctrine.InstanceId++; }
                doctrine.ActiveDoctrineHash = SimHostDoctrineIds.WanderMilitary_BT;
            }
        }

        /// <summary>TestHook: returns the current <see cref="SimTransform"/> of the entity, or default.</summary>
        public SimTransform TestHook_GetSimTransform(long networkId)
        {
            if (_world == null || _entityMap == null) return default;
            if (!_entityMap.TryGetEntity(networkId, out var entity)) return default;
            if (!_world.IsAlive(entity) || !_world.HasComponent<SimTransform>(entity)) return default;
            return _world.GetComponent<SimTransform>(entity);
        }

        /// <summary>TestHook: returns the current <see cref="DoctrineState"/> of the entity, or default.</summary>
        public DoctrineState TestHook_GetDoctrineState(long networkId)
        {
            if (_world == null || _entityMap == null) return default;
            if (!_entityMap.TryGetEntity(networkId, out var entity)) return default;
            if (!_world.IsAlive(entity) || !_world.HasComponent<DoctrineState>(entity)) return default;
            return _world.GetComponent<DoctrineState>(entity);
        }

        /// <summary>TestHook: returns <c>true</c> if the entity has a <see cref="MissionPlanQueue"/> component.</summary>
        public bool TestHook_HasMissionPlanQueue(long networkId)
        {
            if (_world == null || _entityMap == null) return false;
            if (!_entityMap.TryGetEntity(networkId, out var entity)) return false;
            return _world.IsAlive(entity) && _world.HasComponent<MissionPlanQueue>(entity);
        }

        /// <summary>TestHook: returns the current <see cref="MissionPlanQueue"/> of the entity, or default.</summary>
        public MissionPlanQueue TestHook_GetMissionPlanQueue(long networkId)
        {
            if (_world == null || _entityMap == null) return default;
            if (!_entityMap.TryGetEntity(networkId, out var entity)) return default;
            if (!_world.IsAlive(entity) || !_world.HasComponent<MissionPlanQueue>(entity)) return default;
            return _world.GetComponent<MissionPlanQueue>(entity);
        }

        /// <summary>
        /// TestHook: directly activates the WanderMilitary doctrine on the entity's
        /// <see cref="DoctrineState"/>, bypassing <see cref="MissionDirectorSystem"/>.
        /// </summary>
        public void TestHook_ForceDoctrineActive(long networkId)
        {
            if (_world == null || _entityMap == null)
                throw new InvalidOperationException("SimHostApp is not initialized.");

            if (!_entityMap.TryGetEntity(networkId, out var entity))
                throw new InvalidOperationException($"Entity with networkId={networkId} not found.");

            if (!_world.IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive.");

            if (_world.HasComponent<DoctrineState>(entity))
            {
                ref var doctrine = ref _world.GetComponentRW<DoctrineState>(entity);
                unchecked { doctrine.InstanceId++; }
                doctrine.ActiveDoctrineHash = SimHostDoctrineIds.WanderMilitary_BT;
            }
        }

        /// <summary>TestHook: returns child entities that reference the given parent via <see cref="PartMetadata"/>.</summary>
        public List<Entity> TestHook_GetChildEntities(Entity parentEntity)
        {
            if (_world == null)
                throw new InvalidOperationException("SimHostApp is not initialized.");

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

        /// <summary>TestHook: returns the resolved local node ID (override when non-zero, else legacy constant).</summary>
        internal int TestHook_ResolvedLocalNodeId =>
            _nodeIdOverride != 0 ? _nodeIdOverride : SimHostNetworkConstants.LocalNodeId;

        /// <summary>
        /// TestHook: appends an additional ECS system to the kernel system group.
        /// Must be called after <see cref="InitializeEmbedded"/> and before the first
        /// <see cref="Tick"/> so that the system participates from the first frame.
        /// Intended only for in-process integration/E2E tests.
        /// </summary>
        public void TestHook_AddSystem(ComponentSystem system)
        {
            if (_kernelGroup == null)
                throw new InvalidOperationException("SimHostApp is not initialized.");
            _kernelGroup.AddSystem(system);
        }

        // ── Component registration ────────────────────────────────────────────

        /// <summary>
        /// Pre-registers all ECS component types and events required by the SimHost
        /// simulation.  Delegates to <see cref="SimHostComponentRegistry.RegisterAll"/>.
        /// </summary>
        private static void RegisterSimComponents(EntityRepository world)
            => SimHostComponentRegistry.RegisterAll(world);

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Waits up to 30 s for the remote DDS ID allocator server (hosted by <c>Hrot.Orchestrator</c>)
        /// to announce publication. Throws <see cref="InvalidOperationException"/> if the server is not
        /// found within the timeout — SimHost must not start without a working allocator.
        /// </summary>
        private void EnsureIdAllocatorRouting(DdsParticipant participant)
        {
            if (_idAllocator == null) return;
            const int MaxWaitSeconds = 30;
            const int WarnAtSeconds  = 5;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(MaxWaitSeconds);
            var warnAt   = DateTime.UtcNow + TimeSpan.FromSeconds(WarnAtSeconds);
            bool warned  = false;
            while (DateTime.UtcNow < deadline)
            {
                if (_idAllocator.HasPublicationMatch)
                    return;
                if (!warned && DateTime.UtcNow >= warnAt)
                {
                    FdpLog<SimHostApp>.Warn(
                        "[SimHost] IdAllocator: no remote orchestrator server after {0} s — still waiting " +
                        "(up to {1:F0} s total). Verify that Hrot.Orchestrator is running.",
                        WarnAtSeconds, MaxWaitSeconds);
                    warned = true;
                }
                Thread.Sleep(50);
            }

            if (_idAllocator.HasPublicationMatch)
                return;

            throw new InvalidOperationException(
                $"[SimHost] DdsIdAllocator publication match not established within {MaxWaitSeconds} s. " +
                "Hrot.Orchestrator must be running before SimHost starts.");
        }

        /// <summary>
        /// Loads a road-network blob from <paramref name="path"/> using the supplied
        /// <paramref name="loader"/> (default: <see cref="RoadNetworkLoader.LoadFromJson"/>).
        /// Returns a default <see cref="RoadNetworkBlob"/> when the path is empty or the
        /// loader throws.  The <paramref name="loader"/> parameter exists for unit-testing.
        /// </summary>
        internal static RoadNetworkBlob LoadRoadNetwork(
            string?                         path,
            Func<string, RoadNetworkBlob>?  loader = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                return new RoadNetworkBlob();

            try
            {
                return (loader ?? RoadNetworkLoader.LoadFromJson)(path);
            }
            catch (Exception ex)
            {
                FdpLog<SimHostApp>.Warn("[SimHost] Failed to load road network: {0}", ex.Message);
                return new RoadNetworkBlob();
            }
        }
    }
}
