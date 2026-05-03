using Hrot.Core.Mission;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Map.Common.Events;
using Hrot.Map.Definitions.Tkb;
using Hrot.SimHost.Configuration;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Utilities;
using Hrot.Common.Infrastructure;
using Hrot.Common.Scenario;
using CarKinem.Commands;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Tkb;
using Fdp.Presentation.Raylib;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Core.Logging;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Core.Diagnostics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Perception.Modules;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.Toolkit.Scenario;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Diagnostics;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication;
using Fdp.ModuleHost.Scheduling;
using Fdp.Core.Orchestration;
using Fdp.ModuleHost.Time;
using Fdp.Network.Cyclone.Modules;
using Fdp.Network.Cyclone.Services;
using Fdp.Network.Cyclone.Systems;
using Fdp.Network.Cyclone.Translators;
using Raylib_cs;
using rlImGui_cs;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;
using NetworkEntityMap = Fdp.Toolkit.Replication.Services.NetworkEntityMap;
using Fdp.Toolkit.NetworkSpawning;

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
        private TogglableInputGroup?          _toggleInput;
        private TogglableSimulationGroup?     _toggleSim;
        private TogglablePostSimulationGroup? _togglePostSim;
        private INetworkIdAllocator? _idAllocator;
        private FdpEventBus?         _eventBus;        // Swaps kernel to SlaveSyncController when a SwitchTimeModeEvent(Deterministic) arrives.
        // (SlaveTimeModeListener has been removed; SlaveSyncController handles mode transitions internally.)
        // ── HrotNodeBuilder infrastructure context (EAM-M001) ─────────────────
        private HrotNodeContext?     _context;
        // ── Data services ─────────────────────────────────────────────────────
        private NetworkEntityMap?       _entityMap;
        private IGeographicTransform?   _geoTransform;

        // ── Visualization ─────────────────────────────────────────────────────
        private SimHostVisualization? _vis;

        /// <summary>
        /// The visualization layer. Valid after <see cref="InitializeEmbedded"/> in non-headless mode.
        /// Exposed for window-manager panel registration in <c>SimHostSubsystem.RegisterWindows</c>.
        /// </summary>
        public SimHostVisualization? Visualization => _vis;

        // ── Behavior registry ─────────────────────────────────────────────────
        private BehaviorRegistry? _behaviorRegistry;

        // ── SimLogic ─────────────────────────────────────────────────────────
        private SimHostCoreLogicPack? _simCorePack;

        // ── Physics ───────────────────────────────────────────────────────────
        private PhysicsToolkitModule? _physicsModule;

        // ── Orchestration (CGF1-S0104 / CMC-S016) ────────────────────────────
        private Fdp.Toolkit.Orchestration.ClusterSlave? _clusterSlave;
        // HEXAG2-S012: factory-managed slave translator (was NodeOpSlaveTranslator directly).
        private Hrot.Core.Network.ISlaveOrchestrationTranslator? _slaveTranslator;
        // Time-control translators: bridge SwitchTimeModeEvent and FrameOrder/FrameAck so that
        // the SlaveSyncController (installed by HrotNodeBuilder) receives time events from the
        // Orchestrator via DDS (same pattern as CgfApplication).
        private Fdp.Interfaces.IDescriptorTranslator? _timeModeTranslator;
        private Fdp.Interfaces.IDescriptorTranslator? _lockstepTranslator;
        // CheckpointIOWorker owns the background I/O thread; created in OnLoad,
        // passed to BuildOrchestration, and disposed in Shutdown (CGF1-S0303 A.1).
        private CheckpointIOWorker? _checkpointWorker;

        // ── Headless/test support ────────────────────────────────────────────
        private bool _headless;
        private int? _domainOverride;
        private int  _nodeIdOverride;
        private bool _initialized;
        // ── Role-based bootstrap ─────────────────────────────────────────────
        private NodeRole          _role       = NodeRole.MuscleGround | NodeRole.Perception;
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

        /// <summary>Internal test hook: exposes the NedReplicationModule after initialization.</summary>
        internal Hrot.Common.Abstractions.INedReplicationModule? TestHook_NedReplication => _context?.NedReplication;

        // ── Network factory (injected from composition root) ───────────────────
        private INetworkFactory? _networkFactory;
        // ── Perception module (stored to expose ScopedBus to the event browser) ───
        private Fdp.Toolkit.Perception.Modules.AutonomousPerceptionModule? _perceptionMod;
        private readonly DiagnosticEventHistoryService _eventHistoryService = new();

        // ── Constructor ───────────────────────────────────────────────────────

        // ── Static CLI helpers ────────────────────────────────────────────

        /// <summary>
        /// Parses a <see cref="NodeRole"/> from a <c>--role &lt;value&gt;</c> argument pair.
        /// Returns <c>MuscleGround | Perception</c> (standalone default) when the flag
        /// is absent or unrecognised.
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
            return NodeRole.MuscleGround | NodeRole.Perception;
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
        /// Defaults to <c>MuscleGround | Perception</c> (standalone mode).
        /// </param>
        /// <param name="nodeConfig">
        /// Optional <see cref="NodeConfiguration"/>; defaults are used when <c>null</c>.
        /// </param>
        public SimHostApp(
            int?              domainOverride = null,
            NodeRole          role           = NodeRole.MuscleGround | NodeRole.Perception,
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
            var localNodeId = _nodeIdOverride != 0 ? _nodeIdOverride : SimHostNetworkConstants.LocalNodeId;
            Logger.Info($"[Node-{localNodeId}] Starting graphical application...");

            // ── 0. Apply node configuration (sets CYCLONEDDS_URI if needed) ───
            _nodeConfig?.ApplyEnvironment();
            Logger.Info($"[Node-{localNodeId}] Node role: {_role}");

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
            Logger.Info($"[Node-{localNodeId}] Domain ID:       {domainId}");
            Logger.Info($"[Node-{localNodeId}] Node ID:         {localNodeId}");
            Logger.Info($"[Node-{localNodeId}] Simulation Rate: {nodeConfig.SimulationRateHz} Hz");

            // ── 2. Geodetic transform — created before builder so behavior lambdas can close over it ──
            var wgs84     = HrotEnvironment.CreateGeoTransform();
            _geoTransform = wgs84;

            // ── 3. Behavior registry (empty on the Muscle shell; Brain behaviors live in CgfBehaviorSetup) ──
            var behaviorRegistry = new BehaviorRegistry();
            _behaviorRegistry = behaviorRegistry;

            // ── 4. Create DDS participant in the Application Shell (Composition Root) ───
            // Rule: only the outermost executable may instantiate DdsParticipant.
            // HrotNodeBuilder no longer has a fallback — the participant must come from here.
            DdsParticipant? shellParticipant = _networkFactory?.Participant;
            if (shellParticipant == null)
            {
                shellParticipant = HrotEnvironment.CreateParticipant(domainId);
                shellParticipant.EnableSenderTracking(new SenderIdentityConfig
                {
                    AppDomainId   = domainId,
                    AppInstanceId = localNodeId
                });
            }

            // ── 5. Build Hrot node infrastructure — includes entity map,
            //        elm, geoModule (via BaseModules). Replication is created via INetworkFactory.
            var hrotConfig = new HrotNodeConfig
            {
                DomainId            = domainId,
                NodeId              = localNodeId,
                Headless            = false,  // SimHostApp always creates DDS; _headless only controls Raylib window
                ExternalParticipant = shellParticipant,
                LocalTempRoot       = Path.Combine(
                    string.IsNullOrEmpty(nodeConfig.LocalTempRoot)
                        ? Fdp.Toolkit.Orchestration.OrchestrationConstants.DefaultStagingDirectory
                        : nodeConfig.LocalTempRoot,
                    "nodes",
                    $"node-{localNodeId}"),
                LogDirectory        = Path.Combine(System.AppContext.BaseDirectory, "logs"),
                SubsystemName       = "SimHost",
            };
            var baseContext = new HrotNodeBuilder(hrotConfig)
                .WithRole("SimHost", _role)
                .WithNetworkFactory(_networkFactory)
                .Build();

            // Configure the injected factory with this node's participant, entityMap, etc.
            // then create the replication module from the factory.
            // When no factory is injected (unit-test / offline path), fall back to a no-op module.
            INetworkFactory? nodeFactory = _networkFactory?.ConfigureForNode(baseContext, _role, behaviorRegistry);
            Hrot.Common.Abstractions.IReplicationModule replicationModule = nodeFactory?.CreateReplicationModule() ?? new NullReplicationModule();

            _context = baseContext with
            {
                NedReplication      = replicationModule as Hrot.Common.Abstractions.INedReplicationModule,
                GhostCreationSystem = replicationModule.GhostCreationSystem,
            };

            _world       = _context.World;
            _kernel      = _context.Kernel;
            _eventBus    = _context.EventBus;
            _entityMap   = _context.EntityMap;
            _idAllocator = _context.IdAllocator;
            var ddsParticipant = _context.Participant;  // null in headless mode
            var entityMap      = _entityMap;            // alias used by downstream code
            base.World  = _world;
            base.Kernel = _kernel;
            RegisterSimComponents(_world);

            // Distributed time control: bridge SwitchTimeModeEvent and FrameOrder/FrameAck
            // over DDS so the SlaveSyncController (installed by HrotNodeBuilder via
            // TimeControllerFactory) can transition to Stepping mode and advance time on Step.
            if (ddsParticipant != null)
            {
                _timeModeTranslator = TimeNetworkModule.CreateDescriptorTranslator(
                    ddsParticipant, _eventBus!);
                _lockstepTranslator = TimeNetworkModule.CreateSlaveLockstepTranslator(
                    ddsParticipant, _eventBus!, localNodeId);
            }

            // tkbDb and wgs84 come from the built context (same instances as in BaseModules).
            var tkbDb = _context.TkbDb!;

            // ── 6. Road network ───────────────────────────────────────────────
            var roadNetwork = LoadRoadNetwork(nodeConfig.RoadNetworkBlobPath, localNodeId: localNodeId);

            // ── 7. SimHostCoreLogicPack (Muscle-tier simulation modules) ──────
            _simCorePack = new SimHostCoreLogicPack(
                entityMap,
                roadNetwork);

            // Build system lists from the logic pack and wrap in togglable phase groups.
            // Must happen before BuildOrchestration so _toggleSim is non-null for the call.
            var allInputSystems   = new System.Collections.Generic.List<IEcsModuleSystem>();
            var allSimSystems     = new System.Collections.Generic.List<IEcsModuleSystem>();
            var allPostSimSystems = new System.Collections.Generic.List<IEcsModuleSystem>();

            // Add DDS attribute/descriptor update systems from factory (NED-specific, NOP in offline mode).
            if (nodeFactory != null)
            {
                foreach (var sys in nodeFactory.CreateSimHostAttributeUpdateSystems())
                    allInputSystems.Add(sys);
            }
            foreach (var s in _simCorePack.InputSystems)          allInputSystems.Add(s);
            foreach (var s in _simCorePack.SimulationSystems)     allSimSystems.Add(s);
            foreach (var s in _simCorePack.PostSimulationSystems) allPostSimSystems.Add(s);

            _toggleInput   = new TogglableInputGroup("SimHostInput",          allInputSystems);
            _toggleSim     = new TogglableSimulationGroup("SimHostSimulation", allSimSystems);
            _togglePostSim = new TogglablePostSimulationGroup("SimHostPostSim", allPostSimSystems);

            _kernel.RegisterGlobalSystem(_toggleInput);
            _kernel.RegisterModule(new SimHostSimulationModule(_toggleSim));
            _kernel.RegisterGlobalSystem(_togglePostSim);

            // GenesisMaterializationSystem -- Input phase, registered after the togglable groups
            _kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.GenesisMaterializationSystem(entityMap));

            // ── 8. ClusterSlave (CGF1-S0104) ────────────────────────────────────
            // Build ScenarioSerializer for production scenario load/save (CGF1-S0307 / CGF1-S0302).
            // Must be built after RegisterSimComponents so the auto-serializer compiles
            // delegates for all registered component types.
            // These translators replace the auto-serializer stubs for components that contain
            // fixed-size buffers or InlineArrays embedding Entity cross-references.
            // The auto-serialiser produces empty/truncated JSON for those fields, zeroing
            // entity handles on every round-trip.
            var scenarioSerializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(behaviorRegistry);

            // CheckpointIOWorker: starts its background I/O thread here; owned by SimHostApp
            // and disposed in Shutdown().
            // Storage directory: derived from NodeConfiguration.LocalTempRoot so that checkpoints
            // are co-located with pre-fetched scenario files under the same root volume
            // (CGF1-S0303 / A.3 config alignment).  Default: C:\FDP_Temp\checkpoints.
            // Override LocalTempRoot in config.json for non-default deployments.
            var checkpointStoragePath = System.IO.Path.Combine(nodeConfig.LocalTempRoot, "checkpoints");
            _checkpointWorker = new CheckpointIOWorker(checkpointStoragePath, localNodeId);

            // GhostCreationSystem and NetworkLifecycleGroup come from the replication module.
            // The same instances are used for both orchestration replay control and the
            // replication module itself, ensuring a single source of truth.
            var simHostArchService = new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(_kernel);
            var simHostEntityService = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(_world, _entityMap);
            var simHostLogService = new Hrot.Core.Diagnostics.LogArchiveExtractionService(
                string.IsNullOrWhiteSpace(hrotConfig.LogDirectory)
                    ? System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs")
                    : hrotConfig.LogDirectory,
                hrotConfig.SubsystemName,
                localNodeId);
            var diagnosticsDumpHandler = new Hrot.Common.Diagnostics.DiagnosticsDumpClusterOpHandler(
                _eventHistoryService,
                simHostArchService,
                simHostEntityService,
                simHostLogService,
                hrotConfig);
            var ghostCreationSystem   = replicationModule.GhostCreationSystem;
            var networkLifecycleGroup = replicationModule.NetworkLifecycleGroup;
            var nedModule = replicationModule as Hrot.Common.Abstractions.INedReplicationModule;

            var bootstrapper = new NodeBootstrapper(_networkFactory);
            _clusterSlave = bootstrapper.BuildOrchestration(
                _role, _kernel, _world, localNodeId,
                participant: ddsParticipant,
                subsystemName: "SimHost",
                eventBus: _eventBus,
                scenarioSerializer: null, // simhost does not load/save scenarios (cgf does)
				localTempRoot: nodeConfig.LocalTempRoot,
                checkpointWorker: _checkpointWorker,
                simGroup: _toggleSim,
                lifecycleGroup: networkLifecycleGroup,
                ghostCreationSystem: ghostCreationSystem,
                eventAccumulator: _context.EventAccumulator,
                afterSeek: nedModule?.AfterSeekCallback,
                diagnosticsDumpHandler: diagnosticsDumpHandler);
            _slaveTranslator = bootstrapper.SlaveTranslator;

            // Seed GlobalTime singleton.
            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 1.0f / nodeConfig.SimulationRateHz,
                TimeScale = 1.0f
            });

            // ── 9. Toolkit modules ────────────────────────────────────────────
            // Physics: allocate RaycastBatchData singleton so BallisticsSystem and
            // RaycastSolverSystem operate (guards inside those systems return early
            // when the singleton is absent).
            _physicsModule = new PhysicsToolkitModule();
            _physicsModule.Initialize(_world);

            // ── Register infrastructure base modules from builder context ──────
            // BaseModules contains EntityLifecycleModule and GeographicModule.
            // The same elm instance is used by NedReplicationModule's GhostPromotionSystem.
            foreach (var m in _context.BaseModules)
                _kernel.RegisterModule(m);

            // Extract elm reference for spawning systems (uses same instance as BaseModules[0]).
            var elm = (Fdp.Toolkit.Lifecycle.EntityLifecycleModule)_context.BaseModules[0];

            var spawningSystem = new NetworkSpawningSystem(
                tkbDb,
                elm,
                entityMap,
                _idAllocator!,
                localNodeId,
                onEntitySpawned: (world, entity, isLocalAuthority) =>
                {
                    if (isLocalAuthority && world.HasComponent<SimTransform>(entity))
                    {
                        world.SetAuthority<SimTransform>(entity, true);
                        if (world.HasComponent<NetworkTransform>(entity))
                            world.SetAuthority<NetworkTransform>(entity, true);
                        if (world.HasComponent<NetworkVelocity>(entity))
                            world.SetAuthority<NetworkVelocity>(entity, true);
                    }
                });

            // ── DDS adapters and request-handling systems ───────────────────────
            // Entity lifecycle (create/delete) is handled by the brain (CGF), not the muscle (SimHost).
            // UpdateEntityAttributeRequestSystem and UpdateEntityDescriptorRequestSystem are
            // registered by INetworkFactory.CreateSimHostAuxiliaryTranslators().

            var simHostMod = new SimHostModule(
                spawnSystem: spawningSystem);
            _kernel.RegisterModule(simHostMod);

            // Register the core simulation logic pack.
            _kernel.RegisterModule(_simCorePack!);
            _perceptionMod = new AutonomousPerceptionModule(
                colliderRadiusReader: (view, e) => view.HasComponent<PhysicsCollider>(e)
                    ? view.GetComponentRO<PhysicsCollider>(e).Radius
                    : 0f);
            _kernel.RegisterModule(_perceptionMod);

            // ── 10. Register replication module (bundles all translator packs) ──
            // Packs included: SharedTranslatorPack (EntityMaster, EntityInfo, EntityDamage, FireInteraction),
            //                 KinematicTranslatorPack (GeoSpatial, MapVisualOverlay, MapRoute, NavStatus, NavIntent),
            //                 CognitiveTranslatorPack (NavIntent, EntityMission*).
            _kernel.RegisterModule(replicationModule);

            // ── 11. Auxiliary network translators (time-sync, combat, mission-control) ──
            // These translators are SimHost-domain-specific and cannot be bundled into the
            // shared packs due to layer constraints. They are registered alongside the packs.
            if (ddsParticipant != null && nodeFactory != null)
            {
                nodeFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(_kernel);
                nodeFactory.CreateSimHostPerceptionTranslators(ghostCreationSystem).RegisterOn(_kernel);
                nodeFactory.CreateSimHostPathfindingTranslators(_simCorePack!.TrajectoryPool).RegisterOn(_kernel);
            }

            // ── 11. Kernel init ───────────────────────────────────────────────
            _kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("World", _eventHistoryService, _world.Bus));
            if (_eventBus != null)
                _kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Orchestration", _eventHistoryService, _eventBus));
            _kernel.Initialize();
            Logger.Info($"[Node-{localNodeId}] Kernel initialized.");

            // ── 12. Visualization ─────────────────────────────────────────────
            if (!_headless)
            {
                _vis = new SimHostVisualization();
                _vis.Initialize(
                    _world,
                    _kernel,
                    roadNetwork,
                    _simCorePack!.TrajectoryPool,
                    _simCorePack!.FormationTemplates,
                    nodeFactory?.CreateSimHostMissionSender() ?? new NullSimHostMissionSender(),
                    _eventHistoryService,
                    idAllocator: _idAllocator,
                    localNodeId: localNodeId,
                    worldPosDescriptorId: _networkFactory?.WorldPosDescriptorId ?? 0);
                _vis.FdpEntityInspector.ExtractionService = simHostEntityService;

                Logger.Info($"[Node-{localNodeId}] Visualization ready. Window open.");
            }

            _initialized = true;
            Logger.Info($"[Node-{localNodeId}] Initialized.");
        }

        protected override void OnUpdate(float dt)
        {
            // CMC-S016: translator tick BEFORE clusterSlave so DDS->bus ingress is processed first.
            _slaveTranslator?.Tick();
            _clusterSlave?.Tick();
            _vis?.Update(dt);
            _kernel?.Update();     // then run egress scan (picks up dirty -> publishes immediately)
            // Bridge SwitchTimeModeEvent and FrameOrder/FrameAck for distributed time control.
            // Placed after kernel.Update() so ScanAndPublish picks up FrameStepCompletedEvent
            // that SlaveSyncController published this frame (1-frame-delay egress).
            _timeModeTranslator?.ScanAndPublish(null!);
            _timeModeTranslator?.PollIngress(null!, null!);
            _lockstepTranslator?.ScanAndPublish(null!);
            _lockstepTranslator?.PollIngress(null!, null!);
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
        public void InitializeEmbedded(bool headless = false, int? domainIdOverride = null, int nodeIdOverride = 0, INetworkFactory? networkFactory = null)
        {
            _headless       = headless;
            _domainOverride = domainIdOverride;
            _nodeIdOverride = nodeIdOverride;
            _networkFactory = networkFactory;
            OnLoad();
        }

        /// <summary>
        /// Initializes the SimHost application without creating a Raylib window.
        /// Intended for integration tests and headless runners.
        /// </summary>
        public void InitializeHeadless(int? domainIdOverride = null, int nodeIdOverride = 0, INetworkFactory? networkFactory = null)
            => InitializeEmbedded(headless: true, domainIdOverride: domainIdOverride, nodeIdOverride: nodeIdOverride, networkFactory: networkFactory);

        /// <summary>
        /// Disposes all SimHost resources.
        /// Pass <paramref name="ownsWindow"/> = <c>false</c> when the orchestrator
        /// owns the Raylib window (i.e. when used via <see cref="InitializeEmbedded"/>).
        /// </summary>
        public void Shutdown(bool ownsWindow = false)
        {
            if (!_initialized) return;
            _initialized = false;
            var localNodeId = _nodeIdOverride != 0 ? _nodeIdOverride : SimHostNetworkConstants.LocalNodeId;

            // ── Stop ClusterSlave (CGF1-S0104) ──────────────────────────────────
            _clusterSlave?.Dispose();
            _clusterSlave = null;

            // ── Dispose CheckpointIOWorker (lets background I/O finish before kernel teardown)
            _checkpointWorker?.Dispose();
            _checkpointWorker = null;

            // ── Dispose simulation resources ──────────────────────────────────
            _physicsModule?.Dispose();
            _physicsModule = null;
            _vis?.Dispose();
            _vis = null;
            _idAllocator?.Dispose();
            _kernel?.Dispose();

            Logger.Info($"[Node-{localNodeId}] Shutdown complete.");

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

        /// <summary>
        /// TestHook: exposes the <see cref="BehaviorRegistry"/> so integration tests can
        /// register scenario-specific behaviors (e.g. UrbanCombat) before transitioning the
        /// cluster to OperatingLive.
        /// </summary>
        public BehaviorRegistry TestHook_BehaviorRegistry => _behaviorRegistry
            ?? throw new InvalidOperationException("SimHostApp is not initialized.");

        /// <summary>TestHook: current kernel simulation time in seconds. Updates every frame.</summary>
        public double TestHook_CurrentSimTime => _kernel?.CurrentTime.TotalTime ?? 0.0;

        /// <summary>
        /// Current kernel simulation time in seconds.  Updated every frame.
        /// Exposed for the time-transport status-bar UI in <see cref="SimHostSubsystem"/>.
        /// </summary>
        internal double CurrentSimTime => _kernel?.CurrentTime.TotalTime ?? 0.0;

        /// <summary>
        /// The orchestration event bus.  Exposed so <see cref="SimHostSubsystem"/> can
        /// create a <see cref="Hrot.UI.Common.Adapters.ClusterTimeTransportAdapter"/> without
        /// accessing internal kernel state directly.
        /// </summary>
        internal FdpEventBus? OrchestrationEventBus => _eventBus;
        /// <summary>
        /// Internal test hook: returns the runtime type of the currently active time controller
        /// in the SimHost kernel.  Tests use this to verify that after Pause/Resume the
        /// <see cref="Fdp.Toolkit.Time.Controllers.SlaveSyncController"/> transitions correctly.
        /// </summary>
        public Type? TestHook_TimeControllerType => _kernel?.GetTimeController().GetType();

        /// <summary>
        /// TestHook: returns the current <see cref="Fdp.ModuleHost.Time.TimeMode"/> of
        /// the kernel's time controller.  Used in integration tests to verify mode transitions
        /// of <see cref="Fdp.Toolkit.Time.Controllers.SlaveSyncController"/>.
        /// </summary>
        public Fdp.ModuleHost.Time.TimeMode? TestHook_TimeControllerMode
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
            SmartEgressUtil.MarkDirty(_world, entity, _networkFactory?.WorldPosDescriptorId ?? 0);
        }

        /// <summary>
        /// TestHook: attaches a <see cref="NavigationIntent"/> (MoveTo) directly to the entity,
        /// simulating the intent that would arrive from the CGF Brain over the network.
        /// This is the architecturally-correct way to trigger movement on the Muscle node in tests.
        /// </summary>
        public void TestHook_SetMovementIntent(long networkId, Vector2 destination, float speed = 15f)
        {
            if (_world == null || _entityMap == null)
                throw new InvalidOperationException("SimHostApp is not initialized.");

            if (!_entityMap.TryGetEntity(networkId, out var entity))
                throw new InvalidOperationException($"Entity with networkId={networkId} not found in entity map.");

            if (!_world.IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive.");

            var hasIntent = _world.HasComponent<NavigationIntent>(entity);
            var intent = hasIntent
                ? _world.GetComponent<NavigationIntent>(entity)
                : new NavigationIntent();

            intent.Mode             = NavigationMode.DirectPoint;
            intent.FinalDestination = destination;
            intent.TargetSpeed      = speed;
            intent.ArrivalRadius    = 20f;
            unchecked { intent.IntentId++; }

            if (hasIntent)
                _world.SetComponent(entity, intent);
            else
                _world.AddComponent(entity, intent);
        }

        /// <summary>TestHook: returns the current <see cref="SimTransform"/> of the entity, or default.</summary>
        public SimTransform TestHook_GetSimTransform(long networkId)
        {
            if (_world == null || _entityMap == null) return default;
            if (!_entityMap.TryGetEntity(networkId, out var entity)) return default;
            if (!_world.IsAlive(entity) || !_world.HasComponent<SimTransform>(entity)) return default;
            return _world.GetComponent<SimTransform>(entity);
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
        /// TestHook: registers an additional ECS system on the kernel.
        /// Must be called after <see cref="InitializeEmbedded"/> and before the first
        /// <see cref="Tick"/> so that the system participates from the first frame.
        /// Intended only for in-process integration/E2E tests.
        /// </summary>
        public void TestHook_AddSystem(IEcsModuleSystem system)
        {
            if (!_initialized)
                throw new InvalidOperationException("SimHostApp is not initialized.");
            _kernel!.RegisterGlobalSystem(system);
        }

        // ── Component registration ────────────────────────────────────────────

        /// <summary>
        /// Pre-registers all ECS component types and events required by the SimHost
        /// simulation.  Delegates to <see cref="SimHostComponentRegistry.RegisterAll"/>.
        /// </summary>
        private static void RegisterSimComponents(EntityRepository world)
            => SimHostComponentRegistry.RegisterAll(world);

        // ── Private helpers ───────────────────────────────────────────────────
        // NOTE: EnsureIdAllocatorRouting deleted (EAM-M001). DdsIdAllocatorHelper.EnsureRouting
        // is now called by HrotNodeBuilder.Build() internally.

        /// <summary>
        /// Loads a road-network blob from <paramref name="path"/> using the supplied
        /// <paramref name="loader"/> (default: <see cref="RoadNetworkLoader.LoadFromJson"/>).
        /// Returns a default <see cref="RoadNetworkBlob"/> when the path is empty or the
        /// loader throws.  The <paramref name="loader"/> parameter exists for unit-testing.
        /// </summary>
        internal static RoadNetworkBlob LoadRoadNetwork(
            string?                         path,
            Func<string, RoadNetworkBlob>?  loader = null,
            long                            localNodeId = 0)
        {
            if (string.IsNullOrWhiteSpace(path))
                return new RoadNetworkBlob();

            try
            {
                return (loader ?? RoadNetworkLoader.LoadFromJson)(path);
            }
            catch (Exception ex)
            {
                FdpLog<SimHostApp>.Warn("[Node-{0}] Failed to load road network: {1}", localNodeId, ex.Message);
                return new RoadNetworkBlob();
            }
        }

        // IEcsModule wrapper that routes TogglableSimulationGroup into the Simulation phase slot.
        // RegisterGlobalSystem rejects SystemPhase.Simulation; it must be registered via RegisterModule.
        private sealed class SimHostSimulationModule : IEcsModule
        {
            private readonly TogglableSimulationGroup _group;
            public string Name => "SimHostSimulation";
            public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
            public SimHostSimulationModule(TogglableSimulationGroup group) => _group = group;
            public void RegisterSystems(ISystemRegistry registry) => registry.RegisterSystem(_group);
            public void Tick(ISimulationView view, float deltaTime) { }
        }
    }

    /// <summary>No-op replication module used when no INetworkFactory is injected (unit-test / offline path).</summary>
    internal sealed class NullReplicationModule : Hrot.Common.Abstractions.IReplicationModule
    {
        private readonly GhostCreationSystem _gcs = new(new Fdp.Toolkit.Replication.Services.NetworkEntityMap());
        private readonly Fdp.ModuleHost.Scheduling.NetworkLifecycleSystemGroup _nlg = new();
        public string Name => "Null";
        public Fdp.ModuleHost.Abstractions.ExecutionPolicy Policy => Fdp.ModuleHost.Abstractions.ExecutionPolicy.Synchronous();
        public GhostCreationSystem GhostCreationSystem => _gcs;
        public bool DriveFromNetwork => false;
        public Fdp.ModuleHost.Scheduling.NetworkLifecycleSystemGroup NetworkLifecycleGroup => _nlg;
        public void Tick(Fdp.ModuleHost.Abstractions.ISimulationView view, float dt) { }
        public void RegisterSystems(Fdp.ModuleHost.Abstractions.ISystemRegistry registry)
        {
            registry.RegisterSystem(new CycloneNetworkCleanupSystem(System.Linq.Enumerable.Empty<IDescriptorTranslator>()));
        }
    }

    /// <summary>No-op mission sender used when no INetworkFactory is injected.</summary>
    internal sealed class NullSimHostMissionSender : ISimHostMissionSender
    {
        public void SendNavigateToPoint(long id, System.Numerics.Vector2 dest, float speed, float radius) { }
        public void Dispose() { }
    }
}
