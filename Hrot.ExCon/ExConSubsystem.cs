using Hrot.NED.Descriptors.Orchestration;
using Hrot.Common;
using Hrot.Common.Orchestration;
using Hrot.Core.Network;
using Hrot.ExCon;
using Hrot.ExCon.Logic;
using Hrot.ExCon.Services;
using Hrot.Map.Common;
using Hrot.Map.Common.Dds;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using Hrot.ExCon.Panels;
using Hrot.UI.Common.Panels;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using FDP.Toolkit.DER;
using Hrot.ExCon.Windows;
using Hrot.Orchestrator.Panels;
using Hrot.Orchestrator.Windows;
using Fdp.Engine.Runner;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Time;
using FDP.Toolkit.Time.Controllers;
using ModuleHost.Core.Time;

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
    public sealed class ExConSubsystem : ISubsystem, IWindowRegistrar
    {
        /// <inheritdoc/>
        public string Name => "ExCon";

        /// <inheritdoc/>
        /// <remarks>Violet — distinct from IG (green) and SimHost (red).</remarks>
        public System.Numerics.Vector4 TitleBarColor =>
            new System.Numerics.Vector4(0.32f, 0.08f, 0.48f, 1f);

        // Map group 0 = broadcast to all IG instances (matches ExConLogicConstants.DefaultMapGroupId).
        private const int DefaultMapGroupId = 0;

        // Subsystem name used for ClusterSlave registration (avoidance of magic strings).
        private const string SubsystemName = "ExCon";

        /// <summary>MapId of the IG this ExCon issues tool-activation commands to (300 = default IG instance).</summary>
        private const int TargetMapId = 300;

        private ExConMock?         _mock;
        private bool             _headless;
        private DdsParticipant?  _participant;
        private List<IDisposable>? _ingressDisposables;
        private int              _nodeIdOverride;
        private FDP.Toolkit.Orchestration.ClusterSlave? _clusterSlave;

        // ── CMC-S016: Orchestration bus + slave translator (BATCH-06) ──────────
        private FdpEventBus?                          _orchestrationBus;
        private NodeOpSlaveTranslator?                _nodeOpSlaveTranslator;

        // ── S0507 / PACK-C002: Observation bus + observer translator for ClusterUiCache ──
        private FdpEventBus?                          _uiCacheBus;
        private OrchestrationObserverTranslator?      _orchObserverTranslator;

        // ── S0507: Cluster control ─────────────────────────────────────────────
        private FdpEventBus?                          _clusterOpEgressBus;
        private Hrot.Common.Orchestration.ClusterOpEgressTranslator? _clusterOpEgressTranslator;
        private ClusterUiCache?                   _uiCache;
        private ClusterScenarioPanel?             _clusterPanel;

        // ── TC2-P3: Slave time sync ─────────────────────────────────────────────────
        private FdpEventBus?           _timeEventBus;
        private SlaveSyncController?   _slaveSyncController;
        private IDescriptorTranslator? _timeModeTranslator;
        private IDescriptorTranslator? _slaveLockstepTranslator;
        private IDescriptorTranslator? _slaveTimeSyncTranslator;
        // ── TASK-P4-001: neutral ExCon gateway interfaces ──────────────────────────
        private IExConEgressWriters?  _egressWriters;
        private ITimeControlGateway?  _timeControl;
        private ICommandGateway?      _commandGateway;
        private INetworkFactory?      _networkFactory;

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
        /// Internal test hook: exposes the <see cref="FDP.Toolkit.Orchestration.ClusterSlave"/>
        /// for handler-registration assertions (CGF1-S0104 / A.3).
        /// </summary>
        internal FDP.Toolkit.Orchestration.ClusterSlave? TestHook_ClusterSlave => _clusterSlave;

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

            // ── DDS participant ────────────────────────────────────────────────
            _participant = HrotEnvironment.CreateParticipant(config.DomainId);
            _participant.EnableSenderTracking(new SenderIdentityConfig
            {
                AppDomainId   = config.DomainId,
                AppInstanceId = config.NodeId
            });

            // ── ClusterSlave (CGF1-S0104 / CMC-S016 BATCH-06) ────────────────────────
            var iosNodeId  = config.NodeId != 0 ? config.NodeId : 500;

            // CMC-S016: each subsystem has its own orchestration bus + translator (Option C).
            _orchestrationBus = new FdpEventBus();
            var _orchCmdReader    = new DdsReader<NodeOpCommand>(_participant);
            var _orchStatusWriter = new DdsWriter<NodeOpStatus>(_participant);
            var _orchHbWriter     = new DdsWriter<NodeHeartbeat>(_participant);
            _nodeOpSlaveTranslator = new NodeOpSlaveTranslator(
                commandReader:   _orchCmdReader,
                statusWriter:    _orchStatusWriter,
                heartbeatWriter: _orchHbWriter,
                bus:             _orchestrationBus,
                nodeId:          iosNodeId);
            _clusterSlave = new FDP.Toolkit.Orchestration.ClusterSlave(iosNodeId, SubsystemName, _orchestrationBus);

            // ── TC2-P3-T1: Slave time sync pipeline ──────────────────────────────
            _timeEventBus             = new FdpEventBus();
            _slaveSyncController      = new SlaveSyncController(_timeEventBus, iosNodeId, TimeConfig.Default);
            _timeModeTranslator       = TimeNetworkModule.CreateDescriptorTranslator(_participant, _timeEventBus);
            _slaveLockstepTranslator  = TimeNetworkModule.CreateSlaveLockstepTranslator(_participant, _timeEventBus, iosNodeId);
            _slaveTimeSyncTranslator  = TimeNetworkModule.CreateSlaveTimeSyncTranslator(_participant, _timeEventBus, iosNodeId);

            // CGF1-BATCH-23 A.3: ExCon is an orchestrator instructor — it does NOT
            // save scenario fragments or exercise recordings.  If the orchestrator fans
            // out PrepareLive / FinalizeLive / PrepareReplay / FinalizeReplay to ExCon,
            // this node must ACK so the cluster 2PC is never stalled.
            // Shared listener controller tracks IsReplayActive for branch gating.
            var iosRrController = new Hrot.Common.Orchestration.ListenerRecordReplayController("ExCon");

            // Wire ReferenceReplayLoadHandler FIRST (PrepareReplay / FinalizeReplay;
            // PrepareLive only when replay active — Live-from-Replay branch gate).
            _clusterSlave.RegisterHandler(new FDP.Toolkit.Orchestration.Handlers.ReferenceReplayLoadHandler(
                iosRrController,
                simGroup:              null,
                lifecycleGroup:        null,
                bypassLifecycleToggle: null,
                storageDirectory:      @"C:\FDP_Temp"));

            // Wire ReferenceLiveLoadHandler: ACKs cold PrepareLive and FinalizeLive.
            // ExCon carries no ECS state and does not start a recording.
            _clusterSlave.RegisterHandler(new FDP.Toolkit.Orchestration.Handlers.ReferenceLiveLoadHandler(
                checkpointWorker: null,
                controller:       iosRrController,
                storageDirectory: @"C:\FDP_Temp"));

            // CGF1-S0309: wire dry-run snapshot/rewind handler (ExCon carries no ECS state).
            _clusterSlave.RegisterHandler(new ReferencePreviewHandler(liveRepo: null));

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
            _uiCacheBus             = new FdpEventBus();
            _orchObserverTranslator = new OrchestrationObserverTranslator(_participant, _uiCacheBus);
            _uiCache      = new ClusterUiCache(_uiCacheBus, _slaveSyncController);
            // PACK-E001: panel now publishes ClusterOpIntent to egress bus; translator writes DDS
            _clusterOpEgressBus        = new FdpEventBus();
            _clusterOpEgressTranslator = new Hrot.Common.Orchestration.ClusterOpEgressTranslator(_clusterOpEgressBus, _participant);
            _clusterPanel = new ClusterScenarioPanel(_clusterOpEgressBus, _uiCache);

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
                missionPanel:     new MissionPanel(iosNodeId),
                interactionPanel: interactionPanel,
                spawnerPanel:     new SpawnerPanel(tkbCatalog),
                useDockSpace:     config.OwnWindow);
        }

        /// <inheritdoc/>
        public void Update(float deltaTime)
        {
            // Time sync pipeline: ingest DDS → advance controller → egress ACKs + NTP requests → swap bus.
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
            _timeEventBus?.SwapBuffers();

            // CMC-S016: orchestration bus pipeline — swap before tick so translators
            // read events published in the previous frame.
            _orchestrationBus?.SwapBuffers();
            _nodeOpSlaveTranslator?.Tick();  // DDS NodeOpCommand → bus ExecuteNodeOpIntent;
                                             // bus NodeHeartbeatEvent → DDS NodeHeartbeat;
                                             // bus NodeOpCompletedEvent → DDS NodeOpStatus
            _clusterSlave?.Tick();
            // PACK-C002: translate DDS → _uiCacheBus events, then update the cache
            _orchObserverTranslator?.Tick();
            _uiCacheBus?.SwapBuffers();
            _uiCache?.Update();
            _clusterPanel?.Update(deltaTime);
            // PACK-E001: flush panel's ClusterOpIntent events to DDS via egress translator
            _clusterOpEgressBus?.SwapBuffers();
            _clusterOpEgressTranslator?.Tick();

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
        public void RegisterWindows(FDP.Toolkit.ImGui.WindowManager.WindowManager windowManager)
        {
            windowManager.RegisterWindow(new ClusterControlWindow(_clusterPanel, _uiCache));

            if (_mock != null)
            {
                var logic = _mock.Logic;
                windowManager.RegisterWindow(new ExConOrbatWindow(_mock.GetOrbatPanel(), logic));
                windowManager.RegisterWindow(new ExConMissionWindow(_mock.GetMissionPanel(), _mock.MissionShim, _mock.MapPickShim));
                windowManager.RegisterWindow(new ExConDataMonitorWindow(_mock.GetInteractionPanel(), logic));
                windowManager.RegisterWindow(new ExConSpawnerWindow(_mock.GetSpawnerPanel(), _mock.SpawnController));
                windowManager.RegisterWindow(new ExConConfigWindow(_mock.GetConfigPanel(), _mock.MapConfigAdapter));
                windowManager.RegisterWindow(new ExConDiagnosticsWindow(_mock.GetDiagnosticsPanel(), logic));
                _mock.SetPanelsWindowManaged();
            }
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            _clusterSlave?.Dispose();
            _clusterSlave = null;
            _clusterPanel = null;
            _uiCache?.Dispose();
            _uiCache = null;
            _orchObserverTranslator?.Dispose();
            _orchObserverTranslator = null;
            _uiCacheBus = null;
            _clusterOpEgressTranslator?.Dispose();
            _clusterOpEgressTranslator = null;
            _clusterOpEgressBus = null;
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
            _timeEventBus = null;
            if (_ingressDisposables != null)
            {
                for (int i = 0; i < _ingressDisposables.Count; i++)
                    _ingressDisposables[i].Dispose();
                _ingressDisposables = null;
            }
            _participant?.Dispose();
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
}
