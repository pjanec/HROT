using Hrot.Core.Mission;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Map.Common.Events;
using Hrot.Map.Definitions.Tkb;
using Hrot.SimHost.Configuration;
using Hrot.SimHost.Modules;
using Hrot.Common.Infrastructure;
using Hrot.Common.Scenario;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
using Hrot.Common.Systems;
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
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.IG.Components;
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
        // ── Gizmo systems (GZ032) ───────────────────────────────────────
        private DebugPrimitiveBuffer? _gizmoBuffer;
        private GizmoRegistry? _gizmoRegistry;
        private StatelessGizmoRegistry? _statelessGizmoRegistry;
        private GlobalGizmoManager? _globalGizmoManager;
        private DataDrivenGizmoSystem? _dataDrivenGizmoSystem;        private FdpEventBus? _interactionBus;
        private Fdp.Interfaces.INetworkTranslator? _gizmoIngressTranslator;
        private GizmoExecutionController? _gizmoController;
        // DEBT-002: hub broadcasts DTO state to all connected terminals.
        private readonly Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub _gizmoUiHub = new Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub();
        // GZH-003: provides Phase-5 perspective switching with ref-counted gate.
        internal GizmoExecutionController GizmoController => _gizmoController!;
        // DEBT-002: exposed for future module installation (BATCH-04).
        internal Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub GizmoUiHub => _gizmoUiHub;        // ── Schema publisher (GZ052) ────────────────────────────────────
        private Fdp.Toolkit.Replication.Patching.JsonAttributeCompiler? _jsonAttributeCompiler;
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
        // CheckpointIOWorker owns the background I/O thread; created in OnLoad,
        // passed to BuildOrchestration, and disposed in Shutdown (CGF1-S0303 A.1).
        private CheckpointIOWorker? _checkpointWorker;
        // Bootstrapper for SM-009: delegates 7-phase initialization to SharedApplicationBootstrapper.
        private SimHostNodeBootstrapper? _bootstrapper;

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
        private Hrot.SimHost.Modules.CognitiveSpatialModule? _perceptionMod;
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
            FdpLog<SimHostApp>.Info("[Node-{0}] Starting graphical application...", localNodeId);

            // ── 0. Apply node configuration (sets CYCLONEDDS_URI if needed) ───
            _nodeConfig?.ApplyEnvironment();
            FdpLog<SimHostApp>.Info("[Node-{0}] Node role: {1}", localNodeId, _role);

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
            FdpLog<SimHostApp>.Info("[Node-{0}] Domain ID:       {1}", localNodeId, domainId);
            FdpLog<SimHostApp>.Info("[Node-{0}] Node ID:         {1}", localNodeId, localNodeId);
            FdpLog<SimHostApp>.Info("[Node-{0}] Simulation Rate: {1} Hz", localNodeId, nodeConfig.SimulationRateHz);

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
                        ? Fdp.Toolkit.Orchestration.OrchestrationConstants.ResolveStagingRoot()
                        : nodeConfig.LocalTempRoot,
                    "nodes",
                    $"node-{localNodeId}"),
                LogDirectory        = Path.Combine(System.AppContext.BaseDirectory, "logs"),
                SubsystemName       = "SimHost",
            };

            // -- 6. Bootstrapper setup (SM-009) -----------------------------------------------
            // Create SimHostNodeBootstrapper and delegate 7-phase initialization.
            // The bootstrapper creates: CoreLogicPack, ClusterSlave, CheckpointWorker,
            // PhysicsModule, PerceptionModule, and wires all network translators.
            _bootstrapper = new SimHostNodeBootstrapper(
                _networkFactory,
                _role,
                nodeConfig.LocalTempRoot,
                _eventHistoryService,
                hrotConfig,
                nodeConfig.RoadNetworkBlobPath,
                nodeConfig.SimulationRateHz);

            // -- Gizmo systems (GZ032) --------------------------------------------------------
            // Registered via Phase 6d callback so they are part of the kernel before Initialize().
            // RegisterModule / RegisterGlobalSystem throw after Initialize() — must run in Phase 6d.
            var capturedLocalNodeId = localNodeId;
            _bootstrapper.ApplicationSystemsRegistrar = ctx =>
            {
                _gizmoBuffer = new DebugPrimitiveBuffer();
                _gizmoRegistry = new GizmoRegistry();
                _statelessGizmoRegistry = new StatelessGizmoRegistry();
                // GZ057: register entity presentation gizmos for SimHost.
                Hrot.SimHost.Gizmos.GizmoRegistrar.RegisterAll(
                    _gizmoRegistry,
                    _statelessGizmoRegistry,
                    settings: new GizmoSettingsRegistry());
                // Register CanvasContextMenuGizmo for empty-space right-click context menus.
                Hrot.Presentation.Gizmos.GizmoRegistrar.RegisterAll(
                    _gizmoRegistry,
                    _statelessGizmoRegistry,
                    settings: new GizmoSettingsRegistry());
                // BATCH-28 Phase 5: EntityDragGizmo replaces EntityDragTool.
                _gizmoRegistry.Register(new Hrot.ScenarioEditor.Gizmos.EntityDragGizmoDefinition());
                _interactionBus = new FdpEventBus();
                Hrot.Common.Interactions.InteractionEventRegistry.RegisterAll(_interactionBus);
                _globalGizmoManager = new GlobalGizmoManager(_gizmoBuffer, _interactionBus);
                _dataDrivenGizmoSystem = new DataDrivenGizmoSystem(
                    _gizmoRegistry,
                    _gizmoBuffer,
                    isSelectedPredicate: static (view, entity) =>
                        view.HasComponent<SelectionState>(entity) &&
                        view.GetComponentRO<SelectionState>(entity).IsSelected,
                    interactionBus: _interactionBus);
                // Register the global action registry and wire operator action handlers.
                var actionRegistry = new GlobalActionRegistry();
                long layerControlId = GlobalGizmoManager.NewId();
                var layerControlGizmo = new Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo(
                    layerControlId,
                    _interactionBus,
                    new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
                    _gizmoUiHub);
                _globalGizmoManager.Register(layerControlId, layerControlGizmo);
                actionRegistry.Register(GlobalActionIds.OpenLayerControl, (_, _) =>
                {
                    _interactionBus.Publish(new Hrot.Common.Diagnostics.Gizmos.OpenLayerEditorEvent());
                });
                actionRegistry.Register(GlobalActionIds.Rotate, (view, target) =>
                {
                    if (target == Entity.Null) return;
                    if (!view.HasComponent<SimTransform>(target)) return;
                    // Always start fresh: deactivate any existing gizmo, then inject the new one.
                    _dataDrivenGizmoSystem!.DeactivateGizmo(target);
                    var gizmo = new Hrot.SimHost.Gizmos.EntityRotatorGizmo(
                        view, target,
                        onRemove: () => _dataDrivenGizmoSystem!.DeactivateGizmo(target));
                    _dataDrivenGizmoSystem!.ActivateGizmo(target, gizmo);
                });

                // ── AI diagnostics toggles (behav-diag-1) ─────────────────────────
                actionRegistry.Register(GlobalActionIds.ToggleAiTrace, (view, target) =>
                    Hrot.SimHost.Diagnostics.AiTraceContextMenu.PublishToggle(
                        view, target, Fdp.Toolkit.Behavior.Diagnostics.BehaviorDebugFlags.EnableTraceBuffer));
                actionRegistry.Register(GlobalActionIds.ToggleAiTraceLog, (view, target) =>
                    Hrot.SimHost.Diagnostics.AiTraceContextMenu.PublishToggle(
                        view, target, Fdp.Toolkit.Behavior.Diagnostics.BehaviorDebugFlags.EmitToLog));
                // Route gizmo interaction translators and publisher through the network factory
                // so that SimHostApp has no direct dependency on Hrot.Network.NED.
                CycloneNetworkIngressSystem? gizmoIngress = null;
                CycloneEgressSystem? gizmoEgress = null;
                if (_networkFactory != null)
                {
                    // SimHostApp always receives UI interactions from remote viewers (headless=true).
                    var gizmoTranslators = _networkFactory.CreateGizmoTranslators(_interactionBus, capturedLocalNodeId, headless: true);
                    var ingressList = new System.Collections.Generic.List<Fdp.Interfaces.INetworkTranslator>();
                    var egressList  = new System.Collections.Generic.List<Fdp.Interfaces.INetworkTranslator>();
                    foreach (var t in gizmoTranslators)
                    {
                        if ((t.Direction & Fdp.Interfaces.TranslatorDirection.Ingress) != 0) ingressList.Add(t);
                        if ((t.Direction & Fdp.Interfaces.TranslatorDirection.Egress)  != 0) egressList.Add(t);
                    }
                    if (ingressList.Count > 0)
                    {
                        _gizmoIngressTranslator = ingressList[0];
                        gizmoIngress = new CycloneNetworkIngressSystem(ingressList.ToArray());
                    }
                    if (egressList.Count > 0)
                        gizmoEgress = new CycloneEgressSystem(egressList.ToArray());
                    var publisherSystem = _networkFactory.CreateGizmoPublisherSystem(_gizmoBuffer, capturedLocalNodeId);
                    if (publisherSystem != null)
                        ctx.Kernel.RegisterGlobalSystem(publisherSystem);
                }
                var gizmoGroup = new TogglablePostSimulationGroup("GizmoExecution",
                    _globalGizmoManager,
                    _dataDrivenGizmoSystem,
                    new StatelessGizmoSystem(
                        _statelessGizmoRegistry,
                        _gizmoBuffer,
                        isSelectedPredicate: static (view, entity) =>
                            view.HasComponent<SelectionState>(entity) &&
                            view.GetComponentRO<SelectionState>(entity).IsSelected));
                // GZH-003: headless-first; enable only when a terminal connects.
                gizmoGroup.Enabled = false;
                _gizmoController = new GizmoExecutionController(gizmoGroup, _globalGizmoManager, _dataDrivenGizmoSystem);
                ctx.Kernel.RegisterModule(new GizmoInteractionModule(
                    _interactionBus,
                    contextIngress: new ContextActionIngressSystem(ctx.EntityMap!, _interactionBus),
                    interactionSystems: new IEcsModuleSystem[]
                    {
                        new GlobalActionDispatchSystem(actionRegistry, _interactionBus),
                        gizmoGroup,
                    },
                    gizmoIngress: gizmoIngress,
                    gizmoEgress:  gizmoEgress));
                // ── GZ052: Entity attribute schema publisher ──────────────────────
                // Build the compiler using the same geographic transform as the network factory.
                // SimHost is always the default processor in standalone mode.
                _jsonAttributeCompiler = Hrot.SimHost.AttributeCompilerFactory.Build(_geoTransform);
                IDdsWriter<Hrot.NED.Messages.EntityAttributeSchema>? schemaWriter =
                    ctx.Participant != null
                        ? new DdsWriterGizmoAdapter<Hrot.NED.Messages.EntityAttributeSchema>(ctx.Participant)
                        : null;
                ctx.Kernel.RegisterGlobalSystem(new Hrot.Network.NED.Attributes.EntityAttributeSchemaPublisherSystem(
                    nodeId:             capturedLocalNodeId,
                    compiler:           _jsonAttributeCompiler,
                    writer:             schemaWriter,
                    isDefaultProcessor: true));
                // -- Event history and canvas menu (Phase 6d, before kernel.Initialize()) --
                ctx.Kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("World", _eventHistoryService, ctx.World.Bus));
                if (ctx.EventBus != null)
                    ctx.Kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Orchestration", _eventHistoryService, ctx.EventBus));
                ctx.Kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Interaction", _eventHistoryService, _interactionBus));
                // Register canvas menu update so CanvasContextMenuGizmo has state to project.
                ctx.Kernel.RegisterGlobalSystem(new Hrot.Presentation.Systems.CanvasMenuUpdateSystem());
            };

            // BootstrapNode runs all 7 phases including Phase 6d (callback) and Phase 7 (Initialize).
            _context = _bootstrapper.BootstrapNode(hrotConfig, _role, _networkFactory);

            // Extract context fields after bootstrapping.
            _world          = _context.World;
            _kernel         = _context.Kernel;
            _eventBus       = _context.EventBus;
            _entityMap      = _context.EntityMap;
            _idAllocator    = _context.IdAllocator;
            _clusterSlave   = _context.ClusterSlave;
            _slaveTranslator = _bootstrapper.SlaveTranslator;
            _checkpointWorker = _bootstrapper.CheckpointWorker;
            _simCorePack    = _bootstrapper.CoreLogicPack;
            _physicsModule  = _bootstrapper.PhysicsModule;
            _perceptionMod  = _bootstrapper.PerceptionModule;
            _behaviorRegistry = _bootstrapper.BehaviorRegistry;

            // Update base.World and base.Kernel for FdpApplication compatibility.
            base.World  = _world;
            base.Kernel = _kernel;

            // Ensure _entityMap is available as a singleton.
            _world.SetSingletonManaged<NetworkEntityMap>(_entityMap!);

            // Phase 2a: expose the geographic transform as a world singleton so behavior
            // parameter resolvers can reach it through the world (instead of a factory-captured
            // closure). Null in Cartesian-only contexts — skip registration then.
            if (_geoTransform != null)
                _world.SetSingletonManaged<IGeographicTransform>(_geoTransform);

            // Architectural diagnostics service needed for visualization.
            var simHostEntityService = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(_world, _entityMap);
            FdpLog<SimHostApp>.Info("[Node-{0}] Kernel initialized.", localNodeId);

            // -- 12. Visualization ---------------------------------------------------------
            if (!_headless)
            {
                var roadNetwork = _bootstrapper!.RoadNetwork ?? new CarKinem.Road.RoadNetworkBlob();
                var configuredFactory = _networkFactory?.ConfigureForNode(_context, _role, _behaviorRegistry);
                _vis = new SimHostVisualization();
                _vis.Initialize(
                    _world,
                    _kernel,
                    roadNetwork,
                    _simCorePack!.TrajectoryPool,
                    _simCorePack!.FormationTemplates,
                    configuredFactory?.CreateSimHostMissionSender() ?? new NullSimHostMissionSender(),
                    _eventHistoryService,
                    idAllocator: _idAllocator,
                    localNodeId: localNodeId,
                    worldPosDescriptorId: _networkFactory?.WorldPosDescriptorId ?? 0,
                    gizmoBuffer: _gizmoBuffer,
                    gizmoSystem: _dataDrivenGizmoSystem,
                    interactionBus: _interactionBus);
                _vis.FdpEntityInspector.ExtractionService = simHostEntityService;

                FdpLog<SimHostApp>.Info("[Node-{0}] Visualization ready. Window open.", localNodeId);
            }

            _initialized = true;
            FdpLog<SimHostApp>.Info("[Node-{0}] Initialized.", localNodeId);
        }

        protected override void OnUpdate(float dt)
        {
            // CMC-S016: translator tick BEFORE clusterSlave so DDS->bus ingress is processed first.
            _slaveTranslator?.Tick();
            _clusterSlave?.Tick();
            _vis?.Update(dt);
            // Clear the primitive buffer before backend ECS systems populate it.
            _gizmoBuffer?.EndFrame(dt);
            _kernel?.Update();
            // NOTE: TimeNetworkModule translators (_timeModeTranslator, _lockstepTranslator) are now
            // registered by SharedApplicationBootstrapper.BootstrapNode() in Phase 6c. They tick
            // automatically as part of kernel.Update() via CycloneNetworkIngressSystem and
            // CycloneEgressSystem. No manual tick calls needed here.
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

            FdpLog<SimHostApp>.Info("[Node-{0}] Shutdown complete.", localNodeId);

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

        /// <summary>TestHook: exposes the gizmo primitive buffer for integration tests.</summary>
        internal DebugPrimitiveBuffer? TestHook_GizmoBuffer => _gizmoBuffer;

        /// <summary>TestHook: exposes the gizmo interaction ingress translator for integration tests.</summary>
        internal Fdp.Interfaces.INetworkTranslator? TestHook_GizmoIngressTranslator => _gizmoIngressTranslator;

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
            intent.FinalDestination = new Vector3(destination.X, destination.Y, 0f);
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
                return (loader ?? (p => RoadNetworkLoader.LoadFromJson(p)))(path);
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
