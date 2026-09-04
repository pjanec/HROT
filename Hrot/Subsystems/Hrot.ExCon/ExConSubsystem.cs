using Hrot.NED.Descriptors.Orchestration;
using Hrot.Common;
using Hrot.Common.Orchestration;
using Hrot.Core.Network;
using Hrot.ExCon;
using Hrot.ExCon.Logic;
using Hrot.ExCon.Services;
using Hrot.Map.Common;
using Hrot.Map.Common.Dds;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.ExCon.Panels;
using Hrot.UI.Common.Panels;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using Fdp.Toolkit.DER;
using Hrot.ExCon.Windows;
using Hrot.Orchestrator.Panels;
using Hrot.Orchestrator.Windows;
using Hrot.Orchestrator;
using Fdp.Toolkit.Runner;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using Fdp.ModuleHost.Time;
using Fdp.Core.Diagnostics;
using Fdp.ModuleHost.Diagnostics;
using Fdp.Toolkit.Diagnostics;
using Hrot.Core.Diagnostics;
using Hrot.Common.Diagnostics;
using Hrot.Common.Infrastructure;

namespace Hrot.ExCon
{
    /// <summary>
    /// <see cref="ISubsystem"/> implementation that embeds the ExCon (Interactive Operations Station).
    ///
    /// <para>Lifecycle:
    /// <list type="number">
    ///   <item><see cref="Initialize"/> — creates <see cref="DerRepo"/>, all ExCon panels,
    ///   <see cref="ExConLogic"/>, and <see cref="ExConMock"/>.</item>
    ///   <item><see cref="Update"/> — delegates to <see cref="ExConMock.Update"/>.</item>
    ///   <item><see cref="DrawWorld"/> — no-op (ExCon has no 3-D world visuals; all UI is ImGui).</item>
    ///   <item><see cref="DrawUI"/> — delegates to <see cref="ExConMock.DrawUI"/>
    ///   (rendered inside <c>rlImGui.Begin()</c>).
    ///   Skipped when <see cref="SubsystemConfig.Headless"/> is <c>true</c>.</item>
    ///   <item><see cref="Shutdown"/> — disposes <see cref="ExConMock"/> and underlying logic.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class ExConSubsystem : ISubsystem, IWindowRegistrar,
        Hrot.Presentation.DebugApi.IProvidesDebugSurface
    {
        /// <inheritdoc/>
        public string Name => "ExCon";

        /// <summary>
        /// ⭐⭐ <b><c>Q54</c> — ExCon's debug surface, and it is deliberately THIN.</b>
        /// 📄 <c>Architect_Question_54</c> § <c>PARTICIPATE ≠ OBSERVE</c>.
        ///
        /// <para>📐 ExCon has <b>no ECS kernel</b> — it is the operator console, registered on the cluster with
        /// <c>liveRepo: null</c>. ⇒ ⭐ no world, no map, no drive: the provider exists so the PERSPECTIVE is
        /// routable *(its panels are readable, and the manifest says what is absent)*, ⛔ not so it can pretend
        /// to own a simulation.</para>
        ///
        /// <para>⛔⛔ <b>And it must never be added to the step roster</b> — with no frame to execute it can
        /// never publish <c>FrameStepCompletedEvent</c>, so the cluster would wait forever. ⭐ A step issued
        /// while the ExCon perspective is active is still CONFIRMED, because the gate reads the MASTER.</para>
        /// </summary>
        public Hrot.Presentation.DebugApi.ISubsystemDebugProvider? CreateDebugProvider()
            => new Hrot.Presentation.DebugApi.SubsystemDebugProvider(
                subsystemName: Name,
                perspective:   "ExCon",
                world:         null,
                entityMap:     null,
                drive:         null,
                // ⭐⭐ BP-487 — EXPLICITLY null, written out for the same reason `architecture` below is:
                //    📐 measured `2026-08-27`, a repo-wide grep for DebugPrimitiveBuffer in Hrot.ExCon finds
                //    NOTHING. ExCon draws no map and no gizmos, so `panels.gizmo` is FALSE here and that is
                //    the true cell (ruling 49: absent-and-explained beats present-and-broken).
                // ⛔ It is also the reason the capability could not be a hard-coded `true` on every row —
                //    see CapabilityManifest's BP-487 comment.
                gizmoBuffer:   null,
                // ⭐⭐ CE-110 — EXPLICITLY null, for the same reason gizmoBuffer above is: ExCon has no ECS
                //    world at all, so it cannot hold a TKB catalog. ⇒ `tkb.read` is FALSE on its perspective
                //    and /tkb/* answers NOT_SUPPORTED_HERE there. ⛔ An empty catalog would instead read as
                //    "ExCon knows no templates", which is a claim about data rather than about capability.
                tkbDb:         null,
                // ⭐⭐ HN-029: ExCon has NO ECS kernel — no world, no clock — but it DOES have an orchestration
                //    bus with an egress translator, which is exactly why it hosts a ClusterScenarioPanel today.
                //    ⇒ it can request a cluster-wide load without being able to read or step one. 📌 A neat
                //    demonstration that the capabilities are genuinely independent, not one "is it wired" bit.
                requestTransition: Hrot.Presentation.DebugApi.SubsystemDebugProvider
                                       .TransitionsVia(() => _bus),
                // ⭐⭐ MD-002 — EXPLICITLY null, written out rather than omitted: ExCon has no ECS kernel
                //    (it already builds ArchitectureDiagnosticsService(() => null) for its own window),
                //    so `diagnostics.architecture` is FALSE here and that is the true cell. ⛔ An empty
                //    snapshot would read as "this subsystem runs no modules", which is a different claim.
                architecture:      null,
                // ⭐⭐ HN-029: ExCon is the only subsystem in `--mode all` that builds and PUMPS a
                //    ClusterUiCache (`_uiCache?.Update()` per frame), and that cache is where
                //    ClusterStateUpdateEvent lands. ⇒ it supplies the readiness gate's state — and the
                //    scenario inventory — for the whole host. 📄 See PerspectiveScopedDispatcher
                //    .ClusterStateAnyNode for why reading it from another perspective is legitimate.
                // ⚠ TWO `ClusterState` enums exist — `Hrot.NED.Descriptors.Orchestration` (the wire
                //   descriptor, which the cache holds) and `Fdp.Toolkit.Orchestration` (the toolkit's). ⛔ Not
                //   a bug to fix here: `ClusterScenarioPanel` bridges them the same way, by int, and the two
                //   are kept numerically identical on purpose (OperatingLive == 31 in both).
                clusterState:       () => _uiCache is null
                                          ? null
                                          : (Fdp.Toolkit.Orchestration.ClusterState)(int)_uiCache.CurrentState,
                availableScenarios: () => _uiCache?.AvailableScenarios,
                // ⭐⭐ MD-006 — the dump trigger, on the same bus as requestTransition above.
                requestDiagnosticDump: Hrot.Presentation.DebugApi.SubsystemDebugProvider
                                           .DumpsVia(() => _bus),
                // ⭐⭐⭐ MD-007 — the STATUS, and ExCon is where it lives for the same measured reason the
                //    cluster state does: it is the one subsystem in `--mode all` that builds and PUMPS a
                //    ClusterUiCache. ⭐ This projects EXACTLY what ClusterDiagnosticsPanel renders —
                //    `LastDiagnosticManifest` plus the in-flight flag — ⛔ NOT DiagnosticsDumpProcessManager,
                //    which exposes only Tick() and is not what the panel reads.
                // ⚠ Primitives, not the cache: same reason clusterState is projected to an enum above.
                dumpStatus:         () => _uiCache is null
                                          ? null
                                          : new Hrot.Presentation.DebugApi.DiagnosticDumpStatus(
                                                _uiCache.HasInFlightTransaction,
                                                _uiCache.LastDiagnosticManifest
                                                        .Select(e => e.RelativeDest).ToList()));

        /// <inheritdoc/>
        /// <remarks>Violet — distinct from IG (green) and SimHost (red).</remarks>
        public System.Numerics.Vector4 TitleBarColor =>
            new System.Numerics.Vector4(0.32f, 0.08f, 0.48f, 1f);

        // Map group 0 = broadcast to all IG instances (matches ExConLogicConstants.DefaultMapGroupId).
        private const int DefaultMapGroupId = 0;

        // Subsystem name used for ClusterSlave registration (avoidance of magic strings).
        private const string SubsystemName = "ExCon";

        /// <summary>MapId of the IG this ExCon issues tool-activation commands to (0 = broadcast).</summary>
        private const int TargetMapId = 0;

        private ExConMock?         _mock;
        private bool             _headless;
        private DdsParticipant?  _participant;
        private List<IDisposable>? _ingressDisposables;
        private int              _nodeIdOverride;
        private Fdp.Toolkit.Orchestration.ClusterSlave? _clusterSlave;

        // control bus
        private FdpEventBus?                          _bus;


        /// <summary>
        /// Dedicated, read-only event bus for the ExCon UI observation layer.
        /// 
        /// This isolated bus is strictly necessary to prevent an infinite DDS echo loop (network storm). 
        /// ExCon acts as a promiscuous listener to populate its UI, but also actively sends commands. 
        /// If the UI observation layer and the active command layer share the main <c>_bus</c>:
        /// 
        /// 1. <c>OrchestrationObserverTranslator</c> reads <c>NodeOpStatus</c> from DDS and publishes 
        ///    <c>NodeOpCompletedEvent</c> to the shared bus to update the UI.
        /// 2. <c>NodeOpSlaveTranslator</c> listens to that same bus, sees the <c>NodeOpCompletedEvent</c>, 
        ///    assumes ExCon just finished a local operation, and blindly writes a new <c>NodeOpStatus</c> back to DDS.
        /// 3. DDS loopback immediately feeds that new status back to the observer, creating an exponential storm 
        ///    that starves the CPU via <c>DrainPendingLogs()</c> and hangs the application.
        /// 
        /// By quarantining <c>OrchestratorObserverTranslator</c> and <c>ClusterUiCache</c> on this <c>_observerBus</c>, 
        /// the UI can safely monitor all cluster traffic without cross-contaminating the active command bus.
        /// </summary>
        private FdpEventBus? _observerBus;

        // ── HEXAG2-S012: factory-managed slave orchestration handles ──────────
        private ISlaveOrchestrationTranslator?        _slaveTranslator;
        private IOrchestrationObserver?               _observer;
        private ClusterUiCache?                   _uiCache;
        private ClusterScenarioPanel?             _clusterPanel;
        private Hrot.Orchestrator.Panels.ClusterDiagnosticsPanel? _clusterDiagnosticsPanel;
        private DiagnosticLogMergeWorker?         _mergeWorker;
        private Fdp.Presentation.Abstractions.IFileDialogService? _exConFileDialogService;

        // ── TC2-P3: Slave time sync ─────────────────────────────────────────────────
        private SlaveSyncController?   _slaveSyncController;
        private IDescriptorTranslator? _timeModeTranslator;
        private IDescriptorTranslator? _slaveLockstepTranslator;
        private IDescriptorTranslator? _slaveTimeSyncTranslator;
        // ── TASK-P4-001: neutral ExCon gateway interfaces ──────────────────────────
        private IExConEgressWriters?  _egressWriters;
        private ITimeControlGateway?  _timeControl;
        private ICommandGateway?      _commandGateway;
        private INetworkFactory?      _networkFactory;

        /// <summary>Internal test hook: exposes the unified event bus for bus-unification assertions.</summary>
        internal FdpEventBus? BusForTest => _bus;

        /// <summary>Internal test hook: exposes the observer bus (used by <see cref="ClusterUiCache"/>) for test assertions.</summary>
        internal FdpEventBus? ObserverBusForTest => _observerBus;

        /// <summary>Internal test hook: exposes the <see cref="ClusterUiCache"/> for bus-unification assertions.</summary>
        internal ClusterUiCache? UiCacheForTest => _uiCache;

        /// <summary>
        /// Creates ExConSubsystem without a network factory (headless / legacy path).
        /// </summary>
        public ExConSubsystem() { }

        /// <summary>
        /// Creates ExConSubsystem with an injected protocol factory from the composition root.
        /// </summary>
        public ExConSubsystem(INetworkFactory networkFactory)
        {
            _networkFactory = networkFactory;
        }

        /// <summary>
        /// Internal test hook for integration tests.
        /// </summary>
        internal ExConLogic Logic => _mock?.Logic ?? throw new InvalidOperationException("Not initialized");

        /// <summary>
        /// Internal test hook: returns the effective NodeId wired from <see cref="SubsystemConfig.NodeId"/>
        /// at initialization time.
        /// </summary>
        internal int TestHook_NodeIdOverride => _nodeIdOverride;

        /// <summary>
        /// Internal test hook: exposes the <see cref="Fdp.Toolkit.Orchestration.ClusterSlave"/>
        /// for handler-registration assertions (CGF1-S0104 / A.3).
        /// </summary>
        internal Fdp.Toolkit.Orchestration.ClusterSlave? TestHook_ClusterSlave => _clusterSlave;

        /// <summary>
        /// Internal test hook: exposes the <see cref="SlaveSyncController"/> created
        /// during <see cref="Initialize"/> (TC2-P3-T1).
        /// </summary>
        internal SlaveSyncController? TestHook_SlaveSyncController => _slaveSyncController;

        /// <inheritdoc/>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;
            _nodeIdOverride = config.NodeId;

            // ⭐⭐⭐ CLUSTER PARTICIPATION — declared as a NodeBootPlan, exactly like an ECS node's,
            //    on a host that has NO ModuleHostKernel at all.
            //
            // This is the "short list" of docs/DESIGN_Subsystem_Composition_Unification.md §4.1O:
            // composition is not a prefabricated tier a host is sorted into, it is the list of shared
            // steps a host happens to compose. ExCon composes the CLUSTER-PARTICIPATION steps and
            // none of the ECS ones — no world, no kernel, no capabilities, and NodeRole.None below is
            // correct rather than an omission, because it has no capabilities to select.
            //
            // Same runner, same declare-and-verify semantics as SharedApplicationBootstrapper: the
            // order below is unchanged, and the keys make the couplings checkable instead of implied.
            // 📄 §4.1P (the plan), §4.1Q (this adoption).
            var iosNodeId = config.NodeId != 0 ? config.NodeId : 500;

            // ⚠ Locals mirroring the two bus fields. Assignments inside a step's closure are
            //   invisible to the compiler's nullable-flow analysis, so without these every later use
            //   of _bus/_observerBus warns. The locals are not a workaround for an unproven fact:
            //   the plan's declared keys ("orchestration-bus", "observer-bus") are what guarantee
            //   they are set, and Run() throws by key if a step that provides one did not run.
            FdpEventBus bus         = null!;
            FdpEventBus observerBus = null!;

            new Hrot.Common.Infrastructure.NodeBootPlan()

                // ── DDS participant ────────────────────────────────────────────────
                // Create participant in the Application Shell (Composition Root).
                // Rule: only the outermost executable may instantiate DdsParticipant.
                // HrotNodeBuilder no longer has a fallback
                .Step("participant", provides: new[] { "participant" }, run: () =>
                {
                    _participant = _networkFactory?.Participant;
                })

                // ── ClusterSlave (CGF1-S0104 / CMC-S016 BATCH-06) ────────────────────────
                // we pass _bus to the active command layer ...
                .Step("orchestration-bus", provides: new[] { "orchestration-bus" }, run: () =>
                {
                    _bus = bus = new FdpEventBus();
                    Fdp.Toolkit.Orchestration.OrchestrationEventRegistry.RegisterAll(bus);
                })

                .Step("cluster-slave",
                    requires: new[] { "orchestration-bus" },
                    provides: new[] { "cluster-slave" },
                    run: () =>
                    {
                        _clusterSlave = new Fdp.Toolkit.Orchestration.ClusterSlave(iosNodeId, SubsystemName, bus);
                    })

                .Step("observer-bus", provides: new[] { "observer-bus" }, run: () =>
                {
                    _observerBus = observerBus = new FdpEventBus();
                    Fdp.Toolkit.Orchestration.OrchestrationEventRegistry.RegisterAll(observerBus);
                })

                // ── TC2-P3-T1: Slave time sync pipeline ──────────────────────────────
                // SlaveSyncController is always created (no DDS needed).
                // DDS-backed translators are only wired when a participant is available.
                .Step("slave-sync-controller",
                    requires: new[] { "orchestration-bus" },
                    provides: new[] { "slave-sync-controller" },
                    run: () =>
                    {
                        _slaveSyncController = new SlaveSyncController(bus, iosNodeId, TimeConfig.Default);
                    })

                // ⭐ The three translators now come from the SHARED factory the kernel-owning nodes
                //   use — SlaveTimeTranslatorRegistration.Create. Until 2026-09-03 ExCon hand-built
                //   the same three calls here, purely because the shared helper only offered
                //   RegisterOn(kernel, ...) and ExCon has no kernel. The creation half is now
                //   separate from the kernel half, so there is one source for both.
                // ⚠ The `_participant != null` guard is KEPT verbatim: Create tolerates a null
                //   participant, but removing the guard would leave ExCon's fields non-null in
                //   headless mode and its Update() would start polling them. Behaviour unchanged.
                .Step("slave-time-translators",
                    requires: new[] { "participant", "orchestration-bus", "slave-sync-controller" },
                    run: () =>
                    {
                        if (_participant != null)
                        {
                            var t = Hrot.Common.Infrastructure.SlaveTimeTranslatorRegistration
                                .Create(_participant, bus, iosNodeId);
                            _timeModeTranslator      = t.Mode;
                            _slaveLockstepTranslator = t.SlaveLockstep;
                            _slaveTimeSyncTranslator = t.SlaveTimeSync;
                        }
                    })

                .Run(nameof(ExConSubsystem));

            // CGF1-BATCH-23 A.3: ExCon is an orchestrator instructor — it does NOT
            // save scenario fragments or exercise recordings.  If the orchestrator fans
            // out PrepareLive / FinalizeLive / PrepareReplay / FinalizeReplay to ExCon,
            // this node must ACK so the cluster 2PC is never stalled.
            // Shared listener controller tracks IsReplayActive for branch gating.
            var iosRrController = new Hrot.Common.Orchestration.ListenerRecordReplayController("ExCon");

            // Wire ReferenceReplayLoadHandler FIRST (PrepareReplay / FinalizeReplay;
            // PrepareLive only when replay active — Live-from-Replay branch gate).
            _clusterSlave!.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferenceReplayLoadHandler(
                iosRrController,
                inputGroup:            null,
                simGroup:              null,
                postSimGroup:          null,
                lifecycleGroup:        null,
                bypassLifecycleToggle: null,
                storageDirectory:      OrchestrationConstants.ResolveStagingRoot()));

            // Wire ReferenceLiveLoadHandler: ACKs cold PrepareLive and FinalizeLive.
            // ExCon carries no ECS state and does not start a recording.
            _clusterSlave.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferenceLiveLoadHandler(
                checkpointWorker: null,
                controller:       iosRrController,
                storageDirectory: OrchestrationConstants.ResolveStagingRoot()));

            // CGF1-S0309: wire dry-run snapshot/rewind handler (ExCon carries no ECS state).
            _clusterSlave.RegisterHandler(new ReferencePreviewHandler(liveRepo: null));

            // Wire ReferencePrefetchHandler / ReferenceArchiveHandler so ExCon ACKs
            // background file fan-outs (PrefetchFiles / SerializeLocal) and cannot stall 2PC UI tracking.
            var exConStorageProvider = new LocalDiskStorageProvider(OrchestrationConstants.ResolveStagingRoot());
            _clusterSlave.RegisterHandler(new ReferencePrefetchHandler(exConStorageProvider));
            _clusterSlave.RegisterHandler(new ReferenceArchiveHandler(
                OrchestrationConstants.ResolveStagingRoot(), iosNodeId));

            // Diagnostic dumps: ExCon contributes logs and ACKs CollectDiagnostics.
            var exConArchService = new ArchitectureDiagnosticsService(() => null);
            var exConEntityService = new NullEntityStateExtractionService();
            var exConEventHistoryService = new NullDiagnosticEventHistoryService();
            var exConLogService = new LogArchiveExtractionService(
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs"),
                SubsystemName,
                iosNodeId);
            _clusterSlave.RegisterHandler(new DiagnosticsDumpClusterOpHandler(
                exConEventHistoryService,
                exConArchService,
                exConEntityService,
                exConLogService,
                new HrotNodeConfig
                {
                    NodeId = iosNodeId,
                    SubsystemName = SubsystemName,
                    LocalTempRoot = OrchestrationConstants.ResolveStagingRoot(),
                    LogDirectory = System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs"),
                }));

            // ── Construct services ─────────────────────────────────────────────
            // DerRepo takes no external dependencies; node ID uses a fixed default.
            var repo              = new DerRepo();
            var transactionMgr    = new RequestTransactionManager();
            var interactionPanel  = new InteractionPanel();

            var clickQueue                   = new ConcurrentEventQueue<MapClickEventDto>();
            var selectionQueue               = new ConcurrentEventQueue<SelectionChangedEventDto>();
            var entityLifecycleAckQueue      = new ConcurrentEventQueue<EntityLifecycleAckDto>();
            var mapCommandAckQueue           = new ConcurrentEventQueue<MapCommandAckDto>();

            // Configure the injected factory for this node then create ExCon ingress handlers
            // and neutral gateway objects via the factory.
            var nodeFactory = _networkFactory?.ConfigureForNode(_participant, iosNodeId, NodeRole.None);

            var ingressHandlers = nodeFactory != null
                ? new List<IIngressHandler>(nodeFactory.CreateExConIngressHandlers(
                    _participant, iosNodeId, repo,
                    clickQueue.Enqueue,
                    selectionQueue.Enqueue,
                    entityLifecycleAckQueue.Enqueue,
                    mapCommandAckQueue.Enqueue))
                : new List<IIngressHandler>();

            // Neutral ExCon gateway objects — created via the injected factory.
            // When no factory is injected (unit-test / offline path), fall back to no-op stubs.
            _egressWriters  = nodeFactory?.CreateExConEgressWriters()  ?? new NullExConEgressWriters();
            _timeControl    = nodeFactory?.CreateTimeControlGateway()  ?? new NullTimeControlGateway();
            _commandGateway = nodeFactory?.CreateCommandGateway()      ?? new NullCommandGateway();

            var missionEditorSvc = new MissionEditorService(repo, _commandGateway);
            var contextMenuLogic  = new ContextMenuLogic(repo, _egressWriters);

            // ── Cluster control wiring (S0507 / PACK-C002) ────────────────────────────
            _ingressDisposables = new List<IDisposable>(ingressHandlers.Count);
            for (int i = 0; i < ingressHandlers.Count; i++)
            {
                if (ingressHandlers[i] is IDisposable d)
                    _ingressDisposables.Add(d);
            }

            // ── Cluster control wiring (S0507 / PACK-C002) ────────────────────────────
            _uiCache     = new ClusterUiCache(observerBus, _slaveSyncController);
            _clusterPanel = new ClusterScenarioPanel(bus, _uiCache);

            // Wire the cluster diagnostics panel (reads UICache on observerBus; publishes via bus).
            _exConFileDialogService  = Fdp.Presentation.Panels.FileDialogServiceFactory.Create();
            _clusterDiagnosticsPanel = new Hrot.Orchestrator.Panels.ClusterDiagnosticsPanel(
                _uiCache,
                bus,
                _exConFileDialogService,
                nasBasePath: Hrot.Orchestrator.ClusterConfiguration.Default.NasBasePath);
            _mergeWorker = new DiagnosticLogMergeWorker(bus);
            // HEXAG2-S012: factory-based slave orchestration handles.
            _slaveTranslator = nodeFactory?.CreateSlaveOrchestratorTranslators(_bus!, iosNodeId)
                               ?? new NullSlaveOrchestrationTranslator();
            _observer        = nodeFactory?.CreateOrchestrationObserver(_observerBus!)
                               ?? new NullOrchestrationObserver();

            var logic = new ExConLogic(
                repo:                 repo,
                missionEditorService: missionEditorSvc,
                contextMenuLogic:     contextMenuLogic,
                transactionManager:   transactionMgr,
                egressWriters:        _egressWriters,
                clickQueue:           clickQueue,
                selectionQueue:       selectionQueue,
                interactionPanel:     interactionPanel,
                createEntityAckQueue: entityLifecycleAckQueue,
                ingressHandlers:      ingressHandlers,
                mapGroupId:           DefaultMapGroupId,
                targetMapId:          TargetMapId,
                mapCommandAckQueue:   mapCommandAckQueue,
                timeControl:          _timeControl,
                localNodeId:          config.NodeId);

            // S0507: Time ingress handlers removed (TC2-P3-T4): OnTimePulse/OnTimeMode on
            // ExConLogic are purely display properties that are never consumed by any panel
            // or game-logic path. Time display is now handled by SlaveSyncController →
            // ClusterUiCache (injected above).

            var tkbCatalog = new TkbCatalogEntry[]
            {
                new(TkbEntityTypes.Tank_M1Abrams,      "M1 Abrams"),
                new(TkbEntityTypes.IFV_Bradley,        "M2 Bradley IFV"),
                new(TkbEntityTypes.Truck_HMMWV,        "HMMWV"),
                new(TkbEntityTypes.Tank_T72,           "T-72"),
                new(TkbEntityTypes.Infantry_Rifleman,  "Infantry Rifleman"),
                new(TkbEntityTypes.Infantry_Officer,   "Infantry Officer"),
                new(TkbEntityTypes.Unit_TankPlatoon,   "Tank Platoon (Empty)"),
                new(TkbEntityTypes.Unit_InfantrySquad, "Infantry Squad (Empty)"),
                new(TkbEntityTypes.Unit_TankPlatoon_Auto, "Tank Platoon (Auto-Spawn)"),
            };

            // Conceptually, ORBAT panel should only create organizational units.
            var orbatCatalog = tkbCatalog.Where(e => e.Name.Contains("Platoon") || e.Name.Contains("Squad")).ToArray();

            _mock = new ExConMock(
                logic:            logic,
                configPanel:      new ConfigPanel(iosNodeId),
                orbatPanel:       new OrbatPanel(orbatCatalog),
                missionPanel:     new MissionPanel(iosNodeId, Hrot.Presentation.Behavior.BehaviorUiSetup.CreateRegistry()),
                interactionPanel: interactionPanel,
                spawnerPanel:     new SpawnerPanel(tkbCatalog),
                useDockSpace:     config.OwnWindow);
        }

        /// <inheritdoc/>
        public void Update(float deltaTime)
        {
            // Phase 1: Network boundary — DDS ingress and time-sync egress.
            // PollIngress reads from DDS and writes to _bus WRITE buffer.
            // SlaveSyncController reads from _bus CURRENT (previous frame events) and advances state.
            // ScanAndPublish reads from _bus CURRENT and sends ACKs/NTP requests to DDS.
            _timeModeTranslator?.PollIngress(null!, null!);
            _slaveLockstepTranslator?.PollIngress(null!, null!);
            _slaveTimeSyncTranslator?.PollIngress(null!, null!);
            _slaveSyncController?.Update();
            _slaveLockstepTranslator?.ScanAndPublish(null!);
            // Bug 8 fix: ScanAndPublish drains TimeSyncRequest events from the bus and sends
            // them to DDS so the NTP handshake with the master can complete.
            // Without this call the ExCon clock offset stays 0 forever (NTP requests silently
            // discarded each SwapBuffers), making ExCon sim-time wrong in multi-process deployments.
            _slaveTimeSyncTranslator?.ScanAndPublish(null!);

            // Phase 2: Single frame boundary swap — exactly one SwapBuffers per frame.
            // Preserves phase discipline: all ingress/ScanAndPublish complete before swap.
            _bus?.SwapBuffers();

            _observerBus?.SwapBuffers();

            // CMC-S016: orchestration + observer + egress processing after swap so translators
            // read events published in Phase 1 and observe cluster state changes.
            _slaveTranslator?.Tick();  // HEXAG2-S012: NodeOpCommand ingress + heartbeat/status egress + ClusterOp egress
            _clusterSlave?.Tick();
            _mergeWorker?.Tick();
            // HEXAG2-S012: translate DDS orchestration observations -> bus events, then update the cache
            _observer?.Tick();
            _uiCache?.Update();
            _clusterPanel?.Update(deltaTime);

            _mock?.Update(deltaTime);
        }

        /// <summary>No-op — ExCon has no 3-D world visuals; all content is rendered via <see cref="DrawUI"/>.</summary>
        public void DrawWorld() { }

        /// <inheritdoc/>
        /// <remarks>
        /// Renders all ExCon ImGui panels (config, orbat, mission, interaction, spawner).
        /// Called inside <c>rlImGui.Begin()</c> by the orchestrator.
        /// No-op in headless mode.
        /// </remarks>
        public void DrawUI()
        {
            if (_headless) return;
            // ExCon mock panels still rendered via DrawUI (not yet window-managed).
            _mock?.DrawUI();
        }

        /// <inheritdoc/>
        public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
        {
            windowManager.RegisterWindow(new ClusterControlWindow(_clusterPanel, _uiCache));

            // Register diagnostics window.
            if (_clusterDiagnosticsPanel != null)
                windowManager.RegisterWindow(new Hrot.Orchestrator.Windows.DiagnosticsWindow(_clusterDiagnosticsPanel));

            if (_mock != null)
            {
                var logic = _mock.Logic;
                windowManager.RegisterWindow(new ExConOrbatWindow(_mock.GetOrbatPanel(), logic));
                windowManager.RegisterWindow(new ExConMissionWindow(_mock.GetMissionPanel(), _mock.MissionShim, _mock.MapPickShim));
                windowManager.RegisterWindow(new ExConDataMonitorWindow(_mock.GetInteractionPanel(), logic));
                windowManager.RegisterWindow(new ExConSpawnerWindow(_mock.GetSpawnerPanel(), _mock.SpawnController));
                windowManager.RegisterWindow(new ExConConfigWindow(_mock.GetConfigPanel(), _mock.MapConfigAdapter));
                windowManager.RegisterWindow(new ExConDiagnosticsWindow(_mock.GetDiagnosticsPanel(), logic));
                windowManager.RegisterWindow(new ExConDerEntityInspectorWindow(_mock.GetDerEntityInspectorPanel(), logic));
                _mock.SetPanelsWindowManaged();
            }

            // Wire the ImGui file dialog fallback so it renders on non-Windows hosts.
            // Harmless no-op for the Win32 backend: WindowManager only draws the service
            // when it is an ImGuiFileDialogService.
            if (_exConFileDialogService != null)
                windowManager.SetFileDialogService(_exConFileDialogService);
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            _clusterSlave?.Dispose();
            _clusterSlave = null;
            _clusterPanel = null;
            _clusterDiagnosticsPanel = null;
            _mergeWorker?.Dispose();
            _mergeWorker = null;
            _exConFileDialogService = null;
            _uiCache?.Dispose();
            _uiCache = null;
            _observer?.Dispose();
            _observer = null;
            _slaveTranslator?.Dispose();
            _slaveTranslator = null;
            _bus = null;
            (_egressWriters as IDisposable)?.Dispose();
            _egressWriters = null;
            (_timeControl as IDisposable)?.Dispose();
            _timeControl = null;
            (_commandGateway as IDisposable)?.Dispose();
            _commandGateway = null;
            _mock?.Dispose();
            _mock = null;
            _slaveSyncController?.Dispose();
            _slaveSyncController = null;
            _timeModeTranslator = null;
            _slaveLockstepTranslator = null;
            _slaveTimeSyncTranslator = null;
            if (_ingressDisposables != null)
            {
                for (int i = 0; i < _ingressDisposables.Count; i++)
                    _ingressDisposables[i].Dispose();
                _ingressDisposables = null;
            }

            // Only dispose the participant if ExCon instantiated it locally.
            // If it was provided by the factory, the application shell owns it.
            if (_networkFactory?.Participant == null)
            {
                _participant?.Dispose();
            }

            _participant = null;
        }
    }

    internal sealed class NullCommandGateway : ICommandGateway
    {
        public System.Threading.Tasks.Task<int> CreateEntityAsync(CreateEntityCommand cmd, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(0);
        public System.Threading.Tasks.Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task<MissionCommitResult> SendMissionControlRequestAsync(MissionControlCommand cmd, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(new MissionCommitResult { Success = false, ErrorMessage = "No gateway" });
        public System.Threading.Tasks.Task SendUpdateAttributeAsync(Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand cmd, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.CompletedTask;
        public void Dispose() { }
    }

    internal sealed class NullExConEgressWriters : IExConEgressWriters
    {
        public void WriteMapConfig(MapConfigDto config) { }
        public void WriteDeleteEntity(int entityId) { }
        public void WriteCreateEntity(CreateEntityCommand cmd) { }
        public void WriteMapCommand(MapCommandDto cmd) { }
        public void PushContextActions(int mapGroupId, System.Collections.Generic.IReadOnlyList<int>? forSelection, string actionsJson) { }
        public void Dispose() { }
    }

    internal sealed class NullTimeControlGateway : ITimeControlGateway
    {
        public void RequestPause() { }
        public void RequestResume() { }
        public void RequestStep() { }
        public void SetTimeScale(float scale) { }
    }

    internal sealed class NullEntityStateExtractionService : IEntityStateExtractionService
    {
        public IReadOnlyList<EntityStateDumpDto> ExtractEntities(IReadOnlyList<long>? networkIds = null)
            => Array.Empty<EntityStateDumpDto>();
    }

    internal sealed class NullDiagnosticEventHistoryService : IDiagnosticEventHistoryService
    {
        public void Capture(string providerName, FdpEventBus eventBus, uint currentFrame) { }

        public CapturedEventDto[] GetHistory(IReadOnlyList<string>? providerFilter = null)
            => Array.Empty<CapturedEventDto>();

        public void ClearHistory() { }

        public void RewindHistory(uint toFrame) { }
    }
}
